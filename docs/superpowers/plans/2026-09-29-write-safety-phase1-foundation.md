# Write-Safety Redesign Phase 1 — Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the guarded write pipeline of the [write-safety redesign](../specs/2026-09-29-write-safety-redesign-design.md) beside the token flow, add `contentHash` to the content reads, and run the elicitation / approval-marker spike.

**Architecture:** A generic `WriteExecution` runs one call through validate → verified binding → pinned lease → plan (resolve every item) → guards → dry-run stop → mutate → verify → audit → report. A domain plugs in through `IWriteDomain`. The binding lease sits behind `IWriteBindingGate`, so the pipeline is unit-testable with fakes and end-to-end testable through the real client against FakeWorker. No registered tool moves onto the pipeline in this phase.

**Tech Stack:** .NET 10 host, netstandard2.0 contracts, ModelContextProtocol C# SDK 2.2.0, xunit 2.9, FakeWorker.

## Global Constraints

- Build: `dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true` from **PowerShell** (Bash mangles `/p:`).
- Tests: `dotnet test TiaMcpServer.Tests` (filter with `--filter FullyQualifiedName~<Name>`).
- New host code lives in `TiaMcpServer/Safety/Pipeline/`, namespace `TiaMcpServer.Safety.Pipeline`. The test project links it with one wildcard `<Compile Include>` (`Link="Host\Safety\Pipeline\%(Filename)%(Extension)"`); tests go in `TiaMcpServer.Tests/Safety/Pipeline/`.
- The worker (`TiaMcpServer.OpennessWorker`) is not touched.
- The only client-visible change is the additive `contentHash` on `execute_read_batch`, plus the spike probes, which exist only while `TIA_MCP_APPROVAL_SPIKE=1` is set and are deleted in Task 13.
- All new JSON goes through `CanonicalJson` (camelCase, explicit nulls). Response text and `structuredContent` come from one serialization (`StructuredToolResult.CreateCanonical`).
- Commit messages: one line, conventional type, no body, no trailers.
- A new document under `docs/` is listed in `docs/README.md`; `README.md` links are absolute GitHub URLs.

## Decisions this plan implements (spec, decided 2026-09-29)

- **Acknowledgement (§4.3).** Malformed `acknowledge` (blank, duplicate, unknown id, `info`/`block` id, id that did not fire) → `phase: error`, `validation_error`. A fired `acknowledge` guard missing from the list, or any `block` guard → `phase: blocked`, new category `guard_blocked`, `isError: true`. A dry run never blocks and reports `acknowledged: true|false`. One id covers every firing of that guard.
- **Two passes (§4.1).** Plan every item and evaluate every guard before any mutation. Items planned as depending on an earlier item are re-planned just before their own mutation; a late unacknowledged `acknowledge` guard or any late `block` guard fails that item with `guard_blocked` and stops the call.
- **Content hash (§4.5).** `xml:sha256:<hex>` / `source:sha256:<hex>` over the served text; format mismatch → `validation_error`; content drift → `state_changed`. Omitted on `withDependencies` reads and when the result was truncated or omitted for size.
- **Audit (§4.8).** One record per call (every phase) in `writes-yyyy-MM-dd.jsonl`, UTC date, UTF-8 without BOM, `recordKind: "write"`, `recordVersion: 1`.
- **Spike.** Clients: Claude Code, Codex Desktop, MCP Inspector. Probes are deleted after the results are recorded; the helpers stay for Phases 1b and 2.

## File structure

```
TiaMcpServer/Safety/Pipeline/
  WriteVocabulary.cs        WritePhases, WriteGuardSeverities, GuardSatisfactions
  WriteGuards.cs            WriteGuardDefinition, FiredGuard, WriteGuardReport, WriteGuardCatalog
  GuardDecisions.cs         GuardDecisionKind, GuardDecision, GuardDecisions
  ContentHashes.cs          CheckedPrecondition, ContentHashCheck, ContentHashes
  WriteRecords.cs           WriteToolError, WriteEffect<T>, WriteCall<T>, WriteValidation, ItemPlan<T>, WritePlan<T>, ItemReplan<T>, WriteReport<TE,TV>
  IWriteDomain.cs           the domain seam
  IWriteBindingGate.cs      WriteLeaseResult<T>, IWriteBindingGate, OpennessWriteBindingGate
  WriteAudit.cs             WriteAuditGuard, WriteAuditItem, WriteAuditRecord, IWriteAuditSink, JsonlWriteAuditSink
  WriteExecution.cs         public entry point
  WriteRun.cs               internal per-call state machine (keeps WriteExecution small)
  UserConfirmation.cs       elicitation wrapper (spike helper, kept)
  ToolApprovalMarker.cs     anthropic/requiresUserInteraction marker (spike helper, kept)
TiaMcpServer/Batch/BatchContentHashes.cs      attaches contentHash to batch content reads
TiaMcpServer/Tools/ApprovalSpikeTools.cs      spike probes (Task 11, deleted in Task 13)
TiaMcpServer.Contracts/WorkerFailureCategories.cs   + GuardBlocked
```

