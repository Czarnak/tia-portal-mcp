# Library types (`library_write` PR 3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add eight versioned STEP 7 library-type operations to `library_write`, name-first type and version
selectors (also in `library_read`), and apply `library_write` without a planning pass except for dry runs
and prompting calls.

**Architecture:** One opt-in pipeline hook (`PlansBeforeApply`) lets `LibraryWriteDomain` skip planning on
apply; each worker mutation resolves its own target and runs the tool-owned checks next to the native call.
Type operations live in a new worker class beside `LibraryWriter`; `create_type` converts a PLC object in
one project transaction, `create_type_version` writes content through `LibraryTypeVersionComposition.CreateFromDocuments`.

**Tech Stack:** C# (net10.0 host, net48 worker, netstandard2.0 contracts), Siemens Openness V21, xunit.

**Spec:** `docs/superpowers/specs/2026-11-07-library-types-design.md` (approved, revised after Task 0). Native
evidence: `docs/superpowers/acceptance/reports/2026-11-07-library-types-native-probes.md` (P cases) and
`2026-11-06-library-write-native-probes.md` (T cases).

## Global Constraints

- Contract versions stay: write `2.0`, read `1.0`. New members are additive and follow the worker-payload
  rules: required unless `[JsonIgnore(Condition = WhenWritingNull)]` and registered in
  `ConditionalMemberRegisterTests`.
- Operations: `create_type`, `create_type_version`, `release_type_version`, `set_default_type_version`,
  `synchronize_type`, `instantiate_type_version`, `delete_type`, `delete_type_version`. All are content
  operations: they never prompt, in any mode. `edit_type_version` does not exist.
- Supported type kinds: FB, FC, PLC UDT (`CodeBlockLibraryTypeVersion`, `PlcTypeLibraryTypeVersion`); others
  fail `target_kind_unsupported`.
- Release: `CreateOrReleaseDependenciesMode.DoNotAutomaticallyCreateOrReleaseDependencies` only. A given
  `versionNumber` has three numeric parts and is not `0.0.0` (host `validation_error`); no range cap.
- Never call `Versions.CreateFromDocuments` with a test environment argument. Never pass Openness a
  `DirectoryInfo` returned by `Directory.CreateDirectory`; use `new DirectoryInfo(fullPath)`.
- `create_type_version` content is SimaticML XML only; `expectedContentHash` is required and means the same
  as in `plc_write` (`ContentHashRules`).
- Tool-owned checks failing before any native change: `guard_blocked` with the guard id in the message;
  the content-hash mismatch is `state_changed`. Both are precondition refusals.
- Do not pre-check what Portal refuses (ADR-0012): no instance scan on apply for deletes, no in-work check
  for `create_type_version` (Portal refuses, T3).
- Never dispose `TiaPortalProcess`. Never write project or library names into repo files or commits.
- Commits are one-line conventional commits with no body or trailers.
- Use PowerShell for `dotnet` commands with `/p:` flags.
- Implementers run only their task's test command; the orchestrator runs the full suite.
- No README, AGENTS, ARCHITECTURE, ADR or `docs/SupportedOperations` edits before Task 9.

## Review Focus

- **`release_type_version` with the in-work version's own prefilled number passed explicitly** (for example
  `0.0.2` after one release): expect success, not `guard_blocked`; the check compares released versions only.
  Test in Task 5.
- **`create_type` on a PLC object that is already a type instance whose type has a different name** (P4c):
  expect `guard_blocked` `library_source_is_type_instance` and no change. Test in Task 5.
- **Apply call where item 3 references a type that item 1 creates:** expect it to resolve at run time on
  apply, and a dry run to show item 3 as "created by item 1". Test in Task 7.
- **`create_type_version` while the instance is uncompiled** (placed by a library operation): expect the
  fresh export to fail and the item to fail `guard_blocked` `library_state_unverifiable` with nothing
  changed. Test in Task 5.
