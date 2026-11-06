# `library_write` (issue #218, PR 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One new tool, `library_write`, with 12 operations: global-library lifecycle, folders and master
copies. It works on the project library and on open global libraries, and runs through the shared guarded
write pipeline.

**Architecture:** Contracts hold the method names, the closed member rules and the wire models. The worker
(`Openness/Library/`) resolves targets, plans each item through one Observe method and mutates through one
ProjectMutation method per operation. The host `library_write` is an `IWriteDomain` shaped like `vci_write`.
Two small pipeline hooks make confirmation and the host capability gate depend on the call's items.

**Tech Stack:** C# (host net10.0, worker net48, contracts netstandard2.0), Openness V21
`Siemens.Engineering` library API, xunit, ModelContextProtocol SDK, FakeWorker.

**Spec:** [docs/superpowers/specs/2026-11-06-library-write-design.md](../specs/2026-11-06-library-write-design.md).
Native evidence: `docs/superpowers/acceptance/reports/2026-11-06-library-write-native-probes.md` (case ids
G*, F*, M* below).

## Decisions this plan adds to the spec

The code forces these choices. Task 10 carries them into the docs.

- **P1 Worker methods.** `library_global_<operation>` or `library_project_<operation>`. The `project`
  variant applies when the item touches the project library or a PLC: any `scope:"project"` selector,
  either side of `copy_master_copy`, and every `create_master_copy`/`instantiate_master_copy`. Lifecycle
  operations are global only. The plan methods `library_global_plan`/`library_project_plan` are Observe.
  Global methods map to capability `library_write`, project methods to `library_project_write`.
- **P2 PLC members are prefixed** (`plcName`, `plcKind`, segmented `plcFolderPath`, `plcObjectName`), so
  they don't clash with the library `folderPath`/`name`.
  - Source kinds are `block`, `plcType` and `tagTable`.
  - Destination kinds `blockGroup`/`typeGroup`/`tagTableGroup` name the container group; an empty
    `plcFolderPath` is the root.
  - Instantiation uses the container's `Blocks`/`Types`/`TagTables`
    `.CreateFrom(mc, MasterCopyMode.ThrowIfExists)`.
  - There is no `unitName`.
- **P3 Segment matching keeps each domain's rule.** Library folders match ordinal, through the existing
  `LibraryReader` helpers. PLC groups match OrdinalIgnoreCase, through `PlcSoftwareLocator`,
  `BlockTargetResolver` and the type/tag group walks.
- **P4 Saving.** `close_global` with `save` calls `Save()` only when `IsModified` is true. G8 shows that
  `Save()` on a clean read-only library would be refused.
- **P5 Library kind.** `save_global`, `close_global` and `archive_global` need a `UserGlobalLibrary`.
  Any other global library fails `target_kind_unsupported` at plan time.
- **P6 Master-copy collisions.** The collision check covers master copies only, not folders, compared
  OrdinalIgnoreCase. `actualName` is the `Name` of the `MasterCopy` returned by `Create`/`CreateFrom`.
- **P7 `delete_folder`** deletes a user folder of either kind (`masterCopies` or `types`).
  - The preview lists contents recursively, capped at 200 entries, with `contentsTruncated`.
  - A types folder whose types have dependents is refused by Portal (F6).
- **P8 Discard guard.** `library_discards_changes` fires on every `closeAction:"discard"`, with no
  dirty-state read.
- **P9 Already-open and actual paths.** `alreadyOpen` comes from a plan-time scan of `GlobalLibraries` by
  canonical path. `open_global` always calls `Open`, so Portal still refuses a mode change (G4). Created,
  opened and retrieved libraries report `Path.FullName` from the returned library, which covers G6.
- **P10 Outcome.** `LibraryWriteOutcomeInfo { bool? Saved; string? ActualName }` is carried on failure.
  It is used in two cases: `close_global` saved but `Close` threw, and a renamed master copy
  (`postcondition_failed`).
- **P11 No `ExclusiveAccess` and no transactions.** The spec claims no rollback.
- **P12 Counts.** The tool counts move from 9/20/20 to **9/21/21**.
- **P13 Archive extension.** `ArchiveModeNames.EnsureExtension(string name, string extension)` is
  extracted from `EnsureArchiveExtension`, which keeps its behavior.
  `LibraryWriteMethods.ArchiveExtension = ".zal21"`.

## Global Constraints

- `library_write` is registered only when `ProjectMutation` is allowed (read-write, full). `library_read`
  stays at contract `1.0` and is unchanged.
- The write root is `contractVersion:"2.0"` (`LibraryWriteContractVersion.Current`), canonical JSON, with no
  nested JSON strings.
- A call has 1–50 items with unique `operationId`s of at most 256 characters. Items run in caller order and
  stop at the first failure. There is no rollback and no replay. `dryRun` defaults to false.
- No item depends on an earlier item in the same call; `ReplanAsync` throws.
- Every call needs a verified binding; unbound calls fail `binding_conflict`.
- The six lifecycle operations prompt once per read-write call. Full mode never prompts (`policy`).
  Content-only calls never prompt. There are no acknowledge guards, no tokens and no public confirmation
  argument.
