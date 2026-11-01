# vci_write Native Rejections and Shared Trusted Verification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Put `vci_write` on the shared contract:
- Siemens failures become typed `engineering_rejected`/`worker_operation_failed` items with `engineeringError` and a capped failure list; `portal_lost` wins.
- Dependents run without re-planning.
- Completed items are `trusted`/`superseded`, under write contract `2.0`.
- The Stage 0 rulings apply.

**Architecture:** The pipeline is unchanged.
- Worker: a failure log owned by `VciWriter` records failed leaves and builds item exceptions through `WorkerExecution`, whose Portal re-check learns `VciWriteOutcome`.
- Host: the planner plans only the first item per workspace, and dependents carry no plan or dependency, so `WriteRun` never re-plans them.
- Verification: `VciVerificationPolicy` on `TrustedWriteVerifier<T>` replaces `VciWriteVerifier`.

**Tech Stack:** C#/.NET 10 host, netstandard2.0 contracts, net48 worker, xUnit with same-named Siemens doubles, FakeWorker, PowerShell. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-10-30-vci-write-native-rejections-design.md` (§ numbers). **Stage 0 record:** `docs/superpowers/evidence/2026-10-31-vci-write-stage0.md`; rulings 1–6 supersede the spec where they conflict.

**Baseline:** `main` `f7f468bf` (VCI code identical to `8713a47`); branch HEAD `730f6beb`. Line numbers are navigation aids.

## Global Constraints

- **Unchanged surface:**
  - the 8/19/19 tool surface and `vci_write(operations, dryRun=false)`;
  - 1–50 unique-ID items, in caller order;
  - stop after the first failed item, with no rollback across items;
  - no server elicitation, standalone-only binding, the binding lease, access-mode gates and audit v2;
  - one `CanonicalJson.Serialize` for text and `structuredContent`.
- **Unchanged internals:** worker method names, `VciMethods`, `OperationPolicyCatalog` and `ProjectCapabilityCatalog`. No new worker methods or public operations.
- **Unchanged reads:** `vci_read` stays `1.0`, byte-for-byte.
- **Contract hygiene:**
  - New outcome members are required.
  - There are no new conditional members, and `WorkerPayloadNullPolicyRegisterTests` is unchanged.
  - `output-schemas.json` changes only in its `vci_write` key.
- **Project files:** the test project globs host and worker `Vci/*.cs` (`TiaMcpServer.Tests.csproj:66,387`). No csproj edits, so packaging tests do not run.
- **Build and test:**
  - Build with `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true` from PowerShell.
  - End every code task with `dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"` (serial, about 9 min).
  - Offline tests run on net10. Worker code must not depend on net10-only path behavior: net48 `Path.Combine`/`GetFullPath` throw on `"<>|` and control characters.
- **Commits:** one per task, short one-line conventional message, no body or trailers. No push.
- **No project identity:** no TIA project names or paths in tracked files. Before each docs commit, run `git grep -n -i -E "DisposableProjects|_Copy|\.ap21|\.als21"` on the branch diff. Never dispose `TiaPortalProcess`.
- **Live safety:** never a device or library container, never `SimaticSD` on a PLC leaf, no crash or Portal-loss probes.
- **Planning failures:** a Siemens exception in planning (`vci_plan`, `Observe`) or in a read-only recheck never becomes `engineering_rejected`.
- **Kept protections:**
  - container exclusion, the `SimaticSD` PLC rule, the status prefilter and direction rules;
  - `FindMapping` ownership, the import transaction, breadcrumbs and the `alreadyMapped` skip;
  - the project-folder rule, root safety, the exact-root workspace check;
  - containment, directory-segment rules and the length diagnostic.
- **Docs lifecycle:**
  - `docs/superpowers/{plans,specs,evidence}` are transient. Never link them from outside `docs/superpowers/`; cite them as "PR #N, commit `<sha>`".
  - Run `python -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30` before each docs commit.

## Review Focus

1. A VCI `portal_lost` wrap after a disposal bump is rethrown unchanged: same message and `LastAttempted`, while HMI behavior is unchanged. Owner: Task 1 `VciPortalLostWrap_AfterDisposal_IsRethrownUnchanged`.
2. A server-raised first failure, then a native one: the item takes `worker_operation_failed` with no `engineeringError`, and both are listed in order. Owner: Task 1 `ServerRaisedFirst_DecidesCategoryAndBothListed`.
3. 25 failures and 5 successes: 20 entries, `failuresOmitted:5`, exact counts, a 500-character entry message and a whole item message. This holds both in the worker and through the host document. Owners: Task 1 `TwentyFiveFailures_CapsAtTwentyAndCountsOmitted`; Task 2 `ManyRejected_DeliversCappedFailures`.
4. `create_workspace w`, then `export` into `W`: one `vci_plan` per call, the export unplanned, the create `superseded` and the export `trusted`. Owner: Task 6 `CaseVariantDependent_NoPlanInPreviewOrApply`.
5. A leaf name containing `"`, `<`, `>` or `|` on net48: the leaf reaches Portal, and the plan never throws `ArgumentException`. Owner: Task 4 `LengthCheck_NeverUsesPathCombine`.

## Decisions taken in this plan

