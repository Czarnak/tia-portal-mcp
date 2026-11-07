# Library-types native probes — 2026-11-07

**Verdict:** evidence for planning versioned library types on `library_write` (issue #218, PR 3), not an
acceptance of any tool. 37 cases ran on one disposable standalone project and one disposable user global
library, against one UI Portal that stayed alive throughout. In-place conversion of a block into a type
works, and for the UDT it rolls back completely. In the project library Portal refused every probed type
or version delete that had instances. Portal refuses PLC-side deletes and replacements of a type's test
instance, so `plc_write` cannot change a type being edited.
`LibraryTypeVersionComposition.CreateFromDocuments` changes a type's content from SimaticML instead.
None of the three crash-risk release values killed Portal.

## Provenance and scope

- Probe: standalone net48 executable calling Siemens V21 Openness directly with no worker pre-checks. Its
  framework is the PR 2 probe's. Source, case list and record layout are in
  [inputs/2026-11-07-library-types-native-probe/](inputs/2026-11-07-library-types-native-probe/README.md).
- Runs, all against Portal process 24360, times UTC:
  - fixture check 14:47, run 1 14:47–14:53, probe build of commit `74f8b77d`: `--all`, 18 cases;
  - fixture check 18:01–18:02 after the know-how protected FB was added, then run 2 18:06–18:11, build
    `1ac94891`: 8 re-run cases;
  - run 3 18:16–18:18, build `e6fa0bd4`: 4 cases (P15);
  - run 4 18:23–18:26, build `6be429d2`: 4 cases (P16). A first attempt at 18:20 failed in every case
    before any native change because of a probe defect (below); it is not kept;
  - crash-risk cases P13b, P14a, P14b, one per run, 18:28–18:30, build `6be429d2` (executable SHA-256
    `156bef18158165f9ab1aebfca668f76236e7abac4cf7140dd18236582424c1a2`).
- Each case ran on fresh file copies of the saved fixture, closed without saving, and the originals were
  reopened after each run. No case raised an Openness `Confirmation`.
- Three cases (P11c, P12c, P15a) raised a modal Portal UI dialog that the owner confirmed by hand (details
  under "Changing a type's content").
- Raw traces, filtered to `LT_*` objects (caveat in the inputs README):
  [run 1](inputs/2026-11-07-library-types-native-probes-run1.jsonl.gz),
  [run 2](inputs/2026-11-07-library-types-native-probes-run2.jsonl.gz),
  [run 3](inputs/2026-11-07-library-types-native-probes-run3.jsonl.gz),
  [run 4](inputs/2026-11-07-library-types-native-probes-run4.jsonl.gz),
  [P13b](inputs/2026-11-07-library-types-native-probes-p13b.jsonl.gz),
  [P14a](inputs/2026-11-07-library-types-native-probes-p14a.jsonl.gz),
  [P14b](inputs/2026-11-07-library-types-native-probes-p14b.jsonl.gz), fixture checks
  [1](inputs/2026-11-07-library-types-native-probes-fixture-check-run1.jsonl.gz) and
  [2](inputs/2026-11-07-library-types-native-probes-fixture-check-run2.jsonl.gz).

Fixture, identities omitted:

- **Project**: two S7-1500 PLCs. `PLC_1` carries unrelated objects with 23 compile errors, recorded as the
  baseline (none under `LT_*`); a `PLC_2` compile reports one unrelated error. No `LT_*` type or master copy
  in the project library; `PLC_2` holds no `LT_*` object.
- **`PLC_1` objects** (all plain, none a type instance): `LT_Udt` (UDT, `a : Bool`, `b : Int`);
  `LT_UdtUser_DB` (global DB with a member of type `"LT_Udt"`); `LT_Fb` (SCL FB, `in1`/`out1`, block title);
  `LT_Fb_DB` (its instance DB); `LT_Caller` (FC calling `LT_Fb` through `LT_Fb_DB`); `LT_FbUdt` (FB with a
  static member of type `"LT_Udt"`); from run 2 `LT_FbKhp` (know-how protected FB).
- **Global library**: no `LT_*` type. Cases that need a global type build it.
- Test instances are made the PR 2 way: export the `PLC_1` object, `Types.CreateFromDocuments` with `PLC_2`
  as the target, so `PLC_2` holds the type instance.

## Results

### Conversion (`create_type`)

| ID | Act | Native result | Effect |
| --- | --- | --- | --- |
| P1u | Export `LT_Udt` → `ExclusiveAccess` + `Transaction(project)` → delete → `Types.CreateFromDocuments` into `PLC_1` → link check → commit | Returned | New `PLC_1` `LT_Udt` linked to the created type (`0.0.1`, InWork, not default). Only the link and the library type changed. `LT_UdtUser_DB` still uses `"LT_Udt"`. |
| P1u-notx | P1u without `ExclusiveAccess`/`Transaction` | Returned | As P1u. The transaction is needed only for rollback. |
| P1f | P1u route for `LT_Fb` (block group) | Returned | Linked as P1u. `LT_Fb_DB` stays an instance DB of `LT_Fb`; `LT_Caller` compiles. |
| P2 | Compile `PLC_1` after P1u/P1f | Returned | Same result as the baseline (23 errors, none under `LT_*`). |
| P1r-after-import | P1u route, deliberate failure after the import and link check; transaction disposed without commit | Rollback | State after equals state before: plain UDT back, no library type. |
| P1r-after-delete | Deliberate failure right after the delete | Rollback | As above. |
| P1n | P1u route into a nested type folder `LT_Folder/LT_Sub` made in setup | Returned | Type only in the nested folder, linked. |
| P3 | P1u route for `LT_FbUdt` (uses the plain `LT_Udt`), then release `0.0.1` with `DoNotAutomaticallyCreateOrReleaseDependencies` | Conversion returned; **release refused with an empty message** (no inner exception) | `LT_FbUdt` type stays InWork; no type for `LT_Udt`. |
| P3b | As P3, release with `AutomaticallyCreateOrReleaseDependenciesIfRequired` | Returned | **Portal made the plain `LT_Udt` a type, released it `0.0.1` and linked the `PLC_1` UDT to it**; `LT_FbUdt` released `0.0.1`. |
| P4 | Re-convert a released `PLC_2` type instance (no compile first) | Setup refused: "Inconsistent blocks and PLC data types (UDT) cannot be exported." | None. |
| P4c | P4 with `PLC_2` compiled first | Returned | **Not refused**: a second type `LT_Udt_1` (InWork `0.0.1`) is created and the `PLC_2` object (still named `LT_Udt`) relinks to it; the old `LT_Udt` type keeps no instance. |
| P10 | Export the know-how protected `LT_FbKhp` | Returned | 5,015-byte export; `IsKnowHowProtected:true`. `CreateFromDocuments` with it not tried. |

### Versions, release and defaults

| ID | Act | Native result | Effect |
| --- | --- | --- | --- |
| P5 | Version GUID and number after create, release `0.0.1`, edit, release `0.0.2` | Returned | Type GUID stable. **Version GUID changes on release** (the held wrapper follows it). In-work number prefilled: `0.0.1` after create, last release + 1 after edit. Each release becomes the default and the test instance relinks to it. |
| P6 | `SetAsDefault` on the older `0.0.1` (`0.0.2` default and linked) | Returned | Only the default flag moves; the `PLC_2` instance stays on `0.0.2`. |
| P8 | Instantiate a global version the project library lacks into `PLC_2` | Returned | **The project library gains the type with the same type and version GUIDs**; the instance links to it; the returned object reports `IsConsistent:False`. |
| P9 | `UpdateLibrary(..., DoNotDelete, CancelIfStructureConflicts, SetOnlyHigherUpdatedVersionAsDefault)` into a fresh library: a) initial, b) source default lowered to `0.0.1` after `0.0.3`, c) higher `0.0.4` default | Returned | Every missing version is copied with the source GUIDs. A new target type takes the source default. **The target default moves only to the source default, and only when its number is higher**; higher non-default copies never become default. |
| P11 | `Discard` the in-work version (released `0.0.1` exists) | Returned | In-work version removed; the test instance survives and relinks to `0.0.1`. |
| P13b | `Release(NoDeps, null, null, null)` on a new in-work version | Returned | Committed as the prefilled `0.0.1`, default. |
| P14a | Release the first version as `1.0.0` | Returned | Committed `1.0.0`, default. |
| P14b | Release the first version as `0.1.0` | Returned | Committed `0.1.0`, default. |
| P14c | Release the first version as `0.0.1` (control) | Returned | Committed `0.0.1`, default. |

