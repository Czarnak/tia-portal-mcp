# VCI Workspace Phase 1 Mutation Probe Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the internal, guarded VCI mutation probe required by Gate 3 of the approved Phase 1 design, without publishing `workspace_read` or `workspace_write` and without executing a live mutation as part of implementation.

**Architecture:** Add a mutation-specific typed contract and worker operation beside the accepted read probe, reusing its selectors, bounded normalizers, and snapshot readers. A PowerShell harness exposes `Describe`, `Inventory`, and `Apply`; `Apply` is bound to exact disposable project copies, an absent run-specific workspace root, an ordered plan hash, an acknowledgement, and explicit mutation authorization. Each Siemens call is one closed-catalogue worker request, uses `ExclusiveAccess` and a `Transaction` where the installed API permits it, flushes terminal evidence immediately, never saves a project, never retries an uncertain mutation, and stops the affected scenario family on uncertainty.

**Tech Stack:** C# 12 / .NET 8 host and tests, C# / .NET Framework 4.8 Openness worker, Siemens TIA Portal V21 Openness VCI, PowerShell 7, xUnit, and the existing reference-stub build.

## Global Constraints

- Work on the current `feature/workspace-operations` branch. Do not create or switch branches or worktrees.
- Preserve unrelated work. Do not stage, commit, push, or open a pull request without a separate user request.
- Follow strict TDD for every behavior change: add one focused failing test, run it and confirm the expected failure, add the smallest production change, then rerun to green.
- This plan implements Gate 3 only. It does not execute Gate 4 live mutation acceptance.
- Add no public MCP tool, public schema, or host-side `workspace_read` / `workspace_write` behavior.
- Keep the mutation worker operation internal and classify it as `OperationCapability.ProjectMutation`; it must be denied in read-only mode.
- Never open the original project. Every state-dependent scenario uses a user-supplied disposable `.ap21` copy, and all scenario paths must be distinct.
- Never call `Project.Save`, `SaveAs`, archive, compile, download, go online, or commission.
- Keep scope strictly to VCI. Do not add Git, indexing, search, rich diffing, Multiuser/project-server, Teamcenter, Add-In repository workflows, or arbitrary project/file editors.
- Use only fixed case IDs and typed request fields. Do not expose reflection, arbitrary method names, arbitrary property names, scripts, or caller-defined call sequences.
- Use one worker request per case so completed evidence survives a later worker/TIA loss.
- Never automatically retry a mutation after timeout, process loss, transport ambiguity, incomplete filesystem evidence, or an uncertain Siemens outcome.
- Every destructive Openness call runs under `ExclusiveAccess` and a `Transaction` when the operation is accepted there. Transaction rejection is evidence; do not silently fall back to exclusive-only execution.
- Call `CommitOnDispose()` only after the case call, post-call state read, filesystem-boundary checks, and canary all succeed.
- Transaction-characterization cases deliberately omit `CommitOnDispose()` and then verify rollback plus external filesystem effects.
- Record `Project.IsModified`; never reset it and never claim persistence across project close because the probe does not save.
- `Describe` is the default and must not open TIA Portal or create directories.
- `Inventory` may attach and read the disposable copies, but must not invoke a mutating VCI method or create the workspace root.
- `Apply` requires `-AllowMutation`, the exact acknowledgement `I_UNDERSTAND_VCI_MUTATES_DISPOSABLE_PROJECTS_AND_WORKSPACE_FILES`, the displayed plan hash, and interactive confirmation unless `-NonInteractiveAcceptance` is also explicitly supplied.
- The run-specific workspace root must not exist before `Apply`; `Apply` creates it only after every guard and confirmation passes.
- Reject drive roots, profile roots, repository roots, project directories, existing unrelated directories, path traversal, alternate data streams, and reparse-point/symlink escapes.
- Retain generated files and disposable project state by default. Cleanup is a separate future action and is not implemented by this plan.
- Build serially. Use `dotnet build TiaMcpServer.sln --no-restore -m:1 /p:UseTiaPortalReferenceStubs=true` for the vendor-free gate.
- Distinguish vendor-free tests, stub compilation, installed-reference compilation, and separately authorized live evidence in every report.

---

## Evidence-Locked Fixture and Scenario Strategy

The accepted read-only evidence bundle is `artifacts/live-vci-phase1/20260809T072541470Z-d49e4677/`.

The implementation is locked to this Gate 2 evidence:

- overall pass, complete evidence, unchanged project state, unchanged filesystem state, healthy canaries, and no normalized mismatch across two fresh worker sessions;
- zero child groups, two workspaces, 48 mappings, and 42 distinct workspace/object format queries per session;
- mapped object `Simulation_DB`, structural leaf `GlobalDB`, below `ET 200SP station_1 / PLC_1 / Program blocks`;
- a stable V21 object identifier and a structural selector/fingerprint were observed for that object;
- `GetSupportedFileFormats(Simulation_DB)` returned `DB`, `SimaticML`, and `SimaticSD` in both sessions; and
- the accepted mappings already use `SimaticML`, so `SimaticML` is the selected mutation format.

Do not commit the machine-specific stable identifier, existing workspace root, or original project path. `Inventory` must rediscover the selected object in every disposable copy using this typed structural requirement:

```json
{
  "structuralPath": [
    { "index": 0, "name": "ET 200SP station_1", "objectType": "Device" },
    { "index": 0, "name": "PLC_1", "objectType": "PlcSoftware" },
    { "index": 0, "name": "Program blocks", "objectType": "BlockFolder" },
    { "index": 1, "name": "Simulation_DB", "objectType": "GlobalDB" }
  ],
  "requiredFormat": "SimaticML"
}
```

