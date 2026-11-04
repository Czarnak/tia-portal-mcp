# Library Read (PR 1 of 2) — Plan

> **Status:** Decisions D1–D9 confirmed 4 November 2026; plan awaits approval to execute. Authorizes nothing: no implementation, remote write, or live TIA call until the user approves this plan and, separately, the live task.
>
> **For agentic workers:** Execute task by task with `superpowers:subagent-driven-development` or `superpowers:executing-plans`. Each task brief is self-contained; read the Global constraints block too.

**Goal:** Ship `library_read` — the read-only half of issue #218 — adapted to current `main`, in one PR. `library_write` is PR 2 with its own plan, written only after native-rejection probes (see "Carried to the write plan").

**Architecture:** One new read tool on the shared batch-read path (`BatchReadTool`, `StructuredOperationBatchPayloadBudget`, `OperationBatchReadResponse`). Every call needs a verified binding. Every item, global or project library, takes the bound-project route (`SendBoundProjectRequestAsync` → worker `WithActiveContext`, which gives both the bound Portal and the bound `ProjectBase` after the identity check). Paged operations issue host-sealed snapshot cursors like `hmi_read`. The worker reader ports from the contribution with fixes; it never opens, retrieves, saves, closes, or mutates.

**Tech stack:** unchanged (.NET 10 host, netstandard2.0 contracts, net48 worker, xUnit, TIA Portal V21 Openness). No new dependencies.

**Inputs:** issue #218; contribution ZIP SHA256 `bb28077c…45dd8`, patch SHA256 `3d4b0aae…a7b4` (v4.2.0 base `cfdda597`), applies textually only to its base; on `main` @`0f35a77` it fails in README.md, `WorkerRequest.cs`, the test csproj, `OpennessWorkerClient.cs` and `docs/README.md`. Contributor reports 129 focused / 406 delivery / 39 packaging checks (VALIDATION.md); the earlier reviewer replay on the old base gave 13 library / 411 selected / 39 packaging. Neither qualifies this branch, and no live read acceptance exists.

## Global constraints

- Port selectively; never apply the whole patch. Patch line refs below (`P:n`) point into `tia-portal-mcp-4.2.0-library.patch`.
- Read-only means read-only: no Open/Retrieve/Save/Close/Create/Delete/setter in any library read path, in any access mode.
- Reuse shared seams; no parallel response type, budget engine or dispatcher. Cursors reuse `AuthenticatedCursorProtector`, `ProjectBindingCursorState` and the existing cursor failure categories; the only new cursor code is a per-domain codec (precedent: tree, hardware, HMI) and an owner-keyed generalization of `HmiSnapshotStore`, not a second store.
- Typed worker payloads decode through `CanonicalJson.NormalizeWorkerPayload<T>`; a decode failure is `protocol_error` and is never echoed. New contracts write explicit nulls; no conditional members are planned (if one is added, register it in `ConditionalMemberRegisterTests`).
- Budgets read the effective `StructuredOperationBatchPayloadBudget.MaxItemChars`/`MaxDocumentChars`, never literals.
- `ProjectCapabilityCatalog.OperationFor` stays the only worker-method→capability map.
- Siemens DLLs never enter the repo or package; reference stubs carry only the read subset.
- Never write TIA project or library names/paths from real fixtures into repo files or commits.
- Build/test serially (`-m:1`). Use PowerShell for `dotnet … /p:` commands. Implementers run only their task test command; the orchestrator runs full suites (Task 5).
- Commit after each task once build + task tests pass: one-line conventional message, no body. No push, PR, or merge without explicit approval.
- Docs (README, AGENTS, `docs/`) change only in Task 7, after live acceptance.

## Decisions (confirmed 4 November 2026)

