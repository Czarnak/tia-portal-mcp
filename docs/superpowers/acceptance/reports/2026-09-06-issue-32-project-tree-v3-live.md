# Issue #32 project-tree browsing v3 — live TIA Portal V21 acceptance

**Result: PASS — accepted by the user on 2026-09-12**, using the combined offline and live evidence and the explicit live ambiguity waiver below. This acceptance does not change the preserved harness exit codes or mark the waived check as passed.

**Boundary:** read-only TIA Portal V21 acceptance: no project save, engineering mutation, project compilation, lifecycle mutation, PLC control, or deployment was performed. No plant or physical-PLC acceptance is claimed.

Exact project identities, paths, and fixture-specific device, block, folder, and selector names are intentionally omitted at the user's request. They remain in local ignored evidence and the private execution report. This tracked record uses only **Fixture A** and **Fixture B**.

## Build and evidence identity

- Tested implementation: `1670b266f39bd518f344c8f2dc7c7301b0b66918` on `feature/v3-cutover`.
- Environment: Windows build 26200, PowerShell 7.6.6, .NET SDK 10.0.401, net8.0 host and net48 worker; installed modular TIA Portal V21 API version `2100.0.121.1`.
- Real V21 assembly Release build: exit 0, zero errors, seven existing xUnit2031 analyzer warnings. No reference-stub option was supplied. An earlier restricted build failed during NuGet SSL/authentication; its logs remain preserved.
- Prior offline controller gate at the tested commit: 3,017/3,017 tests, line coverage 0.929, focused project-tree suite 61/61, and both package verification modes passed. These are retained offline results, not newly executed tests or live proof.
- Live source: [read-only harness](../../../../scripts/live-test-project-tree-v3.ps1). The host and worker both logged READONLY, and the harness verified exactly `browse_project_tree`, `execute_read_batch`, `get_project_status`, and `network_read` before browsing. Runtime binding logs matched each authorized fixture.

The private execution report retains the exact commands, project binding, timestamps, exit codes, and artifact hashes. The following command forms document the invocation with private values supplied through environment variables; they were not rerun to write this report:

```powershell
dotnet build TiaMcpServer.sln -c Release -m:1 --disable-build-servers /p:TiaPortalV21Dir="$env:TIA_PORTAL_V21_API_DIR"
.\scripts\live-test-project-tree-v3.ps1 -ProjectPath $env:TIA_MCP_LIVE_PROJECT_PATH -ServerPath .\TiaMcpServer\bin\Release\net8.0\TiaMcpServer.exe -EvidencePath $env:TIA_MCP_LIVE_EVIDENCE_PATH
```

## Three-run timing and material improvement

All measurements are milliseconds for a complete initial MCP call; continuation-page collection is separate. Spread is maximum minus minimum. The user accepted Fixture A's **54.44% median reduction** as a material improvement. Fixture B supplies additional evidence with a **62.57% median reduction**; the two fixtures have different content and are not equivalent-project benchmarks.

| Fixture | Initial mode | Run 1 | Run 2 | Run 3 | Median | Minimum | Maximum | Spread |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Fixture A | Full | 17284.3526 | 1627.4875 | 2146.9116 | 2146.9116 | 1627.4875 | 17284.3526 | 15656.8651 |
| Fixture A | Depth 2 | 1634.9528 | 2359.1981 | 2473.5116 | 2359.1981 | 1634.9528 | 2473.5116 | 838.5588 |
| Fixture A | One Device | 929.2938 | 1021.1266 | 978.0573 | 978.0573 | 929.2938 | 1021.1266 | 91.8328 |
| Fixture B | Full | 42772.6072 | 24523.2483 | 21983.9363 | 24523.2483 | 21983.9363 | 42772.6072 | 20788.6709 |
| Fixture B | Depth 2 | 22787.1233 | 23709.1593 | 24298.7523 | 23709.1593 | 22787.1233 | 24298.7523 | 1511.6290 |
| Fixture B | One Device | 9604.1947 | 9179.2877 | 8903.1410 | 9179.2877 | 8903.1410 | 9604.1947 | 701.0537 |

The absolute median reduction was 1168.8543 ms for Fixture A and 15343.9606 ms for Fixture B. There is no invented numeric SLO. Measurements ran in fixed order: full, depth 2, then Device. Initial worker/session startup contributes to the first full sample; warm-up, caching, and measurement order may influence the distributions. All first samples are retained.

## Paging and semantic evidence

Each mode's third snapshot was collected to completion. Earlier initial pages supplied timing samples. Fixture A returned ten successful single-page snapshots. Fixture B returned 18 successful pages across ten snapshots, including **eight successful continuations**: four for the full tree and four for the selected-Device tree.

