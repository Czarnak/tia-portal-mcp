# Guarded-write budget consolidation (B-10 / D-2) implementation plan

> **For agentic workers:** Use `superpowers:executing-plans` for inline execution, or `superpowers:subagent-driven-development` if the maintainer selects delegation. Steps use checkbox syntax for tracking.

**Status:** Implemented and offline-qualified on 2026-10-21, on a new branch with per-step commits. No live TIA verification performed; the maintainer's stop-before-live boundary remains in force.

**Goal:** Correct effective-limit omission metadata and replace the four guarded-write budgeting algorithms with shared host-side machinery while preserving domain-specific delivery and admission behavior.

**Architecture:** Put `GuardedWriteResponseBudget` in `Safety/Pipeline`. Retain the existing typed response records and domain budget entry points as adapters; share the budgeting stages and keep domain-specific projections, omission priorities and diagnostic policies explicit. Continue using `StructuredOperationBatchPayloadBudget` for batch payload omission and canonical document measurement.

**Tech Stack:** C# / .NET 10 host, existing canonical JSON serializer, xUnit and FakeWorker. No new dependencies or Siemens worker changes.

**Spec:** [Authoritative review: B-10 and D-2](2026-10-17-codebase-simplification-review.md), with [HOSTDOMAINS-1 evidence](2026-10-17-codebase-simplification-review-findings.md#hostdomains-1--four-guarded-write-payload-budgets-duplicate-one-algorithm-hmi-and-vci-copies-are-verbatim). The current-source corrections and preservation requirements below qualify that historical proposal.

## Current evidence and scope

Inspected on 2026-10-21 against clean `main` at `395dbb0`. No builds, tests or native calls were run during planning.

- B-10 remains in all four budget classes. Their diagnostic compaction records `StructuredOperationBatchPayloadBudget.MaxDocumentChars` instead of the `maxDocumentChars` resolved for that call.
- That property is already the effective startup configuration, not a fixed default constant. The demonstrated source defect concerns an explicit per-call override differing from the property; startup overrides are not independently shown to be broken.
- HMI and VCI share the same budgeting flow. HMI additionally compacts `HmiWriteOutcomeInfo.CleanupMessage`.
- PLC removes content diffs and block-import failure outcomes before dropping less disposable roots.
- Network additionally caps diagnostics at 512 encoded characters, preserves qualified recovery evidence through an early batch-budgeting pass, and reserves prepared recovery values with `MeasurePreparedCore`.
- The historical D-1 dependency is unnecessary for this implementation: no shared read-tool record is needed when write response records remain unchanged. D-1 and D-3 are outside this change.

## Global constraints

- The only intended observable correction is diagnostic omission metadata using the call's resolved document limit. Preserve Network's `512` diagnostic-cap metadata when that cap alone caused compaction.
- Defaults remain 60,000 item characters and 180,000 document characters. Resolve optional limits once from the effective properties; use the resolved values throughout the call.
- Measure canonical encoded JSON characters, including envelopes and omission/truncation records. Never substitute raw string lengths.
- Preserve response member names, ordering, explicit nulls, contract versions, retry tools and guidance strings.
- Drop values whole. Preserve execution statuses/counts, failure categories, skipped reasons, guard identities and verification truth. Incomplete delivery may make root `success` false without changing execution success.
- Preserve input immutability and exact delivered-document/audit correspondence.
- Preserve admission before binding/worker activity and all existing Network prepared/late reservation checks.
- Preserve guard reserves: PLC/Network `2N+1`, HMI `4N+3`, VCI `5N+5`, including encoded-longest-ID selection and domain-specific verification/warning shapes.
- Do not change lifecycle budgeting, read-tool budgeting, wire DTOs, write execution, worker operations, access policy, CLI options or dependencies.

## Proposed shared boundary

Keep these existing entry points and their signatures:

- Each domain's `Apply(response, int? maxItemChars = null, int? maxDocumentChars = null)`.
- Each domain's `MeasureProtectedCore(IReadOnlyList<DomainOperationRequest> items)`.
- Network's `MeasurePreparedCore(...)` and its domain-owned recovery-value calculations.

The shared implementation owns diagnostic omission bookkeeping, canonical sizing, per-item omission, the ordered document-omission loop, the trusted-status wrapper around the existing batch budget, and protected-core construction common to the four domains. Centralize `diagnosticDetailsOmitted` beside the existing shared omission-reason constants.

Use a small internal typed adapter boundary for response projection, diagnostic rewriting, candidate discovery/order and worst-case envelope construction. Keep private adapter signatures proportional to the extracted code; do not introduce a public interface, JSON-node mutation, reflection, a new serialization path or a hierarchy of response models. Prefer a few delegates/records over a framework. Share whole stages rather than extracting only `Size` and leaving four copies of the algorithm.

Network needs an explicit domain hook before ordinary document-root omission: try budgeting generated batch results while retaining the known recovery core, under its current representability check. Its unconditional diagnostic-cap pass also remains explicit. Do not flatten these into the HMI/VCI schedule.

| Domain | Diagnostic policy | Document-root omission priority |
| --- | --- | --- |
| HMI | Compact only on document/item pressure; include cleanup messages | Verification operations, then effects largest first with stable request-order ties |
| VCI | Compact only on document/item pressure | Verification operations, then effects largest first with stable request-order ties |
| PLC | Preserve diagnostics while they fit; compact under pressure | Content diffs, failure import outcomes, verification operations, whole effects; size descending and request-order ties within each priority |
| Network | Existing 512-character cap, then full compaction under pressure | Existing early batch pass; evidence/final checks before effects, with the current size/kind/index tie breaks |

The adapter also preserves each domain's warning handling: Network may omit warning lists where the others replace messages and deduplicate them. Existing omission detection includes Network's per-operation verification omissions.

## Files and consumers

| Responsibility | Files |
| --- | --- |
| Shared engine | Create `TiaMcpServer/Safety/Pipeline/GuardedWriteResponseBudget.cs`; add narrowly necessary private/internal adapter records there |
| Shared omission reason / existing batch budget | `TiaMcpServer/OperationBatches/StructuredOperationBatchPayloadBudget.cs` |
| Domain adapters | `TiaMcpServer/{Hmi,Vci,Plc,Network}/*WritePayloadBudget.cs` |
| Existing callers, expected unchanged | `HmiWriteDomain.cs`, `VciWriteDomain.cs`, `PlcWriteDomain.cs`, `NetworkWriteDomain.cs` |
| Domain regression suites | `TiaMcpServer.Tests/Hmi/HmiWriteBudgetTests.cs`, `Vci/VciWriteBudgetTests.cs`, `Plc/PlcWriteBudgetTests.cs`, `Network/NetworkGuardedWriteBudgetTests.cs` |
| Shared/configured-limit tests | Create `TiaMcpServer.Tests/Safety/GuardedWriteResponseBudgetTests.cs` if common stages need direct coverage; extend `OperationBatches/ConfiguredPayloadBudgetTests.cs` for isolated process-global configuration checks |
| Integration/audit regression | Existing guarded-write MCP/domain/audit suites and `Tools/ToolOutputContractConformanceTests.cs` |
| Documentation | This plan, `docs/README.md`; update B-10/D-2 in the authoritative review only after implementation evidence exists; update `docs/ARCHITECTURE.md` only if its budget seam description needs correction |

Tests link host sources. `Safety/Pipeline/*.cs` already has a test-project glob; VCI also has a glob and the other three budget files are explicitly linked. Confirm those links remain intact; no speculative project-file edit is needed.

## Review focus

1. Explicit per-call document overrides and pressure caused only by an item limit must report the resolved document limit for diagnostic omission.
2. Network's first-pass diagnostic omission must retain `limit:512`; later full compaction must use the resolved document limit.
3. Successful, failed and skipped operations must retain execution truth even when their values are omitted; verification success remains independent of delivery success.
4. Escaped/unicode IDs, maximal guard counts and long owner paths must retain admission safety and exact recovery identity.
5. Equal-size candidates, pre-existing omissions and null batch/verification roots must not change omission order or canonical delivery/audit correspondence.

## Task 1: Pin behavior and fix B-10

**Files:** Four existing domain budget files and their four budget test files; configured-budget tests if global fallback is exercised.

- [x] Add `DiagnosticOmissionReportsEffectiveDocumentLimit` cases in all four domains. Use explicit limits different from the process defaults and assert `reason`, `limit`, original encoded size, retry tool and unchanged trusted statuses. Cover document pressure and item-only pressure.
- [x] Add Network coverage distinguishing the ordinary 512-character cap from the full-compaction path. Keep the first-pass expectation at 512.
- [x] Add characterization cases where missing: complete responses serialize identically, input remains unchanged, null roots work, existing omissions remain visible, and omission priorities/ties are deterministic. Include HMI cleanup and PLC diff/import-outcome cases.
- [x] Run the focused filter below. Observe the new effective-limit assertions fail for the intended mismatch; distinguish those failures from environment/build failures.
- [x] Pass the resolved `maxDocumentChars` through each compaction method and use it only where document-limit diagnostic metadata is currently taken from the static property. Keep Network's independent cap unchanged.
- [x] Re-run the focused tests; require all new assertions and existing cases to pass.
- [x] Review the diff and commit: `fix: report effective guarded-write omission limits`.

This commit isolates the observable correction before structural consolidation. Later parity expectations use this corrected baseline.

## Task 2: Extract shared stages and migrate HMI/VCI

**Files:** Shared engine, shared omission-reason constants, HMI/VCI adapters and budget tests; shared tests only where needed.

- [x] Use the Task 1 fixtures as characterization evidence. Add focused tests for any new common decision not already exercised through domain entry points: trusted-status restoration, candidate exhaustion and root success after omission.
- [x] Implement the common stages and migrate HMI and VCI together, preserving their typed entry points, protected-envelope shapes, warning policy and HMI cleanup handling.
- [x] Parameterize shared guard generation with per-item/trailing counts; keep domain envelope construction typed so serialized reserves do not silently change.
- [x] Remove the superseded HMI/VCI algorithm copies. Require canonical parity with the corrected baseline for representative complete, diagnostic-only and value-omitted responses.
- [x] Run the focused tests plus HMI/VCI guarded-write domain and MCP suites. Verify maximal late HMI guards and VCI admission-before-binding cases.
- [x] Review production/test wiring and commit: `refactor: share HMI and VCI write response budgeting`.

## Task 3: Migrate PLC

**Files:** PLC budget adapter, shared engine only for demonstrated missing stages, PLC budget tests.

- [x] Pin the exact drop sequence: diff before effect for item pressure; diff, import outcome, checks, effect for document pressure. Assert target identity survives diff omission and failure category/status survives import-outcome omission.
- [x] Move PLC onto the shared stages without changing response records or its `2N+1` admission envelope.
- [x] Remove the copied orchestration, compaction bookkeeping and batch-status restoration. Retain only PLC-specific projections and candidate policy.
- [x] Run focused tests and PLC guarded-write domain/MCP suites; require corrected-baseline canonical parity, unchanged input and pre-worker admission rejection.
- [x] Review and commit: `refactor: use shared budgeting for PLC writes`.

## Task 4: Migrate Network and complete qualification

**Files:** Network budget adapter and tests; shared engine only where Network requires a real extension; final documentation.

- [x] Add any missing characterization for equal-sized evidence/final-check candidates and the early batch-result pass. Pin the ordinary cap and compact diagnostic policies separately.
- [x] Migrate Network onto the common stages, preserving the early representability check and largest-value/stable-tie omission schedule. Keep `MeasurePreparedCore` and operation-specific known recovery calculations domain-owned.
- [x] Require existing tests for `QualifiedRecoveryCore_IsNeverFlattenedOrOmitted`, generated identity overflow, late partial failure, escaped paths, long owner-path admission, final-item overflow and delivered audit identity to pass unchanged in meaning.
- [x] Remove the remaining duplicated common stages and confirm each domain adapter delegates the same shared algorithm rather than maintaining a second copy.
- [x] Run the complete offline qualification below and review the full diff for schema, ordering, accounting and audit changes. Seek independent review of the final branch if execution authorization includes delegation.
- [x] Record actual evidence and remaining limits in the authoritative review's B-10/D-2 entries. Keep this plan linked from `docs/README.md`; update the architecture seam only if needed.
- [x] Commit: `refactor: use shared budgeting for Network writes`.

## Verification and completion gate

Run builds/tests serially. Planning has not executed these commands.

Focused budget filter:

```powershell
dotnet test TiaMcpServer.Tests -m:1 /p:UseTiaPortalReferenceStubs=true --filter "FullyQualifiedName~WriteBudgetTests|FullyQualifiedName~NetworkGuardedWriteBudgetTests|FullyQualifiedName~GuardedWriteResponseBudgetTests|FullyQualifiedName~ConfiguredPayloadBudgetTests"
```

For Tasks 2 and 3, extend the filter with the named domains' guarded-write test classes. Avoid changing process-global limits in parallel tests; use the existing isolated configured-budget collection and restore defaults in `finally`.

Final qualification:

```powershell
dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests -m:1 /p:UseTiaPortalReferenceStubs=true
git diff --check
```

Use the repository's existing coverage configuration and 80% gate. Cover materially changed common branches and each domain's policy hooks; do not claim the existing high whole-suite percentage proves the extracted branches ran. Use process-local `TEMP`/`TMP` under an ignored `TestResults/guarded-write-budget/temp` directory if collecting coverage, and verify the resulting report has nonzero hits.

Acceptance requires bounded canonical delivery for admitted inputs, preserved pre-mutation admission safety, correct effective-limit metadata, unchanged execution/verification truth, exact delivered audit text/hash and no unintended canonical differences from the corrected baseline.

No live TIA test is required for this host-only presentation refactor if these gates pass and worker, dispatch and mutation behavior remain unchanged. No native acceptance is claimed. If implementation expands into those paths, stop and reassess verification scope.

## Handoff

Implemented on `refactor/guarded-write-response-budget` with the four commits above. The branch remains local; no push, PR, merge or live TIA verification was performed. Native verification is not required for the qualified host-only scope and is not claimed.

## Execution evidence

- Branch: `refactor/guarded-write-response-budget`, based on `395dbb0`. The maintainer authorized branch creation and implementation; pushing and PR creation are not authorized by that request.
- Task 1: eight new effective-limit cases failed against the original implementation with the expected `180000` mismatch; the separate Network 512-character cap case passed. After the fix, all 52 focused budget/configuration tests passed, with zero skips. Logs and TRX are under ignored `TestResults/guarded-write-budget/`. Seven existing nullable warnings and a NuGet vulnerability-data access warning were observed; neither was treated as test evidence.
- Task 2: all 122 focused budget/configuration and HMI/VCI guarded-write domain/MCP cases passed. A temporary differential harness against corrected commit `54b6855` also passed all seven tests: 192 canonical-response/exception comparisons and 36 protected-envelope size comparisons. The harness includes inadmissibly small test budgets and requires matching rejection behavior as well as matching delivered JSON. Baseline copies were temporary verification artifacts and were removed from the test project before final qualification; the sources and logs are preserved only under ignored `TestResults/guarded-write-budget/`.
- Task 3: all 100 focused budget/configuration, PLC guarded-write and differential tests passed, with zero skips. PLC diff-before-effect item handling, import-outcome priority and the typed `2N+1` envelope remain in its adapter; common omission loops, compaction bookkeeping and trusted-status restoration use the shared implementation. The differential harness continued to match the corrected baseline.
- Task 4 focused evidence: 156 budget/configuration, Network guarded-write and differential cases passed; the final eight parity/tie tests also passed. `MeasurePreparedCore` remains unchanged. Temporary comparison sources were removed from the test project and preserved only under ignored `TestResults/guarded-write-budget/parity-source/` before the final build. The serial stub solution build passed with the same seven nullable warnings and the NuGet vulnerability-data access warning. Independent review found no actionable issues.

- Final offline qualification: 7,185/7,185 tests passed with zero failures/skips in the serial Debug full-suite run (14 minutes 30 seconds). CI-scoped line coverage is 94.32%, passing the 80% gate. All 141 changed executable production lines were covered; the shared helper covered 67/67 executable lines and 60/62 branches. Each of the four domain budget files has 100% executable-line coverage. The seven pre-existing nullable warnings and NuGet vulnerability-data access warning remain explicit build limitations; no dependency audit success is claimed.
- Commands: serial stub solution build with `--no-restore -m:1 /p:UseTiaPortalReferenceStubs=true`; full `dotnet test` with `--no-build --no-restore`, the existing `coverage.runsettings`, `XPlat Code Coverage`, and xUnit/RunConfiguration parallelism disabled. Process-local `TEMP`/`TMP` used `TestResults/guarded-write-budget/temp`. Full log: `TestResults/guarded-write-budget/full.log`; TRX: `TestResults/guarded-write-budget/full/full.trx`; attached coverage: `TestResults/guarded-write-budget/full/cb88f13b-732f-4ff8-bbbf-cd7ee337985b/coverage.cobertura.xml`. `scripts/verify-coverage-threshold.ps1 -MinimumLineRate 0.80` passed against that attachment.
- Review and boundary: independent review of the complete implementation found no Critical, Important or Minor issues and declined no behaviors as outside scope. Worker code, mutation/dispatch paths, domain callers and response records remain unchanged. No live TIA calls or acceptance are included in these results.
