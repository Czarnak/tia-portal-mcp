# C-9 Paging Defaults and Matrix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Default hardware cursor-only continuations and all network-object pages to 100, preserve explicit paging and hardware opt-in behavior, and document the final paging contracts in one matrix.

**Architecture:** Change the existing hardware coordinator, strict payload validator and worker fallback together, then change the existing network-object page builder default. Keep the current request fields, worker protocol, projection/budget rules and cursor algorithms. C-9 source work starts independently of C-5; its final documentation and integration qualification follow C-5's host-sealed wrapper.

**Tech Stack:** C#, net10.0 host/tests, net48 worker, xUnit, PowerShell, Coverlet.

**Spec:** Item C-9 in the [simplification review](2026-10-17-codebase-simplification-review.md) and wave 4 of the [Workstream C plan](2026-10-21-workstream-c-plan.md). Maintainer decisions on 2026-10-23: use 100 for hardware cursor-only continuations and for both initial and cursor-only `list_network_objects` calls; develop C-5 and C-9 in parallel in separate worktrees, serialize full suites, and merge C-5 first.

**Branch:** `fix/c9-paging-defaults`, its own managed worktree. Once implementation is authorized, create it from the same agreed clean `main` commit as C-5, containing `9ff3083` and these plans; use the checkout path returned by the managed-worktree tools. Do not wait for C-5 source implementation. Create no worktree during this plan-writing session.

## Global Constraints

From the Workstream C plan:

- **Shell:** run `dotnet` from PowerShell. Bash mangles `/p:` flags.
- **Build:** `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true`. The baseline is the seven existing test-assembly nullable warnings and no new warnings.
- **Full suite:** run serialized, outside the sandbox, with process-local `TEMP`/`TMP` under ignored `TestResults/<id>/temp`. Collect with `TiaMcpServer.Tests/coverage.runsettings` and the single-thread xUnit settings from `.github/workflows/ci.yml`. `scripts/verify-coverage-threshold.ps1 -MinimumLineRate 0.80` must pass, with zero failures and zero skips.
- **Shared validation slot:** the orchestrator schedules builds/tests. Keep other worktrees' builds/tests idle while a full suite owns the slot; focused tests with shared host resources take the slot too. Parallel implementation does not permit racing shared processes or installed tools.
- **Test-first:** observe a runtime failure before implementing; the changed defaults must fail assertions on the old code, not only compilation.
- **Test linking:** `TiaMcpServer.Tests` links host and worker sources. All four production default sites below are already included; no new source file or csproj entry is needed.
- **Commits:** commit after each task, as a short one-line conventional commit with no body.
- **Docs come last.** Add an evidence sub-bullet under C-9 in the review plan after qualification.
- **No TIA project names or paths** in the repo, including disposable copies.
- **jCodemunch:** confirm C# consumers with text search before deleting a member.

C-9 rules (maintainer decisions, 2026-10-23):

- **Hardware:** only a cursor with omitted `pageSize` gains the default 100. A request with neither paging field remains unpaged; the first paged request still supplies `pageSize`.
- **Network objects:** omitted `pageSize` means 100 on initial and continued calls.
- **Explicit sizes:** every explicit `pageSize` from 1 through 200 remains valid, including 50; 0 and 201 remain invalid. Size may change between pages.
- **Leave alone:** field names, envelopes/contract versions, ordering, filters, cursor identity and category rules, all budgets, HMI/VCI/tree defaults, and equipment search's `maxResults`. Do not introduce a generic paging abstraction.
- **Parallel ownership:** Tasks 1-2 can execute while C-5 implements its wrapper in its own worktree. C-5 tests of cursor security/routing use explicit sizes so they do not depend on either default. C-9 owns default-sensitive assertions. Preserve C-5 edits when rebasing; adjust only expectations or fixtures made default-sensitive by integration.
- **Integration:** merge C-5 first, rebase C-9 onto that main, then qualify the combined state and write Task 3's final matrix/evidence. C-9 does not merge or release on pre-C-5 evidence.
- **Execution boundary:** the maintainer authorized implementation and committing the plans on 2026-10-23. This does not authorize installation, a live TIA run or remote writes.

## Review Focus

