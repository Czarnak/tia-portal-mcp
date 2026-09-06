#Requires -Version 7.2
<#
.SYNOPSIS
    Separately authorized PR6 acceptance through the public MCP host.
.DESCRIPTION
    Never invoke from tests or CI. Inventory is the default. The exact disposable .ap21
    copy must already be open in V21. No concurrent project edits are allowed.
    Preview and Apply require FixtureGroupPath (a user group, never a Blocks root),
    OccupiedBlockPath (an existing SCL FC inside that group), and NewGroupName (unused).
    The entire fixture subtree must contain only user groups and independent SCL FCs.
    Use small, dependency-free fixtures: incomplete, warning-bearing, inconsistent or
    non-deterministic exports are rejected before mutation. Both PLC and Units paths
    are passed unchanged to the public tools. Run each owner scope separately.

    Apply requires -AllowMutation -Acknowledgement 'OVERRIDE BLOCKS AND DELETE GROUPS'.
    It overrides the occupied FC, creates a group, then deletes the fixture subtree.
    Finally it reconstructs the original groups and FCs through preview/apply, imports
    the original complete XML bundles, and compares every exported byte and tree path.
    Only then may final compile_check run. Restoration failure is a failed acceptance,
    requiring manual recovery from the retained baseline; the project stays open.

    Artifacts contain project content and must be kept private. Tokens are redacted.
    Unique run directories are retained (including recovery bytes) on failure and success.
    This script does not produce a live acceptance report; review the dated run evidence.
