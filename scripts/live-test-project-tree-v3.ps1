#Requires -Version 7.0
<#
.SYNOPSIS
Runs read-only live acceptance for the project-tree v3 contract.

.DESCRIPTION
The default FullV3 profile retains the original selector and pagination matrix.
The BlockHeaders profile is a two-stage Issue #30 acceptance flow. Run it first
against a current-main server without expectation inputs to create baseline
evidence. Then run it against the candidate server with both the customized
expectations JSON and the baseline evidence. The candidate run verifies exact
block-header values, blank omission, complete paging, stable tree identity, and
reports three-run timing and initial-response-size deltas without thresholds.

Both profiles start the server with --read-only. This script does not call save,
write, preview, apply, project lifecycle, compile, or network-write tools.

.EXAMPLE
pwsh ./scripts/live-test-project-tree-v3.ps1 -AcceptanceProfile BlockHeaders -ProjectPath C:\Projects\Disposable.ap21 -ServerPath C:\Builds\main\TiaMcpServer.exe -EvidencePath ./artifacts/issue-30-live/main.json

.EXAMPLE
pwsh ./scripts/live-test-project-tree-v3.ps1 -AcceptanceProfile BlockHeaders -ProjectPath C:\Projects\Disposable.ap21 -ServerPath ./TiaMcpServer/bin/Release/net10.0/TiaMcpServer.exe -EvidencePath ./artifacts/issue-30-live/candidate.json -BlockHeaderExpectationsPath ./artifacts/issue-30-live/expectations.json -BaselineEvidencePath ./artifacts/issue-30-live/main.json
#>
[CmdletBinding()]
param(
    [string] $ProjectPath = $env:TIA_MCP_LIVE_PROJECT_PATH,
    [string] $ServerPath = (Join-Path $PSScriptRoot '..\TiaMcpServer\bin\Release\net10.0\TiaMcpServer.exe'),
    [string] $EvidencePath = (Join-Path $PSScriptRoot '..\artifacts\issue-32-live\project-tree-v3-evidence.json'),
    [ValidateSet('FullV3', 'BlockHeaders')]
    [string] $AcceptanceProfile = 'FullV3',
    [string] $BlockHeaderExpectationsPath = '',
    [string] $BaselineEvidencePath = ''
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

function Resolve-BlockHeaderRunConfiguration {
    param(
        [Parameter(Mandatory)] [string] $AcceptanceProfile,
        [AllowEmptyString()] [string] $ExpectationsPath,
        [AllowEmptyString()] [string] $BaselinePath
    )

    $hasExpectations = -not [string]::IsNullOrWhiteSpace($ExpectationsPath)
    $hasBaseline = -not [string]::IsNullOrWhiteSpace($BaselinePath)
    if ($AcceptanceProfile -ieq 'FullV3') {
        Assert-Condition ((-not $hasExpectations) -and (-not $hasBaseline)) 'Block-header evidence inputs require -AcceptanceProfile BlockHeaders.'
        return [ordered]@{ mode = 'fullV3'; profile = 'FullV3' }
    }

    Assert-Condition ($AcceptanceProfile -ieq 'BlockHeaders') "Unsupported acceptance profile '$AcceptanceProfile'."
    Assert-Condition ($hasExpectations -eq $hasBaseline) 'Candidate block-header acceptance requires both -BlockHeaderExpectationsPath and -BaselineEvidencePath.'
    return [ordered]@{ mode = if ($hasExpectations) { 'candidate' } else { 'baseline' }; profile = 'BlockHeaders' }
}

function Assert-DistinctBlockHeaderArtifactPaths {
    param(
        [Parameter(Mandatory)] [string] $OutputEvidencePath,
        [Parameter(Mandatory)] [string] $ExpectationsPath,
        [Parameter(Mandatory)] [string] $BaselinePath
    )

    $evidenceFullPath = [System.IO.Path]::GetFullPath($OutputEvidencePath)
    $evidenceDirectory = [System.IO.Path]::GetDirectoryName($evidenceFullPath)
    $evidenceBaseName = [System.IO.Path]::GetFileNameWithoutExtension($evidenceFullPath)
    $outputPaths = @(
        $evidenceFullPath,
        (Join-Path $evidenceDirectory ($evidenceBaseName + '.stdout.jsonl')),
        (Join-Path $evidenceDirectory ($evidenceBaseName + '.stderr.log'))
    )
    $inputPaths = @(
        [System.IO.Path]::GetFullPath($ExpectationsPath),
        [System.IO.Path]::GetFullPath($BaselinePath)
    )

    foreach ($outputPath in $outputPaths) {
        foreach ($inputPath in $inputPaths) {
            Assert-Condition (-not [string]::Equals($outputPath, $inputPath, [StringComparison]::OrdinalIgnoreCase)) 'Evidence outputs must not overwrite the block-header expectations or baseline evidence.'
        }
    }
}

function Test-NonNegativeJsonNumber {
    param(
        [AllowNull()] [object] $Value,
        [switch] $Integer
    )

    $isNumber =
        ($Value -is [byte]) -or ($Value -is [sbyte]) -or
        ($Value -is [int16]) -or ($Value -is [uint16]) -or
        ($Value -is [int32]) -or ($Value -is [uint32]) -or
        ($Value -is [int64]) -or ($Value -is [uint64]) -or
        ($Value -is [single]) -or ($Value -is [double]) -or
        ($Value -is [decimal])
    if (-not $isNumber) { return $false }

    $number = [double] $Value
    if ([double]::IsNaN($number) -or [double]::IsInfinity($number) -or ($number -lt 0)) { return $false }
    return (-not $Integer) -or ([math]::Truncate($number) -eq $number)
}

function Assert-BlockHeaderExpectationDocument {
    param([Parameter(Mandatory)] [object] $Expectations)

    $schemaProperty = $Expectations.PSObject.Properties['schemaVersion']
    Assert-Condition (($null -ne $schemaProperty) -and ([string] $schemaProperty.Value -ceq 'issue-30-block-header-expectations/v1')) 'Block-header expectations have the wrong schemaVersion.'
    $casesProperty = $Expectations.PSObject.Properties['cases']
    Assert-Condition ($null -ne $casesProperty) 'Block-header expectations have no cases array.'
    Assert-Condition ($casesProperty.Value -is [System.Object[]]) 'Block-header expectation cases must be a JSON array.'
    $cases = @($casesProperty.Value)
    Assert-Condition ($cases.Count -ge 4) 'Block-header expectations require user, system, softwareUnit, and blank cases.'

    $knownKinds = @('user', 'system', 'softwareUnit', 'blank')
    $requiredKinds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($kind in $knownKinds) { [void] $requiredKinds.Add($kind) }
    $headerFields = @('HeaderAuthor', 'HeaderVersion', 'HeaderFamily', 'HeaderName')
    $verifiedFields = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $caseIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $nameDivergenceSpecified = $false

    foreach ($case in $cases) {
        Assert-Condition ($null -ne $case) 'Block-header expectations contain a null case.'
        $idProperty = $case.PSObject.Properties['id']
        $kindProperty = $case.PSObject.Properties['kind']
        $selectorProperty = $case.PSObject.Properties['selector']
        $expectedProperty = $case.PSObject.Properties['expected']
        $caseId = if ($null -eq $idProperty) { '' } else { [string] $idProperty.Value }
        $kind = if ($null -eq $kindProperty) { '' } else { [string] $kindProperty.Value }
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($caseId)) 'A block-header expectation case has no id.'
        Assert-Condition ($caseIds.Add($caseId)) "Duplicate block-header expectation id '$caseId'."
        Assert-Condition ($kind -cin $knownKinds) "Block-header case '$caseId' has unsupported kind '$kind'."
        [void] $requiredKinds.Remove($kind)

        Assert-Condition ($null -ne $selectorProperty) "Block-header case '$caseId' has no selector."
        Assert-Condition ($selectorProperty.Value -is [System.Object[]]) "Block-header case '$caseId' selector must be a JSON array."
        $selector = @($selectorProperty.Value)
        Assert-Condition ($selector.Count -ge 1) "Block-header case '$caseId' has an empty selector."
        foreach ($segment in $selector) {
            Assert-Condition ($null -ne $segment) "Block-header case '$caseId' has a null selector segment."
            $nodeTypeProperty = $segment.PSObject.Properties['nodeType']
            $nameProperty = $segment.PSObject.Properties['name']
            Assert-Condition (($null -ne $nodeTypeProperty) -and (-not [string]::IsNullOrWhiteSpace([string] $nodeTypeProperty.Value))) "Block-header case '$caseId' has a selector segment without nodeType."
            Assert-Condition (($null -ne $nameProperty) -and (-not [string]::IsNullOrWhiteSpace([string] $nameProperty.Value))) "Block-header case '$caseId' has a selector segment without name."
        }
        # Typed selectors identify the terminal type; mirror ProjectTreeNodeTypes.BlockLeaves.
        Assert-Condition ([string] $selector[-1].nodeType -cin @('OB', 'FB', 'FC', 'GlobalDB', 'InstanceDB', 'ArrayDB', 'Block')) "Block-header case '$caseId' selector must end at a functional PLC block."

        Assert-Condition (($null -ne $expectedProperty) -and ($null -ne $expectedProperty.Value)) "Block-header case '$caseId' has no expected object."
        $expected = $expectedProperty.Value
        $expectedNameProperty = $expected.PSObject.Properties['name']
        $detailsProperty = $expected.PSObject.Properties['details']
        $absentDetailsProperty = $expected.PSObject.Properties['absentDetails']
        $expectedName = if ($null -eq $expectedNameProperty) { '' } else { [string] $expectedNameProperty.Value }
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($expectedName)) "Block-header case '$caseId' has no expected engineering name."
        Assert-Condition (($null -ne $detailsProperty) -and ($null -ne $detailsProperty.Value)) "Block-header case '$caseId' has no expected details object."

        $detailProperties = @($detailsProperty.Value.PSObject.Properties)
        if ($kind -cne 'blank') {
            Assert-Condition ($detailProperties.Count -ge 1) "Block-header case '$caseId' must verify at least one populated header field."
        }

        $caseFields = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $detailProperties) {
            $fieldName = [string] $property.Name
            Assert-Condition ($fieldName -cin $headerFields) "Block-header case '$caseId' contains unsupported expected detail '$fieldName'."
            Assert-Condition (-not [string]::IsNullOrWhiteSpace([string] $property.Value)) "Block-header case '$caseId' expects blank detail '$fieldName'. Use absentDetails instead."
            Assert-Condition ($caseFields.Add($fieldName)) "Block-header case '$caseId' repeats expected detail '$fieldName'."
            [void] $verifiedFields.Add($fieldName)
            if (($fieldName -ceq 'HeaderName') -and ($expectedName -cne [string] $property.Value)) { $nameDivergenceSpecified = $true }
        }

        if ($null -ne $absentDetailsProperty) {
            Assert-Condition ($absentDetailsProperty.Value -is [System.Object[]]) "Block-header case '$caseId' absentDetails must be a JSON array."
        }
        $absentDetails = @(if ($null -ne $absentDetailsProperty) { $absentDetailsProperty.Value })
        foreach ($field in $absentDetails) {
            $fieldName = [string] $field
            Assert-Condition ($fieldName -cin $headerFields) "Block-header case '$caseId' contains unsupported absent detail '$fieldName'."
            Assert-Condition ($caseFields.Add($fieldName)) "Block-header case '$caseId' lists detail '$fieldName' as both populated and absent."
        }
        if ($kind -ceq 'blank') {
            Assert-Condition ($absentDetails.Count -ge 1) "Blank block-header case '$caseId' must declare at least one absent header detail."
        }
    }

    Assert-Condition ($requiredKinds.Count -eq 0) "Block-header expectations are missing case kinds: $(@($requiredKinds) -join ', ')."
    Assert-Condition ($verifiedFields.Count -eq $headerFields.Count) 'Block-header expectations did not specify all four populated header fields.'
    Assert-Condition $nameDivergenceSpecified 'Block-header expectations require an engineering name that differs from HeaderName.'
}

