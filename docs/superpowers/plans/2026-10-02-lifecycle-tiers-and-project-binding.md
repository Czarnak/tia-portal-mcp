# Lifecycle in Read-Write and Runtime Project Binding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the six lifecycle tools into `read-write` behind a per-call elicitation prompt, make `full` run them without prompts, stop implicit project opens, and add a `bind_project` tool that adopts an already-open project in every mode.

**Architecture:** Confirmation becomes a pipeline policy derived from the binding gate's access mode. Binding gains one worker listing operation, one worker selection operation that can re-attach inside the running worker, and one compare-and-set host transition. A new standalone structured tool exposes it.

**Tech Stack:** .NET 10 host and tests, netstandard2.0 contracts, net48 Openness worker, ModelContextProtocol C# SDK, xUnit, FakeWorker. No new dependencies.

**Spec:** [Lifecycle in read-write, mode-derived confirmation, and runtime project binding](../specs/2026-10-01-lifecycle-tiers-and-project-binding-design.md), accepted 2026-10-01.

**Status:** Plan for review. Branch `feat/lifecycle-tiers-bind-project` at `d4f560e`, from `main` `6b6599b`.

## Global Constraints

- Three-layer enforcement stays: discovery, host authorization before any worker activity, worker authorization before dispatch. Unknown operations are denied in every mode.
- Presets: `read-only` = Observe, TemporaryExport, SafetyRead, SessionSelection. `read-write` = + Compile, ProjectMutation, ProjectLifecycle. `full` = + OnlineControl.
- Final tool counts are 5 / 15 / 15. Phase A ends at 4 / 14 / 14.
- In `read-write`, every non-`dryRun` lifecycle call elicits once. Only `accept` with `confirm: true` proceeds. Every other outcome is `access_denied` with no mutation.
- `full` never elicits. Acknowledge guards are satisfied by policy; block guards stop the call in every mode; `dryRun` never prompts.
- No request opens a project except `open_project` and `create_project`, in any mode.
- `bind_project` never sends open, create, close or save. A rejected bind sends nothing to the worker. Ordinary reads never bind or switch.
- No new token-bound surface and no new `SafetyRead` entry. The network and batch token flows stay unchanged.
- Structured contract rules apply: one `CanonicalJson.Serialize` per response, typed worker payloads, explicit nulls, no `[LegacyNullOmission]`, no nested JSON strings.
- Startup default stays `read-write`; installer default stays `read-only`. No version bump or tag; this change rides the unreleased v4.0.0.
- Build serially from PowerShell: `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true`. Never pass `/p:` flags through Bash.
- Commit after each task with a one-line conventional message and no body. No push or other remote write. No live TIA run without the maintainer.
- FakeWorker and source-contract tests do not prove live Siemens behaviour. Task 12 does.

## Review Focus

1. No path opens a project implicitly; that includes reads and writes with `projectPath` in `read-write` and `full`. Owner: Task 4.
2. A `read-write` lifecycle call cannot mutate without a confirmed prompt; dry runs and block guards never prompt. Owners: Tasks 1 and 3.
3. A failed switch never leaves a verified binding pointing at a Portal the worker detached from. Owner: Task 7.
4. `OnlineControl` stays full-only, including PLC items inside `apply_write_batch`. Owner: Task 3.
5. A read still never binds; only `bind_project` and the lifecycle tools change the binding. Owners: Tasks 7 and 8.

## File Map

```text
Contracts:  OperationCapability, OperationPolicyCatalog, ProjectSessionBinding, ProjectOpenPolicy,
            WorkerProtocol, + TiaPortalProcessListInfo, + PortalProjectSelectionInfo
Worker:     Program, Openness/TiaPortalSession, Openness/TiaPortalTargetSelector,
            + Openness/TiaPortalProcessInventory, + Openness/PortalDetachGuard
Host:       Safety/Pipeline/*, ProjectLifecycle/LifecycleWriteDomain, Tools/ProjectWriteTools,
            Tools/McpToolRegistration, Tools/StandaloneToolResponses, Tools/StructuredStandaloneResult,
            + Tools/ProjectBindingTools, Worker/OpennessWorkerClient, + Worker/ProjectBindingDecision,
            + Worker/ProjectBindingPayloadContract, ProjectTree/ProjectTreeCursorCodec,
            Diagnostics/Checks/*, Cli/HostArgumentFilter, Program
Removed:    Cli/UserConfirmationParser, Cli/UserConfirmationRegistration,
            Safety/Pipeline/UserConfirmationOptions
```