| Fixture | Completed snapshot | Nodes | Pages | Nodes per page | Maximum canonical page characters |
| --- | --- | --- | --- | --- | --- |
| Fixture A | Full | 123 | 1 | 123 | 17464 |
| Fixture A | Depth 2 | 4 | 1 | 4 | 981 |
| Fixture A | One Device | 70 | 1 | 70 | 10189 |
| Fixture A | Deep selector | 1 | 1 | 1 | 984 |
| Fixture B | Full | 890 | 5 | 200, 200, 200, 200, 90 | 32315 |
| Fixture B | Depth 2 | 83 | 1 | 83 | 9790 |
| Fixture B | One Device | 804 | 5 | 200, 200, 200, 200, 4 | 32349 |
| Fixture B | Deep selector | 1 | 1 | 1 | 1107 |

Both five-page chains had offsets 0, 200, 400, 600, and 800. Independent raw-evidence checks confirmed stable snapshot identity and totals, contiguous sequence/offsets, exact returned counts, unique IDs, parents before children, complete terminal cursors, and the exact six-field v3 node shape. Text and structured documents matched. No `details.Path`, truncation marker, or canonical response above 60,000 characters appeared.

Both reconstructed deep selectors matched the corresponding selected-Device subtree, including equal semantic-projection hashes. Each selected subtree was a single leaf; this does not claim live equivalence of a branching deep subtree.

| Requirement | Accepted evidence |
| --- | --- |
| Complete pages, order, parents, totals, and no truncation | PASS — live checks and independent inspection, including both five-page chains |
| Canonical text/structured equality and response-size bound | PASS — live documents and independent inspection |
| Reconstructed deep selector equivalence | PASS for the selected leaves; branching-subtree limitation retained |
| Missing selector | PASS — exact `target_not_found` category in both fixtures |
| Invalid selector structure | PASS — exact `invalid_selector` category in both fixtures |
| Live ambiguous selector | **WAIVED, not passed** — explicit user acceptance-scope revision described below |
| One initial worker call and zero continuation worker calls | ACCEPTED from offline integration/source proof alongside live continuation behavior; not live IPC instrumentation |
| Material Device-scoping improvement | PASS — user accepted the recorded Fixture A timing distribution; Fixture B adds supporting measurements |

## Explicit evidence decisions

The user waived the live `target_ambiguous` check because TIA name uniqueness prevents the required same-parent duplicate-name state, including case-only variants, through normal engineering. Neither fixture contained a naturally ambiguous direct-child pair, and no ambiguous request was sent. Offline resolver ambiguity coverage remains distinct. This records the user's waiver rationale; it is not a claim that the live category was observed or that a fixture was mutated to manufacture it.

For worker-call counts, the user accepted the existing offline proof together with Fixture B's eight live continuation responses. In [coordinator tests](../../../../TiaMcpServer.Tests/Project/ProjectTreeBrowseCoordinatorTests.cs), `InitialRequestObservesOnceAndAllContinuationPagesUseTheCachedSnapshot` asserts one observation after initial, continuation, and replay calls; `ContinuationUsesCachedSnapshotAfterPersistentWorkerIsDisposed` exercises continuation after disposing the FakeWorker client. The [coordinator source](../../../../TiaMcpServer/ProjectTree/ProjectTreeBrowseCoordinator.cs) separates initial observation from cached continuation. Thus the accepted requirement is **one worker call initially and zero worker calls for continuation**. The live harness did not instrument IPC or traversal counters; its eight successful continuations are complementary behavioral evidence.

## Failed-attempt history and preservation

The first Fixture A attempt exited 1 after a 120-second wait for the first tree response. Initialize and tool discovery succeeded, but no tree or timing sample was returned. The user reported late Openness approval and explicitly authorized a retry. That retry connected and completed the measurements; this is consistent with the explanation but does not prove the cause of the first timeout.

The Fixture A retry and the Fixture B run each exited 1 at the ambiguity-fixture guard after completing their other checks. Their raw evidence still says `failed`. This report's PASS is the subsequent user acceptance of the combined evidence with the stated waiver, not a rewritten harness outcome. No automatic retry followed any failure.

Raw JSON evidence, protocol stdout JSONL, server/worker stderr, wrapper stdout/stderr, build logs, exact identities, and hashes remain locally under ignored `artifacts/issue-32-live/`; the detailed execution report and acceptance decisions remain in ignored `.superpowers/` records. Private identities and exact absolute paths are intentionally absent from this tracked report. Earlier evidence hashes remained unchanged across subsequent runs.

Read-only mode, the verified tool surface, and unchanged project-file metadata support the no-save/no-mutation boundary. Content hashes of the open projects and comprehensive unsaved-state inspection were not established. Existing TIA Portal processes remained after harness shutdown. This is not persistence, deployment, PLC control, or plant acceptance.

The deeper direct Openness selector resolver and depth-pruned walk remain deferred optimizations. The accepted behavior is the shipped typed/scoped initial walk followed by host snapshot pagination; this report does not claim those deeper optimizations have shipped.