function Assert-BlockHeaderBaselineEvidence {
    param(
        [Parameter(Mandatory)] [object] $Baseline,
        [Parameter(Mandatory)] [string] $ExpectedProjectPath
    )

    $schemaProperty = $Baseline.PSObject.Properties['schemaVersion']
    $profileProperty = $Baseline.PSObject.Properties['acceptanceProfile']
    $modeProperty = $Baseline.PSObject.Properties['runMode']
    $statusProperty = $Baseline.PSObject.Properties['status']
    $projectProperty = $Baseline.PSObject.Properties['projectPath']
    Assert-Condition (($null -ne $schemaProperty) -and ([string] $schemaProperty.Value -ceq 'issue-30-block-header-metadata-live/v1')) 'Baseline evidence has the wrong schemaVersion.'
    Assert-Condition (($null -ne $profileProperty) -and ([string] $profileProperty.Value -ceq 'BlockHeaders')) 'Baseline evidence was not produced with the BlockHeaders profile.'
    Assert-Condition (($null -ne $modeProperty) -and ([string] $modeProperty.Value -ceq 'baseline')) 'Baseline evidence is not a baseline run.'
    Assert-Condition (($null -ne $statusProperty) -and ([string] $statusProperty.Value -ceq 'succeeded')) 'Baseline evidence did not succeed.'
    Assert-Condition (($null -ne $projectProperty) -and [string]::Equals([string] $projectProperty.Value, $ExpectedProjectPath, [StringComparison]::OrdinalIgnoreCase)) 'Baseline evidence used a different project path.'

    $runtimeArtifactsProperty = $Baseline.PSObject.Properties['runtimeArtifacts']
    Assert-Condition (($null -ne $runtimeArtifactsProperty) -and ($null -ne $runtimeArtifactsProperty.Value)) 'Baseline evidence has no runtimeArtifacts provenance.'
    foreach ($artifactName in @('host', 'opennessWorker')) {
        $artifactProperty = $runtimeArtifactsProperty.Value.PSObject.Properties[$artifactName]
        Assert-Condition (($null -ne $artifactProperty) -and ($null -ne $artifactProperty.Value)) "Baseline evidence has no $artifactName runtime artifact."
        $pathProperty = $artifactProperty.Value.PSObject.Properties['path']
        $hashProperty = $artifactProperty.Value.PSObject.Properties['sha256']
        Assert-Condition (($null -ne $pathProperty) -and (-not [string]::IsNullOrWhiteSpace([string] $pathProperty.Value))) "Baseline $artifactName runtime artifact has no path."
        Assert-Condition (($null -ne $hashProperty) -and ([string] $hashProperty.Value -cmatch '^[0-9a-f]{64}$')) "Baseline $artifactName runtime artifact has an invalid lowercase SHA-256."
    }

    $modesProperty = $Baseline.PSObject.Properties['modes']
    Assert-Condition (($null -ne $modesProperty) -and ($null -ne $modesProperty.Value)) 'Baseline evidence has no modes object.'
    $fullProjectProperty = $modesProperty.Value.PSObject.Properties['fullProject']
    Assert-Condition (($null -ne $fullProjectProperty) -and ($null -ne $fullProjectProperty.Value)) 'Baseline evidence has no modes.fullProject object.'
    $fullProject = $fullProjectProperty.Value

    $identityProperty = $fullProject.PSObject.Properties['treeIdentity']
    Assert-Condition (($null -ne $identityProperty) -and ($null -ne $identityProperty.Value)) 'Baseline fullProject evidence has no treeIdentity.'
    $nodeCountProperty = $identityProperty.Value.PSObject.Properties['nodeCount']
    $identityHashProperty = $identityProperty.Value.PSObject.Properties['sha256']
    Assert-Condition (($null -ne $nodeCountProperty) -and (Test-NonNegativeJsonNumber -Value $nodeCountProperty.Value -Integer)) 'Baseline treeIdentity has an invalid nodeCount.'
    Assert-Condition ([int64] $nodeCountProperty.Value -le 4000000) 'Baseline treeIdentity nodeCount exceeds the 4,000,000-node collection bound.'
    Assert-Condition (($null -ne $identityHashProperty) -and ([string] $identityHashProperty.Value -cmatch '^[0-9a-f]{64}$')) 'Baseline treeIdentity has an invalid lowercase SHA-256.'

    $snapshotProperty = $fullProject.PSObject.Properties['snapshot']
    Assert-Condition (($null -ne $snapshotProperty) -and ($null -ne $snapshotProperty.Value)) 'Baseline fullProject evidence has no snapshot.'
    $totalNodesProperty = $snapshotProperty.Value.PSObject.Properties['totalNodes']
    Assert-Condition (($null -ne $totalNodesProperty) -and (Test-NonNegativeJsonNumber -Value $totalNodesProperty.Value -Integer)) 'Baseline snapshot has an invalid totalNodes.'
    Assert-Condition ([int64] $totalNodesProperty.Value -le 4000000) 'Baseline snapshot totalNodes exceeds the 4,000,000-node collection bound.'
    Assert-Condition ([int64] $totalNodesProperty.Value -eq [int64] $nodeCountProperty.Value) 'Baseline snapshot and tree identity node counts differ.'

    $timingProperty = $fullProject.PSObject.Properties['timing']
    Assert-Condition (($null -ne $timingProperty) -and ($null -ne $timingProperty.Value)) 'Baseline fullProject evidence has no timing object.'
    $runsProperty = $timingProperty.Value.PSObject.Properties['runs']
    Assert-Condition ($null -ne $runsProperty) 'Baseline timing has no runs array.'
    $runs = @($runsProperty.Value)
    Assert-Condition ($runs.Count -eq 3) 'Baseline timing must contain exactly three runs.'
    foreach ($run in $runs) {
        Assert-Condition ($null -ne $run) 'Baseline timing contains a null run.'
        $elapsedProperty = $run.PSObject.Properties['elapsedMs']
        $sizeProperty = $run.PSObject.Properties['canonicalResponseChars']
        Assert-Condition (($null -ne $elapsedProperty) -and (Test-NonNegativeJsonNumber -Value $elapsedProperty.Value)) 'Baseline timing run has an invalid elapsedMs.'
        Assert-Condition (($null -ne $sizeProperty) -and (Test-NonNegativeJsonNumber -Value $sizeProperty.Value -Integer)) 'Baseline timing run has an invalid canonicalResponseChars.'
        Assert-Condition ([int64] $sizeProperty.Value -le 60000) 'Baseline timing run canonicalResponseChars exceeds the 60,000-character response bound.'
    }
}

