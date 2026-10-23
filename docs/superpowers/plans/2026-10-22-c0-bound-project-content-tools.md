# C-0 Bound-Project-Only Content Tools Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Content tools and bound-project lifecycle tools take no `projectPath`. They act only on the bound project, and fail closed with `binding_conflict` when nothing is bound.

**Architecture:** Three layers, then the cleanup.
1. **One host gate in `OpennessWorkerClient`.** It sits in the shared request core and in the hardware-page route, keyed by a new `ProjectCapabilityCatalog.RequiresBoundProject(method)`. It refuses an unverified binding with `NotSent`.
2. **Call-level precheck in the four batch read tools.** An unbound call returns one root error, not N item errors.
3. **C-1's schema-derived closed arguments.** Once the parameter and the members are deleted, a stale `projectPath` is rejected automatically.

After that, `projectPath` is deleted from the public surfaces, the client content API, the write pipeline and the same-project rules. The worker and its wire stay byte-identical. The host keeps sending the resolved bound path.

**Tech Stack:** C#/.NET 10 host, netstandard2.0 Contracts, ModelContextProtocol 2.2.0, xUnit with `McpProtocolTestHarness` and FakeWorker.

**Spec:**
- Review item C-0 and decision Q6 in the [review plan](2026-10-17-codebase-simplification-review.md).
- Sequencing and the C-0 decisions in the [Workstream C plan](2026-10-21-workstream-c-plan.md).
- The 2026-10-22 survey (workflow `wf_5fe21bc5-aa3`), summarised under Decisions.

**Branch:** `refactor/c0-bound-project-content-tools`, worktree `.worktrees/C-0`, cut from `main` @ `1ff29d9` (C-1 merged).

**Execution:**
- Tasks 1–5 are subagent-driven: a fresh implementer and a task review for each.
- Then the orchestrator runs branch verification and the maintainer runs the live gate.
- Task 6 (docs) comes last.

## Decisions (maintainer, 2026-10-22)

- `get_project_status` loses its `projectPath`. So does `save_project_as`, whose only `projectPath` was a source assertion; its destination is `targetDirectory`/`targetName`. Only `bind_project` and `open_project` keep a public `projectPath`.
- An unbound batch read returns one call-level root error, never per-item failures.
- The "bind first" category is `binding_conflict`. No new category.
- Audit v2 `projectPath` holds the pinned binding's path. The record version stays 2.
- `search_equipment_catalog` is gated like every other content read.
- `OpennessWorkerClient` content and lifecycle methods lose `projectPath` end to end.
- **Deferred, logged in Task 6:**
  - a worker-side backstop that makes content reads require `ExpectedSessionIdentity`;
  - the tree-cursor message bug (own PR);
  - the `preserveUnboundEpoch` cursor branches (C-6).

## Global Constraints

Copied verbatim from the Workstream C plan:

- **Shell:** run `dotnet` from PowerShell. Bash mangles `/p:` flags.
- **Build:** `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true`. The baseline is the seven existing test-assembly nullable warnings and no new warnings.
- **Full suite:**
  - Run serialized, outside the sandbox.
  - Use process-local `TEMP`/`TMP` under ignored `TestResults/<id>/temp`.
  - Collect coverage with `TiaMcpServer.Tests/coverage.runsettings` and the single-thread xUnit settings from `.github/workflows/ci.yml`.
  - `scripts/verify-coverage-threshold.ps1 -MinimumLineRate 0.80` must pass.
  - Zero failures and zero skips.
- **Test-first:** observe a runtime failure before implementing, not just a compile error, wherever the plan names one. A pure refactor records a green baseline with the named pinning tests.
- **Test linking:** `TiaMcpServer.Tests` links host sources through `<Compile Include>`. `Safety/Pipeline`, `Vci`, `Openness/Hmi` and `Openness/Vci` use globs, and Contracts is a `ProjectReference`. Check the csproj before adding a host file.
- **Commits:** commit after each task, as short one-line conventional commits with no body.
- **Docs come last.** Each PR also adds an evidence sub-bullet under its item in the review plan, in the B-item style.
- **No TIA project names or paths** in the repo, including disposable copies.
- **Live runs:** use the installed candidate, which is separate from the dev build. Never dispose `TiaPortalProcess`.
- **jCodemunch:** `find_references` returned false empties for some C# consumers during planning. Confirm consumers with `search_text` or `git grep` before deleting a member.

C-0 specific:

- **Tests per task.** Run only each task's focused filters plus the stub build. The orchestrator runs the full suite once.
- **Paths that stay:**
  - The public `bind_project.projectPath` and `open_project.projectPath`.
  - `LifecycleWriteItem.ProjectPath`, now set only by `open_project`.
  - The client's internal status and lifecycle-probe methods: `GetProjectStatusAsync(string?)`, `ProbeProjectStatusForLifecycleAsync`, `GetBasicProjectStatusAsync`, `ProbeOpenProjectRebindAsync`, `OpenProjectAsync`, `CheckOpenProjectBinding`, `BindOpenProjectAsync`.
  - `ProjectSessionBinding.TryResolveWithSnapshot`/`TryGetVerified`, unchanged apart from the message.
  - Every output field (`result.query.projectPath`, lifecycle `value.projectPath`, effects, payload DTOs).
  - `WorkerRequest.ProjectPath`.
  - The sealed cursor state (`ResolvedProjectPath`, `HostBinding`).
- **Contract stays fixed:**
  - `TiaMcpServer.Tests/Tools/Snapshots/output-schemas.json` is unchanged, pinned by `OutputSchemasMatchMain`.
  - No `contractVersion` bump, and audit `RecordVersion` stays 2.
  - No cursor wire-format change.
  - `WorkerFailureCategories` is unchanged.
