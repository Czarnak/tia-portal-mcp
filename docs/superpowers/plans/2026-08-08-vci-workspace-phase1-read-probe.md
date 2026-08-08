# VCI Workspace Phase 1 Read-Only Probe Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement and separately live-test the internal read-only VCI probe approved in the [Phase 1 design](../specs/2026-08-08-vci-workspace-phase1-design.md), producing reviewable positive and negative Siemens V21 evidence without adding a public MCP tool or changing project/workspace state.

**Architecture:** Add one `probe_vci_read_contract` worker operation classified as `Observe`. A strict probe-only JSON boundary validates malformed requests before normal worker deserialization; a shared typed contract carries selectors and observations; net48 worker services resolve bounded engineering objects and invoke only VCI reads; and a PowerShell 7 harness sends one JSONL worker request per case, flushes each result immediately, runs the same matrix in two fresh read-only worker processes, and proves project/filesystem invariants. Deliberate Siemens exceptions are evidence inside successful worker payloads; validation, transport, protocol, evidence, timeout, process-loss, and invariant failures remain probe failures.

**Tech Stack:** C# / .NET 8 shared contracts and xUnit tests, C# / .NET Framework 4.8 Siemens Openness V21 worker, System.Text.Json JSONL IPC, PowerShell 7 live harness, SHA-256 evidence hashing.

