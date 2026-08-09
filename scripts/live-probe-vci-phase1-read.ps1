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

$sessionIds = @('session-1', 'session-2')
$negativeCaseIds = @(
    'N-FMT-FOREIGN', 'N-FMT-NULL', 'N-FMT-UNSUPPORTED',
    'N-GRP-FIND-EMPTY', 'N-GRP-FIND-MISSING', 'N-GRP-FIND-NULL',
    'N-GRP-FIND-WHITESPACE', 'N-MAP-INACCESSIBLE-FILE',
    'N-MAP-MISSING-FILE', 'N-WS-FIND-EMPTY', 'N-WS-FIND-MISSING',
    'N-WS-FIND-NULL', 'N-WS-FIND-WHITESPACE'
)
$validWorkerOutcomes = @('returned', 'returned_null', 'not_observable', 'threw')
$probeBudgets = [ordered]@{
    maxGroupDepth = 16
    maxGroups = 500
    maxWorkspaces = 500
    maxMappings = 5000
    maxEngineeringObjects = 200
    maxCollectionItems = 5000
}
$filesystemBudgets = [ordered]@{
    maxFiles = 100000
    maxBytes = 10737418240
}

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
            if (-not $item.PSIsContainer) {
                throw 'EvidenceRoot and its existing ancestors must be directories.'
            }
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'EvidenceRoot cannot contain a reparse-point ancestor.'
            }
        }

        if ($cursor.Equals($RepositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
            break
        }
        $parent = [IO.Directory]::GetParent($cursor)
        if ($null -eq $parent) {
            throw 'EvidenceRoot does not resolve beneath the repository boundary.'
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

function Test-TransportProbeResponse {
    param([Parameter(Mandatory)] [string] $ResponseText)

    try {
        $document = [Text.Json.JsonDocument]::Parse($ResponseText)
    }
    catch {
        throw 'Worker transport preflight returned malformed JSONL.'
    }

    try {
        $root = $document.RootElement
        if ($root.ValueKind -ne [Text.Json.JsonValueKind]::Object) {
            throw 'Worker transport preflight response must be an object.'
        }
        $properties = @($root.EnumerateObject())
        $names = @($properties | ForEach-Object { $_.Name } | Sort-Object)
        if ($names.Count -ne 3 -or ($names -join '|') -ne 'error|failureCategory|success') {
            throw 'Worker transport preflight response has an unexpected envelope.'
        }
        if ($root.GetProperty('success').ValueKind -ne [Text.Json.JsonValueKind]::False) {
            throw 'Worker transport preflight response must be a denial.'
        }
        if ($root.GetProperty('failureCategory').GetString() -ne 'access_denied') {
            throw 'Worker transport preflight response has an unexpected failure category.'
        }
        $expectedError = "Operation '__task7_transport_probe__' is disabled because the worker is running in read-only mode."
        if ($root.GetProperty('error').GetString() -ne $expectedError) {
            throw 'Worker transport preflight response has an unexpected denial message.'
        }
    }
    finally {
        $document.Dispose()
    }
}

function Get-UtcTimestamp {
    return [DateTime]::UtcNow.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
}

function ConvertTo-CanonicalValue {
    param([AllowNull()] [object] $Value)

    if ($null -eq $Value) {
        return $null
    }

    if ($Value -is [Collections.IDictionary]) {
        $result = [ordered]@{}
        foreach ($key in @($Value.Keys | ForEach-Object { [string] $_ } | Sort-Object -CaseSensitive)) {
            $result[$key] = ConvertTo-CanonicalValue -Value $Value[$key]
        }
        return $result
    }

    if ($Value -is [Collections.IEnumerable] -and $Value -isnot [string]) {
        $items = @()
        foreach ($item in $Value) {
            $items += ,(ConvertTo-CanonicalValue -Value $item)
        }
        return ,$items
    }

    if ($Value -is [string] -or $Value -is [bool] -or
        $Value -is [byte] -or $Value -is [sbyte] -or
        $Value -is [int16] -or $Value -is [uint16] -or
        $Value -is [int32] -or $Value -is [uint32] -or
        $Value -is [int64] -or $Value -is [uint64] -or
        $Value -is [single] -or $Value -is [double] -or $Value -is [decimal]) {
        return $Value
    }

    $properties = @($Value.PSObject.Properties | Where-Object { $_.IsGettable } | Sort-Object Name -CaseSensitive)
    if ($properties.Count -gt 0) {
        $result = [ordered]@{}
        foreach ($property in $properties) {
            $result[$property.Name] = ConvertTo-CanonicalValue -Value $property.Value
        }
        return $result
    }

    throw "Unsupported canonical evidence value type '$($Value.GetType().FullName)'."
}

function ConvertTo-CanonicalJson {
    param([AllowNull()] [object] $Value)

    $canonical = ConvertTo-CanonicalValue -Value $Value
    return $canonical | ConvertTo-Json -Compress -Depth 100
}

function Get-Sha256Text {
    param([Parameter(Mandatory)] [string] $Text)

    $encoding = [Text.UTF8Encoding]::new($false)
    $bytes = $encoding.GetBytes($Text)
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        return [Convert]::ToHexString($sha256.ComputeHash($bytes)).ToLowerInvariant()
    }
    finally {
        $sha256.Dispose()
    }
}

function Write-AtomicJsonDocument {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [AllowNull()] [object] $Value
    )

    $json = $Value | ConvertTo-Json -Compress -Depth 100
    $encoding = [Text.UTF8Encoding]::new($false)
    $temporaryPath = "$Path.tmp-$([Guid]::NewGuid().ToString('N'))"
    try {
        [IO.File]::WriteAllBytes($temporaryPath, $encoding.GetBytes($json + [Environment]::NewLine))
        [IO.File]::Move($temporaryPath, $Path, $true)
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Open-CasesWriter {
    param([Parameter(Mandatory)] [string] $Path)

    $stream = [IO.FileStream]::new(
        $Path,
        [IO.FileMode]::CreateNew,
        [IO.FileAccess]::Write,
        [IO.FileShare]::Read)
    try {
        return [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false))
    }
    catch {
        $stream.Dispose()
        throw
    }
}

function Write-CaseRecord {
    param(
        [Parameter(Mandatory)] [IO.StreamWriter] $Writer,
        [Parameter(Mandatory)] [object] $Record
    )

    $Writer.WriteLine(($Record | ConvertTo-Json -Compress -Depth 100))
    $Writer.Flush()
    $Writer.BaseStream.Flush($true)
}

function Stop-JsonLineProcess {
    param([Parameter(Mandatory)] [Diagnostics.Process] $Process)

    try { $Process.StandardInput.Close() } catch { }
    if (-not $Process.HasExited) {
        try { $Process.Kill($true) } catch { }
    }
    try { [void] $Process.WaitForExit(5000) } catch { }
    $Process.Dispose()
}

function Read-WorkerTerminal {
    param(
        [Parameter(Mandatory)] [Diagnostics.Process] $Process,
        [Parameter(Mandatory)] [int] $TimeoutSeconds
    )

    $readTask = $Process.StandardOutput.ReadLineAsync()
    $exitTask = $Process.WaitForExitAsync()
    $delayTask = [Threading.Tasks.Task]::Delay($TimeoutSeconds * 1000)
    $tasks = [Threading.Tasks.Task[]] @($readTask, $exitTask, $delayTask)
    $completed = [Threading.Tasks.Task]::WhenAny($tasks).GetAwaiter().GetResult()

    if ([object]::ReferenceEquals($completed, $readTask)) {
        $line = $readTask.GetAwaiter().GetResult()
        if ($null -ne $line) {
            return [ordered]@{ kind = 'response'; line = $line; exitCode = $null }
        }
        $exitCode = if ($Process.HasExited) { $Process.ExitCode } else { $null }
        return [ordered]@{ kind = 'process_lost'; line = $null; exitCode = $exitCode }
    }

    if ([object]::ReferenceEquals($completed, $exitTask)) {
        if ($readTask.Wait(1000)) {
            $line = $readTask.GetAwaiter().GetResult()
            if ($null -ne $line) {
                return [ordered]@{ kind = 'response'; line = $line; exitCode = $Process.ExitCode }
            }
        }
        $exitCode = if ($Process.HasExited) { $Process.ExitCode } else { $null }
        return [ordered]@{ kind = 'process_lost'; line = $null; exitCode = $exitCode }
    }

    return [ordered]@{ kind = 'timed_out'; line = $null; exitCode = $null }
}

function New-CaseDefinition {
    param(
        [Parameter(Mandatory)] [string] $CaseId,
        [Parameter(Mandatory)] [string] $Phase,
        [AllowNull()] [object] $Workspace,
        [AllowNull()] [object] $EngineeringObject,
        [AllowNull()] [string] $TargetName,
        [AllowNull()] [string] $SecondaryProjectPath
    )

    return [ordered]@{
        caseId = $CaseId
        phase = $Phase
        workspace = $Workspace
        engineeringObject = $EngineeringObject
        targetName = $TargetName
        secondaryProjectPath = $SecondaryProjectPath
    }
}

function Get-CaseInstanceId {
    param([Parameter(Mandatory)] [Collections.IDictionary] $Definition)

    $identity = [ordered]@{
        caseId = $Definition.caseId
        phase = $Definition.phase
        workspace = $Definition.workspace
        engineeringObject = $Definition.engineeringObject
        targetName = $Definition.targetName
        secondaryProjectPath = $Definition.secondaryProjectPath
    }
    $hash = Get-Sha256Text -Text (ConvertTo-CanonicalJson -Value $identity)
    return "$($Definition.caseId.ToLowerInvariant())-$($hash.Substring(0, 20))"
}

function New-ProbeWorkerRequest {
    param(
        [Parameter(Mandatory)] [string] $RunId,
        [Parameter(Mandatory)] [string] $SessionId,
        [Parameter(Mandatory)] [string] $ProjectPath,
        [Parameter(Mandatory)] [Collections.IDictionary] $Definition
    )

    $probe = [ordered]@{
        schemaVersion = 'vci-read-probe/v1'
        runId = $RunId
        sessionId = $SessionId
        caseId = $Definition.caseId
        caseInstanceId = Get-CaseInstanceId -Definition $Definition
        targetName = $Definition.targetName
        workspace = $Definition.workspace
        engineeringObject = $Definition.engineeringObject
        secondaryProjectPath = $Definition.secondaryProjectPath
        maxGroupDepth = $probeBudgets.maxGroupDepth
        maxGroups = $probeBudgets.maxGroups
        maxWorkspaces = $probeBudgets.maxWorkspaces
        maxMappings = $probeBudgets.maxMappings
        maxEngineeringObjects = $probeBudgets.maxEngineeringObjects
        maxCollectionItems = $probeBudgets.maxCollectionItems
    }

    return [ordered]@{
        method = 'probe_vci_read_contract'
        projectPath = $ProjectPath
        vciProbe = $Probe
    }
}

function New-SnapshotDefinitions {
    param([Parameter(Mandatory)] [string] $Phase)

    return @(
        (New-CaseDefinition -CaseId 'R-SVC' -Phase $Phase -Workspace $null -EngineeringObject $null -TargetName $null -SecondaryProjectPath $null),
        (New-CaseDefinition -CaseId 'R-GRP' -Phase $Phase -Workspace $null -EngineeringObject $null -TargetName $null -SecondaryProjectPath $null),
        (New-CaseDefinition -CaseId 'R-WS' -Phase $Phase -Workspace $null -EngineeringObject $null -TargetName $null -SecondaryProjectPath $null),
        (New-CaseDefinition -CaseId 'R-MAP' -Phase $Phase -Workspace $null -EngineeringObject $null -TargetName $null -SecondaryProjectPath $null)
    )
}

function Get-FormatPairs {
    param([AllowNull()] [object[]] $Mappings)

    $pairs = [Collections.Generic.List[object]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($mapping in @($Mappings)) {
        if ($null -eq $mapping -or $null -eq $mapping.selector) {
            continue
        }
        $workspace = $mapping.selector.workspace
        $engineeringObject = $mapping.selector.engineeringObject
        if ($null -eq $workspace -or $null -eq $engineeringObject) {
            continue
        }
        $pair = [ordered]@{ workspace = $workspace; engineeringObject = $engineeringObject }
        $key = ConvertTo-CanonicalJson -Value $pair
        if ($seen.Add($key)) {
            $pairs.Add($pair)
        }
    }
    return $pairs.ToArray()
}

function Get-GroupPathInventory {
    param([AllowNull()] [object[]] $GroupSnapshots)

    $inventory = [Collections.Generic.List[object]]::new()
    $pathsByKey = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $rootPath = @()
    $pathsByKey.Add('root', $rootPath)
    $inventory.Add([ordered]@{
            canonicalKey = 'root'
            selector = [ordered]@{ groupPath = $rootPath; workspaceName = ''; canonicalRootPath = $null }
        })

    foreach ($group in @($GroupSnapshots)) {
        $canonicalKey = [string] $group.canonicalKey
        $parentKey = if ([string]::IsNullOrWhiteSpace([string] $group.parentCanonicalKey)) {
            'root'
        }
        else {
            [string] $group.parentCanonicalKey
        }
        if ([string]::IsNullOrWhiteSpace($canonicalKey) -or $pathsByKey.ContainsKey($canonicalKey)) {
            throw 'malformed_worker_payload: group inventory contains a missing or duplicate canonicalKey.'
        }
        if (-not $pathsByKey.ContainsKey($parentKey)) {
            throw 'malformed_worker_payload: group inventory is not in parent-before-child order.'
        }

        $prefix = $parentKey + '/'
        if (-not $canonicalKey.StartsWith($prefix, [StringComparison]::Ordinal)) {
            throw 'malformed_worker_payload: group canonicalKey does not match parentCanonicalKey.'
        }
        $segmentText = $canonicalKey.Substring($prefix.Length)
        $firstColon = $segmentText.IndexOf(':')
        $secondColon = if ($firstColon -ge 0) { $segmentText.IndexOf(':', $firstColon + 1) } else { -1 }
        [int] $index = 0
        [int] $sameNameOrdinal = 0
        if ($firstColon -le 0 -or $secondColon -le ($firstColon + 1) -or
            -not [int]::TryParse($segmentText.Substring(0, $firstColon), [ref] $index) -or
            -not [int]::TryParse($segmentText.Substring($firstColon + 1, $secondColon - $firstColon - 1), [ref] $sameNameOrdinal) -or
            $segmentText.Substring($secondColon + 1) -cne [string] $group.name) {
            throw 'malformed_worker_payload: group canonicalKey cannot be converted to a complete selector.'
        }

        $groupPath = @($pathsByKey[$parentKey]) + ,([ordered]@{
                index = $index
                name = [string] $group.name
                sameNameOrdinal = $sameNameOrdinal
            })
        $pathsByKey.Add($canonicalKey, $groupPath)
        $inventory.Add([ordered]@{
                canonicalKey = $canonicalKey
                selector = [ordered]@{ groupPath = $groupPath; workspaceName = ''; canonicalRootPath = $null }
            })
    }
    return $inventory.ToArray()
}

function Get-WorkspaceInventory {
    param(
        [AllowNull()] [object[]] $WorkspaceSnapshots,
        [Parameter(Mandatory)] [object[]] $GroupPathInventory
    )

    $inventory = [Collections.Generic.List[object]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($workspace in @($WorkspaceSnapshots)) {
        $canonicalKey = [string] $workspace.canonicalKey
        if ([string]::IsNullOrWhiteSpace($canonicalKey) -or -not $seen.Add($canonicalKey)) {
            throw 'malformed_worker_payload: workspace inventory contains a missing or duplicate canonicalKey.'
        }

        $owner = $null
        foreach ($candidate in $GroupPathInventory) {
            $prefix = [string] $candidate.canonicalKey + '/workspace:'
            if ($canonicalKey.StartsWith($prefix, [StringComparison]::Ordinal) -and
                ($null -eq $owner -or ([string] $candidate.canonicalKey).Length -gt ([string] $owner.canonicalKey).Length)) {
                $owner = $candidate
            }
        }
        if ($null -eq $owner) {
            throw 'malformed_worker_payload: workspace canonicalKey has no discovered owning group.'
        }

        $workspacePrefix = [string] $owner.canonicalKey + '/workspace:'
        $workspaceSegment = $canonicalKey.Substring($workspacePrefix.Length)
        $nameSeparator = $workspaceSegment.IndexOf(':')
        [int] $workspaceIndex = 0
        if ($nameSeparator -le 0 -or
            -not [int]::TryParse($workspaceSegment.Substring(0, $nameSeparator), [ref] $workspaceIndex) -or
            $workspaceSegment.Substring($nameSeparator + 1) -cne [string] $workspace.name) {
            throw 'malformed_worker_payload: workspace canonicalKey cannot be converted to a complete selector.'
        }

        $inventory.Add([ordered]@{
                canonicalKey = $canonicalKey
                selector = [ordered]@{
                    groupPath = @($owner.selector.groupPath)
                    workspaceName = [string] $workspace.name
                    canonicalRootPath = if ($null -eq $workspace.rootPath) { $null } else { [string] $workspace.rootPath }
                }
            })
    }
    return $inventory.ToArray()
}

function New-CaseMatrix {
    param(
        [AllowNull()] [object[]] $Mappings,
        [AllowNull()] [object[]] $GroupSnapshots,
        [AllowNull()] [object[]] $WorkspaceSnapshots,
        [AllowNull()] [string] $SecondaryProjectPath
    )

    $matrix = [Collections.Generic.List[object]]::new()
    $formatPairs = @(Get-FormatPairs -Mappings $Mappings)
    if ($formatPairs.Count -eq 0) {
        $emptyWorkspace = [ordered]@{ groupPath = @(); workspaceName = ''; canonicalRootPath = $null }
        $emptyObject = [ordered]@{ stableIdentifier = $null; structuralPath = @(); fingerprint = $null }
        $matrix.Add((New-CaseDefinition -CaseId 'R-FMT' -Phase 'matrix' -Workspace $emptyWorkspace -EngineeringObject $emptyObject -TargetName $null -SecondaryProjectPath $null))
    }
    else {
        foreach ($pair in $formatPairs) {
            $matrix.Add((New-CaseDefinition -CaseId 'R-FMT' -Phase 'matrix' -Workspace $pair.workspace -EngineeringObject $pair.engineeringObject -TargetName $null -SecondaryProjectPath $null))
        }
    }

    $groupInventory = @(Get-GroupPathInventory -GroupSnapshots $GroupSnapshots)
    $workspaceInventory = @(Get-WorkspaceInventory -WorkspaceSnapshots $WorkspaceSnapshots -GroupPathInventory $groupInventory)
    $nullSelector = [object[]]::new(1)
    $nullSelector[0] = $null
    $formatWorkspaces = if ($workspaceInventory.Count -eq 0) { $nullSelector } else { @($workspaceInventory | ForEach-Object { $_.selector }) }
    $parentSelectors = @($groupInventory | ForEach-Object { $_.selector })

    foreach ($caseId in $negativeCaseIds) {
        if ($caseId.StartsWith('N-FMT-', [StringComparison]::Ordinal)) {
            $workspaces = $formatWorkspaces
        }
        elseif ($caseId.StartsWith('N-GRP-', [StringComparison]::Ordinal) -or
            $caseId.StartsWith('N-WS-', [StringComparison]::Ordinal)) {
            $workspaces = $parentSelectors
        }
        else {
            $workspaces = $nullSelector
        }
        foreach ($workspace in $workspaces) {
            $secondary = if ($caseId -eq 'N-FMT-FOREIGN') { $SecondaryProjectPath } else { $null }
            $matrix.Add((New-CaseDefinition -CaseId $caseId -Phase 'matrix' -Workspace $workspace -EngineeringObject $null -TargetName $null -SecondaryProjectPath $secondary))
        }
    }

    $repeatWorkspace = if ($formatPairs.Count -gt 0) { $formatPairs[0].workspace } else { $null }
    $repeatObject = if ($formatPairs.Count -gt 0) { $formatPairs[0].engineeringObject } else { $null }
    $matrix.Add((New-CaseDefinition -CaseId 'R-REP' -Phase 'matrix' -Workspace $repeatWorkspace -EngineeringObject $repeatObject -TargetName $null -SecondaryProjectPath $null))
    $matrix.Add((New-CaseDefinition -CaseId 'R-CANARY' -Phase 'matrix' -Workspace $null -EngineeringObject $null -TargetName $null -SecondaryProjectPath $null))
    return $matrix.ToArray()
}

function New-TransportFailureRecord {
    param(
        [Parameter(Mandatory)] [ValidateSet('timed_out', 'process_lost')] [string] $Outcome,
        [Parameter(Mandatory)] [Collections.IDictionary] $Request,
        [Parameter(Mandatory)] [int] $TransportSequence,
        [Parameter(Mandatory)] [int] $WorkerProcessId,
        [AllowNull()] [Nullable[int]] $ExitCode,
        [Parameter(Mandatory)] [string] $SentUtc,
        [Parameter(Mandatory)] [string] $ReceivedUtc,
        [Parameter(Mandatory)] [long] $ElapsedMilliseconds
    )

    return [ordered]@{
        schemaVersion = 'vci-phase1-read-case-evidence/v1'
        terminal = $true
        runId = $Request.vciProbe.runId
        sessionId = $Request.vciProbe.sessionId
        caseId = $Request.vciProbe.caseId
        caseInstanceId = $Request.vciProbe.caseInstanceId
        outcome = $Outcome
        exception = $null
        evidenceFailure = $Outcome
        workerPayload = $null
        workerWarnings = @()
        transport = [ordered]@{
            kind = $Outcome
            transportSequence = $TransportSequence
            workerProcessId = $WorkerProcessId
            sentUtc = $SentUtc
            receivedUtc = $ReceivedUtc
            elapsedMilliseconds = $ElapsedMilliseconds
            exitCode = $ExitCode
        }
    }
}

function New-EvidenceFailureRecord {
    param(
        [Parameter(Mandatory)] [string] $Category,
        [Parameter(Mandatory)] [string] $Message,
        [Parameter(Mandatory)] [Collections.IDictionary] $Request,
        [Parameter(Mandatory)] [int] $TransportSequence,
        [Parameter(Mandatory)] [int] $WorkerProcessId,
        [Parameter(Mandatory)] [string] $SentUtc,
        [Parameter(Mandatory)] [string] $ReceivedUtc,
        [Parameter(Mandatory)] [long] $ElapsedMilliseconds,
        [AllowNull()] [string] $RawResponse
    )

    return [ordered]@{
        schemaVersion = 'vci-phase1-read-case-evidence/v1'
        terminal = $true
        runId = $Request.vciProbe.runId
        sessionId = $Request.vciProbe.sessionId
        caseId = $Request.vciProbe.caseId
        caseInstanceId = $Request.vciProbe.caseInstanceId
        outcome = $null
        exception = $null
        evidenceFailure = $Category
        evidenceFailureMessage = $Message
        workerPayload = $null
        workerWarnings = @()
        rawResponse = $RawResponse
        transport = [ordered]@{
            kind = 'response'
            transportSequence = $TransportSequence
            workerProcessId = $WorkerProcessId
            sentUtc = $SentUtc
            receivedUtc = $ReceivedUtc
            elapsedMilliseconds = $ElapsedMilliseconds
            exitCode = $null
        }
    }
}

function ConvertFrom-JsonHashtable {
    param(
        [Parameter(Mandatory)] [string] $Json,
        [Parameter(Mandatory)] [string] $FailureMessage
    )

    try {
        $value = $Json | ConvertFrom-Json -AsHashtable -Depth 100
    }
    catch {
        throw $FailureMessage
    }
    if ($value -isnot [Collections.IDictionary]) {
        throw $FailureMessage
    }
    return $value
}

function Assert-JsonObjectShape {
    param(
        [AllowNull()] [object] $Value,
        [Parameter(Mandatory)] [string] $Label,
        [Parameter(Mandatory)] [string[]] $RequiredFields,
        [AllowNull()] [string[]] $OptionalFields = @()
    )

    if ($Value -isnot [Collections.IDictionary]) {
        throw "malformed_worker_payload: $Label must be an object."
    }
    $allowed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($field in @($RequiredFields) + @($OptionalFields)) {
        [void] $allowed.Add($field)
    }
    foreach ($field in $Value.Keys) {
        if (-not $allowed.Contains([string] $field)) {
            throw "malformed_worker_payload: $Label contains unknown field '$field'."
        }
    }
    foreach ($field in $RequiredFields) {
        if (-not $Value.Contains($field)) {
            throw "malformed_worker_payload: $Label is missing field '$field'."
        }
    }
}

function Assert-JsonArray {
    param(
        [AllowNull()] [object] $Value,
        [Parameter(Mandatory)] [string] $Label
    )

    if ($null -eq $Value -or $Value -is [string] -or
        $Value -is [Collections.IDictionary] -or $Value -isnot [Collections.IEnumerable]) {
        throw "malformed_worker_payload: $Label must be an array."
    }
}

function Assert-JsonString {
    param(
        [AllowNull()] [object] $Value,
        [Parameter(Mandatory)] [string] $Label
    )

    if ($Value -isnot [string]) {
        throw "malformed_worker_payload: $Label must be a string."
    }
}

function Assert-JsonNullableString {
    param(
        [AllowNull()] [object] $Value,
        [Parameter(Mandatory)] [string] $Label
    )

    if ($Value -isnot [string]) {
        throw "malformed_worker_payload: $Label must be a string when present."
    }
}

function Assert-JsonBoolean {
    param(
        [AllowNull()] [object] $Value,
        [Parameter(Mandatory)] [string] $Label
    )

    if ($Value -isnot [bool]) {
        throw "malformed_worker_payload: $Label must be a boolean."
    }
}

function Assert-JsonInteger {
    param(
        [AllowNull()] [object] $Value,
        [Parameter(Mandatory)] [string] $Label
    )

    if ($null -eq $Value -or [Type]::GetTypeCode($Value.GetType()) -notin @(
            [TypeCode]::SByte, [TypeCode]::Byte, [TypeCode]::Int16, [TypeCode]::UInt16,
            [TypeCode]::Int32, [TypeCode]::UInt32, [TypeCode]::Int64, [TypeCode]::UInt64)) {
        throw "malformed_worker_payload: $Label must be an integer."
    }
    try {
        [long] $integer = [Convert]::ToInt64($Value, [Globalization.CultureInfo]::InvariantCulture)
    }
    catch {
        throw "malformed_worker_payload: $Label must fit a CLR Int32."
    }
    if ($integer -lt [int]::MinValue -or $integer -gt [int]::MaxValue) {
        throw "malformed_worker_payload: $Label must fit a CLR Int32."
    }
}

function Assert-VciProbeException {
    param(
        [AllowNull()] [object] $Value,
        [Parameter(Mandatory)] [string] $Label,
        [int] $Depth = 0
    )

    if ($Depth -gt 32) {
        throw "malformed_worker_payload: $Label exceeds the supported nested exception depth."
    }
    Assert-JsonObjectShape -Value $Value -Label $Label `
        -RequiredFields @('exceptionTypeName', 'message', 'hResult') `
        -OptionalFields @('innerException')
    Assert-JsonString -Value $Value.exceptionTypeName -Label "$Label.exceptionTypeName"
    Assert-JsonString -Value $Value.message -Label "$Label.message"
    Assert-JsonInteger -Value $Value.hResult -Label "$Label.hResult"
    if ($Value.Contains('innerException')) {
        Assert-VciProbeException -Value $Value.innerException -Label "$Label.innerException" -Depth ($Depth + 1)
    }
}

function Assert-VciProbeMember {
    param(
        [AllowNull()] [object] $Value,
        [Parameter(Mandatory)] [string] $Label
    )

    Assert-JsonObjectShape -Value $Value -Label $Label `
        -RequiredFields @('name', 'clrTypeName', 'isNull') `
        -OptionalFields @('stringValue', 'exception')
    Assert-JsonString -Value $Value.name -Label "$Label.name"
    Assert-JsonString -Value $Value.clrTypeName -Label "$Label.clrTypeName"
    Assert-JsonBoolean -Value $Value.isNull -Label "$Label.isNull"
    if ($Value.Contains('stringValue')) {
        Assert-JsonNullableString -Value $Value.stringValue -Label "$Label.stringValue"
    }
    if ($Value.Contains('exception')) {
        Assert-VciProbeException -Value $Value.exception -Label "$Label.exception"
    }
}

function Assert-VciProbeReturn {
    param(
        [AllowNull()] [object] $Value,
        [Parameter(Mandatory)] [string] $Label
    )

    Assert-JsonObjectShape -Value $Value -Label $Label `
        -RequiredFields @('clrTypeName', 'isNull', 'members') `
        -OptionalFields @('stringValue')
    Assert-JsonString -Value $Value.clrTypeName -Label "$Label.clrTypeName"
    Assert-JsonBoolean -Value $Value.isNull -Label "$Label.isNull"
    if ($Value.Contains('stringValue')) {
        Assert-JsonNullableString -Value $Value.stringValue -Label "$Label.stringValue"
    }
    Assert-JsonArray -Value $Value.members -Label "$Label.members"
    $index = 0
    foreach ($member in @($Value.members)) {
        Assert-VciProbeMember -Value $member -Label "$Label.members[$index]"
        $index++
    }
}

function Assert-VciWorkspaceSelector {
    param(
        [AllowNull()] [object] $Value,
        [Parameter(Mandatory)] [string] $Label
    )

    Assert-JsonObjectShape -Value $Value -Label $Label `
        -RequiredFields @('groupPath', 'workspaceName') `
        -OptionalFields @('canonicalRootPath')
    Assert-JsonArray -Value $Value.groupPath -Label "$Label.groupPath"
    $index = 0
    foreach ($segment in @($Value.groupPath)) {
        $segmentLabel = "$Label.groupPath[$index]"
        Assert-JsonObjectShape -Value $segment -Label $segmentLabel `
            -RequiredFields @('index', 'name', 'sameNameOrdinal') -OptionalFields @()
        Assert-JsonInteger -Value $segment.index -Label "$segmentLabel.index"
        Assert-JsonString -Value $segment.name -Label "$segmentLabel.name"
        Assert-JsonInteger -Value $segment.sameNameOrdinal -Label "$segmentLabel.sameNameOrdinal"
        $index++
    }
    Assert-JsonString -Value $Value.workspaceName -Label "$Label.workspaceName"
    if ($Value.Contains('canonicalRootPath')) {
        Assert-JsonNullableString -Value $Value.canonicalRootPath -Label "$Label.canonicalRootPath"
    }
}

function Assert-VciEngineeringObjectSelector {
    param(
        [AllowNull()] [object] $Value,
        [Parameter(Mandatory)] [string] $Label
    )

    Assert-JsonObjectShape -Value $Value -Label $Label `
        -RequiredFields @('structuralPath') `
        -OptionalFields @('stableIdentifier', 'fingerprint')
    foreach ($field in @('stableIdentifier', 'fingerprint')) {
        if ($Value.Contains($field)) {
            Assert-JsonNullableString -Value $Value[$field] -Label "$Label.$field"
        }
    }
    Assert-JsonArray -Value $Value.structuralPath -Label "$Label.structuralPath"
    $index = 0
    foreach ($segment in @($Value.structuralPath)) {
        $segmentLabel = "$Label.structuralPath[$index]"
        Assert-JsonObjectShape -Value $segment -Label $segmentLabel `
            -RequiredFields @('index', 'name', 'objectType') -OptionalFields @()
        Assert-JsonInteger -Value $segment.index -Label "$segmentLabel.index"
        Assert-JsonString -Value $segment.name -Label "$segmentLabel.name"
        Assert-JsonString -Value $segment.objectType -Label "$segmentLabel.objectType"
        $index++
    }
}

function Assert-VciMappingSelector {
    param(
        [AllowNull()] [object] $Value,
        [Parameter(Mandatory)] [string] $Label
    )

    Assert-JsonObjectShape -Value $Value -Label $Label `
        -RequiredFields @('workspace', 'engineeringObject') `
        -OptionalFields @('relativeDirectory', 'fileName', 'format')
    Assert-VciWorkspaceSelector -Value $Value.workspace -Label "$Label.workspace"
    Assert-VciEngineeringObjectSelector -Value $Value.engineeringObject -Label "$Label.engineeringObject"
    foreach ($field in @('relativeDirectory', 'fileName', 'format')) {
        if ($Value.Contains($field)) {
            Assert-JsonNullableString -Value $Value[$field] -Label "$Label.$field"
        }
    }
}

function Assert-VciProbeSnapshot {
    param(
        [AllowNull()] [object] $Value,
        [Parameter(Mandatory)] [string] $Label
    )

    Assert-JsonObjectShape -Value $Value -Label $Label `
        -RequiredFields @('members', 'groups', 'workspaces', 'mappings', 'candidates') `
        -OptionalFields @('service', 'candidateCollectionRuntimeType')

    Assert-JsonArray -Value $Value.members -Label "$Label.members"
    $index = 0
    foreach ($member in @($Value.members)) {
        Assert-VciProbeMember -Value $member -Label "$Label.members[$index]"
        $index++
    }

    if ($Value.Contains('service')) {
        Assert-JsonObjectShape -Value $Value.service -Label "$Label.service" `
            -RequiredFields @('serviceAvailable', 'rootGroupAvailable', 'rootGroupCount') -OptionalFields @()
        Assert-JsonBoolean -Value $Value.service.serviceAvailable -Label "$Label.service.serviceAvailable"
        Assert-JsonBoolean -Value $Value.service.rootGroupAvailable -Label "$Label.service.rootGroupAvailable"
        Assert-JsonInteger -Value $Value.service.rootGroupCount -Label "$Label.service.rootGroupCount"
    }

    Assert-JsonArray -Value $Value.groups -Label "$Label.groups"
    $index = 0
    foreach ($group in @($Value.groups)) {
        $itemLabel = "$Label.groups[$index]"
        Assert-JsonObjectShape -Value $group -Label $itemLabel `
            -RequiredFields @('enumerationIndex', 'canonicalKey', 'name', 'depth', 'childGroupCount', 'workspaceCount') `
            -OptionalFields @('parentCanonicalKey')
        foreach ($field in @('enumerationIndex', 'depth', 'childGroupCount', 'workspaceCount')) {
            Assert-JsonInteger -Value $group[$field] -Label "$itemLabel.$field"
        }
        Assert-JsonString -Value $group.canonicalKey -Label "$itemLabel.canonicalKey"
        Assert-JsonString -Value $group.name -Label "$itemLabel.name"
        if ($group.Contains('parentCanonicalKey')) {
            Assert-JsonNullableString -Value $group.parentCanonicalKey -Label "$itemLabel.parentCanonicalKey"
        }
        $index++
    }

    Assert-JsonArray -Value $Value.workspaces -Label "$Label.workspaces"
    $index = 0
    foreach ($workspace in @($Value.workspaces)) {
        $itemLabel = "$Label.workspaces[$index]"
        Assert-JsonObjectShape -Value $workspace -Label $itemLabel `
            -RequiredFields @('enumerationIndex', 'canonicalKey', 'name', 'deleteUnusedTypeVersionFromLibrary', 'mappedObjectCount') `
            -OptionalFields @('rootPath', 'comment', 'workspaceLanguage', 'globalLibraryPath')
        Assert-JsonInteger -Value $workspace.enumerationIndex -Label "$itemLabel.enumerationIndex"
        Assert-JsonString -Value $workspace.canonicalKey -Label "$itemLabel.canonicalKey"
        Assert-JsonString -Value $workspace.name -Label "$itemLabel.name"
        Assert-JsonBoolean -Value $workspace.deleteUnusedTypeVersionFromLibrary -Label "$itemLabel.deleteUnusedTypeVersionFromLibrary"
        Assert-JsonInteger -Value $workspace.mappedObjectCount -Label "$itemLabel.mappedObjectCount"
        foreach ($field in @('rootPath', 'comment', 'workspaceLanguage', 'globalLibraryPath')) {
            if ($workspace.Contains($field)) {
                Assert-JsonNullableString -Value $workspace[$field] -Label "$itemLabel.$field"
            }
        }
        $index++
    }

    Assert-JsonArray -Value $Value.mappings -Label "$Label.mappings"
    $index = 0
    foreach ($mapping in @($Value.mappings)) {
        $itemLabel = "$Label.mappings[$index]"
        Assert-JsonObjectShape -Value $mapping -Label $itemLabel `
            -RequiredFields @('enumerationIndex', 'canonicalKey', 'selector') `
            -OptionalFields @('status', 'statusProperty', 'getStatus', 'childStatus')
        Assert-JsonInteger -Value $mapping.enumerationIndex -Label "$itemLabel.enumerationIndex"
        Assert-JsonString -Value $mapping.canonicalKey -Label "$itemLabel.canonicalKey"
        Assert-VciMappingSelector -Value $mapping.selector -Label "$itemLabel.selector"
        foreach ($field in @('status', 'statusProperty', 'getStatus', 'childStatus')) {
            if ($mapping.Contains($field)) {
                Assert-JsonNullableString -Value $mapping[$field] -Label "$itemLabel.$field"
            }
        }
        $index++
    }

    Assert-JsonArray -Value $Value.candidates -Label "$Label.candidates"
    $index = 0
    foreach ($candidate in @($Value.candidates)) {
        $itemLabel = "$Label.candidates[$index]"
        Assert-JsonObjectShape -Value $candidate -Label $itemLabel `
            -RequiredFields @('enumerationIndex', 'canonicalKey', 'description', 'runtimeTypeName', 'isNull') `
            -OptionalFields @()
        Assert-JsonInteger -Value $candidate.enumerationIndex -Label "$itemLabel.enumerationIndex"
        Assert-JsonString -Value $candidate.canonicalKey -Label "$itemLabel.canonicalKey"
        Assert-JsonString -Value $candidate.description -Label "$itemLabel.description"
        Assert-JsonString -Value $candidate.runtimeTypeName -Label "$itemLabel.runtimeTypeName"
        Assert-JsonBoolean -Value $candidate.isNull -Label "$itemLabel.isNull"
        $index++
    }
    if ($Value.Contains('candidateCollectionRuntimeType')) {
        Assert-JsonNullableString -Value $Value.candidateCollectionRuntimeType -Label "$Label.candidateCollectionRuntimeType"
    }
}

function Assert-VciProbeRepeatability {
    param(
        [AllowNull()] [object] $Value,
        [Parameter(Mandatory)] [string] $Label
    )

    Assert-JsonObjectShape -Value $Value -Label $Label `
        -RequiredFields @('observations', 'isIdentical') -OptionalFields @()
    Assert-JsonArray -Value $Value.observations -Label "$Label.observations"
    $index = 0
    foreach ($observation in @($Value.observations)) {
        Assert-VciProbeReturn -Value $observation -Label "$Label.observations[$index]"
        $index++
    }
    Assert-JsonBoolean -Value $Value.isIdentical -Label "$Label.isIdentical"
}

function Assert-VciProbeProjectState {
    param(
        [AllowNull()] [object] $Value,
        [Parameter(Mandatory)] [string] $Label
    )

    Assert-JsonObjectShape -Value $Value -Label $Label `
        -RequiredFields @('isModifiedBefore', 'isModifiedAfter') -OptionalFields @()
    Assert-JsonBoolean -Value $Value.isModifiedBefore -Label "$Label.isModifiedBefore"
    Assert-JsonBoolean -Value $Value.isModifiedAfter -Label "$Label.isModifiedAfter"
}

function Assert-VciProbeOmission {
    param(
        [AllowNull()] [object] $Value,
        [Parameter(Mandatory)] [string] $Label
    )

    Assert-JsonObjectShape -Value $Value -Label $Label `
        -RequiredFields @('reason', 'budgetName', 'budgetValue', 'observedCount') `
        -OptionalFields @('traversalPath')
    Assert-JsonString -Value $Value.reason -Label "$Label.reason"
    Assert-JsonString -Value $Value.budgetName -Label "$Label.budgetName"
    Assert-JsonInteger -Value $Value.budgetValue -Label "$Label.budgetValue"
    Assert-JsonInteger -Value $Value.observedCount -Label "$Label.observedCount"
    if ($Value.Contains('traversalPath')) {
        Assert-JsonNullableString -Value $Value.traversalPath -Label "$Label.traversalPath"
    }
}

function Assert-VciProbePayload {
    param([AllowNull()] [object] $Value)

    Assert-JsonObjectShape -Value $Value -Label 'payload' `
        -RequiredFields @('schemaVersion', 'runId', 'sessionId', 'caseId', 'caseInstanceId', 'outcome', 'projectState', 'omissions') `
        -OptionalFields @('return', 'snapshot', 'exception', 'repeatability', 'notObservableReason')
    foreach ($field in @('schemaVersion', 'runId', 'sessionId', 'caseId', 'caseInstanceId', 'outcome')) {
        Assert-JsonString -Value $Value[$field] -Label "payload.$field"
    }
    if ($Value.Contains('return')) {
        Assert-VciProbeReturn -Value $Value.return -Label 'payload.return'
    }
    if ($Value.Contains('snapshot')) {
        Assert-VciProbeSnapshot -Value $Value.snapshot -Label 'payload.snapshot'
    }
    if ($Value.Contains('exception')) {
        Assert-VciProbeException -Value $Value.exception -Label 'payload.exception'
    }
    if ($Value.Contains('repeatability')) {
        Assert-VciProbeRepeatability -Value $Value.repeatability -Label 'payload.repeatability'
    }
    if ($Value.Contains('notObservableReason')) {
        Assert-JsonNullableString -Value $Value.notObservableReason -Label 'payload.notObservableReason'
    }
    Assert-VciProbeProjectState -Value $Value.projectState -Label 'payload.projectState'
    Assert-JsonArray -Value $Value.omissions -Label 'payload.omissions'
    $index = 0
    foreach ($omission in @($Value.omissions)) {
        Assert-VciProbeOmission -Value $omission -Label "payload.omissions[$index]"
        $index++
    }

    $hasReturn = $Value.Contains('return')
    $hasSnapshot = $Value.Contains('snapshot')
    $hasException = $Value.Contains('exception')
    $hasRepeatability = $Value.Contains('repeatability')
    $hasNotObservableReason = $Value.Contains('notObservableReason')
    switch ([string] $Value.outcome) {
        'not_observable' {
            if (-not $hasNotObservableReason -or
                [string]::IsNullOrWhiteSpace([string] $Value.notObservableReason)) {
                throw 'malformed_worker_payload: not_observable outcome requires a non-empty reason.'
            }
            if ($hasReturn -or $hasSnapshot -or $hasException -or $hasRepeatability) {
                throw 'malformed_worker_payload: not_observable outcome contains a contradictory result branch.'
            }
        }
        'threw' {
            if (-not $hasException) {
                throw 'malformed_worker_payload: threw outcome requires exception evidence.'
            }
            if ($hasReturn -or $hasSnapshot -or $hasRepeatability -or $hasNotObservableReason) {
                throw 'malformed_worker_payload: threw outcome contains a contradictory result branch.'
            }
        }
        'returned_null' {
            if (-not $hasReturn -or -not $Value.return.isNull) {
                throw 'malformed_worker_payload: returned_null outcome requires a null return observation.'
            }
            if ($hasSnapshot -or $hasException -or $hasRepeatability -or $hasNotObservableReason) {
                throw 'malformed_worker_payload: returned_null outcome contains a contradictory result branch.'
            }
        }
        'returned' {
            if ($hasException -or $hasNotObservableReason) {
                throw 'malformed_worker_payload: returned outcome contains contradictory exception or reason evidence.'
            }
            if ($Value.caseId -in @('R-SVC', 'R-GRP', 'R-WS', 'R-MAP', 'R-FMT', 'R-CANARY')) {
                if (-not $hasSnapshot -or $hasReturn -or $hasRepeatability) {
                    throw 'malformed_worker_payload: returned snapshot case requires only snapshot evidence.'
                }
            }
            elseif ($Value.caseId -eq 'R-REP') {
                if (-not $hasRepeatability -or $hasReturn -or $hasSnapshot) {
                    throw 'malformed_worker_payload: returned repeatability case requires only repeatability evidence.'
                }
            }
            elseif ($Value.caseId.StartsWith('N-', [StringComparison]::Ordinal)) {
                if (-not $hasReturn -or $Value.return.isNull -or $hasSnapshot -or $hasRepeatability) {
                    throw 'malformed_worker_payload: returned observation case requires only a non-null return observation.'
                }
            }
            else {
                throw "malformed_worker_payload: returned outcome has unsupported case '$($Value.caseId)'."
            }
        }
        default {
            throw "malformed_worker_payload: worker outcome '$($Value.outcome)' is not a worker outcome."
        }
    }
}

function Test-WorkerPayload {
    param(
        [Parameter(Mandatory)] [string] $ResponseText,
        [Parameter(Mandatory)] [Collections.IDictionary] $Request,
        [Parameter(Mandatory)] [string] $ExpectedProjectPath
    )

    $envelope = ConvertFrom-JsonHashtable -Json $ResponseText -FailureMessage 'malformed_worker_payload: worker envelope is not a JSON object.'
    $allowedEnvelopeFields = @('success', 'payload', 'error', 'failureCategory', 'warnings', 'resolvedProjectPath')
    foreach ($name in $envelope.Keys) {
        if ($name -notin $allowedEnvelopeFields) {
            throw "malformed_worker_payload: unknown worker envelope field '$name'."
        }
    }
    if ($envelope.success -isnot [bool] -or -not $envelope.success) {
        $category = if ([string]::IsNullOrWhiteSpace([string] $envelope.failureCategory)) { 'protocol_error' } else { [string] $envelope.failureCategory }
        $message = if ([string]::IsNullOrWhiteSpace([string] $envelope.error)) { 'Worker returned an unsuccessful envelope.' } else { [string] $envelope.error }
        throw "${category}: $message"
    }
    if ($envelope.payload -isnot [string] -or [string]::IsNullOrWhiteSpace($envelope.payload)) {
        throw 'malformed_worker_payload: successful worker envelope has no typed payload.'
    }
    if ($envelope.Contains('resolvedProjectPath')) {
        Assert-JsonNullableString -Value $envelope.resolvedProjectPath -Label 'envelope.resolvedProjectPath'
    }
    if ($envelope.Contains('resolvedProjectPath') -and
        -not [string]::IsNullOrWhiteSpace([string] $envelope.resolvedProjectPath) -and
        -not ([string] $envelope.resolvedProjectPath).Equals($ExpectedProjectPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'malformed_worker_payload: worker resolved a different project path.'
    }

    $payload = ConvertFrom-JsonHashtable -Json $envelope.payload -FailureMessage 'malformed_worker_payload: typed payload is not a JSON object.'
    Assert-VciProbePayload -Value $payload
    if (-not $payload.Contains('schemaVersion') -or $payload.schemaVersion -ne 'vci-read-probe/v1') {
        throw 'schema_mismatch: worker payload schema is not vci-read-probe/v1.'
    }
    foreach ($field in @('runId', 'sessionId', 'caseId', 'caseInstanceId')) {
        if (-not $payload.Contains($field) -or $payload[$field] -ne $Request.vciProbe[$field]) {
            throw "malformed_worker_payload: worker payload field '$field' did not echo the request."
        }
    }
    if ($payload.outcome -notin $validWorkerOutcomes) {
        throw "malformed_worker_payload: worker outcome '$($payload.outcome)' is not a worker outcome."
    }
    if ($payload.outcome -eq 'threw' -and
        (-not $payload.Contains('exception') -or $payload.exception -isnot [Collections.IDictionary])) {
        throw 'malformed_worker_payload: threw outcome has no typed exception evidence.'
    }
    if ($payload.outcome -eq 'not_observable' -and
        (-not $payload.Contains('notObservableReason') -or [string]::IsNullOrWhiteSpace([string] $payload.notObservableReason))) {
        throw 'malformed_worker_payload: not_observable outcome has no reason.'
    }
    $warnings = @()
    if ($envelope.Contains('warnings') -and $null -ne $envelope.warnings) {
        Assert-JsonArray -Value $envelope.warnings -Label 'envelope.warnings'
        $warningIndex = 0
        foreach ($warning in @($envelope.warnings)) {
            Assert-JsonString -Value $warning -Label "envelope.warnings[$warningIndex]"
            $warningIndex++
        }
        $warnings = @($envelope.warnings)
    }

    return [ordered]@{
        payload = $payload
        warnings = $warnings
        resolvedProjectPath = if ($envelope.Contains('resolvedProjectPath')) { $envelope.resolvedProjectPath } else { $null }
    }
}

function Invoke-ProbeRequest {
    param(
        [Parameter(Mandatory)] [Diagnostics.Process] $Worker,
        [Parameter(Mandatory)] [Collections.IDictionary] $Request,
        [Parameter(Mandatory)] [int] $TimeoutSeconds,
        [Parameter(Mandatory)] [ref] $TransportSequence,
        [AllowNull()] [IO.StreamWriter] $CasesWriter,
        [Parameter(Mandatory)] [bool] $RecordCase
    )

    $TransportSequence.Value++
    $sequence = $TransportSequence.Value
    $sentUtc = Get-UtcTimestamp
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $terminal = $null
    try {
        $requestJson = $Request | ConvertTo-Json -Compress -Depth 100
        $Worker.StandardInput.WriteLine($requestJson)
        $Worker.StandardInput.Flush()
        $terminal = Read-WorkerTerminal -Process $Worker -TimeoutSeconds $TimeoutSeconds
    }
    catch {
        $stopwatch.Stop()
        $receivedUtc = Get-UtcTimestamp
        $record = New-TransportFailureRecord `
            -Outcome 'process_lost' `
            -Request $Request `
            -TransportSequence $sequence `
            -WorkerProcessId $Worker.Id `
            -ExitCode $(if ($Worker.HasExited) { $Worker.ExitCode } else { $null }) `
            -SentUtc $sentUtc `
            -ReceivedUtc $receivedUtc `
            -ElapsedMilliseconds $stopwatch.ElapsedMilliseconds
        if ($RecordCase -and $null -ne $CasesWriter) {
            Write-CaseRecord -Writer $CasesWriter -Record $record
        }
        if (-not $RecordCase) {
            return $record
        }
        throw "process_lost: $($_.Exception.Message)"
    }

    $stopwatch.Stop()
    $receivedUtc = Get-UtcTimestamp
    if ($terminal.kind -ne 'response') {
        $record = New-TransportFailureRecord `
            -Outcome $terminal.kind `
            -Request $Request `
            -TransportSequence $sequence `
            -WorkerProcessId $Worker.Id `
            -ExitCode $terminal.exitCode `
            -SentUtc $sentUtc `
            -ReceivedUtc $receivedUtc `
            -ElapsedMilliseconds $stopwatch.ElapsedMilliseconds
        if ($RecordCase -and $null -ne $CasesWriter) {
            Write-CaseRecord -Writer $CasesWriter -Record $record
        }
        if ($terminal.kind -eq 'timed_out' -and -not $Worker.HasExited) {
            try { $Worker.Kill($true) } catch { }
        }
        if (-not $RecordCase) {
            return $record
        }
        throw "$($terminal.kind): worker did not return a terminal JSONL payload."
    }

    try {
        $validated = Test-WorkerPayload -ResponseText $terminal.line -Request $Request -ExpectedProjectPath $Request.projectPath
    }
    catch {
        $category = if ($_.Exception.Message.StartsWith('schema_mismatch:', [StringComparison]::Ordinal)) {
            'schema_mismatch'
        }
        elseif ($_.Exception.Message.StartsWith('malformed_worker_payload:', [StringComparison]::Ordinal)) {
            'malformed_worker_payload'
        }
        else {
            'protocol_error'
        }
        $record = New-EvidenceFailureRecord `
            -Category $category `
            -Message $_.Exception.Message `
            -Request $Request `
            -TransportSequence $sequence `
            -WorkerProcessId $Worker.Id `
            -SentUtc $sentUtc `
            -ReceivedUtc $receivedUtc `
            -ElapsedMilliseconds $stopwatch.ElapsedMilliseconds `
            -RawResponse $terminal.line
        if ($RecordCase -and $null -ne $CasesWriter) {
            Write-CaseRecord -Writer $CasesWriter -Record $record
        }
        if (-not $RecordCase) {
            return $record
        }
        throw
    }

    $record = [ordered]@{
        schemaVersion = 'vci-phase1-read-case-evidence/v1'
        terminal = $true
        runId = $Request.vciProbe.runId
        sessionId = $Request.vciProbe.sessionId
        caseId = $Request.vciProbe.caseId
        caseInstanceId = $Request.vciProbe.caseInstanceId
        outcome = $validated.payload.outcome
        exception = if ($validated.payload.Contains('exception')) { $validated.payload.exception } else { $null }
        evidenceFailure = $null
        workerPayload = $validated.payload
        workerWarnings = $validated.warnings
        resolvedProjectPath = $validated.resolvedProjectPath
        transport = [ordered]@{
            kind = 'response'
            transportSequence = $sequence
            workerProcessId = $Worker.Id
            sentUtc = $sentUtc
            receivedUtc = $receivedUtc
            elapsedMilliseconds = $stopwatch.ElapsedMilliseconds
            exitCode = $terminal.exitCode
        }
    }
    if ($RecordCase -and $null -ne $CasesWriter) {
        Write-CaseRecord -Writer $CasesWriter -Record $record
    }
    return $record
}

function Get-WorkspaceRoots {
    param([Parameter(Mandatory)] [object[]] $SnapshotRecords)

    $roots = [Collections.Generic.List[string]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($record in $SnapshotRecords) {
        if ($record.caseId -ne 'R-WS' -or $null -eq $record.workerPayload.snapshot) {
            continue
        }
        foreach ($workspace in @($record.workerPayload.snapshot.workspaces)) {
            if ([string]::IsNullOrWhiteSpace([string] $workspace.rootPath)) {
                throw 'filesystem_hashing_incomplete: a discovered workspace has no usable rootPath.'
            }
            try {
                $root = [IO.Path]::GetFullPath([string] $workspace.rootPath)
            }
            catch {
                throw 'filesystem_hashing_incomplete: a discovered workspace root could not be canonicalized.'
            }
            if ($seen.Add($root)) {
                $roots.Add($root)
            }
        }
    }
    return $roots.ToArray()
}

function Get-MappingSnapshots {
    param([Parameter(Mandatory)] [object[]] $SnapshotRecords)

    $mappings = [Collections.Generic.List[object]]::new()
    foreach ($record in $SnapshotRecords) {
        if ($record.caseId -ne 'R-MAP' -or $null -eq $record.workerPayload.snapshot) {
            continue
        }
        foreach ($mapping in @($record.workerPayload.snapshot.mappings)) {
            $mappings.Add($mapping)
        }
    }
    return $mappings.ToArray()
}

function Get-GroupSnapshots {
    param([Parameter(Mandatory)] [object[]] $SnapshotRecords)

    $groups = [Collections.Generic.List[object]]::new()
    foreach ($record in $SnapshotRecords) {
        if ($record.caseId -ne 'R-GRP' -or $null -eq $record.workerPayload.snapshot) {
            continue
        }
        foreach ($group in @($record.workerPayload.snapshot.groups)) {
            $groups.Add($group)
        }
    }
    return $groups.ToArray()
}

function Get-WorkspaceSnapshots {
    param([Parameter(Mandatory)] [object[]] $SnapshotRecords)

    $workspaces = [Collections.Generic.List[object]]::new()
    foreach ($record in $SnapshotRecords) {
        if ($record.caseId -ne 'R-WS' -or $null -eq $record.workerPayload.snapshot) {
            continue
        }
        foreach ($workspace in @($record.workerPayload.snapshot.workspaces)) {
            $workspaces.Add($workspace)
        }
    }
    return $workspaces.ToArray()
}

function Get-FilesystemSnapshot {
    param(
        [Parameter(Mandatory)] [string[]] $WorkspaceRoots,
        [Parameter(Mandatory)] [int] $MaxFiles,
        [Parameter(Mandatory)] [long] $MaxBytes
    )

    $files = [Collections.Generic.List[object]]::new()
    $omissions = [Collections.Generic.List[object]]::new()
    $complete = $true
    [long] $totalBytes = 0

    :filesystemRoots foreach ($root in $WorkspaceRoots) {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) {
            $complete = $false
            $omissions.Add([ordered]@{ root = $root; path = $null; reason = 'workspace_root_missing' })
            continue
        }
        $rootItem = Get-Item -LiteralPath $root -Force
        if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            $complete = $false
            $omissions.Add([ordered]@{ root = $root; path = $null; reason = 'workspace_root_is_reparse_point' })
            continue
        }

        $pending = [Collections.Generic.Queue[string]]::new()
        $pending.Enqueue($root)
        while ($pending.Count -gt 0) {
            $directory = $pending.Dequeue()
            try {
                $entries = @([IO.Directory]::EnumerateFileSystemEntries($directory) | Sort-Object -CaseSensitive)
            }
            catch {
                $complete = $false
                $omissions.Add([ordered]@{ root = $root; path = $directory; reason = 'directory_enumeration_failed'; message = $_.Exception.Message })
                continue
            }

            foreach ($entry in $entries) {
                try {
                    $attributes = [IO.File]::GetAttributes($entry)
                    if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                        $complete = $false
                        $omissions.Add([ordered]@{ root = $root; path = $entry; reason = 'reparse_point_not_followed' })
                        continue
                    }
                    if (($attributes -band [IO.FileAttributes]::Directory) -ne 0) {
                        $pending.Enqueue($entry)
                        continue
                    }

                    if ($files.Count -ge $MaxFiles) {
                        $complete = $false
                        $omissions.Add([ordered]@{ root = $root; path = $entry; reason = 'max_file_count_exceeded'; budget = $MaxFiles })
                        break filesystemRoots
                    }
                    $fileInfoBefore = [IO.FileInfo]::new($entry)
                    if ($totalBytes + $fileInfoBefore.Length -gt $MaxBytes) {
                        $complete = $false
                        $omissions.Add([ordered]@{ root = $root; path = $entry; reason = 'max_byte_count_exceeded'; budget = $MaxBytes })
                        break filesystemRoots
                    }
                    $hash = (Get-FileHash -LiteralPath $entry -Algorithm SHA256).Hash.ToLowerInvariant()
                    $fileInfoAfter = [IO.FileInfo]::new($entry)
                    if ($fileInfoBefore.Length -ne $fileInfoAfter.Length -or
                        $fileInfoBefore.LastWriteTimeUtc -ne $fileInfoAfter.LastWriteTimeUtc) {
                        $complete = $false
                        $omissions.Add([ordered]@{ root = $root; path = $entry; reason = 'file_changed_while_hashing' })
                        continue
                    }
                    $relativePath = [IO.Path]::GetRelativePath($root, $entry).Replace([IO.Path]::DirectorySeparatorChar, '/')
                    $files.Add([ordered]@{
                            root = $root
                            relativePath = $relativePath
                            length = $fileInfoAfter.Length
                            lastWriteUtc = $fileInfoAfter.LastWriteTimeUtc.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
                            sha256 = $hash
                        })
                    $totalBytes += $fileInfoAfter.Length
                }
                catch {
                    $complete = $false
                    $omissions.Add([ordered]@{ root = $root; path = $entry; reason = 'file_hash_failed'; message = $_.Exception.Message })
                }
            }
        }
    }

    return [ordered]@{
        schemaVersion = 'vci-phase1-filesystem-snapshot/v1'
        capturedUtc = Get-UtcTimestamp
        workspaceRoots = $WorkspaceRoots
        complete = $complete
        maxFiles = $MaxFiles
        maxBytes = $MaxBytes
        observedFiles = $files.Count
        observedBytes = $totalBytes
        files = $files.ToArray()
        omissions = $omissions.ToArray()
    }
}

function Remove-NormalizedEvidenceFields {
    param([AllowNull()] [object] $Value)

    if ($null -eq $Value) {
        return $null
    }
    if ($Value -is [Collections.IDictionary]) {
        $result = [ordered]@{}
        $removedNames = @(
            'runId', 'sessionId', 'sentUtc', 'receivedUtc', 'capturedUtc',
            'elapsedMilliseconds', 'workerProcessId', 'transportSequence'
        )
        foreach ($key in @($Value.Keys | ForEach-Object { [string] $_ } | Sort-Object -CaseSensitive)) {
            if ($key -in $removedNames) {
                continue
            }
            $result[$key] = Remove-NormalizedEvidenceFields -Value $Value[$key]
        }
        return $result
    }
    if ($Value -is [Collections.IEnumerable] -and $Value -isnot [string]) {
        $items = @()
        foreach ($item in $Value) {
            $items += ,(Remove-NormalizedEvidenceFields -Value $item)
        }
        return ,$items
    }
    return $Value
}

function Compare-FilesystemSnapshots {
    param(
        [Parameter(Mandatory)] [Collections.IDictionary] $Before,
        [Parameter(Mandatory)] [Collections.IDictionary] $After
    )

    if (-not $Before.complete -or -not $After.complete) {
        return [ordered]@{ complete = $false; unchanged = $false; failure = 'filesystem_hashing_incomplete' }
    }
    $beforeComparable = [ordered]@{ workspaceRoots = $Before.workspaceRoots; files = $Before.files }
    $afterComparable = [ordered]@{ workspaceRoots = $After.workspaceRoots; files = $After.files }
    $unchanged = (ConvertTo-CanonicalJson -Value $beforeComparable) -ceq (ConvertTo-CanonicalJson -Value $afterComparable)
    return [ordered]@{
        complete = $true
        unchanged = $unchanged
        failure = if ($unchanged) { $null } else { 'filesystem_changed' }
    }
}

function Assert-TerminalCoverage {
    param(
        [Parameter(Mandatory)] [object[]] $Records,
        [Parameter(Mandatory)] [string] $SessionId,
        [Parameter(Mandatory)] [string[]] $ExpectedCaseInstanceIds
    )

    $sessionRecords = @($Records | Where-Object { $_.sessionId -eq $SessionId })
    if ($sessionRecords.Count -eq 0 -or @($sessionRecords | Where-Object { [string]::IsNullOrWhiteSpace([string] $_.caseInstanceId) }).Count -gt 0) {
        throw 'missing_case_instance_id: a session terminal record has no identifier.'
    }
    $actualIdCounts = @{}
    foreach ($record in $sessionRecords) {
        $caseInstanceId = [string] $record.caseInstanceId
        if ($actualIdCounts.ContainsKey($caseInstanceId)) {
            throw 'duplicate_case_instance_id: a session contains duplicate terminal identifiers.'
        }
        $actualIdCounts[$caseInstanceId] = 1
    }
    if ($ExpectedCaseInstanceIds.Count -eq 0 -or
        @($ExpectedCaseInstanceIds | Where-Object { [string]::IsNullOrWhiteSpace([string] $_) }).Count -gt 0) {
        throw 'missing_case_instance_id: the expected terminal identifier set is empty or malformed.'
    }
    $duplicateExpected = @($ExpectedCaseInstanceIds | Group-Object | Where-Object { $_.Count -ne 1 })
    if ($duplicateExpected.Count -gt 0) {
        throw 'duplicate_case_instance_id: the expected terminal identifier set contains duplicates.'
    }

    $actualIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($record in $sessionRecords) {
        [void] $actualIds.Add([string] $record.caseInstanceId)
    }
    $expectedIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($caseInstanceId in $ExpectedCaseInstanceIds) {
        [void] $expectedIds.Add($caseInstanceId)
        if (-not $actualIds.Contains($caseInstanceId)) {
            throw "missing_case_instance_id: session '$SessionId' has no terminal record for expected identifier '$caseInstanceId'."
        }
    }
    foreach ($caseInstanceId in $actualIds) {
        if (-not $expectedIds.Contains($caseInstanceId)) {
            throw "unexpected_case_instance_id: session '$SessionId' contains unexpected terminal identifier '$caseInstanceId'."
        }
    }
    foreach ($caseId in $caseIds) {
        if (@($sessionRecords | Where-Object { $_.caseId -eq $caseId }).Count -eq 0) {
            throw "missing_case_instance_id: session '$SessionId' has no terminal record for case '$caseId'."
        }
    }
    if (@($sessionRecords | Where-Object { $_.schemaVersion -ne 'vci-phase1-read-case-evidence/v1' }).Count -gt 0) {
        throw 'schema_mismatch: cases.jsonl contains an unexpected evidence schema.'
    }
    if (@($sessionRecords | Where-Object { -not $_.terminal }).Count -gt 0) {
        throw 'missing_case_instance_id: a request has no terminal evidence record.'
    }
}

function Assert-SnapshotAfterCoverage {
    param(
        [Parameter(Mandatory)] [object[]] $Records,
        [Parameter(Mandatory)] [string] $SessionId,
        [Parameter(Mandatory)] [string[]] $ExpectedCaseInstanceIds
    )

    $requiredCaseIds = @('R-SVC', 'R-GRP', 'R-WS', 'R-MAP')
    if ($ExpectedCaseInstanceIds.Count -ne $requiredCaseIds.Count -or
        @($ExpectedCaseInstanceIds | Where-Object { [string]::IsNullOrWhiteSpace([string] $_) }).Count -gt 0) {
        throw 'missing_snapshot_after_case_instance_id: the expected after-canary identifier set is incomplete.'
    }
    if (@($ExpectedCaseInstanceIds | Group-Object | Where-Object { $_.Count -ne 1 }).Count -gt 0) {
        throw 'duplicate_snapshot_after_case_instance_id: the expected after-canary identifier set contains duplicates.'
    }

    $sessionRecords = @($Records | Where-Object { $_.sessionId -eq $SessionId })
    if (@($sessionRecords | Where-Object { [string]::IsNullOrWhiteSpace([string] $_.caseInstanceId) }).Count -gt 0) {
        throw 'missing_snapshot_after_case_instance_id: an after-canary record has no identifier.'
    }
    $actualIdCounts = @{}
    foreach ($record in $sessionRecords) {
        $caseInstanceId = [string] $record.caseInstanceId
        if ($actualIdCounts.ContainsKey($caseInstanceId)) {
            throw 'duplicate_snapshot_after_case_instance_id: a session contains duplicate after-canary identifiers.'
        }
        $actualIdCounts[$caseInstanceId] = 1
    }

    for ($index = 0; $index -lt $requiredCaseIds.Count; $index++) {
        $caseId = $requiredCaseIds[$index]
        $caseRecords = @($sessionRecords | Where-Object { $_.caseId -eq $caseId })
        if ($caseRecords.Count -eq 0 -or -not $actualIdCounts.ContainsKey($ExpectedCaseInstanceIds[$index])) {
            throw "missing_snapshot_after_case_instance_id: session '$SessionId' lacks '$caseId' with its expected identifier."
        }
        if ($caseRecords.Count -ne 1 -or $caseRecords[0].caseInstanceId -cne $ExpectedCaseInstanceIds[$index]) {
            throw "unexpected_snapshot_after_case_instance_id: session '$SessionId' has unexpected evidence for '$caseId'."
        }
    }
    for ($index = 0; $index -lt $requiredCaseIds.Count; $index++) {
        if ($sessionRecords[$index].caseId -cne $requiredCaseIds[$index] -or
            $sessionRecords[$index].caseInstanceId -cne $ExpectedCaseInstanceIds[$index]) {
            throw "unexpected_snapshot_after_case_instance_id: session '$SessionId' after-canary evidence is out of order."
        }
    }
    foreach ($record in $sessionRecords) {
        if ($record.caseId -notin $requiredCaseIds -or
            $record.caseInstanceId -notin $ExpectedCaseInstanceIds) {
            throw "unexpected_snapshot_after_case_instance_id: session '$SessionId' contains unexpected after-canary evidence."
        }
        if ($record.schemaVersion -ne 'vci-phase1-read-case-evidence/v1' -or -not $record.terminal) {
            throw 'snapshot_after_schema_mismatch: after-canary evidence is not a terminal case record.'
        }
    }
}

function Compare-SessionRecords {
    param([Parameter(Mandatory)] [object[]] $Records)

    $first = @($Records | Where-Object { $_.sessionId -eq 'session-1' })
    $second = @($Records | Where-Object { $_.sessionId -eq 'session-2' })
    $firstById = @{}
    $secondById = @{}
    foreach ($record in $first) { $firstById[$record.caseInstanceId] = $record }
    foreach ($record in $second) { $secondById[$record.caseInstanceId] = $record }

    $mismatches = [Collections.Generic.List[object]]::new()
    $allIds = @($firstById.Keys + $secondById.Keys | Sort-Object -Unique -CaseSensitive)
    foreach ($caseInstanceId in $allIds) {
        if (-not $firstById.ContainsKey($caseInstanceId) -or -not $secondById.ContainsKey($caseInstanceId)) {
            $mismatches.Add([ordered]@{ caseInstanceId = $caseInstanceId; reason = 'missing_case_instance_id' })
            continue
        }
        $left = ConvertTo-CanonicalJson -Value (Remove-NormalizedEvidenceFields -Value $firstById[$caseInstanceId])
        $right = ConvertTo-CanonicalJson -Value (Remove-NormalizedEvidenceFields -Value $secondById[$caseInstanceId])
        if ($left -cne $right) {
            $mismatches.Add([ordered]@{ caseInstanceId = $caseInstanceId; reason = 'normalized_session_mismatch' })
        }
    }
    return $mismatches.ToArray()
}

function Test-ProjectStateInvariant {
    param([Parameter(Mandatory)] [object[]] $Records)

    $failures = [Collections.Generic.List[object]]::new()
    foreach ($sessionId in $sessionIds) {
        $sessionRecords = @($Records | Where-Object { $_.sessionId -eq $sessionId -and $null -ne $_.workerPayload })
        if ($sessionRecords.Count -eq 0) {
            $failures.Add([ordered]@{ sessionId = $sessionId; reason = 'missing_project_state_evidence' })
            continue
        }
        $baseline = $sessionRecords[0].workerPayload.projectState.isModifiedBefore
        foreach ($record in $sessionRecords) {
            $state = $record.workerPayload.projectState
            if ($state.isModifiedBefore -ne $baseline -or $state.isModifiedAfter -ne $baseline) {
                $failures.Add([ordered]@{ sessionId = $sessionId; caseInstanceId = $record.caseInstanceId; reason = 'project_state_changed' })
            }
        }
    }
    return [ordered]@{ unchanged = $failures.Count -eq 0; failures = $failures.ToArray() }
}

function Read-CaseRecords {
    param([Parameter(Mandatory)] [string] $Path)

    $records = [Collections.Generic.List[object]]::new()
    foreach ($line in [IO.File]::ReadLines($Path, [Text.UTF8Encoding]::new($false))) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }
        $record = ConvertFrom-JsonHashtable -Json $line -FailureMessage 'malformed_worker_payload: cases.jsonl contains malformed evidence.'
        $records.Add($record)
    }
    return $records.ToArray()
}

