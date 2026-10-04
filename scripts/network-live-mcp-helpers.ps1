#Requires -Version 7
# Shared only by the two public Network acceptance harnesses.
# Function definitions only: importing this file never starts a host or invokes a tool.
# Entrypoints own authorization/scenario policy and verify this file's frozen hash before import.

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
        clientInfo      = @{ name = $script:McpClientName; version = '1.0.0' }
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
    # Prefer the declared canonical structured document; retain text parsing as a framing fallback.
    $structuredContent = Get-ToolResultStructuredContent -Result $result
    if ($null -ne $structuredContent) { return $structuredContent }
    return ($result.content[0].text | ConvertFrom-Json -Depth 60)
}

function Assert-FrozenCandidate {
    param(
        [Parameter(Mandatory)] [string] $ExpectedCommit,
        [Parameter(Mandatory)] [string] $ExpectedTree,
        [Parameter(Mandatory)] [string] $ExpectedHarnessSha256,
        [Parameter(Mandatory)] [string] $HarnessPath,
        [Parameter(Mandatory)] [string] $ExpectedSharedHelperSha256
    )

    $testedCommit = (& git -C $script:RepositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($testedCommit)) {
        throw 'Could not resolve the current Git commit.'
    }
    $testedTree = (& git -C $script:RepositoryRoot rev-parse 'HEAD^{tree}').Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($testedTree)) {
        throw 'Could not resolve the current Git tree.'
    }
    $testedHarnessSha256 = (Get-FileHash -LiteralPath $HarnessPath -Algorithm SHA256).Hash
    $testedSharedHelperSha256 = (Get-FileHash -LiteralPath $script:SharedHelperPath -Algorithm SHA256).Hash
    if ($testedCommit -cne $ExpectedCommit -or $testedTree -cne $ExpectedTree -or
        $testedHarnessSha256 -ine $ExpectedHarnessSha256 -or $testedSharedHelperSha256 -ine $ExpectedSharedHelperSha256) {
        throw 'Current source or harness does not match the frozen guarded Network candidate.'
    }

    $candidatePaths = @(
        'TiaMcpServer',
        'TiaMcpServer.Contracts',
        'TiaMcpServer.OpennessWorker',
        'TiaMcpServer.FakeWorker',
        'TiaMcpServer.Tests',
        'scripts/live-test-network-guarded-write.ps1',
        'scripts/live-test-network-phase4-subnets.ps1',
        'scripts/network-live-mcp-helpers.ps1'
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
        testedSharedHelperSha256 = $testedSharedHelperSha256.ToLowerInvariant()
    }
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
