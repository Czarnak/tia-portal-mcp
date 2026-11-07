# Run 1 findings (2026-11-07)

Working notes for the probe report. Run `lt-run-1`: `--all` (18 cases), one disposable standalone project
(two S7-1500 PLCs, 23 unrelated compile errors recorded as the baseline, none under `LT_*`) and one
disposable user global library. Fixture check `lt-check-3` passed. Crash-risk cases P13b, P14a, P14b not run
yet. Every case worked on fresh copies, closed without saving; no confirmations were raised.

## Answers

| ID | Result | Evidence (record fields) | Design consequence |
| --- | --- | --- | --- |
| P1u, P1u-notx, P1f | **Conversion works**, with and without a transaction. New object linked to `CreatedType` (`link.linked`, `typeIsCreatedType`, `versionInCreatedType` all true). Created version `0.0.1` InWork, `isDefault:false`. | all steps `ok:true`; diff shows only the new link and the new library type | `create_type` route (c) confirmed; inline-content fallback not needed. Result reports `isDefault` as read. |
| P2 | Dependents fine: `LT_UdtUser_DB` member still `"LT_Udt"`, `LT_Fb_DB.InstanceOfName` `LT_Fb`, `LT_Caller` compiles. Compile after conversion = baseline (23 errors, `ltErrors:0`). | `compilePLC_1`, `compileCaller`, `udtUserMembersAfter` | No fix-up of callers or instance DBs. |
| P1r-after-import, P1r-after-delete | **Rollback complete**: `stateAfter == stateBefore` (plain UDT back, no library type). | `stateBefore`/`stateAfter` | Transaction kept for rollback; link check failure → `postcondition_failed` after rollback is backed. |
| P3 | Conversion of an FB using a plain UDT works; no type appears for the UDT. `Release(0.0.1)` refused with an empty message (`EngineeringTargetInvocationException`, no inner). | `failureStage:Release(0.0.1)` | Likely `DoNotAutomaticallyCreateOrReleaseDependencies` aborting because the UDT is not a type (inference). Release fails without explanation: needs docs or a dry-run note; optional P3b. |
| P4 | **Not answered.** Setup export of the fresh PLC_2 instance refused: "Inconsistent blocks and PLC data types (UDT) cannot be exported." | `setupStage:export(LT_Udt)` | Re-run with a compile before export. |
| P5 (+P13a) | Type GUID stable; **version GUID changes on release** (held wrapper follows it). In-work number prefilled: `0.0.1` after create, last release + 1 after edit. Each release becomes the default; the test instance relinks to the released version. | `afterRelease001/002`, `inWorkAfterCreate/Edit` | Release result re-reads GUID; in-work GUIDs are temporary; dry run can show the prefilled number. |
| P6 | `SetAsDefault` on older `0.0.1` works; only the flag moves; PLC instance stays on `0.0.2`. | `stateBefore`/`stateAfter` | As specified; docs: default change does not touch existing instances. |
| P7a | Type delete with a PLC instance **refused**: "cannot be deleted because there are instances for the following versions: 0.0.2." Nothing deleted. | exception | "Deletes just delete" holds: Portal refuses, nothing is lost. **Spec/docs wording "may remove PLC instances" is wrong.** |
| P7b | Deleting the default version **refused**: "…cannot be deleted because it is a "default" version. To delete this version, set a different version as default and try again." | exception | Docs: set another default first (can be an earlier item in the same call). |
| P7c | Refused like P7a; confounded (PLC instance still present). Master copy listed in `masterCopiesContainingInstances`. | notes | Master-copy-only case unobserved: re-run. |
| P8 | Instantiating a global version the project lacks works; **project library gains the type with the same type and version GUIDs**; instance links to that version; returned object `IsConsistent:False`. | diff, `returned` | Dry-run note confirmed; instance needs a compile before `plc_read`. |
| P9 | Sync copies every missing version; **target default moves only to the source default, and only when its number is higher**; higher non-default copies never become default; a new type takes the source default. Target keeps source GUIDs. | `sync-a/b/c` | Docs wording updated accordingly. |
| P10 | Not observed: fixture has no know-how protected FB. | `fixtureMissing` | Needs an `LT_FbKhp` in the fixture. |
| P11 | `Discard` removes the in-work version; the instance survives and relinks to released `0.0.1`. | `stateAfter` | `delete_type_version` on in-work keeps instances. |
| P12 | **Not answered.** Export of the in-work test instance refused (inconsistent). | `failureStage:export(LT_Fb)` | Objects placed by library operations are inconsistent until compiled (P4, P8, P12): `plc_read` fails until a compile. Re-run with compile. |
| P14c | Release `0.0.1` valid. | `stateAfter` | Control passes. |

## Cross-cutting

- Export needs a consistent (compiled) object; Release does not. `create_type` exports before it deletes, so an
  uncompiled source fails safely with nothing changed.
- Probe defect: `failureStage` reads `ExclusiveAccess.Dispose` for the two deliberate rollbacks because successful
  Disposes overwrite the stage; the stack trace shows the real throw site. Not a Dispose failure.

## Re-run list (with the crash-risk cases, when the owner resumes)

- P4': compile PLC_2 after setup, before the export.
- P12': after `EditVia`, note `IsConsistent`, compile the test instance, note again, then export/import; release after.
- P7c': remove the PLC instance first, keep the master copy, delete the type.
- P7d: `SetAsDefault(0.0.1)`, then delete `0.0.2` while the instance links to it.
- P11': real content change before `Discard`; record a content hash after.
- P3b (optional): release with `AutomaticallyCreateOrReleaseDependenciesIfRequired`, or make the UDT a type first.
- P10: needs `LT_FbKhp` added to the fixture.
- Optional: `create_type` into a nested type folder.
- Crash-risk, alone, last: P13b, P14a, P14b.
