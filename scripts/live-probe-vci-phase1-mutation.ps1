#Requires -Version 7

[CmdletBinding()]
param(
    [ValidateSet('Describe', 'Inventory', 'Apply')]
    [string] $Mode = 'Describe',

    [string] $ScenarioManifestPath,
    [string] $WorkerExecutable,
    [string] $EvidenceRoot,
    [string] $WorkspaceRoot,

    [ValidateRange(5, 1800)]
    [int] $TimeoutSeconds = 240,

    [ValidateSet('read-write', 'read-only')]
    [string] $WorkerAccessMode = 'read-write',

    [switch] $AllowMutation,
    [string] $Acknowledgement,
    [Alias('PlanHash')]
    [string] $ExpectedPlanHash,
    [switch] $NonInteractiveAcceptance,
    [string] $EquivalentEvidenceRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:HarnessSchema = 'vci-phase1-mutation-harness/v1'
$script:ManifestSchema = 'vci-phase1-mutation-scenarios/v1'
$script:InventorySchema = 'vci-phase1-mutation-inventory/v1'
$script:PlanSchema = 'vci-phase1-mutation-plan/v1'
$script:PlanEvidenceSchema = 'vci-phase1-mutation-plan-evidence/v1'
$script:ProbeSchema = 'vci-mutation-probe/v1'
$script:AcknowledgementText = 'I_UNDERSTAND_VCI_MUTATES_DISPOSABLE_PROJECTS_AND_WORKSPACE_FILES'
$script:CaseIds = @(
    'P-INVENTORY', 'M-CANARY', 'M-GROUP', 'M-WORKSPACE-ROOT',
    'M-WORKSPACE-LANGUAGE', 'M-EXPORT', 'M-DISCONNECT', 'M-CONNECT',
    'M-P2W', 'M-W2P', 'M-DELETE-MAPPING', 'M-DELETE-WORKSPACE',
    'M-DELETE-GROUP', 'M-TX-GROUP', 'M-TX-WORKSPACE', 'M-TX-EXPORT',
    'M-TX-CONNECT', 'M-TX-P2W', 'M-TX-W2P', 'M-TX-DISCONNECT',
    'M-TX-DELETE-WORKSPACE', 'M-TX-DELETE-GROUP', 'N-GROUP-NULL',
    'N-GROUP-EMPTY', 'N-GROUP-WHITESPACE', 'N-GROUP-DUPLICATE',
    'N-GROUP-INVALID', 'N-WORKSPACE-NULL', 'N-WORKSPACE-EMPTY',
    'N-WORKSPACE-WHITESPACE', 'N-WORKSPACE-DUPLICATE', 'N-WORKSPACE-INVALID',
    'N-WORKSPACE-PATH-RELATIVE', 'N-WORKSPACE-PATH-MISSING-PARENT',
    'N-WORKSPACE-PATH-CONFLICT', 'N-WORKSPACE-PATH-FILE',
    'N-WORKSPACE-LANGUAGE-NULL', 'N-WORKSPACE-LANGUAGE-INVALID',
    'N-WORKSPACE-GLOBAL-LIBRARY-NULL', 'N-WORKSPACE-GLOBAL-LIBRARY-INVALID',
    'N-OBJECT-NULL', 'N-OBJECT-UNSUPPORTED', 'N-OBJECT-FOREIGN',
    'N-OBJECT-DISPOSED', 'N-OBJECT-ALREADY-MAPPED', 'N-OBJECT-DELETED',
    'N-FORMAT-NULL', 'N-FORMAT-EMPTY', 'N-FORMAT-UNSUPPORTED',
    'N-FORMAT-WRONG-CASE', 'N-FORMAT-MISMATCH', 'N-FILENAME-INVALID',
    'N-FILENAME-ABSOLUTE', 'N-FILENAME-TRAVERSAL', 'N-FILENAME-COLLISION',
    'N-CONNECT-MISSING', 'N-CONNECT-MALFORMED', 'N-CONNECT-WRONG-OBJECT',
    'N-CONNECT-PARTIAL-FILE-SET', 'N-SYNC-MISSING', 'N-SYNC-MALFORMED',
    'N-SYNC-UNCHANGED', 'N-SYNC-PROJECT-ONLY', 'N-SYNC-WORKSPACE-ONLY',
    'N-SYNC-BOTH-SIDES', 'N-SYNC-INVALID-ENUM', 'N-DELETE-NONEMPTY',
    'N-DELETE-TWICE', 'N-STALE-MAPPING-PROXY'
)
$script:ScenarioOrder = @(
    'lifecycle',
    'mapping',
    'project_to_workspace',
    'workspace_to_project',
    'negative',
    'transaction'
)
$script:ScenarioCases = [ordered]@{
    lifecycle = @(
        'M-CANARY', 'M-GROUP', 'M-WORKSPACE-ROOT', 'M-WORKSPACE-LANGUAGE',
        'M-DELETE-WORKSPACE', 'M-DELETE-GROUP', 'N-GROUP-NULL', 'N-GROUP-EMPTY',
        'N-GROUP-WHITESPACE', 'N-GROUP-DUPLICATE', 'N-GROUP-INVALID',
        'N-WORKSPACE-NULL', 'N-WORKSPACE-EMPTY', 'N-WORKSPACE-WHITESPACE',
        'N-WORKSPACE-DUPLICATE', 'N-WORKSPACE-INVALID',
        'N-WORKSPACE-PATH-RELATIVE', 'N-WORKSPACE-PATH-MISSING-PARENT',
        'N-WORKSPACE-PATH-CONFLICT', 'N-WORKSPACE-PATH-FILE',
        'N-WORKSPACE-LANGUAGE-NULL', 'N-WORKSPACE-LANGUAGE-INVALID',
        'N-WORKSPACE-GLOBAL-LIBRARY-NULL', 'N-WORKSPACE-GLOBAL-LIBRARY-INVALID',
        'N-DELETE-NONEMPTY', 'N-DELETE-TWICE'
    )
    mapping = @(
        'M-EXPORT', 'M-DISCONNECT', 'M-CONNECT', 'M-DELETE-MAPPING',
        'N-OBJECT-NULL', 'N-OBJECT-UNSUPPORTED', 'N-OBJECT-FOREIGN',
        'N-OBJECT-DISPOSED', 'N-OBJECT-ALREADY-MAPPED', 'N-OBJECT-DELETED',
        'N-FORMAT-NULL', 'N-FORMAT-EMPTY', 'N-FORMAT-UNSUPPORTED',
        'N-FORMAT-WRONG-CASE', 'N-FORMAT-MISMATCH', 'N-FILENAME-INVALID',
        'N-FILENAME-ABSOLUTE', 'N-FILENAME-TRAVERSAL', 'N-FILENAME-COLLISION',
        'N-CONNECT-MISSING', 'N-CONNECT-MALFORMED', 'N-CONNECT-WRONG-OBJECT',
        'N-CONNECT-PARTIAL-FILE-SET', 'N-STALE-MAPPING-PROXY'
    )
    project_to_workspace = @('M-P2W')
    workspace_to_project = @('M-W2P')
    negative = @(
        'N-SYNC-MISSING', 'N-SYNC-MALFORMED', 'N-SYNC-UNCHANGED',
        'N-SYNC-PROJECT-ONLY', 'N-SYNC-WORKSPACE-ONLY', 'N-SYNC-BOTH-SIDES',
        'N-SYNC-INVALID-ENUM'
    )
    transaction = @(
        'M-TX-GROUP', 'M-TX-WORKSPACE', 'M-TX-EXPORT', 'M-TX-CONNECT',
        'M-TX-P2W', 'M-TX-W2P', 'M-TX-DISCONNECT',
        'M-TX-DELETE-WORKSPACE', 'M-TX-DELETE-GROUP'
    )
}
$script:Budgets = [ordered]@{
    maxGroupDepth = 16
    maxGroups = 500
    maxWorkspaces = 500
    maxMappings = 5000
    maxEngineeringObjects = 200
    maxCollectionItems = 5000
}

function ConvertTo-CompactJson {
    param([Parameter(Mandatory)] [object] $Value)
    return $Value | ConvertTo-Json -Compress -Depth 100
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [object] $Value
    )
    $utf8 = [Text.UTF8Encoding]::new($false)
    $temporaryPath = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [IO.File]::WriteAllText($temporaryPath, (ConvertTo-CompactJson -Value $Value), $utf8)
        [IO.File]::Move($temporaryPath, $Path, $true)
    }
    finally {
        if ([IO.File]::Exists($temporaryPath)) { [IO.File]::Delete($temporaryPath) }
    }
}