P13b, P14a and P14b ran alone and last because PR 2's `T7Z` (release as `0.0.0`) killed Portal. None did;
`0.0.0` remains the only known fatal value and was not repeated.

### Deletes

| ID | Act | Native result | Effect |
| --- | --- | --- | --- |
| P7a | `LibraryType.Delete` with a `PLC_2` instance | Refused: "Failed to delete the type. LT_Udt cannot be deleted because there are instances for the following versions: 0.0.2." | None |
| P7b | Delete the default version `0.0.2` (linked) | Refused: "…cannot be deleted because it is a "default" version. To delete this version, set a different version as default and try again." | None |
| P7c | Type delete with a master copy of the `PLC_2` instance (instance still present) | Refused as P7a | None |
| P7c2 | Type delete with only the master copy holding an instance (`PLC_2` instance deleted in setup) | Refused as P7a | None |
| P7d | Delete the non-default `0.0.2` that the `PLC_2` instance links to | Refused: "LT_Udt (0.0.2) cannot be deleted because there are still instances of the type in the project." | None |

### Changing a type's content

| ID | Act | Native result | Effect |
| --- | --- | --- | --- |
| P12 | `Blocks.Import(file, ImportOptions.Override)` (the `plc_write update_block_logic` XML call) on the edited, uncompiled `LT_Fb` test instance | Setup export refused (inconsistent) | None |
| P12c | P12 with `PLC_2` compiled first | **Refused** ("Error when calling method 'Delete' of type FB", no inner) **after a modal UI dialog** | None |
| P11c | `Types.Import(file, ImportOptions.Override)` (the `update_type_content` XML call) with an added member on the edited, compiled `LT_Udt` test instance | **Refused** (`PlcStruct.Delete`) **after the same dialog** | None |
| P15a | Delete the edited, compiled `LT_Udt` test instance directly | **Refused** (`PlcStruct.Delete`) after the dialog | None |
| P15b | `ExportAsDocuments` + `ImportFromDocuments(..., ImportDocumentOptions.Override)` of the unchanged `LT_Fb` test instance into its own group | Returned `State:Failure`: "Import failed because an object with the name 'LT_Fb' already exists in the plc." No dialog | None |
| P15c | P15b for `LT_Udt` | Same failure | None |
| P15d | `Types.Import(Override)` with an added member onto a released, not edited instance | Returned | **The instance is unlinked**: `PLC_2` holds a plain UDT with the new member; the type keeps `0.0.1` with no instance. |
| P16a | `type.Versions.CreateFromDocuments(dir, name, CreateOptions.Override, LibraryImportOptions.None)` from the edited, compiled `LT_Udt` test instance's export plus a member | Returned `Success` | In-work `0.0.2` replaced by a new in-work `0.0.2` (**new GUID**); the `PLC_2` test instance links to it and carries the member; release `0.0.2` keeps it. |
| P16b | `Versions.CreateFromDocuments(..., PLC_2 type group, CreateOptions.None, ...)` with only released `0.0.1` | Returned `Success` | New in-work `0.0.2` with the member; the existing `PLC_2` instance becomes its test instance; release keeps the member. |
| P16c | P16b without a test environment | Returned `Success` | As P16b. |
| P16d | P16a for `LT_Fb` with an `Input` member | Returned `Success` | As P16a (`CodeBlockLibraryTypeVersion`). |

