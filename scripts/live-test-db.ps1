#Requires -Version 7
<#
.SYNOPSIS
    Live round-trip test for get_block_content / update_block_logic with format=source, against
    real TIA Portal V21.

.DESCRIPTION
    Talks directly to TiaMcpServer.OpennessWorker.exe over newline-delimited JSON, bypassing the
    MCP host. This is the only coverage the Siemens-touching worker files have: they cannot be
    unit-tested because TiaMcpServer.Tests has no Siemens reference.

    Requires TIA Portal V21 running with the target project open.

    THIS SCRIPT MUTATES THE PROJECT. L2.2b, L2.2c, L2.3b and L2.8 write and leave their write in
    place; L2.7a restores on their behalf. Every write is restored before the run ends, and the run
    ends by confirming the original content is byte-identical, the project compiles clean, and no
    object was added to or removed from the project tree. If L2.7a fails, the project is left
    modified and says which blocks and what to do about it.

.PARAMETER ProjectPath
    Absolute path to the .ap21 project file.

.PARAMETER OptimizedDbPath
    A global data block with optimized block access, e.g. PLC_1/Blocks/InputValues_DB.

.PARAMETER NonOptimizedDbPath
    A global data block with standard (non-optimized) block access, e.g. PLC_1/Blocks/Legacy_DB.
    Its exported source must declare S7_Optimized_Access := 'FALSE'; L2.3a asserts that so the
    check cannot silently degrade into a second run of L2.2.

.PARAMETER InstanceDbPath
    An instance data block, e.g. PLC_1/Blocks/Inputs_FB_DB. Exercises the refusal path.

.PARAMETER FunctionBlockPath
    A function block, e.g. PLC_1/Blocks/Inputs_FB. Exercises the refusal path.

.PARAMETER UnitScopedDbPath
    OPTIONAL. A global data block that lives inside a software unit, e.g.
    PLC_1/Units/MyUnit/Blocks/UnitValues_DB. Enables L2.8, the only check that proves the
    external-source round trip runs against the OWNING software scope rather than the top-level
    PLC. Without it the script prints an explicit NOT VERIFIED notice and leaves that risk open.

    The first segment must be the PLC SOFTWARE name as browse_project_tree reports it (the same
    name that appears in the tree's '<name>/Blocks' path), not the device name — L2.8 fails loudly
    rather than passing vacuously if it cannot find that Blocks root.

.PARAMETER WorkerPath
    Path to the built worker executable.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ProjectPath,
    [Parameter(Mandatory)] [string] $OptimizedDbPath,
    [Parameter(Mandatory)] [string] $NonOptimizedDbPath,
    [Parameter(Mandatory)] [string] $InstanceDbPath,
    [Parameter(Mandatory)] [string] $FunctionBlockPath,
    [string] $UnitScopedDbPath,
    [string] $WorkerPath = "TiaMcpServer/bin/Debug/net8.0/openness-worker/TiaMcpServer.OpennessWorker.exe"
)

$ErrorActionPreference = 'Stop'
$script:Failures = @()

# Every warning any worker call reports, in arrival order. update_block_logic with format=source
# emits a warning — not an error — when TIA generates a number of objects other than exactly 1
# from the submitted source. That is the stray-write signal, and it is invisible to both the
# compiler and the re-export, so it must never be discarded.
$script:Warnings = @()

# update_block_logic calls that actually succeeded. L2.9 refuses to pass on an empty warning list
# unless at least one write got that far. On this route the stray-write signal arrives as a worker
# WARNING rather than in the write's payload, so a run in which every write failed produces exactly
# the same empty list as a clean run — without this counter L2.9 would sweep nothing and print PASS.
$script:VerifiedWrites = 0

$script:optimizedOriginal = $null
$script:nonOptimizedOriginal = $null
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
        # count-mismatch warning during a normal single-DB round trip is itself a finding.
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
    # An empty -Format omits the field entirely, which is how L2.1 exercises "no format supplied".
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

    # Counted only on success, so the refusal checks (L2.5b, L2.5d, L2.5e, L2.5f) — which call this
    # function expecting a rejection — never make L2.9's positive control look satisfied.
    if ($response.success) { $script:VerifiedWrites++ }

    return $response
}

