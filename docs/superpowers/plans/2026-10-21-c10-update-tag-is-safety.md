# C-10 Remove `update_tag` `isSafety` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the `isSafety` field from `update_tag` end to end. Today the field is advertised, but every write that sets it fails. After this change, a caller that sends it is rejected before anything runs.

**Architecture:**
- Delete the field from:
  - the host spec, DTO, working-state preview, invoker and worker client;
  - the Contracts `WorkerRequest`;
  - the worker's `UpdateTag` path and `TagMutationService`.
- The existing `[JsonUnmappedMemberHandling(Disallow)]` on `PlcOperationRequest` then rejects the field at argument binding.

**Tech Stack:** net10.0 host, netstandard2.0 Contracts, net48 worker, xUnit, FakeWorker.

**Spec:** Review plan item C-10 ([review](2026-10-17-codebase-simplification-review.md), finding WORKERDOMAINS-9) and the [Workstream C plan](2026-10-21-workstream-c-plan.md).

**Branch:** `refactor/c10-update-tag-is-safety`, worktree `.worktrees/c-10`.

**Execution:** inline (executing-plans), then one fresh reviewer.

## Global Constraints

- **Rejection path.** Use the generic unknown-member rejection. Do not add a `RetiredItemMembers` entry (maintainer default, 2026-10-21).
- **Rejection shape before and after C-1.** Until C-1 merges, the rejection is the SDK's generic tool error. After C-1, which covers nested item errors, it becomes the canonical `validation_error`. Tests assert `isError`, no worker call and no audit record. They never assert message text.
- **One PR.** Host, Contracts and worker change together.
- **Shell.** Run `dotnet` from PowerShell, never Bash.
- **Build.** `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true`. Capture main's warning count before Task 1. Expect no warnings beyond it.
- **Tests.**
  - Run only each task's focused filters. Do not run the full suite per task; the orchestrator runs it once per branch, one worktree at a time.
  - Every touched source is already linked into `TiaMcpServer.Tests`.
- **Commits.** Commit after each task, with a one-line conventional message and no body. Expected: one new commit, and `git status` is clean.
- **Docs come last.**
- **No TIA project names or paths** in the repo.

## Review Focus

1. **`plc_read` is affected too.** It binds `PlcOperationRequest` as well. Its schema loses `isSafety`, and an item carrying it is still rejected without a worker call. Pinned by `PlcReadItemSchema_HasNoIsSafety` and `PlcReadItemWithIsSafety_RejectedBeforeWorker` (Task 1).
2. **Dry runs.** A dry run used to preview `isSafety` as a planned change. `dryRun: true` must now be rejected the same way. Pinned by the `("true", true)` row of `UpdateTagIsSafety_RejectedBeforeWorker` (Task 1).
3. **Null value.** `isSafety: null` used to pass and is now rejected. Pinned by the `("null", false)` row (Task 1).
4. **No audit record.** The binding rejection writes no audit record. Pinned by the no-audit assertions (Task 1).
5. **Other `update_tag` fields still apply.** Pinned by `UpdateTag_WithoutIsSafety_StillApplies` (Task 1) and the existing `PlcWritePreconditionsTests` (Task 2).

**Contract change (release note):** "plc_write/plc_read: removed the `isSafety` field from `update_tag` items. It was advertised but every write that set it failed; items carrying it are now rejected before anything runs."

**Live verification:** not needed. The removed worker branch always threw before any Siemens call.

## After the tasks

These steps are not implementer tasks. They run after Task 2 and before Task 3.

### Branch verification (orchestrator, serialized across worktrees)

Run the stub build, then the full suite with coverage under `TestResults/c10`:
- set `TEMP`/`TMP` for the process to `TestResults/c10/temp`;
- use `TiaMcpServer.Tests/coverage.runsettings`;
- use the single-thread xUnit settings from `.github/workflows/ci.yml`.

Expected: 0 failed, 0 skipped, and `scripts/verify-coverage-threshold.ps1 -MinimumLineRate 0.80` passes.

Then the final whole-branch review follows, per the execution skill.

---

### Task 1: Host spec stops advertising and accepting `isSafety`

