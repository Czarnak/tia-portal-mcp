# D-3, D-4 and D-5 Shared Refactors Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Status:** Implemented, independently reviewed and offline-qualified on 2026-10-25. The maintainer approved this plan and committed it locally as `e2c108b` before implementation. The original checklist is retained below; the [execution record](#execution-record-2026-10-25) records completed work, exact candidates and evidence limits.

**Goal:** Remove duplicated write records, validation and payload-failure plumbing, and route `network_read` directly through its existing DI executor while preserving public contracts and domain-specific outcomes.

**Architecture:** Keep the existing guarded-write domains, payload decoders and D-1 batch-read runner. Introduce only shared record/validation and failure/diagnostic helpers; inject the already-registered Network executor into the static tool method. Implement D-3, D-4 and D-5 in separate worktrees, then integrate and qualify them serially.

**Tech Stack:** C#/.NET 10 host, existing netstandard2.0 contracts and net48 worker, xUnit, FakeWorker, existing MCP SDK and Coverlet; Windows/PowerShell. No new dependencies.

**Spec:** [Authoritative simplification review, D-3/D-4/D-5](2026-10-17-codebase-simplification-review.md), [verified findings appendix](2026-10-17-codebase-simplification-review-findings.md), and the scope decisions below. The completed [D-1 record](2026-10-25-d1-batch-read-tool.md) supplies compatibility fixtures, not acceptance evidence for these changes.

**Analysis baseline:** Clean local `main`, `e9eb004`, inspected on 2026-10-25. Revalidate the execution base before implementation; line numbers in the historical review are navigation aids only.

## Global constraints

- Preserve the 8/19/19 tool surface, public argument/output schemas, canonical text/structured equality, explicit nulls, omission guidance, failure categories/messages, warnings and optional import outcomes.
- Preserve binding leases, dependency/replan order, access-mode gates, cancellation, worker invocation order, stop-at-first-failure behavior, audit and partial-mutation reporting.
- Preserve effective `MaxItemChars`/`MaxDocumentChars`; do not replace effective properties with literal defaults.
- Preserve process-scoped cursor protection, worker/binding identity checks, continuation reuse and disposal ownership. Do not recreate codecs/protectors per call.
- Keep native TIA calls, worker dispatch, contracts, packaging and dependencies unchanged. Offline qualification is the acceptance gate for this bounded host refactor; it does not prove native or plant behavior. No live TIA action is authorized by this plan.
- Keep production and tests wired together: `Safety/Pipeline/*.cs` is already linked into tests; `OperationBatches` source links are explicit.
- One build/test slot across all worktrees, including focused checks. Other agents may inspect/edit while that slot is occupied. Do not run full suites concurrently.
- Commit completed implementation/fix increments after relevant checks. The maintainer subsequently authorized separate worktrees/branches, implementation, feature-branch pushes and PR creation when offline verification suffices. Remote merging, deployment and live TIA actions remain outside that request.

## Approved scope decisions

1. **D-3:** Replace the PLC/HMI/VCI plan/replan record pairs and share validation across all four write domains. Keep domain classes, caches, mutation dispatch, response records and verification local. Omit `WriteCall.FromItems`: its former project-path work has disappeared, and current wrappers only normalize `operations ?? []` and pass `dryRun`. The maintainer approved this reduction of the historical item before implementation.
2. **D-4:** Share null-result failed items and guarded protocol diagnostics. Retain domain decoding, exception filters, public rejection text and result-bearing partial failures. Do not introduce a universal projection runner, remove VCI reflection, or refactor pagination coordinators merely to remove forwarding methods.
3. **D-5:** Keep the static method and inject `NetworkReadOperationExecutor`. Remove the compatibility registry/codecs/factory and manual startup bridge. Update both test DI builders and all direct callers; do not replace the registry with another cache or convert tools to instance classes.
4. **Concurrency:** D-3 is independent of both read-side changes. D-4 and D-5 are also code-independent after D-1 because their executor/projector interface remains unchanged. Retain D-4-before-D-5 integration to make review and failure attribution easier; it does not block parallel implementation.

