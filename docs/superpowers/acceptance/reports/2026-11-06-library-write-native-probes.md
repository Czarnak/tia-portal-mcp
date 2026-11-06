# library_write native-rejection probes — 2026-11-06

**Verdict:** evidence for planning `library_write` (issue #218, PR 2), not an acceptance of any
tool. 52 of 53 cases produced a record on one disposable standalone project and one disposable user
global library. 30 refused natively with `EngineeringTargetInvocationException`. 22 returned without
an exception, several with silent or destructive effects. One case (`T7Z`, release as `0.0.0`)
killed TIA Portal. It ran once and is excluded from `--all`.

## Provenance and scope

- Probe: standalone net48 executable calling Siemens V21 Openness directly with no worker
  pre-checks. Source, case list and case-record layout are in
  [inputs/2026-11-06-library-write-native-probe/](inputs/2026-11-06-library-write-native-probe/README.md).
- Runs, all against one UI Portal, all times UTC:
  - fixture check, 18:05–18:06, Portal 1, probe build of commit `77c25089`: 15/15 checks passed;
  - run 1, 18:06–18:14, the same Portal and build: cases G1–T7Z. Portal died at `T7Z`;
  - run 2, 18:18–18:24, Portal 2 (restarted), probe build of commit `81b50158`, executable SHA-256
    `ef7e17fa93d443bc21f431658c68b27105fbf8e89db1cb8fce5994c56e471323`: cases T7–U1.
  - Run 2 changed only the `T7` setup (it releases build+2 so that build+1 is a valid lower number)
    and the probe's own reopen and hazard handling. Every other native call is unchanged.
- Each case ran on fresh file copies of the saved fixture. Cases never saved the project, so
  every case started from the same baseline.
- Raw traces:
  [fixture check](inputs/2026-11-06-library-write-native-probe-fixture-check.jsonl.gz),
  [run 1](inputs/2026-11-06-library-write-native-probes-run1.jsonl.gz) and
  [run 2](inputs/2026-11-06-library-write-native-probes-run2.jsonl.gz). In run 1 the crashed case
  is recorded under id `T7`; it is `T7Z` in this report and in the current probe.
- Portal crash evidence: Portal's own error report (`.dr`) for the T7Z crash. It stays outside the
  repository; the relevant lines are quoted under `T7Z`.

Fixture, identities omitted:

- **Project**: one standalone project with two S7-1500 PLCs, `PLC_1` (many library-type
  instances) and `PLC_2` (almost empty), plus HMI and drive devices.
- **Project library**: several type folders and two root master copies.
- **Global library**: one user global library whose types mirror the project library (same
  GUIDs), with no master-copy folders.
- **Type roles**:
  - leaf UDT type `UDT_TIME`, which `UDT_COUNTER_BUFFER` depends on, which `WorkTimeCounter`
    (an FB) depends on, all at one released version `0.0.1`;
  - plain FB `FB_CALENDAR` and plain UDT `CALENDAR_CTRL`;
  - global type `InvertBit`, which nothing depends on.

Not covered: Software Units, HMI types, `HarmonizeProject`, `CleanUpLibrary`, structure-conflict
synchronization, `SaveAs`, multi-object documents, local sessions and Project Server projects.

## Results

"Refused" means a native `EngineeringTargetInvocationException`, with Portal's message quoted after
the first line. "Returned" means no exception; the effect is taken from the probe's independent
before/after snapshots and, for global libraries, the copy reopened read-only after the case.

### Global library lifecycle

| Case | Request | Native result | Observed effect |
| --- | --- | --- | --- |
| G1 | `Create` where the destination folder exists | Refused: "The library cannot be created in the selected folder because <path> is not empty." | None |
| G2 | `Create` with name `LP:Bad` | Refused: "The combination of directory and library name was invalid." | None |
| G3 | `Open` the same file again, same mode | Returned | The already-open library is returned (`Equals` true) |
| G4 | `Open` the same file again, other mode | Refused: "Cannot change the open mode of an already open global library." | None |
| G5 | `Open` a second copy with the same name from another path | Returned | Two open libraries share one name |
| G6 | `Retrieve` into a folder that already holds a file | Returned | Library retrieved into a new `<name>` subfolder; the existing file kept |
| G7 | `Retrieve` into a folder that does not exist | Returned | Folder created, library retrieved |
| G8 | `Save` a library opened read-only | Refused: "Cannot save a ReadOnly global library." | None |
| G9 | `Close` with unsaved changes | Returned | **Changes discarded.** No confirmation event; the reopened copy lacks the new folder |
| G10 | `Archive` with unsaved changes | Refused: "Operation is not possible while user global library has unsaved changes, please save the library before performing 'Archive'" | None |
| G11 | `Archive` twice to the same name | Refused: "Archive Operation is not possible as the target path already has a folder/file with target name 'dup.zal21'." | First archive kept |
| G12 | `Archive` into the library's own folder | Refused: "Archiving the global library failed. A project directory that already exists cannot be saved." | None |
| G13 | `Archive` with name `noext` | Returned | File written as `noext`; **no `.zal21` appended** (as for project archives) |

### Folders

| Case | Request | Native result | Observed effect |
| --- | --- | --- | --- |
| F1 | Create master-copy folder over an existing name | Refused: "A folder with name: LP_McFolder already exists in folder: Master copies." | None |
| F2 | Same, differing only in case | Refused with the same message | None. Folder names are case-insensitive |
| F3 | Create type folder over an existing name | Refused: "A folder with name: UDT already exists in folder: Types." | None |
| F4 | Create folder in a read-only library | Refused: "Cannot write to read-only libraries." | None |
| F5 | Delete a master-copy folder that holds a master copy | Returned | **Folder and contents deleted** |
| F6 | Delete a type folder whose types others depend on | Refused: "WorkTimeCounter cannot be deleted because other types depend on the following versions: 0.0.1." | None |

### Master copies

| Case | Request | Native result | Observed effect |
| --- | --- | --- | --- |
| M1 | `MasterCopies.Create` from a PLC block whose name exists in the folder | Returned | **Created as `FB_CALENDAR_1`** |
| M2 | `CreateFrom` across libraries onto an existing name | Returned | **Created as `FB_CALENDAR_1`** |
| M3 | `CreateFrom` into its own folder | Returned | **Created as `FB_CALENDAR_1`** |
| M4 | `Create` in a read-only library | Refused: "Cannot write to read-only libraries." | None |
| M5 | PLC `Blocks.CreateFrom(mc, ThrowIfExists)` over an existing block | Refused: "Copying from the library failed." | None |
| M6 | PLC `Blocks.CreateFrom(mc)` (no mode) over an existing block | Returned | **Block created as `FB_CALENDAR_1`** in PLC_1 |
| M7 | FB master copy into a PLC type composition | Refused: "The copy cannot be created in the target location." | None |
| M8 | Global master copy from a library-type instance block | Returned | Master copy `WorkTimeCounter` created; global type versions unchanged |

### Types and versions

| Case | Request | Native result | Observed effect |
| --- | --- | --- | --- |
| T1 | `Types.CreateFromDocuments` with an existing type's name (target PLC_2) | Returned: "The type has been created successfully." | **Second type `UDT_TIME`** at the type root with a new GUID and an in-work version; PLC_2 gets a test instance |
| T2 | `CreateFromDocuments` where the target PLC already holds that object | Refused: "The import failed. An instance with the same name already exists." | None |
| T3 | `Versions.CreateFromDocuments(CreateOptions.None)` while a version is in work | Refused: "Creating a new type version has failed because the type is currently 'in test' or 'in work'" | None |
| T4 | Same with `CreateOptions.Override` | Returned | **In-work version replaced** (new GUID); PLC_1's test instance deleted, a new one created in PLC_2 |
| T5 | `Edit` the released version while one is in work | Refused: "Parallel editing is not supported." | None |
| T6 | Edit `UDT_COUNTER_BUFFER`, then look for an in-work dependent | Returned | No in-work `WorkTimeCounter` was created; there was nothing to release |
| T7 | Release build+1 while build+2 is the released default | Returned | **Accepted; the lower number became the default.** The PLC_1 instance moved to it |
| T7Z | Release as `0.0.0` | **`NonRecoverableException`; Portal process terminated** | Portal's report: "The value 0.0.0 is not allowable / Parameter name: build" from `CheckVersionValuesAreWithinRange` |
| T8 | Release with the same number as the existing release | Returned | **Two versions numbered `0.0.1`**; the new one is default |
| T9 | `SetAsDefault` on an in-work version | Refused: "Unreleased version cannot be set as default type version" | None |
| T10 | Delete the default version after a second release | Refused: "UDT_TIME (0.0.2) cannot be deleted because other types exist which are dependent on this version." | None. The trace's version dependency lists do not show which type depends on 0.0.2 |
| T11 | Delete the only version of a global type with no dependents | Returned | **Whole type removed** |
| T12 | Delete a project type with PLC instances and dependents | Refused: "WorkTimeCounter cannot be deleted because other types depend on the following versions: 0.0.1." | None |
| T13 | Delete that type's version | Refused: "WorkTimeCounter (0.0.1) cannot be deleted because other types exist which are dependent on this version." | None |
| T14 | Delete a global type that others depend on | Refused: "UDT_TIME cannot be deleted because other types depend on the following versions: 0.0.1." | None |
| T15 | `UpdateLibrary` into the same library | Refused: "The target library is the same as the source library." | None |
| T16 | `UpdateLibrary` global→project with `NoDefaultVersionChange` | Refused: "The selected value for ForceUpdateMode may only be used when updating the project library." | None |
| T17 | `UpdateLibrary` of `WorkTimeCounter` into an empty global library | Returned | **All dependencies copied too** (`UDT_COUNTER_BUFFER`, `UDT_WORK_CNT`, `UDT_TIME`), with the source folder structure |
| T18 | `UpdateLibrary` into a read-only global library | Refused: "Cannot write to read-only libraries." | None |
| T19 | Instantiate with `ThrowIfPathsConflict` where an instance exists | Refused: "Cannot instantiate UDT_COUNTER_BUFFER V 0.0.1: an instance exists in the target device in another path." | None |
| T20 | Instantiate the FB type version into a PLC lacking its UDTs | Returned | **Dependencies instantiated too**, under the library folder path (`UDT/HMI`) |
| T21 | Instantiate a global type version the project library already holds | Returned | Instance created in PLC_2; project library unchanged |
| T22 | Instantiate over a same-named PLC_1 data type with no `LibraryTypeInstanceInfo` | Refused: "Cannot instantiate PLC_TT_StatusLog_2019 V 0.0.6: an instance exists in the target device in another path." | None |
| T23 | `Discard` a released version | Refused: "A released version cannot be discarded." | None |
| T24 | Delete a type in a read-only library | Refused: "Cannot write to read-only libraries." | None |

### Project update

| Case | Request | Native result | Observed effect |
| --- | --- | --- | --- |
| U1 | `UDT_COUNTER_BUFFER.UpdateProject(PLC_1, DoNotDelete, RetainStructure, NoDefaultVersionChange)` after releasing 0.0.2 | Returned | **56 library-type instances removed from PLC_1** (21 blocks, 35 data types), including the updated type's own instance, `WorkTimeCounter` and unrelated types. 13 linked objects and every unlinked object survived. Project library unchanged |

## Consequences for the library_write plan

Portal is a sufficient refusal authority ([ADR-0012](../../../adr/0012-portal-as-rejection-authority.md))
for the refused rows above: read-only writes, folder collisions (case-insensitive), open-mode
change, read-only save, archive of a dirty library, archive over an existing file or into the
library's own folder, deletes blocked by dependents, same-library update, `NoDefaultVersionChange`
in library sync, unreleased default, discard of a released version, parallel edit, and
instantiation path conflicts.

Portal does not refuse these, so the tool must own them:

1. **Version number validation.** `0.0.0` kills Portal. Same and lower numbers are accepted, and a
   lower release becomes the default. Validate before calling `Release`.
2. **Dirty close.** `Close` discards unsaved changes silently, so close needs an explicit
   save/discard choice.
3. **Master-copy name collisions.** `MasterCopyComposition.Create`/`CreateFrom` have no mode
   argument and auto-rename. They need a collision check or a reported actual name. PLC
   instantiation should use the `ThrowIfExists` overload.
4. **Type creation from documents.** It creates duplicate-name types. Never use
   `CreateOptions.Override`, which deletes the existing test instance.
5. **Folder deletion.** A non-empty master-copy folder is deleted with its contents.
6. **Archive name.** The `.zal21` extension is not appended (same as project archives).
7. **Dependency closure.** `UpdateLibrary` and type-version instantiation pull dependencies in
   implicitly, so a preview must show the closure.
8. **`UpdateProject`.** `DoNotDelete` does not prevent removal of unused type instances across the
   whole PLC. It needs a transaction with an inventory check, or it stays out of v1. This matches
   the contributor's report on issue #218.
9. **Duplicate library names.** Two open libraries can share a name, so selectors need the path.

## Limits

One fixture, one Portal installation (V21), one machine. The probe's own setup steps (edit,
release) are native calls whose success is assumed by later steps. Their failures would be recorded
as setup errors, and none occurred. `T7Z` was not repeated. No compile was run after any case.