- **D1** (R1) Roots-only conflict. A duplicate name reaches `Workspaces.Create`, which throws ETI ("The workspace name must be unique"). The root created first stays.
- **D2** (R2) Exact root, case-insensitive, trailing separator ignored. Nested roots are allowed.
- **D3** (R5) All collision prevention goes: plan `KeyCollisions`, `vci_file_key_collision`, writer mapping/file checks. Native overwrite is the contract, so spec §7.3 "keep" and §9.4 L2/L5 are superseded.
- **D4** (R5) Directory segments (`relativeDirectory` and engineering names) keep every segment rule, because the writer creates them. The leaf keeps only `\`, `/` (directory boundary) and `:` (an alternate data stream is a different object, so this is containment). Its other rules go to Portal.
- **D5** (R5) Length is the complete path, built by string concatenation: root, directory, file name and extension. SimaticML takes `.xml` (4); other formats keep the 8-character reserve. It fails above 259.
- **D6** A create-recheck refusal carries no outcome. It is a precondition refusal and must not supersede (§7.1, §7.5).
- **D7** `lastAttempted` stays set only on VCI's own `NonRecoverableException` path. A wrapped failure that `Execute` re-maps to `portal_lost` keeps `failures`, but its `lastAttempted` is null. This is a design call: the spec did not foresee `Execute`'s re-map.
- **D8** Leaf categories are `engineering_rejected` or `worker_operation_failed` only.
- **D9** Candidate timing replays the Stage 0 macros with Stage 0's validators. `scripts/timing/measure_write_timing.py` cannot restore VCI workspaces or accept failed leaves.
- **D10** New ADR 0014. ADR-0010 rejected weakening export collision checks, and ADR-0012 excludes VCI from the shared verifier; ADRs are immutable.

## After the tasks (orchestrator and maintainer gates — not part of any task brief)

- Tasks 1–7 run offline in order, each ending with the serial full suite.
- Stop after Task 7. Task 8 needs the maintainer's machine, TIA Portal V21, explicit live authorization and the saved Stage 0 fixture.
- An O3 finding stops Task 8. The maintainer rules, and one focused task is added before resuming.
- Task 10 runs only when the maintainer says the PR is ready to merge.

---

### Task 1: Worker failure list, classification and `portal_lost` precedence (§7.1, §7.2)

**Files:**
- Modify: `TiaMcpServer.Contracts/Vci/VciResultInfos.cs:57-68`
- Create: `TiaMcpServer.OpennessWorker/Openness/Vci/VciFailureLog.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/Vci/VciWriter.cs:14-206`
- Modify: `TiaMcpServer.OpennessWorker/Worker/WorkerExecution.cs:29-41`
- Modify: `TiaMcpServer.Tests/Json/ConditionalMemberRegisterTests.cs:43` (comment only)
- Test: `TiaMcpServer.Tests/Vci/{VciWriterTests,VciContractDecodeTests}.cs`, `TiaMcpServer.Tests/Worker/WorkerExecutionTests.cs`

**Interfaces:**
- Consumes: `WorkerExecution.Classify(EngineeringException, bool mutates)`, `FailureMessage(EngineeringException, string)`, `Describe(Exception)`; `WorkerOperationException(…, vciWriteOutcome:, engineeringError:)`.
- Produces:
  - `public sealed class VciLeafFailureInfo { List<ProjectTreeSelectorSegment> Path; [JsonIgnore(Condition = Never)] string? Format; string Category; [JsonIgnore(Condition = Never)] string? EngineeringErrorType; string Message; public const int MaxMessageChars = 500; }`
  - `VciWriteOutcomeInfo` appends `List<VciLeafFailureInfo> Failures = new()` and `int FailuresOmitted`, plus `public const int MaxFailures = 20;`.
  - `internal sealed class VciFailureLog(VciWriteOutcomeInfo outcome)`:
    - `void Record(VciLastAttemptedInfo attempt, Exception ex)`
    - `void Record(VciLastAttemptedInfo attempt, string message)`
    - `bool HasFailure`
    - `WorkerOperationException ItemFailure()`
    - `static WorkerOperationException Stopped(Exception ex, VciWriteOutcomeInfo outcome)`
  - Messages: FailMissing `"The workspace file for this mapping is missing."`; uncommittable import without a throw `"The VCI import transaction could not commit; no candidate was imported."`.

- [ ] **Step 1: Write the failing tests** (`VciWriterTests` unless noted)
  - `ExportLeafTargetInvocation_IsEngineeringRejectedWithEvidenceAndContinues`: three leaves, the middle one throws `EngineeringTargetInvocationException("m")`.
    - Item: `engineering_rejected`, message `"TIA Portal rejected the operation: m"`, `EngineeringError.Type "EngineeringTargetInvocationException"`.
    - Counts: `Succeeded 2`, `Failed 1`.
    - One entry: the leaf's path, `Format "SimaticML"`, the same category and type, message `"m"`.
  - Reshape `RefusedExportLeaf_CountsFailedAndContinues`: a plain `EngineeringException` gives `worker_operation_failed`, a "TIA Portal operation failed: " prefix and an `EngineeringError`. `LastAttempted` stays null.
  - `ServerRaisedFirst_DecidesCategoryAndBothListed`: leaf 1 has no safe format and leaf 2 throws ETI.
    - Item: `worker_operation_failed`, "No safe supported export format.", no `EngineeringError`.
    - Entries in order. Entry 1 has a null type and the attempted format (`"SimaticML"` when none was requested).
  - `TwentyFiveFailures_CapsAtTwentyAndCountsOmitted`: 30 leaves; 25 throw ETI, and the first message is 600 characters.
    - Assert 20 entries in scope order, `FailuresOmitted 5`, `Failed 25`, `Succeeded 5`.
    - Entry 1 message length 500; item `EngineeringError.Message` length 600.
  - `SyncToWorkspace_ReadOnlyTargetInvocation_IsListedAndContinues`.
  - Reshape `SyncToProject_LeafThrows_RollsBackAndCountsAllFailed` into `SyncToProject_ThrowingCandidate_RollsBackAndListsOnlyThrower`: `Failed 2`, `Succeeded 0`, `ProjectModified false`, one entry. Add `SyncToProject_SeveralThrowingCandidates_AllListed`.
  - Reshape `SyncToProject_CanCommitFalse_DoesNotCommit`: the fixed message, `worker_operation_failed`, no `EngineeringError`, empty `Failures`.
  - `MissingImportFile_IsListedAndOthersCommit`, also reshaping `MissingImportFile_FailsWithoutTransaction`: the FailMissing entry has a null type; with a valid second candidate, `Succeeded 1` and `ProjectModified true`.
  - Top-level throws, each giving its classified category, its outcome and an empty `Failures`:
    - `CreateWorkspace_SiemensRejection_IsClassifiedWithOutcome` (`CreateFailure` ETI gives `engineering_rejected` with `Leaves 1`, `Failed 1`);
    - `DeleteWorkspace_SiemensFailure_IsClassifiedWithOutcome` (existing `Workspace.DeleteFailure`);
    - `WriterExpandThrow_IsClassifiedWithoutEntry` (the scope-failure hook used by `VciPlannerTests.OrdinaryReadFailure_IsUnverifiable`).
  - `TopLevelThrowAfterLeafFailures_DecidesCategoryAndKeepsList`: `sync_to_project` with one missing leaf and a valid candidate, then a `VciExclusiveAccessDisposeFailure` `EngineeringException`. The item takes `worker_operation_failed` with an `EngineeringError`, and the missing leaf stays listed.
  - `NonRecoverable_ThrowsPortalLostWithCountsAndLastAttempted`: an earlier recorded failure survives.
  - `WorkerExecutionTests`:
    - `WrappedVciFailure_AfterPortalDisposal_MapsToPortalLostAndKeepsOutcome` (no `EngineeringError`; `Assert.Same` on the outcome).
    - `WrappedVciFailure_WithoutDisposal_IsRethrown`.
    - `VciPortalLostWrap_AfterDisposal_IsRethrownUnchanged`.
    - The existing HMI tests stay green.
  - `VciContractDecodeTests` (`:23`): the outcome fixture gains `failures` (one entry with every member) and `failuresOmitted`.
- [ ] **Step 2:** Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~VciWriterTests|FullyQualifiedName~WorkerExecutionTests|FullyQualifiedName~VciContractDecodeTests"`. Expected: compile errors, then failures.
- [ ] **Step 3: Implement.**
  - Leaf catches become `catch (NonRecoverableException) { throw; } catch (Exception ex) { outcome.Failed++; log.Record(lastAttempted!, ex); }`. The import catch only records, keeping the rollback accounting.
  - Entries:
    - Siemens: `Classify(e, true)` and `GetType().Name`.
    - Other exceptions: `worker_operation_failed` with a null type.
    - The message is verbatim, cut to 500. Past 20, increment `FailuresOmitted`.
    - `Format` is the attempt's format.
  - `ItemFailure()` uses the first record.
  - The final throw is `log.HasFailure ? log.ItemFailure() :` the fixed uncommittable failure.
  - The top-level `catch (Exception)` uses `Stopped`; `Failed` accounting is unchanged.
  - `Execute`'s filter becomes `EngineeringError != null || HmiWriteOutcome != null || (VciWriteOutcome != null && FailureCategory != portal_lost)`, and the re-map copies `VciWriteOutcome`.
  - Register comment: present after a VCI mutation attempt, including `portal_lost`.
