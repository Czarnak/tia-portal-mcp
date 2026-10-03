# Lifecycle in Read-Write and Runtime Project Binding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the six lifecycle tools into `read-write` behind a per-call elicitation prompt, make `full` run them without prompts, stop implicit project opens, and add a `bind_project` tool that adopts an already-open project in every mode.

**Architecture:** Confirmation is a pipeline policy derived from the binding gate's access mode (done). Binding gains one worker listing operation, one worker selection operation that can re-attach inside the running worker, and one compare-and-set host transition. A new standalone structured tool exposes it.

**Tech Stack:** .NET 10 host and tests, netstandard2.0 contracts, net48 Openness worker, ModelContextProtocol C# SDK, xUnit, FakeWorker. No new dependencies.

**Spec:** [Lifecycle in read-write, mode-derived confirmation, and runtime project binding](../specs/2026-10-01-lifecycle-tiers-and-project-binding-design.md), accepted 2026-10-01, amended by the errata below.

**Status:**
- Phase A (Tasks 1–4) is implemented and merged to `main` in PR #104 (merge `24913ac`, commits `d932d19`..`85aec97`).
- Phase B was revised on 2026-10-02 against that code and accepted. It is not started. Branch it from `main` `24913ac`, for example `feat/bind-project`.
- Phase B tasks are renumbered 5–14. The old Tasks 5–12 map to the new Tasks 5, 6+7, 8, 9, 10, 11, 12+14 and 13.
- All documentation work is deferred to Task 14, after live verification.

## Global Constraints

- Three-layer enforcement stays: discovery, host authorization before any worker activity, worker authorization before dispatch. Unknown operations are denied in every mode.
- Presets: `read-only` = Observe, TemporaryExport, SafetyRead, SessionSelection. `read-write` = + Compile, ProjectMutation, ProjectLifecycle. `full` = + OnlineControl.
- Tool counts: `main` is at 4/14/14, and the final counts are 5/15/15.
- No request opens a project except `open_project` and `create_project`, in any mode.
- `bind_project` never sends open, create, close or save. Ordinary reads never bind or switch.
- A host-side rejection sends nothing to the worker. That covers `validation_error`, and `binding_conflict` for a different project without `forceRebind`. The only rejection after a worker round trip is the headless detach refusal (erratum E1).
- **Documentation is deferred.** Before Task 14, no task edits `README.md`, `AGENTS.md`, `SECURITY.md`, `docs/` or the spec. The one exception is Task 13's live acceptance report and its index entries. Docs on `main` stay stale on purpose until Task 14. Tests that pin documentation (`Diagnostics/CiWorkflowTests`) must stay green against the unchanged docs.
- No new token-bound surface and no new `SafetyRead` entry. The network and batch token flows stay unchanged.
- Structured contract rules apply:
  - one `CanonicalJson.Serialize` per response;
  - typed worker payloads with explicit nulls;
  - no `[LegacyNullOmission]`;
  - no nested JSON strings.
- Startup default stays `read-write`; installer default stays `read-only`. No version bump or tag.
- Build before any test, because `RemovedOptionTests` launches the built host (`docs/development/building.md`). From PowerShell, run `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true`, then `dotnet test TiaMcpServer.Tests --no-build [--filter …]`. Never pass `/p:` through Bash.
- Inside the worker, Siemens' session type is `Siemens.Engineering.TiaPortalSession`. The unqualified name is the worker's own class.
- Fixtures that set process-wide FakeWorker environment variables (`FakeWorkerUiOpenProject`, the new `FakeWorkerPortals`) are used only from `RealWorkerProcessCollection` or `"Mcp protocol serial"`.
- Every new fixture states its access mode. `FakeWriteBindingGate.AccessMode` defaults to `ReadOnly`.
- Commit after each task with a one-line conventional message and no body. No push or other remote write. No live TIA run without the maintainer.
- FakeWorker and source-contract tests do not prove live Siemens behaviour. Task 13 does.

## Spec Errata

These corrections to spec §4.2–§4.4 are forced by `main`'s code. Implementation follows them now. Task 14 writes them into the spec after the live run, together with anything the run corrects.

- **E1. Headless refusal.**
  - The worker reports the headless detach refusal (§4.4) on the wire as `guard_blocked`, not `binding_conflict`. `InvokeWorkerAsync` invalidates a verified binding on every worker `binding_conflict` (`OpennessWorkerClient.cs:2181`), and worker identity mismatches use that category.
  - The host maps `guard_blocked` to a rejected `binding_conflict` response.
  - This rejection follows one `select_portal_project` round trip.
- **E2. Pre-detach refusal set.**
  - The set is {`target_not_found`, `target_ambiguous`, `guard_blocked`}. The worker emits these only before any handle, path or generation change.
  - During a switch, the host keeps the previous verified binding only for these three.
  - Every other failure invalidates it: a worker `binding_conflict`, timeout, crash, `protocol_error` or postcondition failure.
- **E3. Binding after a failed call.**
  - A failed outcome reports `binding` as it stands after the call, which can be `invalidated` after a failed switch, not "unchanged".
  - `binding` and `previousBinding` are never null; an unbound session projects as state `unbound`.
  - `transition` is `none` on every non-success.
- **E4. "configured" is `configured_unverified`.** State strings are the `ProjectBindingSnapshot` constants.
- **E5. Verification and portals.**
  - Post-adopt verification and Reverify use `get_project_status`, which is Observe and so works in read-only. `get_basic_project_status` is ProjectLifecycle.
  - `project` carries the status read's result with `Metadata = null`.
  - `portals` comes only from a listing sent in this call. A `Select(path)` that fails with `target_not_found` or `target_ambiguous` sends one listing afterwards.

## Review Focus

Phase A items verified in PR #104: a `read-write` lifecycle call cannot mutate without a confirmed prompt, and `OnlineControl` stays full-only. Open items:

1. **Failed switches.** A failed switch never leaves a verified binding that points at a Portal the worker detached from. Owners: Task 6, where the worker emits E2 categories only before detaching, and Task 8, where the host keeps the binding only for E2.
2. **Read-only binding.** `bind_project` in read-only sends only Observe and SessionSelection operations. Owners: Tasks 8 and 9.
3. **Who changes the binding.** A read never binds; only `bind_project` and the lifecycle tools change the binding. Owners: Tasks 5, 8 and 9.
4. **No implicit opens.** `ProjectLifecycleService.EnsureProject` still calls `session.OpenProject` for save, save-as, archive and close. Owner: Task 6.
5. **FakeWorker gate.** FakeWorker Portal support keeps the explicit source-state gate from `fbdd0cf`, so ordinary requests never establish or switch project state. Owner: Task 7.
6. **Project-tree cursors.** Cursors never replay a snapshot across a binding change, and the binding is captured inside the serialized section. Owner: Task 10.

