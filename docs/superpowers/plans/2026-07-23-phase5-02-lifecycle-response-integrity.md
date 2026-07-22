# Phase 5 Plan 2: Lifecycle and Response Integrity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make project status a side-effect-free read, give lifecycle writes a private state probe, bind only to worker-reported paths, prevent stranding SaveAs calls, and return deterministic categorized outcomes without retrying uncertain writes.

**Architecture:** Add categories to the shared worker envelope and host result. Split the worker's `get_project_status` read from a host-internal `probe_project_status_for_lifecycle` operation. Give each client request an explicit binding transition mode. Preserve worker warnings in direct and guarded results while retaining the existing warning budget and next-call-only worker recovery.

**Tech Stack:** .NET 8 host, .NET Standard 2.0 contracts, net48 Openness worker, newline-delimited JSON IPC, xUnit, FakeWorker.

## Global Constraints

- Primary AC scope: AC-004–AC-022, AC-032–AC-035, AC-042–AC-045.
- Keep exactly ten public MCP tools and exactly seven lifecycle MCP methods. The probe is an IPC operation, never an MCP tool.
- Direct status must not open, close, switch, save, or bind a project.
- Lifecycle preview/apply may use the internal probe, but all safety token/state/audit checks remain intact.
- Open, create, and supported SaveAs bind only after success and only from `WorkerResponse.ResolvedProjectPath`.
- A missing required resolved path is `postcondition_failed`; never substitute `projectPath`, target directory/name, payload text, or preview intent.
- `save_project_as(rebind:false)` must fail before current-state probing, preview generation, token issuance, audit append, or worker invocation.
- Timeout/crash/protocol-loss recovery may restart the worker only for the next caller request. Never replay the failed write.
- Existing warning limits remain `20` lines and `1_000` characters per line with explicit ` [TRUNCATED]` trailers.
- Use the serialized stub build after every task.

---

## Task 1: Add stable failure-category plumbing

**Files:**

- Create: `TiaMcpServer.Contracts/WorkerFailureCategories.cs`
- Modify: `TiaMcpServer.Contracts/WorkerResponse.cs`
- Modify: `TiaMcpServer/Worker/WorkerCallResult.cs`
- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs`
- Modify: `TiaMcpServer/Safety/WriteSafetyTooling.cs`
- Create: `TiaMcpServer.OpennessWorker/WorkerOperationException.cs`
- Modify: `TiaMcpServer.OpennessWorker/Program.cs`
- Modify: `TiaMcpServer.FakeWorker/Program.cs`
- Create: `TiaMcpServer.Tests/WorkerCallResultTests.cs`
- Modify: `TiaMcpServer.Tests/WorkerResponseJsonTests.cs`
- Modify: `TiaMcpServer.Tests/OpennessWorkerClientIntegrationTests.cs`

**Acceptance:** AC-032, AC-033, AC-034, AC-043; foundation for AC-014, AC-015, AC-018, AC-020, AC-029.

- [ ] **Step 1: Add failing contract and renderer tests.**

  Add a round-trip theory for all approved values:

  ```csharp
  [Theory]
  [InlineData("validation_error")]
  [InlineData("binding_conflict")]
  [InlineData("state_changed")]
  [InlineData("worker_operation_failed")]
  [InlineData("worker_timeout")]
  [InlineData("worker_crashed")]
  [InlineData("postcondition_failed")]
  public void WorkerResponse_RoundTripsFailureCategory(string category)
  ```

  Add `WorkerCallResultTests` proving:

  - `Ok` has no category;
  - `Fail` requires one approved category;
  - failure rendering keeps category primary even when warnings exist;
  - rendering does not include credential-like test values placed only in protected input data.

  Also add the timeout/crash RED tests before any production mapping change:

  ```csharp
  [Fact]
  public async Task HangingWrite_ReturnsWorkerTimeout_AndIsIssuedOnce()

  [Theory]
  [InlineData("crash")]
  [InlineData("malformed")]
  [InlineData("null-response")]
  public async Task LostWrite_ReturnsWorkerCrashed_AndIsIssuedOnce(string scenario)
  ```

  Preserve the existing restart-for-next-call assertion and add FakeWorker request counting without adding a retry branch.

- [ ] **Step 2: Run focused tests and observe RED.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~WorkerResponseJsonTests|FullyQualifiedName~WorkerCallResultTests|FullyQualifiedName~OpennessWorkerClientIntegrationTests"
  ```

  Expected failure: no category field/constants exist, and timeout/crash results do not yet expose the required categories and one-request evidence.

