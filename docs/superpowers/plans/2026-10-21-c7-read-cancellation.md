# C-7 Read Batch Cancellation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `plc_read`, `network_read`, `hmi_read` and `vci_read` stop before their next operation when the client cancels the request.

**Architecture:**
- The four batch read tools pass the MCP request `CancellationToken` into `StructuredOperationBatchExecutionEngine.ExecuteReadsAsync`, which checks it before each item.
- The PLC and HMI invokers get the real token instead of `CancellationToken.None`.
- An in-flight worker call is not cancelled; the worker timeout still bounds it.

**Tech Stack:** C#/.NET 10, ModelContextProtocol 2.2.0 (binds a `CancellationToken` parameter to the request token and keeps it out of the schema), xUnit 2.9.3, FakeWorker.

**Spec:** Review plan item C-7 ([review](2026-10-17-codebase-simplification-review.md); finding TOOLS-12, verifier-corrected preferred option), and the [Workstream C plan](2026-10-21-workstream-c-plan.md).

**Branch:** `fix/read-tool-cancellation`, worktree `.worktrees/C-7`.

**Execution:** inline (executing-plans), then one fresh reviewer.

## Global Constraints

- **New parameters go last** and default to `default`.
- **A cancelled call throws** `OperationCanceledException` and returns no batch document. This matches writes, which rethrow after auditing (`WriteRun.cs` :89-92, :209-212).
- **Check at item boundaries only.** Do not add checks inside an item (hardware pagination, HMI cursor resolution).
- **Leave `bind_project` alone.** Do not change its parameter order.
- **Test isolation.** Tests that use `FakeWorkerRequestLog` stay in `RealWorkerProcessCollection`, because the log sets a process-wide environment variable. Create the log before the client's first request.
- **Shell.** Run `dotnet` from PowerShell, never Bash.
- **Build.** `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true`. Expect no warnings beyond the seven existing test-assembly nullable warnings.
- **Tests.** Run only each task's focused filters, not the full suite per task. The orchestrator runs the full suite once per branch, one worktree at a time.
- **Commits.** Commit after each task as a one-line conventional commit with no body. Expected after each commit: one new commit, and `git status` is clean.
- **Docs come last.**
- **No TIA project names or paths** in the repo.

## Review Focus

1. **The token must never appear in any tool's input schema.** C-1 derives allowed keys from the schema. Pinned by `ReadBatchSchemas_ExposeOnlyOperations` (Task 2).
2. **`NetworkRead` must bind the `execute` overload with the token.** Pinned by `NetworkRead_CancelledToken_ThrowsWithoutWorkerRequest` (Task 2).
3. **A cancel during item 1 must not run item 2.** The call throws rather than returning a partial document. Pinned by the two `…CancelDuringFirstItem_StopsBeforeSecondItem` tests (Task 1).
4. **Uncancelled reads stay unchanged.** Pinned by:
   - the existing `ExecuteReadsAsync_*` tests in `StructuredOperationBatchTests` and `StructuredOperationBatchPayloadBudgetTests` (Task 1);
   - the succeeded sanity item in `PlcRead_CancelledToken_ThrowsWithoutWorkerRequest` (Task 2).
5. **The PLC and HMI invoker lambdas must forward the token, not `CancellationToken.None`.** The pre-cancelled tests cannot see this, because the engine throws first. Pinned by the `CancellationToken.None` grep (Task 2, Step 3).

**Contract change:** none to schemas or responses. **Release note:** "plc_read, network_read, hmi_read and vci_read now stop before their next operation when the client cancels the request."

**Live verification:** not needed. The change is host-only, between items. No protocol-level `notifications/cancelled` test is added, because it would need a blocking FakeWorker scenario.

## After the tasks

These are not implementer tasks. They run after Task 2 and before Task 3.

### Branch verification (orchestrator, serialized across worktrees)

Run the stub build, then the full suite with coverage under `TestResults/c7`:
- process-local `TEMP`/`TMP` set to `TestResults/c7/temp`;
- `TiaMcpServer.Tests/coverage.runsettings`;
- the single-thread xUnit settings from `.github/workflows/ci.yml`.

Expected: 0 failed and 0 skipped, and `scripts/verify-coverage-threshold.ps1 -MinimumLineRate 0.80` passes.

Then the final whole-branch review runs, per the execution skill.

---

### Task 1: The engine checks for cancellation before each item

