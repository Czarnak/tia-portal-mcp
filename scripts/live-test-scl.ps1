#Requires -Version 7
<#
.SYNOPSIS
    Live gate for the SCL external-source widening: get_block_content / update_block_logic with
    format=source against SCL-language FB/FC/OB, plus the withDependencies read option, against
    real TIA Portal V21.

.DESCRIPTION
    Talks directly to TiaMcpServer.OpennessWorker.exe over newline-delimited JSON, bypassing the
    MCP host — mirroring scripts/live-test-db.ps1 and scripts/live-test-udt.ps1. This is the only
    coverage the Siemens-touching worker files in TiaMcpServer.OpennessWorker/Openness have: they
    cannot be unit-tested because TiaMcpServer.Tests has no Siemens reference.

    Requires TIA Portal V21 running with the target project open.

    THIS SCRIPT MUTATES THE PROJECT. L3.3, L3.9b and L3.10b write and leave their write in place;
    L3.12 restores all three on their behalf. Every write is restored before the run ends, and the
    run ends by confirming the original content is byte-identical, the project compiles clean, and
    no object was added to or removed from the project tree. If L3.12 fails, the project is left
    modified and says which blocks and what to do about it.

.PARAMETER ProjectPath
    Absolute path to the .ap21 project file.

.PARAMETER SclFunctionBlockPath
    A PLC-scoped SCL function block with a UDT-typed parameter, e.g. PLC_1/Blocks/AnalogInput. Must
    have a plain VAR section (not only VAR_INPUT/VAR_OUTPUT/VAR_IN_OUT/VAR_TEMP) for L3.3 to have
    somewhere to add a probe member. Exercises the single-object read, the dependency read, the
    round trip, and all three write refusals (L3.1-L3.7).

.PARAMETER UnitScopedFunctionBlockPath
    An SCL function block that lives inside a software unit, e.g.
    PLC_1/Units/MyUnit/Blocks/UnitAnalogInput, also with a plain VAR section. Proves the
    external-source round trip runs against the unit's OWN external source group rather than the
    top-level PLC's (L3.10) — a PLC and each of its software units own a separate external source
    group, and using the wrong one writes a stray object while still reporting success.

.PARAMETER GlobalDbPath
    A global data block, e.g. PLC_1/Blocks/Settings_DB, with a plain VAR section. Regression guard
    for the shipped Phase 2 behavior (L3.9) — proves the SCL eligibility widening in
    SourceFormatEligibility did not narrow or otherwise change the data block route.

.PARAMETER LadBlockPath
    A LAD-language block, e.g. PLC_1/Blocks/Inputs_FB. Proves format=source is still refused for a
    graphical language, and that the refusal names the language and suggests format=xml (L3.8).

.PARAMETER WorkerPath
    Path to the built worker executable.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ProjectPath,
    [Parameter(Mandatory)] [string] $SclFunctionBlockPath,
    [Parameter(Mandatory)] [string] $UnitScopedFunctionBlockPath,
    [Parameter(Mandatory)] [string] $GlobalDbPath,
    [Parameter(Mandatory)] [string] $LadBlockPath,
    [string] $WorkerPath = "TiaMcpServer/bin/Debug/net8.0/openness-worker/TiaMcpServer.OpennessWorker.exe"
)

$ErrorActionPreference = 'Stop'
$script:Failures = @()

# Every warning any worker call reports, in arrival order. update_block_logic with format=source
# emits a warning — not an error — when TIA generates a number of objects other than exactly 1
# from the submitted source. That is the stray-write signal, and it is invisible to both the
# compiler and the re-export, so it must never be discarded.
$script:Warnings = @()

# update_block_logic calls that actually succeeded. L3.15 refuses to pass on an empty warning list
# unless at least one write got that far. On this route the stray-write signal arrives as a worker
# WARNING rather than in the write's payload, so a run in which every write failed produces exactly
# the same empty list as a clean run — without this counter L3.15 would sweep nothing and print PASS.
$script:VerifiedWrites = 0