- [ ] **Step 4:** Run the Step 2 filter plus `ConditionalMemberRegisterTests`. Expected: pass.
- [ ] **Step 5:** Build with stubs and run the full default suite. Expected: 0 errors; all pass.
- [ ] **Step 6:** Commit: `refactor: vci write task 1 worker failure list`.

**Task test command:** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Vci|FullyQualifiedName~WorkerExecutionTests"`

---

### Task 2: Host projection of the failure list and FakeWorker fixtures (§7.2, §9.1)

**Files:**
- Modify: `TiaMcpServer/Vci/VciWritePayloadContract.cs:45-54`
- Modify: `TiaMcpServer.FakeWorker/Vci/VciWriteRoundtripScenario.cs`
- Test: `TiaMcpServer.Tests/Vci/{VciGuardedWriteDomainTests,VciGuardedWriteMcpTests}.cs`

**Interfaces:**
- Consumes: Task 1 types and constants (20, 500).
- Produces:
  - `Validate` additionally requires:
    - `Failures` non-null with no null entries, `Count <= 20`;
    - `FailuresOmitted >= 0` and `Failures.Count + FailuresOmitted <= Failed`;
    - per entry: non-empty `Path` with non-blank `NodeType`/`Name`; category in {`engineering_rejected`, `worker_operation_failed`}; type null or non-blank, and required for `engineering_rejected`; `Format` null or non-blank; `Message` non-null and at most 500 characters.
  - A mismatch keeps today's `protocol_error` message and never echoes the entry.
  - FakeWorker:
    - `Fail(category, outcome, engineeringError = null)`.
    - `Rejected` (export): `Leaves 3`, `Succeeded 2`, `Failed 1`, one ETI entry, item `engineering_rejected` with `engineeringError`.
    - `ImportRejected` (`sync_to_project`): `Failed 2`, `Succeeded 0`, `ProjectModified false`, one entry.
    - `ManyRejected` (export): `Leaves 30`, `Succeeded 5`, `Failed 25`, 20 entries (the first message 500 characters) and `FailuresOmitted 5`.
    - `vci-portal-lost`: `Failed 1` and one entry.

- [ ] **Step 1: Write the failing tests**
  - `VciGuardedWriteDomainTests.Failures_OverCapOrCountMismatch_IsProtocolError`, a theory over:
    - 21 entries;
    - count plus omitted greater than `failed`;
    - an empty path;
    - category `portal_lost`;
    - `engineering_rejected` without a type;
    - a 501-character message;
    - a null entry.

    Each case is `protocol_error` and the response lacks the case's marker. Add the over-cap case to `ActualFailureWireRejectsMalformedOutcomeWithoutDefaultsOrLeak`.
  - `VciGuardedWriteMcpTests`:
    - `RejectedExportLeaf_TypedWithFailuresAndStopsLaterItems`: export `Healthy`, `Rejected`, `Healthy`. Item 2 has `failures` and `engineeringError`; item 3 is skipped; root `success:false`, `error:null`, `isError:false`.
    - `RejectedImport_ListsThrowerWithRollbackCounts`.
    - `ManyRejected_DeliversCappedFailures`: the delivered document has 20 entries, `failuresOmitted:5` and the exact counts.
    - `PortalLostWithFailures_KeepsOutcomeAndInvalidatesBinding`: the next `get_project_status` shows the binding invalidated.
- [ ] **Step 2:** Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~VciGuardedWrite"`. Expected: the new cases fail.
- [ ] **Step 3:** Implement per Interfaces.
- [ ] **Step 4:** Run the Step 2 filter. Expected: pass.
- [ ] **Step 5:** Build with stubs and run the full default suite. Expected: all pass.
- [ ] **Step 6:** Commit: `refactor: vci write task 2 host failure projection`.

**Task test command:** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Vci"`

---

### Task 3: Workspace checks and planning narrowing (§7.3 rows 1–3; rulings 1–2)

**Files:**
- Modify: `TiaMcpServer.OpennessWorker/Openness/Vci/VciPlanner.cs:24-41,66-78,102-109`
- Modify: `TiaMcpServer.OpennessWorker/Openness/Vci/VciWriter.cs:28-43`
- Modify: `TiaMcpServer/Vci/VciGuardDefinitions.cs`
- Modify: `TiaMcpServer.FakeWorker/Vci/VciWriteRoundtripScenario.cs:30-38,56`
- Test: `TiaMcpServer.Tests/Vci/{VciPlannerTests,VciWriterTests,VciWritePlannerTests,VciGuardedWriteMcpTests}.cs`