**Files:**
- Modify: `TiaMcpServer/OperationBatches/StructuredOperationBatchExecutionEngine.cs` (already linked)
- Test: `TiaMcpServer.Tests/OperationBatches/StructuredOperationBatchTests.cs`

**Interfaces:**
- Consumes: the existing `IOperationBatchItem`, `StructuredOperationItem`, `WorkerCallResult`, and the test helpers `TestOperation` and `ProjectLikeAContract`.
- Produces:
  - `ExecuteReadsAsync<T>(IReadOnlyList<T> operations, Func<T, Task<StructuredOperationItem>> execute, CancellationToken cancellationToken = default) where T : IOperationBatchItem`
  - `ExecuteReadsAsync<T>(IReadOnlyList<T> operations, Func<T, Task<WorkerCallResult>> invoke, Func<T, WorkerCallResult, StructuredOperationItem> project, CancellationToken cancellationToken = default) where T : IOperationBatchItem`

- [ ] **Step 1: Write the tests.** Each test uses three `TestOperation`s, counts the callback calls, and asserts with `Assert.ThrowsAnyAsync<OperationCanceledException>`.
  - `ExecuteReadsAsync_Execute_PreCancelledToken_InvokesNoItem`: the token is already cancelled. The call throws, and `execute` runs 0 times.
  - `ExecuteReadsAsync_InvokeProject_PreCancelledToken_InvokesNoItem`: the token is already cancelled. The call throws, and `invoke` and `project` each run 0 times.
  - `ExecuteReadsAsync_Execute_CancelDuringFirstItem_StopsBeforeSecondItem`: the callback cancels the source on its first call. The call throws, and `execute` runs once.
  - `ExecuteReadsAsync_InvokeProject_CancelDuringFirstItem_StopsBeforeSecondItem`: same setup. The call throws, and `invoke` and `project` each run once.
- [ ] **Step 2: Add the trailing parameter to both overloads but do not use it yet.**
  - Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~StructuredOperationBatch"`
  - Expected: the four new tests FAIL with "No exception was thrown". The existing tests pass.
- [ ] **Step 3: Call `cancellationToken.ThrowIfCancellationRequested()` at the top of each loop iteration in both overloads.**
  - Run: the same filter.
  - Expected: all PASS.
- [ ] **Step 4: Commit.** Message: `fix: check read batch cancellation between items`

**Task test command:** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~StructuredOperationBatch"`

### Task 2: The token flows through the four read tools

**Files:**
- Modify (all already linked):
  - `TiaMcpServer/Plc/PlcReadTools.cs`
  - `TiaMcpServer/Network/NetworkReadTools.cs`
  - `TiaMcpServer/Hmi/HmiReadTools.cs`
  - `TiaMcpServer/Vci/VciReadTools.cs`
- Test:
  - Modify `TiaMcpServer.Tests/Tools/McpToolSchemaTests.cs`. Replace `NetworkRead_SchemaExposesOnlyOperations` (:267) with the theory below.
  - Create `TiaMcpServer.Tests/OperationBatches/ReadToolCancellationTests.cs`.

**Interfaces:**
- Consumes (Task 1): both `ExecuteReadsAsync` overloads with `cancellationToken`.
- Consumes (unchanged):
  - `PlcWorkerInvoker.InvokeReadAsync(…, CancellationToken = default)`
  - `HmiWorkerInvoker.InvokeReadAsync(…, CancellationToken = default)`
  - `NetworkReadOperationExecutor.ExecuteAsync`
  - `FakeWorkerUiOpenProject.ForWorkerRelativePath(string)`
  - `new FakeWorkerRequestLog(string auditDirectory)`
  - `TempAuditDirectory`
  - `new HmiPageCursorCodec(AuthenticatedCursorProtector.CreateProcessScoped())`
- Produces:
  - `PlcReadTools.PlcRead(OpennessWorkerClient workerClient, PlcOperationRequest[] operations, CancellationToken cancellationToken = default)`
  - `NetworkReadTools.NetworkRead(OpennessWorkerClient workerClient, NetworkOperationRequest[] operations, CancellationToken cancellationToken = default)`
  - `HmiReadTools.HmiRead(OpennessWorkerClient workerClient, HmiPageCursorCodec cursorCodec, HmiOperationRequest[] operations, CancellationToken cancellationToken = default)`
  - `VciReadTools.VciRead(OpennessWorkerClient workerClient, VciOperationRequest[] operations, CancellationToken cancellationToken = default)`
