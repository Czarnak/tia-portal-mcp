#Requires -Version 7
<#
.SYNOPSIS
    Separately authorized public-MCP guarded Network configuration/ordered-outcome acceptance.
.DESCRIPTION
    Inventory is the default. Preview explicitly sends dryRun:true. Apply explicitly sends
    dryRun:false; omitted dryRun executes too. The harness gates are client authorization,
    not server elicitation. All modes require a frozen source and exact already-open ProjectPath.
    FixturePath is a reviewed JSON fixture containing Operations, ExpectedItemStatuses,
    ExpectedSuccess, ExpectedVerificationSuccess, ExpectedSettings, Inspections, BeforeExpected,
    AfterExpected, BeforeNodes, AfterNodes, MultiHomedDevices, RestoreOperations, BeforeRestore,
    BeforeRestoreNodes, RestorationExpected and RestorationNodes. See local-mcp-testing.md.
    Apply additionally requires AllowMutation, APPLY GUARDED NETWORK FIXTURE, exact
    AuthorizedProjectPath and AuthorizedFixtureSha256. Explicit -Restore selects only the
    concrete reviewed RestoreOperations; fresh BeforeRestore inspections and nodes precede its
    dry run and actual call. No automatic restoration after a failed or uncertain write.
    No save, close, compile, download, PLC operation, implicit open, retry or inferred undo.
    Subnet-only movement does not promise implicit IO changes. Record exact subnet/IO tuples.
    This script is never invoked by automated tests. Static source tests do not prove live behavior.
