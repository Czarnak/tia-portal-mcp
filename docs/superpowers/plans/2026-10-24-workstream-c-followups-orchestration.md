# Workstream C follow-ups orchestration plan

> **For agentic workers:** Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` when implementation is authorized. Track the shared checklists below; do not create separate plans for these PRs.

**Goal:** Resolve the remaining Workstream C validation, cursor and binding follow-ups through seven separate PRs, ending with cleanup and accurate completion records.

**Architecture:** Keep the existing host/worker split, shared validation rules, authenticated cursor codecs and binding policies. Tighten the existing boundaries without adding a new pipeline, cursor envelope or dependency. Implement PR2 first, then five independent lanes, then the combined cleanup/documentation PR.

**Tech stack:** Windows PowerShell, .NET 10 host/tests, netstandard2.0 contracts, net48 worker, xUnit and the existing reference stubs/FakeWorker.

**Spec and authority:** The maintainer's 2026-10-24 orchestration decision in this conversation; the [codebase simplification review](2026-10-17-codebase-simplification-review.md), [Workstream C execution record](2026-10-21-workstream-c-plan.md), and [open improvement-log entries](../../IMPROVEMENT_LOG.md). This document is the only new plan. PR1–PR7 are local sequence labels, not GitHub PR numbers; former PR8 is absorbed into PR7.

## Status and authorization

- Planning baseline: clean `main` at `0bd41d4`, the merge of C9 through GitHub PR #173. Revalidate repository state before execution.
- The initial plan-only restriction is superseded by subsequent maintainer authorization. PR1–PR6 are implemented and merged; their exact qualification and merge identities are in the completion ledger below.
- On 2026-10-24 the maintainer authorized PR7 implementation, removal of every already-resolved improvement-log entry, pushing and opening a PR. PR7 starts from clean `main` at `1d148e7cb8259f3350122a3d82464e734b39526a` on `chore/c-followups-pr7-cleanup-records`. Merging PR7 is not part of that authorization.
- Any installed-candidate or live TIA activity needs separate, exact-scope authorization. Offline verification never counts as native acceptance.

## Sequence and branch ownership

| Stage | PR | Local branch | Start condition |
| --- | --- | --- | --- |
| First | PR2: non-nullable argument rejection | `fix/c-followups-pr2-null-arguments` | Plan committed here; implementation authorized |
| Parallel | PR1: operation-name diagnostic limit | `fix/c-followups-pr1-operation-names` | PR2 merged; record its resulting `main` SHA |
| Parallel | PR3: root argument-name diagnostic limit | `fix/c-followups-pr3-root-names` | Same merged PR2 base |
| Parallel | PR4: oversized tree/HMI cursor issuance | `fix/c-followups-pr4-cursor-overflow` | Same merged PR2 base |
| Parallel | PR5: worker content-read identity backstop | `fix/c-followups-pr5-worker-identity` | Same merged PR2 base |
| Parallel | PR6: reject unbound explicit-path resolution | `fix/c-followups-pr6-unbound-path` | Same merged PR2 base |
| Last | PR7: C6 cleanup, disposal coverage and completion records | `chore/c-followups-pr7-cleanup-records` | PR1–PR6 all merged |

1. Commit this plan and the index entry on PR2's branch. After implementation is authorized, finish, qualify, review and merge PR2 with its null-rejection fix; the plan-only commit is not a completed PR2.
2. Only after that merge, create one isolated managed worktree/branch for each of PR1, PR3, PR4, PR5 and PR6, all from the same recorded `main` commit. Prefer the app's managed-worktree tools; inspect attachments and reuse suitable worktrees where possible.
3. Assign one implementer per lane, with ownership restricted to its files below. All agents share the machine: preserve other edits and reserve one controller-managed slot for builds/tests. A free worktree is not permission to run concurrent qualification or live activity.
4. Default integration order is PR1 → PR3 → PR4 → PR5 → PR6. These PRs have no product dependency on each other; the controller may merge a different ready lane first and record the order. Keep merges sequential and rebase each remaining branch onto current `main` before its final gate.
5. Start PR7 from `main` containing all five merges. It owns the former PR8 documentation work; do not create an eighth PR or a separate PR7 plan.

PR2 and PR3 share `StrictArgumentsTool.cs` and its tests; PR2-first removes that concurrent edit. PR4 overlaps the stale imports cleaned by PR7, which is why PR7 goes last. PR5 owns policy/identity tests; PR6 owns resolver/client integration tests. If a lane discovers a required edit in another lane's file, report it to the controller and serialize that edit before proceeding.

## Global constraints and shared records

- Keep all 19 tools structured; argument-shape rejection (PR2/3) remains canonical `validation_error`, MCP `isError:true`, with zero worker requests and zero write-audit records. Catalog/value denials (PR1) retain the existing entered-pipeline audit behavior; do not reclassify them as argument-shape failures.
- Preserve the 50-item batch bound, 256-character operation-ID bound, 4,096-character cursor bound, effective response budgets, existing access modes and binding/cursor invalidation rules. Do not change contract versions, parameter names or paging defaults.
- New diagnostic-name limits are 256 characters. Do not lower the existing operation-ID limit or truncate a valid selector/identity to manufacture a different request.
- Content calls remain bound-project-only. Preserve unbound status, configured startup grounding, open/create transitions and the empty-state verification after close. Never require verified binding globally inside the resolver.
- No direct Siemens execution in the host, no implicit project opening, no new dependencies and no Workstream D client/lifecycle refactor.
- PR1–PR6 own code/tests and any necessary public operation-reference update. Reserve shared improvement-log closure, historical review/Workstream C status reconciliation, this plan's completion ledger and general index reconciliation for PR7/controller ownership. This avoids five branches rewriting the same records.
- Keep per-lane commands, RED/GREEN results, commit IDs, review findings and gate results in an ignored lane-specific directory under `TestResults/`. The controller carries those records into PR descriptions and PR7; do not commit raw logs or private project data.
- Use conventional commits after each implementation/fixing step. Keep the plan-only commit separate. Use GitHub MCP first for authorized PR creation/read-back and attach each created PR to the task.

## Review focus

1. **Nullable controls:** reject forbidden explicit null, while advertised nullable strings/arrays/value types and `bind_project.forceRebind:null` keep working. PR2 owns these tests.
2. **Large caller names:** 256/257-character and very large names cannot bypass response bounds or reach the worker; short diagnostics remain useful. PR3 shape rejection is unaudited; PR1 catalog denial retains its existing audit behavior. PR1 and PR3 own these tests.
3. **Issued versus supplied cursors:** a valid first page whose continuation is too large must get size-specific handling; malformed supplied cursors retain their existing categories. Preserve HMI warnings/later items and terminal pages. PR4 owns these tests.
4. **Identity policy exceptions:** missing identity blocks every content method, but neutral status, basic post-close status, inventory, selection and open/create keep their exemptions; supplied identities still validate. PR5 owns these tests.
5. **Resolver preservation:** reject unbound-plus-explicit-path without changing the snapshot, while null status, configured startup, bound-path comparisons and lifecycle verification retain their behavior. PR6 owns these tests.

## PR2: non-nullable root arguments

**Owns:** `TiaMcpServer/Tools/StrictArgumentsTool.cs`; tests in `TiaMcpServer.Tests/Tools/StrictArgumentsToolTests.cs`, `ToolOutputContractConformanceTests.cs`, `LifecycleMcpProtocolTests.cs` and `BindProjectToolProtocolTests.cs`.

**Boundary:** cache the non-nullable root parameter information at tool construction; reject explicit null before SDK invocation. Keep nullable parameters and existing nested-member handling unchanged. This is not a general serializer migration.

- [x] Add regression cases for non-nullable string and operation-array nulls, including `open_project.projectPath` and a write tool's `operations`. Supply all other required arguments so a missing argument cannot mask the null check.
- [x] Assert canonical rejection, zero worker activity and zero audit records. Preserve nullable controls, `BindProject_NullForceRebind_UsesDefaultFalseThroughSdk`, and rejection of `dryRun:null`.
- [x] Observe the relevant failures before the fix; implement the smallest shared rejection, run the focused tests and commit.
- [x] Complete independent review and the common final gate, then merge before creating the parallel branches.

## PR1: operation-name diagnostic limit

**Owns:** `TiaMcpServer/OperationBatches/OperationBatchRules.cs` and its existing callers in Network, PLC, HMI read/write and VCI read/write catalogs. Prefer changing the shared rule once; edit catalogs only where necessary to prevent an earlier echo.

**Tests:** `TiaMcpServer.Tests/OperationBatches/OperationBatchRulesTests.cs`; catalog tests `Network/NetworkOperationCatalogTests.cs`, `Plc/PlcOperationCatalogTests.cs`, `Hmi/HmiOperationCatalogTests.cs`, `Hmi/HmiWriteCatalogTests.cs`, `Vci/VciReadCatalogTests.cs` and `Vci/VciWriteCatalogTests.cs`, all under `TiaMcpServer.Tests/`. PR1 also owns the public rejection cases in `TiaMcpServer.Tests/Network/NetworkToolsTests.cs` and `NetworkGuardedWriteMcpTests.cs`.

- [x] Reject an operation name above 256 characters before interpolation, with a fixed validation message that contains none of that name. Preserve blank-name, ID, duplicate, short unknown-name and wrong-category behavior unless the new limit applies.
- [x] Pin 256/257 and a 200,000-character name across all six catalogs. Add public read/write rejections asserting a bounded canonical document, no full-name echo and no worker dispatch. Preserve the existing write audit for an entered catalog denial.
- [x] Observe the failures, implement, run the focused tests and commit. Qualification/review is recorded at the exact source/base in the completion ledger; later integration is not another full-suite claim.

## PR3: root argument-name diagnostic limit

**Owns:** `TiaMcpServer/Tools/StrictArgumentsTool.cs`, `TiaMcpServer/Tools/ProjectReadTools.cs`; tests in `TiaMcpServer.Tests/Tools/StrictArgumentsToolTests.cs`, `ToolOutputContractConformanceTests.cs` and `TiaMcpServer.Tests/Project/ProjectTreeStructuredProtocolTests.cs`.

- [x] Use the same bounded unknown-argument formatting in the strict wrapper and the outer tree wrapper. Keep names of at most 256 characters; omit longer names completely while retaining accepted-key guidance.
- [x] Cover 256/257 and a very large key through an ordinary production tool and `browse_project_tree`. Assert bounded canonical rejection, no audit/worker activity and preservation of PR2's null checks.
- [x] Observe the failures, implement, run focused tests and commit. Do not reopen PR2's metadata design merely to format errors.

## PR4: oversized tree/HMI cursor issuance

**Owns:** `TiaMcpServer/ProjectTree/ProjectTreePageProjector.cs`, `ProjectTreeBrowseCoordinator.cs` and `TiaMcpServer/Hmi/HmiPayloadContract.cs`, plus their existing tests. Use `TiaMcpServer.Tests/Project/ProjectTreePageProjectorTests.cs`, `ProjectTreeBrowseCoordinatorTests.cs`, `ProjectTreeStructuredProtocolTests.cs`, `TiaMcpServer.Tests/Hmi/HmiPayloadContractDecodeTests.cs` and `HmiReadToolsTests.cs` as the primary test seams.

**Boundary:** keep shared `AuthenticatedCursorProtector.Seal`/`Open` and the wire envelopes unchanged. Recognize `CursorException.InnerException is AuthenticatedCursorSizeException` at issuance, following `HardwarePageProjector` and `NetworkObjectPaginationCoordinator`.

- [x] Reproduce overflow from a valid long binding path on an initial request that supplied no cursor.
- [x] Return a typed HMI cursor-limit omission carrying actual/limit sizes and retry guidance. Return tree's existing failure shape with `result_metadata_too_large` and explicit size guidance; do not add an omission envelope to tree responses.
- [x] Pin canonical text/structured equality, retained HMI warnings and later items, and terminal-page success without a cursor. Preserve invalid/tampered/stale input-cursor behavior and binding state.
- [x] Observe the failures, implement, run focused tests and commit. Leave stale-import cleanup to PR7.

## PR5: worker content-read identity backstop

**Owns:** `TiaMcpServer.Contracts/Safety/OperationPolicyCatalog.cs`; `TiaMcpServer.Tests/Safety/ReadOnlyModeTests.cs` and `TiaMcpServer.Tests/Worker/FakeWorkerIdentityEnforcementTests.cs`. The existing worker and FakeWorker consume this shared policy; avoid duplicating a method-name list in either dispatcher.

- [x] Make `RequiresExpectedSessionIdentity(string operation)` require identity for `ProjectCapabilityCatalog.RequiresBoundProject(operation)`, before the Observe/TemporaryExport exemptions.
- [x] Pin every registered content method, including catalog search, HMI, VCI and hardware continuations. Add direct-worker missing-identity denials before scenario dispatch, valid-identity controls and retained mismatch behavior.
- [x] Preserve identity-optional status/basic status, inventories, selection and open/create; supplied identities still validate. Keep lifecycle/rebind probes protected, including `LifecycleGuardedContractTests.NoProjectVerification_RequiresWritableMode_WithoutRequiringAProjectIdentity`.
- [x] Observe failures in the new policy/rejection cases, implement, run focused tests and commit. No native dispatcher body or identity-comparison refactor is included.

## PR6: narrow unbound explicit-path resolution

**Owns:** `TiaMcpServer.Contracts/Project/ProjectSessionBinding.cs`; `TiaMcpServer.Tests/Project/ProjectSessionBindingTests.cs` and `TiaMcpServer.Tests/Worker/OpennessWorkerClientIntegrationTests.cs`.

- [x] Change only the unbound-with-no-configured-path plus explicit-request-path branch in `TryResolveWithSnapshot` to reject with bind-first guidance, null effective path and unchanged snapshot. Replace tests that pin the obsolete acceptance behavior; preserve their other assertions.
- [x] Keep unbound-plus-null status neutral. Pin configured grounding success/failure, equivalent/different bound paths, invalidated binding, open/create resolved identities and post-close/basic verification.
- [x] Update FakeWorker scenario helpers that used an explicit path as test selection; preserve neutral-status coverage. Controller-approved fixture adjustments also reached `FakeWorkerBinding.cs`, `StandaloneWireContractTests.cs`, `ReadWriteModeCeilingTests.cs` and `BindOpenProjectIntegrationTests.cs`; no FakeWorker dispatcher change was needed.
- [x] Observe the failing rejection test, implement, run focused tests and commit. Do not remove status/lifecycle path APIs, introduce a new binding API or perform D-6's worker-client split.

## Shared qualification and integration

- [x] For each behavioral PR, run its focused RED/GREEN cycle, then review the complete diff for scope, correct linked-source test wiring and all five Review Focus rows that apply. Use an independent reviewer; fix findings in separate validated commits.
- [ ] Original per-merge requirement: rebase onto current `main`, inspect the resulting diff and run the final gate on that exact candidate. The completed merges are recorded below, but the available lane evidence does not establish this for every later integration candidate. Retain this evidence limitation rather than retrospectively marking it passed.
- [x] Hold one controller-managed build/test slot. In a fresh worktree, restore first: `dotnet restore TiaMcpServer.slnx /p:UseTiaPortalReferenceStubs=true`.
- [x] Build serially: `dotnet build TiaMcpServer.slnx -m:1 --no-restore --configuration Release /p:UseTiaPortalReferenceStubs=true`.
- [x] Use fresh ignored lane-specific results directories, with process-local `TEMP` and `TMP` pointing to their `temp/` subdirectories; restore previous environment values afterward. PR7 uses `TestResults/c-followups-pr7/`.
- [x] Run each behavioral lane's full serialized suite from those Release binaries: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build --no-restore --configuration Release --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory <unique-results-directory> -- xUnit.ParallelizeTestCollections=false xUnit.ParallelizeAssembly=false xUnit.MaxParallelThreads=1 RunConfiguration.MaxCpuCount=1`.
- [x] Require zero failed/skipped tests and a nonempty Cobertura report. Run `./scripts/verify-coverage-threshold.ps1 -CoveragePath <that-report> -MinimumLineRate 0.80`; inspect coverage of materially changed logic. Pass `git diff --check` as well.
- [x] PR1–PR6 were published, read back and attached by the controller, then merged sequentially. PR7 publication follows its own checklist below; its merge remains outside the current authorization.
- [x] Do not rerun a build, full suite or packaging merely because a later commit changes only evidence/docs. Retain valid source evidence and rerun the checks affected by any source change, rebase, failure or unresolved concern.