## Parallel ownership and file map

All implementation workers share the host: preserve others' work and do not revert unrelated changes. Each stream starts from the same approved base in its own managed worktree; record exact base and resulting commit SHAs here when execution is authorized.

| Stream | Production ownership | Test and integration ownership |
| --- | --- | --- |
| D-3 | `TiaMcpServer/Safety/Pipeline/WriteRecords.cs`; `PlcWriteModels.cs`, `PlcWritePlanner.cs`, `PlcWriteDomain.cs` under `TiaMcpServer/Plc/`; corresponding HMI and VCI files; `TiaMcpServer/Network/NetworkWriteDomain.cs` | New `TiaMcpServer.Tests/Safety/Pipeline/WriteValidationTests.cs`; existing planner/domain tests as needed. No test-project edit. |
| D-4 | `TiaMcpServer/OperationBatches/StructuredOperationBatch.cs`; new `StructuredPayloadProjection.cs` in the same folder; the seven payload-contract files listed in Task 3 | New `TiaMcpServer.Tests/OperationBatches/StructuredPayloadProjectionTests.cs`; focused projection regressions; explicit new source include in `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` |
| D-5 | `TiaMcpServer/Network/NetworkReadTools.cs`; removal of the bridge in `TiaMcpServer/Program.cs` | Both DI builders, seven direct-caller files and one small test-construction helper listed in Task 4. No production source-link edit. |
| Coordinator | This plan, `docs/README.md`, final integration and evidence | Owns the build/test slot, conflict resolution, combined review and qualification. |

Keep `BatchReadTool`, `NetworkReadOperationExecutor` and domain `Project(...)` entry signatures unchanged, except for a narrow internal VCI diagnostic test seam described below. If two streams need the same test file, agree on ownership before editing; prefer new focused helper tests over modifying mixed HMI domain/payload tests in both branches.

## Review focus

| Condition | Required behavior and owning check |
| --- | --- |
| Invalid or null write items | Catalog rejection wins; budget/access callbacks do not run. D-3 short-circuit tests and existing public admission tests. |
| Protected core exactly at or above a configured limit | Equality is admitted; overflow rejects before access checks, using the effective limit. D-3 boundary/configuration tests. |
| Protocol rejection while diagnostic formatting or output throws | Still return the original fail-closed `protocol_error`; do not expose rejected payload/exception text. D-4 diagnostic tests. |
| Failed import/write with useful partial evidence, or an oversized HMI cursor | Keep import outcomes/typed results and preserve omission versus failure classification. D-4 existing domain regression tests. |
| Network service registration or protector lifetime changes | DI parameters stay out of schemas; same-owner continuation works and foreign/stale cursors remain rejected. D-5 schema/harness/pagination tests. |

## Task 1: Freeze compatibility and establish parallel work

**Owner:** Coordinator, after maintainer review of this draft.

**Interfaces:** Produces an exact common base SHA, file ownership and unchanged schema/canonical baselines for Tasks 2-4. No new runtime interface.

- [ ] Re-read root `AGENTS.md`, confirm current Git state/base and compare any drift since `e9eb004` against the three scoped areas. Preserve unrelated work; do not assume the previously inspected checkout is still current.
- [ ] Confirm the scope decisions above during plan review. In particular, keep the proposed omission of `WriteCall.FromItems` visible rather than silently treating historical D-3 as fully implemented.
- [ ] Create three isolated managed worktrees using `superpowers:using-git-worktrees`; proposed branch names are `refactor/d3-write-records`, `refactor/d4-payload-failures`, and `refactor/d5-network-read-di`. Do not copy unreviewed or unrelated working changes between them.
- [ ] Establish the serial build/test slot and run the unchanged compatibility checks on the approved base after a Release stub build. Reuse the commands in Task 5, with the baseline test filter `FullyQualifiedName~BatchReadCompatibilityTests|FullyQualifiedName~McpToolSchemaTests|FullyQualifiedName~ToolOutputContractConformanceTests`; do not rerun a full baseline suite solely to reproduce D-1's historical count.
- [ ] Confirm existing schema snapshots and `TiaMcpServer.Tests/Tools/Snapshots/batch-read-responses.json` are unchanged. Record command results and base SHA, then dispatch Tasks 2, 3 and 4 concurrently with their ownership boundaries and shared-workspace warning.

