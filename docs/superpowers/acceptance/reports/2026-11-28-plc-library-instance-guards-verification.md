# PLC library-instance guards: verification report

Date: 2026-11-28. **Verdict: scoped PASS for the full non-packaging Debug suite, installed-reference Release build and executed V21 guard/control cases. PR CI remains pending.**

## Candidate and scope

The code-under-test is commit `fe604c57` on `feat/plc-library-instance-guards`. No source changed
between the focused checks and the final full offline run. The installed-reference Release build
reports version `1.0.0+fe604c5788b0dd70808bf812c2effcfb21c40d9b`. Native qualification remains separate.

Scope is linked-instance protection in `plc_write`: content replacement refuses linked FB/FC/PLC UDT
instances; direct or nested-group deletion protects in-work test instances. Plain objects and
committed-instance deletion retain positive controls. Planning, public tools, access rules and write
contract `2.1` remain unchanged. This candidate introduces neither preview references nor an
apply-planning bypass.

### Release artifacts

| Artifact | SHA-256 |
| --- | --- |
| Host DLL | `c8289e732faaa618d78901c25cd5bac19a706a3d75025f25562cf480de365010` |
| Deployed worker EXE | `5cedae224fb6b4359d226f63d6adbb459aed0d8c108e4a91cffc4e61e0c5d396` |

## Offline evidence

These results qualify the committed code offline, not live Siemens execution.

| Check | Recorded result | Boundary |
| --- | --- | --- |
| Full Debug non-packaging suite | 9,237 passed, zero failed; total time 14.3244 minutes | Offline tests only |
| Final focused regression run | 83 passed, zero failed | Offline tests only |
| Native-boundary double tests | 156 passed, zero failed | Offline importer, inspection and deletion evidence; no live Siemens calls |
| Full solution stub build | Passed with zero errors and eight existing warnings | Compile-only stub evidence |
| Independent code review | No P1/P2 finding reported | Static review, not native qualification |
| Release build against installed V21 references | Passed with zero errors and nine warnings; 45.81 seconds | Installed-reference compilation, not native execution |
| Full instrumented Release suite | 9,233 passed, four failed out of 9,237 selected; 21 minutes 15 seconds | Failed run; failures occurred during fixture startup before domain operations |
| Included-scope line coverage | 95.49%, above the 80% threshold | Global host/Contracts coverage; existing CI settings exclude native-worker namespaces |
| Failed-method Release rerun without instrumentation | 13 cases passed in 22 seconds using the same temporary directory | Focused rerun only |
| Same-method rerun with original coverage settings | 13 cases passed in 16 seconds | Does not turn the failed full instrumented run green |
| Focused new worker-class coverage run | 167 passed, zero failed; 966 ms | Source-linked offline doubles; coverage included only the two new worker classes |

The instrumented failures were the VCI dependent-export and invalid typed-plan cases, Library
`MetadataCalls_KeepStagingRules` with preview/full/content-only arguments, and the initial bind in
the safe-read timeout test. Each stopped during fixture startup before its domain operation.
The focused reruns made no source or timeout changes. The coverage percentage passes its included
scope's threshold; it is not a native-worker or changed-lines coverage claim, and the full
instrumented test result remains failed. The PR CI outcome is still required.

The separate focused coverage run selected guard, inspector, importer, diagnostics, coordinator and
deletion tests. Its transient settings explicitly included only `PlcLibraryInstanceGuard` and
`PlcWriteTargetInspector`:

| Included class scope | Line coverage | Branch coverage |
| --- | --- | --- |
| `PlcLibraryInstanceGuard` | 100% | 90% |
| `PlcWriteTargetInspector` | 95.45% | 95% |
| Combined | 80/81 executable lines (reported 98.76%) | 55/60 branches (reported 91.66%) |

This supplements the global run's worker-namespace exclusion. It measures source-linked offline
double execution, not native Siemens execution or coverage of all worker code.

A separate qualification helper compiled against the installed API and passed its self-test with
zero native calls. That validates the harness only and is excluded from the candidate/native verdict.

## Native qualification boundary