## File Map

```text
Done in Phase A: Safety/Pipeline/WriteConfirmationPolicy, Cli/RemovedCliOptions, ProjectOpenPolicy,
            TestSupport/FakeWorkerUiOpenProject, TestSupport/ScriptedStatusTransport,
            docs/development/building.md; removed UserConfirmationParser/Registration/Options
Contracts:  OperationCapability, OperationPolicyCatalog, McpAccessMode, ProjectSessionBinding,
            ProjectOpenPolicy, WorkerProtocol, + TiaPortalProcessListInfo, + TiaPortalProcessInfo,
            + PortalProjectSelectionInfo
Worker:     Program, Openness/TiaPortalSession, Openness/TiaPortalTargetSelector,
            Openness/ProjectLifecycleService, + Openness/TiaPortalProcessInventory,
            + Openness/PortalDetachGuard
FakeWorker: Program
Host:       Worker/OpennessWorkerClient, + Worker/ProjectBindingDecision,
            + Worker/ProjectBindingPayloadContract, + Worker/ProjectTreeSnapshotCallResult,
            + Tools/ProjectBindingTools, Tools/McpToolRegistration, Tools/StandaloneToolResponses,
            Tools/StructuredStandaloneResult, ProjectTree/{CursorCodec, PageProjector, SnapshotStore,
            BrowseCoordinator}, Diagnostics/Checks/{ProjectBindingCheck, TiaPortalProcessCheck}
Tests:      + TestSupport/FakeWorkerPortals, + TestSupport/FakeWorkerRequestLog (promoted from
            LifecycleMcpProtocolTests), TestSupport/McpProtocolTestHarness
Docs (Task 14 only): README, AGENTS, SECURITY, docs/ARCHITECTURE, docs/guides/*,
            docs/SupportedOperations/*, docs/development/{local-mcp-testing, packaging},
            docs/README, docs/IMPROVEMENT_LOG, docs/roadmap/json-contract, spec
```

`TiaMcpServer.Tests.csproj` links host `Tools/` and `Worker/` files and worker files one by one, so each new file gets an explicit `<Compile Include>`, following `ProjectRebindStatePayloadContract.cs`. `ProjectTree/`, `Diagnostics/`, `Cursors/` and `Safety/Pipeline/` are wildcards.

---

## Phase A: tiers and confirmation (done, PR #104)

Tasks 1–4 shipped as planned in `e977e8c`. These are the facts Phase B relies on:

- **Confirmation.** `ConfirmationMode { AskUser, Policy }` and `WriteConfirmationPolicy.For(McpAccessMode)`. The domain default members are `ConfirmsEveryCall` and `DescribeForConfirmation`. `GuardSatisfactions { User, Policy }`. The audit record is `RecordVersion = 2` with `confirmation {by, outcome}`.
- **CLI.** `RemovedCliOptions.TryGetError` fails startup on `--confirm-with-user` with the migration message.
- **Project open policy.**
  - `ProjectOpenDecision { UseAttached, RequestedNotOpen, Refuse }`.
  - `ProjectOpenPolicy.RefusalMessage(currentPath, requestedPath, McpAccessMode)` (mode parameter added in `fbdd0cf`) and `NotOpenMessage(requestedPath, McpAccessMode)` name `open_project` only where the mode registers it.
  - The worker's read-only-specific messages are gone.
  - `EnsureRequestedProjectOpen` never opens. The residual open in `ProjectLifecycleService.EnsureProject` is Task 6.
- **FakeWorker (`fbdd0cf`).**
  - The open project is explicit state from `TIA_MCP_FAKE_WORKER_UI_OPEN_PROJECT`, set by `FakeWorkerUiOpenProject`.
  - A pre-dispatch gate applies `ProjectOpenPolicy` to every method except `hello`, `open_project`, `create_project` and `probe_open_project_rebind`.
  - `fakeProjectPath` changes only on open, create and save-as, and `FakePortalProcessId = 4242` is constant.
  - With no `projectPath`, the scenario key comes from the UI-open project.
  - `--access-mode` is parsed, and requests log to `TIA_MCP_FAKE_WORKER_REQUEST_LOG`.
- **Never-bind tests.** These stay green through Phase B:
  - `OpennessWorkerClientIntegrationTests.UnboundSession_UnrelatedReadSuccess_DoesNotBindOrSwitchProject`, `UnboundSession_DirectStatusSuccess_DoesNotBindSession` and `DirectStatusSuccess_DoesNotBindUnboundSession`;
  - `StandaloneStatusToolTests.NoProject_StatusIsStructured_WithoutBindingAnUnboundSession`;
  - `ReadWriteModeCeilingTests.ReadWrite_InitialAttachmentStillWorks_FromStartupOrUiOpen` and `ConfiguredProjectCannotBePromotedFromUnopenedCannedStatus`;
  - `OpennessWorkerClientIntegrationTests.ConfiguredSession_WorkerRefusesDifferentOpenProject_LeavesBindingUnverified`.
- **Tool counts.** 4/14/14 is pinned in `AccessModeDiscoveryTests`, `StandaloneToolsProtocolTests` (`ReadOnly ? 4 : 14`) and README line 19.
- **Docs.** Docs on `main` still describe `--confirm-with-user`, `acknowledge` and 4/8/14. This is deliberate; Task 14 updates them.

---

## Phase B: `bind_project`

### Task 5: Session-selection contracts

**Files:**
- Modify in `Contracts/`: `OperationCapability.cs`, `OperationPolicyCatalog.cs` (the entries and the `IsAllowed` doc comment), `McpAccessMode.cs` (the `ReadOnly` doc comment and its "never opens, creates, saves or closes" promise) and `ProjectSessionBinding.cs`.
- Create `Contracts/TiaPortalProcessListInfo.cs`, `TiaPortalProcessInfo.cs` and `PortalProjectSelectionInfo.cs`.
- Tests: `Safety/AccessModeTierTests`, `Safety/ReadOnlyModeTests`, `Project/ProjectSessionBindingTests`, and a new `Worker/PortalSelectionPayloadContractsTests`.

