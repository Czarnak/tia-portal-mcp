#Requires -Version 7
<#
.SYNOPSIS
    Separately authorized, read-only live acceptance harness for paginated hardware reads.

.DESCRIPTION
    Launches the real TiaMcpServer host in read-only mode and calls only the public network_read
    tool with read_hardware_config. It follows nextCursor until the terminal page, verifies the
    public page counters, device-before-subnet order, observed ordered public-entity fingerprints,
    and the 60,000-character operation-item limit. It writes count/order consistency and timing
    evidence to separate JSON artifacts; without an independent expected inventory, it does not
    claim exact project reconstruction from returned counts alone.

    THIS SCRIPT IS NOT RUN BY ANY AUTOMATED TEST OR CI GATE. Running it requires an explicit
    invocation, a running TIA Portal V21 instance, a suitable open project, and separate live-TIA
    authorization. A successful run proves only the exact project/filter/detail combination
    supplied to that invocation.

    The harness stops on every failed or omitted operation. Cursor failures include
    invalid_cursor, cursor_filter_mismatch, cursor_binding_mismatch, cursor_snapshot_mismatch,
    and cursor_out_of_range. Failure artifacts deliberately record whether a cursor was present,
    but never record the opaque cursor value.

.PARAMETER ProjectPath
    Absolute path to the exact TIA Portal V21 .ap21 project being accepted.

.PARAMETER DeviceName
    Optional exact device filter, held unchanged for the full cursor sequence.

.PARAMETER PlcName
    Optional exact PLC name used for tag matching, held unchanged for the full sequence.

.PARAMETER IncludeIoDetails
    Include structured I/O details.

.PARAMETER IncludeTagMatches
    Include PLC tag matches. Requires IncludeIoDetails.

.PARAMETER PageSize
    Requested combined device-then-subnet page size. Valid range: 1 through 200.