1. **A valid 100-candidate hardware continuation must survive strict host decoding.** The coordinator alone is insufficient: `HardwarePagePayloadContract` currently assumes 50 too. Task 1 adds the 100/50/101 returned-count theory.
2. **Explicit 50 after a default page, or a default page after explicit 50, must neither skip nor duplicate rows.** Task 1 reconstructs the complete hardware sequence and Task 2 reconstructs network-object sequences in both starting modes.
3. **Omitting both hardware paging fields must keep the existing unpaged shape.** Task 1 retains `Executor_UnpagedHardwareUsesTheExistingInvokerProjectionPath` and the existing opt-in route theory.
4. **A default is a request size, not a promise of 100 returned rows.** Task 1 runs existing hardware prefix/omission regressions; Task 2 pins a short final page. The matrix preserves budget and terminal-page guidance.
5. **C-5 integration must not silently reinstate 50 or expose the worker token.** The combined gate reruns C-9 default tests and C-5's complete Network/cursor contract tests after rebase; final documentation describes only host-sealed public network-object cursors.

**Contract change:** intentionally observable default page sizes: hardware cursor-only 50 to 100; network-object initial and cursor-only 50 to 100. Explicit 50 preserves the old requested size. No field rename, envelope version change or hardware opt-in change. Larger network-object pages can reach response budgets sooner; keep the existing omission and narrowing guidance.

**Live verification:** C-9 does not require a separate live run: the changed rules are pure and directly linked into tests. C-5's separately authorized scoped smoke remains C-5's gate. Offline tests do not establish live TIA behavior.

---

### Task 1: Hardware cursor-only default is 100 end to end

**Files:**
- Modify: `TiaMcpServer/Network/HardwarePaginationCoordinator.cs`, `ExecuteAsync` (current :95).
- Modify: `TiaMcpServer/Network/HardwarePagePayloadContract.cs`, `Validate` (current :103).
- Modify: `TiaMcpServer.OpennessWorker/Openness/Network/HardwarePageCandidateReader.cs`, `DefaultPageSize` and `ResolvePageSize` (current :14, :103-126).
- Modify: `TiaMcpServer/Network/NetworkReadTools.cs`, the hardware sentence in the `NetworkRead` description (current :30).
- Test: `TiaMcpServer.Tests/Network/HardwarePaginationCoordinatorTests.cs`.
- Test: `TiaMcpServer.Tests/Network/HardwarePagePayloadContractTests.cs`.
- Test: `TiaMcpServer.Tests/Network/HardwarePageCandidateReaderTests.cs`.
- Run unchanged regressions: `HardwarePageProjectorTests` and `HardwarePaginationFakeWorkerTests`.

**Interfaces:**
- Consumes: `HardwarePageCandidateCall.PageSize`; `HardwarePagePayloadContract.Decode(NetworkOperationRequest operation, WorkerCallResult workerResult, HardwarePageContinuationInfo? continuation) -> HardwarePagePayloadContractResult`; `HardwarePageCandidateReader.Read(HardwarePageCandidateSource source, string? deviceName, string? plcName, bool includeIoDetails, bool includeTagMatches, int? requestedPageSize, HardwarePageContinuationInfo? continuation) -> HardwarePageCandidateResultInfo`.
- Produces: the same signatures and payloads, with an effective cursor-only size of 100 in dispatch, validation and worker fallback. The coordinator still supplies an explicit `int` to the worker.

