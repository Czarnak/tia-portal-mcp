#Requires -Version 7
<#
.SYNOPSIS
    Separately authorized live TIA Portal V21 acceptance harness for the PR 3 update_tag safety snapshot.

.DESCRIPTION
    Read is non-mutating. PreviewDrift obtains a preview token for one flag-only update. ApplyDrift
    is the separately authorized disposable-copy acceptance: it makes one intermediate flag-only
    change, proves the original token rejects with state_changed, and restores the original value.
    ProbeUnavailable is optional and only applies to a separately supplied target whose chosen flag
    is actually unavailable. Ordinary tests inspect this script; they never invoke it.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ProjectPath,
    [Parameter(Mandatory)][string]$TableName,
    [Parameter(Mandatory)][string]$TagName,
    [string]$PlcName,
    [ValidateSet('ExternalAccessible','ExternalVisible','ExternalWritable')][string]$DriftFlagName = 'ExternalVisible',
    [string]$ProbeTableName,
    [string]$ProbeTagName,
    [ValidateSet('ExternalAccessible','ExternalVisible','ExternalWritable')][string]$ProbeFlagName = 'ExternalVisible',
    [ValidateSet('Read','PreviewDrift','ApplyDrift','ProbeUnavailable')][string]$Mode = 'Read',
    [switch]$AllowApply,
    [string]$HostExecutable = 'dotnet',
    [string[]]$HostArguments,
    [string]$WorkerExecutable,
    [ValidateRange(10, 600)][int]$TimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RepositoryRoot = Split-Path -Parent $PSScriptRoot
$script:HostProcess = $null
$script:WorkerProcess = $null
$script:NextRequestId = 0
$script:WorkerSessionIdentity = $null

function Start-JsonLineProcess {
    param(
        [Parameter(Mandatory)][string]$Executable,
        [string[]]$Arguments,
        [Parameter(Mandatory)][string]$Label
    )

    if (-not (Test-Path -LiteralPath $Executable -PathType Leaf) -and $Executable -ne 'dotnet') {
        throw "$Label executable was not found: $Executable"
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Executable
    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $false
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "Failed to start $Label."
    }
    return $process
}

function Stop-JsonLineProcess {
    param([System.Diagnostics.Process]$Process)

    if ($null -eq $Process) {
        return
    }

    try {
        if (-not $Process.HasExited) {
            try { $Process.StandardInput.Close() } catch { [Console]::Error.WriteLine($_.Exception.Message) }
            if (-not $Process.WaitForExit(5000)) {
                $Process.Kill($true)
                if (-not $Process.WaitForExit(5000)) {
                    throw 'The child process tree did not exit within 5 seconds after forced termination.'
                }
            }
        }
    }
    finally {
        $Process.Dispose()
    }
}

function Send-JsonLine {
    param(
        [Parameter(Mandatory)][System.Diagnostics.Process]$Process,
        [Parameter(Mandatory)][object]$Message
    )

    $json = $Message | ConvertTo-Json -Compress -Depth 100
    $Process.StandardInput.WriteLine($json)
    $Process.StandardInput.Flush()
}

function Read-JsonLine {
    param(
        [Parameter(Mandatory)][System.Diagnostics.Process]$Process,
        [int]$ExpectedId = -1
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($Process.HasExited) {
            throw "The child process exited with code $($Process.ExitCode)."
        }

        $readTask = $Process.StandardOutput.ReadLineAsync()
        $remaining = $deadline - [DateTimeOffset]::UtcNow
        if ($remaining -le [TimeSpan]::Zero -or -not $readTask.Wait($remaining)) {
            break
        }

        $line = $readTask.Result
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }
        try { $candidate = $line | ConvertFrom-Json -Depth 100 } catch { continue }
        if ($null -eq $candidate) {
            continue
        }
        if ($ExpectedId -lt 0) {
            return $candidate
        }
        $idProperty = $candidate.PSObject.Properties['id']
        if ($null -ne $idProperty -and $candidate.id -eq $ExpectedId) {
            return $candidate
        }
    }
    throw "Timed out after $TimeoutSeconds second(s) waiting for a JSON response."
}