The recorded public-tool guard and control cases passed on the approved disposable project/PLC fixtures. Candidate
artifact hashes still match the recorded `fe604c57` build. These refusals stop in planning and do
not establish native-adjacent race or fatal-path protection. Cleanup and inventory restoration are
recorded below; CI and the explicitly unqualified native areas remain separate.

| Effective live configuration | Value |
| --- | --- |
| Candidate access mode | `full` |
| Individual value budget | 60,000 characters |
| Complete document budget | 180,000 characters |
| Worker request timeout | 5 minutes |
| Installed Siemens build | `Siemens.Automation.Portal.exe` and `Siemens.Engineering.Base.dll` both report ProductVersion and FileVersion `2100.0.121.1` |

These effective values were confirmed from candidate startup logs and its baseline. They describe
this run; the product's startup access-mode policy is unchanged. The installed Siemens version
fields were read directly; no Update number is inferred.

| Native area | Current qualification status |
| --- | --- |
| Public linked-content preview/apply refusals | Linked FB XML preview, FB source apply, FB source patch apply, UDT XML apply and committed FC XML apply passed as recorded below; native-adjacent refusal remains unqualified |
| Public in-work direct block deletion refusal | Linked FB deletion passed as recorded below |
| Mixed nested-group deletion refusal | Passed for an owned group containing a committed FB and nested in-work FC |
| Plain FB source seed and read-back | Source write succeeded; XML re-export contained `CompileUnit` and `McpProbe`, and UI compile reported zero errors/warnings; scoped fixture/control evidence |
| Plain LAD FC deletion | Passed public-tool control with the `plc_deletes_block` info guard; no linked instance was involved |
| Other plain-object formats and paths | Not fully qualified |
| Committed-instance deletion controls | Direct committed FB deletion and all-committed nested-group deletion passed; subsequent inventories proved removal |
| All supported formats and eligible containers | Not fully qualified; report individual executed cases rather than generalizing a sample |
| Native late-linkage races and fatal inspection paths | Offline coverage only; no live race or fatal-path verdict |

### Verified public guard cases

| Case | Observed result |
| --- | --- |
| Linked FB XML preview | `phase:"preview"`, `success:true`, with the `plc_library_instance_edit` block guard |
| Linked FB source apply | `phase:"blocked"`, `success:false`, with `plc_library_instance_edit` |
| Linked FB source patch apply | `phase:"blocked"`, `success:false`, with `plc_library_instance_edit` |
| Linked UDT XML apply | `phase:"blocked"`, `success:false`, with `plc_library_instance_edit` |
| In-work FB direct deletion | `phase:"blocked"`, `success:false`, with `plc_library_test_instance_delete` and the `plc_deletes_block` info guard |
| Mixed group deletion: committed FB and nested in-work FC | `phase:"blocked"`, `success:false`, with `plc_library_test_instance_delete` and the `plc_deletes_group_contents` info guard |
| Committed FC XML apply | `phase:"blocked"`, `success:false`, with `plc_library_instance_edit`; `batch:null` and a fresh unchanged XML hash |

All actual guard-rejection calls returned `batch:null`. After the initial five cases and before FB
release, fresh FB source, FB XML and UDT XML hashes matched their pre-attempt values exactly; fresh
`list_versions` also confirmed the same FB version GUID and `InWork` state. After the mixed-group
refusal, the fresh FC XML hash exactly matched its pre-attempt value. Separate native inspection
confirmed the FC was present, linked and `Committed` at version `0.0.1`, with the version GUID
matching its release. Its XML apply refusal again preserved the exact fresh XML hash. These
observations qualify public planning refusals only; they do not exercise a late race or fatal path.

### Verified committed-instance deletion controls

| Case | Observed result |
| --- | --- |
| Direct committed FB deletion | `phase:"applied"`, `success:true`, with the `plc_deletes_block` info guard; the subsequent complete group snapshot proved the FB absent |
| All-committed nested-group deletion | `phase:"applied"`, `success:true`, with the `plc_deletes_group_contents` info guard; the fresh complete PLC blocks-root snapshot exactly matched the baseline `(type,name,details)` tuples: Program blocks and the existing Main OB1/LAD |

### Setup observation: configured project on an empty Portal