The probe does not hand-edit SimaticML. The user supplies one disposable project copy whose `Simulation_DB` contains a controlled project-side change and another baseline copy. `Apply` uses VCI to export both objects into separate confined roots and compares their complete file inventories and hashes. If they are identical, `M-P2W` and `M-W2P` become `not_observable` and the synchronization family stops. If they differ, the changed project's VCI-produced files are the only accepted workspace-side change used by `M-W2P`.

The strict scenario manifest schema is:

```json
{
  "schemaVersion": "vci-phase1-mutation-scenarios/v1",
  "originalProjectPath": "C:\\Projects\\Original\\Project.ap21",
  "lifecycleProjectPath": "C:\\Projects\\Disposable\\Lifecycle.ap21",
  "mappingProjectPath": "C:\\Projects\\Disposable\\Mapping.ap21",
  "projectToWorkspaceChangedProjectPath": "C:\\Projects\\Disposable\\Changed.ap21",
  "workspaceToProjectBaselineProjectPath": "C:\\Projects\\Disposable\\Baseline.ap21",
  "negativeProjectPath": "C:\\Projects\\Disposable\\Negative.ap21",
  "transactionProjectPath": "C:\\Projects\\Disposable\\Transaction.ap21",
  "selectedObject": {
    "structuralPath": [
      { "index": 0, "name": "ET 200SP station_1", "objectType": "Device" },
      { "index": 0, "name": "PLC_1", "objectType": "PlcSoftware" },
      { "index": 0, "name": "Program blocks", "objectType": "BlockFolder" },
      { "index": 1, "name": "Simulation_DB", "objectType": "GlobalDB" }
    ],
    "requiredFormat": "SimaticML"
  }
}
```

All six disposable paths must be canonical, existing files, pairwise distinct, and different from `originalProjectPath`. `Inventory` must resolve the selected object and the exact-case-sensitive `SimaticML` format in all six copies before it emits an apply plan.

---

## Locked Mutation Case Vocabulary

The mutation contract defines the following exact case IDs. Case availability remains precondition-driven; an unavailable precondition returns `not_observable` with a closed reason rather than fabricating an invocation.

### Inventory and canary

- `P-INVENTORY`
- `M-CANARY`

### Positive and rollback-characterization cases

- `M-GROUP`
- `M-WORKSPACE-ROOT`
- `M-WORKSPACE-LANGUAGE`
- `M-EXPORT`
- `M-DISCONNECT`
- `M-CONNECT`
- `M-P2W`
- `M-W2P`
- `M-DELETE-MAPPING`
- `M-DELETE-WORKSPACE`
- `M-DELETE-GROUP`
- `M-TX-GROUP`
- `M-TX-WORKSPACE`
- `M-TX-EXPORT`
- `M-TX-CONNECT`
- `M-TX-P2W`
- `M-TX-W2P`
- `M-TX-DISCONNECT`
- `M-TX-DELETE-WORKSPACE`
- `M-TX-DELETE-GROUP`

### Group and workspace input negatives

- `N-GROUP-NULL`
- `N-GROUP-EMPTY`
- `N-GROUP-WHITESPACE`
- `N-GROUP-DUPLICATE`
- `N-GROUP-INVALID`
- `N-WORKSPACE-NULL`
- `N-WORKSPACE-EMPTY`
- `N-WORKSPACE-WHITESPACE`
- `N-WORKSPACE-DUPLICATE`
- `N-WORKSPACE-INVALID`
- `N-WORKSPACE-PATH-RELATIVE`
- `N-WORKSPACE-PATH-MISSING-PARENT`
- `N-WORKSPACE-PATH-CONFLICT`
- `N-WORKSPACE-PATH-FILE`
- `N-WORKSPACE-LANGUAGE-NULL`
- `N-WORKSPACE-LANGUAGE-INVALID`
- `N-WORKSPACE-GLOBAL-LIBRARY-NULL`
- `N-WORKSPACE-GLOBAL-LIBRARY-INVALID`

### Object, format, filename, and connection negatives

- `N-OBJECT-NULL`
- `N-OBJECT-UNSUPPORTED`
- `N-OBJECT-FOREIGN`
- `N-OBJECT-DISPOSED`
- `N-OBJECT-ALREADY-MAPPED`
- `N-OBJECT-DELETED`
- `N-FORMAT-NULL`
- `N-FORMAT-EMPTY`
- `N-FORMAT-UNSUPPORTED`
- `N-FORMAT-WRONG-CASE`
- `N-FORMAT-MISMATCH`
- `N-FILENAME-INVALID`
- `N-FILENAME-ABSOLUTE`
- `N-FILENAME-TRAVERSAL`
- `N-FILENAME-COLLISION`
- `N-CONNECT-MISSING`
- `N-CONNECT-MALFORMED`
- `N-CONNECT-WRONG-OBJECT`
- `N-CONNECT-PARTIAL-FILE-SET`

### Synchronization and deletion negatives

- `N-SYNC-MISSING`
- `N-SYNC-MALFORMED`
- `N-SYNC-UNCHANGED`
- `N-SYNC-PROJECT-ONLY`
- `N-SYNC-WORKSPACE-ONLY`
- `N-SYNC-BOTH-SIDES`
- `N-SYNC-INVALID-ENUM`
- `N-DELETE-NONEMPTY`
- `N-DELETE-TWICE`
- `N-STALE-MAPPING-PROXY`

Harness-only boundary rejections use the same case IDs for relative/absolute/traversal paths and filenames, but record `invocationLayer: harness_confinement` and prove that no worker request was sent. Multi-file cases run only when `M-EXPORT` proves more than one generated file; otherwise they return `not_observable` with `selected_format_is_single_file`.

