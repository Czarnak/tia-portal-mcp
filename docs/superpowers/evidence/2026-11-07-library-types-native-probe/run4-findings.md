# Run 4 findings (2026-11-07)

Run `lt-run-4b`: `--case P16a,P16b,P16c,P16d`, same fixture. Fresh copies, closed without saving; no Openness
confirmations, no UI dialog (no step over 12 s). The first attempt (`lt-run-4`) failed in every case before any
change with "The argument 'dirInfo' cannot be a relative path": the probe passed the `DirectoryInfo` returned by
`Directory.CreateDirectory`, whose `OriginalPath` on .NET Framework is only the leaf name, and Openness reads it.
**The worker must pass `new DirectoryInfo(fullPath)` to Openness.**

All four cases use `LibraryTypeVersionComposition.CreateFromDocuments` on the project type with a SimaticML export
of the PLC_2 instance (compiled first), a member `probeAdded : Int` added and `DocumentInfo` removed.

## Answers

| ID | Result | Evidence (record fields) | Design consequence |
| --- | --- | --- | --- |
| P16a | `CreateOptions.Override` with an in-work `0.0.2` (edited test instance): `TransferResultState:Success`. The in-work version is replaced by a new in-work `0.0.2` (**new GUID**: `4d36de0d…` → `44fe4b0a…`), the PLC_2 test instance relinks to it and carries the member. Release `0.0.2` keeps the member. | `versionCreate`, `versionsAfterCreate`, `linkAfterCreate`, `contentAfterCreate`/`AfterRelease` | **The edit route.** An in-work content change is one native call with no test-instance delete and no dialog. |
| P16b | `CreateOptions.None` with test environment PLC_2, only released `0.0.1`: new in-work `0.0.2` with the member; the existing PLC_2 instance becomes its test instance. Release `0.0.2` keeps it. | same | Content → new in-work version without a separate edit step. |
| P16c | As P16b without a test environment: identical result, the existing PLC_2 instance still relinks to the new in-work version. | same | The test environment argument is optional when an instance exists (behavior without any instance unprobed). |
| P16d | P16a for the SCL FB with an `Input` member: same result (`CodeBlockLibraryTypeVersion`, new in-work GUID, member in the test instance and in the released `0.0.2`). | same | Works for blocks as for UDTs. |

Created versions take the next number (`0.0.2`) and `isDefault:false`; `ModifiedDate` reads `1.01.0001` until
released. The in-work GUID changes on Override and again on release, so every in-work GUID is temporary.

## Consequences for the spec

- Replace the edit → `plc_write` → release flow with a version-from-content operation (`Versions.CreateFromDocuments`,
  `None` when no in-work version exists, `Override` when one does), taking SimaticML content as `plc_read` returns it.
- `plc_write` content updates on type instances need a guard: on a test instance they fail and raise a modal UI
  dialog (run 2, P15a); on a released instance they silently unlink it (P15d).
