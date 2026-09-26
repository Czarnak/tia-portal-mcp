# Wave 1 K1 Batch Failure Categories Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Preserve the worker's validated `failureCategory` on each failed generic batch item so callers can inspect read and write failures without parsing the `result` string.

**Architecture:** `WorkerCallResult` already carries a closed-vocabulary category. Add an optional category to the host-only `OperationBatchResult`, copy it in the shared execution engine, and expose it in the shared read/write formatter. The field is additive to the response; preview and apply safety decisions remain where they are.

**Tech Stack:** C# / .NET 10 host, xUnit, the existing FakeWorker, PowerShell, .NET Framework 4.8 worker built with Siemens reference stubs for offline verification.

**Spec:** [Open Bug Parallel Pull-Request Delivery Design](../specs/2026-09-26-open-bug-parallel-pr-delivery-design.md), §K1 and its delivery invariants.

## Global Constraints

- Scope is issue #82 checklist item 7 only. Use `Refs #82` in the PR; do not close umbrella issue #82.
- Prepare the implementation branch `fix/batch-failure-categories` from freshly verified `main` after this approved spec and plan are available there. Target the PR at `main`. Do not use the current documentation-planning branch as an implementation base.
- Wave 1 lanes L1, K1, P1, and N1 may run concurrently only with disjoint file ownership. If K1 needs a reserved file, pause immediately; a same-wave owner cannot merge early. The orchestrator must obtain review and approval for a whole-file ownership/plan amendment before freeze, or remove/defer K1 and reapprove the wave barrier.
- A failed worker item carries exactly its validated `WorkerCallResult.FailureCategory`; never infer a category from the `Error:` text or substitute a generic category. Successful, skipped, and omitted items serialize `failureCategory: null`. Apply the same item shape to `execute_read_batch` and `apply_write_batch`.
- Preserve the existing `result` string, `warnings`, item order and counts, read continuation after failure, write stop-on-first-failure behavior, top-level error envelopes, token binding and consumption, audit mechanism, access modes, and no-rollback contract.
- Do not edit worker operations, Siemens-facing contracts, network structured-batch responses, the FakeWorker program, package dependencies, or tool input schemas. No live TIA Portal or PLC operation is authorized or needed for K1.
- The documentation-planning PR owns indexing this new historical plan in `docs/README.md` and `docs/superpowers/README.md`; those indexes are outside the K1 implementation allowlist.

## File Ownership

| Role | K1 allowlist |
| --- | --- |
| Production | `TiaMcpServer/OperationBatches/OperationBatchResult.cs`; `TiaMcpServer/OperationBatches/OperationBatchExecutionEngine.cs`; `TiaMcpServer/OperationBatches/OperationBatchResultFormatter.cs` |
| Contracts and fixtures | None. `TiaMcpServer/Worker/WorkerCallResult.cs` supplies the existing category and remains unchanged. |
| Tests | `TiaMcpServer.Tests/OperationBatches/OperationBatchKernelTests.cs`; `TiaMcpServer.Tests/OperationBatches/OperationBatchPayloadBudgetTests.cs`; `TiaMcpServer.Tests/Batch/WriteBatchToolsBehaviorTests.cs` |
| Maintained documentation | `docs/SupportedOperations/README.md` only |

Reserve `TiaMcpServer/Tools/ProjectWriteTools.cs`, `TiaMcpServer/Worker/OpennessWorkerClient.cs`, `TiaMcpServer.OpennessWorker/Openness/TiaPortalSession.cs` and lifecycle tests for L1; `CompileChecker`, `CrossReferenceReader`, their tests, and the PLC operation reference for P1; `NetworkDeviceCreator`, its tests, and the network operation reference for N1. K1 also leaves `TiaMcpServer.FakeWorker/Program.cs`, both host and worker `Program.cs`, `TiaMcpServer.Contracts/`, `TiaMcpServer/Batch/WriteBatchTools.cs`, `TiaMcpServer/Safety/WriteSafetyService.cs`, `README.md`, and `docs/ARCHITECTURE.md` untouched. The last two maintained documents belong to the serialized Wave 1 fan-in reconciliation.

## Review Focus