---

## File Map

**Create:**

- `TiaMcpServer.Contracts/VciMutationProbeContract.cs` — closed case/outcome/reason vocabularies and semantic validation.
- `TiaMcpServer.Contracts/VciMutationProbeRequestInfo.cs` — typed one-case request and explicit mutation inputs.
- `TiaMcpServer.Contracts/VciMutationProbeResultInfo.cs` — mutation result, before/after state, transaction, canary, and sanitized-argument evidence.
- `TiaMcpServer.OpennessWorker/Openness/VciMutationProbeJsonBoundary.cs` — strict probe-only raw JSON validator.
- `TiaMcpServer.OpennessWorker/Openness/VciMutationPathPolicy.cs` — pure canonical-path and relative-path confinement rules.
- `TiaMcpServer.OpennessWorker/Openness/VciMutationContractProbeService.cs` — fixed case dispatcher and VCI mutations.
- `TiaMcpServer.Tests/Workspace/VciMutationProbeContractTests.cs`
- `TiaMcpServer.Tests/Workspace/VciMutationProbeJsonBoundaryTests.cs`
- `TiaMcpServer.Tests/Workspace/VciMutationProbeAccessPolicyTests.cs`
- `TiaMcpServer.Tests/Workspace/VciMutationPathPolicyTests.cs`
- `TiaMcpServer.Tests/Workspace/VciMutationProbeWorkerSourceContractTests.cs`
- `TiaMcpServer.Tests/Workspace/VciMutationProbeScriptTests.cs`
- `scripts/live-probe-vci-phase1-mutation.ps1`

**Modify:**

- `TiaMcpServer.Contracts/WorkerRequest.cs` — add only `VciMutationProbeRequestInfo? VciMutationProbe`.
- `TiaMcpServer.Contracts/OperationPolicyCatalog.cs` — register `probe_vci_mutation_contract` as `ProjectMutation`.
- `TiaMcpServer.OpennessWorker/Program.cs` — validate the raw mutation envelope and dispatch the internal handler.
- `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` — link only the Siemens-free mutation JSON boundary and path policy; contracts arrive through the existing project reference, and the Siemens-dependent service remains worker-only.
- `docs/superpowers/README.md` — index this plan now and the later acceptance report only after Gate 4 succeeds.

Do not modify the public MCP tool registrations, public tool counts, `README.md` tool table, or current Supported Operations pages during Gate 3.

---

## Task 1: Lock the Mutation Contract and Evidence-Backed Fixture

**Files:**

- Create: `TiaMcpServer.Contracts/VciMutationProbeContract.cs`
- Create: `TiaMcpServer.Contracts/VciMutationProbeRequestInfo.cs`
- Create: `TiaMcpServer.Contracts/VciMutationProbeResultInfo.cs`
- Create: `TiaMcpServer.Tests/Workspace/VciMutationProbeContractTests.cs`
- Modify: `TiaMcpServer.Contracts/WorkerRequest.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`

**Interfaces:**

- Consumes: existing `VciWorkspaceSelectorInfo`, `VciEngineeringObjectSelectorInfo`, `VciMappingSelectorInfo`, `VciProbeReturnInfo`, `VciProbeExceptionInfo`, `VciProbeProjectStateInfo`, and `VciProbeSnapshotInfo`.
- Produces: `VciMutationProbeContract.OperationName`, `SchemaVersion`, `CaseIds`, `Outcomes`, `NotObservableReasons`, `Validate(VciMutationProbeRequestInfo)`; `WorkerRequest.VciMutationProbe`.

- [x] **Step 1.1: Write the failing closed-vocabulary and DTO-shape tests**

The production break caught by these tests is adding an arbitrary mutation route, silently dropping a typed input, or changing the wire vocabulary without review.

```csharp
[Fact]
public void MutationContract_LocksOperationSchemaAndCaseVocabulary()
{
    Assert.Equal("probe_vci_mutation_contract", VciMutationProbeContract.OperationName);
    Assert.Equal("vci-mutation-probe/v1", VciMutationProbeContract.SchemaVersion);
    Assert.Equal(ExpectedCaseIds.OrderBy(x => x, StringComparer.Ordinal),
        VciMutationProbeContract.CaseIds.OrderBy(x => x, StringComparer.Ordinal));
}

[Fact]
public void WorkerRequest_CarriesOneTypedMutationProbeEnvelope()
{
    Assert.Equal(typeof(VciMutationProbeRequestInfo),
        typeof(WorkerRequest).GetProperty(nameof(WorkerRequest.VciMutationProbe))!.PropertyType);
}
```

