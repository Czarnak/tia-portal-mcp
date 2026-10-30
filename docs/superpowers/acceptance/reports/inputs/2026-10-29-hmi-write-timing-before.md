# hmi_write timing before the native-rejection refactor

Completed the timing protocol in [design §6.6](../../../specs/2026-10-29-hmi-write-native-rejections-design.md#66-timing-protocol) on the installed tool, following the [native-rejection checkpoint](../2026-10-29-hmi-write-checkpoint-probes.md).

**Complete: five discarded workload warm-ups and fifteen measured calls. All succeeded. The final project is open and unmodified, with the full HMI inventory restored exactly. No refactor implementation ran.**

## Build and fixture

| Item | Value |
| --- | --- |
| Installed tool | `4.2.0-local.79.g75c0568`, commit `75c0568d4d83b9430a13feb62aea52e2232c2bad` |
| Surface | Installed public MCP `hmi_write`, write contract `1.0`; `hmi_read` contract `1.1` |
| Client | Direct JSON-RPC stdio client, protocol `2025-06-18`; real TIA Portal V21 |
| Access mode | `full`, checked in every timing audit record |
| Response budgets | `--max-item-chars 1000000 --max-document-chars 1000000` |
| Timeouts | `--worker-timeout-minutes 15`; client deadline 900 seconds per JSON-RPC request |
| Project | One already-open, clean `<disposable-copy>`; no save operations |
| Target HMI inventory | 101 tags, 9 tag tables, 4 alarms: 2 discrete and 2 analog |
| Collection window | 2026-10-29 20:10:26–21:30:30 UTC, or 21:10:26–22:30:30 Europe/Warsaw |
| Installed host SHA-256 | `a8be3d6e4f2fc7fec4b280ccf9be8e21539ddae3b31f17afc19575c796dc67a9` |
| Installed worker SHA-256 | `6eb1db5801f61e3eb83cdf80b217efaf3a12fdeb042dfd6146036ecf01234d59` |
| Saved AP21 file SHA-256 | `564a644a3116fe60cba9265e40e5a797fd68b57b0c954dff7e3059c84d702a81` |

The installed binaries and saved AP21 file had identical hashes before and after collection. The AP21 hash identifies that file; the independent full HMI reads establish restoration of the engineering fixture.

**Baseline deviation:** design §3 names `0b2640a`. These measurements use the currently installed `75c0568`, as requested for this session. Those commits contain different HMI and transport code; this is not a source-equivalent measurement of `0b2640a`. The checkout was not built or installed for these runs. A candidate comparison must identify this installed-version baseline and replay its exact payloads and settings.

## Protocol and workloads

The qualified series used one continuously running timing client/server. Initialization, binding and `startup-discarded-read` are untimed. Cases ran H1 → H2 → H3 → H4 → H5 for discarded round 0, then the same order for measured rounds 1, 2 and 3.

Before every workload, including each preview, close with `saveBeforeClose:false`, reopen the same disposable copy, verify the saved AP21 hash and clean exact project identity, then perform an untimed complete `hmi_read` of tables, tags and alarms. Lifecycle operations and these reads are excluded from timing. One final close/reopen and complete inventory/status capture follow the last measured write. No request timed out, and no timing case required retry or replay.

Exact requests are in [workloads.json](2026-10-29-hmi-write-timing-before/workloads.json). H4 has exactly H2's operation array plus `dryRun:true`.

| Case | Workload |
| --- | --- |
| H1 | Create internal Int tag `HM1029_TimingSingle` in `CP1029_TableA` |
| H2 | Ten items: create `HM1029_TimingTable`; create three internal Int tags; rename the first tag; create discrete and analog alarms; update the discrete alarm's priority; delete the third tag; delete the created table |
| H3 | Delete `CP1029_TableB`, containing one tag referenced by the existing discrete alarm `CP1029_DiscreteB` |
| H4 | H2 with `dryRun:true` |
| H5 | Rename discrete alarm `CP1029_DiscreteB` to `HM1029_TimingRenamedAlarm` |

H2's discrete alarm uses the pre-existing `CP1029_TagA`, unused bit 2, class `Alarm`. Its analog alarm uses pre-existing internal Int tag `CP1029_Reuse`, `UpperLimit`, numeric threshold 30, class `Warning`. Both tags are outside the temporary table, so deleting that table leaves these alarms valid. H3 separately measures the referenced-tag consequence path.

The direct MCP client sends `conditionValue` as a JSON number. The connected bridge advertises a string for that field, which the installed server rejects before mutation; the bridge route was therefore not used for timing.

## Median results

Milliseconds; each column is computed independently over measured runs 1–3. Warm-ups are excluded.

| Case | Root duration | Item sum | Remainder |
| --- | ---: | ---: | ---: |
| H1 | 119,492 | 12,775 | 106,717 |
| H2 | 574,416 | 363,268 | 170,527 |
| H3 | 160,490 | 16,995 | 143,325 |
| H4 | 95,049 | 0 | 95,049 |
| H5 | 142,877 | 16,124 | 126,753 |

The metric source is audit v2: root `durationMs`, sum of `items[].durationMs` with null counted as zero, and root minus item sum **within each individual sample**. Independent column medians need not add up: H2's median remainder is 170,527 ms, not median root minus median item sum.

Item timers include dependent-item replanning in this installed baseline: `WriteRun` starts the item timer before `RunItemAsync`, which calls `ReplanAsync` (installed commit `75c0568`, `WriteRun.cs` lines 357 and 419). The remainder therefore includes initial planning, verification and other overhead, rather than all planning time. Preview item durations are null.

## Individual samples

Each cell is root / item sum / remainder, in milliseconds.

| Case | Warm-up, discarded | Run 1 | Run 2 | Run 3 |
| --- | --- | --- | --- | --- |
| H1 | 57,497 / 6,407 / 51,090 | 57,277 / 6,307 / 50,970 | 119,492 / 12,775 / 106,717 | 134,147 / 15,307 / 118,840 |
| H2 | 360,872 / 227,515 / 133,357 | 385,779 / 259,514 / 126,265 | 574,416 / 403,889 / 170,527 | 586,490 / 363,268 / 223,222 |
| H3 | 84,320 / 12,080 / 72,240 | 165,367 / 16,995 / 148,372 | 108,076 / 9,449 / 98,627 | 160,490 / 17,165 / 143,325 |
| H4 | 39,107 / 0 / 39,107 | 67,873 / 0 / 67,873 | 95,049 / 0 / 95,049 | 98,926 / 0 / 98,926 |
| H5 | 67,864 / 6,592 / 61,272 | 144,657 / 16,399 / 128,258 | 142,877 / 16,124 / 126,753 | 89,771 / 11,365 / 78,406 |

There is substantial run-to-run variation despite identical fixture inventories. No measured samples were discarded or replaced. These are three observations on this machine and installed build; operating-system and Portal cache conditions were not controlled beyond the specified warm-up/reset procedure.

## Verification and retained evidence

Independent review and a separate main-agent calculation agreed on all five median triples. The evidence audit verified:

- All 20 timing requests match the frozen workloads, including numeric analog thresholds.
- Every timing response has equal canonical text/structured content and a fresh, exact matching audit v2 record in `full` mode; response SHA-256 matches. Maximum response-file/audit-completion timestamp difference was approximately 16 ms.
- All actual timing items succeeded with successful verification; previews have null batch/verification and null item durations. There are no errors or omissions in the qualified series.
- All 20 per-workload warm-up inventories and the final inventory exactly equal the startup full structured inventory, including all captured properties and complete pages.
- All 21 close/reopen/status sequences succeeded; every close used `saveBeforeClose:false`. Final status is open and unmodified.
- Guards were informational only: `hmi_deletes_table_contents` and `hmi_deletes_referenced_tag`.
- The timing client exited 0; its host and child processes were absent afterwards. TIA Portal remained running.

The existing desktop MCP binding became stale after the separate timing client's close/reopen cycles. An ordinary binding/status refresh returned `binding_conflict`; explicit `forceRebind:true` to the same already-open project succeeded, followed by an independent successful public status showing open and unmodified. This untimed binding recovery did not alter any timing sample.

[Evidence and replay README](2026-10-29-hmi-write-timing-before/README.md) links the frozen client, offline self-check, workloads, numerical summary, privacy-redacted evidence archive and checksums. Canonical timing responses and their hashes are preserved; lifecycle/status projections are explicitly labelled. Raw local captures remain under ignored `artifacts/hmi-native/timing-before-20261029/`.

No product code was changed. No product build, product test suite, packaging test, HMI UI compile or candidate timing comparison was run for this timing-only task. Harness self-checks and evidence/link/hash validation cover the new collection artifacts.