## Live TIA boundary

None of the seven narrow scopes requires a mandatory new native acceptance run. PR1/2/3 reject input in the host, PR4 changes host cursor projection, PR5 rejects missing identity before the existing native path, and PR6 rejects an obsolete internal selection case. PR7 changes cleanup/tests/records. Real-client protocol checks for PR2/3 can run without a Portal/project and are distinct from live TIA acceptance.

An optional combined **read-only** smoke for PR5/6 is recommended after the parallel PRs merge and the combined candidate is offline-qualified. With separate exact-target authorization, verify neutral unbound status → explicit bind → representative content read, configured `--project` startup grounding, and reads on an already-open local session. Record the installed candidate/provenance and unchanged process/project state; do not open, save, close, restart or mutate projects as part of that smoke. A skipped smoke remains an explicit native-evidence limitation, not a blocker invented after the fact.

If implementation expands into attachment/selection, identity comparison, wire routing or lifecycle transition order, stop the affected expansion and reassess its scope and live gate with the maintainer. Do not silently inherit the original broader C-0 acceptance matrix or claim its old live evidence qualifies a new candidate.

## PR7: cleanup and documentation fan-in

Start only after all PR1–PR6 merges. This PR includes the former PR8 scope.

**Code/test ownership:** remove the stale `using TiaMcpServer.Network;` from `TiaMcpServer/ProjectTree/ProjectTreePageProjector.cs`, `TiaMcpServer/Hmi/HmiPayloadContract.cs`, `TiaMcpServer.Tests/Project/ProjectTreeBrowseCoordinatorTests.cs`, `ProjectTreeCursorCodecTests.cs`, `TiaMcpServer.Tests/Hmi/HmiPageCursorCodecTests.cs` and `HmiReadToolsTests.cs`. In `TiaMcpServer.Tests/Cursors/AuthenticatedCursorProtectorTests.cs`, move the existing test field/helper beside their peers and extend the existing disposal test to assert `ObjectDisposedException` for valid `Seal`/`Open` calls. These are characterization assertions for existing behavior; do not manufacture a RED failure or change production disposal semantics.