**Interfaces (produces):**
- `OperationCapability.SessionSelection`, allowed by all presets. `RequiresExpectedSessionIdentity` gains an explicit `SessionSelection => false` arm; the `_ => true` default stays.
- Catalog: `list_tia_portal_processes` → `Observe`; `select_portal_project` → `SessionSelection`.
- `WorkerTransportFailureGuidance.IsSafeRead` is unchanged. Task 8 adds the selection guidance.
- Payload types:
  - `TiaPortalProcessListInfo { int? AttachedProcessId; List<TiaPortalProcessInfo> Processes }`
  - `TiaPortalProcessInfo { int ProcessId; string? ProjectPath; bool HasUserInterface; bool AttachedByThisWorker }`
  - `PortalProjectSelectionInfo { int? PreviousProcessId; string? PreviousProjectPath; bool PreviousProjectWasWorkerOpened; bool? PreviousProjectIsModified; bool Reattached }`
  - All three are unmarked, so nulls are written. `WorkerPayloadNullPolicyRegisterTests` is unchanged.
- `ProjectSessionBinding.TryAdoptVerified(ProjectBindingSnapshot expected, WorkerSessionIdentity? identity, out string? error) -> bool`. It checks, in order:
  1. `TryValidateCompleteIdentity`: a null or incomplete identity fails.
  2. Already verified with `SameIdentity`: return true with no transition.
  3. `!expected.SameBinding(current)`: fail and leave the binding unchanged.
  4. `SetVerified`: new binding ID and revision + 1.

  It applies no path or `forceRebind` policy and does not call `CanBind`; Task 8 owns that policy.
- `WorkerProtocol` is unchanged here. Task 6 adds the capabilities together with the handlers.

**Steps:**
- [ ] Write the tests:
  - Update `Presets_ReadWriteAllowsLifecycle_FullAddsOnlyOnlineControl` so its read-only predicate includes `SessionSelection`.
  - Add `Presets_SessionSelectionInEveryMode` and `SessionSelection_NoExpectedIdentity`.
  - Add rows for both operations to `ReadOnlyModeTests.ExpectedSessionIdentityPolicy_IsFailClosed` (false) and `ReadOnlyMode_AllowsApprovedOperations`.
  - `TryAdoptVerified_FromUnboundConfiguredInvalidatedAndOtherVerified_Verifies` asserts a new ID, revision + 1, and that the identity's path P can replace the configured path C.
  - `TryAdoptVerified_SameIdentity_NoRevisionBump`, `TryAdoptVerified_StaleExpected_FailsUnchanged`, `TryAdoptVerified_IncompleteIdentity_Fails` and `TryAdoptVerified_NullIdentity_Fails`.
  - `NewPayloads_WriteExplicitNulls`: `WorkerJson.SerializePayload` writes every nullable member as null, and `OmitsNullMembers` is false.
  - `NewPayloads_ReaderRejectsMissingMember`: a theory over each member, using `CanonicalJson.DeserializeWorkerPayload`.
- [ ] Build, run with `--filter "FullyQualifiedName~AccessModeTier|FullyQualifiedName~ReadOnlyMode|FullyQualifiedName~ProjectSessionBinding|FullyQualifiedName~PortalSelectionPayload"`, and confirm the new tests fail.
- [ ] Implement, rerun, then run the full suite. Commit.

### Task 6: Worker listing and selection

**Files:**
- Create `OpennessWorker/Openness/TiaPortalProcessInventory.cs` (not linked into tests because it uses Siemens types) and `Openness/PortalDetachGuard.cs` (linked).
- Modify `Openness/TiaPortalSession.cs`, `TiaPortalTargetSelector.cs`, `ProjectLifecycleService.cs`, `OpennessWorker/Program.cs` and `Contracts/WorkerProtocol.cs`.
- Tests: `Worker/TiaPortalTargetSelectorTests`, `Worker/WorkerOpenPolicySourceTests`, `Worker/WorkerProtocolHandshakeTests`, and new `Worker/PortalDetachGuardTests` and `Worker/PortalSelectionSourceTests`.

**Interfaces (produces):**
- `TiaPortalProcessCandidate(int id, string? projectPath, bool hasUserInterface = false)`. A missing mode reads as headless.
- `TiaPortalProcessInventory.Read() -> IReadOnlyList<PortalInventoryEntry>`, where `PortalInventoryEntry(TiaPortalProcess Process, TiaPortalProcessCandidate Candidate)`.
  - It never attaches.
  - It reads the path and `Mode` defensively; a failed read gives a null path or headless.
  - `TryReadAdvertisedProjectPath` moves into it.
  - `Connect`, the listing handler and `SelectPortalProject` all use it.
- `TiaPortalTargetSelector`:
  - `SelectExactProcessId(candidates, string projectPath) -> int`. Zero Portals or no exact match gives `target_not_found`. Duplicate advertisers give `target_ambiguous`. There is no sole-Portal fallback.
  - `SelectProcessId` is unchanged.
  - `ToProcessInfos(candidates, int? attachedPid) -> List<TiaPortalProcessInfo>`, ordered by PID.
- `PortalDetachGuard.Evaluate(bool hasUserInterface, int otherClientCount, bool hasProject, bool? projectIsModified) -> string?`.
  - It refuses only when the Portal is headless, `otherClientCount == 0`, `hasProject` is true and `projectIsModified != false`.
  - Inputs come from the attached process: `Mode`, `AttachedSessions` excluding this worker's own session, and `Project.IsModified`.
  - A failed read counts as headless, zero other clients and an unknown modified state, so the guard fails closed.
- **Pre-detach rule (E2).** The worker raises `target_not_found`, `target_ambiguous` and `guard_blocked` (the guard refusal) only before any handle, path or generation change. After `Disconnect`, every failure is `worker_operation_failed`: attach error, PID mismatch, missing project, unreadable path.
- `TiaPortalSession.Disconnect()`.
  - Order: unsubscribe the three events; clear the project handle, ownership flag and selected path; release the Portal handle.
  - The generation still advances.
  - `Dispose` calls it.