#>
[CmdletBinding()]
param(
    [ValidateSet('Inventory', 'Preview', 'Apply')]
    [string] $Mode = 'Inventory',
    [Parameter(Mandatory)] [string] $ProjectPath,
    [string] $FixtureGroupPath,
    [string] $OccupiedBlockPath,
    [string] $NewGroupName = 'PR6_NewGroup',
    [switch] $AllowMutation,
    [string] $Acknowledgement,
    [string] $HostExecutable = 'dotnet',
    [string[]] $HostArguments,
    [ValidateRange(10, 600)] [int] $StartupTimeoutSeconds = 120,
    [ValidateRange(10, 600)] [int] $RequestTimeoutSeconds = 180,
    [string] $ArtifactRoot = [IO.Path]::GetTempPath()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:RequiredAcknowledgement = 'OVERRIDE BLOCKS AND DELETE GROUPS'
$script:HostProcess = $null
$script:StderrTask = $null
$script:RequestId = 0
$script:TransportHealthy = $true
$script:MutationStarted = $false
$script:RestorationProven = $false
$script:Baseline = $null
$script:InitialIdentity = $null
$script:Tokens = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$script:Utf8 = [Text.UTF8Encoding]::new($false, $true)
$script:ArtifactDirectory = $null
$script:RunSucceeded = $false

function Assert-Condition([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Resolve-ProjectPath([string] $Path) {
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($Path)) 'Missing project path.'
    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    Assert-Condition ($resolved.Provider.Name -ceq 'FileSystem') 'Project must be a filesystem path.'
    $item = Get-Item -LiteralPath $resolved.ProviderPath
    Assert-Condition (-not $item.PSIsContainer) 'Project path must name a file.'
    return [IO.Path]::GetFullPath($resolved.ProviderPath)
}

function Redact-SafetyToken($Value) {
    if ($null -eq $Value) { return $null }
    if ($Value -is [Collections.IDictionary]) {
        # Gather token values before walking sibling properties that can repeat them.
        foreach ($key in $Value.Keys) {
            if ([string]::Equals([string]$key, 'safetyToken', [StringComparison]::OrdinalIgnoreCase) -and
                ($Value[$key] -is [string]) -and (-not [string]::IsNullOrEmpty($Value[$key]))) {
                [void]$script:Tokens.Add($Value[$key])
            }
        }
        $copy = [ordered]@{}
        foreach ($key in $Value.Keys) {
            if ([string]::Equals([string]$key, 'safetyToken', [StringComparison]::OrdinalIgnoreCase)) {
                $copy[$key] = '[REDACTED]'
            }
            else { $copy[$key] = Redact-SafetyToken $Value[$key] }
        }
        return $copy
    }
    if ($Value -is [string]) {
        $safe = $Value
        if ($safe.TrimStart().StartsWith('{') -or $safe.TrimStart().StartsWith('[')) {
            $parsed = $null
            try { $parsed = ConvertFrom-Json -InputObject $safe -AsHashtable } catch { }
            if ($null -ne $parsed) { $safe = ConvertTo-Json -InputObject (Redact-SafetyToken $parsed) -Depth 100 -Compress }
        }
        foreach ($token in $script:Tokens) { $safe = $safe.Replace($token, '[REDACTED]') }
        return [regex]::Replace($safe, '(?i)("safetyToken"\s*:\s*")[^"]*', '$1[REDACTED]')
    }
    if ($Value -is [Collections.IEnumerable]) {
        return ,@($Value | ForEach-Object { Redact-SafetyToken $_ })
    }
    return $Value
}

function Write-Artifact([string] $Name, $Value) {
    $safe = Redact-SafetyToken $Value
    $json = ConvertTo-Json -InputObject $safe -Depth 100
    # A second pass removes tokens discovered in later nested documents from all siblings.
    foreach ($token in $script:Tokens) { $json = $json.Replace($token, '[REDACTED]') }
    [IO.File]::WriteAllText((Join-Path $script:ArtifactDirectory $Name), $json, $script:Utf8)
}

function Invoke-Mcp([string] $Method, [hashtable] $Parameters, [int] $TimeoutSeconds = $RequestTimeoutSeconds) {
    Assert-Condition ($null -ne $script:HostProcess -and $script:TransportHealthy) 'Host transport unavailable; mutation outcome may be unknown.'
    Assert-Condition ($Method -in @('initialize', 'tools/list', 'tools/call')) 'Protocol route is outside the allowlist.'
    $script:RequestId++
    $id = $script:RequestId
    $request = @{ jsonrpc = '2.0'; id = $id; method = $Method; params = $Parameters }
    Write-Artifact ('{0:D4}-request.json' -f $id) $request
    $result = $null
    try {
        $script:HostProcess.StandardInput.WriteLine((ConvertTo-Json -InputObject $request -Depth 100 -Compress))
        $script:HostProcess.StandardInput.Flush()
        $watch = [Diagnostics.Stopwatch]::StartNew()
        while ($null -eq $result) {
            $remaining = [TimeSpan]::FromSeconds($TimeoutSeconds) - $watch.Elapsed
            Assert-Condition ($remaining -gt [TimeSpan]::Zero) 'MCP response deadline exceeded.'
            $readTask = $script:HostProcess.StandardOutput.ReadLineAsync()
            $line = $readTask.WaitAsync($remaining).GetAwaiter().GetResult()
            Assert-Condition ($null -ne $line) 'Host closed stdout.'
            # dotnet run can emit build banners. Only pre-initialize non-JSON lines are tolerated.
            if (-not $line.TrimStart().StartsWith('{')) {
                Assert-Condition ($Method -ceq 'initialize') 'Unexpected non-protocol stdout.'
                continue
            }
            $response = ConvertFrom-Json -InputObject $line -AsHashtable
            Assert-Condition ($null -ne $response -and $response -is [Collections.IDictionary]) 'Malformed MCP response.'
            Assert-Condition ($response.jsonrpc -ceq '2.0') 'Invalid JSON-RPC version.'
            if (-not $response.ContainsKey('id')) { continue }
            Write-Artifact ('{0:D4}-response.json' -f $id) $response
            Assert-Condition ($response.id -eq $id) 'MCP response id mismatch.'
            Assert-Condition (-not $response.ContainsKey('error')) 'MCP error; see redacted response.'
            Assert-Condition ($response.ContainsKey('result') -and $null -ne $response.result) 'MCP result missing.'
            $result = $response.result
        }
    }
    catch {
        # A deadline read may still own stdout. Never issue recovery requests on that pipe.
        $script:TransportHealthy = $false
        throw
    }
    return $result
}

function Invoke-Tool([string] $Name, [hashtable] $Arguments) {
    Assert-Condition ($Name -in @('get_project_status', 'browse_project_tree', 'execute_read_batch', 'preview_write_batch', 'apply_write_batch', 'compile_check')) 'Tool is outside the allowlist.'
    if ($Name -in @('preview_write_batch', 'apply_write_batch', 'compile_check')) {
        if ($Name -ceq 'apply_write_batch') { Assert-MutationAuthorization }
        Assert-VerifiedStartupBinding
    }
    $result = Invoke-Mcp 'tools/call' @{ name = $Name; arguments = $Arguments }
    Assert-Condition (-not ($result.ContainsKey('isError') -and $result.isError)) 'Public tool returned an error.'
    Assert-Condition ($null -ne $result.content) 'Missing public tool content.'
    $items = @($result.content | Where-Object { $null -ne $_ -and $_.type -ceq 'text' })
    Assert-Condition ($items.Count -eq 1) 'Expected one public JSON text document.'
    $document = ConvertFrom-Json -InputObject $items[0].text -AsHashtable
    Assert-Condition ($null -ne $document -and $document -is [Collections.IDictionary]) 'Invalid public tool envelope.'
    if ($document.ContainsKey('success')) { Assert-Condition ($document.success -eq $true) 'Public tool reported failure.' }
    if ($document.ContainsKey('warnings')) { Assert-Condition (@($document.warnings | Where-Object { $null -ne $_ }).Count -eq 0) 'Warnings prevent complete acceptance evidence.' }
    return $document
}

function Decode-Payload($Envelope) {
    Assert-Condition ($Envelope.success -eq $true -and $null -ne $Envelope.payload) 'Successful payload missing.'
    Assert-Condition ($Envelope.payload -is [string] -and $Envelope.payload -notmatch '\[(TRUNCATED|OMITTED)') 'Payload missing or truncated.'
    $payload = ConvertFrom-Json -InputObject $Envelope.payload -AsHashtable
    Assert-Condition ($null -ne $payload) 'Decoded payload missing.'
    return ,$payload
}

function Assert-VerifiedStartupBinding {
    $status = Invoke-Tool 'get_project_status' @{ projectPath = $ProjectPath }
    Assert-Condition ($status.success -eq $true) 'Startup status did not succeed.'
    $statusPayload = Decode-Payload $status
    Assert-Condition ($statusPayload.isOpen -eq $true) 'Intended disposable project is not open.'
    Assert-Condition ($null -ne $status.sessionIdentity) 'Session identity missing.'
    $intended = Resolve-ProjectPath $ProjectPath
    $payloadPath = Resolve-ProjectPath $statusPayload.path
    $identityPath = Resolve-ProjectPath $status.sessionIdentity.projectPath
    Assert-Condition ([string]::Equals($payloadPath, $intended, [StringComparison]::OrdinalIgnoreCase)) 'Status path differs from intended disposable project.'
    Assert-Condition ([string]::Equals($identityPath, $intended, [StringComparison]::OrdinalIgnoreCase)) 'Session project path differs from intended disposable project.'
    $identity = $status.sessionIdentity
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($identity.workerSessionId)) 'Worker session missing.'
    Assert-Condition ($identity.sessionGeneration -gt 0 -and $identity.portalProcessId -gt 0) 'Session identity incomplete.'
    $signature = '{0}/{1}/{2}' -f $identity.workerSessionId, $identity.sessionGeneration, $identity.portalProcessId
    if ($null -ne $script:InitialIdentity) { Assert-Condition ($signature -ceq $script:InitialIdentity) 'Session changed; refusing further operations.' }
    $script:InitialIdentity = $signature
    Write-Artifact ('{0:D4}-verified-binding.json' -f $script:RequestId) @{
        statusSucceeded = $status.success; isOpen = $statusPayload.isOpen
        payloadPath = $statusPayload.path; sessionIdentityProjectPath = $status.sessionIdentity.projectPath
        canonicalIntendedPath = $intended; utc = [DateTime]::UtcNow.ToString('o')
    }
}

function Assert-MutationAuthorization {
    if ($Mode -cne 'Apply' -or (-not $AllowMutation) -or $Acknowledgement -cne $script:RequiredAcknowledgement) {
        throw 'Apply requires -AllowMutation and the exact case-sensitive acknowledgement.'
    }
}

function New-Operation([string] $Operation, [string] $Path, [hashtable] $Fields = @{}) {
    $item = @{ operationId = [Guid]::NewGuid().ToString('N'); operation = $Operation; projectPath = $ProjectPath; blockPath = $Path }
    foreach ($key in $Fields.Keys) { $item[$key] = $Fields[$key] }
    return $item
}

function Get-Preview([array] $Operations) {
    $preview = Invoke-Tool 'preview_write_batch' @{ operations = $Operations }
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($preview.safetyToken)) 'Preview token missing.'
    [void]$script:Tokens.Add($preview.safetyToken)
    Assert-Condition ($preview.currentStateHash -match '^[0-9a-fA-F]{64}$') 'Preview state hash missing.'
    Assert-Condition ($null -ne $preview.target -and $preview.target.Count -eq $Operations.Count) 'Preview target count mismatch.'
    for ($i = 0; $i -lt $Operations.Count; $i++) {
        Assert-Condition ($preview.target[$i].operationId -ceq $Operations[$i].operationId -and $preview.target[$i].operation -ceq $Operations[$i].operation) 'Preview operation order mismatch.'
    }
    return $preview
}