## Contracts (signatures only)

```csharp
public sealed record FiredGuard(string Id, string? OperationId, string Message);   // severity comes from the catalog
public sealed record WriteGuardReport(string Id, string Severity, string? OperationId, string Message, bool? Acknowledged);
public sealed record CheckedPrecondition(string Name, string Expected, string Actual, bool Satisfied);
public sealed record ItemPlan<TEffect>(TEffect? Effect, string? DependsOn, IReadOnlyList<CheckedPrecondition> Preconditions);
public sealed record WriteReport<TEffect, TVerification>(string Phase, bool Success, WriteToolError? Error, IReadOnlyList<string> Warnings, IReadOnlyList<WriteGuardReport> Guards, IReadOnlyList<WriteEffect<TEffect>> Effects, StructuredOperationBatch? Batch, TVerification? Verification);
```

`IWriteDomain<TItem, TEffect, TVerification, TResponse> where TItem : IOperationBatchItem`:
`ToolName`, `ContractVersion`; `WriteValidation Validate(items, McpAccessMode)`; `Task<WritePlan<TEffect>> PlanAsync(projectPath, items)`; `IReadOnlyList<FiredGuard> EvaluateGuards(items, plans)` (pure); `Task<ItemReplan<TEffect>> ReplanAsync(projectPath, item)`; `Task<WorkerCallResult> MutateAsync(projectPath, item)`; `StructuredOperationItem Project(item, WorkerCallResult)`; `Task<TVerification?> VerifyAsync(projectPath, StructuredOperationBatch)`; `TResponse Compose(WriteReport<TEffect, TVerification>)`.

`IWriteBindingGate`: `McpAccessMode AccessMode`; `ProjectBindingSnapshot CurrentBinding`; `Task<WorkerCallResult> RequireVerifiedWriteBindingAsync(string? projectPath)`; `Task<WriteLeaseResult<T>> RunUnderLeaseAsync<T>(ProjectBindingSnapshot, Func<Task<T>>) where T : class`.

Also: `WriteExecution(IWriteBindingGate, IWriteAuditSink, WriteGuardCatalog, TimeProvider)` with `Task<CallToolResult> RunAsync<…>(IWriteDomain<…> domain, WriteCall<TItem> call)`; `GuardDecisions.ValidateAcknowledgeList(ack, catalog) → string?`, `Decide(fired, ack, catalog, dryRun)`, `DecideLate(fired, ack, catalog)`; `ContentHashes.Compute(format, content)`, `Check(expected, writeFormat, freshContent) → ContentHashCheck`, `internal Sha256Hex(text)`; `UserConfirmation(bool clientSupportsElicitation, Func<ElicitRequestParams, CancellationToken, ValueTask<ElicitResult>> elicit, TimeSpan timeout)`, `static For(McpServer, TimeSpan)`, `Task<UserConfirmationResult> AskAsync(string message, CancellationToken)`; `ToolApprovalMarker.Apply(McpServerTool) → McpServerTool`.

## Data flow

1. **Before any worker call:** empty list, duplicate `operationId`, `domain.Validate`, `ValidateAcknowledgeList` → `phase: error`.
2. **Gate:** `RequireVerifiedWriteBindingAsync`; failure → `phase: error` (`binding_conflict`).
3. **Lease:** `RunUnderLeaseAsync(CurrentBinding, …)`; refusal → `phase: error`. Everything below runs inside it, including the audit append.
4. **Plan:** `PlanAsync` returns one `ItemPlan` per item or fails the call (`target_not_found`, `target_ambiguous`, …). A count mismatch is a programming error (throw).
5. **Guards:** `Decide(EvaluateGuards(...))` → `Invalid` (error), `Blocked` (blocked), else continue. Dry run → `phase: preview`, `success: true`, no batch.
6. **Mutate:** sequential; a dependent item is re-planned, then its guards pass through `DecideLate`; the first non-succeeded item stops the call and later items are `skipped`. In a multi-item call the failed item gets the `partial_write_no_rollback` guard and warning (`WriteExecution.PartialWriteMessage`).
7. **Verify, compose, audit:** `VerifyAsync`, `Compose` → one canonical text → audit record (response + `sha256:` hash, per-item target, preconditions, status, duration) → `CallToolResult` with `isError` true only for `error` and `blocked`. Top-level `warnings` = messages of fired `info` guards.

