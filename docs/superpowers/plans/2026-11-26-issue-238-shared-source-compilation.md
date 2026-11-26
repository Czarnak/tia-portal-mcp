# Issue #238: shared compilation for source block imports — implementation plan

Date: 2026-11-26. Status: plan, awaiting review. Branch `feat/issue-238-shared-source-compile` from `main` @ `8405cc46`.

**Goal:** When one `plc_write` call imports two or more blocks in source format, compile each affected PLC once after the loop instead of once per import. Large projects pay the full PLC compile time for every import today.

**Origin:** This ports doerk77's contribution for issue #238 (a v4.2.0 patch) to current `main` in simplified form: only the compile step is deferred. Implementation commits carry `Co-authored-by: doerk77 <82865479+doerk77@users.noreply.github.com>`, as #225 did.

**Architecture:** The host chooses deferral per item. The worker's source import skips only its whole-PLC compile, reports `compileStage:"deferred"`, and still performs its fresh final read. After the mutation loop, `PlcWriteDomain.VerifyAsync` runs one existing `compile_check` per distinct compile selector that saw a committed import, then runs the unchanged `PlcVerificationPolicy` readback. Compilation results are informational root data, never part of `success`.

**Stack:** C#; host/tests net10.0, Contracts netstandard2.0, worker net48; xunit. No new tool, public argument, worker method, dependency or Openness call.

**Session boundary:** This deliverable is a plan only. Commands below are for implementation.

## Decisions (maintainer, 2026-11-26)

| # | Decision |
| --- | --- |
| D1 | Worth doing: whole-PLC compile time dominates large source batches. |
| D2 | Shared compilation is information only. It never changes item status, verification or root `success`, as per-operation compiles already behave since `0dd0089f`. |
| D3 | After an early stop, compile every selector with at least one **committed** import, including an item that committed and then failed (per-operation mode had already compiled it). |
| D4 | `patch_block_logic` resolved to source format counts like `update_block_logic`. |
| D5 | The members join the still-unreleased write contract `2.1` (v4.4.0 shipped `2.0`). No version change; update the `2.1` doc comment and CHANGELOG. |
| D6 | Eligibility: any call with ≥2 source-format block content imports (update or patch). Other operations in the call (tags, structure, XML imports, types) run exactly as today. |
| D7 | Live acceptance by the maintainer only, on a disposable project. |

## Requirements that every task inherits

- Calls with fewer than two eligible imports are byte-for-byte unchanged in behavior. Their documents only gain `compilationMode:"per-operation"` and `sharedCompilations:null`.
- Only the compile step moves. Hash recheck, source generation, temporary-source cleanup, fresh final read, `BlockPostconditionVerifier`, typed outcomes, stop-at-first-failure, late replan, `dependsOn`, readback, `superseded`, audit v2, leases: all unchanged. No rollback or replay.
- The compile selector is exactly what the per-operation source path passes today: `BlockAddress.Parse(blockPath).PlcName` (null when unqualified), with `blockPath:null` (`BlockImporter.cs:260-268`). The scope is therefore never broader than today. Two spellings of one PLC compile twice; this is accepted and documented.
- Shared compiles reuse `OpennessWorkerClient.CompileCheckAsync(blockPath:null, plcName)` and decode through `StandalonePayloadContract.DecodeCompile`/`StandaloneCompileOutcome.IsPassed`. No hand-rolled report check, no new conditional members, explicit nulls.
- No `CancellationToken` on `PlcWriteDomain`. `WriteRun` already throws between items and before verification. A cancelled call leaves deferred imports uncompiled, and the docs say to run `compile_check`.
- Budget code reads effective `MaxItemChars`/`MaxDocumentChars`. Whole values are omitted, never cut.
- A new host source file is linked explicitly in `TiaMcpServer.Tests.csproj` (after line 234).
- Never write TIA project names or paths into repo files or commits.
- Commits: short one-line conventional messages plus the co-author trailer above; commit after each task.
- Build and test with PowerShell, not Bash, when `/p:` flags are used.

## Current code and implementation decisions

