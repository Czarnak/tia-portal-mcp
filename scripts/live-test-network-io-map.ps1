#Requires -Version 7
<#
.SYNOPSIS
    SEPARATELY AUTHORIZED live-TIA MCP-protocol acceptance harness for the structured I/O map
    (read_hardware_config with deviceName/plcName/includeIoDetails/includeTagMatches).

.DESCRIPTION
    Launches the REAL TiaMcpServer MCP host (net8.0) as a child process and speaks the actual
    MCP JSON-RPC protocol over its stdio pipes -- initialize, notifications/initialized,
    tools/list, tools/call -- exactly as a real MCP client would. It drives the public
    network_read tool with read_hardware_config and prints the structured ioDetails (addresses,
    channels, and -- when requested -- PLC tag matches).

    READ-ONLY BY CONSTRUCTION. This harness never calls a write tool, never calls the batch
    write tools, and contains no confirming call site at all. It only reads through network_read.

    Requires a running TIA Portal V21 instance with the target project already open, and
    requires PowerShell 7 (pwsh) -- the MCP host is a .NET 8 process and the JSON-RPC plumbing
    below assumes PowerShell 7's runtime.

    THIS SCRIPT IS NOT RUN BY ANY AUTOMATED TEST OR CI GATE. Per the Network Operations plans'
    Global Constraints, a compile, stub build, FakeWorker run, or contract test is not evidence
    of live TIA behavior. Running this script against a real project is a deliberate, separately
    authorized action -- never invoke it from an ordinary test run (see
    TiaMcpServer.Tests/NetworkIoMapLiveHarnessContractTests.cs, which proves exactly that by
    reading this file's text rather than executing it).

.PARAMETER ProjectPath
    Obvious placeholder in the examples below: absolute path to a TIA Portal V21 .ap21 project,
    e.g. C:\Sandbox\IoMapAcceptance.ap21. The project must already be open in TIA Portal.

.PARAMETER DeviceName
    Optional exact device filter forwarded to read_hardware_config. When omitted, the worker
    reports all devices. Obvious placeholder: 'PLC_1'.

.PARAMETER PlcName
    Optional exact PLC name used for tag matching. When omitted, tag matching uses a PLC only
    when exactly one PLC exists in the project. Obvious placeholder: 'PLC_1'.

.PARAMETER IncludeTagMatches
    When set, each channel additionally reports tagMatches against the selected PLC's tag tables.
    Requires IncludeIoDetails to also be set.

.PARAMETER HostExecutable
    How to launch the MCP host. Defaults to 'dotnet'.

.PARAMETER HostArguments
    Arguments passed to -HostExecutable. Defaults to running the host from source
    ('run', '--project', 'TiaMcpServer'). Point these at a published 'tia-mcp' executable instead
    to test a packaged build, e.g. -HostExecutable tia-mcp -HostArguments @().

.PARAMETER StartupTimeoutSeconds
    Seconds to wait for each MCP response before timing out.

.EXAMPLE
    # Non-mutating: read devices with structured I/O details and tag matches.
    pwsh -File scripts/live-test-network-io-map.ps1 `
        -ProjectPath C:\Sandbox\IoMapAcceptance.ap21 -IncludeIoDetails -IncludeTagMatches -PlcName PLC_1

.EXAMPLE
    # Non-mutating: read one device's I/O details without tag matching.
    pwsh -File scripts/live-test-network-io-map.ps1 `
        -ProjectPath C:\Sandbox\IoMapAcceptance.ap21 -IncludeIoDetails -DeviceName "ET 200SP station_1"

.NOTES
    Read-only. No project state is changed by any mode of this harness.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ProjectPath,

    [string] $DeviceName,
    [string] $PlcName,
    [switch] $IncludeIoDetails,
    [switch] $IncludeTagMatches,

    [string] $HostExecutable = 'dotnet',
    [string[]] $HostArguments,
    [int] $StartupTimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'

if ($IncludeTagMatches -and -not $IncludeIoDetails) {
    throw '-IncludeTagMatches requires -IncludeIoDetails: tag matches are attached to channels, which only exist inside ioDetails.'
}

if (-not $HostArguments -or $HostArguments.Count -eq 0) {
    $HostArguments = @('run', '--project', 'TiaMcpServer')
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
    $psi.RedirectStandardError = $false
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $psi

    # Inherit stderr so host logs remain visible without a PowerShell callback on a thread-pool thread.
    [void]$process.Start()
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

    $initializeResult = Invoke-McpRequest -Method 'initialize' -Params @{
        protocolVersion = '2025-06-18'
        capabilities    = @{}
        clientInfo      = @{ name = 'live-test-network-io-map'; version = '1.0.0' }
    }
    Invoke-McpNotification -Method 'notifications/initialized'

    $tools = Invoke-McpRequest -Method 'tools/list'
    $toolNames = @($tools.tools | ForEach-Object { $_.name })
    foreach ($required in @('network_read')) {
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
    # network_read declares structuredContent identical to the text block (the canonical JSON
    # contract); prefer it directly rather than re-parsing the text block a second time.
    if ($null -ne $result.structuredContent) { return $result.structuredContent }
    return ($result.content[0].text | ConvertFrom-Json -Depth 60)
}

# --- Read-only I/O-map operation ---------------------------------------------------------------
function Read-IoMap {
    $readOperation = @{
        operationId = 'io-map'
        operation   = 'read_hardware_config'
        projectPath = $ProjectPath
    }
    if ($DeviceName) { $readOperation.deviceName = $DeviceName }
    if ($PlcName) { $readOperation.plcName = $PlcName }
    if ($IncludeIoDetails) { $readOperation.includeIoDetails = $true }
    if ($IncludeTagMatches) { $readOperation.includeTagMatches = $true }

    $response = Invoke-McpToolCall -Name 'network_read' -Arguments @{
        operations = @($readOperation)
    }
    $item = $response.batch.operations[0]
    if ($item.status -ne 'succeeded') {
        throw "read_hardware_config did not succeed: $($item.failure | ConvertTo-Json -Compress -Depth 10)"
    }
    return $item.result
}

function Get-IoChannelCount {
    param($HardwareConfig)
    $count = 0
    foreach ($device in @($HardwareConfig.devices)) {
        Add-IoChannelCount -Items @($device.items) -Count ([ref]$count)
    }
    return $count
}

function Add-IoChannelCount {
    param($Items, [ref] $Count)
    foreach ($item in @($Items)) {
        if ($null -ne $item.ioDetails) {
            $Count.Value += @($item.ioDetails.channels).Count
        }
        Add-IoChannelCount -Items @($item.items) -Count $Count
    }
}

# --- Main --------------------------------------------------------------------------------------
Connect-McpHost | Out-Null
try {
    $hardware = Read-IoMap
    $deviceCount = @($hardware.devices).Count
    $channelCount = Get-IoChannelCount -HardwareConfig $hardware
    Write-Host "Read $deviceCount device(s), $channelCount channel(s) with I/O details."
    if ($IncludeTagMatches) {
        Write-Host 'Tag matching was requested; inspect each channel.tagMatches array below.'
    }
    $hardware | ConvertTo-Json -Depth 40
}
finally {
    Stop-McpHost
}