- **No worker, FakeWorker or wire change.** The only Contracts edits are `ProjectCapabilityCatalog.RequiresBoundProject` and `ProjectSessionBinding.BindFirstMessage`. If a FakeWorker edit looks necessary, stop and report.
- **Order of rejections:**
  1. argument shape (`validation_error`, no audit);
  2. catalog validation;
  3. access mode;
  4. binding (`binding_conflict`).
- **One bind-first text.** `ProjectSessionBinding.BindFirstMessage` is the only copy:
  `"A worker-verified project binding is required. Call bind_project first, or configure --project and verify it. In read-write/full mode, you can also call open_project explicitly."`
- **Positional-call hazard.** When a `string?` parameter disappears, a positional caller can silently rebind to the next `string?` parameter. Use named arguments at every changed call site of `CompileCheck`, `ReadCrossReferences`, `BrowseProjectTree` and the client methods.
- **Out of scope:**
  - the tree-cursor message bug in `ProjectTreeBrowseCoordinator` (it passes the category as the message);
  - `preserveUnboundEpoch` plumbing;
  - any worker `OperationPolicyCatalog` change;
  - retired-member hints for `projectPath`. As in C-10, C-0 relies on the unknown-member rejection.

## Review Focus

1. **A content call under an unbound pinned lease** (open/create pin an unbound snapshot) must not reach the worker. Pinned by `ContentMethodUnderUnboundPin_FailsClosed` (Task 2).
2. **A configured `--project`** still works: the first content read grounds it through status and then succeeds. If grounding fails, the grounding error is returned and the read is not sent. Pinned by `ConfiguredStartupProject_FirstReadGroundsAndSucceeds` and `ConfiguredStartupProject_GroundingFailure_IsReturned` (Task 2).
3. **An unbound caller with a malformed batch** gets `validation_error`, not `binding_conflict`. Pinned by `UnboundRead_InvalidItem_IsValidationError` (Task 2), plus the existing C-1 conformance probes, which run unbound.
4. **A hardware-page or tree continuation after the binding is cleared** fails with `cursor_binding_mismatch` and never falls back to the Portal's open project. Pinned by `ContinuationAfterUnbind_FailsCursorBindingMismatch` (Task 2).
5. **Old clients sending `projectPath`** get canonical `validation_error` naming `projectPath`, with no worker request and no audit. `close_project({projectPath})` closes nothing. Pinned by `RetiredProjectPathIsRejectedCanonically` (items in Task 3, roots in Task 4).

## File Structure

No new host files.
- **Gate:** `TiaMcpServer/Worker/OpennessWorkerClient.cs`, `TiaMcpServer.Contracts/Project/ProjectCapabilityCatalog.cs` and `ProjectSessionBinding.cs`.
- **Public surfaces:**
  - request DTOs: `Plc/PlcOperationRequest.cs`, `Network/NetworkOperationRequest.cs`, `Hmi/HmiOperationRequest.cs`, `Hmi/HmiWriteOperationRequest.cs`, `Vci/VciOperationRequest.cs`;
  - tools: `Tools/ProjectReadTools.cs`, `Tools/ProjectWriteTools.cs`, `Tools/ProjectEngineeringTools.cs`, `CrossReferences/CrossReferenceReadTools.cs`.
- **Deleted:** `TiaMcpServer/Safety/Pipeline/WriteProjectPaths.cs`. It is globbed, so no csproj edit is needed.

**Release note (PR title and body, label `enhancement`, ships as v4.2.0):** Content tools and `save_project`/`save_project_as`/`archive_project`/`close_project`/`get_project_status` no longer accept `projectPath`. They act only on the bound project and fail closed with `binding_conflict` when none is bound: call `bind_project` first, or start with `--project`. A stale `projectPath` is rejected with `validation_error`.

## After the tasks

These steps are not implementer tasks. They run after Task 5 and before Task 6.

### Branch verification (orchestrator)

Run the stub build, then the full suite with coverage under `TestResults/c0`, per Global Constraints.

Expected:
- 0 failed, 0 skipped.
- The coverage threshold passes.
- Every changed executable line in the gate (both `OpennessWorkerClient` sites, `RequiresBoundProject`, the four read-tool prechecks) is covered.
- `git grep -n -i "projectPath" -- TiaMcpServer/Plc TiaMcpServer/Hmi TiaMcpServer/Vci TiaMcpServer/CrossReferences TiaMcpServer/OperationBatches TiaMcpServer/Safety` lists only output, audit or effect fields.

The final whole-branch review comes next.

### Live gate (maintainer-authorized, not an implementer task)

Use the installed candidate built from the branch head against the real V21 assemblies, in read-write mode, on disposable projects, saving nothing that matters.

1. **Unbound, no `--project`.**
   - `plc_read`, `network_read` `search_equipment_catalog`, `hmi_read`, `browse_project_tree` and `read_cross_references` each return one root `binding_conflict` with the bind-first text.
   - `get_project_status {}` still reports the open project, and the session stays unbound.
2. **Stale paths.** `plc_read` with an item `projectPath` and `close_project {projectPath:"x"}` both return canonical `validation_error`. No audit line is written, and the project stays open.
3. **Standalone, after `bind_project`.**
   - `plc_read`, two pages of `read_hardware_config`, a tree continuation, `read_cross_references` and `compile_check` succeed.
   - `plc_write` creates a tag table, then deletes it.
   - `save_project {dryRun:true}` succeeds.
   - The audit `projectPath` equals the bound path.
4. **`--project` startup.** The first `plc_read` grounds the binding and succeeds.
5. **Local session.** After a cold-adopting `bind_project`, `plc_read` and `browse_project_tree` succeed. The `save_project` dry-run effect names the bound session path.
6. **After `close_project`** on the disposable project, `plc_read` returns `binding_conflict`.

Record the candidate SHA and the outcomes privately. Pass them, with the verification counts, to the Task 6 implementer.

