#Requires -Version 7
<#
.SYNOPSIS
    Separately authorized PR5 acceptance through the net8 TiaMcpServer host.
.DESCRIPTION
    Never invoke from ordinary tests or CI. PreviewOnly performs reads and previews only.
    The exact disposable project copy must already be open in TIA Portal V21. Mutation modes
    require a saved, unmodified copy, -AllowMutation, -ConfirmDisposableCopy,
    -AuthorizedProjectPath matching that exact path, and -CleanupStrategy Discard.
    Discard closes without saving; it does not delete
    project files. An inverse-restore strategy is not implemented. Live acceptance must also
    verify that the on-disk copy remains clean. Do not edit the project concurrently.

    Supply existing target/sibling tables, target tag, and target/sibling user constants.
    NewTableName, CollisionTagName, and NewConstantName must be unused fixture names.
    CollisionLogicalAddress must be an unused Bool memory address. ChangedConstantValue and
    ChangedSiblingConstantValue must be valid, distinct values in the existing constant types,
    expressed exactly as TIA returns them. All feature and cleanup writes use preview/token/apply.

    Artifacts are redacted MCP requests/responses plus manifest, checks, cleanup, and failure JSON.
    Tokens remain in memory. Public previews expose hashes and ordered targets, not internal
    typed snapshots or worker call counts; offline tests cover those separate contract claims.
    No live acceptance markdown report is created automatically. Review every run artifact.
#>
[CmdletBinding()]
param(
    [ValidateSet('PreviewOnly','DriftAndRestore','ApplyAndRestore')]
    [string] $Mode = 'PreviewOnly',
    [Parameter(Mandatory)] [string] $ProjectPath,
    [Parameter(Mandatory)] [string] $PlcName,
    [Parameter(Mandatory)] [string] $TargetTable,
    [Parameter(Mandatory)] [string] $SiblingTable,
    [Parameter(Mandatory)] [string] $TargetTagName,
    [Parameter(Mandatory)] [string] $CollisionTagName,
    [Parameter(Mandatory)] [string] $TargetConstantName,
    [Parameter(Mandatory)] [string] $SiblingConstantName,
    [Parameter(Mandatory)] [string] $NewConstantName,
    [Parameter(Mandatory)] [string] $NewTableName,
    [Parameter(Mandatory)] [ValidatePattern('^%M[0-9]+\.[0-7]$')] [string] $CollisionLogicalAddress,
    [Parameter(Mandatory)] [string] $ChangedConstantValue,
    [Parameter(Mandatory)] [string] $ChangedSiblingConstantValue,
    [string] $TargetFolder = '/',
    [string] $SiblingFolder = '/',
    [switch] $AllowMutation,
    [switch] $ConfirmDisposableCopy,
    [string] $AuthorizedProjectPath,
    [ValidateSet('Discard')] [string] $CleanupStrategy,
    [string] $HostDllPath = 'TiaMcpServer/bin/Debug/net8.0/TiaMcpServer.dll',
    [string] $ArtifactRoot = [IO.Path]::GetTempPath(),
    [ValidateRange(10,600)] [int] $TimeoutSeconds = 180
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$script:HostProcess = $null
$script:StderrTask = $null
$script:RequestId = 0
$script:InitialIdentity = $null
$script:InitialBinding = $null
$script:MutationAuthorized = $false
$script:MutationAttempted = $false
$script:TransportHealthy = $true
$script:Tokens = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$checks = [Collections.Generic.List[object]]::new()
$failures = [Collections.Generic.List[object]]::new()
$cleanup = [ordered]@{ strategy = $CleanupStrategy; discard = 'NOT REQUIRED'; hostStopped = $false; transientRemoved = $false }
$runName = 'tia-tag-safety-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N')
$artifactDirectory = Join-Path ([IO.Path]::GetFullPath($ArtifactRoot)) $runName
[void][IO.Directory]::CreateDirectory($artifactDirectory)
$transientDirectory = Join-Path $artifactDirectory 'transient'
[void][IO.Directory]::CreateDirectory($transientDirectory)

