#Requires -Version 7
<#
.SYNOPSIS
    Live round-trip test for get_type_content / update_type_content against real TIA Portal V21.

.DESCRIPTION
    Talks directly to TiaMcpServer.OpennessWorker.exe over newline-delimited JSON, bypassing the
    MCP host. This is the only coverage the Siemens-touching worker files have: they cannot be
    unit-tested because TiaMcpServer.Tests has no Siemens reference.

    Requires TIA Portal V21 running with the target project open.

    THIS SCRIPT MUTATES THE PROJECT. L1.3 and L1.6 write and leave their write in place; L1.7a
    restores on their behalf. Every write is restored before the run ends, and the run ends by
    confirming the original content is byte-identical and the project compiles clean. If L1.7a
    fails, the project is left modified and says so.

.PARAMETER ProjectPath
    Absolute path to the .ap21 project file.

.PARAMETER RootTypePath
    A UDT directly under Types, e.g. PLC_1/Types/AnalogInputSettings.

.PARAMETER NestedTypePath
    A UDT inside a type folder, e.g. PLC_1/Types/Sensors/AnalogInputSettings.
    Exercises the PlcTypeUserGroup overload, which the root type does not.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ProjectPath,
    [Parameter(Mandatory)] [string] $RootTypePath,
    [Parameter(Mandatory)] [string] $NestedTypePath,
    [string] $WorkerPath = "TiaMcpServer/bin/Debug/net8.0/openness-worker/TiaMcpServer.OpennessWorker.exe"
)

$ErrorActionPreference = 'Stop'
$script:Failures = @()

# Every warning any worker call reports, in arrival order, plus the stray-write signal this route
# reports in its payload rather than as a warning (see Set-TypeSource). Both are invisible to the
# compiler and to the re-export, so they must never be discarded.
#
# Each record carries a structural Kind: 'Worker' for something the worker actually warned about,
# 'StrayWrite' for a record this script synthesized from a payload field. L1.8 filters on Kind
# rather than on prose, and the printers label the provenance so the two are never confused.
$script:Warnings = @()

# update_type_content calls that succeeded AND whose generated-object count was readable. L1.8
# refuses to pass on an empty warning list unless at least one write actually reached that point.
$script:VerifiedWrites = 0

function Invoke-Worker {
    param([hashtable] $Request)
    $json = $Request | ConvertTo-Json -Compress -Depth 10
    $response = $json | & $WorkerPath | Select-Object -First 1
    if (-not $response) { throw "Worker returned no response for method '$($Request.method)'." }

    $parsed = $response | ConvertFrom-Json

    foreach ($warning in @($parsed.warnings)) {
        if ($null -eq $warning) { continue }
        $script:Warnings += [pscustomobject]@{
            Kind    = 'Worker'
            Method  = [string]$Request.method
            Target  = [string]$Request.typePath
            Format  = [string]$Request.format
            Message = [string]$warning
        }
    }

    return $parsed
}

<#
One provenance line for a warning record, used by the per-check printer, the L1.8 failure message
and the summary so all three stay consistent. '(derived from payload)' marks a record this script
synthesized, which would otherwise be indistinguishable from something the worker warned about.
#>
function Format-WarningOrigin {
    param($Entry)
    $target = if ($Entry.Target) { " on '$($Entry.Target)'" } else { '' }
    $format = if ($Entry.Format) { " [format=$($Entry.Format)]" } else { '' }
    $origin = if ($Entry.Kind -eq 'StrayWrite') { ' (derived from payload)' } else { '' }
    return "$($Entry.Method)$target$format$origin"
}

function Write-CheckWarnings {
    param([int] $SinceIndex)
    for ($index = $SinceIndex; $index -lt $script:Warnings.Count; $index++) {
        $entry = $script:Warnings[$index]
        Write-Host "      WARNING from $(Format-WarningOrigin $entry)" -ForegroundColor Yellow
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
        # count-mismatch warning during a normal single-type round trip is itself a finding.
        Write-CheckWarnings -SinceIndex $warningMark
    }
}

function Get-TypeSource {
    param([string] $TypePath, [string] $Format = 'source')
    $response = Invoke-Worker @{
        method      = 'get_type_content'
        projectPath = $ProjectPath
        typePath    = $TypePath
        format      = $Format
    }
    if (-not $response.success) { throw "get_type_content failed: $($response.error)" }
    return $response.payload
}

