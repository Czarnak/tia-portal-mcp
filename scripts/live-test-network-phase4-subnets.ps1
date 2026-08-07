#Requires -Version 7
<#
.SYNOPSIS
    SEPARATELY AUTHORIZED live-TIA MCP-protocol acceptance harness for the Network Operations
    Phase 4 subnet lifecycle contract (create_subnet / update_subnet / delete_subnet, exposed as
    operations of network_write).

.DESCRIPTION
    Launches the REAL TiaMcpServer MCP host (net8.0) as a child process and speaks the actual MCP
    JSON-RPC protocol over its stdio pipes -- initialize, notifications/initialized, tools/list,
    tools/call -- exactly as a real MCP client would, reusing the proven process/MCP framing
    helpers from scripts/live-test-network-phase2.ps1 rather than inventing new plumbing or
    talking to the Openness worker directly. Every mutation goes through the PUBLIC network_write
    route; this harness never calls the internal worker mutation-probe operation reserved for
    evidence fixtures and never used by the public host surface.

    Requires a running TIA Portal V21 instance with the target project already open, and requires
    PowerShell 7 (pwsh).

    -Mode Inventory (the default) never mutates the project: it only calls network_read
    (read_hardware_config) and get_project_status. -Mode Preview also never mutates: it calls
    network_write with confirm:false for a representative create/update/delete operation array and
    records the resulting target evidence and non-secret token metadata -- it never reaches a
    confirming apply call. Preview mode also exercises three non-mutating negative-path checks (an
    invalid transmission-speed symbol, a PROFIBUS-only field on an Ethernet target, and a bogus
    subnetId), confirming each is rejected with isError:true before any Openness transaction runs.
    Only -Mode Apply can change the project, and only when BOTH -AllowMutation
    is supplied AND -Acknowledgement matches the exact required string below -- there is no
    shortcut and no default-yes path.

        DELETE SUBNETS AND KEEP DEVICES

    Apply creates one isolated Ethernet subnet and one isolated PROFIBUS subnet, updates both,
    deletes both created subnets, then deletes the caller-supplied connected Ethernet and PROFIBUS
    subnetId values (-ConnectedEthernetSubnetId / -ConnectedProfibusSubnetId -- exact existing IDs
    only, names are never accepted). Every apply call reuses the unchanged ordered operations array
    and safety token returned by its own immediately preceding preview call -- nothing is rebuilt
    or retried in between. After every group this harness re-reads the hardware configuration
    through network_read and proves the root device count is unchanged and that deleted subnetId
    values are absent. Deleting the connected subnets is expected to clear subnet-related
    attributes on the retained devices as a normal TIA Portal project-state effect; this harness
    records that expectation in its output but does not read node-level attributes to confirm it,
    matching the public subnet lifecycle result, which never returns device or attribute detail.

    THIS SCRIPT IS NOT RUN BY ANY AUTOMATED TEST OR CI GATE, and its Preview/Apply modes are not
    executed as part of implementing this harness -- see
    TiaMcpServer.Tests/NetworkPhase4SubnetLiveHarnessContractTests.cs, which proves the invariants
    below by reading this file's own source text rather than executing it. Running this script
    against a real project is a separately authorized action, gated behind its own explicit
    review, independent of the review that approved writing this file.

    Output is a single timestamped JSON artifact written under artifacts/live-network-phase4/.
    Safety tokens are NEVER written to that artifact -- every recorded preview redacts the real
    token with a fixed placeholder string; the real token value lives only in a local variable for
    the immediate duration of its own apply call.

.PARAMETER ProjectPath
    Explicit absolute path to a DISPOSABLE or backed-up TIA Portal V21 .ap21 project. Required in
    every mode. Never point this at a project that matters -- Apply mode writes to it for real,
    with no automatic retry and no batch-wide rollback.

.PARAMETER Mode
    Inventory (default, non-mutating), Preview (non-mutating; previews the exact create/update/
    delete operation arrays and prints their resolved targets and non-secret token metadata), or
    Apply (mutating; requires -AllowMutation and the exact -Acknowledgement string).