- **Apply call where a later master-copy create collides by case only** (PR 2 behavior moved to apply):
  earlier items stay applied, the colliding item fails `guard_blocked` `library_master_copy_exists`, the
  call stops there. Test in Task 7.

---

## Execution notes and gates (orchestrator; not a task)

- Branch `feat/library-types` already holds the spec, the Task 0 probe and the report. Work in the main
  working copy. Do not push.
- After each task: `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true` (PowerShell),
  then `dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"`. After Task 3 also run
  `dotnet test TiaMcpServer.Tests --filter "Category=Packaging"`.
- **Live acceptance gate (maintainer, between Task 8 and Task 9):** spec section 12 cases on the installed
  local build, read-write plus one full-mode case, a disposable project with two PLCs and one disposable
  user global library, objects named `LT_*`. Needs explicit authorization. The report goes to
  `docs/superpowers/acceptance/reports/`. Wording marked "final after live acceptance" in spec section 10
  is settled from it.
- Before the PR merges, the final commit is `docs: prune working files` (AGENTS.md), after moving the
  probe source to `acceptance/reports/inputs/` (already done) and follow-ups to `docs/IMPROVEMENT_LOG.md`.

### Task 1: Planning hook and missing-plan tolerance in the pipeline

**Files:**
- Modify: `TiaMcpServer/Safety/Pipeline/IWriteDomain.cs`, `TiaMcpServer/Safety/Pipeline/WriteRun.cs`
- Test: `TiaMcpServer.Tests/Safety/Pipeline/WriteExecutionTests.cs`, `PipelineFakes.cs`

**Interfaces:**
- Produces: `bool IWriteDomain<…>.PlansBeforeApply(IReadOnlyList<TItem> items) => true;` (default member,
  documented: safe only for domains without acknowledge guards).
- `WriteRun.RunLeasedAsync` plans and evaluates guards only when `_call.DryRun`, or
  `_domain.PlansBeforeApply(Items)`, or the call will prompt (`ConfirmationMode` asks and
  `_domain.ConfirmsCall(Items)`). Otherwise it goes straight to `MutateAndVerifyAsync` with `_plans` all null.
- `RunItemAsync` and `ReplanAsync` treat a null `_plans[index]` as "no dependency"; `Effects()` and
  `AuditItem` already tolerate null.
- `MarkPartialWrite`: a failed item whose category satisfies `WorkerFailureCategories.IsPreconditionRefusal`
  and that carries no outcome counts as not mutated.

- [ ] Write failing tests (fake domain in `PipelineFakes.cs` with a `PlansBeforeApply` switch):
  `Apply_DomainOptsOut_DoesNotCallPlanAsync`, `DryRun_DomainOptsOut_StillPlans`,
  `PromptingCall_DomainOptsOut_StillPlans`, `Apply_NoPlan_ItemsRunInOrderAndStopAtFirstFailure`,
  `PartialWrite_PreconditionRefusalWithoutOutcome_NotCountedAsMutated`,
  `DefaultDomain_StillPlansOnApply` (existing domains unchanged).
- [ ] Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Safety.Pipeline"` — Expected: new tests FAIL.
- [ ] Implement the hook and the three `WriteRun` changes above.
- [ ] Run the same command — Expected: PASS.
- [ ] Commit: `feat: let write domains skip planning on apply`

Task test command: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Safety.Pipeline"`

### Task 2: Contracts for type operations and selectors

**Files:**
- Modify: `TiaMcpServer.Contracts/Library/LibraryWriteMethods.cs`, `LibraryWriteModels.cs`,
  `LibraryReadMethods.cs`, `LibraryReadModels.cs`, `TiaMcpServer.Contracts/Library/LibrarySelectorRules.cs`
- Test: `TiaMcpServer.Tests/Library/LibraryWriteMethodsTests.cs`, `LibraryReadCatalogTests.cs`,
  `TiaMcpServer.Tests/Json/ConditionalMemberRegisterTests.cs`