function Get-GitProvenance {
    param([Parameter(Mandatory)] [string] $RepositoryRoot)

    if ($null -eq (Get-Command git -ErrorAction SilentlyContinue)) {
        throw 'Git is required to capture evidence provenance.'
    }
    $commit = (& git -C $RepositoryRoot rev-parse HEAD 2>$null | Select-Object -First 1)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace([string] $commit)) {
        throw 'Git commit provenance could not be captured.'
    }
    $statusLines = @(& git -C $RepositoryRoot status --porcelain --untracked-files=normal 2>$null)
    if ($LASTEXITCODE -ne 0) {
        throw 'Git dirty-state provenance could not be captured.'
    }
    return [ordered]@{
        commit = ([string] $commit).Trim()
        isDirty = $statusLines.Count -gt 0
        trackedChangeCount = $statusLines.Count
    }
}

function Get-CountSummary {
    param([Parameter(Mandatory)] [object[]] $Records)

    $counts = [Collections.Generic.List[object]]::new()
    foreach ($group in @($Records | Group-Object sessionId, caseId, outcome | Sort-Object Name -CaseSensitive)) {
        $sample = $group.Group[0]
        $counts.Add([ordered]@{
                sessionId = $sample.sessionId
                caseId = $sample.caseId
                outcome = $sample.outcome
                count = $group.Count
            })
    }
    return $counts.ToArray()
}