- `TiaPortalSession.SelectPortalProject(string projectPath) -> PortalProjectSelectionInfo`.
  - It always does the inventory lookup and `SelectExactProcessId` first.
  - **Not attached:** attach, check the PID, select.
  - **Same process:** look up the exact project with `SelectProjectIndex`.
    - A miss is `target_not_found` and leaves the handle untouched.
    - A hit calls `AdoptProject`.
    - `SelectOpenProject` is not reused, because it clears the handle on a miss.
  - **Different process:** guard, `Disconnect`, attach, check the PID, select.
  - `Reattached` is true whenever this call invoked `Attach()`, including a first attach.
  - It never calls open, close or save.
- Worker handlers:
  - Both bypass `WithSession`/`EnsureConnected` and run through `Execute`.
  - `select_portal_project` requires `ProjectPath`; a missing path is `validation_error`.
  - It calls `ValidateExpectedSessionIdentityForRequest(…, allowMissingExpectedIdentity: true)` against the cached identity before the lookup.
- `ProjectLifecycleService.EnsureProject` no longer opens. It applies `ProjectOpenPolicy.Decide`:
  - `RequestedNotOpen` → `access_denied`;
  - `Refuse` → `binding_conflict`;
  - otherwise it uses the open project.
- `Main` disposes the shared session after the read loop. The host's 2 s shutdown grace is unchanged; Task 13 observes whether the detach finishes within it.
- `WorkerProtocol.RequiredCapabilities` adds `portal-process-listing-v1` and `portal-project-selection-v1`. The FakeWorker advertises the shared list, so do not release between Tasks 6 and 7.

**Steps:**
- [ ] Write the tests:
  - Selector: `SelectExactProcessId_ExactMatch_ReturnsPid`, `_NoSoleFallback`, `_NoPortals_TargetNotFound`, `_DuplicateAdvertisers_Ambiguous`, and `ToProcessInfos_SortedMarksAttached`.
  - Guard theory: headless, sole client, and modified or unknown state refuses. A user interface, other clients, an unmodified project or no project allows.
  - Source tests:
    - `ListingBypassesWithSession`, `SelectionBypassesWithSession`, `SelectionValidatesSuppliedIdentityBeforeLookup`;
    - `SelectPortalProjectNeverOpensClosesOrSaves`, `SelectPortalProjectDoesNotReuseSelectOpenProject`;
    - `DisconnectUnsubscribesBeforeRelease`, `GuardRunsBeforeDisconnect`, `NoPreDetachCategoryAfterDisconnect`;
    - `ConnectAndListingShareInventory`, `MainDisposesSharedSessionAfterLoop`;
    - `WorkerSource_OnlyOpenAndCreateCallOpenProject`: `session.OpenProject(` appears only in the open and create paths.
  - Handshake: InlineData `missing-portal-selection-capabilities` on `LegacyWorker_HandshakeFailsBeforeOriginalEngineeringMethodIsSent`, and `ProtocolRequiresPortalSelectionCapabilities`.
- [ ] Build and confirm the new tests fail.
- [ ] Implement. Build to confirm the worker compiles against the stubs.
- [ ] Rerun the tests and the suite. Commit.

### Task 7: FakeWorker Portal inventory

**Files:**
- Modify `TiaMcpServer.FakeWorker/Program.cs`, including its header comment.
- Create `TiaMcpServer.Tests/TestSupport/FakeWorkerPortals.cs` and `TestSupport/FakeWorkerRequestLog.cs`. The request log is promoted from the private `LifecycleMcpProtocolTests.LifecycleRequestLog`, and that test switches to it.
- Tests: `Worker/FakeWorkerIdentityEnforcementTests` and a new `Worker/FakeWorkerPortalInventoryTests` (`RealWorkerProcessCollection`).

**Interfaces (produces).** Tasks 8–10 use this single model.
- The env var `TIA_MCP_FAKE_WORKER_PORTALS` declares the inventory as `;`-separated entries of the form `pid|path-or-empty|ui|headless|otherClients|modified`. `FakeWorkerPortals : IDisposable` sets it with typed entries.
- The initially attached Portal is the entry whose path equals the UI-open project. With no match, the worker starts unattached.
- With no inventory declared, today's single-Portal 4242 behaviour stays.
- `fakePortalProcessId` and `fakeProjectPath` are mutable.
  - They change only through open, create, save-as and a successful `select_portal_project`.
  - Every change bumps the generation.
  - Stamps and `ValidateExpectedSessionIdentity` use the mutable PID.
- Both new methods are exempt from the source-state gate, and the same E2 pre-detach rule holds:
  - not advertised → `target_not_found`;
  - two advertisers → `target_ambiguous`;
  - a headless, sole-client, modified previous Portal → `guard_blocked`.
- `PreviousProjectIsModified` and `PreviousProjectWasWorkerOpened` follow the entry flags.
- Failure injection is keyed by target file name through `ScenarioKey`:
  - `portal-switch-fails-after-detach.ap21`: `worker_operation_failed`, leaving the worker unattached;
  - `portal-switch-hang.ap21`;
  - `portal-switch-crash.ap21`;
  - `portal-selection-malformed.ap21`: an invalid payload.

**Steps:**
- [ ] Write the tests:
  - `Listing_ReportsInventorySortedAndAttached`
  - `Select_SwitchesPidPathAndGeneration`
  - `Select_HeadlessModifiedPrevious_GuardBlockedNoChange`
  - `Select_NotAdvertised_TargetNotFoundNoChange`
  - `Select_FailsAfterDetach_LeavesUnattached`
  - `NoInventory_KeepsSinglePortalBehaviour`
  - `FakeWorkerIdentityEnforcementTests.OrdinaryReadCannotSelectPortalProject` (the gate is intact)
- [ ] Build and confirm the tests fail. Implement, rerun, then run the full suite. Commit.

### Task 8: Host binding method

**Files:**
- Create `TiaMcpServer/Worker/ProjectBindingDecision.cs` and `Worker/ProjectBindingPayloadContract.cs`, both linked.
- Modify `Worker/OpennessWorkerClient.cs`. This includes `WorkerTransportFailureGuidance`, and the `ProbeProjectStatusForLifecycleAsync` summary, which still says the worker may open a project.
- Tests: new `Worker/ProjectBindingDecisionTests`, `Worker/ProjectBindingPayloadContractTests` and `Worker/BindOpenProjectIntegrationTests`, plus `Worker/WorkerTransportFailureGuidanceTests`. `BindOpenProjectIntegrationTests` runs in `RealWorkerProcessCollection` and uses `FakeWorkerPortals`, `FakeWorkerUiOpenProject` and `FakeWorkerRequestLog`.