- [x] **Step 1.2: Run the focused test and verify RED**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter FullyQualifiedName~VciMutationProbeContractTests
```

Expected: compilation fails because the mutation contract and DTOs do not exist.

- [x] **Step 1.3: Add the smallest typed contract**

Use this request surface; cases ignore fields not named in their validation rule and the JSON boundary rejects unknown fields:

```csharp
public sealed class VciMutationProbeRequestInfo
{
    public string SchemaVersion { get; set; } = VciMutationProbeContract.SchemaVersion;
    public string RunId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ScenarioId { get; set; } = string.Empty;
    public string CaseId { get; set; } = string.Empty;
    public string CaseInstanceId { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty; // Inventory or Apply
    public string WorkspaceRoot { get; set; } = string.Empty;
    public string? GroupName { get; set; }
    public string? NestedGroupName { get; set; }
    public string? WorkspaceName { get; set; }
    public string? WorkspaceLanguage { get; set; }
    public VciWorkspaceSelectorInfo? Workspace { get; set; }
    public VciEngineeringObjectSelectorInfo? EngineeringObject { get; set; }
    public VciMappingSelectorInfo? Mapping { get; set; }
    public string? RelativeDirectory { get; set; }
    public string? FileName { get; set; }
    public string? FileFormat { get; set; }
    public string? SeedRelativePath { get; set; }
    public string? SynchronizationMode { get; set; }
    public bool RollbackTransaction { get; set; }
    public int MaxGroupDepth { get; set; } = 16;
    public int MaxGroups { get; set; } = 500;
    public int MaxWorkspaces { get; set; } = 500;
    public int MaxMappings { get; set; } = 5000;
    public int MaxEngineeringObjects { get; set; } = 200;
    public int MaxCollectionItems { get; set; } = 5000;
}
```

Use these exact result types; ordered argument/check lists avoid dictionary-order drift in normalized evidence:

```csharp
public sealed class VciMutationProbeCaseResultInfo
{
    public string SchemaVersion { get; set; } = VciMutationProbeContract.SchemaVersion;
    public string RunId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ScenarioId { get; set; } = string.Empty;
    public string CaseId { get; set; } = string.Empty;
    public string CaseInstanceId { get; set; } = string.Empty;
    public string InvocationLayer { get; set; } = string.Empty;
    public string InputCategory { get; set; } = string.Empty;
    public List<VciMutationArgumentInfo> SanitizedArguments { get; set; } = new();
    public List<VciMutationCheckInfo> Preconditions { get; set; } = new();
    public List<VciMutationCheckInfo> SafetyInvariants { get; set; } = new();
    public string Outcome { get; set; } = string.Empty;
    public VciProbeReturnInfo? Return { get; set; }
    public VciProbeExceptionInfo? Exception { get; set; }
    public VciProbeSnapshotInfo? Before { get; set; }
    public VciProbeSnapshotInfo? After { get; set; }
    public VciProbeProjectStateInfo ProjectState { get; set; } = new();
    public VciMutationTransactionInfo Transaction { get; set; } = new();
    public VciMutationCanaryInfo Canary { get; set; } = new();
    public bool UncertainOutcome { get; set; }
    public bool StopScenarioFamily { get; set; }
    public string? NotObservableReason { get; set; }
    public List<VciProbeOmissionInfo> Omissions { get; set; } = new();
}

public sealed class VciMutationArgumentInfo
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Value { get; set; }
}

public sealed class VciMutationCheckInfo
{
    public string Name { get; set; } = string.Empty;
    public bool Satisfied { get; set; }
    public string? Detail { get; set; }
}

public sealed class VciMutationTransactionInfo
{
    public bool Requested { get; set; }
    public bool Started { get; set; }
    public bool CommitRequested { get; set; }
    public bool CanCommitBeforeDispose { get; set; }
    public bool Disposed { get; set; }
}

public sealed class VciMutationCanaryInfo
{
    public bool Attempted { get; set; }
    public bool Usable { get; set; }
    public string Outcome { get; set; } = string.Empty;
}
```

Do not reuse `VciProbeCaseResultInfo` as the top-level envelope because its schema version and comments are read-probe-specific.

- [x] **Step 1.4: Add per-case semantic validation tests**

Cover exact schema/mode/identifier requirements, positive budgets, absolute workspace root, selector requirements, exact-case `SimaticML`, fixed synchronization values, and explicit-null cases. Use table-driven literal expected messages.

- [x] **Step 1.5: Implement validation and rerun GREEN**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter FullyQualifiedName~VciMutationProbeContractTests
```

Expected: all mutation contract tests pass.

**Review gate:** inspect the DTO for any method/property/call-sequence escape hatch. Reject the task if a caller can select a Siemens member not implied by `CaseId`.

---

## Task 2: Enforce JSON, Access-Mode, and Public-Surface Boundaries

**Files:**

- Create: `TiaMcpServer.OpennessWorker/Openness/VciMutationProbeJsonBoundary.cs`
- Create: `TiaMcpServer.Tests/Workspace/VciMutationProbeJsonBoundaryTests.cs`
- Create: `TiaMcpServer.Tests/Workspace/VciMutationProbeAccessPolicyTests.cs`
- Modify: `TiaMcpServer.Contracts/OperationPolicyCatalog.cs`

**Interfaces:**

- Consumes: `VciMutationProbeContract.Validate` and `OperationCapability.ProjectMutation`.
- Produces: `VciMutationProbeJsonBoundary.Validate(string)` and the internal operation's access classification. Worker dispatch is deliberately deferred to Task 4 so every intermediate checkpoint remains compilable.

- [x] **Step 2.1: Write access-policy RED tests**

```csharp
[Fact]
public void MutationProbe_IsDeniedInReadOnlyModeAndAllowedInReadWriteMode()
{
    Assert.NotNull(new OperationAccessPolicy(McpAccessMode.ReadOnly)
        .Authorize(VciMutationProbeContract.OperationName));
    Assert.Null(new OperationAccessPolicy(McpAccessMode.ReadWrite)
        .Authorize(VciMutationProbeContract.OperationName));
    Assert.Equal(OperationCapability.ProjectMutation,
        OperationPolicyCatalog.GetCapability(VciMutationProbeContract.OperationName));
}
```

- [x] **Step 2.2: Write strict JSON RED tests**

Test non-object `vciMutationProbe`, missing probe, unknown root/probe/nested-selector fields, duplicate fields differing only by case, wrong JSON types, out-of-range integers, write flags, unknown cases, and wrong schema. Confirm non-mutation methods remain unaffected.

- [x] **Step 2.3: Write public-surface guard tests**