- [ ] **Step 1: Write the runtime regressions.**
  - Rename `Coordinator_CursorOnlyUsesPageSizeFiftyAndInjectsCursorPathIdentityAndBinding` to `Coordinator_CursorOnlyUsesPageSizeOneHundredAndInjectsIdentityAndBinding`. Keep all identity/binding assertions; use 201 total devices and 100 returned devices rather than the current empty result. Assert `observed.PageSize == 100`, success and 100 public devices.
  - Rename `Read_DefaultsToFiftyOnlyForACursorOnlyRequest` to `Read_DefaultsToOneHundredOnlyForACursorOnlyRequest`. Grow its descriptor fixture from 60 to 201. Assert 100 candidates; keep the initial missing-size `validation_error` assertion.
  - Add `Decode_CursorOnlyRequiresTheOneHundredCandidatePage` to `HardwarePagePayloadContractTests`, a theory with returned counts 50, 100 and 101. Set `PageSize = null`; supply a continuation with matching ordering/query/snapshot and offset 0, totalDevices 201, totalSubnets 0, and contiguous device offsets. Assert `decoded.IsSuccess` only for 100; 50 and 101 give a failed item with `protocol_error`. Build these payloads with the existing `CandidatePayload` helper, independently of any production default constant.
  - Add `Coordinator_MixedExplicitAndDefaultPagesCoverEveryCandidateOnce`. Use 351 small candidates and requested sizes `[50, null, 50, null, null]`. Continue using each returned public cursor; return `min(call.PageSize, total - startOffset)` candidates through the existing `Success` helper. Assert page counts `[50, 100, 50, 100, 51]`, concatenated names equal the original 351 names in order without duplicates, and the final next cursor is null/absent. Keep the same verified binding/session for the whole sequence.
  - Add an explicit-size row to the worker/decoder tests proving requested 50 still returns/accepts 50 while more than 100 candidates remain.
- [ ] **Step 2: Run RED.**
  - Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~HardwarePaginationCoordinatorTests|FullyQualifiedName~HardwarePagePayloadContractTests|FullyQualifiedName~HardwarePageCandidateReaderTests"`
  - Expected: the renamed default tests, mixed-sequence count assertion, and 100-success/50-rejection decoder rows FAIL on runtime assertions. The 101-rejection and explicit-50 rows already PASS. Record actual failing names.
- [ ] **Step 3: Change the three default sites together.**
  - In `HardwarePaginationCoordinator.ExecuteAsync`, retain the initial no-cursor fallback 0 and change only cursor fallback 50 to 100.
  - In `HardwarePagePayloadContract.Validate`, change the omitted-size fallback 50 to 100; retain every exact-count, offset, kind and evidence check.
  - In `HardwarePageCandidateReader`, change `DefaultPageSize` to 100; retain the first-page missing-size rejection and 1..200 bounds.
  - Change the tool description's `cursor alone defaults to 50` to `cursor alone defaults to 100`. Keep its combined-sequence and opt-in explanation.
- [ ] **Step 4: Run GREEN and the preserved paging regressions.**
  - Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~HardwarePaginationCoordinatorTests|FullyQualifiedName~HardwarePagePayloadContractTests|FullyQualifiedName~HardwarePageCandidateReaderTests|FullyQualifiedName~HardwarePageProjectorTests|FullyQualifiedName~HardwarePaginationFakeWorkerTests"`
  - Expected: all PASS, including `Executor_UnpagedHardwareUsesTheExistingInvokerProjectionPath`, `Executor_EitherPaginationFieldSelectsTheCoordinator`, budget-prefix/omission cases and existing explicit-size combined device/subnet reconstruction.
- [ ] **Step 5: Commit.** Message: `fix: default hardware cursor continuations to 100`

**Task test command:** Step 4's filter. Run only this focused gate; the controller owns full-suite scheduling.

### Task 2: Network-object initial and continuation defaults are 100

**Files:**
- Modify: `TiaMcpServer.OpennessWorker/Openness/Network/NetworkObjectPageBuilder.cs`, `DefaultPageSize` (current :8).
- Modify: `TiaMcpServer/Network/NetworkReadTools.cs`, `NetworkRead` description: state that `list_network_objects` uses `pageSize` 1..200, default 100 on every page.
- Test: `TiaMcpServer.Tests/Network/NetworkObjectPageBuilderTests.cs`.
- Run unchanged regressions: `NetworkIntrospectionContractTests` and `NetworkObjectCursorCodecTests`.

**Interfaces:**
- Consumes: `NetworkObjectPageBuilder.ResolvePageSize(int? requestedPageSize) -> int`; `Build(IReadOnlyList<NetworkObjectSummaryInfo> orderedItems, int pageSize, int offset, string queryHash, string snapshotHash) -> NetworkObjectListInfo`; `NetworkObjectCursorCodec.Decode(string cursor, string expectedQueryHash, string expectedSnapshotHash, int totalCount)`.
- Produces: `DefaultPageSize = 100`; unchanged signatures, raw worker token and max 200. At current main, `Program.ListNetworkObjects` calls `ResolvePageSize` for initial and continued requests before traversal. C-5 wraps the token at the host and does not change this worker paging rule.