The dialog, quoted from the owner's screenshot of P15a (the owner described the P11c/P12c dialogs as
"Library test environment cannot be closed"; their exact text was not captured):

> **Cannot delete or replace (0026:000074)**: The test environment of the library cannot be deleted or
> replaced. The test environment for LT_Udt V 0.0.2 cannot be deleted or replaced because a test instance
> cannot be deleted or replaced.

The P15a dialog offered only OK (so did the earlier ones, per the owner), and none raised the Openness
`Confirmation` event. The P11c and P12c imports
waited about 50 and 45 s for the owner; every other step took under 12 s. An unattended caller blocks until
someone confirms it or the caller times out.

PR 2 probed the same version call: `T3` (`CreateOptions.None` while a version is in work) was refused, and
`T4` (`Override` with a test environment in a different PLC than the current test instance) replaced the
in-work version, deleted the old test instance and created a new one in the given PLC. P16a and P16d passed
no test environment and the existing test instance was kept.

### Cross-cutting

- **Consistency.** Objects placed by library operations (conversion, instantiate, edit, release, version
  creation) are inconsistent until compiled, and `Export` refuses inconsistent objects: `plc_read` fails on
  them until a compile. `Release` and `Delete` do not need consistency.
- **Folder arguments.** `LibraryTypeVersionComposition.CreateFromDocuments` rejected a `DirectoryInfo`
  returned by `Directory.CreateDirectory` as "The argument 'dirInfo' cannot be a relative path": on .NET
  Framework its `OriginalPath` is only the leaf name. `new DirectoryInfo(fullPath)` works.