---

### Task 1: Test fixtures bind before content calls

This task is a pure test refactor, green on the unchanged production code.

**Files:**
- Modify: `TiaMcpServer.Tests/Network/NetworkVerifiedWriteFixture.cs`. `VerifyAsync` grounds through `FakeWorkerBinding.BindVerifiedAsync` instead of the unbound `ReadHardwareConfigAsync` probe at `:67-70`. It keeps its other assertions.
- Modify: the PowerShell-scripted fixtures in `Hmi/HmiWritePlannerTests.cs` (`Fixture.Create`, around `:424`), `Hmi/HmiWriteVerifierTests.cs` (around `:499`) and `Vci/VciWritePlannerTests.cs` (around `:172`). After `GetProjectStatusAsync`, call `binding.BindVerified(status.SessionIdentity, forceRebind: false, out _)` and assert it succeeds.
- Modify: the positional `CompileCheck(client, scenario)` calls in `Project/StandaloneCompileToolTests.cs` (`:28`, `:51`, `:86`) and `Project/ProjectStandaloneToolTests.cs` (`:75-77`, `:96-98`). Make them named: `projectPath: scenario`.
- Modify: in `Tools/ToolOutputContractConformanceTests.cs`, move the read probes' scenario key out of the item or root `projectPath` and into `StartupProjectPath`, as the `hmi_write`, `vci_write` and `compile_check` probes already do (`:65-74`).

**Interfaces:**
- Consumes (from main): `FakeWorkerBinding.BindVerifiedAsync(OpennessWorkerClient, ProjectSessionBinding, string)` and `ProjectSessionBinding.BindVerified(WorkerSessionIdentity, bool, out string?)`.
- Produces: none.

- [ ] **Step 1: Record the baseline.** Run the Task test command and note the counts.
- [ ] **Step 2: Apply the fixture changes.** If FakeWorker does not answer `get_project_status` with a complete identity for a scenario key a fixture uses, stop and report that key. Do not edit FakeWorker.
- [ ] **Step 3: Rerun.**
  - Run: the Task test command, then the stub build.
  - Expected: the same pass count, 0 failures, no new warnings.
- [ ] **Step 4: Commit.** `test: bind fixtures before content calls`

**Task test command:** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Network|FullyQualifiedName~Hmi|FullyQualifiedName~Vci|FullyQualifiedName~StandaloneCompileToolTests|FullyQualifiedName~ProjectStandaloneToolTests|FullyQualifiedName~ToolOutputContractConformanceTests|FullyQualifiedName~McpProtocol"`

### Task 2: Fail-closed gate for unbound content calls

