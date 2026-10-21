# C-3 Failure-Category Constants Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The PLC, HMI and VCI write verifiers share one precondition-refusal set from `WorkerFailureCategories`, and host HMI/VCI code stops hard-coding failure-category strings.

**Architecture:** Add two members to the shared Contracts class. They replace three private `NeverRan` sets and every remaining raw category literal in host `Hmi/` and `Vci/`. Behaviour does not change.

**Tech Stack:** C#, netstandard2.0 Contracts, net10.0 host, xUnit.

**Spec:** Review plan item C-3 ([review](2026-10-17-codebase-simplification-review.md); findings TOOLS-9, HOSTDOMAINS-13, CONTRACTS-4) and the [Workstream C plan](2026-10-21-workstream-c-plan.md).

**Branch:** `refactor/c3-failure-categories`, worktree `.worktrees/c-3`.

**Execution:** inline (executing-plans), then one fresh reviewer.

## Global Constraints

- **No `TransportLoss` set.** It would have only one consumer, so `VciWriteVerifier` uses the existing constants inline (maintainer default, 2026-10-21). Record this deviation from the review line in the evidence.
- **VCI keeps whole-batch verification.** Do not move VCI to `_attempted` filtering; the verifier rejected that under TOOLS-9.
- **Leave these alone:** `VciWriteDomain`'s breadcrumb timeout/crash check (:66) and `OpennessWorkerClient`'s binding-invalidation set (:2353). Both already use constants.
- **Shell:** run `dotnet` from PowerShell, never Bash.
- **Build:** `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true`. Expect no warnings beyond the seven existing test-assembly nullable warnings.
- **Tests:** run only each task's focused filters. Do not run the full suite per task; the orchestrator runs it once per branch, one worktree at a time.
- **Commits:** commit after each task, as a one-line conventional commit with no body. After each commit, expect exactly one new commit and a clean `git status`.
- **Docs come last.**
- **Repo hygiene:** no TIA project names or paths in the repo.
- **Before deleting a member,** confirm its consumers with `git grep`.

## Review Focus

1. **Never-ran items.** A precondition-refused item must still produce `notVerified`, never `superseded`. Pinned by `PlcWriteVerifierTests.FailedItemThatNeverRanIsObservedWithoutFailingOrSuperseding`, `HmiWriteVerifierTests.NeverRanDoesNotSupersedeEarlierSuccess` and `VciWriteVerifierTests`. All three run in Task 2.
2. **VCI fatal categories.** `portal_lost`, `worker_timeout` and `worker_crashed` must still suppress every read. Pinned by `VciGuardedWriteMcpTests`, including `PortalLostBeforeExpansionPreservesFailureAndSuppressesAllReads`, run in Task 2.
3. **Wrong constant.** A literal swapped for the wrong constant still compiles. The HMI test namespace and the sweep in Task 3 catch it.
4. **Check names stay literal.** `HmiWriteVerifier`'s three verification check names (one `"protocol_error"`, two `"postcondition_failed"`) are not failure categories. Pinned by the Task 3 sweep, which expects exactly these three hits.
5. **Null category.** `IsPreconditionRefusal(null)` must be false, because VCI passes `o.Failure?.Category`. Pinned by the `null` row in Task 1.

**Contract change:** none; the wire values are identical.

**Live verification:** not needed.

## After the tasks

These are not implementer tasks. They run after Task 3 and before Task 4.

### Branch verification (orchestrator, serialized across worktrees)

1. Run the stub build.
2. Run the full suite with coverage under `TestResults/c3`. Use process-local `TEMP`/`TMP` set to `TestResults/c3/temp`, `TiaMcpServer.Tests/coverage.runsettings`, and the single-thread xUnit settings from `.github/workflows/ci.yml`.
   - Expected: 0 failed and 0 skipped.
3. Run `scripts/verify-coverage-threshold.ps1 -MinimumLineRate 0.80`.
   - Expected: it passes.
4. Record the per-file line rates for the five changed host files from the Cobertura report.