Every new host file under `Tools/` or `Worker/`, and every new linked worker file, gets an explicit `<Compile Include>` in `TiaMcpServer.Tests.csproj`. The project links those directories file by file.

---

## Phase A: tiers and confirmation

### Task 1: Mode-derived confirmation in the write pipeline

**Files:**
- Create `TiaMcpServer/Safety/Pipeline/WriteConfirmationPolicy.cs`.
- Modify in `Safety/Pipeline`: `WriteRun.cs`, `GuardDecisions.cs`, `IWriteDomain.cs`, `WriteConfirmationContext.cs`, `WriteVocabulary.cs`, `WriteAudit.cs`, `WriteExecution.cs`, and the `WriteCall<TItem>` record.
- Modify `ProjectLifecycle/LifecycleWriteDomain.cs` and `Tools/ProjectWriteTools.cs`.
- Tests: `Safety/Pipeline/*` (`GuardDecisionsTests`, `WriteConfirmationPipelineTests`, `WriteExecutionTests`, `WriteExecutionFailureAuditTests`, `JsonlWriteAuditSinkTests`, `PipelineFakes`), `Project/LifecycleGuarded*Tests`, `Tools/McpToolSchemaTests`, `Tools/LifecycleMcpProtocolTests`.

**Interfaces (produces):**
- `enum ConfirmationMode { AskUser, Policy }`
- `WriteConfirmationPolicy.For(McpAccessMode) -> ConfirmationMode`: `Full` maps to `Policy`; every other value maps to `AskUser`, so an unexpected mode fails closed.
- New `IWriteDomain` default members:
  - `bool ConfirmsEveryCall => false`
  - `string DescribeForConfirmation(IReadOnlyList<TItem> items, IReadOnlyList<ItemPlan<TEffect>> plans) => ToolName`
- `WriteConfirmationContext(UserConfirmation? Confirmation)`. The `Options` member is gone.
- `WriteCall<TItem>` loses `Acknowledge`.
- `GuardDecisions.Decide(fired, ConfirmationMode mode, catalog, bool dryRun)` and `DecideLate(fired, mode, catalog)`.
  - `GuardDecisionKind` becomes `{ Proceed, NeedsUser, Blocked }`; `Invalid` and `ValidateAcknowledgeList` are deleted.
  - Policy mode reports acknowledge guards as `acknowledged: true`.
  - In AskUser mode, a late acknowledge guard blocks.
- `GuardSatisfactions` becomes `{ User = "user", Policy = "policy" }`.
- `WriteAuditConfirmation(string By, string Outcome)`:
  - `By` is `user`, `policy` or `none`.
  - `Outcome` is a `UserConfirmationOutcomes` value or `not_requested`. This refines the illustrative outcome list in spec §3.5.
- `WriteAuditRecord` gains `Confirmation` and moves to `RecordVersion = 2`.

**Steps:**
- [ ] Write the tests below.
  - `Policy_FullIsPolicy_EveryOtherValueAsksUser`
  - Decide: `Decide_PolicyMode_AutoSatisfiesAcknowledge`, `Decide_AskUser_AcknowledgeNeedsUser`, `Decide_BlockGuard_BlocksInBothModes`, `Decide_DryRun_NeverBlocksOrAsks`, `DecideLate_AskUser_AcknowledgeBlocks`.
  - Pipeline, with fake domains and an explicit gate mode:
    - `ReadWrite_EveryCallDomain_PromptsOnceBeforeMutation`
    - `ReadWrite_GuardOnlyDomain_NoGuard_NoPrompt`
    - `ReadWrite_NonConfirmedOutcome_DeniesWithoutMutation`, a theory over declined, cancelled, timed_out, unsupported and failed
    - `Full_NeverPrompts_AuditsPolicy`, `BlockGuard_NeverPrompts`, `DryRun_NeverPrompts`, `ConsequenceChangedWhilePending_RefusesStale`
  - Audit: `AuditV2_RecordsConfirmation` covering user-confirmed, policy, dry run and declined.
  - Lifecycle: `LifecycleDomain_ConfirmsEveryCall`; `DescribeForConfirmation_NamesOperationAndTarget` for all six operations, where close names save versus discard.
  - Schema: `LifecycleSchemas_HaveNoAcknowledge`.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Safety.Pipeline|FullyQualifiedName~Lifecycle"`. The new tests should fail.
