# HMI write acceptance — 2026-10-14

Case definitions are in the [design, section 7.3](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-10-13-hmi-write-design.md#73-live-cases-installed-tool-read-write-and-full); the [implementation plan](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-13-hmi-write.md) and [live write spike](2026-10-13-hmi-write-spike.md) provide the historical scope and Siemens baseline.

**Functional live verification complete within the limits below.** The exact production candidate passed offline qualification, both-mode functional cases and final audit reconciliation. Timing/memory observations are complete with explicit scope limits; the proposed single-observation memory return condition was not met. The maintainer explicitly chose: **“Retain current cache; leave O1 open.”** That decision is not a full performance-gate pass.

## Candidate and evidence boundary

Production source is `48f2edd8d7ecbb62a97a3e703dd51c797e1cd689` on `feature/hmi-write`, reviewed against `e0ad7b65d913cc22332836161c6843e3e402d194`. Live qualification uses the extracted real-assembly package `TiaMcpServer.4.0.0-local.47.g48f2edd.nupkg`, launched side by side. A fresh global-tool inventory confirms the installed connector remains `4.0.0-local.43.gdd9f40d`; it does not qualify this artifact. No global reinstall is claimed.

| Qualified artifact | SHA-256 |
| --- | --- |
| Candidate package | `3027120AA891E8C1FA32A37B81032F8FFC28A39C57D780D732838BC09203154B` |
| Extracted host DLL | `9C3413225066B7FC22EBF713BF386A7298A2AE80580A91AF9575916F53A5749E` |
| Extracted real-assembly worker executable | `C7BE2DD80D1EA569A2DE254C0F8A45D0F8034D825F5AD61A5B9D9EA6B47990DD` |

Source/binary continuity was recorded before and after the final offline suite. Later documentation commits can change Git HEAD without changing the qualified production source or these immutable binaries. Qualification belongs to the source revision and hashes above. The harness field `hostSha256` hashes the `dotnet` launcher, so it is not used as the host DLL hash.

Fixtures are described only as standalone project A, PLC P, Unified HMI H, and localSession. Exact selectors, project names/paths, editor captures and raw protocol/audit files remain private. All live mutations targeted explicitly authorized disposable fixtures and were serialized. This report does not qualify a PLC download, runtime deployment, or plant operation.

## Offline gate and independent review

| Gate | Evidence and result |
| --- | --- |
| Reference-stub solution build | Serial build passed, 0 errors, five existing nullable warnings, 21.77 seconds. |
| Real V21 assembly solution build | Serial build passed, 0 errors, the same five warnings, 27.26 seconds. |
| Real package | Pack passed at the exact source candidate; package provenance is pinned above. |
| Full serial coverage suite | 6,407/6,407 passed; zero failed, skipped/not-executed, errors, timeouts or aborted. TRX completed in 13m13.78s. |
| Changed executable production coverage | 1,256/1,292 lines = **97.21%**, 23 files, measured over the exact base/candidate range; aggregate 80% target met. |
| Source/binary continuity and whitespace | Source objects and four Debug binary hashes matched across the suite; clean tracked tree at qualification; implementation-range whitespace check passed. |
| Independent whole-branch review | Approved within the adjudicated scope. No open Critical or Important implementation finding; the existing-tag member composition gap is retained as a P2 scope limitation below. |

Coverage includes linked HMI worker classes with Siemens doubles and host/IPC behavior with FakeWorker. Actual net48 Program dispatch and Siemens runtime are outside that metric. Two small files are individually below 80%: linked HmiWriteDispatch 10/18 and host OpennessWorkerClient 5/7. Passive declarations and uninstrumented paths are excluded, not counted as covered. The canceled earlier-head suite and older 6,360-test result are retained historical evidence, not passes for this candidate.

## Live case results

`read-write` and `full` are separate executed modes. Expected negative cases pass only when their typed failure, mutation extent, independent readback and preservation/cleanup match the case. A completed runner alone is not a pass. HMI calls require no server elicitation; lifecycle confirmation follows mode.

| Case | Read-write | Full | Evidence and limits |
| --- | --- | --- | --- |
| L01 ordered create and preview | Passed | Passed | One seven-item call creates a table, global/DB/UDT bound tags, an internal tag, a discrete alarm on a UDT member, and an analog alarm. Preview/applied effects match; 7/7 items and 59/59 checks pass in each mode. |
| L02 collisions and case variants | Passed with platform limit | Passed with platform limit | Tag/table/alarm collision previews block; all three actual collisions return `guard_blocked`; complete protected read documents remain equal. Direct table/discrete/analog case-only changes and restorations each pass 9/9 checks. Direct tag case-only rename fails at the Siemens setter and preserves the original; see the limitation below. |
| L03 delete referenced tag | Passed | Passed | `hmi_deletes_referenced_tag` is informational and includes the owned alarm consequence. Preview/applied effects match semantically. Complete recursive cross-references have 57 references/65 locations in read-write and 46/54 in full, zero omitted sources. Tag deletion and independent absence pass. Alarm remains with the original trigger text and an empty trigger-bit address; Validate reports the missing member. Explicit alarm cleanup and independent absence pass. Consequence entries of different reference types are not counted as distinct alarms. |
| L04 delete table and contained tags | Passed | Passed | Complete inventory and preview agree on the three remaining tags. Informational guards list contents/reference consequences; semantic preview/applied effects agree. Table deletion succeeds with four absence checks. Three independent missing-tag reads cover exactly those tags; an additional missing-alarm read covers the prepared analog alarm. Only the owned table disappears; every other table entry remains identical. The previously deleted UDT tag has separate L03 absence evidence. |
| L05 missing PLC source | Passed expected failure | Passed expected failure | Create and connection setter apply; PlcTag setter fails. Typed `cleanup:"deleted"`, absence check and independent `target_not_found` prove cleanup; complete four-tag inventory before/after is equal. Root error is null and MCP `isError:false` for the attempted failure. |
| L06 plain-struct binding | Passed expected verification failure | Passed expected verification failure | Worker creation and binding succeed, but validation produces two unsupported-datatype diagnostics and only 6/8 verification checks pass. Independent read confirms the created tag exists despite overall failure. Explicit cleanup succeeds and independent absence is proved. This is not automatic rollback. |
| L07 default tag-table deletion | Passed expected failure | Passed expected failure | Siemens Delete fails with no applied properties; three preservation checks pass and the complete before/after document is equal. The default table and its existing tags remain. |
| L08 editor text rendering | Retained maintainer check | Retained maintainer check | Maintainer confirmed both languages render correctly, including displayName and tag/alarm texts with special characters. This is one human check on the earlier candidate; it was not repeated at 48f per mode. Latest readback/canonical checks do not independently prove editor rendering. |
| L09 local-session write then save | Passed | Passed | Each mode creates one owned table and internal Int tag, 2/2 items with successful verification; independent read sees the tag. Local save succeeds and verifies status `succeeded`, with `isModified:false`; subsequent status is clean. Explicit table/tag deletion succeeds; independent tag read is `target_not_found`. Entire final table-inventory result equals baseline. Final local save/status are clean. Each actual read-write save elicited once; the harness client accepted, which does not prove a human saw a prompt. Full's fourteen requests had zero elicitation. |
| L10 read-only policy | Passed scoped policy control | Not applicable to writable mode | Latest candidate advertises seven tools and no `hmi_write`; all nine direct read-only worker mutation methods return `access_denied` before mutation. Latest bound read-only content read was not reached after a dialog/timeout boundary. Earlier-head successful content read is historical, not latest-candidate content qualification. |
| L11 rename/reference propagation and rebind | Passed | Passed | Two rename/rebind items and ten checks pass, then two restoration items and ten checks pass. Entire restored tag/binding/alarm read documents equal baseline. Screen and owned alarm UsedBy follow the renamed/restored tag; complete captures have 8 references/8 locations in read-write and 9/9 in full, zero omissions. The extra full reference is an owned HMI tag; no earlier reference disappeared. DB source changes on the same connection, then is restored. Three scoped validation reads are clean. |
| L12 timing | Completed with baseline deviation | Completed with baseline deviation | Per-call results below; archival baseline has no write tool. No causal traversal-saving conclusion. |
| L13 memory | Completed with scope limits | Completed with scope limits | Three-repetition point-2 archival-reference differences are below 20 MB in both modes. Baseline 50-write point is unavailable. Additional genuine-owner point 4 has n=1 and does not meet the proposed 10% return; strict full gate remains unqualified. |

Additional latest-candidate controls reject mixed explicit project paths in dry and actual entry with `validation_error`, null batch/verification, and no mutation. Completed resumed feature-response checks report parsed canonical text/structured parity and null omissions, with zero HMI elicitation. Case evidence comes from independently analyzed root-owned protocol captures; raw selectors and captures stay private.

Fresh native audit reconciliation matches **69 entered write calls to 69 audit rows**, including exact delivered canonical text and response hash: 62 `hmi_write`, five `save_project`, one `close_project` and one `open_project`. Confirmation counts match the expected `none`, `policy` and `user` outcomes. Identical delivered documents are grouped by hash with their expected call multiplicity, so repeated valid calls are not mislabeled duplicate audit records. One SDK-rejected invalid benchmark input has zero audit rows, as expected before pipeline entry. This is a match of the delivered acceptance responses, not an audit of unrelated user history.

## Timing (L12)

The baseline is archival `74827567d118a185759b88167b0d3b71d3b6f802`, production-source equivalent to the initial branch base. It is **not the currently installed release** requested in spec section 7.3. It has no `hmi_write`, so a matched 20-item preview baseline is unavailable. Each row is one fresh host/worker process run on the same fixture while TIA remains running. Read 1 is the first named read after binding; the warm median has two samples within that one run.

| Mode/build | Initialize ms | Tools/list ms | Bind ms | Read 1 ms | Read 2 ms | Read 3 ms | Later-read median ms | 20-item preview ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Read-write archival baseline | 785.073 | 29.832 | 8479.565 | 1742.212 | 1598.802 | 1595.659 | 1597.2305 | N/A |
| Read-write candidate | 534.816 | 28.439 | 1578.081 | 1535.634 | 1299.022 | 1361.917 | 1330.4695 | 19502.161 |
| Full archival baseline | 536.830 | 27.380 | 1405.848 | 1590.454 | 1304.192 | 1533.876 | 1419.0340 | N/A |
| Full candidate | 643.841 | 36.468 | 2795.375 | 3457.269 | 3053.303 | 3383.624 | 3218.4635 | 21756.375 |

Read-write candidate cold/later-read times were 11.86%/16.70% lower in these observations; full candidate times were 117.38%/126.81% higher. This is not a stable distribution or an isolated cache effect. Both candidate previews succeed with 20 same-value updates to one existing tag and valid caller-order dependencies; they are not writes to twenty distinct tags. All 18 benchmark tool calls have root success, canonical parity and exact captured-response equality; all 26 recorded requests have zero elicitation. The initial rejected-input candidate preview is excluded from timing qualification; its preceding reads remain a separate partial run.

## Memory (L13) and cache disposition

Each primary table cell is median **bytes (MiB)** across three fresh runs, n=3, with no unavailable counters. Point 1 follows binding; point 2 follows ten reads; candidate point 3 follows fifty same-value displayName updates to one existing tag. Each run retains the same sampled worker PID, owned host parent and exact package worker path. This establishes identity at sampling points, not uninterrupted continuity between them.

| Read-write build/point | Host working set | Host private bytes | Worker working set | Worker private bytes |
| --- | ---: | ---: | ---: | ---: |
| Baseline / p1 | 64,561,152 (61.570312) | 23,638,016 (22.542969) | 58,421,248 (55.714844) | 41,336,832 (39.421875) |
| Baseline / p2 | 77,160,448 (73.585938) | 31,686,656 (30.218750) | 66,650,112 (63.562500) | 51,548,160 (49.160156) |
| Candidate / p1 | 66,187,264 (63.121094) | 24,113,152 (22.996094) | 59,838,464 (57.066406) | 43,741,184 (41.714844) |
| Candidate / p2 | 75,296,768 (71.808594) | 30,171,136 (28.773438) | 66,584,576 (63.500000) | 53,243,904 (50.777344) |
| Candidate / p3 | 98,930,688 (94.347656) | 45,854,720 (43.730469) | 144,207,872 (137.527344) | 131,874,816 (125.765625) |

| Full build/point | Host working set | Host private bytes | Worker working set | Worker private bytes |
| --- | ---: | ---: | ---: | ---: |
| Baseline / p1 | 66,084,864 (63.023438) | 24,154,112 (23.035156) | 57,982,976 (55.296875) | 40,968,192 (39.070312) |
| Baseline / p2 | 73,494,528 (70.089844) | 28,651,520 (27.324219) | 67,022,848 (63.917969) | 51,851,264 (49.449219) |
| Candidate / p1 | 66,416,640 (63.339844) | 24,338,432 (23.210938) | 58,699,776 (55.980469) | 43,438,080 (41.425781) |
| Candidate / p2 | 78,630,912 (74.988281) | 32,968,704 (31.441406) | 67,006,464 (63.902344) | 53,592,064 (51.109375) |
| Candidate / p3 | 102,252,544 (97.515625) | 49,451,008 (47.160156) | 143,896,576 (137.230469) | 130,461,696 (124.417969) |

Candidate-minus-archival-baseline point-2 worker private bytes are **+1,695,744 bytes (+1.6171875 MiB)** in read-write and **+1,740,800 bytes (+1.66015625 MiB)** in full, below the proposed 20,000,000-byte increase. This qualifies only the archival-reference comparison; it does not replace the spec's installed-release requirement. Baseline point 3 is unavailable because it has no `hmi_write`, so matched point-3 differences and threshold verdicts are N/A. Candidate point-3 growth does not identify cache retention, a leak, or any particular cause.

| Fifty-item primary actual writes | Run 1 ms | Run 2 ms | Run 3 ms | Median ms |
| --- | ---: | ---: | ---: | ---: |
| Read-write | 251822.609 | 228955.963 | 168083.240 | 228955.963 |
| Full | 190018.535 | 199475.396 | 166587.311 | 190018.535 |

All six writes have 50/50 successful items and successful verification: 49 superseded items plus five final checks, without omission/truncation. Across all 16 qualifying primary runs, 188 requests, 156 canonical tool responses and 300 actual write items were successful; no request elicited and no response had an omission. Every host exited normally with drained readers. Independent worker exit is not proved. A source-derived estimate for this one-HMI, single-page, same-tag workload is 206 serial HMI domain dispatches: seven initial reads, 147 dependent replanning reads, fifty mutations and two final reads/validation. This is not an instrumented worker-call trace; extra pages add dispatches. Timing does not isolate locator cost.

### Additional genuine-owner transition, n=1

A distinct continuous full-mode trace uses fifty same-value acquisitionCycle updates at the independently observed baseline T100ms value. Its different property workload is **excluded from primary medians**. All fifty items and verification pass in 221952.519 ms. The complete before/after baseline get_tag result is equal. The same sampled host/worker identities and package path persist at all four points.

| Additional trace point | Host working set bytes (MiB) | Host private bytes (MiB) | Worker working set bytes (MiB) | Worker private bytes (MiB) |
| --- | ---: | ---: | ---: | ---: |
| p1 after binding | 65,163,264 (62.144531) | 23,961,600 (22.851562) | 59,920,384 (57.144531) | 43,720,704 (41.695312) |
| p2 after ten reads | 78,970,880 (75.312500) | 33,267,712 (31.726562) | 67,661,824 (64.527344) | 53,452,800 (50.976562) |
| p3 after fifty writes | 97,427,456 (92.914062) | 44,392,448 (42.335938) | 140,849,152 (134.324219) | 127,455,232 (121.550781) |
| p4 after owner change/unkeyed lookup | 99,856,384 (95.230469) | 46,100,480 (43.964844) | 142,172,160 (135.585938) | 128,905,216 (122.933594) |

Standalone A is saved clean and closed, then the exact authorized ALS opens as a clean, different local-session AMC owner. The first unkeyed, non-snapshot runtime-settings lookup returns expected `target_ambiguous`; independent inventory is complete with five Unified and one Classic HMI. Source clears retained entries on the changed engineering root and does not insert a keyed entry for an omitted HMI selector. This is source-backed cache-empty/no-refill evidence, without independent public cache-state telemetry.

Worker private bytes p4/p1 = **128,905,216 / 43,720,704 = 2.948379239273**, or **+194.8379239273%**. The proposed 10% return condition is **not met in this n=1 observation**. The strict three-repetition point-4 gate remains unqualified, as do the unavailable matched baseline write comparisons. No forced GC was recorded; the measurements do not establish a cache-retention cause or leak. All 21 requests, including three full-mode lifecycle calls, have zero elicitation; all nineteen canonical responses match captured text/structured/raw documents with no omissions. Expected scoped alarm-absence and ambiguous-lookup item failures are retained as failures rather than relabeled root successes.

The maintainer explicitly chose **“Retain current cache; leave O1 open”** after the read-write results, with full/p4 still disclosed as pending. The later unfavorable point-4 observation is reported separately and does not alter that choice or manufacture a full performance pass. The locator still revalidates project membership and selector uniqueness through fresh traversal on every call, including warm retained entries. The planned traversal reduction is **not delivered**, and **O1 remains open**.

## Restoration and saved state

Completed controls independently restore renamed table/alarm objects and L11 baseline names, bindings and references. Missing-source create and plain-struct test objects are independently absent after explicit cleanup. L03 dangling test alarms are explicitly removed. Both L04 tables and their exact remaining inventoried tags are absent while all other table entries remain identical.

The first cleanup/transition attempt successfully deleted three original owned alarms and the original four-tag table, then stopped on a controller-supplied invalid `limit` for a nonpaged inventory operation. Those successful cleanup operations were not replayed. A corrected inventory proves six complete baseline tables and a full baseline tag result equal to the earlier L11 baseline. A broad alarm inventory was budget-omitted at 85,951 characters against the 60,000-character value limit; it is retained as incomplete output, not alarm-absence proof or a product source defect. The later continuous owner-transition trace independently proves all three scoped owned alarms absent, complete baseline tag-result preservation, successful clean standalone save, close and authorized local-session opening.

Both read-write and full localSession testing create and read an owned table/tag, save locally, delete and independently prove absence, confirm entire final table inventory equals baseline, and save the restoration cleanly. All four saves return typed result success and verification status `succeeded`; follow-up status is clean with `isModified:false`. Local save does not update/check in to Project Server. The local project remains open and clean; no generic local-session close, server update or check-in was requested. In-memory readback equality alone is not treated as saved-state proof.

## Retained limitations and unexecuted controls

- Direct case-only tag rename is exposed as a typed Siemens setter failure. An earlier-candidate experiment proved two explicit ordinary renames through a caller-owned unique intermediate name and restored the original. There is no hidden temporary-name mechanism; clients must inspect partial state before retrying. Latest table/alarm case-only success does not establish tag case-only success.
- An existing-tag rebind introducing a new member followed by an alarm on that member in the same call is conservatively rejected before mutation. Use binding update, read tag/members, then create the alarm in another call. The final review adjudicated this P2 scope limit; same-call provisional membership remains limited to earlier-created bound tags. Current documentation and a follow-up are required.
- L08 is retained earlier-candidate human evidence. Latest read-only bound content success, global installation of this package, matched baseline write timings/memory, and three-repetition different-owner point 4 are not claimed.
- Host exit 0/drained readers does not independently prove worker exit. Worker parent/path continuity is observed at sampling points, not continuously between them.
- Offline coverage uses doubles/FakeWorker and does not replace live Siemens, editor, hardware or deployment acceptance. Canceled suites and earlier candidate results are not substituted for the exact final gate.
- Fresh discovery remains non-atomic against concurrent editor changes. No stronger concurrent-editor or topology-race acceptance is claimed beyond the executed controls.

## Final disposition

Offline qualification, both-mode functional verification, restoration/save and final audit reconciliation are complete within the stated scope. L08 remains one retained earlier-candidate human check. Cache retention is explicitly accepted by maintainer choice with O1 open; strict matched-baseline performance and three-repetition point-4 gates are not passed, and the observed single-trace 10% return condition failed. This acceptance record qualifies the production revision and immutable package binaries above, with those deviations and unexecuted controls retained.