**Interfaces (produces):**
- `ProjectBindingTransitions { Bound = "bound", Unchanged = "unchanged", Switched = "switched", None = "none" }`.
- `ProjectBindingDecision.Decide(ProjectBindingSnapshot current, string? requestedPath, bool forceRebind) -> BindingStep`.
  - `BindingStep` is one of `Reverify`, `ListThenSelect`, `Select(string Path, bool IsSwitch)` or `Reject(string Category, string Message)`.
  - It covers every spec §4.2 row. An invalidated binding reselects its retained path.
  - Paths are canonicalized with `ProjectPathNormalization` and compared `OrdinalIgnoreCase`.
  - An unknown state gives `Reject`.
- `ProjectBindingDecision.ChooseFromListing(IReadOnlyList<TiaPortalProcessInfo>) -> ListingChoice`, where `ListingChoice` is `Select(path)`, `NotFound` or `Ambiguous`. Portals without a project are ignored. Two Portals advertising the same path give `Ambiguous`.
- `ProjectBindingDecision.PreDetachRefusalCategories` holds the E2 set.
- `ProjectBindingPayloadContract`:
  - `DecodeProcessList(string)` allows at most one `AttachedByThisWorker`, and that process must equal `AttachedProcessId`.
  - `DecodeSelection(string payload, ProjectBindingSnapshot before)` requires the previous-project fields to agree with `before` when it was verified.
  - A malformed payload gives `protocol_error` and is never echoed.
- `OpennessWorkerClient.BindOpenProjectAsync(string? projectPath, bool forceRebind, CancellationToken cancellationToken) -> Task<ProjectBindingOutcome>`:
  - **Serialization.** It runs in one serialized section through a new cancellable `ExecuteSerializedBindingOperationAsync` overload, which keeps the ambient re-entrancy. The token is honoured while waiting for the gate and once more before the first send. After any send it is ignored, so a dispatched select is never abandoned.
  - **Sending.** It builds its own `list_tia_portal_processes` and `select_portal_project` requests and sends them through `InvokeWorkerAsync` with a binding-neutral transition that skips `ValidateOrPromoteSessionIdentity`. It never uses `SendBoundProjectRequestAsync`, so there is no configured preflight and no `TryResolveWithSnapshot`. The selection carries `Before`'s identity when `Before` is verified, and null otherwise.
  - **Adoption.**
    - Before adopting, it requires a complete stamped identity whose canonical path equals the selected path; otherwise `postcondition_failed`.
    - It then calls `TryAdoptVerified`, followed by a `get_project_status` for the adopted path through the normal send path.
    - That status read fills `Project`, with `Metadata = null`.
    - Reverify uses the same read.
  - **Failures.** Failure handling follows E2 and E3. `guard_blocked` maps to `IsRejection` with `binding_conflict`. `Portals` follows E5.
  - **Warnings.**
    - When `Reattached` is true, the outcome warns that TIA may have shown the Openness dialog, and that "Yes to all" stops it for this worker binary.
    - A modified previous project that the worker opened gets a warning that it remains open with unsaved changes.
- `WorkerTransportFailureGuidance.SessionSelectionTimeout`, returned for SessionSelection operations. It says the Openness dialog may need an answer, that the previous binding was invalidated, and that `bind_project` can be retried.
- `ProjectBindingOutcome(string Transition, ProjectBindingSnapshot Before, ProjectBindingSnapshot After, ProjectStatusInfo? Project, IReadOnlyList<TiaPortalProcessInfo> Portals, IReadOnlyList<string> Warnings, WorkerCallResult? Failure, bool IsRejection)`.

**Steps:**
- [ ] Write the unit tests:
  - Decide: one test per §4.2 row, plus `Decide_PathCaseOnlyDifference_IsSameProject` and `Decide_UnknownState_Rejects`.
  - ChooseFromListing: `_OneProjectAmongEmptyPortals_Selects`, `_NoProjects_NotFound`, `_TwoProjects_Ambiguous` and `_SamePathTwoPortals_Ambiguous`.
  - `PreDetachSet_ExcludesBindingConflict`.
  - Payload decode: a malformed payload, and a selection whose previous fields disagree with `before`.
  - `TimeoutGuidance_SessionSelection_NamesOpennessDialog`.
- [ ] Write the integration tests:
  - Unbound:
    - `Unbound_SingleProject_Binds`, `Unbound_SeveralPortals_AmbiguousWithPortals`, `Unbound_NoProjects_TargetNotFound`;
    - `Unbound_WithPath_SelectsWithoutListing`, `BindWithPath_NotFound_ListsPortals`.
  - Configured:
    - `Configured_NoPathOrSamePath_SelectsAndAdopts`, which sends no status preflight;
    - `Configured_OtherPathNoForce_RejectedNothingSent`, `Configured_OtherPathForce_Binds`.
  - Invalidated: `Invalidated_NoPath_ReselectsRetainedPath`, `Invalidated_OtherPathNoForce_RejectedNothingSent`.
  - Verified:
    - `Verified_SamePath_UnchangedRevision`, `Verified_NoPath_Reverifies_MismatchInvalidates`;
    - `Verified_OtherPathNoForce_RejectedNothingSent`, `Verified_OtherPathForce_Switches`.
  - Switch refusals that keep the previous binding:
    - `Switch_TargetNotAdvertised_KeepsPrevious`;
    - `HeadlessRefusal_KeepsPrevious`, which sends exactly one select and keeps the same revision.
  - Switch failures that invalidate:
    - `SwitchFailsAfterDetach_InvalidatesPrevious`, `Switch_Crash_InvalidatesPrevious`;
    - `Switch_Timeout_InvalidatesAndNamesOpennessDialog`, `MalformedSelectionDuringSwitch_InvalidatesPrevious`;
    - `Switch_WorkerRestartedSinceVerify_IdentityMismatchInvalidates`, `SelectionIdentityForOtherPath_PostconditionFailed`.
  - Warnings: `Switch_Reattached_WarnsAboutOpennessDialog`, `Switch_PreviousWorkerOpenedModified_Warns`.
  - Verification and read-only:
    - `Bound_ProjectIsTheVerifyingStatusRead`;
    - `ReadOnly_Bind_SendsOnlyObserveAndSessionSelectionOperations`, asserted from the request log.
  - `WaitsForLeaseAndHonoursCancellation`: hold the gate with `ExecuteWithPinnedBindingAsync`, cancel, expect `OperationCanceledException`, and check the request log is empty.
  - `NetworkWriteToken_PreviewedBeforeForceSwitch_RejectedAfterSwitch`.
  - `ReadWriteNoProject_BindThenNetworkWritePreview_PassesBindingGate`: before the bind, the preview returns the binding-required `binding_conflict`; after the bind, it does not.