1. A failed read followed by a successful read reports the first item's `validation_error`, continues to the second item, and keeps its order. Task 1 extends `ExecuteReadsAsync_FailureDoesNotStopLaterItems`; Task 2 checks its JSON.
2. A failed write after one successful write reports the failed item's category and leaves the third item skipped with a null category. Task 1 extends `ApplyWritesAsync_StopsAndMarksLaterItemsSkipped`; Task 2 checks the registered FakeWorker path.
3. A successful payload beginning with literal `Error:` stays successful with a null category. Task 1 extends `ExecuteReadsAsync_ErrorPrefixedPayloadIsStillSucceeded`.
4. A failed read with warnings retains the category, failed status, and warnings or their existing truncation marker under a tight response budget. Task 2 adds a budget regression.
5. A malformed worker response after a possibly completed write retains `worker_crashed` and uncertain-state guidance, does not expose the malformed payload, and does not execute later items. Task 2 extends the existing registered write behavior test.

---

### Task 1: Carry the worker category into the generic item result

**Files:**
- Modify: `TiaMcpServer.Tests/OperationBatches/OperationBatchKernelTests.cs`
- Modify: `TiaMcpServer/OperationBatches/OperationBatchResult.cs`
- Modify: `TiaMcpServer/OperationBatches/OperationBatchExecutionEngine.cs`

**Interfaces:**
- Consumes: `WorkerCallResult.FailureCategory` (`string?`, approved category on failure, null on success).
- Produces: `OperationBatchResult(..., IReadOnlyList<string>? Warnings = null, string? FailureCategory = null)`. The optional final member preserves existing positional construction; `OperationBatchExecutionEngine.ToResult` copies the worker category. Skipped items use the default null.

- [ ] **Step 1: Write the failing kernel tests.** Extend `ExecuteReadsAsync_FailureDoesNotStopLaterItems` to assert `validation_error` on the failed item and null on the subsequent success. Extend `ApplyWritesAsync_StopsAndMarksLaterItemsSkipped` to assert `worker_operation_failed` on the failed item and null on the first success and final skipped item. Assert null in `ExecuteReadsAsync_ErrorPrefixedPayloadIsStillSucceeded`.
- [ ] **Step 2: Run the RED.** Run `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -m:1 /p:UseTiaPortalReferenceStubs=true --filter "FullyQualifiedName~OperationBatchKernelTests"`. Expected: compilation fails with `CS1061` because `OperationBatchResult` has no `FailureCategory` member. Do not proceed on an unrelated restore, SDK, or build failure.
- [ ] **Step 3: Implement the smallest GREEN.** Append the optional nullable member to `OperationBatchResult` after `Warnings`. Pass `workerResult.FailureCategory` as the last argument in `OperationBatchExecutionEngine.ToResult`; leave `ApplyWritesAsync`'s skipped-result construction unchanged. Do not add category parsing or new validation here: `WorkerCallResult.Fail` already validates categories.
- [ ] **Step 4: Re-run the focused test.** Use the Step 2 command. Expected: `OperationBatchKernelTests` passes, including existing order, warning, count, and stop behavior.
- [ ] **Step 5: Inspect and commit this boundary.** Run `git diff --check` and review the three-file diff. Commit only these files with `git commit -m "fix(batch): retain worker failure categories in item results"` after staging those exact paths.

### Task 2: Publish categories through the shared read/write response

**Files:**
- Modify: `TiaMcpServer.Tests/OperationBatches/OperationBatchKernelTests.cs`
- Modify: `TiaMcpServer.Tests/OperationBatches/OperationBatchPayloadBudgetTests.cs`
- Modify: `TiaMcpServer.Tests/Batch/WriteBatchToolsBehaviorTests.cs`
- Modify: `TiaMcpServer/OperationBatches/OperationBatchResultFormatter.cs`
- Modify: `docs/SupportedOperations/README.md`

**Interfaces:**
- Consumes: Task 1's `OperationBatchResult.FailureCategory`.
- Produces: `OperationBatchResultFormatter.Read` and `.Apply` keep their existing envelopes and `operations[]` fields and add `failureCategory` to every item. A worker failure emits the exact category; success, skip, and omission emit JSON null.