.PARAMETER ConnectedEthernetSubnetId
    Exact existing subnetId of a connected Ethernet subnet, reported by a prior -Mode Inventory
    run. Required for Preview and Apply. Never a name.

.PARAMETER ConnectedProfibusSubnetId
    Exact existing subnetId of a connected PROFIBUS subnet, reported by a prior -Mode Inventory
    run. Required for Preview and Apply. Never a name.

.PARAMETER AllowMutation
    Required in addition to -Mode Apply. Without it, Apply mode is refused before anything is read
    or written.

.PARAMETER Acknowledgement
    Required in addition to -Mode Apply. Must be the exact case-sensitive string
    'DELETE SUBNETS AND KEEP DEVICES'. There is no shortcut and no default-yes value.

.PARAMETER HostExecutable
    How to launch the MCP host. Defaults to 'dotnet'.

.PARAMETER HostArguments
    Arguments passed to -HostExecutable. Defaults to running the host from source
    ('run', '--project', 'TiaMcpServer').

.PARAMETER StartupTimeoutSeconds
    Seconds to wait for each MCP response before timing out.

.EXAMPLE
    # Non-mutating: read the current hardware configuration, TIA project version, and root device
    # count through the real MCP protocol.
    pwsh -File scripts/live-test-network-phase4-subnets.ps1 -ProjectPath C:\Sandbox\Phase4Fixture.ap21

.EXAMPLE
    # Non-mutating: preview the exact create/update/delete operation arrays.
    pwsh -File scripts/live-test-network-phase4-subnets.ps1 `
        -ProjectPath C:\Sandbox\Phase4Fixture.ap21 -Mode Preview `
        -ConnectedEthernetSubnetId 590-1 -ConnectedProfibusSubnetId 590-2