- Guards:
  - Info: `library_discards_changes`, `library_deletes_folder_contents`.
  - Block: `library_master_copy_exists`, `library_state_unverifiable`.
- Do not pre-check what Portal refuses (ADR-0012). The probe report lists what Portal refuses.
- Budgets use the effective `StructuredOperationBatchPayloadBudget.MaxItemChars` / `MaxDocumentChars`, never
  a literal.
- Never dispose `TiaPortalProcess`. Never write project or library names into repo files or commits.
- Commits are one-line conventional commits with no body.
- Use PowerShell for `dotnet` commands that carry `/p:` flags.
- Implementers run only their task's test command. The orchestrator runs the full suite.
- No README, AGENTS, ARCHITECTURE or `docs/SupportedOperations` edits before Task 10.

## Review Focus

- **Clean read-only library closed with `save`.** Expect success with no `Save()` call (P4, G8). Test in Task 5.
- **Master-copy name differing only in case from an existing one.** Expect the `library_master_copy_exists`
  block guard (P6, M1). Test in Task 4.
- **Archive name already ending in `.ZAL21`.** Expect no second extension. Test in Task 2.
- **Two open global libraries with the same name and a name-only selector.** Expect `target_ambiguous`; the
  path selector succeeds. Test in Task 4.
- **Read-write call mixing one lifecycle item and content items.**
  - The mixed call prompts exactly once.
  - A content-only call never prompts.
  - A declined prompt mutates nothing and returns `access_denied`.
  - Test in Task 8.

---

## Execution notes (orchestrator; not a task)

- The branch is `feat/library-write`, with the probe commits and the spec (`c9315bc3`) already on it.
  Work in the main working copy. Do not push.
- After each task, run `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true` (PowerShell),
  then `dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"`.
- After Task 3, also run `dotnet test TiaMcpServer.Tests --filter "Category=Packaging"`.
- Task 9 is maintainer-run and needs explicit authorization for a disposable Portal fixture.
- Before the PR merges, the final commit is `docs: prune working files`, as described in `AGENTS.md`.

### Task 1: Item-aware confirmation and capability hooks

**Files:**
- Modify: `TiaMcpServer/Safety/Pipeline/IWriteDomain.cs`, `TiaMcpServer/Safety/Pipeline/WriteRun.cs` (gate
  at :136–142, confirmation at :246–247), `TiaMcpServer.Tests/Safety/Pipeline/PipelineFakes.cs`
- Test: `TiaMcpServer.Tests/Safety/Pipeline/ModeDerivedConfirmationTests.cs`, new
  `TiaMcpServer.Tests/Safety/Pipeline/CapabilityHookTests.cs`

**Interfaces:**
- Produces:
  - On `IWriteDomain<TItem,…>`: `bool ConfirmsCall(IReadOnlyList<TItem> items) => ConfirmsEveryCall;`
  - On `IWriteDomain<TItem,…>`: `IEnumerable<string> CapabilityOperations(IReadOnlyList<TItem> items) => new[] { ToolName };`
  - `WriteRun` calls `ConfirmsCall(Items)` in place of `ConfirmsEveryCall`.
  - `WriteRun` refuses with `unsupported_capability` on the first operation where
    `ProjectCapabilityCatalog.Supports(kind, op)` is false. The message names that operation.
  - `FakeWriteDomain` gains `Func<IReadOnlyList<FakeWriteItem>,bool>? ConfirmsCallOverride` and
    `Func<IReadOnlyList<FakeWriteItem>,IEnumerable<string>>? CapabilityOperationsOverride`.

- [ ] Write failing tests:
  - `ReadWrite_ItemAwareDomain_PromptsOnlyWhenItemsRequire(requires, expectedPrompts)`: audit confirmation is
    `user` when a prompt happened, otherwise `none`.
  - `Full_ItemAwareDomain_NeverPrompts_AuditsPolicy`.
  - `CapabilityHook_FirstUnsupportedOperation_RefusesBeforePlan`: no worker call, the error names the
    operation.
  - `CapabilityHook_Default_ChecksToolName`: the existing behavior.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~TiaMcpServer.Tests.Safety.Pipeline"`.
  Expected: the new tests FAIL to compile or fail.
- [ ] Implement the two default members and the two `WriteRun` call sites. Existing domains stay unchanged.
- [ ] Run the same command. Expected: PASS, including the existing `ConfirmsEveryCall` tests in
  `WriteConfirmationPipelineTests`, `LifecycleGuardedBehaviorTests` and `HmiGuardedWriteDomainTests`.
- [ ] Commit `feat: add item-aware confirmation and capability hooks`.

Task test command: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~TiaMcpServer.Tests.Safety.Pipeline"`

### Task 2: Library write contracts

**Files:**
- Create: `TiaMcpServer.Contracts/Library/LibraryWriteMethods.cs`, `TiaMcpServer.Contracts/Library/LibraryWriteModels.cs`,
  `TiaMcpServer.Contracts/Library/LibrarySelectorRules.cs`