- Overload binding:
  - `NetworkRead` passes `(operations, operationExecutor.ExecuteAsync, cancellationToken)`.
  - `PlcRead`, `HmiRead` and `VciRead` pass the token as the fourth argument, to the `invoke`/`project` overload.

- [ ] **Step 1: Write the tests.**
  - `McpToolSchemaTests.ReadBatchSchemas_ExposeOnlyOperations(Type toolType, string methodName)`: a `[Theory]` over `PlcRead`, `NetworkRead`, `HmiRead` and `VciRead`. The schema properties are exactly `["operations"]`.
  - `ReadToolCancellationTests`, marked `[Collection(RealWorkerProcessCollection.Name)]`. Set up each test in this order:
    1. `using var audit = new TempAuditDirectory()`, then `Directory.CreateDirectory(audit.Path)`.
    2. `using var log = new FakeWorkerRequestLog(audit.Path)`.
    3. Build a client the way `PlcReadToolsTests.CreateClient` does (`McpAccessMode.ReadOnly`).

    Tests:
    - `PlcRead_CancelledToken_ThrowsWithoutWorkerRequest`:
      - Use `FakeWorkerUiOpenProject.ForWorkerRelativePath("plc-read-roundtrip")`.
      - First make one uncancelled call with item `{OperationId:"b", Operation:"get_block_content", BlockPath:"PLC_1/Main", ProjectPath:"plc-read-roundtrip"}`. Assert its item `status == "succeeded"`. Record `log.Methods().Length`, which must be > 0.
      - Then make the same call with a pre-cancelled token. It throws, and the count is unchanged.
    - `NetworkRead_CancelledToken_ThrowsWithoutWorkerRequest`: item `{OperationId:"n", Operation:"search_equipment_catalog", Query:"CPU"}`. It throws, and `log.Methods()` is empty.
    - `HmiRead_CancelledToken_ThrowsWithoutWorkerRequest`: item `{OperationId:"h", Operation:"list_hmi_devices"}`, with the process-scoped codec. It throws, and the log is empty.
    - `VciRead_CancelledToken_ThrowsWithoutWorkerRequest`: item `{OperationId:"v", Operation:"list_workspaces"}`. It throws, and the log is empty.
- [ ] **Step 2: Add the trailing parameter to the four tools but do not forward it yet.**
  - Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~ReadToolCancellationTests|FullyQualifiedName~McpToolSchemaTests"`
  - Expected: the four cancellation tests FAIL with "No exception was thrown". The schema theory PASSES; it is a guard.
- [ ] **Step 3: Forward the token.** Pass it to `ExecuteReadsAsync` in all four tools. In `PlcReadTools` and `HmiReadTools`, also replace `CancellationToken.None` in the invoker lambda.
  - Run: the same filter, then `git grep -n "CancellationToken.None" -- TiaMcpServer/Plc/PlcReadTools.cs TiaMcpServer/Hmi/HmiReadTools.cs`
  - Expected: all tests PASS, and the grep prints nothing.
- [ ] **Step 4: Commit.** Message: `fix: pass request cancellation through batch read tools`

**Task test command:** the Step 2 filter.

### Task 3: Docs and evidence

The controller supplies the branch-verification counts. Use them; do not run or invent them.

**Files:**
- Modify `docs/ARCHITECTURE.md` §7 "Batch and network execution".
- Modify `docs/superpowers/plans/2026-10-17-codebase-simplification-review.md` (item C-7).

**Interfaces:** none.

- [ ] **Step 1: ARCHITECTURE.md §7.** After "Read batches execute items independently…", add one sentence saying:
  - read batches check caller cancellation before each item;
  - an in-flight worker call is not cancelled, and the worker timeout bounds it;
  - a cancelled call returns no batch document.
- [ ] **Step 2: Review plan, item C-7.** Add an evidence sub-bullet in the B-9 format. Include:
  - the date;
  - the option chosen (wire the token through);
  - the full-suite counts;
  - "No live TIA verification was performed."
- [ ] **Step 3: Check.**
  - Run: `git grep -n "cancellation before each item" -- docs/ARCHITECTURE.md`
  - Expected: one hit in §7.
- [ ] **Step 4: Commit.** Message: `docs: record read batch cancellation`

**Task test command:** the Step 3 grep.