function Set-TypeSource {
    param([string] $TypePath, [string] $Content, [string] $Format = 'source')
    $response = Invoke-Worker @{
        method                = 'update_type_content'
        projectPath           = $ProjectPath
        typePath              = $TypePath
        sourceContent         = $Content
        format                = $Format
        allowTiaConfirmations = $true
    }

    # The stray-write signal reaches this route through the PAYLOAD, not through `warnings`.
    # PlcTypePostconditionVerifier only warns about a residual external source node; the object
    # count that the block route emits as a "generated N objects" warning of its own is, here,
    # PlcTypeImportResult.generatedObjectCount. Lifting it into $script:Warnings in that same
    # wording gives L1.8 a single list to sweep and makes the count visible per check.
    #
    # GenerateBlocksFromSource has no notion of the object it was addressed to, so a count other
    # than 1 is the only cheap signal that the write landed on a software scope it was not pointed
    # at. Nothing else sees it: the type still compiles and still re-exports, and postcondition
    # verification re-resolves through the original path and finds the original unchanged.
    if ($response.success -and $response.payload) {
        try { $report = $response.payload | ConvertFrom-Json }
        catch {
            throw ("update_type_content returned an unreadable payload, so the generated object " +
                "count could not be checked: $($_.Exception.Message)")
        }

        $count = $report.generatedObjectCount
        if ($null -eq $count) {
            throw ("update_type_content's payload carries no generatedObjectCount, so the " +
                "stray-write signal could not be read. The worker contract changed; this harness " +
                "must not report a pass it did not verify.")
        }

        $script:VerifiedWrites++

        if ($count -ne 1) {
            # Kind is what L1.8 filters on. The message wording mirrors the block route's warning
            # only so an operator reads the same sentence on both routes — rewording it must not be
            # able to disarm the check, which is why the structural field exists.
            $script:Warnings += [pscustomobject]@{
                Kind    = 'StrayWrite'
                Method  = 'update_type_content'
                Target  = $TypePath
                Format  = $Format
                Message = ("TIA Portal generated $count objects from the submitted source; " +
                    "expected 1. Inspect '$TypePath' and its PLC for objects this write was not " +
                    "addressed to.")
            }
        }
    }

    return $response
}

# TWIN: the helper below is duplicated byte-for-byte in scripts/live-test-db.ps1. The two
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

# --- L1.1 both group kinds export -------------------------------------------------
$rootOriginal = $null
$nestedOriginal = $null

Assert-Check 'L1.1a' 'Export a type at the Types root' {
    $script:rootOriginal = Get-TypeSource -TypePath $RootTypePath
    if ($script:rootOriginal -notmatch '(?m)^\s*TYPE\b') { throw 'Payload is not a TYPE declaration.' }
}

Assert-Check 'L1.1b' 'Export a type in a nested type folder' {
    $script:nestedOriginal = Get-TypeSource -TypePath $NestedTypePath
    if ($script:nestedOriginal -notmatch '(?m)^\s*TYPE\b') { throw 'Payload is not a TYPE declaration.' }
}

# --- L1.2 unchanged round trip is lossless ----------------------------------------
Assert-Check 'L1.2' 'Unchanged round trip re-exports byte-identically' {
    $result = Set-TypeSource -TypePath $NestedTypePath -Content $script:nestedOriginal
    if (-not $result.success) { throw "update_type_content failed: $($result.error)" }
    $after = Get-TypeSource -TypePath $NestedTypePath
    if ($after -ne $script:nestedOriginal) { throw 'Re-export differs from the original.' }
}

# --- L1.3 a real edit applies ------------------------------------------------------
Assert-Check 'L1.3' 'A modified initial value survives the round trip' {
    if (-not $script:nestedOriginal) { throw 'No original source was captured in L1.1b.' }

    # Which value is mutated, and in which direction, is Select-MutableInitialValue's job — see the
    # comment there for why it always mutates downward and why hex and real literals are excluded.
    #
    # Both misses below are FIXTURE limitations, not round-trip failures. They say so, and they name
    # the parameter to repoint, so neither can be read as a verdict on the source route.
    $choice = Select-MutableInitialValue -Source $script:nestedOriginal
    if (-not $choice) {
        if ($script:nestedOriginal -notmatch ':=\s*(\d+)(?![#.\w])') {
            throw ('Fixture type has no plain integer initial value to mutate — only hex, real or ' +
                'non-numeric literals. This is a FIXTURE limitation, not a round-trip failure: ' +
                'point -NestedTypePath at a UDT with an ordinary integer member.')
        }
        throw ('No integer initial value in this type can be mutated safely — every candidate is ' +
            'either 0 or already has its predecessor present in the source, which would make the ' +
            're-export assertion unable to tell a real write from a no-op. This is a FIXTURE ' +
            'limitation, not a round-trip failure: point -NestedTypePath at a UDT with an ' +
            'ordinary integer member (an Int somewhere in 2..127 is ideal).')
    }

    $original = $choice.Original
    $mutant = $choice.Mutant

    # -replace is GLOBAL: every member sharing this initial value is rewritten, not just the first
    # one found. That is deliberate and harmless — L1.7a restores the whole type from
    # $script:nestedOriginal — and Select-MutableInitialValue already proved the mutant token is
    # absent from the source, so finding it below still distinguishes a real write from a no-op.
    $edited = $script:nestedOriginal -replace ":=\s*$original(?![#.\w])", ":= $mutant"
    if ($edited -eq $script:nestedOriginal) {
        throw ("Rewriting ':= $original' to ':= $mutant' changed nothing, so this check would " +
            'submit the unmodified original and prove nothing about the write.')
    }

    $result = Set-TypeSource -TypePath $NestedTypePath -Content $edited
    if (-not $result.success) {
        throw ("update_type_content failed: $($result.error). The submitted edit only LOWERED " +
            "':= $original' to ':= $mutant', which is in range for every integer type a member " +
            'can declare, so a rejection here points at the round trip rather than at the value.')
    }

    $after = Get-TypeSource -TypePath $NestedTypePath
    if ($after -notmatch ":=\s*$mutant(?![#.\w])") {
        throw ("Edited value $mutant is absent after re-export — the write reported success but " +
            'did not change the type.')
    }
}