- [ ] **Step 3: Add the immutable shared category vocabulary.**

  Create:

  ```csharp
  public static class WorkerFailureCategories
  {
      public const string ValidationError = "validation_error";
      public const string BindingConflict = "binding_conflict";
      public const string StateChanged = "state_changed";
      public const string WorkerOperationFailed = "worker_operation_failed";
      public const string WorkerTimeout = "worker_timeout";
      public const string WorkerCrashed = "worker_crashed";
      public const string PostconditionFailed = "postcondition_failed";

      public static bool IsKnown(string? value);
  }
  ```

  Add `public string? FailureCategory { get; init; }` to `WorkerResponse`. Keep the wire name camelCase under the existing JSON policy and set it through object initialization/deserialization, never later mutation.

- [ ] **Step 4: Make the host result category-aware.**

  Change the record shape to:

  ```csharp
  public sealed record WorkerCallResult(
      bool Success,
      string Payload,
      string? Error,
      string? FailureCategory,
      IReadOnlyList<string> Warnings)
  ```

  Keep `Ok(string payload, IReadOnlyList<string>? warnings = null)`. Replace free-text-only failure construction with:

  ```csharp
  public static WorkerCallResult Fail(
      string failureCategory,
      string error,
      IReadOnlyList<string>? warnings = null)
  ```

  Validate `failureCategory` with `WorkerFailureCategories.IsKnown`. Add `ToEnvelopeText()` for direct lifecycle results. It serializes `{ success, payload, failureCategory, error, warnings }`; success and failure remain primary, warnings remain a separate array. Keep `ToText()` backward-compatible for unrelated read tools.

- [ ] **Step 5: Map transport and worker failures deterministically.**

  In `OpennessWorkerClient.InvokeWorkerAsync`:

  - worker `Success=false` with an approved category -> preserve it;
  - worker `Success=false` without a category -> `worker_operation_failed`;
  - request timeout -> `worker_timeout` and message `The write outcome is unknown. Inspect current project state before retrying.`;
  - crash, broken pipe, null response, or malformed JSON/protocol -> `worker_crashed` and the same inspect-before-retry guidance;
  - issue exactly one transport request; restart/dispose only for the next call.

  Create `WorkerOperationException` in the worker with get-only `FailureCategory` and `Warnings`. Its constructor rejects unknown categories and copies warnings into a read-only collection. All caller-validation sites must throw `WorkerOperationException(validation_error, ...)` explicitly. Add a specific catch in `Program` before the generic catch: `WorkerOperationException` preserves its category/warnings; the generic catch becomes `worker_operation_failed`. Do not classify arbitrary `ArgumentException` values as caller errors, and do not include stack traces in worker responses.

- [ ] **Step 6: Preserve categories at the guarded-write renderer.**

  Update `WriteSafetyTooling.BuildApplyResult` so failed `WorkerCallResult` values serialize `failureCategory`, `error`, and warnings, never a success-shaped result. Keep audit append and existing token consumption order unchanged.

- [ ] **Step 7: Rerun the timeout/crash tests and observe GREEN, then run verification and commit.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~WorkerResponseJsonTests|FullyQualifiedName~WorkerCallResultTests|FullyQualifiedName~OpennessWorkerClientIntegrationTests"
  dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
  dotnet test TiaMcpServer.sln --no-restore --no-build --verbosity minimal
  git diff --check
  git add TiaMcpServer.Contracts TiaMcpServer/Worker TiaMcpServer/Safety/WriteSafetyTooling.cs TiaMcpServer.OpennessWorker/WorkerOperationException.cs TiaMcpServer.OpennessWorker/Program.cs TiaMcpServer.FakeWorker/Program.cs TiaMcpServer.Tests
  git commit -m "feat: categorize worker outcomes"
  ```

---

## Task 2: Split direct status from lifecycle state probing

**Files:**

- Modify: `TiaMcpServer/Tools/ProjectLifecycleTools.cs`
- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs`
- Modify: `TiaMcpServer.OpennessWorker/Program.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/ProjectLifecycleService.cs`
- Modify: `TiaMcpServer.FakeWorker/Program.cs`
- Modify: `TiaMcpServer.Tests/ProjectLifecycleToolTests.cs`
- Modify: `TiaMcpServer.Tests/OpennessWorkerClientIntegrationTests.cs`
- Modify: `TiaMcpServer.Tests/McpToolSchemaTests.cs`