| Source on `main` | Consequence |
| --- | --- |
| `TiaMcpServer.Contracts/Worker/WorkerRequest.cs` (block region, ~l.75) | Add one host-only flag; extend its "Forwarded by" comment. |
| `TiaMcpServer.OpennessWorker/Openness/Block/BlockCompileObservation.cs` | Add a `Deferred()` observation (stage `"deferred"`). `CreateImportOutcome` already uses it only for completed imports (`BlockPostconditionVerifier.cs:72-76`). |
| `.../Block/BlockImporter.cs` `Import` (l.13), `ImportSource` (l.119), `ObservePostconditions` (l.251) | Thread `deferCompilation` to `ObservePostconditions`, which then skips `CompileChecker.CompileObserved` but keeps `ObserveFinalState`. |
| `TiaMcpServer.OpennessWorker/Program.PlcDispatch.cs:53` | Forward the flag. |
| `TiaMcpServer/Worker/BlockImportOutcomeValidator.cs:17` | Accept `"deferred"` only when it was requested. |
| `TiaMcpServer/Worker/OpennessWorkerClient.Plc.cs:25,142` | Update takes the flag; compile is reused unchanged. |
| `TiaMcpServer/Plc/PlcWorkerInvoker.cs:41,81` | Pass the flag through. |
| `TiaMcpServer/Plc/PlcWriteDomain.cs` | Selection, commit tracking, shared compile and composition live here, with patches via `ExecutionItem`. |
| `TiaMcpServer/Plc/PlcWriteModels.cs:44` | Two positional root members, so the schema marks them required. |
| `TiaMcpServer/Plc/PlcWritePayloadBudget.cs` | New omittable roots, compaction and admission reservation. |
| `TiaMcpServer.FakeWorker/Plc/PlcWriteRoundtripScenario.cs:98` | Echo deferral; add a `compile_check` arm. |

## After the tasks (orchestrator gates; not part of any task brief)

1. **Offline gate** after Task 6: `dotnet build TiaMcpServer.slnx -m:1` and `dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"` are green, plus a final whole-branch review.
2. **Live acceptance (D7)** after the offline gate, only with explicit maintainer authorization in that session. Rebuild with the real V21 assemblies and run the server from the branch build (the installed plugin binary does not reflect local builds). Use a disposable CPU project, never saved over a baseline. Cases:
   - two independent source updates: one shared compile, against the per-operation baseline compile count
   - an input rename plus the matching caller update in one call
   - a rename with a stale caller: imports succeed, compile `failed`, root `success:true`
   - generation rejection at item 2: item 1 compiled, item 2 not, later items skipped
   - a source patch plus an update mixed with a tag edit
   - each `contentHash` equals a fresh `plc_read` hash
   - wall time for a ≥5-block batch, against the same batch per-operation

   Write the report to `docs/superpowers/acceptance/reports/` before Task 7.
3. **Docs** (Task 7) only after live acceptance. At the end of the PR, prune `docs/superpowers/plans|specs|evidence` in a final `docs: prune working files` commit.

---

### Task 1: Worker defers only the source compile

**Files:** `TiaMcpServer.Contracts/Worker/WorkerRequest.cs`, `TiaMcpServer.OpennessWorker/Openness/Block/BlockCompileObservation.cs`, `.../Block/BlockImporter.cs`, `TiaMcpServer.OpennessWorker/Program.PlcDispatch.cs`; tests in `TiaMcpServer.Tests/Block/BlockImportCoordinatorTests.cs`, `BlockImporterRouteSourceTests.cs`, `BlockPostconditionVerifierTests.cs`.

**Interfaces (Produces)**
- `WorkerRequest.DeferBlockCompilation : bool` (default false). Doc: host-selected, `update_block_logic` source format only, not public.
- `BlockCompileObservation.Deferred() : BlockCompileObservation` — `Stage="deferred"`, `Report=null`, `DetailsOmitted=false`, `SessionAvailable=true`.
- `BlockImporter.Import(ProjectBase, string blockPath, string content, string format, string? expectedContentHash = null, bool deferCompilation = false)`. With `deferCompilation` and non-source format: throw `WorkerOperationException(ValidationError, …)` before any read.
- `ImportSource(…, bool deferCompilation)` and `ObservePostconditions(…, bool compileAllowed, bool deferCompilation, …)`. When deferred: compile observation `Deferred()`, and `ObserveFinalState` runs as today.
- The dispatch passes `request.DeferBlockCompilation`.

**Tests (names → behavior)**
- `DeferredSourceImport_CompletesWithDeferredStageAndFreshFinalRead`: outcome `importStage completed`, `compileStage deferred`, `compileReport null`, `finalReadStage succeeded`, `targetPresent true`.
- `DeferredSourceImport_FailedGeneration_KeepsExistingStages`: not-started/unknown imports report the existing `not_started`/`unavailable`, never `deferred`.
- `DeferredFlagWithXml_IsValidationErrorBeforeReads`.
- `VerifyImport_DeferredStage_DoesNotAffectSuccess`.
- Route source test: the source route still uses the common fresh final reader; `CompileObserved` is not reached when deferred.