**Files:**
- Modify: `TiaMcpServer.Contracts/Project/ProjectCapabilityCatalog.cs`. Add `RequiresBoundProject`.
- Modify: `TiaMcpServer.Contracts/Project/ProjectSessionBinding.cs`. Add `BindFirstMessage`. `TryGetVerified`'s non-verified branch (`:296-297`) uses it.
- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs`.
  - Gate in `SendBoundProjectRequestCoreAsync`, after the pin-drift check (`:1905-1914`).
  - Gate in `ReadHardwarePageCandidatesCoreAsync`, after the resolve (`:727-739`).
  - Rename `RequireVerifiedWriteBindingAsync(string?)` to `RequireVerifiedBindingAsync(string? projectPath = null)`.
  - Fix the `<summary>` text: it is the gate for every project-scoped call.
- Modify: the callers of the old name:
  - `Safety/Pipeline/IWriteBindingGate.cs:55` (`OpennessWriteBindingGate` forwards to the new name; the interface method name stays `RequireVerifiedWriteBindingAsync`);
  - `ProjectLifecycle/LifecycleBindingStrategy.cs:55`;
  - `Tools/ProjectEngineeringTools.cs:27`;
  - `TiaMcpServer.Tests/Plc/PlcGuardedWriteFixture.cs:102-104`;
  - `TiaMcpServer.Tests/Worker/TiaPortalSessionBindingGuidanceSourceTests.cs:54` (the literal signature text).
- Modify: `Plc/PlcReadTools.cs`, `Network/NetworkReadTools.cs`, `Hmi/HmiReadTools.cs` and `Vci/VciReadTools.cs`. Add the precheck after the access-mode check and before `ExecuteReadsAsync`.
- Modify: `TiaMcpServer/Diagnostics/Checks/ProjectBindingCheck.cs:49`. New read-only unconfigured message: `"No project binding is configured. Project tools fail until bind_project binds an already-open project."`
- Modify: the `ExpectedSessionIdentity` doc comment in `TiaMcpServer.Contracts/Worker/WorkerRequest.cs:42-46`, wording only. Null now means status, selection, inventory, open or create, never a content read.
- Tests:
  - `Project/ProjectCapabilityCatalogTests.cs`
  - `Worker/OpennessWorkerClientIntegrationTests.cs`
  - `Network/HardwarePageWorkerClientTests.cs`
  - `Network/HardwarePaginationFakeWorkerTests.cs`
  - `Network/HardwarePageCursorCodecTests.cs`
  - `Project/ProjectTreeBrowseCoordinatorTests.cs`
  - `Tools/ToolOutputContractConformanceTests.cs`
  - `Diagnostics/ProjectBindingCheckTests.cs`
  - every test the gate turns red (Step 6)

**Interfaces:**
- Consumes (from main):
  - `ProjectCapabilityCatalog.OperationFor(string)`
  - `ProjectBindingSnapshot.IsVerified`
  - `WorkerCallResult.Fail(category, error) with { DispatchState = WorkerDispatchState.NotSent }`
  - `HardwarePageFailure(snapshot, category, message)`
  - the read tools' `internal static CallToolResult Error(string category, string message)`
- Produces:
  - `public static bool ProjectCapabilityCatalog.RequiresBoundProject(string method)`. It is true when `method == "search_equipment_catalog"`, or when `OperationFor(method)` is non-null and not one of `get_project_status`, `open_project`, `create_project`. So status, `get_basic_project_status`, the lifecycle probes, open/create, selection and inventory are exempt.
  - `public const string ProjectSessionBinding.BindFirstMessage`, with the exact text in Global Constraints.
  - `public Task<WorkerCallResult> OpennessWorkerClient.RequireVerifiedBindingAsync(string? projectPath = null)`, with the same behaviour as today's write gate.
  - **Core gate:** when `!bindingBeforeCall.IsVerified && RequiresBoundProject(method)`, return `Fail(BindingConflict, BindFirstMessage)` with `NotSent`. There is no exemption for a pinned binding.
  - **Hardware-route gate:** when `!hostBinding.IsVerified`, return `HardwarePageFailure(hostBinding, isContinuation ? CursorBindingMismatch : BindingConflict, BindFirstMessage)`.
  - **Read-tool precheck:** `var binding = await workerClient.RequireVerifiedBindingAsync();` and, on failure, `return Error(binding.FailureCategory!, binding.Error!)`.

- [ ] **Step 1: Write the tests.**
  - `ProjectCapabilityCatalogTests.RequiresBoundProject_GatesContentMethodsOnly`. It is a theory: true for `read_hardware_config`, `read_hardware_page_candidates`, `browse_project_tree_v3_snapshot`, `read_cross_references`, `get_block_content`, `list_tag_tables`, `hmi_list_tag_tables`, `vci_list_workspaces` (or any `VciMethods.Read` entry), `search_equipment_catalog`, `compile_check` and `save_project`. It is false for `get_project_status`, `get_basic_project_status`, `probe_project_status_for_lifecycle`, `open_project`, `create_project`, `select_portal_project` and `list_local_sessions`.
  - In `OpennessWorkerClientIntegrationTests`:
    - `UnboundSession_ContentRead_FailsClosedWithoutWorkerRequest` replaces `UnboundSession_UnrelatedReadSuccess_DoesNotBindOrSwitchProject` (`:956`). Unbound `ListTagTablesAsync` returns `binding_conflict` and `NotSent`, its error equals `BindFirstMessage`, the FakeWorker request log has no `list_tag_tables`, and the binding stays unbound.
    - `UnboundSession_EquipmentSearch_FailsClosed`: the same check for `SearchEquipmentCatalogAsync`.
    - `ContentMethodUnderUnboundPin_FailsClosed`: under `ExecuteWithPinnedBindingAsync` with the unbound snapshot, `ListTagTablesAsync` returns `binding_conflict` and `NotSent`.
    - `ConfiguredStartupProject_FirstReadGroundsAndSucceeds`: with `new ProjectSessionBinding(path)`, the read succeeds, the log shows `get_project_status` and then the read, and the binding is verified.
    - `ConfiguredStartupProject_GroundingFailure_IsReturned`: with a scenario whose status fails, the status failure category is returned and the read is not logged.
  - In `HardwarePageWorkerClientTests`:
    - `UnboundFirstPage_FailsClosed`: `binding_conflict`, nothing sent.
    - `UnboundContinuation_UsesCursorPathAndIdentityWithoutCreatingAHostBinding` (`:61`) is rewritten as `ContinuationAfterUnbind_FailsCursorBindingMismatch`: a cursor issued while bound, then `binding.Clear(null)`, gives `cursor_binding_mismatch` with nothing sent.
  - `ToolOutputContractConformanceTests.UnboundContentReadsFailClosedCanonically`: production surface, no startup path. For each of `plc_read`, `network_read`, `hmi_read`, `vci_read` (one valid item each), `browse_project_tree {}` and `read_cross_references` (valid target), check that `StructuredContractInspector.FindViolations` is empty, `isError` is true and the category is `binding_conflict` (`error.category`, or `failure.category` for `browse_project_tree`). For the four batch tools, also check that `batch` is null. No worker content method is logged.
  - `ToolOutputContractConformanceTests.UnboundRead_InvalidItem_IsValidationError`: an unbound `plc_read` with an unknown `operation` returns `validation_error`.
  - `ProjectBindingCheckTests`: update the read-only unconfigured row to the new message.
- [ ] **Step 2: Run them.**
  - Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~ProjectCapabilityCatalogTests|FullyQualifiedName~OpennessWorkerClientIntegrationTests|FullyQualifiedName~HardwarePageWorkerClientTests|FullyQualifiedName~ToolOutputContractConformanceTests|FullyQualifiedName~ProjectBindingCheckTests"`
  - Expected:
    - `RequiresBoundProject` fails to compile. Add a stub returning `false`, then rerun: its theory FAILS on the true rows.
    - The unbound and pin tests FAIL with a successful read.
    - `UnboundRead_InvalidItem_IsValidationError` and the configured tests PASS.
- [ ] **Step 3: Implement** the predicate, the constant, the rename, both client gates, the four prechecks, the doctor text and the comment.
- [ ] **Step 4: Rerun Step 2's filter.** Expected: all PASS.
- [ ] **Step 5: Rewrite the remaining unbound pins.**
  - `HardwarePaginationFakeWorkerTests.NetworkRead_UnpagedFixtureIsCanonicalAndPagedContinuationStaysUnbound` (`:164`): bind first, and assert that the continuation stays on the binding.
  - `OpennessWorkerClientIntegrationTests.ReadHardwarePageCandidatesAsync_ForwardsTheDedicatedInternalRequest` (`:131`): bind first.
  - `ProjectTreeBrowseCoordinatorTests.Cursor_Unbound_Continues` (`:108`): becomes `Cursor_AfterUnbind_IsRejected`.
  - Leave `HardwarePageCursorCodecTests` codec-level unbound-state tests (`:21`, `:35`, `:287`) as they are. They test the codec, not reachability.