## Task 2: D-3 shared write records and lazy validation

**Files:** The D-3 row above, plus new `TiaMcpServer.Tests/Safety/Pipeline/WriteValidationTests.cs`.

**Interfaces:** Add these declarations to `Safety/Pipeline/WriteRecords.cs`, using existing `OperationValidation`, `WritePlan`, `ItemReplan` and `FiredGuard` types:

```csharp
public sealed record PlanResult<TEffect>(
    WritePlan<TEffect> Plan,
    IReadOnlyDictionary<string, IReadOnlyList<FiredGuard>> Guards);
public sealed record ReplanResult<TEffect>(
    ItemReplan<TEffect> Replan,
    IReadOnlyList<FiredGuard> Guards);

// Static member on the existing WriteValidation record:
public static WriteValidation Compose(
    OperationValidation catalogResult,
    Func<int> measureProtectedCore,
    Func<IReadOnlyList<string>> validateAccess,
    string toolName);
```

No generic constraints or new record wrappers are needed. PLC/HMI/VCI planners return the shared generic records. Network continues returning its existing `WritePlan`/`ItemReplan` directly. Public tool and domain interfaces remain unchanged; D-4/D-5 consume none of these new APIs.

- [ ] Add `WriteValidationTests` to the existing `ConfiguredPayloadBudgetCollection`. Specify: `Compose_InvalidCatalogPreservesErrorAndSkipsLaterStages` (distinctive category/message; both callbacks throw); `Compose_ProtectedCoreOverLimitRejectsBeforeAccessValidation` (four tool names, effective limit + 1, access callback throws); `Compose_ProtectedCoreAtLimitIsAdmitted`; `Compose_AccessErrorsPreserveOrderAndNewlines` (`["first", "second"]` becomes `"first\nsecond"` with `access_denied`); `Compose_ValidCallsEachLaterStageOnce` (order `budget`, `access`); and `Compose_UsesConfiguredDocumentLimit` (raised limit restored in `finally`).
- [ ] Run `dotnet test TiaMcpServer.Tests --configuration Release /p:UseTiaPortalReferenceStubs=true --filter "FullyQualifiedName~WriteValidationTests"`. Observe failure because `Compose` is absent; if the first failure is compilation, record it accurately as the missing-interface red check.
- [ ] Implement `Compose`: return an invalid catalog verdict unchanged; otherwise measure once, reject only when `> StructuredOperationBatchPayloadBudget.MaxDocumentChars`, then evaluate access errors once. Preserve the exact overflow text: `The encoded operation identities leave insufficient room for the required response summaries. Use smaller {toolName} calls.` Preserve `string.Join("\n", errors)` and existing categories. Do not catch new exceptions or run callbacks eagerly.
- [ ] Replace the six PLC/HMI/VCI record declarations with the two generic records and update their planner signatures/constructions. Route PLC/HMI/Network/VCI domain validation through `Compose` using lazy budget/access callbacks. Preserve resolved HMI names, original `DependsOn` presentation, successful-replan-only cache updates, Network prepared identities and VCI deferred/null effects and timeout breadcrumbs.
- [ ] Run the new tests and the existing `PlcWritePlannerTests`, `HmiWritePlannerTests`, `VciWritePlannerTests`, four `GuardedWriteDomainTests` suites and four `GuardedWriteMcpTests` suites with a combined `FullyQualifiedName` filter. Expected: all pass; no new constructor-only tests for the generic records.
- [ ] Review the diff for admission precedence, exact messages and unchanged mutation paths, then commit the checked increment as `refactor: share write planning records and validation`. Do not edit payload contracts, Network read wiring, snapshots or the test project in this stream.