**Acceptance:** AC-005, AC-006, AC-007, AC-008, AC-009, AC-010, AC-012, AC-022, AC-044.

- [ ] **Step 1: Flip the obsolete direct-read binding test to RED.**

  Replace `UnboundSession_BindsToTheWorkerReportedPathAfterSuccess` with:

  ```csharp
  [Fact]
  public async Task UnboundSession_DirectStatusSuccess_DoesNotBindSession()
  ```

  Keep FakeWorker returning a successful status plus `ResolvedProjectPath`; assert `BoundProjectPath` remains null.

- [ ] **Step 2: Add routing and no-side-effect RED tests.**

  Add integration tests that record operation names and assert:

  - direct `get_project_status(projectPath:B)` uses only `get_project_status`;
  - no-project status returns `isOpen:false` and does not invoke open;
  - save, SaveAs, archive, and close preview/current-state paths use `probe_project_status_for_lifecycle`;
  - the probe operation never appears in MCP tool metadata;
  - the public surface stays at ten tools and seven lifecycle methods.

  Run:

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~OpennessWorkerClientIntegrationTests|FullyQualifiedName~ProjectLifecycleToolTests|FullyQualifiedName~McpToolSchemaTests"
  ```

  Observe RED before changing production routing.

- [ ] **Step 3: Add the private host/client operation.**

  Keep the public signature:

  ```csharp
  public Task<WorkerCallResult> GetProjectStatusAsync(string? projectPath)
  ```

  Add:

  ```csharp
  internal Task<WorkerCallResult> ProbeProjectStatusForLifecycleAsync(string? projectPath)
  ```

  The former sends operation `get_project_status` with no binding transition. The latter sends `probe_project_status_for_lifecycle`, is callable only from host lifecycle implementation, and also performs no host binding transition.

- [ ] **Step 4: Split worker service behavior.**

  Replace the current side-effecting `GetStatus` seam with:

  ```csharp
  public static ProjectStatusInfo GetStatusReadOnly(
      TiaPortalSession session,
      string? requestedProjectPath)

  public static ProjectStatusInfo ProbeStatusForLifecycle(
      TiaPortalSession session,
      string? projectPath)
  ```

  `GetStatusReadOnly` rules:

  - when `session.Project` is null, return `IsOpen=false` without calling `OpenProject`;
  - when a project is open and `requestedProjectPath` is null/equivalent, return its status;
  - when a different path is requested, throw `WorkerOperationException(binding_conflict, ...)` with guidance to use `open_project` deliberately;
  - never call `EnsureProject`, `OpenProject`, `Close`, or save methods.

  `ProbeStatusForLifecycle` retains the intentional write-state acquisition needed by save/SaveAs/archive/close safety checks.

- [ ] **Step 5: Add worker dispatch and reroute lifecycle tooling.**

  In worker `Program`, route the two operation names to the two service methods. In `ProjectLifecycleTools`, replace `GetProjectStatusAsync` with `ProbeProjectStatusForLifecycleAsync` only for save, SaveAs, archive, and close preview/apply current-state reads. Leave the user-facing `GetProjectStatus` on the read-only method and render it with `ToEnvelopeText()`.

- [ ] **Step 6: Update FakeWorker and schema guards.**

  FakeWorker must echo/record both operation names distinctly. Extend schema tests to prove `probe_project_status_for_lifecycle` is absent from MCP metadata and injected services remain hidden.

- [ ] **Step 7: Run verification and commit.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~OpennessWorkerClientIntegrationTests|FullyQualifiedName~ProjectLifecycleToolTests|FullyQualifiedName~McpToolSchemaTests"
  dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
  dotnet test TiaMcpServer.sln --no-restore --no-build --verbosity minimal
  git add TiaMcpServer/Tools/ProjectLifecycleTools.cs TiaMcpServer/Worker/OpennessWorkerClient.cs TiaMcpServer.OpennessWorker/Program.cs TiaMcpServer.OpennessWorker/Openness/ProjectLifecycleService.cs TiaMcpServer.FakeWorker/Program.cs TiaMcpServer.Tests
  git commit -m "fix: separate status reads from lifecycle probes"
  ```