- [ ] Implement.
  - `RunLeasedAsync` resolves the mode from `_gate.AccessMode`. It prompts once when the mode is AskUser, the call is not a dry run, nothing is blocked, and either `ConfirmsEveryCall` is set or an acknowledge guard fired.
  - The prompt text is `DescribeForConfirmation` plus the acknowledge guard messages. The existing stale re-check stays.
  - `ProjectWriteTools` drops `acknowledge` and `UserConfirmationOptions` and passes only the per-call `UserConfirmation`.
  - Set `FakeWriteBindingGate` fixtures to an explicit mode instead of relying on the read-write default.
- [ ] Rerun the focused tests, then the whole suite. Commit.

### Task 2: Remove `--confirm-with-user`

**Files:**
- Delete `Cli/UserConfirmationParser.cs`, `Cli/UserConfirmationRegistration.cs` and `Safety/Pipeline/UserConfirmationOptions.cs`.
- Create `Cli/RemovedCliOptions.cs`.
- Modify `Cli/HostArgumentFilter.cs` and `Program.cs`.
- Tests: delete `Cli/UserConfirmationParserTests.cs`, update `Cli/Phase1bCliModeTests.cs`, and create `Cli/RemovedOptionTests.cs`.

**Interfaces:** `RemovedCliOptions.TryGetError(string[] args, out string error) -> bool` matches every spelling of the flag. Startup prints the migration message to stderr and exits with the existing invalid-arguments code. The message reads: "--confirm-with-user was removed. Confirmation follows the access mode: read-write asks for every lifecycle call; use --access-mode full to run lifecycle tools without prompts."

**Steps:**
- [ ] Write the tests:
  - `ConfirmWithUser_AnySpelling_FailsStartupWithMigrationMessage` (bare, `=true` and `=false`)
  - `NoFlag_StartupUnaffected`
  - `StartupBanner_OmitsConfirmationSetting`
- [ ] Run them and confirm they fail.
- [ ] Delete the parser, options and registration. Wire `RemovedCliOptions` into startup before host building.
- [ ] Rerun the CLI tests and the suite. Commit.

### Task 3: Lifecycle tools in read-write

**Files:**
- Modify `Contracts/OperationPolicyCatalog.cs` (the preset; also update the "stay full-only" comment on the three internal probes) and `Contracts/McpAccessMode.cs` (doc comments).
- Modify `LifecycleWriteDomain.Validate` so it denies only `ReadOnly`.
- Modify the `ProjectWriteTools` descriptions: "read-write asks the user to confirm each call; full runs it directly".
- Tests: `Safety/AccessModeTierTests`, `ReadOnlyModeTests`, `ReadWriteModeCeilingTests`, `ReadOnlyModeHardeningTests`, `Tools/AccessModeDiscoveryTests`, `StandaloneToolsProtocolTests`, `Project/LifecycleBindingStrategyTests`, `Tools/LifecycleMcpProtocolTests`, plus `Diagnostics/CiWorkflowTests` with any README count line it pins.

**Steps:**
- [ ] Write or rewrite these tests:
  - `Presets_ReadWriteAllowsLifecycle_FullAddsOnlyOnlineControl`
  - `ReadWrite_DeniesOnlineControlBeforeTransport`, including a mixed batch with `start_plc`
  - `ToolsList_AdvertisesOnlyTheModeSurface`, with counts 4/14/14
  - `ToolsCall_CannotReachHiddenLifecycleTools`, for read-only only
- [ ] Write the protocol tests. The theories run over all six tools.
  - `ReadWrite_LifecycleAccept_Mutates`, `ReadWrite_LifecycleDecline_NoMutation`
  - `ReadWrite_NoElicitationCapability_AccessDenied`
  - `Full_Lifecycle_SendsNoElicitation`, `Full_CloseWithoutSaveModified_ExecutesWithPolicyGuard`
  - `ReadWrite_OpenProject_BindsAndUnblocksWrites`
- [ ] Run them and confirm they fail.
- [ ] Change the preset and the domain validation. Move the existing full-mode elicitation tests to read-write.
- [ ] Rerun the tests and the suite. Commit.

### Task 4: No implicit project opens