**Interfaces:**
- Produces:
  - `LibraryWriteMethods.TypeOperations` (the eight names), appended to `Operations`. Method names follow
    `ForItem`: `library_project_<op>` / `library_global_<op>`.
  - `TouchesProject` adds `create_type`, `create_type_version`, `release_type_version`,
    `instantiate_type_version` by name; the rest follow `Scope`/`SourceScope`.
  - `LibraryWriteRequestInfo` new members: `TypeName`, `TypeGuid`, `VersionNumber`, `VersionGuid`,
    `bool? InWork`, `Author`, `Comment`, `Content`, `ExpectedContentHash`, `bool? ReplaceInWork`.
  - `LibraryVersionRules.Check(string? versionNumber) -> string?` (three numeric parts, not `0.0.0`).
  - `LibraryWriteResultInfo` new conditional member `LibraryTypeResultInfo? Type` with `TypeName`,
    `TypeGuid`, `List<string> FolderPath`, `VersionNumber`, `VersionGuid`, `VersionState`,
    `string? DefaultBefore`, `string? DefaultAfter`, `bool? ReplacedInWork`, `List<string> InstancePaths`,
    `List<string> DeletedVersions`, `bool? Discarded`, `bool? TypeDeleted`.
  - `LibraryWriteOutcomeInfo` new conditional member `LibraryTypeResultInfo? Type` (evidence a failed item
    left, e.g. `create_type` link check after rollback).
  - `LibraryWritePlanInfo` new conditional members: `LibraryTypeResultInfo? Type`,
    `List<string> BlockingInstances`, `List<string> Dependencies`, `int? CreatedByItem`.
  - `LibraryQueryInfo` gains `TypeName`, `VersionNumber`; `list_versions`/`list_dependencies` accept
    `typeName` (+ `folderPath`) or `typeGuid`, and `versionNumber` or `versionGuid`.
- `Validate` closed members (every other member rejected):

| Operation | Required | Optional |
| --- | --- | --- |
| `create_type` | `plcName`, `plcKind` (`block`/`plcType`), `plcObjectName` | `plcFolderPath`, `folderPath` |
| `create_type_version` | `plcName`, `plcKind`, `plcObjectName`, `content`, `expectedContentHash` | `plcFolderPath`, `replaceInWork` |
| `release_type_version` | `scope:"project"`, type | `versionNumber`, `author`, `comment` |
| `set_default_type_version` | `scope`, type, version | library |
| `synchronize_type` | `sourceScope`, type, `scope` | both libraries, `folderPath` |
| `instantiate_type_version` | `sourceScope`, type, `plcName` | source library, version, `plcFolderPath` |
| `delete_type` | `scope`, type | library |
| `delete_type_version` | `scope`, type, version or `inWork:true` | library |

  Type = exactly one of `typeName` (optional `folderPath`) or `typeGuid`. Version = at most one of
  `versionNumber`, `versionGuid`, `inWork:true`.

- [ ] Write failing tests: `Validate_TypeOperation_RejectsMemberNotInOperation` (theory over the eight),
  `Validate_TypeSelector_RequiresExactlyOne`, `Validate_VersionSelector_AtMostOne`,
  `Validate_CreateTypeVersion_RequiresContentAndHash`, `VersionRules_Rejects000AndNonNumeric`,
  `VersionRules_Accepts100And010And001`, `TouchesProject_TypeOperations`, `OperationFor_MapsTypeMethods`,
  `ReadValidate_ListVersions_AcceptsTypeNameOrGuid`, and the new conditional members registered.
- [ ] Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Tests.Library|FullyQualifiedName~ConditionalMemberRegister"` — Expected: FAIL.
- [ ] Implement.
- [ ] Run the same command — Expected: PASS.
- [ ] Commit: `feat: add library type contracts`

