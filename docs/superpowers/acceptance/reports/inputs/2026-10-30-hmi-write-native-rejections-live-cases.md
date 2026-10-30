# HMI native-rejection candidate: captured live cases, 2026-10-30

This Task 6 input records the installed candidate's **31 P variants and L1–L4** on
`<disposable-copy>`. The P variants reproduce **21 O1 native rejections, three O2 accepted
invalid references, five accepted controls and two worker refusals**. No O3 was detected
in the captured state and diagnostic comparisons. Task 6 remains open: this is not the
completed acceptance report.

The protocol is [design §6.7](../../../specs/2026-10-29-hmi-write-native-rejections-design.md#67-live-probing-protocol)
and [plan Task 6](../../../plans/2026-10-29-hmi-write-native-rejections.md#task-6-live-verification-timing-after-captured-fixtures-maintainers-machine-tia-portal-v21).
Compare the earlier [native checkpoint](../2026-10-29-hmi-write-checkpoint-probes.md).
The [evidence bundle README](2026-10-30-hmi-write-native-rejections-live-cases/README.md)
describes preserved canonical documents, projections, source hashes and offline checks.

## Candidate and fixture

| Item | Recorded value |
| --- | --- |
| Installed version for all 35 cases | `4.2.0-local.112.g96a28b7` |
| Source | `96a28b7` |
| Initial host SHA-256 | `c01713aa2394e6159e0401e3200ac7d2de51c6295916f655af6700188e191fa8` |
| Initial worker SHA-256 | `50f24a4fa9f84da2c10b0485adbab66e6dbf1b71c80f45de4ed21335415dc736` |
| Access mode | `full` |
| Item / document budgets | 1,000,000 / 1,000,000 characters |
| Worker timeout / JSON-RPC deadline | 15 minutes / 900 seconds |
| Saved baseline | 101 tags, 9 tables, 4 alarms: 2 discrete and 2 analog |
| Language evidence | Native messages are English; persisted setting is `en-US`; direct UI-language observation pending |

The controller supplied the installed identity; the exporter does not inspect binaries.
The bundle contains 35 case records and 238 trace captures. All 35 writes have one matched
canonical trace response and a matched audit capture. The audit data is a privacy-redacted
projection, not the original audit-line bytes.

## Exact P-variant matrix

Every O1 item below returned `engineering_rejected` with an outcome and
`engineeringError.type:"EngineeringTargetInvocationException"`. The native checkpoint's
qualified type is `Siemens.Engineering.EngineeringTargetInvocationException`: its terminal
type name matches in all 21 cases, and all 21 message strings match exactly, including
line endings. These comparisons use the native checkpoint records, rather than matching
translated prose in the checkpoint report. The canonical responses retain the full messages.

Rejected actual writes have `success:false`, `phase:"applied"`, `error:null` and MCP
`isError:false`; accepted variants have `success:true`. “Clean” below means captured public
`hmi_read validate` results for the named surviving objects, not a candidate UI compile.

| Variant | Probe | Classification / failure point | Observed result |
| --- | --- | --- | --- |
| P1 | Duplicate table, same group | O1 / `Create` | Original table and its three tags retained; no cleanup needed |
| P2-cross-group | Duplicate table, another group | O1 / `Create` | No extra table; original table and contents retained |
| P2-case | Case-variant duplicate table | O1 / `Create` | No extra table; original spelling and contents retained |
| P3 | Colliding table rename | O1 / `Name` | Original table names and membership retained; contained tag clean |
| P4 | Duplicate tag, same table | O1 / `Create` | Create did not return an object; original retained and clean; no cleanup needed |
| P5-cross-table | Duplicate tag, another table | O1 / `Create` | Original name, membership and properties retained; clean |
| P5-case | Case-variant duplicate tag | O1 / `Create` | Original spelling and membership retained; clean |
| P6 | Colliding tag rename | O1 / `Name` | Both original tags retained; clean |
| P7 | Case-only tag rename | O1 / `Name` | Original case retained; clean |
| P8 | Duplicate discrete alarm | O1 / `Create` | Original alarm retained and clean; no cleanup needed |
| P8-case | Case-variant duplicate discrete alarm | O1 / `Create` | Original spelling and properties retained; clean |
| P9 | Duplicate analog alarm | O1 / `Create` | Original alarm retained and clean; no cleanup needed |
| P10 | Analog alarm using an existing discrete alarm name | Accepted control | Separate scopes both present; both validate clean |
| P11-discrete | Colliding discrete-alarm rename | O1 / `Name` | Both original alarms retained; clean |
| P11-analog | Colliding analog-alarm rename | O1 / `Name` | Both original alarms retained; clean |
| P12-discrete | Case-only discrete-alarm rename | Accepted control | Requested lowercase name landed; properties retained; clean |
| P12-analog | Case-only analog-alarm rename | Accepted control | Requested lowercase name landed; properties retained; clean |
| P12-table | Case-only table rename | Accepted control | Lowercase table name landed; child membership followed; child tag clean |
| P13 | Delete tag, then create its case variant | Accepted control | Original absent, requested lowercase replacement present; clean |
| P14 | Delete default tag table | O1 / `Delete` | Default table and all 12 child tags retained |
| P15-update | Set `dataType` on a bound tag | O1 / `DataType` | Original binding and `Int` retained; clean |
| P15-create | Create bound tag with `dataType` | O1 / `DataType` | Earlier binding properties reported applied; failed create cleanup `deleted` |
| P16-create | Create with `plcTag`, no connection | O1 / `PlcTag` | Earlier create/properties may have applied; cleanup `deleted`; new tag absent |
| P16-update | Set `plcTag` without connection | O1 / `PlcTag` | Internal tag retained unchanged; clean |
| P17 | Create into a missing table | O1 / `Create` | No tag, fallback placement or automatic table creation; no cleanup needed |
| P18 | Create with missing connection | O1 / subsequent `PlcTag` | Create/connection assignment returned before rejection; cleanup `deleted`; new tag absent |
| P19 | Create with missing alarm class | O2 | Requested class retained; Validate reports `AlarmClass` missing and shared tag/bit findings |
| P20-missing-tag | Create with missing trigger tag | O2 | Requested reference retained; Validate reports `RaisedStateTag` missing |
| P20-missing-member | Create with missing struct member | O2 | Requested member retained; Validate reports `RaisedStateTag` missing |
| P21 | Create table in missing group | Worker refusal / `target_not_found` | No native Create reached; no mutation |
| P22 | Create tag with comment in missing culture | Worker refusal / `target_not_found`, `Comment` | Create and earlier properties returned; cleanup `deleted`; new tag absent |

For example, P4's exact message is the following JSON string (the L2 rejection uses the
same message):

```json
"Error when calling method 'Create' of type 'Siemens.Engineering.HmiUnified.HmiTags.HmiTagComposition'.\r\n\r\n-The value must be unique.\r\n"
```

Cleanup `deleted` describes identity-checked cleanup of the new object, not rollback of an
entire batch. The outcome's `appliedProperties` and `failedProperty` preserve the partial
sequence. Rejected duplicate creates did not delete the pre-existing colliding objects.

## L1–L4

| Case | Captured result |
| --- | --- |
| L1 | Eleven items cover all nine operations; all succeed. Seven earlier items are `superseded`, four are `trusted`. Independent reads agree with the final surviving/deleted objects; surviving objects validate clean. |
| L2 | Five earlier items succeed, the sixth duplicate-tag create fails `engineering_rejected`, and six later items are skipped. Root is `success:false`, `error:null`, `isError:false`; the failed item retains typed evidence and outcome. Verification contains the five earlier successes only: two `superseded`, three `trusted`. Earlier mutations remain until restoration. |
| L3 | Dry run of L2 returns `phase:"preview"`, effects and guards, with no attempted item outcomes or native rejection. Independent reads and the modification flag show no mutation. |
| L4 | Referenced-tag delete and referenced-table delete both succeed. Informational `hmi_deletes_referenced_tag` guards report two references for each delete; `hmi_deletes_table_contents` reports the table's one child. Both verification entries are `trusted`. Public Validate reports the expected dangling alarm trigger references after deletion. |

L4's four successful `read_cross_references` queries exposed all four expected alarm names:
`AI_CL_M101` → `CP1029_AnalogA`, `CP1029_TagA` → `CP1029_DiscreteA`,
`CP1029_TagB` → `CP1029_DiscreteB`, and `accessLevel` → `CP1029_AnalogB`.
This is evidence for those four queries on this fixture; it does not establish complete
alarm discovery for every reference form. Exact observations are in L4's case capture.

## Independent reads, validation and restoration

Each case captures independent `hmi_read` results for the objects it could touch, including
pre-existing collision targets. The state review compared nine shared tag fields (`name`,
`table`, `dataType`, `hmiDataType`, `connection`, `plcTag`, `plcName`, `acquisitionCycle`,
`comment`) and ten shared alarm fields (`kind`, `name`, `triggerTag`, `alarmClass`,
`priority`, `triggerBit`, `condition`, `conditionValue`, `eventText`, `infoText`) with the
native checkpoint, plus table names, groups, child names and counts. The report's
classifications incorporate that review and the public validation
findings; the generated summary intentionally leaves its `oClass` unclassified and its
alarm-comparison limitation intact.

Public Validate does not validate a table directly. P1, both P2 variants and P14 therefore
have no table Validate result. The separate [validation supplement](2026-10-30-hmi-write-native-rejections-validation-supplement.json)
checks their 15 distinct child tags after the matrix on the unchanged restored baseline:
all 15 operations succeeded with zero findings. Status remained open and unmodified before
and after, and the saved-file hash was unchanged. Its canonical HMI response SHA-256 is
`783029130e63633568b25ef1d13f89c8e58e6ed97c377c3d83c5767f223d905a`.
This post-matrix child check is not a native table validator or an in-case table validation.
Expected `target_not_found` responses for deleted, renamed or never-created names in other
cases are absence checks, not clean validation of a surviving object.

After each state-changing case the controller closed without saving and reopened the saved
baseline. Every restoration records matching inventory and the baseline 101/9/4 counts;
state-changing restorations also record an unchanged saved-file hash. Cases recorded as
needing no restoration have an unmodified project and identical inventory. The `postCheck` is the
post-write observation: its inventory can intentionally differ before restoration. It must
not be mistaken for a failed restoration.

## Candidate boundary and unfinished evidence

All 35 captured batches ran on installed `.112` / `96a28b7`. The later R1 source fix
`31d1a44` changes only the conservative supersession predicate: no native calls, object
keys or schema changes. Source qualification found no old/new predicate admission
differences across the 72 earlier-success/later-attempted pairs in these 35 batches.
That bounded comparison is not a full rerun on the planned `.113` candidate. Focused R1
live checks and timing on `.113` remain pending; R1 was not installed or live checked for
this matrix.

Candidate UI compiles for state-changing variants, direct UI-language observation, L5 on
a local-session root, and timing-after are pending. Earlier checkpoint UI observations
belong to the checkpoint and do not substitute for these candidate checks. The timings
inside trace records are capture metadata, not the prescribed timing-after benchmark.

The evidence does not prove global absence of O3, race behavior, every native fault site,
or cleanup behavior under induced cleanup/transport failures. No such faults were induced
by this matrix. Task 6 must remain incomplete until its remaining evidence and checks are
recorded.
