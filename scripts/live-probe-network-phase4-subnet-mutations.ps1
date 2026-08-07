#Requires -Version 7
<#
.SYNOPSIS
    Separately authorized live TIA Portal V21 subnet lifecycle mutation probe for Network Phase 4.

.DESCRIPTION
    Inventory mode is read-only and records the complete hardware configuration. Apply mode starts
    the Openness worker in read-write mode and runs controlled subnet lifecycle experiments against
    an explicitly supplied disposable .ap21 project.

    Apply creates and edits isolated Ethernet and PROFIBUS subnets, exercises rollback and invalid
    inputs, deletes the created empty subnets, and then attempts to delete the two explicitly
    selected existing connected subnets. It post-reads the project and records whether every root
    device identity observed before the deletion still exists afterward.

    Apply is destructive. It requires -AllowMutation plus the exact -Acknowledgement value shown
    by -Describe. The script never chooses a connected subnet implicitly. Ordinary tests use only
    -Describe, invalid preflight calls, or isolated function loading; CI never runs live mode.

.PARAMETER Mode
    Inventory is read-only and is the default. Apply performs the destructive mutation probe.

.PARAMETER ProjectPath
    Absolute path to a disposable TIA Portal V21 .ap21 project copy.

.PARAMETER ConnectedEthernetSubnetId
    Exact SubnetId of an existing connected Ethernet subnet to delete in Apply mode.

.PARAMETER ConnectedProfibusSubnetId
    Exact SubnetId of an existing connected PROFIBUS subnet to delete in Apply mode.

.PARAMETER AllowMutation
    Required for Apply mode. Without it, the script stops before inspecting the project path.

.PARAMETER Acknowledgement
    Apply requires the exact value DELETE-CONNECTED-SUBNETS-IN-DISPOSABLE-PROJECT.

.EXAMPLE
    pwsh -File scripts/live-probe-network-phase4-subnet-mutations.ps1 `
        -Mode Inventory `
        -ProjectPath C:\Sandbox\NetworkPhase4Disposable.ap21

.EXAMPLE
    pwsh -File scripts/live-probe-network-phase4-subnet-mutations.ps1 `
        -Mode Apply `
        -ProjectPath C:\Sandbox\NetworkPhase4Disposable.ap21 `
        -ConnectedEthernetSubnetId 590-2 `
        -ConnectedProfibusSubnetId 590-3 `
        -AllowMutation `
        -Acknowledgement DELETE-CONNECTED-SUBNETS-IN-DISPOSABLE-PROJECT

.NOTES
    Never point Apply mode at the source project. Deleting a connected subnet removes the subnet
    and its network relationships from the disposable project; the probe measures whether TIA
    leaves all devices in place.
#>
[CmdletBinding()]
param(
    [switch] $Describe,
    [ValidateSet('Inventory', 'Apply')]
    [string] $Mode = 'Inventory',
    [string] $ProjectPath,
    [string] $ConnectedEthernetSubnetId,
    [string] $ConnectedProfibusSubnetId,
    [switch] $AllowMutation,
    [string] $Acknowledgement,
    [ValidateRange(0, 126)]
    [int] $ProfibusHighestAddress = 125,
    [string] $ProfibusTransmissionSpeed = 'Baud1500000',
    [string] $WorkerExecutable,
    [ValidateRange(30, 3600)]
    [int] $TimeoutSeconds = 600
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RepositoryRoot = Split-Path -Parent $PSScriptRoot
$script:WorkerProcess = $null
$script:WorkerRequestCount = 0
$script:RequiredAcknowledgement = 'DELETE-CONNECTED-SUBNETS-IN-DISPOSABLE-PROJECT'

if ($Describe) {
    [ordered]@{
        schemaVersion = 'network-phase4-subnet-mutation-probe/v1'
        defaultMode = 'Inventory'
        modes = @('Inventory', 'Apply')
        applyRequiresAllowMutation = $true
        requiredAcknowledgement = $script:RequiredAcknowledgement
        requiresExplicitConnectedSubnetIds = $true
        internalWorkerOperations = @(
            'read_hardware_config'
            'probe_subnet_lifecycle_mutations'
        )
        evidenceDirectory = 'artifacts/live-network-phase4'
    } | ConvertTo-Json -Compress -Depth 20
    exit 0
}

if ($Mode -eq 'Apply') {
    if (-not $AllowMutation) {
        throw "Mode 'Apply' requires -AllowMutation before any project is inspected."
    }
    if (-not [string]::Equals(
            $Acknowledgement,
            $script:RequiredAcknowledgement,
            [StringComparison]::Ordinal)) {
        throw "Mode 'Apply' requires -Acknowledgement $($script:RequiredAcknowledgement)."
    }
    if ([string]::IsNullOrWhiteSpace($ConnectedEthernetSubnetId)) {
        throw "Mode 'Apply' requires -ConnectedEthernetSubnetId."
    }
    if ([string]::IsNullOrWhiteSpace($ConnectedProfibusSubnetId)) {
        throw "Mode 'Apply' requires -ConnectedProfibusSubnetId."
    }
    if ([string]::Equals(
            $ConnectedEthernetSubnetId,
            $ConnectedProfibusSubnetId,
            [StringComparison]::Ordinal)) {
        throw 'The connected Ethernet and PROFIBUS subnet IDs must differ.'
    }
}

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    throw 'ProjectPath is required for a live subnet lifecycle probe.'
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
        [Parameter(Mandatory)] [string[]] $Arguments
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
    param([Parameter(Mandatory)] [Diagnostics.Process] $Process)

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $matched = $null
    while ($null -eq $matched -and [DateTimeOffset]::UtcNow -lt $deadline) {
        if ($Process.HasExited) {
            throw "The worker process exited with code $($Process.ExitCode)."
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
            $matched = $line | ConvertFrom-Json -Depth 100
        }
        catch {
            $matched = $null
        }
    }

    if ($null -eq $matched) {
        throw "Timed out after $TimeoutSeconds second(s) waiting for the worker response."
    }
    $matched
}