function Assert-WorkerSuccess {
    param(
        [Parameter(Mandatory)][object]$Response,
        [Parameter(Mandatory)][string]$Method
    )

    if ($null -eq $Response -or -not $Response.success) {
        $category = if ($null -ne $Response -and $null -ne $Response.failureCategory) { $Response.failureCategory } else { 'unknown' }
        $error = if ($null -ne $Response -and $null -ne $Response.error) { $Response.error } else { 'no response' }
        throw "Worker method '$Method' failed ($category): $error"
    }
}

function Invoke-WorkerRequest {
    param(
        [Parameter(Mandatory)][string]$Method,
        [Parameter(Mandatory)][hashtable]$Arguments
    )

    $request = @{ method = $Method; protocolVersion = 'project-binding-v1' }
    foreach ($key in $Arguments.Keys) { $request[$key] = $Arguments[$key] }
    Send-JsonLine -Process $script:WorkerProcess -Message $request
    $response = Read-JsonLine -Process $script:WorkerProcess
    Assert-WorkerSuccess -Response $response -Method $Method
    return $response
}

function Convert-WorkerPayload {
    param([Parameter(Mandatory)][object]$Response)

    if ($null -eq $Response.payload -or [string]::IsNullOrWhiteSpace([string]$Response.payload)) {
        throw 'The worker returned no payload.'
    }
    $payload = $Response.payload | ConvertFrom-Json -Depth 100
    if ($null -eq $payload) {
        throw 'The worker returned a null payload.'
    }
    return $payload
}

function Connect-Worker {
    Send-JsonLine -Process $script:WorkerProcess -Message @{
        method = 'hello'
        protocolVersion = 'project-binding-v1'
    }
    $hello = Read-JsonLine -Process $script:WorkerProcess
    Assert-WorkerSuccess -Response $hello -Method 'hello'
    if ($hello.protocolVersion -ne 'project-binding-v1') {
        throw "Worker hello returned incompatible protocolVersion '$($hello.protocolVersion)'."
    }
    $capabilities = @($hello.capabilities)
    foreach ($required in @('expected-session-identity', 'response-session-identity', 'deterministic-project-selection')) {
        if ($capabilities -notcontains $required) {
            throw "Worker hello did not advertise required capability '$required'."
        }
    }
}

function Get-CompleteSessionIdentity {
    $statusResponse = Invoke-WorkerRequest -Method 'get_project_status' -Arguments @{ projectPath = $ProjectPath }
    $identity = $statusResponse.sessionIdentity
    if ($null -eq $identity `
        -or [string]::IsNullOrWhiteSpace([string]$identity.workerSessionId) `
        -or $identity.sessionGeneration -lt 1 `
        -or $null -eq $identity.portalProcessId -or $identity.portalProcessId -lt 1 `
        -or [string]::IsNullOrWhiteSpace([string]$identity.projectPath)) {
        throw 'get_project_status did not return a complete worker sessionIdentity.'
    }
    $script:WorkerSessionIdentity = $identity
}

function Read-UpdateTagSafetySnapshot {
    param(
        [Parameter(Mandatory)][string]$RequestedTableName,
        [Parameter(Mandatory)][string]$RequestedTagName,
        [string]$RequestedPlcName
    )

    if ($null -eq $script:WorkerSessionIdentity) {
        throw 'A worker-stamped sessionIdentity is required before an internal safety read.'
    }
    $response = Invoke-WorkerRequest -Method 'read_update_tag_safety_snapshot' -Arguments @{
        projectPath = $ProjectPath
        plcName = $RequestedPlcName
        tableName = $RequestedTableName
        folderPath = '/'
        name = $RequestedTagName
        expectedSessionIdentity = $script:WorkerSessionIdentity
    }
    $snapshot = Convert-WorkerPayload -Response $response
    if ([string]::IsNullOrWhiteSpace([string]$snapshot.plcName)) {
        throw 'The strict update_tag snapshot did not resolve a PLC name.'
    }
    return $snapshot
}

function Get-ReadableSnapshotFlag {
    param(
        [Parameter(Mandatory)][object]$Snapshot,
        [Parameter(Mandatory)][string]$FlagName
    )

    $property = $Snapshot.PSObject.Properties[$FlagName]
    if ($null -eq $property -or $null -eq $property.Value) {
        throw "The mandatory drift flag '$FlagName' is unreadable on the exact target. This is a live-gate failure."
    }
    return [bool]$property.Value
}