- [ ] Build and confirm the new tests fail.
- [ ] Implement, rerun, then run the full suite, including the never-bind tests listed under Phase A. Commit.

### Task 9: `bind_project` tool

**Files:**
- Create `Tools/ProjectBindingTools.cs`, which holds the `[McpServerToolType]`, `ProjectBindingToolRegistration` and `BindProjectArgumentValidatingTool : DelegatingMcpServerTool`.
- Modify `Tools/McpToolRegistration.cs`, `Tools/StandaloneToolResponses.cs` (the response records) and `Tools/StructuredStandaloneResult.cs`.
- Modify `TestSupport/McpProtocolTestHarness.RegisterToolType` so that `ProjectBindingTools` goes through its registration. Add the csproj link.
- Tests: the deliberate rewrites listed below, `ToolOutputContractConformanceTests` (new probes plus branches in its probe-setup switch), and a new `Tools/BindProjectToolProtocolTests` in the `"Mcp protocol serial"` collection.

**Interfaces (produces):**
- Tool declaration:
  - `[McpServerTool(Name = "bind_project", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(BindProjectResponse))]`
  - `BindProject(OpennessWorkerClient workerClient, string? projectPath = null, bool forceRebind = false, CancellationToken cancellationToken = default) -> Task<CallToolResult>`
  - The description follows spec §4.1.
- Registration:
  - `ProjectBindingToolRegistration.WithProjectBindingTools(this IMcpServerBuilder)` mirrors `ProjectReadToolRegistration`: `McpServerTool.Create` with `DisallowAdditionalProperties = true`, then the wrapper.
  - `WithAccessModeTools` calls it unconditionally, right after `WithProjectReadTools()`.
- Validation is syntactic only:
  - The only keys are `projectPath` (string or null) and `forceRebind` (boolean or null).
  - The path must pass `Path.IsPathFullyQualified` and have a `.ap21` extension, compared case-insensitively.
  - There is no `File.Exists` check and no worker call.
  - A failure returns the canonical `validation_error` rejection.
  - A path that no Portal advertises is a failed `target_not_found` outcome, not a validation error.
- Outcome mapping. The tool maps `ProjectBindingOutcome` itself instead of using `StandalonePayloadContract.Rejection`:
  - `IsRejection` → `isError: true`, `result: null`.
  - Failure → `status: "failed"`, with `value` (transition `none`, the E3 binding, and the portals) and `failure` set, and `isError: false`.
  - Success → `status: "succeeded"`.
  - `StructuredStandaloneResult.Compose` gains the `bind_project`/`ProjectBindingResult` case, and `Omit` gains `bind_project` guidance.
- Response records. Every member is always written.
  - `BindProjectResponse(ContractVersion, Success, Error, Warnings, StandaloneToolOutcome<ProjectBindingResult>? Result)`, with `Tool => "bind_project"`.
  - `ProjectBindingResult(string Transition, ProjectBindingInfo Binding, ProjectBindingInfo PreviousBinding, ProjectStatusInfo? Project, IReadOnlyList<PortalProcessInfo> Portals)`.
  - `ProjectBindingInfo(string State, string? ProjectPath, int? PortalProcessId)`.
  - `PortalProcessInfo(int ProcessId, string? ProjectPath, bool HasUserInterface, bool IsBound)`, where `IsBound = After.IsVerified && After.PortalProcessId == ProcessId`. It is never copied from `AttachedByThisWorker`.

**Steps:**
- [ ] Rewrite the surface tests that pin the old counts and names:
  - `AccessModeDiscoveryTests.ToolsList_AdvertisesOnlyTheModeSurface`: add `bind_project` to the always-registered set; counts become 5/15/15.
  - `StandaloneToolsProtocolTests`: the count assertion becomes `5 : 15`.
  - `WriteToolMcpAnnotationProtocolTests`: add `bind_project` to both name arrays, change the counts to 15 and 5, and add `Idempotent` to the annotation tuple.
  - `McpToolSchemaTests`: `…ExactlyFifteenApprovedTools`, and `McpReadOnlySurface_RemainsExactlyFiveApprovedTools` with `ProjectBindingTools`.
  - `ReadOnlyModeTests`: `FullSurface_HasExactlyFifteenDistinctTools` and `ReadOnlyMode_HasExactlyFiveTools`.
- [ ] Write the protocol tests:
  - Registration and validation: `BindProject_RegisteredInEveryMode`, `BindProject_RejectsUnknownOrMistypedArguments` and `BindProject_RejectsRelativeOrNonAp21Path`. Both rejection tests assert the transport never started.
  - Outcomes:
    - `BindProject_NoPathSingleProject_Bound`, `_SamePath_Unchanged`, `_OtherPathForce_Switched`;
    - `_OtherPathNoForce_RejectedBindingConflictNothingSent`;
    - `_NoProjects_FailedTargetNotFoundIsErrorFalse`, `_Ambiguous_FailedOutcomeWithPortals_IsErrorFalse`;
    - `_FailedSwitch_ReportsInvalidatedBinding`, `_ReadOnly_Binds`.
  - Response shape:
    - `PortalProcessInfo_IsBoundFollowsVerifiedBindingNotAttachment`;
    - `BindProject_TextEqualsStructuredContent`, `BindProject_DescriptionStatesNeverOpensAndOpennessDialog`.
  - Never-bind and end-to-end: `BindProject_ThenRead_DoesNotChangeBinding`, and the MCP form of `ReadWriteNoProject_BindThenNetworkWritePreview_PassesBindingGate`.
- [ ] Add the conformance probes, each with a setup branch:
  - `bind_project/succeeded` (a single Portal): `isError: false`;
  - `bind_project/rejected` (`relative.ap21`): `isError: true`;
  - `bind_project/ambiguous` (two Portals): `isError: false`.
- [ ] Build and confirm the new tests fail.
- [ ] Implement, rerun, then run the full suite. Commit.