function Assert-Condition([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Redact-SafetyToken($Value) {
    if ($null -eq $Value) { return $null }
    if ($Value -is [Collections.IDictionary]) {
        $copy = [ordered]@{}
        foreach ($key in $Value.Keys) {
            if ([string]::Equals([string]$key, 'safetyToken', [StringComparison]::OrdinalIgnoreCase)) {
                if ($Value[$key] -is [string] -and -not [string]::IsNullOrEmpty($Value[$key])) {
                    [void]$script:Tokens.Add($Value[$key])
                }
                $copy[$key] = '[REDACTED]'
            }
            else { $copy[$key] = Redact-SafetyToken $Value[$key] }
        }
        return $copy
    }
    if ($Value -is [string]) {
        $text = $Value
        if ($text.TrimStart().StartsWith('{') -or $text.TrimStart().StartsWith('[')) {
            $parsed = $null
            try { $parsed = ConvertFrom-Json -InputObject $text -AsHashtable -ErrorAction Stop } catch { }
            if ($null -ne $parsed) { $text = ConvertTo-Json -InputObject (Redact-SafetyToken $parsed) -Depth 100 -Compress }
        }
        foreach ($token in $script:Tokens) { $text = $text.Replace($token, '[REDACTED]') }
        return [regex]::Replace($text, '(?i)("safetyToken"\s*:\s*")[^"]*', '$1[REDACTED]')
    }
    if ($Value -is [Collections.IEnumerable]) {
        $items = @($Value | ForEach-Object { Redact-SafetyToken $_ })
        return ,$items
    }
    return $Value
}

function Write-Artifact([string] $Name, $Value) {
    $safe = Redact-SafetyToken $Value
    $json = ConvertTo-Json -InputObject $safe -Depth 100
    [IO.File]::WriteAllText((Join-Path $artifactDirectory $Name), $json, $utf8)
}

function Write-Failure([string] $Stage, [string] $Message) {
    $failures.Add(@{ stage = $Stage; error = $Message; utc = [DateTime]::UtcNow.ToString('o') })
    Write-Artifact 'failure.json' @{ failures = $failures; checks = $checks; cleanup = $cleanup; mutationAttempted = $script:MutationAttempted }
}

function Invoke-Mcp([string] $Method, [hashtable] $Parameters) {
    Assert-Condition ($null -ne $script:HostProcess -and $script:TransportHealthy) 'MCP host transport is unavailable; outcome may be unknown.'
    $script:RequestId++
    $id = $script:RequestId
    $request = @{ jsonrpc = '2.0'; id = $id; method = $Method; params = $Parameters }
    Write-Artifact ('{0:D4}-request.json' -f $id) $request
    try {
        $script:HostProcess.StandardInput.WriteLine((ConvertTo-Json -InputObject $request -Depth 100 -Compress))
        $script:HostProcess.StandardInput.Flush()
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        $notificationIndex = 0
        $result = $null
        while ($null -eq $result) {
            $remaining = $deadline - (Get-Date)
            Assert-Condition ($remaining -gt [TimeSpan]::Zero) "MCP response timed out for $Method; state may be unknown."
            $readTask = $script:HostProcess.StandardOutput.ReadLineAsync()
            $line = $readTask.WaitAsync($remaining).GetAwaiter().GetResult()
            Assert-Condition ($null -ne $line) "Host closed stdout during $Method."
            $response = ConvertFrom-Json -InputObject $line -AsHashtable
            Assert-Condition ($null -ne $response -and $response -is [Collections.IDictionary]) 'Invalid MCP response document.'
            if (-not $response.ContainsKey('id')) {
                $notificationIndex++
                Write-Artifact ('{0:D4}-notification-{1:D4}.json' -f $id, $notificationIndex) $response
                continue
            }
            Write-Artifact ('{0:D4}-response.json' -f $id) $response
            Assert-Condition ($response.id -eq $id) 'Unexpected MCP response id.'
            Assert-Condition (-not $response.ContainsKey('error')) "MCP $Method failed; see redacted response."
            Assert-Condition ($response.ContainsKey('result') -and $null -ne $response.result) 'MCP response has no result.'
            $result = $response.result
        }
    }
    catch {
        # A timed-out ReadLineAsync may still own stdout. Do not issue cleanup through that pipe.
        $script:TransportHealthy = $false
        throw
    }
    return $result
}

function Invoke-Tool([string] $Name, [hashtable] $Arguments, [switch] $AllowFailure) {
    # Central deny gate also guards future call sites and lifecycle cleanup.
    if ($Name -in @('apply_write_batch', 'close_project')) {
        Assert-MutationAuthorization
    }
    Assert-Condition ($Name -in @('get_project_status','execute_read_batch','preview_write_batch','apply_write_batch','close_project')) 'Tool is outside the harness allowlist.'
    $result = Invoke-Mcp 'tools/call' @{ name = $Name; arguments = $Arguments }
    Assert-Condition (-not ($result.ContainsKey('isError') -and $result.isError)) "MCP tool $Name failed; see redacted response."
    Assert-Condition ($result.ContainsKey('content') -and $null -ne $result.content) 'Missing MCP content.'
    $items = @($result.content | Where-Object { $null -ne $_ -and $_.type -eq 'text' })
    Assert-Condition ($items.Count -eq 1) "$Name must return exactly one text document."
    $document = ConvertFrom-Json -InputObject $items[0].text -AsHashtable
    Assert-Condition ($null -ne $document -and $document -is [Collections.IDictionary]) 'Invalid tool JSON document.'
    if (-not $AllowFailure) {
        Assert-Condition (-not ($document.ContainsKey('success') -and $document.success -ne $true)) "$Name reported failure; see redacted response."
    }
    return $document
}

function Read-Binding {
    $status = Invoke-Tool 'get_project_status' @{ projectPath = $ProjectPath }
    Assert-Condition ($status.success -eq $true -and $null -ne $status.payload -and $null -ne $status.sessionIdentity) 'Missing project status/identity.'
    $payload = ConvertFrom-Json -InputObject $status.payload -AsHashtable
    Assert-Condition ($null -ne $payload -and $null -ne $payload.project) 'Missing project status payload.'
    Assert-Condition ($payload.success -eq $true -and $payload.project.isOpen -eq $true) 'Explicit target project is not open.'
    $identity = $status.sessionIdentity
    foreach ($path in @($payload.projectPath, $payload.project.path, $identity.projectPath)) {
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($path)) 'Missing project path.'
        Assert-Condition ([string]::Equals([IO.Path]::GetFullPath($path), $ProjectPath, [StringComparison]::OrdinalIgnoreCase)) 'Active project differs from exact disposable project copy.'
    }
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($identity.workerSessionId)) 'Missing worker session id.'
    Assert-Condition ($identity.sessionGeneration -gt 0 -and $identity.portalProcessId -gt 0) 'Incomplete session identity.'
    $signature = '{0}/{1}/{2}' -f $identity.workerSessionId, $identity.sessionGeneration, $identity.portalProcessId
    if ($null -ne $script:InitialIdentity) {
        Assert-Condition ($signature -ceq $script:InitialIdentity) 'Project session changed; refusing further work.'
    }
    $script:InitialIdentity = $signature
    return $payload.project
}