.EXAMPLE
    pwsh -File scripts/live-test-hardware-pagination.ps1 `
        -ProjectPath C:\Sandbox\PaginationAcceptance.ap21 -PageSize 25

.EXAMPLE
    pwsh -File scripts/live-test-hardware-pagination.ps1 `
        -ProjectPath C:\Sandbox\PaginationAcceptance.ap21 -PageSize 10 `
        -DeviceName PLC_1 -PlcName PLC_1 -IncludeIoDetails -IncludeTagMatches

.NOTES
    Read-only. No project state is changed by this harness.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ProjectPath,
    [string] $DeviceName,
    [string] $PlcName,
    [switch] $IncludeIoDetails,
    [switch] $IncludeTagMatches,
    [ValidateRange(1, 200)]
    [int] $PageSize = 50,
    [string] $HostExecutable = 'dotnet',
    [string[]] $HostArguments,
    [int] $TimeoutSeconds = 240
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($IncludeTagMatches -and -not $IncludeIoDetails) {
    throw '-IncludeTagMatches requires -IncludeIoDetails.'
}

$script:RepositoryRoot = Split-Path -Parent $PSScriptRoot
$script:HostProcess = $null
$script:NextRequestId = 0
$script:ItemCharacterLimit = 60000
$script:ArtifactDirectory = $null
$script:TranscriptPath = $null
$script:PageEvidence = [System.Collections.Generic.List[object]]::new()
$script:TimingEvidence = [System.Collections.Generic.List[object]]::new()
$script:EntityFingerprintEvidence = [System.Collections.Generic.List[object]]::new()
$script:ExpectedTotals = $null
$script:DeviceOffset = 0
$script:SubnetOffset = 0
$script:BoundQuery = [ordered]@{
    projectPath = $ProjectPath
}

if (-not [string]::IsNullOrWhiteSpace($DeviceName)) {
    $script:BoundQuery['deviceName'] = $DeviceName
}
if (-not [string]::IsNullOrWhiteSpace($PlcName)) {
    $script:BoundQuery['plcName'] = $PlcName
}
if ($IncludeIoDetails) {
    $script:BoundQuery['includeIoDetails'] = $true
}
if ($IncludeTagMatches) {
    $script:BoundQuery['includeTagMatches'] = $true
}

if ($null -eq $HostArguments -or $HostArguments.Count -eq 0) {
    $hostDll = Join-Path $script:RepositoryRoot 'TiaMcpServer'
    $hostDll = Join-Path $hostDll 'bin'
    $hostDll = Join-Path $hostDll 'Debug'
    $hostDll = Join-Path $hostDll 'net8.0'
    $hostDll = Join-Path $hostDll 'TiaMcpServer.dll'
    $HostArguments = @($hostDll, '--access-mode', 'read-only')
}

function Initialize-ArtifactDirectory {
    $artifactRoot = Join-Path $script:RepositoryRoot 'artifacts'
    $artifactRoot = Join-Path $artifactRoot 'live-hardware-pagination'
    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmssfff'
    $script:ArtifactDirectory = Join-Path $artifactRoot $timestamp
    [void] (New-Item -ItemType Directory -Force -Path $script:ArtifactDirectory)
    $script:TranscriptPath = Join-Path $script:ArtifactDirectory 'transcript.ndjson'
}

function Write-JsonArtifact {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] $Value
    )

    $path = Join-Path $script:ArtifactDirectory $Name
    $Value | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $path -Encoding utf8NoBOM
    return $path
}

function Add-TranscriptEntry {
    param(
        [Parameter(Mandatory)] [string] $Direction,
        [Parameter(Mandatory)] [int] $Id,
        [string] $Method,
        [string] $Outcome
    )

    $entry = [ordered]@{
        timestampUtc = (Get-Date).ToUniversalTime().ToString('o')
        direction = $Direction
        id = $Id
    }
    if (-not [string]::IsNullOrWhiteSpace($Method)) {
        $entry['method'] = $Method
    }
    if (-not [string]::IsNullOrWhiteSpace($Outcome)) {
        $entry['outcome'] = $Outcome
    }
    $line = $entry | ConvertTo-Json -Compress -Depth 10
    Add-Content -LiteralPath $script:TranscriptPath -Value $line -Encoding utf8NoBOM
}

function Start-McpHost {
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $HostExecutable
    foreach ($argument in $HostArguments) {
        [void] $psi.ArgumentList.Add($argument)
    }
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $false
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $psi
    [void] $process.Start()
    $script:HostProcess = $process
}

function Stop-McpHost {
    if ($null -ne $script:HostProcess -and -not $script:HostProcess.HasExited) {
        try {
            $script:HostProcess.StandardInput.Close()
        }
        catch {
            # Best-effort close; the process kill below remains the cleanup backstop.
        }
        if (-not $script:HostProcess.WaitForExit(5000)) {
            $script:HostProcess.Kill($true)
        }
    }
}

function Send-McpMessage {
    param(
        [Parameter(Mandatory)] [hashtable] $Message,
        [Parameter(Mandatory)] [int] $Id,
        [Parameter(Mandatory)] [string] $Method
    )

    $json = $Message | ConvertTo-Json -Compress -Depth 30
    $script:HostProcess.StandardInput.WriteLine($json)
    $script:HostProcess.StandardInput.Flush()
    Add-TranscriptEntry -Direction 'request' -Id $Id -Method $Method
}

function Read-McpResponse {
    param([Parameter(Mandatory)] [int] $Id)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ($script:HostProcess.HasExited) {
            throw "The MCP host exited with code $($script:HostProcess.ExitCode) before response id $Id."
        }

        $remaining = $deadline - (Get-Date)
        if ($remaining.TotalMilliseconds -le 0) {
            break
        }

        $readTask = $script:HostProcess.StandardOutput.ReadLineAsync()
        try {
            $line = $readTask.WaitAsync($remaining).GetAwaiter().GetResult()
        }
        catch [System.TimeoutException] {
            break
        }
        if ($null -eq $line -or [string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $parsed = $null
        try {
            $parsed = $line | ConvertFrom-Json -Depth 100
        }
        catch {
            continue
        }
        if ($null -ne $parsed.PSObject.Properties['id'] -and $parsed.id -eq $Id) {
            $outcome = 'result'
            if ($null -ne $parsed.PSObject.Properties['error'] -and $null -ne $parsed.error) {
                $outcome = 'error'
            }
            Add-TranscriptEntry -Direction 'response' -Id $Id -Outcome $outcome
            return $parsed
        }
    }
    throw "Timed out after $TimeoutSeconds second(s) waiting for response id $Id."
}

function Invoke-McpRequest {
    param(
        [Parameter(Mandatory)] [string] $Method,
        [hashtable] $Params = @{}
    )

    $id = ++$script:NextRequestId
    $message = @{ jsonrpc = '2.0'; id = $id; method = $Method; params = $Params }
    Send-McpMessage -Message $message -Id $id -Method $Method
    $response = Read-McpResponse -Id $id
    if ($null -ne $response.PSObject.Properties['error'] -and $null -ne $response.error) {
        $errorJson = $response.error | ConvertTo-Json -Compress -Depth 20
        throw "MCP request '$Method' returned a protocol error: $errorJson"
    }
    return $response.result
}

function Invoke-McpNotification {
    param(
        [Parameter(Mandatory)] [string] $Method,
        [hashtable] $Params = @{}
    )

    $message = @{ jsonrpc = '2.0'; method = $Method; params = $Params }
    $json = $message | ConvertTo-Json -Compress -Depth 20
    $script:HostProcess.StandardInput.WriteLine($json)
    $script:HostProcess.StandardInput.Flush()
}

function Connect-McpHost {
    Start-McpHost
    $initializeResult = Invoke-McpRequest -Method 'initialize' -Params @{
        protocolVersion = '2025-06-18'
        capabilities = @{}
        clientInfo = @{ name = 'live-test-hardware-pagination'; version = '1.0.0' }
    }
    Invoke-McpNotification -Method 'notifications/initialized'

    $tools = Invoke-McpRequest -Method 'tools/list'
    $toolNames = @($tools.tools | ForEach-Object { $_.name })
    if ($toolNames -notcontains 'network_read') {
        throw "The connected read-only MCP host does not advertise network_read."
    }
    Write-Host "Connected to MCP host: $($initializeResult.serverInfo.name) $($initializeResult.serverInfo.version)"
}

function Invoke-McpToolCall {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [hashtable] $Arguments
    )

    $result = Invoke-McpRequest -Method 'tools/call' -Params @{ name = $Name; arguments = $Arguments }
    if ($result.isError) {
        throw "Tool '$Name' returned isError:true."
    }
    if ($null -eq $result.content -or @($result.content).Count -eq 0) {
        throw "Tool '$Name' returned no text content."
    }
    $contentText = [string] $result.content[0].text
    if ([string]::IsNullOrWhiteSpace($contentText)) {
        throw "Tool '$Name' returned empty text content."
    }
    return [pscustomobject]@{
        Response = ($contentText | ConvertFrom-Json -Depth 100)
        ContentText = $contentText
    }
}

function Assert-BoundQueryUnchanged {
    param([Parameter(Mandatory)] [hashtable] $ReadOperation)

    $actual = [ordered]@{}
    foreach ($name in @('projectPath', 'deviceName', 'plcName', 'includeIoDetails', 'includeTagMatches')) {
        if ($ReadOperation.ContainsKey($name)) {
            $actual[$name] = $ReadOperation[$name]
        }
    }
    $expectedJson = $script:BoundQuery | ConvertTo-Json -Compress -Depth 10
    $actualJson = $actual | ConvertTo-Json -Compress -Depth 10
    if (-not [string]::Equals($expectedJson, $actualJson, [StringComparison]::Ordinal)) {
        throw 'The next page request changed a cursor-bound query field.'
    }
}

function New-HardwareReadOperation {
    param([AllowNull()] [string] $nextCursor)

    $readOperation = [ordered]@{
        operationId = 'hardware-pagination'
        operation = 'read_hardware_config'
    }
    foreach ($name in $script:BoundQuery.Keys) {
        $readOperation[$name] = $script:BoundQuery[$name]
    }
    $readOperation.pageSize = $PageSize
    if (-not [string]::IsNullOrEmpty($nextCursor)) {
        $readOperation.cursor = $nextCursor
    }
    Assert-BoundQueryUnchanged -ReadOperation $readOperation
    return $readOperation
}

function Invoke-HardwarePage {
    param([AllowNull()] [string] $nextCursor)

    $readOperation = New-HardwareReadOperation -nextCursor $nextCursor
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $toolCall = Invoke-McpToolCall -Name 'network_read' -Arguments @{ operations = @($readOperation) }
    $stopwatch.Stop()
    return [pscustomobject]@{
        Response = $toolCall.Response
        ContentText = $toolCall.ContentText
        ElapsedMilliseconds = $stopwatch.ElapsedMilliseconds
    }
}

function Get-CanonicalSha256 {
    param([Parameter(Mandatory)] [string] $CanonicalText)

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($CanonicalText)
    $hash = [System.Security.Cryptography.SHA256]::HashData($bytes)
    return [System.Convert]::ToHexString($hash).ToLowerInvariant()
}

function Get-CanonicalOperationItemEvidence {
    param([Parameter(Mandatory)] [string] $ContentText)

    $document = [System.Text.Json.JsonDocument]::Parse($ContentText)
    try {
        $operations = $document.RootElement.GetProperty('batch').GetProperty('operations')
        if ($operations.GetArrayLength() -ne 1) {
            throw 'Canonical network_read content did not contain exactly one operation item.'
        }

        $item = $null
        foreach ($candidate in $operations.EnumerateArray()) {
            $item = $candidate
            break
        }
        $itemText = $item.GetRawText()
        $devices = [System.Collections.Generic.List[object]]::new()
        $subnets = [System.Collections.Generic.List[object]]::new()
        $result = $item.GetProperty('result')
        if ($result.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
            foreach ($device in $result.GetProperty('devices').EnumerateArray()) {
                $name = $device.GetProperty('name').GetString()
                $devices.Add([pscustomobject]@{
                    kind = 'device'
                    name = $name
                    identifier = $null
                    canonicalSha256 = Get-CanonicalSha256 -CanonicalText ($device.GetRawText())
                })
            }
            foreach ($subnet in $result.GetProperty('subnets').EnumerateArray()) {
                $name = $subnet.GetProperty('name').GetString()
                $identifier = $subnet.GetProperty('subnetId').GetString()
                $subnets.Add([pscustomobject]@{
                    kind = 'subnet'
                    name = $name
                    identifier = $identifier
                    canonicalSha256 = Get-CanonicalSha256 -CanonicalText ($subnet.GetRawText())
                })
            }
        }

        return [pscustomobject]@{
            ItemCharacters = $itemText.Length
            Devices = @($devices)
            Subnets = @($subnets)
        }
    }
    finally {
        $document.Dispose()
    }
}

function Assert-StableTotals {
    param([Parameter(Mandatory)] $Pagination)

    $current = [ordered]@{
        totalDevices = [int] $Pagination.totalDevices
        totalSubnets = [int] $Pagination.totalSubnets
    }
    if ($null -eq $script:ExpectedTotals) {
        $script:ExpectedTotals = $current
        return
    }
    if ($current.totalDevices -ne $script:ExpectedTotals.totalDevices -or
        $current.totalSubnets -ne $script:ExpectedTotals.totalSubnets) {
        throw 'Hardware pagination totals changed during the cursor sequence.'
    }
}

function Assert-CombinedPageOrderAndSize {
    param(
        [Parameter(Mandatory)] $Result,
        [Parameter(Mandatory)] $Pagination
    )

    $devices = @($Result.devices)
    $subnets = @($Result.subnets)
    $combinedCount = $devices.Count + $subnets.Count
    if ($combinedCount -gt $PageSize) {
        throw "A hardware page returned $combinedCount entities for pageSize $PageSize."
    }
    if ($script:SubnetOffset -gt 0 -and $devices.Count -gt 0) {
        throw 'A hardware page returned devices after the subnet portion of the sequence began.'
    }
    $prospectiveDeviceEnd = $script:DeviceOffset + $devices.Count
    if ($subnets.Count -gt 0 -and $prospectiveDeviceEnd -ne [int] $Pagination.totalDevices) {
        throw 'A hardware page returned subnets before all matching devices were reconstructed.'
    }
}

function Assert-PageOffsets {
    param(
        [Parameter(Mandatory)] $Result,
        [Parameter(Mandatory)] $Pagination
    )

    $devices = @($Result.devices)
    $subnets = @($Result.subnets)
    if ($devices.Count -ne [int] $Pagination.returnedDevices) {
        throw 'returnedDevices does not match the page devices array.'
    }
    if ($subnets.Count -ne [int] $Pagination.returnedSubnets) {
        throw 'returnedSubnets does not match the page subnets array.'
    }

    $offsets = [ordered]@{
        deviceStartOffset = $script:DeviceOffset
        deviceEndOffset = $script:DeviceOffset + $devices.Count
        subnetStartOffset = $script:SubnetOffset
        subnetEndOffset = $script:SubnetOffset + $subnets.Count
    }
    $script:DeviceOffset = $offsets.deviceEndOffset
    $script:SubnetOffset = $offsets.subnetEndOffset
    return $offsets
}

function Write-FailureArtifact {
    param(
        [Parameter(Mandatory)] [string] $Kind,
        [Parameter(Mandatory)] [int] $PageIndex,
        [Parameter(Mandatory)] [bool] $CursorPresent,
        $Evidence
    )

    $failure = [ordered]@{
        outcome = 'failed'
        kind = $Kind
        pageIndex = $PageIndex
        cursorPresent = $CursorPresent
        evidence = $Evidence
        guidance = 'Retry the unchanged request at the same cursor, or start a new sequence with narrower filters or fewer detail options. Never reuse the old cursor after changing bound fields.'
    }
    return Write-JsonArtifact -Name 'failure.json' -Value $failure
}

function Save-PartialEvidence {
    param([Parameter(Mandatory)] [hashtable] $Combination)

    $correctness = [ordered]@{
        outcome = 'partial'
        testedCombination = $Combination
        observedOrderedEntities = @($script:EntityFingerprintEvidence)
        pages = @($script:PageEvidence)
    }
    $timing = [ordered]@{ pages = @($script:TimingEvidence) }
    [void] (Write-JsonArtifact -Name 'correctness.json' -Value $correctness)
    [void] (Write-JsonArtifact -Name 'timing.json' -Value $timing)
}

Initialize-ArtifactDirectory
$testedCombination = [ordered]@{
    projectPath = $ProjectPath
    deviceName = $DeviceName
    plcName = $PlcName
    includeIoDetails = [bool] $IncludeIoDetails
    includeTagMatches = [bool] $IncludeTagMatches
    pageSize = $PageSize
}
$pageIndex = 0
$nextCursor = ''
$failureRecorded = $false

try {
    Connect-McpHost

    while ($null -ne $nextCursor) {
        $cursorPresent = -not [string]::IsNullOrEmpty($nextCursor)
        $call = Invoke-HardwarePage -nextCursor $nextCursor
        $batch = $call.Response.batch
        if ($null -eq $batch -or @($batch.operations).Count -ne 1) {
            throw 'network_read did not return exactly one operation item.'
        }
        $item = @($batch.operations)[0]
        $canonicalEvidence = Get-CanonicalOperationItemEvidence -ContentText $call.ContentText
        $itemCharacters = $canonicalEvidence.ItemCharacters
        if ($itemCharacters -gt $script:ItemCharacterLimit) {
            throw "Canonical operation item length $itemCharacters exceeds $($script:ItemCharacterLimit)."
        }
        $script:TimingEvidence.Add([ordered]@{
            pageIndex = $pageIndex
            elapsedMilliseconds = $call.ElapsedMilliseconds
        })

        if ($item.status -eq 'omitted') {
            $failurePath = Write-FailureArtifact -Kind 'omitted' -PageIndex $pageIndex -CursorPresent $cursorPresent -Evidence $item.omission
            $failureRecorded = $true
            throw "Hardware page was omitted. Review $failurePath."
        }
        if ($item.status -ne 'succeeded') {
            $cursorCategories = @(
                'invalid_cursor',
                'cursor_filter_mismatch',
                'cursor_binding_mismatch',
                'cursor_snapshot_mismatch',
                'cursor_out_of_range'
            )
            $category = [string] $item.failure.category
            $kind = 'operationFailure'
            if ($cursorCategories -contains $category) {
                $kind = 'cursorFailure'
            }
            $failurePath = Write-FailureArtifact -Kind $kind -PageIndex $pageIndex -CursorPresent $cursorPresent -Evidence $item.failure
            $failureRecorded = $true
            throw "Hardware page failed with category '$category'. Review $failurePath."
        }

        $result = $item.result
        if ($null -eq $result -or $null -eq $result.pagination) {
            throw 'A successful hardware page omitted result or pagination metadata.'
        }
        Assert-StableTotals -Pagination $result.pagination
        Assert-CombinedPageOrderAndSize -Result $result -Pagination $result.pagination
        $offsets = Assert-PageOffsets -Result $result -Pagination $result.pagination
        $pageEntities = [System.Collections.Generic.List[object]]::new()
        $entityIndex = 0
        foreach ($entity in @($canonicalEvidence.Devices)) {
            $record = [ordered]@{
                sequenceIndex = $offsets.deviceStartOffset + $entityIndex
                kind = $entity.kind
                name = $entity.name
                identifier = $entity.identifier
                canonicalSha256 = $entity.canonicalSha256
            }
            $script:EntityFingerprintEvidence.Add($record)
            $pageEntities.Add($record)
            $entityIndex++
        }
        $entityIndex = 0
        foreach ($entity in @($canonicalEvidence.Subnets)) {
            $record = [ordered]@{
                sequenceIndex = [int] $result.pagination.totalDevices + $offsets.subnetStartOffset + $entityIndex
                kind = $entity.kind
                name = $entity.name
                identifier = $entity.identifier
                canonicalSha256 = $entity.canonicalSha256
            }
            $script:EntityFingerprintEvidence.Add($record)
            $pageEntities.Add($record)
            $entityIndex++
        }
        $returnedCount = [int] $result.pagination.returnedDevices + [int] $result.pagination.returnedSubnets
        $nextCursorProperty = $result.pagination.PSObject.Properties['nextCursor']
        $hasNextCursor = $null -ne $nextCursorProperty -and $null -ne $nextCursorProperty.Value
        if ($hasNextCursor -and $returnedCount -eq 0) {
            throw 'A non-terminal hardware page made no offset progress.'
        }

        $script:PageEvidence.Add([ordered]@{
            pageIndex = $pageIndex
            canonicalOperationItemCharacters = $itemCharacters
            returnedDevices = [int] $result.pagination.returnedDevices
            returnedSubnets = [int] $result.pagination.returnedSubnets
            totalDevices = [int] $result.pagination.totalDevices
            totalSubnets = [int] $result.pagination.totalSubnets
            deviceStartOffset = $offsets.deviceStartOffset
            deviceEndOffset = $offsets.deviceEndOffset
            subnetStartOffset = $offsets.subnetStartOffset
            subnetEndOffset = $offsets.subnetEndOffset
            observedOrderedEntities = @($pageEntities)
            hasNextCursor = $hasNextCursor
        })

        $nextCursor = $null
        if ($hasNextCursor) {
            $nextCursor = [string] $nextCursorProperty.Value
        }
        $pageIndex++
    }

    if ($script:DeviceOffset -ne $script:ExpectedTotals.totalDevices -or
        $script:SubnetOffset -ne $script:ExpectedTotals.totalSubnets) {
        throw 'Terminal offsets were inconsistent with the reported device and subnet totals.'
    }

    $correctness = [ordered]@{
        outcome = 'passed'
        scope = 'Only the exact live project/filter/detail combination recorded here was tested; count/order consistency is observed evidence, not an independent expected inventory.'
        testedCombination = $testedCombination
        totalDevices = $script:ExpectedTotals.totalDevices
        totalSubnets = $script:ExpectedTotals.totalSubnets
        observedOrderedEntities = @($script:EntityFingerprintEvidence)
        pages = @($script:PageEvidence)
    }
    $timing = [ordered]@{ pages = @($script:TimingEvidence) }
    $correctnessPath = Write-JsonArtifact -Name 'correctness.json' -Value $correctness
    $timingPath = Write-JsonArtifact -Name 'timing.json' -Value $timing
    Write-Host 'PASS: read-only live pagination reported internally consistent counts, order, and observed entity fingerprints.'
    Write-Host 'This proves only the exact live project/filter/detail combination recorded in the artifact.'
    Write-Host "Correctness evidence: $correctnessPath"
    Write-Host "Timing evidence: $timingPath"
}
catch {
    Save-PartialEvidence -Combination $testedCombination
    if (-not $failureRecorded) {
        $cursorPresent = -not [string]::IsNullOrEmpty($nextCursor)
        $message = $_.Exception.Message
        $failurePath = Write-FailureArtifact -Kind 'harnessFailure' -PageIndex $pageIndex -CursorPresent $cursorPresent -Evidence @{ message = $message }
        Write-Error "Hardware pagination harness failed. Review $failurePath. $message"
    }
    else {
        Write-Error $_
    }
}
finally {
    Stop-McpHost
}