**Run:** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Block"` **Expected:** all pass.
**Task test command:** same.

### Task 2: Host client and outcome validation

**Files:** `TiaMcpServer/Worker/BlockImportOutcomeValidator.cs`, `TiaMcpServer/Worker/OpennessWorkerClient.Plc.cs`, `TiaMcpServer/Plc/PlcWorkerInvoker.cs`, `TiaMcpServer.FakeWorker/Plc/PlcWriteRoundtripScenario.cs`; tests in `TiaMcpServer.Tests/Block/BlockImportOutcomeInfoTests.cs`, `TiaMcpServer.Tests/Worker/OpennessWorkerClientIntegrationTests.cs`.

**Interfaces**
- `BlockImportOutcomeValidator.Validate(BlockImportOutcomeInfo? outcome, string? normalizedFormatOrNull, bool compilationDeferred = false) : bool`. Rules:
  - `"deferred"` is valid only when `compilationDeferred`, the format is source, `importStage` is `completed`, `compileReport` is null and `compileDetailsOmitted` is false.
  - When `compilationDeferred` and `importStage` is `completed`, the stage must be `"deferred"`.
  - Every other rule is unchanged.
- `OpennessWorkerClient.UpdateBlockLogicAsync(string blockPath, string content, string? format = null, string? expectedContentHash = null, bool deferCompilation = false)` sets the request flag and validates with it. A mismatch is the existing `protocol_error` path.
- `PlcWorkerInvoker.InvokeWriteAsync(OpennessWorkerClient client, PlcOperationRequest op, bool deferCompilation = false)`.
- FakeWorker: when the flag is set, the update returns the completed outcome with `compileStage:"deferred"` and no report. The `compile_check` arm asserts `BlockPath == null`, returns a per-PLC report, and supports fixtures for compile errors, a worker failure and a malformed payload.
- Before finishing, run `find_references` on `CompileStage` consumers in host code: `"deferred"` must read as "no compile evidence" everywhere.

**Tests**
- `Validate_DeferredStage_RequiresRequestCompletedSourceAndNoReport` (theory over each violating field).
- `Validate_DeferredStageWithoutRequest_IsRejected`.
- `Validate_DeferralRequestedButCompiled_IsRejected`.
- `UpdateBlockLogic_DeferredRoundtrip_ForwardsFlagAndAcceptsDeferredOutcome`.
- `UpdateBlockLogic_DeferralIgnoredByWorker_IsProtocolError`.

**Run:** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~BlockImportOutcome|FullyQualifiedName~OpennessWorkerClientIntegration"` **Expected:** all pass.
**Task test command:** same.

### Task 3: Selection, commit tracking and shared compilation (pure host logic)

**Files:** new `TiaMcpServer/Plc/PlcSharedCompilation.cs` (link it in `TiaMcpServer.Tests.csproj`), `TiaMcpServer/Plc/PlcWriteModels.cs`; new `TiaMcpServer.Tests/Plc/PlcSharedCompilationTests.cs`.

**Interfaces**
- Record `PlcSharedCompilation(string? PlcName, IReadOnlyList<string> OperationIds, string Stage, CompileCheckReport? Report, StructuredOperationFailure? Failure, StructuredOperationOmission? Omission)`.
  - `PlcName` is the selector (null means the unqualified per-operation scope).
  - `Stage` is one of `succeeded | failed | unavailable | skipped`.
- `static class PlcSharedCompilationPolicy`:
  - `IReadOnlySet<string> DeferredOperationIds(IReadOnlyList<PlcOperationRequest> items)`: the IDs of `update_block_logic`/`patch_block_logic` items whose `PlcFormatNames.TryNormalize` gives source. Empty unless the count is ≥2. Items that fail normalization do not count.
  - `string? Selector(string blockPath)`: `BlockAddress.Parse(blockPath).PlcName`.
  - `string StageOf(StandaloneToolOutcome<CompileCheckReport> decoded)`:
    - `IsPassed` (Warning included) → `succeeded`
    - report with `TotalErrorCount > 0` → `failed`
    - report without errors but not passed (an invocation failure or ambiguous selection) → `unavailable`
    - no report → `unavailable` with the decoded failure (`protocol_error` for a malformed payload, never echoed)