---

## Task 3: Make binding transitions explicit and worker-grounded

**Files:**

- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs`
- Modify: `TiaMcpServer.Contracts/ProjectSessionBinding.cs` only if a small explicit clear helper is needed
- Modify: `TiaMcpServer.Tests/ProjectSessionBindingTests.cs`
- Modify: `TiaMcpServer.Tests/OpennessWorkerClientIntegrationTests.cs`
- Modify: `TiaMcpServer.Tests/OpennessWorkerClientWarningTests.cs`

**Acceptance:** AC-011–AC-014, AC-017, AC-045.

- [ ] **Step 1: Add binding-transition RED tests.**

  Add tests proving:

  ```csharp
  [Fact] public async Task FailedLifecycleCall_DoesNotChangeExistingBinding()
  [Fact] public async Task DirectStatusSuccess_DoesNotBindUnboundSession()
  [Fact] public async Task OpenSuccess_BindsWorkerResolvedPath_NotCallerPath()
  [Fact] public async Task CreateSuccess_BindsWorkerResolvedPath_NotCallerPath()
  [Fact] public async Task RequiredResolvedPathMissing_ReturnsPostconditionFailed()
  [Fact] public async Task CloseSuccess_ClearsBinding()
  [Fact] public async Task DirectStatusDivergence_WarnsButDoesNotAdoptWorkerPath()
  ```

  For the worker-ground-truth tests, deliberately make caller input and `ResolvedProjectPath` differ. Assert no caller-path fallback.

- [ ] **Step 2: Run focused tests and observe RED.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~ProjectSessionBindingTests|FullyQualifiedName~OpennessWorkerClientIntegrationTests|FullyQualifiedName~OpennessWorkerClientWarningTests"
  ```

- [ ] **Step 3: Replace implicit bind-on-success with an explicit transition mode.**

  Add a private host enum:

  ```csharp
  private enum BindingTransition
  {
      None,
      BindResolvedPath,
      Clear
  }
  ```

  Pass it to the common project-request helper. Apply transitions only after `WorkerCallResult.Success`:

  - direct status, internal probe, save, archive, and unrelated reads: `None`;
  - open, create, and supported SaveAs: `BindResolvedPath`;
  - close: `Clear`.

  For `BindResolvedPath`, require a non-empty worker `ResolvedProjectPath`, canonicalize it through the existing binding service, and return `postcondition_failed` without mutation if absent/invalid. Do not read binding values from payload text.

- [ ] **Step 4: Preserve divergence warnings without adoption.**

  When a direct read succeeds and reports a resolved path different from an existing binding, add one capped warning naming canonical A and B. Equivalent path spellings produce no warning. The binding remains A.

- [ ] **Step 5: Run verification and commit.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~ProjectSessionBindingTests|FullyQualifiedName~OpennessWorkerClientIntegrationTests|FullyQualifiedName~OpennessWorkerClientWarningTests"
  dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
  dotnet test TiaMcpServer.sln --no-restore --no-build --verbosity minimal
  git add TiaMcpServer/Worker/OpennessWorkerClient.cs TiaMcpServer.Contracts/ProjectSessionBinding.cs TiaMcpServer.Tests
  git commit -m "fix: bind lifecycle transitions to worker paths"
  ```

---

## Task 4: Prevent SaveAs from stranding host and worker state

**Files:**

- Modify: `TiaMcpServer/Tools/ProjectLifecycleTools.cs`
- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs`
- Modify: `TiaMcpServer.OpennessWorker/Program.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/ProjectLifecycleService.cs`
- Modify: `TiaMcpServer.FakeWorker/Program.cs`
- Modify: `TiaMcpServer.Tests/ProjectLifecycleToolTests.cs`
- Modify: `TiaMcpServer.Tests/OpennessWorkerClientIntegrationTests.cs`
- Modify: `TiaMcpServer.Tests/AuditIsolationTests.cs`