function Invoke-Apply([array] $Operations, $Preview) {
    Assert-MutationAuthorization
    Assert-Condition ($null -ne $script:Baseline) 'Restorable baseline required before apply.'
    $script:MutationStarted = $true
    $result = Invoke-Tool 'apply_write_batch' @{ operations = $Operations; safetyToken = $Preview.safetyToken; confirm = $true }
    Assert-Condition ($result.success -eq $true -and $result.failed -eq 0 -and $result.succeeded -eq $Operations.Count) 'Apply incomplete; restoration required.'
}

function Invoke-Change($Operation) {
    $operations = @($Operation)
    $preview = Get-Preview $operations
    Invoke-Apply $operations $preview
}

function Read-Tree([string] $StartPath) {
    $envelope = Invoke-Tool 'browse_project_tree' @{ projectPath = $ProjectPath; startPath = $StartPath }
    $nodes = Decode-Payload $envelope
    return ,@($nodes)
}

function Read-BlockBytes([string] $Path) {
    $operation = New-Operation 'get_block_content' $Path @{ format = 'xml' }
    $read = Invoke-Tool 'execute_read_batch' @{ operations = @($operation) }
    Assert-Condition ($read.success -eq $true -and $null -ne $read.operations -and $read.operations.Count -eq 1) 'Block export failed.'
    $item = $read.operations[0]
    Assert-Condition ($item.operationId -ceq $operation.operationId -and $item.status -ceq 'succeeded') 'Block export result mismatch.'
    Assert-Condition (@($item.warnings | Where-Object { $null -ne $_ }).Count -eq 0) 'Block export warnings prevent restoration.'
    $content = $item.result
    Assert-Condition ($content -is [string] -and (-not [string]::IsNullOrWhiteSpace($content)) -and $content -notmatch '\[(TRUNCATED|OMITTED)') 'Block export missing or truncated.'
    Assert-Condition ($content -match '(?m)^--- FILE: [^\r\n]+\.xml ---(\r?\n|$)') 'Authoritative Simatic ML XML is required for restoration.'
    # Preserve the complete deterministic public export, including companion documents.
    return ,$script:Utf8.GetBytes($content)
}