- [ ] **Step 6: Migrate the tests the gate turns red.**
  - Run: `dotnet test TiaMcpServer.Tests`, focused only in the sense that you read its failures; do not collect coverage.
  - Expected: failures from tests that read through an unbound client or harness, which leads to `binding_conflict` or the bind-first message.
  - Fix each one by binding to the test's scenario path, using `FakeWorkerBinding.BindVerifiedAsync` in the file's `CreateClient`, or the harness `startupProjectPath`. Known files: `PlcReadToolsTests`, `CrossReferenceReadToolsTests`, `HmiReadToolsTests`, `HmiReadFakeWorkerTests`, `NetworkIoMapFakeWorkerTests`, `NetworkOperationFakeWorkerTests`, `NetworkToolsTests`, `NetworkStructuredProtocolTests`, `NetworkIntrospectionEndToEndTests`, `VciReadMcpTests` and `ProjectTreeStructuredProtocolTests`.
  - A test that sends a path different from its bound path in order to switch scenarios becomes two bound fixtures.
  - Never weaken the gate. If a test's purpose needs an unbound content read, convert it into a fail-closed assertion and list it in the task report.
  - Rerun until 0 failures.
- [ ] **Step 7: Build.** Run the stub build. Expected: no new warnings.
- [ ] **Step 8: Commit.** `feat: fail closed on unbound content calls`

**Task test command:** Step 2's filter plus `FullyQualifiedName~HardwarePagination|FullyQualifiedName~ProjectTreeBrowseCoordinatorTests|FullyQualifiedName~ReadTools|FullyQualifiedName~FakeWorkerTests|FullyQualifiedName~StructuredProtocolTests|FullyQualifiedName~VciReadMcpTests`

### Task 3: Remove item `projectPath` from the eight batch tools

**Files:**
- Modify:
  - `ProjectPath` on `PlcOperationRequest`, `NetworkOperationRequest`, `HmiOperationRequest`, `HmiWriteOperationRequest` and `VciOperationRequest`: delete it;
  - `OperationBatches/IOperationBatchItem.cs`: delete the member, and keep the interface if it has other members;
  - `LifecycleWriteItem.ProjectPath`: becomes a plain property, no longer an interface implementation;
  - `"projectPath"` in `Network/NetworkOperationCatalog.cs:42-47` (`UniversalFields`): delete it.
- Delete:
  - the same-project checks at `NetworkOperationCatalog.cs:258-268`, `Plc/PlcOperationCatalog.cs:364-372`, `Hmi/HmiWriteCatalog.cs:129-135`, `Vci/VciWriteCatalog.cs:68-70` and `Vci/VciReadCatalog.cs:59-61`;
  - `Safety/Pipeline/WriteProjectPaths.cs`.
- Modify the item-path consumers:
  - The four write tools (`PlcWriteTools.cs:21-24`, `NetworkWriteTools.cs:21-24`, `HmiWriteTools.cs:19-21`, `VciWriteTools.cs:19-21`) build `WriteCall` with `ProjectPath: null`.
  - `PlcWorkerInvoker` and `NetworkWorkerInvoker` read and write branches drop `op.ProjectPath`; the write branch passes `commonProjectPath`.
  - `HmiWorkerInvoker.cs:40` and `VciReadTools.cs:45` pass `null`.
  - `HardwarePaginationCoordinator.cs:106` uses only `cursorState?.ResolvedProjectPath`.
  - The `request.ProjectPath` repeat rule at `HardwarePageCursorCodec.cs:133-137` is deleted.
  - `PlcWritePlanner.cs:147` and `NetworkWritePlanner.cs:186`: delete the `ProjectPath =` initializers.
- Modify the descriptions:
  - `projectPath?` comes out of the item-shape text in `PlcReadTools.cs:27`, `NetworkReadTools.cs:33` and `HmiReadTools.cs:28`;
  - "all in/constrained to the same project" comes out of `PlcWriteTools.cs:18`, `NetworkWriteTools.cs:18`, `HmiWriteTools.cs:16` and `VciWriteTools.cs:16`.
- Tests:
  - Delete the same-project tests: `PlcOperationCatalogTests.RejectsDifferingProjectPaths`, `NetworkOperationCatalogTests.ValidateWrite_RejectsMixedNormalizedProjectPaths`, `HmiWriteCatalogTests.BatchPaths_*` (and edit the reflection exclusion at `:164`), `HmiGuardedWriteMcpTests.MixedProjectPathsAreDeniedBeforeWorkerActivity`, `NetworkToolsTests.NetworkWrite_RejectsMixedProjectPathsBeforeWorkerStartup`, `PlcReadToolsTests.MismatchedProjectPathFailsOnlyThatItem`, `PlcGuardedWriteDomainTests.MismatchedProjectPath_IsBindingConflictBeforeWorker` and `NetworkSubnetLifecycleFakeWorkerTests.NetworkWrite_SubnetLifecycleApply_DifferentProjectPathIsRejectedAsBindingConflict`.
  - Delete the `"mixed-projects"` row in `NetworkGuardedWriteAuditTests`.
  - Trim the multi-project halves of `VciReadCatalogTests.NullMembersAndMultipleProjectsReject`, `VciWriteCatalogTests.BatchBoundariesAndAccessMode` and `NetworkGuardedWriteDomainTests.DomainValidation_RejectsMixedProjectsAndOversizedCallsBeforeWorker`.
  - Collapse `PlcGuardedWriteDomainTests.CreateTableThenTagInOneCall_Applies` to one case.
  - Drop the item-fallback half of `PlcWriteInvokerTests.WriteUsesPinnedProjectPathOrItemFallback`.
  - Delete the cursor repeat tests `HardwarePageCursorCodecTests.Validate_ExplicitProjectPathMismatch_*` and `Validate_OmittedProjectPathAndEquivalentNormalizedPath_AreAccepted`. Keep their non-path assertions if any.
  - Remove every remaining typed `ProjectPath =` on these DTOs and every JSON item `projectPath` the compiler or the C-1 rejection points to.