function Assert-PreviewBinding($Preview) {
    Assert-Condition ($null -ne $Preview -and $null -ne $Preview.projectBinding) 'Preview binding missing.'
    $binding = $Preview.projectBinding
    Assert-Condition ($binding.state -eq 'verified' -and $binding.revision -gt 0 -and -not [string]::IsNullOrWhiteSpace($binding.bindingId)) 'Preview binding is not verified.'
    $identity = '{0}/{1}/{2}' -f $binding.workerSessionId, $binding.sessionGeneration, $binding.portalProcessId
    Assert-Condition ($identity -ceq $script:InitialIdentity -and [string]::Equals($binding.projectPath, $ProjectPath, [StringComparison]::OrdinalIgnoreCase)) 'Preview session differs from exact target.'
    $signature = '{0}/{1}' -f $binding.bindingId, $binding.revision
    if ($null -ne $script:InitialBinding) {
        Assert-Condition ($signature -ceq $script:InitialBinding) 'Host binding changed during acceptance.'
    }
    $script:InitialBinding = $signature
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($Preview.safetyToken)) 'Preview did not issue a token.'
}

function Get-Preview([array] $Operations) {
    $null = Read-Binding
    $preview = Invoke-Tool 'preview_write_batch' @{ operations = $Operations }
    Assert-PreviewBinding $preview
    Assert-Condition ($preview.currentStateHash -match '^[0-9a-fA-F]{64}$' -and $preview.requestedInputHash -match '^[0-9a-fA-F]{64}$') 'Preview hashes missing.'
    Assert-Condition ($null -ne $preview.target -and $preview.target.Count -eq $Operations.Count) 'Preview target count differs.'
    for ($i = 0; $i -lt $Operations.Count; $i++) {
        Assert-Condition ($preview.target[$i].operationId -ceq $Operations[$i].operationId -and $preview.target[$i].operation -ceq $Operations[$i].operation) 'Preview target order or operation differs.'
    }
    return $preview
}