function Read-ProjectContent {
    $roots = Read-Tree $FixtureGroupPath
    Assert-Condition ($roots.Count -eq 1) 'Fixture group must resolve exactly once.'
    $queue = [Collections.Generic.Queue[object]]::new()
    $queue.Enqueue($roots[0])
    $records = [Collections.Generic.List[object]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    while ($queue.Count -gt 0) {
        $node = $queue.Dequeue()
        Assert-Condition ($null -ne $node -and $null -ne $node.details) 'Incomplete tree node.'
        $path = $node.details.Path
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($path)) 'Missing tree node path.'
        Assert-Condition ($path -ceq $FixtureGroupPath -or $path.StartsWith($FixtureGroupPath + '/', [StringComparison]::Ordinal)) 'Tree node outside fixture scope.'
        Assert-Condition ($seen.Add($path)) 'Duplicate tree node.'
        if ($node.nodeType -ceq 'BlockFolder') {
            Assert-Condition ($null -ne $node.children) 'Group child collection missing.'
            $records.Add(@{ path = $path; kind = 'BlockFolder'; bytes = [byte[]]@(); contentSha256 = '' })
            foreach ($child in $node.children) { $queue.Enqueue($child) }
        }
        else {
            Assert-Condition ($node.nodeType -ceq 'FC' -and $node.details.ProgrammingLanguage -ceq 'SCL') 'Restoration fixture supports only independent SCL FC blocks.'
            Assert-Condition (-not $node.details.ContainsKey('IsSystemBlock')) 'System content is not a disposable fixture.'
            $bytes = Read-BlockBytes $path
            $records.Add(@{ path = $path; kind = 'FC'; bytes = $bytes; contentSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)) })
        }
    }
    Assert-Condition ($records[0].path -ceq $FixtureGroupPath -and $records[0].kind -ceq 'BlockFolder') 'Fixture root must be a user group.'
    return ,@($records | Sort-Object { $_.path })
}