**Interfaces:**
- Consumes (Task 2): bound fixtures.
- Produces: the request DTOs and `IOperationBatchItem` have no `ProjectPath`, and `WriteCall.ProjectPath` is always null for content writes. Task 5 deletes it.

- [ ] **Step 1: Write the test.** `ToolOutputContractConformanceTests.RetiredProjectPathIsRejectedCanonically`: production surface, bound through `StartupProjectPath`, with the existing request log and audit directory. For each of the eight batch tools, send one otherwise-valid item that carries `projectPath` set to the bound path. Each reply passes `ArgumentRejectionAssert.IsCanonicalValidationError(reply, tool)`, its message contains `projectPath`, no content method is logged, and no audit line is written.
- [ ] **Step 2: Run it.**
  - Run: `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~RetiredProjectPathIsRejectedCanonically`
  - Expected: FAIL. The calls succeed.
- [ ] **Step 3: Delete and modify as listed.** Fix compile errors in tests by deleting the path argument; do not add a replacement.
- [ ] **Step 4: Rerun and build.**
  - Run: the Task test command, then the stub build.
  - Expected: all PASS, and no new warnings.
- [ ] **Step 5: Commit.** `refactor: remove item projectPath from batch tools`

**Task test command:** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Plc|FullyQualifiedName~Network|FullyQualifiedName~Hmi|FullyQualifiedName~Vci|FullyQualifiedName~ToolOutputContractConformanceTests|FullyQualifiedName~Pipeline"`

### Task 4: Remove root `projectPath` from eight tools

**Files:**
- Modify: `Tools/ProjectReadTools.cs`.
  - Delete the `projectPath` parameters of `get_project_status` (`:24`; call `GetProjectStatusAsync(null)`) and `browse_project_tree` (`:43`).
  - Delete the `("projectPath", …)` entry in `ProjectTreeArgumentValidatingTool.ShapeFailure` (`:123`).
  - Fix both descriptions. Status reports the bound project, or the sole open project when unbound, and never binds.
- Modify: `ProjectTree/ProjectTreeBrowseRequest.cs`. Delete `ProjectPath`, the path comparison in `ValidateRepeatedQuery` (`:71-75`) and `SameProject` (`:94-100`). Keep the selector and depth comparisons and their category as they are.
- Modify: `ProjectTree/ProjectTreeBrowseCoordinator.cs`. Stop passing a request path.
- Modify: `CrossReferences/CrossReferenceReadTools.cs:32` and `Tools/ProjectEngineeringTools.cs:18`. Delete the parameter. The descriptions say the tool reads the bound project.
- Modify: `Tools/ProjectWriteTools.cs`.
  - Delete the parameter from `save_project` (`:50`), `save_project_as` (`:63`), `archive_project` (`:80`) and `close_project` (`:92`).
  - `ExecuteAsync` (`:104`) passes `null` as the `WriteCall` path.
  - Only `open_project` sets `LifecycleWriteItem.ProjectPath`.
- Modify: `ProjectLifecycle/LifecycleWriteDomain.cs`.
  - Move the absolute-path check in `Validate` (`:70-71`) inside the `open_project` branch.
  - `MutateAsync` (`:224-227`) passes `null` to the save, save-as, archive and close client calls.
- Modify: `ProjectLifecycle/LifecycleBindingStrategy.cs:48-49`. Pass `null` as the source assertion.
- Tests:
  - `McpToolSchemaTests`: the exact-key pins at `:134-137`, `:399` and `:432`.
  - `StandaloneToolsProtocolTests`: `:23`, `:76-77` and `:91`.
  - The direct `GetProjectStatus(client, projectPath: …)` callers move their scenario into `FakeWorkerUiOpenProject` or a bound fixture: `OpennessWorkerClientIntegrationTests:1248,1259`, `ProjectLifecycleToolTests:73`, `ProjectMetadataTests:168` and `StandaloneStatusToolTests:28,47`.
  - Also: `ProjectLifecyclePreviewSafetyTests:143-189`, `WriteSafetyLeaseConcurrencyTests:70-71`, `LifecycleResponseBudgetTests:112`, `CrossReferenceReadToolsTests`, `ProjectTreeBrowseRequestTests` and `ProjectTreeBrowseCoordinatorTests.ContinuationAllowsChangedPageSizeAndEquivalentRepeatedQuery`.

**Interfaces:**
- Consumes (Task 3): `LifecycleWriteItem.ProjectPath` as a plain property.
- Produces: `LifecycleWriteItem.ProjectPath` is non-null only for `open_project`. No public tool other than `bind_project` and `open_project` has a `projectPath` input.

- [ ] **Step 1: Extend the test.** Add rows to `RetiredProjectPathIsRejectedCanonically`: root `projectPath` on `get_project_status`, `browse_project_tree`, `read_cross_references`, `compile_check`, `save_project`, `save_project_as`, `archive_project` and `close_project`, each otherwise valid, in read-write mode.
  - Assertions: canonical `validation_error` containing `unknown argument 'projectPath'`, 0 elicitations, no audit line, and no lifecycle method logged. `close_project` closes nothing.
  - Add `McpToolSchemaTests.OnlyBindAndOpenAcceptProjectPath`: across `tools/list`, `projectPath` appears in an `inputSchema` only for `bind_project` and `open_project`.
- [ ] **Step 2: Run them.**
  - Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~RetiredProjectPathIsRejectedCanonically|FullyQualifiedName~OnlyBindAndOpenAcceptProjectPath"`
  - Expected: FAIL on the eight root rows.
