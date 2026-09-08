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

    return [pscustomobject]@{
        result = $mcpResult.structuredContent
        contentText = [string] $mcpResult.content[0].text
        structuredJson = $structuredJson
        rawResponse = $response.Raw
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
    generatedAt = [DateTimeOffset]::UtcNow.ToString('O')
    projectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
    status = 'transport_ready'
    toolNames = @()
}

Initialize-EvidenceDirectory
try {
    Connect-McpServer
    $evidence.toolNames = $script:ToolNames
}
finally {
    Stop-McpServer
    Write-EvidenceArtifacts -Evidence $evidence
}
