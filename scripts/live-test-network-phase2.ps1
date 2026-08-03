#Requires -Version 7
<#
.SYNOPSIS
    SEPARATELY AUTHORIZED live-TIA MCP-protocol acceptance harness for the Network Operations
    Phase 2 JSON contract (network_read / network_write).

.DESCRIPTION
    Launches the REAL TiaMcpServer MCP host (net8.0) as a child process and speaks the actual
    MCP JSON-RPC protocol over its stdio pipes -- initialize, notifications/initialized,
    tools/list, tools/call -- exactly as a real MCP client would. This is deliberately NOT the
    worker-only pattern used by scripts/live-test-db.ps1 and scripts/live-test-udt.ps1: Task 8 of
    the Network Operations Phase 2 plan requires proving the PUBLIC protocol surface (the
    advertised output schema, the single canonical JSON document identical in `content` and
    `structuredContent`, the preview/apply safety envelope), which direct worker IPC cannot see.

    Requires a running TIA Portal V21 instance with the target project already open, and
    requires PowerShell 7 (pwsh) -- the MCP host is a .NET 8 process and the JSON-RPC plumbing
    below assumes PowerShell 7's runtime.

    THIS SCRIPT IS NOT RUN BY ANY AUTOMATED TEST OR CI GATE. Per the Network Operations Phase 2
    plan's Global Constraints: "A compile, stub build, FakeWorker run, or contract test is not
    evidence of live TIA behavior. Run the live harness only under the separate authorization
    gate." Running this script against a real project is a deliberate, separately authorized
    action -- never invoke it from an ordinary test run (see
    TiaMcpServer.Tests/NetworkLiveHarnessContractTests.cs, which proves exactly that by reading
    this file's text rather than executing it).

    -Mode Read (the default) and -Mode Preview never mutate the project: Read only calls
    network_read; Preview calls network_write with confirm:false, which the write-safety model
    guarantees changes nothing (see docs/ARCHITECTURE.md, "Write safety"). Only -Mode Apply can
    change the project, and only when ALSO given -AllowApply. Before touching anything, Apply
    mode prints the exact device/node identity and the exact new value(s) it is about to write,
    then requires a typed interactive confirmation -- unless -ConfirmApplyForCi is supplied,
    which exists ONLY for an authorized, disposable-project CI job and must never be used against
    a project that matters.

    Apply performs an explicit post-read after applying and compares every node's canonical JSON
    signature before vs. after: the selected node must have changed, and every OTHER node must be
    byte-for-byte identical. This is the same evidence Task 7's
    NetworkWrite_MultiHomedFlow_ReadSelectPreviewApplyRead_... protocol test proves against the
    FakeWorker -- this script proves it against real TIA Portal V21.

.PARAMETER ProjectPath
    Obvious placeholder in the examples below: absolute path to a DISPOSABLE or backed-up TIA
    Portal V21 .ap21 project, e.g. C:\Sandbox\NetworkPhase2Acceptance.ap21. Never point this at a
    production project -- Apply mode writes to it for real, with no rollback.

.PARAMETER Mode
    Read (default, non-mutating), Preview (non-mutating; previews a configure_network_device
    write and prints the resolved target and safety token), or Apply (mutating; requires
    -AllowApply and interactive confirmation).

.PARAMETER DeviceName
    Exact existing device name to target. Required for Preview and Apply. Obvious placeholder:
    'PLC_1'. Read it from a prior -Mode Read run's hardware-config output; this script never
    guesses a device by partial name, and neither does network_write.

.PARAMETER NodeId
    Exact nodeId reported by a prior -Mode Read run for the node to configure -- required
    because a device may expose more than one interface/node (see the multi-homed example in
    docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md). Obvious placeholder:
    'node-placeholder-from-read-hardware-config'.

.PARAMETER NewIpAddress
    New IP address for the targeted node. At least one of NewIpAddress/NewSubnetMask/
    NewPnDeviceName is required for Preview/Apply. Obvious placeholder: 192.0.2.99 -- 192.0.2.0/24
    is the IANA TEST-NET-1 documentation range; pick a real value from your own subnet plan
    before actually applying against your sandbox project.

.PARAMETER NewSubnetMask
    New subnet mask for the targeted node. Obvious placeholder: 255.255.255.0.

.PARAMETER NewPnDeviceName
    New PROFINET device name for the targeted node. Obvious placeholder: plc-placeholder.

.PARAMETER AllowApply
    Required in addition to -Mode Apply. Without it, Apply mode is refused before anything is
    read or written.

.PARAMETER ConfirmApplyForCi
    CI-only bypass of the interactive "type YES" confirmation gate. Do not pass this from an
    interactive session; it exists only so an authorized, disposable-project CI job can run
    unattended.

.PARAMETER HostExecutable
    How to launch the MCP host. Defaults to 'dotnet'.

.PARAMETER HostArguments
    Arguments passed to -HostExecutable. Defaults to running the host from source
    ('run', '--project', 'TiaMcpServer'). Point these at a published 'tia-mcp' executable instead
    to test a packaged build, e.g. -HostExecutable tia-mcp -HostArguments @().

.PARAMETER StartupTimeoutSeconds
    Seconds to wait for each MCP response before timing out.

.EXAMPLE
    # Non-mutating: read the hardware configuration through the real MCP protocol.
    pwsh -File scripts/live-test-network-phase2.ps1 -ProjectPath C:\Sandbox\NetworkPhase2Acceptance.ap21

.EXAMPLE
    # Non-mutating: preview a configure_network_device write and inspect the resolved target.
    pwsh -File scripts/live-test-network-phase2.ps1 `
        -ProjectPath C:\Sandbox\NetworkPhase2Acceptance.ap21 -Mode Preview `
        -DeviceName PLC_1 -NodeId node-placeholder-from-read-hardware-config -NewIpAddress 192.0.2.99

.EXAMPLE
    # MUTATING. Requires -AllowApply and a typed "YES" confirmation. Use a disposable project.
    pwsh -File scripts/live-test-network-phase2.ps1 `
        -ProjectPath C:\Sandbox\NetworkPhase2Acceptance.ap21 -Mode Apply -AllowApply `
        -DeviceName PLC_1 -NodeId node-placeholder-from-read-hardware-config -NewIpAddress 192.0.2.99

.NOTES
    Use a disposable or backed-up TIA Portal V21 project. Apply mode writes to it for real and
    this harness performs no rollback, matching network_write's own no-rollback contract.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ProjectPath,

    [ValidateSet('Read', 'Preview', 'Apply')]
    [string] $Mode = 'Read',

    [string] $DeviceName,
    [string] $NodeId,
    [string] $NewIpAddress,
    [string] $NewSubnetMask,
    [string] $NewPnDeviceName,

    [switch] $AllowApply,
    [switch] $ConfirmApplyForCi,

    [string] $HostExecutable = 'dotnet',
    [string[]] $HostArguments,
    [int] $StartupTimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'

if (-not $HostArguments -or $HostArguments.Count -eq 0) {
    $HostArguments = @('run', '--project', 'TiaMcpServer')
}

# --- Mode gating: validated BEFORE the MCP host is launched or anything is read/written --------
if ($Mode -ne 'Read') {
    if (-not $DeviceName) {
        throw "Mode '$Mode' requires -DeviceName (the exact existing device to target)."
    }
    if (-not $NodeId) {
        throw "Mode '$Mode' requires -NodeId (the exact nodeId reported by a prior -Mode Read run)."
    }
    if (-not $NewIpAddress -and -not $NewSubnetMask -and -not $NewPnDeviceName) {
        throw "Mode '$Mode' requires at least one of -NewIpAddress, -NewSubnetMask, or -NewPnDeviceName."
    }
}

if ($Mode -eq 'Apply' -and -not $AllowApply) {
    throw "Mode 'Apply' additionally requires -AllowApply. This switch exists so Apply can never run by accident."
}

# --- Print the exact selected identity/values before doing anything ----------------------------
if ($Mode -ne 'Read') {
    Write-Host ''
    Write-Host 'About to target:' -ForegroundColor Cyan
    Write-Host "  projectPath  : $ProjectPath"
    Write-Host "  deviceName   : $DeviceName"
    Write-Host "  nodeId       : $NodeId"
    if ($NewIpAddress) { Write-Host "  ipAddress    -> $NewIpAddress" }
    if ($NewSubnetMask) { Write-Host "  subnetMask   -> $NewSubnetMask" }
    if ($NewPnDeviceName) { Write-Host "  pnDeviceName -> $NewPnDeviceName" }
    Write-Host ''
}

if ($Mode -eq 'Apply') {
    if ($ConfirmApplyForCi) {
        Write-Host 'Apply confirmed non-interactively via -ConfirmApplyForCi (CI-only path).' -ForegroundColor Yellow
    }
    else {
        $typed = Read-Host 'Type YES to APPLY this exact change to the project above'
        if ($typed -ne 'YES') {
            throw 'Apply was not confirmed. Nothing was read or written. Re-run and type YES to proceed, or pass -ConfirmApplyForCi only from an authorized, disposable-project CI job.'
        }
    }
}

# --- Minimal real MCP JSON-RPC client over the host's stdio -------------------------------------
# One JSON-RPC 2.0 message per line, matching TiaMcpServer/Program.cs's `.WithStdioServerTransport()`
# and the MCP stdio transport specification: newline-delimited, no Content-Length framing (unlike LSP).
$script:NextRequestId = 0
$script:HostProcess = $null

function Start-McpHost {
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $HostExecutable
    foreach ($arg in $HostArguments) { [void]$psi.ArgumentList.Add($arg) }
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $psi

    # Drain stderr asynchronously -- the host logs there (see Program.cs's `LogToStandardErrorThreshold`),
    # and an undrained pipe can deadlock the child process once its OS buffer fills.
    $process.add_ErrorDataReceived({
            param($sender, $e)
            if ($e.Data) { Write-Host "[host] $($e.Data)" -ForegroundColor DarkGray }
        })

    [void]$process.Start()
    $process.BeginErrorReadLine()
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
    while ((Get-Date) -lt $deadline) {
        if ($script:HostProcess.HasExited) {
            throw "The MCP host process exited (code $($script:HostProcess.ExitCode)) before responding to request id $Id."
        }

        $line = $script:HostProcess.StandardOutput.ReadLine()
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
        clientInfo      = @{ name = 'live-test-network-phase2'; version = '1.0.0' }
    }
    Invoke-McpNotification -Method 'notifications/initialized'

    $tools = Invoke-McpRequest -Method 'tools/list'
    $toolNames = @($tools.tools | ForEach-Object { $_.name })
    foreach ($required in @('network_read', 'network_write')) {
        if ($toolNames -notcontains $required) {
            throw "The connected MCP host does not advertise '$required'. Advertised tools: $($toolNames -join ', '). Is this read-only mode, or a mismatched build?"
        }
    }

    Write-Host "Connected to MCP host: $($initializeResult.serverInfo.name) $($initializeResult.serverInfo.version)"
    Write-Host "Advertised tools: $($toolNames -join ', ')"
    return $initializeResult
}

function Invoke-McpToolCall {
    param([string] $Name, [hashtable] $Arguments)
    $result = Invoke-McpRequest -Method 'tools/call' -Params @{ name = $Name; arguments = $Arguments }
    if ($result.isError) {
        throw "Tool '$Name' returned isError:true -- $($result.content[0].text)"
    }
    # network_read/network_write declare structuredContent identical to the text block (the
    # Phase 2 contract); prefer it directly rather than re-parsing the text block a second time.
    if ($null -ne $result.structuredContent) { return $result.structuredContent }
    return ($result.content[0].text | ConvertFrom-Json -Depth 60)
}

# --- Network operations --------------------------------------------------------------------
function Read-HardwareConfig {
    $response = Invoke-McpToolCall -Name 'network_read' -Arguments @{
        operations = @(
            @{ operationId = 'read'; operation = 'read_hardware_config'; projectPath = $ProjectPath }
        )
    }
    $item = $response.batch.operations[0]
    if ($item.status -ne 'succeeded') {
        throw "read_hardware_config did not succeed: $($item.failure | ConvertTo-Json -Compress -Depth 10)"
    }
    return $item.result
}

function Get-AllNodeSignatures {
    # Every (deviceName, nodeId) -> canonical node JSON in one hardware-config snapshot, used to
    # prove Apply changed only the selected node and left every other node byte-for-byte alone --
    # the same evidence NetworkStructuredProtocolTests proves against the FakeWorker.
    param($HardwareConfig)
    $signatures = @{}
    foreach ($device in @($HardwareConfig.devices)) {
        Add-NodeSignatures -Items @($device.items) -DeviceName $device.name -Signatures $signatures
    }
    return $signatures
}

function Add-NodeSignatures {
    param($Items, [string] $DeviceName, [hashtable] $Signatures)
    foreach ($item in @($Items)) {
        foreach ($iface in @($item.networkInterfaces)) {
            foreach ($node in @($iface.nodes)) {
                $key = "$DeviceName::$($node.nodeId)"
                $Signatures[$key] = ($node | ConvertTo-Json -Compress -Depth 20)
            }
        }
        Add-NodeSignatures -Items @($item.items) -DeviceName $DeviceName -Signatures $Signatures
    }
}

function Build-RequestedChanges {
    $changes = @{}
    if ($NewIpAddress) { $changes.ipAddress = $NewIpAddress }
    if ($NewSubnetMask) { $changes.subnetMask = $NewSubnetMask }
    if ($NewPnDeviceName) { $changes.pnDeviceName = $NewPnDeviceName }
    return $changes
}

function Invoke-NetworkWritePreview {
    $operations = @(
        @{
            operationId = 'configure'
            operation   = 'configure_network_device'
            projectPath = $ProjectPath
            target      = @{ deviceName = $DeviceName; nodeId = $NodeId }
            changes     = Build-RequestedChanges
        }
    )
    $response = Invoke-McpToolCall -Name 'network_write' -Arguments @{ operations = $operations; confirm = $false }
    if ($response.phase -ne 'preview') {
        throw "Expected phase 'preview' but got '$($response.phase)': $($response | ConvertTo-Json -Compress -Depth 20)"
    }
    return $response
}

function Invoke-NetworkWriteApply {
    param([string] $SafetyToken)
    # THE ONLY confirm:$true call site in this script -- reached only when $Mode -eq 'Apply',
    # -AllowApply was supplied, and the interactive/CI confirmation above already succeeded.
    $operations = @(
        @{
            operationId = 'configure'
            operation   = 'configure_network_device'
            projectPath = $ProjectPath
            target      = @{ deviceName = $DeviceName; nodeId = $NodeId }
            changes     = Build-RequestedChanges
        }
    )
    $response = Invoke-McpToolCall -Name 'network_write' -Arguments @{
        operations  = $operations
        confirm     = $true
        safetyToken = $SafetyToken
    }
    if ($response.phase -ne 'apply') {
        throw "Expected phase 'apply' but got '$($response.phase)': $($response | ConvertTo-Json -Compress -Depth 20)"
    }
    return $response
}

# --- Main ----------------------------------------------------------------------------------
Connect-McpHost | Out-Null
try {
    switch ($Mode) {
        'Read' {
            $hardware = Read-HardwareConfig
            Write-Host "Read $(@($hardware.devices).Count) device(s), $(@($hardware.subnets).Count) subnet(s)."
            $hardware | ConvertTo-Json -Depth 40
        }

        'Preview' {
            $preview = Invoke-NetworkWritePreview
            Write-Host 'Preview only -- nothing was changed.' -ForegroundColor Green
            $preview.preview.target | ConvertTo-Json -Depth 20
            Write-Host "safetyToken: $($preview.preview.safetyToken)"
        }

        'Apply' {
            $before = Read-HardwareConfig
            $beforeSignatures = Get-AllNodeSignatures -HardwareConfig $before

            $preview = Invoke-NetworkWritePreview
            $token = $preview.preview.safetyToken
            $resolvedTarget = $preview.preview.target[0]
            Write-Host "Resolved target: deviceName=$($resolvedTarget.deviceName) nodeId=$($resolvedTarget.nodeId)"

            $applied = $null
            if ($Mode -eq 'Apply') {
                $applied = Invoke-NetworkWriteApply -SafetyToken $token
            }

            if (-not $applied.success) {
                throw "network_write reported success:false. Batch: $($applied.batch | ConvertTo-Json -Compress -Depth 20)"
            }

            # --- Explicit post-read + compare selected vs. non-selected nodes ------------------
            $after = Read-HardwareConfig
            $afterSignatures = Get-AllNodeSignatures -HardwareConfig $after

            $selectedKey = "$DeviceName::$NodeId"
            if (-not $afterSignatures.ContainsKey($selectedKey)) {
                throw "Post-read no longer reports the SELECTED node ($selectedKey) at all."
            }
            if ($beforeSignatures.ContainsKey($selectedKey) -and $beforeSignatures[$selectedKey] -eq $afterSignatures[$selectedKey]) {
                throw "Post-read shows the SELECTED node ($selectedKey) unchanged after a successful apply. The write did not land."
            }
            Write-Host "Selected node ($selectedKey) changed as expected." -ForegroundColor Green

            $unexpectedChanges = @()
            foreach ($key in $beforeSignatures.Keys) {
                if ($key -eq $selectedKey) { continue }
                if (-not $afterSignatures.ContainsKey($key)) {
                    $unexpectedChanges += "$key disappeared after apply."
                    continue
                }
                if ($beforeSignatures[$key] -ne $afterSignatures[$key]) {
                    $unexpectedChanges += "$key changed after apply, but it was not the selected target."
                }
            }

            if ($unexpectedChanges.Count -gt 0) {
                throw "Apply changed node(s) other than the selected target:`n$($unexpectedChanges -join "`n")"
            }

            Write-Host 'Every other node is byte-for-byte unchanged after apply.' -ForegroundColor Green
        }
    }
}
finally {
    Stop-McpHost
}
