# hmi_write timing after the native-rejection refactor

**Complete: five discarded workload warm-ups and fifteen measured calls. All 20 workload
calls succeed; final inventory is restored exactly, and independent status is open and
unmodified.** This input compares installed candidate `4.2.0-local.113.g31d1a44` with the
[installed-version baseline](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-29-hmi-write-timing-before.md). Functional qualification,
maintainer UI compiles and the R1–R3 scope decisions are recorded in the
[acceptance report](../2026-10-30-hmi-write-native-rejections-live-verification.md).

## Median comparison

Milliseconds. Each root, item-sum and remainder median is calculated independently over
measured rounds 1–3; round 0 is excluded. Negative percentages indicate a shorter observed
root duration.

| Case | Root before | Root after | Root change | Item sum before | Item sum after | Remainder before | Remainder after |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| H1 | 119,492 | 12,117 | -89.9% | 12,775 | 6,365 | 106,717 | 5,385 |
| H2 | 574,416 | 128,725 | -77.6% | 363,268 | 69,850 | 170,527 | 34,924 |
| H3 | 160,490 | 33,934 | -78.9% | 16,995 | 6,843 | 143,325 | 28,036 |
| H4 | 95,049 | 34,542 | -63.7% | 0 | 0 | 95,049 | 34,542 |
| H5 | 142,877 | 17,824 | -87.5% | 16,124 | 5,862 | 126,753 | 11,962 |

The metric source is audit v2: root `durationMs`; sum of `items[].durationMs`, treating null
as zero; and root minus item sum **within each sample**. Independent medians need not add
up. For H2 after, median remainder is 34,924 ms, rather than median root minus median item
sum (58,875 ms). A separate recomputation from both archives' individual rows agrees with
every before/after median above. H4 is a preview with null item durations.

The observed median root durations are 63.7%–89.9% shorter in this series. There are only
three measured observations per workload on each installed version. OS and Portal cache
conditions were controlled only by the specified warm-up/reset sequence, and the baseline
and candidate were measured in separate series. These results do not establish statistical
significance, predict every workload, or isolate the cost of a particular native call.
Baseline item timers include dependent-item replanning, so the remainder is not all
planning time. All individual rows are retained in [summary.json](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-30-hmi-write-timing-after/summary.json).

## Installed artifacts and fixture

| Item | Recorded value |
| --- | --- |
| Candidate | `4.2.0-local.113.g31d1a44`, production source `31d1a44` |
| Comparison baseline | `4.2.0-local.79.g75c0568`, source `75c0568` |
| Candidate contracts | `hmi_write` 2.0; `hmi_read` 1.1 |
| Client surface | Installed public MCP over JSON-RPC stdio, protocol `2025-06-18` |
| Access mode | `full` |
| Item / document budgets | 1,000,000 / 1,000,000 characters |
| Worker timeout / client deadline | 15 minutes / 900 seconds per request |
| Standalone HMI fixture | `HMI_RT_1`: 101 tags, nine tables, four alarms (two discrete, two analog) |
| Candidate host SHA-256 | `369695059fa9da0b0f149d79c0beae3359d4afa74c28b78af03761f8536749b0` |
| Candidate worker SHA-256 | `52c8e7de27ef6eb9d93832667baf6033737643610118cf29cf265e36015a797c` |
| Saved standalone file SHA-256 | `564a644a3116fe60cba9265e40e5a797fd68b57b0c954dff7e3059c84d702a81` |
| Timing client calls | 107, including untimed preparation/restoration and 20 workload calls; one independent final-status call follows |
| Request timeouts | 0 |
| Timing client / driver exit | Both 0 |

**Baseline deviation:** the analysis/design names `0b2640a`, but the before measurements
were collected on installed `75c0568`. Those sources differ; no source-equivalence claim
or measurement of exact `0b2640a` is made. The comparison above retains the actual installed
baseline and its frozen payloads/settings.