Assert that the internal operation does not add a public tool registration or change the expected four read-only / fourteen read-write public tool counts. Dispatch tests begin in Task 4 together with the mutation service they exercise.

- [x] **Step 2.4: Run focused tests and verify RED**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~VciMutationProbeJsonBoundaryTests|FullyQualifiedName~VciMutationProbeAccessPolicyTests"
```

Expected: failures for the missing mutation boundary and policy entry.

- [x] **Step 2.5: Add the minimal policy and strict JSON boundary**

- [x] **Step 2.6: Rerun GREEN and the existing read-probe boundary suite**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~VciMutationProbe|FullyQualifiedName~VciReadProbe|FullyQualifiedName~VciProbeJsonBoundary"
```

Expected: all focused tests pass; existing read-probe behavior is unchanged.

---

## Task 3: Implement Pure Workspace Confinement and Fail-Closed Preconditions

**Files:**

- Create: `TiaMcpServer.OpennessWorker/Openness/VciMutationPathPolicy.cs`
- Create: `TiaMcpServer.Tests/Workspace/VciMutationPathPolicyTests.cs`

**Interfaces:**

- Consumes: raw workspace root, repository root, project paths, user profile, relative directory, filename, and seed path.
- Produces: `VciMutationPathValidationResult ValidateWorkspaceRoot(...)`, `ResolveRelativeDirectory(...)`, `ResolveFile(...)`, and deterministic rejection categories.

- [x] **Step 3.1: Write path-policy RED tests**

Name the production breaks: accepting a path outside the run root, accepting a reparse escape, treating case-only Windows path differences inconsistently, or allowing the workspace root to overlap a protected directory.

Cover drive root, profile root, repository root, project directory, existing nonempty root, `..`, rooted child, UNC/device path, alternate data stream, invalid filename, file-valued directory, missing parent, and an existing ancestor marked as a reparse point. Use a temporary test tree and literal expected categories.

- [x] **Step 3.2: Run focused tests and verify RED**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter FullyQualifiedName~VciMutationPathPolicyTests
```

- [x] **Step 3.3: Implement canonical containment without filesystem mutation**

Use `Path.GetFullPath`, separator-normalized prefix checks with `StringComparison.OrdinalIgnoreCase`, and explicit component traversal. Never infer safety from string prefix alone. `Inventory` validation must accept only a nonexistent run root whose existing parent chain contains no reparse point.

- [x] **Step 3.4: Rerun GREEN and mutation contract tests**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~VciMutationPathPolicyTests|FullyQualifiedName~VciMutationProbeContractTests"
```

---

## Task 4: Implement Inventory, Canary, Group, Workspace, and Delete Cases

**Files:**

- Create: `TiaMcpServer.OpennessWorker/Openness/VciMutationContractProbeService.cs`
- Create: `TiaMcpServer.Tests/Workspace/VciMutationProbeWorkerSourceContractTests.cs`
- Modify: `TiaMcpServer.OpennessWorker/Program.cs`
- Modify: `TiaMcpServer.Tests/Workspace/VciMutationProbeWorkerSourceContractTests.cs`

**Interfaces:**

- Consumes: read-probe selector resolver/snapshot reader/value normalizer, `VersionControlInterface`, `WorkspaceGroup`, `ExclusiveAccess`, and `Transaction`.
- Produces: `public static VciMutationProbeCaseResultInfo Execute(TiaPortal currentPortal, Project project, VciMutationProbeRequestInfo request)` plus fixed dispatch for `P-INVENTORY`, `M-CANARY`, group/workspace lifecycle, delete, and corresponding negative cases.

The `Program.cs` handler shape introduced with the service is fixed:

```csharp
private static WorkerResponse ProbeVciMutationContract(WorkerRequest request)
{
    if (request.VciMutationProbe is null)
    {
        throw new WorkerOperationException(
            WorkerFailureCategories.ValidationError,
            "'vciMutationProbe' is required for probe_vci_mutation_contract.");
    }

    var validationError = VciMutationProbeContract.Validate(request.VciMutationProbe);
    if (validationError is not null)
    {
        throw new WorkerOperationException(
            WorkerFailureCategories.ValidationError,
            validationError);
    }

    return WithProject(request, (tiaPortal, project) =>
        Success(VciMutationContractProbeService.Execute(
            tiaPortal, project, request.VciMutationProbe)));
}
```

- [ ] **Step 4.1: Write worker-source and dispatcher RED tests**

Assert raw mutation-envelope validation before normal deserialization, one internal dispatch entry and private handler, one switch arm per locked case, rejection of an unknown case, no reflection/dynamic invocation, `Project.Save` absence, `ExclusiveAccess` around every Apply mutation, and `CommitOnDispose()` after post-state/canary validation only.

- [ ] **Step 4.2: Run focused tests and verify RED**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter FullyQualifiedName~VciMutationProbeWorkerSourceContractTests
```

- [ ] **Step 4.3: Implement `P-INVENTORY` and `M-CANARY` first**

`P-INVENTORY` resolves `Simulation_DB`, calls `GetSupportedFileFormats`, requires exact `SimaticML`, records current groups/workspaces/mappings, and verifies the workspace root remains nonexistent. `M-CANARY` reads `Project.IsModified`, VCI service/root, scenario-created group/workspace/mapping counts, and root confinement without mutation.

- [ ] **Step 4.4: Implement group/workspace lifecycle cases**

Use exact run-derived names such as `CodexVci_<run-short>` and `CodexVci_<run-short>_Nested`; never accept arbitrary names outside the typed request. Positive cases create a top-level group, nested group, explicit-root workspace, and explicit-root-plus-`en-US` workspace, then read back runtime types, ordering, counts, language, root, and project state.

- [ ] **Step 4.5: Implement group/workspace negative cases**

Invoke only signature-compatible null/empty/whitespace/duplicate/invalid values. Harness-confined path cases do not reach this service. Signature-incompatible cases return `not_observable` with `signature_does_not_permit_argument`.

- [ ] **Step 4.6: Implement dependency-safe deletes and stale-proxy evidence**

Delete mappings before workspaces and child groups before parents. Each `.Delete()` must remain within the active exclusive/transaction scope. `N-DELETE-TWICE` and `N-STALE-MAPPING-PROXY` preserve the deleted proxy only long enough for the immediate typed observation; never use it as a later selector.

- [ ] **Step 4.7: Rerun GREEN**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter FullyQualifiedName~VciMutationProbeWorkerSourceContractTests
```