Task test command: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Tests.Library|FullyQualifiedName~ConditionalMemberRegister"`

### Task 3: Reference stubs and test doubles

**Files:**
- Modify: `reference-stubs/Siemens.Engineering.Base/Library.cs`, `reference-stubs/Siemens.Engineering.Step7/`
  (the files declaring `PlcSoftware`, `PlcBlockComposition`, `PlcTypeComposition`, `PlcType`),
  `TiaMcpServer.Tests/TestSupport/LibrarySiemensDoubles.cs`
- Test: packaging tests (`Category=Packaging`), `scripts/verify-reference-stubs.ps1`

**Interfaces:**
- Produces (Base): `LibraryTypeComposition.CreateFromDocuments(DirectoryInfo, string, IEngineeringObject, LibraryImportOptions) -> TypeCreateTransferResults`
  (`CreatedType`, `TransferResultState`); `LibraryTypeVersionComposition.CreateFromDocuments(DirectoryInfo, string, CreateOptions, LibraryImportOptions) -> VersionCreateTransferResults`
  (`CreatedVersion`); `CreateOptions { None, Override }`; `LibraryTypeVersion.Release(CreateOrReleaseDependenciesMode, Version?, string?, string?)`,
  `SetAsDefault()`, `Discard()`, `Delete()`, `FindInstances(IInstanceSearchScope)`, `VersionNumber`, `State`,
  `IsDefault`, `Guid`, `TypeObject`; `LibraryType.Delete()`, `UpdateLibrary(ILibrary, DeleteUnusedVersionsMode, StructureConflictResolutionMode, ForceUpdateMode)`;
  `LibraryTypeInstanceInfo.LibraryTypeVersion`; the enums `CreateOrReleaseDependenciesMode`,
  `UpdatePathsMode`, `DeleteUnusedVersionsMode`, `StructureConflictResolutionMode`, `ForceUpdateMode`,
  `LibraryTypeVersionState`; `IInstanceSearchScope`.
- Produces (Step7): `CodeBlockLibraryTypeVersion`, `PlcTypeLibraryTypeVersion`,
  `PlcBlockComposition.CreateFrom(CodeBlockLibraryTypeVersion, UpdatePathsMode)`,
  `PlcTypeComposition.CreateFrom(PlcTypeLibraryTypeVersion, UpdatePathsMode)`, `PlcSoftware : IInstanceSearchScope`,
  `PlcType.Delete()`. Already present: `ExclusiveAccess.Transaction`, `ProjectBase : ITransactionSupport`,
  `PlcBlock.Export`/`Delete`, `PlcType.Export`.
- Doubles mirror each member with in-memory behavior the worker tests in Tasks 4–6 script (type tree,
  versions with state/number/default, instance links, transaction commit/rollback, `CreateFromDocuments`
  recording its arguments).

- [ ] Add a compile probe use of every new member to the stub probe; run
  `pwsh -NoProfile -File scripts/verify-reference-stubs.ps1` — Expected: FAIL (members missing).
- [ ] Add the stubs (placeholders throw `NotSupportedException`) and the doubles.
- [ ] Run the script and `dotnet test TiaMcpServer.Tests --filter "Category=Packaging"` — Expected: PASS.
- [ ] Commit: `chore: add library type reference stubs`

Task test command: `dotnet test TiaMcpServer.Tests --filter "Category=Packaging"`

### Task 4: Worker type and version resolution, `library_read` name selectors

**Files:**
- Create: `TiaMcpServer.OpennessWorker/Openness/Library/LibraryTypeTargets.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/Library/LibraryReader.cs` (`list_versions`,
  `list_dependencies` use the new resolver)
- Test: new `TiaMcpServer.Tests/Library/LibraryTypeTargetsTests.cs`, `LibraryReaderTests.cs`