- `sealed class PlcSharedCompilationTracker`:
  - `void Record(PlcOperationRequest executionItem, WorkerCallResult result)`: when `result.BlockImportOutcome?.TargetMutationCommitted == true`, append the ID under its selector, in first-commit order. Selectors compare `OrdinalIgnoreCase`, with null as its own key.
  - `Task<IReadOnlyList<PlcSharedCompilation>> CompileAsync(Func<string?, Task<WorkerCallResult>> compile, StructuredOperationBatch batch)`: compiles sequentially in first-commit order.
  - If the first non-succeeded item failed with `portal_lost`, `worker_timeout`, `worker_crashed`, `protocol_error` or `binding_conflict`, issue no compile and report every entry `skipped`.

**Tests**
- `Eligibility_TwoSourceImports_IncludingResolvedPatch`.
- `Eligibility_OneSourceImport_IsEmpty`.
- `Eligibility_XmlAndTypeImports_NotCounted`.
- `Eligibility_MixedWithTagsAndCreates_StillEligible`.
- `Tracker_GroupsBySelectorCaseInsensitive_FirstCommitOrder`.
- `Tracker_UnqualifiedPathsShareNullSelector`.
- `Tracker_CommittedThenFailedItem_IsCompiled`.
- `Tracker_UncommittedFailure_NotCompiled`.
- `Compile_WarningsOnly_IsSucceeded`.
- `Compile_Errors_IsFailed`.
- `Compile_InvocationFailureReport_IsUnavailable`.
- `Compile_MalformedPayload_IsUnavailableProtocolError`.
- `Compile_WorkerFailure_KeepsCategory`.
- `UncertainStop_SkipsEveryCompile` (theory over the five categories).
- `NoCommits_ReturnsEmptyList`.

**Run:** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~PlcSharedCompilation"` **Expected:** all pass.
**Task test command:** same.

### Task 4: Domain wiring and response members

**Files:** `TiaMcpServer/Plc/PlcWriteDomain.cs`, `TiaMcpServer/Plc/PlcWriteModels.cs`, every `new PlcGuardedWriteResponse(` call site (find with `find_references`); tests in `TiaMcpServer.Tests/Plc/PlcGuardedWriteDomainTests.cs` and a new `PlcSharedCompilationPipelineTests.cs` (FakeWorker through the MCP path).

**Interfaces**
- `PlcGuardedWriteResponse(…, PlcWriteVerification? Verification, string CompilationMode, IReadOnlyList<PlcSharedCompilation>? SharedCompilations, StructuredOperationOmission? Omission)`.
  - `CompilationMode` is `"per-operation" | "shared-per-plc"`.
  - `SharedCompilations` is null in per-operation mode, in previews and in pre-apply rejections, and a list (possibly empty) after apply in shared mode.
- `PlcWriteDomain`:
  - `Validate` stores `DeferredOperationIds(items)`, so every composed document reports the mode, rejections included.
  - `MutateAsync` becomes `async`. It invokes with `deferCompilation: _deferred.Contains(item.OperationId)` on `ExecutionItem(item)`, then records the result in the tracker. The existing patch audit capture stays first.
  - `VerifyAsync` runs `tracker.CompileAsync(plc => client.CompileCheckAsync(null, plc), batch)` in shared mode, then the unchanged `PlcVerificationPolicy.VerifyAsync`.
  - `Compose` fills both members.
- `PlcWriteContractVersion` stays `"2.1"`; the doc comment adds shared compilation.

**Tests (FakeWorker pipeline unless noted)**
- `TwoSourceUpdates_OneCompilePerSelector_AfterAllImports`: call order is imports, then compile, then readback exports.
- `CrossPlcBatch_CompilesEachSelectorOnce`.
- `PatchPlusUpdate_SourceFormat_SharedMode`.
- `SingleSourceUpdate_PerOperation_NoCompileCheckCall`.
- `MixedCall_TagEditAndXmlImport_UnchangedWhileSourceDeferred`.
- `CompileErrors_RootSuccessStaysTrue` (D2).
- `GenerationFailureAtItem2_CompilesItem1Only_ReadbackKeepsHashes`.
- `UncertainStop_SkipsCompile_ReadbackUnchanged`.
- `DryRun_ReportsSharedModeAndNullCompilations`.
- `ValidationRejection_ReportsMode`.
- `ContentHashAfterSharedCompile_EqualsNextPlcReadHash`.
- `AuditRecord_ContainsSharedCompilations` (from the delivered document).