## Task 3: D-4 shared failed items and guarded diagnostics

**Modify:** `OperationBatches/StructuredOperationBatch.cs`; `Plc/PlcPayloadContract.cs`; `Network/NetworkPayloadContract.cs`; `Network/HardwarePagePayloadContract.cs`; `Hmi/HmiPayloadContract.cs`; `Hmi/HmiWritePayloadContract.cs`; `Vci/VciPayloadContract.cs`; `Vci/VciWritePayloadContract.cs`, all under `TiaMcpServer/`.

**Create:** `TiaMcpServer/OperationBatches/StructuredPayloadProjection.cs` and `TiaMcpServer.Tests/OperationBatches/StructuredPayloadProjectionTests.cs`. Add the new production file's explicit linked-source entry in `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`; the existing structured-batch entry already covers the factory.

**Interfaces:** Add the factory to the existing item record and the diagnostic method to an internal static `StructuredPayloadProjection` class:

```csharp
public static StructuredOperationItem Failed(
    IOperationBatchItem item,
    string category,
    string message,
    IReadOnlyList<string> warnings,
    BlockImportOutcomeInfo? blockImportOutcome = null);

internal static void TryWriteProtocolDiagnostic(
    string operation,
    Action<string> writeDiagnostic,
    Func<string>? createDiagnostic = null);
```

The factory produces only the existing null-result failed shape: `Result`, `Omission` and `SkipReason` null; existing category/message/warnings and optional import outcome intact. Outcome-bearing failed items remain local. The diagnostic method guards both formatting and writing; its default text is exactly `TiaMcpServer: worker payload contract rejection: operation={operation}.` Network supplies its existing formatter as a lazy callback. No public projector signature changes are needed; for VCI read only, add an internal `Project(VciOperationRequest item, WorkerCallResult result, Action<string> writeDiagnostic)` overload, with the existing public method delegating using `Console.Error.WriteLine`.

- [ ] Add tests named `TryWriteProtocolDiagnostic_ThrowingWriterDoesNotEscape`, `TryWriteProtocolDiagnostic_ThrowingFormatterDoesNotEscape`, and `VciRead_MalformedPayloadAndThrowingDiagnosticStillReturnProtocolError`. Check operation-only default text; assert a malformed payload marker and exception messages are absent from public output. Use existing projection tests to verify failed-item shape/import outcomes instead of adding constructor-mirroring tests.
- [ ] Run `dotnet test TiaMcpServer.Tests --configuration Release /p:UseTiaPortalReferenceStubs=true --filter "FullyQualifiedName~StructuredPayloadProjectionTests"`; record the missing-interface red check. Implement only the shared factory/helper and the explicit source include needed by those tests.
- [ ] Migrate null-result/protocol-rejection branches in the seven contracts to the helpers. Preserve their exact public text and exception filters. Keep HMI cursor-sealing exceptions distinct from oversized-cursor omissions; retain PLC `BlockImportOutcome` and validated HMI/VCI/Network partial results.
- [ ] Keep Network's local validator formatter: declaring-type filtering, distinct validator names, at most six names, `unavailable` fallback and 512-character cap. Run formatting inside the shared guard. Do not pass payloads or exceptions into the shared diagnostic helper, change native failures into protocol failures, or refactor the VCI decoder map.
- [ ] Run the new tests plus existing PLC read/write payload tests, Network payload/IO-map/hardware/subnet tests, HMI decode/guarded-write tests and VCI read/guarded-write tests. Include existing cases for skipped settings/postconditions, failed import outcomes, failed HMI updates/creates, failed VCI leaves, oversized HMI cursors, bounded diagnostics and payload non-disclosure.
- [ ] Run `BatchReadCompatibilityTests`, `McpToolSchemaTests` and `ToolOutputContractConformanceTests`; expect all snapshots/frozen responses unchanged. Review and commit the checked increment as `refactor: share payload failure construction and diagnostics`. Leave coordinator forwarding wrappers and D-5's read-tool/startup files alone.