# TWIN: the helper below is duplicated byte-for-byte in scripts/live-test-udt.ps1. The two
# harnesses share no module, so the copies are kept identical on purpose — any difference between
# them is a divergence, not a customization. Edit both or neither.
<#
Picks an integer initial value in $Source that the caller's mutation check can safely rewrite, and
returns @{ Original; Mutant } — or $null when the fixture offers none.

Mutating DOWNWARD is what makes this safe. The pattern only matches a non-negative literal, so
value-1 is never below 0 as long as value is at least 1, and every integer type in play — SInt
through ULInt, signed or unsigned — accepts every value from 0 upward. Mutating UP would be
rejected by TIA for any member sitting at its type's maximum (:= 127 on an SInt, := 255 on a Byte,
:= 65535 on a Word), and that rejection would surface as a failed write, blaming the round trip for
what is really a fixture the check chose badly.

The (?![#.\w]) guard is what keeps a hex or real literal out of the candidate set. Without it '16'
is picked out of '16#0F' and '1' out of '1.5', and the rewrite then either corrupts the document or
matches nothing at all — and a check that submitted the unmodified original reports the resulting
"value is absent after re-export" as a round-trip failure.

A candidate whose predecessor already appears in the source is rejected too: the caller proves the
write landed by finding the mutant on re-export, and it could not tell a real write from a no-op if
the mutant token were in the document to begin with.

The first pass prefers an ordinary small member (2..127 — in range for every integer type, and
unambiguously not a Bool written as 0 or 1) before the second pass falls back to any usable value.
BigInteger, not [int], because an initial value can exceed Int32 and even Int64.
#>
function Select-MutableInitialValue {
    param([string] $Source)

    $candidates = @([regex]::Matches($Source, ':=\s*(\d+)(?![#.\w])') |
        ForEach-Object { $_.Groups[1].Value } |
        Select-Object -Unique)
    if ($candidates.Count -eq 0) { return $null }

    foreach ($preferSmall in @($true, $false)) {
        foreach ($text in $candidates) {
            $value = [bigint]::Parse($text)
            if ($value -lt [bigint]1) { continue }
            if ($preferSmall -and ($value -lt [bigint]2 -or $value -gt [bigint]127)) { continue }

            $mutant = ($value - [bigint]1).ToString()
            if ($Source -match ":=\s*$mutant(?![#.\w])") { continue }

            return @{ Original = $text; Mutant = $mutant }
        }
    }

    return $null
}

function Assert-RejectionNamesKind {
    param([string] $ErrorText, [string] $Pattern)
    if (-not $ErrorText) { throw 'The rejection carried no error message.' }
    if ($ErrorText -notmatch $Pattern) {
        throw "The rejection does not name the block type. Expected /$Pattern/, got: $ErrorText"
    }
    if ($ErrorText -notmatch 'not a global data block') {
        throw "The rejection does not state the global-data-block constraint: $ErrorText"
    }
}

<#
Flattens browse_project_tree into one record per node: { Type, Path }. Used to detect objects that
appeared or vanished — the stray-write signature that neither the compiler nor a re-export can see.
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

function Write-UnitScopeNotVerified {
    Write-Host ''
    Write-Host '  ============================================================================' -ForegroundColor Yellow
    Write-Host '  [L2.8] NOT VERIFIED — -UnitScopedDbPath was not supplied.' -ForegroundColor Yellow
    Write-Host '' -ForegroundColor Yellow
    Write-Host '  What went unchecked:' -ForegroundColor Yellow
    Write-Host '    format=source was never exercised against a global data block that lives' -ForegroundColor Yellow
    Write-Host '    inside a software unit. Every check above ran against PLC-scoped blocks only.' -ForegroundColor Yellow
    Write-Host '' -ForegroundColor Yellow
    Write-Host '  Why it matters:' -ForegroundColor Yellow
    Write-Host '    A PLC and each of its software units own a SEPARATE external source group.' -ForegroundColor Yellow
    Write-Host '    Using the top-level one for a unit-scoped object writes a stray NEW object' -ForegroundColor Yellow
    Write-Host '    into the top-level PLC and leaves the addressed one untouched — and the write' -ForegroundColor Yellow
    Write-Host '    still reports SUCCESS, because postcondition verification re-resolves through' -ForegroundColor Yellow
    Write-Host '    the unit path and finds the unchanged original. The PLC data type route' -ForegroundColor Yellow
    Write-Host '    shipped exactly this bug. The data block route threads the owning scope' -ForegroundColor Yellow
    Write-Host '    through BlockTargetResolver to prevent it, but that fix touches Siemens types' -ForegroundColor Yellow
    Write-Host '    and therefore has NO unit test — it is verified by code reading alone.' -ForegroundColor Yellow
    Write-Host '' -ForegroundColor Yellow
    Write-Host '  How to close it:' -ForegroundColor Yellow
    Write-Host '    Re-run with -UnitScopedDbPath "<PlcSoftware>/Units/<Unit>/Blocks/<Db>".' -ForegroundColor Yellow
    Write-Host '  ============================================================================' -ForegroundColor Yellow
    Write-Host ''
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

# --- L2.1 the default read is unchanged ----------------------------------------------
Assert-Check 'L2.1' 'get_block_content without format still returns the Simatic ML bundle' {
    $bundle = Get-BlockSource -BlockPath $OptimizedDbPath -Format ''
    if ($bundle -notmatch '(?m)^--- FILE: ') {
        throw 'The default payload has no "--- FILE:" document delimiter; the bundle format changed.'
    }
    if ($bundle -notmatch '<Document') {
        throw 'The default payload contains no Simatic ML document.'
    }
}

# --- L2.2 optimized DB round trip -----------------------------------------------------
Assert-Check 'L2.2a' 'The optimized DB fixture exports as a DATA_BLOCK source' {
    $script:optimizedOriginal = Get-BlockSource -BlockPath $OptimizedDbPath
    if ($script:optimizedOriginal -notmatch '(?m)^\s*DATA_BLOCK\b') {
        throw 'Payload is not a DATA_BLOCK declaration.'
    }
    if ($script:optimizedOriginal -match "S7_Optimized_Access\s*:=\s*'FALSE'") {
        throw ("-OptimizedDbPath resolves to a NON-optimized data block. Swap the fixtures: " +
            "L2.2 must cover optimized access and L2.3 must cover standard access.")
    }
}

Assert-Check 'L2.2b' 'Optimized DB: unchanged round trip re-exports byte-identically' {
    if (-not $script:optimizedOriginal) { throw 'No original source was captured in L2.2a.' }

    $result = Set-BlockSource -BlockPath $OptimizedDbPath -Content $script:optimizedOriginal
    if (-not $result.success) { throw "update_block_logic failed: $($result.error)" }

    $after = Get-BlockSource -BlockPath $OptimizedDbPath
    if ($after -ne $script:optimizedOriginal) { throw 'Re-export differs from the original.' }
}

# --- L2.2c a real edit applies ---------------------------------------------------------
# Ported from live-test-udt.ps1's L1.3. Every other DB write in this run submits the content it
# just read, so "the re-export is byte-identical" holds just as well if GenerateBlocksFromSource
# silently failed to override the existing block: without this check the gate that will be quoted
# as proof the DB round trip works cannot observe a total write failure. The mutation is left in
# place; L2.7a's restore already covers $OptimizedDbPath and undoes it.
#
# L2.4 is retired, not vacant: it was the stale-offset contingency, dropped once non-optimized DB
# exports were established to carry no offset column. Do not reuse the ID.
Assert-Check 'L2.2c' 'A modified initial value survives the round trip' {
    if (-not $script:optimizedOriginal) { throw 'No original source was captured in L2.2a.' }

    # Which value is mutated, and in which direction, is Select-MutableInitialValue's job — see the
    # comment there for why it always mutates downward.
    # Both misses below are FIXTURE limitations, not round-trip failures. They say so, and they
    # name the parameter to repoint, so neither can be read as a verdict on format=source.
    $choice = Select-MutableInitialValue -Source $script:optimizedOriginal
    if (-not $choice) {
        if ($script:optimizedOriginal -notmatch ':=\s*(\d+)(?![#.\w])') {
            throw ('Fixture DB has no plain integer initial value to mutate — only hex, real or ' +
                'non-numeric literals. This is a FIXTURE limitation, not a round-trip failure: ' +
                'point -OptimizedDbPath at an optimized global DB with an ordinary integer member.')
        }
        throw ('No integer initial value in this DB can be mutated safely — every candidate is ' +
            'either 0 or already has its predecessor present in the source, which would make the ' +
            're-export assertion unable to tell a real write from a no-op. This is a FIXTURE ' +
            'limitation, not a round-trip failure: point -OptimizedDbPath at an optimized global ' +
            'DB with an ordinary integer member (an Int somewhere in 2..127 is ideal).')
    }

    $original = $choice.Original
    $mutant = $choice.Mutant

    # -replace is GLOBAL: every member sharing this initial value is rewritten, not just the first
    # one found. That is deliberate and harmless — L2.7a restores the whole block from
    # $script:optimizedOriginal — and Select-MutableInitialValue already proved the mutant token is
    # absent from the source, so finding it below still distinguishes a real write from a no-op.
    $edited = $script:optimizedOriginal -replace ":=\s*$original(?![#.\w])", ":= $mutant"
    if ($edited -eq $script:optimizedOriginal) {
        throw ("Rewriting ':= $original' to ':= $mutant' changed nothing, so this check would " +
            'submit the unmodified original and prove nothing about the write.')
    }

    $result = Set-BlockSource -BlockPath $OptimizedDbPath -Content $edited
    if (-not $result.success) {
        throw ("update_block_logic failed: $($result.error). The submitted edit only LOWERED " +
            "':= $original' to ':= $mutant', which is in range for every integer type a DB member " +
            'can declare, so a rejection here points at the round trip rather than at the value.')
    }

    $after = Get-BlockSource -BlockPath $OptimizedDbPath
    if ($after -notmatch ":=\s*$mutant(?![#.\w])") {
        throw ("Edited value $mutant is absent after re-export — the write reported success but " +
            'did not change the block.')
    }
}

# --- L2.3 non-optimized DB round trip -------------------------------------------------
Assert-Check 'L2.3a' 'The non-optimized DB fixture really is non-optimized' {
    $script:nonOptimizedOriginal = Get-BlockSource -BlockPath $NonOptimizedDbPath
    if ($script:nonOptimizedOriginal -notmatch '(?m)^\s*DATA_BLOCK\b') {
        throw 'Payload is not a DATA_BLOCK declaration.'
    }
    if ($script:nonOptimizedOriginal -notmatch "S7_Optimized_Access\s*:=\s*'FALSE'") {
        throw ("The exported source does not declare S7_Optimized_Access := 'FALSE', so this " +
            "fixture is optimized and L2.3b would merely repeat L2.2b. Point " +
            "-NonOptimizedDbPath at a data block with standard access.")
    }
}

Assert-Check 'L2.3b' 'Non-optimized DB: unchanged round trip re-exports byte-identically' {
    if (-not $script:nonOptimizedOriginal) { throw 'No original source was captured in L2.3a.' }

    $result = Set-BlockSource -BlockPath $NonOptimizedDbPath -Content $script:nonOptimizedOriginal
    if (-not $result.success) { throw "update_block_logic failed: $($result.error)" }

    $after = Get-BlockSource -BlockPath $NonOptimizedDbPath
    if ($after -ne $script:nonOptimizedOriginal) { throw 'Re-export differs from the original.' }
}

# --- L2.5 every other block kind is refused by name -----------------------------------
# The refusals land before any project mutation, so the submitted document is deliberately a
# throwaway: what is under test is that the block KIND is refused, not the document.
$script:probeSource = "DATA_BLOCK `"NotTheTargetBlock`"`r`nSTRUCT`r`n   Value : Int;`r`nEND_STRUCT;`r`nBEGIN`r`nEND_DATA_BLOCK`r`n"

Assert-Check 'L2.5a' 'Reading the instance DB as source is refused by block type' {
    $response = Invoke-Worker @{
        method      = 'get_block_content'
        projectPath = $ProjectPath
        blockPath   = $InstanceDbPath
        format      = 'source'
    }
    if ($response.success) { throw 'format=source was accepted for an instance data block.' }
    Assert-RejectionNamesKind -ErrorText $response.error -Pattern 'instance data block \(InstanceDB\)'
}

Assert-Check 'L2.5b' 'Writing the instance DB as source is refused by block type' {
    $response = Set-BlockSource -BlockPath $InstanceDbPath -Content $script:probeSource
    if ($response.success) { throw 'format=source was accepted for an instance data block.' }
    Assert-RejectionNamesKind -ErrorText $response.error -Pattern 'instance data block \(InstanceDB\)'
}

Assert-Check 'L2.5c' 'Reading the FB as source is refused by block type' {
    $response = Invoke-Worker @{
        method      = 'get_block_content'
        projectPath = $ProjectPath
        blockPath   = $FunctionBlockPath
        format      = 'source'
    }
    if ($response.success) { throw 'format=source was accepted for a function block.' }
    Assert-RejectionNamesKind -ErrorText $response.error -Pattern 'function block \(FB\)'
}

Assert-Check 'L2.5d' 'Writing the FB as source is refused by block type' {
    $response = Set-BlockSource -BlockPath $FunctionBlockPath -Content $script:probeSource
    if ($response.success) { throw 'format=source was accepted for a function block.' }
    Assert-RejectionNamesKind -ErrorText $response.error -Pattern 'function block \(FB\)'
}

# --- L2.5e/f the other two preflight refusals -----------------------------------------
# BlockImporter.ImportSource refuses three things before it mutates anything: a missing block, a
# block that is not a GlobalDB, and a declared name that does not match the target. L2.5a-d cover
# only the second. The name-mismatch refusal is what stops a path typo from making
# GenerateBlocksFromSource create a stray block — this branch's own highest-risk failure mode — and
# BlockImporter touches Siemens types, so it cannot be linked into TiaMcpServer.Tests: these two
# checks are its ONLY coverage. Ported from live-test-udt.ps1's L1.5a and L1.5b.
Assert-Check 'L2.5e' 'A name mismatch is rejected and changes nothing' {
    $before = Get-BlockSource -BlockPath $OptimizedDbPath
    $wrongName = $before -replace '(?m)^(\s*DATA_BLOCK\s+)("?)([A-Za-z_][A-Za-z0-9_]*)\2', '$1"NotTheTargetBlock"'
    if ($wrongName -eq $before) {
        throw ('Could not rewrite the declared block name, so this check would submit the ' +
            'unmodified original and a success would be misreported as an accepted mismatch. ' +
            'The DATA_BLOCK header format changed.')
    }

    $result = Set-BlockSource -BlockPath $OptimizedDbPath -Content $wrongName
    if ($result.success) { throw 'Name mismatch was accepted; the write should be strict.' }

    # Asserting on the refusal's wording, the way L2.5a-d already do through
    # Assert-RejectionNamesKind: 'some error happened' would also be satisfied by the document
    # being rejected as unparseable, which would leave the name-mismatch refusal itself unproven.
    if ($result.error -notmatch "declares 'NotTheTargetBlock'") {
        throw ("The rejection does not name the mismatched declaration, so it may not be the " +
            "name-mismatch refusal at all: $($result.error)")
    }

    # If the refusal had not landed, the project would now hold a block named 'NotTheTargetBlock'.
    # This comparison sees the addressed block being clobbered; L2.7c sees the stray node.
    $after = Get-BlockSource -BlockPath $OptimizedDbPath
    if ($after -ne $before) { throw 'Project changed despite the rejection.' }
}

Assert-Check 'L2.5f' 'A nonexistent block path is rejected' {
    if (-not $script:optimizedOriginal) { throw 'No original source was captured in L2.2a.' }

    # The parent scope is derived from a fixture that is known to resolve, so the refusal is for
    # the block name and not for a PLC this project does not have. A hardcoded 'PLC_1/Blocks/...'
    # would be rejected on any differently-named PLC for the wrong reason and pass vacuously.
    $missingPath = ($OptimizedDbPath -replace '/[^/]+$', '') + '/DefinitelyNotARealBlock'

    $result = Set-BlockSource -BlockPath $missingPath -Content $script:optimizedOriginal
    if ($result.success) { throw 'Nonexistent block was accepted; update must never create.' }
    if ($result.error -notmatch 'No block exists at') {
        throw ("The rejection does not state that no block exists there, so it may not be the " +
            "missing-block refusal at all: $($result.error)")
    }
}

# --- L2.6 no residual external source node --------------------------------------------
# The description states what is actually being checked. The plan-mandated tree scan below is
# INERT — ProjectTreeWalker visits blocks, tag tables, types and software units, never external
# source groups — so a surviving node can never appear in the JSON it searches. It is kept because
# the plan calls for it, but the printed line must not let a reader over-credit it. The warning
# scan is the half that can actually fail, and L2.9 re-runs it over the writes that happen later.
Assert-Check 'L2.6' 'No residual-external-source warning was reported (tree scan is inert — the walker does not visit external source groups)' {
    $tree = Invoke-Worker @{
        method      = 'browse_project_tree'
        projectPath = $ProjectPath
    }
    if (-not $tree.success) { throw "browse_project_tree failed: $($tree.error)" }
    $rendered = $tree | ConvertTo-Json -Depth 30
    if ($rendered -match '_tiamcp_') { throw 'A temporary external source node survived in the project.' }

    # This covers only the writes that have happened so far. L2.8 and L2.7a write AFTER this
    # point, so L2.9 sweeps the full warning list once every write is done.
    $residual = @($script:Warnings | Where-Object { $_.Message -match 'temporary external source node' })
    if ($residual.Count -gt 0) {
        throw "The worker reported a residual external source node: $($residual[0].Message)"
    }
}

# --- L2.8 the unit-scoped write lands in the unit -------------------------------------
# Ordered before L2.7a because L2.7a's restore is itself a write and must be the LAST write of the
# run. L2.9 then sweeps every warning both of them produced.
if ($UnitScopedDbPath) {
    Assert-Check 'L2.8' 'Unit-scoped DB round trip adds nothing to the top-level PLC' {
        # Trimmed the way BlockAddress.SplitSegments trims every segment, so this check reads the
        # path the same way the worker resolves it. The tree paths compared against below are built
        # by ProjectTreeWalker from real object names and carry no padding, so they need no trim.
        $plcSegment = (($UnitScopedDbPath -split '/')[0]).Trim()
        $blocksRoot = "$plcSegment/Blocks"

        $before = Get-ProjectNodeIndex
        $rootFound = @($before | Where-Object { $_.Path -ceq $blocksRoot }).Count -gt 0
        if (-not $rootFound) {
            $candidates = @($before |
                Where-Object {
                    $_.Type -eq 'BlockFolder' -and
                    $_.Path.EndsWith('/Blocks', [System.StringComparison]::Ordinal) -and
                    -not $_.Path.Contains('/Units/')
                } | ForEach-Object { $_.Path }) -join ', '
            throw ("No '$blocksRoot' node exists in the project tree, so this check could not " +
                "observe the top-level PLC and would pass vacuously. Use the PLC software name " +
                "as the first segment of -UnitScopedDbPath. Top-level Blocks roots found: " +
                "$candidates")
        }

        $topLevelBefore = ConvertTo-NodeKeys @($before | Where-Object {
            $_.Path.StartsWith("$blocksRoot/", [System.StringComparison]::Ordinal)
        })

        # The stray-node assertion at the end of this check is set membership on Type|Path, so it
        # can only see an object that was ADDED. If the top-level PLC already holds a block with
        # this same name, a misdirected GenerateBlocksFromSource OVERWRITES it instead: no key
        # appears, this check passes, L2.7c passes, and the clobbered top-level block is never
        # restored — a silent, unreported mutation of a real project. Software units make
        # same-named blocks natural, so refuse the fixture rather than run blind.
        # Trim, replace, Trim — the worker's exact order: SplitSegments trims every segment
        # (BlockAddress.cs:141) BEFORE StripBlockSuffix runs its $-anchored replace and trims again
        # (BlockAddress.cs:155). The order matters: the leading Trim is what lets the anchor match
        # on " UnitValues_DB [DB5] ", and the trailing Trim handles " UnitValues_DB ". Reading the
        # path any other way than the worker does would let padding defeat this guard silently —
        # the worker would resolve and write the block while the guard matched nothing.
        $unitBlockName = ((($UnitScopedDbPath -split '/')[-1]).Trim() -replace '\s*\[[A-Za-z]+\d*\]$', '').Trim()
        $collisions = @($before | Where-Object {
            $_.Path.StartsWith("$blocksRoot/", [System.StringComparison]::Ordinal) -and
            $_.Path.EndsWith("/$unitBlockName", [System.StringComparison]::OrdinalIgnoreCase)
        } | ForEach-Object { $_.Path })
        if ($collisions.Count -gt 0) {
            throw ("The top-level PLC already contains a block named '$unitBlockName' (" +
                ($collisions -join ', ') + "). A misdirected write would overwrite that block " +
                "rather than add a node, so the stray-node assertion would miss it and the " +
                "clobbered block would never be restored. Point -UnitScopedDbPath at a unit " +
                "block whose name is unique across the whole PLC.")
        }

        $script:unitOriginal = Get-BlockSource -BlockPath $UnitScopedDbPath
        if ($script:unitOriginal -notmatch '(?m)^\s*DATA_BLOCK\b') {
            throw 'Payload is not a DATA_BLOCK declaration.'
        }

        $result = Set-BlockSource -BlockPath $UnitScopedDbPath -Content $script:unitOriginal
        if (-not $result.success) { throw "update_block_logic failed: $($result.error)" }

        $after = Get-BlockSource -BlockPath $UnitScopedDbPath
        if ($after -ne $script:unitOriginal) { throw 'Re-export differs from the original.' }

        $topLevelAfter = ConvertTo-NodeKeys @((Get-ProjectNodeIndex) | Where-Object {
            $_.Path.StartsWith("$blocksRoot/", [System.StringComparison]::Ordinal)
        })

        $added = @($topLevelAfter | Where-Object { $_ -cnotin $topLevelBefore })
        if ($added.Count -gt 0) {
            throw ("The unit-scoped write added $($added.Count) object(s) under '$blocksRoot' — " +
                "the stray-write signature of using the top-level external source group instead " +
                "of the unit's own: " + ($added -join ', '))
        }
    }
}
else {
    Write-UnitScopeNotVerified
}

# --- L2.7 restore and verify -----------------------------------------------------------
Assert-Check 'L2.7a' 'Original content is restored byte-identically' {
    # This is the only restore in the run: L2.2b, L2.2c, L2.3b and L2.8 leave their writes in
    # place — L2.2c leaves a deliberately MUTATED optimized DB, which this restore undoes. If it
    # fails, the user's real project keeps the written blocks, so both failure paths must say so and
    # say what to do about it — naming every target still outstanding, not just the one that failed.
    # A block whose original was never captured was never written either, so it is not pending.
    $pending = @(@(
        @{ Path = $OptimizedDbPath;    Original = $script:optimizedOriginal }
        @{ Path = $NonOptimizedDbPath; Original = $script:nonOptimizedOriginal }
        @{ Path = $UnitScopedDbPath;   Original = $script:unitOriginal }
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

Assert-Check 'L2.7b' 'Project compiles without errors' {
    $result = Invoke-Worker @{ method = 'compile_check'; projectPath = $ProjectPath }
    if (-not $result.success) { throw "compile_check failed: $($result.error)" }

    # compile_check succeeds whenever the compile RAN; the report says whether it was clean.
    $report = $result.payload | ConvertFrom-Json
    if ($report.totalErrorCount -ne 0) {
        throw "Compilation reported $($report.totalErrorCount) error(s); state '$($report.overallState)'."
    }
    if ($report.overallState -eq 'Error') {
        throw "Compilation reported no error count but an overall state of 'Error'."
    }
}

Assert-Check 'L2.7c' 'The project tree is exactly as the run found it' {
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

# --- L2.9 the unrequested-change warnings decide the outcome ---------------------------
# Runs last, after every write in the run, over the COMPLETE warning list.
#
# These two warnings are the only detectors that exist for their failure modes, and both are
# reported as warnings rather than errors, so without this check they print in yellow and the run
# still exits 0 — a green line that would then be quoted as proof the route is sound.
#
#   * "generated N objects; expected 1" — GenerateBlocksFromSource has no notion of the object it
#     was addressed to. A count other than 1 is the only cheap signal that the write landed on a
#     software scope it was not pointed at. Nothing else sees it: the block still compiles and
#     still re-exports, and postcondition verification re-resolves through the original path and
#     finds the original unchanged.
#   * "temporary external source node could not be removed" — a visible, persistent addition to
#     the user's project that browse_project_tree structurally cannot see (see L2.6).
Assert-Check 'L2.9' 'No worker warning reports an unrequested change to the project' {
    # An empty warning list is only meaningful if a write actually got far enough to be measured.
    # Without this, a run in which L2.2b, L2.3b and L2.8 all failed would print a green L2.9 having
    # swept nothing — the run still exits 1, but that green line is exactly what gets quoted.
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
    Write-Host '  means the write touched something it was not addressed to. L2.9 fails the run on' -ForegroundColor Yellow
    Write-Host '  that warning and on a residual external source node; any other warning here is' -ForegroundColor Yellow
    Write-Host '  informational, but read it before treating this run as a pass.' -ForegroundColor Yellow
    Write-Host ''
}

if (-not $UnitScopedDbPath) {
    Write-UnitScopeNotVerified
}

if ($script:Failures.Count -eq 0) {
    Write-Host 'All Phase 2 live checks passed.' -ForegroundColor Green
    if (-not $UnitScopedDbPath) {
        Write-Host 'L2.8 did not run — see the NOT VERIFIED notice above.' -ForegroundColor Yellow
    }
    exit 0
}

Write-Host "$($script:Failures.Count) check(s) FAILED:" -ForegroundColor Red
$script:Failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
Write-Host ''
Write-Host 'L2.1, L2.2 (all of L2.2a, L2.2b and L2.2c) and L2.9 are blocking. If any failed,' -ForegroundColor Yellow
Write-Host 'format=source for global data blocks is not proven and must not ship. L2.2c is the' -ForegroundColor Yellow
Write-Host 'only check that can observe a write that did nothing; L2.9 is the only one that can' -ForegroundColor Yellow
Write-Host 'observe a stray write or a residual external source node.' -ForegroundColor Yellow
Write-Host 'L2.2c can also fail because the FIXTURE has no integer member it can safely mutate.' -ForegroundColor Yellow
Write-Host 'If its message names -OptimizedDbPath, repoint the fixture and re-run: that is not a' -ForegroundColor Yellow
Write-Host 'code defect and says nothing about format=source.' -ForegroundColor Yellow
Write-Host 'If only L2.3b failed, the non-optimized round trip is lossy: ship optimized-DB-only' -ForegroundColor Yellow
Write-Host 'support rather than a lossy round trip.' -ForegroundColor Yellow
exit 1
