#Requires -Version 7
<#
.SYNOPSIS
    Separately authorized host-level structured preview diff acceptance on a disposable project.
.DESCRIPTION
    Requires TIA Portal V21 with the exact disposable project already open, and source-exportable
    block/type targets. Preview performs reads and previews only. Apply additionally requires
    -AllowApply and typed confirmation; -ConfirmApplyForCi is ONLY for explicitly authorized CI.
    Never invoke from ordinary tests or CI. Source contract tests read this file as text only.
    Source restoration compares UTF-8 bytes of the public exported text, not project-file bytes.
    Compile may leave the disposable project modified in memory; this harness never saves it.
    If restoration fails, original source files remain in the run evidence directory for recovery.
    No transaction or rollback is claimed. Review the report even when this script fails.
#>
[CmdletBinding()]
param(
    [ValidateSet('Preview', 'Apply')]
    [string] $Mode = 'Preview',
    [Parameter(Mandatory)] [string] $ProjectPath,
    [Parameter(Mandatory)] [string] $BlockPath,
    [Parameter(Mandatory)] [string] $TypePath,
    [switch] $AllowApply,
    [switch] $ConfirmApplyForCi,
    [string] $HostDllPath = "TiaMcpServer/bin/Debug/net8.0/TiaMcpServer.dll"
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Mode -eq 'Apply' -and -not $AllowApply) {
    throw 'Apply mode is disabled by default. Use -AllowApply only for an explicitly authorized disposable project copy.'
}
if ($ConfirmApplyForCi -and $Mode -ne 'Apply') { throw '-ConfirmApplyForCi requires Apply mode.' }
if (-not [IO.Path]::IsPathFullyQualified($ProjectPath)) { throw 'ProjectPath must be an absolute disposable project path.' }
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).ProviderPath
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not [IO.Path]::IsPathFullyQualified($HostDllPath)) { $HostDllPath = Join-Path $repoRoot $HostDllPath }
$HostDllPath = (Resolve-Path -LiteralPath $HostDllPath).ProviderPath
$hostHash = (Get-FileHash -LiteralPath $HostDllPath -Algorithm SHA256).Hash
$reportPath = Join-Path $repoRoot 'docs/superpowers/acceptance/reports/2026-09-01-pr4-structured-preview-diff-live.md'
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$script:HostProcess = $null
$script:RequestId = 0
$script:InitialIdentity = $null
$evidence = [ordered]@{
    Binding = 'PENDING'; Block = 'NOT RUN'; Type = 'NOT RUN'; LineEnding = 'NOT RUN'
    Oversized = 'NOT RUN'; Authorization = 'No apply authorized by Preview mode'
    Applied = 'NOT RUN'; Restore = 'NOT RUN'; Bytes = 'NOT RUN'; Compile = 'NOT RUN'
    FinalState = 'NOT READ'; Outcome = 'INCOMPLETE'; TiaVersion = 'Not observed'
}

function Assert-Condition([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Get-TextHash([string] $Text) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($utf8.GetBytes($Text))).ToLowerInvariant()
}

function Invoke-Mcp([string] $Method, [hashtable] $Parameters) {
    $script:RequestId++
    $request = @{ jsonrpc = '2.0'; id = $script:RequestId; method = $Method; params = $Parameters }
    $script:HostProcess.StandardInput.WriteLine(($request | ConvertTo-Json -Depth 100 -Compress))
    $script:HostProcess.StandardInput.Flush()
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        $remaining = 180000 - [int]$timer.ElapsedMilliseconds
        Assert-Condition ($remaining -gt 0) "MCP response timed out for $Method; state may be unknown."
        $read = $script:HostProcess.StandardOutput.ReadLineAsync()
        Assert-Condition ($read.Wait($remaining)) "MCP response timed out for $Method; state may be unknown."
        $line = $read.GetAwaiter().GetResult()
        Assert-Condition ($null -ne $line) "Host closed stdout during $Method."
        $response = ConvertFrom-Json -InputObject $line -AsHashtable
        if (-not $response.ContainsKey('id')) { continue }
        Assert-Condition ($response.id -eq $script:RequestId) 'Unexpected MCP response id.'
        Assert-Condition (-not $response.ContainsKey('error')) "MCP $Method failed."
        Assert-Condition ($response.ContainsKey('result')) 'MCP response has no result.'
        return $response.result
    }
}