- [ ] **Step 1: Write the runtime regressions.**
  - Change the null row of `ResolvePageSize_UsesDefaultAndAcceptedExplicitBounds` to expected 100. Add explicit 50 beside existing 1 and 200 rows.
  - Add `Build_DefaultInitialAndCursorOnlyPagesUseOneHundred`. Build 251 ordered small items. Resolve omitted size for the initial call, decode its cursor to obtain offset 100, and resolve omitted size again for the continuation. Assert counts 100/100, offsets 0/100/200 and unchanged totals/order.
  - Add `Build_MixedDefaultAndExplicitFiftyPagesCoverEveryItemOnce`, a theory whose starting mode is default or explicit 50. For 351 items, request `[null, 50, null, 50, null]` or `[50, null, 50, null, null]` respectively. On each continuation get the offset from `Decode`, using unchanged query/snapshot hashes. Assert respective counts `[100, 50, 100, 50, 51]` and `[50, 100, 50, 100, 51]`, every item exactly once in original order, and terminal `NextCursor == null`. Literal expectations must not come from `DefaultPageSize`.
  - Retain `ResolvePageSize_RejectsValuesOutsideAcceptedBounds` for 0 and 201, terminal-page and out-of-range cases.
- [ ] **Step 2: Run RED.**
  - Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~NetworkObjectPageBuilderTests"`
  - Expected: the null default row, default initial/cursor-only test and both mixed-sequence rows FAIL at runtime; explicit 1/50/200 and invalid-bound rows PASS.
- [ ] **Step 3: Change the existing default to 100 and update the tool description.**
  - Leave `MaxPageSize = 200`, `Build`, the worker codec and `Program.ListNetworkObjects` unchanged.
  - Do not edit C-5's wrapper/security tests to encode C-9 defaults; their explicit sizes remain explicit.
- [ ] **Step 4: Run GREEN.**
  - Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~NetworkObjectPageBuilderTests|FullyQualifiedName~NetworkObjectCursorCodecTests|FullyQualifiedName~NetworkIntrospectionContractTests"`
  - Expected: all PASS. No worker-token encoding, filter/snapshot error or public input-schema change.
- [ ] **Step 5: Commit.** Message: `fix: default network object pages to 100`

**Task test command:** Step 4's filter. Tasks 1-2 can finish before C-5 merges.

## After source tasks: integration qualification (controller)

These are controller gates after Task 2 and before Task 3. They are future execution steps, not evidence already obtained.

1. C-5 finishes its own branch verification, independent review and separately authorized scoped smoke. Merge C-5 first through the user's approved integration workflow.
2. Rebase C-9 onto that main. Preserve C-5's sealed-wrapper behavior and explicit-size tests. Inspect the merged diff; resolve overlap in `NetworkReadTools.cs` by retaining both the C-5 cursor explanation and the C-9 defaults.
3. Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~TiaMcpServer.Tests.Network|FullyQualifiedName~TiaMcpServer.Tests.Cursors"`. This includes C-5's `NetworkObjectPageCursorCodecTests`, `NetworkObjectPaginationCoordinatorTests`, `NetworkOperationFakeWorkerTests` and `NetworkStructuredProtocolTests`, plus C-9's hardware/default/validator tests. Inspect the resulting C-5 tests explicitly: public cursors remain host-sealed, raw worker cursors never escape, and binding/filter/snapshot categories retain C-5's contract.
4. Obtain the single full-build/test slot. No C-5 or other worktree full suite may overlap. Run the following PowerShell sequence outside the sandbox, from the C-9 worktree; the test settings are copied from `.github/workflows/ci.yml`. Use a fresh results directory so exactly one report can be checked.