**Interfaces:**
- Consumes: `VciFailureLog.Stopped` (Task 1).
- Produces:
  - `WorkspaceConflict = "An existing workspace uses this root: " + name`. Roots are compared after `GetFullPath` and trailing-separator trim, `OrdinalIgnoreCase`; names are never compared.
  - Guard descriptions: `vci_workspace_exists` `"An existing workspace uses the requested root."`; `vci_state_unverifiable` `"A safe export format or export directory could not be established."`.
  - `Unverifiable` is set only for:
    - `"No safe supported export format for leaf: {name}"`;
    - `"{name}: {reason}"` for an invalid resolved directory;
    - `"Supported formats could not be read for leaf {name}: {message}"` for a recoverable `GetSupportedFileFormats` exception.
  - All other exceptions leave `Plan`. Root syntax was already rejected by host `ValidateRoot`.
  - Writer create recheck:
    - A root conflict or invalid root throws `state_changed` with **no outcome**.
    - A Siemens exception from the recheck's `Plan` throws `worker_operation_failed` (`FailureMessage`, `Describe`, `Classify(ex, false)`) with no outcome.
  - FakeWorker:
    - `vci_plan` reports a conflict on a root match only.
    - Creating an existing name fails `engineering_rejected` with outcome `Leaves 1`, `Failed 1` and `engineeringError` `{type:"EngineeringTargetInvocationException", message:"Error when calling method 'Create' of type 'Siemens.Engineering.VersionControl.WorkspaceComposition'.\r\n\r\nThe workspace name must be unique"}` (P1 verbatim).

- [ ] **Step 1: Write the failing tests**
  - `VciPlannerTests`:
    - Reshape `Create_DuplicateNameIsConflictRegardlessOfRoot` into `Create_DuplicateNameWithDifferentRoot_HasNoConflict`.
    - `RootComparison_IgnoresCaseAndTrailingSeparator` asserts the root wording.
    - Add `Create_NestedRoot_HasNoConflict`.
    - Split `OrdinaryReadFailure_IsUnverifiable`: a format read gives the new text; mappings, service, scope and workspace-lookup failures escape as the original exception.
    - Add `PlanSiemensFailure_ThroughExecute_IsWorkerOperationFailedNeverRejected`: the body calls `VciDispatch.Plan` and returns a success `WorkerResponse`, with `mutates:false`. An ETI from expansion gives `worker_operation_failed` with an `EngineeringError`.
  - `VciWriterTests`:
    - `Create_DuplicateName_ReachesPortal`: an existing name with a different root calls `Workspaces.Create`, and a `CreateFailure` ETI gives `engineering_rejected`.
    - Reshape `Create_RechecksRootConflictBeforeDirectoryOrApiMutation`: `state_changed`, root wording, `VciWriteOutcome` null.
    - `Create_RecheckReadFailure_IsWorkerOperationFailedNotRejected`.
  - `VciWritePlannerTests.PlanMembersMapToBoundedGuards`: the root wording.
  - `VciGuardedWriteMcpTests`:
    - `CreateDuplicateName_IsEngineeringRejectedFromPortal`: no `vci_workspace_exists`; the message contains "The workspace name must be unique".
    - `CreateSharedRoot_IsBlockedByWorkspaceExists`.
- [ ] **Step 2:** Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~VciPlannerTests|FullyQualifiedName~VciWriterTests|FullyQualifiedName~VciWritePlannerTests|FullyQualifiedName~VciGuardedWriteMcpTests"`. Expected: the new cases fail.
- [ ] **Step 3:** Implement per Interfaces. Split the `||` at `VciPlanner.cs:30-40`. Wrap only the format read, and delete the catch-all.
- [ ] **Step 4:** Run the Step 2 filter. Expected: pass.
- [ ] **Step 5:** Build with stubs and run the full default suite. Expected: all pass.
- [ ] **Step 6:** Commit: `refactor: vci write task 3 workspace checks`.

**Task test command:** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Vci"`

---

### Task 4: Export collision removal, segment rules and corrected length (ruling 5)

**Files:**
- Modify: `TiaMcpServer.Contracts/Vci/VciMirroredPath.cs:38,69-82`, `TiaMcpServer.Contracts/Vci/VciResultInfos.cs:45-55`
- Modify: `TiaMcpServer.OpennessWorker/Openness/Vci/VciPlanner.cs:80-101`, `VciWriter.cs:74-84,208-219`
- Modify: `TiaMcpServer/Vci/VciGuardDefinitions.cs`, `TiaMcpServer/Vci/VciWritePlanner.cs:38-64,74-85`
- Modify: `TiaMcpServer.FakeWorker/Vci/VciWriteRoundtripScenario.cs`
- Test: `TiaMcpServer.Tests/Vci/{VciMirroredPathTests,VciPlannerTests,VciWriterTests,VciWritePlannerTests,VciGuardDefinitionsTests,VciContractDecodeTests,VciGuardedWriteMcpTests,VciWriteVerifierTests}.cs`