function Invoke-Tool([string] $Name, [hashtable] $Arguments) {
    $result = Invoke-Mcp 'tools/call' @{ name = $Name; arguments = $Arguments }
    Assert-Condition (-not ($result.ContainsKey('isError') -and $result.isError)) "MCP tool $Name failed."
    $textItems = @($result.content | Where-Object { $_.type -eq 'text' })
    Assert-Condition ($textItems.Count -eq 1) "$Name must return exactly one text document."
    $document = ConvertFrom-Json -InputObject $textItems[0].text -AsHashtable
    Assert-Condition (-not ($document.ContainsKey('success') -and $document.success -ne $true)) "$Name reported failure."
    return $document
}

function Read-Binding {
    $status = Invoke-Tool 'get_project_status' @{ projectPath = $ProjectPath }
    Assert-Condition ($status.success -eq $true) 'Project status envelope failed.'
    $payload = ConvertFrom-Json -InputObject $status.payload -AsHashtable
    Assert-Condition ($payload.success -eq $true -and $payload.project.isOpen -eq $true) 'Target project is not open.'
    $identity = $status.sessionIdentity
    foreach ($path in @($payload.projectPath, $payload.project.path, $identity.projectPath)) {
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($path)) 'Project identity path is missing.'
        Assert-Condition ([string]::Equals([IO.Path]::GetFullPath($path), $ProjectPath, [StringComparison]::OrdinalIgnoreCase)) 'Active project differs from the explicit disposable target.'
    }
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($identity.workerSessionId)) 'Missing worker session id.'
    Assert-Condition ($identity.sessionGeneration -gt 0 -and $identity.portalProcessId -gt 0) 'Incomplete session identity.'
    $signature = '{0}/{1}/{2}' -f $identity.workerSessionId, $identity.sessionGeneration, $identity.portalProcessId
    if ($null -ne $script:InitialIdentity) {
        Assert-Condition ($signature -ceq $script:InitialIdentity) 'Project session changed during acceptance; refusing further work.'
    }
    $script:InitialIdentity = $signature
    $evidence.Binding = "PASS: exact project path and worker/session/Portal identity $signature"
    $evidence.TiaVersion = "V21 prerequisite; project version reported: $($payload.project.version)"
    return $payload.project
}

function Read-OriginalText {
    $readOperations = @(
        @{ operationId = 'read-block'; operation = 'get_block_content'; projectPath = $ProjectPath; blockPath = $BlockPath; format = 'source' },
        @{ operationId = 'read-type'; operation = 'get_type_content'; projectPath = $ProjectPath; typePath = $TypePath; format = 'source' }
    )
    $readResult = Invoke-Tool 'execute_read_batch' @{ operations = $readOperations }
    Assert-Condition ($readResult.success -eq $true -and $readResult.operations.Count -eq 2) 'Incomplete source read.'
    $texts = @()
    for ($i = 0; $i -lt 2; $i++) {
        $item = $readResult.operations[$i]
        Assert-Condition ($item.operationId -ceq $readOperations[$i].operationId -and $item.status -eq 'succeeded') 'Source read identity/status mismatch.'
        Assert-Condition (@($item.warnings | Where-Object { $null -ne $_ }).Count -eq 0) 'Source read has warnings; exact restoration cannot be assumed.'
        Assert-Condition ($item.result -is [string] -and $item.result.Length -gt 0) 'Source read is empty or invalid.'
        Assert-Condition ($item.result -notmatch '\[(TRUNCATED|OMITTED)') 'Source read was truncated or omitted.'
        $texts += $item.result
    }
    return ,$texts
}

function New-BlockOperation([string] $Id, [string] $Text) {
    return @{ operationId = $Id; operation = 'update_block_logic'; projectPath = $ProjectPath; blockPath = $BlockPath; format = 'source'; yamlContent = $Text }
}
function New-TypeOperation([string] $Id, [string] $Text) {
    return @{ operationId = $Id; operation = 'update_type_content'; projectPath = $ProjectPath; typePath = $TypePath; format = 'source'; sourceContent = $Text }
}