# --- L1.4 no residual external source node ----------------------------------------
# The description states what is actually being checked. The plan-mandated tree scan below is
# INERT — ProjectTreeWalker visits blocks, tag tables, types and software units, never external
# source groups — and the '_tiamcp_' marker exists only in the PlcExternalSource node name, so a
# surviving node can never appear in the JSON it searches. It is kept because the plan calls for
# it, but the printed line must not let a reader over-credit it. The warning scan is the half that
# can actually fail, and L1.8 re-runs it over the writes that happen later.
Assert-Check 'L1.4' 'No residual-external-source warning was reported (tree scan is inert — the walker does not visit external source groups)' {
    $tree = Invoke-Worker @{
        method      = 'browse_project_tree'
        projectPath = $ProjectPath
    }
    if (-not $tree.success) { throw "browse_project_tree failed: $($tree.error)" }
    $rendered = $tree | ConvertTo-Json -Depth 30
    if ($rendered -match '_tiamcp_') { throw 'A temporary external source node survived in the project.' }

    # This covers only the writes that have happened so far. L1.6 and L1.7a write AFTER this
    # point, so L1.8 sweeps the full warning list once every write is done.
    $residual = @($script:Warnings | Where-Object { $_.Message -match 'temporary external source node' })
    if ($residual.Count -gt 0) {
        throw "The worker reported a residual external source node: $($residual[0].Message)"
    }
}

# --- L1.5 strict preflight ---------------------------------------------------------
Assert-Check 'L1.5a' 'A name mismatch is rejected and changes nothing' {
    $before = Get-TypeSource -TypePath $NestedTypePath
    $wrongName = $script:nestedOriginal -replace '(?m)^(\s*TYPE\s+)("?)([A-Za-z_][A-Za-z0-9_]*)\2', '$1"NotTheTargetName"'

    $result = Set-TypeSource -TypePath $NestedTypePath -Content $wrongName
    if ($result.success) { throw 'Name mismatch was accepted; the write should be strict.' }

    $after = Get-TypeSource -TypePath $NestedTypePath
    if ($after -ne $before) { throw 'Project changed despite the rejection.' }
}

Assert-Check 'L1.5b' 'A nonexistent type path is rejected' {
    $result = Set-TypeSource -TypePath 'PLC_1/Types/DefinitelyNotARealType' -Content $script:nestedOriginal
    if ($result.success) { throw 'Nonexistent type was accepted; update must never create.' }
}

# --- L1.6 xml fallback stays reachable ---------------------------------------------
Assert-Check 'L1.6' 'format=xml round-trips' {
    $xml = Get-TypeSource -TypePath $NestedTypePath -Format 'xml'
    if ($xml -notmatch '<Document') { throw 'format=xml did not return a Simatic ML document.' }

    $result = Set-TypeSource -TypePath $NestedTypePath -Content $xml -Format 'xml'
    if (-not $result.success) { throw "xml import failed: $($result.error)" }
}

# --- L1.7 restore and compile ------------------------------------------------------
Assert-Check 'L1.7a' 'Original content is restored byte-identically' {
    # This is the only restore in the run: L1.3 and L1.6 leave their writes in place. If it fails,
    # the user's real project keeps the mutated type, so both failure paths must say so and say
    # what to do about it.
    $result = Set-TypeSource -TypePath $NestedTypePath -Content $script:nestedOriginal
    if (-not $result.success) {
        throw ("Restore of '$NestedTypePath' failed: $($result.error). THE PROJECT IS LEFT " +
            "MODIFIED — L1.3 and L1.6 wrote to this type and nothing has undone them. Restore " +
            "'$NestedTypePath' manually in TIA Portal before using this project.")
    }

    $after = Get-TypeSource -TypePath $NestedTypePath
    if ($after -ne $script:nestedOriginal) {
        throw ("Restored content of '$NestedTypePath' differs from the original. THE PROJECT IS " +
            "LEFT MODIFIED — restore '$NestedTypePath' manually in TIA Portal before using this " +
            "project.")
    }
}