$script:sclOriginal = $null
$script:sclAfterEdit = $null
$script:sclBlockName = $null
$script:sclWithDependencies = $null
$script:dbOriginal = $null
$script:unitOriginal = $null
$script:BaselineNodes = $null

function Invoke-Worker {
    param([hashtable] $Request)
    $json = $Request | ConvertTo-Json -Compress -Depth 10
    $response = $json | & $WorkerPath | Select-Object -First 1
    if (-not $response) { throw "Worker returned no response for method '$($Request.method)'." }

    $parsed = $response | ConvertFrom-Json

    foreach ($warning in @($parsed.warnings)) {
        if ($null -eq $warning) { continue }
        $script:Warnings += [pscustomobject]@{
            Method  = [string]$Request.method
            Target  = [string]$Request.blockPath
            Message = [string]$warning
        }
    }

    return $parsed
}

function Write-CheckWarnings {
    param([int] $SinceIndex)
    for ($index = $SinceIndex; $index -lt $script:Warnings.Count; $index++) {
        $entry = $script:Warnings[$index]
        $target = if ($entry.Target) { " on '$($entry.Target)'" } else { '' }
        Write-Host "      WARNING from $($entry.Method)$target" -ForegroundColor Yellow
        Write-Host "        $($entry.Message)" -ForegroundColor Yellow
    }
}

function Assert-Check {
    param([string] $Id, [string] $Description, [scriptblock] $Test)
    Write-Host "[$Id] $Description ... " -NoNewline
    $warningMark = $script:Warnings.Count
    try {
        & $Test
        Write-Host "PASS" -ForegroundColor Green
    }
    catch {
        Write-Host "FAIL" -ForegroundColor Red
        Write-Host "      $($_.Exception.Message)" -ForegroundColor Red
        $script:Failures += "$Id — $Description — $($_.Exception.Message)"
    }
    finally {
        # Printed after the verdict so a warning is never lost inside a passing check: a
        # count-mismatch warning during a normal single-block round trip is itself a finding.
        Write-CheckWarnings -SinceIndex $warningMark
    }
}

function Get-BlockSource {
    param([string] $BlockPath, [string] $Format = 'source')
    $request = @{
        method      = 'get_block_content'
        projectPath = $ProjectPath
        blockPath   = $BlockPath
    }
    if ($Format) { $request.format = $Format }

    $response = Invoke-Worker $request
    if (-not $response.success) { throw "get_block_content failed: $($response.error)" }
    return $response.payload
}

function Set-BlockSource {
    param([string] $BlockPath, [string] $Content, [string] $Format = 'source')
    # yamlContent is update_block_logic's historical field name for "the document being submitted";
    # it carries the external source text under format=source exactly as it carries the Simatic ML
    # bundle under format=xml.
    $request = @{
        method                = 'update_block_logic'
        projectPath           = $ProjectPath
        blockPath             = $BlockPath
        yamlContent           = $Content
        allowTiaConfirmations = $true
    }
    if ($Format) { $request.format = $Format }

    $response = Invoke-Worker $request

    # Counted only on success, so the refusal checks (L3.5, L3.6, L3.7) — which call this function
    # expecting a rejection — never make L3.15's positive control look satisfied.
    if ($response.success) { $script:VerifiedWrites++ }

    return $response
}

<#
Lists every declared object in $Content as { Keyword; Name }, in file order. Mirrors the anchor
half of TiaMcpServer.OpennessWorker.Openness.SourceDeclarationScanner's pattern closely enough for
a live harness reading real TIA exports — comments and string literals are NOT masked here, because
that edge is already covered offline by SourceDeclarationScannerTests (Task 2); this harness only
needs the object list from a real, unedited-except-by-this-script export.
#>
function Get-DeclaredObjects {
    param([string] $Content)
    $pattern = '(?im)^[ \t]*(?<keyword>ORGANIZATION_BLOCK|FUNCTION_BLOCK|DATA_BLOCK|FUNCTION|TYPE)[ \t]+' +
        '(?:"(?<quoted>[^"\r\n]+)"|(?<bare>[A-Za-z_][A-Za-z0-9_]*))'
    $found = [regex]::Matches($Content, $pattern)
    return @($found | ForEach-Object {
        $name = if ($_.Groups['quoted'].Success) { $_.Groups['quoted'].Value } else { $_.Groups['bare'].Value }
        [pscustomobject]@{ Keyword = $_.Groups['keyword'].Value.ToUpperInvariant(); Name = $name }
    })
}