function Start-McpHost {
    $script:HostProcess = Start-JsonLineProcess -Executable $HostExecutable -Arguments $HostArguments -Label 'MCP host'
    $null = Invoke-McpRequest -Method 'initialize' -Params @{
        protocolVersion = '2025-06-18'
        capabilities = @{}
        clientInfo = @{ name = 'live-test-update-tag-safety'; version = '1.0.0' }
    }
    Invoke-McpNotification -Method 'notifications/initialized'
    $tools = Invoke-McpRequest -Method 'tools/list'
    $toolNames = @($tools.tools | ForEach-Object { $_.name })
    foreach ($required in @('execute_read_batch', 'preview_write_batch', 'apply_write_batch')) {
        if ($toolNames -notcontains $required) {
            throw "The MCP host does not advertise '$required'."
        }
    }
}

function Invoke-McpRequest {
    param([Parameter(Mandatory)][string]$Method, [hashtable]$Params = @{})

    $id = ++$script:NextRequestId
    Send-JsonLine -Process $script:HostProcess -Message @{ jsonrpc = '2.0'; id = $id; method = $Method; params = $Params }
    $response = Read-JsonLine -Process $script:HostProcess -ExpectedId $id
    if ($null -ne $response.error) {
        throw "MCP request '$Method' failed: $($response.error | ConvertTo-Json -Compress -Depth 20)"
    }
    return $response.result
}

function Invoke-McpNotification {
    param([Parameter(Mandatory)][string]$Method, [hashtable]$Params = @{})
    Send-JsonLine -Process $script:HostProcess -Message @{ jsonrpc = '2.0'; method = $Method; params = $Params }
}

function Test-McpToolResultIsError {
    param([Parameter(Mandatory)][object]$Result)

    $property = $Result.PSObject.Properties['isError']
    if ($null -eq $property) {
        return $false
    }
    return [bool]$property.Value
}

function Invoke-McpTool {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][hashtable]$Arguments,
        [switch]$AllowApplicationError
    )

    $result = Invoke-McpRequest -Method 'tools/call' -Params @{ name = $Name; arguments = $Arguments }
    if ($null -eq $result) {
        throw "MCP tool '$Name' returned no result."
    }
    if ((Test-McpToolResultIsError -Result $result) -and -not $AllowApplicationError) {
        $errorText = if ($null -ne $result.content -and @($result.content).Count -gt 0) { [string]$result.content[0].text } else { 'no error content' }
        throw "MCP tool '$Name' returned an application error: $errorText"
    }
    $text = if ($null -ne $result.content -and @($result.content).Count -gt 0) { [string]$result.content[0].text } else { $null }
    $document = if ([string]::IsNullOrWhiteSpace($text)) { $null } else { $text | ConvertFrom-Json -Depth 100 }
    return [pscustomobject]@{ Result = $result; Text = $text; Document = $document }
}

function Assert-OptionalProbeTargetIsDistinct {
    # ProbeUnavailable has no independent PLC/folder selector. Its target therefore shares the
    # mandatory target's $PlcName and root folder scope; table/tag must identify a second target.
    $samePlcScope = $true
    $sameFolderScope = $true
    $sameTable = [string]::Equals($ProbeTableName, $TableName, [System.StringComparison]::Ordinal)
    $sameTag = [string]::Equals($ProbeTagName, $TagName, [System.StringComparison]::Ordinal)
    if ($samePlcScope -and $sameFolderScope -and $sameTable -and $sameTag) {
        throw 'Mode ProbeUnavailable requires a second table/tag target distinct from the mandatory drift target.'
    }
}

function New-UpdateTagOperation {
    param(
        [Parameter(Mandatory)][object]$Snapshot,
        [Parameter(Mandatory)][string]$FlagName,
        [Parameter(Mandatory)][bool]$Value,
        [Parameter(Mandatory)][string]$OperationId
    )

    $operation = [ordered]@{
        operationId = $OperationId
        operation = 'update_tag'
        projectPath = $ProjectPath
        plcName = [string]$Snapshot.plcName
        tableName = [string]$Snapshot.tableName
        folderPath = [string]$Snapshot.folderPath
        name = [string]$Snapshot.tagName
    }
    $operation[$FlagName] = $Value
    return $operation
}

