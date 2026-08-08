#Requires -Version 7
[CmdletBinding()]
param(
    [ValidateSet('Describe', 'Run')]
    [string] $Mode = 'Describe',

    [string] $ProjectPath,
    [string] $SecondaryProjectPath,
    [string] $WorkerExecutable,
    [string] $EvidenceRoot = 'artifacts/live-vci-phase1',

    [ValidateRange(5, 1800)]
    [int] $TimeoutSeconds = 240,

    [switch] $AllowSecondaryProjectRead
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$caseIds = @(
    'N-FMT-FOREIGN', 'N-FMT-NULL', 'N-FMT-UNSUPPORTED',
    'N-GRP-FIND-EMPTY', 'N-GRP-FIND-MISSING', 'N-GRP-FIND-NULL',
    'N-GRP-FIND-WHITESPACE', 'N-MAP-INACCESSIBLE-FILE',
    'N-MAP-MISSING-FILE', 'N-WS-FIND-EMPTY', 'N-WS-FIND-MISSING',
    'N-WS-FIND-NULL', 'N-WS-FIND-WHITESPACE', 'R-CANARY', 'R-FMT',
    'R-GRP', 'R-MAP', 'R-REP', 'R-SVC', 'R-WS'
)

$evidenceFiles = @(
    'manifest.json',
    'cases.jsonl',
    'snapshot-before.json',
    'snapshot-after.json',
    'filesystem-before.json',
    'filesystem-after.json',
    'summary.json'
)

if ($Mode -eq 'Describe') {
    [ordered]@{
        schemaVersion = 'vci-phase1-read-harness/v1'
        readOnly = $true
        mutatesProject = $false
        workerOperation = 'probe_vci_read_contract'
        workerAccessMode = 'read-only'
        requiresSeparateLiveAuthorization = $true
        workerSessions = 2
        caseIds = $caseIds
        evidenceFiles = $evidenceFiles
        secondaryProjectRequiresAuthorization = $true
    } | ConvertTo-Json -Compress -Depth 10
    exit 0
}

function Test-AbsolutePath {
    param([Parameter(Mandatory)] [string] $Path)

    return [IO.Path]::IsPathFullyQualified($Path)
}

function Resolve-ExistingFilePath {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Label,
        [string] $Extension
    )

    if (-not (Test-AbsolutePath -Path $Path)) {
        throw "$Label must be an absolute path."
    }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label must be an existing file."
    }

    $item = Get-Item -LiteralPath $Path -Force
    if ($null -eq $item) {
        throw "$Label could not be canonicalized."
    }
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Label must not be a reparse point."
    }
    if ($Extension -and -not $item.Extension.Equals($Extension, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label must have extension '$Extension'."
    }

    try {
        return [IO.Path]::GetFullPath($item.FullName)
    }
    catch {
        throw "$Label could not be canonicalized."
    }
}

function Resolve-CanonicalDirectoryPath {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $AllowedRoot
    )

    $candidate = $Path
    if (-not (Test-AbsolutePath -Path $candidate)) {
        $candidate = Join-Path $RepositoryRoot $candidate
    }

    try {
        $canonical = [IO.Path]::GetFullPath($candidate)
    }
    catch {
        throw 'EvidenceRoot could not be canonicalized.'
    }

    $allowedPrefix = $AllowedRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $canonical.Equals($AllowedRoot, [StringComparison]::OrdinalIgnoreCase) -and
        -not $canonical.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'EvidenceRoot must be beneath artifacts/live-vci-phase1.'
    }

    $cursor = $canonical
    while ($true) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if ($null -eq $item) {
                throw 'EvidenceRoot could not be canonicalized.'
            }
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'EvidenceRoot cannot contain a reparse-point ancestor.'
            }
        }

        if ($cursor.Equals($AllowedRoot, [StringComparison]::OrdinalIgnoreCase)) {
            break
        }
        $parent = [IO.Directory]::GetParent($cursor)
        if ($null -eq $parent) {
            throw 'EvidenceRoot could not be canonicalized.'
        }
        $cursor = $parent.FullName
    }

    return $canonical
}