The candidate was configured with `--project` for the exact approved disposable file. Portal-only
attachment succeeded after the authorised one-time Openness confirmation. The `open_project`
bootstrap then failed before planning or project mutation with `binding_conflict`: the worker did
not return a project path while the binding was `configured_unverified`.

Read-only tracing located the failure in lifecycle source preparation: `LifecycleBindingStrategy`
attempted to ground the configured source against the empty Portal, where no project path existed.
The relevant lifecycle/binding files are unchanged from `af47bbe6`; this is an existing setup
limitation, not a PLC library-guard result.

The UI confirmed the Portal was blank. Opening the exact authorised file through TIA's Open dialog
allowed the candidate to bind to the exact Portal/project successfully; the initial status reported
`isModified:false`. The observed PLC software is `PLC_1` under `S7-1500/ET200MP station_1`.
These observations establish the fixture and binding only. They do not qualify a planning or
native-adjacent guard; the separately recorded cases above establish the scoped public refusals.

Subsequent authorised preparation created a task-owned block group, nested group, FB, plain FC and
library folder. FB source seeding and the plain LAD FC deletion control completed as recorded above;
the first UDT fixture import was refused by culture validation, and a fresh public read confirmed
the target was absent. A culture-neutral import then committed the plain UDT. Direct
`compile_check` on `PLC_1` succeeded with zero errors/warnings, after which UDT XML export succeeded.
The plain UDT content preview succeeded without guards.

The owned FB, FC and UDT conversions all succeeded with version `0.0.1` in `InWork`, returning concrete
GUIDs and exact owned instance paths. A subsequent compile again reported zero errors/warnings.
Resuming qualification preserved validated fixture ownership. A non-mutating uncompiled-UDT export
refusal was not automatically replayed. The `isModified:false` observation above is the initial
pre-fixture state, not the final project status.

The owned FB was then released successfully. Native release retained the type GUID and version
number `0.0.1` but produced a new committed version GUID. Separate native inspection confirmed the
FB remained present and linked, with `Committed` state and a version GUID matching the release.
The owned-group snapshot contained four nodes: the root group, that FB, a nested group and its FC.
Deleting this mixed group was blocked as recorded above while the nested FC was still in-work.
The later committed-instance controls above qualify direct FB deletion and the all-committed
nested-group deletion actually executed.

### Cleanup and final state

Cleanup completed within the authorised ownership scope:

- The native UDT cleanup transaction committed, returned true and confirmed the owned type absent.
  This was cleanup, not a public PLC UDT-deletion guard test.
- All three owned library types were deleted using fresh GUID-lineage inventories. The owned library
  folder was deleted only after a fresh complete read established it was empty.
- The entire final Library-root canonical result exactly matched its 15-item baseline; the PLC
  Types root was empty. All ten ownership-manifest entries were deleted and zero owned fixtures remained.
- The original on-disk `.ap21` SHA-256 and both candidate artifact hashes were unchanged.

Final `get_project_status` reported `isOpen:true`, `isModified:true`. The Portal was left open and
unsaved; no `SaveProject` call was made. The `.ap21` hash comparison concerns that file only and does
not establish that every byte in the project directory was unchanged. The inventory comparisons
establish the recorded fixture restoration scope, not a rollback of all project state.

The [November 7 native probes](2026-11-07-library-types-native-probes.md) motivate the guards but do
not qualify these new paths. Do not deliberately repeat their unguarded modal or unlink cases.
The [October 8 singleton probes](2026-10-08-ob-creation-spike.md) and
[October 26 guarded-tool report](2026-10-26-plc-write-native-rejections-live-verification.md) retain
their separate scopes; their documentation clarification is not a new native test.

## Qualification limits and remaining gate

The PR CI outcome is still required. The failed full instrumented Release run remains failed despite
its passing focused reruns and coverage threshold; this report does not label it green.

No live qualification is claimed for native-adjacent late-linkage races or fatal inspection paths,
local-session behavior, or the exhaustive FB/FC/UDT format matrix. Those paths retain their separately
identified offline evidence and unexecuted native scope. Public planning refusals must not be used
as proof that a native-adjacent replacement or deletion was entered and refused. The scoped verdict
covers only the recorded code, candidate artifacts, configuration, executed cases and cleanup.