**Files:**
- Modify `TiaMcpServer/Plc/PlcOperationCatalog.cs`: the optional entry at :41 and the `OperationFields` row at :82.
- Modify `TiaMcpServer/Plc/PlcOperationRequest.cs`: the property at :72-73.
- Modify `TiaMcpServer/Plc/PlcWorkerInvoker.cs`: the argument at :58.
- Modify `TiaMcpServer/Plc/PlcWorkingState.cs`: the `.Concat(Change("isSafety", …))` at :276.
- Modify `TiaMcpServer/Worker/OpennessWorkerClient.cs`: the parameter at :1312 and the assignment at :1330.
- Test: `TiaMcpServer.Tests/Plc/PlcOperationCatalogTests.cs` and `TiaMcpServer.Tests/Plc/PlcGuardedWriteMcpTests.cs`.

**Interfaces:**
- Produces: `OpennessWorkerClient.UpdateTagAsync(string? plcName, string tableName, string? folderPath, string name, string? newName, string? dataType, string? logicalAddress, bool? externalAccessible, bool? externalVisible, bool? externalWritable, string? projectPath)`. Its only caller is `PlcWorkerInvoker`.
- Removed: `PlcOperationRequest.IsSafety`. `WorkerRequest.IsSafety` stays until Task 2, with no host writer.

The MCP setup used below is the same as `LegacyOrMalformedArguments_DoNotReachWorker`:
- production surface, ReadWrite, scenario `plc-write-roundtrip`;
- `TempAuditDirectory` plus `Directory.CreateDirectory`, and `FakeWorkerRequestLog`;
- `before = requests.Methods()`.

The scenario seeds tag `Start` in PLC_2 "Default tag table".

- [ ] **Step 1: Write and adjust the tests.**
  - `PlcOperationCatalogTests.ExpectedWriteFields` (:386): remove `"isSafety"` from the `update_tag` optional array. This is the input to `WriteSpecsKeepTheRetiredBatchFieldSets`.
  - `PlcOperationCatalogTests.RejectsIsSafetyOnUpdateTag`, next to `RejectsUnknownMember` (:112): deserializing `{"operationId":"a","operation":"update_tag","tableName":"T","name":"X","isSafety":true}` with `JsonSerializerDefaults.Web` throws `JsonException`.
  - `PlcGuardedWriteMcpTests.RegisteredSchema_OnlyOperationsAndBooleanDryRun`: add `Assert.DoesNotContain("isSafety", item)`.
  - `PlcGuardedWriteMcpTests.UpdateTagIsSafety_RejectedBeforeWorker(string json, bool dryRun)`:
    - `[Theory]` rows: `("true", false)`, `("false", false)`, `("null", false)`, `("true", true)`.
    - Item: `{operationId:"tag", operation:"update_tag", plcName:"PLC_2", tableName:"Default tag table", name:"Start", isSafety:<json>}`, sent with the row's `dryRun`.
    - Assert: `reply.IsError` is true, `requests.Methods()` equals `before`, and there are no audit lines.
  - `PlcGuardedWriteMcpTests.PlcReadItemSchema_HasNoIsSafety`: the registered `plc_read` item schema does not contain `isSafety`.
  - `PlcGuardedWriteMcpTests.PlcReadItemWithIsSafety_RejectedBeforeWorker`:
    - `plc_read` with `{operationId:"r", operation:"get_block_content", blockPath:"PLC_2/Main", isSafety:true}`.
    - Assert: `reply.IsError` is true, the worker methods are unchanged, and there are no audit lines.
    - This is a regression lock: it passes today, because the catalog rejects the field before dispatch.
  - `PlcGuardedWriteMcpTests.UpdateTag_WithoutIsSafety_StillApplies`:
    - `plc_write` with `{operationId:"tag", operation:"update_tag", plcName:"PLC_2", tableName:"Default tag table", name:"Start", externalWritable:false}`.
    - Assert: phase `applied`, `success: true`, `update_tag` among the worker methods, and exactly one audit line.
    - This is a regression lock: it passes today.
- [ ] **Step 2: Run the tests.**
  - Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~PlcOperationCatalogTests|FullyQualifiedName~PlcGuardedWriteMcpTests"`
  - Expected to FAIL:
    - `WriteSpecsKeepTheRetiredBatchFieldSets`;
    - `RejectsIsSafetyOnUpdateTag` (nothing thrown);
    - the schema assertion;
    - every `UpdateTagIsSafety_RejectedBeforeWorker` row (the call binds, and is applied, previewed or audited);
    - `PlcReadItemSchema_HasNoIsSafety`.
  - Expected to PASS: the two regression locks.
- [ ] **Step 3: Remove the host field.** Delete the catalog entries, the property, the invoker argument, the working-state change, and the client parameter and assignment.
- [ ] **Step 4: Run the PLC tests.**
  - Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Plc"`
  - Expected: all PASS.