.EXAMPLE
    # MUTATING. Requires -AllowMutation and the exact -Acknowledgement string. Use a disposable
    # or backed-up project.
    pwsh -File scripts/live-test-network-phase4-subnets.ps1 `
        -ProjectPath C:\Sandbox\Phase4Fixture.ap21 -Mode Apply `
        -ConnectedEthernetSubnetId 590-1 -ConnectedProfibusSubnetId 590-2 `
        -AllowMutation -Acknowledgement 'DELETE SUBNETS AND KEEP DEVICES'

.NOTES
    Use a disposable or backed-up TIA Portal V21 project. Apply mode writes to it for real; this
    harness performs no automatic retry and network_write itself performs no batch-wide rollback.
#>
[CmdletBinding()]
param(
    [ValidateSet('Inventory', 'Preview', 'Apply')]
    [string] $Mode = 'Inventory',

    [Parameter(Mandatory)] [string] $ProjectPath,

    [string] $ConnectedEthernetSubnetId,
    [string] $ConnectedProfibusSubnetId,

    [switch] $AllowMutation,
    [string] $Acknowledgement,

    [string] $HostExecutable = 'dotnet',
    [string[]] $HostArguments,
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

if (-not $HostArguments -or $HostArguments.Count -eq 0) {
    $HostArguments = @('run', '--project', 'TiaMcpServer')
}

# --- Mode gating: validated BEFORE the MCP host is launched or anything is read/written --------

if (-not $ProjectPath.EndsWith('.ap21', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "ProjectPath must be an explicit '.ap21' TIA Portal V21 project path. Got: '$ProjectPath'."
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
    $psi.FileName = $HostExecutable
    foreach ($argument in $HostArguments) { [void] $psi.ArgumentList.Add($argument) }
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
    foreach ($required in @('network_read', 'network_write', 'get_project_status')) {
        if ($toolNames -notcontains $required) {
            throw "The connected MCP host does not advertise '$required'. Advertised tools: $($toolNames -join ', ')."
        }
    }

    Write-Host "Connected to MCP host: $($initializeResult.serverInfo.name) $($initializeResult.serverInfo.version)"
    return $initializeResult
}

function Invoke-McpToolCall {
    param([string] $Name, [hashtable] $Arguments)
    $result = Invoke-McpRequest -Method 'tools/call' -Params @{ name = $Name; arguments = $Arguments }
    if ($result.isError) {
        throw "Tool '$Name' returned isError:true -- $($result.content[0].text)"
    }
    # network_read/network_write declare structuredContent identical to the text block; prefer it
    # directly. Plain-string tools (get_project_status) have no structuredContent, so fall back to
    # parsing their text block.
    if ($null -ne $result.structuredContent) { return $result.structuredContent }
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
    if (-not $result.isError) {
        throw "Tool '$Name' was expected to return isError:true for this negative case but succeeded instead -- this likely means validation regressed."
    }
    if ($null -ne $result.structuredContent) { return $result.structuredContent }
    return ($result.content[0].text | ConvertFrom-Json -Depth 60)
}

# --- Provenance -----------------------------------------------------------------------------

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

function Get-TiaVersion {
    $envelope = Invoke-McpToolCall -Name 'get_project_status' -Arguments @{ projectPath = $ProjectPath }
    if (-not $envelope.success) {
        throw "get_project_status reported failure: $($envelope.error)"
    }
    $payload = $envelope.payload | ConvertFrom-Json -Depth 40
    if ($null -eq $payload.project) {
        throw 'get_project_status returned no project metadata.'
    }
    return $payload.project.version
}

# --- network_read / network_write operations -------------------------------------------------

function Read-HardwareConfig {
    $response = Invoke-McpToolCall -Name 'network_read' -Arguments @{
        operations = @(
            @{ operationId = 'read'; operation = 'read_hardware_config'; projectPath = $ProjectPath }
        )
    }
    $item = $response.batch.operations[0]
    if ($item.status -ne 'succeeded') {
        throw "read_hardware_config did not succeed: $($item.failure | ConvertTo-Json -Compress -Depth 20)"
    }
    return $item.result
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
    $response = Invoke-McpToolCall -Name 'network_write' -Arguments @{ operations = $Operations; confirm = $false }
    if ($response.phase -ne 'preview') {
        throw "Expected phase 'preview' but got '$($response.phase)': $($response | ConvertTo-Json -Compress -Depth 20)"
    }
    return $response
}

function Invoke-NetworkWriteApply {
    param(
        [Parameter(Mandatory)] [object[]] $Operations,
        [Parameter(Mandatory)] [string] $SafetyToken
    )
    # THE ONLY confirm:$true call site in this script -- reached only from Invoke-LifecycleGroupAndVerify,
    # which is reached only when $Mode -eq 'Apply', -AllowMutation was supplied, and -Acknowledgement
    # already matched the exact required string above.
    $response = Invoke-McpToolCall -Name 'network_write' -Arguments @{
        operations  = $Operations
        confirm     = $true
        safetyToken = $SafetyToken
    }
    if ($response.phase -ne 'apply') {
        throw "Expected phase 'apply' but got '$($response.phase)': $($response | ConvertTo-Json -Compress -Depth 20)"
    }
    return $response
}

function Get-RedactedPreviewRecord {
    param([Parameter(Mandatory)] [object] $Preview)
    # The real safetyToken is NEVER placed here -- it lives only in the local variable that feeds
    # the immediately following Invoke-NetworkWriteApply call, and is never written to $evidence.
    [ordered]@{
        target              = $Preview.preview.target
        summary             = $Preview.preview.summary
        currentStateHash    = $Preview.preview.currentStateHash
        requestedInputHash  = $Preview.preview.requestedInputHash
        expiresAtUtc        = $Preview.preview.expiresAtUtc
        safetyToken         = 'REDACTED-NOT-PERSISTED'
        instructions        = $Preview.preview.instructions
    }
}

# --- Lifecycle group: preview -> apply -> verify -> post-read, using the unchanged ordered ----
# --- request and token from that same preview -------------------------------------------------

function Invoke-LifecycleGroupAndVerify {
    param(
        [Parameter(Mandatory)] [string] $GroupName,
        [Parameter(Mandatory)] [object[]] $Operations,
        [Parameter(Mandatory)] [int] $DeviceCountBefore
    )

    $preview = Invoke-NetworkWritePreview -Operations $Operations
    $redactedPreview = Get-RedactedPreviewRecord -Preview $preview
    $token = $preview.preview.safetyToken
    if ([string]::IsNullOrWhiteSpace($token)) {
        throw "Preview for group '$GroupName' returned no safety token."
    }

    # Apply reuses the SAME $Operations array and the token from THIS preview -- never rebuilt,
    # never retried.
    $applied = Invoke-NetworkWriteApply -Operations $Operations -SafetyToken $token
    if (-not $applied.success) {
        throw "network_write group '$GroupName' reported success:false: $($applied.batch | ConvertTo-Json -Compress -Depth 20)"
    }

    $results = @()
    foreach ($item in @($applied.batch.operations)) {
        if ($item.status -ne 'succeeded') {
            throw "Group '$GroupName' operation '$($item.operationId)' did not succeed: $($item.failure | ConvertTo-Json -Compress -Depth 20)"
        }

        $memberNames = @($item.result.PSObject.Properties | ForEach-Object { $_.Name } | Sort-Object)
        $expectedMemberNames = @('name', 'networkDeviceCount', 'networkDeviceCountUnchanged', 'subnetId')
        if (Compare-Object -ReferenceObject $expectedMemberNames -DifferenceObject $memberNames) {
            throw "Group '$GroupName' operation '$($item.operationId)' result is not the exact four-member minimal shape. Got: $($memberNames -join ', ')."
        }
        if (-not $item.result.networkDeviceCountUnchanged) {
            throw "Group '$GroupName' operation '$($item.operationId)' reported networkDeviceCountUnchanged:false."
        }

        $results += [ordered]@{
            operationId        = $item.operationId
            operation          = $item.operation
            subnetId           = $item.result.subnetId
            name               = $item.result.name
            networkDeviceCount = $item.result.networkDeviceCount
        }
    }

    $postRead = Read-HardwareConfig
    $deviceCountAfter = @($postRead.devices).Count
    if ($deviceCountAfter -ne $DeviceCountBefore) {
        throw "Group '$GroupName' changed the root device count from $DeviceCountBefore to $deviceCountAfter."
    }

    [ordered]@{
        groupName               = $GroupName
        requestedOperations     = $Operations
        preview                 = $redactedPreview
        applyResults            = $results
        postReadSubnetIds       = @($postRead.subnets | ForEach-Object { $_.subnetId })
        postReadRootDeviceCount = $deviceCountAfter
        rootDeviceCountUnchanged = $true
    }
}

# --- Modes ------------------------------------------------------------------------------------

function Invoke-Inventory {
    $hardware = Read-HardwareConfig
    $tiaVersion = Get-TiaVersion
    [ordered]@{
        mode            = 'Inventory'
        rootDeviceCount = @($hardware.devices).Count
        subnets         = @($hardware.subnets | ForEach-Object {
                [ordered]@{ subnetId = $_.subnetId; name = $_.name; networkType = $_.networkType }
            })
        tiaVersion      = $tiaVersion
    }
}

function Invoke-Preview {
    $before = Read-HardwareConfig
    $tiaVersion = Get-TiaVersion

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
    # Each of these three cases is rejected by the host BEFORE any Openness transaction runs
    # (validated in NetworkOperationCatalog/NetworkIdentityResolver during preview construction),
    # so calling them with confirm:false is fully safe. They stay entirely within this function,
    # confirm:false only, and never advance to a confirming apply call -- there is no safety token
    # for a request preview rejects.
    $negativeCases = @()

    # 1. Invalid transmission-speed symbol -- rejected by NetworkOperationCatalog.ValidateWrite
    #    before any hardware state is even read.
    $invalidSpeedOperation = New-CreateSubnetOperation -OperationId 'preview-negative-invalid-transmission-speed' -Name 'Phase4HarnessInvalidSpeedPreview' -NetworkType $script:ProfibusNetworkType -HighestAddress 20 -TransmissionSpeed 'NotARealBaudRate'
    $invalidSpeedResult = Invoke-McpToolCallExpectingError -Name 'network_write' -Arguments @{ operations = @($invalidSpeedOperation); confirm = $false }
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
    #    token is issued or any worker call happens.
    $ethernetHighestAddressOperation = New-UpdateSubnetOperation -OperationId 'preview-negative-ethernet-highest-address' -SubnetId $ConnectedEthernetSubnetId -Changes @{ highestAddress = 10 }
    $ethernetHighestAddressResult = Invoke-McpToolCallExpectingError -Name 'network_write' -Arguments @{ operations = @($ethernetHighestAddressOperation); confirm = $false }
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
    #    postcondition_failed, not target_not_found.
    $bogusSubnetId = "$ConnectedEthernetSubnetId-does-not-exist"
    if (@($before.subnets | Where-Object { $_.subnetId -eq $bogusSubnetId }).Count -ne 0) {
        throw "The synthesized bogus subnetId '$bogusSubnetId' unexpectedly collides with a real subnet in the current hardware configuration -- choose a different suffix."
    }
    $bogusSubnetOperation = New-DeleteSubnetOperation -OperationId 'preview-negative-bogus-subnet-id' -SubnetId $bogusSubnetId
    $bogusSubnetResult = Invoke-McpToolCallExpectingError -Name 'network_write' -Arguments @{ operations = @($bogusSubnetOperation); confirm = $false }
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
        rootDeviceCount     = @($before.devices).Count
        tiaVersion          = $tiaVersion
        requestedOperations = [ordered]@{
            create = $createOperations
            update = $updateOperations
            delete = $deleteOperations
        }
        createPreview = Get-RedactedPreviewRecord -Preview $createPreview
        updatePreview = Get-RedactedPreviewRecord -Preview $updatePreview
        deletePreview = Get-RedactedPreviewRecord -Preview $deletePreview
        negativeCases = $negativeCases
    }
}

function Invoke-Apply {
    $before = Read-HardwareConfig
    $deviceCountBefore = @($before.devices).Count
    $tiaVersion = Get-TiaVersion

    # --- Group 1: create one isolated Ethernet subnet and one isolated PROFIBUS subnet ---------
    $createOperations = @(
        New-CreateSubnetOperation -OperationId 'create-eth' -Name 'Phase4HarnessEthernet' -NetworkType $script:EthernetNetworkType
        New-CreateSubnetOperation -OperationId 'create-pb' -Name 'Phase4HarnessProfibus' -NetworkType $script:ProfibusNetworkType -HighestAddress 20 -TransmissionSpeed 'Baud500000'
    )
    $createGroup = Invoke-LifecycleGroupAndVerify -GroupName 'create-isolated-subnets' -Operations $createOperations -DeviceCountBefore $deviceCountBefore

    $createdEthernetId = ($createGroup.applyResults | Where-Object { $_.operationId -eq 'create-eth' }).subnetId
    $createdProfibusId = ($createGroup.applyResults | Where-Object { $_.operationId -eq 'create-pb' }).subnetId
    if ([string]::IsNullOrWhiteSpace($createdEthernetId) -or [string]::IsNullOrWhiteSpace($createdProfibusId)) {
        throw 'The create group did not report both created subnet IDs.'
    }

    # --- Group 2: update both created subnets ---------------------------------------------------
    $updateOperations = @(
        New-UpdateSubnetOperation -OperationId 'update-eth' -SubnetId $createdEthernetId -Changes @{ name = 'Phase4HarnessEthernetRenamed' }
        New-UpdateSubnetOperation -OperationId 'update-pb' -SubnetId $createdProfibusId -Changes @{ name = 'Phase4HarnessProfibusRenamed'; highestAddress = 30; transmissionSpeed = 'Baud1500000' }
    )
    $updateGroup = Invoke-LifecycleGroupAndVerify -GroupName 'update-isolated-subnets' -Operations $updateOperations -DeviceCountBefore $deviceCountBefore

    # --- Group 3: delete the two created (isolated) subnets --------------------------------------
    $deleteIsolatedOperations = @(
        New-DeleteSubnetOperation -OperationId 'delete-created-eth' -SubnetId $createdEthernetId
        New-DeleteSubnetOperation -OperationId 'delete-created-pb' -SubnetId $createdProfibusId
    )
    $deleteIsolatedGroup = Invoke-LifecycleGroupAndVerify -GroupName 'delete-isolated-subnets' -Operations $deleteIsolatedOperations -DeviceCountBefore $deviceCountBefore

    if ($deleteIsolatedGroup.postReadSubnetIds -contains $createdEthernetId -or $deleteIsolatedGroup.postReadSubnetIds -contains $createdProfibusId) {
        throw 'A deleted isolated subnet ID is still present after delete-isolated-subnets.'
    }

    # --- Group 4: delete the caller-supplied connected Ethernet and PROFIBUS subnets -------------
    $deleteConnectedOperations = @(
        New-DeleteSubnetOperation -OperationId 'delete-connected-eth' -SubnetId $ConnectedEthernetSubnetId
        New-DeleteSubnetOperation -OperationId 'delete-connected-pb' -SubnetId $ConnectedProfibusSubnetId
    )
    $deleteConnectedGroup = Invoke-LifecycleGroupAndVerify -GroupName 'delete-connected-subnets' -Operations $deleteConnectedOperations -DeviceCountBefore $deviceCountBefore

    if ($deleteConnectedGroup.postReadSubnetIds -contains $ConnectedEthernetSubnetId -or $deleteConnectedGroup.postReadSubnetIds -contains $ConnectedProfibusSubnetId) {
        throw 'A deleted connected subnet ID is still present after delete-connected-subnets.'
    }
    if ($deleteConnectedGroup.postReadRootDeviceCount -ne $deviceCountBefore) {
        throw 'Deleting the connected subnets changed the root device count -- connected subnet deletion must never remove devices.'
    }

    [ordered]@{
        mode                     = 'Apply'
        tiaVersion               = $tiaVersion
        deviceCountBefore        = $deviceCountBefore
        createGroup              = $createGroup
        updateGroup              = $updateGroup
        deleteIsolatedGroup      = $deleteIsolatedGroup
        deleteConnectedGroup     = $deleteConnectedGroup
        finalRootDeviceCount     = $deleteConnectedGroup.postReadRootDeviceCount
        rootDeviceCountUnchangedAcrossAllGroups = $true
        expectedProjectStateEffects = 'Deleting the connected Ethernet and PROFIBUS subnets may clear subnet-related node and IO-system attributes on the retained devices as a normal TIA Portal project-state effect. The public subnet lifecycle result never returns those attributes; this harness records the expectation here only and does not read node-level attributes to confirm it.'
    }
}

# --- Main --------------------------------------------------------------------------------------

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
$evidence['projectPath'] = $ProjectPath
$evidence['serverCommit'] = Get-ServerCommit
$evidence['generatedAtUtc'] = (Get-Date).ToUniversalTime().ToString('o')

$artifactRoot = Join-Path $script:RepositoryRoot 'artifacts'
$artifactRoot = Join-Path $artifactRoot 'live-network-phase4'
[void] (New-Item -ItemType Directory -Force -Path $artifactRoot)
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmssfff'
$artifactName = "$timestamp-$($Mode.ToLowerInvariant()).json"
$artifactPath = Join-Path $artifactRoot $artifactName
$evidence | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $artifactPath -Encoding utf8NoBOM
[Console]::Out.WriteLine($artifactPath)