function Invoke-ProbeSession {
    param(
        [Parameter(Mandatory)] [string] $RunId,
        [Parameter(Mandatory)] [string] $SessionId,
        [Parameter(Mandatory)] [string] $ProjectPath,
        [Parameter(Mandatory)] [string] $WorkerExecutable,
        [Parameter(Mandatory)] [string[]] $WorkerArguments,
        [AllowNull()] [string] $SecondaryProjectPath,
        [Parameter(Mandatory)] [int] $TimeoutSeconds,
        [Parameter(Mandatory)] [IO.StreamWriter] $CasesWriter,
        [Parameter(Mandatory)] [bool] $CaptureFilesystemBefore
    )

    $worker = $null
    $transportSequence = 0
    $baselineRecords = [Collections.Generic.List[object]]::new()
    $matrixRecords = [Collections.Generic.List[object]]::new()
    $afterCanaryRecords = [Collections.Generic.List[object]]::new()
    $expectedCaseInstanceIds = [Collections.Generic.List[string]]::new()
    $expectedAfterCanaryCaseInstanceIds = [Collections.Generic.List[string]]::new()
    $snapshotFailure = $null
    try {
        $worker = Start-JsonLineProcess -Executable $WorkerExecutable -Arguments $WorkerArguments

        foreach ($definition in (New-SnapshotDefinitions -Phase 'baseline')) {
            $request = New-ProbeWorkerRequest `
                -RunId $RunId `
                -SessionId $SessionId `
                -ProjectPath $ProjectPath `
                -Definition $definition
            $expectedCaseInstanceIds.Add([string] $request.vciProbe.caseInstanceId)
            $record = Invoke-ProbeRequest `
                -Worker $worker `
                -Request $request `
                -TimeoutSeconds $TimeoutSeconds `
                -TransportSequence ([ref] $transportSequence) `
                -CasesWriter $CasesWriter `
                -RecordCase $true
            $baselineRecords.Add($record)
        }

        $workspaceRoots = @(Get-WorkspaceRoots -SnapshotRecords $baselineRecords.ToArray())
        $mappings = @(Get-MappingSnapshots -SnapshotRecords $baselineRecords.ToArray())
        $groups = @(Get-GroupSnapshots -SnapshotRecords $baselineRecords.ToArray())
        $workspaces = @(Get-WorkspaceSnapshots -SnapshotRecords $baselineRecords.ToArray())
        $filesystemBefore = $null
        if ($CaptureFilesystemBefore) {
            $filesystemBefore = Get-FilesystemSnapshot `
                -WorkspaceRoots $workspaceRoots `
                -MaxFiles $filesystemBudgets.maxFiles `
                -MaxBytes $filesystemBudgets.maxBytes
            if (-not $filesystemBefore.complete) {
                throw 'filesystem_hashing_incomplete: pre-run workspace hashing did not complete.'
            }
        }

        $matrix = @(New-CaseMatrix `
                -Mappings $mappings `
                -GroupSnapshots $groups `
                -WorkspaceSnapshots $workspaces `
                -SecondaryProjectPath $SecondaryProjectPath)
        foreach ($definition in $matrix) {
            $request = New-ProbeWorkerRequest `
                -RunId $RunId `
                -SessionId $SessionId `
                -ProjectPath $ProjectPath `
                -Definition $definition
            $expectedCaseInstanceIds.Add([string] $request.vciProbe.caseInstanceId)
            $record = Invoke-ProbeRequest `
                -Worker $worker `
                -Request $request `
                -TimeoutSeconds $TimeoutSeconds `
                -TransportSequence ([ref] $transportSequence) `
                -CasesWriter $CasesWriter `
                -RecordCase $true
            $matrixRecords.Add($record)
        }

        if ($matrixRecords.Count -eq 0 -or $matrixRecords[$matrixRecords.Count - 1].caseId -ne 'R-CANARY') {
            throw 'missing_case_instance_id: R-CANARY was not the final case-matrix terminal record.'
        }

        $afterCanaryRequests = @(
            foreach ($definition in (New-SnapshotDefinitions -Phase 'after-canary')) {
                $request = New-ProbeWorkerRequest `
                    -RunId $RunId `
                    -SessionId $SessionId `
                    -ProjectPath $ProjectPath `
                    -Definition $definition
                $expectedAfterCanaryCaseInstanceIds.Add([string] $request.vciProbe.caseInstanceId)
                $request
            }
        )
        foreach ($request in $afterCanaryRequests) {
            $record = Invoke-ProbeRequest `
                -Worker $worker `
                -Request $request `
                -TimeoutSeconds $TimeoutSeconds `
                -TransportSequence ([ref] $transportSequence) `
                -CasesWriter $null `
                -RecordCase $false
            $afterCanaryRecords.Add($record)
            if (-not [string]::IsNullOrWhiteSpace([string] $record.evidenceFailure)) {
                $snapshotFailure = [string] $record.evidenceFailure
                break
            }
        }

        return [ordered]@{
            sessionId = $SessionId
            workerProcessId = $worker.Id
            snapshotBefore = [ordered]@{
                sessionId = $SessionId
                observations = @($baselineRecords.ToArray() | ForEach-Object { $_.workerPayload })
            }
            snapshotAfter = [ordered]@{
                sessionId = $SessionId
                observations = @($afterCanaryRecords.ToArray() | ForEach-Object { $_.workerPayload })
                terminalRecords = $afterCanaryRecords.ToArray()
            }
            afterCanaryRecords = $afterCanaryRecords.ToArray()
            expectedCaseInstanceIds = $expectedCaseInstanceIds.ToArray()
            expectedAfterCanaryCaseInstanceIds = $expectedAfterCanaryCaseInstanceIds.ToArray()
            workspaceRoots = $workspaceRoots
            filesystemBefore = $filesystemBefore
            failure = $snapshotFailure
        }
    }
    finally {
        if ($null -ne $worker) {
            Stop-JsonLineProcess -Process $worker
        }
    }
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
$gitProvenance = Get-GitProvenance -RepositoryRoot $repositoryRoot
$dotnetVersion = (& dotnet --version 2>$null | Select-Object -First 1)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace([string] $dotnetVersion)) {
    throw '.NET version provenance could not be captured.'
}

$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ', [Globalization.CultureInfo]::InvariantCulture) +
    '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$runDirectory = Join-Path $canonicalEvidenceRoot $runId
if (Test-Path -LiteralPath $runDirectory) {
    throw 'The generated evidence run directory already exists.'
}
[void] [IO.Directory]::CreateDirectory($runDirectory)

$manifest = [ordered]@{
    schemaVersion = 'vci-phase1-read-manifest/v1'
    runId = $runId
    createdUtc = Get-UtcTimestamp
    readOnly = $true
    mutatesProject = $false
    workerOperation = 'probe_vci_read_contract'
    workerAccessMode = 'read-only'
    workerSessions = $sessionIds
    script = [ordered]@{
        path = [IO.Path]::GetFullPath($PSCommandPath)
        sha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    worker = [ordered]@{
        path = $canonicalWorkerExecutable
        sha256 = (Get-FileHash -LiteralPath $canonicalWorkerExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    git = $gitProvenance
    environment = [ordered]@{
        operatingSystem = [Environment]::OSVersion.VersionString
        powerShellVersion = $PSVersionTable.PSVersion.ToString()
        dotnetVersion = ([string] $dotnetVersion).Trim()
    }
    paths = [ordered]@{
        repositoryRoot = $repositoryRoot
        projectPath = $canonicalProjectPath
        secondaryProjectPath = $canonicalSecondaryProjectPath
        evidenceRoot = $canonicalEvidenceRoot
        runDirectory = $runDirectory
    }
    authorizationInputs = [ordered]@{
        mode = $Mode
        separateLiveAuthorizationRequired = $true
        secondaryProjectPathSupplied = -not [string]::IsNullOrWhiteSpace($SecondaryProjectPath)
        secondaryProjectReadAuthorized = [bool] $AllowSecondaryProjectRead
    }
    timeoutSeconds = $TimeoutSeconds
    probeBudgets = $probeBudgets
    filesystemBudgets = $filesystemBudgets
    caseIds = $caseIds
    evidenceFiles = $evidenceFiles
}

$manifestPath = Join-Path $runDirectory 'manifest.json'
$casesPath = Join-Path $runDirectory 'cases.jsonl'
$snapshotBeforePath = Join-Path $runDirectory 'snapshot-before.json'
$snapshotAfterPath = Join-Path $runDirectory 'snapshot-after.json'
$filesystemBeforePath = Join-Path $runDirectory 'filesystem-before.json'
$filesystemAfterPath = Join-Path $runDirectory 'filesystem-after.json'
$summaryPath = Join-Path $runDirectory 'summary.json'
Write-AtomicJsonDocument -Path $manifestPath -Value $manifest

$failureReasons = [Collections.Generic.List[string]]::new()
$sessionResults = [Collections.Generic.List[object]]::new()
$snapshotBeforeEntries = [Collections.Generic.List[object]]::new()
$snapshotAfterEntries = [Collections.Generic.List[object]]::new()
$afterCanaryRecords = [Collections.Generic.List[object]]::new()
$expectedCaseInstanceIdsBySession = @{}
$expectedAfterCanaryCaseInstanceIdsBySession = @{}
$workspaceRoots = @()
$filesystemBefore = $null
$filesystemAfter = $null
$casesWriter = $null
try {
    $casesWriter = Open-CasesWriter -Path $casesPath
    foreach ($sessionId in $sessionIds) {
        $sessionResult = Invoke-ProbeSession `
            -RunId $runId `
            -SessionId $sessionId `
            -ProjectPath $canonicalProjectPath `
            -WorkerExecutable $canonicalWorkerExecutable `
            -WorkerArguments $workerArguments `
            -SecondaryProjectPath $canonicalSecondaryProjectPath `
            -TimeoutSeconds $TimeoutSeconds `
            -CasesWriter $casesWriter `
            -CaptureFilesystemBefore ($sessionId -eq 'session-1')
        $sessionResults.Add($sessionResult)
        $expectedCaseInstanceIdsBySession[$sessionId] = @($sessionResult.expectedCaseInstanceIds)
        $expectedAfterCanaryCaseInstanceIdsBySession[$sessionId] = @($sessionResult.expectedAfterCanaryCaseInstanceIds)
        $snapshotBeforeEntries.Add($sessionResult.snapshotBefore)
        $snapshotAfterEntries.Add($sessionResult.snapshotAfter)
        foreach ($record in @($sessionResult.afterCanaryRecords)) {
            $afterCanaryRecords.Add($record)
        }
        if ($sessionId -eq 'session-1') {
            $workspaceRoots = @($sessionResult.workspaceRoots)
            $filesystemBefore = $sessionResult.filesystemBefore
        }
        if (-not [string]::IsNullOrWhiteSpace([string] $sessionResult.failure)) {
            throw $sessionResult.failure
        }
    }
}
catch {
    $failureReasons.Add($_.Exception.Message)
}
finally {
    if ($null -ne $casesWriter) {
        $casesWriter.Dispose()
    }
}

if ($null -eq $filesystemBefore) {
    $filesystemBefore = [ordered]@{
        schemaVersion = 'vci-phase1-filesystem-snapshot/v1'
        capturedUtc = Get-UtcTimestamp
        workspaceRoots = $workspaceRoots
        complete = $false
        maxFiles = $filesystemBudgets.maxFiles
        maxBytes = $filesystemBudgets.maxBytes
        observedFiles = 0
        observedBytes = 0
        files = @()
        omissions = @([ordered]@{ root = $null; path = $null; reason = 'filesystem_hashing_incomplete' })
    }
}

try {
    $filesystemAfter = Get-FilesystemSnapshot `
        -WorkspaceRoots $workspaceRoots `
        -MaxFiles $filesystemBudgets.maxFiles `
        -MaxBytes $filesystemBudgets.maxBytes
}
catch {
    $failureReasons.Add("filesystem_hashing_incomplete: $($_.Exception.Message)")
    $filesystemAfter = [ordered]@{
        schemaVersion = 'vci-phase1-filesystem-snapshot/v1'
        capturedUtc = Get-UtcTimestamp
        workspaceRoots = $workspaceRoots
        complete = $false
        maxFiles = $filesystemBudgets.maxFiles
        maxBytes = $filesystemBudgets.maxBytes
        observedFiles = 0
        observedBytes = 0
        files = @()
        omissions = @([ordered]@{ root = $null; path = $null; reason = 'filesystem_hashing_incomplete' })
    }
}

$snapshotBeforeDocument = [ordered]@{
    schemaVersion = 'vci-phase1-read-snapshot/v1'
    runId = $runId
    phase = 'before'
    sessions = $snapshotBeforeEntries.ToArray()
}
$snapshotAfterDocument = [ordered]@{
    schemaVersion = 'vci-phase1-read-snapshot/v1'
    runId = $runId
    phase = 'after-canary'
    sessions = $snapshotAfterEntries.ToArray()
}
Write-AtomicJsonDocument -Path $snapshotBeforePath -Value $snapshotBeforeDocument
Write-AtomicJsonDocument -Path $snapshotAfterPath -Value $snapshotAfterDocument
Write-AtomicJsonDocument -Path $filesystemBeforePath -Value $filesystemBefore
Write-AtomicJsonDocument -Path $filesystemAfterPath -Value $filesystemAfter

$caseRecords = @()
try {
    $caseRecords = @(Read-CaseRecords -Path $casesPath)
}
catch {
    $failureReasons.Add($_.Exception.Message)
}

$terminalCoverageComplete = $true
foreach ($sessionId in $sessionIds) {
    try {
        [string[]] $expectedCaseInstanceIds = if ($expectedCaseInstanceIdsBySession.ContainsKey($sessionId)) {
            @($expectedCaseInstanceIdsBySession[$sessionId])
        }
        else {
            @()
        }
        Assert-TerminalCoverage `
            -Records $caseRecords `
            -SessionId $sessionId `
            -ExpectedCaseInstanceIds $expectedCaseInstanceIds
        $sessionCases = @($caseRecords | Where-Object { $_.sessionId -eq $sessionId })
        if ($sessionCases.Count -eq 0 -or $sessionCases[$sessionCases.Count - 1].caseId -ne 'R-CANARY') {
            throw 'missing_case_instance_id: R-CANARY is not the final cases.jsonl record for the session.'
        }
    }
    catch {
        $terminalCoverageComplete = $false
        $failureReasons.Add($_.Exception.Message)
    }
}