- **Probe defects found and fixed during the runs** (no effect on any native observation): run 1 recorded
  `failureStage:"ExclusiveAccess.Dispose"` for the two deliberate rollbacks (a successful dispose relabelled
  the stage; fixed for run 2); the first run 4 attempt used the `DirectoryInfo` above; P15c's `.s7dcl` edit
  did not apply (members are quoted), which did not matter because the import failed first.

## Consequences for the library-types spec

- `create_type` route (c), export → delete → `CreateFromDocuments` in one project transaction, is confirmed
  for UDT and FB; rollback, the no-transaction variant and a nested folder were shown for the UDT. An
  uncompiled source fails safely at export.
- `create_type` must refuse a source that is already a type instance (P4c): Portal creates a second type.
- Releases use `DoNotAutomaticallyCreateOrReleaseDependencies`. Automatic mode creates and releases types the
  caller did not name (P3b); the plain refusal has an empty message (P3).
- A missing release number takes Portal's prefilled number (P13b); `1.0.0` and `0.1.0` are valid first
  numbers. The version GUID changes on release and on `Override`: in-work GUIDs are temporary.
- Deletes need no tool check in the probed cases: Portal refused project-library type deletes with PLC
  instances or a master copy holding one, a version delete with a PLC instance, and the default version
  while another version exists (P7a–P7d); the only version deletes the type (PR 2 `T11`). Discarding an
  in-work version with a released version and no content change keeps its instance (P11).
- The planned edit → `plc_write` → release flow does not work (P11c, P12c, P15a–c). Changing a type's
  content goes through `Versions.CreateFromDocuments`, `None` without an in-work version and `Override` with
  one, with no test environment argument (P16a–d, T4).
- Outside PR 3: `plc_write` content updates on type instances either fail behind a modal dialog (test
  instance) or silently unlink the instance (released instance, P15d).

## Limits

- One project, one global library, one Portal, V21 only. UDT and SCL FB sources only; FC, LAD/FBD bodies and
  know-how protected conversion were not tried.
- No compile was run after rollbacks; P2 compiled only `PLC_1` and `LT_Caller`.
- `Versions.CreateFromDocuments` was tried only when a type instance already existed in `PLC_2`; behavior with
  no instance anywhere, with global libraries and with content that does not match the type is unprobed.
- Release numbers below an existing version, out-of-range parts and the range Portal checks were not
  probed. Synchronize was probed into a fresh library only.
- Deletes were probed in the project library only; a version held only by a master copy was not tried.
  Discard was tried only with a released version and no content change.
- `Versions.CreateFromDocuments` with `Override` but no in-work version, and with instances in several
  PLCs, was not tried.
- The dialog was observed through the owner, not recorded by the probe; its exact text is from P15a only.