function Restore-ByteEquivalentProjectContent {
    Assert-MutationAuthorization
    Assert-Condition ($script:TransportHealthy) 'Transport failed after mutation. Manual recovery from baseline is required; restoration cannot be claimed.'
    # A failed delete can leave some or all of the original subtree. Remove that exact
    # current fixture root before rebuilding; no object outside the root is addressed.
    $parentPath = $FixtureGroupPath.Substring(0, $FixtureGroupPath.LastIndexOf('/'))
    $parent = Read-Tree $parentPath
    Assert-Condition ($parent.Count -eq 1 -and $null -ne $parent[0].children) 'Surviving fixture parent is incomplete.'
    $current = @($parent[0].children | Where-Object { $null -ne $_.details -and $_.details.Path -ceq $FixtureGroupPath })
    Assert-Condition ($current.Count -le 1) 'Fixture membership is ambiguous.'
    if ($current.Count -eq 1) {
        Assert-Condition ($current[0].nodeType -ceq 'BlockFolder') 'Fixture membership changed kind.'
        Invoke-Change (New-Operation 'delete_block_group' $FixtureGroupPath)
    }
    foreach ($group in @($script:Baseline | Where-Object { $_.kind -ceq 'BlockFolder' } | Sort-Object { ($_.path -split '/').Count }, { $_.path })) {
        Invoke-Change (New-Operation 'create_block_group' $group.path)
    }
    # Create every independent placeholder first, then restore every exact bundle.
    foreach ($block in @($script:Baseline | Where-Object { $_.kind -ceq 'FC' })) {
        Invoke-Change (New-Operation 'create_block' $block.path @{ blockType = 'FC'; language = 'SCL' })
    }
    foreach ($block in @($script:Baseline | Where-Object { $_.kind -ceq 'FC' })) {
        $original = $script:Utf8.GetString($block.bytes)
        Invoke-Change (New-Operation 'update_block_logic' $block.path @{ format = 'xml'; yamlContent = $original })
    }
}

function Assert-ByteEquivalentProjectContent($Expected, $Actual) {
    Assert-Condition ($null -ne $Expected -and $null -ne $Actual -and $Expected.Count -eq $Actual.Count) 'Restored subtree membership differs.'
    $evidence = @()
    for ($i = 0; $i -lt $Expected.Count; $i++) {
        $before = $Expected[$i]
        $after = $Actual[$i]
        Assert-Condition ($before.path -ceq $after.path -and $before.kind -ceq $after.kind) 'Restored subtree paths or kinds differ.'
        # Full byte comparison is required; a digest alone is not the restoration proof.
        Assert-Condition ([Convert]::ToBase64String($before.bytes) -ceq [Convert]::ToBase64String($after.bytes)) 'Restored exported content differs byte-for-byte.'
        $evidence += @{ path = $before.path; preApplyContentSha256 = $before.contentSha256; restoredContentSha256 = $after.contentSha256; bytesEqual = $true }
    }
    Write-Artifact ('{0:D4}-byte-equivalence.json' -f $script:RequestId) $evidence
}