## Task 4: D-5 direct injection of the Network read executor

**Production files:** `TiaMcpServer/Network/NetworkReadTools.cs`, `TiaMcpServer/Program.cs`.

**Test DI builders:** `TiaMcpServer.Tests/Tools/McpToolSchemaTests.cs` (`BuildServices`) and `TiaMcpServer.Tests/TestSupport/McpProtocolTestHarness.cs` (`StartCoreAsync`). The shared protocol harness owns registration for canonical-response and registered-tool consumers; updating direct-call tests alone is insufficient.

**Direct-caller files under `TiaMcpServer.Tests/`:** `Network/NetworkIoMapFakeWorkerTests.cs`, `Network/NetworkOperationFakeWorkerTests.cs`, `Network/NetworkObjectPaginationCoordinatorTests.cs`, `Network/HardwarePaginationFakeWorkerTests.cs`, `Network/NetworkSubnetLifecycleFakeWorkerTests.cs`, `Network/NetworkConnectionEvidenceTests.cs`, and `OperationBatches/ReadToolCancellationTests.cs`.

**Create:** `TiaMcpServer.Tests/TestSupport/NetworkReadTestFactory.cs`, only to share constructor wiring across those callers. No caching or implicit lifetime ownership.

**Interfaces:** Keep current tool attributes, descriptions and output type. The production method becomes:

```csharp
public static Task<CallToolResult> NetworkRead(
    OpennessWorkerClient workerClient,
    NetworkReadOperationExecutor operationExecutor,
    NetworkOperationRequest[] operations,
    CancellationToken cancellationToken = default);

// Test-only factory; caller owns and disposes the protector:
internal static NetworkReadOperationExecutor Create(
    OpennessWorkerClient client, AuthenticatedCursorProtector protector);
```

Pass `operationExecutor.ExecuteAsync` to the existing `BatchReadTool.RunAsync`. Production already registers the executor, coordinators and codecs as singletons. The test factory constructs hardware/object codecs and coordinators with the supplied protector; reuse one executor/protector for all calls in a continuation sequence. Existing fixtures with custom coordinators retain their explicit construction and expose/pass that executor directly.

- [ ] Adapt the existing injected-protector test to direct injection and rename it `ListNetworkObjects_InjectedExecutor_UsesInjectedProtector`. Retain assertions that the supplied codec can decode the returned host-sealed cursor and that continuation succeeds using the same executor. Do not add another duplicate pagination fixture.
- [ ] Run `dotnet test TiaMcpServer.Tests --configuration Release /p:UseTiaPortalReferenceStubs=true --filter "FullyQualifiedName~ListNetworkObjects_InjectedExecutor_UsesInjectedProtector"` before changing the production signature; record the missing-signature compilation failure as the red check.
- [ ] Add/register the executor and its constructor dependencies in both test DI builders, reusing each builder's existing protector. In the protocol harness use singleton codecs, projector, coordinators and executor, matching production ownership. Keep schema-provider service detection intact.
- [ ] Implement `NetworkReadTestFactory.Create` with the existing constructor pattern and explicit caller-owned protector, then use it when migrating the seven direct-call files. Retain custom fixture/coordinator construction for the two former `RegisterExecutor` callers.
- [ ] Add the service parameter and remove `ConditionalWeakTable`, compatibility protector/codecs, compatibility executor factory and `RegisterExecutor` from `NetworkReadTools`. Delete only the manual post-build bridge in `Program.cs`; keep the actual DI registrations and SDK service-provider wiring.
- [ ] Run `McpToolSchemaTests.ReadBatchSchemas_ExposeOnlyOperations`, `McpToolSchemaTests.OutputSchemasMatchMain`, `BatchReadCompatibilityTests.CanonicalResponsesMatchPreD1` and `ReadToolCancellationTests`. Expected: public read input remains exactly `{operations}`, both protocol snapshots remain unchanged, and cancellation sends no worker request.
- [ ] Run `NetworkOperationFakeWorkerTests`, `NetworkIoMapFakeWorkerTests`, `HardwarePaginationFakeWorkerTests`, `NetworkObjectPaginationCoordinatorTests`, `NetworkSubnetLifecycleFakeWorkerTests` and `NetworkConnectionEvidenceTests`. Retain same-owner reverify, foreign-process signing, host-binding change, queued rebind, lease-held cursor issuance, unpaged/paged canonical output and two-call sealed-cursor continuation assertions.
- [ ] Search production/tests for `RegisterExecutor`, `CompatibilityExecutors`, `CompatibilityCursorCodec`, `CompatibilityObjectCursorCodec`, `CompatibilityProtector` and `CreateCompatibilityExecutor`; expect no remaining Network compatibility references. Review protector/executor disposal and commit the checked increment as `refactor: inject network read executor directly`.

