# Network write timing after native-rejections refactor

Date: 2026-10-29. **PASS: 20 timing captures validated, comprising five discarded warm-ups and 15 measured calls. Independent final review passed.**

The [full functional rerun](2026-10-29-network-write-native-rejections-live-rerun.md) passed all 17 expected outcomes before this timing series started. This report compares the installed candidate with the [Task 0 baseline](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-27-network-write-timing-before.md). Negative percentages below mean a shorter measured duration.

## Median root durations

Each scenario has three measured samples. Warm-ups, preparation, lifecycle calls and separate hardware reads are excluded from the duration samples.

| Scenario | Before, seconds | After, seconds | Change |
| --- | ---: | ---: | ---: |
| N1: Single IP-address change | 113.088 | 72.668 | -35.7% |
| N2: Ten mixed write operations | 627.273 | 81.174 | -87.1% |
| N3: Connected subnet deletion | 115.959 | 75.940 | -34.5% |
| N4: Dry-run preview of N2 | 57.230 | 70.591 | +23.3% |
| N5: Device creation | 113.739 | 74.545 | -34.5% |

The largest observed reduction is N2: 627.273 s to 81.174 s, or 87.1%. Preview N4 increased from 57.230 s to 70.591 s, or 23.3%. The results do not establish that every path is faster.

## Item and remainder medians

All values are milliseconds. For each captured call, item sum is the sum of audit item `durationMs` values, treating null as zero; remainder is root minus that sum. Each column's median is computed independently over runs 1–3. Subtracting the median item sum from the median root is not the median remainder.

| Case | Before item sum | After item sum | Before remainder | After remainder |
| --- | ---: | ---: | ---: | ---: |
| N1 | 968 | 681 | 112120 | 71987 |
| N2 | 516709 | 11385 | 114207 | 69790 |
| N3 | 6945 | 1319 | 109932 | 74267 |
| N4 | 0 | 0 | 57230 | 70591 |
| N5 | 4383 | 5638 | 109356 | 68907 |

Item duration includes the work performed inside each executor; baseline item time can include replanning and reads later removed by the refactor. Remainder includes work outside the item executors and is not a direct measurement of planning alone. N5 item sum increased even though its root duration fell. No causal conclusion about native device creation follows from this small series.

## Protocol and provenance

- Installed package: `4.2.0-local.79.g75c0568`; production source `75c0568d4d83b9430a13feb62aea52e2232c2bad`. Later checkout changes during this run were documentation only.
- Host SHA256: `a8be3d6e4f2fc7fec4b280ccf9be8e21539ddae3b31f17afc19575c796dc67a9`; worker SHA256: `6eb1db5801f61e3eb83cdf80b217efaf3a12fdeb042dfd6146036ecf01234d59`. Both matched the local Release publish before the functional gate.
- Same disposable project, `full` mode, and item/document limits of 1,000,000 characters. Project path stays in local provenance.
- N1–N5 order for round 0, then measured rounds 1–3. Round 0 is discarded. The candidate client ran continuously without a restart.
- Close without saving and reopen before every scenario, including N4. N2/N4 use an untimed freshly created `ZZ_N0_Target`; only that native subnet ID is substituted into the original request. The separately created `ZZ_N0_Created` remains a measured N2 operation.
- One complete hardware-read warm-up immediately precedes each actual timed write. N4 has no additional warm-up read, matching the baseline preview protocol.
- Requests match the original baseline requests after normalizing only fresh setup IDs. Actual calls retain `dryRun:false`; N4 retains `dryRun:true`.
- The timing source is audit v2 root/item durations, not client wall-clock time. All 28 network audit records (20 samples plus eight untimed setups) matched exact canonical response text/hash and were fresh; maximum response-file/completion difference was 3.250 ms.
- The offline replay self-check validated the baseline captures and rejected stale, hash-mismatched and wrong-count examples. No build, test suite, source edit or agent analysis ran concurrently with measured writes.

The baseline contains 25 captures across two machine sessions: 15 measured calls and ten discarded initial/restart warm-ups. The uninterrupted candidate needs 20 captures: 15 measured and five discarded. Both comparisons use exactly three measured calls per scenario. Portal, OS cache and machine-session state were not experimentally controlled across the two collection windows; these are observed medians for this fixture, not general performance guarantees.

The candidate deliberately uses the initial planning snapshot for delete effects. Thus N2/N4's untimed target starts unconnected: a node connected earlier in the same call is not added to the initial deletion guard. The baseline's replanning could report that connection. This is the documented snapshot limit; input operations and targets remain the same.

## Exact samples and delivery hashes

Hashes cover exact UTF-8 canonical response text. Full MCP wrappers, requests, audit records, measurement logs and the unchanged replay helper are retained locally under ignored `artifacts/network-native/task7-20261029-timing-after/`. `comparison.json` contains the raw-derived baseline medians, candidate summary and percentage calculations.