<#
The object name a block's own address resolves to, stripped the same way BlockAddress does: trim,
strip a trailing bracket suffix like " [FB3]", trim again. Used to prove a read's declared name
actually matches the block the caller addressed, not just that A name was returned.
#>
function Get-LeafName {
    param([string] $Path)
    return ((($Path -split '/')[-1]).Trim() -replace '\s*\[[A-Za-z]+\d*\]$', '').Trim()
}

<#
Inserts one new Bool member into the first BARE "VAR" section — deliberately not VAR_INPUT,
VAR_OUTPUT, VAR_IN_OUT or VAR_TEMP, which all fail the exact-line anchor below on purpose, since an
interface-changing edit is a materially different (and riskier) test than an internal-storage edit.

Throws rather than silently doing nothing if the probe name already exists or no bare VAR section
is found — either would make the caller submit content indistinguishable from the original and
prove nothing about the write. Both are FIXTURE limitations, not round-trip failures: they name the
parameter to repoint.
#>
function Add-BoolMember {
    param([string] $Source, [string] $MemberName = 'TiaMcpLiveTestFlag')

    if ($Source -match "\b$MemberName\b") {
        throw ("The fixture already declares a member named '$MemberName'. This is a FIXTURE " +
            'limitation, not a round-trip failure: rename the probe or point at a different block.')
    }

    # Real V21 exports append a trailing space after section keywords (confirmed against the
    # committed fixtures, e.g. TiaMcpServer.Tests/Fixtures/DamperAnalog.scl:88 is "   VAR " with a
    # trailing space before the line break) — [ \t]* absorbs it. It must NOT absorb past the line,
    # or "VAR RETAIN" would be mistaken for a bare VAR section.
    $pattern = '(?m)^(\s*VAR)[ \t]*\r?$'
    if ($Source -notmatch $pattern) {
        throw ('No bare VAR section (as opposed to VAR_INPUT/VAR_OUTPUT/VAR_IN_OUT/VAR_TEMP) was ' +
            'found to extend. This is a FIXTURE limitation, not a round-trip failure: point at a ' +
            'block that declares a plain VAR section.')
    }

    # -replace is GLOBAL, matching Select-MutableInitialValue's convention in the other two
    # harnesses: harmless here because a block source declares at most one bare VAR section, so
    # "first" and "every" coincide in practice.
    $edited = $Source -replace $pattern, "`$1`r`n  $MemberName : Bool;"
    if ($edited -eq $Source) {
        throw 'Inserting the probe member changed nothing, so this check would prove nothing about the write.'
    }

    return $edited
}

<#
Flattens browse_project_tree into one record per node: { Type, Path }. Used to detect objects that
appeared or vanished — the stray-write signature that neither the compiler nor a re-export can see.

TWIN: duplicated from scripts/live-test-db.ps1's helper of the same name. The two harnesses share
no module, so the copies are kept identical on purpose — any difference between them is a
divergence, not a customization. Edit both or neither.
#>
function Get-ProjectNodeIndex {
    $response = Invoke-Worker @{ method = 'browse_project_tree'; projectPath = $ProjectPath }
    if (-not $response.success) { throw "browse_project_tree failed: $($response.error)" }

    $nodes = New-Object System.Collections.Generic.List[object]
    $pending = New-Object System.Collections.Generic.Stack[object]
    foreach ($root in @($response.payload | ConvertFrom-Json)) {
        if ($null -ne $root) { $pending.Push($root) }
    }

    while ($pending.Count -gt 0) {
        $node = $pending.Pop()
        if ($node.details -and $node.details.Path) {
            $nodes.Add([pscustomobject]@{
                Type = [string]$node.nodeType
                Path = [string]$node.details.Path
            })
        }
        foreach ($child in @($node.children)) {
            if ($null -ne $child) { $pending.Push($child) }
        }
    }

    return $nodes
}