## Task 5: Serial integration, review and combined qualification

**Owner:** Coordinator. **Interfaces:** Consume the three checked stream heads; produce one reviewed combined candidate and exact offline evidence. D-3 can integrate independently; integrate D-4 before D-5. This is local integration only, after implementation approval; publishing or merging a remote PR requires a separate explicit request.

- [ ] Inspect each stream's diff, changed files, focused test evidence and commit identity. Independently review validation short-circuiting, partial outcomes, diagnostic privacy, DI schema inference and cursor ownership. Resolve issues within the owning stream and commit validated fixes there.
- [ ] Integrate the checked commits in a dedicated local checkout/worktree, preserving each stream's commits. Coordinate the D-4 test-project source include with D-5 harness changes; keep frozen snapshots unchanged. Record the integrated SHA and run `git diff --check` against the approved base, not only the empty post-commit working diff.
- [ ] Run the Release stub restore/build, one full non-packaging coverage run and the threshold check below, all in the serial slot. Use a unique ignored result directory and process-local temporary paths; verify a nonempty report. Fail the gate on any failed/skipped test, missing/duplicate coverage report, or line coverage below 80%. Record existing build warnings separately; do not turn them into a passing test claim.

```powershell
dotnet restore TiaMcpServer.slnx /p:UseTiaPortalReferenceStubs=true
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
dotnet build TiaMcpServer.slnx -m:1 --no-restore --configuration Release /p:UseTiaPortalReferenceStubs=true
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$runRoot = Join-Path (Get-Location) ('TestResults/d3-d4-d5-' + [guid]::NewGuid().ToString('N'))
$tempRoot = Join-Path $runRoot 'temp'
$previousTemp, $previousTmp = $env:TEMP, $env:TMP
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
try {
    $env:TEMP = $tempRoot
    $env:TMP = $tempRoot
    dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build --configuration Release --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory $runRoot --filter "Category!=Packaging"
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    $reports = @(Get-ChildItem -LiteralPath $runRoot -Recurse -Filter coverage.cobertura.xml)
    if ($reports.Count -ne 1 -or $reports[0].Length -eq 0) { throw 'Expected one nonempty Cobertura report.' }
    ./scripts/verify-coverage-threshold.ps1 -CoveragePath $reports[0].FullName -MinimumLineRate 0.80
    if ($LASTEXITCODE -ne 0) { throw 'Coverage threshold failed.' }
} finally {
    $env:TEMP = $previousTemp
    $env:TMP = $previousTmp
}
```

- [ ] Inspect the test summary for zero failures/skips and the report for coverage of materially changed logic, including both lazy validation boundaries and throwing diagnostics. Existing helper/shape changes do not justify constructor-only tests. If a concern remains, add the smallest relevant regression and rerun affected checks, then repeat full qualification only if the candidate changed.
- [ ] Packaging tests are unnecessary for the planned host/test-only scope: a test `.csproj` source include is not a product `.csproj` change. If the implementation expands into any packaging trigger listed in `AGENTS.md`, run the Packaging category too and explain the scope expansion. No dependency audit is needed while dependencies remain unchanged.
- [ ] Update this shared plan with exact integrated commit, commands, test/coverage counts, warnings, independent-review outcome and unexecuted native limits. Update the authoritative review's D-3/D-4/D-5 status only after the evidence exists; explicitly record the reviewed `WriteCall.FromItems` omission. Keep `docs/README.md` accurate. Run local link/whitespace checks for documentation-only follow-up edits without repeating the full suite.
- [ ] Stop with the reviewed local result and report readiness. Do not claim live TIA acceptance, publish, push, open/merge a PR, or archive worktrees without the relevant user request and recoverability checks.

