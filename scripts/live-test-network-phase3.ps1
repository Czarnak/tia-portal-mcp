#Requires -Version 7
<#
.SYNOPSIS
    Separately authorized, read-only live acceptance harness for Network Phase 3.

.DESCRIPTION
    Matrix, Repeatability, and MeasureListValue use the normal MCP host in read-only access mode.
    RawProbe discovers the required observed fixture selectors through network_read, then sends
    them to an internal read-only worker diagnostic route. No mode changes a TIA project.

    This file is acceptance tooling. Ordinary tests inspect its source and never invoke it.
#>
[CmdletBinding()]
param(
    [ValidateSet('Matrix', 'Repeatability', 'MeasureListValue', 'RawProbe')]
    [string] $Mode = 'Matrix',

    [string] $HostExecutable = 'dotnet',
    [string[]] $HostArguments,
    [string] $WorkerExecutable,
    [int] $TimeoutSeconds = 240,
    [ValidateRange(1, 200)]
    [int] $PageSize = 10
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
    $startInfo.RedirectStandardError = $false
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    # Inherit stderr so child logs remain visible without a PowerShell callback on a thread-pool thread.
    [void] $process.Start()
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

function Compare-CanonicalText {
    param(
        [Parameter(Mandatory)] [string] $First,
        [Parameter(Mandatory)] [string] $Second
    )

    $firstBytes = [Text.Encoding]::UTF8.GetBytes($First)
    $secondBytes = [Text.Encoding]::UTF8.GetBytes($Second)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $firstSha256 = [Convert]::ToHexString($algorithm.ComputeHash($firstBytes)).ToLowerInvariant()
        $secondSha256 = [Convert]::ToHexString($algorithm.ComputeHash($secondBytes)).ToLowerInvariant()
    }
    finally {
        $algorithm.Dispose()
    }

    $firstDifferenceByteOffset = $null
    $firstDifferenceFirstByte = $null
    $firstDifferenceSecondByte = $null
    $sharedLength = [Math]::Min($firstBytes.Length, $secondBytes.Length)
    for ($index = 0; $index -lt $sharedLength; $index++) {
        if ($firstBytes[$index] -ne $secondBytes[$index]) {
            $firstDifferenceByteOffset = $index
            $firstDifferenceFirstByte = $firstBytes[$index]
            $firstDifferenceSecondByte = $secondBytes[$index]
            break
        }
    }
    if ($null -eq $firstDifferenceByteOffset -and $firstBytes.Length -ne $secondBytes.Length) {
        $firstDifferenceByteOffset = $sharedLength
        if ($sharedLength -lt $firstBytes.Length) {
            $firstDifferenceFirstByte = $firstBytes[$sharedLength]
        }
        if ($sharedLength -lt $secondBytes.Length) {
            $firstDifferenceSecondByte = $secondBytes[$sharedLength]
        }
    }

    [ordered]@{
        bytesEqual = ($null -eq $firstDifferenceByteOffset)
        firstSha256 = $firstSha256
        secondSha256 = $secondSha256
        firstByteCount = $firstBytes.Length
        secondByteCount = $secondBytes.Length
        firstDifferenceByteOffset = $firstDifferenceByteOffset
        firstDifferenceFirstByte = $firstDifferenceFirstByte
        firstDifferenceSecondByte = $firstDifferenceSecondByte
    }
}

function Get-AssemblySourceRevision {
    param([string] $Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return $null
    }
    $productVersion = (Get-Item -LiteralPath $Path).VersionInfo.ProductVersion
    if ($productVersion -match '\+([0-9a-fA-F]{40})(?:$|\.)') {
        return $Matches[1].ToLowerInvariant()
    }
    $null
}