**Interfaces:**
- Consumes: Task 2 request members, Task 3 doubles.
- Produces (`internal static class LibraryTypeTargets`):
  - `LibraryType Type(ILibrary library, string? typeName, List<string>? folderPath, string? typeGuid)` —
    enumerates the whole type tree (ordinal name match); none → `target_not_found`; several →
    `target_ambiguous` listing each candidate's folder path and GUID.
  - `LibraryTypeVersion Version(LibraryType type, string? versionNumber, string? versionGuid, bool? inWork)` —
    omitted → the default version; duplicates by number (T8) → `target_ambiguous`.
  - `LibraryTypeVersion InWork(LibraryType type)` — the single in-work version or `target_not_found`.
  - `(IEngineeringServiceProvider instance, LibraryTypeVersion version) Instance(ProjectBase project, LibraryWriteRequestInfo item)` —
    PLC object by PR 2's PLC members; not a type instance → `target_kind_unsupported`.
  - `string Kind(LibraryTypeVersion version)` — `codeBlock`/`plcType`, else `target_kind_unsupported`.
  - `List<string> FolderPath(LibraryType type)`; `string InstancePath(IEngineeringServiceProvider instance)`.

- [ ] Write failing tests: `Type_ByName_SearchesSubfolders`, `Type_DuplicateName_Ambiguous_ListsGuids`,
  `Type_FolderPathNarrows`, `Version_Omitted_IsDefault`, `Version_DuplicateNumber_Ambiguous`,
  `InWork_NoneIsTargetNotFound`, `Instance_PlainObject_KindUnsupported`, `Kind_HmiType_Unsupported`,
  `Read_ListVersions_ByTypeName`.
- [ ] Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~LibraryTypeTargets|FullyQualifiedName~LibraryReaderTests"` — Expected: FAIL.
- [ ] Implement.
- [ ] Run the same command — Expected: PASS.
- [ ] Commit: `feat: resolve library types and versions by name`

Task test command: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~LibraryTypeTargets|FullyQualifiedName~LibraryReaderTests"`

### Task 5: Worker conversion, version creation and release

**Files:**
- Create: `TiaMcpServer.OpennessWorker/Openness/Library/LibraryTypeWriter.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/Library/LibraryWriter.cs` (delegate type operations),
  `TiaMcpServer/Library/LibraryGuardDefinitions.cs` (new guard id constants)
- Test: new `TiaMcpServer.Tests/Library/LibraryTypeWriterTests.cs`

**Interfaces:**
- Consumes: `LibraryTypeTargets` (Task 4); `PlcWritePreconditions.RequireContentHash(string? expected, string format, string freshExport)`;
  the PLC read path's exporters (`BlockExporter`, `PlcTypeExporter`) for the fresh export.
- Produces: `internal static LibraryWriteResultInfo LibraryTypeWriter.Write(TiaPortal portal, ProjectBase project, string method, LibraryWriteRequestInfo item)`
  for all eight operations (this task: `create_type`, `create_type_version`, `release_type_version`).
  Guard ids (block, raised from the worker as `guard_blocked`): `LibraryGuardDefinitions.TypeNameExists =
  "library_type_name_exists"`, `SourceIsTypeInstance = "library_source_is_type_instance"`,
  `VersionNotHigher = "library_version_not_higher"`. The worker carries the id in the failure message.
- `create_type`: resolve PLC object and target type folder; checks in order: no type with that name in the
  project library (`library_type_name_exists`), object is not a type instance
  (`library_source_is_type_instance`); export to a unique folder under the worker temp root
  (export failure = nothing changed); `ExclusiveAccess` + `Transaction(project)`: delete original,
  `Types.CreateFromDocuments(dir, name, group, None)`, link check (instance links to `CreatedType`), commit;
  link failure → dispose without commit and fail `postcondition_failed` with outcome `Type`; delete the
  folder in `finally`.