## Completion criteria

- The maintainer has reviewed this draft before implementation starts.
- The scoped duplicate records/validation/failure construction and Network compatibility registry are removed without expanding into domain/state-machine redesign.
- Public schemas, frozen canonical responses and domain-specific failure/omission evidence remain unchanged; diagnostic sink failures cannot replace protocol rejection.
- Each stream and the combined candidate have fresh relevant evidence, independent review and recorded commit identities; combined qualification meets the existing 80% line threshold with zero failed/skipped tests.
- Documentation distinguishes implemented/offline-qualified work from native or plant acceptance. Initial plan preparation remained uncommitted for review; the maintainer then committed the plan and authorized implementation and PR delivery.

## Execution record (2026-10-25)

The common implementation base was `e2c108b9c7cdff62f97675bebe05869ff15ecddc`, the maintainer's local plan commit on `main`. Remote `main` was `e9eb004b03234484ce3a5a5a2a268c37df343639` when checked. Three managed worktrees and named branches were created from that same base; a fourth local-only worktree combined the reviewed commits for qualification. The original `main` checkout was not used for implementation.

| Stream | Branch and source candidate | Isolated evidence | Review |
| --- | --- | --- | --- |
| D-3 | `refactor/d3-write-records`, `0481d99fcfa8a920efaace0566839ce160682eaf` | Missing-`Compose` compile red before production edits; 230 focused planner/domain/admission tests passed, zero failed/skipped | Independent task review approved, no actionable findings |
| D-4 | `refactor/d4-payload-failures`, `28e18c0c933dca17824ffc27f50f3346ebdb90e4` | Missing-helper/overload compile red; 911 focused payload/schema/canonical tests and 7,622 full non-packaging tests passed, zero failed/skipped | Independent task review approved, no actionable findings |
| D-5 | `refactor/d5-network-read-di`, implementation `cea22ae4e5cd34b52f5268b0b0227b7869235832`, fixture fix `128c57c5c97e2e1688d69ddbe48938e4071c138f` | Missing-signature compile red; 161 focused schema/cursor/cancellation tests passed; reflection-fixture red reproduced eight failures, then all 18 `NetworkToolsTests` passed after the fix | Independent task review and scoped fix review approved, no actionable findings |

The serial baseline Release stub build passed with seven existing warnings and zero errors; 116 unchanged compatibility/schema/conformance tests passed with zero failures/skips. D-5 also passed a serial Release stub solution build. Frozen canonical responses and both protocol schema snapshots were preserved.

### Implemented behavior

- D-3 introduces shared `PlanResult<TEffect>`/`ReplanResult<TEffect>` for PLC/HMI/VCI and lazy `WriteValidation.Compose` for the four write domains. Catalog rejection still precedes budget measurement, then access validation. Equality at the effective document limit remains admitted. Per-domain state, mutation, verification and failure text are preserved. `WriteCall.FromItems` was deliberately omitted under the approved scope.
- D-4 shares null-result failure construction and guards existing diagnostic formatting/writing. PLC import evidence, typed Network/HMI/VCI partial failures, HMI cursor omissions and Network validator diagnostics remain local and unchanged. Previously silent hardware/write rejection paths remain silent.
- D-5 injects the existing executor into the static `NetworkRead` method and removes the compatibility registry/codecs/factory and startup bridge. Both test DI builders, seven direct-caller files and the additional reflective `NetworkToolsTests` helper supply the executor. Protectors are explicitly owned/disposed, and continuation callers reuse their executor/protector.

### Combined verification and follow-up

