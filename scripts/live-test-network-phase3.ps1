#Requires -Version 7
<#
.SYNOPSIS
    Separately authorized, read-only live acceptance harness for Network Phase 3.

.DESCRIPTION
    Matrix, Repeatability, and MeasureListValue use the normal MCP host in read-only access mode.
    RawProbe first discovers one selectable target through network_read, then sends that selector
    to an internal read-only worker diagnostic route. No mode in this script changes a TIA project.

    This file is acceptance tooling. Ordinary tests inspect its source and never invoke it.
#>
[CmdletBinding()]
param(
    [ValidateSet('Matrix', 'Repeatability', 'MeasureListValue', 'RawProbe')]
    [string] $Mode = 'Matrix',

    [string] $HostExecutable = 'dotnet',
    [string[]] $HostArguments,
    [string] $WorkerExecutable,
    [int] $TimeoutSeconds = 60,
    [ValidateRange(1, 200)]
    [int] $PageSize = 100
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RepositoryRoot = Split-Path -Parent $PSScriptRoot
$script:McpProcess = $null
$script:WorkerProcess = $null
$script:NextRequestId = 0
$script:NetworkRequestCount = 0
$script:ObjectKinds = @(
    'deviceItem',
    'networkInterface',
    'node',
    'subnet',
    'ioSystem',
    'communicationConnection'
)

if ($null -eq $HostArguments -or $HostArguments.Count -eq 0) {
    $hostProject = Join-Path $script:RepositoryRoot 'TiaMcpServer'
    $HostArguments = @(
        'run',
        '--no-build',
        '--project',
        $hostProject,
        '--',
        '--access-mode',
        'read-only'
    )
}

if ([string]::IsNullOrWhiteSpace($WorkerExecutable)) {
    $hostBin = Join-Path $script:RepositoryRoot 'TiaMcpServer'
    $hostBin = Join-Path $hostBin 'bin'
    $hostBin = Join-Path $hostBin 'Debug'
    $hostBin = Join-Path $hostBin 'net8.0'
    $workerBin = Join-Path $hostBin 'openness-worker'
    $WorkerExecutable = Join-Path $workerBin 'TiaMcpServer.OpennessWorker.exe'
}

function Start-JsonLineProcess {
    param(
        [Parameter(Mandatory)] [string] $Executable,
        [Parameter(Mandatory)] [string[]] $Arguments,
        [Parameter(Mandatory)] [string] $Label
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Executable
    foreach ($argument in $Arguments) {
        [void] $startInfo.ArgumentList.Add($argument)
    }
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $process.add_ErrorDataReceived({
            param($sender, $eventArgs)
            if ($null -ne $eventArgs -and $null -ne $eventArgs.Data) {
                [Console]::Error.WriteLine("[$Label] $($eventArgs.Data)")
            }
        })
    [void] $process.Start()
    $process.BeginErrorReadLine()
    $process
}

function Stop-JsonLineProcess {
    param([System.Diagnostics.Process] $Process)

    if ($null -ne $Process -and -not $Process.HasExited) {
        try {
            $Process.StandardInput.Close()
        }
        catch {
            [Console]::Error.WriteLine($_.Exception.Message)
        }
        finally {
            if (-not $Process.WaitForExit(5000)) {
                $Process.Kill($true)
            }
            $Process.Dispose()
        }
    }
}

function Send-JsonLine {
    param(
        [Parameter(Mandatory)] [System.Diagnostics.Process] $Process,
        [Parameter(Mandatory)] [object] $Message
    )

    $json = $Message | ConvertTo-Json -Compress -Depth 100
    $Process.StandardInput.WriteLine($json)
    $Process.StandardInput.Flush()
}

function Read-JsonLine {
    param(
        [Parameter(Mandatory)] [System.Diagnostics.Process] $Process,
        [int] $ExpectedId = -1
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $matched = $null
    while ($null -eq $matched -and [DateTimeOffset]::UtcNow -lt $deadline) {
        if ($Process.HasExited) {
            throw "The child process exited with code $($Process.ExitCode)."
        }

        $readTask = $Process.StandardOutput.ReadLineAsync()
        $remaining = $deadline - [DateTimeOffset]::UtcNow
        if ($remaining -le [TimeSpan]::Zero -or -not $readTask.Wait($remaining)) {
            break
        }

        $line = $readTask.Result
        if ($null -eq $line -or [string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        try {
            $candidate = $line | ConvertFrom-Json -Depth 100
        }
        catch {
            continue
        }

        if ($null -eq $candidate) {
            continue
        }

        if ($ExpectedId -lt 0) {
            $matched = $candidate
        }
        else {
            $idProperty = $candidate.PSObject.Properties['id']
            if ($null -ne $idProperty -and $candidate.id -eq $ExpectedId) {
                $matched = $candidate
            }
        }
    }

    if ($null -eq $matched) {
        throw "Timed out after $TimeoutSeconds second(s) waiting for a JSON response."
    }
    $matched
}

function Invoke-McpRequest {
    param(
        [Parameter(Mandatory)] [string] $Method,
        [hashtable] $Params = @{}
    )

    $id = ++$script:NextRequestId
    Send-JsonLine -Process $script:McpProcess -Message @{
        jsonrpc = '2.0'
        id = $id
        method = $Method
        params = $Params
    }
    $response = Read-JsonLine -Process $script:McpProcess -ExpectedId $id
    $errorProperty = $response.PSObject.Properties['error']
    if ($null -ne $errorProperty -and $null -ne $response.error) {
        $errorJson = $response.error | ConvertTo-Json -Compress -Depth 20
        throw "MCP request '$Method' failed: $errorJson"
    }
    $response.result
}

function Invoke-McpNotification {
    param(
        [Parameter(Mandatory)] [string] $Method,
        [hashtable] $Params = @{}
    )

    Send-JsonLine -Process $script:McpProcess -Message @{
        jsonrpc = '2.0'
        method = $Method
        params = $Params
    }
}

function Connect-McpHost {
    $script:McpProcess = Start-JsonLineProcess `
        -Executable $HostExecutable `
        -Arguments $HostArguments `
        -Label 'mcp-host'

    $null = Invoke-McpRequest -Method 'initialize' -Params @{
        protocolVersion = '2025-06-18'
        capabilities = @{}
        clientInfo = @{ name = 'live-test-network-phase3'; version = '1.0.0' }
    }
    Invoke-McpNotification -Method 'notifications/initialized'

    $tools = Invoke-McpRequest -Method 'tools/list'
    if ($null -eq $tools -or $null -eq $tools.tools) {
        throw 'The MCP host returned no tool catalogue.'
    }
    $toolNames = @($tools.tools | ForEach-Object { $_.name })
    if ($toolNames -notcontains 'network_read') {
        throw "The MCP host does not expose network_read."
    }
}

function Invoke-NetworkRead {
    param([Parameter(Mandatory)] [object[]] $Operations)

    $script:NetworkRequestCount++
    $toolResult = Invoke-McpRequest -Method 'tools/call' -Params @{
        name = 'network_read'
        arguments = @{ operations = $Operations }
    }
    if ($null -eq $toolResult) {
        throw 'network_read returned no result.'
    }
    if ($toolResult.isError) {
        throw "network_read returned isError:true: $($toolResult.content[0].text)"
    }

    $content = @($toolResult.content)
    if ($content.Count -eq 0 -or $null -eq $content[0].text) {
        throw 'network_read returned no canonical text block.'
    }
    $canonicalText = [string] $content[0].text
    $envelope = $canonicalText | ConvertFrom-Json -Depth 100
    if ($null -eq $envelope) {
        throw 'network_read returned a null canonical document.'
    }
    [pscustomobject]@{
        Envelope = $envelope
        CanonicalText = $canonicalText
    }
}

function Invoke-DiscoveryPage {
    param([string] $Cursor)

    $operation = @{
        operationId = 'discovery'
        operation = 'list_network_objects'
        objectKinds = $script:ObjectKinds
        pageSize = $PageSize
    }
    if (-not [string]::IsNullOrWhiteSpace($Cursor)) {
        $operation.cursor = $Cursor
    }

    $call = Invoke-NetworkRead -Operations @($operation)
    if ($null -eq $call.Envelope.batch -or $null -eq $call.Envelope.batch.operations) {
        throw 'Network discovery returned no batch operations.'
    }
    $operationItems = @($call.Envelope.batch.operations)
    if ($operationItems.Count -eq 0) {
        throw 'Network discovery returned an empty batch.'
    }
    $item = $operationItems[0]
    if ($null -eq $item) {
        throw 'Network discovery returned no operation item.'
    }
    $resultValue = $null
    if ($item.status -eq 'omitted') {
        if ($null -eq $item.omission) {
            throw 'Network discovery omitted its result without omission evidence.'
        }
    }
    elseif ($item.status -ne 'succeeded') {
        $failureJson = $item.failure | ConvertTo-Json -Compress -Depth 20
        throw "Network discovery failed: $failureJson"
    }
    else {
        if ($null -eq $item.result) {
            throw 'Network discovery returned no result.'
        }
        $resultValue = $item.result
    }
    [pscustomobject]@{
        Result = $resultValue
        CanonicalText = $call.CanonicalText
        Omission = $item.omission
        Truncation = $call.Envelope.batch.truncation
    }
}

function Get-CompleteDiscovery {
    $pages = @()
    $items = @()
    $cursor = $null
    $pageNumber = 0
    do {
        $pageNumber++
        if ($pageNumber -gt 1000) {
            throw 'Network discovery exceeded 1000 pages.'
        }
        $page = Invoke-DiscoveryPage -Cursor $cursor
        $pages += $page
        if ($null -eq $page.Result) {
            $cursor = $null
        }
        else {
            if ($null -ne $page.Result.items) {
                $items += @($page.Result.items)
            }
            $cursor = $page.Result.nextCursor
        }
    } while (-not [string]::IsNullOrWhiteSpace($cursor))

    [pscustomobject]@{
        Pages = $pages
        Items = $items
    }
}

function Invoke-Inspection {
    param(
        [Parameter(Mandatory)] [object] $Target,
        [string[]] $AttributeNames
    )

    $operation = @{
        operationId = 'inspection'
        operation = 'inspect_network_object'
        target = $Target
    }
    if ($null -ne $AttributeNames -and $AttributeNames.Count -gt 0) {
        $operation.attributeNames = $AttributeNames
    }
    $call = Invoke-NetworkRead -Operations @($operation)
    if ($null -eq $call.Envelope.batch -or $null -eq $call.Envelope.batch.operations) {
        throw 'Network inspection returned no batch operations.'
    }
    $operationItems = @($call.Envelope.batch.operations)
    if ($operationItems.Count -eq 0) {
        throw 'Network inspection returned an empty batch.'
    }
    $item = $operationItems[0]
    if ($null -eq $item) {
        throw 'Network inspection returned no operation item.'
    }
    if ($item.status -ne 'succeeded') {
        $failureJson = $item.failure | ConvertTo-Json -Compress -Depth 20
        throw "Network inspection failed: $failureJson"
    }
    if ($null -eq $item.result -or $null -eq $item.result.evidence) {
        throw 'Network inspection returned no result evidence.'
    }
    [pscustomobject]@{
        Result = $item.result
        CanonicalText = $call.CanonicalText
        Omission = $item.omission
        Truncation = $call.Envelope.batch.truncation
    }
}

function Get-FirstSelectableTarget {
    param([Parameter(Mandatory)] [object[]] $Items)

    $target = $null
    foreach ($item in $Items) {
        if ($null -eq $item) {
            continue
        }
        if ($item.selectable -and $null -ne $item.selector) {
            $target = $item.selector
            break
        }
    }
    if ($null -eq $target) {
        throw 'Discovery returned no complete selector.'
    }
    $target
}

function Invoke-Matrix {
    $discovery = Get-CompleteDiscovery
    $observed = [ordered]@{
        nestedDeviceItem = $null
        networkInterface = $null
        ethernetNode = $null
        ethernetSubnet = $null
        profinetIoSystem = $null
        communicationConnection = $null
    }
    $coverageGaps = @()
    $networkTypes = @()
    $connectionTypes = @()

    foreach ($item in $discovery.Items) {
        if ($null -eq $item) {
            continue
        }
        if (-not $item.selectable -or $null -eq $item.selector) {
            if ($item.kind -eq 'communicationConnection') {
                $coverageGaps += 'communicationConnectionSelectorUnavailable'
            }
            continue
        }

        $inspection = $null
        switch ($item.kind) {
            'deviceItem' {
                $itemPath = @($item.selector.itemPath)
                if ($null -eq $observed.nestedDeviceItem -and $itemPath.Count -gt 1) {
                    $inspection = Invoke-Inspection -Target $item.selector
                    $observed.nestedDeviceItem = $inspection.Result
                }
            }
            'networkInterface' {
                if ($null -eq $observed.networkInterface) {
                    $inspection = Invoke-Inspection -Target $item.selector
                    $observed.networkInterface = $inspection.Result
                }
            }
            'node' {
                $inspection = Invoke-Inspection -Target $item.selector
                if ($null -ne $inspection.Result.evidence.nodeType) {
                    $networkTypes += [string] $inspection.Result.evidence.nodeType
                }
                if ($null -eq $observed.ethernetNode -and $inspection.Result.evidence.nodeType -eq 'Ethernet') {
                    $observed.ethernetNode = $inspection.Result
                }
            }
            'subnet' {
                $inspection = Invoke-Inspection -Target $item.selector
                if ($null -ne $inspection.Result.evidence.networkType) {
                    $networkTypes += [string] $inspection.Result.evidence.networkType
                }
                if ($null -eq $observed.ethernetSubnet -and $inspection.Result.evidence.networkType -eq 'Ethernet') {
                    $observed.ethernetSubnet = $inspection.Result
                }
            }
            'ioSystem' {
                $inspection = Invoke-Inspection -Target $item.selector
                if ($null -ne $inspection.Result.evidence.networkType) {
                    $networkTypes += [string] $inspection.Result.evidence.networkType
                }
                if ($null -eq $observed.profinetIoSystem `
                    -and $inspection.Result.evidence.networkType -eq 'Ethernet') {
                    $observed.profinetIoSystem = $inspection.Result
                }
            }
            'communicationConnection' {
                $inspection = Invoke-Inspection -Target $item.selector
                if ($null -ne $inspection.Result.target -and $null -ne $inspection.Result.target.connectionType) {
                    $connectionTypes += [string] $inspection.Result.target.connectionType
                }
                if ($null -eq $observed.communicationConnection) {
                    $observed.communicationConnection = $inspection.Result
                }
            }
        }
    }

    $requiredGaps = @()
    foreach ($entry in $observed.GetEnumerator()) {
        if ($null -eq $entry.Value) {
            $gap = "$($entry.Key)NotObserved"
            $requiredGaps += $gap
            $coverageGaps += $gap
        }
    }
    if ($networkTypes -notcontains 'Profibus') {
        $coverageGaps += 'profibusOrDpNotObserved'
    }
    if (@($connectionTypes | Select-Object -Unique).Count -lt 2) {
        $coverageGaps += 'additionalConnectionClassesNotObserved'
    }

    [ordered]@{
        mode = 'Matrix'
        matrixComplete = ($requiredGaps.Count -eq 0)
        observed = $observed
        coverageGaps = @($coverageGaps | Select-Object -Unique)
        discoveryPageCount = $discovery.Pages.Count
        requestCount = $script:NetworkRequestCount
    }
}

function Invoke-Repeatability {
    $firstDiscovery = Invoke-DiscoveryPage -Cursor $null
    $secondDiscovery = Invoke-DiscoveryPage -Cursor $null
    if ($null -eq $firstDiscovery.Result -or $null -eq $secondDiscovery.Result) {
        throw 'Repeatability requires complete discovery results.'
    }
    $target = Get-FirstSelectableTarget -Items @($firstDiscovery.Result.items)
    $firstInspection = Invoke-Inspection -Target $target
    $secondInspection = Invoke-Inspection -Target $target

    $discoveryBytesEqual = [System.Linq.Enumerable]::SequenceEqual(
        [Text.Encoding]::UTF8.GetBytes($firstDiscovery.CanonicalText),
        [Text.Encoding]::UTF8.GetBytes($secondDiscovery.CanonicalText))
    $inspectionBytesEqual = [System.Linq.Enumerable]::SequenceEqual(
        [Text.Encoding]::UTF8.GetBytes($firstInspection.CanonicalText),
        [Text.Encoding]::UTF8.GetBytes($secondInspection.CanonicalText))

    [ordered]@{
        mode = 'Repeatability'
        canonicalBytesEqual = ($discoveryBytesEqual -and $inspectionBytesEqual)
        discoveryCanonicalBytesEqual = $discoveryBytesEqual
        inspectionCanonicalBytesEqual = $inspectionBytesEqual
        requestCount = $script:NetworkRequestCount
    }
}

function Invoke-MeasureListValue {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $discovery = Get-CompleteDiscovery
    $allText = ($discovery.Pages | ForEach-Object { $_.CanonicalText }) -join ''
    $selectable = @($discovery.Items | Where-Object { $_.selectable })
    $selectors = @($selectable | Where-Object { $null -ne $_.selector })
    $connections = @($discovery.Items | Where-Object { $_.kind -eq 'communicationConnection' })
    $usableConnections = @($connections | Where-Object { $_.selectable -and $null -ne $_.selector })
    $inspection = $null
    if ($discovery.Items.Count -gt 0) {
        $target = Get-FirstSelectableTarget -Items $discovery.Items
        $inspection = Invoke-Inspection -Target $target
    }
    $timer.Stop()
    $omissions = @($discovery.Pages | Where-Object { $null -ne $_.Omission }).Count
    if ($null -ne $inspection -and $null -ne $inspection.Omission) {
        $omissions++
    }
    $truncation = @($discovery.Pages | Where-Object { $null -ne $_.Truncation }).Count
    if ($null -ne $inspection -and $null -ne $inspection.Truncation) {
        $truncation++
    }
    $inspectionText = if ($null -eq $inspection) { '' } else { $inspection.CanonicalText }

    [ordered]@{
        mode = 'MeasureListValue'
        canonicalByteCount = [Text.Encoding]::UTF8.GetByteCount($allText + $inspectionText)
        elapsedMilliseconds = $timer.ElapsedMilliseconds
        selectorCount = $selectors.Count
        selectorsComplete = ($selectors.Count -eq $selectable.Count)
        omissions = $omissions
        truncation = $truncation
        requestCount = $script:NetworkRequestCount
        discoveryThenInspectionRequestCount = $script:NetworkRequestCount
        connectionDiscoveryUsable = ($usableConnections.Count -gt 0)
        discoveryItemCount = $discovery.Items.Count
        discoveryPageCount = $discovery.Pages.Count
    }
}

function Invoke-RawProbe {
    $discovery = Get-CompleteDiscovery
    $target = Get-FirstSelectableTarget -Items $discovery.Items
    if (-not (Test-Path -LiteralPath $WorkerExecutable)) {
        throw "The worker executable was not found at '$WorkerExecutable'."
    }

    $script:WorkerProcess = Start-JsonLineProcess `
        -Executable $WorkerExecutable `
        -Arguments @('--access-mode', 'read-only') `
        -Label 'raw-probe-worker'
    Send-JsonLine -Process $script:WorkerProcess -Message @{
        method = 'probe_network_object_attributes'
        networkObjectTarget = $target
    }
    $response = Read-JsonLine -Process $script:WorkerProcess
    if ($null -eq $response -or -not $response.success) {
        $failure = $response | ConvertTo-Json -Compress -Depth 20
        throw "RawProbe failed: $failure"
    }
    $payloadText = $response.payload
    if ($null -eq $payloadText) {
        throw 'RawProbe returned no payload.'
    }
    if ([string]::IsNullOrWhiteSpace($payloadText)) {
        throw 'RawProbe returned no payload.'
    }
    $payload = $payloadText | ConvertFrom-Json -Depth 100
    if ($null -eq $payload) {
        throw 'RawProbe returned a null payload.'
    }
    [ordered]@{
        mode = 'RawProbe'
        target = $target
        probe = $payload
        requestCount = $script:NetworkRequestCount
    }
}

$evidence = $null
try {
    Connect-McpHost
    $evidence = switch ($Mode) {
        'Matrix' { Invoke-Matrix }
        'Repeatability' { Invoke-Repeatability }
        'MeasureListValue' { Invoke-MeasureListValue }
        'RawProbe' { Invoke-RawProbe }
    }
}
finally {
    Stop-JsonLineProcess -Process $script:WorkerProcess
    Stop-JsonLineProcess -Process $script:McpProcess
}

if ($null -eq $evidence) {
    throw 'The selected mode produced no evidence.'
}

$artifactRoot = Join-Path $script:RepositoryRoot 'artifacts'
$artifactRoot = Join-Path $artifactRoot 'live-network-phase3'
[void] (New-Item -ItemType Directory -Force -Path $artifactRoot)
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmssfff'
$artifactName = "$timestamp-$($Mode.ToLowerInvariant()).json"
$artifactPath = Join-Path $artifactRoot $artifactName
$evidence | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $artifactPath -Encoding utf8NoBOM
[Console]::Out.WriteLine($artifactPath)
if ($Mode -eq 'Matrix' -and -not $evidence.matrixComplete) {
    throw 'Matrix did not observe every required object category. Review the recorded coverageGaps.'
}