function New-Operation([string] $Id, [string] $Operation, [string] $Table, [string] $Folder, [hashtable] $Fields = @{}) {
    $result = @{ operationId = $Id; operation = $Operation; projectPath = $ProjectPath; plcName = $PlcName; tableName = $Table; folderPath = $Folder }
    foreach ($key in $Fields.Keys) { $result[$key] = $Fields[$key] }
    return $result
}

function Read-Tables {
    $operation = @{ operationId = 'fixture-read'; operation = 'list_tag_tables'; projectPath = $ProjectPath; plcName = $PlcName }
    $read = Invoke-Tool 'execute_read_batch' @{ operations = @($operation) }
    Assert-Condition ($read.success -eq $true -and $null -ne $read.operations -and $read.operations.Count -eq 1) 'Fixture read failed.'
    $item = $read.operations[0]
    Assert-Condition ($item.status -eq 'succeeded' -and $item.operationId -ceq 'fixture-read') 'Fixture read identity/status mismatch.'
    Assert-Condition (@($item.warnings | Where-Object { $null -ne $_ }).Count -eq 0) 'Fixture read has warnings; refusing incomplete evidence.'
    Assert-Condition ($item.result -is [string] -and $item.result -notmatch '\[(TRUNCATED|OMITTED)') 'Fixture read missing/truncated.'
    $tables = ConvertFrom-Json -InputObject $item.result -AsHashtable
    Assert-Condition ($null -ne $tables) 'No fixture tables returned.'
    return ,@($tables)
}

function Find-Table([array] $Tables, [string] $Name, [string] $Folder) {
    $matches = @($Tables | Where-Object { $null -ne $_ -and $_.name -ceq $Name -and $_.folderPath -ceq $Folder })
    Assert-Condition ($matches.Count -eq 1) 'Fixture table must resolve exactly once at its explicit folder/name.'
    return $matches[0]
}

function Find-Constant($Table, [string] $Name) {
    Assert-Condition ($null -ne $Table -and $null -ne $Table.userConstants) 'User constant collection missing.'
    $matches = @($Table.userConstants | Where-Object { $null -ne $_ -and $_.name -ceq $Name })
    Assert-Condition ($matches.Count -eq 1) 'Fixture user constant must resolve exactly once.'
    return $matches[0]
}

function Assert-MutationAuthorization {
    Assert-Condition ($Mode -ne 'PreviewOnly' -and $AllowMutation -and $ConfirmDisposableCopy -and $script:MutationAuthorized -and $CleanupStrategy -ceq 'Discard') 'Mutation requires an explicitly authorized mode and restore or discard strategy.'
    Assert-Condition ([string]::Equals($AuthorizedProjectPath, $ProjectPath, [StringComparison]::OrdinalIgnoreCase)) 'Mutation authorization does not match the exact disposable project copy.'
}

function Invoke-Apply([array] $Operations, $Preview, [switch] $ExpectStateChanged) {
    Assert-MutationAuthorization
    $null = Read-Binding
    Assert-PreviewBinding $Preview
    $script:MutationAttempted = $true
    $result = Invoke-Tool 'apply_write_batch' @{ operations = $Operations; confirm = $true; safetyToken = $Preview.safetyToken } -AllowFailure
    if ($ExpectStateChanged) {
        Assert-Condition ($result.success -eq $false -and $result.failureCategory -ceq 'state_changed') 'Stale token did not reject with state_changed.'
    }
    else {
        Assert-Condition ($result.success -eq $true -and $result.succeeded -eq $Operations.Count -and $result.failed -eq 0) 'Authorized apply failed.'
    }
    return $result
}