function ConvertTo-NodeKeys {
    param($Nodes)
    return @(@($Nodes) | ForEach-Object { "$($_.Type)|$($_.Path)" } | Sort-Object)
}

# --- baseline ------------------------------------------------------------------------
# Not a check: if the project cannot be read at all there is nothing to test, and a wall of
# identical failures would only obscure that.
Write-Host 'Capturing the project tree baseline ... ' -NoNewline
try {
    $script:BaselineNodes = Get-ProjectNodeIndex
    Write-Host "OK ($($script:BaselineNodes.Count) nodes)" -ForegroundColor Green
}
catch {
    Write-Host 'FAILED' -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
    Write-Host '  Cannot continue. Is TIA Portal V21 running with the project open, and is' -ForegroundColor Red
    Write-Host "  '$WorkerPath' built?" -ForegroundColor Red
    exit 1
}
Write-Host ''

# --- L3.1 single-object read -----------------------------------------------------------
Assert-Check 'L3.1' 'get_block_content on the SCL FB (format=source, withDependencies omitted) declares exactly one FUNCTION_BLOCK matching the block' {
    $script:sclOriginal = Get-BlockSource -BlockPath $SclFunctionBlockPath
    $declared = Get-DeclaredObjects -Content $script:sclOriginal

    if ($declared.Count -ne 1) {
        $described = ($declared | ForEach-Object { "$($_.Keyword) '$($_.Name)'" }) -join ', '
        throw "Expected exactly 1 declared object, found $($declared.Count): $described"
    }
    if ($declared[0].Keyword -ne 'FUNCTION_BLOCK') {
        throw "Expected a FUNCTION_BLOCK declaration, found $($declared[0].Keyword) '$($declared[0].Name)'."
    }

    $expectedName = Get-LeafName -Path $SclFunctionBlockPath
    if ($declared[0].Name -ne $expectedName) {
        throw "Declared name '$($declared[0].Name)' does not match the addressed block '$expectedName'."
    }

    $script:sclBlockName = $declared[0].Name
}

# --- L3.2 dependency read ---------------------------------------------------------------
Assert-Check 'L3.2' 'get_block_content with withDependencies=true declares more than one object and warns "context only"' {
    $response = Invoke-Worker @{
        method           = 'get_block_content'
        projectPath      = $ProjectPath
        blockPath        = $SclFunctionBlockPath
        format           = 'source'
        withDependencies = $true
    }
    if (-not $response.success) { throw "get_block_content failed: $($response.error)" }
    $script:sclWithDependencies = $response.payload

    $declared = Get-DeclaredObjects -Content $script:sclWithDependencies
    if ($declared.Count -le 1) {
        throw "Expected more than 1 declared object with withDependencies=true, found $($declared.Count)."
    }

    $warningTexts = @($response.warnings)
    if (-not ($warningTexts -match 'context only')) {
        throw "Expected a warning containing 'context only'; got: $($warningTexts -join ' | ')"
    }
}

# --- L3.3 round trip ---------------------------------------------------------------------
Assert-Check 'L3.3' 'A modified SCL FB round-trips: an appended member survives write + re-read' {
    if (-not $script:sclOriginal) { throw 'No original source was captured in L3.1.' }

    $edited = Add-BoolMember -Source $script:sclOriginal
    $result = Set-BlockSource -BlockPath $SclFunctionBlockPath -Content $edited
    if (-not $result.success) {
        throw ("update_block_logic failed: $($result.error). The submitted edit only ADDED a new " +
            'Bool member to the VAR section, which a compiling FB should never refuse.')
    }

    $after = Get-BlockSource -BlockPath $SclFunctionBlockPath
    if ($after -notmatch 'TiaMcpLiveTestFlag\s*:\s*Bool') {
        throw ('The appended member is absent after re-export — the write reported success but did ' +
            'not change the block.')
    }

    $script:sclAfterEdit = $after
}