function Invoke-CompileCheck {
    Assert-Condition ($script:MutationStarted -and $script:RestorationProven) 'Final compile requires proven restoration after mutation.'
    $compile = Invoke-Tool 'compile_check' @{ projectPath = $ProjectPath; plcName = ($FixtureGroupPath -split '/')[0] }
    $payload = Decode-Payload $compile
    Assert-Condition ($payload.overallState -ceq 'Success' -and $payload.totalErrorCount -eq 0 -and $null -ne $payload.plcs -and $payload.plcs.Count -gt 0) 'Restored fixture did not compile successfully.'
    Write-Artifact 'final-compile.json' $compile
}

function Stop-McpHost {
    if ($null -ne $script:HostProcess) {
        try {
            if (-not $script:HostProcess.HasExited) {
                try { $script:HostProcess.StandardInput.Close() } catch { }
                if (-not $script:HostProcess.WaitForExit(5000)) {
                    $script:HostProcess.Kill($true)
                    Assert-Condition ($script:HostProcess.WaitForExit(5000)) 'Host failed to stop.'
                }
            }
            # stderr is drained concurrently, but raw logs may contain source or tokens.
            # Only its completion state is retained; never persist unparsed process output.
            Write-Artifact 'host-cleanup.json' @{ stopped = $script:HostProcess.HasExited; stderrDrainComplete = ($null -ne $script:StderrTask -and $script:StderrTask.IsCompleted) }
        }
        finally { $script:HostProcess.Dispose() }
    }
}