---

## Task 5: Implement Export, Disconnect, Connect, and File-Set Evidence

**Files:**

- Modify: `TiaMcpServer.OpennessWorker/Openness/VciMutationContractProbeService.cs`
- Modify: `TiaMcpServer.Tests/Workspace/VciMutationProbeWorkerSourceContractTests.cs`

**Interfaces:**

- Consumes: resolved `Simulation_DB`, exact `SimaticML`, scenario-created workspace, confined directory/filename, and retained VCI-produced files.
- Produces: `M-EXPORT`, `M-DISCONNECT`, `M-CONNECT`, object/format/filename/connect negatives, mapping before/after snapshots, and external file observations.

- [ ] **Step 5.1: Add failing export/connect/disconnect source contracts**

Assert the order `GetSupportedFileFormats` → exact case-sensitive membership check → resolve confined target → `ExportObject`/`ConnectObject` → rediscover mapping → `GetStatus`/`GetChildStatus` → canary. Assert no raw caller path reaches `DirectoryInfo` or `FileInfo` before `VciMutationPathPolicy` succeeds.

- [ ] **Step 5.2: Verify RED**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter FullyQualifiedName~VciMutationProbeWorkerSourceContractTests
```

- [ ] **Step 5.3: Implement `M-EXPORT`**

Export `Simulation_DB` as `SimaticML` to `<workspaceRoot>/mapping/export/Simulation_DB`. Capture the method return normalization, mapping selector, status property, `GetStatus()`, `GetChildStatus()`, and a worker-side bounded file list. The harness remains authoritative for complete before/after SHA-256 inventory.

- [ ] **Step 5.4: Implement `M-DISCONNECT` and `M-CONNECT`**

`M-DISCONNECT` calls the installed `MappedObject.Delete()` inside exclusive/transaction scope and independently records whether files remain. `M-CONNECT` reconnects only the retained `M-EXPORT` file set and verifies the mapping/status. Do not infer file deletion from mapping deletion.

- [ ] **Step 5.5: Implement exact negative cases**

Use typed cases for null/unsupported/foreign/disposed/already-mapped/deleted objects; null/empty/unsupported/wrong-case/mismatched formats; invalid/collision filenames; and missing/malformed/wrong-object/partial connect content. Boundary escapes are harness rejections. Partial-file cases are `not_observable` unless export proves a multi-file set.

- [ ] **Step 5.6: Rerun GREEN**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~VciMutationProbeWorkerSourceContractTests|FullyQualifiedName~VciMutationPathPolicyTests"
```

---

## Task 6: Implement Bidirectional Synchronization and Transaction Characterization

**Files:**

- Modify: `TiaMcpServer.OpennessWorker/Openness/VciMutationContractProbeService.cs`
- Modify: `TiaMcpServer.Tests/Workspace/VciMutationProbeWorkerSourceContractTests.cs`

**Interfaces:**

- Consumes: baseline and changed VCI-produced SimaticML sets, connected mappings, exact `SynchronizationMode.ProjectToWorkspace` / `WorkspaceToProject` mapping, and rollback-only transaction flag.
- Produces: `M-P2W`, `M-W2P`, `N-SYNC-*`, and `M-TX-*` evidence without automatic retry.

- [ ] **Step 6.1: Add failing synchronization source contracts**

Assert `Synchronize()` is treated as `void`, post-status is queried separately, invalid enum values are created only by explicit cast in `N-SYNC-INVALID-ENUM`, and every sync result records before/after project plus filesystem state.

- [ ] **Step 6.2: Verify RED**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter FullyQualifiedName~VciMutationProbeWorkerSourceContractTests
```

- [ ] **Step 6.3: Implement `M-P2W` using the changed disposable copy**

Connect the baseline VCI-produced file set to the changed project's `Simulation_DB`, require a project-only difference observation, call `Synchronize(ProjectToWorkspace)`, then compare complete file hashes and status. If baseline and changed exports are identical or the expected project-only state cannot be established, return `not_observable` and stop the synchronization family.

- [ ] **Step 6.4: Implement `M-W2P` using the baseline disposable copy**

Connect the retained changed file set to the baseline project's `Simulation_DB`, require a workspace-only difference observation, call `Synchronize(WorkspaceToProject)`, then verify mapping status and independently export the project object to a verification directory. The verification export must hash-match the changed VCI-produced set after normalization; no compile or save is run.

- [ ] **Step 6.5: Implement synchronization negatives**

Create only confined states from VCI-produced files: missing, malformed, unchanged, project-only, workspace-only, both-sides-changed, and invalid enum. Record `threw` as a successful probe observation when the service captures a complete typed exception plus healthy canary.

- [ ] **Step 6.6: Implement rollback-only `M-TX-*` cases**

For each representative mutation, open `ExclusiveAccess`, open `Transaction`, invoke exactly one VCI mutation, capture state, omit `CommitOnDispose()`, dispose, then reacquire and record project/VCI/filesystem state. Never treat project rollback as proof that external files rolled back.

- [ ] **Step 6.7: Rerun GREEN**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter FullyQualifiedName~VciMutationProbeWorkerSourceContractTests
```