function Get-Sha256Text {
    param([Parameter(Mandatory)] [string] $Text)
    $bytes = [Text.Encoding]::UTF8.GetBytes($Text)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Get-Sha256File {
    param([Parameter(Mandatory)] [string] $Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-ExactProperties {
    param(
        [Parameter(Mandatory)] [object] $Object,
        [Parameter(Mandatory)] [string[]] $Allowed,
        [Parameter(Mandatory)] [string] $Scope
    )
    foreach ($property in @($Object.PSObject.Properties.Name)) {
        if ($Allowed -notcontains $property) {
            throw "${Scope}_unknown_property:$property"
        }
    }
    foreach ($property in $Allowed) {
        if ($null -eq $Object.PSObject.Properties[$property]) {
            throw "${Scope}_missing_property:$property"
        }
    }
}

function Read-ScenarioManifest {
    param([Parameter(Mandatory)] [string] $Path)
    if (-not [IO.Path]::IsPathFullyQualified($Path)) {
        throw 'manifest_path_must_be_absolute'
    }
    $canonicalPath = [IO.Path]::GetFullPath($Path)
    if (-not [IO.File]::Exists($canonicalPath)) {
        throw 'manifest_not_found'
    }
    $manifest = Get-Content -LiteralPath $canonicalPath -Raw | ConvertFrom-Json -Depth 100
    Assert-ExactProperties -Object $manifest -Allowed @(
        'schemaVersion', 'originalProjectPath', 'lifecycleProjectPath', 'mappingProjectPath',
        'projectToWorkspaceChangedProjectPath', 'workspaceToProjectBaselineProjectPath',
        'negativeProjectPath', 'transactionProjectPath', 'selectedObject'
    ) -Scope 'manifest'
    if (-not [string]::Equals([string]$manifest.schemaVersion, $script:ManifestSchema, [StringComparison]::Ordinal)) {
        throw 'manifest_schema_mismatch'
    }
    Assert-ExactProperties -Object $manifest.selectedObject -Allowed @('structuralPath', 'requiredFormat') -Scope 'selected_object'
    if (@($manifest.selectedObject.structuralPath).Count -ne 4) {
        throw 'selected_object_structural_path_mismatch'
    }
    $requiredSegments = @(
        [ordered]@{ index = 0; name = 'ET 200SP station_1'; objectType = 'Device' },
        [ordered]@{ index = 0; name = 'PLC_1'; objectType = 'PlcSoftware' },
        [ordered]@{ index = 0; name = 'Program blocks'; objectType = 'BlockFolder' },
        [ordered]@{ index = 1; name = 'Simulation_DB'; objectType = 'GlobalDB' }
    )
    for ($index = 0; $index -lt $requiredSegments.Count; $index++) {
        $segment = @($manifest.selectedObject.structuralPath)[$index]
        Assert-ExactProperties -Object $segment -Allowed @('index', 'name', 'objectType') -Scope "selected_object_segment_$index"
        $required = $requiredSegments[$index]
        if ([int]$segment.index -ne [int]$required.index -or
            -not [string]::Equals([string]$segment.name, [string]$required.name, [StringComparison]::Ordinal) -or
            -not [string]::Equals([string]$segment.objectType, [string]$required.objectType, [StringComparison]::Ordinal)) {
            throw 'selected_object_structural_path_mismatch'
        }
    }
    if (-not [string]::Equals([string]$manifest.selectedObject.requiredFormat, 'SimaticML', [StringComparison]::Ordinal)) {
        throw 'selected_object_format_mismatch'
    }
    return $manifest
}

function Resolve-ProjectPaths {
    param([Parameter(Mandatory)] [object] $Manifest)
    $roles = @(
        'originalProjectPath', 'lifecycleProjectPath', 'mappingProjectPath',
        'projectToWorkspaceChangedProjectPath', 'workspaceToProjectBaselineProjectPath',
        'negativeProjectPath', 'transactionProjectPath'
    )
    $resolved = [ordered]@{}
    foreach ($role in $roles) {
        $value = [string]$Manifest.$role
        if (-not [IO.Path]::IsPathFullyQualified($value)) {
            throw "project_path_must_be_absolute:$role"
        }
        $path = [IO.Path]::GetFullPath($value)
        if (-not [IO.File]::Exists($path)) {
            throw "project_path_not_found:$role"
        }
        if (-not [string]::Equals([IO.Path]::GetExtension($path), '.ap21', [StringComparison]::OrdinalIgnoreCase)) {
            throw "project_path_not_ap21:$role"
        }
        $resolved[$role] = $path
    }
    $original = [string]$resolved.originalProjectPath
    $disposableRoles = $roles | Select-Object -Skip 1
    foreach ($role in $disposableRoles) {
        if ([string]::Equals([string]$resolved[$role], $original, [StringComparison]::OrdinalIgnoreCase)) {
            throw "disposable_project_matches_original:$role"
        }
    }
    $distinct = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($role in $disposableRoles) {
        if (-not $distinct.Add([string]$resolved[$role])) {
            throw 'disposable_project_paths_not_distinct'
        }
    }
    return $resolved
}

function Resolve-SafeWorkspaceRoot {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [object] $ProjectPaths
    )
    if (-not [IO.Path]::IsPathFullyQualified($Path) -or $Path.StartsWith('\\', [StringComparison]::Ordinal)) {
        throw 'workspace_root_unsafe:not_local_absolute'
    }
    if ($Path -match '(^|[\\/])\.\.([\\/]|$)' -or $Path.Substring(2).Contains(':', [StringComparison]::Ordinal)) {
        throw 'workspace_root_unsafe:path_escape'
    }
    $canonical = [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $driveRoot = [IO.Path]::GetPathRoot($canonical).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $profileRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $unsafe = @($driveRoot, $profileRoot, $RepositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar))
    $projectPathValues = if ($ProjectPaths -is [Collections.IDictionary]) {
        @($ProjectPaths.Values)
    }
    else {
        @($ProjectPaths.PSObject.Properties | ForEach-Object { $_.Value })
    }
    $unsafe += @($projectPathValues | ForEach-Object { [IO.Path]::GetDirectoryName([string]$_) })
    foreach ($candidate in $unsafe) {
        if ([string]::Equals($canonical, $candidate, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'workspace_root_unsafe:protected_path'
        }
    }
    if ([IO.Directory]::Exists($canonical) -or [IO.File]::Exists($canonical)) {
        throw 'workspace_root_unsafe:must_be_absent'
    }
    $parent = [IO.Directory]::GetParent($canonical)
    if ($null -eq $parent -or -not $parent.Exists) {
        throw 'workspace_root_unsafe:missing_parent'
    }
    $cursor = $parent
    while ($null -ne $cursor) {
        if (($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'workspace_root_unsafe:reparse_ancestor'
        }
        $cursor = $cursor.Parent
    }
    return $canonical
}

function Resolve-CommonInputs {
    param([Parameter(Mandatory)] [string] $RepositoryRoot)
    if ([string]::IsNullOrWhiteSpace($ScenarioManifestPath)) { throw 'scenario_manifest_required' }
    if ([string]::IsNullOrWhiteSpace($WorkerExecutable)) { throw 'worker_required' }
    if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) { throw 'evidence_root_required' }
    if ([string]::IsNullOrWhiteSpace($WorkspaceRoot)) { throw 'workspace_root_required' }
    $manifest = Read-ScenarioManifest -Path $ScenarioManifestPath
    $projects = Resolve-ProjectPaths -Manifest $manifest
    $worker = if ([IO.Path]::IsPathFullyQualified($WorkerExecutable)) { [IO.Path]::GetFullPath($WorkerExecutable) } else { $null }
    if ($null -eq $worker -or -not [IO.File]::Exists($worker)) { throw 'worker_not_found' }
    if (-not [string]::Equals($WorkerAccessMode, 'read-write', [StringComparison]::Ordinal)) { throw 'worker_must_be_read_write' }
    if (-not [IO.Path]::IsPathFullyQualified($EvidenceRoot)) { throw 'evidence_root_must_be_absolute' }
    $evidence = [IO.Path]::GetFullPath($EvidenceRoot)
    $workspace = Resolve-SafeWorkspaceRoot -Path $WorkspaceRoot -RepositoryRoot $RepositoryRoot -ProjectPaths $projects
    return [ordered]@{
        manifest = $manifest
        manifestPath = [IO.Path]::GetFullPath($ScenarioManifestPath)
        projectPaths = $projects
        workerExecutable = $worker
        evidenceRoot = $evidence
        workspaceRoot = $workspace
    }
}

function Start-ProbeWorker {
    param([Parameter(Mandatory)] [string] $Executable)
    $psi = [Diagnostics.ProcessStartInfo]::new()
    if ([string]::Equals([IO.Path]::GetExtension($Executable), '.ps1', [StringComparison]::OrdinalIgnoreCase)) {
        $psi.FileName = (Get-Command pwsh -ErrorAction Stop).Source
        [void]$psi.ArgumentList.Add('-NoProfile')
        [void]$psi.ArgumentList.Add('-File')
        [void]$psi.ArgumentList.Add($Executable)
    }
    else {
        $psi.FileName = $Executable
    }
    [void]$psi.ArgumentList.Add('--access-mode')
    [void]$psi.ArgumentList.Add('read-write')
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $false
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $process = [Diagnostics.Process]::Start($psi)
    if ($null -eq $process) { throw 'worker_start_failed' }
    return $process
}

function Invoke-ProbeWorker {
    param(
        [Parameter(Mandatory)] [Diagnostics.Process] $Process,
        [Parameter(Mandatory)] [object] $Request,
        [Parameter(Mandatory)] [int] $Timeout
    )
    $requestJson = ConvertTo-CompactJson -Value $Request
    $Process.StandardInput.WriteLine($requestJson)
    $Process.StandardInput.Flush()
    $readTask = $Process.StandardOutput.ReadLineAsync()
    if (-not $readTask.Wait($Timeout * 1000)) {
        $Process.Kill($true)
        throw 'worker_request_timed_out'
    }
    $line = $readTask.GetAwaiter().GetResult()
    if ([string]::IsNullOrWhiteSpace($line)) { throw 'worker_process_lost' }
    try { $response = $line | ConvertFrom-Json -Depth 100 } catch { throw 'worker_response_malformed' }
    if (-not [string]::Equals([string]$response.requestId, [string]$Request.requestId, [StringComparison]::Ordinal)) {
        throw 'worker_response_request_id_mismatch'
    }
    if (-not [bool]$response.success) { throw "worker_failure:$($response.error)" }
    if ($response.payload -is [string]) {
        try { return [string]$response.payload | ConvertFrom-Json -Depth 100 } catch { throw 'worker_payload_malformed' }
    }
    return $response.payload
}

function Get-ReturnMember {
    param(
        [Parameter(Mandatory)] [object] $Result,
        [Parameter(Mandatory)] [string] $Name
    )
    $matches = @($Result.return.members | Where-Object { [string]::Equals([string]$_.name, $Name, [StringComparison]::Ordinal) })
    if ($matches.Count -ne 1) { throw "inventory_member_missing_or_ambiguous:$Name" }
    return [string]$matches[0].stringValue
}

function Assert-InventoryResult {
    param([Parameter(Mandatory)] [object] $Result)
    if (-not [string]::Equals([string]$Result.schemaVersion, $script:ProbeSchema, [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$Result.caseId, 'P-INVENTORY', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$Result.outcome, 'returned', [StringComparison]::Ordinal) -or
        [bool]$Result.uncertainOutcome -or [bool]$Result.stopScenarioFamily) {
        throw 'inventory_result_not_usable'
    }
    $requiredChecks = @('selected_engineering_object_is_Simulation_DB', 'exact_SimaticML_supported')
    foreach ($checkName in $requiredChecks) {
        $checks = @($Result.preconditions | Where-Object { [string]::Equals([string]$_.name, $checkName, [StringComparison]::Ordinal) })
        if ($checks.Count -ne 1 -or -not [bool]$checks[0].satisfied) { throw "inventory_precondition_failed:$checkName" }
    }
    $rootChecks = @($Result.safetyInvariants | Where-Object { [string]::Equals([string]$_.name, 'workspace_root_absent_after_inventory', [StringComparison]::Ordinal) })
    if ($rootChecks.Count -ne 1 -or -not [bool]$rootChecks[0].satisfied) { throw 'inventory_workspace_root_invariant_failed' }
    $formats = @($Result.return.members | Where-Object { ([string]$_.name).StartsWith('fileFormat[', [StringComparison]::Ordinal) } | ForEach-Object { [string]$_.stringValue })
    if ($formats -notcontains 'SimaticML') { throw 'inventory_exact_format_missing' }
}

function Get-InventoryMappingSelector {
    param([Parameter(Mandatory)] [object] $Result)
    if ($null -eq $Result.before -or $null -eq $Result.before.mappings) { return $null }
    $matches = @($Result.before.mappings | Where-Object {
        $selector = $_.selector
        $null -ne $selector -and
        [string]::Equals([string]$selector.format, 'SimaticML', [StringComparison]::Ordinal) -and
        $null -ne $selector.engineeringObject -and
        @($selector.engineeringObject.structuralPath).Count -eq 4 -and
        [string]::Equals(
            [string]@($selector.engineeringObject.structuralPath)[3].name,
            'Simulation_DB',
            [StringComparison]::Ordinal)
    })
    if ($matches.Count -eq 0) { return $null }
    return $matches[0].selector
}

function Get-GitCommit {
    param([Parameter(Mandatory)] [string] $RepositoryRoot)
    $psi = [Diagnostics.ProcessStartInfo]::new('git')
    [void]$psi.ArgumentList.Add('-C')
    [void]$psi.ArgumentList.Add($RepositoryRoot)
    [void]$psi.ArgumentList.Add('rev-parse')
    [void]$psi.ArgumentList.Add('HEAD')
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $false
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $process = [Diagnostics.Process]::Start($psi)
    if ($null -eq $process) { throw 'git_provenance_failed' }
    try {
        $value = $process.StandardOutput.ReadToEnd().Trim()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0 -or $value.Length -ne 40) { throw 'git_provenance_failed' }
        return $value
    }
    finally { $process.Dispose() }
}

function New-PlanScenarios {
    $scenarios = @()
    foreach ($scenarioId in $script:ScenarioOrder) {
        $scenarios += [ordered]@{
            scenarioId = $scenarioId
            caseIds = @($script:ScenarioCases[$scenarioId])
        }
    }
    return $scenarios
}

function Invoke-Inventory {
    param(
        [Parameter(Mandatory)] [object] $Inputs,
        [Parameter(Mandatory)] [string] $RepositoryRoot
    )
    if ([IO.Directory]::Exists($Inputs.workspaceRoot) -or [IO.File]::Exists($Inputs.workspaceRoot)) {
        throw 'workspace_root_unsafe:must_be_absent'
    }
    $roles = @(
        'lifecycleProjectPath', 'mappingProjectPath', 'projectToWorkspaceChangedProjectPath',
        'workspaceToProjectBaselineProjectPath', 'negativeProjectPath', 'transactionProjectPath'
    )
    $projects = @()
    $worker = Start-ProbeWorker -Executable $Inputs.workerExecutable
    try {
        $sequence = 0
        foreach ($role in $roles) {
            $sequence++
            $requestId = "inventory-$sequence"
            $probe = [ordered]@{
                schemaVersion = $script:ProbeSchema
                runId = 'inventory'
                sessionId = $role
                scenarioId = 'inventory'
                caseId = 'P-INVENTORY'
                caseInstanceId = $requestId
                mode = 'Inventory'
                workspaceRoot = $Inputs.workspaceRoot
                engineeringObject = [ordered]@{
                    structuralPath = @($Inputs.manifest.selectedObject.structuralPath)
                }
                fileFormat = 'SimaticML'
                maxGroupDepth = $script:Budgets.maxGroupDepth
                maxGroups = $script:Budgets.maxGroups
                maxWorkspaces = $script:Budgets.maxWorkspaces
                maxMappings = $script:Budgets.maxMappings
                maxEngineeringObjects = $script:Budgets.maxEngineeringObjects
                maxCollectionItems = $script:Budgets.maxCollectionItems
            }
            $request = [ordered]@{
                requestId = $requestId
                method = 'probe_vci_mutation_contract'
                projectPath = [string]$Inputs.projectPaths[$role]
                vciMutationProbe = $probe
            }
            $result = Invoke-ProbeWorker -Process $worker -Request $request -Timeout $TimeoutSeconds
            Assert-InventoryResult -Result $result
            $selectedMapping = Get-InventoryMappingSelector -Result $result
            $projects += [ordered]@{
                role = $role
                projectPath = [string]$Inputs.projectPaths[$role]
                projectSha256 = Get-Sha256File -Path ([string]$Inputs.projectPaths[$role])
                selectedObjectName = 'Simulation_DB'
                selectedObjectRuntimeType = Get-ReturnMember -Result $result -Name 'engineeringObject.runtimeType'
                selectedObjectStableIdentifier = Get-ReturnMember -Result $result -Name 'engineeringObject.stableIdentifier'
                selectedObjectFingerprint = Get-ReturnMember -Result $result -Name 'engineeringObject.fingerprint'
                selectedObjectStructuralPath = Get-ReturnMember -Result $result -Name 'engineeringObject.structuralPath'
                selectedWorkspaceName = Get-ReturnMember -Result $result -Name 'workspace.name'
                selectedWorkspaceGroupPath = Get-ReturnMember -Result $result -Name 'workspace.groupPath'
                selectedWorkspaceRootPath = Get-ReturnMember -Result $result -Name 'workspace.canonicalRootPath'
                selectedFormat = 'SimaticML'
                selectedMapping = $selectedMapping
            }
        }
    }
    finally {
        if (-not $worker.HasExited) {
            $worker.StandardInput.Close()
            if (-not $worker.WaitForExit(5000)) { $worker.Kill($true) }
        }
        $worker.Dispose()
    }
    if ([IO.Directory]::Exists($Inputs.workspaceRoot) -or [IO.File]::Exists($Inputs.workspaceRoot)) {
        throw 'workspace_root_created_during_inventory'
    }

    $inventory = [ordered]@{
        schemaVersion = $script:InventorySchema
        manifestSchemaVersion = $script:ManifestSchema
        workspaceRoot = $Inputs.workspaceRoot
        workspaceRootExistsAfter = $false
        projects = $projects
    }
    $scriptPath = [IO.Path]::GetFullPath($PSCommandPath)
    $planProjects = @($projects | ForEach-Object {
        [ordered]@{
            role = $_.role
            projectPath = $_.projectPath
            projectSha256 = $_.projectSha256
            selectedObjectStableIdentifier = $_.selectedObjectStableIdentifier
            selectedObjectFingerprint = $_.selectedObjectFingerprint
            selectedWorkspaceName = $_.selectedWorkspaceName
            selectedWorkspaceGroupPath = $_.selectedWorkspaceGroupPath
            selectedWorkspaceRootPath = $_.selectedWorkspaceRootPath
            selectedMapping = $_.selectedMapping
        }
    })
    $canonicalPlan = [ordered]@{
        schemaVersion = $script:PlanSchema
        provenance = [ordered]@{
            gitCommit = Get-GitCommit -RepositoryRoot $RepositoryRoot
            workerSha256 = Get-Sha256File -Path $Inputs.workerExecutable
            scriptSha256 = Get-Sha256File -Path $scriptPath
        }
        manifestPath = $Inputs.manifestPath
        originalProject = [ordered]@{
            projectPath = [string]$Inputs.projectPaths.originalProjectPath
            projectSha256 = Get-Sha256File -Path ([string]$Inputs.projectPaths.originalProjectPath)
        }
        projects = $planProjects
        selectedObject = [ordered]@{
            structuralPath = @($Inputs.manifest.selectedObject.structuralPath)
            requiredFormat = 'SimaticML'
        }
        workspaceRoot = $Inputs.workspaceRoot
        scenarios = @(New-PlanScenarios)
        expectedPreconditions = @(
            'all disposable project hashes unchanged since Inventory',
            'workspace root remains absent until Apply confirmation',
            'Simulation_DB resolves with exact SimaticML support in every disposable copy'
        )
        acknowledgement = $script:AcknowledgementText
        timeoutSeconds = $TimeoutSeconds
        budgets = $script:Budgets
    }
    $canonicalPlanJson = ConvertTo-CompactJson -Value $canonicalPlan
    $planHashValue = Get-Sha256Text -Text $canonicalPlanJson
    $planEvidence = [ordered]@{
        schemaVersion = $script:PlanEvidenceSchema
        planHash = $planHashValue
        canonicalPlan = $canonicalPlan
    }
    [IO.Directory]::CreateDirectory($Inputs.evidenceRoot) | Out-Null
    $evidenceRunId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ', [Globalization.CultureInfo]::InvariantCulture) + '-' + $planHashValue.Substring(0, 12)
    $evidenceRunRoot = [IO.Path]::Combine($Inputs.evidenceRoot, $evidenceRunId)
    if ([IO.Directory]::Exists($evidenceRunRoot) -or [IO.File]::Exists($evidenceRunRoot)) {
        throw 'evidence_run_root_already_exists'
    }
    [IO.Directory]::CreateDirectory($evidenceRunRoot) | Out-Null
    Write-JsonFile -Path ([IO.Path]::Combine($evidenceRunRoot, 'inventory.json')) -Value $inventory
    Write-JsonFile -Path ([IO.Path]::Combine($evidenceRunRoot, 'plan.json')) -Value $planEvidence
    return [ordered]@{
        schemaVersion = $script:InventorySchema
        evidenceRunId = $evidenceRunId
        evidenceRunRoot = $evidenceRunRoot
        inventoryPath = [IO.Path]::Combine($evidenceRunRoot, 'inventory.json')
        planPath = [IO.Path]::Combine($evidenceRunRoot, 'plan.json')
        planHash = $planHashValue
        selectedObject = 'Simulation_DB'
        resolvedProjectPaths = @($projects | ForEach-Object { $_.projectPath })
        resolvedWorkspacePaths = @($projects | ForEach-Object { $_.selectedWorkspaceRootPath })
        workspaceRoot = $Inputs.workspaceRoot
        workspaceRootExistsAfter = $false
    }
}

function Assert-ApplyGuards {
    param(
        [Parameter(Mandatory)] [object] $Inputs,
        [Parameter(Mandatory)] [string] $RepositoryRoot
    )
    if (-not $AllowMutation) { throw 'allow_mutation_required' }
    if (-not [string]::Equals($Acknowledgement, $script:AcknowledgementText, [StringComparison]::Ordinal)) {
        throw 'acknowledgement_required'
    }
    if ([string]::IsNullOrWhiteSpace($ExpectedPlanHash)) { throw 'plan_hash_required' }
    if (-not [IO.Directory]::Exists($Inputs.evidenceRoot)) { throw 'plan_not_found' }
    $planPaths = @()
    $directPlan = [IO.Path]::Combine($Inputs.evidenceRoot, 'plan.json')
    if ([IO.File]::Exists($directPlan)) { $planPaths += $directPlan }
    foreach ($directory in [IO.Directory]::EnumerateDirectories($Inputs.evidenceRoot)) {
        $candidate = [IO.Path]::Combine($directory, 'plan.json')
        if ([IO.File]::Exists($candidate)) { $planPaths += $candidate }
    }
    if ($planPaths.Count -eq 0) { throw 'plan_not_found' }
    $planMatches = @()
    foreach ($candidatePath in $planPaths) {
        $candidatePlan = Get-Content -LiteralPath $candidatePath -Raw | ConvertFrom-Json -Depth 100
        $candidateCanonicalJson = ConvertTo-CompactJson -Value $candidatePlan.canonicalPlan
        $candidateHash = Get-Sha256Text -Text $candidateCanonicalJson
        if ([string]::Equals([string]$candidatePlan.planHash, $candidateHash, [StringComparison]::Ordinal) -and
            [string]::Equals($ExpectedPlanHash, $candidateHash, [StringComparison]::Ordinal)) {
            $planMatches += [ordered]@{ path = $candidatePath; evidence = $candidatePlan; calculatedHash = $candidateHash }
        }
    }
    if ($planMatches.Count -ne 1) { throw 'plan_hash_mismatch' }
    $planPath = [string]$planMatches[0].path
    $planEvidence = $planMatches[0].evidence
    $calculatedHash = [string]$planMatches[0].calculatedHash
    $Inputs.evidenceRoot = [IO.Path]::GetDirectoryName($planPath)
    if (-not [string]::Equals([string]$planEvidence.planHash, $calculatedHash, [StringComparison]::Ordinal)) {
        throw 'plan_hash_mismatch'
    }
    if (-not [string]::Equals([string]$planEvidence.canonicalPlan.workspaceRoot, $Inputs.workspaceRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'plan_workspace_root_mismatch'
    }
    if (-not [string]::Equals([string]$planEvidence.canonicalPlan.manifestPath, $Inputs.manifestPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'plan_manifest_path_mismatch'
    }
    if ([int]$planEvidence.canonicalPlan.timeoutSeconds -ne $TimeoutSeconds) { throw 'plan_timeout_mismatch' }
    if (-not [string]::Equals(
            [string]$planEvidence.canonicalPlan.provenance.gitCommit,
            (Get-GitCommit -RepositoryRoot $RepositoryRoot),
            [StringComparison]::Ordinal)) { throw 'plan_git_commit_mismatch' }
    if (-not [string]::Equals(
            [string]$planEvidence.canonicalPlan.provenance.workerSha256,
            (Get-Sha256File -Path $Inputs.workerExecutable),
            [StringComparison]::Ordinal)) { throw 'plan_worker_hash_mismatch' }
    if (-not [string]::Equals(
            [string]$planEvidence.canonicalPlan.provenance.scriptSha256,
            (Get-Sha256File -Path ([IO.Path]::GetFullPath($PSCommandPath))),
            [StringComparison]::Ordinal)) { throw 'plan_script_hash_mismatch' }
    if (-not [string]::Equals(
            [string]$planEvidence.canonicalPlan.originalProject.projectSha256,
            (Get-Sha256File -Path ([string]$Inputs.projectPaths.originalProjectPath)),
            [StringComparison]::Ordinal)) { throw 'plan_original_project_hash_mismatch' }
    foreach ($project in @($planEvidence.canonicalPlan.projects)) {
        $role = [string]$project.role
        if ($null -eq $Inputs.projectPaths[$role]) { throw "plan_project_role_unknown:$role" }
        if (-not [string]::Equals([string]$project.projectPath, [string]$Inputs.projectPaths[$role], [StringComparison]::OrdinalIgnoreCase)) {
            throw "plan_project_path_mismatch:$role"
        }
        if (-not [string]::Equals([string]$project.projectSha256, (Get-Sha256File -Path ([string]$Inputs.projectPaths[$role])), [StringComparison]::Ordinal)) {
            throw "plan_project_hash_mismatch:$role"
        }
    }
    foreach ($fileName in @(
            'manifest.json', 'cases.jsonl', 'snapshot-before.json', 'snapshot-after.json',
            'filesystem-before.json', 'filesystem-after.json', 'summary.json')) {
        if ([IO.File]::Exists([IO.Path]::Combine($Inputs.evidenceRoot, $fileName))) {
            throw "apply_evidence_already_exists:$fileName"
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($EquivalentEvidenceRoot)) {
        $equivalentRoot = if ([IO.Path]::IsPathFullyQualified($EquivalentEvidenceRoot)) {
            [IO.Path]::GetFullPath($EquivalentEvidenceRoot)
        }
        else { $null }
        if ($null -eq $equivalentRoot -or
            -not [IO.File]::Exists([IO.Path]::Combine($equivalentRoot, 'cases.jsonl')) -or
            -not [IO.File]::Exists([IO.Path]::Combine($equivalentRoot, 'manifest.json')) -or
            -not [IO.File]::Exists([IO.Path]::Combine($equivalentRoot, 'filesystem-after.json')) -or
            -not [IO.File]::Exists([IO.Path]::Combine($equivalentRoot, 'summary.json'))) {
            throw 'equivalent_evidence_not_complete'
        }
    }
    return $planEvidence
}

function Get-ScenarioProjectRole {
    param([Parameter(Mandatory)] [string] $ScenarioId)
    switch ($ScenarioId) {
        'lifecycle' { return 'lifecycleProjectPath' }
        'mapping' { return 'mappingProjectPath' }
        'project_to_workspace' { return 'projectToWorkspaceChangedProjectPath' }
        'workspace_to_project' { return 'workspaceToProjectBaselineProjectPath' }
        'negative' { return 'negativeProjectPath' }
        'transaction' { return 'transactionProjectPath' }
        default { throw "unknown_scenario:$ScenarioId" }
    }
}

function Get-PlanProject {
    param(
        [Parameter(Mandatory)] [object] $PlanEvidence,
        [Parameter(Mandatory)] [string] $Role
    )
    $matches = @($PlanEvidence.canonicalPlan.projects | Where-Object {
        [string]::Equals([string]$_.role, $Role, [StringComparison]::Ordinal)
    })
    if ($matches.Count -ne 1) { throw "plan_project_missing_or_ambiguous:$Role" }
    return $matches[0]
}

function New-ApplyWorkerRequest {
    param(
        [Parameter(Mandatory)] [object] $PlanEvidence,
        [Parameter(Mandatory)] [string] $RunId,
        [Parameter(Mandatory)] [string] $ScenarioId,
        [Parameter(Mandatory)] [string] $CaseId,
        [Parameter(Mandatory)] [string] $Role,
        [Parameter(Mandatory)] [string] $ProjectPath,
        [Parameter(Mandatory)] [string] $WorkspaceRoot,
        [Parameter(Mandatory)] [int] $Sequence
    )
    $project = Get-PlanProject -PlanEvidence $PlanEvidence -Role $Role
    $engineeringObject = [ordered]@{
        stableIdentifier = $project.selectedObjectStableIdentifier
        structuralPath = @($PlanEvidence.canonicalPlan.selectedObject.structuralPath)
        fingerprint = $project.selectedObjectFingerprint
    }
    $synchronizationMode = switch ($CaseId) {
        { $_ -in @('M-P2W', 'M-TX-P2W') } { 'ProjectToWorkspace'; break }
        { $_ -in @('M-W2P', 'M-TX-W2P') } { 'WorkspaceToProject'; break }
        default { $null }
    }
    $probe = [ordered]@{
        schemaVersion = $script:ProbeSchema
        runId = $RunId
        sessionId = $ScenarioId
        scenarioId = $ScenarioId
        caseId = $CaseId
        caseInstanceId = "$ScenarioId-$Sequence"
        mode = 'Apply'
        workspaceRoot = $WorkspaceRoot
        engineeringObject = $engineeringObject
        mapping = $project.selectedMapping
        fileFormat = $(if ($CaseId -in @('M-EXPORT', 'M-TX-EXPORT')) { 'SimaticML' } else { $null })
        seedRelativePath = 'mapping\export\Simulation_DB.xml'
        synchronizationMode = $synchronizationMode
        rollbackTransaction = $CaseId.StartsWith('M-TX-', [StringComparison]::Ordinal)
        maxGroupDepth = [int]$PlanEvidence.canonicalPlan.budgets.maxGroupDepth
        maxGroups = [int]$PlanEvidence.canonicalPlan.budgets.maxGroups
        maxWorkspaces = [int]$PlanEvidence.canonicalPlan.budgets.maxWorkspaces
        maxMappings = [int]$PlanEvidence.canonicalPlan.budgets.maxMappings
        maxEngineeringObjects = [int]$PlanEvidence.canonicalPlan.budgets.maxEngineeringObjects
        maxCollectionItems = [int]$PlanEvidence.canonicalPlan.budgets.maxCollectionItems
    }
    return [ordered]@{
        requestId = "apply-$Sequence"
        method = 'probe_vci_mutation_contract'
        projectPath = $ProjectPath
        vciMutationProbe = $probe
    }
}

function Get-FilesystemSnapshot {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [Parameter(Mandatory)] [int] $MaxFiles
    )
    $files = @()
    $omissions = @()
    $complete = $true
    $exists = [IO.Directory]::Exists($Root)
    if ($exists) {
        $pending = [Collections.Generic.Stack[IO.DirectoryInfo]]::new()
        $pending.Push([IO.DirectoryInfo]::new($Root))
        while ($pending.Count -gt 0) {
            $directory = $pending.Pop()
            try {
                $entries = @($directory.EnumerateFileSystemInfos() | Sort-Object FullName)
            }
            catch {
                $complete = $false
                $omissions += [ordered]@{ relativePath = [IO.Path]::GetRelativePath($Root, $directory.FullName); reason = 'directory_unreadable' }
                continue
            }
            foreach ($entry in $entries) {
                $relativePath = [IO.Path]::GetRelativePath($Root, $entry.FullName).Replace('\', '/')
                if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    $complete = $false
                    $omissions += [ordered]@{ relativePath = $relativePath; reason = 'reparse_point_not_followed' }
                    continue
                }
                if ($entry -is [IO.DirectoryInfo]) {
                    $pending.Push($entry)
                    continue
                }
                if ($files.Count -ge $MaxFiles) {
                    $complete = $false
                    $omissions += [ordered]@{ relativePath = $relativePath; reason = 'file_budget_exhausted' }
                    break
                }
                try {
                    $file = [IO.FileInfo]$entry
                    $files += [ordered]@{
                        relativePath = $relativePath
                        size = $file.Length
                        sha256 = Get-Sha256File -Path $file.FullName
                    }
                }
                catch {
                    $complete = $false
                    $omissions += [ordered]@{ relativePath = $relativePath; reason = 'file_unreadable' }
                }
            }
            if ($files.Count -ge $MaxFiles) { break }
        }
    }
    $snapshot = [ordered]@{
        schemaVersion = 'vci-phase1-mutation-filesystem/v1'
        root = $Root
        exists = $exists
        complete = $complete
        files = @($files | Sort-Object relativePath)
        omissions = @($omissions | Sort-Object relativePath, reason)
        snapshotId = $null
    }
    $snapshot.snapshotId = Get-Sha256Text -Text (ConvertTo-CompactJson -Value $snapshot)
    return $snapshot
}

function Stop-ProbeWorker {
    param([AllowNull()] [Diagnostics.Process] $Process)
    if ($null -eq $Process) { return }
    try {
        if (-not $Process.HasExited) {
            $Process.StandardInput.Close()
            if (-not $Process.WaitForExit(5000)) { $Process.Kill($true) }
        }
    }
    finally { $Process.Dispose() }
}

function Get-ApplyResultStopReason {
    param(
        [Parameter(Mandatory)] [object] $Result,
        [Parameter(Mandatory)] [string] $CaseId,
        [Parameter(Mandatory)] [string] $CaseInstanceId
    )
    if (-not [string]::Equals([string]$Result.schemaVersion, $script:ProbeSchema, [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$Result.caseId, $CaseId, [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$Result.caseInstanceId, $CaseInstanceId, [StringComparison]::Ordinal)) {
        return 'protocol_error'
    }
    if ($null -eq $Result.before -or $null -eq $Result.after) { return 'incomplete_evidence' }
    if ([bool]$Result.uncertainOutcome) { return 'uncertain_mutation' }
    if ([bool]$Result.stopScenarioFamily) { return 'worker_family_stop' }
    if ([string]$Result.outcome -in @('returned', 'returned_null', 'threw')) {
        if ($null -eq $Result.canary -or -not [bool]$Result.canary.attempted -or -not [bool]$Result.canary.usable) {
            return 'canary_unusable'
        }
    }
    return $null
}

function ConvertTo-NormalizedValue {
    param(
        [AllowNull()] [object] $Value,
        [Parameter(Mandatory)] [object] $Replacements
    )
    if ($null -eq $Value) { return $null }
    if ($Value -is [string]) {
        $normalized = [string]$Value
        foreach ($replacement in @($Replacements)) {
            if (-not [string]::IsNullOrEmpty([string]$replacement.from)) {
                $normalized = $normalized.Replace([string]$replacement.from, [string]$replacement.to, [StringComparison]::OrdinalIgnoreCase)
            }
        }
        return $normalized
    }
    if ($Value -is [bool] -or $Value -is [ValueType]) { return $Value }
    if ($Value -is [Collections.IEnumerable] -and $Value -isnot [Collections.IDictionary]) {
        $items = @()
        foreach ($item in $Value) { $items += ,(ConvertTo-NormalizedValue -Value $item -Replacements $Replacements) }
        return ,$items
    }
    $result = [ordered]@{}
    $properties = if ($Value -is [Collections.IDictionary]) {
        @($Value.Keys | ForEach-Object { [ordered]@{ Name = [string]$_; Value = $Value[$_] } })
    }
    else {
        @($Value.PSObject.Properties | ForEach-Object { [ordered]@{ Name = $_.Name; Value = $_.Value } })
    }
    foreach ($property in $properties) {
        if ([string]$property.Name -in @('runId', 'caseInstanceId')) { continue }
        $result[[string]$property.Name] = ConvertTo-NormalizedValue -Value $property.Value -Replacements $Replacements
    }
    return $result
}

function Get-NormalizationReplacements {
    param([Parameter(Mandatory)] [object] $ManifestEvidence)
    $replacements = @(
        [ordered]@{ from = [string]$ManifestEvidence.runId; to = '<RUN_ID>' },
        [ordered]@{ from = [string]$ManifestEvidence.workspaceRoot; to = '<WORKSPACE_ROOT>' }
    )
    $projectProperties = if ($ManifestEvidence.projectPaths -is [Collections.IDictionary]) {
        @($ManifestEvidence.projectPaths.Keys | ForEach-Object {
            [ordered]@{ Name = [string]$_; Value = $ManifestEvidence.projectPaths[$_] }
        })
    }
    else {
        @($ManifestEvidence.projectPaths.PSObject.Properties | ForEach-Object {
            [ordered]@{ Name = $_.Name; Value = $_.Value }
        })
    }
    foreach ($property in $projectProperties) {
        $replacements += [ordered]@{ from = [string]$property.Value; to = "<PROJECT:$($property.Name)>" }
    }
    return $replacements
}

function Get-NormalizedCaseRecords {
    param(
        [Parameter(Mandatory)] [string] $CasesPath,
        [Parameter(Mandatory)] [object] $ManifestEvidence
    )
    $replacements = Get-NormalizationReplacements -ManifestEvidence $ManifestEvidence
    $records = @()
    foreach ($line in [IO.File]::ReadLines($CasesPath)) {
        $record = $line | ConvertFrom-Json -Depth 100
        $records += [ordered]@{
            sequence = [int]$record.sequence
            scenarioId = [string]$record.scenarioId
            caseId = [string]$record.caseId
            transportOutcome = [string]$record.transport.outcome
            stopReason = $record.stopReason
            workerResult = ConvertTo-NormalizedValue -Value $record.workerResult -Replacements $replacements
        }
    }
    return $records
}

function Get-NormalizedFilesystemAfter {
    param([Parameter(Mandatory)] [string] $Path)
    $snapshot = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -Depth 100
    return [ordered]@{
        complete = [bool]$snapshot.complete
        files = @($snapshot.files | Where-Object {
            -not [string]::Equals([string]$_.relativePath, '.vci-mutation-run.json', [StringComparison]::Ordinal)
        } | ForEach-Object {
            [ordered]@{ relativePath = $_.relativePath; size = $_.size; sha256 = $_.sha256 }
        })
        omissions = @($snapshot.omissions)
    }
}

function Compare-EquivalentEvidence {
    param(
        [Parameter(Mandatory)] [string] $CurrentEvidenceRoot,
        [Parameter(Mandatory)] [object] $CurrentManifestEvidence,
        [AllowNull()] [string] $OtherEvidenceRoot
    )
    if ([string]::IsNullOrWhiteSpace($OtherEvidenceRoot)) { return @() }
    $otherRoot = [IO.Path]::GetFullPath($OtherEvidenceRoot)
    $otherManifest = Get-Content -LiteralPath ([IO.Path]::Combine($otherRoot, 'manifest.json')) -Raw | ConvertFrom-Json -Depth 100
    $current = @(Get-NormalizedCaseRecords -CasesPath ([IO.Path]::Combine($CurrentEvidenceRoot, 'cases.jsonl')) -ManifestEvidence $CurrentManifestEvidence)
    $other = @(Get-NormalizedCaseRecords -CasesPath ([IO.Path]::Combine($otherRoot, 'cases.jsonl')) -ManifestEvidence $otherManifest)
    $mismatches = @()
    $count = [Math]::Max($current.Count, $other.Count)
    for ($index = 0; $index -lt $count; $index++) {
        if ($index -ge $current.Count -or $index -ge $other.Count) {
            $caseId = if ($index -lt $current.Count) { [string]$current[$index].caseId } else { [string]$other[$index].caseId }
            $mismatches += [ordered]@{ index = $index; caseId = $caseId; reason = 'record_count_or_order_difference' }
            continue
        }
        if (-not [string]::Equals(
                (ConvertTo-CompactJson -Value $current[$index]),
                (ConvertTo-CompactJson -Value $other[$index]),
                [StringComparison]::Ordinal)) {
            $mismatches += [ordered]@{ index = $index; caseId = [string]$current[$index].caseId; reason = 'normalized_case_difference' }
        }
    }
    $currentFilesystem = Get-NormalizedFilesystemAfter -Path ([IO.Path]::Combine($CurrentEvidenceRoot, 'filesystem-after.json'))
    $otherFilesystem = Get-NormalizedFilesystemAfter -Path ([IO.Path]::Combine($otherRoot, 'filesystem-after.json'))
    if (-not [string]::Equals(
            (ConvertTo-CompactJson -Value $currentFilesystem),
            (ConvertTo-CompactJson -Value $otherFilesystem),
            [StringComparison]::Ordinal)) {
        $mismatches += [ordered]@{ index = -1; caseId = '<filesystem-after>'; reason = 'normalized_filesystem_difference' }
    }
    return $mismatches
}

function Invoke-Apply {
    param(
        [Parameter(Mandatory)] [object] $Inputs,
        [Parameter(Mandatory)] [object] $PlanEvidence
    )
    $runId = [IO.Path]::GetFileName($Inputs.evidenceRoot.TrimEnd([IO.Path]::DirectorySeparatorChar))
    if ([IO.Directory]::Exists($Inputs.workspaceRoot) -or [IO.File]::Exists($Inputs.workspaceRoot)) {
        throw 'workspace_root_appeared_after_apply_guard'
    }
    $filesystemBefore = Get-FilesystemSnapshot -Root $Inputs.workspaceRoot -MaxFiles $script:Budgets.maxCollectionItems
    [IO.Directory]::CreateDirectory($Inputs.workspaceRoot) | Out-Null
    $markerPath = [IO.Path]::Combine($Inputs.workspaceRoot, '.vci-mutation-run.json')
    Write-JsonFile -Path $markerPath -Value ([ordered]@{
        schemaVersion = 'vci-phase1-mutation-run-marker/v1'
        runId = $runId
        planHash = [string]$PlanEvidence.planHash
    })
    if (-not $NonInteractiveAcceptance) {
        $confirmation = Read-Host "Type APPLY $($PlanEvidence.planHash) to continue"
        if (-not [string]::Equals($confirmation, "APPLY $($PlanEvidence.planHash)", [StringComparison]::Ordinal)) {
            $entries = @([IO.Directory]::EnumerateFileSystemEntries($Inputs.workspaceRoot))
            if ($entries.Count -eq 1 -and [string]::Equals($entries[0], $markerPath, [StringComparison]::OrdinalIgnoreCase)) {
                [IO.File]::Delete($markerPath)
                [IO.Directory]::Delete($Inputs.workspaceRoot, $false)
            }
            throw 'interactive_confirmation_declined'
        }
    }
    $manifestEvidence = [ordered]@{
        schemaVersion = 'vci-phase1-mutation-manifest/v1'
        runId = $runId
        planHash = [string]$PlanEvidence.planHash
        workspaceRoot = $Inputs.workspaceRoot
        workerExecutable = $Inputs.workerExecutable
        scenarioManifestPath = $Inputs.manifestPath
        projectPaths = $Inputs.projectPaths
        scenarioManifest = $Inputs.manifest
    }
    Write-JsonFile -Path ([IO.Path]::Combine($Inputs.evidenceRoot, 'manifest.json')) -Value $manifestEvidence
    Write-JsonFile -Path ([IO.Path]::Combine($Inputs.evidenceRoot, 'filesystem-before.json')) -Value $filesystemBefore

    $casesPath = [IO.Path]::Combine($Inputs.evidenceRoot, 'cases.jsonl')
    $stream = [IO.File]::Open($casesPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false))
    $writer.AutoFlush = $true
    $sequence = 0
    $records = @()
    $snapshotBefore = @()
    $snapshotAfter = @()
    $stoppedFamilies = @()
    try {
        foreach ($scenario in @($PlanEvidence.canonicalPlan.scenarios)) {
            $scenarioId = [string]$scenario.scenarioId
            $role = Get-ScenarioProjectRole -ScenarioId $scenarioId
            $projectPath = [string]$Inputs.projectPaths[$role]
            $familyStopped = $false
            $worker = $null
            try {
                $worker = Start-ProbeWorker -Executable $Inputs.workerExecutable
                foreach ($caseIdValue in @($scenario.caseIds)) {
                    if ($familyStopped) { break }
                    $caseId = [string]$caseIdValue
                    $sequence++
                    $request = New-ApplyWorkerRequest `
                        -PlanEvidence $PlanEvidence `
                        -RunId $runId `
                        -ScenarioId $scenarioId `
                        -CaseId $caseId `
                        -Role $role `
                        -ProjectPath $projectPath `
                        -WorkspaceRoot $Inputs.workspaceRoot `
                        -Sequence $sequence
                    $filesystemCaseBefore = Get-FilesystemSnapshot -Root $Inputs.workspaceRoot -MaxFiles $script:Budgets.maxCollectionItems
                    $sentUtc = [DateTime]::UtcNow
                    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
                    $result = $null
                    $transportOutcome = 'response'
                    $stopReason = $null
                    try {
                        $result = Invoke-ProbeWorker -Process $worker -Request $request -Timeout $TimeoutSeconds
                        $stopReason = Get-ApplyResultStopReason `
                            -Result $result `
                            -CaseId $caseId `
                            -CaseInstanceId ([string]$request.vciMutationProbe.caseInstanceId)
                        if ($null -ne $stopReason) { $transportOutcome = $stopReason }
                    }
                    catch {
                        $message = [string]$_.Exception.Message
                        $transportOutcome = switch ($message) {
                            'worker_request_timed_out' { 'timed_out'; break }
                            'worker_process_lost' { 'process_lost'; break }
                            default { 'protocol_error' }
                        }
                        $stopReason = $transportOutcome
                    }
                    $stopwatch.Stop()
                    $receivedUtc = [DateTime]::UtcNow
                    $filesystemCaseAfter = Get-FilesystemSnapshot -Root $Inputs.workspaceRoot -MaxFiles $script:Budgets.maxCollectionItems
                    if (-not [bool]$filesystemCaseBefore.complete -or -not [bool]$filesystemCaseAfter.complete) {
                        $transportOutcome = 'incomplete_evidence'
                        $stopReason = 'incomplete_filesystem_evidence'
                    }
                    $exitCode = if ($worker.HasExited) { $worker.ExitCode } else { $null }
                    $record = [ordered]@{
                        schemaVersion = 'vci-phase1-mutation-case-evidence/v1'
                        terminal = $true
                        sequence = $sequence
                        runId = $runId
                        scenarioId = $scenarioId
                        caseId = $caseId
                        caseInstanceId = [string]$request.vciMutationProbe.caseInstanceId
                        planHash = [string]$PlanEvidence.planHash
                        project = [ordered]@{ role = $role; projectPath = $projectPath; projectSha256 = Get-Sha256File -Path $projectPath }
                        transport = [ordered]@{
                            outcome = $transportOutcome
                            workerPid = $worker.Id
                            sentUtc = $sentUtc.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
                            receivedUtc = $receivedUtc.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
                            elapsedMilliseconds = $stopwatch.ElapsedMilliseconds
                            exitCode = $exitCode
                        }
                        filesystemBeforeSnapshotId = [string]$filesystemCaseBefore.snapshotId
                        filesystemAfterSnapshotId = [string]$filesystemCaseAfter.snapshotId
                        workerResult = $result
                        stopReason = $stopReason
                    }
                    $writer.WriteLine((ConvertTo-CompactJson -Value $record))
                    $records += $record
                    if ($null -ne $result) {
                        $snapshotBefore += [ordered]@{ scenarioId = $scenarioId; caseId = $caseId; snapshot = $result.before }
                        $snapshotAfter += [ordered]@{ scenarioId = $scenarioId; caseId = $caseId; snapshot = $result.after }
                    }
                    if ($null -ne $stopReason) {
                        $familyStopped = $true
                        $stoppedFamilies += [ordered]@{ scenarioId = $scenarioId; caseId = $caseId; reason = $stopReason }
                    }
                }
            }
            catch {
                $familyStopped = $true
                $stoppedFamilies += [ordered]@{ scenarioId = $scenarioId; caseId = $null; reason = 'worker_start_failed' }
            }
            finally { Stop-ProbeWorker -Process $worker }
        }
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }

    $filesystemAfter = Get-FilesystemSnapshot -Root $Inputs.workspaceRoot -MaxFiles $script:Budgets.maxCollectionItems
    Write-JsonFile -Path ([IO.Path]::Combine($Inputs.evidenceRoot, 'snapshot-before.json')) -Value ([ordered]@{
        schemaVersion = 'vci-phase1-mutation-snapshots/v1'; snapshots = $snapshotBefore
    })
    Write-JsonFile -Path ([IO.Path]::Combine($Inputs.evidenceRoot, 'snapshot-after.json')) -Value ([ordered]@{
        schemaVersion = 'vci-phase1-mutation-snapshots/v1'; snapshots = $snapshotAfter
    })
    Write-JsonFile -Path ([IO.Path]::Combine($Inputs.evidenceRoot, 'filesystem-after.json')) -Value $filesystemAfter
    $mismatches = @(Compare-EquivalentEvidence `
        -CurrentEvidenceRoot $Inputs.evidenceRoot `
        -CurrentManifestEvidence $manifestEvidence `
        -OtherEvidenceRoot $EquivalentEvidenceRoot)
    $plannedCount = @($PlanEvidence.canonicalPlan.scenarios | ForEach-Object { @($_.caseIds).Count } | Measure-Object -Sum).Sum
    $overallPass = $stoppedFamilies.Count -eq 0 -and
        $records.Count -eq $plannedCount -and
        [bool]$filesystemAfter.complete -and
        $mismatches.Count -eq 0
    $summary = [ordered]@{
        schemaVersion = 'vci-phase1-mutation-summary/v1'
        runId = $runId
        planHash = [string]$PlanEvidence.planHash
        workspaceRoot = $Inputs.workspaceRoot
        plannedCaseCount = $plannedCount
        requestedCaseCount = $records.Count
        stoppedFamilies = $stoppedFamilies
        normalizedMismatches = $mismatches
        filesystemEvidenceComplete = [bool]$filesystemAfter.complete
        overallPass = $overallPass
    }
    Write-JsonFile -Path ([IO.Path]::Combine($Inputs.evidenceRoot, 'summary.json')) -Value $summary
    return $summary
}

function Get-DescribeDocument {
    $parameters = @(
        [ordered]@{ name = 'Mode'; type = 'Describe|Inventory|Apply'; default = 'Describe' },
        [ordered]@{ name = 'ScenarioManifestPath'; type = 'absolute file path'; default = $null },
        [ordered]@{ name = 'WorkerExecutable'; type = 'absolute file path'; default = $null },
        [ordered]@{ name = 'EvidenceRoot'; type = 'absolute directory path'; default = $null },
        [ordered]@{ name = 'WorkspaceRoot'; type = 'absolute absent directory path'; default = $null },
        [ordered]@{ name = 'TimeoutSeconds'; type = 'integer 5..1800'; default = 240 },
        [ordered]@{ name = 'WorkerAccessMode'; type = 'read-write|read-only'; default = 'read-write' },
        [ordered]@{ name = 'AllowMutation'; type = 'switch'; default = $false },
        [ordered]@{ name = 'Acknowledgement'; type = 'exact text'; default = $null },
        [ordered]@{ name = 'ExpectedPlanHash'; type = 'SHA-256 hex'; default = $null },
        [ordered]@{ name = 'NonInteractiveAcceptance'; type = 'switch'; default = $false },
        [ordered]@{ name = 'EquivalentEvidenceRoot'; type = 'absolute completed evidence path'; default = $null }
    )
    return [ordered]@{
        schemaVersion = $script:HarnessSchema
        probeSchemaVersion = $script:ProbeSchema
        manifestSchemaVersion = $script:ManifestSchema
        inventorySchemaVersion = $script:InventorySchema
        planSchemaVersion = $script:PlanSchema
        workerOperation = 'probe_vci_mutation_contract'
        workerAccessMode = 'read-write'
        modes = @('Describe', 'Inventory', 'Apply')
        parameters = $parameters
        manifestProperties = @(
            'schemaVersion', 'originalProjectPath', 'lifecycleProjectPath', 'mappingProjectPath',
            'projectToWorkspaceChangedProjectPath', 'workspaceToProjectBaselineProjectPath',
            'negativeProjectPath', 'transactionProjectPath', 'selectedObject'
        )
        caseIds = $script:CaseIds
        scenarioOrder = $script:ScenarioOrder
        acknowledgement = $script:AcknowledgementText
        requiresSeparateLiveAuthorization = $true
        safetyRules = @(
            'Original project is never opened.',
            'Inventory invokes only P-INVENTORY and leaves the workspace root absent.',
            'Apply requires exact plan hash, acknowledgement, explicit mutation switch, and confirmation.',
            'No project persistence, archive, build, download, online, or commissioning operation is permitted.'
        )
        stopConditions = @(
            'Stop the affected scenario family after timeout, process loss, incomplete evidence, or uncertain mutation.',
            'Never invoke an uncertain request again automatically.'
        )
        retentionPolicy = 'Retain generated VCI files, evidence, and disposable project state; cleanup is not implemented.'
        inertStatement = 'Describe did not open or create any TIA process or filesystem path.'
    }
}

if ([string]::Equals($Mode, 'Describe', [StringComparison]::Ordinal)) {
    [Console]::Out.WriteLine((ConvertTo-CompactJson -Value (Get-DescribeDocument)))
    exit 0
}

$repositoryRoot = [IO.Directory]::GetParent($PSScriptRoot).FullName
$inputs = Resolve-CommonInputs -RepositoryRoot $repositoryRoot
if ([string]::Equals($Mode, 'Inventory', [StringComparison]::Ordinal)) {
    [Console]::Out.WriteLine((ConvertTo-CompactJson -Value (Invoke-Inventory -Inputs $inputs -RepositoryRoot $repositoryRoot)))
    exit 0
}

$plan = Assert-ApplyGuards -Inputs $inputs -RepositoryRoot $repositoryRoot
$applyResult = Invoke-Apply -Inputs $inputs -PlanEvidence $plan
[Console]::Out.WriteLine((ConvertTo-CompactJson -Value $applyResult))
if (-not [bool]$applyResult.overallPass) { exit 2 }
exit 0
