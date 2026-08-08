#Requires -Version 7
<#
.SYNOPSIS
    Read-only TIA Portal V21 metadata probe for Network Phase 4 subnet lifecycle design.

.DESCRIPTION
    Uses the existing network_read tool to discover and inspect subnet selectors, then uses the
    internal read-only worker route probe_network_object_attributes to record raw Openness
    attribute metadata. The script never calls network_write and never changes the project.

    Live mode requires an explicit absolute .ap21 project path. Results are written as timestamped
    JSON under artifacts/live-network-phase4. Ordinary tests use -Describe and never start TIA.
#>
[CmdletBinding()]
param(
    [switch] $Describe,
    [string] $ProjectPath,
    [string] $HostExecutable = 'dotnet',
    [string[]] $HostArguments,
    [string] $WorkerExecutable,
    [int] $TimeoutSeconds = 240,
    [ValidateRange(1, 200)]
    [int] $PageSize = 50
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RepositoryRoot = Split-Path -Parent $PSScriptRoot
$script:McpProcess = $null
$script:WorkerProcess = $null
$script:NextRequestId = 0
$script:NetworkRequestCount = 0
$script:WorkerRequestCount = 0
$script:HardwareConfigSource = $null
$script:HardwareConfigOmission = $null
$script:SupportedSubnetTypes = @('Ethernet', 'Profibus')

if ($Describe) {
    [ordered]@{
        schemaVersion = 'network-phase4-subnet-metadata-probe/v1'
        readOnly = $true
        mutatesProject = $false
        requiresProjectPath = $true
        subnetTypes = $script:SupportedSubnetTypes
        publicReadOperations = @(
            'read_hardware_config',
            'list_network_objects',
            'inspect_network_object'
        )
        internalWorkerOperations = @(
            'read_hardware_config'
            'probe_network_object_attributes'
        )
        evidenceDirectory = 'artifacts/live-network-phase4'
    } | ConvertTo-Json -Compress -Depth 20
    exit 0
}

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    throw 'ProjectPath is required for a live subnet metadata probe.'
}
if (-not [IO.Path]::IsPathFullyQualified($ProjectPath)) {
    throw 'ProjectPath must be an absolute path.'
}
if (-not [string]::Equals(
        [IO.Path]::GetExtension($ProjectPath),
        '.ap21',
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'ProjectPath must identify a TIA Portal V21 .ap21 project.'
}
if (-not (Test-Path -LiteralPath $ProjectPath -PathType Leaf)) {
    throw "The project file was not found at '$ProjectPath'."
}
$resolvedProject = Resolve-Path -LiteralPath $ProjectPath -ErrorAction Stop
$ProjectPath = $resolvedProject.ProviderPath

if ($null -eq $HostArguments -or $HostArguments.Count -eq 0) {
    $hostDll = Join-Path $script:RepositoryRoot 'TiaMcpServer'
    $hostDll = Join-Path $hostDll 'bin'
    $hostDll = Join-Path $hostDll 'Debug'
    $hostDll = Join-Path $hostDll 'net8.0'
    $hostDll = Join-Path $hostDll 'TiaMcpServer.dll'
    $HostArguments = @(
        $hostDll,
        '--access-mode',
        'read-only'
    )
}

$accessModeIndex = [Array]::IndexOf($HostArguments, '--access-mode')
if ($accessModeIndex -lt 0 `
    -or $accessModeIndex + 1 -ge $HostArguments.Count `
    -or -not [string]::Equals(
        $HostArguments[$accessModeIndex + 1],
        'read-only',
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'HostArguments must launch the MCP host with --access-mode read-only.'
}

if ([string]::IsNullOrWhiteSpace($WorkerExecutable)) {
    $hostBin = Join-Path $script:RepositoryRoot 'TiaMcpServer'
    $hostBin = Join-Path $hostBin 'bin'
    $hostBin = Join-Path $hostBin 'Debug'
    $hostBin = Join-Path $hostBin 'net8.0'
    $workerBin = Join-Path $hostBin 'openness-worker'
    $WorkerExecutable = Join-Path $workerBin 'TiaMcpServer.OpennessWorker.exe'
}
if (-not (Test-Path -LiteralPath $WorkerExecutable -PathType Leaf)) {
    throw "The worker executable was not found at '$WorkerExecutable'."
}

function Start-JsonLineProcess {
    param(
        [Parameter(Mandatory)] [string] $Executable,
        [Parameter(Mandatory)] [string[]] $Arguments,
        [Parameter(Mandatory)] [string] $Label
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Executable
    foreach ($argument in $Arguments) {
        [void] $startInfo.ArgumentList.Add($argument)
    }
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $false
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    [void] $process.Start()
    $process
}

function Stop-JsonLineProcess {
    param([Diagnostics.Process] $Process)

    if ($null -eq $Process) {
        return
    }
    try {
        if (-not $Process.HasExited) {
            $Process.StandardInput.Close()
            if (-not $Process.WaitForExit(5000)) {
                $Process.Kill($true)
            }
        }
    }
    catch {
        [Console]::Error.WriteLine($_.Exception.Message)
    }
    finally {
        $Process.Dispose()
    }
}

function Send-JsonLine {
    param(
        [Parameter(Mandatory)] [Diagnostics.Process] $Process,
        [Parameter(Mandatory)] [object] $Message
    )

    $json = $Message | ConvertTo-Json -Compress -Depth 100
    $Process.StandardInput.WriteLine($json)
    $Process.StandardInput.Flush()
}

function Read-JsonLine {
    param(
        [Parameter(Mandatory)] [Diagnostics.Process] $Process,
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
        -Label 'phase4-subnet-probe-host'

    $null = Invoke-McpRequest -Method 'initialize' -Params @{
        protocolVersion = '2025-06-18'
        capabilities = @{}
        clientInfo = @{ name = 'live-probe-network-phase4-subnet-metadata'; version = '1.0.0' }
    }
    Invoke-McpNotification -Method 'notifications/initialized'

    $tools = Invoke-McpRequest -Method 'tools/list'
    if ($null -eq $tools -or $null -eq $tools.tools) {
        throw 'The MCP host returned no tool catalogue.'
    }
    $toolNames = @($tools.tools | ForEach-Object { $_.name })
    if ($toolNames -notcontains 'network_read') {
        throw 'The read-only MCP host does not expose network_read.'
    }
    if ($toolNames -contains 'network_write') {
        throw 'The probe host unexpectedly exposes network_write in read-only mode.'
    }
}

function Connect-Worker {
    if ($null -ne $script:WorkerProcess -and -not $script:WorkerProcess.HasExited) {
        return
    }

    $script:WorkerProcess = Start-JsonLineProcess `
        -Executable $WorkerExecutable `
        -Arguments @('--access-mode', 'read-only') `
        -Label 'phase4-subnet-probe-worker'
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
    $envelope
}

function Get-SingleOperationItem {
    param(
        [Parameter(Mandatory)] [object] $Envelope,
        [Parameter(Mandatory)] [string] $Description
    )

    if ($null -eq $Envelope.batch -or $null -eq $Envelope.batch.operations) {
        throw "$Description returned no batch operations."
    }
    $items = @($Envelope.batch.operations)
    if ($items.Count -ne 1 -or $null -eq $items[0]) {
        throw "$Description did not return exactly one operation item."
    }
    $items[0]
}

function Get-SucceededOperationResult {
    param(
        [Parameter(Mandatory)] [object] $Envelope,
        [Parameter(Mandatory)] [string] $Description
    )

    $item = Get-SingleOperationItem -Envelope $Envelope -Description $Description
    if ($item.status -ne 'succeeded') {
        $failureJson = $item.failure | ConvertTo-Json -Compress -Depth 20
        throw "$Description failed: $failureJson"
    }
    if ($null -eq $item.result) {
        throw "$Description returned no result."
    }
    $item.result
}

function Invoke-WorkerHardwareConfig {
    Connect-Worker
    Send-JsonLine -Process $script:WorkerProcess -Message @{
        method = 'read_hardware_config'
        projectPath = $ProjectPath
    }
    $script:WorkerRequestCount++
    $response = Read-JsonLine -Process $script:WorkerProcess
    if ($null -eq $response -or -not $response.success) {
        $failureJson = $response | ConvertTo-Json -Compress -Depth 20
        throw "Worker hardware configuration read failed: $failureJson"
    }
    if ([string]::IsNullOrWhiteSpace($response.payload)) {
        throw 'Worker hardware configuration read returned no payload.'
    }
    $payload = $response.payload | ConvertFrom-Json -Depth 100
    if ($null -eq $payload) {
        throw 'Worker hardware configuration read returned a null payload.'
    }
    $payload
}

function Invoke-HardwareConfig {
    $envelope = Invoke-NetworkRead -Operations @(@{
            operationId = 'phase4-hardware'
            operation = 'read_hardware_config'
            projectPath = $ProjectPath
        })
    $item = Get-SingleOperationItem `
        -Envelope $envelope `
        -Description 'Hardware configuration'
    if ($item.status -eq 'omitted') {
        $script:HardwareConfigSource = 'workerFallback'
        $script:HardwareConfigOmission = $item.omission
        return Invoke-WorkerHardwareConfig
    }

    $script:HardwareConfigSource = 'networkRead'
    Get-SucceededOperationResult `
        -Envelope $envelope `
        -Description 'Hardware configuration'
}

function Invoke-SubnetDiscoveryPage {
    param([string] $Cursor)

    $operation = @{
        operationId = 'phase4-subnets'
        operation = 'list_network_objects'
        projectPath = $ProjectPath
        objectKinds = @('subnet')
        pageSize = $PageSize
    }
    if (-not [string]::IsNullOrWhiteSpace($Cursor)) {
        $operation.cursor = $Cursor
    }
    $envelope = Invoke-NetworkRead -Operations @($operation)
    Get-SucceededOperationResult -Envelope $envelope -Description 'Subnet discovery'
}

function Get-CompleteSubnetDiscovery {
    $items = @()
    $cursor = $null
    $pageCount = 0
    do {
        $pageCount++
        if ($pageCount -gt 1000) {
            throw 'Subnet discovery exceeded 1000 pages.'
        }
        $page = Invoke-SubnetDiscoveryPage -Cursor $cursor
        if ($null -ne $page.items) {
            $items += @($page.items)
        }
        $cursor = $page.nextCursor
    } while (-not [string]::IsNullOrWhiteSpace($cursor))

    [pscustomobject]@{
        Items = $items
        PageCount = $pageCount
    }
}

function Invoke-SubnetInspection {
    param([Parameter(Mandatory)] [object] $Target)

    $envelope = Invoke-NetworkRead -Operations @(@{
            operationId = 'phase4-subnet-inspection'
            operation = 'inspect_network_object'
            projectPath = $ProjectPath
            target = $Target
        })
    Get-SucceededOperationResult -Envelope $envelope -Description 'Subnet inspection'
}

function Invoke-RawAttributeProbe {
    param([Parameter(Mandatory)] [object] $Target)

    Send-JsonLine -Process $script:WorkerProcess -Message @{
        method = 'probe_network_object_attributes'
        projectPath = $ProjectPath
        networkObjectTarget = $Target
    }
    $script:WorkerRequestCount++
    $response = Read-JsonLine -Process $script:WorkerProcess
    if ($null -eq $response -or -not $response.success) {
        $failureJson = $response | ConvertTo-Json -Compress -Depth 20
        throw "Raw subnet attribute probe failed: $failureJson"
    }
    if ([string]::IsNullOrWhiteSpace($response.payload)) {
        throw 'Raw subnet attribute probe returned no payload.'
    }
    $payload = $response.payload | ConvertFrom-Json -Depth 100
    if ($null -eq $payload) {
        throw 'Raw subnet attribute probe returned a null payload.'
    }
    $payload
}

function Find-HardwareSubnet {
    param(
        [object] $HardwareConfig,
        [Parameter(Mandatory)] [string] $SubnetId
    )

    if ($null -eq $HardwareConfig -or $null -eq $HardwareConfig.subnets) {
        return $null
    }
    $matches = @($HardwareConfig.subnets | Where-Object {
            [string]::Equals($_.subnetId, $SubnetId, [StringComparison]::Ordinal)
        })
    if ($matches.Count -eq 1) {
        return $matches[0]
    }
    $null
}

$evidence = [ordered]@{
    schemaVersion = 'network-phase4-subnet-metadata-probe/v1'
    observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    projectPath = $ProjectPath
    readOnly = $true
    supportedSubnetTypes = $script:SupportedSubnetTypes
    discoveryPageCount = 0
    discoveredSubnetCount = 0
    probedSubnetCount = 0
    missingSubnetTypes = @()
    nonSelectableSubnets = @()
    unsupportedSubnetTypes = @()
    subnets = @()
    hardwareConfigAvailable = $false
    hardwareConfigSource = $null
    hardwareConfigOmission = $null
    networkRequestCount = 0
    workerRequestCount = 0
    failure = $null
}
$probeFailure = $null
try {
    Connect-McpHost
    $hardwareConfig = Invoke-HardwareConfig
    $evidence.hardwareConfigAvailable = ($null -ne $hardwareConfig)

    $discovery = Get-CompleteSubnetDiscovery
    $evidence.discoveryPageCount = $discovery.PageCount
    $evidence.discoveredSubnetCount = $discovery.Items.Count

    $candidates = @()
    foreach ($summary in $discovery.Items) {
        if ($null -eq $summary -or -not $summary.selectable -or $null -eq $summary.selector) {
            $evidence.nonSelectableSubnets += $summary
            continue
        }

        $inspection = Invoke-SubnetInspection -Target $summary.selector
        $networkType = [string] $inspection.evidence.networkType
        $entry = [pscustomobject]@{
            Summary = $summary
            Inspection = $inspection
            NetworkType = $networkType
        }
        if ($script:SupportedSubnetTypes -contains $networkType) {
            $candidates += $entry
        }
        else {
            $evidence.unsupportedSubnetTypes += [ordered]@{
                target = $summary.selector
                networkType = $networkType
                evidence = $inspection.evidence
            }
        }
    }

    if ($candidates.Count -gt 0) {
        Connect-Worker
    }
    foreach ($candidate in $candidates) {
        $target = $candidate.Summary.selector
        $rawProbe = Invoke-RawAttributeProbe -Target $target
        $hardwareSubnet = Find-HardwareSubnet `
            -HardwareConfig $hardwareConfig `
            -SubnetId ([string] $target.subnetId)
        $evidence.subnets += [ordered]@{
            target = $target
            networkType = $candidate.NetworkType
            inspection = $candidate.Inspection
            rawAttributeMetadata = $rawProbe
            relationships = $hardwareSubnet
        }
    }

    $evidence.probedSubnetCount = $evidence.subnets.Count
    $observedTypes = @($evidence.subnets | ForEach-Object { $_.networkType } | Select-Object -Unique)
    $evidence.missingSubnetTypes = @($script:SupportedSubnetTypes | Where-Object {
            $observedTypes -notcontains $_
        })
}
catch {
    $probeFailure = $_.Exception
    $evidence.failure = [ordered]@{
        category = $probeFailure.GetType().FullName
        message = $probeFailure.Message
    }
}
finally {
    Stop-JsonLineProcess -Process $script:WorkerProcess
    Stop-JsonLineProcess -Process $script:McpProcess
}

$evidence.networkRequestCount = $script:NetworkRequestCount
$evidence.workerRequestCount = $script:WorkerRequestCount
$evidence.hardwareConfigSource = $script:HardwareConfigSource
$evidence.hardwareConfigOmission = $script:HardwareConfigOmission
$artifactRoot = Join-Path $script:RepositoryRoot 'artifacts'
$artifactRoot = Join-Path $artifactRoot 'live-network-phase4'
[void] (New-Item -ItemType Directory -Force -Path $artifactRoot)
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmssfff'
$artifactPath = Join-Path $artifactRoot "$timestamp-subnet-metadata.json"
$evidence | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $artifactPath -Encoding utf8NoBOM
[Console]::Out.WriteLine($artifactPath)

if ($null -ne $probeFailure) {
    throw "Subnet metadata probe failed. Review '$artifactPath'. Error: $($probeFailure.Message)"
}