Reviewed commits were combined locally with D-4 before D-5. The initial combined candidate `64b21a03c109c283b36409401c8fab4af7b66d95` passed the broad code review but its full run reported **7,623 passed, eight failed, zero skipped**. All eight failures were `TargetParameterCountException` from `NetworkToolsTests` invoking `NetworkRead` reflectively with the old three-argument list. This helper had escaped the initial direct-caller inventory.

The original D-5 implementer reproduced the failures before changing the helper, supplied the injected executor with the existing test factory/protector, preserved null-client rejection assertions, and passed all 18 tests in that class. A bounded audit of 403 test C# files, Network read string/reflection paths and static-null `Invoke` calls found no other stale invocation. The fix changed one test file (four insertions, one deletion); production code did not change. A separate scoped review approved it.

The final combined source candidate is `3ec5b7e71fa7549020f9aefdf88c1a15089c9ac9`. Its Release stub solution build passed with seven existing warnings and zero errors. The fresh full non-packaging run passed **7,631 tests, zero failed, zero skipped**, in 6 minutes 5 seconds. CI-scoped line coverage was **12,973 / 13,642 (95.09%)**, exceeding the existing 80% threshold. The shared `WriteValidation` class (eight executable lines), new plan/replan records (six executable lines) and guarded diagnostic helper (seven executable lines) were fully covered. The final broad code review and scoped fixture-fix review found no actionable issues.

Exact qualification commands, run serially with unique ignored result directories and process-local `TEMP`/`TMP`, were:

```powershell
dotnet restore TiaMcpServer.slnx /p:UseTiaPortalReferenceStubs=true
dotnet build TiaMcpServer.slnx -m:1 --no-restore --configuration Release /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build --configuration Release --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory $runRoot --logger "trx;LogFileName=tests.trx" --filter "Category!=Packaging"
./scripts/verify-coverage-threshold.ps1 -CoveragePath $coveragePath -MinimumLineRate 0.80
```

The extra D-4 full run covered **12,986 / 13,655** CI-scoped lines (**95.10%**); 58/60 changed executable production lines were covered, including all seven lines of the new diagnostic helper and all ten factory lines. Two existing VCI-write rejection call sites remained unexecuted. TRX logging copied the collector report to its attachment directory: both coverage files were byte-identical by SHA-256. The initial postprocessing check rejected the duplicate file count; report equality and the threshold were then verified without rerunning tests. The local qualification helper accepts identical copies and rejects differing reports.

Local evidence directories (not committed) are `TestResults/d3-*`, `TestResults/d4-*` and `TestResults/d5-*` in their respective worktrees, plus `TestResults/combined-full-6feb392e21094413b653469c90824b9e` and `TestResults/combined-final-c7c5cf64b0ee4fdf85cfefc4b4e9463c` in the local verification worktree. Source qualification refers to the exact candidates above; subsequent documentation-only commits do not change tested production/test code.

### Implementation rulings

1. Preserve VCI's existing guard-cache update on failed replans and success-only deferred removal. The plan's preservation wording does not authorize harmonizing domain state policies; changing that interpretation could alter dependency guards.
2. Keep previously silent hardware/HMI-write/VCI-write diagnostic paths silent, sharing the failed-item factory there and guarding only existing diagnostic paths. Adding stderr behavior would expand the refactor; the retained limitation is unchanged diagnostic coverage for those paths.
3. Use isolated focused checks for each branch plus the planned full combined gate for D-3/D-5, rather than add two unplanned full reruns. D-4's extra full run was already underway and was retained. The combined gate caught the missed reflective fixture; any concern that combined changes could mask an isolated regression requires a focused independent follow-up.

### Evidence limits

No live TIA operation, installed-tool deployment, PLC operation or plant acceptance was performed. These changes affect host records, validation/projection wiring and test construction; native invocation paths and worker/contracts remain unchanged. Offline/FakeWorker and stub-build evidence is the acceptance scope for this refactor. Packaging tests and a dependency audit were not triggered: only the test project's source include changed, and product projects, packaging and dependencies were untouched.