- `create_type_version`: resolve instance and type; read whether an in-work version exists; fresh export
  of the instance (failure → `guard_blocked` `library_state_unverifiable`); `RequireContentHash`
  (`state_changed`); write `content` as `<instance name>.xml` in a unique folder;
  `type.Versions.CreateFromDocuments(new DirectoryInfo(dir), <instance name>, replaceInWork ? Override : None, None)`;
  result: created version number/GUID/state, `ReplacedInWork` from the read before, `InstancePaths` read back
  from the PLCs' instances linked to the created version; delete the folder in `finally`.
- `release_type_version`: resolve in-work; number = given or the in-work version's prefilled
  `VersionNumber`; require it strictly higher than every **released** version (`library_version_not_higher`); read
  default; `Release(DoNotAutomatically…, given-or-null, author, comment)`; result reads number, new GUID,
  `DefaultBefore`, `DefaultAfter`.

- [ ] Write failing tests: `CreateType_ConvertsInTransaction_LinksInstance`,
  `CreateType_NameExists_GuardBlocked_NoChange`, `CreateType_SourceIsTypeInstance_GuardBlocked`,
  `CreateType_LinkCheckFails_RollsBack_PostconditionFailedWithOutcome`, `CreateType_TempFolderDeleted`,
  `CreateTypeVersion_NoInWork_UsesNone`, `CreateTypeVersion_ReplaceInWork_UsesOverride_ReportsReplaced`,
  `CreateTypeVersion_NeverPassesTestEnvironment`, `CreateTypeVersion_PassesAbsoluteDirectoryInfo`,
  `CreateTypeVersion_StaleHash_StateChanged_NoNativeCall`, `CreateTypeVersion_ExportFails_StateUnverifiable`,
  `CreateTypeVersion_ReportsLinkedInstancesReadBack`, `Release_OwnPrefilledNumberGiven_Succeeds`,
  `Release_NumberOmitted_PassesNull_ChecksPrefilled`, `Release_EqualToReleased_GuardBlocked`,
  `Release_ReportsDefaultBeforeAfterAndNewGuid`, `Release_UsesDoNotAutomaticallyCreateOrRelease`.
- [ ] Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~LibraryTypeWriterTests"` — Expected: FAIL.
- [ ] Implement.
- [ ] Run the same command — Expected: PASS.
- [ ] Commit: `feat: add library type conversion, version creation and release`

Task test command: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~LibraryTypeWriterTests"`

### Task 6: Worker defaults, synchronize, instantiate, deletes; dispatch and capability

**Files:**
- Modify: `LibraryTypeWriter.cs`, `LibraryWriter.cs` (master-copy name check moves into
  `create_master_copy`/`copy_master_copy`), `TiaMcpServer.OpennessWorker/Program.cs` (dispatch unchanged in
  shape; type methods reach `WriteLibrary`), `ProjectCapabilityCatalog` mapping via
  `LibraryWriteMethods.OperationFor`
- Test: `LibraryTypeWriterTests.cs`, `LibraryWriterTests.cs`, `LibraryWorkerDispatchTests.cs`

**Interfaces:**
- Consumes: Tasks 4–5.
- `set_default_type_version`: read default; `SetAsDefault()`; `DefaultBefore`/`DefaultAfter`.
- `synchronize_type`: read target default; `type.UpdateLibrary(target, DoNotDelete, CancelIfStructureConflicts, SetOnlyHigherUpdatedVersionAsDefault)`;
  target type GUID, `DefaultBefore`/`DefaultAfter` of the target.
- `instantiate_type_version`: `Blocks.CreateFrom(CodeBlockLibraryTypeVersion, ThrowIfPathsConflict)` or
  `Types.CreateFrom(PlcTypeLibraryTypeVersion, ThrowIfPathsConflict)`; `InstancePaths` = root instance path.
- `delete_type`: `LibraryType.Delete()`; `TypeName`, `DeletedVersions`. `delete_type_version`: released →
  `Delete()`, in work → `Discard()`; `DeletedVersions` or `Discarded`, `TypeDeleted`. No instance scan.
