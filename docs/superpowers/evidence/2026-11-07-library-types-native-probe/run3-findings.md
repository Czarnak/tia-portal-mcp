# Run 3 findings (2026-11-07)

Run `lt-run-3`: `--case P15a,P15b,P15c,P15d`, same fixture. Fresh copies, closed without saving; no Openness
confirmations. One modal UI dialog appeared (P15a), confirmed by the owner with its only button, OK:

> **Cannot delete or replace (0026:000074)**: The test environment of the library cannot be deleted or
> replaced. The test environment for LT_Udt V 0.0.2 cannot be deleted or replaced because a test instance
> cannot be deleted or replaced.

## Answers

| ID | Result | Evidence (record fields) | Design consequence |
| --- | --- | --- | --- |
| P15a | Deleting the in-work test instance directly is refused (`EngineeringTargetInvocationException` on `PlcStruct.Delete`, no inner) and raises the dialog above. Nothing changed. | `exception`, `stateAfter` | Confirms run 2's cause: Portal never deletes or replaces a test instance. |
| P15b | `ImportFromDocuments(Override)` of the unchanged `LT_Fb` documents into its own group returns `State:Failure`: "Import failed because an object with the name 'LT_Fb' already exists in the plc." No exception, no dialog; instance still linked to in-work `0.0.2`. | `sdImport`, `linkAfterImport` | The document route does not replace a test instance either. Whether its Override replaces a plain object is unprobed. |
| P15c | Same failure for `LT_Udt` (`State:Failure`, "already exists"). The `.s7dcl` edit did not apply (members are quoted, `"b" : Int;`), irrelevant since the import failed. Release `0.0.2` then committed the unchanged content. | `sdImport`, `sdEditApplied`, `contentAfterRelease` | As P15b. |
| P15d | `Import(Override)` with an added member onto a **released** (not edited) instance succeeds but **unlinks** it: the PLC_2 UDT becomes a plain UDT with the new member; the type keeps `0.0.1` with no instance. No dialog. | `linkAfterImport:null`, `contentAfterImport`, `stateAfter` | Today's `plc_write` silently turns a type instance into a plain object. Hazard independent of PR 3. |

Not tried before this run: `LibraryTypeVersionComposition.CreateFromDocuments(dir, name, [testEnvironment],
CreateOptions, LibraryImportOptions)`, documented as "Creates a new LibraryTypeVersion from document(s)";
`CreateOptions.Override` "overwrite[s] the current InWork/In-Test version". Run 4 (P16a-d) probes it.