---

### Task 1: Vocabulary, guard records, catalog, `guard_blocked`

**Files:** Create `WriteVocabulary.cs`, `WriteGuards.cs`. Modify `WorkerFailureCategories.cs` (const + `Known` entry) and `TiaMcpServer.Tests.csproj` (wildcard link). Test `WriteGuardCatalogTests.cs`.

**Produces:** `WritePhases.{Preview,Applied,Blocked,Error}`, `WriteGuardSeverities.{Info,Acknowledge,Block}` + `IsKnown`, `GuardSatisfactions.{Agent,User}`, `WriteGuardCatalog(IEnumerable<WriteGuardDefinition>)` (always prepends `partial_write_no_rollback`/info), `.Production`, `TryGet`, `Get` (throws `InvalidOperationException` for unknown ids).

- [ ] Tests first: production contains the partial-write guard as info; domain guard found with severity; blank id, unknown severity, duplicate id, and redefining the pipeline guard throw `ArgumentException`; `Get` of unknown id throws; `guard_blocked` is known and accepted by `WorkerCallResult.Fail`.
- [ ] Run → fail; implement; run → pass; build solution.
- [ ] Commit `feat: add write guard vocabulary and catalog`.

### Task 2: Guard decisions

**Files:** Create `GuardDecisions.cs`. Test `GuardDecisionsTests.cs`.

**Consumes:** Task 1. **Produces:** `GuardDecisionKind {Proceed, Blocked, Invalid}`, `GuardDecision(Kind, Guards, Message)`; `Decide` = not-fired check + `DecideLate` + dry-run downgrade.

- [ ] Tests: list validation (null/empty ok; blank, duplicate, unknown, info id, block id rejected with distinct messages); `Decide` for no guards, info (`acknowledged` null), unacknowledged (Blocked, message names id), acknowledged (true), block despite acks, not-fired ack (Invalid), one ack covering two firings, dry run never blocks, dry run still rejects not-fired ack; `DecideLate` ignores acks for unfired guards and blocks an unacknowledged late guard; report severity comes from the catalog.
- [ ] Red → implement → green. Commit `feat: add acknowledge rule for write guards`.

### Task 3: Content hashes

**Files:** Create `ContentHashes.cs`. Test `ContentHashesTests.cs`.

- [ ] Tests: `Compute` against the SHA-256 vector for `"abc"` (`ba7816bf…15ad`) in both formats; unknown format throws; `Check` → missing/blank and malformed (no tag, wrong algorithm, upper-case or short hex, unknown format) are `validation_error` with no evidence; cross-format names both formats; match succeeds with satisfied evidence; drift is `state_changed` with the actual hash.
- [ ] Red → implement (reuse `SourceFormatNames.Allowed`) → green. Commit `feat: add format-tagged content hash`.

### Task 4: `contentHash` on content reads

**Files:** Modify `OperationBatchResult.cs` (`string? ContentHash { get; init; }`), `OperationBatchResultFormatter.cs` (project it, `WhenWritingNull`), `OperationBatchPayloadBudget.cs` (every `with` that rewrites `Result` also clears `ContentHash`), `BatchWorkerInvoker.cs` (`NormalizeFormat` → `internal`), `ReadBatchTools.cs` (attach between the read and the budget). Create `BatchContentHashes.cs` (`Attach(requests, results)`). Link the new file in the test csproj. Docs: `README.md` paragraph after the `withDependencies` paragraph; `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md` note under the read-operations table. Test `Batch/BatchContentHashTests.cs`.

- [ ] Tests: hash tagged with the served (normalized) format for both operations, with and without `format`; none for `withDependencies`, failed reads, or other operations; mismatched counts throw; budget truncation and omission clear it; formatter emits it only when present; FakeWorker `type-content-roundtrip` `get_type_content` returns `Compute("source", result)`.
- [ ] Red → implement → green; run the full `Batch` filter and fix any exact-shape assertion by computing the expected hash with `ContentHashes.Compute`.
- [ ] Commit `feat: return contentHash from content reads`.

### Task 5: Records, domain seam, binding gate

**Files:** Create `WriteRecords.cs`, `IWriteDomain.cs`, `IWriteBindingGate.cs`. Test `OpennessWriteBindingGateTests.cs`.