Assert-Check 'L1.7b' 'Project compiles without errors' {
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

# --- L1.8 the unrequested-change warnings decide the outcome ---------------------------
# Runs last, after every write in the run — including L1.7a's restore, which is itself a write —
# over the COMPLETE warning list.
#
# These two signals are the only detectors that exist for their failure modes, and neither is
# reported as an error, so without this check they print in yellow and the run still exits 0 — a
# green line that would then be quoted as proof the route is sound.
#
#   * Kind 'StrayWrite' — lifted from update_type_content's payload by Set-TypeSource. A count
#     other than 1 is the only cheap signal that the write landed on a software scope it was not
#     pointed at. Nothing else sees it: the type still compiles and still re-exports, and
#     postcondition verification re-resolves through the original path and finds the original
#     unchanged. This is the exact bug class the type route shipped.
#   * "temporary external source node could not be removed" — a visible, persistent addition to
#     the user's project that browse_project_tree structurally cannot see (see L1.4).
#
# The stray-write branch filters on the structural Kind, not on the message text. Both ends of
# that record live in THIS file, so a regex would couple the detector to prose this same script
# owns: rewording Set-TypeSource's message to "generated 3 object(s)" would silently turn this
# branch into a check that cannot fail. The regex is retained as a second path so a genuine worker
# warning carrying that wording is still caught.
Assert-Check 'L1.8' 'No worker warning reports an unrequested change to the project' {
    # An empty warning list is only meaningful if a write actually got far enough to be measured.
    # Without this, a run where L1.1b failed and every later write errored would print a green
    # L1.8 having swept nothing.
    if ($script:VerifiedWrites -eq 0) {
        throw ('No update_type_content call succeeded with a readable object count, so the ' +
            'stray-write signal was never sampled and this check swept nothing. Fix the failures ' +
            'above and re-run; do not read this as evidence the route is sound.')
    }

    $strayWrites = @($script:Warnings | Where-Object {
        $_.Kind -eq 'StrayWrite' -or $_.Message -match 'generated \d+ objects'
    })
    $residual = @($script:Warnings | Where-Object { $_.Message -match 'temporary external source node' })

    $problems = @()
    foreach ($entry in @($strayWrites) + @($residual)) {
        $problems += "[$(Format-WarningOrigin $entry)] $($entry.Message)"
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
        Write-Host "  - [$(Format-WarningOrigin $entry)] $($entry.Message)" -ForegroundColor Yellow
    }
    Write-Host '  A "generated N objects; expected 1" warning during a single-type round trip' -ForegroundColor Yellow
    Write-Host '  means the write touched something it was not addressed to. L1.8 fails the run on' -ForegroundColor Yellow
    Write-Host '  that warning and on a residual external source node; any other warning here is' -ForegroundColor Yellow
    Write-Host '  informational, but read it before treating this run as a pass.' -ForegroundColor Yellow
    Write-Host '  "(derived from payload)" marks a record this script synthesized from the write''s' -ForegroundColor Yellow
    Write-Host '  own result rather than something the worker warned about; "[format=...]" names' -ForegroundColor Yellow
    Write-Host '  which write produced it, since the source and xml writes share a target type.' -ForegroundColor Yellow
    Write-Host ''
}

if ($script:Failures.Count -eq 0) {
    Write-Host 'All Phase 1 live checks passed.' -ForegroundColor Green
    exit 0
}

Write-Host "$($script:Failures.Count) check(s) FAILED:" -ForegroundColor Red
$script:Failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
Write-Host ''
Write-Host 'L1.1 and L1.8 are blocking. If either failed, do not start Phase 2.' -ForegroundColor Yellow
Write-Host 'L1.8 is the check that can actually observe a stray write or a residual external' -ForegroundColor Yellow
Write-Host 'source node; L1.4 tree scan alone proves neither (see the comment above it).' -ForegroundColor Yellow
Write-Host 'L1.3 can also fail because the FIXTURE has no integer member it can safely mutate.' -ForegroundColor Yellow
Write-Host 'If its message names -NestedTypePath, repoint the fixture and re-run: that is not a' -ForegroundColor Yellow
Write-Host 'code defect and says nothing about the round trip.' -ForegroundColor Yellow
exit 1