**Files:**
- Modify `Contracts/ProjectOpenPolicy.cs`.
- Modify `OpennessWorker/Program.cs` (`EnsureRequestedProjectOpen`) and the comment in `OpennessWorker/Openness/ProjectLifecycleService.cs`.
- Modify `FakeWorker/Program.cs` where it simulates opening.
- Tests: `Project/ProjectOpenPolicyTests`, `Safety/ReadWriteModeCeilingTests`, and a new `Worker/WorkerOpenPolicySourceTests`.

**Interfaces:**
- `ProjectOpenDecision.OpenRequested` is renamed `RequestedNotOpen`.
- `ProjectOpenPolicy.NotOpenMessage(string requestedPath, McpAccessMode mode) -> string` names `open_project` only when the mode registers it.
- `EnsureRequestedProjectOpen` uses one path for every mode:
  - nothing open and a path requested: `access_denied`;
  - a different project open: `binding_conflict`;
  - no project open and no path requested: `worker_operation_failed`.

**Steps:**
- [ ] Write the tests:
  - `Decide_NothingOpenWithPath_RequestedNotOpen`
  - `NotOpenMessage_NamesOpenProjectOnlyWhenRegistered`
  - `WorkerSource_EnsureRequestedProjectOpen_NeverOpens`: no `OpenProject` call is reachable from it.
  - FakeWorker protocol `ReadWrite_ReadWithUnopenedPath_DeniedAndNoOpenRequest`
  - Rewrite `ReadWrite_InitialAttachmentStillWorks` so it covers only startup `--project` and the UI-open project.
- [ ] Run them and confirm they fail.
- [ ] Implement, then rerun.
- [ ] Run the serial stub build so the worker compiles.
- [ ] Run the suite. Commit.

---

## Phase B: `bind_project`

### Task 5: Session-selection contracts

**Files:**
- Modify `Contracts/OperationCapability.cs`, `OperationPolicyCatalog.cs`, `ProjectSessionBinding.cs` and `WorkerProtocol.cs`.
- Create `Contracts/TiaPortalProcessListInfo.cs` and `Contracts/PortalProjectSelectionInfo.cs`.
- Tests: `AccessModeTierTests`, `Project/ProjectSessionBindingTests`, `Worker/WorkerPayloadNullPolicyRegisterTests`, `Worker/WorkerProtocolHandshakeTests`.

**Interfaces (produces):**
- `OperationCapability.SessionSelection` is allowed by all presets. `RequiresExpectedSessionIdentity` returns false for it.
- Catalog entries: `list_tia_portal_processes` is `Observe`; `select_portal_project` is `SessionSelection`.
- `TiaPortalProcessListInfo { int? AttachedProcessId; List<TiaPortalProcessInfo> Processes }`
- `TiaPortalProcessInfo { int ProcessId; string? ProjectPath; bool HasUserInterface; bool AttachedByThisWorker }`
- `PortalProjectSelectionInfo { int? PreviousProcessId; string? PreviousProjectPath; bool PreviousProjectWasWorkerOpened; bool? PreviousProjectIsModified; bool Reattached }`. The worker stamps it like any other success.
- `ProjectSessionBinding.TryAdoptVerified(ProjectBindingSnapshot expected, WorkerSessionIdentity identity, out string? error) -> bool`:
  - Compare-and-set on `expected`.
  - Rejects an incomplete identity.
  - Makes no transition when the binding is already verified with the identical identity.
  - Otherwise moves to `verified` with a new binding ID and revision.
- `WorkerProtocol.RequiredCapabilities` adds `portal-process-listing-v1` and `portal-project-selection-v1`.

**Steps:**
- [ ] Write the tests:
  - `Presets_SessionSelectionInEveryMode`, `SessionSelection_NoExpectedIdentity`
  - `TryAdoptVerified_FromUnboundConfiguredInvalidatedAndOtherVerified_Verifies`
  - `TryAdoptVerified_SameIdentity_NoRevisionBump`, `TryAdoptVerified_StaleExpected_FailsUnchanged`, `TryAdoptVerified_IncompleteIdentity_Fails`
  - `NewPayloads_WriteExplicitNulls`
  - `Handshake_OldWorkerWithoutSelection_Refused`
- [ ] Run them and confirm they fail.
- [ ] Implement, then rerun. Commit.

### Task 6: Worker listing and selection