Then the final whole-branch review follows, per the execution skill.

---

### Task 1: Shared precondition-refusal set

**Files:**
- Modify: `TiaMcpServer.Contracts/Worker/WorkerFailureCategories.cs`. Put the set next to `Known` and the predicate next to `IsKnown`.
- Create: `TiaMcpServer.Tests/Worker/WorkerFailureCategoriesTests.cs`. Contracts is a `ProjectReference`, so no csproj change is needed.

**Interfaces:**
- Consumes: the existing constants and `IsKnown`.
- Produces:
  - `public static IReadOnlyCollection<string> PreconditionRefusals { get; }`, backed by a private ordinal `HashSet<string>` of `ValidationError`, `TargetNotFound`, `TargetAmbiguous`, `StateChanged`, `GuardBlocked`, `BindingConflict` and `AccessDenied`.
  - `public static bool IsPreconditionRefusal(string? category)`. The comparison is ordinal, and `null` returns false. XML summary: "Categories of a precondition refusal: the item never changed TIA state."

- [ ] **Step 1: Write the tests.**
  - `PreconditionRefusals_HasExactlySevenWireValues`: order-insensitive equality with these literal strings: `validation_error`, `target_not_found`, `target_ambiguous`, `state_changed`, `guard_blocked`, `binding_conflict`, `access_denied`.
  - `PreconditionRefusals_AreAllKnown`: every member satisfies `IsKnown`.
  - `IsPreconditionRefusal_MatchesSetOnly`, a `[Theory]`:
    - true for each of the seven;
    - false for `portal_lost`, `worker_timeout`, `worker_crashed`, `worker_operation_failed`, `protocol_error`, `postcondition_failed`, `""`, `null` and `"Validation_Error"`.
- [ ] **Step 2: Add both members, with `IsPreconditionRefusal` returning `false`.**
  - Run: `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~WorkerFailureCategoriesTests`
  - Expected: the seven `true` rows FAIL. Everything else passes.
- [ ] **Step 3: Implement the predicate.**
  - Run: the same filter.
  - Expected: all PASS.
- [ ] **Step 4: Commit.** Message: `feat: add WorkerFailureCategories.PreconditionRefusals`

**Task test command:** `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~WorkerFailureCategoriesTests`

### Task 2: Verifiers use the shared set

**Files:**
- Modify: `TiaMcpServer/Plc/PlcWriteVerifier.cs`. Delete `NeverRan` and its summary (:30-36), and change its uses at :84 and :146.
- Modify: `TiaMcpServer/Hmi/HmiWriteVerifier.cs`. Delete `NeverRan` (:15-16), change its use at :39, and add `using TiaMcpServer.Contracts.Worker;`.
- Modify: `TiaMcpServer/Vci/VciWriteVerifier.cs`. Delete `NeverRan` (:11-12), change its uses at :21 and :27 and the fatal literals at :17, add the same `using`, and add a comment.
- Test: no new tests. The pins in Review Focus 1–2 are the guard.

**Interfaces:**
- Consumes (Task 1): `WorkerFailureCategories.IsPreconditionRefusal(string?)`.
- Produces: nothing new.

- [ ] **Step 1: Record a green baseline.**
  - Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~WriteVerifierTests|FullyQualifiedName~TiaMcpServer.Tests.Vci"`
  - Expected: PASS. Record the count.
- [ ] **Step 2: PLC.** Use `WorkerFailureCategories.IsPreconditionRefusal(failure.Category)` at :84 and :146.
- [ ] **Step 3: HMI.** Use `WorkerFailureCategories.IsPreconditionRefusal(refusal.Category)` at :39.
- [ ] **Step 4: VCI.**
  - At :21 and :27, use `WorkerFailureCategories.IsPreconditionRefusal(o.Failure?.Category)` and drop the `?? ""`.
  - At :17, use `is WorkerFailureCategories.PortalLost or WorkerFailureCategories.WorkerTimeout or WorkerFailureCategories.WorkerCrashed`.
  - Add a comment of at most three lines on `VerifyAsync`, saying why VCI verifies the whole batch:
    - skipped items still need a per-item `notVerified`;
    - the worker can refuse after dispatch with `state_changed`/`target_not_found`;
    - the inferred attempted list drives `superseded`.
