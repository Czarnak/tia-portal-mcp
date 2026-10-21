# C-10 Remove `update_tag` `isSafety` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the advertised-but-always-failing `isSafety` field from `update_tag` end to end, so a caller that sends it is rejected before anything runs.

**Architecture:** Delete the field from:
- the host spec and DTO (`PlcOperationRequest`), the working-state preview, the invoker and the worker client;
- the Contracts `WorkerRequest`;
- the worker `UpdateTag` path and `TagMutationService`.

After that, the existing `[JsonUnmappedMemberHandling(Disallow)]` on `PlcOperationRequest` rejects the field during argument binding.

**Tech Stack:** net10.0 host, netstandard2.0 Contracts, net48 worker, xUnit, FakeWorker.

**Spec:**
- Review plan item C-10: [review](2026-10-17-codebase-simplification-review.md), finding WORKERDOMAINS-9.
- [Workstream C plan](2026-10-21-workstream-c-plan.md).

**Branch:** `refactor/c10-update-tag-is-safety`, worktree `.worktrees/c-10`.

## Global Constraints

The [Workstream C global constraints](2026-10-21-workstream-c-plan.md#global-constraints-apply-to-every-wave-1-plan) apply. In addition:

- **Use the generic unknown-member rejection.** Do not add a `RetiredItemMembers` entry (maintainer default, 2026-10-21).
- **Rejection shape changes with C-1.** Until C-1 merges, the rejection is the SDK's generic tool error, which does not name the field. After C-1, whose scope covers nested item errors, it becomes the canonical `validation_error`. Tests must assert `isError`, no worker call and no audit record. They must not assert the message text, so they survive either order of merging.
- **Ship host, Contracts and worker together.** They change in the same PR and the same package, so there is no version skew.

## Review Focus

1. **`plc_read` is affected too.** `plc_read` binds `PlcOperationRequest` as well, so its item schema also loses `isSafety`. Any PLC item that carries it, read or write, now fails at binding.
2. **The old error changes kind.** Before, a non-`update_tag` operation with `isSafety` got the catalog error "`'isSafety' is not valid for <op>`". For `plc_write` that error was audited. Now it is an unaudited binding rejection.
3. **Null is rejected too.** `isSafety: null` used to pass, because the presence check is `is not null`. Now any `isSafety` key fails.
4. **The dry run used to lie.** It showed `isSafety` as a planned change (`PlcWorkingState.cs` :276), and the real call then failed. Removing that preview line is part of the fix.
5. **Other `update_tag` fields are untouched.** They still flow through the invoker, the client and `TagMutationService` in the same positional order. This is covered by `PlcWritePreconditionsTests` and the FakeWorker round trip.

**Contract change (release note):** "plc_write/plc_read: removed the `isSafety` field from `update_tag` items. It was advertised but every write that set it failed; items carrying it are now rejected before anything runs."

**Live verification:** Not needed. The removed worker branch always threw before any Siemens call, and the new rejection happens at host binding. That rejection is proven through the production MCP surface with the FakeWorker.

## Files

- **Modify:**
  - `TiaMcpServer/Plc/PlcOperationCatalog.cs`: the optional entry (:41) and the `OperationFields` row (:82).
  - `TiaMcpServer/Plc/PlcOperationRequest.cs`: the property (:72-73).
  - `TiaMcpServer/Plc/PlcWorkerInvoker.cs`: the argument (:58).
  - `TiaMcpServer/Plc/PlcWorkingState.cs`: the `.Concat(Change("isSafety", …))` call (:276).
  - `TiaMcpServer/Worker/OpennessWorkerClient.cs`: the parameter (:1312) and the assignment (:1330).
  - `TiaMcpServer.Contracts/Worker/WorkerRequest.cs`: the property (:174-175).
  - `TiaMcpServer.OpennessWorker/Program.cs`: the argument (:1076).
  - `TiaMcpServer.OpennessWorker/Openness/Plc/TagMutationService.cs`: the parameter (:76) and the throw (:80-84).
- **Test:**
  - `TiaMcpServer.Tests/Plc/PlcOperationCatalogTests.cs`
  - `TiaMcpServer.Tests/Plc/PlcGuardedWriteMcpTests.cs`
  - `TiaMcpServer.Tests/Worker/PlcWritePreconditionsTests.cs`: the new test, plus five call sites at :119, :123, :172, :176 and :198.
  - No csproj change. Every touched source is already linked.
- **Docs (last):**
  - `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md`
  - `docs/IMPROVEMENT_LOG.md`
  - the review plan's C-10 evidence

## Interfaces

- `OpennessWorkerClient.UpdateTagAsync(string? plcName, string tableName, string? folderPath, string name, string? newName, string? dataType, string? logicalAddress, bool? externalAccessible, bool? externalVisible, bool? externalWritable, string? projectPath)`. Its only caller is `PlcWorkerInvoker`.
- `TagMutationService.UpdateTag(ProjectBase project, string? plcName, string tableName, string? folderPath, string name, string? newName, string? dataType, string? logicalAddress, bool? externalAccessible, bool? externalVisible, bool? externalWritable)`. It takes 11 arguments instead of 12.
- Removed: `PlcOperationRequest.IsSafety` and `WorkerRequest.IsSafety`.

---

### Task 1: Host spec stops advertising and accepting `isSafety`

- [ ] **Red, catalog.** In `PlcOperationCatalogTests.ExpectedWriteFields` (:386), remove `"isSafety"` from the `update_tag` optional fields. `WriteSpecsKeepTheRetiredBatchFieldSets` then fails.
- [ ] **Red, binding.** Add `RejectsIsSafetyOnUpdateTag` next to `RejectsUnknownMember` (:112).
  - Input: `{"operationId":"a","operation":"update_tag","tableName":"T","name":"X","isSafety":true}`.
  - Expected: `Assert.Throws<JsonException>`, using `JsonSerializerDefaults.Web`.
- [ ] **Red, schema.** In `PlcGuardedWriteMcpTests.RegisteredSchema_OnlyOperationsAndBooleanDryRun`, add `Assert.DoesNotContain("isSafety", item)`.
- [ ] **Red, MCP.** Add the theory `PlcGuardedWriteMcpTests.UpdateTagIsSafety_RejectedBeforeWorker(string json)`.
  - Data: `"true"`, `"false"`, `"null"`.
  - Setup: reuse the setup from `LegacyOrMalformedArguments_DoNotReachWorker` (production surface, ReadWrite, `FakeWorkerRequestLog`, audit directory created).
  - Call: one `update_tag` item on `PLC_2` / "Default tag table", naming a tag the scenario seeds, with `isSafety:<json>`.
  - Assert: `reply.IsError`, the worker methods are unchanged, and there are no audit lines. Do not assert any text.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~PlcOperationCatalogTests|FullyQualifiedName~PlcGuardedWriteMcpTests"`. All four fail. In the MCP theory, the calls bind and are audited today.
- [ ] Remove the host field: catalog entries, `PlcOperationRequest.IsSafety`, the invoker argument, the working-state change, and the client parameter and assignment. `WorkerRequest.IsSafety` stays for now, with no host writer.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Plc"`. All pass.
- [ ] Commit `refactor: remove isSafety from plc_write update_tag spec`.

### Task 2: Contracts and worker

- [ ] **Red.** Add `PlcWritePreconditionsTests.UpdateTag_HasNoIsSafetyParameterOrWorkerField`, following the reflection precedent in `NetworkFieldForwardingTests.WorkerRequest_NoLongerCarriesTheConfigureOnlyNameFields`.
  - `typeof(WorkerRequest).GetProperty("IsSafety")` is null.
  - No `TagMutationService.UpdateTag` parameter is named `isSafety`.

  Run it and see it fail.
- [ ] Remove the `TagMutationService.UpdateTag` parameter and its throw block, the `Program.UpdateTag` argument, and `WorkerRequest.IsSafety`. Then drop the trailing `null` from the five test call sites.
- [ ] Build the stub solution, net48 worker included. It must succeed with no warnings beyond main's baseline. Capture that baseline first.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Plc"`. All pass.
- [ ] Commit `refactor: drop isSafety from worker request and tag mutation`.

### Task 3: Full offline verification

- [ ] Run `git grep -n -i issafety -- ':!docs'`. It should print nothing.
- [ ] Run the full suite with coverage under `TestResults/c10`, per the global constraints.
- [ ] Get an independent review of the diff.

### Task 4: Docs (last)

- [ ] `PLC_OPERATIONS_SUMMARY.md`: remove `isSafety` from the `update_tag` row, and delete the bullet "`isSafety` is not verified after the write."
- [ ] `IMPROVEMENT_LOG.md`:
  - Remove M4 from the open plc_write follow-ups.
  - Add a completed entry: "`update_tag` `isSafety` removed — DONE (review C-10, M4)". Say the field was removed rather than verified, and that items carrying it are rejected at binding.
  - Drop `isSafety` from the "Next round" `create_tag` attribute list.
  - Leave the historical Round 4 note alone.
- [ ] Review plan C-10 evidence: implementation summary, regression evidence, full offline evidence, and "No live TIA verification was performed; not needed because …".
- [ ] Run `git grep -n -i issafety -- docs ':!docs/superpowers'`. Only the historical Round 4 note and the new completed entry should match.
- [ ] Commit `docs: record isSafety removal from update_tag`.