#>
[CmdletBinding()]
param(
    [ValidateSet('Inventory', 'Preview', 'Apply')] [string] $Mode = 'Inventory',
    [Parameter(Mandatory)] [string] $ProjectPath,
    [ValidateSet('read-write', 'full')] [string] $AccessMode = 'read-write',
    [Parameter(Mandatory)] [string] $FixturePath,
    [switch] $AllowMutation,
    [string] $Acknowledgement,
    [string] $AuthorizedProjectPath,
    [string] $AuthorizedFixtureSha256,
    [switch] $Restore,
    [Parameter(Mandatory)] [string] $ExpectedCommit,
    [Parameter(Mandatory)] [string] $ExpectedTree,
    [Parameter(Mandatory)] [string] $ExpectedHarnessSha256,
    [int] $StartupTimeoutSeconds = 60
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:RepositoryRoot = Split-Path -Parent $PSScriptRoot
$script:HostProcess = $null
$script:NextRequestId = 0
$script:ObservedBinding = $null
$script:ElicitationCount = 0
$script:RequiredAcknowledgement = 'APPLY GUARDED NETWORK FIXTURE'
if (-not [System.IO.Path]::IsPathFullyQualified($ProjectPath) -or
    [System.IO.Path]::GetFullPath($ProjectPath) -cne $ProjectPath -or
    -not $ProjectPath.EndsWith('.ap21', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'ProjectPath must be the exact canonical absolute approved .ap21 path.'
}
$fixtureSha256 = (Get-FileHash -LiteralPath $FixturePath -Algorithm SHA256).Hash
$fixture = Get-Content -LiteralPath $FixturePath -Raw | ConvertFrom-Json -AsHashtable -Depth 100
if ($Restore -and $Mode -ne 'Apply') { throw 'Restore requires explicitly authorized Apply mode.' }
if ($Mode -eq 'Apply') {
    if (-not $AllowMutation) { throw 'Apply requires AllowMutation.' }
    if ($Acknowledgement -cne $script:RequiredAcknowledgement -or
        $AuthorizedProjectPath -cne $ProjectPath -or
        $AuthorizedFixtureSha256 -ine $fixtureSha256) {
        throw 'Apply requires exact target, fixture hash and acknowledgement authorization.'
    }
}
$operations = if ($Restore) { @($fixture.RestoreOperations) } else { @($fixture.Operations) }
if ($operations.Count -lt 1 -or $operations.Count -gt 50) { throw 'Fixture must contain 1..50 exact operations.' }
foreach ($operation in $operations) {
    if ($operation.projectPath -cne $ProjectPath) { throw 'Fixture operation projectPath differs from approved target.' }
}
# Check each configured exact target is represented in concrete before/after node expectations.
foreach ($operation in $operations) {
    if ($operation.operation -ne 'configure_network_device') { throw 'This fixture harness accepts configuration operations only; use Phase4 for subnet lifecycle.' }
    $beforeNodes = if ($Restore) { @($fixture.BeforeRestoreNodes) } else { @($fixture.BeforeNodes) }
    $afterNodes = if ($Restore) { @($fixture.RestorationNodes) } else { @($fixture.AfterNodes) }
    foreach ($expectedNodes in @(@{ values = $beforeNodes }, @{ values = $afterNodes })) {
        $matched = @($expectedNodes.values | Where-Object { $_.deviceName -ceq $operation.target.deviceName -and $_.nodeId -ceq $operation.target.nodeId })
        if ($matched.Count -ne 1) { throw 'Each configured exact node needs concrete before/after expectations.' }
    }
}
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
        clientInfo      = @{ name = 'live-test-network-guarded-write'; version = '1.0.0' }
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
        'scripts/live-test-network-guarded-write.ps1'
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
function Assert-NodeExpectations {
    param($Hardware, [object[]] $Expected)
    $nodes = @(Get-HardwareNodes $Hardware)
    foreach ($deviceName in @($fixture.MultiHomedDevices)) {
        if (@($nodes | Where-Object { $_.deviceName -ceq $deviceName }).Count -lt 2) {
            throw 'The exact multi-homed fixture does not have at least two readable nodes.'
        }
    }
    foreach ($expectation in $Expected) {
        $found = @($nodes | Where-Object { $_.deviceName -ceq $expectation.deviceName -and $_.node.nodeId -ceq $expectation.nodeId })
        if ($found.Count -ne 1) { throw 'Expected one exact device/node identity.' }
        foreach ($key in @('subnetId', 'ioSystemSubnetId', 'ioSystemNumber')) {
            if (-not $expectation.ContainsKey($key)) { throw 'Node expectation must specify the complete exact subnet/IO tuple, including nulls.' }
            if ($found[0].node.connectionEvidence.$key -cne $expectation[$key]) { throw "Exact node relationship mismatch: $key." }
        }
        foreach ($key in @('ipAddress', 'subnetMask', 'pnDeviceName')) {
            if ($expectation.ContainsKey($key) -and $found[0].node.$key -cne $expectation[$key]) { throw "Node setting mismatch: $key." }
        }
    }
    return $nodes
}
function Assert-Subset {
    param($Observed, $Expected, [string] $Path = 'inspection')
    if ($Expected -is [System.Collections.IDictionary]) {
        foreach ($key in $Expected.Keys) {
            if ($Observed -is [System.Collections.IDictionary]) {
                if (-not $Observed.Contains($key)) { throw "Missing $Path.$key." }
                $value = $Observed[$key]
            } else {
                $member = $Observed.PSObject.Properties[$key]
                if ($null -eq $member) { throw "Missing $Path.$key." }
                $value = $member.Value
            }
            Assert-Subset $value $Expected[$key] "$Path.$key"
        }
    } elseif ($Expected -is [array]) {
        if ($Observed -isnot [array] -or $Observed.Count -ne $Expected.Count) { throw "Array mismatch at $Path." }
        for ($i = 0; $i -lt $Expected.Count; $i++) { Assert-Subset $Observed[$i] $Expected[$i] "$Path[$i]" }
    } elseif ($null -eq $Expected) {
        if ($null -ne $Observed) { throw "Expected explicit null at $Path." }
    } elseif ($Observed -cne $Expected) { throw "Evidence mismatch at $Path." }
}
function Read-FixtureIdentities {
    $observations = [ordered]@{}
    foreach ($inspection in @($fixture.Inspections)) {
        $operation = @{ operationId = $inspection.id; operation = 'inspect_network_object'; projectPath = $ProjectPath; target = $inspection.target }
        if ($inspection.ContainsKey('attributeNames')) { $operation.attributeNames = $inspection.attributeNames }
        $response = Invoke-McpToolCall -Name 'network_read' -Arguments @{ operations = @($operation) }
        $items = @($response.batch.operations)
        if (-not $response.success -or $items.Count -ne 1 -or $items[0].status -ne 'succeeded' -or
            $null -eq $items[0].result -or $null -ne $items[0].omission) { throw 'Fresh exact identity inspection failed or was omitted.' }
        Assert-Subset $items[0].result.target $inspection.target
        $observations[$inspection.id] = $items[0].result
    }
    return $observations
}
function Assert-Inspections {
    param($Observed, $Expected)
    if ($Expected.Count -eq 0) { throw 'Concrete fixture inspection expectations are required.' }
    Assert-Subset $Observed $Expected
}
function Assert-VerificationCheck {
    param($Check, [string] $Name, [string] $Expected)
    if ($null -eq $Check -or $Check.name -isnot [string] -or $Check.name -cne $Name -or
        $Check.expected -isnot [string] -or $Check.expected -cne $Expected -or
        $Check.observed -isnot [string] -or $Check.status -notin @('passed', 'failed') -or
        ($null -ne $Check.message -and $Check.message -isnot [string]) -or
        ($Check.status -eq 'passed' -and $Check.observed -cne $Expected) -or
        ($Check.status -eq 'failed' -and ($Check.observed -ceq $Expected -or [string]::IsNullOrWhiteSpace($Check.message)))) {
        throw 'Verification check is missing, unreadable, wrongly typed or contradictory; inspect before retry.'
    }
}
function Assert-Outcome {
    param($Response, [object[]] $ExpectedItemStatuses, [bool] $ExpectedSuccess, [bool] $ExpectedVerificationSuccess, [object[]] $ExpectedSettings)
    if ($Response.contractVersion -ne '1.0' -or $Response.phase -ne 'applied' -or $null -ne $Response.error -or
        $Response.success -isnot [bool] -or $Response.success -ne $ExpectedSuccess -or $null -ne $Response.omission -or
        $Response.verification.success -isnot [bool] -or $Response.verification.success -ne $ExpectedVerificationSuccess -or
        $null -ne $Response.verification.omission) { throw 'Unexpected typed applied outcome; inspect before retry.' }
    $items = @($Response.batch.operations)
    if ($items.Count -ne $operations.Count -or $items.Count -ne $ExpectedItemStatuses.Count) { throw 'Ordered outcome count mismatch.' }
    $failed = $false
    for ($i = 0; $i -lt $items.Count; $i++) {
        $item = $items[$i]
        if ($item.operationId -cne $operations[$i].operationId -or $item.status -cne $ExpectedItemStatuses[$i] -or $null -ne $item.omission) {
            throw 'Exact ordered operation identity/status or evidence delivery mismatch.'
        }
        if ($failed -and ($item.status -ne 'skipped' -or $item.skipReason -ne 'earlierOperationFailed')) { throw 'Later operation was not stopped after failure.' }
        if ($item.status -eq 'failed') { $failed = $true }
    }
    foreach ($settings in $ExpectedSettings) {
        $item = @($items | Where-Object { $_.operationId -ceq $settings.operationId })
        if ($item.Count -ne 1 -or $null -eq $item[0].result) { throw 'Expected sparse typed settings result unavailable.' }
        foreach ($field in @('appliedSettings', 'skippedSettings')) {
            $observed = $item[0].result.$field | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
            if ($observed.Count -ne $settings[$field].Count) { throw 'Unrequested settings appeared or requested sparse settings vanished.' }
            Assert-Subset $observed $settings[$field]
        }
    }
    # Require the exact attempted sequence, not just any entries the response happens to include.
    $attempted = @($items | Where-Object { $_.status -ne 'skipped' })
    $immediateChecks = $Response.verification.operations
    $finalChecks = $Response.verification.finalChecks
    if ($immediateChecks -isnot [array] -or $finalChecks -isnot [array] -or $immediateChecks.Count -ne $attempted.Count) {
        throw 'Complete immediate/final verification arrays are required.'
    }
    $requiredFinal = [System.Collections.Generic.List[object]]::new()
    $verificationPassed = $true
    for ($i = 0; $i -lt $attempted.Count; $i++) {
        $item = $attempted[$i]
        $operation = @($operations | Where-Object { $_.operationId -ceq $item.operationId })[0]
        $verification = $immediateChecks[$i]
        $evidence = $verification.evidence
        if ($verification.operationId -cne $item.operationId -or $verification.operation -cne 'configure_network_device' -or
            $item.operation -cne $verification.operation -or $null -ne $verification.omission -or $null -eq $evidence -or
            $null -eq $item.result -or $item.result.deviceName -isnot [string] -or
            -not [string]::Equals($item.result.deviceName, $operation.target.deviceName, [System.StringComparison]::OrdinalIgnoreCase) -or
            $evidence.status -cne $verification.status -or $evidence.status -notin @('passed', 'failed', 'not_required') -or
            $evidence.identity.deviceName -isnot [string] -or $evidence.identity.nodeId -isnot [string] -or
            $evidence.identity.deviceName -cne $item.result.deviceName -or $evidence.identity.nodeId -cne $operation.target.nodeId -or
            @($evidence.identity.PSObject.Properties).Count -ne 2 -or $evidence.checks -isnot [array]) {
            throw 'Exact attempted identity/order and typed immediate evidence are required.'
        }
        $resultEvidence = $item.result.verification | ConvertTo-Json -Depth 30 | ConvertFrom-Json -AsHashtable
        Assert-Subset $evidence $resultEvidence
        $requested = [System.Collections.Generic.Dictionary[string,string]]::new([System.StringComparer]::Ordinal)
        foreach ($pair in @(@('Address','ipAddress'), @('SubnetMask','subnetMask'), @('PnDeviceName','pnDeviceName'))) {
            if ($operation.changes.ContainsKey($pair[1]) -and $null -ne $operation.changes[$pair[1]]) { $requested.Add($pair[0], $operation.changes[$pair[1]]) }
        }
        if ($operation.changes.ContainsKey('subnet') -and $null -ne $operation.changes.subnet) { $requested.Add('Subnet', $operation.changes.subnet.subnetId) }
        if ($operation.changes.ContainsKey('ioSystem') -and $null -ne $operation.changes.ioSystem) { $requested.Add('IoSystem', $operation.changes.ioSystem.number.ToString([System.Globalization.CultureInfo]::InvariantCulture)) }
        $applied = $item.result.appliedSettings | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $skipped = $item.result.skippedSettings | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable
        $accounted = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($key in $skipped.Keys) {
            if (-not $requested.ContainsKey($key) -or $skipped[$key] -isnot [string] -or [string]::IsNullOrWhiteSpace($skipped[$key]) -or -not $accounted.Add($key)) { throw 'Malformed sparse skipped settings.' }
        }
        if ($skipped.Count -gt 0 -and $item.status -ne 'failed') { throw 'Requested skips must fail the item.' }
        if ($evidence.checks.Count -ne $applied.Count) { throw 'Immediate applied-setting check coverage is incomplete.' }
        $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($check in $evidence.checks) {
            if ($null -eq $check -or -not $seen.Add($check.name) -or -not $applied.Contains($check.name) -or
                -not $requested.ContainsKey($check.name) -or $applied[$check.name] -isnot [string] -or
                $applied[$check.name] -cne $requested[$check.name] -or -not $accounted.Add($check.name)) { throw 'Missing, duplicate or unexpected applied verification keys.' }
            $expected = $applied[$check.name]
            if ($check.name -ceq 'IoSystem') {
                $tuple = ConvertFrom-Json -InputObject $check.expected -NoEnumerate
                if ($tuple -isnot [array] -or $tuple.Count -ne 2 -or $tuple[0] -cne $operation.changes.ioSystem.subnetId -or
                    $tuple[1] -isnot [long] -and $tuple[1] -isnot [int] -or $tuple[1] -ne $operation.changes.ioSystem.number) { throw 'Immediate IO verification requires the exact subnet/number tuple.' }
                $expected = $check.expected
            }
            Assert-VerificationCheck $check $check.name $expected
        }
        if ($accounted.Count -ne $requested.Count) { throw 'Requested settings are missing from sparse results.' }
        $status = if ($applied.Count -eq 0) { 'not_required' } elseif (@($evidence.checks | Where-Object { $_.status -eq 'failed' }).Count) { 'failed' } else { 'passed' }
        if ($evidence.status -cne $status) { throw 'Immediate summary contradicts applied-setting evidence.' }
        if ($status -eq 'failed') { $verificationPassed = $false }
        # Configuration-only effective prefix: latest explicit same-field value supersedes; IO remains independent of Subnet.
        foreach ($field in @('exists') + @($evidence.checks | ForEach-Object { $_.name })) {
            $value = if ($field -ceq 'exists') { 'true' } else { @($evidence.checks | Where-Object { $_.name -ceq $field })[0].expected }
            $existing = @($requiredFinal | Where-Object {
                [string]::Equals($_.deviceName, $item.result.deviceName, [System.StringComparison]::OrdinalIgnoreCase) -and
                $_.nodeId -ceq $operation.target.nodeId -and $_.field -ceq $field
            })
            if ($existing.Count) { $existing[0].expected = $value }
            else { $requiredFinal.Add(@{ deviceName = $item.result.deviceName; nodeId = $operation.target.nodeId; field = $field; expected = $value }) }
        }
    }
    if ($finalChecks.Count -ne $requiredFinal.Count) { throw 'Final effective-prefix evidence is incomplete.' }
    $seenFinal = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($check in $finalChecks) {
        if ($null -eq $check -or -not $seenFinal.Add($check.name)) { throw 'Duplicate or missing final evidence.' }
        $expected = @($requiredFinal | Where-Object { $check.name -ceq "node/$($_.deviceName)/$($_.nodeId)/$($_.field)" })
        if ($expected.Count -ne 1) { throw 'Unexpected final identity/setting evidence.' }
        Assert-VerificationCheck $check $check.name $expected[0].expected
        if ($check.status -eq 'failed') { $verificationPassed = $false }
    }
    if ($Response.verification.success -ne $verificationPassed) { throw 'Verification summary contradicts immediate/final evidence.' }
}
function Invoke-NetworkWritePreview {
    $preview = Invoke-McpToolCall -Name 'network_write' -Arguments @{ operations = $operations; dryRun = $true }
    if ($preview.phase -ne 'preview' -or -not $preview.success -or $null -ne $preview.error -or
        $null -ne $preview.omission -or @($preview.effects | Where-Object { $null -ne $_.omission }).Count -ne 0) {
        throw 'Preview blocked or incomplete; no execution permitted.'
    }
    return $preview
}
function Invoke-NetworkWriteApply {
    return (Invoke-McpToolCall -Name 'network_write' -Arguments @{ operations = $operations; dryRun = $false })
}
function Invoke-LifecycleGroupAndVerify {
    $script:Evidence['preview'] = Invoke-NetworkWritePreview
    $script:Evidence['applied'] = Invoke-NetworkWriteApply
    if ($Restore) {
        Assert-Outcome $script:Evidence.applied (@($operations | ForEach-Object { 'succeeded' })) $true $true @()
    } else {
        Assert-Outcome $script:Evidence.applied $fixture.ExpectedItemStatuses $fixture.ExpectedSuccess $fixture.ExpectedVerificationSuccess $fixture.ExpectedSettings
    }
}
function Invoke-Inventory {
    $script:Evidence['hardware'] = Read-HardwareConfig
    $script:Evidence['inspections'] = Read-FixtureIdentities
}
function Invoke-Preview {
    Invoke-Inventory
    Assert-Inspections $script:Evidence.inspections $fixture.BeforeExpected
    Assert-NodeExpectations $script:Evidence.hardware $fixture.BeforeNodes | Out-Null
    $script:Evidence['preview'] = Invoke-NetworkWritePreview
}
function Invoke-Apply {
    Invoke-Inventory
    if ($Restore) {
        # Fresh exact identity inspection and concrete restoration preconditions BEFORE preview.
        Assert-Inspections $script:Evidence.inspections $fixture.BeforeRestore
        Assert-NodeExpectations $script:Evidence.hardware $fixture.BeforeRestoreNodes | Out-Null
    } else {
        Assert-Inspections $script:Evidence.inspections $fixture.BeforeExpected
        Assert-NodeExpectations $script:Evidence.hardware $fixture.BeforeNodes | Out-Null
    }
    Invoke-LifecycleGroupAndVerify
    $script:Evidence['postHardware'] = Read-HardwareConfig
    $script:Evidence['postInspections'] = Read-FixtureIdentities
    if ($Restore) {
        Assert-Inspections $script:Evidence.postInspections $fixture.RestorationExpected
        Assert-NodeExpectations $script:Evidence.postHardware $fixture.RestorationNodes | Out-Null
    } else {
        Assert-Inspections $script:Evidence.postInspections $fixture.AfterExpected
        Assert-NodeExpectations $script:Evidence.postHardware $fixture.AfterNodes | Out-Null
    }
}
$candidate = Assert-FrozenCandidate -ExpectedCommit $ExpectedCommit -ExpectedTree $ExpectedTree -ExpectedHarnessSha256 $ExpectedHarnessSha256
$script:Evidence = [ordered]@{
    mode = $Mode; restore = [bool]$Restore; requestedProjectPath = $ProjectPath; accessMode = $AccessMode
    fixtureSha256 = $fixtureSha256.ToLowerInvariant(); requestedOperations = $operations
    testedCommit = $candidate.testedCommit; testedTree = $candidate.testedTree; testedHarnessSha256 = $candidate.testedHarnessSha256
    outcome = 'started'; failure = $null
}
try {
    Connect-McpHost | Out-Null
    $script:Evidence['projectStatus'] = Get-ObservedProjectStatus
    switch ($Mode) {
        'Inventory' { Invoke-Inventory }
        'Preview' { Invoke-Preview }
        'Apply' { Invoke-Apply }
    }
    $script:Evidence.outcome = 'passed'
} catch {
    $script:Evidence.outcome = 'failed-inspect-before-retry'
    $script:Evidence.failure = $_.Exception.Message
    throw
} finally {
    Stop-McpHost
    $script:Evidence['elicitationCount'] = $script:ElicitationCount
    $script:Evidence['generatedAtUtc'] = [DateTime]::UtcNow.ToString('o')
    $artifactRoot = Join-Path $script:RepositoryRoot 'artifacts/live-network-guarded-write'
    [void](New-Item -ItemType Directory -Force -Path $artifactRoot)
    $artifactPath = Join-Path $artifactRoot ((Get-Date -Format 'yyyyMMdd-HHmmssfff') + '-' + $Mode.ToLowerInvariant() + '.json')
    $script:Evidence | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $artifactPath -Encoding utf8NoBOM
    [Console]::Out.WriteLine($artifactPath)
}
