# C-5 Sealed Network Object Cursors Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every public `list_network_objects` continuation is authenticated by the host and belongs to the exact host binding and worker session that issued it.

**Architecture:** A small host codec seals the existing worker continuation together with its host binding and worker session identity, using C-6's `AuthenticatedCursorProtector.Seal`/`Open`. A dedicated coordinator handles every inventory call, pins the binding through dispatch, payload validation and cursor issuance, and replaces only `NetworkObjectListInfo.NextCursor`. The worker's internal unsigned token, query/snapshot/range checks and public result shape stay in place.

**Tech Stack:** C#, net10.0 host, existing netstandard2.0 contracts and net48 worker, xUnit/FakeWorker. No new dependency.

**Spec:** Review item C-5 ([review](2026-10-17-codebase-simplification-review.md), WORKERNET-5) and the approved wave-4 decisions and execution rules in the [Workstream C plan](2026-10-21-workstream-c-plan.md#wave-4-plans-and-execution-maintainer-2026-10-23).

**Branch:** `fix/c5-sealed-network-object-cursors`, its own managed worktree. Start from the same agreed clean `main` base as C-9, containing `9ff3083` and these plans. Create no worktree until implementation is authorized. C-9 starts in parallel; C-5 merges first.

## Global Constraints

- **Scope:** implement C-5 only. Keep the default page size at 50 in this branch; C-9 owns both changes to 100. Do not migrate the worker to typed continuations, change hashing/ordering, add a snapshot store, rename paging fields, or perform the D-5 DI cleanup.
- **Shell/build:** use PowerShell and `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true`. The recorded warning baseline is seven existing test-assembly nullable warnings; require zero errors and no new warnings.
- **Tests:** task implementers run focused filters only. Full suites and coverage run one worktree at a time, outside the sandbox through the normal permission mechanism, with process-local `TEMP`/`TMP` in ignored `TestResults/<run-id>/temp`. Use the CI xUnit single-thread settings, zero failures/skips, and the 80% line-coverage gate. Match build configuration to `--no-build` tests.
- **Test-first:** observe a public runtime failure before production edits. New codec tests may initially fail to compile, but that is supplementary evidence, not the behavior RED. Preserve green characterization tests without claiming they failed.
- **Test linking:** host Network files are explicitly enumerated in `TiaMcpServer.Tests.csproj`; add both new source files. Contracts remain a project reference.
- **Binding:** preserve catalog/access validation and the root verified-binding gate before per-item cursor handling. No implicit binding, project opening or failed-request replay.
- **Lease:** use the existing `OpennessWorkerClient.ExecuteWithPinnedBindingAsync<T>(ProjectBindingSnapshot?, Func<Task<T>>, CancellationToken)`; do not introduce another lock. Capture/recheck the issuing binding inside that critical section. Do not attach a post-await binding snapshot to a result from an earlier epoch.
- **Identity:** seal the worker session ID, session generation, Portal PID and canonical project path. Require a verified bound host state, coherent identity and matching project paths. Reuse existing binding/identity rules rather than comparing only a path or managed handle.
- **Public contract:** keep request/result schemas, explicit nulls and Network `contractVersion:"1.0"`. Only cursor values and the added stale-binding rejection change. Reject old unsigned cursors; never fall back to them. Canonical text and structured content remain one document.
- **Failures:** local stale-cursor refusal preserves a healthy binding; actual worker identity/transport loss follows existing invalidation before outward mapping. Preserve `portal_lost`, timeout/crash categories, warnings, independent later read items and no replay.
- **Parallel ownership:** C-5 owns cursor plumbing, production/compatibility wiring, cursor tests and related FakeWorker scenarios. C-9 owns default values, their tests, the tool-description default wording and the paging matrix. Preserve others' edits; use explicit page sizes in C-5 fixtures. Shared files/merge handling are in the wave plan.
- **Commits:** commit after each implementation/fix task, with a short conventional subject and no body. No push, PR creation, merge, installation or live TIA run without explicit authorization.
- **Docs last:** after offline qualification, independent review and the separately authorized C-5 live gate, record evidence under C-5 in the review and improvement log. Never store real TIA project names or paths, including disposable targets.
- **Live runs:** use the installed candidate, separate from the dev build. Never dispose `TiaPortalProcess`. The plan defines a future gate; it authorizes no live action.
- **Code navigation:** use the current jCodemunch guide. Confirm consumers with text search before deleting a member; index reference searches have produced false empties for C# consumers.

## Review Focus

1. **First pages and both executor routes:** no public response can bypass sealing; production registration and the compatibility factory both use the coordinator. Task 1 pins both routes and the first-page response.
2. **Binding changes while waiting or reading:** an old continuation is never sent under a new epoch or minted with a newer binding. Task 2 pins queued binding changes, a new binding epoch at the same path, unchanged-owner reverify and lease serialization. Reverify alone is not an epoch change.
3. **Incomplete or divergent worker identity:** malformed successful responses fail closed, while genuine divergence/Portal loss still invalidates without replay. Task 2 pins missing identity, generation/PID changes and fatal transport outcomes.
4. **Cursor issuance exceeds 4,096 characters:** return a bounded omission, no unsigned fallback or unusable advancement; a terminal page still succeeds. Task 2 pins a long synthetic path and a following successful item.
5. **Continuation semantics survive wrapping:** preserve worker query/snapshot/range failures, explicit page-size changes, warnings and canonical output. Task 1 pins a successful two-call sequence; Task 2 pins the failure/category table.

---

### Task 1: Authenticate the complete public inventory path

**Files:**
- Create: `TiaMcpServer/Network/NetworkObjectPageCursorCodec.cs` (state and codec together).
- Create: `TiaMcpServer/Network/NetworkObjectPaginationCoordinator.cs`.
- Modify: `TiaMcpServer/Network/NetworkReadOperationExecutor.cs`, `TiaMcpServer/Network/NetworkReadTools.cs` (compatibility wiring only), `TiaMcpServer/Program.cs`, `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`.
- Test: new `TiaMcpServer.Tests/Network/NetworkObjectPageCursorCodecTests.cs`; modify `NetworkOperationFakeWorkerTests.cs`, `NetworkStructuredProtocolTests.cs`, `NetworkToolsTests.cs`, and `NetworkFieldForwardingTests.cs` under `TiaMcpServer.Tests/Network/` where constructor/cursor expectations change.
- Modify fixtures only as needed: `TiaMcpServer.FakeWorker/Program.cs` inventory scenarios; keep raw-worker forwarding tests distinct from public MCP tests.

**Interfaces:**
- Consume `AuthenticatedCursorProtector`, `CursorKind`, `CursorException`, `ProjectBindingCursorState`, `CursorStateRules`, the existing client pinned-binding method, and `NetworkPayloadContract.Project(NetworkOperationRequest, WorkerCallResult)`.
- Produce `internal sealed record NetworkObjectPageCursorState(int Version, string WorkerCursor, ProjectBindingCursorState HostBinding, WorkerSessionIdentity SessionIdentity)` in the codec file.
- Produce `NetworkObjectPageCursorCodec(AuthenticatedCursorProtector protector)`, `string Encode(NetworkObjectPageCursorState state)` and `NetworkObjectPageCursorState Decode(string cursor)`.
- Produce `NetworkObjectPaginationCoordinator(OpennessWorkerClient workerClient, NetworkObjectPageCursorCodec cursorCodec)` and `Task<StructuredOperationItem> ExecuteAsync(NetworkOperationRequest operation)`. Test the real client with the existing in-process transport/FakeWorker patterns; add no generic coordinator abstraction.
- Extend both existing `NetworkReadOperationExecutor` constructors with a third dependency: production takes `NetworkObjectPaginationCoordinator`; the delegate overload takes `Func<NetworkOperationRequest, Task<StructuredOperationItem>> executeObjectPage`. Dispatch every `list_network_objects` call there, whether or not it supplies a cursor. Keep all other routing unchanged.

- [ ] **Step 1: Add the public behavior RED before creating the host classes.** Replace the raw `"large-list-page-2"` expectation in `ListNetworkObjects_LargeListScenarioReturnsCursorWithTwentyItems` with assertions equivalent to:

  ```csharp
  Assert.NotEqual("large-list-page-2", nextCursor);
  Assert.Equal(2, nextCursor!.Split('.').Length);
  ```

  Add `ListNetworkObjects_PublicCursor_ContinuesAcrossTwoCalls` using explicit `PageSize = 20`. Feed the returned public token into a second actual `network_read`; assert the next items/counts, no nested JSON string, and no skipped/duplicated items. Extend the FakeWorker scenario to accept/verify the original internal token on call two. Assert text JSON equals structured JSON.
- [ ] **Step 2: Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~ListNetworkObjects_LargeListScenarioReturnsCursorWithTwentyItems|FullyQualifiedName~ListNetworkObjects_PublicCursor_ContinuesAcrossTwoCalls"`.** Expect a runtime failure because the first response still exposes the raw worker token; record the actual failure before implementation.
- [ ] **Step 3: Implement the codec.** Use version `1`, purpose `network-object-page`, message `The supplied network object cursor is not valid for this request.`, and foreign-process category `invalid_cursor`. Call shared `Seal`/`Open`. Require a nonblank internal token, `HostBinding.IsBound && HostBinding.IsValid()`, a nonblank worker session ID, generation at least zero, positive Portal PID, and a canonical worker project path matching the host path through `CursorStateRules.SameProject`. Do not decode or duplicate the worker query/snapshot algorithm in the host.
- [ ] **Step 4: Implement the coordinator and wire both routes.** Open the incoming public token before dispatch; reject a retained host-binding/identity mismatch locally as `cursor_binding_mismatch`. Execute against the exact verified binding under the existing pinned lease. Forward a fresh copy of the operation whose cursor is only `state.WorkerCursor`, keeping caller input immutable. Let the existing client validate/invalidate real worker outcomes. Decode successful payloads through `NetworkPayloadContract.Project` before extracting the typed `NetworkObjectListInfo`; no permissive parallel parser or rejected-payload echo. Verify complete response identity even on a terminal page. Replace a non-null `NextCursor` with the sealed state, and serialize the typed result back to a detached JSON value using the existing canonical helpers. Preserve item warnings and all other fields.
- [ ] **Step 5: Register the codec/coordinator with the existing process protector in `Program.cs`.** In the compatibility path, retain the existing hardware codec API, retain one process-scoped compatibility protector for both codecs, and create the object coordinator alongside the hardware coordinator. Keep `ConditionalWeakTable`, `RegisterExecutor` and direct-call support; their removal belongs to D-5. Add the new host files to the test project's explicit links and update executor constructor callers.
- [ ] **Step 6: Add codec cases and run GREEN.** Pin round-trip, malformed/version/state fields, wrong purpose, tampering, foreign process and unsigned legacy rejection. Verify both a registered executor with injected protector and the compatibility factory return a sealed first-page token. Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~NetworkObjectPageCursorCodecTests|FullyQualifiedName~NetworkOperationFakeWorkerTests|FullyQualifiedName~NetworkStructuredProtocolTests|FullyQualifiedName~NetworkToolsTests|FullyQualifiedName~NetworkFieldForwardingTests|FullyQualifiedName~HardwarePagination"`. Expect all selected tests to pass. Worker-only token tests stay green unchanged.
- [ ] **Step 7: Review the task diff and commit `fix: authenticate public network object cursors`.**

### Task 2: Pin lease races, identity loss and bounded failure behavior

**Files:**
- Create: `TiaMcpServer.Tests/Network/NetworkObjectPaginationCoordinatorTests.cs`.
- Modify: `TiaMcpServer/Network/NetworkObjectPaginationCoordinator.cs`; extend the Task 1 codec/fixtures and `NetworkStructuredProtocolTests.cs` only where the cases require it.
- Reference only: `TiaMcpServer/Worker/OpennessWorkerClient.cs` existing pinned lease, identity validation and invalidation; `TiaMcpServer/Network/HardwarePageProjector.cs` existing cursor-size omission shape.

**Interfaces:** consume Task 1's codec/coordinator and the real client binding lease. Retain those signatures. Produce the category/omission behavior below, without new public fields or worker methods.

- [ ] **Step 1: Add the failure table using explicit small page sizes and counted dispatches.** Name the tests `InvalidPublicCursor_IsRejectedBeforeWorkerDispatch`, `StaleBinding_PreservesHealthyCurrentBinding`, `SameOwnerReverify_KeepsCursorUsable`, `QueuedRebind_DoesNotDispatchOldContinuation`, `Lease_PreventsBindingChangeDuringCursorIssuance`, `IncompleteIdentity_IsProtocolError`, `WorkerIdentityLoss_InvalidatesWithoutReplay`, `WorkerContinuationFailures_KeepCategoryAndWarnings`, `OversizedIssuedCursor_IsBoundedOmission`, and `TerminalPage_DoesNotRequireCursorIssuance`. Cover:
  - Same-path binding ID/revision changes, different project, worker session ID/generation/PID changes; local stale refusal sends no inventory request and keeps the healthy binding usable.
  - Same-owner reverify retains its epoch and the cursor stays valid. Obtain a genuinely different epoch through existing binding transitions/synthetic fixture state; do not assume `forceRebind:true` changes an already verified identical owner or alter bind semantics for this test.
  - A rebind that wins while continuation waits for the gate; no old token dispatch. A competing bind during a blocked inventory read waits until projection/sealing finishes. Use synchronization signals, not sleep-based races.
  - Successful worker payloads with absent/incomplete/incoherent identity become `protocol_error`, never cursors. Real worker identity divergence reaches the client's existing invalidation logic before mapping outward to `cursor_binding_mismatch`; never mask `portal_lost`, timeout or crash, and assert one attempt/no replay.
  - After a valid outer token, worker `cursor_filter_mismatch`, `cursor_snapshot_mismatch`, `cursor_out_of_range`, warnings and malformed-payload `protocol_error` retain their behavior. Authenticate and reject stale host state before worker query/snapshot/range checks. Root validation/access/unbound failures still precede item execution.
  - Long synthetic paths force `Seal` past 4,096 characters. Assert `status:"omitted"`, null result/failure, reason `networkObjectCursorExceededCharLimit`, actual size/4,096 limit, `retryTool:"network_read"`, and guidance to shorten the project path and start again or narrow the request until it fits one terminal page. No raw cursor or partially advanced page is published. A later read item still runs; a terminal result with `NextCursor:null` still succeeds with complete identity.
- [ ] **Step 2: Run `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~NetworkObjectPaginationCoordinatorTests`.** Record actual runtime RED cases before fixing them. Cases already correct from Task 1 are characterization GREEN, not invented failures.
- [ ] **Step 3: Complete the failure paths in the coordinator.** Map a local lease-entry mismatch to `cursor_binding_mismatch` without clearing the current binding. Preserve client invalidation for actual worker loss. Catch only the relevant `CursorException` whose inner exception is `AuthenticatedCursorSizeException` when issuing; produce the existing `StructuredOperationOmission` shape above and let the common Network budget bound the delivered item/document. Do not catch `ObjectDisposedException`, swallow fatal outcomes, publish an unsigned fallback or introduce a second budget engine.
- [ ] **Step 4: Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~TiaMcpServer.Tests.Network|FullyQualifiedName~TiaMcpServer.Tests.Cursors|FullyQualifiedName~BoundProjectContent|FullyQualifiedName~ToolOutputContractConformanceTests"`.** Expect all selected tests to pass, with canonical text/structured equality on rejection, success and omission.
- [ ] **Step 5: Review and commit `fix: guard network cursor identity and failure boundaries`.**

## Branch qualification and live gate (orchestrator)

Run after Task 2 and before Task 3. Reviewers/implementers do not start full suites independently. Reserve the wave's single validation slot; keep all other builds/tests idle while a full suite owns it. Choose a fresh ignored results directory for every attempt, retain failures, and restore process-local `TEMP`/`TMP` afterward.

```powershell
dotnet build TiaMcpServer.slnx -m:1 --configuration Release /p:UseTiaPortalReferenceStubs=true
if ($LASTEXITCODE -ne 0) { throw 'C-5 stub build failed.' }
$runRoot = Join-Path (Get-Location) ('TestResults/c5-' + [Guid]::NewGuid().ToString('N'))
$testTemp = Join-Path $runRoot 'temp'
New-Item -ItemType Directory -Path $testTemp -Force | Out-Null
$previousTemp = $env:TEMP; $previousTmp = $env:TMP
try {
    $env:TEMP = $testTemp; $env:TMP = $testTemp
    dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build --configuration Release --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory $runRoot -- xUnit.ParallelizeTestCollections=false xUnit.ParallelizeAssembly=false xUnit.MaxParallelThreads=1 RunConfiguration.MaxCpuCount=1
    if ($LASTEXITCODE -ne 0) { throw 'Full suite failed.' }
    $reports = @(Get-ChildItem -LiteralPath $runRoot -Recurse -Filter coverage.cobertura.xml)
    if ($reports.Count -ne 1) { throw 'Expected exactly one coverage report.' }
    ./scripts/verify-coverage-threshold.ps1 -CoveragePath $reports[0].FullName -MinimumLineRate 0.80
    if ($LASTEXITCODE -ne 0) { throw 'C-5 coverage gate failed.' }
} finally { $env:TEMP = $previousTemp; $env:TMP = $previousTmp }
```

Stop immediately on a failed build before running tests; require zero failures/skips, no new warnings and passing coverage. Record exact source HEAD, commands, counts and changed-file line coverage. Obtain an independent whole-branch review and resolve material findings. Only repeat relevant gates when new changes/failures justify them.

The maintainer then separately authorizes an installed-candidate, read-only smoke against an exact already-open disposable target with enough network objects for two pages. Use explicit `pageSize:1` so C-9 defaults are irrelevant: bind, read page one and its continuation, reject a tampered cursor, reverify the same unchanged owner and show its binding epoch/cursor remain usable, then show a fresh sequence succeeds. Same-owner binding can resolve to `Reverify` even with `forceRebind:true`; it is not evidence of a new epoch. No open/create/save/close, project mutation, target switch, Portal termination or restart is authorized by this gate. Binding-epoch changes, worker restart/identity faults, foreign-process cursors and size overflow remain offline-only in this scoped smoke. The report records the frozen candidate, access mode, exact observed results and unexecuted cases; FakeWorker evidence is not live evidence. The gate is not executed until separately authorized.

### Task 3: Documentation and evidence (docs last)

**Files:** `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md` (inventory cursor semantics), `docs/superpowers/plans/2026-10-17-codebase-simplification-review.md` (C-5 evidence), `docs/IMPROVEMENT_LOG.md` (completed item). Add/link a scoped report under `docs/superpowers/acceptance/reports/` and in `docs/README.md` only when actual live evidence exists.

**Interfaces:** consume the exact offline/review/live evidence above. C-9 owns the cross-domain matrix and default wording; coordinate shared document sections without rewriting its changes.

- [x] **Step 1: Document public host authentication, retained internal worker continuation, worker/binding identity, foreign-process `invalid_cursor`, stale `cursor_binding_mismatch`, restart-from-first-page migration, issuance-size omission and preserved transport-loss recovery.** Record no schema or `contractVersion` bump and no default change in C-5.
- [x] **Step 2: Record exact qualified HEAD and bounded evidence under C-5 and in the improvement log.** Preserve failed/unexecuted cases; do not mark a live gate passed without observed evidence or a maintainer's explicit scoped acceptance.
- [x] **Step 3: Run `git diff --check`, verify changed Markdown links/anchors and review `git diff --stat`.** Documentation-only edits need no repeat of the unchanged .NET suite. Commit `docs: record C-5 cursor evidence`.

**Handoff:** no push/merge without approval. Merge C-5 before C-9, then give C-9 the merged SHA and the exact cursor/recovery contract for its rebase and combined qualification. Both lane implementations may proceed before this handoff; only integration and final merge order depend on it.

## Offline execution record (2026-10-23)

Source Tasks 1-2 and the review fix are committed on `fix/c5-sealed-network-object-cursors` at
`db2947ba3aac2f07b33510a6d877dded3f990282`, from the common approved-plans base `5b5f4c1`.
The installed-candidate live gate and final acceptance are pending, not waived.

- Commits: `8f726bb` authenticates both public routes, `fc840e7` handles identity/lease/failure/size boundaries, and `db2947b` rejects blank worker continuations at the existing payload boundary.
- Task 1: existing baseline 1 passed; public runtime RED 2/2 failed on the exposed raw token; focused GREEN 130 passed. Task 2: initial table 13 product failures and one fixture-only PowerShell array-wrapping failure, then all cases passed; the later broad gate found one old unsigned-token consumer and migrated it to a real two-call public continuation. Source gate: 2,363 passed, zero failed/skipped.
- Independent whole-branch review of `5b5f4c1..fc840e7` checked all five Review Focus rows. Its only Important finding was a blank worker `NextCursor` escaping the read engine. Four public empty/space/tab/CRLF regressions failed at runtime before the fix and passed afterward, asserting `protocol_error`, null result, retained warnings/healthy binding and a successful following item. The renewed focused gate passed 2,367 tests, zero failed/skipped. No Critical/Minor findings or other Important findings were reported.
- Final Release stub build at `db2947b`: 7 existing nullable warnings, 0 errors. The full serialized run used the command in this plan with `--logger 'console;verbosity=normal'` added only for progress visibility, a unique ignored `TestResults/c5-fixed-full-<id>` directory and process-local `TEMP`/`TMP`. All 7,531 tests passed, zero failed/skipped, in 7.9278 minutes. Cobertura: 13,044/13,731 executable lines, line rate 0.9499; the 0.80 threshold passed. New codec 26/26, coordinator 87/87, executor 22/22; Network payload contract 573/583, read tools 72/74.
- The earlier `fc840e7` full run passed 7,527 tests / 94.99% coverage, but was superseded by the source fix and its renewed gate. It is not the final qualification evidence.
- `dotnet list TiaMcpServer.slnx package --vulnerable --include-transitive --no-restore --format json` completed successfully for five projects, reporting no known vulnerabilities or diagnostics. The review fix changed no dependencies.
- Exact commands, failures and results remain in ignored `.superpowers/sdd/2026-10-23-c5-sealed-network-object-cursors/`; full build/test/coverage evidence is under ignored `TestResults/`. No live report has been created because no live run occurred.

Rulings and scope limits:

1. A complete coherent `sessionIdentity` is authoritative when optional `resolvedProjectPath` is absent; check the latter for conflict when present, matching hardware behavior. Requiring it unconditionally rejected valid existing replies.
2. The broader gate required migrating the additional `NetworkIntrospectionEndToEndTests` consumer from a hardcoded unsigned token to the actual public cursor. The review fix also extends `NetworkPayloadContract` beyond the original file list because the defect belongs at that shared trust boundary; no broad catch was added.
3. Task evidence was recorded directly in PowerShell ledgers alongside generated briefs, and bounded native searches were used where the worktree index was absent. The same RED/GREEN, commit and verification requirements were retained.
4. Documentation is committed now as an offline implementation record with the live gate visibly pending, honoring the explicit request to commit docs. This does not mark Task 3's live-dependent acceptance complete or waive the gate.
5. Existing worker hash/order algorithms, local-context coherence checks and item-boundary cancellation semantics are preserved. The two-call FakeWorker scenario is bounded evidence, not a complete inventory simulator. Full native/installed behavior and performance are not established by offline tests.

At this offline checkpoint, remaining gates were package preparation, separate installation/exact-target
authorization, the scoped read-only smoke, actual evidence, and remote-write approval before
publishing/merging C-5. No installation, live TIA operation, push, PR creation or merge was performed
during that offline phase. The following record supersedes the pending package/live status.

## Installed-candidate live execution record (2026-10-23)

The maintainer installed `4.1.2-local.110.g5647477` and identified the exact already-open disposable
standalone target in response to the pending authorization request. The scoped read-only gate
**passed** on that installed candidate in `full` mode. Source remains `db2947b`; package HEAD
`5647477` adds documentation only. All 56 installed runtime files matched the verified real-reference
package by SHA-256.

- Explicit `pageSize:1`, `objectKinds:["deviceItem"]`: first page and continuation succeeded, with one item per page and total 1,087. Public cursor purpose was `network-object-page` with the complete host/worker tuple around the retained internal token.
- An interior-character tamper returned per-item `invalid_cursor`, null result and no omission. A valid continuation immediately afterward succeeded in the same batch, preserving the original second item.
- Same-owner binding with `forceRebind:true` returned `unchanged`. Exact host binding tuple/revision 1, worker session identity and process issuer stayed equal. The original cursor remained usable; fresh first/second pages also succeeded and matched the original sequence.
- All nine text documents exactly matched JSON serialization of `structuredContent`. Seven Network operations yielded six successes and one expected rejection, zero omitted/skipped. Only initial binding carried the generic access-dialog advisory; all subsequent warnings were empty.
- Final status retained the same path, open state, `isModified:false`, last-modified time and size. AP21 SHA-256 and Portal/host/worker PID plus creation time were unchanged. No project mutation, lifecycle operation, compile, online operation or restoration occurred.

The [scoped report](../acceptance/reports/2026-10-23-c5-network-object-cursors-live-verification.md)
retains provenance, checks and unexecuted limits. De-identified receipts are in ignored
`TestResults/c5-live-20261023/verification-manifest.json`; no real target name/path/selector or raw
cursor is stored there. No .NET suite rerun was needed for these documentation-only edits.

Remaining gates: approval for remote publication/integration of C-5, then C-9's rebase, serialized
combined qualification and final matrix. No push, PR creation or merge occurred in this gate;
C-5-before-C-9 integration order is unchanged.