- [ ] **Step 5: Rerun.**
  - Run: the same filter.
  - Expected: PASS, with the same count as the baseline.
- [ ] **Step 6: Commit.** Message: `refactor: share precondition refusals across write verifiers`

**Task test command:** the Step 1 filter.

### Task 3: Remaining HMI category literals

**Files:**
- Modify: `TiaMcpServer/Hmi/HmiWorkingState.cs`.
- Modify: `TiaMcpServer/Hmi/HmiWritePlanner.cs`.
- Modify: `TiaMcpServer/Hmi/HmiWriteVerifier.cs`.
- The first two files need `using TiaMcpServer.Contracts.Worker;`.

Task 2 shifted `HmiWriteVerifier` by one line, so the steps locate targets by content.

**Interfaces:**
- Consumes: the existing constants `TargetNotFound`, `TargetAmbiguous`, `ValidationError`, `TargetKindUnsupported`, `WorkerOperationFailed` and `ProtocolError`.
- Produces: nothing.

- [ ] **Step 1: Record a green baseline.**
  - Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~TiaMcpServer.Tests.Hmi"`
  - Expected: PASS. Record the count.
- [ ] **Step 2: Replace the literals with constants.**
  - `HmiWorkingState`:
    - :47 `"target_not_found"` becomes `TargetNotFound`.
    - :66 `"target_not_found"`/`"target_ambiguous"` become `TargetNotFound`/`TargetAmbiguous`.
    - :119 and :121 `"validation_error"` become `ValidationError`.
  - `HmiWritePlanner`:
    - :178 becomes `TargetNotFound`/`TargetAmbiguous`.
    - :179 becomes `TargetKindUnsupported`.
    - :286 becomes `TargetNotFound`.
    - :287 and :329 become `WorkerOperationFailed`.
    - :296 and :345 become `ProtocolError`.
  - `HmiWriteVerifier`: in the optional-read helper, the `"target_not_found"` comparison on the result's failure category becomes `TargetNotFound`.
- [ ] **Step 3: Sweep.**
  - Run: `git grep -nE '"(validation|binding|portal|state|target|unsupported|worker|postcondition|access|protocol|invalid|cursor|snapshot|result|guard)_[a-z_]+"' -- TiaMcpServer/Hmi TiaMcpServer/Vci TiaMcpServer/Plc/PlcWriteVerifier.cs`
  - Expected: exactly three hits, all in `HmiWriteVerifier.cs`: one `"protocol_error"` and two `"postcondition_failed"` check names.
- [ ] **Step 4: Rerun.**
  - Run: the Step 1 filter.
  - Expected: PASS, with the same count as the baseline.
- [ ] **Step 5: Commit.** Message: `refactor: use failure-category constants in HMI host code`

**Task test command:** the Step 1 filter.

### Task 4: Evidence (docs last)

The controller supplies the branch-verification values: the full-suite count, the skip count and the per-file line rates. Use them as given. Do not run or invent them.

**Files:** Modify `docs/superpowers/plans/2026-10-17-codebase-simplification-review.md`, item C-3.

**Interfaces:** none.

- [ ] **Step 1: Add an evidence sub-bullet under C-3, in the format of the B-9 bullets.** Include:
  - the date;
  - the full-suite count with zero skips;
  - the changed-file line rates;
  - that `TransportLoss` was not added, and why;
  - that the three check-name literals were kept;
  - "No live TIA verification was performed; not needed because the wire values are identical."
- [ ] **Step 2: Check.**
  - Run: `git grep -n "TransportLoss" -- docs/superpowers/plans/2026-10-17-codebase-simplification-review.md`
  - Expected: at least two hits: the original C-3 line and the new evidence bullet.
- [ ] **Step 3: Commit.** Message: `docs: record C-3 evidence`

**Task test command:** the Step 2 grep.