**Produces:** the contracts above; factories `WriteValidation.Valid/Invalid`, `ItemPlan.Resolved/DependsOnItem`, `WritePlan.Ok/Fail`, `ItemReplan.Ok/Fail`. `OpennessWriteBindingGate` maps `ExecuteWithPinnedBindingAsync` failures to `binding_conflict` and uses read-write when there is no access policy.

- [ ] FakeWorker tests (`network-subnet-lifecycle`, bound via `NetworkVerifiedWriteFixture.VerifyAsync`): a verified binding passes the gate and runs under the current snapshot; a snapshot taken before binding is refused without running the operation; an unbound session fails the gate with `binding_conflict`.
- [ ] Red → implement → green. Commit `feat: add write domain seam and binding gate`.

### Task 6: Audit sink

**Files:** Create `WriteAudit.cs`. Test `JsonlWriteAuditSinkTests.cs`.

**Produces:** `WriteAuditRecord` (`Kind`, `CurrentVersion`, `ModeName`), `WriteAuditItem` (`DurationMs` as `long?`), `JsonlWriteAuditSink(string? directory)`, `FileNameFor(DateTimeOffset)`. Append under a static lock; on failure write one line to stderr and never throw.

- [ ] Tests (use `TempAuditDirectory`): two records → two parseable lines in `writes-2026-09-29.jsonl`, first byte `{` (no BOM), `recordKind`/`recordVersion` present, legacy daily file untouched; `FileNameFor` uses the UTC date across midnight; a directory path that is a file is swallowed; `ModeName` spells `read-only`/`read-write`.
- [ ] Red → implement → green. Commit `feat: add write audit sink`.

### Task 7: `WriteExecution`

**Files:** Create `WriteExecution.cs`, `WriteRun.cs`. Test support `PipelineFakes.cs` (`FakeWriteItem`, `FakeEffect`, `FakeVerification`, `FakeWriteResponse`, a scriptable `FakeWriteDomain` with test guards `test_acknowledge`/`test_block`/`test_info`, `FakeWriteBindingGate` that records gate calls and whether a lease is active, `RecordingAuditSink`, `SteppingTimeProvider`). Test `WriteExecutionTests.cs`.

**Consumes:** Tasks 1–6.

- [ ] Tests, each asserting phase, `isError`, error category, text = `structuredContent`, and which domain calls happened:
  empty call and duplicate ids rejected before the gate; domain validation keeps its category; malformed acknowledge rejected before the gate; gate failure and refused lease stop before planning; plan failure mutates nothing; dry run reports effects and guards and mutates nothing; unacknowledged guard → blocked; acknowledged → applied, audit `satisfiedBy: "agent"`; block guard refuses despite acks; ack that did not fire → validation error; info guard lands in `warnings`; failed middle item → succeeded/failed/skipped, partial-write guard and warning, verify still called; single-item failure has no partial-write guard; dependent item re-planned after the earlier mutation (exact call order); late unacknowledged guard fails the dependent item with `guard_blocked`; every mutation runs inside the lease; the audit record carries kind, version, tool, contract version, access mode, project path, requested operations, response text and its hash, item target, preconditions, status, and durations above zero.
- [ ] Red → implement → green; keep every method under 50 lines. Commit `feat: add guarded write pipeline`.

### Task 8: End to end through FakeWorker

**Files:** Test `SubnetProbeDomain.cs` (test-only domain over `NetworkOperationRequest`: plan from `NetworkSafetySnapshot.ReadCurrentStateAsync`, exact `subnetId` match, guard `test_deletes_connected_subnet` (acknowledge) when connected nodes > 0, mutate via `NetworkWorkerInvoker.InvokeWriteAsync`, project via `NetworkPayloadContract.Project`, verify via subnet count). Test `WriteExecutionFakeWorkerTests.cs`.

- [ ] Tests on `network-subnet-lifecycle` with `subnet-eth-1`: dry run reports the guard and the subnet survives; apply without ack is blocked and the subnet survives; apply with ack deletes it, verification drops the count from 2 to 1, and the audit item names the subnet; unknown `subnetId` → `target_not_found`.
- [ ] Red → green. Commit `test: exercise write pipeline through fake worker`.

### Task 9: Architecture documentation

**Files:** `docs/ARCHITECTURE.md` §8: new subsection "Guarded write pipeline (write-safety redesign Phase 1)" that states no registered tool uses it yet and describes the stages, guards and acknowledgement, dry run, dependent items, audit stream, and `contentHash`.