---

## Task 7: Build the `Describe` and `Inventory` Harness Gates

**Files:**

- Create: `scripts/live-probe-vci-phase1-mutation.ps1`
- Create: `TiaMcpServer.Tests/Workspace/VciMutationProbeScriptTests.cs`

**Interfaces:**

- Consumes: strict scenario manifest, worker executable, evidence root, workspace root, timeout, mutation switches, and acknowledgement.
- Produces: deterministic Describe document, mutation-free Inventory document, ordered plan, and SHA-256 plan hash.

- [ ] **Step 7.1: Write failing script tests**

Run the script rather than grepping only its text. Test default `Describe`, strict manifest rejection, missing/duplicate/original-equal project paths, unsafe workspace roots, missing worker, read-only worker mode, absent acknowledgement, plan-hash mismatch, and `Inventory` with a scripted FakeWorker. Add a source contract only for forbidden commands such as `Project.Save` and automatic retry loops.

- [ ] **Step 7.2: Verify RED**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter FullyQualifiedName~VciMutationProbeScriptTests
```

- [ ] **Step 7.3: Implement default `Describe`**

`Describe` prints schema versions, exact parameters, manifest schema, case IDs, scenario order, acknowledgement, safety rules, stop conditions, retention policy, and the statement that no TIA process or filesystem path was opened/created.

- [ ] **Step 7.4: Implement strict `Inventory`**

Validate all project/root invariants, start a read-write worker, invoke only `P-INVENTORY` once per disposable copy, resolve `Simulation_DB`/`SimaticML`, display selected object and all resolved paths, and write `inventory.json` plus `plan.json`. Inventory must prove the root still does not exist afterward.

- [ ] **Step 7.5: Canonicalize and hash the exact plan**

The hash input includes schema, Git/worker/script hashes, project identities/hashes, selected object structural path, format, workspace root, ordered scenario/case sequence, expected preconditions, acknowledgement text, and all budgets. Absolute paths remain local evidence and are never copied into committed reports.

- [ ] **Step 7.6: Rerun GREEN**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter FullyQualifiedName~VciMutationProbeScriptTests
```

---

## Task 8: Implement `Apply`, Immediate Evidence, Stop Rules, and Repeatability

**Files:**

- Modify: `scripts/live-probe-vci-phase1-mutation.ps1`
- Modify: `TiaMcpServer.Tests/Workspace/VciMutationProbeScriptTests.cs`

**Interfaces:**

- Consumes: accepted Inventory plan and hash, explicit switches/acknowledgement, and scenario-specific project copies.
- Produces: immutable evidence bundle under `artifacts/live-vci-phase1-mutation/<run-id>/`.

- [ ] **Step 8.1: Add failing apply/evidence tests with FakeWorker scripts**

Cover ordered requests, one request per case, flush-after-record, case timeout, process loss, malformed payload, incomplete pre/post snapshot, uncertain mutation, worker restart prohibition, no retry, family stop, later-family continuation only when independent, exact canary placement, and normalized equivalent-run comparison.

- [ ] **Step 8.2: Verify RED**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter FullyQualifiedName~VciMutationProbeScriptTests
```

- [ ] **Step 8.3: Implement guarded root creation and apply confirmation**

Immediately before creation, repeat every root/project/hash/plan guard. Create only the absent run root, write a run marker containing the run ID and plan hash, then start the first worker. If confirmation is declined, remove only the just-created empty marked root and report no TIA invocation.

- [ ] **Step 8.4: Implement per-case evidence**

Flush one terminal JSONL record after every worker response. The record contains the typed worker result plus transport sequence, worker PID, sent/received UTC provenance, elapsed time, exit code, resolved disposable project identity, plan hash, and harness-level pre/post filesystem snapshot IDs.

- [ ] **Step 8.5: Implement the full evidence bundle**

Write atomically:

- `manifest.json`
- `inventory.json`
- `plan.json`
- `cases.jsonl`
- `snapshot-before.json`
- `snapshot-after.json`
- `filesystem-before.json`
- `filesystem-after.json`
- `summary.json`

Keep generated VCI files below the run root as evidence; do not delete them.

- [ ] **Step 8.6: Implement independent equivalent-run comparison**

The harness accepts two separately supplied equivalent scenario manifests and workspace roots. Normalize run IDs, process IDs, durations, UTC values, absolute disposable paths, and root paths while preserving case order, CLR types, outcomes, exceptions, status, project-state transitions, relative file sets, sizes, hashes, omissions, and stop reasons. Any difference is explicit in `normalizedMismatches`.

- [ ] **Step 8.7: Rerun GREEN and TIA-free stderr regression**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter FullyQualifiedName~VciMutationProbeScriptTests
```

The FakeWorker regression must inherit stderr rather than attach a PowerShell script block to `ErrorDataReceived`.

---

## Task 9: Static Verification, Real-Reference Compile, Diff Review, and Documentation Index

**Files:**

- Modify: `docs/superpowers/README.md`
- Verification only: all files above

- [ ] **Step 9.1: Run focused mutation and existing read-probe tests**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~VciMutationProbe|FullyQualifiedName~VciReadProbe|FullyQualifiedName~VciProbe"
```

- [ ] **Step 9.2: Run the serialized stub build**

```powershell
dotnet build TiaMcpServer.sln --no-restore -m:1 /p:UseTiaPortalReferenceStubs=true
```

- [ ] **Step 9.3: Run the full vendor-free test suite**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build
```