**Acceptance:** AC-015–AC-018, AC-022, AC-035, AC-042, AC-043.

- [ ] **Step 1: Add pre-preview rejection RED tests.**

  Add `SaveProjectAs_RebindFalse_RejectsBeforePreviewTokenGeneration`. Inject observable worker and audit fakes. Assert:

  - result category `validation_error`;
  - worker invocation count `0`;
  - no preview token in the response;
  - no audit file/entry;
  - existing binding unchanged.

- [ ] **Step 2: Add supported-path and uncertain-path RED tests.**

  Add:

  ```csharp
  [Fact] public async Task SaveProjectAs_RebindTrue_BindsOnlyWorkerCopiedPath()
  [Fact] public async Task SaveProjectAs_Failure_PreservesOriginalBinding()
  [Fact] public async Task SaveProjectAs_MissingCopiedPath_IsPostconditionFailedWithUncertainStateWarning()
  ```

  Run SaveAs-focused tests and observe RED.

- [ ] **Step 3: Reject unsupported mode at the first host boundary.**

  Keep the MCP schema parameter for compatibility, but at the first line of `ProjectLifecycleTools.SaveProjectAs` after primitive argument binding, return a categorized validation envelope when `rebind` is false. Do not call `WriteSafetyTooling.CreatePreview`, current-state probe, audit, or worker client.

  Mirror the rejection in `OpennessWorkerClient.SaveProjectAsAsync` and worker dispatch as defense in depth; those guards must also avoid Siemens calls.

- [ ] **Step 4: Make the worker establish and re-open the copied path.**

  Keep the existing public worker service signature:

  ```csharp
  public static ProjectLifecycleResultInfo SaveProjectAs(
      TiaPortalSession session,
      string? projectPath,
      string targetDirectory,
      string targetName,
      bool rebind)
  ```

  For `rebind:true`:

  1. validate target directory/name before Siemens invocation;
  2. call Siemens `SaveAs` once;
  3. discover the copied `.ap??` project path beneath the exact target directory;
  4. read the actual active project path from the worker session after `SaveAs` and require it to equal the discovered copied path;
  5. if discovery or active-path validation fails, throw `WorkerOperationException(postcondition_failed, ...)` with warning `Project state may have changed; inspect the open project before retrying.`;
  6. return the validated already-active copied path as `ResolvedProjectPath` and let the host bind only from that field.

  Do not close/reopen after `SaveAs`; live evidence shows Siemens already switches the active project, and a second lifecycle mutation could recreate the divergence being fixed.

- [ ] **Step 5: Rerun SaveAs tests and verify audit semantics.**

  Successful apply keeps the existing guarded audit flow. Validation failure writes no audit. Worker/postcondition failure is recorded as failure and never as successful completion.

- [ ] **Step 6: Run full verification and commit.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~SaveProjectAs|FullyQualifiedName~AuditIsolationTests"
  dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
  dotnet test TiaMcpServer.sln --no-restore --no-build --verbosity minimal
  git add TiaMcpServer/Tools/ProjectLifecycleTools.cs TiaMcpServer/Worker/OpennessWorkerClient.cs TiaMcpServer.OpennessWorker/Program.cs TiaMcpServer.OpennessWorker/Openness/ProjectLifecycleService.cs TiaMcpServer.FakeWorker/Program.cs TiaMcpServer.Tests
  git commit -m "fix: prevent save as session divergence"
  ```

---

## Task 5: Preserve direct warnings, budgets, safety, and no-retry invariants

**Files:**

- Modify: `TiaMcpServer/Worker/WorkerCallResult.cs`
- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs`
- Modify: `TiaMcpServer/Tools/ProjectLifecycleTools.cs`
- Modify: `TiaMcpServer/Safety/WriteSafetyTooling.cs`
- Modify: `TiaMcpServer/Safety/WriteSafetyService.cs`
- Modify: `TiaMcpServer.Tests/WorkerCallResultTests.cs`
- Modify: `TiaMcpServer.Tests/OpennessWorkerClientWarningTests.cs`
- Modify: `TiaMcpServer.Tests/OpennessWorkerClientIntegrationTests.cs`
- Modify: `TiaMcpServer.Tests/WriteToolSafetyTokenTests.cs`
- Modify: `TiaMcpServer.Tests/AuditIsolationTests.cs`