- Modify: `TiaMcpServer.Contracts/Library/LibraryReadMethods.cs` (use `LibrarySelectorRules`; behavior
  unchanged), `TiaMcpServer.Contracts/Project/ArchiveModeNames.cs`
- Test: new `TiaMcpServer.Tests/Library/LibraryWriteMethodsTests.cs`, `TiaMcpServer.Tests/Project/ArchiveModeNamesTests.cs`

**Interfaces:**
- Produces:
  - `LibrarySelectorRules.Check(string? scope, string? libraryPath, string? libraryName, List<string>? folderPath, string memberPrefix) -> string?`.
    It extracts the rules from `LibraryReadMethods.Validate` L56–64: scope, absolute path, nonblank name,
    at most 32 nonblank segments. `memberPrefix` is `""` or `"source"` and is used in messages.
  - `LibraryWriteMethods`:
    - `Operations` (the 12 names), `LifecycleOperations` (6).
    - `GlobalLibraryWrites`, `ProjectLibraryWrites`, `All` (the write methods plus both plan methods).
    - `const GlobalPlan = "library_global_plan"`, `const ProjectPlan = "library_project_plan"`,
      `const ArchiveExtension = ".zal21"`.
    - `bool TouchesProject(LibraryWriteRequestInfo)`, `string ForItem(LibraryWriteRequestInfo)`,
      `string PlanFor(LibraryWriteRequestInfo)` (P1).
    - `string? OperationFor(string method)`: returns `"library_write"`, `"library_project_write"` or null.
    - `string? Validate(LibraryWriteRequestInfo)`: closed members per operation.
  - `LibraryWriteRequestInfo` (`[JsonUnmappedMemberHandling(Disallow)]`) has these members: `Operation`,
    `OperationId`, `Scope`, `LibraryPath`, `LibraryName`, `FolderKind`, `List<string>? FolderPath`, `Name`,
    `SourceScope`, `SourceLibraryPath`, `SourceLibraryName`, `List<string>? SourceFolderPath`, `Directory`,
    `FilePath`, `OpenMode`, `CloseAction`, `PlcName`, `PlcKind`, `List<string>? PlcFolderPath`,
    `PlcObjectName`.
  - `LibraryWritePlanInfo { LibraryIdentityInfo? Library; LibraryIdentityInfo? Target; bool? AlreadyOpen; bool MasterCopyExists; List<LibraryIdentityInfo> FolderContents; bool ContentsTruncated; string? Unverifiable }`.
  - `LibraryWriteResultInfo { LibraryIdentityInfo Library; LibraryIdentityInfo? Target; bool? AlreadyOpen; string? ArchivePath; string? ActualName; bool? Saved }`.
  - `LibraryWriteOutcomeInfo { bool? Saved; string? ActualName }`.
  - Nullable members are written explicitly (`JsonIgnoreCondition.Never`), as in the VCI result infos.
  - `ArchiveModeNames.EnsureExtension(string name, string extension) -> string` (P13).
- Required members per operation (every other member is rejected):

| Operation | Required | Optional |
| --- | --- | --- |
| `create_global` | `directory`, `name` | — |
| `open_global` | `filePath` (`.al21`), `openMode` (`readOnly`/`readWrite`) | — |
| `retrieve_global` | `filePath` (`.zal21`), `directory`, `openMode` | — |
| `save_global` | `scope:"global"` + library | — |
| `close_global` | `scope:"global"` + library, `closeAction` (`save`/`discard`) | — |
| `archive_global` | `scope:"global"` + library, `directory`, `name` | — |
| `create_folder`, `delete_folder` | `scope`, `folderKind`, `name` | library, `folderPath` |
| `create_master_copy` | `scope`, `plcName`, `plcKind` (source kind), `plcObjectName` | library, `folderPath`, `plcFolderPath` |
| `copy_master_copy` | `scope`, `sourceScope`, `name` | both libraries, both folder paths |
| `instantiate_master_copy` | `scope`, `name`, `plcName`, `plcKind` (destination kind) | library, `folderPath`, `plcFolderPath` |
| `delete_master_copy` | `scope`, `name` | library, `folderPath` |

- [ ] Write failing tests:
  - `Validate_RejectsMemberNotInOperation` (theory over every operation).
  - `Validate_RequiresCloseAction`.
  - `Validate_RejectsRelativeDirectoryAndFilePath`.
  - `Validate_ProjectScopeRejectsLibraryPathAndName`.
  - `ForItem_UsesProjectVariantWhenItemTouchesProject` (each P1 case).
  - `LifecycleOperations_HaveOnlyGlobalMethods`.
  - `OperationFor_MapsEveryMethod`.
  - `EnsureExtension_AppendsZal21_CaseInsensitive`, `EnsureExtension_UpperCaseExtension_NotDoubled`.
  - The existing `LibraryReadCatalogTests` stay green.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~TiaMcpServer.Tests.Library|FullyQualifiedName~ArchiveModeNamesTests"`.
  Expected: FAIL.
