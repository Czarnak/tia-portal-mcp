#Requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ProjectPath,
    [Parameter(Mandatory)] [string] $TypePath,
    [string] $HostExecutable = 'dotnet',
    [string[]] $HostArguments,
    [int] $StartupTimeoutSeconds = 30
)

if (-not $HostArguments -or $HostArguments.Count -eq 0) {
    $HostArguments = @('run', '--project', 'TiaMcpServer', '--', '--project', $ProjectPath)
}

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:ExpectedRegisteredToolNames = @(
    'execute_read_batch'
    'apply_write_batch'
    'archive_project'
    'close_project'
    'create_project'
    'open_project'
    'preview_write_batch'
    'save_project'
    'save_project_as'
)
$script:HostProcess = $null
$script:NextRequestId = 0

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resolvedProjectPath = [System.IO.Path]::GetFullPath($ProjectPath)

if ([string]::IsNullOrWhiteSpace($TypePath)) {
    throw 'TypePath must not be empty.'
}
if ($StartupTimeoutSeconds -le 0) {
    throw 'StartupTimeoutSeconds must be greater than zero.'
}

function Start-McpHost {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $HostExecutable
    foreach ($argument in $HostArguments) {
        [void] $startInfo.ArgumentList.Add($argument)
    }
    $startInfo.WorkingDirectory = $repositoryRoot
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $false
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    [void] $process.Start()
    $script:HostProcess = $process
}

function Stop-McpHost {
    if ($null -eq $script:HostProcess) {
        return
    }

    try {
        if (-not $script:HostProcess.HasExited) {
            try {
                $script:HostProcess.StandardInput.Close()
            }
            catch {
                # Best-effort shutdown continues below.
            }

            if (-not $script:HostProcess.WaitForExit(5000)) {
                $script:HostProcess.Kill($true)
                [void] $script:HostProcess.WaitForExit(5000)
            }
        }
    }
    finally {
        $script:HostProcess.Dispose()
        $script:HostProcess = $null
    }
}

function Send-McpMessage {
    param([hashtable] $Message)

    $json = $Message | ConvertTo-Json -Compress -Depth 40
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

        try {
            $response = $line | ConvertFrom-Json -Depth 80
        }
        catch {
            continue
        }

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
        throw "MCP request '$Method' returned an error."
    }

    return $response.result
}

function Invoke-McpNotification {
    param([string] $Method)

    Send-McpMessage -Message @{ jsonrpc = '2.0'; method = $Method; params = @{} }
}

function Invoke-PreviewToolCall {
    param([string] $ToolName, [hashtable] $Arguments)

    $result = Invoke-McpRequest -Method 'tools/call' -Params @{
        name      = $ToolName
        arguments = $Arguments
    }
    $isErrorProperty = $result.PSObject.Properties['isError']
    if ($null -ne $isErrorProperty -and [bool] $isErrorProperty.Value) {
        throw "Tool '$ToolName' returned isError:true."
    }

    return $result
}

function Get-ToolResultDocument {
    param([object] $ToolResult, [string] $ToolName)

    $content = @($ToolResult.content)
    if ($content.Count -eq 0) {
        throw "Tool '$ToolName' returned no content blocks."
    }

    $text = [string] $content[0].text
    if ([string]::IsNullOrWhiteSpace($text)) {
        throw "Tool '$ToolName' returned no text content."
    }

    try {
        $document = $text | ConvertFrom-Json -Depth 80
    }
    catch {
        throw "Tool '$ToolName' did not return a JSON document."
    }

    return $document
}

function Assert-ExpectedToolNames {
    param([object] $ToolsResult)

    $actualNames = @($ToolsResult.tools | ForEach-Object { [string] $_.name })
    $observedNames = @(
        $actualNames |
            Where-Object { $script:ExpectedRegisteredToolNames -ccontains $_ } |
            Sort-Object -CaseSensitive
    )
    $expectedNames = @($script:ExpectedRegisteredToolNames | Sort-Object -CaseSensitive)

    if ($observedNames.Count -ne $expectedNames.Count) {
        throw "tools/list did not expose all nine expected registered tool names."
    }

    $difference = Compare-Object -ReferenceObject $expectedNames -DifferenceObject $observedNames -CaseSensitive
    if ($null -ne $difference) {
        throw 'tools/list did not match the exact nine-name registered-tool census.'
    }

    return $observedNames
}

