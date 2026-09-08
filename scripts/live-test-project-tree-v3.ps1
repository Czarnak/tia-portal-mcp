[CmdletBinding()]
param(
    [string] $ProjectPath = $env:TIA_MCP_LIVE_PROJECT_PATH,
    [string] $ServerPath = (Join-Path $PSScriptRoot '..\TiaMcpServer\bin\Release\net8.0\TiaMcpServer.exe'),
    [string] $EvidencePath = (Join-Path $PSScriptRoot '..\artifacts\issue-32-live\project-tree-v3-evidence.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    throw 'Supply -ProjectPath or TIA_MCP_LIVE_PROJECT_PATH. HU00954_CPU_AA_V21 is preferred when available.'
}

$script:Server = $null
$script:StderrTask = $null
$script:NextRequestId = 0
$script:RequestTimeoutSeconds = 120
$script:StdoutLines = [System.Collections.Generic.List[string]]::new()
$script:ToolNames = @()

function Assert-Condition {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Initialize-EvidenceDirectory {
    $directory = Split-Path -Parent $EvidencePath
    if ([string]::IsNullOrWhiteSpace($directory)) {
        throw 'EvidencePath must include a parent directory.'
    }

    [void] [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetFullPath($directory))
}

function Get-EvidenceSidecarPath {
    param([Parameter(Mandatory)] [string] $Extension)

    $directory = Split-Path -Parent $EvidencePath
    $fileName = [System.IO.Path]::GetFileNameWithoutExtension($EvidencePath)
    return Join-Path $directory ($fileName + $Extension)
}

function Start-McpServer {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = (Resolve-Path -LiteralPath $ServerPath).Path
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.ArgumentList.Add('--read-only')
    $startInfo.ArgumentList.Add('--project')
    $startInfo.ArgumentList.Add((Resolve-Path -LiteralPath $ProjectPath).Path)
    $server = [System.Diagnostics.Process]::new()
    $server.StartInfo = $startInfo
    if (-not $server.Start()) { throw 'Could not start the TIA MCP server.' }

    $script:Server = $server
    $script:StderrTask = $server.StandardError.ReadToEndAsync()
}

function Stop-McpServer {
    if ($null -eq $script:Server) {
        return
    }

    try {
        if (-not $script:Server.HasExited) {
            try {
                $script:Server.StandardInput.Close()
            }
            catch {
                # Best-effort close; forced termination below remains the backstop.
            }

            if (-not $script:Server.WaitForExit(5000)) {
                $script:Server.Kill($true)
                Assert-Condition ($script:Server.WaitForExit(5000)) 'The TIA MCP server did not stop after termination.'
            }
        }
    }
    finally {
        $script:Server.Dispose()
        $script:Server = $null
    }
}

function Send-McpMessage {
    param([Parameter(Mandatory)] [object] $Message)

    $json = $Message | ConvertTo-Json -Compress -Depth 100
    $script:Server.StandardInput.WriteLine($json)
    $script:Server.StandardInput.Flush()
}

function Read-McpResponse {
    param([Parameter(Mandatory)] [int] $Id)

    $deadline = (Get-Date).AddSeconds($script:RequestTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ($script:Server.HasExited) {
            throw "The TIA MCP server exited with code $($script:Server.ExitCode) before responding to request id $Id."
        }

        $remaining = $deadline - (Get-Date)
        $line = $script:Server.StandardOutput.ReadLineAsync().WaitAsync($remaining).GetAwaiter().GetResult()
        if ($null -eq $line) {
            throw "The TIA MCP server closed stdout before responding to request id $Id."
        }

        $script:StdoutLines.Add($line)
        $parsed = $line | ConvertFrom-Json -Depth 100
        $idProperty = $parsed.PSObject.Properties['id']
        if (($null -eq $idProperty) -or ([int] $idProperty.Value -ne $Id)) {
            continue
        }

        $errorProperty = $parsed.PSObject.Properties['error']
        if ($null -ne $errorProperty) {
            throw "MCP request id $Id failed: $($errorProperty.Value | ConvertTo-Json -Compress -Depth 100)"
        }

        return [pscustomobject]@{ Parsed = $parsed; Raw = $line }
    }

    throw "Timed out waiting for MCP request id $Id."
}

function Invoke-McpRequest {
    param(
        [Parameter(Mandatory)] [string] $Method,
        [hashtable] $Params = @{}
    )

    $script:NextRequestId += 1
    $id = $script:NextRequestId
    Send-McpMessage ([ordered]@{
        jsonrpc = '2.0'
        id = $id
        method = $Method
        params = $Params
    })
    return Read-McpResponse -Id $id
}

function Invoke-McpNotification {
    param([Parameter(Mandatory)] [string] $Method)

    Send-McpMessage ([ordered]@{ jsonrpc = '2.0'; method = $Method })
}

function Connect-McpServer {
    Start-McpServer
    [void] (Invoke-McpRequest -Method 'initialize' -Params @{
        protocolVersion = '2025-06-18'
        capabilities = @{}
        clientInfo = @{ name = 'live-test-project-tree-v3'; version = '1.0.0' }
    })
    Invoke-McpNotification -Method 'notifications/initialized'

    $toolsResponse = Invoke-McpRequest -Method 'tools/list'
    $script:ToolNames = @($toolsResponse.Parsed.result.tools | ForEach-Object name | Sort-Object)
    $expectedToolNames = @('browse_project_tree', 'execute_read_batch', 'get_project_status', 'network_read')
    Assert-Condition (($script:ToolNames -join "`n") -ceq ($expectedToolNames -join "`n")) 'The server did not expose the exact read-only tool surface.'
}

function Invoke-McpTool {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [hashtable] $Arguments
    )

    Assert-Condition ($script:ToolNames -ccontains $Name) "Tool '$Name' is not on the verified read-only surface."
    $response = Invoke-McpRequest -Method 'tools/call' -Params @{ name = $Name; arguments = $Arguments }
    $mcpResult = $response.Parsed.result
    Assert-Condition ($null -ne $mcpResult) "Tool '$Name' returned no MCP result."

    $document = [System.Text.Json.JsonDocument]::Parse($response.Raw)
    try {
        $structuredJson = $document.RootElement.GetProperty('result').GetProperty('structuredContent').GetRawText()
    }
    finally {
        $document.Dispose()
    }

    Assert-Condition ($mcpResult.content.Count -ge 1) "Tool '$Name' returned no text content."
    $envelope = $mcpResult.structuredContent
    Assert-Condition ($null -ne $envelope) "Tool '$Name' returned no structured content."
    $envelope | Add-Member -NotePropertyName '__contentText' -NotePropertyValue ([string] $mcpResult.content[0].text) -Force
    $envelope | Add-Member -NotePropertyName '__structuredJson' -NotePropertyValue $structuredJson -Force
    return $envelope
}

function Assert-CanonicalRepresentationsEqual {
    param([Parameter(Mandatory)] [object] $Response)

    Assert-Condition ($Response.__contentText -ceq $Response.__structuredJson) 'The MCP text and structured representations are not the same canonical JSON document.'
    Assert-Condition ($Response.contractVersion -ceq '3.0') 'browse_project_tree did not return contractVersion 3.0.'
}

function Assert-SucceededResponse {
    param([Parameter(Mandatory)] [object] $Response)

    Assert-CanonicalRepresentationsEqual $Response
    Assert-Condition ($Response.status -ceq 'succeeded') "Expected a succeeded response, received '$($Response.status)'."
    Assert-Condition ($null -ne $Response.result) 'A succeeded response did not contain result.'
    Assert-Condition ($null -eq $Response.failure) 'A succeeded response contained failure.'
}

function Assert-FailureResponse {
    param(
        [Parameter(Mandatory)] [object] $Response,
        [Parameter(Mandatory)] [string] $Category
    )

    Assert-CanonicalRepresentationsEqual $Response
    Assert-Condition ($Response.status -ceq 'failed') "Expected a failed response for '$Category'."
    Assert-Condition ($null -eq $Response.result) "Failure '$Category' unexpectedly contained result."
    Assert-Condition ($Response.failure.category -ceq $Category) "Expected failure '$Category', received '$($Response.failure.category)'."
}

function Get-MedianMilliseconds([double[]] $Values) {
    $ordered = @($Values | Sort-Object)
    return $ordered[[math]::Floor($ordered.Count / 2)]
}

function Measure-InitialBrowse([hashtable] $Arguments) {
    $runs = foreach ($run in 1..3) {
        $watch = [System.Diagnostics.Stopwatch]::StartNew()
        $response = Invoke-McpTool -Name 'browse_project_tree' -Arguments $Arguments
        $watch.Stop()
        Assert-CanonicalRepresentationsEqual $response
        Assert-SucceededResponse $response
        [pscustomobject]@{ run = $run; elapsedMs = $watch.Elapsed.TotalMilliseconds; response = $response }
    }
    [pscustomobject]@{
        runs = $runs
        medianMs = Get-MedianMilliseconds @($runs.elapsedMs)
    }
}

function Read-AllSnapshotPages([object] $FirstPage) {
    $pages = @($FirstPage)
    $cursor = $FirstPage.result.pagination.nextCursor
    while ($null -ne $cursor) {
        $next = Invoke-McpTool -Name 'browse_project_tree' -Arguments @{ cursor = $cursor; pageSize = 200 }
        Assert-SucceededResponse $next
        $pages += $next
        $cursor = $next.result.pagination.nextCursor
    }
    return $pages
}

function Get-SnapshotNodes {
    param([Parameter(Mandatory)] [object[]] $Pages)

    return @($Pages | ForEach-Object { @($_.result.nodes) })
}

function Assert-CompleteSnapshotEvidence {
    param(
        [Parameter(Mandatory)] [string] $Mode,
        [Parameter(Mandatory)] [object[]] $Pages
    )

    Assert-Condition ($Pages.Count -ge 1) "Mode '$Mode' produced no pages."
    $snapshotId = [string] $Pages[0].result.snapshot.snapshotId
    $totalNodes = [int] $Pages[0].result.snapshot.totalNodes
    $expectedSequence = 0
    $seenNodeIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $expectedNodeProperties = @('details', 'name', 'nodeId', 'nodeType', 'parentNodeId', 'sequence')

    for ($pageIndex = 0; $pageIndex -lt $Pages.Count; $pageIndex++) {
        $page = $Pages[$pageIndex]
        Assert-SucceededResponse $page
        Assert-Condition ([string]::Equals($snapshotId, [string] $page.result.snapshot.snapshotId, [StringComparison]::Ordinal)) "Mode '$Mode' changed snapshot while walking continuations."
        Assert-Condition ([int] $page.result.snapshot.totalNodes -eq $totalNodes) "Mode '$Mode' changed totalNodes while walking continuations."
        Assert-Condition ([int] $page.result.pagination.offset -eq $expectedSequence) "Mode '$Mode' returned a non-contiguous page offset."
        Assert-Condition ([int] $page.result.pagination.returnedCount -eq @($page.result.nodes).Count) "Mode '$Mode' returned an incorrect returnedCount."
        Assert-Condition ($page.__contentText -notmatch '\[TRUNCATED\]') "Mode '$Mode' returned a truncation marker."

        if ($pageIndex -lt ($Pages.Count - 1)) {
            Assert-Condition ($null -ne $page.result.pagination.nextCursor) "Mode '$Mode' ended before all collected pages."
        }
        else {
            Assert-Condition ($null -eq $page.result.pagination.nextCursor) "Mode '$Mode' did not complete its cursor chain."
        }

        foreach ($node in @($page.result.nodes)) {
            Assert-Condition ([int] $node.sequence -eq $expectedSequence) "Mode '$Mode' broke exact sequence continuity at $expectedSequence."
            $actualNodeProperties = @($node.PSObject.Properties.Name | Where-Object { -not $_.StartsWith('__', [StringComparison]::Ordinal) } | Sort-Object)
            Assert-Condition (($actualNodeProperties -join "`n") -ceq ($expectedNodeProperties -join "`n")) "Mode '$Mode' returned a non-v3 node shape."

            if ($null -ne $node.parentNodeId) {
                Assert-Condition ($seenNodeIds.Contains([string] $node.parentNodeId)) "Mode '$Mode' returned a child before its parent."
            }

            $detailNames = @($node.details.PSObject.Properties.Name)
            Assert-Condition (-not ($detailNames -contains 'Path')) "Mode '$Mode' returned the removed details.Path member."
            Assert-Condition ($seenNodeIds.Add([string] $node.nodeId)) "Mode '$Mode' returned a duplicate nodeId."
            $expectedSequence += 1
        }
    }

    Assert-Condition ($expectedSequence -eq $totalNodes) "Mode '$Mode' returned $expectedSequence nodes but declared $totalNodes."
    return [ordered]@{
        snapshotId = $snapshotId
        pageCount = $Pages.Count
        totalNodes = $totalNodes
        sequenceContinuity = $true
        parentBeforeChild = $true
        cursorCompleted = $true
        legacyPathAbsent = $true
        truncationMarkerAbsent = $true
        canonicalRepresentationsEqual = $true
    }
}

function Reconstruct-TypedSelector {
    param(
        [Parameter(Mandatory)] [object[]] $Nodes,
        [Parameter(Mandatory)] [string] $NodeId
    )

    $byId = @{}
    foreach ($node in $Nodes) {
        $byId[[string] $node.nodeId] = $node
    }

    $segments = [System.Collections.Generic.List[object]]::new()
    $currentId = $NodeId
    while ($null -ne $currentId) {
        Assert-Condition ($byId.ContainsKey($currentId)) "Cannot reconstruct selector: node '$currentId' is absent."
        $node = $byId[$currentId]
        $segments.Insert(0, [ordered]@{ nodeType = [string] $node.nodeType; name = [string] $node.name })
        $currentId = if ($null -eq $node.parentNodeId) { $null } else { [string] $node.parentNodeId }
    }

    Assert-Condition ($segments.Count -ge 1) 'Cannot reconstruct an empty selector.'
    Assert-Condition ([string] $segments[0].nodeType -ceq 'Device') 'A reconstructed selector must begin with Device.'
    return @($segments)
}

function Get-NodeDepth {
    param(
        [Parameter(Mandatory)] [hashtable] $ById,
        [Parameter(Mandatory)] [object] $Node
    )

    $depth = 1
    $parentId = $Node.parentNodeId
    while ($null -ne $parentId) {
        Assert-Condition ($ById.ContainsKey([string] $parentId)) "Parent '$parentId' is absent from the complete snapshot."
        $depth += 1
        $parentId = $ById[[string] $parentId].parentNodeId
    }
    return $depth
}

function Get-DeepestNode {
    param([Parameter(Mandatory)] [object[]] $Nodes)

    $byId = @{}
    foreach ($node in $Nodes) {
        $byId[[string] $node.nodeId] = $node
    }

    $deepest = $Nodes |
        Sort-Object -Property @{ Expression = { Get-NodeDepth -ById $byId -Node $_ }; Descending = $true }, @{ Expression = { [int] $_.sequence }; Descending = $true } |
        Select-Object -First 1
    Assert-Condition ($null -ne $deepest) 'The selected-device snapshot contained no nodes.'
    Assert-Condition ((Get-NodeDepth -ById $byId -Node $deepest) -ge 3) 'The selected-device snapshot did not contain a deep selector target.'
    return $deepest
}

function Get-SubtreeProjection {
    param(
        [Parameter(Mandatory)] [object[]] $Nodes,
        [Parameter(Mandatory)] [string] $RootNodeId
    )

    $included = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $subtree = [System.Collections.Generic.List[object]]::new()
    foreach ($node in $Nodes) {
        $nodeId = [string] $node.nodeId
        $isRoot = [string]::Equals($nodeId, $RootNodeId, [StringComparison]::Ordinal)
        $parentIncluded = ($null -ne $node.parentNodeId) -and $included.Contains([string] $node.parentNodeId)
        if ($isRoot -or $parentIncluded) {
            [void] $included.Add($nodeId)
            $subtree.Add($node)
        }
    }

    Assert-Condition ($subtree.Count -ge 1) "Subtree root '$RootNodeId' was absent."
    $relativeSequenceById = @{}
    for ($index = 0; $index -lt $subtree.Count; $index++) {
        $relativeSequenceById[[string] $subtree[$index].nodeId] = $index
    }

    return @($subtree | ForEach-Object {
        $parentSequence = if (($null -ne $_.parentNodeId) -and $relativeSequenceById.ContainsKey([string] $_.parentNodeId)) {
            $relativeSequenceById[[string] $_.parentNodeId]
        }
        else {
            $null
        }
        [ordered]@{
            sequence = $relativeSequenceById[[string] $_.nodeId]
            parentSequence = $parentSequence
            name = [string] $_.name
            nodeType = [string] $_.nodeType
            details = $_.details
        }
    })
}

function Get-Sha256 {
    param([Parameter(Mandatory)] [string] $Text)

    $bytes = [Text.Encoding]::UTF8.GetBytes($Text)
    $hash = [Security.Cryptography.SHA256]::HashData($bytes)
    return [Convert]::ToHexString($hash).ToLowerInvariant()
}

function Find-AmbiguousSelector {
    param([Parameter(Mandatory)] [object[]] $Nodes)

    $groups = @{}
    foreach ($node in $Nodes) {
        if ($null -eq $node.parentNodeId) {
            continue
        }
        $key = ([string] $node.parentNodeId) + "`u{001f}" + ([string] $node.nodeType) + "`u{001f}" + ([string] $node.name).ToUpperInvariant()
        if (-not $groups.ContainsKey($key)) {
            $groups[$key] = [System.Collections.Generic.List[object]]::new()
        }
        $groups[$key].Add($node)
    }

    foreach ($group in $groups.Values) {
        if ($group.Count -gt 1) {
            $representative = $group[0]
            $parentSelector = Reconstruct-TypedSelector -Nodes $Nodes -NodeId ([string] $representative.parentNodeId)
            return @($parentSelector) + @([ordered]@{ nodeType = [string] $representative.nodeType; name = [string] $representative.name })
        }
    }

    throw 'target_ambiguous acceptance failed: the observed tree has no naturally ambiguous direct-child (nodeType, name) pair. Use another read-only project fixture after authorization.'
}

function New-MissingChildSelector {
    param(
        [Parameter(Mandatory)] [object[]] $Nodes,
        [Parameter(Mandatory)] [object] $ParentNode,
        [Parameter(Mandatory)] [object[]] $ParentSelector
    )

    $existingNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($node in $Nodes) {
        if (
            ($null -ne $node.parentNodeId) -and
            ([string] $node.parentNodeId -ceq [string] $ParentNode.nodeId) -and
            ([string] $node.nodeType -ceq 'PlcSoftware')
        ) {
            [void] $existingNames.Add([string] $node.name)
        }
    }

    $suffix = 0
    do {
        $missingName = "__TIA_MCP_MISSING_PLC_SOFTWARE_${suffix}__"
        $suffix += 1
    } while ($existingNames.Contains($missingName))

    return @($ParentSelector) + @([ordered]@{ nodeType = 'PlcSoftware'; name = $missingName })
}

function Get-MeasurementEvidence {
    param([Parameter(Mandatory)] [object] $Measurement)

    return [ordered]@{
        runs = @($Measurement.runs | ForEach-Object {
            [ordered]@{
                run = $_.run
                elapsedMs = $_.elapsedMs
                snapshotId = $_.response.result.snapshot.snapshotId
                returnedCount = $_.response.result.pagination.returnedCount
            }
        })
        medianMs = $Measurement.medianMs
    }
}

function Write-EvidenceArtifacts {
    param([Parameter(Mandatory)] [object] $Evidence)

    $stdoutPath = Get-EvidenceSidecarPath -Extension '.stdout.jsonl'
    $stderrPath = Get-EvidenceSidecarPath -Extension '.stderr.log'
    [System.IO.File]::WriteAllLines([System.IO.Path]::GetFullPath($stdoutPath), $script:StdoutLines)

    $stderr = ''
    if ($null -ne $script:StderrTask) {
        $stderr = $script:StderrTask.GetAwaiter().GetResult()
    }
    [System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($stderrPath), $stderr)
    $Evidence | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $EvidencePath -Encoding utf8
}

$evidence = [ordered]@{
    schemaVersion = 'issue-32-project-tree-v3-live/v1'
    startedAt = [DateTimeOffset]::UtcNow.ToString('O')
    projectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
    serverPath = (Resolve-Path -LiteralPath $ServerPath).Path
    status = 'running'
    toolNames = @()
    modes = [ordered]@{}
    selectorReconstruction = $null
    selectorFailures = [ordered]@{}
    error = $null
}

Initialize-EvidenceDirectory
try {
    Connect-McpServer
    $evidence.toolNames = $script:ToolNames

    $fullMeasurement = Measure-InitialBrowse -Arguments @{ pageSize = 200 }
    $fullPages = @(Read-AllSnapshotPages -FirstPage $fullMeasurement.runs[-1].response)
    $fullNodes = @(Get-SnapshotNodes -Pages $fullPages)
    $evidence.modes.fullProject = [ordered]@{
        arguments = [ordered]@{ pageSize = 200 }
        timing = Get-MeasurementEvidence $fullMeasurement
        snapshot = Assert-CompleteSnapshotEvidence -Mode 'fullProject' -Pages $fullPages
    }

    $depthMeasurement = Measure-InitialBrowse -Arguments @{ depth = 2; pageSize = 200 }
    $depthPages = @(Read-AllSnapshotPages -FirstPage $depthMeasurement.runs[-1].response)
    $evidence.modes.depthLimited = [ordered]@{
        arguments = [ordered]@{ depth = 2; pageSize = 200 }
        timing = Get-MeasurementEvidence $depthMeasurement
        snapshot = Assert-CompleteSnapshotEvidence -Mode 'depthLimited' -Pages $depthPages
    }

    $deviceNode = $fullNodes | Where-Object { ($null -eq $_.parentNodeId) -and ([string] $_.nodeType -ceq 'Device') } | Select-Object -First 1
    Assert-Condition ($null -ne $deviceNode) 'The complete project snapshot contained no Device root.'
    $deviceSelector = @([ordered]@{ nodeType = 'Device'; name = [string] $deviceNode.name })
    $deviceMeasurement = Measure-InitialBrowse -Arguments @{ startSelector = $deviceSelector; pageSize = 200 }
    $devicePages = @(Read-AllSnapshotPages -FirstPage $deviceMeasurement.runs[-1].response)
    $deviceNodes = @(Get-SnapshotNodes -Pages $devicePages)
    $evidence.modes.selectedDevice = [ordered]@{
        arguments = [ordered]@{ startSelector = $deviceSelector; pageSize = 200 }
        timing = Get-MeasurementEvidence $deviceMeasurement
        snapshot = Assert-CompleteSnapshotEvidence -Mode 'selectedDevice' -Pages $devicePages
    }

    $deepNode = Get-DeepestNode -Nodes $deviceNodes
    $deepSelector = @(Reconstruct-TypedSelector -Nodes $deviceNodes -NodeId ([string] $deepNode.nodeId))
    $targetFirstPage = Invoke-McpTool -Name 'browse_project_tree' -Arguments @{ startSelector = $deepSelector; pageSize = 200 }
    Assert-SucceededResponse $targetFirstPage
    $targetPages = @(Read-AllSnapshotPages -FirstPage $targetFirstPage)
    $targetNodes = @(Get-SnapshotNodes -Pages $targetPages)
    $targetSnapshotEvidence = Assert-CompleteSnapshotEvidence -Mode 'reconstructedSelector' -Pages $targetPages
    $expectedProjection = @(Get-SubtreeProjection -Nodes $deviceNodes -RootNodeId ([string] $deepNode.nodeId))
    $actualProjection = @(Get-SubtreeProjection -Nodes $targetNodes -RootNodeId ([string] $targetNodes[0].nodeId))
    $expectedJson = $expectedProjection | ConvertTo-Json -Compress -Depth 100
    $actualJson = $actualProjection | ConvertTo-Json -Compress -Depth 100
    Assert-Condition ($expectedJson -ceq $actualJson) 'The reconstructed selector result did not equal the complete selected-device subtree.'
    $evidence.selectorReconstruction = [ordered]@{
        selector = $deepSelector
        sourceNodeId = $deepNode.nodeId
        sourceSequence = $deepNode.sequence
        nodeCount = $targetNodes.Count
        sourceSubtreeSha256 = Get-Sha256 $expectedJson
        selectedResultSha256 = Get-Sha256 $actualJson
        completeResultEqual = $true
        snapshot = $targetSnapshotEvidence
    }

    $missingSelector = @(New-MissingChildSelector -Nodes $fullNodes -ParentNode $deviceNode -ParentSelector $deviceSelector)
    $missingResponse = Invoke-McpTool -Name 'browse_project_tree' -Arguments @{ startSelector = $missingSelector; pageSize = 200 }
    Assert-FailureResponse -Response $missingResponse -Category 'target_not_found'
    $evidence.selectorFailures.targetNotFound = [ordered]@{ category = $missingResponse.failure.category; selector = $missingSelector }

    $invalidSelector = @([ordered]@{ nodeType = 'InvalidNodeType'; name = 'invalid' })
    $invalidResponse = Invoke-McpTool -Name 'browse_project_tree' -Arguments @{ startSelector = $invalidSelector; pageSize = 200 }
    Assert-FailureResponse -Response $invalidResponse -Category 'invalid_selector'
    $evidence.selectorFailures.invalidSelector = [ordered]@{ category = $invalidResponse.failure.category; selector = $invalidSelector }

    $ambiguousSelector = @(Find-AmbiguousSelector -Nodes $fullNodes)
    $ambiguousResponse = Invoke-McpTool -Name 'browse_project_tree' -Arguments @{ startSelector = $ambiguousSelector; pageSize = 200 }
    Assert-FailureResponse -Response $ambiguousResponse -Category 'target_ambiguous'
    $evidence.selectorFailures.targetAmbiguous = [ordered]@{ category = $ambiguousResponse.failure.category; selector = $ambiguousSelector }

    $evidence.status = 'succeeded'
}
catch {
    $evidence.status = 'failed'
    $evidence.error = $_.Exception.Message
    throw
}
finally {
    Stop-McpServer
    $evidence.completedAt = [DateTimeOffset]::UtcNow.ToString('O')
    Write-EvidenceArtifacts -Evidence $evidence
}