# --- L3.4 compile clean --------------------------------------------------------------------
Assert-Check 'L3.4' 'Project compiles without errors after the SCL FB round trip' {
    $result = Invoke-Worker @{ method = 'compile_check'; projectPath = $ProjectPath }
    if (-not $result.success) { throw "compile_check failed: $($result.error)" }

    $report = $result.payload | ConvertFrom-Json
    if ($report.totalErrorCount -ne 0) {
        throw "Compilation reported $($report.totalErrorCount) error(s); state '$($report.overallState)'."
    }
    if ($report.overallState -eq 'Error') {
        throw "Compilation reported no error count but an overall state of 'Error'."
    }
}

# --- L3.5 multi-object write refused ---------------------------------------------------------
Assert-Check 'L3.5' 'A multi-object (withDependencies) payload is refused as a write and changes nothing' {
    if (-not $script:sclWithDependencies) { throw 'No dependency-bearing payload was captured in L3.2.' }
    if (-not $script:sclAfterEdit) { throw 'No post-round-trip content was captured in L3.3.' }

    $result = Set-BlockSource -BlockPath $SclFunctionBlockPath -Content $script:sclWithDependencies
    if ($result.success) { throw 'A multi-object source was accepted; writes must be strict to exactly one object.' }
    if ($result.error -notmatch '\d+ objects') {
        throw "The rejection does not name the object count: $($result.error)"
    }

    $after = Get-BlockSource -BlockPath $SclFunctionBlockPath
    if ($after -ne $script:sclAfterEdit) { throw 'The block changed despite the rejection.' }
}

# --- L3.6 wrong-name write refused ------------------------------------------------------------
Assert-Check 'L3.6' 'A source declaring the wrong name is rejected and changes nothing' {
    if (-not $script:sclOriginal) { throw 'No original source was captured in L3.1.' }
    if (-not $script:sclAfterEdit) { throw 'No post-round-trip content was captured in L3.3.' }

    $wrongName = $script:sclOriginal -replace '(?m)^(\s*FUNCTION_BLOCK\s+)("?)([A-Za-z_][A-Za-z0-9_]*)\2', '$1"NotTheTargetBlock"'
    if ($wrongName -eq $script:sclOriginal) {
        throw ('Could not rewrite the declared block name, so this check would submit the ' +
            'unmodified original. The FUNCTION_BLOCK header format changed.')
    }

    $result = Set-BlockSource -BlockPath $SclFunctionBlockPath -Content $wrongName
    if ($result.success) { throw 'Name mismatch was accepted; the write should be strict.' }
    if ($result.error -notmatch "declares 'NotTheTargetBlock'") {
        throw ("The rejection does not name the mismatched declaration, so it may not be the " +
            "name-mismatch refusal at all: $($result.error)")
    }

    $after = Get-BlockSource -BlockPath $SclFunctionBlockPath
    if ($after -ne $script:sclAfterEdit) { throw 'The block changed despite the rejection.' }
}