### Task 10: Project-tree cursors follow the binding

**Files:**
- Modify `ProjectTree/ProjectTreeCursorCodec.cs`, `ProjectTreePageProjector.cs`, `ProjectTreeSnapshotStore.cs` and `ProjectTreeBrowseCoordinator.cs`.
- Modify `Worker/OpennessWorkerClient.cs`.
- Create `Worker/ProjectTreeSnapshotCallResult.cs`, linked into the tests.
- Optionally move `ProjectBindingCursorState` to `Cursors/ProjectBindingCursorState.cs`.
- Tests: `Project/ProjectTreeCursorCodecTests`, `ProjectTreeBrowseCoordinatorTests`, `ProjectTreePageProjectorTests`, and the direct callers in `ProjectTreeWorkerProtocolTests` and `OpennessWorkerClientIntegrationTests`.
- The `ARCHITECTURE.md` and `PROJECT_OPERATIONS_SUMMARY.md` updates for this behaviour go in Task 14.

**Interfaces (produces):**
- `ProjectTreeCursorState` gains `ProjectBindingCursorState HostBinding`, reusing the record in `Network/HardwarePageCursorState.cs`. There is no second comparison.
  - Encode stamps `ProjectBindingCursorState.FromSnapshot`. An unbound binding encodes as `IsBound=false` with null fields.
  - The codec rejects an inconsistent `HostBinding` as `invalid_cursor`.
  - A forged payload in the old format, signed with the same key, is `invalid_cursor`.
  - A cursor from an earlier host process stays `snapshot_unavailable`, because cursor keys are process-scoped.
- `ProjectTreeSnapshotCandidate` and `ProjectTreeSnapshotView` carry `HostBinding`.
- `BrowseProjectTreeV3SnapshotAsync` returns `ProjectTreeSnapshotCallResult(WorkerCallResult WorkerResult, ProjectBindingSnapshot HostBinding)`.
  - `HostBinding` is captured after the call, inside the same serialized section, as `HardwarePageWorkerCallResult` does.
  - It is never captured before or after the section, because a configured `--project` gets promoted during the first browse.
- Coordinator:
  - The production constructor keeps its signature and passes `() => workerClient.BindingSnapshot`. The test constructor gains `Func<ProjectBindingSnapshot> captureBinding`.
  - `BrowseContinuation` checks `HostBinding.Matches(captureBinding())` right after `Decode`, before the snapshot lookup. A mismatch returns `cursor_binding_mismatch` with the message "The project binding changed after this project-tree cursor was issued; start again without a cursor." and does not refresh the snapshot TTL.

**Steps:**
- [ ] Write the tests:
  - `Cursor_LegacyFormat_InvalidCursor`: a 3-member state signed through `Sign` with the codec's key.
  - `Encode_RejectsInconsistentHostBinding`.
  - `EncodeDecode_RoundTripsStateWithHostBinding`, replacing the three-field round-trip test.
  - `Cursor_AfterBindingChange_BindingMismatch`, `Cursor_SameBinding_Continues`, `Cursor_Unbound_Continues` and `Cursor_ConfiguredBindingPromotedByInitialBrowse_Continues`.
  - Optionally, `BrowseThenForceBind_ContinuationBindingMismatch`, using the Task 7 inventory.
  - Update every existing `ProjectTreeCursorState` construction to the new shape.
  - `ContinuationUsesCachedSnapshotAfterPersistentWorkerIsDisposed` stays green.
- [ ] Build and confirm the new tests fail.
- [ ] Implement, rerun, then run the full suite. Commit.

### Task 11: Guidance strings and doctor

**Files:**
- `Contracts/ProjectSessionBinding.cs`: `RebindInstruction`, and the binding-required error in `TryGetVerified`.
- `Contracts/ProjectOpenPolicy.cs`: `RefusalMessage`, `NotOpenMessage`, and a new `NoProjectOpenMessage(McpAccessMode)`.
- Worker strings:
  - `OpennessWorker/Program.cs`: the no-project literal in `EnsureRequestedProjectOpen`, and the stale "Provide a projectPath argument" in `WithProject`. The same stale text is in `ProjectLifecycleService.EnsureProject`.
  - `FakeWorker/Program.cs`: its copy of the no-project literal.
  - `Openness/TiaPortalSession.cs`: the missing-identity guidance.
  - `TiaPortalTargetSelector.AmbiguousProcessSelection`.
- `Worker/OpennessWorkerClient.cs`: the `RequireVerifiedWriteBindingAsync` doc comment and the `RegroundInvalidatedSourceForOpenAsync` refusal.
- `Diagnostics/Checks/ProjectBindingCheck.cs` and `TiaPortalProcessCheck.cs`.
- Tests:
  - `Batch/WriteBatchToolsBehaviorTests`, which pins the full binding-required string;
  - `Project/ProjectSessionBindingTests` and `Project/ProjectOpenPolicyTests`;
  - `Worker/TiaPortalTargetSelectorTests` and `Worker/TiaPortalSessionBindingGuidanceSourceTests`;
  - `Diagnostics/ProjectBindingCheckTests` and `Diagnostics/TiaPortalProcessCheckTests`.

**Interfaces:**
- Every listed string names `bind_project` first.
- `RebindInstruction` stays mode-agnostic, because the binding has no access mode. It names `bind_project` with `forceRebind=true`, then `open_project` as read-write/full only.
- The `ProjectOpenPolicy` messages add `open_project` only where the mode registers it.
- The worker and FakeWorker share `NoProjectOpenMessage`.
- Doctor reports Warning instead of Failed in two cases: a writable mode without `--project`, and several Portals while unbound. The remediation names `bind_project`, with `--project` as an alternative. The read-only unbound remediation names `bind_project` too.

**Steps:**
- [ ] Write the tests:
  - `BindingRequiredMessage_NamesBindProjectFirst`
  - `RefusalAndNotOpenMessages_NameBindProjectInEveryMode`
  - `AmbiguousProcessSelection_NamesBindProjectWithPath`
  - `GuidanceStrings_NameBindProject`, extending the existing source test so `bind_project` appears before `open_project`
  - `Doctor_WritableWithoutProject_Warning`, a theory over read-write and full, replacing `NoBinding_WritableMode_ReturnsFailed`
  - `Doctor_MultipleProcessesUnbound_Warning`, replacing `MultipleTiaProcesses_UnboundWritableMode_ReturnsFailed`
