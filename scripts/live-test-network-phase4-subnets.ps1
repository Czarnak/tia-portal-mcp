#Requires -Version 7
<#
.SYNOPSIS
    Separately authorized public-MCP acceptance for Ethernet/PROFIBUS subnet lifecycle.
.DESCRIPTION
    Inventory (default) reads only. Preview uses dryRun:true. Apply requires AllowMutation
    and the exact acknowledgement DELETE SUBNETS AND KEEP DEVICES before host startup.
    The explicit already-open ProjectPath is inspected, then bound with bind_project.
    No save, close, compile, download, PLC action, batch rollback or automatic retry.
    Connected Ethernet/PROFIBUS subnet IDs must come from fresh inventory; names are not IDs.
    Apply creates/updates/deletes isolated subnets, then permanently deletes the two connected
    fixture subnets. It does not restore connected deletions: use a disposable backed-up fixture
    and a separately authorized restoration procedure, followed by fresh identity inspection.
    Network has zero server elicitation in read-write/full; harness gates are client protections.
    Explicit dryRun:false is used for execution (omitting dryRun also executes).
    Commit/tree/script SHA-256 freeze evidence is required in every mode. No mode runs in CI.
    Historical Phase4 acceptance does not qualify this guarded candidate.
#>
[CmdletBinding()]
param(
    [ValidateSet('Inventory', 'Preview', 'Apply')]
    [string] $Mode = 'Inventory',

    [Parameter(Mandatory)] [string] $ProjectPath,
    [ValidateSet('read-write', 'full')] [string] $AccessMode = 'read-write',

    [string] $ConnectedEthernetSubnetId,
    [string] $ConnectedProfibusSubnetId,

    [switch] $AllowMutation,
    [string] $Acknowledgement,

    [Parameter(Mandatory)] [string] $ExpectedCommit,
    [Parameter(Mandatory)] [string] $ExpectedTree,
    [Parameter(Mandatory)] [string] $ExpectedHarnessSha256,

    [int] $StartupTimeoutSeconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RepositoryRoot = Split-Path -Parent $PSScriptRoot
$script:RequiredAcknowledgement = 'DELETE SUBNETS AND KEEP DEVICES'
$script:EthernetNetworkType = 'Ethernet'
$script:ProfibusNetworkType = 'Profibus'
$script:HostProcess = $null
$script:NextRequestId = 0
$script:ObservedBinding = $null
$script:ElicitationCount = 0

# --- Mode gating: validated BEFORE the MCP host is launched or anything is read/written --------

if (-not $ProjectPath.EndsWith('.ap21', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "ProjectPath must be an explicit '.ap21' TIA Portal V21 project path. Got: '$ProjectPath'."
}
if (-not [System.IO.Path]::IsPathFullyQualified($ProjectPath) -or
    -not [string]::Equals([System.IO.Path]::GetFullPath($ProjectPath), $ProjectPath,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "ProjectPath must be an absolute canonical path to the approved project. Got: '$ProjectPath'."
}

if ($Mode -ne 'Inventory') {
    if ([string]::IsNullOrWhiteSpace($ConnectedEthernetSubnetId) -or [string]::IsNullOrWhiteSpace($ConnectedProfibusSubnetId)) {
        throw "Mode '$Mode' requires -ConnectedEthernetSubnetId and -ConnectedProfibusSubnetId -- the exact existing subnetId values of the caller-supplied connected subnets. A name is never accepted here."
    }
}

if ($Mode -eq 'Apply') {
    if (-not $AllowMutation) {
        throw "Mode 'Apply' additionally requires -AllowMutation. This switch exists so Apply can never run by accident."
    }
    if ($Acknowledgement -cne $script:RequiredAcknowledgement) {
        throw "Mode 'Apply' requires the EXACT acknowledgement string '$script:RequiredAcknowledgement' passed via -Acknowledgement. There is no shortcut and no default-yes value."
    }
}

# --- Minimal real MCP JSON-RPC client over the host's stdio, reused from live-test-network-phase2.ps1 -------

function Start-McpHost {
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $hostProject = Join-Path $script:RepositoryRoot 'TiaMcpServer/TiaMcpServer.csproj'
    $psi.FileName = 'dotnet'
    $psi.WorkingDirectory = $script:RepositoryRoot
    [void] $psi.ArgumentList.Add('run')
    [void] $psi.ArgumentList.Add('--project')
    [void] $psi.ArgumentList.Add($hostProject)
    [void] $psi.ArgumentList.Add('--')
    [void] $psi.ArgumentList.Add('--project')
    [void] $psi.ArgumentList.Add($ProjectPath)
    [void] $psi.ArgumentList.Add('--access-mode')
    [void] $psi.ArgumentList.Add($AccessMode)
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $false
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $psi

    # Inherit stderr so host logs remain visible without a PowerShell callback on a thread-pool thread.
    [void] $process.Start()
    $script:HostProcess = $process
    return $process
}

function Stop-McpHost {
    if ($script:HostProcess -and -not $script:HostProcess.HasExited) {
        try { $script:HostProcess.StandardInput.Close() } catch { }
        if (-not $script:HostProcess.WaitForExit(5000)) {
            $script:HostProcess.Kill($true)
        }
    }
}

function Send-McpMessage {
    param([hashtable] $Message)
    $json = $Message | ConvertTo-Json -Compress -Depth 20
    $script:HostProcess.StandardInput.WriteLine($json)
    $script:HostProcess.StandardInput.Flush()
}

function Read-McpResponse {
    param([int] $Id, [int] $TimeoutSeconds = $StartupTimeoutSeconds)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    # A bare synchronous ReadLine() blocks this thread until a line arrives, so the deadline below
    # would never get a chance to fire while a read is in flight. Reading asynchronously and
    # waiting on the returned Task with a per-iteration timeout lets the loop recheck the deadline
    # (and whether the host has exited) instead of blocking forever on a hung host. The same
    # pending Task is reused across iterations rather than starting a new ReadLineAsync() call
    # every loop -- issuing a second overlapping read before the first completes is not a safe
    # StreamReader usage.
    $pendingReadTask = $null
    while ((Get-Date) -lt $deadline) {
        if ($script:HostProcess.HasExited) {
            throw "The MCP host process exited (code $($script:HostProcess.ExitCode)) before responding to request id $Id."
        }

        if ($null -eq $pendingReadTask) {
            $pendingReadTask = $script:HostProcess.StandardOutput.ReadLineAsync()
        }

        $remainingMs = [int] [Math]::Max(0, ($deadline - (Get-Date)).TotalMilliseconds)
        if (-not $pendingReadTask.Wait($remainingMs)) {
            # No line arrived within this iteration's remaining budget -- loop again and recheck
            # the deadline; the same pending read is kept for the next iteration.
            continue
        }

        $line = $pendingReadTask.Result
        $pendingReadTask = $null
        if ($null -eq $line -or [string]::IsNullOrWhiteSpace($line)) { continue }

        try { $parsed = $line | ConvertFrom-Json -Depth 60 } catch { continue }

        if ($null -ne $parsed.PSObject.Properties['method'] -and $parsed.method -eq 'elicitation/create') {
            $script:ElicitationCount++
            throw 'Unexpected server elicitation from Network acceptance.'
        }
        if ($null -ne $parsed.PSObject.Properties['id'] -and $parsed.id -eq $Id) {
            return $parsed
        }
        # Anything else (a notification, or a response to an id we are not waiting for) is
        # ignored -- this harness only ever has one request in flight at a time.
    }
    throw "Timed out waiting $TimeoutSeconds second(s) for a response to request id $Id."
}

function Invoke-McpRequest {
    param([string] $Method, [hashtable] $Params = @{})
    $id = ++$script:NextRequestId
    Send-McpMessage -Message @{ jsonrpc = '2.0'; id = $id; method = $Method; params = $Params }
    $response = Read-McpResponse -Id $id
    if ($response.PSObject.Properties.Match('error').Count -gt 0 -and $null -ne $response.error) {
        throw "MCP request '$Method' (id $id) returned a protocol error: $($response.error | ConvertTo-Json -Compress -Depth 10)"
    }
    return $response.result
}

function Invoke-McpNotification {
    param([string] $Method, [hashtable] $Params = @{})
    Send-McpMessage -Message @{ jsonrpc = '2.0'; method = $Method; params = $Params }
}

function Connect-McpHost {
    Start-McpHost | Out-Null

    # The real initialize/list/call sequence a genuine MCP client performs -- this is what makes
    # this harness a proof of the PUBLIC protocol, not direct worker IPC.
    $initializeResult = Invoke-McpRequest -Method 'initialize' -Params @{
        protocolVersion = '2025-06-18'
        capabilities    = @{}
        clientInfo      = @{ name = 'live-test-network-phase4-subnets'; version = '1.0.0' }
    }
    Invoke-McpNotification -Method 'notifications/initialized'

    $tools = Invoke-McpRequest -Method 'tools/list'
    $toolNames = @($tools.tools | ForEach-Object { $_.name })
    foreach ($required in @('network_read', 'network_write', 'get_project_status', 'bind_project')) {
        if ($toolNames -notcontains $required) {
            throw "The connected MCP host does not advertise '$required'. Advertised tools: $($toolNames -join ', ')."
        }
    }

    # A status read never binds or opens. Refuse a mismatch before session selection.
    Get-ObservedProjectStatus | Out-Null
    $bound = Invoke-McpToolCall -Name 'bind_project' -Arguments @{ projectPath = $ProjectPath }
    if (-not $bound.success -or $bound.result.status -ne 'succeeded' -or
        $bound.result.value.binding.state -ne 'verified' -or
        -not [string]::Equals($bound.result.value.binding.projectPath, $ProjectPath,
            [System.StringComparison]::OrdinalIgnoreCase) -or $bound.result.value.binding.portalProcessId -le 0) {
        throw 'bind_project did not verify the exact already-open fixture.'
    }
    $script:ObservedBinding = $bound.result.value.binding
    Write-Host "Connected to MCP host: $($initializeResult.serverInfo.name) $($initializeResult.serverInfo.version)"
    return $initializeResult
}

function Get-ToolResultIsError {
    param([object] $Result)
    $property = $Result.PSObject.Properties['isError']
    if ($null -eq $property) { return $false }
    return [bool] $property.Value
}

function Get-ToolResultStructuredContent {
    param([object] $Result)
    $property = $Result.PSObject.Properties['structuredContent']
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Invoke-McpToolCall {
    param([string] $Name, [hashtable] $Arguments)
    $result = Invoke-McpRequest -Method 'tools/call' -Params @{ name = $Name; arguments = $Arguments }
    if ($null -eq $result) {
        throw "Tool '$Name' returned no result for 'tools/call'."
    }
    if (Get-ToolResultIsError -Result $result) {
        throw "Tool '$Name' returned isError:true -- $($result.content[0].text)"
    }
    # network_read/network_write declare structuredContent identical to the text block; prefer it
    # directly. Plain-string tools (get_project_status) have no structuredContent, so fall back to
    # parsing their text block.
    $structuredContent = Get-ToolResultStructuredContent -Result $result
    if ($null -ne $structuredContent) { return $structuredContent }
    return ($result.content[0].text | ConvertFrom-Json -Depth 60)
}

function Invoke-McpToolCallExpectingError {
    # Invoke-McpToolCall throws on isError:true, which is the wrong helper for a call that is
    # EXPECTED to fail (the three non-mutating negative Preview cases below). This bypasses that
    # throwing wrapper, asserts the call actually failed (throwing if it unexpectedly succeeded --
    # that would mean validation regressed), and returns the parsed error envelope using the same
    # structuredContent-preferred / text-block-fallback logic as Invoke-McpToolCall.
    param([string] $Name, [hashtable] $Arguments)
    $result = Invoke-McpRequest -Method 'tools/call' -Params @{ name = $Name; arguments = $Arguments }
    if ($null -eq $result) {
        throw "Tool '$Name' returned no result for 'tools/call'."
    }
    if (-not (Get-ToolResultIsError -Result $result)) {
        throw "Tool '$Name' was expected to return isError:true for this negative case but succeeded instead -- this likely means validation regressed."
    }
    $structuredContent = Get-ToolResultStructuredContent -Result $result
    if ($null -ne $structuredContent) { return $structuredContent }
    return ($result.content[0].text | ConvertFrom-Json -Depth 60)
}

# --- Provenance -----------------------------------------------------------------------------

function Assert-FrozenCandidate {
    param(
        [Parameter(Mandatory)] [string] $ExpectedCommit,
        [Parameter(Mandatory)] [string] $ExpectedTree,
        [Parameter(Mandatory)] [string] $ExpectedHarnessSha256
    )

    $testedCommit = (& git -C $script:RepositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($testedCommit)) {
        throw 'Could not resolve the current Git commit.'
    }
    $testedTree = (& git -C $script:RepositoryRoot rev-parse 'HEAD^{tree}').Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($testedTree)) {
        throw 'Could not resolve the current Git tree.'
    }
    $testedHarnessSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    if ($testedCommit -cne $ExpectedCommit -or $testedTree -cne $ExpectedTree -or
        $testedHarnessSha256 -ine $ExpectedHarnessSha256) {
        throw 'Current source or harness does not match the frozen guarded Network candidate.'
    }

    $candidatePaths = @(
        'TiaMcpServer',
        'TiaMcpServer.Contracts',
        'TiaMcpServer.OpennessWorker',
        'TiaMcpServer.FakeWorker',
        'TiaMcpServer.Tests',
        'scripts/live-test-network-phase4-subnets.ps1'
    )
    $dirty = @(& git -C $script:RepositoryRoot status --porcelain=v1 `
        --untracked-files=all -- @candidatePaths)
    if ($LASTEXITCODE -ne 0) { throw 'Could not verify the frozen candidate worktree.' }
    if ($dirty.Count -ne 0) {
        throw "Frozen candidate paths are dirty:`n$($dirty -join "`n")"
    }

    [ordered]@{
        testedCommit = $testedCommit
        testedTree = $testedTree
        testedHarnessSha256 = $testedHarnessSha256.ToLowerInvariant()
    }
}

function Get-ServerCommit {
    try {
        $commit = (& git -C $script:RepositoryRoot rev-parse HEAD 2>$null)
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($commit)) {
            return $commit.Trim().ToLowerInvariant()
        }
    }
    catch {
        return $null
    }
    return $null
}

function Get-ObservedProjectStatus {
    $envelope = Invoke-McpToolCall -Name 'get_project_status' -Arguments @{ projectPath = $ProjectPath }
    if (-not $envelope.success -or $null -ne $envelope.error -or $envelope.result.status -ne 'succeeded') {
        throw 'get_project_status did not deliver successful canonical evidence.'
    }
    $project = $envelope.result.value
    if ($project.isOpen -isnot [bool] -or -not $project.isOpen -or
        $project.isModified -isnot [bool]) {
        throw 'get_project_status must report an open project and a boolean isModified state.'
    }
    if (-not [System.IO.Path]::IsPathFullyQualified($ProjectPath)) {
        throw 'The requested project path must be absolute.'
    }
    $expectedPath = [System.IO.Path]::GetFullPath($ProjectPath)
    foreach ($observedPath in @($project.path)) {
        if ([string]::IsNullOrWhiteSpace($observedPath) -or
            -not [System.IO.Path]::IsPathFullyQualified($observedPath) -or
            -not [string]::Equals([System.IO.Path]::GetFullPath($observedPath), $expectedPath,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw 'Observed project/session path does not match the requested project.'
        }
    }
    $versionProperty = $project.PSObject.Properties['version']
    $projectVersion = if ($null -eq $versionProperty -or
        [string]::IsNullOrWhiteSpace($versionProperty.Value)) { $null } else { $versionProperty.Value }
    [ordered]@{
        projectPath     = $project.path
        binding         = $script:ObservedBinding
        isModified      = $project.isModified
        projectVersion  = $projectVersion
    }
}

# --- network_read / network_write operations -------------------------------------------------

function Read-HardwareConfig {
    $devices = [System.Collections.Generic.List[object]]::new()
    $subnets = [System.Collections.Generic.List[object]]::new()
    $messages = [System.Collections.Generic.List[object]]::new()
    $seenCursors = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $cursor = $null
    $expectedDevices = $null
    $expectedSubnets = $null
    do {
        $operation = @{ operationId = 'read'; operation = 'read_hardware_config'; projectPath = $ProjectPath; pageSize = 50 }
        if ($null -ne $cursor) { $operation.cursor = $cursor }
        $response = Invoke-McpToolCall -Name 'network_read' -Arguments @{ operations = @($operation) }
        $items = @($response.batch.operations)
        if ($items.Count -ne 1 -or $null -eq $items[0]) {
            throw 'read_hardware_config returned no single operation result.'
        }
        $item = $items[0]
        if ($item.status -eq 'omitted') {
            throw "read_hardware_config page was omitted: $($item.omission | ConvertTo-Json -Compress -Depth 20)"
        }
        if ($item.status -eq 'failed') {
            throw "read_hardware_config page failed: $($item.failure | ConvertTo-Json -Compress -Depth 20)"
        }
        if ($item.status -ne 'succeeded' -or $null -eq $item.result) {
            throw "read_hardware_config page did not succeed (status '$($item.status)')."
        }
        $page = $item.result
        $pagination = $page.pagination
        if ($null -eq $pagination -or $page.devices -isnot [array] -or $page.subnets -isnot [array] -or
            $page.messages -isnot [array]) {
            throw 'read_hardware_config returned a malformed paged result.'
        }
        foreach ($field in @('totalDevices', 'totalSubnets', 'returnedDevices', 'returnedSubnets')) {
            $value = $pagination.$field
            if ($value -isnot [int] -and $value -isnot [long] -or $value -lt 0) {
                throw "read_hardware_config pagination has an invalid $field."
            }
        }
        if ($pagination.returnedDevices -ne $page.devices.Count -or
            $pagination.returnedSubnets -ne $page.subnets.Count) {
            throw 'read_hardware_config returned counts do not match page entities.'
        }
        if ($null -eq $expectedDevices) {
            $expectedDevices = $pagination.totalDevices
            $expectedSubnets = $pagination.totalSubnets
        }
        elseif ($pagination.totalDevices -ne $expectedDevices -or $pagination.totalSubnets -ne $expectedSubnets) {
            throw 'read_hardware_config pagination totals changed between pages.'
        }
        if ($devices.Count + $page.devices.Count -gt $expectedDevices -or
            $subnets.Count + $page.subnets.Count -gt $expectedSubnets) {
            throw 'read_hardware_config returned more entities than its declared totals.'
        }
        foreach ($device in $page.devices) {
            if ($null -eq $device) { throw 'read_hardware_config returned a null device.' }
            $devices.Add($device)
        }
        foreach ($subnet in $page.subnets) {
            if ($null -eq $subnet) { throw 'read_hardware_config returned a null subnet.' }
            $subnets.Add($subnet)
        }
        foreach ($message in $page.messages) { $messages.Add($message) }
        # The public contract omits nextCursor entirely on a terminal page.
        $nextCursor = $null
        if ($pagination -is [System.Collections.IDictionary]) {
            if ($pagination.Contains('nextCursor')) { $nextCursor = $pagination['nextCursor'] }
        }
        else {
            $cursorProperty = $pagination.PSObject.Properties['nextCursor']
            if ($null -ne $cursorProperty) { $nextCursor = $cursorProperty.Value }
        }
        if ($null -ne $nextCursor) {
            if ($nextCursor -isnot [string] -or [string]::IsNullOrWhiteSpace($nextCursor) -or
                -not $seenCursors.Add($nextCursor) -or
                $pagination.returnedDevices + $pagination.returnedSubnets -eq 0) {
                throw 'read_hardware_config cursor did not make progress.'
            }
        }
        elseif ($devices.Count -ne $expectedDevices -or $subnets.Count -ne $expectedSubnets) {
            throw 'read_hardware_config ended before all declared entities were returned.'
        }
        $cursor = $nextCursor
    } while ($null -ne $cursor)
    return @{ devices = $devices.ToArray(); subnets = $subnets.ToArray(); messages = $messages.ToArray() }
}

function Get-HardwareNodes {
    param($Hardware)
    $nodes = [System.Collections.Generic.List[object]]::new()
    function Visit-Item($DeviceName, $Item) {
        foreach ($interface in @($Item.networkInterfaces)) {
            foreach ($node in @($interface.nodes)) {
                if ([string]::IsNullOrWhiteSpace($node.nodeId) -or
                    $null -eq $node.connectionEvidence -or -not $node.connectionEvidence.complete) {
                    throw 'Incomplete node identity/connection inventory; inspect before any mutation.'
                }
                $nodes.Add(@{ deviceName = $DeviceName; node = $node })
            }
        }
        foreach ($child in @($Item.items)) { Visit-Item $DeviceName $child }
    }
    if (@($Hardware.messages).Count -ne 0) { throw 'Hardware discovery contains degradation diagnostics.' }
    foreach ($device in @($Hardware.devices)) {
        foreach ($item in @($device.items)) { Visit-Item $device.name $item }
    }
    return $nodes.ToArray()
}
function Assert-ConnectedFixture {
    param($Hardware)
    foreach ($expected in @(@{ id = $ConnectedEthernetSubnetId; type = $script:EthernetNetworkType },
                            @{ id = $ConnectedProfibusSubnetId; type = $script:ProfibusNetworkType })) {
        $found = @($Hardware.subnets | Where-Object { $_.subnetId -ceq $expected.id })
        if ($found.Count -ne 1 -or $found[0].networkType -cne $expected.type -or
            $null -eq $found[0].connectionEvidence -or -not $found[0].connectionEvidence.complete -or
            @($found[0].connectionEvidence.nodes).Count -eq 0) {
            throw 'Exact connected Ethernet/PROFIBUS fixture identity or complete inventory was not proved.'
        }
    }
    Get-HardwareNodes $Hardware | Out-Null
}

function Get-ExistingSubnetName {
    param([Parameter(Mandatory)] [object] $Hardware, [Parameter(Mandatory)] [string] $SubnetId)
    # Named "subnetMatches" rather than PowerShell's automatic "matches" variable (the one
    # populated by the -match operator), so a future edit that also uses -match in this scope
    # cannot be silently confused by this being an ordinary local variable instead.
    $subnetMatches = @($Hardware.subnets | Where-Object { $_.subnetId -eq $SubnetId })
    if ($subnetMatches.Count -ne 1) {
        throw "Expected exactly one subnet with subnetId '$SubnetId' in the current hardware configuration; found $($subnetMatches.Count)."
    }
    return $subnetMatches[0].name
}

function New-CreateSubnetOperation {
    param(
        [Parameter(Mandatory)] [string] $OperationId,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $NetworkType,
        [int] $HighestAddress,
        [string] $TransmissionSpeed
    )
    $subnet = [ordered]@{ name = $Name; networkType = $NetworkType }
    if ($PSBoundParameters.ContainsKey('HighestAddress')) { $subnet.highestAddress = $HighestAddress }
    if (-not [string]::IsNullOrWhiteSpace($TransmissionSpeed)) { $subnet.transmissionSpeed = $TransmissionSpeed }
    [ordered]@{
        operationId = $OperationId
        operation   = 'create_subnet'
        projectPath = $ProjectPath
        subnet      = $subnet
    }
}

function New-UpdateSubnetOperation {
    param(
        [Parameter(Mandatory)] [string] $OperationId,
        [Parameter(Mandatory)] [string] $SubnetId,
        [Parameter(Mandatory)] [hashtable] $Changes
    )
    [ordered]@{
        operationId   = $OperationId
        operation     = 'update_subnet'
        projectPath   = $ProjectPath
        target        = [ordered]@{ kind = 'subnet'; subnetId = $SubnetId }
        subnetChanges = $Changes
    }
}

function New-DeleteSubnetOperation {
    param(
        [Parameter(Mandatory)] [string] $OperationId,
        [Parameter(Mandatory)] [string] $SubnetId
    )
    [ordered]@{
        operationId = $OperationId
        operation   = 'delete_subnet'
        projectPath = $ProjectPath
        target      = [ordered]@{ kind = 'subnet'; subnetId = $SubnetId }
    }
}

function Invoke-NetworkWritePreview {
    param([Parameter(Mandatory)] [object[]] $Operations)
    $response = Invoke-McpToolCall -Name 'network_write' -Arguments @{ operations = $Operations; dryRun = $true }
    if ($response.phase -ne 'preview') {
        throw "Expected phase 'preview' but got '$($response.phase)': $($response | ConvertTo-Json -Compress -Depth 20)"
    }
    if (-not $response.success -or $null -ne $response.omission) { throw 'Preview evidence incomplete; no apply.' }
    return $response
}

function Invoke-NetworkWriteApply {
    param([Parameter(Mandatory)] [object[]] $Operations)
    # Sole actual call, reachable only from the authorized Apply dispatch.
    $response = Invoke-McpToolCall -Name 'network_write' -Arguments @{ operations = $Operations; dryRun = $false }
    if ($response.phase -ne 'applied' -or $null -ne $response.error -or $response.contractVersion -ne '1.0') {
        throw 'Expected canonical applied Network response. Inspect before retry.'
    }
    if ($null -ne $response.omission) { throw 'Applied evidence omitted. Inspect before retry.' }
    return $response
}

function Get-PreviewRecord {
    param([Parameter(Mandatory)] [object] $Preview)
    return $Preview
}

function Invoke-LifecycleGroupAndVerify {
    param(
        [Parameter(Mandatory)] [string] $GroupName,
        [Parameter(Mandatory)] [object[]] $Operations,
        [Parameter(Mandatory)] [int] $TotalHardwareDeviceCountBefore,
        [Parameter(Mandatory)] [ref] $RootDeviceCount
    )

    $preview = Invoke-NetworkWritePreview -Operations $Operations
    $previewRecord = Get-PreviewRecord -Preview $preview
    if (-not $preview.success) { throw 'Preview was blocked or incomplete; no mutation attempted.' }
    $applied = Invoke-NetworkWriteApply -Operations $Operations
    if ($applied.verification.success -isnot [bool] -or -not $applied.verification.success -or
        @($applied.verification.finalChecks | Where-Object { $_.status -ne 'passed' }).Count -ne 0 -or
        @($applied.verification.operations | Where-Object { $_.status -notin @('passed', 'not_required') }).Count -ne 0) {
        throw 'Applied verification failed or is unavailable. Inspect before retry; no restoration attempted.'
    }
    if (-not $applied.success) {
        throw "network_write group '$GroupName' reported success:false: $($applied.batch | ConvertTo-Json -Compress -Depth 20)"
    }

    $results = @()
    foreach ($item in @($applied.batch.operations)) {
        if ($item.status -ne 'succeeded') {
            throw "Group '$GroupName' operation '$($item.operationId)' did not succeed: $($item.failure | ConvertTo-Json -Compress -Depth 20)"
        }

        $memberNames = @($item.result.PSObject.Properties | ForEach-Object { $_.Name } | Sort-Object)
        $expectedMemberNames = @('name', 'networkDeviceCount', 'networkDeviceCountUnchanged', 'subnetId', 'verification')
        if (Compare-Object -ReferenceObject $expectedMemberNames -DifferenceObject $memberNames) {
            throw "Group '$GroupName' operation '$($item.operationId)' result is not the typed lifecycle shape including immediate verification. Got: $($memberNames -join ', ')."
        }
        if ($item.result.networkDeviceCountUnchanged -isnot [bool] -or
            -not $item.result.networkDeviceCountUnchanged) {
            throw "Group '$GroupName' operation '$($item.operationId)' reported networkDeviceCountUnchanged:false."
        }
        if (($item.result.networkDeviceCount -isnot [int] -and
             $item.result.networkDeviceCount -isnot [long]) -or
            $item.result.networkDeviceCount -lt 0) {
            throw "Group '$GroupName' returned no nonnegative integer root device count."
        }
        if ($null -eq $RootDeviceCount.Value) {
            # First lifecycle result establishes the observed root count; never use the hardware aggregate.
            $RootDeviceCount.Value = $item.result.networkDeviceCount
        }
        elseif ($item.result.networkDeviceCount -ne $RootDeviceCount.Value) {
            throw "Group '$GroupName' reported a root device count inconsistent with earlier lifecycle results."
        }

        $results += [ordered]@{
            operationId        = $item.operationId
            operation          = $item.operation
            subnetId           = $item.result.subnetId
            name               = $item.result.name
            networkDeviceCount = $item.result.networkDeviceCount
            networkDeviceCountUnchanged = $item.result.networkDeviceCountUnchanged
        }
    }

    $postRead = Read-HardwareConfig
    $postNodes = @(Get-HardwareNodes $postRead)
    foreach ($effect in @($preview.effects)) {
        foreach ($affected in @($effect.effect.affectedNodes)) {
            $found = @($postNodes | Where-Object { $_.deviceName -ceq $affected.deviceName -and $_.node.nodeId -ceq $affected.nodeId })
            if ($found.Count -ne 1) { throw 'Affected exact device/node was not preserved in fresh grouped/ungrouped inventory.' }
            if ($effect.effect.operation -eq 'delete_subnet' -and
                ($found[0].node.connectionEvidence.subnetId -ceq $effect.effect.target.subnetId -or
                 $found[0].node.connectionEvidence.ioSystemSubnetId -ceq $effect.effect.target.subnetId)) {
                throw 'Fresh affected node still references the deleted subnet/IO tuple.'
            }
        }
    }
    $totalHardwareDeviceCountAfter = @($postRead.devices).Count
    if ($totalHardwareDeviceCountAfter -ne $TotalHardwareDeviceCountBefore) {
        throw "Group '$GroupName' changed the total hardware device count from $TotalHardwareDeviceCountBefore to $totalHardwareDeviceCountAfter."
    }

    [ordered]@{
        groupName               = $GroupName
        requestedOperations     = $Operations
        preview                 = $previewRecord
        applyResults            = $results
        postReadSubnetIds       = @($postRead.subnets | ForEach-Object { $_.subnetId })
        postReadTotalHardwareDeviceCount = $totalHardwareDeviceCountAfter
        totalHardwareDeviceCountUnchanged = $true
        rootDeviceCount = $RootDeviceCount.Value
        rootDeviceCountUnchanged = $true
    }
}

# --- Modes ------------------------------------------------------------------------------------

function Invoke-Inventory {
    $hardware = Read-HardwareConfig
    $projectStatus = Get-ObservedProjectStatus
    [ordered]@{
        mode            = 'Inventory'
        totalHardwareDeviceCount = @($hardware.devices).Count
        subnets         = @($hardware.subnets | ForEach-Object {
                [ordered]@{ subnetId = $_.subnetId; name = $_.name; networkType = $_.networkType; connectedNodeNames = @($_.ConnectedNodeNames) }
            })
        projectVersion  = $projectStatus.projectVersion
        projectStatus   = $projectStatus
    }
}

function Invoke-Preview {
    $before = Read-HardwareConfig
    Assert-ConnectedFixture $before
    $projectStatus = Get-ObservedProjectStatus

    # Harness-created ISOLATED subnets: request-derived identity only, no subnetId invented here.
    $createOperations = @(
        New-CreateSubnetOperation -OperationId 'preview-create-eth' -Name 'Phase4HarnessEthernetPreview' -NetworkType $script:EthernetNetworkType
        New-CreateSubnetOperation -OperationId 'preview-create-pb' -Name 'Phase4HarnessProfibusPreview' -NetworkType $script:ProfibusNetworkType -HighestAddress 20 -TransmissionSpeed 'Baud500000'
    )

    # Caller-supplied CONNECTED subnets: exact existing subnetId only -- a name is never accepted.
    $updateOperations = @(
        New-UpdateSubnetOperation -OperationId 'preview-update-eth' -SubnetId $ConnectedEthernetSubnetId -Changes @{ name = (Get-ExistingSubnetName -Hardware $before -SubnetId $ConnectedEthernetSubnetId) }
        New-UpdateSubnetOperation -OperationId 'preview-update-pb' -SubnetId $ConnectedProfibusSubnetId -Changes @{ name = (Get-ExistingSubnetName -Hardware $before -SubnetId $ConnectedProfibusSubnetId) }
    )
    $deleteOperations = @(
        New-DeleteSubnetOperation -OperationId 'preview-delete-connected-eth' -SubnetId $ConnectedEthernetSubnetId
        New-DeleteSubnetOperation -OperationId 'preview-delete-connected-pb' -SubnetId $ConnectedProfibusSubnetId
    )

    $createPreview = Invoke-NetworkWritePreview -Operations $createOperations
    $updatePreview = Invoke-NetworkWritePreview -Operations $updateOperations
    $deletePreview = Invoke-NetworkWritePreview -Operations $deleteOperations

    # --- Non-mutating negative-path checks -------------------------------------------------------
    # These negative dry runs never dispatch a mutation.
    $negativeCases = @()

    # 1. Invalid transmission-speed symbol -- rejected by NetworkOperationCatalog.ValidateWrite
    #    before any hardware state is even read.
    $invalidSpeedOperation = New-CreateSubnetOperation -OperationId 'preview-negative-invalid-transmission-speed' -Name 'Phase4HarnessInvalidSpeedPreview' -NetworkType $script:ProfibusNetworkType -HighestAddress 20 -TransmissionSpeed 'NotARealBaudRate'
    $invalidSpeedResult = Invoke-McpToolCallExpectingError -Name 'network_write' -Arguments @{ operations = @($invalidSpeedOperation); dryRun = $true }
    if ($invalidSpeedResult.error.category -ne 'validation_error') {
        throw "Expected error.category 'validation_error' for the invalid-transmission-speed negative case; got '$($invalidSpeedResult.error.category)'."
    }
    $negativeCases += [ordered]@{
        case          = 'invalid-transmission-speed-symbol'
        operation     = $invalidSpeedOperation
        errorCategory = $invalidSpeedResult.error.category
        errorMessage  = $invalidSpeedResult.error.message
    }

    # 2. PROFIBUS-only field on an Ethernet target -- rejected during preview's target resolution
    #    (NetworkIdentityResolver.ResolveExistingSubnet), after the hardware read but before any
    #    mutation is dispatched (ordinary discovery reads may run).
    $ethernetHighestAddressOperation = New-UpdateSubnetOperation -OperationId 'preview-negative-ethernet-highest-address' -SubnetId $ConnectedEthernetSubnetId -Changes @{ highestAddress = 10 }
    $ethernetHighestAddressResult = Invoke-McpToolCallExpectingError -Name 'network_write' -Arguments @{ operations = @($ethernetHighestAddressOperation); dryRun = $true }
    if ($ethernetHighestAddressResult.error.category -ne 'validation_error') {
        throw "Expected error.category 'validation_error' for the PROFIBUS-only-field-on-Ethernet negative case; got '$($ethernetHighestAddressResult.error.category)'."
    }
    $negativeCases += [ordered]@{
        case          = 'profibus-only-field-on-ethernet-target'
        operation     = $ethernetHighestAddressOperation
        errorCategory = $ethernetHighestAddressResult.error.category
        errorMessage  = $ethernetHighestAddressResult.error.message
    }

    # 3. Bogus subnetId, synthesized deterministically from the real connected Ethernet subnetId
    #    already read above -- defensively confirmed absent from the current hardware
    #    configuration before use. Rejected during preview's target resolution's exact-one-match
    #    check (NetworkIdentityResolver.ResolveExistingSubnet's no-match branch), which reports
    #    postcondition_failed for the established lifecycle selector category.
    $bogusSubnetId = "$ConnectedEthernetSubnetId-does-not-exist"
    if (@($before.subnets | Where-Object { $_.subnetId -eq $bogusSubnetId }).Count -ne 0) {
        throw "The synthesized bogus subnetId '$bogusSubnetId' unexpectedly collides with a real subnet in the current hardware configuration -- choose a different suffix."
    }
    $bogusSubnetOperation = New-DeleteSubnetOperation -OperationId 'preview-negative-bogus-subnet-id' -SubnetId $bogusSubnetId
    $bogusSubnetResult = Invoke-McpToolCallExpectingError -Name 'network_write' -Arguments @{ operations = @($bogusSubnetOperation); dryRun = $true }
    if ($bogusSubnetResult.error.category -ne 'postcondition_failed') {
        throw "Expected error.category 'postcondition_failed' for the bogus-subnetId negative case; got '$($bogusSubnetResult.error.category)'."
    }
    $negativeCases += [ordered]@{
        case          = 'bogus-subnet-id'
        operation     = $bogusSubnetOperation
        errorCategory = $bogusSubnetResult.error.category
        errorMessage  = $bogusSubnetResult.error.message
    }

    [ordered]@{
        mode                = 'Preview'
        totalHardwareDeviceCount = @($before.devices).Count
        projectVersion      = $projectStatus.projectVersion
        projectStatus       = $projectStatus
        requestedOperations = [ordered]@{
            create = $createOperations
            update = $updateOperations
            delete = $deleteOperations
        }
        createPreview = Get-PreviewRecord -Preview $createPreview
        updatePreview = Get-PreviewRecord -Preview $updatePreview
        deletePreview = Get-PreviewRecord -Preview $deletePreview
        negativeCases = $negativeCases
    }
}

function Invoke-Apply {
    $before = Read-HardwareConfig
    Assert-ConnectedFixture $before
    $totalHardwareDeviceCountBefore = @($before.devices).Count
    $rootDeviceCount = $null
    $projectStatus = Get-ObservedProjectStatus

    # --- Group 1: create one isolated Ethernet subnet and one isolated PROFIBUS subnet ---------
    $createOperations = @(
        New-CreateSubnetOperation -OperationId 'create-eth' -Name 'Phase4HarnessEthernet' -NetworkType $script:EthernetNetworkType
        New-CreateSubnetOperation -OperationId 'create-pb' -Name 'Phase4HarnessProfibus' -NetworkType $script:ProfibusNetworkType -HighestAddress 20 -TransmissionSpeed 'Baud500000'
    )
    $createGroup = Invoke-LifecycleGroupAndVerify -GroupName 'create-isolated-subnets' -Operations $createOperations -TotalHardwareDeviceCountBefore $totalHardwareDeviceCountBefore -RootDeviceCount ([ref]$rootDeviceCount)

    $createdEthernetId = ($createGroup.applyResults | Where-Object { $_.operationId -eq 'create-eth' }).subnetId
    $createdProfibusId = ($createGroup.applyResults | Where-Object { $_.operationId -eq 'create-pb' }).subnetId
    if ([string]::IsNullOrWhiteSpace($createdEthernetId) -or [string]::IsNullOrWhiteSpace($createdProfibusId)) {
        throw 'The create group did not report both created subnet IDs.'
    }

    # --- Group 2: update both created subnets ---------------------------------------------------
    $updateOperations = @(
        # TIA Portal enforces a 24-character subnet name limit (confirmed live: Openness'
        # Subnet.set_Name throws "the subnet name is too long. max 24 character are allowed").
        # The original "...Renamed" literals here were 28 characters and failed against a real
        # project -- these two are 23/22 characters, staying under the limit.
        New-UpdateSubnetOperation -OperationId 'update-eth' -SubnetId $createdEthernetId -Changes @{ name = 'Phase4HarnessEthRenamed' }
        New-UpdateSubnetOperation -OperationId 'update-pb' -SubnetId $createdProfibusId -Changes @{ name = 'Phase4HarnessPbRenamed'; highestAddress = 30; transmissionSpeed = 'Baud1500000' }
    )
    $updateGroup = Invoke-LifecycleGroupAndVerify -GroupName 'update-isolated-subnets' -Operations $updateOperations -TotalHardwareDeviceCountBefore $totalHardwareDeviceCountBefore -RootDeviceCount ([ref]$rootDeviceCount)

    # --- Group 3: delete the two created (isolated) subnets --------------------------------------
    $deleteIsolatedOperations = @(
        New-DeleteSubnetOperation -OperationId 'delete-created-eth' -SubnetId $createdEthernetId
        New-DeleteSubnetOperation -OperationId 'delete-created-pb' -SubnetId $createdProfibusId
    )
    $deleteIsolatedGroup = Invoke-LifecycleGroupAndVerify -GroupName 'delete-isolated-subnets' -Operations $deleteIsolatedOperations -TotalHardwareDeviceCountBefore $totalHardwareDeviceCountBefore -RootDeviceCount ([ref]$rootDeviceCount)

    if ($deleteIsolatedGroup.postReadSubnetIds -contains $createdEthernetId -or $deleteIsolatedGroup.postReadSubnetIds -contains $createdProfibusId) {
        throw 'A deleted isolated subnet ID is still present after delete-isolated-subnets.'
    }

    # --- Group 4: delete the caller-supplied connected Ethernet and PROFIBUS subnets -------------
    $deleteConnectedOperations = @(
        New-DeleteSubnetOperation -OperationId 'delete-connected-eth' -SubnetId $ConnectedEthernetSubnetId
        New-DeleteSubnetOperation -OperationId 'delete-connected-pb' -SubnetId $ConnectedProfibusSubnetId
    )
    $deleteConnectedGroup = Invoke-LifecycleGroupAndVerify -GroupName 'delete-connected-subnets' -Operations $deleteConnectedOperations -TotalHardwareDeviceCountBefore $totalHardwareDeviceCountBefore -RootDeviceCount ([ref]$rootDeviceCount)

    if ($deleteConnectedGroup.postReadSubnetIds -contains $ConnectedEthernetSubnetId -or $deleteConnectedGroup.postReadSubnetIds -contains $ConnectedProfibusSubnetId) {
        throw 'A deleted connected subnet ID is still present after delete-connected-subnets.'
    }

    [ordered]@{
        mode                     = 'Apply'
        projectVersion           = $projectStatus.projectVersion
        projectStatus            = $projectStatus
        totalHardwareDeviceCountBefore = $totalHardwareDeviceCountBefore
        createGroup              = $createGroup
        updateGroup              = $updateGroup
        deleteIsolatedGroup      = $deleteIsolatedGroup
        deleteConnectedGroup     = $deleteConnectedGroup
        finalRootDeviceCount     = $rootDeviceCount
        rootDeviceCountEvidenceSource = 'subnet lifecycle results; no independent pre-apply root count'
        rootDeviceCountUnchangedAcrossAllGroups = $true
        expectedProjectStateEffects = 'Connected deletion removes subnet/IO references while exact affected devices/nodes remain. Fresh complete node inventory checks those identities/relationships after each group. Connected deletions are not restored by this script.'
    }
}

# --- Main --------------------------------------------------------------------------------------

$candidate = Assert-FrozenCandidate -ExpectedCommit $ExpectedCommit -ExpectedTree $ExpectedTree `
    -ExpectedHarnessSha256 $ExpectedHarnessSha256
$evidence = $null
try {
    Connect-McpHost | Out-Null
    $evidence = switch ($Mode) {
        'Inventory' { Invoke-Inventory }
        'Preview'   { Invoke-Preview }
        'Apply'     { Invoke-Apply }
    }
}
finally {
    Stop-McpHost
}

if ($null -eq $evidence) {
    throw 'The selected mode produced no evidence.'
}

$evidence['mode'] = $Mode
$evidence['requestedProjectPath'] = $ProjectPath
$evidence['serverCommit'] = Get-ServerCommit
$evidence['testedCommit'] = $candidate.testedCommit
$evidence['testedTree'] = $candidate.testedTree
$evidence['testedHarnessSha256'] = $candidate.testedHarnessSha256
$evidence['accessMode'] = $AccessMode
$evidence['elicitationCount'] = $script:ElicitationCount
$evidence['generatedAtUtc'] = (Get-Date).ToUniversalTime().ToString('o')

$artifactRoot = Join-Path $script:RepositoryRoot 'artifacts/live-network-phase4'
[void] (New-Item -ItemType Directory -Force -Path $artifactRoot)
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmssfff'
$artifactName = "$timestamp-$($Mode.ToLowerInvariant()).json"
$artifactPath = Join-Path $artifactRoot $artifactName
$evidence | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $artifactPath -Encoding utf8NoBOM
[Console]::Out.WriteLine($artifactPath)