**Files:**
- Create `OpennessWorker/Openness/TiaPortalProcessInventory.cs` and `Openness/PortalDetachGuard.cs`. Link the guard into the tests.
- Modify `Openness/TiaPortalSession.cs`, `Openness/TiaPortalTargetSelector.cs`, `OpennessWorker/Program.cs` and `FakeWorker/Program.cs`.
- Tests: `Worker/TiaPortalTargetSelectorTests`, new `Worker/PortalDetachGuardTests`, new `Worker/PortalSelectionSourceTests`.

**Interfaces (produces):**
- `TiaPortalProcessCandidate` gains `bool HasUserInterface`.
- `TiaPortalTargetSelector`:
  - `SelectExactProcessId(candidates, string projectPath) -> int` requires an exact advertised match and never falls back to the sole Portal. It throws `target_not_found` or `target_ambiguous`.
  - `ToProcessInfos(candidates, int? attachedPid) -> List<TiaPortalProcessInfo>` orders by process ID.
- `PortalDetachGuard.Evaluate(bool hasUserInterface, int otherClientCount, bool? projectIsModified) -> string?` refuses a headless Portal with no other clients unless the project is known to be unmodified.
- `TiaPortalSession` changes:
  - `Disconnect()`: unsubscribe events, clear the handles, release the Portal handle. The generation still advances. `Dispose` calls it.
  - `SelectPortalProject(string projectPath) -> PortalProjectSelectionInfo`. In the same process it re-selects the project. In a different process it runs the guard, then `Disconnect`, attach, a PID check, and select. It never calls open, close or save. It does the lookup and the guard before detaching.
- Worker handlers:
  - `list_tia_portal_processes` bypasses `WithSession`, so it never attaches.
  - `select_portal_project` requires `ProjectPath`.
- `Main` disposes the shared session at stdin end-of-file.
- FakeWorker scenarios, keyed by `projectPath` as today: `portals-single`, `portals-two`, `portals-none`, `portal-switch`, `portal-switch-fails-after-detach`, `portal-switch-headless-refused`, `portal-selection-malformed`.

**Steps:**
- [ ] Write the tests:
  - `SelectExactProcessId_NoSoleFallback`, `SelectExactProcessId_DuplicateAdvertisers_Ambiguous`, `ToProcessInfos_SortedMarksAttached`
  - The guard theory: headless with a sole client and a modified project refuses, an unknown modified state refuses, and UI, other clients or unmodified allow.
  - Source tests: `ListingBypassesWithSession`, `SelectPortalProjectNeverOpensClosesOrSaves`, `DisconnectUnsubscribesBeforeRelease`, `GuardRunsBeforeDisconnect`
- [ ] Run them and confirm they fail.
- [ ] Implement. Run the serial stub build so the worker compiles.
- [ ] Rerun the tests. Commit.

### Task 7: Host binding method

**Files:**
- Create `TiaMcpServer/Worker/ProjectBindingDecision.cs` and `Worker/ProjectBindingPayloadContract.cs`. Link both into the tests.
- Modify `Worker/OpennessWorkerClient.cs`.
- Tests: new `Worker/ProjectBindingDecisionTests` and `Worker/BindOpenProjectIntegrationTests`.

**Interfaces (produces):**
- `ProjectBindingDecision.Decide(ProjectBindingSnapshot current, string? requestedPath, bool forceRebind) -> BindingStep`. `BindingStep` is one of `Reverify`, `ListThenSelect`, `Select(path)` or `Reject(message)`, and covers every row of spec §4.2.
- `ProjectBindingPayloadContract.DecodeProcessList(string)` and `DecodeSelection(string)` follow the `ProjectRebindStatePayloadContract` pattern. A malformed payload gives `protocol_error`.
- `OpennessWorkerClient.BindOpenProjectAsync(string? projectPath, bool forceRebind, CancellationToken ct) -> Task<ProjectBindingOutcome>`.
  - It runs inside one serialized binding section.
  - `ExecuteSerializedBindingOperationAsync` gets an overload that takes a `CancellationToken`.
