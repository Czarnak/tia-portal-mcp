# HMI write native-rejection checkpoint probes — 2026-10-29

**Native probing complete: P1–P22, 31 variants.** Results: **21 native rejections**, **eight
accepted variants**, **two worker-equivalent refusals**. All 31 restorations matched the saved
baseline. No silent dropped write or wrong-object mutation was observed.

**Interactive UI follow-up is also complete for all eight accepted variants.** Five expected-success
controls had the expected read-back, clean validation of their validatable objects and successful
maintainer UI compilation. The three accepted invalid-reference variants were diagnosed by both
native validation and the HMI compiler. No implementation plan or product refactor was started.

## Scope and provenance

Matrix: [design §6.7](../../specs/2026-10-29-hmi-write-native-rejections-design.md#67-live-probing-protocol).
The maintainer first requested installed-tool checks, then authorized dedicated native probes,
confirmed that the entire project is disposable, and requested 15-minute probe timeouts.

- Installed server/worker: `4.2.0-local.79.g75c0568`, commit
  `75c0568d4d83b9430a13feb62aea52e2232c2bad`; mode `full`.
- TIA Portal V21, PID `35872`; observed UI language: English.
- Project: `C:\Users\LCZ\Desktop\RnD\TIA-Portal\DisposableProjects\MixOfEverything\MixOfEverything.ap21`.
- HMI: `HMI_RT_1` on `HMI_Wash` (MTP1000 Unified Basic).
- Original inventory: 97 tags, seven tables, zero alarms.
- Saved qualified fixture baseline: **101 tags, nine tables, four alarms**. The disposable file
  retains `CP1029_*` fixtures; restoration refers to this baseline.

The maintainer compiled the qualified baseline in TIA and reported **“Clean compilation, no
errors.”** A separate warning count was not supplied. The initial unattended run had no
post-case UI compilation; the subsequent interactive checks are recorded below.

The separate probe used real installed V21 Base/WinCCUnified assemblies in a 64-bit,
.NET Framework 4.8 MTA process. It followed worker native calls/setter order, without transactions
or extra ExclusiveAccess, and recorded exception types/messages/stacks/inner chains, mutation
stages, cleanup, fresh state and `IValidator.Validate()`. P21/P22 reproduced kept worker checks;
their custom probe exceptions are explicitly separated from Siemens exceptions.

The installed tools and product source were unchanged. Direct native observations do not
demonstrate the proposed canonical error projection, guard removal or shared verifier.

## Results

All **21 native exceptions** were
`Siemens.Engineering.EngineeringTargetInvocationException`, with empty `inner[]` in this run.
“O1” means native rejection; “O2” means accepted with a specific native validation diagnostic.
Expected-success controls are not invalid writes requiring an O3 ruling.

| Case | Native result | Independent observation |
| --- | --- | --- |
| P1 duplicate table, same group | **O1**, Create | Original table/contents unchanged |
| P2 cross-group / case-variant table | **O1**, both Creates | No extra table; originals unchanged |
| P3 colliding table rename | **O1**, Name setter | Original names retained |
| P4 duplicate tag, same table | **O1**, Create | Create did not return; original unchanged; no cleanup needed |
| P5 cross-table / case-variant tag | **O1**, both Creates | Original name/table membership retained |
| P6 colliding tag rename | **O1**, Name setter | Original names retained |
| P7 case-only tag rename | **O1**, Name setter | `CP1029_Reuse` retained original case; validation clean |
| P8 duplicate discrete alarm / case variant | **O1**, both Creates | Create did not return; original unchanged; no cleanup needed |
| P9 duplicate analog alarm | **O1**, Create | Create did not return; original unchanged; no cleanup needed |
| P10 analog with existing discrete name | **Accepted control** | Separate discrete and analog `CP1029_DiscreteA`; both validate clean |
| P11 colliding alarm rename, both kinds | **O1**, both Name setters | Names/properties retained |
| P12 case-only discrete / analog / table rename | **Accepted controls**, all three | Exact lowercase names landed; membership followed; alarms/contained tag validate clean |
| P13 delete tag then create case variant | **Accepted control** | Only `cp1029_reuse`, Int in `CP1029_TableA`; count 101; validation clean |
| P14 delete default tag table | **O1**, Delete | Default table and all 12 tags remained |
| P15 bound-tag dataType, update / create | **O1**, both DataType setters | Update retained Int; failed create applied binding then cleaned up |
| P16 plcTag without connection, create / update | **O1**, both PlcTag setters | Internal update unchanged; failed create cleaned up |
| P17 create into missing table | **O1**, Create: “Tag table not found” | No tag, fallback placement or automatic table creation |
| P18 create with missing connection | **O1 at subsequent PlcTag setter** | Create/Connection returned; cleanup deleted new tag |
| P19 missing alarm class | **O2**, accepted | Missing class retained; Validate explicitly reported AlarmClass missing |
| P20 missing trigger tag / struct member | **O2**, both accepted | Requested references retained; Validate reported each RaisedStateTag missing |
| P21 missing table group | **Worker-equivalent refusal before Create** | No native Create reached |
| P22 missing project culture | **Worker-equivalent refusal after Create** | Create/DataType returned; culture lookup refused; cleanup deleted tag |

Every variant had `restoreMatchesBefore:true`. Recursive checks found no harness errors,
observation/read errors or incomplete native validation results.

HmiTagTable has no native Validate operation. Table cases use fresh names/contents; P12-table
also validates its contained tag, whose TagTableName followed the rename exactly.

### Important distinctions

**P18 differs from the design's expected O2 outcome.** The Connection setter accepted the
missing connection string. The following PlcTag setter threw “The controller tag AI_CL_M101 was
not found.” Applied stages were `Create → Connection`, then identity-checked cleanup deleted the
tag. This proves rejection of the tested complete request, not connection-existence validation
by the Connection setter. A connection-only assignment was not tested.

**P19 has an additional fixture diagnostic.** It reused `CP1029_TagA` bit 0, also used by the
existing discrete alarm. Validate reported that duplicate combination on both alarms.
Independently, the new alarm's AlarmClass diagnostic explicitly said:

```text
The object "CP1029_NoSuchClass" does not exist. Please update or delete the reference.
```

Both P20 variants returned specific RaisedStateTag missing-object diagnostics. These support O2
even though UI compile remains pending.

**Duplicate Create never returned the original object in P4/P8/P9.** The forced later
setter/culture failure was unreachable in those collisions; originals survived unchanged.
Failed-create cleanup succeeded in P15-create, P16-create, P18 and P22. No cleanup failure or
alias-returning Create was induced.

**P21/P22 are not native rejection examples.** Their kept worker logic was reproduced by the
probe rather than invoked through the production worker dispatcher. P22 accepted native
creation/DataType before the worker-equivalent culture refusal and identity-checked cleanup.

## Installed-tool checks and design corrections

Seventeen numbered cases stopped in current host checks:

- P1–P6, P8/P9 and both P11 kinds: per-operation `hmi_name_collision` guards.
- P15/P16 create/update: `validation_error`.
- P17–P22: `target_not_found`, including both P20 references.

Installed-tool P7 returned `worker_operation_failed`, `failedProperty:"Name"`,
`appliedProperties:[]` and no `engineeringError`. Independent `hmi_read` preserved its name and
clean validation. The native probe subsequently captured its actual exception type.
P10/P12/P13/P14 were not separately executed through the installed tool.

The design header needs two reachability corrections: current source permits P13's dependency
and the native sequence succeeded, although the public P13 batch was not executed; P21/P22 are
blocked by current host group/culture checks, so installed P22 never reached Create/cleanup.
These findings do not authorize implementing the proposed guard removals.

## Initial execution and restored state

The successful case run was **15:52:16–16:08:42 Europe/Warsaw** on 2026-10-29
(14:52:16–15:08:42 UTC). Process exit: **0**, with all 31 completion records.

Attachment setup had failed/canceled requests before any case ran. The probe was corrected from
STA to the worker's default MTA and gained boundary logs/persisted fatal phase and stack.
This was a fidelity correction, not proof that STA caused an attachment failure.
“Yes to all” opened an inaccessible Windows UAC prompt; one-time “Yes” ultimately authorized the
successful session. Persistent authorization was not confirmed.

A **900-second watchdog** covered attachment and each whole variant. No completed native case
timed out. The probe never saved and restored after every variant by closing without saving and
reopening the exact baseline. It exits only its own process on timeout, never kills Portal.

Build: zero warnings/errors. Offline self-test: all 31 variants/22 numbered cases, four scope
refusals, 64-bit MTA, `attached:false`. Independent source/data review qualified the run.

Final executable SHA-256:
`AC5056A03B9F68ABF73A753FAE25277CA40E5CDE44B1EBFA1D484FB92C54040F`.

External close/reopen invalidated the old MCP binding. Explicit rebind recovered it; the final
public-tool check found **101 tags, nine tables, four fixture alarms, no probe-tag residue**.
The bound project was open and `isModified:false`.

## Evidence

- [Native summary](inputs/2026-10-29-hmi-write-native-summary.json): per-case stages, messages,
  cleanup, restoration and validation findings.
- [Complete native trace, gzip](inputs/2026-10-29-hmi-write-native-probes.jsonl.gz): exact flushed
  JSONL with all snapshots/progress; decompressed SHA-256:
  `664A47AC170B333473831BA77235647EBB3BE0560532F66AB0A1B30934244A22`.
- [Archived source/build instructions](inputs/2026-10-29-hmi-native-probe/README.md).
- [Installed preflight/fixtures](inputs/2026-10-29-hmi-write-baseline-preflight.json).
- [Installed P7/restoration](inputs/2026-10-29-hmi-write-installed-P7.json).
- [Final public-tool restoration](inputs/2026-10-29-hmi-write-final-restoration.json).

Initial eight-item fixture setup hit the MCP client's 300-second timeout. Its missing canonical
response was not reconstructed: independent reads established state, then smaller writes and
validation qualified the baseline. During setup only, mixing discrete/analog use of one trigger
was accepted but reported by Validate; distinct triggers corrected it before the clean compile.

Automatic approval review rejected the first P15 attempt on original `AI_CL_M101`. The actual
installed check used temporary bound tag `CP1029_Bound`; the original attempt was not repeated.
The maintainer subsequently confirmed that the whole project was disposable.

## Interactive UI follow-up — 2026-10-29

The maintainer returned and completed the eight remaining UI compile checks one at a time.
They asked the agent to leave completed cases in place. The five success-control changes remained
present through the final probe. The maintainer subsequently confirmed manually deleting P19 and
P20-missing-tag after their compile checks, matching their absence before the next probe. Those
errors must not be added to later compile totals. No automatic Portal removal is inferred.

| Variant | Reproduction and validation | Maintainer UI result | Post-check state |
| --- | --- | --- | --- |
| P10 | Native leave-open helper succeeded; both alarms validate clean; original discrete unchanged | **“Clean compile; both alarms visible”**; visible Compile panel: **0 errors, 39 warnings** | Retained; cancelled cleanup never dispatched native deletion |
| P12-discrete | Native rename returned; exact `cp1029_discreteb`, original settings and clean Validate | Lowercase name confirmed in Discrete alarms; no compilation error, everything looks fine | Retained with P10; warning count not supplied |
| P12-analog | Native rename returned; exact `cp1029_analogb`, original settings and clean Validate | **“Lowercase name confirmed, compilation clean, everything is fine”** | Retained with earlier cases; warning count not supplied |
| P12-table | Native rename returned; exact `cp1029_tableb` contains `CP1029_TagB`; its tag-table read-back matches and tag Validate is clean | Lowercase name and existing tag confirmed; compilation clean, everything is fine | Retained with earlier cases; warning count not supplied |
| P13 | Delete/Create/DataType returned; only `cp1029_reuse`, Int in `CP1029_TableA`; total tags 101 and Validate clean | **“Tag correctly replaced, compilation clean, everything is fine”** | Retained with earlier cases; warning count not supplied |
| P19 | Native create returned; missing `CP1029_NoSuchClass` retained; Validate reports AlarmClass missing and shared tag/bit | Alarm created; compiler reports invalid class on `CP1029_P19` and shared trigger on it and `CP1029_DiscreteA`; screenshot: **3 errors, 39 warnings** | Manually deleted by maintainer after compilation; absent before P20 |
| P20-missing-tag | Native create returned; `CP1029_NoSuchTag` retained; Validate explicitly reports RaisedStateTag missing | Alarm created; compiler: `Trigger tag: The object "CP1029_NoSuchTag" does not exist. Please update or delete the reference.` | Manually deleted by maintainer after compilation; supplied excerpt identifies one error, not a complete compile total |
| P20-missing-member | Native create returned; existing struct root confirmed; Validate explicitly reports the requested member missing | Alarm created; compiler: `Trigger tag: The object "Communication_DB_MOT01.CP1029_NoSuchMember" does not exist. Please update or delete the reference.` | Helper left result open; supplied excerpt identifies one error, not a complete compile total |

P10's 39 warnings were observed directly in TIA's Compile panel after the maintainer response;
the baseline warning count was not recorded, so no warning delta is claimed. Only the baseline
received UI compilation during the initial run; no post-P7/per-case compile was performed then.

The installed P10 create attempt stopped at argument validation: this bridge advertised
`conditionValue` as a string, whereas the installed server requires a finite JSON number. The
supplied string `"30"` was rejected and no mutation occurred. The dedicated native helper reproduced
the intended Int32 setter call instead; its read-back was Double 30, matching the initial run.

The already-sent installed P10 cleanup hit the client's 300-second timeout without a canonical
response. Its later audit recorded cancellation before mutation: `ui-P10-cleanup`, `access_denied`,
item `skipped`, no item duration or batch. The analog alarm was independently still present in
P12-discrete's before-snapshot. Cleanup was not retried. The exact slow planning read was not identified.

Interactive evidence:

- [Leave-open source and instructions](inputs/2026-10-29-hmi-ui-compile-probe/README.md).
- [P10 native trace, gzip](inputs/2026-10-29-hmi-ui-P10.jsonl.gz), decompressed SHA-256
  `D1BEF5ACBCD63F6523DAF1A21DA23D9A50632737226E917D4A0B6B6F9F753F45`.
- [P10 cancelled-cleanup audit](inputs/2026-10-29-hmi-ui-P10-cleanup-audit.json): exact cancellation
  before mutation, with the delete item skipped.
- [P12-discrete native trace, gzip](inputs/2026-10-29-hmi-ui-P12-discrete.jsonl.gz), decompressed
  SHA-256 `AD5ECCCF812A72930FEFCA7135EB3DFA98A7776E8FAAB5556A08A1DC979FB563`.
- [P12-analog native trace, gzip](inputs/2026-10-29-hmi-ui-P12-analog.jsonl.gz), decompressed
  SHA-256 `6324FF9826CC474B4B65D81C1A138F065D9EF6491BFA5D2AA19C6042B1B0A639`.
- [P12-table native trace, gzip](inputs/2026-10-29-hmi-ui-P12-table.jsonl.gz), decompressed
  SHA-256 `BB62A5C7F0EC6A9C95655E35F099B1405561D5914CF23715EBA1E8B948E9C63C`.
- [P13 native trace, gzip](inputs/2026-10-29-hmi-ui-P13.jsonl.gz), decompressed SHA-256
  `C832EE08CC6ABB7DFD86226CBCF34C0C9D252E0A487367B433DB1812F74420EA`.
- [P19 native trace, gzip](inputs/2026-10-29-hmi-ui-P19.jsonl.gz), decompressed SHA-256
  `FF64293F85421248FF78B1FDC53E6E1BE60BC222BE284C62BB36F87EF0912E49`.
- [P19 compiler screenshot](inputs/2026-10-29-hmi-ui-P19-compile.png), supplied by the maintainer:
  invalid alarm class, both shared-trigger messages, and total 3 errors/39 warnings.
- [P20 missing-tag native trace, gzip](inputs/2026-10-29-hmi-ui-P20-missing-tag.jsonl.gz), decompressed
  SHA-256 `5C1A69233C55E073D2097D85F5352CA1C9C49BC2804D5143CBE7D042B63A0BF8`.
- [P20 missing-member native trace, gzip](inputs/2026-10-29-hmi-ui-P20-missing-member.jsonl.gz), decompressed
  SHA-256 `0E285FEF7648443BF338461585B7527E51A0B1570291514D0D3F50998D6648E3`.
- [Final public project status](inputs/2026-10-29-hmi-ui-final-status.json): exact disposable project
  open and modified after the last UI check. No helper Save was performed.

**Checkpoint complete.** The tested matrix contains 21 native rejections (O1), three accepted
invalid-reference variants diagnosed by Validate and UI compilation (O2), five successful controls,
and two worker-equivalent refusals. No O3 finding was observed within this matrix.

Refactor acceptance, product error-projection tests, timing comparisons, local-session regression,
cleanup-failure induction and Portal-loss tests remain outside this checkpoint. Product implementation
remains stopped; no further mutation was issued after the final requested case.