# --- L3.7 wrong-kind write refused -------------------------------------------------------------
Assert-Check 'L3.7' 'A TYPE payload submitted to the FB path is rejected by kind and names both keywords' {
    if (-not $script:sclBlockName) { throw 'No declared block name was captured in L3.1.' }
    if (-not $script:sclAfterEdit) { throw 'No post-round-trip content was captured in L3.3.' }

    $probe = "TYPE `"$($script:sclBlockName)`"`r`nEND_TYPE`r`n"
    $result = Set-BlockSource -BlockPath $SclFunctionBlockPath -Content $probe
    if ($result.success) { throw 'A TYPE source was accepted for a FUNCTION_BLOCK path; kind must be enforced.' }
    if ($result.error -notmatch 'TYPE' -or $result.error -notmatch 'FUNCTION_BLOCK') {
        throw "The rejection does not name both keywords: $($result.error)"
    }

    $after = Get-BlockSource -BlockPath $SclFunctionBlockPath
    if ($after -ne $script:sclAfterEdit) { throw 'The block changed despite the rejection.' }
}

# --- L3.8 LAD block refused ----------------------------------------------------------------
Assert-Check 'L3.8' 'Reading the LAD block as source is refused, naming LAD and suggesting format=xml' {
    $response = Invoke-Worker @{
        method      = 'get_block_content'
        projectPath = $ProjectPath
        blockPath   = $LadBlockPath
        format      = 'source'
    }
    if ($response.success) { throw 'format=source was accepted for a LAD block.' }
    if ($response.error -notmatch 'LAD') { throw "The rejection does not name LAD: $($response.error)" }
    if ($response.error -notmatch 'format=xml') { throw "The rejection does not suggest format=xml: $($response.error)" }
}

# --- L3.9 global DB unchanged (Phase 2 regression guard) ------------------------------------
Assert-Check 'L3.9a' 'Global DB: get_block_content (format=source) still declares exactly one DATA_BLOCK matching the block' {
    $script:dbOriginal = Get-BlockSource -BlockPath $GlobalDbPath
    $declared = Get-DeclaredObjects -Content $script:dbOriginal

    if ($declared.Count -ne 1) {
        throw "Expected exactly 1 declared object, found $($declared.Count)."
    }
    if ($declared[0].Keyword -ne 'DATA_BLOCK') {
        throw "Expected a DATA_BLOCK declaration, found $($declared[0].Keyword)."
    }

    $expectedName = Get-LeafName -Path $GlobalDbPath
    if ($declared[0].Name -ne $expectedName) {
        throw "Declared name '$($declared[0].Name)' does not match the addressed block '$expectedName'."
    }
}

Assert-Check 'L3.9b' 'Global DB: a modified round trip still applies' {
    if (-not $script:dbOriginal) { throw 'No original DB source was captured in L3.9a.' }

    $edited = Add-BoolMember -Source $script:dbOriginal
    $result = Set-BlockSource -BlockPath $GlobalDbPath -Content $edited
    if (-not $result.success) { throw "update_block_logic failed: $($result.error)" }

    $after = Get-BlockSource -BlockPath $GlobalDbPath
    if ($after -notmatch 'TiaMcpLiveTestFlag\s*:\s*Bool') {
        throw ('The appended member is absent after re-export — the write reported success but did ' +
            'not change the block.')
    }
}

# --- L3.10 software unit ----------------------------------------------------------------------
Assert-Check 'L3.10a' 'Software unit: the SCL FB inside the unit exports as a single FUNCTION_BLOCK matching the block' {
    $script:unitOriginal = Get-BlockSource -BlockPath $UnitScopedFunctionBlockPath
    $declared = Get-DeclaredObjects -Content $script:unitOriginal

    if ($declared.Count -ne 1) {
        throw "Expected exactly 1 declared object, found $($declared.Count)."
    }
    if ($declared[0].Keyword -ne 'FUNCTION_BLOCK') {
        throw "Expected a FUNCTION_BLOCK declaration, found $($declared[0].Keyword)."
    }

    $expectedName = Get-LeafName -Path $UnitScopedFunctionBlockPath
    if ($declared[0].Name -ne $expectedName) {
        throw "Declared name '$($declared[0].Name)' does not match the addressed block '$expectedName'."
    }
}

Assert-Check 'L3.10b' "Software unit: a modified round trip applies through the unit's own external source group" {
    if (-not $script:unitOriginal) { throw 'No original source was captured in L3.10a.' }

    $edited = Add-BoolMember -Source $script:unitOriginal
    $result = Set-BlockSource -BlockPath $UnitScopedFunctionBlockPath -Content $edited
    if (-not $result.success) { throw "update_block_logic failed: $($result.error)" }

    $after = Get-BlockSource -BlockPath $UnitScopedFunctionBlockPath
    if ($after -notmatch 'TiaMcpLiveTestFlag\s*:\s*Bool') {
        throw ('The appended member is absent after re-export — the write reported success but did ' +
            'not change the block.')
    }
}

# --- L3.11 no residual external source node (so far) ------------------------------------------
# The description states what is actually being checked. The tree scan below is INERT —
# ProjectTreeWalker visits blocks, tag tables, types and software units, never external source
# groups — so a surviving node can never appear in the JSON it searches. It is kept for the same
# reason scripts/live-test-db.ps1 and scripts/live-test-udt.ps1 keep theirs: the printed line must
# not let a reader over-credit it. The warning scan is the half that can actually fail, and L3.15
# re-runs it over the writes (L3.12's restore) that happen later.
Assert-Check 'L3.11' 'No residual-external-source warning was reported so far (tree scan is inert — the walker does not visit external source groups)' {
    $tree = Invoke-Worker @{ method = 'browse_project_tree'; projectPath = $ProjectPath }
    if (-not $tree.success) { throw "browse_project_tree failed: $($tree.error)" }
    $rendered = $tree | ConvertTo-Json -Depth 30
    if ($rendered -match '_tiamcp_') { throw 'A temporary external source node survived in the project.' }

    $residual = @($script:Warnings | Where-Object { $_.Message -match 'temporary external source node' })
    if ($residual.Count -gt 0) {
        throw "The worker reported a residual external source node: $($residual[0].Message)"
    }
}

# --- L3.12 restore ------------------------------------------------------------------------------
# L3.3, L3.9b and L3.10b leave their writes in place; this is the only restore in the run. If it
# fails, the user's real project keeps the written blocks, so both failure paths must say so and
# say what to do about it — naming every target still outstanding, not just the one that failed.
Assert-Check 'L3.12' 'Every round-trip write is restored byte-identically' {
    $pending = @(@(
        @{ Path = $SclFunctionBlockPath;        Original = $script:sclOriginal }
        @{ Path = $GlobalDbPath;                Original = $script:dbOriginal }
        @{ Path = $UnitScopedFunctionBlockPath; Original = $script:unitOriginal }
    ) | Where-Object { $_.Path -and $_.Original })

    $restored = 0
    for ($index = 0; $index -lt $pending.Count; $index++) {
        $target = $pending[$index]
        $outstanding = @($pending[$index..($pending.Count - 1)] |
            ForEach-Object { "'$($_.Path)'" }) -join ', '

        $result = Set-BlockSource -BlockPath $target.Path -Content $target.Original
        if (-not $result.success) {
            throw ("Restore of '$($target.Path)' failed: $($result.error). THE PROJECT IS LEFT " +
                "MODIFIED — these blocks were written and nothing has undone them: $outstanding. " +
                "Restore them manually in TIA Portal before using this project.")
        }

        $after = Get-BlockSource -BlockPath $target.Path
        if ($after -ne $target.Original) {
            throw ("Restored content of '$($target.Path)' differs from the original. THE PROJECT " +
                "IS LEFT MODIFIED — restore these blocks manually in TIA Portal before using this " +
                "project: $outstanding")
        }
        $restored++
    }

    if ($restored -eq 0) {
        throw 'No original content was captured, so nothing could be restored or verified.'
    }
}

Assert-Check 'L3.13' 'Project compiles without errors after restore' {
    $result = Invoke-Worker @{ method = 'compile_check'; projectPath = $ProjectPath }
    if (-not $result.success) { throw "compile_check failed: $($result.error)" }

    $report = $result.payload | ConvertFrom-Json
    if ($report.totalErrorCount -ne 0) {
        throw "Compilation reported $($report.totalErrorCount) error(s); state '$($report.overallState)'."
    }
    if ($report.overallState -eq 'Error') {
        throw "Compilation reported no error count but an overall state of 'Error'."
    }
}

Assert-Check 'L3.14' 'The project tree is exactly as the run found it' {
    $baselineKeys = ConvertTo-NodeKeys $script:BaselineNodes
    $finalKeys = ConvertTo-NodeKeys (Get-ProjectNodeIndex)

    $added = @($finalKeys | Where-Object { $_ -cnotin $baselineKeys })
    $removed = @($baselineKeys | Where-Object { $_ -cnotin $finalKeys })

    if ($added.Count -gt 0 -or $removed.Count -gt 0) {
        $detail = @()
        if ($added.Count -gt 0) { $detail += "added: $($added -join ', ')" }
        if ($removed.Count -gt 0) { $detail += "removed: $($removed -join ', ')" }
        throw ("The run changed the set of objects in the project — " + ($detail -join '; '))
    }
}

# --- L3.15 the unrequested-change warnings decide the outcome ----------------------------------
# Runs last, after every write in the run — including L3.12's restore, which is itself a write —
# over the COMPLETE warning list. Same rationale as scripts/live-test-db.ps1's L2.9 and
# scripts/live-test-udt.ps1's L1.8: both signals below are reported as warnings, not errors, so
# without this check they print in yellow and the run still exits 0 — a green line that would then
# be quoted as proof the route is sound.
Assert-Check 'L3.15' 'No worker warning reports an unrequested change to the project' {
    if ($script:VerifiedWrites -eq 0) {
        throw ('No update_block_logic call succeeded, so the stray-write signal was never sampled ' +
            'and this check swept nothing. Fix the failures above and re-run; do not read this as ' +
            'evidence the route is sound.')
    }

    $strayWrites = @($script:Warnings | Where-Object { $_.Message -match 'generated \d+ objects' })
    $residual = @($script:Warnings | Where-Object { $_.Message -match 'temporary external source node' })

    $problems = @()
    foreach ($entry in @($strayWrites) + @($residual)) {
        $target = if ($entry.Target) { " on '$($entry.Target)'" } else { '' }
        $problems += "[$($entry.Method)$target] $($entry.Message)"
    }

    if ($problems.Count -gt 0) {
        throw ("$($problems.Count) warning(s) report an unrequested project change: " +
            ($problems -join ' | '))
    }
}

# --- summary ------------------------------------------------------------------------
Write-Host ''

if ($script:Warnings.Count -gt 0) {
    Write-Host "$($script:Warnings.Count) warning(s) were reported during the run:" -ForegroundColor Yellow
    foreach ($entry in $script:Warnings) {
        $target = if ($entry.Target) { " on '$($entry.Target)'" } else { '' }
        Write-Host "  - [$($entry.Method)$target] $($entry.Message)" -ForegroundColor Yellow
    }
    Write-Host '  A "generated N objects; expected 1" warning during a single-block round trip' -ForegroundColor Yellow
    Write-Host '  means the write touched something it was not addressed to. L3.15 fails the run on' -ForegroundColor Yellow
    Write-Host '  that warning and on a residual external source node; any other warning here is' -ForegroundColor Yellow
    Write-Host '  informational, but read it before treating this run as a pass.' -ForegroundColor Yellow
    Write-Host ''
}

if ($script:Failures.Count -eq 0) {
    Write-Host 'All Phase 3 live checks passed.' -ForegroundColor Green
    exit 0
}

Write-Host "$($script:Failures.Count) check(s) FAILED:" -ForegroundColor Red
$script:Failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
Write-Host ''
Write-Host 'L3.1, L3.2, L3.3, L3.5, L3.6, L3.7, L3.8 and L3.15 are blocking. If any failed,' -ForegroundColor Yellow
Write-Host 'format=source for SCL-language FB/FC/OB is not proven and must not ship. L3.15 is the' -ForegroundColor Yellow
Write-Host 'only check that can observe a stray write or a residual external source node.' -ForegroundColor Yellow
Write-Host 'L3.9 and L3.10 are regression guards: if only one of them failed, the SCL widening broke' -ForegroundColor Yellow
Write-Host 'the previously-shipped global-DB route or the software-unit scoping — investigate' -ForegroundColor Yellow
Write-Host 'SourceFormatEligibility and BlockTargetResolver rather than the SCL-specific code.' -ForegroundColor Yellow
Write-Host 'L3.3, L3.9b and L3.10b can also fail because the FIXTURE has no bare VAR section to' -ForegroundColor Yellow
Write-Host 'extend, or already declares the probe member name. If the message names one of' -ForegroundColor Yellow
Write-Host '-SclFunctionBlockPath, -GlobalDbPath or -UnitScopedFunctionBlockPath, repoint that' -ForegroundColor Yellow
Write-Host 'fixture and re-run: that is not a code defect and says nothing about the round trip.' -ForegroundColor Yellow
exit 1