- [ ] Implement in netstandard2.0.
- [ ] Run the same command. Expected: PASS.
- [ ] Commit `feat: add library write contracts`.

Task test command: as in the run step above.

### Task 3: Catalogs, wire members, outcome plumbing and stubs

**Files:**
- Modify:
  - `TiaMcpServer.Contracts/Safety/OperationPolicyCatalog.cs`: after the `LibraryReadMethods` loop, add
    `LibraryWriteMethods.All`. The two plan methods are Observe; everything else is ProjectMutation.
  - `TiaMcpServer.Contracts/Project/ProjectCapabilityCatalog.cs`: put `library_write` in
    `ProjectContentOperations` and `library_project_write` in `StandaloneLibraryOperations`. Map
    `OperationFor` through `LibraryWriteMethods.OperationFor`.
  - `TiaMcpServer.Contracts/Worker/WorkerRequest.cs`: add `LibraryWrite`.
  - `TiaMcpServer.Contracts/Worker/WorkerResponse.cs`: add a conditional `LibraryWriteOutcome`.
  - `TiaMcpServer/Worker/WorkerCallResult.cs`.
  - The copy sites in `OpennessWorkerClient.Invocation.cs` (:197, :217) and `OpennessWorkerClient.Binding.cs`
    (:813, :834, :860, :874).
  - `TiaMcpServer/Worker/PersistentWorkerTransport.cs`: add a third validation block after :137.
  - `TiaMcpServer.OpennessWorker/Worker/WorkerOperationException.cs` and
    `TiaMcpServer.OpennessWorker/Worker/WorkerExecution.cs` (:31–43).
  - `TiaMcpServer.OpennessWorker/Program.cs`: the `Failure(...)` helper.
  - `reference-stubs/Siemens.Engineering.Base/Library.cs`, `reference-stubs/Siemens.Engineering.Step7/*.cs`
    and the generated `ref/` assemblies, following `reference-stubs/README.md`.
- Create: `TiaMcpServer/Library/LibraryWritePayloadContract.cs` (only `ValidateEnvelopeOutcome` in this task).
- Test: `TiaMcpServer.Tests/Json/ConditionalMemberRegisterTests.cs`, `Project/ProjectCapabilityCatalogTests.cs`,
  `Multiuser/LocalSessionContextContractTests.cs` (:344, :434), `Worker/WorkerExecutionTests.cs`, new
  `Library/LibraryWriteCatalogRegistrationTests.cs`

**Interfaces:**
- Consumes (Task 2): `LibraryWriteMethods`, `LibraryWriteRequestInfo`, `LibraryWriteOutcomeInfo`.
- Produces:
  - `WorkerRequest.LibraryWrite` (`LibraryWriteRequestInfo?`).
  - `WorkerResponse.LibraryWriteOutcome` / `WorkerCallResult.LibraryWriteOutcome` (`LibraryWriteOutcomeInfo?`).
  - A new `libraryWriteOutcome:` parameter on `WorkerOperationException`.
  - `LibraryWritePayloadContract.ValidateEnvelopeOutcome(JsonElement)`: exact-cased, single member, else `JsonException`.
  - Stub members:
    - `GlobalLibraryComposition.Create<UserGlobalLibrary>(DirectoryInfo,string)`, `Open(FileInfo,OpenMode)`,
      `Retrieve(FileInfo,DirectoryInfo,OpenMode)`.
    - `UserGlobalLibrary.Save()`, `Close()`, `Archive(DirectoryInfo,string,LibraryArchivationMode)`,
      `IsModified`.
    - `MasterCopyComposition.Create(IMasterCopySource)`, `CreateFrom(MasterCopy)`.
    - `MasterCopyUserFolder.Delete()`, `LibraryTypeUserFolder.Delete()`, `MasterCopy.Delete()`, `Folders.Create(string)`.
    - `PlcBlockComposition`, `PlcTypeComposition` and `PlcTagTableComposition`
      `.CreateFrom(MasterCopy, MasterCopyMode)`.
    - Add only the members that are missing.

- [ ] Write failing tests:
  - `WriteMethods_AreProjectMutation_PlansAreObserve`.
  - `WriteMethods_DeniedInReadOnly`.
  - `GlobalWrites_StandaloneAndLocalSession`, `ProjectWrites_StandaloneOnly`.
  - `ServerProject_LibraryWrite_NotYetDelivered`.
  - The new rows in `LocalSessionContextContractTests`.
  - A `"WorkerResponse.LibraryWriteOutcome"` register entry, with a description of when it appears.
  - `LibraryOutcome_SurvivesPortalLostReclassification`.
  - `Transport_MalformedLibraryOutcome_IsProtocolError` (outer case, duplicate, nested case; the payload is
    not echoed).
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Library|FullyQualifiedName~ConditionalMemberRegisterTests|FullyQualifiedName~ProjectCapabilityCatalogTests|FullyQualifiedName~LocalSessionContextContractTests|FullyQualifiedName~WorkerExecutionTests|FullyQualifiedName~PersistentWorkerTransport"`.
  Expected: FAIL.