| Capture | Inclusion | Root ms | Item sum ms | Remainder ms | Canonical SHA256 |
| --- | --- | ---: | ---: | ---: | --- |
| N1-run0 | warm-up; discarded | 66533 | 664 | 65869 | `sha256:1f5423658208f2ecc908f67b4efba4cbfba7d37a76a8206c7d94c55f14aa8675` |
| N2-run0 | warm-up; discarded | 89439 | 13479 | 75960 | `sha256:474f6df1df4d0f8b0a0657367f78ce767653b2759f41f8848a2f0a018a603c42` |
| N3-run0 | warm-up; discarded | 81011 | 1335 | 79676 | `sha256:e1ad5f2329c9a0d84e3cf3503f3529c20d9159c92a0d00e30834d847349e1f3a` |
| N4-run0 | warm-up; discarded | 77540 | 0 | 77540 | `sha256:c72924279b2d2a80151c1de9bce2d3e40d257293d9ef904f1aa0e3d82979ed35` |
| N5-run0 | warm-up; discarded | 78073 | 6444 | 71629 | `sha256:14509438a38789b4b3f633f2ce2e5438ef80b158485506ddb2fc87f583597f9c` |
| N1-run1 | measured | 78465 | 657 | 77808 | `sha256:1f5423658208f2ecc908f67b4efba4cbfba7d37a76a8206c7d94c55f14aa8675` |
| N2-run1 | measured | 80162 | 11689 | 68473 | `sha256:474f6df1df4d0f8b0a0657367f78ce767653b2759f41f8848a2f0a018a603c42` |
| N3-run1 | measured | 74780 | 1271 | 73509 | `sha256:e1ad5f2329c9a0d84e3cf3503f3529c20d9159c92a0d00e30834d847349e1f3a` |
| N4-run1 | measured | 71629 | 0 | 71629 | `sha256:c72924279b2d2a80151c1de9bce2d3e40d257293d9ef904f1aa0e3d82979ed35` |
| N5-run1 | measured | 74393 | 5493 | 68900 | `sha256:14509438a38789b4b3f633f2ce2e5438ef80b158485506ddb2fc87f583597f9c` |
| N1-run2 | measured | 72668 | 681 | 71987 | `sha256:1f5423658208f2ecc908f67b4efba4cbfba7d37a76a8206c7d94c55f14aa8675` |
| N2-run2 | measured | 81174 | 11384 | 69790 | `sha256:474f6df1df4d0f8b0a0657367f78ce767653b2759f41f8848a2f0a018a603c42` |
| N3-run2 | measured | 75940 | 1673 | 74267 | `sha256:e1ad5f2329c9a0d84e3cf3503f3529c20d9159c92a0d00e30834d847349e1f3a` |
| N4-run2 | measured | 70591 | 0 | 70591 | `sha256:c72924279b2d2a80151c1de9bce2d3e40d257293d9ef904f1aa0e3d82979ed35` |
| N5-run2 | measured | 76075 | 5825 | 70250 | `sha256:14509438a38789b4b3f633f2ce2e5438ef80b158485506ddb2fc87f583597f9c` |
| N1-run3 | measured | 72244 | 797 | 71447 | `sha256:1f5423658208f2ecc908f67b4efba4cbfba7d37a76a8206c7d94c55f14aa8675` |
| N2-run3 | measured | 83786 | 11385 | 72401 | `sha256:474f6df1df4d0f8b0a0657367f78ce767653b2759f41f8848a2f0a018a603c42` |
| N3-run3 | measured | 76153 | 1319 | 74834 | `sha256:e1ad5f2329c9a0d84e3cf3503f3529c20d9159c92a0d00e30834d847349e1f3a` |
| N4-run3 | measured | 69597 | 0 | 69597 | `sha256:c72924279b2d2a80151c1de9bce2d3e40d257293d9ef904f1aa0e3d82979ed35` |
| N5-run3 | measured | 74545 | 5638 | 68907 | `sha256:14509438a38789b4b3f633f2ce2e5438ef80b158485506ddb2fc87f583597f9c` |

## Restoration and scope

The final full hardware result exactly matches the original: 95 devices, 14 root devices, 108 nodes and four subnets; canonical text SHA256 `9bf25234758521461a1da1db8774d209c2f04f215d1e90d6de43dd3ff8a9acb4` over 964,930 characters. All P/A/B/H address, mask, PROFINET-name and automatic-naming values equal the original functional preflight. The timing-specific X2 name/automatic-naming check also matches the original.

Final status and automatic `client-final-restore-025137-status.json` both report the exact project open with `isModified:false`. The timing client exited 0; post-exit AP21 SHA256 is `564a644a3116fe60cba9265e40e5a797fd68b57b0c954dff7e3059c84d702a81`. The matching-client process check and independent final review are recorded in `restoration-verified.json`.

Independent final review confirmed all 116 canonical text/structured pairs, 28 audit records, request equivalence, independently computed medians and final restoration. The functional and timing work requested here is complete. Task 7's captured FakeWorker fixtures and remaining offline closeout, and Task 8's broader documentation migration, remain pending. No PLC download, online execution or new hardware-compatibility claim is included.