```powershell
$c9Evidence = Join-Path (Get-Location) ('TestResults/c9-combined-' + [Guid]::NewGuid().ToString('N'))
$c9Temp = Join-Path $c9Evidence 'temp'
New-Item -ItemType Directory -Path $c9Temp -Force | Out-Null
$c9PreviousTemp = $env:TEMP
$c9PreviousTmp = $env:TMP
try {
    $env:TEMP = $c9Temp
    $env:TMP = $c9Temp
    dotnet build TiaMcpServer.slnx -m:1 --configuration Release /p:UseTiaPortalReferenceStubs=true
    if ($LASTEXITCODE -ne 0) { throw 'C-9 combined stub build failed.' }
    dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build --configuration Release --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory $c9Evidence -- xUnit.ParallelizeTestCollections=false xUnit.ParallelizeAssembly=false xUnit.MaxParallelThreads=1 RunConfiguration.MaxCpuCount=1
    if ($LASTEXITCODE -ne 0) { throw 'C-9 combined full suite failed.' }
    $c9Reports = @(Get-ChildItem -LiteralPath $c9Evidence -Recurse -Filter coverage.cobertura.xml)
    if ($c9Reports.Count -ne 1) { throw "Expected one Cobertura report; found $($c9Reports.Count)." }
    ./scripts/verify-coverage-threshold.ps1 -CoveragePath $c9Reports[0].FullName -MinimumLineRate 0.80
    if ($LASTEXITCODE -ne 0) { throw 'C-9 combined coverage gate failed.' }
}
finally {
    $env:TEMP = $c9PreviousTemp
    $env:TMP = $c9PreviousTmp
}
```

5. Require 0 errors, no new warnings, zero failed and skipped tests, a nonzero Cobertura report and line rate at least 80%. Record candidate/base commits, test count, exact report and the changed-line evidence for the three hardware rules and network-object default. Do not reuse a pre-rebase C-9 report as combined evidence.
6. Obtain a final independent whole-branch review, including all five Review Focus rows. Fix and commit any source issue, then rerun affected focused gates and any invalidated integration gate. Only then give Task 3 the actual evidence values.

### Task 3: Final paging matrix and evidence (docs last)

**Files:**
- Modify: `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md`, existing `Hardware configuration pagination` section and a new `Paging matrix` section within this file.
- Modify: `docs/README.md`, add a Network-reference row under `Understanding the design` pointing to `SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md#paging-matrix` so the cross-tool matrix is directly discoverable.
- Modify: `docs/IMPROVEMENT_LOG.md`, completed C-9 evidence.
- Modify: `docs/superpowers/plans/2026-10-17-codebase-simplification-review.md`, evidence under C-9.
- Modify: `docs/superpowers/plans/2026-10-21-workstream-c-plan.md`, C-9 completion/evidence and resolved-decision state only; preserve the approved parallel-worktree and C-5-first integration instructions.

**Interfaces:**
- Consumes: the qualified C-5/C-9 combined behavior and actual controller-supplied build/test/coverage/review evidence. It does not generate or rerun that evidence.
- Produces: one paging matrix in the existing indexed Network reference, with relative links to the existing HMI/VCI/PLC references. No new documentation file or public paging field.

- [x] **Step 1: Document the final defaults and matrix.**
  - Change only the hardware cursor-only default sentence from 50 to 100. Keep explicit-50 examples: 50 remains supported.
  - Document network-object default 100 for initial and continuation calls alongside C-5's final sealed-cursor behavior and error/recovery rules.
  - Add the following five matrix rows. Columns are reader, public size/position fields, default/opt-in behavior, bounds, and scan/continuation semantics:
    - `browse_project_tree`: `pageSize`, optional `cursor`; default 100 initially and on continuation, 1..200, cached point-in-time flat-node snapshot.
    - `network_read/read_hardware_config`: `pageSize`, optional `cursor`; first paged call supplies size, cursor-only defaults 100, neither stays unpaged, 1..200; devices then subnets, current candidate snapshot checked, budget projection can shorten the complete returned prefix.
    - `network_read/list_network_objects`: `pageSize`, optional `cursor`; default 100 on every page, 1..200; current deterministic inventory checked against the worker snapshot, public cursor host-sealed by C-5.
    - HMI paged reads: `offset` default 0, `limit` default 100, 1..2000; `cursor` excludes `offset`, continuing the saved snapshot. Without cursor a fresh scan occurs. Name the seven operations: `list_tags`, `list_system_tags`, `list_alarms`, `list_logging_tags`, `list_screen_items`, `list_faceplate_instances`, `validate`.
    - `vci_read/list_mappings`: `offset` default 0, `limit` default 100, 1..200; plain live offset paging without a snapshot or cursor, so mapping changes can shift rows.
  - State that explicit sizes remain adjustable between pages, subject to each row's bounds; follow returned continuation metadata rather than assuming exactly the requested count. Preserve domain-specific lifetime, eviction, budget and recovery distinctions through links.
  - State that `search_equipment_catalog.maxResults` is a truncation cap without continuation and is outside this paging-default change. Do not change its default 50.
  - Add the docs index row above to make the matrix discoverable.