function Get-PreviewToken {
    param([Parameter(Mandatory)][object]$ToolCall)

    if ((Test-McpToolResultIsError -Result $ToolCall.Result) -or $null -eq $ToolCall.Document) {
        throw "preview_write_batch failed before issuing a token: $($ToolCall.Text)"
    }
    $token = $ToolCall.Document.safetyToken
    if ([string]::IsNullOrWhiteSpace([string]$token)) {
        throw 'preview_write_batch succeeded without a safetyToken.'
    }
    return [string]$token
}

function Invoke-Preview {
    param([Parameter(Mandatory)][object]$Operation)
    return Invoke-McpTool -Name 'preview_write_batch' -Arguments @{ operations = @($Operation) }
}

function Invoke-Apply {
    param(
        [Parameter(Mandatory)][object]$Operation,
        [Parameter(Mandatory)][string]$SafetyToken
    )
    return Invoke-McpTool -Name 'apply_write_batch' -Arguments @{
        confirm = $true
        safetyToken = $SafetyToken
        operations = @($Operation)
    }
}

function Assert-SnapshotFlagEquals {
    param(
        [Parameter(Mandatory)][object]$Snapshot,
        [Parameter(Mandatory)][string]$FlagName,
        [Parameter(Mandatory)][bool]$ExpectedValue
    )

    $actualValue = Get-ReadableSnapshotFlag -Snapshot $Snapshot -FlagName $FlagName
    if ($actualValue -ne $ExpectedValue) {
        throw "Strict snapshot '$FlagName' was '$actualValue', expected original value '$ExpectedValue'."
    }
}

function Assert-PublicTagRowMatchesSnapshot {
    param(
        [Parameter(Mandatory)][object]$ToolCall,
        [Parameter(Mandatory)][object]$Snapshot
    )

    if ((Test-McpToolResultIsError -Result $ToolCall.Result) -or $null -eq $ToolCall.Document -or -not $ToolCall.Document.success) {
        throw "list_tag_tables returned an application error: $($ToolCall.Text)"
    }
    $operations = @($ToolCall.Document.operations)
    if ($operations.Count -ne 1 -or $operations[0].operation -ne 'list_tag_tables' -or $operations[0].status -ne 'succeeded') {
        throw "list_tag_tables did not return exactly one successful operation: $($ToolCall.Text)"
    }
    $payload = $operations[0].result
    if ($payload -is [string]) {
        $payload = $payload | ConvertFrom-Json -Depth 100
    }
    if ($null -eq $payload -or $null -eq $payload.tables) {
        throw "list_tag_tables returned no tables payload: $($ToolCall.Text)"
    }
    $tables = @($payload.tables)
    $tables = @($tables | Where-Object {
        $_.name -eq $Snapshot.tableName -and $_.folderPath -eq $Snapshot.folderPath
    })
    if ($tables.Count -ne 1) {
        throw "list_tag_tables did not return exactly one matching table '$($Snapshot.tableName)'."
    }
    $tags = @($tables[0].tags | Where-Object { $_.name -eq $Snapshot.tagName })
    if ($tags.Count -ne 1) {
        throw "list_tag_tables did not return exactly one matching tag '$($Snapshot.tagName)'."
    }
    if ($tags[0].dataType -ne $Snapshot.dataType -or $tags[0].logicalAddress -ne $Snapshot.logicalAddress) {
        throw "list_tag_tables tag values differ from the strict snapshot for '$($Snapshot.tagName)'."
    }
}

function Write-SnapshotEvidence {
    param(
        [Parameter(Mandatory)][object]$Snapshot,
        [Parameter(Mandatory)][string]$FlagName
    )
    $flag = $Snapshot.PSObject.Properties[$FlagName].Value
    Write-Output "resolvedPlcName: $($Snapshot.plcName)"
    Write-Output "tableName: $($Snapshot.tableName)"
    Write-Output "tagName: $($Snapshot.tagName)"
    Write-Output "${FlagName}: $flag"
}