- `ProjectBindingOutcome(string Transition, ProjectBindingSnapshot Before, ProjectBindingSnapshot After, ProjectStatusInfo? Project, IReadOnlyList<TiaPortalProcessInfo> Portals, IReadOnlyList<string> Warnings, WorkerCallResult? Failure, bool IsRejection)`.
- Fail-closed rule: during a switch, any selection failure other than `target_not_found` or `binding_conflict` invalidates the previous binding. Timeouts and crashes invalidate it too.
- Warnings: a re-attach happened, which may have shown the Openness dialog; or the previous project was opened by the worker and is still modified.
- A worker timeout during selection reports that TIA's Openness access dialog may be waiting for an answer.

**Steps:**
- [ ] Write one `Decide` test per spec §4.2 row.
- [ ] Write the integration tests:
  - `Unbound_SingleProject_Binds`, `Unbound_SeveralPortals_AmbiguousWithPortals`, `Unbound_NoProjects_TargetNotFound`
  - `Configured_PromotesLikeStartup`
  - `Verified_SamePath_UnchangedRevision`
  - `Verified_OtherPathNoForce_RejectedNothingSent`, `Verified_OtherPathForce_Switches`
  - `SwitchFailsAfterDetach_InvalidatesPrevious`, `HeadlessRefusal_KeepsPrevious`
  - `MalformedSelection_ProtocolErrorNoBinding`
  - `WaitsForLeaseAndHonoursCancellation`
  - `ReadWriteNoProject_BindThenNetworkWritePreview_PassesBindingGate`, the reported scenario
  - Existing `Read_NeverBinds` stays green.
- [ ] Run them and confirm they fail.
- [ ] Implement, then rerun. Commit.

### Task 8: `bind_project` tool

**Files:**
- Create `Tools/ProjectBindingTools.cs`, containing the tool and `BindProjectArgumentValidatingTool`.
- Modify `Tools/McpToolRegistration.cs`, `Tools/StandaloneToolResponses.cs` and `Tools/StructuredStandaloneResult.cs`, and update the test csproj links.
- Tests: `AccessModeDiscoveryTests`, `StandaloneToolsProtocolTests`, `McpToolSchemaTests`, `WriteToolMcpAnnotationProtocolTests`, `ReadOnlyModeTests`, `ToolOutputContractConformanceTests` and its probes, and a new `Tools/BindProjectToolProtocolTests`.

**Interfaces (produces):**
- `McpToolRegistration.WithProjectBindingTools(this IMcpServerBuilder)`, called unconditionally.
- `ProjectBindingTools.BindProject(OpennessWorkerClient client, string? projectPath = null, bool forceRebind = false, CancellationToken ct = default) -> Task<CallToolResult>`, with annotations ReadOnly=false, Destructive=false, Idempotent=true, OpenWorld=false.
- Response types:
  - `BindProjectResponse(ContractVersion, Success, Error, Warnings, StandaloneToolOutcome<ProjectBindingResult>? Result)`, with `Tool => "bind_project"`
  - `ProjectBindingResult(Transition, Binding, PreviousBinding, Project, Portals)`
  - `ProjectBindingInfo(State, ProjectPath, PortalProcessId)`
  - `PortalProcessInfo(ProcessId, ProjectPath, HasUserInterface, IsBound)`, where `IsBound` is true for the process of the verified binding after the call
  - Every member is always written.
- Outcome mapping follows spec §4.3. The description follows spec §4.1.

**Steps:**
- [ ] Write the tests:
  - `ToolsList_AdvertisesOnlyTheModeSurface`, updated to 5/15/15
  - `BindProject_RegisteredInEveryMode`, `BindProject_Annotations`
  - `BindProject_RejectsRelativeOrNonAp21Path`, `BindProject_RejectsUnknownArguments`
  - `BindProject_Ambiguous_FailedOutcomeWithPortals_IsErrorFalse`
  - `BindProject_TextEqualsStructuredContent`
  - Conformance probes `bind_project/succeeded`, `bind_project/rejected` and `bind_project/ambiguous`
- [ ] Run them and confirm they fail.
- [ ] Implement, then rerun. Commit.

### Task 9: Project-tree cursors follow the binding

**Files:** Modify `ProjectTree/ProjectTreeCursorCodec.cs` and `ProjectTreeBrowseCoordinator.cs`. Tests go in the project-tree cursor tests.

**Interfaces:** The cursor state carries `BindingId` and `Revision`, both null while unbound, compared as `HardwarePageCursorState.Matches` does. A mismatch gives `cursor_binding_mismatch`. A cursor in the old format decodes as `invalid_cursor`.