**Required Skill Pool:** Use one plan-execution skill named above. For every TIA implementation task, load these TIA skills in order: `totally-integrated-claude:tia-openness-roadmap` (route and scope), `totally-integrated-claude:tia-csharp-common` (mandatory C# Openness foundation), then `totally-integrated-claude:tia-project-general` (VCI domain). From `tia-project-general`, load only `project-attributes.md`, `portal-settings.md`, `vci-management.md`, and `vci-operations.md` for this read-only probe.

## Global Constraints

- Work on the current branch. Do not create or switch branches or worktrees.
- Follow strict TDD for executable behavior: add the narrow test first, run it and observe the expected failure, then make the smallest production change, rerun green, and review the diff.
- Use `apply_patch` for repository edits. Preserve unrelated user changes.
- Do not commit during plan execution unless the user explicitly authorizes a commit. Use review checkpoints instead of automatic commits.
- This plan ends at the read-only live-evidence review gate. Do not implement, plan in detail, register, or live-test `probe_vci_mutation_contract`.
- Do not add `workspace_read`, `workspace_write`, or any other public MCP tool. Existing read-only and read-write public tool counts must remain unchanged.
- The only new worker operation is `probe_vci_read_contract`; classify it as `OperationCapability.Observe` and run its worker process with `--access-mode read-only`.
- The worker probe may call only read members: `Project.GetService<T>()`, VCI/group/workspace/mapping properties and compositions, `Find`, `GetSupportedFileFormats`, `MappedObject.GetStatus`, `MappedObject.GetChildStatus`, and harmless identity/property reads used to resolve a selector.
- The read-only probe must never call `Create`, `Delete`, `ConnectObject`, `ExportObject`, `Synchronize`, `SetAttribute`, `SetAttributes`, `Project.Save`, compile, download, start/stop, import, or any project lifecycle write.
- Do not create, delete, rename, chmod, lock, or deliberately corrupt any VCI workspace file. Missing or inaccessible mapping cases are observable only when that state already exists; otherwise record `not_observable`.
- An empty VCI group/workspace/mapping tree is valid live evidence, not a harness failure.
- Contract-invalid arguments are vendor-free boundary observations. Runtime-invalid but type-correct arguments are the only negative inputs sent to VCI. Boundary-escaping paths never reach Openness.
- Every probe case is exactly one worker request and one terminal JSONL evidence record. A worker-returned Siemens exception is `outcome: "threw"` inside a successful worker response.
- Only the harness may synthesize `timed_out` or `process_lost`, and only from observed transport/process state.
- PowerShell process handling must inherit stderr (`RedirectStandardError = $false`). Do not attach PowerShell script blocks to .NET output/error events.
- Live execution is never part of ordinary tests or CI. Stop after static verification and request separate authorization with the exact `.ap21` project path before `-Mode Run`.
- A live run must use two independently started worker processes against the same unchanged project, compare normalized observations, and retain raw evidence from both sessions.
- Build serially (`-m:1`) to avoid worker-copy conflicts. Run both the reference-stub build and the installed V21 real-reference build before requesting live authorization.

---

## Locked Read-Only Case Vocabulary

The shared contract and harness use these exact case IDs:

| Case | Invocation / observation |
| --- | --- |
| `R-SVC` | `project.GetService<VersionControlInterface>()`, then `WorkspaceGroup` |
| `R-GRP` | Recursively enumerate root/system/user groups, parents, counts, order, and duplicates |
| `R-WS` | Enumerate workspaces and read `Name`, `RootPath`, `Comment`, `WorkspaceLanguage`, `GlobalLibraryPath`, `DeleteUnusedTypeVersionFromLibrary`, and mapped count |
| `R-MAP` | Enumerate mappings; read mapping properties, `Status`, `GetStatus()`, and `GetChildStatus()` |
| `R-FMT` | `Workspace.GetSupportedFileFormats(IEngineeringObject)` for one bounded workspace/object selector pair |
| `R-REP` | Repeat selected service/group/workspace/format reads in one request and retain both ordered observations |
| `R-CANARY` | Re-read VCI service, root group, and counts after negative cases |
| `N-GRP-FIND-MISSING` | `WorkspaceGroup.Groups.Find` with a guaranteed-absent name |
| `N-GRP-FIND-EMPTY` | `WorkspaceGroup.Groups.Find("")` |
| `N-GRP-FIND-WHITESPACE` | `WorkspaceGroup.Groups.Find("   ")` |
| `N-GRP-FIND-NULL` | `WorkspaceGroup.Groups.Find(null)` where the installed signature permits it |
| `N-WS-FIND-MISSING` | `WorkspaceGroup.Workspaces.Find` with a guaranteed-absent name |
| `N-WS-FIND-EMPTY` | `WorkspaceGroup.Workspaces.Find("")` |
| `N-WS-FIND-WHITESPACE` | `WorkspaceGroup.Workspaces.Find("   ")` |
| `N-WS-FIND-NULL` | `WorkspaceGroup.Workspaces.Find(null)` where the installed signature permits it |
| `N-FMT-NULL` | `workspace.GetSupportedFileFormats(null)` |
| `N-FMT-UNSUPPORTED` | `GetSupportedFileFormats` with the compile-time-compatible VCI service object |
| `N-FMT-FOREIGN` | Object from a separately supplied, already-open secondary project; otherwise `not_observable` |
| `N-MAP-MISSING-FILE` | Read status for a naturally existing mapping whose file set is missing |
| `N-MAP-INACCESSIBLE-FILE` | Read status for a naturally existing mapping whose file set is inaccessible |

Every result uses exactly one of:

```text
returned | returned_null | not_observable | threw | timed_out | process_lost
```

`timed_out` and `process_lost` are evidence-layer outcomes and are never emitted by worker code.

---

## Task 1: Lock the Typed Wire Contract and Malformed-Argument Boundary

**Files:**

- Create: `TiaMcpServer.Contracts/VciReadProbeContract.cs`
- Create: `TiaMcpServer.Contracts/VciProbeRequestInfo.cs`
- Create: `TiaMcpServer.Contracts/VciProbeSelectorInfo.cs`
- Create: `TiaMcpServer.Contracts/VciProbeResultInfo.cs`
- Create: `TiaMcpServer.Contracts/VciProbeSnapshotInfo.cs`
- Modify: `TiaMcpServer.Contracts/WorkerRequest.cs`
- Create: `TiaMcpServer.OpennessWorker/Openness/VciProbeJsonBoundary.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
- Create: `TiaMcpServer.Tests/Workspace/VciReadProbeContractTests.cs`
- Create: `TiaMcpServer.Tests/Workspace/VciProbeJsonBoundaryTests.cs`

### 1.1 Add failing contract vocabulary and request-shape tests

- [ ] Create `TiaMcpServer.Tests/Workspace/VciReadProbeContractTests.cs` with tests that require:
  - operation name `probe_vci_read_contract`;
  - schema `vci-read-probe/v1`;
  - every case ID in the locked table above, with no extras;
  - all six outcome strings;
  - semantic rejection of blank run/session/case-instance IDs, unknown cases, invalid budgets, and missing selectors required by `R-FMT`;
  - explicit-null cases remaining constructible (`N-GRP-FIND-NULL`, `N-WS-FIND-NULL`, `N-FMT-NULL`).

Use an exact assertion for the vocabulary:

```csharp
Assert.Equal(
    new[]
    {
        "N-FMT-FOREIGN", "N-FMT-NULL", "N-FMT-UNSUPPORTED",
        "N-GRP-FIND-EMPTY", "N-GRP-FIND-MISSING", "N-GRP-FIND-NULL",
        "N-GRP-FIND-WHITESPACE", "N-MAP-INACCESSIBLE-FILE",
        "N-MAP-MISSING-FILE", "N-WS-FIND-EMPTY", "N-WS-FIND-MISSING",
        "N-WS-FIND-NULL", "N-WS-FIND-WHITESPACE", "R-CANARY", "R-FMT",
        "R-GRP", "R-MAP", "R-REP", "R-SVC", "R-WS"
    },
    VciReadProbeContract.CaseIds.OrderBy(x => x, StringComparer.Ordinal));
```

- [ ] Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter FullyQualifiedName~VciReadProbeContractTests
```

Expected: FAIL because the VCI contract types do not exist.

### 1.2 Add the shared request, selector, observation, and snapshot DTOs

- [ ] Add a single optional nested request to `WorkerRequest` rather than another set of flat VCI fields:

```csharp
#region Internal VCI Phase 1 read probe

/// <summary>Forwarded only by the internal probe_vci_read_contract worker operation.</summary>
public VciProbeRequestInfo? VciProbe { get; set; }

#endregion
```

- [ ] Implement `VciProbeRequestInfo` with these exact fields:

```csharp
public sealed class VciProbeRequestInfo
{
    public string SchemaVersion { get; set; } = VciReadProbeContract.SchemaVersion;
    public string RunId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string CaseId { get; set; } = string.Empty;
    public string CaseInstanceId { get; set; } = string.Empty;
    public string? TargetName { get; set; }
    public VciWorkspaceSelectorInfo? Workspace { get; set; }
    public VciEngineeringObjectSelectorInfo? EngineeringObject { get; set; }
    public string? SecondaryProjectPath { get; set; }
    public int MaxGroupDepth { get; set; } = 16;
    public int MaxGroups { get; set; } = 500;
    public int MaxWorkspaces { get; set; } = 500;
    public int MaxMappings { get; set; } = 5000;
    public int MaxEngineeringObjects { get; set; } = 200;
    public int MaxCollectionItems { get; set; } = 5000;
}
```

- [ ] Model workspace selectors as complete group segments plus workspace identity and canonical root; engineering selectors as stable V21 identifier when available plus typed structural path and fingerprint; mapping selectors as the two selectors plus normalized relative directory, filename, and format.
- [ ] Keep selectors provisional and internal. Do not reuse them in a public tool or safety-token contract.
- [ ] Define typed result DTOs for:
  - case identity and outcome;
  - normalized return/member observations;
  - exception type/message/HResult without stack trace;
  - before/after `Project.IsModified`;
  - omissions/budget exhaustion;
  - repeatability pairs;
  - service/group/workspace/mapping/candidate snapshots.
- [ ] Preserve collection enumeration order by storing `EnumerationIndex`. Also store a canonical key for matching; do not sort the raw observed collection in the worker.
- [ ] Ensure `VciProbeCaseResultInfo` has nullable `NotObservableReason`, `Return`, `Snapshot`, `Exception`, and `Repeatability`, plus a non-null `ProjectState` and `Omissions` list.

The case envelope must remain structurally stable:

```csharp
public sealed class VciProbeCaseResultInfo
{
    public string SchemaVersion { get; set; } = VciReadProbeContract.SchemaVersion;
    public string RunId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string CaseId { get; set; } = string.Empty;
    public string CaseInstanceId { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public VciProbeReturnInfo? Return { get; set; }
    public VciProbeSnapshotInfo? Snapshot { get; set; }
    public VciProbeExceptionInfo? Exception { get; set; }
    public VciProbeRepeatabilityInfo? Repeatability { get; set; }
    public string? NotObservableReason { get; set; }
    public VciProbeProjectStateInfo ProjectState { get; set; } = new();
    public List<VciProbeOmissionInfo> Omissions { get; set; } = new();
}
```

### 1.3 Add failing malformed-JSON boundary tests

- [ ] Create `VciProbeJsonBoundaryTests.cs` to pass raw JSON and assert exact `validation_error` reasons for:
  - missing `vciProbe`;
  - `vciProbe` as an array/string/number;
  - wrong types for every scalar and selector member;
  - unknown top-level or nested fields;
  - duplicate fields differing only by case;
  - unknown `caseId`;
  - a required field omitted versus explicitly present as JSON `null`;
  - write flags (`confirm`, `allowTiaConfirmations`) appearing even as `false`.
- [ ] Also assert that non-VCI worker requests return `null` from the probe boundary, so existing operations retain their current permissive JSON behavior.

Representative tests:

```csharp
[Theory]
[InlineData("{\"method\":\"probe_vci_read_contract\",\"projectPath\":\"C:\\\\P.ap21\",\"vciProbe\":[]}", "vciProbe must be an object")]
[InlineData("{\"method\":\"probe_vci_read_contract\",\"projectPath\":\"C:\\\\P.ap21\",\"vciProbe\":{\"schemaVersion\":\"vci-read-probe/v1\",\"runId\":\"r\",\"sessionId\":\"s\",\"caseId\":\"R-SVC\",\"caseInstanceId\":\"i\",\"extra\":1}}", "Unknown vciProbe field 'extra'")]
public void Validate_RejectsMalformedProbeArguments(string json, string expected)
{
    Assert.Contains(expected, VciProbeJsonBoundary.Validate(json), StringComparison.Ordinal);
}
```

- [ ] Run the focused boundary test and observe failure because the helper does not exist.

### 1.4 Implement the strict probe-only JSON boundary

- [ ] Implement `VciProbeJsonBoundary` as a pure System.Text.Json helper with no Siemens dependency. It must:
  - parse the root with `JsonDocument`;
  - apply only when a case-insensitive `method` value equals `probe_vci_read_contract`;
  - allow exactly `method`, `projectPath`, and `vciProbe` at the root;
  - allow exactly the DTO fields inside `vciProbe` and selectors;
  - detect case-insensitive duplicates before deserialization;
  - validate JSON value kinds and required presence, preserving the distinction between absent and explicit null;
  - call `VciReadProbeContract` for case-specific semantic rules;
  - return a deterministic message or `null`, without throwing for malformed input.
- [ ] Link it into the net8 test project:

```xml
<Compile Include="..\TiaMcpServer.OpennessWorker\Openness\VciProbeJsonBoundary.cs"
  Link="Workspace\VciProbeJsonBoundary.cs" />
```

- [ ] Run both focused test classes. Expected: PASS.
- [ ] Review the wire shape: no `object`, `dynamic`, `JsonElement`, or nested JSON string may cross in a success payload.

**Review checkpoint:** typed contract and contract-invalid negative boundary only. No Siemens calls yet.

---

## Task 2: Authorize and Dispatch the Internal Read Operation Without Changing the Public Surface

**Files:**

- Modify: `TiaMcpServer.Contracts/OperationPolicyCatalog.cs`
- Modify: `TiaMcpServer.OpennessWorker/Program.cs`
- Create: `TiaMcpServer.Tests/Workspace/VciReadProbeAccessPolicyTests.cs`
- Create: `TiaMcpServer.Tests/Workspace/VciReadProbeWorkerSourceContractTests.cs`

### 2.1 Add failing access-policy tests

- [ ] Test the exact classification and both authorization layers:

```csharp
[Fact]
public void ProbeVciReadContract_IsObserveAndAllowedInReadOnlyMode()
{
    Assert.Equal(
        OperationCapability.Observe,
        OperationPolicyCatalog.GetCapability(VciReadProbeContract.OperationName));
    Assert.True(OperationPolicyCatalog.IsAllowed(
        McpAccessMode.ReadOnly,
        VciReadProbeContract.OperationName));
    Assert.Null(WorkerOperationAuthorization.Authorize(
        McpAccessMode.ReadOnly,
        VciReadProbeContract.OperationName));
}
```

- [ ] Run the focused test. Expected: FAIL because the operation is unclassified and denied by default.
- [ ] Add `["probe_vci_read_contract"] = OperationCapability.Observe` beside the existing internal network read probe.
- [ ] Rerun. Expected: PASS.

### 2.2 Add failing dispatch and public-surface source contracts

- [ ] In `VciReadProbeWorkerSourceContractTests.cs`, assert that:
  - `Program.cs` dispatches the exact method to `ProbeVciReadContract(request)`;
  - the handler validates the probe request before `WithProject`;
  - the handler calls `VciReadContractProbeService.Execute` and `Success(...)`;
  - `VciProbeJsonBoundary.Validate(line)` occurs before `JsonSerializer.Deserialize<WorkerRequest>`;
  - no file beneath `TiaMcpServer/Tools/` or host tool-registration source contains `probe_vci_read_contract`;
  - existing public-tool count assertions remain 4 read-only and 14 read-write.
- [ ] Run the focused source-contract test. Expected: FAIL because dispatch/handler do not exist.

### 2.3 Add the smallest worker dispatch seam

- [ ] At the start of `HandleLine`, call the JSON boundary and return `Failure(ValidationError, message)` before normal deserialization when it rejects the VCI envelope.
- [ ] Add the switch arm:

```csharp
"probe_vci_read_contract" => ProbeVciReadContract(request),
```

- [ ] Add the handler with semantic validation before `WithProject`:

```csharp
private static WorkerResponse ProbeVciReadContract(WorkerRequest request)
{
    var validationError = VciReadProbeContract.Validate(request.VciProbe);
    if (validationError is not null)
    {
        throw new WorkerOperationException(
            WorkerFailureCategories.ValidationError,
            validationError);
    }

    return WithProject(request, project =>
        Success(VciReadContractProbeService.Execute(project, request.VciProbe!)));
}
```

- [ ] Initially add a compile-only service shell that throws `NotImplementedException`; do not run the full suite yet. The next tasks replace it before any green completion claim.
- [ ] Rerun policy tests and source-contract tests. The dispatch assertions should pass; the full build is intentionally not yet the green gate.

**Review checkpoint:** authorization precedes dispatch; operation remains internal; no public tool or host invoker was added.

---

## Task 3: Implement Pure Observation, Exception, and Return-Value Normalization

**Files:**

- Create: `TiaMcpServer.OpennessWorker/Openness/VciProbeValueNormalizer.cs`
- Create: `TiaMcpServer.OpennessWorker/Openness/VciProbeObservationRunner.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
- Create: `TiaMcpServer.Tests/Workspace/VciProbeValueNormalizerTests.cs`
- Create: `TiaMcpServer.Tests/Workspace/VciProbeObservationRunnerTests.cs`

### 3.1 Add failing value-normalization tests

- [ ] Cover null, string, Boolean, signed/unsigned integral values, floating point using invariant round-trip formatting, enum name and numeric value, `CultureInfo`, `FileInfo`, `DirectoryInfo`, and ordered collections.
- [ ] Cover collection budget exhaustion, recursion-depth exhaustion, and an unsupported object whose `ToString()` throws. Assert the normalizer records only its runtime type and never calls arbitrary `ToString()`.
- [ ] Assert normalization preserves source order and emits stable `kind` values.
- [ ] Link the pure helper into the test project and run the focused test. Expected: FAIL until implemented.

### 3.2 Implement bounded return normalization

- [ ] Implement a closed type switch. Unknown objects return `kind: "unsupported_value"` plus `RuntimeType`; they do not fail the case.
- [ ] Normalize enum values as both declared name and invariant integral value.
- [ ] Normalize paths with `Path.GetFullPath` only inside a `try`; retain the original path and a member-level exception if canonicalization fails.
- [ ] Preserve collection order, apply `MaxCollectionItems`, and append a typed omission when truncated.
- [ ] Rerun focused tests. Expected: PASS.

### 3.3 Add failing observation-runner tests

- [ ] Test these exact transitions:
  - non-null return -> `returned`;
  - null -> `returned_null`;
  - explicit unavailable prerequisite -> `not_observable` with reason;
  - thrown exception -> `threw` with type/message/HResult and no stack;
  - `Project.IsModified` sampled before and in `finally` after every invocation;
  - an exception from state sampling is not converted into Siemens evidence and instead propagates as an infrastructure failure.
- [ ] Assert the runner cannot create `timed_out` or `process_lost`.
- [ ] Run focused tests. Expected: FAIL.

### 3.4 Implement observation execution

- [ ] Implement factories `Returned`, `ReturnedNull`, `NotObservable`, and `Threw` around a supplied read delegate.
- [ ] Catch exceptions only around the deliberate VCI member invocation. Never catch request validation, project resolution, evidence serialization, or `Project.IsModified` sampling as a case outcome.
- [ ] Normalize exceptions recursively to at most three inner exceptions and omit stack traces and `Exception.Data`.
- [ ] Rerun both focused test classes. Expected: PASS.

**Review checkpoint:** pure behavior is vendor-free and fully unit-tested; no Siemens-specific logic or filesystem mutation exists.

---

## Task 4: Build Bounded Engineering-Object Discovery and Selector Resolution

**Files:**

- Create: `TiaMcpServer.OpennessWorker/Openness/VciProbeEngineeringObjectCatalog.cs`
- Create: `TiaMcpServer.OpennessWorker/Openness/VciProbeEngineeringObjectResolver.cs`
- Create: `TiaMcpServer.OpennessWorker/Openness/VciProbeSelectorFingerprint.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
- Create: `TiaMcpServer.Tests/Workspace/VciProbeSelectorFingerprintTests.cs`
- Modify: `TiaMcpServer.Tests/Workspace/VciReadProbeWorkerSourceContractTests.cs`

### 4.1 Add failing pure selector/fingerprint tests

- [ ] Specify canonical structural-path serialization: ordinal segment list with exact `kind`, `name`, and `sameNameOrdinal` fields, ordinal string comparison, and no culture-sensitive formatting.
- [ ] Specify SHA-256 lowercase hex fingerprints over schema version, runtime type, structural path, and stable read-only identity fields.
- [ ] Assert that changing any segment, ordinal, runtime type, or identity field changes the fingerprint.
- [ ] Link `VciProbeSelectorFingerprint.cs`, run the focused test, and observe FAIL.
- [ ] Implement the smallest deterministic fingerprint helper and rerun green.

### 4.2 Add source-contract tests for Siemens selector behavior

- [ ] Require `project.GetService<ObjectIdentifierProvider>()`, `GetIdentifier`, and `Find` in the catalog/resolver.
- [ ] Require typed structural fallback for exactly these bounded candidate families:
  - project;
  - device;
  - nested device item;
  - PLC block;
  - PLC tag table;
  - PLC type.
- [ ] Require reuse of existing `PlcSoftwareLocator` and the established recursive traversal patterns in `ProjectTreeWalker` / `NetworkObjectIndexReader`; do not modify those readers' public DTOs.
- [ ] Require one representative per distinct runtime type before filling remaining candidate budget, so the first large device tree cannot consume all coverage.
- [ ] Require selector re-resolution to verify runtime type and fingerprint before invocation.
- [ ] Require omissions instead of unbounded traversal or silent drops.
- [ ] Run source-contract tests. Expected: FAIL because the catalog/resolver do not exist.

### 4.3 Implement bounded catalog and resolver

- [ ] Enumerate candidates without invoking any VCI write. Preserve source enumeration index.
- [ ] Ask `ObjectIdentifierProvider.GetIdentifier(candidate)` inside a member-level observation. When it returns a nonblank identifier, store it as the preferred selector.
- [ ] Always store the typed structural path and fingerprint as evidence, even when a stable identifier exists.
- [ ] Resolve by stable identifier first. If unavailable or unsupported, resolve the typed structural path. Verify runtime type and fingerprint; return `not_observable: selector_stale_or_ambiguous` rather than choosing by name alone.
- [ ] Enforce `MaxEngineeringObjects`, traversal depth, and per-composition bounds with explicit omission records.
- [ ] Do not cache Siemens object proxies between worker requests.
- [ ] Run source-contract and fingerprint tests. Expected: PASS.
- [ ] Run the real-reference compile for the worker only:

```powershell
dotnet build TiaMcpServer.OpennessWorker/TiaMcpServer.OpennessWorker.csproj --no-restore -m:1 /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
```

Expected: PASS. Fix only API-shape/compiler errors; do not infer runtime behavior from the compile.

**Review checkpoint:** selectors are internal evidence locators, not approved public selectors or write identities.

---

## Task 5: Read VCI Service, Groups, Workspaces, Mappings, and Formats

**Files:**

- Create: `TiaMcpServer.OpennessWorker/Openness/VciProbeSnapshotReader.cs`
- Modify: `TiaMcpServer.Tests/Workspace/VciReadProbeWorkerSourceContractTests.cs`

### 5.1 Add failing source contracts for the positive matrix

- [ ] Require these exact V21 members in `VciProbeSnapshotReader.cs`:

```text
GetService<VersionControlInterface>
WorkspaceGroup
Groups
Workspaces
MappedObjects
GetSupportedFileFormats
Status
GetStatus
GetChildStatus
```

- [ ] Require workspace property reads for `Name`, `RootPath`, `Comment`, `WorkspaceLanguage`, `GlobalLibraryPath`, and `DeleteUnusedTypeVersionFromLibrary`.
- [ ] Require recursive system/user group traversal with explicit parent chain, enumeration index, duplicate-name detection per parent, and count observations.
- [ ] Require every property/method result to be a `VciProbeMemberObservationInfo`, so one throwing property does not erase successful sibling observations.
- [ ] Require raw order retention and budget omissions; prohibit sorting the raw VCI compositions.
- [ ] Run the focused source-contract test. Expected: FAIL.

### 5.2 Implement service and group reads (`R-SVC`, `R-GRP`)

- [ ] Implement `ReadService` using `project.GetService<VersionControlInterface>()`, recording returned/null/threw and runtime type.
- [ ] Read `WorkspaceGroup` separately so its null/exception behavior is not conflated with service acquisition.
- [ ] Recursively enumerate `WorkspaceGroup.Groups` and each user group's nested `Groups`, plus `Workspaces` at every group.
- [ ] Apply `MaxGroupDepth`, `MaxGroups`, and `MaxWorkspaces`; record the exact group path where traversal was truncated.
- [ ] Preserve duplicates as separate entries with `SameNameOrdinal`; do not collapse by name.

### 5.3 Implement workspace and mapping reads (`R-WS`, `R-MAP`)

- [ ] Read each approved workspace property independently through the observation runner.
- [ ] Canonicalize `RootPath` as evidence only; a canonicalization exception is a member observation, not a guessed path.
- [ ] Enumerate `MappedObjects` up to `MaxMappings` and record `DirectoryPath`, `FileNameWithoutExtension`, `FileFormat`, and engineering-object selector.
- [ ] Read `Status`, `GetStatus()`, and `GetChildStatus()` as three distinct member observations. Never assume the property and methods agree.
- [ ] Preserve the runtime return type and enum representation exactly through the normalizer.

### 5.4 Implement supported-format read (`R-FMT`)

- [ ] Resolve the supplied workspace selector and engineering-object selector fresh in the current project.
- [ ] Invoke `workspace.GetSupportedFileFormats(engineeringObject)` exactly once.
- [ ] Preserve collection type, item runtime type, raw order, casing, empty strings, duplicates, and null items.
- [ ] If no workspace/candidate pair exists in an empty VCI project, return `not_observable: no_workspace_candidate_pair` rather than failing the run.
- [ ] Run source-contract tests and the real-reference worker build. Expected: PASS.

**Review checkpoint:** positive read paths compile against installed V21; runtime semantics remain explicitly unverified until the live gate.

---

## Task 6: Implement Runtime-Negative Cases, Repeatability, and Canary

**Files:**

- Create: `TiaMcpServer.OpennessWorker/Openness/VciReadContractProbeService.cs`
- Modify: `TiaMcpServer.OpennessWorker/Program.cs`
- Modify: `TiaMcpServer.Tests/Workspace/VciReadProbeWorkerSourceContractTests.cs`

### 6.1 Add failing closed-dispatch and no-mutation source tests

- [ ] Require a switch arm for every locked case ID and a validation failure for all other IDs.
- [ ] Require the service to capture `Project.IsModified` before and after every case.
- [ ] Require a deliberate invocation exception to remain inside `VciProbeCaseResultInfo` and therefore pass through `Program.Success(...)`.
- [ ] Reject these invocation patterns in all VCI read-probe production sources using call-site regular expressions, not comment substring checks:

```text
.Create(  .Delete(  .ConnectObject(  .ExportObject(  .Synchronize(
.SetAttribute(  .SetAttributes(  .Save(  .Compile(  .Download(
```

- [ ] Require `R-CANARY` to reacquire the service and root group after negatives.
- [ ] Require `R-REP` to retain first and second observations rather than only a Boolean equality result.
- [ ] Run the focused source-contract test. Expected: FAIL.

### 6.2 Implement exact negative invocations

- [ ] Resolve the selected parent group/workspace composition, then invoke `Find` with the case-specific guaranteed-missing, empty, whitespace, or null name. For missing names, derive a GUID-suffixed name and prove it is absent immediately before the deliberate call.
- [ ] Invoke `GetSupportedFileFormats(null!)` only for `N-FMT-NULL`.
- [ ] For `N-FMT-UNSUPPORTED`, use the acquired `VersionControlInterface` cast to `IEngineeringObject`; do not substitute a random object that might actually be supported.
- [ ] For `N-FMT-FOREIGN`, use only a user-supplied secondary project that is already open in a separately identifiable TIA Portal process. Attach read-only, obtain one bounded object, and never open, close, or save the project. If zero/multiple safe candidates exist, attach is denied, or remoting cannot supply the object, return `not_observable` with the exact reason.
- [ ] For missing/inaccessible mapped-file cases, select only naturally existing mappings whose filesystem evidence already proves the state. If no mapping qualifies, return `not_observable`; never force the state.
- [ ] If the installed signature or CLR prevents constructing an intended argument, return `not_observable: signature_does_not_permit_argument`; do not replace it with reflection-based invocation.

### 6.3 Implement repeatability and canary

- [ ] `R-REP` repeats service/root-group counts and, when available, one workspace `GetSupportedFileFormats` invocation using the same freshly resolved selector. Store both ordered observations and a worker-local normalized equality flag.
- [ ] `R-CANARY` reacquires `VersionControlInterface`, reads `WorkspaceGroup`, `Groups.Count`, and `Workspaces.Count`. It must execute after all negative cases in the harness.
- [ ] In `finally`, sample `Project.IsModified` and attach before/after values to every returned result, including `threw` and `not_observable`.
- [ ] Remove the temporary `NotImplementedException` service shell from Task 2.
- [ ] Run source-contract tests, all `Workspace` tests, and the real-reference worker build. Expected: PASS.

**Review checkpoint:** every live-negative input is type-correct and safety-confined; malformed JSON and boundary escapes remain vendor-free.

---

## Task 7: Add a Safe PowerShell Harness Shell and `Describe` Contract

**Files:**

- Create: `scripts/live-probe-vci-phase1-read.ps1`
- Create: `TiaMcpServer.Tests/Workspace/VciReadProbeScriptTests.cs`

### 7.1 Add failing `Describe` and static-safety tests

- [ ] Assert the script requires PowerShell 7, uses strict mode, sets `$ErrorActionPreference = 'Stop'`, and defaults to `-Mode Describe`.
- [ ] Execute `-Mode Describe`, parse its one JSON document, and require:

```json
{
  "schemaVersion": "vci-phase1-read-harness/v1",
  "readOnly": true,
  "mutatesProject": false,
  "workerOperation": "probe_vci_read_contract",
  "workerAccessMode": "read-only",
  "requiresSeparateLiveAuthorization": true,
  "workerSessions": 2
}
```

- [ ] Require the complete case vocabulary and evidence filenames in the description.
- [ ] Assert the script contains no public MCP host startup and no public `workspace_*` tool name.
- [ ] Assert `RedirectStandardInput`/`RedirectStandardOutput` are true, `RedirectStandardError` is false, and no `ErrorDataReceived`/`BeginErrorReadLine` callback exists.
- [ ] Assert ordinary test code never invokes the script with `-Mode Run`.
- [ ] Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter FullyQualifiedName~VciReadProbeScriptTests
```

Expected: FAIL because the script does not exist.

### 7.2 Implement `Describe` and fail-closed `Run` preflight

- [ ] Add parameters:

```powershell
[ValidateSet('Describe', 'Run')]
[string]$Mode = 'Describe',
[string]$ProjectPath,
[string]$SecondaryProjectPath,
[string]$WorkerExecutable,
[string]$EvidenceRoot = 'artifacts/live-vci-phase1',
[ValidateRange(5, 1800)]
[int]$TimeoutSeconds = 240
```

- [ ] `Describe` must not resolve the worker, inspect a project, create evidence directories, or start any process.
- [ ] `Run` must require PowerShell 7, an absolute existing `.ap21` project path, an absolute existing worker executable, and a canonical evidence root beneath repository `artifacts/live-vci-phase1`.
- [ ] Reject project/evidence path equality, project paths beneath the evidence root, reparse-point evidence ancestors, and any boundary whose canonicalization fails.
- [ ] Accept `SecondaryProjectPath` only when absolute, existing, `.ap21`, different from the primary project, and separately authorized; otherwise leave the foreign-object case `not_observable`.
- [ ] Start the worker with `--access-mode read-only`, hidden/no shell, stdin/stdout redirected, and stderr inherited.
- [ ] Use `ReadLineAsync()` plus bounded task waiting. Do not use PowerShell event callbacks.
- [ ] Rerun focused script tests. Expected: PASS for `Describe` and static safety; no TIA process starts.

### 7.3 Add a TIA-free stderr regression smoke test

- [ ] Reuse the existing network harness pattern: start a tiny child process that writes one JSONL stdout record and one stderr line, then prove the harness-style process reader receives stdout without `PSInvalidOperationException` or a missing-runspace error.
- [ ] Run that focused test twice. Expected: PASS both times.

**Review checkpoint:** the script is inert by default and cannot silently cross into live execution.

---

## Task 8: Implement the Evidence Bundle, Case Matrix, and Two-Session Run

**Files:**

- Modify: `scripts/live-probe-vci-phase1-read.ps1`
- Modify: `TiaMcpServer.Tests/Workspace/VciReadProbeScriptTests.cs`

### 8.1 Add failing evidence/schema source contracts

- [ ] Require one run directory `artifacts/live-vci-phase1/<run-id>/` and exactly these files:

```text
manifest.json
cases.jsonl
snapshot-before.json
snapshot-after.json
filesystem-before.json
filesystem-after.json
summary.json
```

- [ ] Require UTF-8 without BOM, atomic replacement for JSON documents, and append-plus-flush behavior for every `cases.jsonl` terminal record.
- [ ] Require session IDs `session-1` and `session-2`, each backed by a separately started and stopped worker process.
- [ ] Require every request to contain only `method`, `projectPath`, and `vciProbe`; forbid confirm/write flags.
- [ ] Require a terminal synthesized record when a worker times out or exits before a response; do not fabricate a Siemens exception.
- [ ] Require `R-CANARY` after all negative cases in each session.
- [ ] Require failure if duplicate/missing `caseInstanceId`, schema mismatch, malformed worker payload, incomplete filesystem hashing, changed project state, changed files, or normalized session mismatch is observed.
- [ ] Run focused tests. Expected: FAIL.

### 8.2 Implement provenance and pre-run snapshots

- [ ] Create a run ID from UTC timestamp plus random suffix and refuse to reuse an existing directory.
- [ ] Write `manifest.json` with script/worker SHA-256, git commit and dirty-state summary, OS/PowerShell/.NET information, exact canonical paths, budgets, timeouts, case vocabulary, and authorization inputs. Do not include secrets or full environment dumps.
- [ ] Start `session-1`; run `R-SVC`, `R-GRP`, `R-WS`, and `R-MAP` discovery requests. Aggregate their typed payloads into the first entry of `snapshot-before.json`.
- [ ] Discover all workspace roots from typed results. Record them before enumerating files.
- [ ] Build `filesystem-before.json` by hashing every regular file under discovered workspace roots with SHA-256, relative path, length, and last-write UTC. Never follow reparse points.
- [ ] Enforce explicit file-count and byte budgets. If hashing is incomplete, mark evidence incomplete and fail rather than claiming unchanged files.

### 8.3 Execute and flush the complete matrix

- [ ] Derive stable `caseInstanceId` values from session-independent case ID plus canonical selector hash; keep `sessionId` separate so results can match across sessions.
- [ ] For each discovered workspace/candidate pair selected by the bounded catalog, send one `R-FMT` request. Preserve zero pairs as one `R-FMT` `not_observable` case.
- [ ] Send every safe negative case exactly once per applicable parent/selector. State-dependent cases that lack a prerequisite still get a terminal `not_observable` worker record.
- [ ] Send `R-REP`, then `R-CANARY` last.
- [ ] After every response, validate worker envelope and typed payload, add harness timestamps/elapsed time/transport facts, append one compact JSON line, and flush the file handle.
- [ ] If response waiting exceeds the deadline, record `timed_out`, terminate that worker, and fail the session. If the process exits first, record `process_lost` with exit code and fail the session.
- [ ] Never restart mid-session and pretend continuity. A failed session remains failed evidence.

### 8.4 Repeat in a fresh worker and capture post-state

- [ ] Stop/dispose `session-1`, start a new worker process as `session-2`, and repeat the same discovery and case-generation rules from scratch.
- [ ] Store both session baselines in `snapshot-before.json` and both final snapshots (same positive reads after canary) in `snapshot-after.json`.
- [ ] Stop `session-2`, then capture `filesystem-after.json` from the same canonical workspace roots and budgets.
- [ ] Compare every case by `caseInstanceId` after removing only run/session IDs, timestamps, duration, process ID, and transport sequencing. Preserve collection order, return/exception content, omissions, and project-state values in the comparison.
- [ ] Write `summary.json` with counts by case/outcome/session, normalized mismatches, canary status, project-state invariant, filesystem invariant, evidence completeness, and an overall pass/fail.
- [ ] Treat `returned`, `returned_null`, `not_observable`, and deliberate `threw` as valid observations. Treat timeout, process loss, validation/protocol/evidence/invariant failure, or incomplete terminal coverage as overall failure.
- [ ] Rerun script/source-contract tests. Expected: PASS without running TIA.

**Review checkpoint:** inspect the script line-by-line for writes. Its only filesystem writes must target the new evidence directory.

---

## Task 9: Run Static, Stub, Real-Reference, and Documentation Verification

**Files:**

- Modify only if verification exposes a defect in files already listed above.

### 9.1 Run focused tests from clean build inputs

- [ ] Restore once, then run Workspace tests:

```powershell
dotnet restore TiaMcpServer.sln
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter FullyQualifiedName~Workspace
```

Expected: PASS.

### 9.2 Run the serialized reference-stub build and full test suite

- [ ] Run:

```powershell
dotnet build TiaMcpServer.sln --no-restore -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build --configuration Debug
```

Expected: PASS. If configuration/output mismatch makes `--no-build` invalid, build the same configuration explicitly; do not hide the mismatch.

### 9.3 Run the installed V21 real-reference build

- [ ] Run:

```powershell
dotnet build TiaMcpServer.sln --no-restore -m:1 /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
```

Expected: PASS. This proves API shape only, not a live VCI result.

### 9.4 Prove scope and documentation integrity

- [ ] Run:

```powershell
pwsh -NoProfile -File scripts/verify-doc-links.ps1
git diff --check
git status --short
```

- [ ] Confirm existing tests still report exactly four read-only tools and fourteen read-write tools.
- [ ] Confirm no test/CI script invokes `live-probe-vci-phase1-read.ps1 -Mode Run`.
- [ ] Confirm the diff contains no mutation operation, public workspace tool, project save, or acceptance claim.
- [ ] Inspect all newly generated build output only as verification; do not add it to Git.

**Mandatory stop:** report static verification and request separate live authorization with the exact project path. Do not execute Task 10 from a general approval to implement this plan.

---

## Task 10: Separately Authorized Live Read-Only Acceptance and Review Gate

**Files:**

- Generated evidence only: `artifacts/live-vci-phase1/<run-id>/`
- After a successful reviewed run, create: `docs/superpowers/acceptance/reports/<run-id>-vci-workspace-phase1-read-probe.md`
- After creating the report, modify: `docs/superpowers/README.md`

### 10.1 Obtain exact authorization

- [ ] Present the exact command with canonical `ProjectPath`, `WorkerExecutable`, evidence root, timeout, and optional secondary project.
- [ ] State that the run attaches to TIA Portal V21 twice, reads VCI/workspace objects, reads/hashes discovered workspace files, and writes only local evidence.
- [ ] Wait for explicit authorization for that exact project and command.

Command shape after the paths are filled from current build output:

```powershell
pwsh -NoProfile -File scripts/live-probe-vci-phase1-read.ps1 `
  -Mode Run `
  -ProjectPath '<authorized-absolute-project.ap21>' `
  -WorkerExecutable '<absolute-real-reference-worker.exe>' `
  -EvidenceRoot 'artifacts/live-vci-phase1' `
  -TimeoutSeconds 240
```

`-SecondaryProjectPath` is omitted unless the user separately supplies and authorizes an already-open secondary project for `N-FMT-FOREIGN`.

### 10.2 Run once and preserve all evidence

- [ ] Execute the exact authorized command once. Do not rerun automatically to turn a failure into a pass.
- [ ] If a TIA confirmation dialog, access denial, timeout, process loss, or evidence/invariant failure occurs, stop and report it with the run directory.
- [ ] Do not edit raw evidence after the run.

### 10.3 Review acceptance conditions

- [ ] Verify `manifest.json` hashes match the executed script/worker.
- [ ] Verify every planned case has one terminal record per applicable instance and session.
- [ ] Verify both workers were independently started and completed.
- [ ] Verify normalized session comparison passed or list every mismatch as live behavior requiring design review.
- [ ] Verify `Project.IsModified` remained equal to its baseline throughout; baseline may already be true.
- [ ] Verify discovered workspace file inventories/hashes are identical before and after and evidence was complete.
- [ ] Verify both canaries returned a usable service/root-group observation after negative cases.
- [ ] Treat Siemens `threw` observations as data; summarize exact exception type/message without classifying them as infrastructure failure.
- [ ] Treat empty groups/workspaces/mappings and absent state-dependent negatives as valid `not_observable` evidence.

### 10.4 Write the acceptance report and stop

- [ ] Create the report named from the immutable run ID. Include authorization scope, exact command, repository commit/dirty state, environment, case outcome table, normalized mismatches, exception observations, omissions, invariant results, and raw artifact path/hashes.
- [ ] Explicitly separate:
  - vendor-free contract evidence;
  - real-reference compile evidence;
  - live Siemens V21 evidence;
  - cases that remained `not_observable`.
- [ ] Add the report to the Acceptance reports table in `docs/superpowers/README.md` and run `scripts/verify-doc-links.ps1` plus `git diff --check`.
- [ ] Present the report for user review.
- [ ] Stop. Do not write or implement the mutating-probe plan until the user approves the read-only evidence and explicitly requests the next gate.

---

## Final Definition of Done

- `probe_vci_read_contract` is an internal, typed, read-only worker operation classified `Observe`.
- Malformed/wrong-type/unknown-field/unknown-case inputs have deterministic vendor-free rejection evidence and never reach Openness.
- Positive and runtime-invalid safe cases have one-request/one-record typed observations.
- Deliberate VCI exceptions are preserved as `threw`; infrastructure failures remain failures.
- The harness is inert by default, uses inherited stderr, and cannot be invoked live by ordinary tests.
- The live matrix runs in two fresh worker processes with normalized repeatability, project-state, canary, and workspace-file invariants.
- Public tool counts and public workspace surface are unchanged.
- The read-only acceptance report is reviewed before any mutation-probe work begins.