- [ ] **Step 1: Write the formatter RED.** Extend `ReadFormatter_FailureClearsSuccess` with a failed result constructed using `FailureCategory: WorkerFailureCategories.ValidationError`, then assert `operations[1].failureCategory == "validation_error"` and `operations[0].failureCategory` is JSON null. Extend `ApplyFormatter_CountsSucceededFailedAndSkipped` with `FailureCategory: WorkerFailureCategories.WorkerOperationFailed` on the failed item, then assert `operations[1].failureCategory == "worker_operation_failed"` and explicit null for the succeeded and skipped items. Extend `ReadFormatter_ProjectsCountsWarningsAndOmissions` to assert explicit null for the omitted item. Keep assertions that `result` remains a string, `warnings` and counts retain their old values, and top-level `success` remains correct.
- [ ] **Step 2: Run the RED.** Run the Task 1 focused command. Expected: the new assertions fail because `JsonElement.GetProperty("failureCategory")` cannot find that property; the tests must compile using Task 1's record member.
- [ ] **Step 3: Implement the formatter GREEN.** Add `failureCategory = result.FailureCategory` to the single `OperationBatchResultFormatter.Project` projection, after its existing fields. Preserve `Read`, `Apply`, and both `Error` overloads otherwise. `TiaJson.Presentation` serializes nulls, so no custom serializer or parallel read/write projection is needed.
- [ ] **Step 4: Re-run the focused formatter test.** Use the Task 1 focused command. Expected: the new read/apply shape tests pass and old count/result/warnings assertions remain green.
- [ ] **Step 5: Add the public-path and budget regressions.** In the existing `WriteBatchToolsBehaviorTests.ApplyWriteBatch_RegisteredPath_PreservesRequestOrder_StopsOnProtocolFailure_SkipsLaterItems_AndWritesOnlyInjectedAudit`, assert null on the first and third items and `worker_crashed` on the second, preserving the uncertain-state text, raw-payload redaction, one failure, one skip, and audit assertions. In `OperationBatchPayloadBudgetTests`, add a near-cap read response containing a failed item with `worker_operation_failed` and warnings; assert the failure category survives `OperationBatchPayloadBudget.Apply`, the failed count stays one, existing detail/warning truncation behavior remains valid, and `OperationBatchResultFormatter.Read(...).Length` is at most the configured `maxBatchChars`. The budget engine already measures the final formatter output; changing its algorithm is outside K1 unless this test exposes a concrete defect requiring replan.
- [ ] **Step 6: Run the focused integration slice.** Run `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -m:1 /p:UseTiaPortalReferenceStubs=true --filter "FullyQualifiedName~OperationBatchKernelTests|FullyQualifiedName~OperationBatchPayloadBudgetTests|FullyQualifiedName~WriteBatchToolsBehaviorTests"`. Expected: all selected tests pass without a live TIA process. Confirm the FakeWorker scenario still stops after the malformed second response.
- [ ] **Step 7: Document the additive item field.** In `docs/SupportedOperations/README.md`, state that generic `execute_read_batch` and `apply_write_batch` items carry `failureCategory`, that failures preserve approved worker categories, and that success/skipped/omitted items have null. Keep the existing tool names and response structure. No new document or index entry is needed.
- [ ] **Step 8: Inspect and commit this boundary.** Run `git diff --check`, review only the Task 2 allowlisted paths, and commit them with `git commit -m "fix(batch): publish categories in generic item responses"`.

## Offline Verification and Handoff

- [ ] From the implementation checkout, run the focused command from Task 2 again after both commits. Expected: all selected tests pass.
- [ ] Run `dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true`. Expected: serial solution build succeeds without Siemens assemblies.
- [ ] Run `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -m:1 /p:UseTiaPortalReferenceStubs=true`. Expected: full offline suite passes. Preserve any unrelated failure output as evidence rather than calling it a K1 regression.
- [ ] Run `dotnet pack TiaMcpServer/TiaMcpServer.csproj -c Release -m:1 /p:UseTiaPortalReferenceStubs=true`. Expected: the local package builds; do not install or publish it.
- [ ] Refresh the remote read, record `git rev-parse origin/main`, then run `git diff --check` and `git diff --name-only origin/main...HEAD`. Expected: only K1 allowlisted files changed. Verify unchanged preview/apply token hashes, access-mode registration, top-level errors, audit call placement, worker and network contracts by scoped diff inspection and existing tests. A real-reference build and live V21 run are unnecessary for this host-only projection.
- [ ] Obtain independent review of the two commits and the public item shape. Open a PR targeting `main` with `Refs #82` only after the plan's review and execution authority are satisfied. Do not claim the whole umbrella issue is fixed.
- [ ] Mark K1 `offline-ready`; it requires no live TIA call, but it participates in the hard Wave 1 barrier. Do not start any lane's live acceptance or merge K1 until L1, K1, P1, and N1 are all offline-ready and the frozen combined-wave candidate has completed its authorized live program.
- [ ] Merge serially with other Wave 1 PRs. After merge, rerun the focused and full offline gates on current `main` before I2 or G4 relies on the category shape. Reconcile `README.md` and `docs/ARCHITECTURE.md` through the shared Wave 1 fan-in PR; do not edit them concurrently in K1.