function Invoke-AuthorizedChange([array] $Operations) {
    $preview = Get-Preview $Operations
    $null = Invoke-Apply $Operations $preview
}

function Test-Drift([string] $Claim, [array] $Target, [array] $Mutation, [array] $Restoration) {
    $before = Get-Preview $Target
    Invoke-AuthorizedChange $Mutation
    $after = Get-Preview $Target
    Assert-Condition ($before.currentStateHash -cne $after.currentStateHash) "$Claim did not change the bound state hash."
    $null = Invoke-Apply $Target $before -ExpectStateChanged
    $rejected = Get-Preview $Target
    Assert-Condition ($after.currentStateHash -ceq $rejected.currentStateHash) "$Claim rejection changed target state."
    $checks.Add(@{ claim = $Claim; result = 'PASS'; rejection = 'state_changed'; beforeHash = $before.currentStateHash; driftHash = $after.currentStateHash; unchangedAfterRejection = $true })
    # Best-effort inverse fixture reset for the next scenario; final cleanup still discards the copy.
    Invoke-AuthorizedChange $Restoration
}

function Stop-McpHost {
    if ($null -ne $script:HostProcess) {
        try {
            try {
                if (-not $script:HostProcess.HasExited) {
                    $script:HostProcess.StandardInput.Close()
                    [void]$script:HostProcess.WaitForExit(5000)
                }
            }
            finally {
                if (-not $script:HostProcess.HasExited) {
                    $script:HostProcess.Kill($true)
                    Assert-Condition ($script:HostProcess.WaitForExit(5000)) 'Host did not stop after termination.'
                }
                $cleanup.hostStopped = $script:HostProcess.HasExited
            }
            if ($null -ne $script:StderrTask) {
                $stderr = $script:StderrTask.WaitAsync([TimeSpan]::FromSeconds(5)).GetAwaiter().GetResult()
                Write-Artifact 'host-stderr.json' @{ text = $stderr }
            }
        }
        finally {
            $script:HostProcess.Dispose()
            $script:HostProcess = $null
        }
    }
    $cleanup.hostStopped = $true
}