**Documentation ownership:** `docs/IMPROVEMENT_LOG.md`, the original simplification review, `2026-10-21-workstream-c-plan.md`, this plan's ledger, the docs index and any affected current operation reference.

- [x] Verify which cleanup lines remain after the preceding fixes; remove only stale imports and move only the identified test members.
- [x] Add the disposal assertions, run the cursor tests, compile affected projects and independently review the complete cleanup diff. All 318 focused tests passed; independent complete-diff review found no actionable findings. No characterization failed or production behavior changed.
- [x] Close all six logged follow-ups with exact merged PR/commit and verification references. Record the C6 cleanup/test resolution. The previously missing RED evidence is already present in the original review record; do not invent or rerun historical RED tests.
- [x] Record C9's actual merge through PR #173 at `0bd41d4` and supersede its stale integration-pending text with a dated completion note. Preserve historical qualification and live-evidence limits; merging does not establish a separate publication/release event.
- [x] Reconcile operation references/index links and record whether the optional PR5/6 smoke was performed or skipped. Do not upgrade offline/FakeWorker evidence into native acceptance.
- [x] Maintainer extension: remove every already-resolved improvement-log entry, including completed history outside Workstream C; retain partial/unresolved follow-ups and the HMI reference anchor. The log is reduced from 1,443 to 381 lines across 16 open sections.
- [x] Check Markdown links, whitespace, changed-file scope and `git diff --check`: 219 local links/anchors passed, including the HMI backlink. Cleanup is committed separately at `7abe50b`; documentation is the final logical commit in this PR. No full suite/package rerun is needed for that documentation-only commit.
- [x] Independent complete-diff review found no actionable findings.
- [ ] Push and open PR7 through the authorized PR workflow; read back and attach the actual URL/head. Merging is not authorized by this implementation/push/open request. Keep its managed worktree attached for follow-up.