- [x] **Step 2: Record qualified evidence under C-9, in the improvement log and in the Workstream C completion state.**
  - Include date, exact combined candidate/base, defaults changed, unchanged explicit sizes/max 200/hardware opt-in, focused runtime RED/GREEN evidence, actual full-suite count with zero skips, coverage rate/report, independent review and C-5-first integration.
  - State: no separate C-9 live TIA verification was performed; C-5's scoped smoke is separate evidence and does not qualify every paging combination.
  - Do not rewrite old acceptance reports or claim the current plan-writing session executed implementation.
- [x] **Step 3: Validate documentation only.**
  - Run: `git diff --check`
  - Run: `git diff -- docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md docs/README.md docs/IMPROVEMENT_LOG.md docs/superpowers/plans/2026-10-17-codebase-simplification-review.md docs/superpowers/plans/2026-10-21-workstream-c-plan.md`
  - Expected: no whitespace errors; all five matrix rows match qualified code; relative links point to existing files; C-5 recovery categories and explicit-50 examples remain accurate. Check the edited links and anchors directly; do not add or invoke a nonexistent docs-link script.
  - Do not repeat expensive builds/full suites for this documentation-only task.
- [x] **Step 4: Commit.** Message: `docs: document paging defaults and matrix`

**Task test command:** Step 3's documentation checks.

## Completion boundary

The PR is ready for the approved integration/release workflow only after C-5 is merged, C-9 is rebased and qualified on that combined state, Task 3 is committed, and the final diff is clean. Report offline versus scoped live evidence separately. Pushing, opening/merging a PR, publishing a package or running live TIA requires the authorization applicable at execution time.

## Source execution checkpoint (2026-10-23)

At this source checkpoint, Tasks 1-2 were implemented on `fix/c9-paging-defaults` at candidate `00d7f36890e4df1b534a6238c833747bc884cd53`, from common base `5b5f4c1`. This was focused offline evidence, not final combined qualification or Task 3 completion; the combined record below supersedes its pending status.

- Commits: `5fdf22c` changes the three hardware defaults, `8c9d199` corrects one test-only nullable-name warning, and `00d7f36` changes the network-object default.
- Hardware: baseline 112 passed; runtime RED 5 failed / 84 passed; focused GREEN 118 passed, zero failures/skips. The decoder's 100-success and 50-rejection failures were both observed before the default changes; explicit-50 and 101-rejection controls stayed green.
- Network objects: baseline 116 passed; runtime RED 4 failed / 10 passed; focused GREEN 120 passed, zero failures/skips. Initial/continuation defaults and both mixed-size sequences are pinned independently of production constants.
- The combined focused gate passed 238 tests. Full-log comparison found exactly the seven unchanged baseline warnings after the nullable test correction; `git diff --check` passed. Task helpers also reran the 118- and 120-test filters successfully. Logs and the per-task ledger are under ignored `.superpowers/sdd/2026-10-23-c9-paging-defaults-and-matrix/`.
- An independent read-only source review of `5b5f4c1..00d7f36` found no Critical, Important or Minor findings and checked all five Review Focus rows. It explicitly did not establish post-rebase C-5 interaction or live Siemens behavior/performance.
- Ruling: defer the full suite to the required rebased combined gate; focused source evidence cannot replace it. Use bounded native source navigation because this worktree has no jCodemunch index.
- At that checkpoint, C-5 merge, C-9 rebase/combined qualification, integration review and Task 3 remained pending. No live TIA, installation or remote write was performed in that phase.

## Combined execution record (2026-10-23)

C-5 PR #172 merged at `8fe0632d1bb270eb43f4e560ed99e48017b67857`; fresh GitHub metadata and
`origin/main` agreed. C-9 rebased cleanly onto it. Original source commits map to `b0e6e55`
(hardware), `5404dec` (nullable test correction), `ce750af` (network objects), and `61e7305`
(source checkpoint). Final qualified candidate is `ce2c50b5d671ef14ee6af8a4f3f471261040cfe9`.

