# C-7 Read Batch Cancellation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `plc_read`, `network_read`, `hmi_read` and `vci_read` stop before their next operation when the client cancels the request.

**Architecture:** Pass the MCP request `CancellationToken` from the four batch read tools into `StructuredOperationBatchExecutionEngine.ExecuteReadsAsync`. The engine checks the token before each item. The PLC and HMI invokers get the real token instead of `CancellationToken.None`. Cancellation is only checked between items: an in-flight worker call is not cancelled, and the worker timeout bounds it.

**Tech Stack:** C#/.NET 10, ModelContextProtocol 2.2.0 (binds a `CancellationToken` parameter automatically and keeps it out of the schema), xUnit, FakeWorker.

**Spec:**
- Review plan item C-7: [review](2026-10-17-codebase-simplification-review.md). The finding is TOOLS-12, using the verifier-corrected "preferred" option.
- [Workstream C plan](2026-10-21-workstream-c-plan.md)

**Branch:** `fix/read-tool-cancellation`, worktree `.worktrees/C-7`.

## Global Constraints

The [Workstream C global constraints](2026-10-21-workstream-c-plan.md#global-constraints-apply-to-every-wave-1-plan) apply, plus these:

- Every new parameter goes last and defaults to `default`, so existing positional callers still compile.
- A cancelled call throws `OperationCanceledException` and returns no batch document. Writes already rethrow after auditing (`WriteRun.cs` :89-92 and :209-212).
- Do not add checks inside an item (hardware pagination, HMI cursor resolution). Leave the order of `bind_project`'s parameters as it is.

## Review Focus

1. **Tool schemas.** The token must not appear in any tool's input schema. C-1 derives the allowed keys from the schema, and the Task 2 schema theory pins this.
2. **Overload binding.** `NetworkRead` passes `(operations, operationExecutor.ExecuteAsync, cancellationToken)`, which can only bind the `execute` overload. The other three tools pass the token fourth and bind `invoke`/`project`.
3. **Already-run items.** A cancelled batch drops items that already succeeded. A partial batch would be a contract change and is out of scope.
4. **HMI snapshots** created by items that already ran stay in `HmiSnapshotStore` until it evicts them (at most 4, with a 10-minute idle limit). This is harmless.
5. **Test isolation.** `FakeWorkerRequestLog` sets a process-wide environment variable. Keep these tests in `RealWorkerProcessCollection`, and create the log before the client's first request.

**Contract change:** none to schemas or responses. Release note: "plc_read, network_read, hmi_read and vci_read now stop before their next operation when the client cancels the request."

**Live verification:** not needed. The change is host-only and sits between items. A pre-cancelled token with a request-log assertion is deterministic, while a live mid-batch cancel depends on timing. No protocol-level `notifications/cancelled` test is added, because it would need a blocking FakeWorker scenario.

## Files

- Modify:
  - `TiaMcpServer/OperationBatches/StructuredOperationBatchExecutionEngine.cs`
  - `TiaMcpServer/Plc/PlcReadTools.cs`
  - `TiaMcpServer/Network/NetworkReadTools.cs`
  - `TiaMcpServer/Hmi/HmiReadTools.cs`
  - `TiaMcpServer/Vci/VciReadTools.cs`

  All of these are already linked in `TiaMcpServer.Tests.csproj`.
- Test, modify:
  - `TiaMcpServer.Tests/OperationBatches/StructuredOperationBatchTests.cs`
  - `TiaMcpServer.Tests/Tools/McpToolSchemaTests.cs`
- Test, create: `TiaMcpServer.Tests/OperationBatches/ReadToolCancellationTests.cs`
- Docs (last):
  - `docs/ARCHITECTURE.md` §7 "Batch and network execution"
  - the review plan's C-7 evidence

## Interfaces

Produced:
- `ExecuteReadsAsync<T>(IReadOnlyList<T> operations, Func<T, Task<StructuredOperationItem>> execute, CancellationToken cancellationToken = default) where T : IOperationBatchItem`
- `ExecuteReadsAsync<T>(IReadOnlyList<T> operations, Func<T, Task<WorkerCallResult>> invoke, Func<T, WorkerCallResult, StructuredOperationItem> project, CancellationToken cancellationToken = default) where T : IOperationBatchItem`
- `PlcReadTools.PlcRead(OpennessWorkerClient workerClient, PlcOperationRequest[] operations, CancellationToken cancellationToken = default)`
- `NetworkReadTools.NetworkRead(OpennessWorkerClient workerClient, NetworkOperationRequest[] operations, CancellationToken cancellationToken = default)`
- `HmiReadTools.HmiRead(OpennessWorkerClient workerClient, HmiPageCursorCodec cursorCodec, HmiOperationRequest[] operations, CancellationToken cancellationToken = default)`
- `VciReadTools.VciRead(OpennessWorkerClient workerClient, VciOperationRequest[] operations, CancellationToken cancellationToken = default)`

Consumed unchanged:
- `PlcWorkerInvoker.InvokeReadAsync(…, CancellationToken = default)` and `HmiWorkerInvoker.InvokeReadAsync(…, CancellationToken = default)`. Their existing `ThrowIfCancellationRequested` now sees a live token.
- `NetworkReadOperationExecutor.ExecuteAsync`
- `OpennessWorkerClient.ReadVciAsync`

---

### Task 1: The engine checks for cancellation before each item

- [ ] Add four `[Fact]`s to `StructuredOperationBatchTests`. Each uses three `TestOperation`s and counts how often each callback runs:
  - `ExecuteReadsAsync_Execute_PreCancelledToken_InvokesNoItem`: throws `OperationCanceledException` (via `ThrowsAnyAsync`), and `execute` runs 0 times.
  - `ExecuteReadsAsync_InvokeProject_PreCancelledToken_InvokesNoItem`: throws, and `invoke` and `project` each run 0 times.
  - `ExecuteReadsAsync_Execute_CancelDuringFirstItem_StopsBeforeSecondItem`: the callback cancels the source on its first call. It throws, and `execute` runs once.
  - `ExecuteReadsAsync_InvokeProject_CancelDuringFirstItem_StopsBeforeSecondItem`: throws, and `invoke` and `project` each run once.
- [ ] Add the trailing parameter to both overloads but do not use it yet. Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~StructuredOperationBatch"`. The four new tests fail with "No exception was thrown".
- [ ] Call `cancellationToken.ThrowIfCancellationRequested()` at the top of each loop iteration in both overloads.
- [ ] Run the same filter. Everything passes: the four new tests, plus the existing `ExecuteReadsAsync_*` tests in `StructuredOperationBatchTests` and `StructuredOperationBatchPayloadBudgetTests`.
- [ ] Commit `fix: check read batch cancellation between items`.

### Task 2: The token passes through the four read tools

- [ ] Add a `[Theory]` to `McpToolSchemaTests`: `ReadBatchSchemas_ExposeOnlyOperations(Type toolType, string methodName)`. Its four rows are `PlcRead`, `NetworkRead`, `HmiRead` and `VciRead`. Each tool's schema properties must be exactly `["operations"]`. The theory passes before and after the change; it is a guard.
- [ ] Create `ReadToolCancellationTests`, `[Collection(RealWorkerProcessCollection.Name)]`.
  - Setup per test:
    1. `TempAuditDirectory`, then `Directory.CreateDirectory`. The directory is not created for you, and the FakeWorker log append fails without it.
    2. `FakeWorkerRequestLog`.
    3. The client, built the same way `PlcReadToolsTests.CreateClient` builds it (`McpAccessMode.ReadOnly`).
  - `PlcRead_CancelledToken_ThrowsWithoutWorkerRequest` (scenario `plc-read-roundtrip`):
    - First make one uncancelled `get_block_content` call and record the worker method count, which must be greater than 0.
    - A pre-cancelled call then throws, and the count is unchanged.
  - `NetworkRead_CancelledToken_ThrowsWithoutWorkerRequest` (`search_equipment_catalog`): throws, and the log is empty.
  - `HmiRead_CancelledToken_ThrowsWithoutWorkerRequest` (`list_hmi_devices`, with a process-scoped `HmiPageCursorCodec`): throws, and the log is empty.
  - `VciRead_CancelledToken_ThrowsWithoutWorkerRequest` (`list_workspaces`): throws, and the log is empty.
- [ ] Add the trailing parameter to the four tools but do not forward it yet. Run with `FullyQualifiedName~ReadToolCancellationTests|FullyQualifiedName~McpToolSchemaTests`. The four cancellation tests fail; the schema theory passes.
- [ ] Forward the token into `ExecuteReadsAsync` in all four tools. In `PlcReadTools` and `HmiReadTools`, also replace `CancellationToken.None` in the invoker lambda with the token.
- [ ] Run the same filter. Everything passes.
- [ ] Commit `fix: pass request cancellation through batch read tools`.

### Task 3: Full verification

- [ ] Build, then run the full suite with coverage under `TestResults/c7`, per the global constraints. Record the counts.

### Task 4: Docs (last)

- [ ] In `docs/ARCHITECTURE.md` §7, after "Read batches execute items independently…", add one sentence. It should say that read batches check caller cancellation before each item, that an in-flight worker call is not cancelled and is bounded by the worker timeout, and that a cancelled call returns no batch document.
- [ ] Add a C-7 evidence sub-bullet to the review plan with:
  - the date;
  - the chosen option (wire the token through, rather than delete the invoker parameters);
  - the counts;
  - "No live TIA verification was performed."
- [ ] Commit `docs: record read batch cancellation`.
