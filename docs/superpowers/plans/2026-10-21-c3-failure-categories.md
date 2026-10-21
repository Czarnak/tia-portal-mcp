# C-3 Failure-Category Constants Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The PLC, HMI and VCI write verifiers share one precondition-refusal set from `WorkerFailureCategories`. Host HMI/VCI code stops hard-coding failure-category strings.

**Architecture:** Two members are added to the shared Contracts class. Three private `NeverRan` sets and all remaining raw category literals in host `Hmi/` and `Vci/` code are replaced with them. Behaviour does not change.

**Tech Stack:** C#, netstandard2.0 Contracts, net10.0 host, xUnit.

**Spec:**
- Review plan item C-3: [review](2026-10-17-codebase-simplification-review.md), findings TOOLS-9, HOSTDOMAINS-13 and CONTRACTS-4.
- [Workstream C plan](2026-10-21-workstream-c-plan.md).

**Branch:** `refactor/c3-failure-categories`, worktree `.worktrees/c-3`.

## Global Constraints

The [Workstream C global constraints](2026-10-21-workstream-c-plan.md#global-constraints-apply-to-every-wave-1-plan) apply. In addition:
- **No `TransportLoss` set.** It would have one consumer (`VciWriteVerifier`), which uses the constants inline instead. This was a maintainer default on 2026-10-21. Record the deviation from the review line in the evidence.
- **Keep VCI's whole-batch verification.** Do not move VCI to `_attempted` filtering, which the verifier rejected under TOOLS-9.
- **Leave two sites alone:**
  - `VciWriteDomain.cs` :66 is the breadcrumb timeout/crash check.
  - `OpennessWorkerClient.cs` :2353 is the binding-invalidation set.

## Review Focus

1. **Precondition refusals reported as "never ran".** A precondition-refusal category must still produce `notVerified` without `superseded` in all three verifiers. This is pinned by:
   - `PlcWriteVerifierTests.FailedItemThatNeverRanIsObservedWithoutFailingOrSuperseding`
   - `HmiWriteVerifierTests.NeverRanDoesNotSupersedeEarlierSuccess`
   - `VciWriteVerifierTests` (`state_changed` → `notVerified`)
2. **VCI fatal categories.** `portal_lost`, `worker_timeout` and `worker_crashed` must still suppress every read. This is pinned by `VciGuardedWriteMcpTests` and `PortalLostBeforeExpansionPreservesFailureAndSuppressesAllReads`.
3. **Category mapping.** A literal swapped for the wrong constant compiles. The Task 3 sweep and the HMI test namespace catch it.
4. **Check names are not categories.** `HmiWriteVerifier` :45, :53 and :194 are verification **check names** (`protocol_error`, `postcondition_failed`). Keep them as literals so check vocabulary stays separate from failure vocabulary.
5. **Null handling.** `IsPreconditionRefusal(null)` must be false. VCI passes `o.Failure?.Category`.

## Files

- **Modify `TiaMcpServer.Contracts/Worker/WorkerFailureCategories.cs`.** Add the set next to `Known` and the predicate next to `IsKnown`.
- **Modify `TiaMcpServer/Plc/PlcWriteVerifier.cs`.** Delete `NeverRan` and its summary (:30-36), and update its uses at :84 and :146.
- **Modify `TiaMcpServer/Hmi/HmiWriteVerifier.cs`:**
  - delete `NeverRan` (:15-16);
  - update its use at :39;
  - replace the literal at :272;
  - add the `using`.
- **Modify `TiaMcpServer/Vci/VciWriteVerifier.cs`:**
  - delete `NeverRan` (:11-12);
  - update its uses at :21 and :27;
  - replace the fatal literals at :17;
  - add the `using` and a comment of three lines or fewer.
- **Modify `TiaMcpServer/Hmi/HmiWorkingState.cs`** (:47, :66, :119, :121) and **`TiaMcpServer/Hmi/HmiWritePlanner.cs`** (:178, :179, :286, :287, :296, :329, :345). Each file needs `using TiaMcpServer.Contracts.Worker;`.
- **Create the test `TiaMcpServer.Tests/Worker/WorkerFailureCategoriesTests.cs`.** Contracts is a `ProjectReference`, so the csproj does not change.

## Interfaces

**Produced:**
- `public static IReadOnlyCollection<string> PreconditionRefusals { get; }`
  - Backed by a private ordinal `HashSet<string>`: `ValidationError`, `TargetNotFound`, `TargetAmbiguous`, `StateChanged`, `GuardBlocked`, `BindingConflict`, `AccessDenied`.
  - netstandard2.0 has no `IReadOnlySet`.
- `public static bool IsPreconditionRefusal(string? category)`: ordinal comparison; `null` returns false.
  - XML summary: "Categories of a precondition refusal: the item never changed TIA state."

**Consumed:** the existing constants `PortalLost`, `WorkerTimeout`, `WorkerCrashed`, `TargetNotFound`, `TargetAmbiguous`, `ValidationError`, `TargetKindUnsupported`, `WorkerOperationFailed` and `ProtocolError`.

---

### Task 1: Shared precondition-refusal set

- [ ] Create `WorkerFailureCategoriesTests` with three tests:
  - `PreconditionRefusals_HasExactlySevenWireValues`: order-insensitive equality with the **literal** strings `validation_error`, `target_not_found`, `target_ambiguous`, `state_changed`, `guard_blocked`, `binding_conflict` and `access_denied`.
  - `PreconditionRefusals_AreAllKnown`: every member satisfies `IsKnown`.
  - `IsPreconditionRefusal_MatchesSetOnly` (`[Theory]`): true for each of the seven; false for `portal_lost`, `worker_timeout`, `worker_crashed`, `worker_operation_failed`, `protocol_error`, `postcondition_failed`, `""`, `null` and `"Validation_Error"`.
- [ ] Add both members, with `IsPreconditionRefusal` returning false. Run `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~WorkerFailureCategoriesTests`. The true rows fail.
- [ ] Implement both members, then run the filter again. All pass.
- [ ] Commit `feat: add WorkerFailureCategories.PreconditionRefusals`.

### Task 2: Verifiers use the shared set

- [ ] Record a baseline with the `FullyQualifiedName~WriteVerifierTests|FullyQualifiedName~TiaMcpServer.Tests.Vci` filter. This is a pure refactor, so the Review Focus 1–2 pins act as the red-to-green guard.
- [ ] Update `PlcWriteVerifier`: delete `NeverRan` and call `WorkerFailureCategories.IsPreconditionRefusal(failure.Category)` at :84 and :146.
- [ ] Update `HmiWriteVerifier`: delete `NeverRan` and call `IsPreconditionRefusal(refusal.Category)` at :39.
- [ ] Update `VciWriteVerifier`:
  - Delete `NeverRan`.
  - At :21 and :27, call `IsPreconditionRefusal(o.Failure?.Category)` and drop the `?? ""`.
  - At :17, use the pattern `is WorkerFailureCategories.PortalLost or WorkerFailureCategories.WorkerTimeout or WorkerFailureCategories.WorkerCrashed`.
- [ ] Add a comment on `VciWriteVerifier.VerifyAsync` saying why VCI verifies the whole batch:
  - skipped items still need a per-item `notVerified`;
  - the worker can refuse after dispatch with `state_changed` or `target_not_found`;
  - the inferred attempted list drives `superseded`.
- [ ] Run the same filter. The pass count matches the baseline.
- [ ] Commit `refactor: share precondition refusals across write verifiers`.

### Task 3: Remaining HMI category literals

- [ ] Record a baseline with the `FullyQualifiedName~TiaMcpServer.Tests.Hmi` filter.
- [ ] `HmiWorkingState`:
  - :47 and :66 become `TargetNotFound`/`TargetAmbiguous`;
  - :119 and :121 become `ValidationError`.
- [ ] `HmiWritePlanner`:
  - :178 and :286 become `TargetNotFound`/`TargetAmbiguous`;
  - :179 becomes `TargetKindUnsupported`;
  - :287 and :329 become `WorkerOperationFailed`;
  - :296 and :345 become `ProtocolError`.
- [ ] `HmiWriteVerifier` :272 becomes `TargetNotFound`.
- [ ] Sweep with a regex `search_text` over `TiaMcpServer/Hmi`, `TiaMcpServer/Vci` and `TiaMcpServer/Plc/PlcWriteVerifier.cs`, looking for any quoted `WorkerFailureCategories` wire value. Expect exactly three hits: `HmiWriteVerifier.cs` :45, :53 and :194, which are the check names.
- [ ] Run the same filter. The pass count matches the baseline.
- [ ] Commit `refactor: use failure-category constants in HMI host code`.

### Task 4: Full verification and evidence (docs last)

- [ ] Build, then run the full suite with coverage under `TestResults/c3`, per the global constraints.
- [ ] Add an evidence sub-bullet under C-3 in the review plan. Include:
  - the date;
  - the pass count, with zero skips;
  - coverage of the changed lines;
  - that `TransportLoss` was not added, and why;
  - that the three check-name literals were kept;
  - "No live TIA verification was performed; not needed because the wire values are identical."
- [ ] Commit `docs: record C-3 evidence`.