function Get-Preview([array] $Operations) {
    $null = Read-Binding
    $preview = Invoke-Tool 'preview_write_batch' @{ operations = $Operations }
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($preview.safetyToken)) 'Preview did not issue a safety token.'
    $binding = $preview.projectBinding
    Assert-Condition ($binding.state -eq 'verified' -and $binding.revision -gt 0 -and -not [string]::IsNullOrWhiteSpace($binding.bindingId)) 'Preview did not retain a verified host binding.'
    $signature = '{0}/{1}/{2}' -f $binding.workerSessionId, $binding.sessionGeneration, $binding.portalProcessId
    Assert-Condition ($signature -ceq $script:InitialIdentity -and [string]::Equals($binding.projectPath, $ProjectPath, [StringComparison]::OrdinalIgnoreCase)) 'Preview binding differs from the verified target session.'
    Assert-Condition ($preview.diff.operations.Count -eq $Operations.Count) 'Structured diff is missing eligible operations.'
    for ($i = 0; $i -lt $Operations.Count; $i++) {
        Assert-Condition ($preview.diff.operations[$i].operationId -ceq $Operations[$i].operationId) 'Diff order differs from request order.'
    }
    return $preview
}

# Keep existing source and bundle headers intact; add a comment inside the first declaration.
function Add-AcceptanceComment([string] $Text) {
    $declaration = [regex]::Match($Text, '(?m)^[ \t]*(?:TYPE|DATA_BLOCK|FUNCTION_BLOCK|FUNCTION|ORGANIZATION_BLOCK)\b[^\r\n]*(\r\n|\n)')
    Assert-Condition ($declaration.Success) 'Target must expose a source declaration with a line terminator.'
    return $Text.Insert($declaration.Index + $declaration.Length, '// PR4 disposable acceptance comment' + $declaration.Groups[1].Value)
}

