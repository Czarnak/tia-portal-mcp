# Wave 2 I2 Truthful Block-Import Outcomes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every `update_block_logic` result state what is known about import, compilation, in-memory mutation, and the independent final read, including when import completed before verification failed.

**Architecture:** Add one optional typed `blockImportOutcome` that travels with the existing worker response, host result, and generic batch item without changing the existing `result`, `warnings`, or `failureCategory` fields. The worker import coordinator records the mutation boundary, reuses P1's bounded compiler projection, and performs a fresh target read even after an uncertain import exception; the verifier returns or throws with the same outcome. The contract remains explicitly non-atomic: it never rolls back, retries, saves, downloads, or claims that an exported document equals the requested content.

**Tech Stack:** C#; .NET 10 host/tests/FakeWorker; .NET Framework 4.8 Openness worker; netstandard2.0 contracts; xUnit; `System.Text.Json`; TIA Portal V21 for separately authorized live acceptance.

**Spec:** [Open Bug Parallel Pull-Request Delivery Design](../specs/2026-09-26-open-bug-parallel-pr-delivery-design.md), lane I2. Issue evidence: [#72](https://github.com/Czarnak/tia-portal-mcp/issues/72).

**Future branch:** `fix/truthful-block-import-outcomes`, created in its own worktree from the then-current `origin/main`; its pull request targets `main` directly.

## Global Constraints

- K1 and P1 are merged at the planning baseline `9b0fb50f`. Refresh `origin/main`, issue/PR state, and the working tree before implementation. If a later merge changed any listed seam or public envelope, stop and rewrite the stale task rather than mechanically rebasing it.
- This plan authorizes implementation only after user review and selection of an execution method. It does not authorize push, PR creation, merge, issue edits, or a live TIA mutation.
- Preserve the existing non-atomic sequential batch contract: stop after the first failed item, mark later items skipped, and perform no automatic rollback or retry.
- `TargetMutationCommitted` is a conservative boundary for the addressed block call. It is true once `Blocks.Import`, `ImportFromDocuments`, or `GenerateBlocksFromSource` returns normally, including when a returned document state or later cleanup fails; at that point the target must be treated as possibly changed. It never means saved, downloaded, plant-accepted, or byte-equivalent to the request.
- A target call that throws or is lost after invocation starts has `TargetMutationCommitted = null`. A deterministic refusal before target invocation has false. Never infer target commitment from target presence alone.
- Source-format staging has a separate project mutation: `ExternalSources.CreateFromFile` creates a temporary project node before generation. Track its lifecycle explicitly; no source setup failure may be reported as globally mutation-free merely because the target generation call did not start.
- The independent final read reports only what it establishes. Fresh resolution/export may establish target presence; until S3/#82.8 qualifies a freshness/equality signal, `ContentRelation` remains `unknown` and must never claim `requested` or `prior`.
- Reuse P1's whole `CompileCheckReport` and preserve its report-level totals across every selected PLC; bounded public detail may retain only the first deterministic PLC entries. Add a structured internal invocation-availability signal; never parse P1 diagnostic prose or interpret zero fallback counts as a clean compile. Preserve current target selection: an exact block target when a block path is supplied, the named PLC when only `plcName` is supplied, otherwise every discovered PLC in deterministic locator order. Aggregate stage precedence is `unavailable` over `failed` over `succeeded`.
- Do not echo raw Siemens exceptions, submitted block content, staging paths, local file paths, or exact disposable-project names. Keep diagnostic strings bounded and sanitized.
- In Task 3's complete wiring commit, advertise required worker capability `typed-block-import-outcome-v1` before any engineering request. A worker that lacks it must fail the hello handshake before `update_block_logic` can be sent; no earlier commit may advertise partial support.
- Keep preview input, safety-token generation/validation, pinned project-binding lease, audit record format, access-mode policy, and batch operation registration unchanged. The outcome is response-only apply evidence.
- Run builds and tests serially (`-m:1`). Stub/FakeWorker/static/package evidence is not live V21 evidence. A live mutation requires fresh authorization for the exact disposable `.ap21` immediately before execution.
- Use one conventional commit per task after the focused tests and scoped diff review. The implementation session must not commit unrelated user work.

## Locked Public Contract

Create `TiaMcpServer.Contracts/BlockImportOutcomeInfo.cs` with these JSON-facing types and exact values:

- `BlockImportOutcomeInfo.ImportStage`: `not_started`, `completed`, or `unknown`.
- `BlockImportOutcomeInfo.ImportResultState`: `success`, `non_success`, or `unavailable`. A normal return from void `Blocks.Import` maps to success; SIMATIC SD maps a normally returned `DocumentResultState` to success/non-success; source generation maps a normal return to success. Never expose arbitrary enum/string text. A call that does not return uses unavailable.
- `BlockImportOutcomeInfo.CompileStage`: `not_started`, `succeeded`, `failed`, or `unavailable`.
- `BlockImportOutcomeInfo.FinalReadStage`: `not_started`, `succeeded`, or `unavailable`.
- `bool? TargetMutationCommitted`: false only before target import/generation invocation, true only after that target call returns normally, and null when target commitment is unknown.
- `TemporarySourceState`: `not_applicable`, `not_created`, `removed`, `residue_possible`, or `unknown`. Valid XML uses `not_applicable`. Valid source uses `not_created` only before `CreateFromFile` starts, `unknown` if that call starts but does not return, `removed` after confirmed deletion, and `residue_possible` when deletion is not confirmed. A refusal before an invalid/unnormalizable format is classified uses `unknown`. Never expose the generated node name or path.
- `bool? TargetPresent`: true/false only from the fresh final resolver/read; null when that read is unavailable or unreliable.
- `ContentRelation`: `unknown` in I2. Define the closed vocabulary with `requested`, `prior`, and `unknown`, but do not emit the first two until a later approved source-freshness design proves them.
- `CompileCheckReport? CompileReport`: a bounded copy of P1's whole report. `CompileStage=not_started` requires null; `succeeded` and `failed` require a report; `unavailable` permits null when PLC/target/session resolution failed before any report existed and otherwise retains the partial report. Keep at most 8 PLC entries, 20 projected message rows total, 8 diagnostic notes total, 256 decoded characters per string, and 1,024 decoded diagnostic characters across messages/paths/notes. Preserve full aggregate error/warning totals even when detail is omitted.
- `bool CompileDetailsOmitted`: true whenever any PLC entry, message, note, or string detail was removed or truncated by the I2 projection.

The target matrix is closed: `not_started` requires false plus unavailable result; `unknown` requires null plus unavailable result; `completed` requires true plus success/non-success result. Valid XML requires `TemporarySourceState=not_applicable`. Valid source may use `not_created` only before node creation starts, `removed`/`residue_possible` only after creation returned and deletion was attempted, and `unknown` after creation starts without trustworthy final cleanup evidence. Invalid pre-normalization format and post-dispatch transport ambiguity use unknown source state.

`CompileStage=failed` means every intended compiler invocation returned but at least one result has errors (`TotalErrorCount > 0` or P1 overall error state). Warnings without errors remain `succeeded` and retain P1's warning state/totals. `CompileStage=unavailable` comes only from structured target-selection, session, or invocation availability—not prose—and wins over a simultaneous error from another PLC.

The final outcome must serialize to at most 12,000 UTF-16 characters under the actual worker/host JSON options, including escape expansion and metadata. The worker projection deterministically drops lowest-priority message/note detail and truncates bounded strings until both component caps and this final limit hold. The host rejects an oversized outcome; it never truncates trusted evidence after receipt.

Add nullable `BlockImportOutcome` properties to `WorkerResponse`, `WorkerOperationException`, `WorkerCallResult`, and `OperationBatchResult`. `OperationBatchResultFormatter` emits it as `operations[].blockImportOutcome`. Decorate/construct nullable wire and presentation members so unrelated operations omit the member entirely; their serialized document and payload-budget decision remain byte-for-byte unchanged.

Create one closed host validator for the method-specific response. It rejects unknown stage/result/source-state/relation values, `requested` or `prior` in I2, contradictory stage/target-commitment/result/source-lifecycle combinations, an outcome above any collection/field/serialized limit, `CompileStage=not_started` with a report, a non-started final read with target evidence, and success/failure compile stages inconsistent with P1 totals/state. Valid ambiguity remains expressible: an invoked target call may have unknown commitment and an unavailable or successful final read, an unavailable final export may retain independently established target presence, and source-node state may be unknown even when target generation never started.

The user-visible surface is the generic batch item `operations[].blockImportOutcome`. `WorkerCallResult` is the typed internal carrier only; I2 does not add or alter a standalone direct-update envelope or `ToEnvelopeText` presentation.

Add a non-serialized, closed `WorkerDispatchState` to `WorkerCallResult`: `NotSent`, `Sent`, or `Unknown`. It is relative to the intended `update_block_logic` request, not to any prerequisite `get_project_status` call. `NotSent` is set only at an explicit control-flow point that proves the update request did not cross the worker transport boundary; `Sent` is set only after a worker response is received; timeout, crash, EOF, malformed transport, and any unclassified path remain `Unknown`. Never infer this state from a failure category or message.

`BlockImportOutcomeSynthesizer` is the single host authority for a missing outcome. `NotSent` produces target `not_started`/false, compile/final-read `not_started`, null report/presence, and source `not_applicable` for normalized XML, `not_created` for normalized source, or `unknown` when format normalization never succeeded. `Sent` or `Unknown` without a valid worker outcome produces conservative target unknown/null, unavailable/null compile, unavailable/null final read, and source `unknown` for normalized source or invalid format (`not_applicable` remains valid XML's source state). A received valid worker outcome wins; a successful `update_block_logic` response without one, or any received malformed outcome, is `protocol_error` with the conservative synthesized outcome. Categories and messages never create commitment evidence.

## File Ownership

I2 implementation allowlist:

| Responsibility | Files |
| --- | --- |
| Typed public contract | Create `TiaMcpServer.Contracts/BlockImportOutcomeInfo.cs`; modify `TiaMcpServer.Contracts/WorkerResponse.cs` |
| Worker target/source boundaries and compile evidence | Create `TiaMcpServer.OpennessWorker/Openness/BlockImportInvocationBoundary.cs`, `BlockSourceArtifactTracker.cs`, `BlockCompileObservation.cs`, `BlockImportOutcomeProjection.cs`, and `BlockImportDiagnosticSanitizer.cs`; modify `BlockImportResult.cs`, `BlockImportCoordinator.cs`, `BlockPostconditionEvidence.cs`, `BlockPostconditionVerifier.cs`, `BlockImporter.cs`, `ExternalSourceScope.cs`, and `CompileChecker.cs` |
| Worker pre-import decoration and response propagation | Create `TiaMcpServer.OpennessWorker/BlockUpdateOutcomeDecorator.cs`; modify `WorkerOperationException.cs` and `Program.cs` |
| Host validation, dispatch provenance, and batch propagation | Create `TiaMcpServer/Worker/BlockImportOutcomeValidator.cs` and `BlockImportOutcomeSynthesizer.cs`; modify `WorkerCallResult.cs`, `OpennessWorkerClient.cs`, `TiaMcpServer/Batch/BatchWorkerInvoker.cs`, `TiaMcpServer.Contracts/WorkerProtocol.cs`, `TiaMcpServer/OperationBatches/OperationBatchResult.cs`, `OperationBatchExecutionEngine.cs`, and `OperationBatchResultFormatter.cs` |
| Focused contracts, stage, sanitizer, and route tests | Create `TiaMcpServer.Tests/Block/BlockImportOutcomeInfoTests.cs`, `BlockCompileObservationTests.cs`, `BlockImportDiagnosticSanitizerTests.cs`, and `BlockImporterRouteSourceTests.cs`; modify `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`, `TiaMcpServer.Tests/Block/BlockImportCoordinatorTests.cs`, `BlockPostconditionVerifierTests.cs`, `BlockMutationPostconditionTests.cs`, and `CompileReportProjectionTests.cs` |
| Handshake, worker refusal, dispatch, transport, budget, and apply tests | Create `TiaMcpServer.Tests/Worker/BlockUpdateOutcomeDecoratorTests.cs`, `TiaMcpServer.Tests/Worker/UpdateBlockLogicWorkerSourceContractTests.cs`, and `TiaMcpServer.Tests/Batch/BlockUpdateDispatchProvenanceTests.cs`; modify `WorkerProtocolHandshakeTests.cs`, `WorkerResponseJsonTests.cs`, `WorkerCallResultTests.cs`, `OpennessWorkerClientIntegrationTests.cs`, `TiaMcpServer.Tests/OperationBatches/OperationBatchKernelTests.cs`, `OperationBatchPayloadBudgetTests.cs`, `TiaMcpServer.Tests/Batch/WriteBatchToolsBehaviorTests.cs`, `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`, and `TiaMcpServer.FakeWorker/Program.cs` |
| Maintained user documentation | Modify `docs/SupportedOperations/IMPORT_EXPORT_OPTIONS_SUMMARY.md` |

Reserve every file above while I2 is active. In particular, worker `Program.cs`, `OpennessWorkerClient.cs`, `WorkerResponse.cs`, FakeWorker `Program.cs`, and generic batch-result files are whole-file hotspots. B2 is not allowed to start until I2 merges and its current-main integration gate passes; N2 must not expand into these generic files. T2 owns `OperationPolicyCatalog.cs`, `TiaPortalSession.cs`, and its own tests, so I2 and T2 are disjoint.

Do not edit `WorkerRequest.cs`, `BatchOperationRequest.cs`, `PersistentWorkerTransport.cs`, `WriteBatchTools.cs`, `BatchSafetySnapshot.cs`, safety-token/audit classes, `BlockMutationService.cs`, `BlockCreationCoordinator.cs`, `BlockExporter*.cs`, `CompileCheckReport.cs`, `PlcCompileInfo.cs`, `README.md`, `docs/ARCHITECTURE.md`, or `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md`. A need for any reserved file pauses the lane for a reviewed whole-file plan amendment.

## Review Focus

1. Capability and dispatch provenance: capability activation occurs only in the final wiring commit; a stale worker is rejected before the original `update_block_logic` request, and every `NotSent`/`Sent`/`Unknown` assignment is made at an explicit control-flow seam rather than inferred from text.
2. Exact target boundary: ordinary XML `Blocks.Import`, SIMATIC SD `ImportFromDocuments`, and source `GenerateBlocksFromSource` each use the tracker. Pre-call refusal is false/not-started, an API throw is null/unknown, and a normal return is true/completed; the returned XML/SD/source result maps only to the closed result value.
3. Source artifact and redaction: `CreateFromFile` plus deletion have their own lifecycle evidence. Setup/generation/cleanup failures cannot claim no project mutation, cannot fabricate a zero generated count, and expose neither raw exception text nor local/project-node paths.
4. Compile aggregation: block/named/all-PLC selection remains current-main behavior; pre-invocation no-PLC/target/session failures are representable, and any unavailable invocation wins over compile failure, which wins over warning-only/clean success. No diagnostic prose is parsed.
5. Response, final read, and audit: malformed/bloated combinations fail closed; unrelated JSON/budget decisions remain byte-identical; every attempted target import gets one independent fresh resolution/export; the bounded response is covered by the full-result audit hash and the preview remains bounded/redacted while later items are skipped.

---

### Task 1: Lock the closed outcome contract and additive carriers

**Files:** Create `TiaMcpServer.Contracts/BlockImportOutcomeInfo.cs`, `TiaMcpServer/Worker/BlockImportOutcomeValidator.cs`, and `TiaMcpServer.Tests/Block/BlockImportOutcomeInfoTests.cs`; modify `TiaMcpServer.Contracts/WorkerResponse.cs`, `TiaMcpServer.OpennessWorker/WorkerOperationException.cs`, `TiaMcpServer/Worker/WorkerCallResult.cs`, `TiaMcpServer/OperationBatches/OperationBatchResult.cs`, `OperationBatchExecutionEngine.cs`, `OperationBatchResultFormatter.cs`, `TiaMcpServer.Tests/Worker/WorkerResponseJsonTests.cs`, `WorkerCallResultTests.cs`, `TiaMcpServer.Tests/OperationBatches/OperationBatchKernelTests.cs`, and `OperationBatchPayloadBudgetTests.cs`.

**Interfaces:** Define the DTO, P1 `CompileCheckReport`, caps, and closed values exactly as in **Locked Public Contract**. Add optional `BlockImportOutcome` properties through worker/call/batch types and extend `WorkerOperationException` with one optional final constructor argument. Use `JsonIgnoreCondition.WhenWritingNull` or an equivalent conditional formatter member so unrelated results omit the field. `OperationBatchExecutionEngine.ToResult` copies the outcome without parsing payload/error text. `BlockImportOutcomeValidator.Validate(outcome, normalizedFormatOrNull)` is the single host method for vocabulary, format/state combinations, component caps, and actual serialized-size checks; null format permits only conservative unknown source state. Do not add or advertise `typed-block-import-outcome-v1` in this task.

- [ ] **Step 1: Write contract and closed-validator REDs.** Cover every valid stage/target-commitment/result/source-state matrix and add named failures for unknown closed values, `requested`/`prior`, non-completed import with an available result state, completed import with unavailable result state, non-started target import with non-false commitment, valid XML carrying anything other than `not_applicable`, valid source carrying an impossible lifecycle, null/invalid format carrying anything except unknown, non-started compile with a report, non-started final read with target evidence, success/failure inconsistent with P1 totals/state, excessive PLC/message/note/field sizes, and escape-heavy JSON above 12,000 serialized characters. Explicitly accept warning-only P1 reports as `succeeded` and unavailable compile with a null report.
- [ ] **Step 2: Write propagation, omission, and budget REDs.** Add round-trip/failure tests plus `Formatter_UnrelatedOperation_RemainsByteIdentical`, `PayloadBudget_UnrelatedOperation_KeepsSameOmissionDecision`, and a typed-apply formatter case. Compare the complete pre-I2 unrelated JSON string, not only a null property. Assert existing result text, K1 category/warnings, stop-later behavior, and byte-count decisions.
- [ ] **Step 3: Run RED.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -m:1 /p:UseTiaPortalReferenceStubs=true --filter "FullyQualifiedName~BlockImportOutcomeInfoTests|FullyQualifiedName~WorkerResponseJsonTests|FullyQualifiedName~WorkerCallResultTests|FullyQualifiedName~OperationBatchKernelTests|FullyQualifiedName~OperationBatchPayloadBudgetTests"
```

Expected: the DTO, validator, propagation, and conditional member do not exist. Existing protocol capabilities and K1 category/warning/result/stop-later behavior remain the compatibility baseline.

- [ ] **Step 4: Implement the contract, validator, and additive propagation.** Keep `WorkerCallResult.Ok/Fail` and unrelated `OperationBatchResult` call sites source-compatible through optional/init properties. Make omission explicit rather than relying on the presentation serializer's global null policy. Leave `WorkerProtocol.RequiredCapabilities` byte-for-byte unchanged.
- [ ] **Step 5: Run the Step 3 filter to GREEN.** Confirm the current host, worker, and FakeWorker still negotiate the pre-I2 capability set.
- [ ] **Step 6: Review and commit.** Run `git diff --check`, inspect only Task 1 files, stage them explicitly, inspect `git diff --cached --check` and `git diff --cached --stat`, then commit `feat: define typed block import outcomes`.

### Task 2: Record the import boundary and independent final read

**Files:** Create `TiaMcpServer.OpennessWorker/Openness/BlockImportInvocationBoundary.cs`, `BlockSourceArtifactTracker.cs`, `BlockCompileObservation.cs`, `BlockImportOutcomeProjection.cs`, and `BlockImportDiagnosticSanitizer.cs`; modify `BlockImportResult.cs`, `BlockImportCoordinator.cs`, `BlockPostconditionEvidence.cs`, `BlockPostconditionVerifier.cs`, `BlockImporter.cs`, `ExternalSourceScope.cs`, and `CompileChecker.cs`; create `TiaMcpServer.Tests/Block/BlockCompileObservationTests.cs`, `BlockImportDiagnosticSanitizerTests.cs`, and `BlockImporterRouteSourceTests.cs`; modify `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`, `TiaMcpServer.Tests/Block/BlockImportCoordinatorTests.cs`, `BlockPostconditionVerifierTests.cs`, `BlockMutationPostconditionTests.cs`, and `CompileReportProjectionTests.cs`.

**Interfaces:** Change `BlockImportResult` to `BlockImportResult(string payload, BlockImportOutcomeInfo outcome, IReadOnlyList<string>? warnings)`. Preserve the staged-XML entry and add a source entry, but route both through one private outcome runner. `BlockImportInvocationBoundary` is a typed lifecycle recorder with single-use ordered `BeforeSiemensCall()`, `AfterSiemensCallReturned()`, and `RecordReturnedResult(BlockImportReturnedState)` methods; the closed internal enum has only `Success` and `NonSuccess`. Ordinary XML brackets `target.Group.Blocks.Import` and then records success; SIMATIC SD brackets `ImportFromDocuments`, marks return before inspecting its state, then maps that state to the closed enum; source brackets `GenerateBlocksFromSource`, records success, and only then cleans up. The coordinator snapshots this recorder to derive target not-started/false/unavailable, unknown/null/unavailable, or completed/true/success-or-non-success even when route code later fails. Invalid ordering or duplicate markers fail closed in Siemens-free tests.

`BlockSourceArtifactTracker` is a second typed recorder with ordered `BeforeCreateFromFile()`, `AfterCreateFromFileReturned()`, and `AfterDeleteAttempt(bool removed)` markers. Add a source-compatible optional observer overload to `ExternalSourceScope.Create`; keep the existing `PlcTypeImporter` call and behavior unchanged. Mark immediately around project-node creation and from `Dispose`; creation throw snapshots unknown, confirmed deletion snapshots removed, and failed/unconfirmed deletion snapshots residue-possible. The final source outcome combines both recorder snapshots; neither may overwrite the other.

Add internal `CompileChecker.CompileObserved` output containing the unchanged P1 report when available plus structural target-selection/session/invocation availability. Keep public `CompileChecker.Compile` response-compatible. `BlockCompileObservation` uses no prose: pre-report failure yields unavailable/null report; any unavailable invocation yields unavailable with any partial report; otherwise errors yield failed; warning-only and clean results yield succeeded. Keep current block/named/all-PLC selection/order. `BlockImportOutcomeProjection` copies/sanitizes under every cap.

Extend `BlockPostconditionEvidence` with compile and independent final-resolution/export evidence while retaining the old create-block constructor. Without editing `BlockExporter*.cs`, freshly resolve the target, then invoke the exact-format verifier once; preserve reliable presence even if export later fails. `BlockImportDiagnosticSanitizer` maps import, generation, source-node creation/deletion, staging cleanup, and verification exceptions to closed bounded summaries and sanitizes all carried warnings. It never forwards exception messages. `VerifyImport` returns or throws `postcondition_failed` with the same outcome; legacy `Verify` remains unchanged.

Link the Siemens-free boundary, source-artifact tracker, compile observation, outcome projection, and sanitizer sources into `TiaMcpServer.Tests.csproj`. Do not link Siemens-dependent `BlockImporter`, `ExternalSourceScope`, or `CompileChecker`; their exact wiring is guarded by source-contract tests plus the serial stub/real-reference worker builds and live gate.

- [ ] **Step 1: Write exact target-boundary REDs.** Cover preflight refusal; ordinary `Blocks.Import` return/throw; SD return with success/non-success state; and source generation return/throw. Assert false/not-started before a call, null/unknown after a thrown call, and true/completed after normal return; ordinary XML and source normal returns map to success, SD maps its returned state to success/non-success. Reject result-before-return, duplicate start/return/result, and completed-without-result snapshots. Assert one target attempt, no retry/rollback, compile only after target return, and one final observation after every started target attempt.
- [ ] **Step 2: Write source-artifact and sanitizer REDs.** Cover not-applicable XML; local staging failure before `CreateFromFile` (`not_created`); creation throw (`unknown`); confirmed delete (`removed`); and swallowed delete failure (`residue_possible`). Reject delete-before-create and duplicate lifecycle markers. Generation throw must still carry the final source-node state and must not emit a zero generated-count warning. Feed path-bearing import, generation, creation, deletion, staging-cleanup, and verification exceptions through coordinator cases; assert fixed bounded error/warning text with no raw message, exception type, drive/UNC/temp path, node name, or submitted content. Retain controlled generated-count/cleanup warnings only when their facts are known.
- [ ] **Step 3: Write compile aggregation/projection REDs.** Cover exact block, named PLC, and no-qualifier/all-PLC observations; no PLC, target-resolution failure, propagated session/compiler failure before a report; clean success, warning-only success, compile error, unavailable, and mixed error-plus-unavailable precedence. In the mixed case PLC A is structurally unavailable, PLC B reports errors, aggregate stage is unavailable, and the partial report retains both identities/totals without prose parsing. Cover all component caps, redaction, and escape-heavy projection at or below 12,000 serialized characters.
- [ ] **Step 4: Write three-route source-contract and verifier REDs.** Locate and prove marker ordering around all three Siemens target calls in `BlockImporter.cs`; prove `CreateFromFile` lifecycle callbacks in `ExternalSourceScope.cs`; prove ordinary XML, SD, and source use the common final-reader. Label this as static wiring evidence. Add verifier cases for nullable unavailable compile report, target absence/read failure, relation always unknown, and unchanged create-block behavior.
- [ ] **Step 5: Run RED.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -m:1 /p:UseTiaPortalReferenceStubs=true --filter "FullyQualifiedName~BlockImportCoordinatorTests|FullyQualifiedName~BlockCompileObservationTests|FullyQualifiedName~BlockImportDiagnosticSanitizerTests|FullyQualifiedName~BlockImporterRouteSourceTests|FullyQualifiedName~BlockPostconditionVerifierTests|FullyQualifiedName~BlockMutationPostconditionTests|FullyQualifiedName~CompileReportProjectionTests"
```

Expected: current code lacks target/source trackers and a sanitizer, ordinary XML is not bracketed, compile availability exists only in prose, and final read is skipped after some failures.

- [ ] **Step 6: Implement all three target boundaries and the source-artifact lifecycle.** Run preflight before markers; bracket only the exact Siemens calls. Keep the optional `ExternalSourceScope` observer backward-compatible for type import. Preserve existing closed failure-category precedence, source lifecycle evidence, and only factually known warnings. Compile only after a normal target return; after every started target attempt, freshly resolve/export once. Never compare export to request.
- [ ] **Step 7: Implement structural compile observation, projection, and diagnostic sanitization.** Preserve public P1 behavior, aggregate current selected PLCs, permit null report for pre-report unavailability, and project a copy. Route every existing coordinator error/warning through the named sanitizer before constructing `WorkerOperationException` or `BlockImportResult`; never mutate original reports or parse note strings.
- [ ] **Step 8: Run the Step 5 filter to GREEN, then the surrounding block slice and worker stub build.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -m:1 /p:UseTiaPortalReferenceStubs=true --filter "FullyQualifiedName~BlockImport|FullyQualifiedName~BlockPostcondition|FullyQualifiedName~BlockMutationPostcondition|FullyQualifiedName~CompileReportProjection"
dotnet build TiaMcpServer.OpennessWorker/TiaMcpServer.OpennessWorker.csproj -m:1 /p:UseTiaPortalReferenceStubs=true
```

- [ ] **Step 9: Review and commit.** Confirm the shared `ExternalSourceScope` overload leaves type-import callers/behavior unchanged and no creation, safety, or exporter file changed; inspect all three target markers and source-node lifecycle markers; run `git diff --check`; stage only Task 2 files and commit `fix: report truthful block import stages`.

### Task 3: Preserve outcomes across worker success, failure, and transport ambiguity

**Files:** Create `TiaMcpServer.OpennessWorker/BlockUpdateOutcomeDecorator.cs`, `TiaMcpServer/Worker/BlockImportOutcomeSynthesizer.cs`, `TiaMcpServer.Tests/Worker/BlockUpdateOutcomeDecoratorTests.cs`, `TiaMcpServer.Tests/Worker/UpdateBlockLogicWorkerSourceContractTests.cs`, and `TiaMcpServer.Tests/Batch/BlockUpdateDispatchProvenanceTests.cs`; modify `TiaMcpServer.Contracts/WorkerProtocol.cs`, `TiaMcpServer.OpennessWorker/Program.cs`, `TiaMcpServer/Worker/WorkerCallResult.cs`, `TiaMcpServer/Worker/OpennessWorkerClient.cs`, `TiaMcpServer/Batch/BatchWorkerInvoker.cs`, `TiaMcpServer.FakeWorker/Program.cs`, `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`, `TiaMcpServer.Tests/Worker/WorkerProtocolHandshakeTests.cs`, `OpennessWorkerClientIntegrationTests.cs`, and `TiaMcpServer.Tests/Batch/WriteBatchToolsBehaviorTests.cs`.

**Interfaces:** `BlockUpdateOutcomeDecorator` is the single Siemens-free worker helper that builds format-aware not-started and unknown synthetic outcomes and copies them onto a failed `WorkerResponse` or `WorkerOperationException` without changing its closed category. Program applies it at two actual boundaries: `HandleLine` decorates the returned `WorkerOperationAuthorization.Authorize` denial when `request.Method == "update_block_logic"`, before returning it; `UpdateBlockLogic` wraps validation, format normalization, and its entire `WithProject` call to decorate parameter/format, project-open/access/no-project, and expected-identity failures. Set `importerEntered` immediately before `BlockImporter.Import`; an unannotated exception after that point gets conservative unknown rather than false. Other methods and the shared authorization helper remain unchanged. Sanitize public text through the Task 2 sanitizer.

Extend `Program.RawPayload` with optional outcome; attach `BlockImportResult.Outcome`; copy an exception outcome into `WorkerResponse`. Add internal `WorkerDispatchState` to `WorkerCallResult` and set it at the actual host seams. Relative to the update request, configured-project verification failure, binding conflict, pinned-binding drift, host access-policy denial, `Win32Exception` before process start, protocol/capability-handshake mismatch, and batch format rejection are `NotSent`; a received worker response is `Sent`; timeout/crash/EOF/malformed transport is `Unknown`. When prerequisite `get_project_status` fails, override that prerequisite call's state with `NotSent` before returning it as the update result. `BlockImportOutcomeSynthesizer` maps only this typed state plus normalized-format-or-null to the locked synthetic outcomes.

`OpennessWorkerClient.UpdateBlockLogicAsync` passes the exact normalized request format to the validator for every received outcome and synthesizes only when the outcome is missing/untrusted. `BatchWorkerInvoker` attaches the null-format `NotSent` outcome when format normalization rejects before invoking the client. Missing/invalid completed responses become `protocol_error` with conservative unknown evidence; timeout/crash/EOF retains its existing category with unknown target/source/compile evidence. No case derives dispatch or stage from message/category text. Append `typed-block-import-outcome-v1` to `WorkerProtocol.RequiredCapabilities` only after worker emission, host validation/synthesis, and all dispatch-state assignments are implemented in this same task; do not commit an intermediate head that advertises partial support.

- [ ] **Step 1: Add worker pre-import refusal REDs.** Unit-test the decorator with parameter/invalid-format, access-denied, no-project, project-open failure, and expected-identity mismatch inputs: preserve category/warnings, sanitize text, and attach target not-started/false plus format-appropriate source state. Add source-contract assertions that `HandleLine` decorates only the returned `update_block_logic` authorization denial before the dispatch switch, `UpdateBlockLogic` wraps validation/normalization and every returned/thrown `WithProject` failure, and `importerEntered` is set immediately before `BlockImporter.Import`; after-entry unannotated failure must be unknown.
- [ ] **Step 2: Add capability, dispatch-provenance, FakeWorker, and host REDs.** Add `ProtocolRequiresTypedBlockImportOutcomeCapability` and legacy mode `missing-block-import-outcome-capability`; assert the stale script records only `hello`, never the original method/target/content. Prove each `NotSent` seam separately: invalid format in `BatchWorkerInvoker`; configured-project verification failure; binding conflict; pinned-binding drift; host access-policy denial; worker `Win32Exception` before start; and protocol/capability-handshake mismatch. Prove a received worker response is `Sent` and timeout/crash/EOF is `Unknown`; never use category/message matching. Add all worker pre-import categories with typed not-started outcomes; valid success; valid `postcondition_failed`; unavailable compile with partial report; success/failure without outcome; invalid combination; escape-oversized outcome; and transport loss after request receipt. Assert valid categories/outcomes survive, invalid/missing received outcomes become `protocol_error`, every `NotSent` case synthesizes false/not-started with the exact format state, invalid format uses unknown source state, ambiguous transport synthesizes target null/unknown with unavailable/null compile and unknown source state, and every scenario sends at most one update request.
- [ ] **Step 3: Add registered apply/audit REDs.** `ApplyWriteBatch_UpdateBlockLogicFailure_EmitsOutcomeAndSkipsLaterItem` previews an unchanged two-item batch and asserts typed partial evidence plus the skipped second item. Parse the audit JSONL, recompute the complete formatted-result hash, and compare `resultHash`; assert only configured bound/redaction for truncated `resultPreview`. Confirm submitted content/paths are absent and existing token, target/input/current-state hashes, result text, warnings, category, and line count remain compatible.
- [ ] **Step 4: Run RED.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -m:1 /p:UseTiaPortalReferenceStubs=true --filter "FullyQualifiedName~BlockUpdateOutcomeDecoratorTests|FullyQualifiedName~UpdateBlockLogicWorkerSourceContractTests|FullyQualifiedName~BlockUpdateDispatchProvenanceTests|FullyQualifiedName~WorkerProtocolHandshakeTests|FullyQualifiedName~OpennessWorkerClientIntegrationTests|FullyQualifiedName~WriteBatchToolsBehaviorTests"
```

Expected: pre-import worker refusals lack outcomes; dispatch provenance/synthesis and the required capability do not exist; the outcome is dropped on failure/success; malformed combinations are trusted; missing outcomes are silently accepted; or audit content is not asserted.

- [ ] **Step 5: Implement pre-import decoration, response propagation, typed dispatch provenance, synthesis, and fail-closed validation.** Preserve binding invalidation, valid worker category, warnings, result text, request count, safety-token use, and audit flow. Assign `NotSent` only at the enumerated control-flow seams, `Sent` only after a response, and otherwise `Unknown`; synthesize from the typed value and format only. For a received invalid/missing outcome, replace untrusted category/evidence with `protocol_error` plus conservative unknown; never echo it. After worker emission, host enforcement/synthesis, and provenance are complete, append `typed-block-import-outcome-v1` to the shared required capabilities as the final implementation change in this task. Reuse the existing handshake; do not edit `PersistentWorkerTransport.cs`.
- [ ] **Step 6: Run the Step 4 filter to GREEN, then Tasks 1-3 together.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -m:1 /p:UseTiaPortalReferenceStubs=true --filter "FullyQualifiedName~BlockImportOutcomeInfoTests|FullyQualifiedName~BlockImportCoordinatorTests|FullyQualifiedName~BlockPostconditionVerifierTests|FullyQualifiedName~BlockUpdateOutcomeDecoratorTests|FullyQualifiedName~UpdateBlockLogicWorkerSourceContractTests|FullyQualifiedName~BlockUpdateDispatchProvenanceTests|FullyQualifiedName~WorkerProtocolHandshakeTests|FullyQualifiedName~OperationBatchKernelTests|FullyQualifiedName~OpennessWorkerClientIntegrationTests|FullyQualifiedName~WriteBatchToolsBehaviorTests"
```

- [ ] **Step 7: Review and commit.** Check every pre-import category remains intact with not-started evidence; every host `NotSent` result is proven at its seam; prerequisite status calls cannot make an unsent update look sent; unrelated methods omit the member; no crash/timeout is labeled committed; invalid evidence is never echoed; the capability first appears in this complete wiring commit; and audit shape changes only by the bounded field. Run `git diff --check`, stage only Task 3 files, and commit `fix: preserve block import outcome across IPC`.

### Task 4: Document the non-atomic outcome and run I2 gates

**Files:** Modify `docs/SupportedOperations/IMPORT_EXPORT_OPTIONS_SUMMARY.md`.

**Interfaces:** Document the exact `operations[].blockImportOutcome` contract defined in Task 1,
including the required worker capability and omission on unrelated operations; do not add a second
response shape, rename an existing operation field, or imply that target commitment means
save/download/persistence.

- [ ] **Step 1: Update maintained documentation.** Document `typed-block-import-outcome-v1`, `operations[].blockImportOutcome`, every stage/result/source-artifact value, nullable target-commitment/presence semantics, the conservative meaning of a normally returned target call, whole-report compile aggregation and bounds, warning-only compile success, `ContentRelation=unknown`, omission for unrelated operations, stop-later behavior, and the absence of rollback/save/download. State that existing `result`, `warnings`, and `failureCategory` field shapes and category semantics remain compatible, while unsafe exception-derived message text is deliberately replaced by bounded summaries. State that there is no new standalone direct-update envelope.
- [ ] **Step 2: Run complete serial offline/reference/package gates.**

```powershell
dotnet restore TiaMcpServer.sln
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet build TiaMcpServer.sln -m:1 /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
$i2PackageDir = Join-Path 'artifacts' ('i2-package-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $i2PackageDir | Out-Null
dotnet pack TiaMcpServer/TiaMcpServer.csproj -c Release -m:1 /p:UseTiaPortalReferenceStubs=true -o $i2PackageDir
$i2Packages = @(Get-ChildItem -LiteralPath $i2PackageDir -Filter '*.nupkg')
if ($i2Packages.Count -ne 1) { throw "Expected exactly one I2 package, found $($i2Packages.Count)." }
pwsh -NoProfile -File scripts/verify-doctor-package.ps1 -PackagePath $i2Packages[0].FullName
git diff --check
```

If installed V21 references are unavailable, record that limitation; do not relabel the stub build as Siemens API proof.

- [ ] **Step 3: Perform branch self-review and independent review.** Check every spec sentence against a task; validate the exact whole-file allowlist, additive JSON compatibility, outcome bounds, sanitization, access/safety/audit non-effects, and no changes outside I2. Resolve all findings and rerun affected focused/full gates.
- [ ] **Step 4: Commit maintained documentation.** Stage only `docs/SupportedOperations/IMPORT_EXPORT_OPTIONS_SUMMARY.md`, inspect the staged diff, and commit `docs: describe truthful block import outcomes`.

## Separately Authorized Live Acceptance

I2 requires mutating V21 acceptance before merge because offline/FakeWorker evidence cannot prove Siemens import side effects or a fresh post-read. The future orchestrator freezes the reviewed I2 head after any ready T2 merge and current-main refresh, records base/head/tree hashes, reruns the complete offline gate, and closes the lane if any byte changes.

With fresh authorization for one exact disposable project and both exact reviewed target updates, referred to in tracked documentation only as `Fixture A`:

1. Capture verified project identity/status, exact-format baselines, target presence, and compile state for an ordinary Simatic ML target and a source-format-capable target. Do not save or download.
2. Preview and apply one ordinary XML update that reaches `target.Group.Blocks.Import` and is known to import but fail compilation. Use the unchanged operations list and token.
3. Confirm `postcondition_failed`, completed/success, `TargetMutationCommitted=true`, `TemporarySourceState=not_applicable`, bounded compile evidence, one independent final read, later-item skip, and the expected audit hash/preview. Independently re-read, then restore that target from its frozen baseline before continuing.
4. Preview and apply one reviewed source-format update that reaches `GenerateBlocksFromSource` and returns before the intended compile failure. Confirm completed/success, `TargetMutationCommitted=true`, `TemporarySourceState=removed`, bounded compile evidence, one independent final read, no temporary source residue, later-item skip, and the expected audit evidence.
5. Independently re-read both targets and compile state through maintained tools. Do not claim requested-content equality from a nonempty export.
6. Restore the source target from its frozen baseline through a separately previewed/applied write, or discard the disposable project. Record final identity/status, absence of temporary source residue, and restoration/discard evidence.

SIMATIC SD `ImportFromDocuments` is covered by its marker/state tests and real-reference build. If the authorized fixture has no reviewed SD target, record that route as not live-qualified rather than implying otherwise; do not invent a third mutation.

A timeout, crash, or lost response requires inspection of project state before any replay. This plan authorizes no live operation and no automatic retry or rollback.

## Pull-Request and Integration Gate

- PR title: `fix(plc): report truthful block import outcomes`.
- PR body uses `Refs #72` until the exact frozen commit completes and passes the authorized live scenario. Use `Closes #72` only after that evidence is reviewed and accepted.
- Base branch is `main`; I2 is never stacked on T2 or an unmerged feature branch.
- No wave-wide I2/T2 live barrier applies. T2 may merge independently. Before I2 freezes for live evidence, incorporate then-current `main`, rerun focused/full/reference/package gates, and obtain independent review of the refreshed diff.
- Once I2 is frozen or live authorization is pending, do not merge another lane until I2 either merges or its gate is explicitly closed.
- After merge, pull current `main`, rerun the serial stub build/full tests and the focused I2 slice, inspect the merge result, then release worker `Program.cs` and the other hotspots for B2 planning.