- [ ] Full suite + solution build green. Commit `docs: describe guarded write pipeline`.

### Task 10: Spike helpers

**Files:** Create `UserConfirmation.cs` (`UserConfirmationOutcomes`: confirmed, declined, cancelled, timed_out, unsupported, failed), `ToolApprovalMarker.cs` (`MetaKey = "anthropic/requiresUserInteraction"`). Tests `UserConfirmationTests.cs`, `ToolApprovalMarkerTests.cs`.

Rules: no elicitation capability → `unsupported`, no request sent; only `accept` with `confirm: true` confirms; accept with false, empty or missing content (Codex auto-accept sends `{}`) → declined; `cancel`/other → cancelled; own timeout → timed_out; transport exception → failed; caller cancellation propagates. The request is one `BooleanSchema` field `confirm`, `Default = false`, listed in `Required`.

- [ ] Tests for every rule above, plus the request shape; marker sets `true`, returns the same tool, and keeps existing `Meta` keys.
- [ ] Red → green. Commit `feat: add user confirmation and approval marker helpers`.

### Task 11: Spike probes

**Files:** Create `TiaMcpServer/Tools/ApprovalSpikeTools.cs`: `EnvironmentVariable = "TIA_MCP_APPROVAL_SPIKE"`, `IsEnabled(Func<string, string?>)` (exactly `"1"`), `Create()` built with `McpServerTool.Create(MethodInfo, …)`, `spike_marked_probe` (marker applied, returns a fixed string), `spike_elicitation_probe` (`McpServer` injected, reports capability presence, outcome, detail; 2-minute timeout). Both read-only and never touch the worker. Modify `Program.cs`: when enabled, log one stderr line and `mcp.WithTools(ApprovalSpikeTools.Create())`, independent of access mode. Link the file in the test csproj. Test `ApprovalSpikeToolsTests.cs`.

- [ ] Tests: `IsEnabled` only for `"1"`; `Create` returns exactly the two probes and marks only the marker probe; the marker probe returns its fixed string.
- [ ] Red → green; build. Commit `feat: add approval spike probes`.

### Task 12: Manual client verification (maintainer)

Server: `TiaMcpServer\bin\Debug\net10.0\TiaMcpServer.exe --read-only` with `TIA_MCP_APPROVAL_SPIKE=1` (a stub build suffices; the probes never start TIA work).

- [ ] **Claude Code** (record `claude --version`, needs ≥ 2.1.199): `claude mcp add --env TIA_MCP_APPROVAL_SPIKE=1 --transport stdio tia-spike -- <exe> --read-only`. Call the marker probe in the default mode, with an allow rule for it, and in `bypassPermissions`; note whether it prompts and whether "don't ask again" is offered. Call the elicitation probe and answer accept+checked, accept+unchecked, decline, dismiss, and no answer for more than 2 minutes. Remove with `claude mcp remove tia-spike`.
- [ ] **Codex Desktop** (record version): `codex mcp add tia-spike --env TIA_MCP_APPROVAL_SPIKE=1 -- <exe> --read-only`, restart Codex Desktop, and run the same probes under the default approval policy and under full auto (Codex may auto-accept elicitations with `{}`). Remove with `codex mcp remove tia-spike`.
- [ ] **MCP Inspector** (record version): `npx @modelcontextprotocol/inspector -e TIA_MCP_APPROVAL_SPIKE=1 -- <exe> --read-only`. Confirm `_meta` on `spike_marked_probe` in `tools/list`; run the elicitation probe through each answer path.
- [ ] Record per client in spec Appendix A: version, elicitation declared, dialog shown, the outcome of each answer path, marker prompt per mode, and "don't ask again" offered. Commit `docs: record approval spike results`.

### Task 13: Remove probes and close Phase 1

- [ ] Delete `ApprovalSpikeTools.cs`, its test, its csproj link, and the `Program.cs` block; keep the helpers and their tests.
- [ ] Update the spec status (Phase 1 complete), add a completed entry at the end of `docs/IMPROVEMENT_LOG.md` (suite count, spike summary), and adjust `ARCHITECTURE.md` if the spike changes any statement.
- [ ] Full suite, solution build, and an independent review of the branch diff (code-reviewer, csharp-reviewer). Commit `chore: remove approval spike probes`.

## Out of scope

Moving any tool onto the pipeline (Phases 2–4); the `--user-approval` and `--confirm-with-user` switches and the `full` mode (Phase 1b); production guards other than `partial_write_no_rollback`; enforcing `expectedContentHash` on writes (arrives with the domain write tools).