## Completion ledger

Reconciled on 2026-10-24 from the controller's per-lane records and GitHub MCP read-back of merged PRs. The table reports the actual qualified source and base; it does not claim that every later merge candidate or the combined PR5/PR6 source received another full suite. All recorded full suites had zero failures/skips, a nonempty coverage report and a passing 80% gate. Each Release stub solution build reported seven existing warnings and zero errors. Independent lane and cross-lane reviews found no actionable findings.

| PR | Merge identity | Qualification and review | Native evidence |
| --- | --- | --- | --- |
| PR2 | [#174](https://github.com/Czarnak/tia-portal-mcp/pull/174), `c375f79f97319ac4b262edf7ac27fee356972c01` | Head `ed9c3efb766de3a5de5d04243ee0900e3b8e1e51`, base `0bd41d4`; RED 18, focused GREEN 247; full 7,561; coverage 94.99%; all eight added production lines covered | Not performed; no live gate |
| PR1 | [#175](https://github.com/Czarnak/tia-portal-mcp/pull/175), `22878be3f0bafee5e3fdca2bee170bac10f182f9` | Head `34bbaa7aa2eedeeb83dbdee32acf2df60b0ffa3c`, base `c375f79`; RED 22, focused GREEN 407; full 7,594; coverage 95.05%; shared rule 16/16 lines covered | Not performed; no live gate |
| PR3 | [#176](https://github.com/Czarnak/tia-portal-mcp/pull/176), `018ff6f627b4b5b158a3525a49c74c73784fdd58` | Head `fcb717e63c5a87e581c281c49810633ad22f6d04`, base `c375f79`; RED 4, focused GREEN 39; full 7,567; coverage 94.99%; changed production lines covered | Not performed; no live gate |
| PR4 | [#177](https://github.com/Czarnak/tia-portal-mcp/pull/177), `ffe3214f7fedf3e4b438b0cf67067270a805bbb6` | Head `26403a06e89268bc02c5a5965f02c52e4e47765b`, base `c375f79`; RED 3 with two terminal controls passing, focused GREEN 108; full 7,567; coverage 94.99%; added production lines covered | Not performed; no live gate |
| PR5 | [#178](https://github.com/Czarnak/tia-portal-mcp/pull/178), `61b5a75bc00c87296e0b5f03532d985964dc488d` | Head `534e85b6a20b243163b42d7a084c3f478f82444b`, base `ffe3214`; RED 5, expanded focused GREEN 435; full 7,636; coverage 95.05%; added policy lines covered. Initial full-run fixture failures were corrected in a separate test commit and requalified | Optional combined smoke skipped; not authorized/performed |
| PR6 | [#179](https://github.com/Czarnak/tia-portal-mcp/pull/179), `1d148e7cb8259f3350122a3d82464e734b39526a` | Head `cfe37ad428f2ca6b5a193195c3afa787e6d2a766`, base `ffe3214`; RED 5, focused GREEN 211; full 7,609; coverage 95.06%; both changed resolver lines covered | Optional combined smoke skipped; not authorized/performed |
| PR7 | Cleanup `7abe50b`, based on all six merges at `1d148e7`; publication/merge pending | Six stale imports removed; test members regrouped; valid `Seal`/`Open` disposal assertions added. Release stub restore/build passed (seven existing warnings, zero errors); 318 focused cursor/projection tests passed, zero failures/skips; 219 local links/anchors and whitespace passed. Independent complete-diff review found no actionable findings | Not performed; no live gate |

The six follow-ups are resolved by the merged PRs above; resolved improvement-log entries are removed rather than retained as a second completion history. Original runtime RED evidence remains in the review record. C6 cleanup and its additional disposal characterization are completed in PR7. C9 merged through [#173](https://github.com/Czarnak/tia-portal-mcp/pull/173) at `0bd41d4f612e2dd397f34f34d4378abca2a81182`; its earlier 7,541-test/94.98% qualification and separate C5 live-evidence limits remain in the linked historical records. This merge establishes integration, with no claim of a separate release event.

Private command/log/TRX/coverage records remain under ignored `TestResults/c-followups-pr2*/` and `TestResults/c-followups-controller/` in the controller checkout. Per-lane artifacts were preserved under `TestResults/worktree-cleanup-20261024/` with a SHA-256 manifest before the earlier worktrees were archived; PR7's logs and TRX are under its own `TestResults/c-followups-pr7/`. PR descriptions above retain the qualified heads, commands/results and review summaries without private fixture data. No historical RED tests were rerun for PR7, and no optional PR5/PR6 smoke, installation or native acceptance was performed.