foreach ($record in $caseRecords) {
    if (-not [string]::IsNullOrWhiteSpace([string] $record.evidenceFailure)) {
        $failureReasons.Add([string] $record.evidenceFailure)
    }
}

$snapshotAfterCoverageComplete = $true
foreach ($sessionId in $sessionIds) {
    try {
        [string[]] $expectedAfterCanaryCaseInstanceIds = if ($expectedAfterCanaryCaseInstanceIdsBySession.ContainsKey($sessionId)) {
            @($expectedAfterCanaryCaseInstanceIdsBySession[$sessionId])
        }
        else {
            @()
        }
        Assert-SnapshotAfterCoverage `
            -Records $afterCanaryRecords.ToArray() `
            -SessionId $sessionId `
            -ExpectedCaseInstanceIds $expectedAfterCanaryCaseInstanceIds
    }
    catch {
        $snapshotAfterCoverageComplete = $false
        $failureReasons.Add($_.Exception.Message)
    }
}

$normalizedMismatches = @(Compare-SessionRecords -Records $caseRecords)
$snapshotMismatches = @(Compare-SessionRecords -Records $afterCanaryRecords.ToArray())
foreach ($mismatch in $snapshotMismatches) {
    $normalizedMismatches += ,$mismatch
}
if ($normalizedMismatches.Count -gt 0) {
    $failureReasons.Add('normalized_session_mismatch')
}

$projectStateRecords = @($caseRecords) + @($afterCanaryRecords.ToArray())
$projectStateInvariant = Test-ProjectStateInvariant -Records $projectStateRecords
if (-not $projectStateInvariant.unchanged) {
    $failureReasons.Add('project_state_changed')
}
$filesystemInvariant = Compare-FilesystemSnapshots -Before $filesystemBefore -After $filesystemAfter
if (-not $filesystemInvariant.complete) {
    $failureReasons.Add('filesystem_hashing_incomplete')
}
elseif (-not $filesystemInvariant.unchanged) {
    $failureReasons.Add('filesystem_changed')
}

$canaryStatus = [Collections.Generic.List[object]]::new()
foreach ($sessionId in $sessionIds) {
    $canary = @($caseRecords | Where-Object { $_.sessionId -eq $sessionId -and $_.caseId -eq 'R-CANARY' })
    $usable = $canary.Count -eq 1 -and $canary[0].outcome -in @('returned', 'returned_null')
    $canaryStatus.Add([ordered]@{ sessionId = $sessionId; usable = $usable; outcome = if ($canary.Count -eq 1) { $canary[0].outcome } else { $null } })
    if (-not $usable) {
        $failureReasons.Add("$sessionId canary_not_usable")
    }
}

$evidenceComplete = $sessionResults.Count -eq 2 -and
    $snapshotBeforeEntries.Count -eq 2 -and
    $snapshotAfterEntries.Count -eq 2 -and
    $terminalCoverageComplete -and
    $snapshotAfterCoverageComplete -and
    $filesystemBefore.complete -and
    $filesystemAfter.complete
if (-not $evidenceComplete) {
    $failureReasons.Add('evidence_incomplete')
}

$preSummaryExpected = @($evidenceFiles | Where-Object { $_ -ne 'summary.json' } | Sort-Object -CaseSensitive)
$preSummaryActual = @(Get-ChildItem -LiteralPath $runDirectory -File | Select-Object -ExpandProperty Name | Sort-Object -CaseSensitive)
if ((ConvertTo-CanonicalJson -Value $preSummaryActual) -cne (ConvertTo-CanonicalJson -Value $preSummaryExpected)) {
    $failureReasons.Add('evidence_file_set_mismatch')
}

$uniqueFailureReasons = @($failureReasons | Sort-Object -Unique -CaseSensitive)
$summary = [ordered]@{
    schemaVersion = 'vci-phase1-read-summary/v1'
    runId = $runId
    completedUtc = Get-UtcTimestamp
    counts = Get-CountSummary -Records $caseRecords
    normalizedMismatches = $normalizedMismatches
    canaryStatus = $canaryStatus.ToArray()
    projectStateInvariant = $projectStateInvariant
    filesystemInvariant = $filesystemInvariant
    evidenceComplete = $evidenceComplete
    failures = $uniqueFailureReasons
    overallPass = $uniqueFailureReasons.Count -eq 0
}
Write-AtomicJsonDocument -Path $summaryPath -Value $summary

$actualEvidenceFiles = @(Get-ChildItem -LiteralPath $runDirectory -File | Select-Object -ExpandProperty Name | Sort-Object -CaseSensitive)
$expectedEvidenceFiles = @($evidenceFiles | Sort-Object -CaseSensitive)
if ((ConvertTo-CanonicalJson -Value $actualEvidenceFiles) -cne (ConvertTo-CanonicalJson -Value $expectedEvidenceFiles)) {
    $summary.overallPass = $false
    $summary.evidenceComplete = $false
    $summary.failures = @($summary.failures + 'evidence_file_set_mismatch' | Sort-Object -Unique -CaseSensitive)
    Write-AtomicJsonDocument -Path $summaryPath -Value $summary
}

[ordered]@{
    schemaVersion = 'vci-phase1-read-run-result/v1'
    runId = $runId
    runDirectory = $runDirectory
    overallPass = $summary.overallPass
} | ConvertTo-Json -Compress -Depth 10

if (-not $summary.overallPass) {
    throw "VCI Phase 1 read evidence run failed. Evidence retained at '$runDirectory'."
}