- [ ] Implement.
- [ ] Rebuild the stubs per `reference-stubs/README.md`. Run `pwsh -NoProfile -File scripts/verify-reference-stubs.ps1`.
  Expected: no drift reported.
- [ ] Run the task command. Expected: PASS. Then run `dotnet test TiaMcpServer.Tests --filter "Category=Packaging"`.
  Expected: PASS.
- [ ] Commit `feat: register library write methods and outcome`.

Task test command: as in the run step above.

### Task 4: Worker target resolution and plan

**Files:**
- Create: `TiaMcpServer.OpennessWorker/Openness/Library/LibraryWriteTargets.cs`, `.../LibraryWritePlanReader.cs`
- Modify:
  - `.../Openness/Library/LibraryReader.cs`: make `ChildRows` and the `RowReader` identity builders
    (`Identity`, `Child`, `Copy`) internal static in a new `LibraryIdentities` class, with read output
    unchanged.
  - The private type and tag group walks in `PlcTypeTargetResolver` and `TagMutationService`: expose them
    as internal segmented helpers.
  - `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`: link the new worker files.
  - `TiaMcpServer.Tests/TestSupport/LibrarySiemensDoubles.cs`.
- Test: new `TiaMcpServer.Tests/Library/LibraryWritePlanReaderTests.cs`

**Interfaces:**
- Consumes (Tasks 2–3): `LibraryWriteRequestInfo`, `LibraryWritePlanInfo`, `LibraryReader.Resolve`/`Exact`/`CopyFolder`/`TypeFolder`/`CanonicalPath`.
- Produces:
  - `LibraryWriteTargets.Library(TiaPortal, ProjectBase, string? scope, string? path, string? name) -> ILibrary`.
  - `LibraryWriteTargets.UserLibrary(...) -> UserGlobalLibrary` (P5).
  - `LibraryWriteTargets.PlcSource(ProjectBase, LibraryWriteRequestInfo) -> IMasterCopySource`.
  - `LibraryWriteTargets.PlcDestination(ProjectBase, LibraryWriteRequestInfo) -> (object group, string kind)`.
  - `LibraryWritePlanReader.Plan(TiaPortal, ProjectBase, string method, LibraryWriteRequestInfo) -> LibraryWritePlanInfo`.
    It refuses a method/item mismatch (`validation_error`), as `VciWriter` does.
  - Plan rules:
    - Creates and copies set `MasterCopyExists` (P6).
    - `delete_folder` fills `FolderContents`, capped at 200 (P7).
    - Lifecycle sets `AlreadyOpen` (P9).
    - An unreadable folder listing or flag sets `Unverifiable` and never throws.
    - Missing targets fail `target_not_found`; duplicates fail `target_ambiguous`.

- [ ] Write failing tests against the doubles:
  - `Plan_MasterCopyNameDiffersOnlyInCase_SetsMasterCopyExists`.
  - `Plan_DuplicateGlobalName_NameOnly_TargetAmbiguous`, `Plan_DuplicateGlobalName_PathSelector_Resolves`.
  - `Plan_UnopenedGlobalLibrary_TargetNotFound`.
  - `Plan_SaveOnCorporateLibrary_TargetKindUnsupported`.
  - `Plan_DeleteFolder_ListsNestedContents_TruncatesAt200`.
  - `Plan_UnreadableFolder_SetsUnverifiable`.
  - `Plan_OpenAlreadyOpenPath_AlreadyOpenTrue`.
  - `Plan_ProjectScope_LocalSession_UnsupportedCapability`.
  - `Plan_CallsNoMutatingMember`.
  - The existing `LibraryReaderTests` stay green.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~TiaMcpServer.Tests.Library"`. Expected: FAIL.
- [ ] Implement.
- [ ] Run the same command. Expected: PASS.
- [ ] Commit `feat: add worker library write planning`.

Task test command: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~TiaMcpServer.Tests.Library"`

### Task 5: Worker mutations and dispatch

**Files:**
- Create: `TiaMcpServer.OpennessWorker/Openness/Library/LibraryWriter.cs`
- Modify: `TiaMcpServer.OpennessWorker/Program.cs` (dispatch next to :211; `PlanLibraryWrite`/`WriteLibrary`
  next to `ReadLibrary`, both through `WithActiveContext`), `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`,
  `TestSupport/LibrarySiemensDoubles.cs`
- Test: new `TiaMcpServer.Tests/Library/LibraryWriterTests.cs`, `Library/LibraryWorkerDispatchTests.cs`