**Interfaces:**
- Produces:
  - `public static string? FirstProblem(string rootPath, VciFileKey key, string format)`; the two-argument overload is removed. The only production callers are `VciPlanner.cs:82` and `VciWriter.cs:75`, where `format.Format` is in scope.
  - Segment rules:
    - Directory segments (all but the last) keep every current rule, with message `"Name is not representable: {segment}"`.
    - The leaf fails only on `\`, `/` or `:`.
  - Length:
    - `string.Join("\\", nonEmpty(root.TrimEnd('\\','/'), dir.Trim('\\'), fileName)).Length + ExtensionReserve(format) > 259` gives `"Mirrored path exceeds 259 characters: {fileName}"`.
    - The length is built by concatenation, never `Path.Combine`.
    - `internal static int ExtensionReserve(string format)` returns 4 for `"SimaticML"` (`OrdinalIgnoreCase`) and `MaxExtensionLength` (8) otherwise.
  - `VciPlanInfo` drops `KeyCollisions`. There are five guards, without `vci_file_key_collision`.
  - `ExportDirectory(Workspace, VciFileKey)` checks containment only. It is called before and after `CreateDirectory`, with no mapping enumeration and no `EnumerateFiles`.
  - FakeWorker: `Blocked` reports `Unrepresentable`; `PartialCollision` is removed.

- [ ] **Step 1: Write the failing tests**
  - `VciMirroredPathTests`:
    - `LeafReservedOrTrailingDotName_IsLeftToPortal` (`CON`, `CON.part`, `Trailing.`, `Trailing `, `a"b`, `a|b`).
    - `DirectorySegmentRules_StillApply` (the same names as a group or `relativeDirectory`).
    - `LeafBoundaryCharacters_AreNotRepresentable` (`a\b`, `a/b`, `name:stream`).
    - `SimaticMlCompletePathOf259_IsAccepted`, `SimaticMlCompletePathOf260_IsReported`, `OtherFormat_KeepsEightCharacterReserve`.
    - `LengthCheck_NeverUsesPathCombine` (a leaf with `"<>|`).
    - Move the existing calls (`:61,98,102,111,120`) to the three-argument form. Reshape `LongPath_Over259_IsReported` (`:126-128`) and `ReservedAndTrailingDotNames_AreNotRepresentable` for directory segments only.
  - `VciPlannerTests`:
    - Delete `LeftoverFileOnDisk_IsCollision`, `DifferentStemOnDisk_IsNotCollision`, `ExistingMappingKey_IsCollision`, `ExportPlan_MapsEachExistingKeyOnce`, `DuplicateKeyWithinItem_IsCollision` and `RerunExport_AlreadyMappedLeavesAreNotCollisions`.
    - Reshape `Lists_AreCappedAtTen` (`:305`) without `KeyCollisions`, and `UnrepresentableLeaf_ReportedByPlan`: `CON` and `Trailing.` are not reported, separators are.
    - Add `Plan_HasNoCollisionEvidenceAndNeverEnumeratesFiles`: a stray file and an existing key give no evidence; `EnumerationStarts == 0`.
  - `VciWriterTests`:
    - Replace `LateFileOrMappingCollision_RefusesLeafWithoutOverwrite` and `Export_CollisionAfterDirectoryCreation_IsRefused` with `Export_ExistingFileOrMappingKey_ReachesPortal(bool mapping)`. This covers files left by `delete_mapping` (§7.6): `ExportObject` is called and `Succeeded 1`.
    - Replace `Export_MappingWorkGrowsWithExistingMappings` with `Export_DoesNotScanExistingMappings` (0 and 20 mappings, `EnumerationStarts 0`).
    - Add `Export_DirectorySegmentProblem_IsListedLeafFailure`.
  - `VciWritePlannerTests`: drop the `"null-collisions"` case of `InconsistentPlanEvidenceFailsClosed` (`:154`) and the collision member from `InvalidTypedPlanFailsClosed`/`PlanMembersMapToBoundedGuards`.
  - `VciGuardDefinitionsTests`: five guards.
  - `VciContractDecodeTests`: the `VciPlanInfo` fixture has no `keyCollisions`, and a payload with it is rejected as unmapped.
  - `VciGuardedWriteMcpTests.ExportOntoExistingKey_IsNotBlocked`: a `dryRun` preview with no guards.
  - Delete `VciWriteVerifierTests.PartialExportWithKnownFailedLeafCollisionStillVerifiesMappedCount`.
- [ ] **Step 2:** Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~VciMirroredPathTests|FullyQualifiedName~VciPlannerTests|FullyQualifiedName~VciWriterTests|FullyQualifiedName~VciWritePlannerTests|FullyQualifiedName~VciGuardDefinitionsTests|FullyQualifiedName~VciContractDecodeTests"`. Expected: build errors, then failures.
- [ ] **Step 3:** Implement per Interfaces.
- [ ] **Step 4:** Run the Step 2 filter plus `FullyQualifiedName~VciGuardedWrite|FullyQualifiedName~VciFormatsTests`. Expected: pass. These pins must stay green unchanged:
  - `DefaultPlcSdOnly_IsUnverifiableInsteadOfUnsafeFallback`, `Skips_AreCounted`, `StatusFiltering_WritesBreadcrumbBeforeEveryReadOutsideTransaction`;
  - `Synchronize_RemappedKeyOutsideSelectedScope_RefusesReplacement`, `LateMirroredDirectoryFile_RefusesBeforeExport`, `AlreadyMapped_SkipsOwnFilesAndFormatLookup`.
- [ ] **Step 5:** Build with stubs and run the full default suite. Expected: all pass.
- [ ] **Step 6:** Commit: `refactor: vci write task 4 native export overwrite and path rules`.

**Task test command:** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Vci"`

---

### Task 5: Shared trusted verifier and write contract 2.0 (§7.5, §7.7)

**Files:**
- Create: `TiaMcpServer/Vci/VciVerificationPolicy.cs`, `TiaMcpServer.Tests/Vci/VciVerificationPolicyTests.cs`
- Delete: `TiaMcpServer/Vci/VciWriteVerifier.cs`, `TiaMcpServer.Tests/Vci/VciWriteVerifierTests.cs`
- Modify: `TiaMcpServer/Vci/VciContractVersion.cs`, `VciWriteDomain.cs:19,51-70`, `VciWriteModels.cs:10-15`, `VciWritePayloadBudget.cs:26-72`
- Modify: `TiaMcpServer.Tests/Tools/Snapshots/output-schemas.json` (`vci_write` key only)
- Test: `TiaMcpServer.Tests/Vci/{VciWriteBudgetTests,VciGuardedWriteMcpTests}.cs`, `TiaMcpServer.Tests/Tools/ToolOutputContractConformanceTests.cs:207-211`

**Interfaces:**
- Consumes: `IWriteVerificationPolicy<TItem>`, `TrustedWriteVerifier<TItem>(items, policy)`, `WriteVerification`/`WriteOperationVerification`, `StructuredOperationBatch.FromItems`. The pattern is `HmiWriteDomain.cs:57,64-66` and `HmiVerificationPolicy`.
- Produces:
  - `public static class VciWriteContractVersion { public const string Current = "2.0"; }`, used by `VciWriteDomain.ContractVersion` and the budget-failure root (`VciWritePayloadBudget.cs:52`). Reads keep `VciContractVersion` `1.0`.
  - `internal sealed class VciVerificationPolicy : IWriteVerificationPolicy<VciOperationRequest>`:
    - `ReadTool "vci_read"`
    - keys `["vci\nworkspace\n" + item.WorkspaceName.ToUpperInvariant()]`
    - `HasReadBack` false
    - `ReadBackAsync` throws `NotSupportedException`
  - `VciWriteDomain`: `_attempted` is filled in `MutateAsync`, and `VerifyAsync` verifies attempted items only.
  - `VciGuardedWriteResponse.Verification` is `WriteVerification`. `VciOperationVerification`, `VciWriteVerification` and the `exists`/`absent`/`mappedLeaves`/`notStatusVerified`/`notVerified` checks are removed.
  - Behavior change (§7.5): after a fatal failure, earlier completed items are `trusted` and the failed item has no entry.

