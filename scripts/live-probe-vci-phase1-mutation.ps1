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
    [string] $PlanHash,
    [switch] $NonInteractiveAcceptance
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
    [IO.File]::WriteAllText($Path, (ConvertTo-CompactJson -Value $Value), $utf8)
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
    $unsafe += @($ProjectPaths.PSObject.Properties | ForEach-Object { [IO.Path]::GetDirectoryName([string]$_.Value) })
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
    Write-JsonFile -Path ([IO.Path]::Combine($Inputs.evidenceRoot, 'inventory.json')) -Value $inventory
    Write-JsonFile -Path ([IO.Path]::Combine($Inputs.evidenceRoot, 'plan.json')) -Value $planEvidence
    return [ordered]@{
        schemaVersion = $script:InventorySchema
        inventoryPath = [IO.Path]::Combine($Inputs.evidenceRoot, 'inventory.json')
        planPath = [IO.Path]::Combine($Inputs.evidenceRoot, 'plan.json')
        planHash = $planHashValue
        selectedObject = 'Simulation_DB'
        resolvedProjectPaths = @($projects | ForEach-Object { $_.projectPath })
        resolvedWorkspacePaths = @($projects | ForEach-Object { $_.selectedWorkspaceRootPath })
        workspaceRoot = $Inputs.workspaceRoot
        workspaceRootExistsAfter = $false
    }
}

function Assert-ApplyGuards {
    param([Parameter(Mandatory)] [object] $Inputs)
    if (-not $AllowMutation) { throw 'allow_mutation_required' }
    if (-not [string]::Equals($Acknowledgement, $script:AcknowledgementText, [StringComparison]::Ordinal)) {
        throw 'acknowledgement_required'
    }
    if ([string]::IsNullOrWhiteSpace($PlanHash)) { throw 'plan_hash_required' }
    $planPath = [IO.Path]::Combine($Inputs.evidenceRoot, 'plan.json')
    if (-not [IO.File]::Exists($planPath)) { throw 'plan_not_found' }
    $planEvidence = Get-Content -LiteralPath $planPath -Raw | ConvertFrom-Json -Depth 100
    $canonicalJson = ConvertTo-CompactJson -Value $planEvidence.canonicalPlan
    $calculatedHash = Get-Sha256Text -Text $canonicalJson
    if (-not [string]::Equals([string]$planEvidence.planHash, $calculatedHash, [StringComparison]::Ordinal) -or
        -not [string]::Equals($PlanHash, $calculatedHash, [StringComparison]::Ordinal)) {
        throw 'plan_hash_mismatch'
    }
    if (-not [string]::Equals([string]$planEvidence.canonicalPlan.workspaceRoot, $Inputs.workspaceRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'plan_workspace_root_mismatch'
    }
    if (-not $NonInteractiveAcceptance) { throw 'interactive_confirmation_required' }
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
        [ordered]@{ name = 'PlanHash'; type = 'SHA-256 hex'; default = $null },
        [ordered]@{ name = 'NonInteractiveAcceptance'; type = 'switch'; default = $false }
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

Assert-ApplyGuards -Inputs $inputs
throw 'apply_not_implemented_until_task_8'