**Acceptance:** AC-019, AC-020, AC-021, AC-022, AC-032–AC-034, AC-043, AC-045.

- [ ] **Step 1: Add warning-envelope RED tests.**

  Prove successful direct status returns a separate warnings array, categorized failures remain failures when warnings exist, and direct warnings are capped to 20 lines/1,000 characters with explicit truncation.

- [ ] **Step 2: Add no-retry and safety regression tests.**

  Extend existing tests for missing/invalid/used token, `confirm=false`, reordered input, changed project path, and changed current state. Add assertions that state change renders `state_changed`, binding mismatch renders `binding_conflict`, and no failure is audit-recorded or returned as success.

- [ ] **Step 3: Run the focused suite and observe RED where new category/warning assertions are absent.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~WorkerCallResultTests|FullyQualifiedName~OpennessWorkerClientWarningTests|FullyQualifiedName~WriteToolSafetyTokenTests|FullyQualifiedName~AuditIsolationTests"
  ```

- [ ] **Step 4: Carry safety categories structurally and complete rendering.**

  Extend the existing safety validation result passed from `WriteSafetyService` to `WriteSafetyTooling.ValidateForApplyAsync` with a category field. Set `validation_error` for missing/invalid/used token, `confirm=false`, and invalid/reordered input; `state_changed` for state-hash/current-state mismatch; and `binding_conflict` for project-path mismatch. Do not infer categories by parsing human error text.

  Route all worker warnings through existing `CapWarnings`/`CapWarningLine` before constructing `WorkerCallResult`. Use `ToEnvelopeText()` for direct lifecycle results and `BuildApplyResult()` for guarded writes. Do not introduce an uncapped direct-result path.

- [ ] **Step 5: Prove one-request uncertain outcomes.**

  Run the timeout, crash, malformed, and null-response cases. Assert the failed write operation count is exactly one, then make one separate read request and assert the restarted worker handles that next request. This proves recovery without replay.

- [ ] **Step 6: Run the complete Plan 2 gate.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~ProjectLifecycleToolTests|FullyQualifiedName~ProjectSessionBindingTests|FullyQualifiedName~OpennessWorkerClientIntegrationTests|FullyQualifiedName~OpennessWorkerClientWarningTests|FullyQualifiedName~WorkerCallResultTests|FullyQualifiedName~WorkerResponseJsonTests|FullyQualifiedName~McpToolSchemaTests|FullyQualifiedName~WriteToolSafetyTokenTests|FullyQualifiedName~AuditIsolationTests"
  dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
  dotnet test TiaMcpServer.sln --no-restore --no-build --verbosity minimal
  ```

- [ ] **Step 7: Review and commit.**

  Review all Plan 2 commits as one diff. Check input validation, category completeness, protected-data rendering, audit ordering, and absence of retry loops. Commit any final focused adjustment:

  ```powershell
  git add TiaMcpServer TiaMcpServer.Contracts TiaMcpServer.OpennessWorker TiaMcpServer.FakeWorker TiaMcpServer.Tests
  git commit -m "fix: preserve lifecycle warnings and uncertain outcomes"
  ```

## Plan 2 Exit Gate

- [ ] Direct status is demonstrably side-effect-free and non-binding.
- [ ] Internal probe is absent from MCP metadata and used only by lifecycle write safety paths.
- [ ] Failed calls and successful reads never change binding.
- [ ] Open/create/SaveAs bind only to worker `ResolvedProjectPath`; close clears binding.
- [ ] `rebind:false` is rejected before preview/token/worker/audit.
- [ ] All seven categories round-trip and render deterministically.
- [ ] Warnings are separate, capped, and never mask failure.
- [ ] Timeout/crash/protocol-loss writes are issued once and never replayed.
- [ ] Full serial build and test suite pass before Plan 3.