function Invoke-Main {
    if ($Mode -eq 'ApplyDrift' -and -not $AllowApply) {
        throw "Mode 'ApplyDrift' requires -AllowApply. It changes one flag on a disposable project copy."
    }

    if ($Mode -eq 'ProbeUnavailable') {
        foreach ($required in @('ProbeTableName', 'ProbeTagName', 'ProbeFlagName')) {
            if ([string]::IsNullOrWhiteSpace([string](Get-Variable -Name $required -ValueOnly))) {
                throw "Mode 'ProbeUnavailable' requires -$required. This optional probe uses a separate target."
            }
        }
        Assert-OptionalProbeTargetIsDistinct
    }

    if ($null -eq $HostArguments -or $HostArguments.Count -eq 0) {
        $hostDll = Join-Path $script:RepositoryRoot 'TiaMcpServer\bin\Debug\net8.0\TiaMcpServer.dll'
        $HostArguments = @($hostDll)
    }

    if ([string]::IsNullOrWhiteSpace($WorkerExecutable)) {
        $WorkerExecutable = Join-Path $script:RepositoryRoot 'TiaMcpServer\bin\Debug\net8.0\openness-worker\TiaMcpServer.OpennessWorker.exe'
    }

    try {
        $script:WorkerProcess = Start-JsonLineProcess -Executable $WorkerExecutable -Arguments @() -Label 'Openness worker'
        Connect-Worker
        Get-CompleteSessionIdentity
        Start-McpHost

        switch ($Mode) {
        'Read' {
            $snapshot = Read-UpdateTagSafetySnapshot -RequestedTableName $TableName -RequestedTagName $TagName -RequestedPlcName $PlcName
            $null = Get-ReadableSnapshotFlag -Snapshot $snapshot -FlagName $DriftFlagName
            Write-SnapshotEvidence -Snapshot $snapshot -FlagName $DriftFlagName
            $listResult = Invoke-McpTool -Name 'execute_read_batch' -Arguments @{ operations = @(@{
                operationId = 'public-list-tag-tables'
                operation = 'list_tag_tables'
                projectPath = $ProjectPath
                plcName = $PlcName
            }) }
            Assert-PublicTagRowMatchesSnapshot -ToolCall $listResult -Snapshot $snapshot
            Write-Output 'list_tag_tables public response:'
            Write-Output $listResult.Text
        }
        'PreviewDrift' {
            $snapshot = Read-UpdateTagSafetySnapshot -RequestedTableName $TableName -RequestedTagName $TagName -RequestedPlcName $PlcName
            $currentValue = Get-ReadableSnapshotFlag -Snapshot $snapshot -FlagName $DriftFlagName
            $operation = New-UpdateTagOperation -Snapshot $snapshot -FlagName $DriftFlagName -Value (-not $currentValue) -OperationId 'update-tag-preview-drift'
            $preview = Invoke-Preview -Operation $operation
            $token = Get-PreviewToken -ToolCall $preview
            Write-SnapshotEvidence -Snapshot $snapshot -FlagName $DriftFlagName
            Write-Output "safetyToken: $token"
        }
        'ApplyDrift' {
            $snapshot = Read-UpdateTagSafetySnapshot -RequestedTableName $TableName -RequestedTagName $TagName -RequestedPlcName $PlcName
            $currentValue = Get-ReadableSnapshotFlag -Snapshot $snapshot -FlagName $DriftFlagName
            $originalOperation = New-UpdateTagOperation -Snapshot $snapshot -FlagName $DriftFlagName -Value (-not $currentValue) -OperationId 'update-tag-stale-token'
            $originalPreview = Invoke-Preview -Operation $originalOperation
            $originalToken = Get-PreviewToken -ToolCall $originalPreview
            $reconciliationIntent = New-UpdateTagOperation -Snapshot $snapshot -FlagName $DriftFlagName -Value $currentValue -OperationId 'update-tag-restore-original-flag'
            $reconciliationRequired = $true
            try {
                $intermediatePreview = Invoke-Preview -Operation $originalOperation
                $intermediateToken = Get-PreviewToken -ToolCall $intermediatePreview
                $intermediateApply = Invoke-Apply -Operation $originalOperation -SafetyToken $intermediateToken
                if (Test-McpToolResultIsError -Result $intermediateApply.Result) {
                    throw "The authorized intermediate update_tag drift failed: $($intermediateApply.Text)"
                }

                $staleApply = Invoke-Apply -Operation $originalOperation -SafetyToken $originalToken
                $failureCategory = if ($null -ne $staleApply.Document) { [string]$staleApply.Document.failureCategory } else { '' }
                if ($failureCategory -ne 'state_changed') {
                    throw "The stale-token apply did not reject with state_changed: $($staleApply.Text)"
                }
                Write-Output 'stale apply failureCategory: state_changed'
                Write-SnapshotEvidence -Snapshot (Read-UpdateTagSafetySnapshot -RequestedTableName $TableName -RequestedTagName $TagName -RequestedPlcName $PlcName) -FlagName $DriftFlagName
            }
            finally {
                if (-not $reconciliationRequired) {
                    throw 'ApplyDrift lost its required reconciliation plan before the intermediate mutation completed.'
                }
                $restoreSnapshot = Read-UpdateTagSafetySnapshot -RequestedTableName $TableName -RequestedTagName $TagName -RequestedPlcName $PlcName
                $restoreValue = Get-ReadableSnapshotFlag -Snapshot $restoreSnapshot -FlagName $DriftFlagName
                if ($restoreValue -ne $currentValue) {
                    $restoreOperation = New-UpdateTagOperation -Snapshot $restoreSnapshot -FlagName $DriftFlagName -Value $currentValue -OperationId 'update-tag-restore-original-flag'
                    if ($restoreOperation.tableName -ne $reconciliationIntent.tableName -or $restoreOperation.folderPath -ne $reconciliationIntent.folderPath -or $restoreOperation.name -ne $reconciliationIntent.name) {
                        throw 'The fresh reconciliation snapshot no longer identifies the originally planned tag target.'
                    }
                    $restorePreview = Invoke-Preview -Operation $restoreOperation
                    $restoreToken = Get-PreviewToken -ToolCall $restorePreview
                    $restoreApply = Invoke-Apply -Operation $restoreOperation -SafetyToken $restoreToken
                    if (Test-McpToolResultIsError -Result $restoreApply.Result) {
                        throw "The restoration update_tag failed: $($restoreApply.Text)"
                    }
                    Write-Output 'Restored the original flag value on the disposable copy.'
                }
                $finalSnapshot = Read-UpdateTagSafetySnapshot -RequestedTableName $TableName -RequestedTagName $TagName -RequestedPlcName $PlcName
                Assert-SnapshotFlagEquals -Snapshot $finalSnapshot -FlagName $DriftFlagName -ExpectedValue $currentValue
                Write-SnapshotEvidence -Snapshot $finalSnapshot -FlagName $DriftFlagName
            }
        }
        'ProbeUnavailable' {
            $probeSnapshot = Read-UpdateTagSafetySnapshot -RequestedTableName $ProbeTableName -RequestedTagName $ProbeTagName -RequestedPlcName $PlcName
            $probeProperty = $probeSnapshot.PSObject.Properties[$ProbeFlagName]
            if ($null -eq $probeProperty -or $null -ne $probeProperty.Value) {
                throw "Optional unavailable probe is NOT RUN: '$ProbeFlagName' is readable on the supplied second target."
            }
            $probeOperation = New-UpdateTagOperation -Snapshot $probeSnapshot -FlagName $ProbeFlagName -Value $true -OperationId 'update-tag-unavailable-flag-probe'
            $probePreview = Invoke-McpTool -Name 'preview_write_batch' -Arguments @{ operations = @($probeOperation) } -AllowApplicationError
            if (-not (Test-McpToolResultIsError -Result $probePreview.Result)) {
                throw "Optional unavailable probe unexpectedly issued a preview result: $($probePreview.Text)"
            }
            if ($null -ne $probePreview.Document -and -not [string]::IsNullOrWhiteSpace([string]$probePreview.Document.safetyToken)) {
                throw 'Optional unavailable probe unexpectedly issued a safety token.'
            }
            Write-Output 'Optional unavailable flag preview rejected before token issuance.'
        }
        }
    }
    finally {
        Stop-JsonLineProcess -Process $script:HostProcess
        Stop-JsonLineProcess -Process $script:WorkerProcess
    }
}

Invoke-Main