**Steps:**
- [ ] Write the tests: `Cursor_AfterBindingChange_BindingMismatch`, `Cursor_SameBinding_Continues`, `Cursor_Unbound_Continues`, `Cursor_LegacyFormat_InvalidCursor`.
- [ ] Run them and confirm they fail.
- [ ] Implement, then rerun. Commit.

### Task 10: Guidance strings and doctor

**Files:**
- Modify the guidance text in `Contracts/ProjectSessionBinding.cs` (`RebindInstruction`), `Contracts/ProjectOpenPolicy.cs`, `Worker/OpennessWorkerClient.cs` (binding-required message), `OpennessWorker/Openness/TiaPortalSession.cs`, `TiaPortalTargetSelector.cs` (ambiguity message) and the worker's read-only messages.
- Modify `Diagnostics/Checks/ProjectBindingCheck.cs` and `TiaPortalProcessCheck.cs`.
- Tests: `WriteBatchToolsBehaviorTests`, `ProjectSessionBindingTests`, `TiaPortalSessionBindingGuidanceSourceTests`, `ProjectBindingCheckTests`, `TiaPortalProcessCheckTests`.

**Steps:**
- [ ] Write the tests:
  - `BindingRequiredMessage_NamesBindProjectFirst`
  - `GuidanceStrings_NameBindProject`
  - `Doctor_WritableWithoutProject_Warning`, a theory over read-write and full
  - `Doctor_MultipleProcessesUnbound_Warning`
- [ ] Run them and confirm they fail.
- [ ] Implement, then rerun. Commit.

### Task 11: Documentation, verification and branch review

**Files:**
- `README.md` (keep absolute links) and `AGENTS.md`.
- `docs/ARCHITECTURE.md`, `docs/guides/installation.md`, `mcp-client-configuration.md`, `troubleshooting.md`.
- `docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md` and `PLC_OPERATIONS_SUMMARY.md`.
- `docs/development/local-mcp-testing.md`, `docs/IMPROVEMENT_LOG.md`, `docs/roadmap/json-contract.md` and `SECURITY.md`.
- The spec status line.

**Steps:**
- [ ] Document:
  - the 5/15/15 tiers and per-mode confirmation;
  - the flag removal with the migration message;
  - `bind_project` usage and the Openness dialog;
  - that worker ownership of a project is not carried across a detach;
  - no implicit opens;
  - the reworded read-only promise ("never opens, creates, saves or closes").
- [ ] Correct the false claims that read-write binds on its own, and fix the stale lines listed in spec §8.
- [ ] Add an IMPROVEMENT_LOG follow-up for the `totally-integrated-claude` plugin's `tia-portal-mcp` skill.
- [ ] Run the serial stub build, `dotnet test TiaMcpServer.Tests` and the CI coverage and documentation checks present in the checkout. Expect zero failures and coverage at the repository threshold.
- [ ] Review the whole diff against Review Focus with independent reviewer subagents. Fix the findings.
- [ ] Record the offline evidence in `IMPROVEMENT_LOG.md`. Mark the spec "implemented; live acceptance pending". Commit.

### Task 12: Live acceptance (maintainer-run)

Needs the maintainer, TIA Portal V21 and two Portal processes with projects A and B. Follow `docs/development/local-mcp-testing.md` for the dev build. Do not start it without explicit authorization.

- [ ] Run `bind_project` in every mode:
  - no path: expect `target_ambiguous` with both Portals listed;
  - bind A, then a write in read-write;
  - bind B without force: expect a rejection;
  - bind B with force: expect a switch, and note whether the Openness dialog appeared.
- [ ] In read-write, run each lifecycle tool in Claude Code. Each one prompts; a decline mutates nothing.
- [ ] In full, run the same calls. None prompts.
- [ ] Run a read with an unopened `projectPath` in read-write. Expect `access_denied`.
- [ ] Confirm that `TiaPortalProcess.ProjectPath` reports the primary project.
- [ ] Write the report under `docs/superpowers/acceptance/reports/`, named `<run date>-lifecycle-tiers-bind-project-live-validation.md`, and list it in both indexes.

## Execution Handoff

Run the tasks serially. Phase A ends after Task 4 with a fully green suite; Phase B builds on it. Tasks share the catalog, `OpennessWorkerClient` and the tool registration, so do not run them in parallel. Use a fresh implementer subagent per task and an independent reviewer subagent after each task. Preserve unrelated work in the tree.
