#Requires -Version 7
<#
.SYNOPSIS
    Live round-trip test for get_type_content / update_type_content against real TIA Portal V21.

.DESCRIPTION
    Talks directly to TiaMcpServer.OpennessWorker.exe over newline-delimited JSON, bypassing the
    MCP host. This is the only coverage the Siemens-touching worker files have: they cannot be
    unit-tested because TiaMcpServer.Tests has no Siemens reference.

    Requires TIA Portal V21 running with the target project open.

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

function Invoke-Worker {
    param([hashtable] $Request)
    $json = $Request | ConvertTo-Json -Compress -Depth 10
    $response = $json | & $WorkerPath | Select-Object -First 1
    if (-not $response) { throw "Worker returned no response for method '$($Request.method)'." }
    return $response | ConvertFrom-Json
}

function Assert-Check {
    param([string] $Id, [string] $Description, [scriptblock] $Test)
    Write-Host "[$Id] $Description ... " -NoNewline
    try {
        & $Test
        Write-Host "PASS" -ForegroundColor Green
    }
    catch {
        Write-Host "FAIL" -ForegroundColor Red
        Write-Host "      $($_.Exception.Message)" -ForegroundColor Red
        $script:Failures += "$Id — $Description — $($_.Exception.Message)"
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
    return Invoke-Worker @{
        method                = 'update_type_content'
        projectPath           = $ProjectPath
        typePath              = $TypePath
        sourceContent         = $Content
        format                = $Format
        allowTiaConfirmations = $true
    }
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
    if ($script:nestedOriginal -notmatch ':=\s*(\d+)') {
        throw 'Fixture type has no numeric initial value to mutate. Pick a different NestedTypePath.'
    }
    $original = $Matches[1]
    $mutant = [int]$original + 1
    $edited = $script:nestedOriginal -replace ":=\s*$original\b", ":= $mutant"

    $result = Set-TypeSource -TypePath $NestedTypePath -Content $edited
    if (-not $result.success) { throw "update_type_content failed: $($result.error)" }

    $after = Get-TypeSource -TypePath $NestedTypePath
    if ($after -notmatch ":=\s*$mutant\b") { throw "Edited value $mutant is absent after re-export." }
}

# --- L1.4 no residual external source node ----------------------------------------
Assert-Check 'L1.4' 'No residual PlcExternalSource node remains' {
    $tree = Invoke-Worker @{
        method      = 'browse_project_tree'
        projectPath = $ProjectPath
    }
    if (-not $tree.success) { throw "browse_project_tree failed: $($tree.error)" }
    $rendered = $tree | ConvertTo-Json -Depth 30
    if ($rendered -match '_tiamcp_') { throw 'A temporary external source node survived in the project.' }
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
    $result = Set-TypeSource -TypePath $NestedTypePath -Content $script:nestedOriginal
    if (-not $result.success) { throw "restore failed: $($result.error)" }

    $after = Get-TypeSource -TypePath $NestedTypePath
    if ($after -ne $script:nestedOriginal) { throw 'Restored content differs from the original.' }
}

Assert-Check 'L1.7b' 'Project compiles without errors' {
    $result = Invoke-Worker @{ method = 'compile_check'; projectPath = $ProjectPath }
    if (-not $result.success) { throw "compile_check failed: $($result.error)" }
}

# --- summary ------------------------------------------------------------------------
Write-Host ''
if ($script:Failures.Count -eq 0) {
    Write-Host 'All Phase 1 live checks passed.' -ForegroundColor Green
    exit 0
}

Write-Host "$($script:Failures.Count) check(s) FAILED:" -ForegroundColor Red
$script:Failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
Write-Host ''
Write-Host 'L1.1 and L1.4 are blocking. If either failed, do not start Phase 2.' -ForegroundColor Yellow
exit 1