- [ ] **Step 5: Commit.** Message: `refactor: remove isSafety from plc_write update_tag spec`

**Task test command:** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Plc"`

### Task 2: Contracts and worker

**Files:**
- Modify `TiaMcpServer.Contracts/Worker/WorkerRequest.cs`: the summary and property at :173-174.
- Modify `TiaMcpServer.OpennessWorker/Program.cs`: the argument at :1076.
- Modify `TiaMcpServer.OpennessWorker/Openness/Plc/TagMutationService.cs`: the parameter at :76 and the throw at :80-84.
- Test: `TiaMcpServer.Tests/Worker/PlcWritePreconditionsTests.cs`: one new test, plus the five call sites at :119, :123, :172, :176 and :198.

**Interfaces:**
- Consumes (Task 1): the host no longer writes `WorkerRequest.IsSafety`.
- Produces: `TagMutationService.UpdateTag(ProjectBase project, string? plcName, string tableName, string? folderPath, string name, string? newName, string? dataType, string? logicalAddress, bool? externalAccessible, bool? externalVisible, bool? externalWritable)`. It now takes 11 arguments instead of 12.
- Removed: `WorkerRequest.IsSafety`.

- [ ] **Step 1: Write the test.** Add `PlcWritePreconditionsTests.UpdateTag_HasNoIsSafetyParameterOrWorkerField`, following `NetworkFieldForwardingTests.WorkerRequest_NoLongerCarriesTheConfigureOnlyNameFields`:
  - `typeof(WorkerRequest).GetProperty("IsSafety")` is null;
  - no `TagMutationService.UpdateTag` parameter is named `isSafety`.
- [ ] **Step 2: Run it.**
  - Run: `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~PlcWritePreconditions`
  - Expected: the new test FAILS.
- [ ] **Step 3: Remove the field from Contracts and the worker.** Delete the `TagMutationService.UpdateTag` parameter and its throw block, the `Program.UpdateTag` argument, and `WorkerRequest.IsSafety`. Drop the trailing `null` at the five test call sites.
- [ ] **Step 4: Build and run.**
  - Run: the stub build (includes the net48 worker), then `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Plc"`.
  - Expected: the build succeeds with no warnings beyond the baseline, and all tests PASS.
- [ ] **Step 5: Check that only tests still mention the field.**
  - Run: `git grep -n -i issafety -- ':!docs' ':!TiaMcpServer.Tests'`, then `git grep -l -i issafety -- TiaMcpServer.Tests`.
  - Expected: the first prints nothing. The second lists exactly `TiaMcpServer.Tests/Plc/PlcOperationCatalogTests.cs`, `TiaMcpServer.Tests/Plc/PlcGuardedWriteMcpTests.cs` and `TiaMcpServer.Tests/Worker/PlcWritePreconditionsTests.cs`.
- [ ] **Step 6: Commit.** Message: `refactor: drop isSafety from worker request and tag mutation`

**Task test command:** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Plc"`

### Task 3: Docs and evidence

The controller supplies the branch-verification counts and coverage. Use them as given; do not run or invent them.

**Files:**
- Modify `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md`.
- Modify `docs/IMPROVEMENT_LOG.md`.
- Modify `docs/superpowers/plans/2026-10-17-codebase-simplification-review.md` (item C-10).

**Interfaces:** none.

- [ ] **Step 1: PLC summary.** Remove `isSafety` from the `update_tag` row, and delete the bullet "`isSafety` is not verified after the write."
- [ ] **Step 2: `IMPROVEMENT_LOG.md`.**
  - Remove M4 from the open plc_write follow-ups.
  - In the completed section at the end, add "`update_tag` `isSafety` removed — DONE (review C-10, M4)". Say the field was removed rather than verified, and that `plc_write` and `plc_read` items carrying it are rejected at binding.
  - Drop `isSafety` from the "Next round" `create_tag` list.
  - Leave the historical Round 4 note alone.
- [ ] **Step 3: Review plan, item C-10.** Add sub-bullets in the format of the B-9 bullets:
  - implementation;
  - regression evidence (the Task 1 and Task 2 tests);
  - full offline evidence (controller-supplied);
  - "No live TIA verification was performed; not needed because the removed worker branch always threw before any Siemens call."
- [ ] **Step 4: Check.**
  - Run: `git grep -n -i issafety -- docs ':!docs/superpowers'`
  - Expected: only the historical Round 4 note and the new completed entry.
- [ ] **Step 5: Commit.** Message: `docs: record isSafety removal from update_tag`

**Task test command:** the Step 4 grep.