**Run:** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~PlcGuardedWriteDomain|FullyQualifiedName~PlcSharedCompilation"` **Expected:** all pass.
**Task test command:** same.

### Task 5: Budget, compaction and admission

**Files:** `TiaMcpServer/Plc/PlcWritePayloadBudget.cs`; tests in `TiaMcpServer.Tests/Plc/PlcWriteBudgetTests.cs`.

**Interfaces**
- Each entry's `Report` is its own omittable value: an item root when over `maxItemChars`, and a document root ordered after content diffs and before failure outcomes. Omitting it sets the entry's `Report=null` and `Omission`.
- Verification checks remain their own root, so a large compile report can never drop a `contentHash`.
- `Compact` maps each entry's `Failure.Message` through the shared diagnostic summary.
- `MeasureProtectedCore` reserves, per eligible item, one worst-case entry: its ID, the selector length from its `blockPath`, the summary failure and an omission record, plus `compilationMode`.
- `HasOmission` includes entry omissions.

**Tests**
- `OversizedCompileReport_OmittedBeforeVerificationChecks`.
- `CompileReportOverItemLimit_OmittedWholeWithOmissionRecord`.
- `Compact_SummarizesCompileFailureMessages`.
- `ProtectedCore_ReservesSharedCompilationEntries`.
- `NonEligibleCall_BudgetBehaviorUnchanged` (existing fixtures, byte-equal documents apart from the two new members).

**Run:** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~PlcWriteBudget"` **Expected:** all pass.
**Task test command:** same.

### Task 6: Tool description, schema snapshot and contract conformance

**Files:** `TiaMcpServer/Plc/PlcWriteTools.cs` (description, l.14), `TiaMcpServer.Tests/Tools/Snapshots/output-schemas.json`, `TiaMcpServer.Tests/Tools/ToolOutputContractConformanceTests.cs`, `TiaMcpServer.Tests/Tools/McpToolSchemaTests.cs`.

**Changes**
- Edit the existing description; do not replace it. Append:
  - two or more source-format `update_block_logic`/`patch_block_logic` items defer their compile to one `compile_check` per PLC selector after the loop
  - `compilationMode`/`sharedCompilations`
  - compiler results stay informational
  - cancellation leaves deferred imports uncompiled
- Regenerate only the `plc_write` entry of the snapshot from `tools/list`. `compileStage` stays a plain string in all 20 import-outcome sites.
- Conformance: add a success probe in shared mode and a rejection probe that shows `compilationMode`.

**Tests:** `OutputSchemasMatchMain` (both protocols), `ToolOutputContractConformanceTests`, `ConditionalMemberRegisterTests` (must stay unchanged and green: no new conditional members).

**Run:** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~McpToolSchema|FullyQualifiedName~ToolOutputContractConformance|FullyQualifiedName~ConditionalMemberRegister"` **Expected:** all pass.
**Task test command:** same.

### Task 7: Documentation (after live acceptance)

**Files:** `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md` (writes, verification, Known limits), `docs/SupportedOperations/IMPORT_EXPORT_OPTIONS_SUMMARY.md` (`compileStage:"deferred"`), `docs/ARCHITECTURE.md` (PLC write pipeline: deferral and finalization in `VerifyAsync`), `AGENTS.md` PLC writes bullet (one sentence), `CHANGELOG.md` (extend the Unreleased 2.1 entry and add a line crediting doerk77 for #238), `docs/IMPROVEMENT_LOG.md` (follow-ups: dedupe of two spellings of one PLC; resolved-target grouping).

**Content rules:** current behavior only. State the selector rule, D2, D3, D6, the uncertain-stop skip, cancellation, and that the scope equals today's per-operation compile. Do not cite transient files.

**Run:** `python -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30` **Expected:** passes.
**Task test command:** `git grep -n "sharedCompilations" -- docs AGENTS.md` lists the PLC reference, the import options summary, ARCHITECTURE and AGENTS.

## Coverage map

| Requirement | Tasks |
| --- | --- |
| Compile-only deferral, final read kept | 1, 2 |
| Eligibility D4/D6, selector rule | 3, 4 |
| Informational compile D2 | 3, 4, 6 |
| Committed-prefix compile D3, uncertain skip | 3, 4 |
| Readback/hash evidence preserved | 4, 5 |
| Contract 2.1, schema, no conditional members D5 | 4, 6 |
| Budget never drops hashes for compile reports | 5 |
| Live evidence D7, docs | gates, 7 |