function Get-RuntimeArtifactProvenance {
    param([Parameter(Mandatory)] [string] $ResolvedServerPath)

    $resolvedHostPath = (Resolve-Path -LiteralPath $ResolvedServerPath).Path
    $workerCandidate = Join-Path (Split-Path -Parent $resolvedHostPath) 'openness-worker\TiaMcpServer.OpennessWorker.exe'
    Assert-Condition (Test-Path -LiteralPath $workerCandidate -PathType Leaf) "The deployed Openness worker was not found at '$workerCandidate'."
    $resolvedWorkerPath = (Resolve-Path -LiteralPath $workerCandidate).Path
    return [ordered]@{
        host = [ordered]@{
            path = $resolvedHostPath
            sha256 = (Get-FileHash -LiteralPath $resolvedHostPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        opennessWorker = [ordered]@{
            path = $resolvedWorkerPath
            sha256 = (Get-FileHash -LiteralPath $resolvedWorkerPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
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
    Assert-Condition ($Response.__contentText.Length -le 60000) 'The canonical project-tree response exceeds 60,000 characters.'
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

function Read-AllSnapshotPages([object] $FirstPage, [double] $MaximumCollectionSeconds = 600) {
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $pages = [System.Collections.Generic.List[object]]::new()
    $seenCursors = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    Assert-SucceededResponse $FirstPage
    $totalNodes = [long] $FirstPage.result.snapshot.totalNodes
    # Even one serialized character per node cannot exceed the snapshot content budget.
    Assert-Condition ($totalNodes -ge 0 -and $totalNodes -le 4000000) 'Snapshot totalNodes exceeds the collection bound.'
    $maximumPages = [math]::Max(1, $totalNodes)
    $snapshotId = [string] $FirstPage.result.snapshot.snapshotId
    $expectedOffset = 0L
    $page = $FirstPage
    while ($true) {
        Assert-SucceededResponse $page
        Assert-Condition ($pages.Count -lt $maximumPages) 'Snapshot exceeded its totalNodes page bound.'
        Assert-Condition ([string] $page.result.snapshot.snapshotId -ceq $snapshotId) 'Snapshot changed during collection.'
        Assert-Condition ([long] $page.result.snapshot.totalNodes -eq $totalNodes) 'Snapshot totalNodes changed during collection.'
        Assert-Condition ([long] $page.result.pagination.offset -eq $expectedOffset) 'Snapshot page offset did not advance contiguously.'
        $count = [long] $page.result.pagination.returnedCount
        Assert-Condition ($count -ge 0 -and $count -le 200 -and $count -eq @($page.result.nodes).Count) 'Snapshot page returnedCount is invalid.'
        $expectedOffset += $count
        Assert-Condition ($expectedOffset -le $totalNodes) 'Snapshot returned more nodes than totalNodes.'
        $cursor = $page.result.pagination.nextCursor
        if ($null -eq $cursor) {
            Assert-Condition ($expectedOffset -eq $totalNodes) 'Snapshot cursor ended before totalNodes.'
            $pages.Add($page)
            break
        }
        Assert-Condition ($count -gt 0 -and $expectedOffset -lt $totalNodes) 'Snapshot continuation made no forward progress or exceeded totalNodes.'
        Assert-Condition (-not [string]::IsNullOrWhiteSpace([string] $cursor)) 'Snapshot returned an empty cursor.'
        Assert-Condition ($seenCursors.Add([string] $cursor)) 'Snapshot cursor cycle detected.'
        $pages.Add($page)
        Assert-Condition ($pages.Count -lt $maximumPages) 'Snapshot exceeded its totalNodes page bound.'
        Assert-Condition ($watch.Elapsed.TotalSeconds -lt $MaximumCollectionSeconds) 'Snapshot exceeded the overall collection deadline.'
        $page = Invoke-McpTool -Name 'browse_project_tree' -Arguments @{ cursor = $cursor; pageSize = 200 }
    }
    return $pages.ToArray()
}

function Get-SnapshotNodes {
    param([Parameter(Mandatory)] [object[]] $Pages)

    return @($Pages | ForEach-Object { @($_.result.nodes) })
}

function Resolve-NodeBySelector {
    param(
        [Parameter(Mandatory)] [object[]] $Nodes,
        [Parameter(Mandatory)] [object[]] $Selector,
        [Parameter(Mandatory)] [string] $CaseId
    )

    Assert-Condition ($Selector.Count -ge 1) "Block-header case '$CaseId' has an empty selector."
    $parentNodeId = $null
    $resolved = $null
    foreach ($segment in $Selector) {
        $segmentType = [string] $segment.nodeType
        $segmentName = [string] $segment.name
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($segmentType)) "Block-header case '$CaseId' has a selector segment without nodeType."
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($segmentName)) "Block-header case '$CaseId' has a selector segment without name."

        $matches = @($Nodes | Where-Object {
            $parentMatches = if ($null -eq $parentNodeId) {
                $null -eq $_.parentNodeId
            }
            else {
                [string] $_.parentNodeId -ceq [string] $parentNodeId
            }
            $parentMatches -and
                ([string] $_.nodeType -ceq $segmentType) -and
                [string]::Equals([string] $_.name, $segmentName, [StringComparison]::OrdinalIgnoreCase)
        })
        Assert-Condition ($matches.Count -eq 1) "Block-header case '$CaseId' selector segment '$segmentType/$segmentName' resolved to $($matches.Count) nodes."
        $resolved = $matches[0]
        $parentNodeId = [string] $resolved.nodeId
    }
    return $resolved
}

function Assert-BlockHeaderExpectations {
    param(
        [Parameter(Mandatory)] [object[]] $Pages,
        [Parameter(Mandatory)] [object] $Expectations
    )

    Assert-BlockHeaderExpectationDocument -Expectations $Expectations
    $cases = @($Expectations.cases)

    $nodes = @(Get-SnapshotNodes -Pages $Pages)
    $requiredKinds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($kind in @('user', 'system', 'softwareUnit', 'blank')) { [void] $requiredKinds.Add($kind) }
    $headerFields = @('HeaderAuthor', 'HeaderVersion', 'HeaderFamily', 'HeaderName')
    $verifiedFields = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $caseIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $caseEvidence = [System.Collections.Generic.List[object]]::new()
    $nameDivergenceVerified = $false
    $blankHeadersOmitted = $false

    foreach ($case in $cases) {
        $caseId = [string] $case.id
        $kind = [string] $case.kind
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($caseId)) 'A block-header expectation case has no id.'
        Assert-Condition ($caseIds.Add($caseId)) "Duplicate block-header expectation id '$caseId'."
        Assert-Condition ($requiredKinds.Contains($kind) -or ($kind -in @('user', 'system', 'softwareUnit', 'blank'))) "Block-header case '$caseId' has unsupported kind '$kind'."
        [void] $requiredKinds.Remove($kind)

        $node = Resolve-NodeBySelector -Nodes $nodes -Selector @($case.selector) -CaseId $caseId
        # Validate the observed node as well as the manifest's declared terminal type.
        Assert-Condition ([string] $node.nodeType -cin @('OB', 'FB', 'FC', 'GlobalDB', 'InstanceDB', 'ArrayDB', 'Block')) "Block-header case '$caseId' must resolve to a functional PLC block, received '$($node.nodeType)'."
        $expected = $case.expected
        Assert-Condition ($null -ne $expected) "Block-header case '$caseId' has no expected object."
        $expectedName = [string] $expected.name
        Assert-Condition ([string] $node.name -ceq $expectedName) "Block-header case '$caseId' expected engineering name '$expectedName' but received '$($node.name)'."
        Assert-Condition ($null -ne $node.details) "Block-header case '$caseId' returned no details object."

        $expectedDetailsProperty = $expected.PSObject.Properties['details']
        $expectedDetailProperties = @(if ($null -ne $expectedDetailsProperty) { $expectedDetailsProperty.Value.PSObject.Properties })
        if ($kind -ne 'blank') {
            Assert-Condition ($expectedDetailProperties.Count -ge 1) "Block-header case '$caseId' must verify at least one populated header field."
        }

        $observedDetails = [ordered]@{}
        foreach ($property in $expectedDetailProperties) {
            $fieldName = [string] $property.Name
            Assert-Condition ($fieldName -cin $headerFields) "Block-header case '$caseId' contains unsupported expected detail '$fieldName'."
            $expectedValue = [string] $property.Value
            Assert-Condition (-not [string]::IsNullOrWhiteSpace($expectedValue)) "Block-header case '$caseId' expects blank detail '$fieldName'. Use absentDetails instead."
            $actualProperties = @($node.details.PSObject.Properties | Where-Object { $_.Name -ceq $fieldName })
            Assert-Condition ($actualProperties.Count -eq 1) "Block-header case '$caseId' did not return exact detail '$fieldName'."
            $actualValue = [string] $actualProperties[0].Value
            Assert-Condition ($actualValue -ceq $expectedValue) "Block-header case '$caseId' expected '$fieldName' value '$expectedValue' but received '$actualValue'."
            [void] $verifiedFields.Add($fieldName)
            $observedDetails[$fieldName] = $actualValue
            if (($fieldName -ceq 'HeaderName') -and ($expectedName -cne $expectedValue)) {
                $nameDivergenceVerified = $true
            }
        }

        $absentDetailsProperty = $expected.PSObject.Properties['absentDetails']
        $absentDetails = @(if ($null -ne $absentDetailsProperty) { $absentDetailsProperty.Value })
        foreach ($field in $absentDetails) {
            $fieldName = [string] $field
            Assert-Condition ($fieldName -cin $headerFields) "Block-header case '$caseId' contains unsupported absent detail '$fieldName'."
            $actualProperties = @($node.details.PSObject.Properties | Where-Object { $_.Name -ceq $fieldName })
            Assert-Condition ($actualProperties.Count -eq 0) "Block-header case '$caseId' unexpectedly returned blank detail '$fieldName'."
        }

        if ($kind -ceq 'system') {
            $systemProperties = @($node.details.PSObject.Properties | Where-Object { $_.Name -ceq 'IsSystemBlock' })
            Assert-Condition (($systemProperties.Count -eq 1) -and ([string] $systemProperties[0].Value -ceq 'true')) "Block-header case '$caseId' is not a system-block path."
        }
        elseif ($kind -ceq 'softwareUnit') {
            $unitProperties = @($node.details.PSObject.Properties | Where-Object { $_.Name -ceq 'SoftwareUnit' })
            Assert-Condition (($unitProperties.Count -eq 1) -and (-not [string]::IsNullOrWhiteSpace([string] $unitProperties[0].Value))) "Block-header case '$caseId' is not a software-unit block path."
        }
        elseif ($kind -ceq 'user') {
            $systemProperties = @($node.details.PSObject.Properties | Where-Object { $_.Name -ceq 'IsSystemBlock' })
            $unitProperties = @($node.details.PSObject.Properties | Where-Object { $_.Name -ceq 'SoftwareUnit' })
            Assert-Condition (($systemProperties.Count -eq 0) -and ($unitProperties.Count -eq 0)) "Block-header case '$caseId' is not a PLC-scoped user block path."
        }
        elseif ($kind -ceq 'blank') {
            if ($absentDetails.Count -ge 1) { $blankHeadersOmitted = $true }
        }

        $caseEvidence.Add([ordered]@{
            id = $caseId
            kind = $kind
            selector = @($case.selector)
            nodeId = [string] $node.nodeId
            nodeType = [string] $node.nodeType
            name = [string] $node.name
            verifiedDetails = $observedDetails
            absentDetails = $absentDetails
        })
    }

    Assert-Condition ($requiredKinds.Count -eq 0) "Block-header expectations are missing case kinds: $(@($requiredKinds) -join ', ')."
    Assert-Condition ($verifiedFields.Count -eq $headerFields.Count) "Block-header expectations did not verify all four header fields."
    Assert-Condition $nameDivergenceVerified 'Block-header expectations require an engineering name that differs from HeaderName.'
    Assert-Condition $blankHeadersOmitted 'Block-header expectations require a blank-header omission case.'

    return [ordered]@{
        cases = $caseEvidence.ToArray()
        allHeaderFieldsVerified = $true
        engineeringNameDiffersFromHeaderName = $true
        blankHeadersOmitted = $true
    }
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
    $maximumCanonicalResponseChars = 0
    $seenNodeIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $expectedNodeProperties = @('details', 'name', 'nodeId', 'nodeType', 'parentNodeId', 'sequence')

    for ($pageIndex = 0; $pageIndex -lt $Pages.Count; $pageIndex++) {
        $page = $Pages[$pageIndex]
        Assert-SucceededResponse $page
        $maximumCanonicalResponseChars = [math]::Max($maximumCanonicalResponseChars, $page.__contentText.Length)
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

            $legacyPathProperty = if ($null -eq $node.details) { $null } else { $node.details.PSObject.Properties['Path'] }
            Assert-Condition ($null -eq $legacyPathProperty) "Mode '$Mode' returned the removed details.Path member."
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
        maximumCanonicalResponseChars = $maximumCanonicalResponseChars
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
        Where-Object { Test-UniqueNodeSelector -Nodes $Nodes -Node $_ } |
        Sort-Object -Property @{ Expression = { Get-NodeDepth -ById $byId -Node $_ }; Descending = $true }, @{ Expression = { [int] $_.sequence }; Descending = $true } |
        Select-Object -First 1
    Assert-Condition ($null -ne $deepest) 'The selected-device snapshot contained no nodes.'
    Assert-Condition ((Get-NodeDepth -ById $byId -Node $deepest) -ge 3) 'The selected-device snapshot did not contain a deep selector target.'
    return $deepest
}

function Test-UniqueNodeSelector {
    param([object[]] $Nodes, [object] $Node)

    $current = $Node
    while ($null -ne $current) {
        $matches = @($Nodes | Where-Object {
            [string] $_.parentNodeId -ceq [string] $current.parentNodeId -and
            [string] $_.nodeType -ceq [string] $current.nodeType -and
            [string]::Equals([string] $_.name, [string] $current.name, [StringComparison]::OrdinalIgnoreCase)
        })
        if ($matches.Count -ne 1) { return $false }
        if ($null -eq $current.parentNodeId) { return ([string] $current.nodeType -ceq 'Device') }
        $parents = @($Nodes | Where-Object { [string] $_.nodeId -ceq [string] $current.parentNodeId })
        if ($parents.Count -ne 1) { return $false }
        $current = $parents[0]
    }
    return $false
}

function Find-EligibleDevice {
    param([object[]] $Nodes)

    $byId = @{}
    foreach ($node in $Nodes) { $byId[[string] $node.nodeId] = $node }
    foreach ($target in $Nodes) {
        if ((Get-NodeDepth -ById $byId -Node $target) -lt 3) { continue }
        if (-not (Test-UniqueNodeSelector -Nodes $Nodes -Node $target)) { continue }
        $root = $target
        while ($null -ne $root.parentNodeId) { $root = $byId[[string] $root.parentNodeId] }
        return $root
    }
    throw 'The complete project snapshot has no uniquely addressable Device with an addressable deep target.'
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

function Get-TreeIdentityEvidence {
    param([Parameter(Mandatory)] [object[]] $Nodes)

    $identity = @($Nodes | ForEach-Object {
        [ordered]@{
            nodeId = [string] $_.nodeId
            parentNodeId = if ($null -eq $_.parentNodeId) { $null } else { [string] $_.parentNodeId }
            sequence = [int] $_.sequence
            nodeType = [string] $_.nodeType
            name = [string] $_.name
        }
    })
    $canonicalIdentity = ConvertTo-Json -InputObject $identity -Compress -Depth 10
    return [ordered]@{
        nodeCount = $identity.Count
        sha256 = Get-Sha256 $canonicalIdentity
    }
}

function Get-MeasurementSpread {
    param([Parameter(Mandatory)] [double[]] $Values)

    Assert-Condition ($Values.Count -eq 3) 'Live comparison requires exactly three initial-snapshot measurements.'
    return [ordered]@{
        values = @($Values)
        min = [double] ($Values | Measure-Object -Minimum).Minimum
        median = [double] (Get-MedianMilliseconds $Values)
        max = [double] ($Values | Measure-Object -Maximum).Maximum
    }
}

function Compare-ProjectTreeEvidence {
    param(
        [Parameter(Mandatory)] [object] $Candidate,
        [Parameter(Mandatory)] [object] $Baseline
    )

    Assert-Condition ([int64] $Candidate.treeIdentity.nodeCount -eq [int64] $Baseline.treeIdentity.nodeCount) 'Candidate and baseline tree node counts differ.'
    Assert-Condition ([string] $Candidate.treeIdentity.sha256 -ceq [string] $Baseline.treeIdentity.sha256) 'Candidate and baseline tree identities differ.'

    $baselineTiming = Get-MeasurementSpread @($Baseline.timing.runs | ForEach-Object { [double] $_.elapsedMs })
    $candidateTiming = Get-MeasurementSpread @($Candidate.timing.runs | ForEach-Object { [double] $_.elapsedMs })
    $baselineSize = Get-MeasurementSpread @($Baseline.timing.runs | ForEach-Object { [double] $_.canonicalResponseChars })
    $candidateSize = Get-MeasurementSpread @($Candidate.timing.runs | ForEach-Object { [double] $_.canonicalResponseChars })

    $timingDelta = $candidateTiming.median - $baselineTiming.median
    $sizeDelta = $candidateSize.median - $baselineSize.median
    $timingDeltaPercent = if ($baselineTiming.median -eq 0) { $null } else { 100.0 * $timingDelta / $baselineTiming.median }
    $sizeDeltaPercent = if ($baselineSize.median -eq 0) { $null } else { 100.0 * $sizeDelta / $baselineSize.median }

    return [ordered]@{
        treeIdentityEqual = $true
        timingMs = [ordered]@{
            baseline = $baselineTiming
            candidate = $candidateTiming
            medianDelta = $timingDelta
            medianDeltaPercent = $timingDeltaPercent
        }
        initialResponseChars = [ordered]@{
            baseline = $baselineSize
            candidate = $candidateSize
            medianDelta = $sizeDelta
            medianDeltaPercent = $sizeDeltaPercent
        }
        thresholdApplied = $false
    }
}

function Find-AmbiguousSelector {
    param([Parameter(Mandatory)] [object[]] $Nodes)

    $groups = @{}
    foreach ($node in $Nodes) {
        $parentGroup = if ($null -eq $node.parentNodeId) {
            'project-root'
        }
        else {
            'node-parent' + "`u{001f}" + ([string] $node.parentNodeId)
        }
        $key = $parentGroup + "`u{001f}" + ([string] $node.nodeType) + "`u{001f}" + ([string] $node.name).ToUpperInvariant()
        if (-not $groups.ContainsKey($key)) {
            $groups[$key] = [System.Collections.Generic.List[object]]::new()
        }
        $groups[$key].Add($node)
    }

    foreach ($group in $groups.Values) {
        if ($group.Count -gt 1) {
            $representative = $group[0]
            if ($null -eq $representative.parentNodeId) {
                return @([ordered]@{ nodeType = [string] $representative.nodeType; name = [string] $representative.name })
            }
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
                canonicalResponseChars = $_.response.__contentText.Length
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

$runConfiguration = Resolve-BlockHeaderRunConfiguration `
    -AcceptanceProfile $AcceptanceProfile `
    -ExpectationsPath $BlockHeaderExpectationsPath `
    -BaselinePath $BaselineEvidencePath
$blockHeaderExpectations = $null
$baselineEvidence = $null
$resolvedBaselineEvidencePath = $null
if ($runConfiguration.mode -ceq 'candidate') {
    $resolvedExpectationsPath = (Resolve-Path -LiteralPath $BlockHeaderExpectationsPath).Path
    $resolvedBaselineEvidencePath = (Resolve-Path -LiteralPath $BaselineEvidencePath).Path
    Assert-DistinctBlockHeaderArtifactPaths `
        -OutputEvidencePath $EvidencePath `
        -ExpectationsPath $resolvedExpectationsPath `
        -BaselinePath $resolvedBaselineEvidencePath
    $expectationsJson = Get-Content -LiteralPath $resolvedExpectationsPath -Raw
    Assert-Condition ($expectationsJson -notmatch '\bREPLACE_') 'Replace every REPLACE_ placeholder in the block-header expectations before live acceptance.'
    $blockHeaderExpectations = $expectationsJson | ConvertFrom-Json -Depth 100
    $baselineEvidence = Get-Content -LiteralPath $resolvedBaselineEvidencePath -Raw | ConvertFrom-Json -Depth 100
    Assert-BlockHeaderExpectationDocument -Expectations $blockHeaderExpectations
}

$resolvedProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
if ($runConfiguration.mode -ceq 'candidate') {
    Assert-BlockHeaderBaselineEvidence -Baseline $baselineEvidence -ExpectedProjectPath $resolvedProjectPath
}
$resolvedServerPath = (Resolve-Path -LiteralPath $ServerPath).Path
$runtimeArtifacts = Get-RuntimeArtifactProvenance -ResolvedServerPath $resolvedServerPath

$evidence = [ordered]@{
    schemaVersion = if ($runConfiguration.mode -ceq 'fullV3') { 'issue-32-project-tree-v3-live/v1' } else { 'issue-30-block-header-metadata-live/v1' }
    startedAt = [DateTimeOffset]::UtcNow.ToString('O')
    projectPath = $resolvedProjectPath
    serverPath = $resolvedServerPath
    serverSha256 = $runtimeArtifacts.host.sha256
    runtimeArtifacts = $runtimeArtifacts
    acceptanceProfile = $runConfiguration.profile
    runMode = $runConfiguration.mode
    status = 'running'
    toolNames = @()
    modes = [ordered]@{}
    blockHeaders = $null
    baselineEvidencePath = $resolvedBaselineEvidencePath
    baselineComparison = $null
    selectorReconstruction = $null
    selectorFailures = [ordered]@{}
    error = $null
}

Initialize-EvidenceDirectory
try {
    Connect-McpServer
    $evidence.toolNames = $script:ToolNames

    $fullMeasurement = Measure-InitialBrowse -Arguments @{ pageSize = 200 }
    $completedRuns = [System.Collections.Generic.List[object]]::new()
    $evidence.modes.fullProject = [ordered]@{
        arguments = [ordered]@{ pageSize = 200 }
        timing = Get-MeasurementEvidence $fullMeasurement
        snapshot = $null
        treeIdentity = $null
        completedRuns = $completedRuns
    }
    foreach ($run in $fullMeasurement.runs) {
        $fullPages = @(Read-AllSnapshotPages -FirstPage $run.response)
        $fullNodes = @(Get-SnapshotNodes -Pages $fullPages)
        $snapshot = Assert-CompleteSnapshotEvidence -Mode "fullProject/run-$($run.run)" -Pages $fullPages
        $treeIdentity = Get-TreeIdentityEvidence -Nodes $fullNodes
        if ($completedRuns.Count -gt 0) {
            $firstIdentity = $completedRuns[0].treeIdentity
            Assert-Condition (($treeIdentity.nodeCount -eq $firstIdentity.nodeCount) -and ($treeIdentity.sha256 -ceq $firstIdentity.sha256)) 'Measured fullProject tree identities differ between runs.'
        }
        $headerEvidence = $null
        if ($runConfiguration.mode -ceq 'candidate') {
            $headerEvidence = Assert-BlockHeaderExpectations -Pages $fullPages -Expectations $blockHeaderExpectations
            $evidence.blockHeaders = $headerEvidence
        }
        $completedRuns.Add([ordered]@{
            run = $run.run
            snapshot = $snapshot
            treeIdentity = $treeIdentity
            blockHeaders = $headerEvidence
        })
        # Keep the established summary fields describing the final completed run.
        $evidence.modes.fullProject.snapshot = $snapshot
        $evidence.modes.fullProject.treeIdentity = $treeIdentity
    }

    if ($runConfiguration.mode -ceq 'fullV3') {
        $depthMeasurement = Measure-InitialBrowse -Arguments @{ depth = 2; pageSize = 200 }
        $depthPages = @(Read-AllSnapshotPages -FirstPage $depthMeasurement.runs[-1].response)
        $evidence.modes.depthLimited = [ordered]@{
            arguments = [ordered]@{ depth = 2; pageSize = 200 }
            timing = Get-MeasurementEvidence $depthMeasurement
            snapshot = Assert-CompleteSnapshotEvidence -Mode 'depthLimited' -Pages $depthPages
        }

        $deviceNode = Find-EligibleDevice -Nodes $fullNodes
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
    }
    elseif ($runConfiguration.mode -ceq 'candidate') {
        $comparison = Compare-ProjectTreeEvidence `
            -Candidate $evidence.modes.fullProject `
            -Baseline $baselineEvidence.modes.fullProject
        $comparison['runtimeArtifacts'] = [ordered]@{
            baseline = $baselineEvidence.runtimeArtifacts
            candidate = $runtimeArtifacts
        }
        $evidence.baselineComparison = $comparison
    }

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
