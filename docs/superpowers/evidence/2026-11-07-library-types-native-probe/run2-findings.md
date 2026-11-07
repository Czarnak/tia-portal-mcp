# Run 2 findings (2026-11-07)

Working notes for the probe report. Run `lt-run-2`: `--case P1n,P3b,P4c,P7c2,P7d,P10,P11c,P12c` against the
same fixture plus the know-how protected `LT_FbKhp` (fixture check `lt-check-5` passed, baseline unchanged:
23 errors, none under `LT_*`). Every case worked on fresh copies, closed without saving; no confirmations.
PLC_2 compiles report one unrelated error (`ltErrors:0`); `IsConsistent` still flips to true after it.

## Answers

| ID | Result | Evidence (record fields) | Design consequence |
| --- | --- | --- | --- |
| P1n | `CreateFromDocuments` into a nested type folder works: type only in `LT_Folder/LT_Sub`, linked, InWork `0.0.1`. | `inNestedFolder`, `inRootFolder`, `link` | Folder target as specified. |
| P3b | Release with `AutomaticallyCreateOrReleaseDependenciesIfRequired` **makes the plain `LT_Udt` a type, releases it `0.0.1` and links the PLC_1 UDT to it**; `LT_FbUdt` released `0.0.1`. | `udtTypeAfterRelease`, `udtPlc1Link`, `versionsAfterRelease` | Explains P3's empty refusal (dependency not a type). Release needs a decision: automatic mode creates types the caller did not name. |
| P4c | With a compile first, re-converting a released type instance **is not refused**: a second type `LT_Udt_1` (InWork `0.0.1`) is created and the PLC_2 object (still named `LT_Udt`) relinks to it; the old `LT_Udt` type stays with no instance. | `created`, `after.plcs`, `after.projectLibrary` | `create_type` must refuse a source that is already a type instance (tool-owned check). |
| P7c2 | A master copy alone blocks the type delete: "cannot be deleted because there are instances for the following versions: 0.0.2." | exception, `masterCopiesContainingInstancesAfterInstanceDelete` | Portal protects master-copy instances too. |
| P7d | Deleting a non-default version with an instance refused: "LT_Udt (0.0.2) cannot be deleted because there are still instances of the type in the project." | exception | Every delete with instances is refused by Portal. |
| P10 | Know-how protected FB exports (5,015 bytes); `IsKnowHowProtected:true`. | `steps`, `after.files` | Export of a protected source works; whether `CreateFromDocuments` accepts it stays unprobed. |
| P11c | **Override import onto the in-work UDT test instance refused**: `PlcTypeComposition.Import` → "Error when calling method 'Delete' of type PlcStruct" (no inner). Setup stopped there; Discard not reached. | `setupError`, `setupStage` | See below. |
| P12c | Same for the FB: `PlcBlockComposition.Import(Override)` on the compiled in-work test instance → "Error when calling method 'Delete' of type FB". Instance and versions unchanged. | `exception`, `stateBefore`/`stateAfter` | See below. |

## The edit flow is blocked

`plc_write`'s `update_block_logic` and `update_type_content` both import with `ImportOptions.Override`, which
deletes the target first. Portal refuses to delete an in-work test instance, so neither can change the
content of a type being edited. The spec's edit → `plc_write` → release flow does not work as written.
Nothing changed, but the refusal is **not** silent: during both imports Portal showed a modal UI dialog,
"Library test environment cannot be closed", which the owner had to confirm by hand (the step waited 50 s in
P11c and 45 s in P12c; every other step took under 9 s). The dialog did not go through the Openness
`Confirmation` event (the record has no confirmations), so an unattended worker would block on it until its
request timeout. That is a hazard of today's `plc_write` on any type test instance, independent of PR 3.
The dialog offered only OK, so the import failed regardless of the answer. Released instances
can be deleted (P7c2, P8 setups) with no dialog, so the block is specific to the test instance (inference).

Candidate routes for a run 3 (unprobed):

- P15a: delete the in-work test instance directly (confirms the cause).
- P15b/c: `ExportAsDocuments` + `ImportFromDocuments(..., ImportDocumentOptions.Override)` on the in-work
  SCL FB and UDT test instances (may update in place instead of delete + create).
- P15d: `Import(Override)` on a released instance: is the result still a type instance, and of which version?