- [ ] Build and confirm the tests fail.
- [ ] Implement, rerun, then run the full suite. Commit.

### Task 12: Offline verification and branch review

**Steps:**
- [ ] Build.
- [ ] Run `dotnet test TiaMcpServer.Tests --no-build` with coverage from `coverage.runsettings`, then `scripts/verify-coverage-threshold.ps1 -MinimumLineRate 0.80`.
- [ ] Expect zero failures. `CiWorkflowTests` must still pass against the unchanged docs.
- [ ] Review the whole branch diff against Review Focus and the errata, using independent reviewer subagents.
- [ ] Fix the findings. Commit each fix.
- [ ] Keep the offline evidence (commands, test counts, coverage, review verdicts) in the session notes for the Task 14 IMPROVEMENT_LOG entry. Edit no docs.

### Task 13: Live acceptance (maintainer-run)

Needs the maintainer, TIA Portal V21, and two Portal processes with projects A and B. Follow `docs/development/local-mcp-testing.md` for the dev build; its stale mode text is corrected in Task 14. Do not start without explicit authorization.

- [ ] Run `bind_project` in every mode:
  - no path: expect `target_ambiguous` with both Portals listed;
  - bind A, then a write in read-write;
  - bind B without force: expect a rejection;
  - bind B with force: expect a switch, and record whether the Openness dialog appeared.
- [ ] In read-only, bind with no path, then run `browse_project_tree`.
- [ ] On host exit, confirm the detach finished within the 2 s grace: no Openness-client crash, and the previous Portal keeps its project.
- [ ] In read-write, repeat the frozen 2026-10-01 lifecycle matrix: six operations, guard coverage, artifacts and restoration.
  - Each call prompts, and a decline mutates nothing.
  - `dryRun` and block guards never prompt.
  - Audit v2 records `user`, `policy` and `none`.
- [ ] In full, run the same calls. None prompts.
- [ ] In full, close a modified project without saving. It executes with `acknowledged: true` and `satisfiedBy: policy`.
- [ ] In read-write, run a read with an unopened `projectPath`. Expect `access_denied`.
- [ ] Confirm that `TiaPortalProcess.ProjectPath` reports the primary project.
- [ ] Note any behaviour that contradicts the spec, the errata or this plan, for Task 14.
- [ ] Write `docs/superpowers/acceptance/reports/<run date>-lifecycle-tiers-bind-project-live-validation.md`.
- [ ] List the report in `docs/README.md` and `docs/superpowers/README.md`, and backfill the missing 2026-09-30 and 2026-10-01 rows in the latter's table. This is the only documentation edit before Task 14. Commit.

### Task 14: Documentation (after live verification)

All deferred documentation lands here, written against the live-verified behaviour. Correct any claim that Task 13 contradicted, including the errata.

**Files:**
- `README.md`:
  - counts, tool list and write-safety paragraph;
  - mode lines and the migrate-to-full note;
  - the duplicated batch-write list in the Network section, which wrongly lists network operations and PLC run/stop.
  - Keep absolute links.
- `AGENTS.md`: overview counts, the write-safety model, and the `agent` provenance.
- `SECURITY.md`: only Network and generic batch writes use tokens.
- `docs/ARCHITECTURE.md`:
  - tiers;
  - the confirmation-configuration section;
  - the acknowledge and `satisfiedBy: "agent"` passages;
  - binding;
  - §7, where the "independent of worker-session and host-binding changes" sentence is replaced.
- `docs/guides/installation.md` (modes, user-confirmation section, read-path attach claim, switching guidance), `mcp-client-configuration.md` and `troubleshooting.md`.
- `docs/SupportedOperations/README.md`, `PROJECT_OPERATIONS_SUMMARY.md` (including `cursor_binding_mismatch`) and `PLC_OPERATIONS_SUMMARY.md`.
- `docs/development/local-mcp-testing.md`, and `docs/development/packaging.md`, which quotes the old refusal message verbatim.
- The `docs/README.md` index descriptions, `docs/IMPROVEMENT_LOG.md` and `docs/roadmap/json-contract.md`.
- The spec: errata E1–E5 written into §4.2–§4.4 and §5.1, a note in §6 that Phase A shipped separately, and the status line.
- `Diagnostics/CiWorkflowTests.InstallationDoc_…`: pin `bind_project` for adopting or switching to an already-open project, and `open_project` only for opening one.

**Steps:**
- [ ] Document:
  - Tool counts and confirmation:
    - counts are 5/15/15;
    - `--confirm-with-user` is removed (quote the `RemovedCliOptions` message);
    - lifecycle tools run in read-write with one prompt per call, run in full with none, and block guards stop the call in every mode;
    - there is no `acknowledge` parameter;
    - audit v2 records `user`, `policy` and `none`.
  - Binding:
    - there are no implicit opens;
    - `bind_project` usage and the Openness dialog;
    - worker ownership is not carried across a detach;
    - the read-only promise is "never opens, creates, saves or closes";
    - doctor reports Warning for an unbound writable session;
    - project-tree cursors reject a binding change.
- [ ] Update IMPROVEMENT_LOG and `json-contract.md`.
  - The 2026-10-01 lifecycle live validation passed, this change invalidated it, and the Task 13 run replaced it.
  - Add the offline and live evidence.
  - Add a follow-up for the `totally-integrated-claude` plugin's `tia-portal-mcp` skill.
- [ ] Set the spec status to "implemented and live-accepted <date>", or note what remains open.
- [ ] Verify that `rg -n -- "--confirm-with-user|8 in read-write|4/8/14|satisfiedBy: \"agent\"|require[s]? full" README.md AGENTS.md SECURITY.md docs --glob '!docs/superpowers/**'` matches only removal notes and the `OnlineControl` (PLC run/stop) tier. `acknowledge` should appear only as a guard severity.
- [ ] Build and run the full suite.
- [ ] Commit `docs: document lifecycle tiers and bind_project`.

## Execution Handoff

Run Phase B serially on a new branch from `main` `24913ac`. Tasks 5–11 share the catalog, `OpennessWorkerClient`, the FakeWorker and the tool registration, so do not run them in parallel.

Use a fresh implementer subagent for each task and an independent reviewer subagent after each one. Task 13 waits for the maintainer, and Task 14 follows it. Preserve unrelated work in the tree.