**Interfaces:**
- Consumes (Task 4): `LibraryWriteTargets`. Consumes (Task 3): `WorkerOperationException(libraryWriteOutcome:)`.
- Produces: `LibraryWriter.Write(TiaPortal, ProjectBase, string method, LibraryWriteRequestInfo) -> LibraryWriteResultInfo`.
  It re-checks the method/item pair, and it re-resolves targets itself because there is no plan handoff.
  Native calls per operation, per the spec's table:
  - `archive_global`: `Archive(dir, EnsureExtension(name, ".zal21"), LibraryArchivationMode.Compressed)`;
    `ArchivePath` is the final path.
  - `close_global`: `Save()` only when `save` and `IsModified`, then `Close()`. When `Close` throws after a
    save, throw a `WorkerOperationException` that carries `LibraryWriteOutcomeInfo{Saved=true}` and keeps
    the Siemens classification (`WorkerExecution.Classify(ex, true)`, `engineeringError`).
  - `create_master_copy` / `copy_master_copy`: when the returned `MasterCopy.Name` differs from the
    requested name, throw `postcondition_failed` with `ActualName`.
  - `instantiate_master_copy`: always `MasterCopyMode.ThrowIfExists` (P2).
  - Lifecycle results use the returned library's `Path.FullName` (P9).

- [ ] Write failing tests against the doubles:
  - One success test per operation (12).
  - `CloseSave_CleanReadOnlyLibrary_DoesNotCallSave` (Review Focus).
  - `CloseSave_CloseThrowsAfterSave_OutcomeSavedTrue`.
  - `CloseDiscard_NeverCallsSave`.
  - `Archive_NameWithoutExtension_AppendsZal21`.
  - `CreateMasterCopy_NativeRename_PostconditionFailedWithActualName`.
  - `Instantiate_UsesThrowIfExists`.
  - `EngineeringTargetInvocation_IsEngineeringRejected`.
  - `LibraryWorkerDispatchTests`: the write and plan dispatch lines exist and use `WithActiveContext`.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~TiaMcpServer.Tests.Library"`. Expected: FAIL.
- [ ] Implement.
- [ ] Run the same command. Expected: PASS.
- [ ] Commit `feat: add worker library write operations`.

Task test command: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~TiaMcpServer.Tests.Library"`

### Task 6: Host catalog, guards, planner and payloads

**Files:**
- Create in `TiaMcpServer/Library/` (linked by the existing glob):
  - `LibraryWriteItem.cs`, `LibraryWriteCatalog.cs`, `LibraryGuardDefinitions.cs`, `LibraryWritePlanner.cs`
  - `LibraryWriteModels.cs`, `LibraryWritePayloadBudget.cs`, `LibraryVerificationPolicy.cs`
  - `LibraryWriteContractVersion.cs`
- Modify: `TiaMcpServer/Library/LibraryWritePayloadContract.cs` (add `Project`),
  `TiaMcpServer/Worker/OpennessWorkerClient.Domains.cs`
- Test: new `TiaMcpServer.Tests/Library/LibraryWriteCatalogTests.cs`, `LibraryGuardDefinitionsTests.cs`,
  `LibraryWritePlannerTests.cs`, `LibraryVerificationPolicyTests.cs`, `LibraryWriteBudgetTests.cs`,
  `LibraryWriteContractDecodeTests.cs`

**Interfaces:**
- Consumes (Tasks 2–3): contracts and `ValidateEnvelopeOutcome`.
- Produces:
  - `LibraryWriteItem : IOperationBatchItem` (Disallow unmapped). Flat members mirror `LibraryWriteRequestInfo`.
  - `LibraryWriteCatalog.ValidateWrite(IReadOnlyList<LibraryWriteItem>?) -> OperationValidation`,
    `ValidateAccessMode(items, McpAccessMode) -> IReadOnlyList<string>`,
    `ToWorkerRequest(LibraryWriteItem) -> LibraryWriteRequestInfo`.
  - `LibraryGuardDefinitions`: four `const string` ids and
    `Definitions = WriteGuardCatalog.NonInteractive(..., "Library")`.
  - `record LibraryPlanResult(WritePlan<LibraryWriteEffect> Plan, IReadOnlyDictionary<string, IReadOnlyList<FiredGuard>> Guards)`.
  - `LibraryWritePlanner(OpennessWorkerClient).PlanAsync(items) -> Task<LibraryPlanResult>`: one
    `PlanFor(item)` call per item, decoded with `CanonicalJson.DeserializeWorkerPayload<LibraryWritePlanInfo>`.
    A `JsonException` becomes `protocol_error`. Guards fire as follows:
    - `library_master_copy_exists` when `MasterCopyExists`.
    - `library_deletes_folder_contents` when the contents list is non-empty.
    - `library_discards_changes` on every discard (P8).
    - `library_state_unverifiable` when `Unverifiable` is set.
  - `record LibraryWriteEffect(LibraryIdentityInfo? Library, LibraryIdentityInfo? Target, bool? AlreadyOpen, IReadOnlyList<LibraryIdentityInfo> FolderContents, bool ContentsTruncated)`,
    plus `LibraryWriteEffectPresentation` and `LibraryGuardedWriteResponse`, with the same shape as
    `VciGuardedWriteResponse`.
  - `LibraryWritePayloadContract.Project(LibraryWriteItem, WorkerCallResult) -> StructuredOperationItem`:
    it decodes the result on success and the outcome on failure. A malformed result is `protocol_error`.
  - `LibraryWritePayloadBudget.Apply(response, int? maxItemChars = null, int? maxDocumentChars = null)` and
    `MeasureProtectedCore(items)`.
  - `LibraryVerificationPolicy : IWriteVerificationPolicy<LibraryWriteItem>` with `HasReadBack => false`.
    `ObjectKeys` uses the canonical library path or the project, plus folder segments and name. A later
    item on the same object supersedes an earlier one.
  - `LibraryWriteContractVersion.Current = "2.0"`.
  - `OpennessWorkerClient.WriteLibraryAsync(string method, LibraryWriteRequestInfo write) -> Task<WorkerCallResult>`,
    through `SendBoundProjectRequestAsync`, as `WriteVciAsync` does.

