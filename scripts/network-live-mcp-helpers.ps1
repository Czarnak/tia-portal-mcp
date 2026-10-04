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
    param([switch] $Paged)
    $devices = [System.Collections.Generic.List[object]]::new()
    $subnets = [System.Collections.Generic.List[object]]::new()
    $messages = [System.Collections.Generic.List[object]]::new()
    $seenCursors = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $cursor = $null
    $expectedDevices = $null
    $expectedSubnets = $null
    do {
        $operation = @{ operationId = 'read'; operation = 'read_hardware_config'; projectPath = $ProjectPath }
        if ($Paged) { $operation.pageSize = 50 }
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
        $pagination = Get-NetworkMember $page 'pagination'
        if (-not $Paged) {
            if ($null -ne $pagination -or $page.devices -isnot [array] -or $page.subnets -isnot [array] -or $page.messages -isnot [array]) {
                throw 'Ordinary hardware read returned malformed or paged evidence.'
            }
            return $page
        }
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

# Read conditional members without treating omission as a value or completeness proof.
function Get-NetworkMember {
    param($Value, [string] $Name)
    if ($null -eq $Value) { return $null }
    if ($Value -is [System.Collections.IDictionary]) {
        foreach ($key in $Value.Keys) { if ($key -ceq $Name) { return ,$Value[$key] } }
    } else {
        $member = @($Value.PSObject.Properties | Where-Object { $_.Name -ceq $Name })
        if ($member.Count -eq 1) { return ,$member[0].Value }
    }
    return $null
}

function ConvertTo-NetworkInterfacePath {
    param($Path)
    # Immediate verification dictionaries carry encoded paths. Parse their fields before
    # ConvertFrom-Json could discard duplicates or coerce malformed scalar types.
    if ($Path -is [string]) {
        $document = [System.Text.Json.JsonDocument]::Parse($Path, [System.Text.Json.JsonDocumentOptions]::new())
        try {
            if ($document.RootElement.ValueKind.ToString() -cne 'Array') { throw 'Interface path must be an array.' }
            $decoded = [System.Collections.Generic.List[object]]::new()
            foreach ($element in $document.RootElement.EnumerateArray()) {
                if ($element.ValueKind.ToString() -cne 'Object') { throw 'Interface segment must be an object.' }
                $segment = [ordered]@{}
                $fields = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
                foreach ($field in $element.EnumerateObject()) {
                    if (-not $fields.Add($field.Name)) { throw 'Duplicate interface segment field.' }
                    switch -CaseSensitive ($field.Name) {
                        'positionNumber' {
                            $position = 0
                            if ($field.Value.ValueKind.ToString() -cne 'Number' -or -not $field.Value.TryGetInt32([ref]$position)) { throw 'Invalid positionNumber.' }
                            $segment[$field.Name] = $position
                        }
                        { $_ -cin @('name','typeIdentifier') } {
                            if ($field.Value.ValueKind.ToString() -cne 'String') { throw 'Invalid interface segment string.' }
                            $segment[$field.Name] = $field.Value.GetString()
                        }
                        default { throw 'Unknown interface segment field.' }
                    }
                }
                $decoded.Add($segment)
            }
            $Path = $decoded.ToArray()
        } finally { $document.Dispose() }
    }
    if ($Path -isnot [array] -or $Path.Count -eq 0) { throw 'Interface owner path must be a nonempty array.' }
    foreach ($segment in $Path) {
        if ($null -eq $segment) { throw 'Null interface segment.' }
        $fields = if ($segment -is [System.Collections.IDictionary]) { @($segment.Keys) } else { @($segment.PSObject.Properties.Name) }
        if (@($fields | Where-Object { $_ -cnotin @('name','positionNumber','typeIdentifier') }).Count -or
            'name' -cnotin $fields -or 'positionNumber' -cnotin $fields) { throw 'Unknown or missing interface segment fields.' }
        $name = Get-NetworkMember $segment 'name'; $position = Get-NetworkMember $segment 'positionNumber'
        $type = Get-NetworkMember $segment 'typeIdentifier'
        if ($name -isnot [string] -or [string]::IsNullOrWhiteSpace($name) -or
            ($position -isnot [int] -and $position -isnot [long]) -or $position -lt 0 -or $position -gt [int]::MaxValue -or
            ('typeIdentifier' -cin $fields -and ($type -isnot [string] -or [string]::IsNullOrWhiteSpace($type)))) { throw 'Invalid interface owner segment.' }
    }
    return ,$Path
}

function ConvertTo-NetworkPathJson {
    param($Path)
    $segments = ConvertTo-NetworkInterfacePath $Path
    $encoded = [System.Collections.Generic.List[string]]::new()
    foreach ($segment in $segments) {
        $name = [System.Text.Json.JsonSerializer]::Serialize((Get-NetworkMember $segment 'name'), [string], [System.Text.Json.JsonSerializerOptions]::new())
        $position = Get-NetworkMember $segment 'positionNumber'
        $text = '{"name":' + $name + ',"positionNumber":' + $position.ToString([System.Globalization.CultureInfo]::InvariantCulture)
        $type = Get-NetworkMember $segment 'typeIdentifier'
        if ($null -ne $type) { $text += ',"typeIdentifier":' + [System.Text.Json.JsonSerializer]::Serialize($type, [string], [System.Text.Json.JsonSerializerOptions]::new()) }
        $encoded.Add($text + '}')
    }
    return '[' + ($encoded -join ',') + ']'
}

function Get-NetworkNodeKey {
    param($Identity)
    $device = Get-NetworkMember $Identity 'deviceName'; $node = Get-NetworkMember $Identity 'nodeId'
    if ($device -isnot [string] -or [string]::IsNullOrWhiteSpace($device) -or $node -isnot [string] -or [string]::IsNullOrWhiteSpace($node)) { throw 'Invalid node identity.' }
    $path = Get-NetworkMember $Identity 'interfacePath'
    if ($null -eq $path) { throw 'Unqualified node identity cannot be a write evidence map key.' }
    $pairs = @()
    if ($null -ne $path) {
        foreach ($segment in (ConvertTo-NetworkInterfacePath $path)) { $pairs += ,@((Get-NetworkMember $segment 'name'), (Get-NetworkMember $segment 'positionNumber')) }
    }
    # Type and interface-name constraints are evidence, not tuple equality.
    return ConvertTo-Json -InputObject @($device.ToUpperInvariant(), $pairs, $node) -Compress -Depth 20
}

function Test-NetworkNodeIdentity {
    param($Expected, $Observed, [switch] $SelectorConstraints)
    foreach ($identity in @($Expected,$Observed)) {
        $device = Get-NetworkMember $identity 'deviceName'; $node = Get-NetworkMember $identity 'nodeId'
        if ($device -isnot [string] -or [string]::IsNullOrWhiteSpace($device) -or $node -isnot [string] -or [string]::IsNullOrWhiteSpace($node)) { throw 'Invalid node identity.' }
        $path = Get-NetworkMember $identity 'interfacePath'
        if ($null -ne $path) { $null = ConvertTo-NetworkInterfacePath $path }
    }
    if (-not [string]::Equals($Expected.deviceName, $Observed.deviceName, [System.StringComparison]::OrdinalIgnoreCase) -or $Expected.nodeId -cne $Observed.nodeId) { return $false }
    $expectedPath = Get-NetworkMember $Expected 'interfacePath'; $observedPath = Get-NetworkMember $Observed 'interfacePath'
    if ($null -ne $expectedPath) {
        if ($null -eq $observedPath) { return $false }
        $left = ConvertTo-NetworkInterfacePath $expectedPath; $right = ConvertTo-NetworkInterfacePath $observedPath
        if ($left.Count -ne $right.Count) { return $false }
        for ($i=0; $i -lt $left.Count; $i++) {
            if ((Get-NetworkMember $left[$i] 'name') -cne (Get-NetworkMember $right[$i] 'name') -or
                (Get-NetworkMember $left[$i] 'positionNumber') -ne (Get-NetworkMember $right[$i] 'positionNumber')) { return $false }
            $type = Get-NetworkMember $left[$i] 'typeIdentifier'
            if ($null -ne $type -and $type -cne (Get-NetworkMember $right[$i] 'typeIdentifier')) { return $false }
        }
    }
    $interfaceName = Get-NetworkMember $Expected 'interfaceName'
    if ($null -ne $interfaceName -and ($interfaceName -isnot [string] -or [string]::IsNullOrWhiteSpace($interfaceName) -or $interfaceName -cne (Get-NetworkMember $Observed 'interfaceName'))) { return $false }
    if ($SelectorConstraints) {
        foreach ($field in @('nodeIndex','interfaceType','interfaceOperatingMode')) {
            $constraint = Get-NetworkMember $Expected $field
            if ($null -ne $constraint -and $constraint -cne (Get-NetworkMember $Observed $field)) { return $false }
        }
        $legacyPath = Get-NetworkMember $Expected 'itemPath'
        if ($null -ne $legacyPath) {
            $actualPath = Get-NetworkMember $Observed 'itemPath'
            if ($legacyPath -isnot [array] -or $actualPath -isnot [array] -or $legacyPath.Count -ne $actualPath.Count) { return $false }
            for ($i=0; $i -lt $legacyPath.Count; $i++) {
                foreach ($field in @('index','name','positionNumber','typeIdentifier')) {
                    if ((Get-NetworkMember $legacyPath[$i] $field) -cne (Get-NetworkMember $actualPath[$i] $field)) { return $false }
                }
            }
        }
    }
    return $true
}

# Resolve source constraints from a fresh ordinary read; keep request selectors untouched.
function Resolve-NetworkNodeSelectorEvidence {
    param($Hardware, $Selector)
    $legacy = Get-NetworkMember $Selector 'itemPath'
    $nodeIndex = Get-NetworkMember $Selector 'nodeIndex'
    if ($null -eq $legacy -and $null -eq $nodeIndex) { return $Selector }
    Assert-HardwareWriteEvidence $Hardware
    $null = @(Get-HardwareNodes $Hardware)
    $devices = @($Hardware.devices | Where-Object { [string]::Equals($_.name, $Selector.deviceName, [System.StringComparison]::OrdinalIgnoreCase) })
    if ($devices.Count -ne 1) { throw 'Exact source device identity is missing or ambiguous; inspect before retry.' }
    $path = Get-NetworkMember $Selector 'interfacePath'
    if ($null -ne $legacy -and $null -ne $path) { throw 'Both node owner paths supplied.' }
    if ($null -ne $legacy) {
        if ($legacy -isnot [array] -or $legacy.Count -eq 0 -or $null -eq $nodeIndex) { throw 'Legacy source path requires a node index.' }
        $segments = $legacy
    } else { $segments = ConvertTo-NetworkInterfacePath $path }
    $siblings = $devices[0].items
    $owner = $null
    $semanticPath = @()
    foreach ($segment in $segments) {
        if ($null -ne $legacy) {
            $index = Get-NetworkMember $segment 'index'
            if (($index -isnot [int] -and $index -isnot [long]) -or $index -lt 0 -or $index -ge $siblings.Count) { throw 'Legacy source item index does not match.' }
            $owner = $siblings[$index]
            $type = Get-NetworkMember $segment 'typeIdentifier'
            if ($type -isnot [string] -or [string]::IsNullOrWhiteSpace($type)) { throw 'Legacy source type constraint is required.' }
        } else {
            if (@($siblings | Where-Object { [string]::IsNullOrWhiteSpace((Get-NetworkMember $_ 'name')) -or $null -eq (Get-NetworkMember $_ 'positionNumber') }).Count) { throw 'Owner namespace identity is unreadable; inspect before retry.' }
            $matches = @($siblings | Where-Object { (Get-NetworkMember $_ 'name') -ceq $segment.name -and (Get-NetworkMember $_ 'positionNumber') -eq $segment.positionNumber })
            if ($matches.Count -ne 1) { throw 'Exact source owner is missing or ambiguous.' }
            $owner = $matches[0]
            $type = Get-NetworkMember $segment 'typeIdentifier'
        }
        if ((Get-NetworkMember $owner 'name') -cne $segment.name -or (Get-NetworkMember $owner 'positionNumber') -cne $segment.positionNumber -or
            ($null -ne $type -and (Get-NetworkMember $owner 'typeIdentifier') -cne $type)) { throw 'Source owner name/position/type constraint does not match.' }
        $semantic = @{name=$segment.name;positionNumber=$segment.positionNumber}
        if ($null -ne $type) { $semantic.typeIdentifier=$type }
        $semanticPath += $semantic
        $siblings = $owner.items
    }
    if ($owner.networkInterfaces.Count -ne 1) { throw 'Source owner does not expose exactly one interface.' }
    $interface = $owner.networkInterfaces[0]
    $interfaceName = Get-NetworkMember $Selector 'interfaceName'
    if ($null -ne $interfaceName -and $interfaceName -cne $interface.name) { throw 'Source interface name constraint does not match.' }
    $matches = @($interface.nodes | Where-Object { $_.nodeId -ceq $Selector.nodeId })
    if ($matches.Count -ne 1) { throw 'Source interface node identity is missing or ambiguous.' }
    if ($null -ne $nodeIndex -and (($nodeIndex -isnot [int] -and $nodeIndex -isnot [long]) -or $nodeIndex -lt 0 -or
        $nodeIndex -ge $interface.nodes.Count -or -not [object]::ReferenceEquals($interface.nodes[$nodeIndex], $matches[0]))) { throw 'Source node index constraint does not match.' }
    $normalized = $Selector | ConvertTo-Json -Depth 100 | ConvertFrom-Json -AsHashtable -Depth 100
    $normalized.Remove('itemPath')
    $normalized.interfacePath = $semanticPath
    return $normalized
}
function Get-NetworkNodeCheckName {
    param($Identity, [string] $Field)
    $null = Get-NetworkNodeKey $Identity
    $path = Get-NetworkMember $Identity 'interfacePath'
    $owner = if ($null -eq $path) { '' } else { ConvertTo-NetworkPathJson $path }
    return "node/$($Identity.deviceName)/$owner/$($Identity.nodeId)/$Field"
}

function Assert-HardwareWriteEvidence {
    param($Hardware)
    $evidence = Get-NetworkMember $Hardware 'discoveryEvidence'
    if ($null -ne (Get-NetworkMember $Hardware 'pagination') -or $null -eq $evidence -or
        (Get-NetworkMember $evidence 'scope') -cne 'project' -or
        (Get-NetworkMember $evidence 'complete') -isnot [bool] -or -not $evidence.complete -or
        (Get-NetworkMember $evidence 'failures') -isnot [array] -or $evidence.failures.Count -ne 0) {
        throw 'Complete ordinary project traversal evidence required before any guarded Network call.'
    }
    # Root count is independent; lifecycle evidence validates it only where required.
}

function Assert-NetworkFixtureHash {
    param([string] $Path, [string] $ExpectedSha256)
    if ([string]::IsNullOrWhiteSpace($Path) -or [string]::IsNullOrWhiteSpace($ExpectedSha256) -or
        (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ine $ExpectedSha256) { throw 'Fixture changed since authorization; no tool call permitted.' }
}

function Get-HardwareNodes {
    param($Hardware)
    $nodes = [System.Collections.Generic.List[object]]::new()
    $keys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    function Visit-Item($DeviceName, $Item) {
        if ($null -eq $Item -or (Get-NetworkMember $Item 'networkInterfaces') -isnot [array] -or (Get-NetworkMember $Item 'items') -isnot [array]) { throw 'Incomplete required hardware item namespace.' }
        foreach ($interface in $Item.networkInterfaces) {
            if ($null -eq $interface -or (Get-NetworkMember $interface 'nodes') -isnot [array]) { throw 'Incomplete required interface node namespace.' }
            foreach ($node in $interface.nodes) {
                $selector = Get-NetworkMember $node 'selector'
                if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.nodeId) -or
                    $null -eq $node.connectionEvidence -or $node.connectionEvidence.complete -isnot [bool] -or -not $node.connectionEvidence.complete -or
                    $null -eq $selector -or $node.selectable -isnot [bool] -or -not $node.selectable -or $selector.kind -cne 'node' -or
                    -not [string]::Equals($selector.deviceName, $DeviceName, [System.StringComparison]::OrdinalIgnoreCase) -or $selector.nodeId -cne $node.nodeId) {
                    throw 'Incomplete node identity/connection inventory; inspect before any mutation.'
                }
                $path = Get-NetworkMember $selector 'interfacePath'
                if ($null -ne $path -and $path -isnot [array]) { throw 'Public node selector owner path must be an array.' }
                $identity = @{ deviceName=$DeviceName; nodeId=$node.nodeId; interfacePath=$path; interfaceName=$interface.name }
                foreach ($field in @('itemPath','nodeIndex','interfaceType','interfaceOperatingMode')) {
                    $constraint = Get-NetworkMember $selector $field
                    if ($null -ne $constraint) { $identity[$field] = $constraint }
                }
                # Unqualified legacy rows stay readable. Exact callers must prove one match;
                # a repeated bare node ID is not itself invalid hardware or a usable key.
                if (-not (Test-NetworkNodeIdentity $selector $identity -SelectorConstraints) -or
                    ($null -ne $path -and -not $keys.Add((Get-NetworkNodeKey $identity)))) { throw 'Ambiguous or inconsistent qualified node identity.' }
                # Preserve the public selector verbatim, including legacy constraints and indices.
                $nodes.Add(@{ deviceName=$DeviceName; node=$node; selector=$selector; identity=$identity })
            }
        }
        foreach ($child in $Item.items) { Visit-Item $DeviceName $child }
    }
    if ((Get-NetworkMember $Hardware 'devices') -isnot [array]) { throw 'Incomplete required device namespace.' }
    foreach ($device in $Hardware.devices) {
        if ($device.name -isnot [string] -or [string]::IsNullOrWhiteSpace($device.name) -or (Get-NetworkMember $device 'items') -isnot [array]) { throw 'Incomplete required device/item identity.' }
        foreach ($item in $device.items) { Visit-Item $device.name $item }
    }
    return $nodes.ToArray()
}