- [ ] **Step 1: Write the failing tests**
  - `VciVerificationPolicyTests`:
    - `DistinctWorkspaces_AreTrusted`, `LaterItemSameWorkspace_Supersedes`, `CaseVariantWorkspace_KeysMatch`, `FailedItemWithOutcome_SupersedesEarlier`, `FailedItems_HaveNoEntry`.
    - `PreconditionRefusal_DoesNotSupersede`: the Task 3 create-recheck shape, `state_changed` with a null result.
  - `VciGuardedWriteMcpTests`:
    - Reshape `PreviewApplySyncAndAudit`: no `list_workspaces`/`vci_plan` after mutation; only `trusted`/`superseded`.
    - Reshape `FatalFailureInvalidatesBindingAndNeverVerifies`: the earlier item is `trusted` and the failed one has no entry.
    - Reshape `PortalLostBeforeExpansionPreservesFailureAndSuppressesAllReads`: assert an empty `operations`.
    - Add `SixOperationsOneWorkspace_LastTrustedEarlierSuperseded`.
    - Move in from the deleted file: `TimeoutForeignBreadcrumbIgnoredAndRemoved` and `IndependentCreatesSharingRootSecondLateCheckRefusesSafely` (the second create is `state_changed` and has no entry; the first is `trusted`).
  - `VciWriteBudgetTests`: replace the VCI verification records (`:22,54,69`) with `WriteVerification`/`WriteOperationVerification` using `trusted`, and expect `"2.0"` (`:19,52,66,85`).
  - Conformance: `contractVersion` is `"2.0"` on the `vci_write` probes and `"1.0"` on `vci_read`.
- [ ] **Step 2:** Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Vci|FullyQualifiedName~ToolOutputContractConformanceTests"`. Expected: failures.
- [ ] **Step 3:** Implement per Interfaces, and delete the two verifier files.
- [ ] **Step 4:** Regenerate the snapshot.
  - Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~McpToolSchemaTests.OutputSchemasMatchMain"`. Expected: a `vci_write` diff only.
  - Replace that key from `ProtocolTool.OutputSchema`, dumped through `McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full)` in an uncommitted scratch test.
  - Re-run. Expected: pass; `git diff` touches only that key.
- [ ] **Step 5:** Build with stubs and run the full default suite. Expected: all pass.
- [ ] **Step 6:** Commit: `refactor: vci write task 5 shared verifier and contract 2.0`.

**Task test command:** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Vci|FullyQualifiedName~ToolOutputContractConformanceTests|FullyQualifiedName~McpToolSchemaTests"`

---

### Task 6: Dependent items without plan or replan, and the tool description (§7.4, §7.7)

**Files:**
- Modify: `TiaMcpServer/Vci/VciWritePlanner.cs:13-64`, `VciWriteDomain.cs:14-49,73-81`, `VciWriteTools.cs:13`
- Test: `TiaMcpServer.Tests/Vci/{VciWritePlannerTests,VciGuardedWriteDomainTests,VciGuardedWriteMcpTests}.cs`

**Interfaces:**
- Consumes: `ItemPlan<TEffect>(TEffect? Effect, string? DependsOn, IReadOnlyList<CheckedPrecondition> Preconditions)`. `WriteRun` re-plans only when `DependsOn` is set (`WriteRun.cs:391`) and drops null effects from the report and the audit (`:586`).
- Produces:
  - `PlanAsync`, first item per workspace (`OrdinalIgnoreCase`): one `vci_plan`, `ItemPlan.Resolved(effect)`, evidence guards and operation guards.
  - `PlanAsync`, later items: `new ItemPlan<VciWriteEffect>(null, null, Array.Empty<CheckedPrecondition>())` with operation guards only: `vci_files_remain` for `delete_workspace`/`delete_mapping`, `vci_import_unvalidated` for `sync_to_project`. The planner reports each dependent with its first item's ID.
  - `ReplanAsync` becomes `private PlanItemAsync`; `VciWriteVerifier`, its last outside caller, is gone. `ProjectFolderError` still applies to every item.
  - `VciWriteDomain.ReplanAsync` throws `InvalidOperationException("vci_write plans declare no dependencies; ReplanAsync must never be called.")`.
  - `_deferred` becomes `_dependents` (ID to first ID), filled from the planner's dependent report. The warning: `"Dependent operations [{id} after {firstId}, …]: they rely on the earlier same-workspace item's plan; no inventory, counts or evidence guards are computed for them."`.
  - Tool description:
    - classification;
    - `failures` (up to 20, scope order, `category`, `engineeringErrorType`, message trimmed to 500) and `failuresOmitted`, with exact counts;
    - re-run recovery (already-mapped leaves are skipped);
    - native overwrite (no existing-file or mapping-key check; Portal may silently overwrite, delete companion files or disturb another mapping);
    - dependent items;
    - `trusted`/`superseded`;
    - `vci_read list_mappings` with status after writes, and `compile_check` after `sync_to_project`.

    Keep the standalone, crash and timeout text.

- [ ] **Step 1: Write the failing tests**
  - `VciWritePlannerTests`:
    - Reshape `ExportAfterCreateInSameCall_DependsOnCreate` into `ExportAfterCreateInSameCall_HasNoPlanAndOneWorkerPlan`.
    - Add `DependentItems_KeepOperationInfoGuards` (theory).
    - Keep `RootInsideProjectFolder_FailsPlan(bool dependent)`.
    - Delete `ReplanFiresLateBlock`; rename `WorkerFailurePassesThroughPlanAndReplan` to `WorkerFailurePassesThroughPlan`.
  - `VciGuardedWriteDomainTests.ReplanAsync_Throws`: the exact message.
  - `VciGuardedWriteMcpTests`:
    - `CaseVariantDependent_NoPlanInPreviewOrApply`: `create_workspace w`, then `export` into `W`.
      - One `vci_plan` per call in `dryRun` and in apply.
      - The preview warning names `after <create id>`.
      - Apply mutates both; the create is `superseded` and the export `trusted`.
    - `Description_StatesContractEssentials`: contains `failuresOmitted`, `overwrite`, `superseded`, `compile_check`.