function New-MutationWorkerRequest {
    param(
        [Parameter(Mandatory)] [string] $ResolvedProjectPath,
        [Parameter(Mandatory)] [string] $RunId,
        [Parameter(Mandatory)] [string] $EthernetSubnetId,
        [Parameter(Mandatory)] [string] $ProfibusSubnetId,
        [Parameter(Mandatory)] [int] $HighestAddress,
        [Parameter(Mandatory)] [string] $TransmissionSpeed
    )

    [ordered]@{
        method = 'probe_subnet_lifecycle_mutations'
        projectPath = $ResolvedProjectPath
        confirm = $true
        allowTiaConfirmations = $true
        probeRunId = $RunId
        probeConnectedEthernetSubnetId = $EthernetSubnetId
        probeConnectedProfibusSubnetId = $ProfibusSubnetId
        probeProfibusHighestAddress = $HighestAddress
        probeProfibusTransmissionSpeed = $TransmissionSpeed
    }
}

function Invoke-WorkerRequest {
    param([Parameter(Mandatory)] [object] $Request)

    Send-JsonLine -Process $script:WorkerProcess -Message $Request
    $script:WorkerRequestCount++
    $response = Read-JsonLine -Process $script:WorkerProcess
    if ($null -eq $response -or -not $response.success) {
        $failureJson = $response | ConvertTo-Json -Compress -Depth 100
        throw "Worker request failed: $failureJson"
    }
    if ([string]::IsNullOrWhiteSpace($response.payload)) {
        throw 'Worker request returned no payload.'
    }
    $payload = $response.payload | ConvertFrom-Json -Depth 100
    if ($null -eq $payload) {
        throw 'Worker request returned a null payload.'
    }
    $payload
}

$runId = Get-Date -Format 'HHmmssff'
$isApply = $Mode -eq 'Apply'
$evidence = [ordered]@{
    schemaVersion = 'network-phase4-subnet-mutation-probe/v1'
    observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    completedAtUtc = $null
    mode = $Mode
    projectPath = $ProjectPath
    readOnly = -not $isApply
    mutatesProject = $isApply
    explicitMutationEnabled = [bool] $AllowMutation
    acknowledgementAccepted = [string]::Equals(
        $Acknowledgement,
        $script:RequiredAcknowledgement,
        [StringComparison]::Ordinal)
    runId = $runId
    selectedConnectedSubnets = [ordered]@{
        ethernetSubnetId = $ConnectedEthernetSubnetId
        profibusSubnetId = $ConnectedProfibusSubnetId
    }
    workerAccessMode = if ($isApply) { 'read-write' } else { 'read-only' }
    workerRequestCount = 0
    result = $null
    failure = $null
}

$probeFailure = $null
try {
    $workerAccessMode = if ($isApply) { 'read-write' } else { 'read-only' }
    $script:WorkerProcess = Start-JsonLineProcess `
        -Executable $WorkerExecutable `
        -Arguments @('--access-mode', $workerAccessMode)

    if ($isApply) {
        [Console]::Error.WriteLine('[WARN] APPLY MODE: deleting explicitly selected connected subnets in the disposable project.')
        $request = New-MutationWorkerRequest `
            -ResolvedProjectPath $ProjectPath `
            -RunId $runId `
            -EthernetSubnetId $ConnectedEthernetSubnetId `
            -ProfibusSubnetId $ConnectedProfibusSubnetId `
            -HighestAddress $ProfibusHighestAddress `
            -TransmissionSpeed $ProfibusTransmissionSpeed
        $evidence.result = Invoke-WorkerRequest -Request $request
        if (@($evidence.result.preflightErrors).Count -gt 0) {
            throw "Mutation preflight failed: $(@($evidence.result.preflightErrors) -join '; ')"
        }
    }
    else {
        $evidence.result = Invoke-WorkerRequest -Request ([ordered]@{
                method = 'read_hardware_config'
                projectPath = $ProjectPath
            })
    }
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
}

$evidence.completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
$evidence.workerRequestCount = $script:WorkerRequestCount
$artifactRoot = Join-Path $script:RepositoryRoot 'artifacts'
$artifactRoot = Join-Path $artifactRoot 'live-network-phase4'
[void] (New-Item -ItemType Directory -Force -Path $artifactRoot)
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmssfff'
$artifactSuffix = if ($isApply) { 'subnet-mutations' } else { 'subnet-mutation-inventory' }
$artifactPath = Join-Path $artifactRoot "$timestamp-$artifactSuffix.json"
$evidence | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $artifactPath -Encoding utf8NoBOM
[Console]::Out.WriteLine($artifactPath)

if ($null -ne $probeFailure) {
    throw "Subnet lifecycle probe failed. Review '$artifactPath'. Error: $($probeFailure.Message)"
}
