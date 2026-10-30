# HMI write native-rejection verification — 2026-10-30

**Scoped live qualification passed.** The installed build-112 standalone matrix, focused
build-113 R1 regression, manual compile checkpoints under accepted R3 scope, L5 cleanup,
20-call candidate timing series and offline qualification have the results below. This
qualifies the recorded R1–R3 scope. Current documentation and evidence are complete; broader
native-fault and workload coverage remains limited as recorded below.

The protocol is [design §6.7](../../specs/2026-10-29-hmi-write-native-rejections-design.md#67-live-probing-protocol)
and [plan Task 6](../../plans/2026-10-29-hmi-write-native-rejections.md#task-6-live-verification-timing-after-captured-fixtures-maintainers-machine-tia-portal-v21).
The [case input](inputs/2026-10-30-hmi-write-native-rejections-live-cases.md) contains the
complete 31-variant matrix and L1–L4 results; its [archive README](inputs/2026-10-30-hmi-write-native-rejections-live-cases/README.md)
describes canonical responses, privacy projections, source hashes and archive validation.

## Candidate boundaries and fixture

| Evidence | Installed version | Source | Scope |
| --- | --- | --- | --- |
| P1–P22, all 31 variants; L1–L4 | `4.2.0-local.112.g96a28b7` | `96a28b7` | 35 complete standalone cases |
| Focused R1 replay | `4.2.0-local.113.g31d1a44` | `31d1a44` | Partial-update failure and conservative supersession |
| Fresh maintainer UI queue | `4.2.0-local.113.g31d1a44` | `31d1a44` | P10, P12-discrete, L1 and L2 clean; L4 expected missing-trigger diagnostics |
| L5 local-session case | `4.2.0-local.113.g31d1a44` | `31d1a44` | Two creates, independent read/Validate, clean maintainer compile and exact inventory restoration |
| Timing-after | `4.2.0-local.113.g31d1a44` | `31d1a44` | 20 successful workload calls; 15 measured; final inventory/hash and independent clean status verified |

Both installed candidates use real TIA Portal V21 through the public MCP surface, in `full`
mode. The direct JSON-RPC client uses protocol `2025-06-18`, a 900-second request deadline,
`--worker-timeout-minutes 15`, and item/document budgets of 1,000,000 characters. The
standalone disposable fixture is `HMI_RT_1`, with **101 tags, nine tables and four alarms**
(two discrete, two analog). Actual project identities and full project paths remain local.

| Installed artifact | Build 112 SHA-256 | Build 113 SHA-256 |
| --- | --- | --- |
| Host | `c01713aa2394e6159e0401e3200ac7d2de51c6295916f655af6700188e191fa8` | `369695059fa9da0b0f149d79c0beae3359d4afa74c28b78af03761f8536749b0` |
| Worker | `50f24a4fa9f84da2c10b0485adbab66e6dbf1b71c80f45de4ed21335415dc736` | `52c8e7de27ef6eb9d93832667baf6033737643610118cf29cf265e36015a797c` |

The saved standalone file's SHA-256 is
`564a644a3116fe60cba9265e40e5a797fd68b57b0c954dff7e3059c84d702a81`.
File-hash checks establish that the saved file was preserved; independent inventory checks
establish restoration of its engineering contents.

## Standalone functional matrix

The 31 P variants reproduce **21 O1 native rejections, three O2 accepted invalid-reference
cases, five accepted controls and two worker refusals**. No O3 was detected in the captured
state and diagnostic comparisons. Accepted controls and worker refusals are kept separate
from O1/O2 rather than labelled as invalid writes.

| Cases | Variants | Result |
| --- | ---: | --- |
| P1, P2 same/cross-group and case-variant table creates | 3 | O1 at `Create`; original tables and contents retained |
| P3 colliding table rename | 1 | O1 at `Name`; original names and membership retained |
| P4, P5 same/cross-table and case-variant tag creates | 3 | O1 at `Create`; original tags retained |
| P6 colliding tag rename; P7 case-only tag rename | 2 | O1 at `Name`; original names/properties retained |
| P8 duplicate discrete alarm and case variant; P9 duplicate analog alarm | 3 | O1 at `Create`; original alarms retained |
| P10 analog alarm sharing a discrete-alarm name | 1 | Accepted control; both scopes present and valid |
| P11 colliding discrete/analog alarm rename | 2 | O1 at `Name`; original alarms retained |
| P12 case-only discrete-alarm, analog-alarm and table renames | 3 | Accepted controls; requested case lands and membership follows |
| P13 delete tag then create its case variant | 1 | Accepted control; replacement present, original absent |
| P14 delete default table | 1 | O1 at `Delete`; table and all 12 children retained |
| P15 bound-tag `dataType`, update/create | 2 | O1 at `DataType`; update retains binding; failed create cleaned up |
| P16 `plcTag` without connection, create/update | 2 | O1 at `PlcTag`; failed create cleaned up; existing internal tag retained |
| P17 create into missing table | 1 | O1 at `Create`; no fallback placement or automatic table |
| P18 create with missing connection | 1 | O1 at subsequent `PlcTag`; failed create cleaned up |
| P19 missing alarm class | 1 | O2; requested class retained and public Validate diagnoses it |
| P20 missing trigger tag/member | 2 | O2; requested reference retained and public Validate diagnoses it |
| P21 missing table group | 1 | Worker `target_not_found` before native Create; no mutation |
| P22 missing comment culture | 1 | Worker `target_not_found` after Create; cleanup reports `deleted` |

Every O1 result is `engineering_rejected`, with an outcome and
`engineeringError.type:"EngineeringTargetInvocationException"`. All 21 terminal type names
and verbatim messages match the [native checkpoint](2026-10-29-hmi-write-checkpoint-probes.md),
including line endings. Attempted failures retain root `success:false`, `phase:"applied"`,
`error:null` and MCP `isError:false`. P4 supplies this captured documentation/fixture example:

```json
"Error when calling method 'Create' of type 'Siemens.Engineering.HmiUnified.HmiTags.HmiTagComposition'.\r\n\r\n-The value must be unique.\r\n"
```

The duplicate create never returned a new object, so no cleanup was needed and the original
was retained. When a later stage of a new-object create fails, cleanup is identity-checked
and best-effort; `deleted` is not batch rollback. `appliedProperties` records completed
stages, and a failing setter can itself have changed state before throwing.

| Final-run case | Captured build-112 result |
| --- | --- |
| L1 | Eleven successful items cover all nine operations. Seven earlier items are `superseded`, four are `trusted`; independent reads agree with final state and surviving objects validate clean. |
| L2 | Five earlier items succeed; the sixth duplicate-tag create is a typed native rejection; six later items are skipped. Verification includes only the five successes: two `superseded`, three `trusted`. Earlier changes remain until restoration. |
| L3 | L2 preview returns effects/guards without attempted outcomes or native rejection. Independent inventory and the modification flag show no mutation. |
| L4 | Referenced-tag and referenced-table deletes succeed. Informational guards retain reference and child counts; both items are `trusted`. Public Validate reports expected dangling alarm trigger references. |

L4's four successful cross-reference queries alone list all four expected alarm trigger
users on this fixture. This supports evaluating removal of the auxiliary alarm inventory
from delete planning; it does not prove complete cross-reference coverage for every alarm
reference form.

Each functional case includes independent reads of every object it could touch, including
pre-existing collision targets, plus public validation of validatable objects. The
[validation supplement](inputs/2026-10-30-hmi-write-native-rejections-validation-supplement.json)
checks the 15 distinct child tags from table-only cases after the matrix on the unchanged
restored baseline: all 15 operations succeed with zero findings. Public Validate has no
table validator; this supplement is not in-case table validation. Expected missing selectors
after deletion, rename or failed creation are absence checks.

## R1 and R2: partial failures and candidate reuse

Independent review found a verification defect: HMI text mapping can fail `target_not_found`
after a rename or partial text assignment. Treating that category alone as proof of no
mutation incorrectly left an earlier write `trusted`. **R1** changes the shared verifier to
consider a non-null result as evidence of possible mutation. Such a later failure can mark
an earlier affected write `superseded`; an ordinary precondition refusal without an outcome
retains its previous behavior. This adds no Portal rule or mutation read-back.

Two focused regressions first fail with `trusted` instead of `superseded`, then pass after
the fix. The [focused build-113 live capture](inputs/2026-10-30-hmi-write-native-rejections-r1-live.json)
shows the first update succeeding, the second applying `Name` before `Comment` fails on a
missing culture, and the third item skipped. The sole verification entry marks the earlier
write `superseded` and warns that the failed later item may have changed the object and its
final state is unverified. Independent reads find the renamed tag, partial `en-GB` comment,
and alarm reference following the rename. The renamed tag and alarm validate clean; the
expected missing old selector makes the aggregate validation root false.

The first build-113 connection attempt times out on read-only status while an Openness
access prompt awaits approval. The maintainer approves it, and a fresh client completes the
focused check. No write was dispatched by the timed-out attempt.

**R2** keeps the complete matrix attributed to build 112 and qualifies R1 separately on
build 113. Comparison of all 35 captured documents and 72 earlier-success/later-attempted
pairs finds zero old/new supersession-admission differences for those batches. R1 changes
no worker logic, native call, object key or schema. This is scoped reuse of captured
evidence, not a full build-113 matrix rerun or proof about arbitrary workloads.

## Maintainer UI compiles and R3 scope decision

The maintainer confirms English UI during P10. Exact error/warning counts were not supplied
for the clean reports below; they remain unknown rather than being filled with zeroes.

| Fresh build-113 checkpoint | Maintainer report | Status |
| --- | --- | --- |
| P10 | “Both present, compilation, clean, english language is used” | Clean; both alarm scopes confirmed |
| P12-discrete | “Lowercase confirmed; clean compile” | Requested case confirmed; clean |
| L1 | “Clean” | Clean |
| L2 | “Compilation clean” | Clean |
| L4 | “Compilation showed 2 proper errors” | Two expected missing-trigger errors, zero warnings; English |
| L5 | “Compilation clean, table and its tag exist” | Clean; created objects confirmed |

L4's English diagnostics identify `CP1029_DiscreteA` and `CP1029_DiscreteB`, respectively:

> Trigger tag: The object "CP1029_TagA" does not exist. Please update or delete the reference.

> Trigger tag: The object "CP1029_TagB" does not exist. Please update or delete the reference.

These are the expected consequences of deleting the referenced tags. They confirm the
compiler detects the dangling references; they are not unexpected failures or O3 findings.

L5 starts on a local-session root with seven tables, 72 tags and 78 alarms, and
`isModified:false`. Its two create items add a uniquely owned internal Int tag and table.
Independent read and public Validate succeed before the maintainer confirms the clean
compile and the presence of both objects. No per-case language or numeric compile counts
were supplied for L5.

**R3:** the maintainer accepts “Reduce to those four (Recommended)”, reducing the remaining
manual checks to L1, L2, L4 and L5. Fresh build-113 compiles are waived for P12-analog,
P12-table, P13, P19, P20-missing-tag and P20-missing-member. Their earlier checkpoint UI
observations are reused together with the fresh build-112 canonical writes, independent
reads and public Validate evidence. These six rows are **not fresh candidate compiles**.
The prior checkpoint reports clean compiles for its accepted controls and compiler
diagnostics for its three accepted invalid-reference variants.

The queued P12-analog build-113 case was cancelled before a maintainer compile and restored
without saving before the reduced queue began. Its cancellation is not a successful compile.
The [completed/reduced UI queue archive](inputs/2026-10-30-hmi-write-native-rejections-ui-compiles/README.md)
and [L5 local-session input](inputs/2026-10-30-hmi-write-native-rejections-local-session/README.md)
preserve the maintainer reports, canonical HMI evidence and labelled restoration/status
projections. All manual checkpoints required under R3 are complete.

## Timing comparison

The installed build-113 replay completes **20 successful workload calls: five discarded
warm-ups and 15 measured calls**, with no timeout or replacement sample. The client makes
107 calls including untimed preparation/restoration, followed by a separate independent
status call. The archive's 22 complete HMI reads and 66 minimal lifecycle/status projections
include that independent status and retain fixture/restoration evidence. Timing client and
driver exit 0. The actual archive's offline self-check and validator pass.

The [frozen baseline](inputs/2026-10-29-hmi-write-timing-before.md) measures installed
`4.2.0-local.79.g75c0568`, **not analysis commit `0b2640a`**; source equivalence is not
claimed. The build-113 replay retains exact workloads, settings and reset/read
sequence. Its client changes only the expected installed version and write-contract
assertion from `1.0` to `2.0`.

| Workload | Baseline root / item sum / remainder, ms | Candidate root / item sum / remainder, ms | Root change |
| --- | ---: | ---: | ---: |
| H1: create internal Int tag | 119,492 / 12,775 / 106,717 | 12,117 / 6,365 / 5,385 | -89.9% |
| H2: ten mixed items | 574,416 / 363,268 / 170,527 | 128,725 / 69,850 / 34,924 | -77.6% |
| H3: delete referenced table | 160,490 / 16,995 / 143,325 | 33,934 / 6,843 / 28,036 | -78.9% |
| H4: H2 preview | 95,049 / 0 / 95,049 | 34,542 / 0 / 34,542 | -63.7% |
| H5: rename discrete alarm | 142,877 / 16,124 / 126,753 | 17,824 / 5,862 / 11,962 | -87.5% |

The protocol uses discarded round 0 followed by three measured rounds, H1→H5 in each,
with close-without-save/reopen and an untimed full HMI read before every workload. Root,
item-sum and remainder medians are computed independently from audit v2; median remainder
need not equal median root minus median item sum. Baseline item timers include dependent
replanning. The series excludes builds, tests and other live clients.
The columns were independently recomputed from both archives' individual rows. Observed
median root durations are shorter for all five workloads, but three samples per workload
and uncontrolled OS/Portal cache conditions do not support a general performance guarantee
or isolate individual native-call costs. Functional trace elapsed times are excluded.
The [timing input](inputs/2026-10-30-hmi-write-timing-after.md) records exact client changes,
hashes, all before/after metrics, settings, restoration and limitations; its
[evidence README](inputs/2026-10-30-hmi-write-timing-after/README.md) links the validated archive.

## Offline qualification and restoration

The two required captured-message tests,
`LiveP4_DuplicateTagCreate_IsEngineeringRejected` and
`LiveL2_MiddleRejection_EarlierAppliedRejectedTypedRestSkipped`, pass with the focused HMI
MCP suite (**33 tests**). Existing FakeWorker messages already match captured messages
byte-for-byte, so their values need no replacement.

The full default Release suite passes **7,644 tests, zero failures and zero skips**. Scoped
coverage is **95.05% (12,311 / 12,952)**, above the 80% threshold. The serial stub build
passes with zero errors and seven existing warnings; the real-assembly package build and
package verifier pass for build 113. The separate `Category=Packaging` suite was not run;
no packaging or product project-file changes required it. Independent reviews of R1 and
the captured fixtures found no further issues.

All 35 standalone functional cases have matched write/audit evidence and either verified
unchanged state or close-without-save/reopen restoration. State-changing restorations
preserve the saved-file hash and restore exact baseline inventory; the focused R1 replay
also finishes open and unmodified with matching inventory/hash. No standalone save was
performed. The reduced standalone UI queue also finishes after L4 with equal inventory,
unchanged saved-file hash and client exit code 0.

L5's two cleanup items succeed. The raw after-inventory exactly equals both the raw
baseline inventory and the case record's baseline: seven tables, 72 tags and 78 alarms.
The saved standalone file's hash remains unchanged. The case record has
`inventoryEqual:true` and `completionChecksPassed:true`; its last local-session status is
open with `isModified:true`, compared with `false` before the case. The helper leaves the
local session open and unsaved after removing only its owned objects. Net inventory
restoration does not clear Portal's modification flag.

The L5 helper's process session was lost when the turn was interrupted, so its exit code
is unknown. Captured responses establish successful cleanup and restoration. A later
process inspection finds the helper, host and worker gone. The maintainer subsequently
reports manual cleanup and standalone opening; those actions follow the helper's captured
local-session state.

Timing ends after one final close-without-save/reopen with exact full HMI inventory restored,
the same saved-file hash and an independent status showing the configured project open and
unmodified. Installed host and worker hashes are unchanged before/after collection. No
standalone save is performed during the timing series.

## Retained limits

This is bounded evidence for the captured fixture and native call paths. It does not
establish global absence of O3, arbitrary race behavior, every native fault site, or cleanup
under induced transport/cleanup failure. Those faults were not induced. Connection-only
assignment remains an unprobed native path; the broader member-rebinding path is not
live-qualified here. The HMI read diagnostic-evidence gap and completeness of delete
cross-reference coverage remain follow-ups. The full matrix remains attributed to build
112; focused R1, the accepted compile scope, L5 and timing qualify their recorded build-113
paths. Broader native faults, races and workload coverage remain untested.