- [ ] **Step 2:** Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~VciWritePlannerTests|FullyQualifiedName~VciGuardedWrite"`. Expected: the new cases fail.
- [ ] **Step 3:** Implement per Interfaces.
- [ ] **Step 4:** Run the Step 2 filter. Expected: pass.
- [ ] **Step 5:** Build with stubs and run the full default suite. Expected: all pass.
- [ ] **Step 6:** Commit: `refactor: vci write task 6 dependent items without replan`.

**Task test command:** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Vci"`

---

### Task 7: Offline qualification

**Files:** this plan's execution record.

- [ ] **Step 1:** Run the stub build, then `dotnet build TiaMcpServer.slnx -m:1 /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"` when installed. Expected: 0 errors on both.
- [ ] **Step 2:** Run: `dotnet test TiaMcpServer.Tests --filter "Category!=Packaging" --collect "XPlat Code Coverage"`. Expected: all pass. Record the count and the changed VCI files' line coverage; at least 80%.
- [ ] **Step 3:** Run: `git grep -n -E "vci_file_key_collision|KeyCollisions|export file key is now occupied|notStatusVerified|VciWriteVerifier|uses this name or root" -- "*.cs" "*.json"`. Expected: no match.
- [ ] **Step 4:** Whole-branch review (superpowers:requesting-code-review) against the spec, the rulings and this plan. Fix CRITICAL/HIGH findings with a test each, then re-run Step 2.
- [ ] **Step 5:** Record results and commit: `docs: vci write offline qualification`.

**Task test command:** `dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"`

---

### Task 8: Live verification and candidate timing (maintainer's machine, TIA Portal V21)

**Files:**
- Create: `docs/superpowers/evidence/<date>-vci-write-candidate/` (transient)
- Create: `docs/superpowers/acceptance/reports/<date>-vci-write-native-rejections-live-verification.md` (kept; also summarizes Stage 0)
- Modify: `docs/superpowers/README.md` (report row, so `verify-docs` reaches the report)

**Inputs** (Stage 0, `docs/superpowers/evidence/`):
- `2026-10-31-vci-write-stage0.md`, which holds the probe meanings;
- `2026-10-31-vci-write-stage0/case-inputs.json`, which holds the `T*`, `B*` and `P*` macros;
- `validate_timing_protocol.py` and `summarize.py`;
- `protected-supplement/` and `path-supplement/`.

**Setup:** match Stage 0.
- Candidate package from the Task 7 commit, in a short temporary local install.
- `full` mode, a 15-minute worker timeout and 1,000,000-character budgets from the CLI.
- The saved fixture (protected-FC revision).

**Record:**
- Once: Portal build and UI language, tool commit, worker timeout and its source.
- Before Step 1: the timed scopes' leaf counts (3, 10, 58; T5 10 mapped of 33) and the `base` mapping count (216). Compare them with Stage 0, and stop on a mismatch.
- Per case:
  - the canonical document;
  - independent `vci_read list_workspaces` and `list_mappings` with status;
  - before/after manifests with sizes and hashes;
  - `plc_read` and `compile_check` for imports.

  Restore after each mutating case.

The report has exactly one verdict table, one row per case, with a plain ID in the first cell. It never links evidence; it cites "PR #N, commit".

- [ ] **Step 1: Timing.** Replay `T1`–`T7` unchanged: warm-up plus three measured calls, with independent root, item-sum and remainder medians. Validate with the Stage 0 scripts, adapting only version or verification literals and recording the diff.
  Expected (inference): T1–T3 and T7 lower, T7 the most because the mapping scans are gone; T4–T6 flat. Record a slower case as it is.
- [ ] **Step 2: Contract matrix.** Expected:
  - C1 (P1, exact and case): `engineering_rejected`, ETI "must be unique"; the empty root remains.
  - C2 (P2, three variants): `vci_workspace_exists` blocks.
  - C3 (P3 nested): applied.
  - C4 (P4-OBmix): `engineering_rejected`; one entry names the inconsistent leaf; six leaves exported, `Equal`.
  - C5 (P5-protected): the entry carries the know-how message; no file and no mapping.
  - C6: a 259-character complete `.xml` exports; 260 is blocked by `vci_name_not_representable`.
  - C7 (P7 read-only): `engineering_rejected`; bytes unchanged.
  - C8 (P8 malformed): `failed:2`, `succeeded:0`, `projectModified:false`, one entry; sources unchanged; compile clean.
  - C9 (P8 missing): partial commit; the missing leaf is listed.
  - C10: re-running C4 skips six mapped leaves and lists the same failure.
- [ ] **Step 3: L matrix.** Expected:
  - L1 (all six operations, one workspace): the last item `trusted`, earlier ones `superseded`.
  - L2 (dependent export onto a stray `<stem>.xml`) and L5 (first-item export onto a stray file): no guard and no server-raised failure.
    - Overwrite, companion-file deletion and disruption of another mapping in that directory are accepted (ruling 5). Record them only.
    - A write outside the key's directory, or an import into another object, is O3: stop.
  - L3 (L1 with the C4 scope in the middle): `success:false`, `error:null`; the failed item has `failures` and `engineeringError`; later items skipped.
  - L4 (L1 `dryRun`): the dependent warning; first-item effects only. "No `vci_plan` for dependents" is offline-only (Task 6).
  - L6: `sync_to_project` (info `vci_import_unvalidated`), then `compile_check`.
- [ ] **Step 4:** Write the report:
  - provenance;
  - the timing comparison with Stage 0;
  - the verdict table;
  - the rulings applied;
  - limits: no live 25-failure case, untested formats and directory names, Portal loss offline only.

  Run `verify-docs` and the identity grep. Commit: `docs: vci write live verification`.

**Task test command:** `(Select-String -Path docs/superpowers/acceptance/reports/*vci-write-native-rejections-live-verification.md -Pattern '^\| (C([1-9]|10)|L[1-6]) \|').Count` returns 16.

---

### Task 9: Documentation (last, from Task 8 evidence; §10)

**Files:**
- Modify: `docs/SupportedOperations/VCI_OPERATIONS_SUMMARY.md` (2.0, classification with the C4 example, failures and recovery, native overwrite and files left by `delete_mapping`, the corrected length, dependents, `trusted`/`superseded`)
- Modify: `AGENTS.md`
  - VCI bullet: drop "Export safety scans grow with leaves × mappings"; add 2.0, failures and cap, native overwrite, root-only workspace check and the corrected length.
  - Failure-categories sentence: only PLC content importers still flatten.