- Master copies: `library_master_copy_exists` (OrdinalIgnoreCase among master copies) is checked in the
  mutation call before `Create`/`CopyTo` and fails `guard_blocked`.

- [ ] Write failing tests: `SetDefault_ReportsBeforeAfter`, `Synchronize_UsesPinnedModes_ReportsTargetDefaults`,
  `Instantiate_CodeBlockAndPlcType`, `DeleteType_NoInstanceScan_NativeRefusalIsEngineeringRejected`,
  `DeleteVersion_InWork_Discards`, `DeleteVersion_LastVersion_ReportsTypeDeleted`,
  `CreateMasterCopy_CaseCollision_GuardBlockedAtMutation`, `Dispatch_EveryTypeMethodReachesWriter`,
  `Capability_ProjectAndGlobalTypeMethods`.
- [ ] Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~LibraryTypeWriterTests|FullyQualifiedName~LibraryWriterTests|FullyQualifiedName~LibraryWorkerDispatch"` — Expected: FAIL.
- [ ] Implement.
- [ ] Run the same command — Expected: PASS.
- [ ] Commit: `feat: add library type defaults, sync, instantiate and deletes`

Task test command: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~LibraryTypeWriterTests|FullyQualifiedName~LibraryWriterTests|FullyQualifiedName~LibraryWorkerDispatch"`

### Task 7: Plans for dry runs, host domain and verification

**Files:**
- Modify: `TiaMcpServer.OpennessWorker/Openness/Library/LibraryWritePlanReader.cs`,
  `TiaMcpServer/Library/LibraryWritePlanner.cs`, `LibraryWriteDomain.cs`, `LibraryVerificationPolicy.cs`,
  `LibraryWriteCatalog.cs`, `LibraryWritePayloadContract.cs`, `LibraryWritePayloadBudget.cs`,
  `LibraryWriteTools.cs` (descriptions), `LibraryGuardDefinitions.cs`
- Test: `LibraryWritePlanReaderTests.cs`, `LibraryWritePlannerTests.cs`, `LibraryGuardedWriteDomainTests.cs`,
  `LibraryVerificationPolicyTests.cs`, `LibraryWriteContractDecodeTests.cs`, `LibraryWriteBudgetTests.cs`

**Interfaces:**
- Consumes: Task 1 hook; Task 2 plan members; Tasks 4–6 worker results.
- Worker plan per type operation fills `LibraryWritePlanInfo.Type` with the "Dry run shows" evidence of spec
  section 7 (`create_type_version`: in-work version that would be replaced, the named instance, and a note
  when instances exist in more than one PLC; deletes: `BlockingInstances` from PLC instances and
  `MasterCopiesContainingInstances`; release: prefilled number and expected default move; instantiate and
  synchronize: `Dependencies`). Unreadable evidence → `library_state_unverifiable`.
- `LibraryWritePlanner.PlanAsync`: an unresolved item whose target is a type, folder or master copy that an
  earlier item in the call creates (same operation family and name) gets a plan with `CreatedByItem = N`;
  any other unresolved target still fails the call.
- `LibraryWriteDomain.PlansBeforeApply(items) => false`; `_items` set at the start of the run (not only in
  `PlanAsync`); it keeps each item's resolved identity from its result, or its outcome on failure.
- `LibraryVerificationPolicy.ObjectKeys` builds keys from resolved identities (library root path, folder
  kind and path, master-copy name, type GUID, version GUID); an attempted item with no outcome falls back to
  a library-level key from its selector and conservatively supersedes earlier items on that library.
- `LibraryWritePayloadContract.ValidateEnvelopeOutcome` accepts the new `Type` outcome member and rejects
  malformed ones (`protocol_error`). `library_deletes_folder_contents` becomes dry-run information only.