- [ ] Write failing tests:
  - Catalog: count/id rules, closed members via Task 2, read-only denial.
  - Guards: every id is registered and none is acknowledge.
  - Planner: each guard fires from its plan flag, discard always fires, the malformed plan is
    `protocol_error`, and `target_not_found` passes through.
  - Verification: `trusted`/`superseded` on the same folder and name.
  - Budget: an oversized folder-contents preview is omitted, not truncated mid-entry.
  - Decode: an absent outcome is omitted.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~TiaMcpServer.Tests.Library"`. Expected: FAIL.
- [ ] Implement.
- [ ] Run the same command. Expected: PASS.
- [ ] Commit `feat: add library write host planning and payloads`.

Task test command: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~TiaMcpServer.Tests.Library"`

### Task 7: Domain, tool and registration

**Files:**
- Create: `TiaMcpServer/Library/LibraryWriteDomain.cs`, `TiaMcpServer/Library/LibraryWriteTools.cs`
- Modify:
  - `TiaMcpServer/Tools/McpToolRegistration.cs`: add the `ProductionTools` entry, append the tool to the
    write array at :84, and concat `LibraryGuardDefinitions.Definitions` at :116.
  - The tool-surface tests: `Tools/AccessModeDiscoveryTests.cs`, `Safety/ReadOnlyModeTests.cs` (~586),
    `Tools/McpToolSchemaTests.cs` (~162), `Tools/WriteToolMcpAnnotationProtocolTests.cs`,
    `Safety/Pipeline/WriteValidationTests.cs`, `Tools/ToolOutputContractConformanceTests.cs` (probes,
    contract `2.0`, required arguments), `Multiuser/LocalSessionCapabilityTests.cs` (:204, :258).
- Test: new `TiaMcpServer.Tests/Library/LibraryGuardedWriteDomainTests.cs`

**Interfaces:**
- Consumes (Task 1): `ConfirmsCall`, `CapabilityOperations`. Consumes (Task 6): every host type.
- Produces:
  - `LibraryWriteDomain(OpennessWorkerClient) : IWriteDomain<LibraryWriteItem, LibraryWriteEffect, WriteVerification, LibraryGuardedWriteResponse>`.
    - `ToolName = "library_write"`.
    - `ConfirmsCall` is true when any item is in `LifecycleOperations`.
    - `DescribeForConfirmation` names each lifecycle operation and its target.
    - `CapabilityOperations` yields `ForItem` per item.
    - `Validate` uses `WriteValidation.Compose(ValidateWrite, MeasureProtectedCore, ValidateAccessMode, ToolName)`.
    - `ReplanAsync` throws.
    - `VerifyAsync` uses `TrustedWriteVerifier<LibraryWriteItem>`.
  - `LibraryWriteTools.LibraryWrite(OpennessWorkerClient workerClient, WriteExecution execution, LibraryWriteItem[] operations, bool dryRun = false, McpServer? server = null, CancellationToken cancellationToken = default)`
    with attributes `Name="library_write", ReadOnly=false, Destructive=true, OpenWorld=false,
    UseStructuredContent=true`. It passes `new WriteConfirmationContext(server is null ? null :
    UserConfirmation.For(server, TimeSpan.FromMinutes(2)))`. The tool description lists the 12 operations,
    names the lifecycle prompt, and says there are no intra-call dependencies.

- [ ] Write failing tests:
  - `ConfirmsCall_TrueOnlyWithLifecycleItem`.
  - `CapabilityOperations_ProjectItemOnLocalSession_RefusedBeforeAnyMutation`, for a call that mixes global
    and project items.
  - `DryRun_PlansWithoutMutation`.
  - `StopsAtFirstFailure_NoLaterMutation`.
  - The surface counts move to 9/21/21, and the annotation tuple is `("library_write", false, true, null, false)`.
  - Conformance: rejection and preview probes, plus `"library_write"` in the contract-`2.0` set.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~TiaMcpServer.Tests.Library|FullyQualifiedName~AccessModeDiscoveryTests|FullyQualifiedName~ReadOnlyModeTests|FullyQualifiedName~McpToolSchemaTests|FullyQualifiedName~WriteToolMcpAnnotationProtocolTests|FullyQualifiedName~WriteValidationTests|FullyQualifiedName~ToolOutputContractConformanceTests|FullyQualifiedName~LocalSessionCapabilityTests"`.
  Expected: FAIL.
