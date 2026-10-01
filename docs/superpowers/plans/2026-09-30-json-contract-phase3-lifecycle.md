# JSON Contract Phase 3: Guarded Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task after maintainer review and base revalidation. Steps use checkbox (`- [ ]`) syntax for tracking. Use subagents only when separately authorized by the active session.

**Goal:** Deliver JSON contract Phase 3 together with write-safety Phase 2: migrate all six lifecycle tools to guarded single-call writes, typed results/verification, and elicitation-backed acknowledgement.

**Architecture:** Build one lifecycle domain on the existing `WriteExecution` pipeline. Extend binding preparation explicitly for lifecycle transitions; keep ordinary project-write binding rules as the default. Connect the already-landed immutable confirmation options and `UserConfirmation` helper to the shared guard stage, then supply lifecycle resolution, effects, mutations, verification, and schemas.

**Tech Stack:** C#, host/tests net10.0, shared contracts netstandard2.0, Siemens worker net48, MCP form elicitation, canonical JSON, xUnit/FakeWorker, existing JSONL write audit sink.

**Spec:** [JSON contract roadmap Phase 3](../../roadmap/json-contract.md#phase-3-lifecycle-onto-the-guarded-write-pipeline); [accepted write-safety design](../specs/2026-09-29-write-safety-redesign-design.md), sections 4.1–4.8 and delivery Phase 2; [Phase 1b implementation record](2026-09-30-write-safety-phase1b-access-modes.md).

**Status:** Implementation authorized by the maintainer on 2026-10-01, including stepwise commits on the current `phase3-json-contract` branch. The GitHub connector confirmed fresh `main` at `6b10dc28812947fc802cf96c8abc6dfa8d981ffa` (merged Phase 2 PR #101). Phase 1/1b and the merged `StandaloneToolOutcome<TPayload>` contract are present. Phase 2's live evidence still excludes compiler-error reports, no-project status, and legacy lifecycle wire compatibility; this phase must verify its own producers. Exact-target live mutation and remote writes remain separately authorized gates.

**Offline completion (2026-10-01):** Code/test candidate `8f0e26f83db4faf6cd243fef7489f6419db3925b` passed all 4,640 tests with zero skips, the 80% coverage gate at 93.86% scoped line coverage, and serial Release builds with stubs and installed V21 references. The independent review's response-budget finding is fixed and verified. The [validation record](../acceptance/reports/2026-10-01-json-contract-phase3-offline-validation.md) records the evidence and frozen live matrix. Live acceptance is pending fresh exact-target authorization; no runtime, human-client, persisted-fixture, or restoration acceptance is claimed.

## Global Constraints

- Migrate exactly `open_project`, `create_project`, `save_project`, `save_project_as`, `archive_project`, and `close_project`. They require full mode at discovery, host, and worker.
- Replace public `confirm`/`safetyToken` with `dryRun: bool = false` and optional `acknowledge: string[]`. Keep other operation inputs and established lifecycle restrictions.
- Use the existing canonical seam and audit sink. Render once and audit the exact returned document/hash, including blocked and dry-run calls.
- Use `preview`, `applied`, `blocked`, and `error`. Rejection has top-level `error` and `isError: true`; an attempted write/verification failure has `success: false`, top-level `error: null`, and `isError: false`.
- With confirmation on, ignore the agent acknowledgement list and require form elicitation only for fired acknowledge-severity guards. Explicit `accept` plus `confirm: true` is required. Unsupported capability, decline, cancel, timeout, and transport failure deny with `access_denied`.
- With confirmation off, enforce the exact fired acknowledge-guard set and existing malformed-array rules. Hard block guards cannot be acknowledged away.
- Dry run resolves and reports guards/effects without a confirmation prompt or lifecycle mutation. It does not create a token or change the acknowledgement requirements of a later call.
- Preserve source/destination identity, binding revision, fail-closed recovery, worker-owned versus externally owned source behavior, and no read-driven switching/closing.
- Preserve the net10.0/net48 process boundary. Add no dependencies, vendor-specific approval marker, or multi-round-trip protocol implementation.
- Leave Network/batch tokens and their shared service in place until their designated phases. Release once at the redesign's final major-version gate.
- Live TIA mutations require fresh authorization for the exact disposable target. Use fixture aliases in tracked material; inspect unknown outcomes rather than replaying them.

## Review Focus

- An unbound session must be able to open/create, without weakening the default verified-binding gate for other writes.
- A project can change in the TIA UI while the user reads an elicitation prompt; re-resolve and re-evaluate before dispatch.
- Source recovery and save-as rebinding must pin the exact prepared revision and destination; stale requests must not be reinterpreted against a new project.
- Mutation can succeed while verification fails; do not report full success or suggest safe replay.
- Audit acknowledgement provenance must say `user` for elicitation and `agent` for the explicit switch-off path; never infer consent from a token or client auto-accept.

## Baseline Code Gaps Addressed

This section records the pre-phase baseline; Tasks 1–4 implement the corrections.

`WriteRun.ExecuteAsync` always calls `RequireVerifiedWriteBindingAsync` and its pinning logic expects a verified binding. That is insufficient for unbound open/create or source-to-destination lifecycle transitions. The domain adapter alone cannot work around the pipeline's pinning check.

`WriteRun` currently evaluates `GuardDecisions` using the agent array; `UserConfirmationOptions` is only registered. Its audit path labels satisfied guards as `agent`. The existing `UserConfirmation` helper already rejects accept-without-`confirm: true`, so integrate it rather than rewriting the SDK interaction.

The foundation currently carries an attempted item's error into `WriteReport.Error`, and the probe/fake domains copy it into top-level `error`. Correct this at the shared reporting seam: after dispatch, retain failures in typed outcomes; reserve top-level `error` for rejection.

The prose calls these "six lifecycle guards", but the accepted table has seven distinct lifecycle guard IDs. Implement and test the table's rules rather than dropping a rule to satisfy the prose count.

## Frozen Contract and File Responsibilities

- `TiaMcpServer/ProjectLifecycle/LifecycleWriteItem.cs`: internal single-operation request implementing `IOperationBatchItem`; public tools do not gain caller-supplied operation IDs or a batch input.
- `LifecycleWriteDomain.cs`: operation validation, planning, guard evaluation, worker mutation, typed projection, and verification.
- `LifecycleBindingStrategy.cs`: existing lifecycle-specific source grounding/recovery and exact snapshot preparation.
- `LifecycleEffects.cs`: typed source/destination target identities and current/requested consequences.
- `LifecycleWriteResponse.cs`: concrete schema for `tool`, `contractVersion`, `success`, `error`, `warnings`, `phase`, `guards`, `effects`, `result`, and `verification`. The initial contract version is `1.0`.
- `LifecyclePayloadContract.cs`: strict decoding of lifecycle and basic-status worker roots.
- `TiaMcpServer/Safety/Pipeline/IWriteBindingStrategy.cs`: explicit binding preparation seam used by the shared pipeline.
- `WriteConfirmationContext.cs`: per-call immutable options plus the current MCP client's `UserConfirmation` adapter.

Reuse Phase 2's merged `StandaloneToolOutcome<T>` for the single typed lifecycle result, including attempted failures and omissions. Verification is a typed outcome containing basic `ProjectStatusInfo`; no `operationResult` or `verification.result` JSON strings. The signatures below match the merged Phase 2 types and the implemented lifecycle seam.

Implemented pipeline entry point:

```csharp
Task<CallToolResult> RunAsync<TItem, TEffect, TVerification, TResponse>(
    IWriteDomain<TItem, TEffect, TVerification, TResponse> domain,
    WriteCall<TItem> call,
    WriteConfirmationContext? confirmation = null,
    IWriteBindingStrategy<TItem>? bindingStrategy = null,
    CancellationToken cancellationToken = default)
    where TItem : IOperationBatchItem;
```

`IWriteBindingStrategy<TItem>.PrepareAsync(WriteCall<TItem> call, CancellationToken cancellationToken = default)` returns `WriteBindingPreparation` with success/error and the exact `ProjectBindingSnapshot` to pin. The default implementation preserves today's verified-project gate and promotion checks. Only lifecycle registration supplies the lifecycle strategy; input JSON cannot select a weaker policy. Preparation does not acquire a second pipeline or bypass the pinned lease. A null confirmation context preserves opt-out behavior for unregistered foundation callers; production lifecycle calls always supply immutable startup options and a per-call confirmation adapter. `IWriteDomain.VerificationSucceeded` determines the composed success without promoting attempted failures to top-level rejection.

## Task 1: Lifecycle Binding Preparation

**Files:** New binding strategy types above; modify `WriteExecution.cs`, `WriteRun.cs`, and `IWriteBindingGate.cs` only where required. Add `TiaMcpServer.Tests/Project/LifecycleBindingStrategyTests.cs`; extend pipeline and existing project-rebind/concurrency tests.

- [x] Revalidate the plan against merged Phase 2 and verify Phase 1/1b are present. Freeze proposed type/signature names and inventory every old lifecycle binding test before changes.
- [x] Add failing tests for unbound open/create, verified same-project open, configured-source grounding, invalidated-source recovery, force-rebind refusal, externally owned source preservation, worker-owned source closing, and stale revision refusal before dispatch.
- [x] Add regression tests proving ordinary writes still require a verified current project and read-write cannot invoke lifecycle preparation/probes.
- [x] Implement explicit preparation and lease pinning. For open, distinguish source binding from destination path. For create, resolve the destination filesystem target without implicitly opening another project. Save/archive/close/save-as retain the appropriate active-source requirement.
- [x] Do not use a no-op binding gate or pretend an unbound source is verified. Keep the existing worker/client transition checks and pin the exact revision returned by recovery.
- [x] Run binding, worker restart/recovery, three-layer access, and lease-concurrency tests before proceeding.

## Task 2: Shared Elicitation Enforcement and Reporting

**Files:** New `WriteConfirmationContext.cs`; modify `WriteExecution.cs`, `WriteRun.cs`, `GuardDecisions.cs`, and audit composition as needed. Preserve `UserConfirmation.cs` unless a demonstrated outcome/cancellation issue requires a fix. Extend tests under `TiaMcpServer.Tests/Safety/Pipeline/`.

- [x] Add failing tests for default-on ignoring even malformed/supplied agent IDs, absent elicitation, accept-with-confirm, accept-with-empty/false confirmation, decline, cancel, timeout, request failure, and caller cancellation.
- [x] Add `DryRun_NeverPrompts_ReportsUnacknowledgedGuards`, `InfoOnlyWrite_NeverPrompts`, and `HardBlock_NeverPromptsOrMutates`. With the switch off, retain exact-set validation, unknown/duplicate/blank IDs, non-fired IDs, and info/block-ID rejection.
- [x] Pass the actual tool call's server/capability adapter and immutable startup options to the shared guard stage. Do not keep mutable client capability state in a global service.
- [x] Satisfy acknowledge guards through one prompt naming the concrete consequence/target; record accepted guard satisfaction as `user`. Off-mode acknowledgement records `agent`. Do not prompt to override a hard block.
- [x] Re-read exact target/state and re-evaluate guards after human acceptance, before mutation. If identity or acknowledged consequences changed, deny the stale call rather than silently broadening approval or replaying it. The lease does not prevent external TIA UI edits.
- [x] Add `AttemptedFailure_HasTypedFailure_ErrorNull_IsErrorFalse` and correct shared report construction. Verification failure must also make the composed call unsuccessful while preserving the mutation outcome and no-replay warning.
- [x] Prove one audit append per call, exact returned response/hash, true satisfaction provenance, and cancellation behavior. Audit and respond inside the existing lease where appropriate; never acquire the same non-reentrant lease again from confirmation/verification callbacks.
- [x] Run pipeline, confirmation, audit-isolation, and concurrency regressions serially.

## Task 3: Lifecycle Domain, Guards, and Public Tools

**Files:** New `TiaMcpServer/ProjectLifecycle/` files above; modify `TiaMcpServer/Tools/ProjectWriteTools.cs`, worker-client lifecycle paths where necessary, `TiaMcpServer.Contracts/ProjectLifecycleResultInfo.cs`, worker/FakeWorker producers, null-policy register tests, and project lifecycle tests.

- [x] Add a behavior matrix for each of the six public operations covering rejected input, dry run, successful dispatch, worker failure, malformed success payload, and typed verification failure.
- [x] Add a test for every accepted lifecycle guard ID: `closes_source_project` (info), `discards_unsaved_source_changes` (block), `discards_unsaved_changes` (acknowledge), `archive_without_save` (info), `archive_discards_restorable_data` (info), `archive_inside_project_folder` (block), and `target_exists` (block). Exercise target-exists on both create and save-as.
- [x] Keep destination-path canonicalization and existing `rebind:false`/archive-directory restrictions. Use fresh probes and filesystem checks for resolution; do not introduce a snapshot token or new `SafetyRead` operation.
- [x] Remove the lifecycle root's legacy-null marker only when all affected lifecycle/basic-status decode paths migrate together. Decode through `CanonicalJson.DeserializeWorkerPayload<ProjectLifecycleResultInfo>` and typed rebind state; reject semantically wrong operation/identity and missing or unreadable candidates.
- [x] Replace the six registered tools' return types/metadata and public parameters. Internally adapt each to one pipeline item. Keep mandatory worker-internal `Confirm=true` where still required by the worker; removing the public agent confirmation parameter does not remove the worker's mutation fence.
- [x] Produce typed effects naming current source and requested destination/consequence. Preserve ownership, saved-state, and no-project evidence rather than inventing status or metadata.
- [x] Verify open/create against the resulting bound destination; save/archive against the source; save-as against its required rebound destination; close against no open project. Read basic status without extended metadata and without a read reopening the project. Report failed verification explicitly.
- [x] Keep `probe_project_status_for_lifecycle`, `probe_open_project_rebind`, and `get_basic_project_status` because they feed guards/verification. Prevent dry-run planning from invoking a probe path that could open a project.
- [x] Run lifecycle, worker producer, metadata scope, access, path guard, and binding regressions, including old safety rules rewritten for the new flow.

## Task 4: Registered Protocol Surface and Targeted Retirement

**Files:** Modify `ToolOutputContractConformanceTests.cs`, `ToolAuthorizationAnnotationTests`/existing access metadata tests, the production MCP harness, FakeWorker scenarios, and lifecycle callers. Delete `TiaMcpServer/Tools/ProjectLifecycleTools.cs` after its callers migrate; remove only now-unused lifecycle token helpers/tests.

- [x] Add all six success and rejection probes on the full production surface. Exercise at least one actual applied call, not merely six dry-run responses.
- [x] Add protocol-level elicitation handlers for accept/decline/unsupported capability and a closed-with-unsaved-changes scenario. Prove confirmation on cannot be satisfied by the tool's `acknowledge` array.
- [x] Assert schemas, canonical representation equality, explicit nulls, no nested JSON strings, expected phases, and truthful `isError` on rejection versus attempted failures.
- [x] Remove exactly the six lifecycle names from the legacy register; after Phase 2, only the three batch tools remain legacy. Network/tree alignment stays with JSON Phase 4.
- [x] Assert input schemas no longer contain public `confirm` or `safetyToken`; all lifecycle tools remain destructive and full-only.
- [x] Delete the compatibility wrapper and lifecycle token paths after caller inventory shows they are unused. Preserve `WriteSafetyService`, `WriteSafetyTooling` parts, `CanonicalWriteSafety`, `SafetyRead`, batch token tests, and legacy audit support still used by other surfaces.
- [x] Run protocol and whole-solution regressions serially.

## Task 5: Documentation, Offline Gate, and Frozen Live Acceptance

**Files:** Update `PROJECT_OPERATIONS_SUMMARY.md`, `README.md`, `AGENTS.md` write-safety transitional guidance, `ARCHITECTURE.md` sections 7a/8, the existing JSON contract roadmap, and relevant testing/troubleshooting/client guidance. The write-safety delivery phases remain in the linked historical design; there is no separate write-safety roadmap. Index any acceptance report in `docs/README.md`.

- [x] Explain the phase's mixed flows: lifecycle is single-call/elicitation; Network and legacy batch retain tokens. Document default-on capability refusal, exact-set opt-out acknowledgement, dry-run examples, and the client-dependent consent boundary.
- [x] Add final-major-release migration notes for removed inputs and structured outputs. Do not cut a package tag or claim whole-redesign completion.
- [x] Run restore, serial stub solution build, full tests, existing 80% coverage gate, and `git diff --check`; review the final diff for unrelated changes.
- [x] Build the frozen candidate against installed V21 assemblies.
- [x] Obtain separate exact-target authorization for live acceptance. The indexed validation record freezes the live matrix without running it early.
- [x] On that candidate, exercise open/create/save/save-as/archive/close and every lifecycle guard, including both target-exists operations. Also test default-on accepted/declined elicitation in the client and unsupported-capability refusal; confirm dry runs do not mutate or prompt. Use disposable fixtures and record restoration checks.
- [x] Inspect state after timeout/crash/disconnect/possible mutation; never automatically replay. Any code/base change invalidates the frozen acceptance evidence.
- [x] Record the offline gates and explicit exclusions in the indexed validation report. Offline tests and a reference build do not establish live runtime behavior, human client confirmation, or persisted fixture artifacts.
- [x] After authorized live acceptance, record worker/runtime behavior, client confirmation behavior, persisted fixture artifacts, and restoration checks separately.
- [ ] After the authorized merge, plan write-safety Network Phase 3 and JSON alignment/retirement against fresh `main`; do not infer authority to execute them from this lifecycle plan.

## Implementation and Acceptance Boundary

The implemented scope combines the same delivery unit named differently in the two source documents: all six tools, all seven catalog guard IDs, default-on/off confirmation, binding transitions, typed outcomes/verification, audit, conformance, targeted retirement, and documentation. The explicit lifecycle binding seam and merged single-outcome wrapper are implemented. Offline qualification and the installed V21 reference build are recorded separately from live acceptance; only fresh, exact-target authorization permits the latter. Successor phases and remote integration remain separately authorized work.