function Get-ReadSourceContent {
    param([object] $ToolResult)

    $document = Get-ToolResultDocument -ToolResult $ToolResult -ToolName 'execute_read_batch'
    $operations = @($document.operations)
    if ($operations.Count -ne 1) {
        throw 'execute_read_batch did not return exactly one operation result.'
    }

    $operation = $operations[0]
    if ($operation.operationId -cne 'read-type-content' -or $operation.status -cne 'succeeded') {
        throw 'execute_read_batch did not return the expected successful operation result.'
    }

    $sourceContent = [string] $operation.result
    if ([string]::IsNullOrWhiteSpace($sourceContent)) {
        throw 'execute_read_batch returned empty type content.'
    }

    return $sourceContent
}

function Get-RequiredPreviewProperty {
    param([object] $Document, [string] $Name, [string] $ToolName)

    $property = $Document.PSObject.Properties[$Name]
    if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string] $property.Value)) {
        throw "Tool '$ToolName' did not return '$Name'."
    }

    return [string] $property.Value
}

function Get-PreviewEvidence {
    param([object] $ToolResult, [string] $ToolName)

    $document = Get-ToolResultDocument -ToolResult $ToolResult -ToolName $ToolName
    $token = Get-RequiredPreviewProperty -Document $document -Name 'safetyToken' -ToolName $ToolName
    if ([string]::IsNullOrWhiteSpace($token)) {
        throw "Tool '$ToolName' returned an empty token."
    }

    return [pscustomobject] [ordered]@{
        tokenStatus       = 'token present (redacted)'
        requestedInputHash = Get-RequiredPreviewProperty -Document $document -Name 'requestedInputHash' -ToolName $ToolName
        currentStateHash   = Get-RequiredPreviewProperty -Document $document -Name 'currentStateHash' -ToolName $ToolName
        instructions       = Get-RequiredPreviewProperty -Document $document -Name 'instructions' -ToolName $ToolName
    }
}

try {
    Start-McpHost

    $null = Invoke-McpRequest -Method 'initialize' -Params @{
        protocolVersion = '2025-06-18'
        capabilities    = @{}
        clientInfo      = @{ name = 'live-test-write-safety-pr2-registered-tools'; version = '1.0.0' }
    }
    Invoke-McpNotification -Method 'notifications/initialized'

    $toolsResult = Invoke-McpRequest -Method 'tools/list'
    $registeredToolNames = Assert-ExpectedToolNames -ToolsResult $toolsResult

    $readResult = Invoke-PreviewToolCall -ToolName 'execute_read_batch' -Arguments @{
        operations = @(
            @{
                operationId = 'read-type-content'
                operation   = 'get_type_content'
                typePath    = $TypePath
                projectPath = $resolvedProjectPath
            }
        )
    }
    $sourceContent = Get-ReadSourceContent -ToolResult $readResult

    $batchPreviewResult = Invoke-PreviewToolCall -ToolName 'preview_write_batch' -Arguments @{
        operations = @(
            @{
                operationId  = 'preview-update-type-content'
                operation    = 'update_type_content'
                typePath     = $TypePath
                sourceContent = $sourceContent
                projectPath  = $resolvedProjectPath
            }
        )
    }
    $batchEvidence = Get-PreviewEvidence -ToolResult $batchPreviewResult -ToolName 'preview_write_batch'

    $lifecyclePreviewResult = Invoke-PreviewToolCall -ToolName 'save_project' -Arguments @{
        projectPath = $resolvedProjectPath
    }
    $lifecycleEvidence = Get-PreviewEvidence -ToolResult $lifecyclePreviewResult -ToolName 'save_project'

    Write-Output "registered tools: $($registeredToolNames -join ', ')"
    Write-Output "generic batch safetyToken: $($batchEvidence.tokenStatus)"
    Write-Output "generic batch requestedInputHash: $($batchEvidence.requestedInputHash)"
    Write-Output "generic batch currentStateHash: $($batchEvidence.currentStateHash)"
    Write-Output "generic batch instructions: $($batchEvidence.instructions)"
    Write-Output "lifecycle safetyToken: $($lifecycleEvidence.tokenStatus)"
    Write-Output "lifecycle requestedInputHash: $($lifecycleEvidence.requestedInputHash)"
    Write-Output "lifecycle currentStateHash: $($lifecycleEvidence.currentStateHash)"
    Write-Output "lifecycle instructions: $($lifecycleEvidence.instructions)"
    Write-Output 'No apply call was issued; this harness performed preview and read calls only.'
}
finally {
    Stop-McpHost
}