- [ ] Write failing tests: `Plan_DeleteType_ListsBlockingInstancesAndMasterCopies`,
  `Plan_CreateTypeVersion_ShowsReplacedInWorkAndMultiPlcNote`, `Planner_TargetCreatedByEarlierItem_CreatedByItem`,
  `Planner_UnrelatedMissingTarget_FailsCall`, `Domain_Apply_SkipsPlanning`, `Domain_DryRun_Plans`,
  `Domain_ReadWriteLifecycleItem_PlansAndPrompts`, `Apply_LaterItemUsesTypeCreatedByEarlierItem`,
  `Apply_MasterCopyCollisionAfterEarlierItems_StopsWithGuardBlocked`, `Verification_KeysFromResolvedIdentity`,
  `Verification_AttemptedWithoutOutcome_SupersedesLibrary`, `Decode_TypeOutcome_Malformed_ProtocolError`,
  `Budget_GuardCountIncludesNewGuard`.
- [ ] Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Tests.Library"` — Expected: FAIL.
- [ ] Implement.
- [ ] Run the same command — Expected: PASS.
- [ ] Commit: `feat: apply library writes without planning`

Task test command: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Tests.Library"`

### Task 8: FakeWorker scenario and MCP protocol tests

**Files:**
- Modify: `TiaMcpServer.FakeWorker/Library/LibraryWriteScenario.cs` (operation filter, type scripts)
- Test: `TiaMcpServer.Tests/Library/LibraryWriteMcpTests.cs`, `LibraryWriteCatalogRegistrationTests.cs`

**Interfaces:**
- Consumes: everything above through the real host and the FakeWorker over stdio.

- [ ] Write failing tests: `Mcp_CreateTypeThenRelease_OneCall_TwoItemsApplied`,
  `Mcp_TypeOperations_NeverPrompt_ReadWrite`, `Mcp_DryRun_ShowsCreatedByItem`,
  `Mcp_CreateTypeVersion_StaleHash_StateChanged`, `Mcp_ApplyResponse_HasNoEffects`,
  `Mcp_ToolDescription_ListsTwentyOperations`, `Registration_CountAssertionsUpdated`.
- [ ] Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~LibraryWriteMcp|FullyQualifiedName~LibraryWriteCatalogRegistration"` — Expected: FAIL.
- [ ] Implement the scenario scripts.
- [ ] Run the same command — Expected: PASS.
- [ ] Commit: `test: cover library type operations over MCP`

Task test command: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~LibraryWriteMcp|FullyQualifiedName~LibraryWriteCatalogRegistration"`

### Task 9: Documentation (after live acceptance)

**Files:**
- Create: `docs/adr/00NN-library-writes-apply-without-planning.md` (next free number; links both probe
  reports and the live report)
- Modify: `docs/SupportedOperations/LIBRARY_OPERATIONS_SUMMARY.md`, `docs/ARCHITECTURE.md`, `AGENTS.md`
  (library bullets, transaction, `create_type_version`), `README.md` (absolute links), `docs/IMPROVEMENT_LOG.md`,
  `docs/roadmap/json-contract.md`, `docs/adr/README.md`

**Interfaces:**
- Consumes: the live acceptance report; spec sections 9 and 10 list every statement and follow-up.
- Docs state verbatim the spec section 10 bullets, with the wording marked "final after live acceptance"
  settled from the live report. IMPROVEMENT_LOG adds: other write tools adopt the hook, batched planning,
  optional `keepDefault`, mandatory dry run before deletes, the `plc_write` type-instance hazard, the
  unknown release-number range.

- [ ] Write the ADR and doc changes.
- [ ] Run: `python -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30` — Expected: `0 error(s)`.
- [ ] Run: `git grep -n "edit_type_version" -- docs README.md AGENTS.md ':!docs/superpowers'` — Expected: no output.
- [ ] Commit: `docs: document library type operations`

Task test command: `python -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30`