# Report each run separately under the durable report, preserving prior evidence.
$runTime = [DateTime]::UtcNow.ToString('o')
$runDirectory = Join-Path ([IO.Path]::GetTempPath()) ('tia-preview-diff-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($runDirectory)
try {
    if ($Mode -eq 'Apply') {
        Write-Host "Disposable project: $ProjectPath`nBlock: $BlockPath`nType: $TypePath"
        if (-not $ConfirmApplyForCi) {
            $typed = Read-Host 'Type YES to apply temporary source comments, restore original text, and compile this disposable project'
            Assert-Condition ($typed -ceq 'YES') 'Apply was not confirmed.'
        }
        $evidence.Authorization = if ($ConfirmApplyForCi) { 'Explicit -AllowApply and -ConfirmApplyForCi (authorized CI only)' } else { 'Explicit -AllowApply and interactive YES' }
    }

    $startInfo = [Diagnostics.ProcessStartInfo]::new('dotnet')
    foreach ($argument in @($HostDllPath, '--read-write', '--project', $ProjectPath)) { $startInfo.ArgumentList.Add($argument) }
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.StandardInputEncoding = $utf8
    $startInfo.StandardOutputEncoding = $utf8
    $script:HostProcess = [Diagnostics.Process]::Start($startInfo)
    $initialize = Invoke-Mcp 'initialize' @{ protocolVersion = '2024-11-05'; capabilities = @{}; clientInfo = @{ name = 'preview-diff-acceptance'; version = '1.0' } }
    Assert-Condition ($initialize.protocolVersion -eq '2024-11-05') 'Unexpected negotiated MCP protocol version.'
    $script:HostProcess.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
    $script:HostProcess.StandardInput.Flush()
    $null = Read-Binding
    $original = Read-OriginalText
    [IO.File]::WriteAllText((Join-Path $runDirectory 'original-block.source.txt'), $original[0], $utf8)
    [IO.File]::WriteAllText((Join-Path $runDirectory 'original-type.source.txt'), $original[1], $utf8)
    $changedBlock = Add-AcceptanceComment $original[0]
    $changedType = Add-AcceptanceComment $original[1]

    if ($Mode -eq 'Preview') {
        $blockPreview = Get-Preview @(New-BlockOperation 'block-change' $changedBlock)
        $typePreview = Get-Preview @(New-TypeOperation 'type-change' $changedType)
        foreach ($preview in @($blockPreview, $typePreview)) {
            $entry = $preview.diff.operations[0]
            Assert-Condition ($entry.rawTextEqual -eq $false -and $entry.normalizedLinesEqual -eq $false -and $entry.requested.excerpt.lines.Count -gt 0) 'Expected a real content change and retained structured excerpt.'
        }
        $evidence.Block = 'PASS: source content change with structured diff'
        $evidence.Type = 'PASS: source content change with structured diff'
        $lf = $original[0].Replace("`r`n", "`n")
        $eolText = if ($lf -ceq $original[0]) { $lf.Replace("`n", "`r`n") } else { $lf }
        Assert-Condition ($eolText -cne $original[0]) 'Block needs CRLF or LF line endings for the line-ending-only case.'
        $eolPreview = Get-Preview @(New-BlockOperation 'line-endings' $eolText)
        $entry = $eolPreview.diff.operations[0]
        Assert-Condition ($entry.rawTextEqual -eq $false -and $entry.normalizedLinesEqual -eq $true -and $entry.lineEndingOnly -eq $true) 'Line-ending-only flags are incorrect.'
        $evidence.LineEnding = 'PASS: rawTextEqual=false, normalizedLinesEqual=true, lineEndingOnly=true'

        # One long changed line proves the 512-character per-line cap independently of the side cap.
        $linePreview = Get-Preview @(New-TypeOperation 'long-line' ($original[1] + "`n" + ('x' * 1024)))
        $line = @($linePreview.diff.operations[0].requested.excerpt.lines | Where-Object { $_.text.Length -eq 512 -and $_.omittedCharacterCount -gt 0 })
        Assert-Condition ($line.Count -gt 0) 'Per-line truncation did not retain 512 characters.'
        # 60 requested lines of 512 characters exercise both side caps; nine copies exceed
        # the 32,768-character/320-line whole-batch budgets regardless of small original text.
        $oversizedText = ((1..60 | ForEach-Object { 'x' * 512 }) -join "`n")
        $largeOperations = @(1..9 | ForEach-Object { New-TypeOperation "oversized-$_" $oversizedText })
        $largeOperations += New-TypeOperation 'small-after-exhaustion' $changedType
        $largePreview = Get-Preview $largeOperations
        $repeatedPreview = Get-Preview $largeOperations
        Assert-Condition (($largePreview.diff | ConvertTo-Json -Depth 100 -Compress) -ceq ($repeatedPreview.diff | ConvertTo-Json -Depth 100 -Compress)) 'Repeated ordered preview diff is not deterministic.'
        $first = $largePreview.diff.operations[0].requested.excerpt
        Assert-Condition ($first.lines.Count -eq 40 -and $first.omittedLineCount -gt 0 -and $first.omittedCharacterCount -gt 0) 'Per-side truncation evidence missing.'
        $retainedChars = ($first.lines | ForEach-Object { $_.text.Length } | Measure-Object -Sum).Sum
        Assert-Condition ($retainedChars -le 8192) 'Per-side character limit exceeded.'
        $exhausted = $false
        $firstExhausted = -1
        for ($i = 0; $i -lt $largePreview.diff.operations.Count; $i++) {
            $entry = $largePreview.diff.operations[$i]
            if ($entry.batchBudgetExhausted) {
                if (-not $exhausted) { $firstExhausted = $i }
                $exhausted = $true
            }
            if ($exhausted) {
                Assert-Condition ($entry.batchBudgetExhausted -eq $true -and $entry.current.excerpt.lines.Count -eq 0 -and $entry.requested.excerpt.lines.Count -eq 0) 'Later excerpts reappeared after batch exhaustion.'
                Assert-Condition ($entry.requested.characterCount -gt 0 -and $entry.requested.sha256.Length -eq 64) 'Exhausted entry lost raw-text metadata.'
            }
        }
        Assert-Condition ($exhausted -and $firstExhausted -gt 0) 'Whole-batch exhaustion not demonstrated.'
        $evidence.Oversized = "PASS: 512-character line cap; 40-line/8192-character side caps; deterministic exhaustion at zero-based index $firstExhausted including later small request"
        $diffEvidence = @{ block = $blockPreview.diff; type = $typePreview.diff; lineEnding = $eolPreview.diff; longLine = $linePreview.diff; oversized = $largePreview.diff }
        [IO.File]::WriteAllText((Join-Path $runDirectory 'preview-diffs.json'), ($diffEvidence | ConvertTo-Json -Depth 100), $utf8)
    }
    elseif ($Mode -eq 'Apply') {
        # All confirmed writes are lexically inside this explicitly gated Apply branch.
        $changes = @((New-BlockOperation 'change-block' $changedBlock), (New-TypeOperation 'change-type' $changedType))
        $restoration = @((New-BlockOperation 'restore-block' $original[0]), (New-TypeOperation 'restore-type' $original[1]))
        $changePreview = Get-Preview $changes
        for ($i = 0; $i -lt 2; $i++) {
            Assert-Condition ($changePreview.diff.operations[$i].current.sha256 -ceq (Get-TextHash $original[$i])) 'Source changed since original read; refusing apply and restoration of stale text.'
            Assert-Condition ($changePreview.diff.operations[$i].rawTextEqual -eq $false) 'Temporary comment did not produce a content change.'
        }
        $writeAttempted = $false
        try {
            $writeAttempted = $true
            $evidence.Applied = 'ATTEMPTED; outcome not yet verified'
            $applied = Invoke-Tool 'apply_write_batch' @{ operations = $changes; confirm = $true; safetyToken = $changePreview.safetyToken }
            Assert-Condition ($applied.success -eq $true -and $applied.succeeded -eq 2) 'Two-item apply did not succeed.'
            $evidence.Applied = 'PASS: both operations reported success; restoring original text next'
        }
        finally {
            # Also attempt restoration after a partial/failed apply. A changed session fails closed.
            if ($writeAttempted) {
                $evidence.Restore = 'ATTEMPTED; final state unknown until re-read'
                $restorePreview = Get-Preview $restoration
                $restored = Invoke-Tool 'apply_write_batch' @{ operations = $restoration; confirm = $true; safetyToken = $restorePreview.safetyToken }
                Assert-Condition ($restored.success -eq $true -and $restored.succeeded -eq 2) 'Restore batch did not succeed; use retained originals for recovery.'
                $evidence.Restore = 'PASS: both original source replacements reported success'
                $after = Read-OriginalText
                for ($i = 0; $i -lt 2; $i++) {
                    Assert-Condition ([Convert]::ToBase64String($utf8.GetBytes($after[$i])) -ceq [Convert]::ToBase64String($utf8.GetBytes($original[$i]))) "Target $i failed byte-identical restoration."
                }
                $evidence.Bytes = "PASS: byte-identical UTF-8 exported text; block SHA256=$(Get-TextHash $after[0]); type SHA256=$(Get-TextHash $after[1])"
                $null = Read-Binding
                $compileEnvelope = Invoke-Tool 'compile_check' @{ projectPath = $ProjectPath }
                $compile = ConvertFrom-Json -InputObject $compileEnvelope.payload -AsHashtable
                Assert-Condition ($compile.totalErrorCount -eq 0 -and $compile.plcs.Count -gt 0 -and $compile.overallState -ne 'Error') 'Compile must cover at least one PLC and report zero errors.'
                $evidence.Compile = "PASS: zero errors; warnings=$($compile.totalWarningCount); PLCs=$($compile.plcs.Count)"
            }
        }
    }
    $finalProject = Read-Binding
    $evidence.FinalState = "Same verified session; project isModified=$($finalProject.isModified). No save performed."
    $evidence.Outcome = "PASS for $Mode mode only"
}
catch {
    $evidence.Outcome = 'FAILED: ' + $_.Exception.Message
    throw
}
finally {
    if ($null -ne $script:HostProcess) {
        try {
            $script:HostProcess.StandardInput.Close()
            if (-not $script:HostProcess.WaitForExit(5000)) { $script:HostProcess.Kill($true) }
        }
        catch {
            $evidence.FinalState += ' Host cleanup failed: ' + $_.Exception.Message
            Write-Warning 'Host cleanup failed; inspect the acceptance report and child processes.'
        }
        finally { $script:HostProcess.Dispose() }
    }
    $report = @"

## Run $runTime ($Mode)

### Environment

- Date: $runTime
- TIA Portal version: $($evidence.TiaVersion)
- Host build: $HostDllPath; SHA256=$hostHash
- Disposable project path: $ProjectPath
- Binding verification: $($evidence.Binding)
- Block target: $BlockPath
- Type target: $TypePath
- Local evidence and original sources: $runDirectory

### Preview-Only Evidence

- Block preview: $($evidence.Block)
- Type preview: $($evidence.Type)
- Line-ending-only preview: $($evidence.LineEnding)
- Oversized batch preview: $($evidence.Oversized)

### Apply / Restore / Compile

- Apply authorization: $($evidence.Authorization)
- Applied changes: $($evidence.Applied)
- Restore result: $($evidence.Restore)
- Byte-identical re-read: $($evidence.Bytes)
- Compile result: $($evidence.Compile)
- Final state: $($evidence.FinalState)

### Evidence Boundary

- Outcome: $($evidence.Outcome)
- Proven: only checks marked PASS in this run, through the real host MCP protocol.
- Not proven: checks marked NOT RUN/INCOMPLETE; production or plant acceptance; disk project-byte identity; saved project state; semantic equivalence of replacements. Preview alone cannot qualify apply/restore/compile.
"@
    [IO.File]::AppendAllText($reportPath, $report + [Environment]::NewLine, $utf8)
    Write-Host "Acceptance evidence appended to $reportPath"
}