- [ ] **Step 9.4: Run the installed V21 real-reference compile only**

```powershell
dotnet build TiaMcpServer.sln --no-restore -m:1 /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
```

This proves compilation against installed V21 assemblies, not live VCI behavior.

- [ ] **Step 9.5: Run documentation, whitespace, and scope gates**

```powershell
pwsh -NoProfile -File scripts/verify-doc-links.ps1
git diff --check
git status --short
```

Confirm:

- public tool counts remain exactly four read-only and fourteen read-write;
- no test/CI invokes mutation `Apply`;
- no `Project.Save`, compile, download, online, cleanup, or arbitrary reflection path was added;
- every new C# file is included in its project/test link configuration;
- the original project path and machine-specific stable identifier are absent from tracked changes; and
- only the plan is indexed under `docs/superpowers/README.md`; no acceptance report exists yet.

- [ ] **Step 9.6: Register edited files with jCodeMunch and review the complete diff**

Register all changed paths, then inspect correctness, security, unintended public surface, transaction order, path confinement, and unrelated changes.

**Mandatory stop:** report static/stub/real-reference evidence and the exact remaining Gate 4 boundary. Do not run `Inventory` or `Apply` from the general approval to implement this plan.

---

## Task 10: Separately Authorized Gate 4 Live Mutation Acceptance

**Files:**

- Generated local evidence: `artifacts/live-vci-phase1-mutation/<run-id>/`
- After successful review, create: `docs/superpowers/acceptance/reports/<run-id>-vci-workspace-phase1-mutation-probe.md`
- After report creation, modify: `docs/superpowers/README.md`

This task is not authorized by plan implementation.

- [ ] **Step 10.1: Obtain exact assets and authorization**

Present the canonical original path, six disposable paths, worker executable, evidence root, two absent run-specific workspace roots, timeout, acknowledgement, plan hash, ordered cases, and exact `Describe`/`Inventory`/`Apply` commands. State that the run mutates the disposable projects in memory and creates/changes/deletes VCI workspace mappings/files, but never saves a project.

- [ ] **Step 10.2: Run `Describe` and review it**

```powershell
pwsh -NoProfile -File scripts/live-probe-vci-phase1-mutation.ps1 -Mode Describe
```

- [ ] **Step 10.3: Run `Inventory`, review resolved assets, and stop for approval**

```powershell
pwsh -NoProfile -File scripts/live-probe-vci-phase1-mutation.ps1 `
  -Mode Inventory `
  -ScenarioManifestPath '<absolute-manifest.json>' `
  -WorkerExecutable '<absolute-worker.exe>' `
  -WorkspaceRoot '<absolute-absent-run-root>' `
  -EvidenceRoot 'artifacts/live-vci-phase1-mutation' `
  -TimeoutSeconds 240
```

- [ ] **Step 10.4: Run `Apply` only after approval of the exact plan hash**

```powershell
pwsh -NoProfile -File scripts/live-probe-vci-phase1-mutation.ps1 `
  -Mode Apply `
  -ScenarioManifestPath '<absolute-manifest.json>' `
  -WorkerExecutable '<absolute-worker.exe>' `
  -WorkspaceRoot '<absolute-absent-run-root>' `
  -EvidenceRoot 'artifacts/live-vci-phase1-mutation' `
  -TimeoutSeconds 240 `
  -ExpectedPlanHash '<inventory-plan-sha256>' `
  -AllowMutation `
  -Acknowledgement 'I_UNDERSTAND_VCI_MUTATES_DISPOSABLE_PROJECTS_AND_WORKSPACE_FILES'
```

- [ ] **Step 10.5: Repeat against an equivalent independent asset set**

Use a second manifest and absent workspace root. Do not reuse a mutated project copy or run root.

- [ ] **Step 10.6: Rerun the read-only probe against post-mutation disposable projects**

Verify the final VCI hierarchy, mappings, status, project modification state, and retained filesystem evidence using the accepted read-only harness. This remains read-only verification of the disposable copies, not proof of saved persistence.

- [ ] **Step 10.7: Review acceptance conditions and write the report**

Gate 4 fails on filesystem escape, original-project access, automatic retry, unrecorded cleanup, missing case result, unexplained normalized difference, incomplete snapshot, missing canary, unresolved uncertainty, or evidence that a transaction/project rollback assumption hid an external file effect.

Record exactly which cases returned, returned null, threw, or remained not observable; document any transaction rejection and every runtime gap. Stop for user review before planning Gate 5 public contract/safety foundations.

---

## Final Definition of Done for Gate 3

- [ ] Mutation-specific contract, DTOs, JSON boundary, access policy, worker dispatch, service, path policy, harness, and tests exist.
- [ ] The selected `Simulation_DB` / `GlobalDB` / `SimaticML` fixture is traceable to Gate 2 evidence without committing local identifiers or paths.
- [ ] All locked cases are dispatched or deterministically reported `not_observable` under documented preconditions.
- [ ] The harness defaults to non-mutating `Describe`; `Inventory` is mutation-free; `Apply` is strongly gated.
- [ ] Every mutation is one request, immediately evidenced, non-retried, and confined to a disposable project plus run root.
- [ ] Destructive operations use exclusive access and transactions where accepted; rollback-only cases measure external effects separately.
- [ ] No public tool or public schema changed.
- [ ] Focused tests, full tests, stub build, installed-reference compile, doc links, and diff checks pass with exact reported counts.
- [ ] No live mutation was run during Gate 3 implementation.
- [ ] No files were staged, committed, pushed, or published without separate authorization.