try {
    Assert-Condition ($PSVersionTable.PSVersion -ge [Version]'7.2') 'PowerShell 7.2 or later is required for Task.WaitAsync.'
    Assert-Condition ([IO.Path]::IsPathFullyQualified($ProjectPath)) 'ProjectPath must be absolute.'
    $ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).ProviderPath
    Assert-Condition ([IO.Path]::GetExtension($ProjectPath) -ieq '.ap21') 'ProjectPath must identify a TIA Portal V21 .ap21 file.'
    foreach ($name in @($PlcName,$TargetTable,$SiblingTable,$TargetTagName,$CollisionTagName,$TargetConstantName,$SiblingConstantName,$NewConstantName,$NewTableName)) {
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($name)) 'Fixture names must be explicit and nonempty.'
    }
    Assert-Condition ($TargetTable -cne $SiblingTable -or $TargetFolder -cne $SiblingFolder) 'Unrelated sibling must be a different table.'
    if ($Mode -eq 'PreviewOnly') {
        Assert-Condition (-not $AllowMutation -and -not $ConfirmDisposableCopy -and [string]::IsNullOrEmpty($AuthorizedProjectPath) -and [string]::IsNullOrEmpty($CleanupStrategy)) 'PreviewOnly must not carry mutation authorization.'
    }
    else {
        Assert-Condition ($AllowMutation -and $ConfirmDisposableCopy -and $CleanupStrategy -ceq 'Discard') 'Explicit mutation modes require -AllowMutation, -ConfirmDisposableCopy, and -CleanupStrategy Discard.'
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($AuthorizedProjectPath) -and [IO.Path]::IsPathFullyQualified($AuthorizedProjectPath)) 'Authorize the exact absolute disposable project copy path.'
        $AuthorizedProjectPath = (Resolve-Path -LiteralPath $AuthorizedProjectPath).ProviderPath
        Assert-Condition ([string]::Equals($AuthorizedProjectPath, $ProjectPath, [StringComparison]::OrdinalIgnoreCase)) 'AuthorizedProjectPath must equal ProjectPath.'
    }
    $repoRoot = Split-Path -Parent $PSScriptRoot
    if (-not [IO.Path]::IsPathFullyQualified($HostDllPath)) { $HostDllPath = Join-Path $repoRoot $HostDllPath }
    $HostDllPath = (Resolve-Path -LiteralPath $HostDllPath).ProviderPath
    Assert-Condition ([IO.Path]::GetFileName($HostDllPath) -ceq 'TiaMcpServer.dll') 'Launch the net8 TiaMcpServer host DLL.'
    $hostHash = (Get-FileHash -LiteralPath $HostDllPath -Algorithm SHA256).Hash
    Write-Artifact 'manifest.json' @{ mode = $Mode; projectPath = $ProjectPath; plcName = $PlcName; targetTable = $TargetTable; targetFolder = $TargetFolder; siblingTable = $SiblingTable; siblingFolder = $SiblingFolder; hostDllPath = $HostDllPath; hostSha256 = $hostHash; cleanupStrategy = $CleanupStrategy; authorizedProjectPath = $AuthorizedProjectPath; disposableCopyConfirmed = [bool]$ConfirmDisposableCopy; evidenceBoundary = 'Live host observations for this run only; no automatic PR5 acceptance report or plant acceptance.' }

    $startInfo = [Diagnostics.ProcessStartInfo]::new('dotnet')
    foreach ($argument in @($HostDllPath, '--read-write', '--project', $ProjectPath)) { $startInfo.ArgumentList.Add($argument) }
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardInputEncoding = $utf8
    $startInfo.StandardOutputEncoding = $utf8
    $script:HostProcess = [Diagnostics.Process]::Start($startInfo)
    Assert-Condition ($null -ne $script:HostProcess) 'Host failed to start.'
    $script:StderrTask = $script:HostProcess.StandardError.ReadToEndAsync()
    $initialize = Invoke-Mcp 'initialize' @{ protocolVersion = '2024-11-05'; capabilities = @{}; clientInfo = @{ name = 'tag-operation-safety-acceptance'; version = '1.0' } }
    Assert-Condition ($initialize.protocolVersion -ceq '2024-11-05') 'Unexpected negotiated MCP protocol version.'
    $notification = @{ jsonrpc = '2.0'; method = 'notifications/initialized' }
    Write-Artifact 'initialized-notification.json' $notification
    $script:HostProcess.StandardInput.WriteLine((ConvertTo-Json -InputObject $notification -Depth 10 -Compress))
    $script:HostProcess.StandardInput.Flush()
    $initialProject = Read-Binding
    if ($Mode -ne 'PreviewOnly') {
        Assert-Condition ($initialProject.isModified -eq $false) 'Mutation requires a pre-saved clean disposable project copy.'
        $script:MutationAuthorized = $true
    }
    $tables = Read-Tables
    $target = Find-Table $tables $TargetTable $TargetFolder
    $sibling = Find-Table $tables $SiblingTable $SiblingFolder
    $constant = Find-Constant $target $TargetConstantName
    $siblingConstant = Find-Constant $sibling $SiblingConstantName
    $tags = @($target.tags | Where-Object { $null -ne $_ -and $_.name -ceq $TargetTagName })
    Assert-Condition ($tags.Count -eq 1) 'Target tag must resolve exactly once.'
    Assert-Condition ($constant.value -cne $ChangedConstantValue -and $siblingConstant.value -cne $ChangedSiblingConstantValue) 'Changed constant values must differ from their baselines.'
    Assert-Condition (@($tables | Where-Object { $_.name -ceq $NewTableName -and $_.folderPath -ceq $TargetFolder }).Count -eq 0) 'NewTableName already exists.'
    $allTags = @($tables | ForEach-Object { $_.tags })
    $allConstants = @($tables | ForEach-Object { $_.userConstants })
    Assert-Condition (@($allTags | Where-Object { $null -ne $_ -and ($_.name -ceq $CollisionTagName -or $_.logicalAddress -ieq $CollisionLogicalAddress) }).Count -eq 0) 'Collision fixture name/address must be unused.'
    Assert-Condition (@($allConstants | Where-Object { $null -ne $_ -and $_.name -ceq $NewConstantName }).Count -eq 0) 'NewConstantName must be unused.'
    Write-Artifact 'baseline.json' @{ project = $initialProject; tables = $tables; limitation = 'Public list_tag_tables remains best-effort; strict safety reads and guarded applies remain authoritative.' }

    $updateConstant = New-Operation 'update-constant' 'update_user_constant' $TargetTable $TargetFolder @{ name = $TargetConstantName; value = $ChangedConstantValue }
    $resetConstant = New-Operation 'reset-constant' 'update_user_constant' $TargetTable $TargetFolder @{ name = $TargetConstantName; value = $constant.value }
    $createTag = New-Operation 'create-tag' 'create_tag' $TargetTable $TargetFolder @{ name = $CollisionTagName; dataType = 'Bool' }
    $deleteCollision = New-Operation 'remove-collision' 'delete_tag' $TargetTable $TargetFolder @{ name = $CollisionTagName }
    $operations = @(
        (New-Operation 'create-table' 'create_tag_table' $NewTableName $TargetFolder),
        (New-Operation 'delete-table' 'delete_tag_table' $TargetTable $TargetFolder),
        $createTag,
        (New-Operation 'update-tag' 'update_tag' $TargetTable $TargetFolder @{ name = $TargetTagName; logicalAddress = $CollisionLogicalAddress }),
        (New-Operation 'delete-tag' 'delete_tag' $TargetTable $TargetFolder @{ name = $TargetTagName }),
        (New-Operation 'create-constant' 'create_user_constant' $TargetTable $TargetFolder @{ name = $NewConstantName; dataType = $constant.dataType; value = $constant.value }),
        $updateConstant,
        (New-Operation 'delete-constant' 'delete_user_constant' $TargetTable $TargetFolder @{ name = $TargetConstantName })
    )
    foreach ($operation in $operations) {
        $preview = Get-Preview @($operation)
        $repeat = Get-Preview @($operation)
        Assert-Condition ($preview.currentStateHash -ceq $repeat.currentStateHash -and $preview.requestedInputHash -ceq $repeat.requestedInputHash) 'Repeated exact selector preview differs.'
        $checks.Add(@{ claim = $operation.operation; result = 'PREVIEW PASS'; currentStateHash = $preview.currentStateHash; requestedInputHash = $preview.requestedInputHash })
    }
    # Repeated selectors in one ordered batch exercise expansion without claiming live read counts.
    $duplicate = New-Operation 'update-constant-second' 'update_user_constant' $TargetTable $TargetFolder @{ name = $TargetConstantName; value = $ChangedConstantValue }
    $null = Get-Preview @($updateConstant, $duplicate)
    $checks.Add(@{ claim = 'ordered duplicate selector preview'; result = 'PASS'; limitation = 'Within-phase worker read count is offline/FakeWorker evidence only.' })

    if ($Mode -eq 'DriftAndRestore') {
        Test-Drift 'same-object drift' @($resetConstant) @($updateConstant) @($resetConstant)
        Test-Drift 'relevant collision: name' @($createTag) @($createTag) @($deleteCollision)
        $addressMutation = New-Operation 'create-address-collision' 'create_tag' $TargetTable $TargetFolder @{ name = $CollisionTagName; dataType = 'Bool'; logicalAddress = $CollisionLogicalAddress }
        Test-Drift 'relevant collision: address' @($operations[3]) @($addressMutation) @($deleteCollision)
        $tolerant = Get-Preview @($updateConstant)
        $siblingMutation = New-Operation 'change-sibling' 'update_user_constant' $SiblingTable $SiblingFolder @{ name = $SiblingConstantName; value = $ChangedSiblingConstantValue }
        Invoke-AuthorizedChange @($siblingMutation)
        $afterSibling = Find-Constant (Find-Table (Read-Tables) $SiblingTable $SiblingFolder) $SiblingConstantName
        Assert-Condition ($afterSibling.value -ceq $ChangedSiblingConstantValue) 'Sibling mutation was not observed.'
        $after = Get-Preview @($updateConstant)
        Assert-Condition ($tolerant.currentStateHash -ceq $after.currentStateHash) 'Unrelated sibling invalidated the target snapshot.'
        $null = Invoke-Apply @($updateConstant) $tolerant
        $checks.Add(@{ claim = 'unrelated sibling tolerance'; result = 'PASS'; unchangedTargetHash = $after.currentStateHash; originalTokenApplied = $true })
    }
    if ($Mode -eq 'ApplyAndRestore') {
        $preview = Get-Preview @($updateConstant)
        $null = Invoke-Apply @($updateConstant) $preview
        $checks.Add(@{ claim = 'one authorized apply'; result = 'PASS'; unchangedIssuedToken = $true })
    }
    if ($Mode -ne 'PreviewOnly') {
        $observed = Find-Constant (Find-Table (Read-Tables) $TargetTable $TargetFolder) $TargetConstantName
        Assert-Condition ($observed.value -ceq $ChangedConstantValue) 'Authorized target value was not observed.'
    }
    $null = Read-Binding
    Write-Artifact 'checks.json' $checks
}
catch {
    Write-Failure 'checks' $_.Exception.Message
    throw (Redact-SafetyToken $_.Exception.Message)
}
finally {
    try {
        if ($script:MutationAttempted) {
            $cleanup.discard = 'ATTEMPTED'
            Assert-Condition $script:TransportHealthy 'Transport failed; discard cannot be confirmed. Keep the copy isolated and close without saving manually.'
            $null = Read-Binding
            $arguments = @{ projectPath = $ProjectPath; saveBeforeClose = $false }
            $preview = Invoke-Tool 'close_project' $arguments
            Assert-PreviewBinding $preview
            $applyArguments = $arguments.Clone()
            $applyArguments.confirm = $true
            $applyArguments.safetyToken = $preview.safetyToken
            $closed = Invoke-Tool 'close_project' $applyArguments
            Assert-Condition ($closed.success -eq $true) 'Discard close did not succeed.'
            Assert-Condition (-not [string]::IsNullOrWhiteSpace($closed.operationResult)) 'Discard close operation result missing.'
            $closedPayload = ConvertFrom-Json -InputObject $closed.operationResult -AsHashtable
            Assert-Condition ($null -ne $closedPayload -and $null -ne $closedPayload.project) 'Discard close payload is missing project state.'
            Assert-Condition ($closedPayload.success -eq $true -and $closedPayload.project.isOpen -eq $false) 'Discard close was not confirmed by its payload.'
            Assert-Condition ([string]::Equals($closedPayload.projectPath, $ProjectPath, [StringComparison]::OrdinalIgnoreCase) -and [string]::Equals($closedPayload.project.path, $ProjectPath, [StringComparison]::OrdinalIgnoreCase)) 'Discard close payload names a different project.'
            $cleanup.discard = 'PASS: exact copy closed with saveBeforeClose=false; on-disk clean-copy verification remains a live acceptance obligation'
        }
    }
    catch {
        $cleanup.discard = 'FAILED: manual no-save discard required; keep the copy isolated'
        Write-Failure 'discard' $_.Exception.Message
        throw (Redact-SafetyToken $_.Exception.Message)
    }
    finally {
        try { Stop-McpHost }
        catch {
            Write-Failure 'host cleanup' $_.Exception.Message
            throw (Redact-SafetyToken $_.Exception.Message)
        }
        finally {
            try {
                # Only this run's dedicated empty transient directory is removed. Worker-owned
                # temporary safety exports use the worker reader's own finally cleanup.
                if (Test-Path -LiteralPath $transientDirectory) { Remove-Item -LiteralPath $transientDirectory -ErrorAction Stop }
                $cleanup.transientRemoved = $true
            }
            catch {
                Write-Failure 'transient cleanup' $_.Exception.Message
                throw (Redact-SafetyToken $_.Exception.Message)
            }
            finally {
                Write-Artifact 'cleanup.json' $cleanup
                $script:Tokens.Clear()
                Write-Host "Redacted run artifacts: $artifactDirectory"
            }
        }
    }
}