function Test-PathBelow {
    param(
        [Parameter(Mandatory)] [string] $Candidate,
        [Parameter(Mandatory)] [string] $Root
    )

    $rootPrefix = $Root.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    return $Candidate.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)
}

function Start-JsonLineProcess {
    param(
        [Parameter(Mandatory)] [string] $Executable,
        [Parameter(Mandatory)] [string[]] $Arguments
    )

    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $Executable
    foreach ($argument in $Arguments) {
        [void] $psi.ArgumentList.Add($argument)
    }
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $false
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $psi
    if (-not $process.Start()) {
        $process.Dispose()
        throw 'Worker process did not start.'
    }
    return $process
}

function Read-JsonLine {
    param(
        [Parameter(Mandatory)] [Diagnostics.Process] $Process,
        [Parameter(Mandatory)] [int] $TimeoutSeconds
    )

    $readTask = $Process.StandardOutput.ReadLineAsync()
    $timeoutMilliseconds = [Math]::Min($TimeoutSeconds * 1000, [int]::MaxValue)
    if (-not $readTask.Wait($timeoutMilliseconds)) {
        throw "Timed out waiting $TimeoutSeconds second(s) for a worker JSONL response."
    }
    return $readTask.GetAwaiter().GetResult()
}

$scriptDirectory = Split-Path -Parent $PSCommandPath
if ([string]::IsNullOrWhiteSpace($scriptDirectory)) {
    throw 'The harness repository boundary could not be canonicalized.'
}

try {
    $repositoryRoot = [IO.Path]::GetFullPath((Join-Path $scriptDirectory '..'))
    $allowedEvidenceRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts/live-vci-phase1'))
}
catch {
    throw 'The harness repository boundary could not be canonicalized.'
}

$canonicalProjectPath = Resolve-ExistingFilePath -Path $ProjectPath -Label 'ProjectPath' -Extension '.ap21'
$canonicalWorkerExecutable = Resolve-ExistingFilePath -Path $WorkerExecutable -Label 'WorkerExecutable'
$canonicalEvidenceRoot = Resolve-CanonicalDirectoryPath `
    -Path $EvidenceRoot `
    -RepositoryRoot $repositoryRoot `
    -AllowedRoot $allowedEvidenceRoot

if ($canonicalProjectPath.Equals($canonicalEvidenceRoot, [StringComparison]::OrdinalIgnoreCase) -or
    (Test-PathBelow -Candidate $canonicalProjectPath -Root $canonicalEvidenceRoot)) {
    throw 'ProjectPath must not equal or be beneath EvidenceRoot.'
}

$canonicalSecondaryProjectPath = $null
if (-not [string]::IsNullOrWhiteSpace($SecondaryProjectPath)) {
    if ($AllowSecondaryProjectRead) {
        $candidateSecondaryProjectPath = Resolve-ExistingFilePath `
            -Path $SecondaryProjectPath `
            -Label 'SecondaryProjectPath' `
            -Extension '.ap21'
        if ($candidateSecondaryProjectPath.Equals($canonicalProjectPath, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'SecondaryProjectPath must differ from ProjectPath.'
        }
        $canonicalSecondaryProjectPath = $candidateSecondaryProjectPath
    }
}

$workerArguments = @('--access-mode', 'read-only')
$worker = $null
try {
    $worker = Start-JsonLineProcess -Executable $canonicalWorkerExecutable -Arguments $workerArguments
    throw 'The Task 7 shell completed preflight. Task 8 must provide the separately authorized evidence run logic.'
}
finally {
    if ($null -ne $worker) {
        try { $worker.StandardInput.Close() } catch { }
        if (-not $worker.HasExited) {
            try { $worker.Kill($true) } catch { }
        }
        $worker.Dispose()
    }
}