| # | Decision | Outcome |
| --- | --- | --- |
| D1 | PR split | PR 1 = `library_read` only (this plan). PR 2 = `library_write`, planned after native-rejection probes. |
| D2 | Public arguments | `library_read(operations[])` only. No `portalProcessId`: a verified binding already pins the Portal. `ReadBatchSchemas_ExposeOnlyOperations` stays unchanged. Unbound global reads (no project open) are a logged follow-up. |
| D3 | Item shape | Flat items like `hmi_read`/`vci_read` (and GitHub MCP's `pull_request_read`): `{operationId, operation, scope?, libraryPath?, libraryName?, folderKind?, folderPath?, typeGuid?, versionGuid?, direction?, offset?, limit?, cursor?}`. Each optional field's description names the operations that use it; cross-field rules live in `LibraryReadMethods.Validate`. The host maps an item to the worker-only `LibraryQueryInfo`. |
| D4 | Response | Shared `OperationBatchReadResponse`, `contractVersion "1.0"`; drop the contributed `LibraryReadResponse` (required by the shared-seam rule). |
| D5 | Binding rule | Every call needs a verified binding, all-global batches included; unbound → whole-call `binding_conflict`, like every other content read. Global reads use the bound Portal. No `BatchReadTool` change. |
| D6 | Containers | Global-library reads: standalone and local session (capability `library_read`). Project-library reads: standalone only (capability `library_project_read`), like VCI. Local-session project-library reads are a follow-up after a separate probe. |
| D7 | Paging | Snapshot cursors like `hmi_read`: `offset` ≥ 0, `limit` 1–200 (default 100), optional `cursor` (not valid with `offset`), page `{offset, nextOffset, nextCursor, complete}`. The first page of any paged scan with more rows freezes the ordered row set in a worker snapshot (at most 4 library snapshots, 10-minute idle lifetime); the host seals the snapshot id, operation, query hash, offset and binding state into `nextCursor` and never exposes the raw id. Failures: `invalid_cursor`, `cursor_filter_mismatch`, `cursor_binding_mismatch`, plus HMI's snapshot-mismatch error when the snapshot is gone. |
| D8 | Dispatch | All library methods use the bound route (`SendBoundProjectRequestAsync` → `WithActiveContext`). No `InspectPortalAsync` change; do not port `LibraryPortalReadDispatch`; `MultiuserPortalReadDispatch` is untouched. |
| D9 | ADR | One short ADR: content tools stay gated on the verified binding, and may read Portal-scope objects of the bound Portal (global libraries). Refines ADR-0007/0011's "content tools act only on the bound project". |

Counts after this PR: 9 tools read-only, 20 read-write, 20 full.

## Read surface (contract 1.0)

| Operation | Scope | Selectors | Paging |
| --- | --- | --- | --- |
| `list_global_libraries` | global (bound Portal) | none | yes |
| `inspect_library` | `project` \| `global` | library | no |
| `list_children` | both | library + `folderKind` (`types`\|`masterCopies`) + optional `folderPath` | yes |
| `list_versions` | both | library + `typeGuid` + optional `folderPath` | yes |
| `list_dependencies` | both | library + `typeGuid` + `versionGuid` + `direction` (`dependencies` default \| `dependents`) + optional `folderPath` | yes |

- Neither version operation accepts `folderKind`; the type lookup is non-recursive within the selected type folder.
- Project scope: no `libraryPath`/`libraryName`; identity comes from the binding. Global scope: non-blank `libraryPath` (absolute drive or UNC ≥ 2 segments) or `libraryName`; both given must both match. `folderPath` ≤ 32 non-blank literal segments.
- Global libraries are found only among those already open in the bound Portal: exact ordinal name, or `Path.GetFullPath` compared case-insensitively. Missing → `target_not_found`; multiple → `target_ambiguous`.
- Cursor query hash covers the operation and every selector; it excludes `offset`, `limit` and `cursor`. A cursor continues the frozen row set; property values are read live per page.
- Worker methods: `ProjectLibraryReads` = `library_project_{inspect_library,list_children,list_versions,list_dependencies}`; `GlobalLibraryReads` = `library_list_global_libraries` + `library_global_{same four}`. All `Observe`, all need a bound project and the expected session identity.
- Payload: every operation returns worker `LibraryPageInfo {items, offset, nextOffset, snapshotId, complete, diagnostics}` of `LibraryEntryInfo {identity, namespace, versionNumber, state, isDefault, isReadOnly, isWriteProtected, isModified, nativeType, diagnostics}` (P:256-314; `snapshotId` is new, written as explicit null when no snapshot). The host projects it to public `LibraryPageResult {items, offset, nextOffset, nextCursor, complete, diagnostics}`. An unknown flag is null with a diagnostic, never false.
- Errors: `validation_error` (shape), `binding_conflict`, `target_not_found`/`target_ambiguous`, `unsupported_capability`, `invalid_cursor`/`cursor_filter_mismatch`/`cursor_binding_mismatch`, `protocol_error`, `worker_operation_failed`, `portal_lost`.
- The worker raises `binding_conflict` only for identity or Portal-attachment loss, because the host invalidates a verified binding on any worker-reported `binding_conflict`. A missing project library (or any other library-level miss) uses `unsupported_capability` or `target_not_found`.

## Carried to the write plan (PR 2 input, not in scope here)

These review findings must reach the write plan; Task 7 records them in `docs/IMPROVEMENT_LOG.md` because this file is pruned. Native-rejection probes run before PR 2 is planned.

- Portal identity is not part of `WriteBindingPreparation`, the `WriteRun` lease, or confirmation rechecks (all compare `ProjectBindingSnapshot` only).
- `WriteRun` gates on the tool name (`WriteRun.cs:136-141`). Pick a per-call seam aligned with the host (`Invocation.cs:168-174`) and worker (`Program.cs:612-616`) per-method gates, or keep it fail-closed for v1.
- Lifecycle: the contribution picks the lifecycle ops by position (`Take(6)`) and classifies them `ProjectMutation`. The Portal write route also runs `Execute` without the mutates flag. Name the set, its capability, and per-operation `ConfirmsEveryCall`.
- `release_type_version` and `synchronize_type` do multi-step default changes with no transaction or receipt.
- Mixed guards: `library_collision`, `library_state_unverifiable`, `library_unsaved_changes` (8 uses, 5 are release preconditions), and `library_read_only` (also covers access-mode mismatch).
- Admission accounting: the contributed `Validate` skips `WriteValidation.Compose(MeasureProtectedCore…)`.
- An oversized item `result` is omitted whole; commit and partial-move facts need a compact rewrite.
- Typed outcome plumbing touches ~15 copy sites. `PersistentWorkerTransport` validates only VCI/Network outcomes.
- All four validation helpers reflect into `LibraryWriter.Verify` and use `Verified` as oracle. The probe convention is `docs/superpowers/acceptance/reports/inputs/<date>-<topic>-native-probe/`.
- Remove the `verify` RPC and `LibraryWriteVerification`; stage approved XML (sidecar race); one operation per call versus the 1–50 convention.
- Library mutations must drop library read snapshots for the touched library, as HMI mutations do (`HmiSnapshotStore.DropFor`).

## After the tasks (orchestrator gates)

- Task 5 runs the full CI-equivalent qualification; implementers never run full suites.
- Task 6 needs explicit user authorization naming the disposable fixtures; stop and ask before it.
- Task 7 starts only after Task 6's report exists. Task 8 runs last, before merge.

### Task 0 — Branch and baseline

**Files:** none in repo except carrying this plan. **Produces:** this plan committed on `feat/library-read`, recorded baseline.

- [ ] Confirm the main working copy is on `feat/library-read` (created 4 November 2026 from `main` @`0f35a77`, no worktree), clean except this untracked plan. Commit `docs: add library read plan`.
- [ ] Run: `dotnet build TiaMcpServer.slnx -m:1 -c Release /p:UseTiaPortalReferenceStubs=true` — Expected: success.
- [ ] Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build -c Release --filter "FullyQualifiedName~McpToolSchemaTests|FullyQualifiedName~AccessModeDiscoveryTests|FullyQualifiedName~ReadOnlyModeTests"` — Expected: all pass; record counts as baseline (8 read-only / 19 writable tools).
- [ ] Extract the contribution to a scratch directory and verify both SHA256 values above; never execute its content.

**Task test command:** the `dotnet test` line above.

### Task 1 — Contracts and catalogs

**Files:** create `TiaMcpServer.Contracts/Library/LibraryReadMethods.cs`, `LibraryReadModels.cs`; modify `TiaMcpServer.Contracts/Worker/WorkerRequest.cs`, `Safety/OperationPolicyCatalog.cs`, `Project/ProjectCapabilityCatalog.cs`; tests `TiaMcpServer.Tests/Library/LibraryReadCatalogTests.cs`, `Multiuser/Snapshots/capabilities.json`, `Multiuser/LocalSessionContextContractTests.cs`.

**Interfaces — Produces:**
- `static class LibraryReadMethods { string[] Operations; string[] ProjectLibraryReads; string[] GlobalLibraryReads; string[] All; string ForOperation(string op, string? scope); string? OperationFor(string method); string? Validate(string op, LibraryQueryInfo q); bool IsAbsolutePath(string p); }`. Port P:185-255, reads only. `Validate` returns an error message or null; `OperationValidation` is a host type, so the host wraps the result.
- `LibraryQueryInfo` (adds worker-only `SnapshotId`), `LibraryIdentityInfo`, `LibraryEntryInfo`, `LibraryPageInfo` (adds `SnapshotId`) (P:256-314; `[JsonUnmappedMemberHandling(Disallow)]` on the query).
- `WorkerRequest.LibraryQuery : LibraryQueryInfo?` only (no write fields).
- Policy: every `LibraryReadMethods.All` → `Observe`. Capability: `GlobalLibraryReads` → `"library_read"` on standalone and local session; `ProjectLibraryReads` → `"library_project_read"` in a standalone-only array (D6).

- [ ] Write failing tests in `LibraryReadCatalogTests`:
  - `Methods_AreUniqueAndPartitioned` — `All` = `ProjectLibraryReads` ∪ `GlobalLibraryReads`, no overlap; `ForOperation`/`OperationFor` round-trip.
  - `AllMethods_AllowedInEveryAccessMode` — including read-only.
  - `AllMethods_NeedBindingAndIdentity` — via `RequiresBoundProject` and `RequiresExpectedSessionIdentity`.
  - `GlobalReads_StandaloneAndLocalSession` / `ProjectLibraryReads_StandaloneOnly` — supported containers as in D6, every other container kind rejected.
  - `Validate_*` cases — selector rules in the Read surface table: project scope rejects path/name; global needs one; relative path rejected; UNC ≥ 2 segments; 33 `folderPath` segments rejected; limit 0/201 rejected; `list_global_libraries` rejects any non-paging field.
- [ ] Add `library_read` and `library_project_read` rows to `capabilities.json` and update `LocalSessionContextContractTests` counts.
- [ ] Implement until green.

Run: `dotnet build TiaMcpServer.slnx -m:1 -c Release /p:UseTiaPortalReferenceStubs=true` then the task test command — Expected: pass.

**Task test command:** `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build -c Release --filter "FullyQualifiedName~TiaMcpServer.Tests.Library|FullyQualifiedName~ReadOnlyModeTests|FullyQualifiedName~LocalSessionContextContractTests"`

Commit: `feat: add library read contracts`

### Task 2 — Worker reader and snapshots

**Files:**
- Create `TiaMcpServer.OpennessWorker/Openness/Library/LibraryReader.cs`.
- Modify:
  - `Openness/Hmi/HmiSnapshotStore.cs` (owner-keyed generalization; move the generic core to a shared worker file if that is cleaner);
  - `Infrastructure/OpennessExceptions.cs`;
  - `Openness/Hmi/HmiReadLog.cs`;
  - worker `Program.cs`;
  - `TiaMcpServer.FakeWorker/Program.cs`.
- Stubs: `reference-stubs/Siemens.Engineering.Base/Core.cs`, new `reference-stubs/Siemens.Engineering.Base/Library.cs`, regenerated `ref/`.
- Tests:
  - `TiaMcpServer.Tests/TestSupport/LibrarySiemensDoubles.cs`;
  - `Library/LibraryReaderTests.cs`;
  - `Library/LibraryWorkerDispatchTests.cs`;
  - existing HMI snapshot-store tests (must stay green unchanged);
  - `Worker/FakeWorkerIdentityEnforcementTests.cs`;
  - test csproj `<Compile Include>` links for `Openness\Library\LibraryReader.cs` and any extracted store file.

**Interfaces:**
- Consumes: Task 1 contracts.
- Produces: `static LibraryPageInfo LibraryReader.Read(TiaPortal portal, ProjectBase project, string method, LibraryQueryInfo query, <library snapshot store>)`. Internal `Resolve`, `Exact<T>`, `TypeFolder`, `CopyFolder`, and `Owner` stay `internal` for PR 2 reuse.
- Snapshot store: `HmiSnapshotStore` keys entries by an owner object instead of `HmiSoftware`; HMI and library each get their own `Shared` instance (capacity 4, 10-minute idle) so neither evicts the other. Library entries are owned by (bound project, resolved library, operation). HMI behavior and its tests stay unchanged; if the generalization cannot keep HMI behavior identical, stop and report.
- Dispatch arm: every `LibraryReadMethods.All` → `WithActiveContext(request, session => Success(LibraryReader.Read(...)))` with the Portal and `ProjectBase` from the active context. A null `LibraryQuery` throws `validation_error`.
- Paging: a paged scan with more rows puts the ordered native objects in the store and returns `snapshotId`; a request with `SnapshotId` takes the snapshot (project and operation must match, mismatch uses the HMI snapshot error) and reads the requested slice; the last page returns no `snapshotId`.

Port fixes against P:1477-1652:
- `Optional()` swallows only `EngineeringTargetInvocationException` and `EngineeringNotSupportedException`.
  - Add a new `OpennessExceptions.IsRecoverablePropertyFailure(Exception)` with exactly that rule, and make `HmiReadLog.IsRecoverable` forward to it.
  - The existing broad `OpennessExceptions.IsRecoverable` (not `NonRecoverableException`) stays unchanged; Multiuser and Network depend on it.
  - Disposed and all other exceptions escape so `WorkerExecution` can classify `portal_lost`.
- A null `ProjectLibrary` fails `unsupported_capability`, not the patch's `binding_conflict`.
- `Children` reuses `CopyFolder` instead of repeating its walk.
- `TypeEntry` hoists only the owner library's `LibraryEntry`/`Optional` reads, cached by owner library. The `Owner()` walk stays per row, because dependency rows sit in different folders.
- Stubs carry the read subset only:
  - `ProjectBase.ProjectLibrary`, `TiaPortal.GlobalLibraries`;
  - `ILibrary`, `GlobalLibrary`, `GlobalLibraryComposition`, `ProjectLibrary`;
  - `MasterCopy`, `MasterCopyFolder`, `MasterCopySystemFolder`, `MasterCopyUserFolder` and their user-folder/master-copy compositions;
  - `LibraryTypeFolder`, `LibraryTypeSystemFolder`, `LibraryTypeUserFolder` and their user-folder/type compositions;
  - `LibraryType` and `LibraryTypeVersion` with their compositions, plus `LibraryTypeVersionAssociation`;
  - enums `LibraryTypeVersionState`, `ConsistencyStatus`;
  - the `Parent` members used by `Owner`.

  Add only the read getters and enumerators, plus the `IEngineeringObject` members the interface requires. No Step7 stub changes.

- [ ] Write failing `LibraryReaderTests` against `LibrarySiemensDoubles`:
  - `Global_ExactNameMatch_IsOrdinal`.
  - `Global_PathMatch_IsCanonicalCaseInsensitive`.
  - `Global_NameAndPathMustBothMatch`.
  - `Global_NotOpen_IsTargetNotFound` and `Duplicate_IsTargetAmbiguous`.
  - `Project_NoProjectLibrary_IsUnsupportedCapability`.
  - `FolderPath_SegmentIsLiteral`.
  - `Paging_LookAheadSetsNextOffset` and `Paging_LastPageComplete_NoSnapshot`.
  - `Snapshot_ContinuationReadsFrozenOrder` (rows added after page 1 do not appear).
  - `Snapshot_OtherProjectOrOperation_Rejected` and `Snapshot_Evicted_Rejected`.
  - `Snapshot_LibraryAndHmiStoresIndependent`.
  - `Optional_RecoverableEngineeringFailure_NullWithDiagnostic`.
  - `Optional_DisposedOrFatal_Escapes`.
  - `Owner_DepthCapFails`.
  - `Reader_CallsNoMutatingMember` (doubles throw on any mutator).
- [ ] Add `LibraryReadWithoutQuery_RejectsWithoutCrashingWorker` to `FakeWorkerIdentityEnforcementTests`.
- [ ] Add source-pinning `LibraryWorkerDispatchTests`, following the precedent of `NetworkSubnetLifecycleWorkerDispatchTests`:
  - every library method routes through `WithActiveContext`;
  - a read with a null `LibraryQuery` throws `validation_error`.
- [ ] Add the FakeWorker library read branch, keyed on `LibraryReadMethods.All` and placed after the identity check, before the scenario switch. It returns a fixture entry for global and project methods, a two-page fixture with `snapshotId` on page 1; `Missing` → `target_not_found`; `Malformed` → bad payload.
- [ ] Update stubs. Run: `pwsh -NoProfile -File scripts/verify-reference-stubs.ps1 -Configuration Release -Update`, inspect the `ref/` diff, then rerun without `-Update` — Expected: verified, no further diff.

Run:
- stub build, then the task test command and `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build -c Release --filter "Category=Packaging"` — Expected: pass.
- `dotnet build TiaMcpServer.slnx -m:1 -c Release /p:UseTiaPortalReferenceStubs=false` — Expected: success. This catches stub/V21 member mismatches early; it needs the installed V21 DLLs, not a running Portal.

**Task test command:** `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build -c Release --filter "FullyQualifiedName~TiaMcpServer.Tests.Library|FullyQualifiedName~TiaMcpServer.Tests.Hmi|FullyQualifiedName~FakeWorkerIdentityEnforcementTests"`

Commit: `feat: add library reader to worker`

### Task 3 — Host client route and cursors

**Files:** modify `TiaMcpServer/Worker/OpennessWorkerClient.Domains.cs`, the DI registration that creates `HmiPageCursorCodec`; create `TiaMcpServer/Library/LibraryPageCursorCodec.cs`, `LibraryWorkerInvoker.cs`; tests `TiaMcpServer.Tests/Library/LibraryReadClientTests.cs`, `Library/LibraryPageCursorCodecTests.cs`.

**Interfaces:**
- Consumes: Task 1 contracts and the Task 2 FakeWorker branch.
- Produces: `Task<WorkerCallResult> ReadLibraryAsync(string method, LibraryQueryInfo query, CancellationToken ct)` → `SendBoundProjectRequestAsync(method, null, r => r.LibraryQuery = query, "{}")`, like `ReadVciAsync`.
- Produces: `LibraryPageCursorCodec` over the shared `AuthenticatedCursorProtector` (`CursorKind` `"library-page"`, category `invalid_cursor`), state `{SnapshotId, Operation, QueryHash, Offset, HostBinding}`, `QueryHash(item)` per the Read surface rule. Mirrors `HmiPageCursorCodec`.
- Produces: `LibraryWorkerInvoker.InvokeReadAsync(client, codec, item, ct)`: decodes a cursor and checks operation, query hash and binding before sending (`invalid_cursor`/`cursor_filter_mismatch`/`cursor_binding_mismatch`, all NotSent), then sends `SnapshotId` + offset. Mirrors `HmiWorkerInvoker.ResolveCursor`.

- [ ] Write failing tests:
  - `Unbound_BindingConflictNotSent` (global and project methods).
  - `ConfiguredUnverifiedProject_GroundedFirst` (same as other content reads).
  - `GlobalRead_StandaloneBound_SendsExpectedIdentity`.
  - `GlobalRead_LocalSessionBound_Succeeds`.
  - `ProjectRead_LocalSession_UnsupportedCapabilityNotSent`.
  - Codec: `Tampered_OtherKey_InvalidCursor`, `OtherOperationOrSelectors_FilterMismatch`, `BindingRevisionChanged_BindingMismatch`, `QueryHash_ExcludesPaging`.
- [ ] Implement until green.

Run: stub build, then the task test command — Expected: pass.

**Task test command:** `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build -c Release --filter "FullyQualifiedName~TiaMcpServer.Tests.Library"`

Commit: `feat: route library reads through the bound project`

### Task 4 — `library_read` tool and registration

**Files:** create `TiaMcpServer/Library/LibraryReadTools.cs`, `LibraryReadCatalog.cs` (validation, item→query mapping), `LibraryPayloadContract.cs`; modify `Tools/McpToolRegistration.cs`, test csproj (host `Library\*.cs` glob); tests `Library/LibraryReadToolsTests.cs`, `Library/LibraryReadBudgetTests.cs`, `Tools/ToolOutputContractConformanceTests.cs`, `AccessModeDiscoveryTests.cs`, `McpToolSchemaTests.cs`, `StandaloneToolsProtocolTests.cs`, `WriteToolMcpAnnotationProtocolTests.cs`, `Tools/Snapshots/output-schemas.json`.

**Interfaces:**
- Consumes: `ReadLibraryAsync`, `LibraryPageCursorCodec`, `LibraryWorkerInvoker` (Task 3) and the contracts (Task 1).
- `LibraryReadTools.LibraryRead(OpennessWorkerClient, LibraryPageCursorCodec, LibraryReadItem[] operations, CancellationToken)` on `BatchReadTool.RunAsync` unchanged, attributes `ReadOnly=true, Destructive=false, OpenWorld=false, UseStructuredContent=true`, output `OperationBatchReadResponse`, `contractVersion "1.0"`.
- `LibraryReadItem` field descriptions name the operations that use each optional field (D3).
- `LibraryPayloadContract.Project(item, WorkerCallResult, cursorIssuer)`: typed `LibraryPageInfo` decode, then project to public `LibraryPageResult`: drop `snapshotId`, seal `nextCursor` while more rows exist. Follows `VciPayloadContract` for decoding and `HmiPayloadContract.IssueNextCursor` for the cursor.
- Registration: `WithClosedTools(..., closeNestedSchemas: true)` and the always-registered read list.

- [ ] Write failing tests:
  - `Unbound_AnyBatch_WholeCallBindingConflict` (an all-global batch too).
  - `ItemFailures_DoNotHideLaterItems`.
  - `MalformedPayload_ProtocolError_NotEchoed`.
  - `UnknownOrMiscasedField_ValidationErrorBeforeDispatch`.
  - `CursorWithOffset_ValidationError`.
  - `Cursor_RoundTripAcrossPages_NoSnapshotIdInResponse`.
  - `Count0And51_Rejected`, `DuplicateOrOverlongId_Rejected`.
  - `ReadOnlyMode_Available_NoMutationMethodSent` (FakeWorker request log).
  - `Budget_OversizedItemOmitted_AtDefaultAndRaisedLimits`.
  - `TextEqualsStructuredContent`.
  - `ItemFieldDescriptions_NameTheirOperations`.
- [ ] Update conformance probes (rejected: empty operations; succeeded: `list_global_libraries` on a bound fixture), discovery and annotation lists, schema counts (9/20/20), and the output-schema snapshot. Regenerate snapshots from the built tool and review the diff; do not copy the patch snapshots.
- [ ] Implement until green.

Run: stub build, then the task test command — Expected: pass.

**Task test command:** `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build -c Release --filter "FullyQualifiedName~TiaMcpServer.Tests.Library|FullyQualifiedName~TiaMcpServer.Tests.Tools|FullyQualifiedName~ReadOnlyModeTests"`

Commit: `feat: expose library_read tool`

### Task 5 — Offline qualification (orchestrator)

**Files:** none unless fixes are needed (each fix gets its own failing test and its own commit). **Produces:** a CI-equivalent green candidate with recorded commit SHA.

Run each serially and expect success:
- [ ] CI path (stub build):
  - `dotnet restore TiaMcpServer.slnx`
  - `pwsh -NoProfile -File scripts/verify-reference-stubs.ps1 -Configuration Release`
  - `dotnet build TiaMcpServer.slnx -m:1 -c Release /p:UseTiaPortalReferenceStubs=true`
  - `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build -c Release --filter "Category!=Packaging" --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory TestResults`
  - `pwsh -NoProfile -File scripts/verify-coverage-threshold.ps1 -CoveragePath <this run's cobertura> -MinimumLineRate 0.80`
- [ ] Packaging: `dotnet test … --no-build -c Release --filter "Category=Packaging"`; `dotnet pack TiaMcpServer/TiaMcpServer.csproj -m:1 -c Release -o <scratch>/out`; `pwsh -NoProfile -File scripts/verify-doctor-package.ps1 -PackagePath <nupkg>`; confirm no Siemens DLL in the package.
- [ ] Real references: `dotnet build TiaMcpServer.slnx -m:1 -c Release /p:UseTiaPortalReferenceStubs=false` and the non-packaging suite.
- [ ] Review the full diff (`superpowers:requesting-code-review`) for read-only guarantees, shared-seam regressions (HMI snapshots especially), and scope. A partial run, a timeout, or a pass on retry is not green; record any instability.

**Task test command:** the CI-path `dotnet test` line.

### Task 6 — Live acceptance (needs explicit authorization)

**Files:** transient evidence under `docs/superpowers/evidence/2026-11-03-library-read/`; report `docs/superpowers/acceptance/reports/<date>-library-read-live-verification.md`; any kept raw Openness probe under `reports/inputs/<date>-library-read-native-probe/` with an `inputs/README.md` row.

- [ ] Freeze the candidate SHA and the package hash. Use only disposable projects and libraries, and keep their names out of repo files.
- [ ] Run sequentially in read-only and read-write modes:
  - all five operations on a global and a project library;
  - name and path selectors;
  - nested folders, a multi-version type, dependencies and dependents;
  - cursor paging across a boundary, and a cursor after a library change between pages (frozen order, live values);
  - not-open and duplicate libraries;
  - unbound (`binding_conflict`), bound standalone, bound local session (global reads succeed, project-library reads refused), and a cursor after rebind (`cursor_binding_mismatch`).
- [ ] Probe and record:
  - whether system or corporate libraries appear in `GlobalLibraries`;
  - whether `GetAttribute("IsReadOnly"/"IsModified")` works on a project;
  - the `ConsistencyStatus` strings;
  - the behavior of a read, and of a cursor continuation, after the library is closed in the UI.
- [ ] Prove read-only: library file hashes and `IsModified` are unchanged before and after the run.
- [ ] Write the report listing passed, failed, and untested paths.

**Task test command:** `Test-Path docs/superpowers/acceptance/reports/*library-read-live-verification.md` returns True.

Commit: `test: add library read live verification report`

### Task 7 — Documentation (after Task 6)

**Files:** `README.md` (absolute GitHub links), `AGENTS.md` (counts 9/20/20, `library_read` in the overview, refined bound-only content rule), `docs/ARCHITECTURE.md`, new `docs/SupportedOperations/LIBRARY_OPERATIONS_SUMMARY.md` (reads only), `docs/SupportedOperations/README.md`, `docs/README.md`, one new ADR per `docs/adr/README.md` (D9), and `docs/IMPROVEMENT_LOG.md` entries (the write-plan carry list above, unbound global reads, local-session project-library reads, and any Task 6 gaps).

- [ ] Describe only live-verified behavior; state snapshot-cursor paging (frozen order, live values), nullable flags, and that global reads use the bound Portal.
- [ ] Run: `python -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30` — Expected: pass.

**Task test command:** the `verify-docs.py` line.

Commit: `docs: document library_read`

### Task 8 — Prune working files (before merge)

- [ ] Confirm that everything lasting was moved (Task 7). Delete this plan and the evidence directory. Run `python -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30 --fail-on-transient` — Expected: pass.
- [ ] Commit `docs: prune working files`. Push and open the PR only with explicit approval.

**Task test command:** the `verify-docs.py --fail-on-transient` line.
