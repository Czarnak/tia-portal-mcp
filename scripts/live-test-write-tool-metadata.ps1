#Requires -Version 7
<#
.SYNOPSIS
    Separately authorized, non-mutating TIA Portal V21 acceptance harness for PR 1 explicit MCP
    tool annotations.

.DESCRIPTION
    Launches the real TiaMcpServer host twice over stdio: once in --read-only mode and once in
    --read-write mode. Each host receives initialize, notifications/initialized, tools/list, and
    the one benign tools/call request get_project_status. The harness neither talks to the net48
    worker directly nor invokes any write, preview, or apply tool.

    This script is not an ordinary test or CI action. Run it only with separate authorization,
    while the supplied disposable .ap21 project is already open in TIA Portal V21. The generated
    report proves only the live host/project/session combination it records; static, stub, and
    FakeWorker evidence remain separate evidence classes.

.PARAMETER ProjectPath
    Absolute path to the already-open, disposable TIA Portal V21 .ap21 project to verify.

.PARAMETER ReportPath
    Required explicit destination. It must be the dated PR 1 report under
    docs/superpowers/acceptance/reports so a live run replaces the pending evidence template.

.EXAMPLE
    pwsh -NoProfile -File scripts/live-test-write-tool-metadata.ps1 `
        -ProjectPath 'C:\Sandbox\ExplicitToolAnnotations.ap21' `
        -ReportPath 'docs\superpowers\acceptance\reports\2026-09-01-pr1-explicit-mcp-tool-annotations-live.md'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ProjectPath,

    [Parameter(Mandatory)]
    [string] $ReportPath,

    [int] $StartupTimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:NextRequestId = 0