# Main - initialization and every operation are deliberately outside test execution.
$ProjectPath = Resolve-ProjectPath $ProjectPath
Assert-Condition ([IO.Path]::GetExtension($ProjectPath) -ieq '.ap21') 'The disposable project must be an .ap21 file.'
if ($Mode -ceq 'Apply') { Assert-MutationAuthorization }
if ($Mode -cne 'Inventory') {
    Assert-Condition ($FixtureGroupPath -cmatch '^[^/]+/(?:Units/[^/]+/)?Blocks/[^/]+(?:/[^/]+)*$') 'Provide an exact user fixture group path.'
    Assert-Condition ($FixtureGroupPath -notmatch '(^|/)\.\.?(/|$)' -and $FixtureGroupPath -notmatch '\\') 'Fixture path contains an unsafe segment.'
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($OccupiedBlockPath)) 'Provide an existing occupied block path.'
    Assert-Condition ($OccupiedBlockPath.StartsWith($FixtureGroupPath + '/', [StringComparison]::Ordinal)) 'Occupied block must be inside the fixture group.'
    Assert-Condition ($NewGroupName -cmatch '^[A-Za-z_][A-Za-z0-9_]*$') 'New group name must be a simple fixture identifier.'
}
if (-not $HostArguments -or $HostArguments.Count -eq 0) {
    $HostArguments = @('run', '--project', 'TiaMcpServer', '--', '--project', $ProjectPath)
}
$runName = 'tia-project-tree-safety-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N')
$script:ArtifactDirectory = Join-Path ([IO.Path]::GetFullPath($ArtifactRoot)) $runName
[void][IO.Directory]::CreateDirectory($script:ArtifactDirectory)
try {
    Write-Artifact 'manifest.json' @{ mode = $Mode; projectPath = $ProjectPath; fixtureGroupPath = $FixtureGroupPath; occupiedBlockPath = $OccupiedBlockPath; utc = [DateTime]::UtcNow.ToString('o') }
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $HostExecutable
    $start.WorkingDirectory = Split-Path -Parent $PSScriptRoot
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardInputEncoding = $script:Utf8
    $start.StandardOutputEncoding = $script:Utf8
    $start.StandardErrorEncoding = $script:Utf8
    foreach ($argument in $HostArguments) { $start.ArgumentList.Add($argument) }
    $script:HostProcess = [Diagnostics.Process]::Start($start)
    $script:StderrTask = $script:HostProcess.StandardError.ReadToEndAsync()
    $null = Invoke-Mcp 'initialize' @{ protocolVersion = '2024-11-05'; capabilities = @{}; clientInfo = @{ name = 'pr6-guarded-live-harness'; version = '1.0' } } $StartupTimeoutSeconds
    $script:HostProcess.StandardInput.WriteLine((ConvertTo-Json -InputObject @{ jsonrpc = '2.0'; method = 'notifications/initialized'; params = @{} } -Depth 100 -Compress))
    $script:HostProcess.StandardInput.Flush()
    Assert-VerifiedStartupBinding
    $inventory = Invoke-Mcp 'tools/list' @{}
    Write-Artifact 'tool-inventory.json' $inventory
    if ($Mode -cne 'Inventory') {
        $baseline = Read-ProjectContent
        Assert-Condition (@($baseline | Where-Object { $_.path -ceq $OccupiedBlockPath -and $_.kind -ceq 'FC' }).Count -eq 1) 'Occupied fixture SCL FC missing.'
        $newGroupPath = $FixtureGroupPath + '/' + $NewGroupName
        Assert-Condition (@($baseline | Where-Object { [string]::Equals($_.path, $newGroupPath, [StringComparison]::OrdinalIgnoreCase) }).Count -eq 0) 'New group name is occupied.'
        $script:Baseline = $baseline
        Write-Artifact 'pre-apply-baseline.json' @{ projectPath = $ProjectPath; records = $baseline; preApplyContentSha256 = @($baseline | ForEach-Object { $_.contentSha256 }) }
        $operations = @(
            (New-Operation 'create_block' $OccupiedBlockPath @{ blockType = 'FC'; language = 'SCL' }),
            (New-Operation 'create_block_group' $newGroupPath),
            (New-Operation 'delete_block_group' $FixtureGroupPath)
        )
        # Ordered previews remain separate, so each snapshot corresponds to its actual state.
        if ($Mode -ceq 'Preview') {
            foreach ($operation in $operations) { $null = Get-Preview @($operation) }
        }
        if ($Mode -ceq 'Apply') {
            # Re-export twice before the first apply to prove deterministic baseline bytes.
            Assert-ByteEquivalentProjectContent $script:Baseline (Read-ProjectContent)
            try {
                foreach ($operation in $operations) {
                    $preview = Get-Preview @($operation)
                    Invoke-Apply @($operation) $preview
                }
            }
            finally {
                if ($script:MutationStarted) {
                    Restore-ByteEquivalentProjectContent
                    $restored = Read-ProjectContent
                    Write-Artifact 'restored-baseline.json' @{ records = $restored; restoredContentSha256 = @($restored | ForEach-Object { $_.contentSha256 }) }
                    Assert-ByteEquivalentProjectContent $script:Baseline $restored
                    $script:RestorationProven = $true
                }
            }
            Invoke-CompileCheck
        }
    }
    $script:RunSucceeded = $true
}
catch {
    # Fixed error text prevents accidental propagation of tokens in exception messages.
    Write-Artifact 'failure.json' @{ failed = $true; mutationStarted = $script:MutationStarted; restorationProven = $script:RestorationProven; transportHealthy = $script:TransportHealthy; manualRecoveryRequired = ($script:MutationStarted -and (-not $script:RestorationProven)); errorType = $_.Exception.GetType().FullName }
    throw 'Harness failed. Review private redacted run artifacts; if restoration is unproven, recover the exact fixture baseline manually.'
}
finally {
    try { Stop-McpHost }
    catch { $script:RunSucceeded = $false; throw 'Host cleanup failed; inspect the private run artifacts.' }
    finally {
        Write-Artifact 'result.json' @{ success = $script:RunSucceeded; mode = $Mode; mutationStarted = $script:MutationStarted; restorationProven = $script:RestorationProven; utc = [DateTime]::UtcNow.ToString('o') }
    }
}
Write-Output ('[OK] {0} finished. Review artifacts: {1}' -f $Mode, $script:ArtifactDirectory)