- [ ] Implement.
- [ ] Run the same command. Expected: PASS.
- [ ] Commit `feat: add library_write tool`.

Task test command: as in the run step above.

### Task 8: FakeWorker scenario and MCP protocol tests

**Files:**
- Create: `TiaMcpServer.FakeWorker/Library/LibraryWriteScenario.cs`, `TiaMcpServer.Tests/Library/LibraryWriteMcpTests.cs`
- Modify: `TiaMcpServer.FakeWorker/Program.cs`: route `LibraryWriteMethods.All` next to the read branch
  (:461–472), for scenarios `library-write-roundtrip` and `library-write-portal-lost`.

**Interfaces:**
- Consumes (Task 7): the registered tool. Consumes (Task 3): the wire members.
- Produces: scripted plan and write replies.
  - Library name `Missing` returns `target_not_found`.
  - Name `Taken` returns `MasterCopyExists:true`.
  - Name `Renamed` returns a failure with outcome `ActualName`.
  - Name `MalformedOutcome` returns a bad outcome member.

- [ ] Write failing tests (`[Collection("Mcp protocol serial")]`):
  - `ReadWrite_LifecycleCall_PromptsOnce_AuditUser`.
  - `ReadWrite_MixedLifecycleAndContent_PromptsOnce`.
  - `ReadWrite_ContentOnly_NoPrompt_AuditNone`.
  - `ReadWrite_Declined_NoMutation_AccessDenied`.
  - `Full_LifecycleCall_NoPrompt_AuditPolicy`.
  - `ReadOnly_ToolAbsent`.
  - `Unbound_BindingConflict`.
  - `UnknownMember_CanonicalValidationError_NoAudit` (`ArgumentRejectionAssert.IsCanonicalValidationError(reply, "library_write")`).
  - `MasterCopyExists_Blocked`.
  - `Renamed_PostconditionFailed_ReportsActualName`.
  - `MalformedOutcome_ProtocolError_NotEchoed`.
  - `PortalLost_InvalidatesBinding`.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~LibraryWriteMcpTests"`. Expected: FAIL.
- [ ] Implement the scenario.
- [ ] Run the same command. Expected: PASS.
- [ ] Commit `test: add library_write protocol tests`.

Task test command: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~LibraryWriteMcpTests"`

### Task 9: Offline gate and live acceptance (maintainer)

**Files:**
- Create: `docs/superpowers/acceptance/reports/<date>-library-write-live-verification.md` and its index row
  in that directory's README.

- [ ] Run `dotnet build TiaMcpServer.slnx -m:1` against the real V21 assemblies (PowerShell), then
  `dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"`. Expected: build succeeds, 0 failed.
- [ ] Get the maintainer's authorization for one disposable standalone project, one disposable user global
  library, and one local session. Then run the cases through MCP in read-write mode, plus one full-mode call:
  - each of the 12 operations once;
  - G9 discard (guard shown, changes gone) and close-save on a clean read-only library;
  - G13 archive without extension;
  - M1 collision blocked; instantiate over an existing block refused (M5);
  - non-empty folder delete preview (F5);
  - duplicate-name selector;
  - lifecycle prompt shown, content-only call silent;
  - a global-only call on the local session, and a project item refused there.
- [ ] Write the report: verdict, provenance, cases, and limits. Record any defect as a follow-up before
  Task 10.
- [ ] Commit `docs: add library_write live report`.

Task test command: `rg -c "library_write" docs/superpowers/acceptance/reports/*library-write-live-verification.md`
(expected: at least 1).

### Task 10: Documentation (after live acceptance)

**Files:**
- Modify:
  - `README.md` (tool list and counts, absolute links), `AGENTS.md`.
  - `docs/ARCHITECTURE.md`.
  - `docs/SupportedOperations/LIBRARY_OPERATIONS_SUMMARY.md` (new write section), `docs/README.md`.
  - `docs/IMPROVEMENT_LOG.md`.
- Create: `docs/adr/00NN-library-write-staging-and-lifecycle-prompt.md`, plus its row in `docs/adr/README.md`.

- [ ] Write the docs.
  - Counts become 9/21/21.
  - The `library_write` bullet carries decisions P1–P13.
  - Global-library lifecycle is not subject to the project-lifecycle standalone-only rule.
  - The two new pipeline hooks.
  - The ADR records the staging, the lifecycle-only prompt, and no ownership distinction.
- [ ] Update `IMPROVEMENT_LOG`:
  - Close the delivered and superseded carry-list items: Portal identity, snapshot drop, per-call gate,
    `Take(6)`, the admission accounting and the outcome plumbing.
  - Add the PR 3 list from the spec, plus any live-report defects.
- [ ] Run `python -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30`. Expected: no errors.
- [ ] Commit `docs: document library_write`.

Task test command: `python -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30`