- The first combined focused gate at `61e7305` passed 2,303 and failed two tests; no full suite started. One metadata test still expected 50. The other launched an old Debug FakeWorker from Release tests and repeated page one. Binary inspection confirmed that Debug lacked C-5's second-page fixture token while the freshly built Release fixture contained it.
- `ce2c50b` changes test support only: metadata assertions cover both defaults of 100, and `FakeWorkerLocator` selects the test assembly's configuration using the existing `StartupProcess` pattern. Both observed failures then passed (2/2), with the stale Debug artifact still present. No production paging change was required for this fix.
- Renewed Release stub build: 7 unchanged nullable warnings, 0 errors, 8.91 seconds. Programmatic comparison found exactly the same seven baseline warnings, none added/removed.
- Required combined Network/cursor gate: 2,305 passed, zero failed/skipped, 1.6762 minutes. This includes 17 codec, 38 network-object coordinator, 23 Network FakeWorker, 13 structured protocol, 59 hardware payload, 15 hardware coordinator, 15 worker hardware reader and 14 network-object builder cases; no raw public worker token or default-size regression remained.
- Full serialized Release run: 7,541 passed, zero failed/skipped, 8.1280 minutes. One nonzero Cobertura report (3,469,687 bytes) records 13,043/13,731 executable lines, line rate 0.9498 (94.98%); the 0.80 gate passed. Host default line `HardwarePaginationCoordinator.cs:95` was hit 36 times and strict decoder line `HardwarePagePayloadContract.cs:103` 62 times. C-5 codec/coordinator retain 26/26 and 87/87 covered lines. Worker default declarations are excluded by the existing CI worker coverage policy; their changed behavior is directly exercised by the passing reader/builder cases.
- Exact full evidence: ignored `TestResults/c9-combined-final-e76a1c6d540449edbb6bcf1841443e23/{build.log,focused.log,test.log,coverage-check.log}` and `fcdf3d99-4764-46b1-94f2-186be3a10d32/coverage.cobertura.xml` beneath that directory. Failed/targeted logs remain in the earlier `TestResults/c9-combined-e6e9482976634cd5adc6e5b06fa19e53/`. The renewed qualification ran sequentially, outside the sandbox, with process-local `TEMP`/`TMP` and the planned CI settings.
- Independent whole-branch review of `8fe0632..ce2c50b` found no actionable findings across all five Review Focus rows: strict counts; mixed sizes without loss/duplicates; unpaged hardware/opt-in; budget/terminal semantics; and merged C-5 sealing, identity and defaults. The test-support correction was reviewed against existing configuration detection and project wiring.

Rulings: use bounded native navigation for this unindexed worktree; extend the test-only file list
to correct the demonstrated fixture-selection defect; gather read-only review/matrix facts while
immutable-source tests run, but make final review disposition and Task 3 edits only after the
completed qualification gate. The failed and pre-rebase runs are not final qualification evidence.

Task 3 records the final [five-row matrix](../../SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md#paging-matrix),
preserves explicit-50 examples and catalog cap 50, and indexes the matrix directly. Documentation
checks cover whitespace, links/anchors, scope and the final diff; unchanged .NET suites are not
repeated for documentation-only edits.

No separate C-9 live TIA run was performed or required by the approved plan. C-5's installed
`pageSize:1` smoke is separate evidence and does not qualify all paging combinations or native
performance. No C-9 installation or remote write was performed during that qualification phase.

## Integration completion (2026-10-24 reconciliation)

C-9 merged through [PR #173](https://github.com/Czarnak/tia-portal-mcp/pull/173) at
`0bd41d4f612e2dd397f34f34d4378abca2a81182`, after C-5's merge. GitHub metadata was read back
during PR7 on 2026-10-24. This supersedes the earlier integration-pending status. Qualified source
remains `ce2c50b`; PR head `38f183b73490ef54553c00291dd4390e340bc701` adds documentation only,
so no unchanged suite was rerun. The merge establishes integration without a separate package
publication/release claim or new live TIA evidence. The [follow-up completion ledger](2026-10-24-workstream-c-followups-orchestration.md#completion-ledger)
records the later Workstream C fixes.