- Modify: `docs/ARCHITECTURE.md` (§7a and the VCI section)
- Modify: `docs/IMPROVEMENT_LOG.md`
  - completed entry with timing;
  - close the VCI halves of "HMI and VCI adopt the same error contract" and "onto the shared verifier";
  - close the leaves × mappings follow-up;
  - add F28 ("XML read after a VCI re-import returns `.s7dcl`"), F29 ("counters appear to carry over between items") and any O3 rulings.
- Create: `docs/adr/0014-vci-write-native-rejection-contract.md` from `TEMPLATE.md` (why VCI joins ADR-0012's contract and accepts native overwrite; rejected: keep collision checks), plus its row in `docs/adr/README.md`
- Modify: `docs/roadmap/json-contract.md` (version note); `README.md` only if it states VCI guards or versions (absolute links)

- [ ] **Step 1:** Write the changes, citing transient files as "PR #N, commit `<sha>`".
- [ ] **Step 2:** Run `verify-docs`, the identity grep, and `git grep -n -E "vci_file_key_collision|notStatusVerified|leaves × mappings" -- "*.md" ":!docs/superpowers"`. Expected: 0 errors; no matches.
- [ ] **Step 3:** Commit: `docs: vci write native rejections`.

**Task test command:** `python -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30`

---

### Task 10: Prune working files (maintainer go-ahead only)

**Files:** Delete:
- `docs/superpowers/plans/2026-11-01-vci-write-native-rejections.md`
- `docs/superpowers/specs/2026-10-30-vci-write-native-rejections-design.md`
- `docs/superpowers/evidence/2026-10-31-vci-write-stage0*`
- the Task 8 evidence directory

- [ ] **Step 1:** Confirm the survivors: ADR 0014, the report and the IMPROVEMENT_LOG entry. Run `git grep -n -E "\]\([^)]*(evidence|plans|specs)/20" -- "*.md" ":!docs/superpowers/plans" ":!docs/superpowers/specs" ":!docs/superpowers/evidence"`. Expected: no match.
- [ ] **Step 2:** Delete. Run: `python -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30 --fail-on-transient`. Expected: 0 errors.
- [ ] **Step 3:** Commit: `docs: prune working files`.

**Task test command:** `python -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30 --fail-on-transient`

---

## Execution record

- 2026-11-01: branch rebased onto `main` `f7f468bf`; Stage 0 record moved to `docs/superpowers/evidence/`; plan written and adversarially reviewed (coverage, code facts, executability); findings folded in.
- 2026-11-01: Task 7 offline qualification. Stub build and real-assembly build (`TiaPortalV21Dir`, compile only): 0 errors each. `dotnet test --filter "Category!=Packaging"`: 7692 passed, 0 failed. Line coverage of the 16 non-test VCI source files changed on this branch (coverage collected with the test assembly included because host and worker sources are linked into it): 706/716 lines, 98.6%; one file below 80% is `VciVerificationPolicy.cs` (3/4 lines, 4-line file); `VciWriteContractVersion.cs` and `VciWriteRoundtripScenario.cs` have no coverable lines. Retired-name grep (production `*.cs`/`*.json`, tests excluded): no match; the only test matches are intentional negative tests. Whole-branch review: one Important finding (net48 root validation) plus minors, fixed in `7930e954` and re-reviewed Approved. `48a2a964` escapes a raw control character in a path-policy theory case. Task 8 (live) not started - awaits maintainer authorization.
- 2026-11-01: Task 8 complete on installed candidate `4057b44a` (`4.2.0-local.160.g4057b44a`), Portal build `2100.0.121.1`, English, full/CLI timeout15/budgets1000000. Unchanged Stage0 replay/validators: 21 measured samples, seven warmups, one discard, 103 exact audit pairs, all qualified. Root medians before→after ms: T1 3313→528, T2 12726→10103, T3 15797→11151, T4 39→42, T5 74073→87047, T6 22214→25124, T7 261084→154438; T5/T6 slowdowns retained. All20 final C/L variants pass (16 report rows). Test-helper corrections covered valid empty later pages, native create metadata, shared partial-write info, XML reads for the LAD fixture and observed GlobalDB selectors; original stopped attempts preserved, temporary workspace explicitly recovered, no candidate code changes. Final216 base mappings identical/Equal, six workspace inventories identical, 1249 file names/sizes/hashes unchanged, same saved-project hash independently confirmed, project open/unmodified, clients stopped. Durable report and index added; no remote writes. Task9 documentation follows; Task10 still awaits ready-to-merge go-ahead.
- 2026-11-01: Task 9 Ruling: describe failure diagnostics in recording order, superseding the plan/spec's global scope-order wording. Source review proved that synchronization prefilter failures can be recorded before earlier queued leaves fail natively; the first recorded leaf failure controls accumulated-failure classification. The minimal correction changes documentation and the public tool-description string, preserving qualified mutation logic; callers must not assume selected-leaf order. Also clarified the existing read-only workspace-create planning recheck, which explicitly retains worker_operation_failed plus engineeringError even for target-invocation exceptions. Final review's Important ordering discrepancy and contract-classification qualification are addressed; no runtime sorting or new mutation behavior is introduced.
- 2026-11-01: Task 9 complete. Updated AGENTS, VCI reference, architecture, improvement log, JSON-contract roadmap and ADR index; added ADR0014. Native-overwrite and shared-verifier decisions are current, VCI follow-ups closed, F28/F29 remain open historical observations, and T5/T6 regressions are explicit. Independent review found one Important ordering-description discrepancy and a read-only recheck classification qualification; both are corrected in current docs and the public tool description. Only the Description attribute changed in production source; executable/mutation code is identical to the qualified candidate. Focused description/schema tests: 26 passed, 0 failed/skipped (`dotnet test TiaMcpServer.Tests --no-restore -m:1 --filter "FullyQualifiedName~VciGuardedWriteMcpTests.Description_StatesContractEssentials|FullyQualifiedName~McpToolSchemaTests"`), run outside the sandbox after sandboxed startup/restore attempts were canceled before tests. No full-suite, package or additional live rerun was needed for this text-only correction. Docs verifier, retired-term/identity scans and diff checks are the final local commit gates. No push; retain working files until the maintainer's Task10 ready-to-merge go-ahead.