- [ ] **Step 3: Implement as listed.** Use named arguments at every changed call site.
- [ ] **Step 4: Rerun and build.**
  - Run: the Task test command, then the stub build.
  - Expected: all PASS, including `OutputSchemasMatchMain`, and no new warnings.
- [ ] **Step 5: Commit.** `refactor: remove root projectPath from bound-project tools`

**Task test command:** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~McpToolSchemaTests|FullyQualifiedName~ToolOutputContractConformanceTests|FullyQualifiedName~Lifecycle|FullyQualifiedName~ProjectTree|FullyQualifiedName~CrossReference|FullyQualifiedName~Standalone|FullyQualifiedName~ProjectMetadataTests|FullyQualifiedName~WriteSafetyLeaseConcurrencyTests|FullyQualifiedName~OpennessWorkerClientIntegrationTests"`

### Task 5: Remove `projectPath` from the client API and the write pipeline

**Files:**
- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs`. Delete the `projectPath` parameter from every content and bound-lifecycle method:
  - `BrowseProjectTreeV3SnapshotAsync`, `ReadHardwareConfigAsync`, `ReadHardwarePageCandidatesAsync` (and its Core), `SearchEquipmentCatalogAsync`;
  - the network list, inspect, device and subnet methods (`:892-1029`);
  - `ReadCrossReferencesAsync`, `GetBlockContentAsync`, `GetTypeContentAsync`, `ListTagTablesAsync`;
  - `ReadVciAsync`, `WriteVciAsync`, `ReadHmiAsync`, `WriteHmiAsync`, `CompileCheckAsync`;
  - the block, tag-table, tag, user-constant and block-group methods (`:1242-1473`);
  - `SaveProjectAsync`, `SaveProjectAsAsync`, `ArchiveProjectAsync`, `CloseProjectAsync`.

  They pass `null` to `SendBoundProjectRequestAsync`, which keeps its `projectPath` parameter for the status and probe methods. Other changes in this file:
  - `RequireVerifiedBindingAsync()` becomes parameterless.
  - In `ReadHardwarePageCandidatesCoreAsync`, delete the repeated-path check (`:688-699`) and the cursor-path routing (`:701-703`). Resolve with `null`.
  - `CloseProjectAsync`'s `Clear` transition clears with `null`.
- Modify: `Network/HardwarePaginationCoordinator.cs`. Delete `HardwarePageCandidateCall.ProjectPath` (`:9-18`, `:36`). Keep the identity cross-checks at `:144`, `:189` and `:202-203`.
- Modify: `Safety/Pipeline/WriteRecords.cs`. The record becomes `WriteCall<TItem>(IReadOnlyList<TItem> Items, bool DryRun)`.
- Modify: `Safety/Pipeline/IWriteDomain.cs`. Drop the path from `PlanAsync`, `ReplanAsync`, `MutateAsync` and `VerifyAsync`.
- Modify: `Safety/Pipeline/IWriteBindingGate.cs`. `RequireVerifiedWriteBindingAsync()` takes no parameter.
- Modify: `Safety/Pipeline/WriteRun.cs` (`:120`, `:219`, `:317`, `:373`, `:408`, `:416`). Audit at `:544` uses `ProjectPath = _binding.ProjectPath`.
- Modify: the four content domains and their planners, verifiers and invokers: PLC, Network (its parameter is named `path`), HMI and VCI. Also `LifecycleWriteDomain` and `LifecycleBindingStrategy`. Drop the parameters and `commonProjectPath`. In `VciWritePlanner.ProjectFolderError` (`:66-71`), use only `client.BindingSnapshot.ProjectPath`.
- Modify the callers: `ProjectEngineeringTools`, `CrossReferenceReadTools`, `ProjectTreeBrowseCoordinator`, `NetworkWorkerInvoker`, `PlcWorkerInvoker`, `HmiWorkerInvoker`, `VciReadTools` and the write tools.
- Tests:
  - compile-driven: `PipelineFakes.cs:264` and `:19`, `PlcGuardedWriteFixture.cs:66` and `:102`, `OpennessWriteBindingGateTests.cs:39,87`, `SubnetProbeDomain.cs:62-66`, `NetworkGuardedWriteBudgetTests`, `ModeDerivedConfirmationTests`, `PlcWriteInvokerTests` (B-9 cases collapse to "sends the bound path");
  - the client callers in `OpennessWorkerClientIntegrationTests`, `HardwarePageWorkerClientTests`, `LifecycleIdentityContinuityTests` and the forwarding tests;
  - `WriteExecutionTests`.

**Interfaces:**
- Consumes (Tasks 2–4): the renamed `RequireVerifiedBindingAsync`, and `WriteCall.ProjectPath` always being null.
- Produces:
  - `public Task<WorkerCallResult> OpennessWorkerClient.RequireVerifiedBindingAsync()`
  - `Task<WorkerCallResult> IWriteBindingGate.RequireVerifiedWriteBindingAsync()`
  - `record WriteCall<TItem>(IReadOnlyList<TItem> Items, bool DryRun)`
  - `IWriteDomain<…>.PlanAsync(IReadOnlyList<TItem> items)`, `ReplanAsync(TItem item)`, `MutateAsync(TItem item)` and `VerifyAsync(StructuredOperationBatch batch)`. Each keeps its other parameters, such as `CancellationToken`, unchanged.

- [ ] **Step 1: Write the tests.**
  - `WriteExecutionTests.AuditProjectPath_IsPinnedBindingPath`: a gated write with a verified fake binding writes a record where `ProjectPath == Binding.ProjectPath == FakeWriteBindingGate.ProjectPath`.
  - `WriteExecutionTests.AuditProjectPath_OnGateRejection_IsPreGateSnapshotPath`: with an unbound gate, the record's `ProjectPath` is null and `binding.state` is `unbound`.
  - `PlcWriteInvokerTests.WriteSendsTheBoundPath`: under a verified binding, the `WorkerRequest.ProjectPath` on the wire equals the bound path.
- [ ] **Step 2: Run them.**
  - Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~WriteExecutionTests|FullyQualifiedName~PlcWriteInvokerTests"`
  - Expected: `AuditProjectPath_IsPinnedBindingPath` FAILS, because the record path is null (the call path). The other two PASS; they are baseline pins.