function Get-ExecutionProvenance {
    $sourceRevision = $null
    $sourceTreeClean = $false
    try {
        $sourceRevision = (& git -C $script:RepositoryRoot rev-parse HEAD 2>$null).Trim().ToLowerInvariant()
        if ($LASTEXITCODE -eq 0) {
            $status = @(& git -C $script:RepositoryRoot status --porcelain --untracked-files=normal 2>$null)
            $sourceTreeClean = ($LASTEXITCODE -eq 0 -and $status.Count -eq 0)
        }
    }
    catch {
        $sourceRevision = $null
        $sourceTreeClean = $false
    }

    $hostArtifact = Join-Path $script:RepositoryRoot 'TiaMcpServer'
    $hostArtifact = Join-Path $hostArtifact 'bin'
    $hostArtifact = Join-Path $hostArtifact 'Debug'
    $hostArtifact = Join-Path $hostArtifact 'net8.0'
    $hostArtifact = Join-Path $hostArtifact 'TiaMcpServer.dll'
    $hostExecutableSha256 = if (Test-Path -LiteralPath $hostArtifact) {
        (Get-FileHash -LiteralPath $hostArtifact -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    else { $null }
    $workerExecutableSha256 = if (Test-Path -LiteralPath $WorkerExecutable) {
        (Get-FileHash -LiteralPath $WorkerExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    else { $null }
    $hostSourceRevision = Get-AssemblySourceRevision -Path $hostArtifact
    $workerSourceRevision = Get-AssemblySourceRevision -Path $WorkerExecutable
    $binariesMatchSource = -not [string]::IsNullOrWhiteSpace($sourceRevision) `
        -and $hostSourceRevision -eq $sourceRevision `
        -and $workerSourceRevision -eq $sourceRevision

    [ordered]@{
        sourceRevision = $sourceRevision
        sourceTreeClean = $sourceTreeClean
        hostArtifact = $hostArtifact
        workerExecutable = $WorkerExecutable
        hostExecutableSha256 = $hostExecutableSha256
        workerExecutableSha256 = $workerExecutableSha256
        hostSourceRevision = $hostSourceRevision
        workerSourceRevision = $workerSourceRevision
        finalCommittedCodeExercised = ($sourceTreeClean -and $binariesMatchSource)
    }
}

function ConvertTo-SelectorKey {
    param([Parameter(Mandatory)] [object] $Selector)
    $Selector | ConvertTo-Json -Compress -Depth 100
}

function Get-ItemSelectorKeys {
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Items)
    @($Items | Where-Object { $null -ne $_ -and $null -ne $_.selector } |
        ForEach-Object { ConvertTo-SelectorKey -Selector $_.selector } |
        Sort-Object)
}

function Test-SelectorKeysEqual {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $Expected,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $Actual
    )
    if ($Expected.Count -ne $Actual.Count) {
        return $false
    }
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        if (-not [string]::Equals($Expected[$index], $Actual[$index], [StringComparison]::Ordinal)) {
            return $false
        }
    }
    $true
}

function Get-DiscoveryCanonicalByteCount {
    param([Parameter(Mandatory)] [object[]] $Pages)
    $text = ($Pages | ForEach-Object { $_.CanonicalText }) -join "`n"
    [Text.Encoding]::UTF8.GetByteCount($text)
}

function Get-MaxDiscoveryResultChars {
    param([Parameter(Mandatory)] [object[]] $Pages)
    $lengths = @($Pages | Where-Object { $null -ne $_.Result } | ForEach-Object {
            ($_.Result | ConvertTo-Json -Compress -Depth 100).Length
        })
    if ($lengths.Count -eq 0) { return 0 }
    ($lengths | Measure-Object -Maximum).Maximum
}

function Invoke-HardwareConfig {
    $call = Invoke-NetworkRead -Operations @(@{
            operationId = 'hardware'
            operation = 'read_hardware_config'
        })
    if ($null -eq $call.Envelope.batch -or $null -eq $call.Envelope.batch.operations) {
        throw 'Hardware configuration returned no batch operations.'
    }
    $items = @($call.Envelope.batch.operations)
    if ($items.Count -eq 0 -or $null -eq $items[0]) {
        throw 'Hardware configuration returned an empty batch.'
    }
    $item = $items[0]
    if ($item.status -ne 'succeeded' -and $item.status -ne 'omitted') {
        $failureJson = $item.failure | ConvertTo-Json -Compress -Depth 20
        throw "Hardware configuration failed: $failureJson"
    }
    [pscustomobject]@{
        Result = $item.result
        CanonicalText = $call.CanonicalText
        Omission = $item.omission
        Truncation = $call.Envelope.batch.truncation
    }
}

function Invoke-DiscoveryPage {
    param(
        [string] $Cursor,
        [string] $DeviceName,
        [string[]] $ObjectKinds = $script:ObjectKinds
    )

    $operation = @{
        operationId = 'discovery'
        operation = 'list_network_objects'
        objectKinds = $ObjectKinds
        pageSize = $PageSize
    }
    if (-not [string]::IsNullOrWhiteSpace($Cursor)) {
        $operation.cursor = $Cursor
    }
    if (-not [string]::IsNullOrWhiteSpace($DeviceName)) {
        $operation.deviceName = $DeviceName
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
    param(
        [string] $DeviceName,
        [string[]] $ObjectKinds = $script:ObjectKinds
    )

    $pages = @()
    $items = @()
    $cursor = $null
    $pageNumber = 0
    do {
        $pageNumber++
        if ($pageNumber -gt 1000) {
            throw 'Network discovery exceeded 1000 pages.'
        }
        $page = Invoke-DiscoveryPage -Cursor $cursor -DeviceName $DeviceName -ObjectKinds $ObjectKinds
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

function Get-RequiredMatrixFixtureTargets {
    param([Parameter(Mandatory)] [object[]] $Items)

    $targets = [ordered]@{
        nestedDeviceItem = $null
        networkInterface = $null
        ethernetNode = $null
        ethernetSubnet = $null
        profinetIoSystem = $null
        communicationConnection = $null
    }
    foreach ($item in $Items) {
        if ($null -eq $item -or -not $item.selectable -or $null -eq $item.selector) {
            continue
        }
        switch ($item.kind) {
            'deviceItem' {
                if ($null -eq $targets.nestedDeviceItem -and @($item.selector.itemPath).Count -gt 1) {
                    $targets.nestedDeviceItem = $item.selector
                }
            }
            'networkInterface' {
                if ($null -eq $targets.networkInterface) {
                    $targets.networkInterface = $item.selector
                }
            }
            'node' {
                if ($null -eq $targets.ethernetNode) {
                    $inspection = Invoke-Inspection -Target $item.selector
                    if ($inspection.Result.evidence.nodeType -eq 'Ethernet') {
                        $targets.ethernetNode = $item.selector
                    }
                }
            }
            'subnet' {
                if ($null -eq $targets.ethernetSubnet) {
                    $inspection = Invoke-Inspection -Target $item.selector
                    if ($inspection.Result.evidence.networkType -eq 'Ethernet') {
                        $targets.ethernetSubnet = $item.selector
                    }
                }
            }
            'ioSystem' {
                if ($null -eq $targets.profinetIoSystem) {
                    $inspection = Invoke-Inspection -Target $item.selector
                    if ($inspection.Result.evidence.networkType -eq 'Ethernet') {
                        $targets.profinetIoSystem = $item.selector
                    }
                }
            }
            'communicationConnection' {
                if ($null -eq $targets.communicationConnection) {
                    $targets.communicationConnection = $item.selector
                }
            }
        }
    }
    $targets
}

function Get-MissingFixtureNames {
    param([Parameter(Mandatory)] [Collections.IDictionary] $Targets)
    @($Targets.GetEnumerator() | Where-Object { $null -eq $_.Value } | ForEach-Object { $_.Key })
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
    $firstDiscovery = Get-CompleteDiscovery
    $secondDiscovery = Get-CompleteDiscovery
    if ($firstDiscovery.Pages.Count -eq 0 -or $secondDiscovery.Pages.Count -eq 0) {
        throw 'Repeatability requires complete discovery results.'
    }

    $firstDiscoveryText = ($firstDiscovery.Pages | ForEach-Object { $_.CanonicalText }) -join "`n"
    $secondDiscoveryText = ($secondDiscovery.Pages | ForEach-Object { $_.CanonicalText }) -join "`n"
    $discoveryComparison = Compare-CanonicalText -First $firstDiscoveryText -Second $secondDiscoveryText
    $targets = Get-RequiredMatrixFixtureTargets -Items $firstDiscovery.Items
    $missingFixtures = Get-MissingFixtureNames -Targets $targets
    $fixtureComparisons = [ordered]@{}
    foreach ($entry in $targets.GetEnumerator()) {
        if ($null -eq $entry.Value) {
            continue
        }
        $firstInspection = Invoke-Inspection -Target $entry.Value
        $secondInspection = Invoke-Inspection -Target $entry.Value
        $fixtureComparisons[$entry.Key] = [ordered]@{
            target = $entry.Value
            comparison = Compare-CanonicalText `
                -First $firstInspection.CanonicalText `
                -Second $secondInspection.CanonicalText
        }
    }
    $fixturesComplete = ($missingFixtures.Count -eq 0)
    $inspectionBytesEqual = $fixturesComplete
    foreach ($entry in $fixtureComparisons.GetEnumerator()) {
        if (-not $entry.Value.comparison.bytesEqual) {
            $inspectionBytesEqual = $false
        }
    }

    [ordered]@{
        mode = 'Repeatability'
        canonicalBytesEqual = ($discoveryComparison.bytesEqual -and $inspectionBytesEqual)
        discoveryCanonicalBytesEqual = $discoveryComparison.bytesEqual
        inspectionCanonicalBytesEqual = $inspectionBytesEqual
        discoveryComparison = $discoveryComparison
        fixtureComparisons = $fixtureComparisons
        fixturesComplete = $fixturesComplete
        missingFixtures = $missingFixtures
        requestCount = $script:NetworkRequestCount
    }
}

function Invoke-MeasureListValue {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $discovery = Get-CompleteDiscovery
    $selectable = @($discovery.Items | Where-Object { $_.selectable })
    $selectors = @($selectable | Where-Object { $null -ne $_.selector })
    $unselectable = @($discovery.Items | Where-Object { -not $_.selectable -or $null -eq $_.selector })
    $fullCanonicalByteCount = Get-DiscoveryCanonicalByteCount -Pages $discovery.Pages

    $hardware = Invoke-HardwareConfig
    $hardwareOriginalChars = if ($null -ne $hardware.Omission) {
        [int] $hardware.Omission.originalChars
    }
    elseif ($null -ne $hardware.Result) {
        ($hardware.Result | ConvertTo-Json -Compress -Depth 100).Length
    }
    else { 0 }
    $readHardwareConfigExceededPerItemBudget = ($hardwareOriginalChars -gt 60000)
    $discoveryFixtureObserved = ($discovery.Items.Count -gt 0)
    $discoverySelectorsComplete = ($discoveryFixtureObserved `
        -and $selectors.Count -eq $discovery.Items.Count)
    $hardwareBudgetConditionMet = ($readHardwareConfigExceededPerItemBudget -and $discoverySelectorsComplete)

    $representativeDeviceName = $null
    $targetedDiscovery = $null
    $matchingSelectorsPreserved = $false
    $targetedCanonicalByteCount = 0
    $canonicalByteReductionPercent = 0.0
    $deviceGroups = @($selectors | Where-Object {
            -not [string]::IsNullOrWhiteSpace([string] $_.selector.deviceName)
        } | Group-Object { [string] $_.selector.deviceName } |
        Sort-Object -Property @{ Expression = 'Count'; Descending = $true }, @{ Expression = 'Name'; Descending = $false })
    if ($deviceGroups.Count -gt 0) {
        $representativeDeviceName = [string] $deviceGroups[0].Name
        $targetedDiscovery = Get-CompleteDiscovery -DeviceName $representativeDeviceName
        $fullMatchingItems = @($discovery.Items | Where-Object {
                $null -ne $_.selector `
                    -and [string]::Equals(
                        [string] $_.selector.deviceName,
                        $representativeDeviceName,
                        [StringComparison]::Ordinal)
            })
        $fullMatchingKeys = @(Get-ItemSelectorKeys -Items $fullMatchingItems)
        $targetedKeys = @(Get-ItemSelectorKeys -Items $targetedDiscovery.Items)
        $matchingSelectorsPreserved = Test-SelectorKeysEqual `
            -Expected $fullMatchingKeys `
            -Actual $targetedKeys
        $targetedCanonicalByteCount = Get-DiscoveryCanonicalByteCount -Pages $targetedDiscovery.Pages
        if ($fullCanonicalByteCount -gt 0) {
            $canonicalByteReductionPercent = [Math]::Round(
                (1.0 - ($targetedCanonicalByteCount / [double] $fullCanonicalByteCount)) * 100.0,
                2)
        }
    }
    $atLeastFiftyPercentSmaller = ($canonicalByteReductionPercent -ge 50.0)
    $representativeDeviceConditionMet = ($matchingSelectorsPreserved -and $atLeastFiftyPercentSmaller)

    $connections = @($discovery.Items | Where-Object { $_.kind -eq 'communicationConnection' })
    $connectionFixtureObserved = ($connections.Count -gt 0)
    $connectionBaselineKeys = @(Get-ItemSelectorKeys -Items $connections)
    $connectionOnly = Get-CompleteDiscovery -ObjectKinds @('communicationConnection')
    $connectionOnlyKeys = @(Get-ItemSelectorKeys -Items $connectionOnly.Items)
    $allConnectionSelectorsPreserved = Test-SelectorKeysEqual `
        -Expected $connectionBaselineKeys `
        -Actual $connectionOnlyKeys
    $allObservedConnectionsSelectable = ($connectionFixtureObserved `
        -and $connectionBaselineKeys.Count -eq $connections.Count)
    $connectionOmissions = @($connectionOnly.Pages | Where-Object { $null -ne $_.Omission }).Count
    $connectionTruncations = @($connectionOnly.Pages | Where-Object {
            $null -ne $_.Truncation -and $_.Truncation.truncated
        }).Count
    $connectionMaxResultChars = Get-MaxDiscoveryResultChars -Pages $connectionOnly.Pages
    $underPerItemBudget = ($connectionOmissions -eq 0 `
        -and $connectionTruncations -eq 0 `
        -and $connectionMaxResultChars -le 60000)
    $fullTreeCallsAvoided = if ($allConnectionSelectorsPreserved `
        -and $allObservedConnectionsSelectable `
        -and $underPerItemBudget) { 1 } else { 0 }
    $connectionConditionMet = ($fullTreeCallsAvoided -eq 1)

    $allPages = @($discovery.Pages) + @($connectionOnly.Pages)
    if ($null -ne $targetedDiscovery) {
        $allPages += @($targetedDiscovery.Pages)
    }
    $omissions = @($allPages | Where-Object { $null -ne $_.Omission }).Count
    if ($null -ne $hardware.Omission) { $omissions++ }
    $truncation = @($allPages | Where-Object {
            $null -ne $_.Truncation -and $_.Truncation.truncated
        }).Count
    if ($null -ne $hardware.Truncation -and $hardware.Truncation.truncated) { $truncation++ }
    $timer.Stop()

    [ordered]@{
        mode = 'MeasureListValue'
        canonicalByteCount = $fullCanonicalByteCount
        elapsedMilliseconds = $timer.ElapsedMilliseconds
        selectorCount = $selectors.Count
        selectableEntriesCarrySelectors = ($selectors.Count -eq $selectable.Count)
        unselectableItemCount = $unselectable.Count
        omissions = $omissions
        truncation = $truncation
        requestCount = $script:NetworkRequestCount
        connectionDiscoveryUsable = $connectionConditionMet
        discoveryItemCount = $discovery.Items.Count
        discoveryPageCount = $discovery.Pages.Count
        retentionGates = [ordered]@{
            hardwareBudgetCoverage = [ordered]@{
                readHardwareConfigExceededPerItemBudget = $readHardwareConfigExceededPerItemBudget
                readHardwareConfigOriginalChars = $hardwareOriginalChars
                discoveryFixtureObserved = $discoveryFixtureObserved
                discoverySelectorsComplete = $discoverySelectorsComplete
                discoveryItemCount = $discovery.Items.Count
                discoverySelectorCount = $selectors.Count
                conditionMet = $hardwareBudgetConditionMet
            }
            representativeDeviceQuery = [ordered]@{
                representativeDeviceName = $representativeDeviceName
                matchingSelectorsPreserved = $matchingSelectorsPreserved
                fullCanonicalByteCount = $fullCanonicalByteCount
                targetedCanonicalByteCount = $targetedCanonicalByteCount
                canonicalByteReductionPercent = $canonicalByteReductionPercent
                atLeastFiftyPercentSmaller = $atLeastFiftyPercentSmaller
                conditionMet = $representativeDeviceConditionMet
            }
            connectionOnlyQuery = [ordered]@{
                allObservedConnectionCount = $connections.Count
                allObservedConnectionSelectorCount = $connectionBaselineKeys.Count
                connectionOnlySelectorCount = $connectionOnlyKeys.Count
                connectionFixtureObserved = $connectionFixtureObserved
                allConnectionSelectorsPreserved = $allConnectionSelectorsPreserved
                allObservedConnectionsSelectable = $allObservedConnectionsSelectable
                maxResultChars = $connectionMaxResultChars
                underPerItemBudget = $underPerItemBudget
                fullTreeCallsAvoided = $fullTreeCallsAvoided
                requestCountSavings = ($discovery.Pages.Count - $connectionOnly.Pages.Count)
                conditionMet = $connectionConditionMet
            }
        }
        retentionConditionMet = ($hardwareBudgetConditionMet `
            -or $representativeDeviceConditionMet `
            -or $connectionConditionMet)
    }
}

function Invoke-RawProbe {
    $discovery = Get-CompleteDiscovery
    $targets = Get-RequiredMatrixFixtureTargets -Items $discovery.Items
    $missingFixtures = Get-MissingFixtureNames -Targets $targets
    if (-not (Test-Path -LiteralPath $WorkerExecutable)) {
        throw "The worker executable was not found at '$WorkerExecutable'."
    }

    $script:WorkerProcess = Start-JsonLineProcess `
        -Executable $WorkerExecutable `
        -Arguments @('--access-mode', 'read-only') `
        -Label 'raw-probe-worker'
    $probesByFixture = [ordered]@{}
    $workerRequestCount = 0
    foreach ($entry in $targets.GetEnumerator()) {
        if ($null -eq $entry.Value) {
            continue
        }
        Send-JsonLine -Process $script:WorkerProcess -Message @{
            method = 'probe_network_object_attributes'
            networkObjectTarget = $entry.Value
        }
        $workerRequestCount++
        $response = Read-JsonLine -Process $script:WorkerProcess
        if ($null -eq $response -or -not $response.success) {
            $failure = $response | ConvertTo-Json -Compress -Depth 20
            throw "RawProbe failed for '$($entry.Key)': $failure"
        }
        $payloadText = $response.payload
        if ($null -eq $payloadText) {
            throw "RawProbe returned no payload for '$($entry.Key)'."
        }
        if ([string]::IsNullOrWhiteSpace($payloadText)) {
            throw "RawProbe returned no payload for '$($entry.Key)'."
        }
        $payload = $payloadText | ConvertFrom-Json -Depth 100
        if ($null -eq $payload) {
            throw "RawProbe returned a null payload for '$($entry.Key)'."
        }
        $probesByFixture[$entry.Key] = [ordered]@{
            target = $entry.Value
            probe = $payload
        }
    }
    [ordered]@{
        mode = 'RawProbe'
        probesByFixture = $probesByFixture
        probeComplete = ($missingFixtures.Count -eq 0)
        missingFixtures = $missingFixtures
        requestCount = $script:NetworkRequestCount
        workerRequestCount = $workerRequestCount
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
$evidence['executionProvenance'] = Get-ExecutionProvenance

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