$script:HostProcess = $null
$script:ExpectedReadOnlyToolCount = 4
$script:ExpectedReadWriteToolCount = 14
$script:ExpectedReadOnlyToolNames = @(
    'browse_project_tree',
    'execute_read_batch',
    'get_project_status',
    'network_read'
)
$script:ExpectedReadWriteToolNames = @(
    'apply_write_batch',
    'archive_project',
    'browse_project_tree',
    'close_project',
    'compile_check',
    'create_project',
    'execute_read_batch',
    'get_project_status',
    'network_read',
    'network_write',
    'open_project',
    'preview_write_batch',
    'save_project',
    'save_project_as'
)
$script:AnnotatedWriteToolNames = @(
    'preview_write_batch',
    'apply_write_batch',
    'open_project',
    'create_project',
    'save_project',
    'save_project_as',
    'archive_project',
    'close_project'
)
$script:ExpectedWriteToolAnnotations = @{
    'preview_write_batch' = @{
        readOnlyHint    = $true
        destructiveHint = $false
        openWorldHint   = $false
    }
    'apply_write_batch' = @{
        readOnlyHint    = $false
        destructiveHint = $true
        openWorldHint   = $false
    }
    'open_project' = @{
        readOnlyHint    = $false
        destructiveHint = $true
        openWorldHint   = $false
    }
    'create_project' = @{
        readOnlyHint    = $false
        destructiveHint = $true
        openWorldHint   = $false
    }
    'save_project' = @{
        readOnlyHint    = $false
        destructiveHint = $true
        openWorldHint   = $false
    }
    'save_project_as' = @{
        readOnlyHint    = $false
        destructiveHint = $true
        openWorldHint   = $false
    }
    'archive_project' = @{
        readOnlyHint    = $false
        destructiveHint = $true
        openWorldHint   = $false
    }
    'close_project' = @{
        readOnlyHint    = $false
        destructiveHint = $true
        openWorldHint   = $false
    }
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$expectedReportPath = Join-Path $repositoryRoot 'docs\superpowers\acceptance\reports\2026-09-01-pr1-explicit-mcp-tool-annotations-live.md'
$resolvedReportPath = [System.IO.Path]::GetFullPath($ReportPath, (Get-Location).Path)
$resolvedProjectPath = [System.IO.Path]::GetFullPath($ProjectPath, (Get-Location).Path)

if (-not [string]::Equals($resolvedReportPath, $expectedReportPath, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "-ReportPath must be '$expectedReportPath' so this run records the intended dated acceptance evidence."
}

if (-not $resolvedProjectPath.EndsWith('.ap21', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "-ProjectPath must name a .ap21 project, not '$resolvedProjectPath'."
}

if (-not (Test-Path -LiteralPath $resolvedProjectPath -PathType Leaf)) {
    throw "-ProjectPath does not exist: '$resolvedProjectPath'."
}

function Start-McpHost {
    param([string] $AccessModeArgument)

    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = 'dotnet'
    foreach ($argument in @('run', '--project', 'TiaMcpServer', '--', '--project', $resolvedProjectPath, $AccessModeArgument)) {
        [void] $psi.ArgumentList.Add($argument)
    }
    $psi.WorkingDirectory = $repositoryRoot
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
        try { $script:HostProcess.StandardInput.Close() } catch { }
        if (-not $script:HostProcess.WaitForExit(5000)) {
            $script:HostProcess.Kill($true)
        }
    }

    $script:HostProcess = $null
}

function Send-McpMessage {
    param([hashtable] $Message)

    $json = $Message | ConvertTo-Json -Compress -Depth 30
    $script:HostProcess.StandardInput.WriteLine($json)
    $script:HostProcess.StandardInput.Flush()
}

function Read-McpResponse {
    param([int] $Id)

    $deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
    $pendingReadTask = $null
    while ((Get-Date) -lt $deadline) {
        if ($script:HostProcess.HasExited) {
            throw "The MCP host exited with code $($script:HostProcess.ExitCode) before responding to request id $Id."
        }

        if ($null -eq $pendingReadTask) {
            $pendingReadTask = $script:HostProcess.StandardOutput.ReadLineAsync()
        }

        $remainingMilliseconds = [int] [Math]::Max(0, ($deadline - (Get-Date)).TotalMilliseconds)
        if (-not $pendingReadTask.Wait($remainingMilliseconds)) {
            continue
        }

        $line = $pendingReadTask.Result
        $pendingReadTask = $null
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        try { $response = $line | ConvertFrom-Json -Depth 80 } catch { continue }
        if ($null -ne $response.PSObject.Properties['id'] -and $response.id -eq $Id) {
            return $response
        }
    }

    throw "Timed out after $StartupTimeoutSeconds second(s) waiting for MCP request id $Id."
}

function Invoke-McpRequest {
    param([string] $Method, [hashtable] $Params = @{})

    $id = ++$script:NextRequestId
    Send-McpMessage -Message @{ jsonrpc = '2.0'; id = $id; method = $Method; params = $Params }
    $response = Read-McpResponse -Id $id
    if ($null -ne $response.PSObject.Properties['error'] -and $null -ne $response.error) {
        throw "MCP request '$Method' returned: $($response.error | ConvertTo-Json -Compress -Depth 20)"
    }

    return $response.result
}

function Invoke-McpNotification {
    param([string] $Method)

    Send-McpMessage -Message @{ jsonrpc = '2.0'; method = $Method; params = @{} }
}

function Invoke-McpToolCall {
    param([string] $Name, [hashtable] $Arguments)

    $result = Invoke-McpRequest -Method 'tools/call' -Params @{ name = $Name; arguments = $Arguments }
    $isError = $result.PSObject.Properties['isError']
    if ($null -ne $isError -and [bool] $isError.Value) {
        throw "Tool '$Name' returned isError:true: $($result | ConvertTo-Json -Compress -Depth 40)"
    }

    return $result
}

function Get-PropertyValue {
    param([object] $Object, [string] $Name)

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Assert-ToolSurface {
    param(
        [string] $Mode,
        [object[]] $ActualNames,
        [string[]] $ExpectedNames,
        [int] $ExpectedCount
    )

    $actual = @($ActualNames | Sort-Object)
    if ($actual.Count -ne $ExpectedCount) {
        throw "$Mode tools/list returned $($actual.Count) tools; expected exactly ${ExpectedCount}: $($actual -join ', ')."
    }

    $difference = Compare-Object -ReferenceObject $ExpectedNames -DifferenceObject $actual
    if ($null -ne $difference) {
        throw "$Mode tools/list did not match the approved surface: $($difference | Out-String)."
    }

    return $actual
}

function Get-ToolAnnotationEvidence {
    param([object[]] $Tools)

    $byName = @{}
    foreach ($tool in $Tools) {
        $byName[$tool.name] = $tool
    }

    $records = @()
    foreach ($toolName in $script:AnnotatedWriteToolNames) {
        if (-not $byName.ContainsKey($toolName)) {
            throw "Read-write tools/list did not advertise required annotated tool '$toolName'."
        }

        $records += Assert-ToolAnnotationEvidence -ToolName $toolName -Annotations (Get-PropertyValue -Object $byName[$toolName] -Name 'annotations')
    }

    return $records
}

function Assert-ToolAnnotationEvidence {
    param([string] $ToolName, [object] $Annotations)

    if ($null -eq $Annotations) {
        throw "Read-write tools/list omitted annotations for '$ToolName'."
    }

    if (-not $script:ExpectedWriteToolAnnotations.ContainsKey($ToolName)) {
        throw "No approved annotation matrix entry exists for '$ToolName'."
    }

    $expected = $script:ExpectedWriteToolAnnotations[$ToolName]
    $actual = [ordered]@{ name = $ToolName }
    foreach ($hintName in @('readOnlyHint', 'destructiveHint', 'openWorldHint')) {
        $actualValue = Get-PropertyValue -Object $Annotations -Name $hintName
        if ($actualValue -isnot [bool] -or $actualValue -ne $expected[$hintName]) {
            throw "Read-write tools/list annotation mismatch for '$ToolName.$hintName': expected '$($expected[$hintName])', actual '$actualValue'."
        }

        $actual[$hintName] = $actualValue
    }

    return [pscustomobject] $actual
}

function Get-ToolResultText {
    param([object] $ToolResult)

    $content = @(Get-PropertyValue -Object $ToolResult -Name 'content')
    if ($content.Count -eq 0) {
        throw 'get_project_status returned no content blocks.'
    }

    $text = Get-PropertyValue -Object $content[0] -Name 'text'
    if ([string]::IsNullOrWhiteSpace($text)) {
        throw 'get_project_status returned no text content.'
    }

    return $text
}

function Get-TiaPortalVersion {
    param([string] $ProjectStatusText)

    try { $payload = $ProjectStatusText | ConvertFrom-Json -Depth 80 } catch {
        throw "Could not parse the benign get_project_status result as JSON: $($_.Exception.Message)"
    }

    $identity = Get-PropertyValue -Object $payload -Name 'sessionIdentity'
    $portalProcessId = Get-PropertyValue -Object $identity -Name 'portalProcessId'
    if ($null -eq $portalProcessId) {
        throw 'get_project_status did not return a portalProcessId from which to record the attached TIA Portal version.'
    }

    $portalProcess = Get-Process -Id ([int] $portalProcessId) -ErrorAction Stop
    $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($portalProcess.Path).ProductVersion
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "The attached TIA Portal process '$($portalProcess.ProcessName)' did not expose a ProductVersion."
    }

    return $version
}

function Invoke-LiveSession {
    param(
        [string] $Mode,
        [string] $AccessModeArgument,
        [string[]] $ExpectedNames,
        [int] $ExpectedCount
    )

    try {
        Start-McpHost -AccessModeArgument $AccessModeArgument
        $initializeResult = Invoke-McpRequest -Method 'initialize' -Params @{
            protocolVersion = '2025-06-18'
            capabilities    = @{}
            clientInfo      = @{ name = 'live-test-write-tool-metadata'; version = '1.0.0' }
        }
        Invoke-McpNotification -Method 'notifications/initialized'

        $toolsResult = Invoke-McpRequest -Method 'tools/list'
        $tools = @($toolsResult.tools)
        $toolNames = Assert-ToolSurface -Mode $Mode -ActualNames @($tools | ForEach-Object { $_.name }) -ExpectedNames $ExpectedNames -ExpectedCount $ExpectedCount

        $projectStatus = Invoke-McpToolCall -Name 'get_project_status' -Arguments @{ projectPath = $resolvedProjectPath }
        $projectStatusText = Get-ToolResultText -ToolResult $projectStatus

        return [pscustomobject] [ordered]@{
            mode                = $Mode
            serverName          = Get-PropertyValue -Object (Get-PropertyValue -Object $initializeResult -Name 'serverInfo') -Name 'name'
            serverVersion       = Get-PropertyValue -Object (Get-PropertyValue -Object $initializeResult -Name 'serverInfo') -Name 'version'
            toolCount           = $toolNames.Count
            toolNames           = $toolNames
            writeToolAnnotations = if ($Mode -eq 'read-write') { Get-ToolAnnotationEvidence -Tools $tools } else { @() }
            projectStatusResult = $projectStatusText
            tiaPortalVersion    = Get-TiaPortalVersion -ProjectStatusText $projectStatusText
        }
    }
    finally {
        Stop-McpHost
    }
}

function Convert-ToMarkdownCodeBlock {
    param([object] $Value)

    return ('```json' + [Environment]::NewLine + ($Value | ConvertTo-Json -Depth 80) + [Environment]::NewLine + '```')
}

$readOnlySession = Invoke-LiveSession -Mode 'read-only' -AccessModeArgument '--read-only' -ExpectedNames $script:ExpectedReadOnlyToolNames -ExpectedCount $script:ExpectedReadOnlyToolCount
$readWriteSession = Invoke-LiveSession -Mode 'read-write' -AccessModeArgument '--read-write' -ExpectedNames $script:ExpectedReadWriteToolNames -ExpectedCount $script:ExpectedReadWriteToolCount

if (-not [string]::Equals($readOnlySession.tiaPortalVersion, $readWriteSession.tiaPortalVersion, [System.StringComparison]::Ordinal)) {
    throw "The two host sessions resolved different TIA Portal product versions ('$($readOnlySession.tiaPortalVersion)' and '$($readWriteSession.tiaPortalVersion)')."
}

$reportLines = @(
    '# PR 1 Explicit MCP Tool Annotations — Live TIA Portal V21 Acceptance',
    '',
    '## Evidence status',
    '',
    'Live acceptance completed by this separately authorized, non-mutating run. This evidence proves only the exact live TiaMcpServer host, attached TIA Portal session, and disposable project path recorded below. It does not replace offline, stub, or FakeWorker evidence, and those evidence classes do not replace this live run.',
    '',
    '## Tested environment',
    '',
    "- TIA Portal product version: $($readOnlySession.tiaPortalVersion)",
    ('- Project copy path: `' + $resolvedProjectPath + '`'),
    ('- Harness report path: `' + $resolvedReportPath + '`'),
    "- Read-only server: $($readOnlySession.serverName) $($readOnlySession.serverVersion)",
    "- Read-write server: $($readWriteSession.serverName) $($readWriteSession.serverVersion)",
    '',
    '## Read-only MCP surface',
    '',
    "Expected and observed exactly $($readOnlySession.toolCount) tools:",
    '',
    (Convert-ToMarkdownCodeBlock -Value $readOnlySession.toolNames),
    '',
    'Benign call: `tools/call` for `get_project_status` with the project copy path. Result summary:',
    '',
    (Convert-ToMarkdownCodeBlock -Value ($readOnlySession.projectStatusResult | ConvertFrom-Json -Depth 80)),
    '',
    '## Read-write MCP surface',
    '',
    "Expected and observed exactly $($readWriteSession.toolCount) tools:",
    '',
    (Convert-ToMarkdownCodeBlock -Value $readWriteSession.toolNames),
    '',
    'Emitted annotations for the approved write-tool matrix:',
    '',
    (Convert-ToMarkdownCodeBlock -Value $readWriteSession.writeToolAnnotations),
    '',
    'Benign call: `tools/call` for `get_project_status` with the project copy path. Result summary:',
    '',
    (Convert-ToMarkdownCodeBlock -Value ($readWriteSession.projectStatusResult | ConvertFrom-Json -Depth 80)),
    '',
    '## Non-mutation and evidence boundary',
    '',
    'The harness sent only `initialize`, `notifications/initialized`, `tools/list`, and one `get_project_status` `tools/call` per access mode. It did not call any lifecycle, preview, apply, compilation, network-write, or PLC-control operation; no project mutation was performed. PLC `start_plc` and `stop_plc` remain deferred.',
    '',
    'The explicit MCP annotations are client-facing, untrusted metadata. Server-enforced access policy, preview/token/apply validation, binding checks, and auditing remain the write-safety authority.'
)

[System.IO.Directory]::CreateDirectory((Split-Path -Parent $resolvedReportPath)) | Out-Null
[System.IO.File]::WriteAllText($resolvedReportPath, ($reportLines -join [Environment]::NewLine) + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
Write-Host "Live acceptance evidence written to $resolvedReportPath"