- [ ] **Step 3: Implement as listed.** Follow the compiler. Use named arguments wherever a `string?` parameter was removed ahead of another `string?`.
- [ ] **Step 4: Rerun and build.**
  - Run: the Task test command, then the stub build.
  - Expected: all PASS, and no new warnings.
- [ ] **Step 5: Check.**
  - Run: `git grep -n "string? projectPath" -- TiaMcpServer/Worker/OpennessWorkerClient.cs`
  - Expected: only `SendBoundProjectRequestAsync`/`Core`, `GetProjectStatusAsync`, `ProbeProjectStatusForLifecycleAsync`, `GetBasicProjectStatusAsync` and `BindOpenProjectAsync` and its overload.
- [ ] **Step 6: Commit.** `refactor: drop projectPath from client API and write pipeline`

**Task test command:** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Pipeline|FullyQualifiedName~Worker|FullyQualifiedName~Plc|FullyQualifiedName~Network|FullyQualifiedName~Hmi|FullyQualifiedName~Vci|FullyQualifiedName~Lifecycle|FullyQualifiedName~ProjectTree|FullyQualifiedName~CrossReference|FullyQualifiedName~Standalone"`

### Task 6: Docs and evidence

Dispatch this task only after the live gate. The controller passes in the branch-verification counts, the coverage, and the live-gate SHA and outcomes. Use those values; do not run or invent them.

**Files:** modify:
- `README.md` (absolute links only)
- `AGENTS.md`
- `docs/ARCHITECTURE.md`
- `docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md`, `PLC_OPERATIONS_SUMMARY.md`, `NETWORK_OPERATIONS_SUMMARY.md`, `NETWORK_PHASE4_SUBNET_LIFECYCLE.md`, `HMI_OPERATIONS_SUMMARY.md`, `VCI_OPERATIONS_SUMMARY.md` and `DEVICES_OPERATIONS_SUMMARY.md`
- `docs/guides/installation.md` and `troubleshooting.md`
- `docs/development/local-mcp-testing.md`, `packaging.md` and `codebase-review.md`
- `docs/IMPROVEMENT_LOG.md`
- `docs/superpowers/plans/2026-10-17-codebase-simplification-review.md`
- `docs/superpowers/plans/2026-10-21-workstream-c-plan.md`

**Interfaces:** none.

- [ ] **Step 1: Remove the input `projectPath`** from every tool row, signature and JSON example, except `bind_project` and `open_project`.
  - Sites: `README.md:28,74,75`; `ARCHITECTURE.md:125-126,165,285,294-297,757-762`; `PROJECT_OPERATIONS_SUMMARY.md:7-9,104,115,122,279-282,292,443`; `PLC_OPERATIONS_SUMMARY.md:7,11,12,28,48,102,107-108,162-164`; `NETWORK_OPERATIONS_SUMMARY.md:148,182-189,229,539`; `NETWORK_PHASE4_SUBNET_LIFECYCLE.md:46,62,81`; `HMI_OPERATIONS_SUMMARY.md:162-164`; `VCI_OPERATIONS_SUMMARY.md:9,33`; `installation.md:79-92,210-211,243`; `troubleshooting.md:22`; `local-mcp-testing.md:25,47,54,73,102,130,144,165`; `packaging.md:36-44`.
  - Keep output fields.
  - Wherever an assertion or fallback was described, state the bound-project rule once: content tools act on the bound project only; with no verified binding (a configured `--project` is grounded by status first) they fail closed with `binding_conflict`; call `bind_project` first.
  - In `local-mcp-testing.md`, add a `bind_project` step before the smoke calls.
  - In `codebase-review.md:41-43`, drop the *(target)* marker.
- [ ] **Step 2: Update `AGENTS.md`.** In the project overview and the write-safety model, say that content tools act only on the bound project and fail closed when unbound. `get_project_status` no longer takes a path.
- [ ] **Step 3: Update `troubleshooting.md`.** Add a "bind first" bullet for `binding_conflict` with the bind-first text.
- [ ] **Step 4: Update `IMPROVEMENT_LOG.md`.** Add a C-0 DONE entry. Add open follow-ups:
  - the worker backstop (content reads require `ExpectedSessionIdentity`);
  - the tree-cursor message bug (`ProjectTreeBrowseCoordinator` passes the category as the message);
  - the unreachable `preserveUnboundEpoch` branches (C-6);
  - `ProjectSessionBinding.TryResolveWithSnapshot`'s unbound-with-path branch, now reached only by internal status calls and test helpers.

  Mark the Round-4/5 entries at `:503-522` and `:733-735` "superseded by C-0". Do not rewrite them.
- [ ] **Step 5: Update the plans.**
  - In the review plan, under C-0, add "Regression evidence", "Full offline evidence" and "Live gate" sub-bullets in the B-item style, and correct "8 catalogs" (only Network had an explicit field spec).
  - In the Workstream C plan, mark the C-0 decisions resolved, with a link to this plan.
- [ ] **Step 6: Check.**
  - Run: `git grep -n -i "currently open in TIA Portal\|repeat the same project\|same project path" -- README.md AGENTS.md docs/ARCHITECTURE.md docs/SupportedOperations docs/guides docs/development TiaMcpServer`
  - Expected: no output.
- [ ] **Step 7: Commit.** `docs: document bound-project-only content tools`

**Task test command:** the Step 6 grep.