The timing driver checks installed host/worker hashes and the saved standalone file hash
before and after collection; all are unchanged. Project names and full paths are omitted
from published evidence. The saved-file hash identifies the file; independent complete
HMI reads establish engineering-content restoration.

## Exact replay inputs

The candidate [workloads.json](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-30-hmi-write-timing-after/workloads.json) is byte-for-byte
identical to the baseline, including H2's JSON-number `conditionValue:30`. The candidate
[client.py](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-30-hmi-write-timing-after/client.py) has exactly these two line changes:

```diff
-EXPECTED_VERSION = '4.2.0-local.79.g75c0568'
+EXPECTED_VERSION = '4.2.0-local.113.g31d1a44'
-    assert document['contractVersion'] == '1.0'
+    assert document['contractVersion'] == '2.0'
```

| Source bytes | SHA-256 |
| --- | --- |
| Baseline `client.py` | `8d1579a7624e02e4fe8d94c020869a6bd0de2ee18634cc4404aeb02602beacc4` |
| Candidate `client.py` | `1412f1d29ae4d5035cbcd0dfc966a75860e825874db4af1c1cea528ac45bec2e` |
| Both `workloads.json` files | `b9efb11e4b5db42873b40a0bd8fbc4d93238aff4a713e4cb66acf52552261fbe` |

| Case | Workload |
| --- | --- |
| H1 | Create one internal Int tag in an existing table |
| H2 | Ten items: create a table and three tags; rename the first tag; create discrete and analog alarms; update discrete-alarm priority; delete the third tag and the created table |
| H3 | Delete a table containing a tag referenced by an existing discrete alarm |
| H4 | H2's exact operation array with `dryRun:true` |
| H5 | Rename an existing discrete alarm |

H2's alarms use stable tags outside the temporary table. The direct client preserves the
numeric analog threshold; the connected bridge's string-shaped field is not used.

## Collection and verification

The driver first checks a clean standalone owner, saved-file hash and complete 101/9/4
fixture through the installed tool. The measurement client/server then runs continuously:
initial binding and a discarded untimed full read, followed by H1→H5 in discarded round 0,
then H1→H5 in each measured round 1, 2 and 3. No builds, tests or other live clients run
alongside this sequence.

Before every workload, including preview H4, the client closes without saving, reopens the
same file, checks its hash and clean exact identity, and performs an untimed complete
`hmi_read` of tables, tags and alarms. Lifecycle calls, startup and warm-up reads are
excluded from timing. One final close/reopen, complete HMI inventory and clean status
follow the last measured call. After the measurement client exits, a separate client
confirms the same configured project open and unmodified.

The completed evidence contains:

- 20 exact requests/canonical write responses with matching audit projections: five
  discarded workload warm-ups and 15 measured calls, with no timeout, retry or replacement
  sample.
- 22 complete canonical HMI reads: one discarded startup read, 20 per-workload reads and
  the final inventory. The inventories match exactly and retain the qualified counts.
- 66 labelled, metadata-minimal lifecycle/status projections, including the independent
  final status; these projections are derived evidence with original canonical hashes.
- Successful saved-file/artifact hash checks, final exact inventory restoration, clean
  open status and timing-client/driver exit code 0.

The [archive README](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-30-hmi-write-timing-after/README.md) links the evidence format;
[self_test.py](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-30-hmi-write-timing-after/self_test.py) and
[validate_archive.py](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-30-hmi-write-timing-after/validate_archive.py) pass offline on the
actual archive. Checks cover schedule, canonical/audit pairing, complete reads, projections,
independent medians and byte/decompressed hashes. The
[manifest](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-30-hmi-write-timing-after/manifest.json) records input/output hashes and
excludes itself; `.gitattributes` preserves exact bytes.

Canonical response text and audit response hashes are unchanged. Audit paths are redacted;
original collector-line hashes retain provenance and are distinct from original server
audit-file-line hashes. Capture response timestamps support the original freshness checks;
extracted archive file times are not timing evidence. These timing results supplement the
separate functional and maintainer compile evidence, and do not broaden its native-fault
or race coverage.
