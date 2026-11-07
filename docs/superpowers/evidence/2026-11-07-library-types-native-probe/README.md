# Native library-types probe

Standalone .NET Framework 4.8 executable that calls the Siemens V21 library-type APIs directly, with
no worker pre-checks, to answer spec section 5 (P1–P14) of the library-types design before any product
code is written (issue #218, PR 3, Task 0). It does not use or change the MCP server. Its framework
(arguments, guards, attach, originals, per-case copies, snapshots, records, watchdog, cleanup) is the
PR 2 library-write probe's; the cases and the fixture check are new.

Every case runs on fresh copies of the disposable project and global library, taken into
`--work\<case>\`. Cases never save the project. A library copy may be saved only by the cleanup
fallback, and the record says so.

If the originals are open in the attached Portal, the probe closes them first (only when they have
no unsaved changes, otherwise it stops without changing anything) and reopens them at the end in
the mode it found them. Any other open project, or another open library below the disposable root,
stops the run before anything changes.

## Fixture roles

One standalone project (`.ap21`) and one writable user global library (`.al21`; library folder and
file share the library name), in separate folders under one disposable root. The work folder must be
new and also under that root, and the root must not be inside a git work tree. Give the project and
the library distinctive role-style names of at least 6 characters (for example `LtProbeProject`,
`LtProbeLibrary`); the probe redacts them from its output. Object names are fixed in the probe.
`--check-fixture` verifies every row on a copy and reports each as pass/fail with the reason.

| Role | Object | Requirement |
| --- | --- | --- |
| PLCs | `PLC_1`, `PLC_2` (S7-1500) | `PLC_2` holds no `LT_*` object |
| Plain UDT | `LT_Udt` in the PLC_1 types root | not a type instance |
| UDT user | `LT_UdtUser_DB` global DB in PLC_1 | has a member of type `"LT_Udt"` |
| Plain FB | `LT_Fb` in the PLC_1 blocks root | not a type instance, uses no UDT, has a block title or comment (`P12` edits it) |
| Instance DB | `LT_Fb_DB` | instance DB of `LT_Fb` |
| Caller | `LT_Caller` FC | calls `LT_Fb` with `LT_Fb_DB` |
| FB using plain UDT | `LT_FbUdt` in the PLC_1 blocks root | static or input member of type `"LT_Udt"`; not a type instance |
| Know-how protected FB | `LT_FbKhp` | optional; `P10` records `fixtureMissing` if absent |
| Global library | | writable: no read-only file, not write-protected (`P8` opens its copy read-write); no type named `LT_*` (`P8` builds its own) |
| Project library | | no type or master copy named `LT_*` |
| Baseline | | no compile error under an `LT_*` object; other PLC_1 compile errors are recorded as the baseline and do not fail the check |

The check reports the PLCs' device-item type identifiers and `LT_FbKhp` as information rows; they do
not fail it. Member and call checks read a fresh XML export of the copy. The baseline compile runs on
the copy, which is closed without saving.

## Build and offline checks

    dotnet build NativeLibraryTypesProbe.csproj -m:1
    bin\Debug\net48\NativeLibraryTypesProbe.exe --self-test
    bin\Debug\net48\NativeLibraryTypesProbe.exe --list

`--self-test` and `--list` do not attach to Portal. The self-test covers argument parsing, the path
guards (including git work tree detection, short names and repeated case IDs), the output redaction,
the case table, the crash-risk exclusion from `--all` and the one-crash-risk-case-per-run rule.

## Live run

Save the disposable project and library (they may stay open), note the Portal process ID, then run, in
this order and each with a new `--work`:

    bin\Debug\net48\NativeLibraryTypesProbe.exe --check-fixture --confirm-disposable --pid <PID> ^
        --disposable-root <root> --project <root>\<proj>\<proj>.ap21 ^
        --library <root>\<lib>\<lib>.al21 --work <root>\probe-check-1

    bin\Debug\net48\NativeLibraryTypesProbe.exe --all --confirm-disposable --pid <PID> ^
        --disposable-root <root> --project <root>\<proj>\<proj>.ap21 ^
        --library <root>\<lib>\<lib>.al21 --work <root>\probe-run-1

Use `--case P1u,P9` instead of `--all` for selected cases. Run the crash-risk cases **last and
alone**, one per run, each with its own new `--work`, and check Portal is still alive after each:

    ... --case P13b --work <root>\probe-p13b
    ... --case P14a --work <root>\probe-p14a
    ... --case P14b --work <root>\probe-p14b

Approve the Openness access dialog when Portal shows it; the probe never answers it.

Results are appended to `--work\results.jsonl` (one JSON line per event) and echoed to stdout.
Paths are reported relative to the work folder. Every emitted line has the disposable root, the user
profile path, the project and library names and the user name replaced by `<root>`, `<profile>`,
`<proj>`, `<lib>` and `<user>` (ignoring case; names only as whole words). Before any copy of
`results.jsonl` goes into `acceptance/reports/inputs`, search it for project, library, user and
customer identifiers anyway: Siemens messages can quote text the redaction does not know.

## Cases

| ID | Act |
| --- | --- |
| `P1u` | `LT_Udt`: export (setup) → `ExclusiveAccess` + `Transaction(project)` → delete → `CreateFromDocuments` into PLC_1 → link check → `CommitOnDispose`; then compile PLC_1 (P2) |
| `P1u-notx` | `P1u` without `ExclusiveAccess`/`Transaction` |
| `P1f` | `P1u` route for `LT_Fb` (block group); compile PLC_1 and `LT_Caller` |
| `P1r-after-import` | `P1u` route, deliberate failure after the link check; the transaction is disposed without commit |
| `P1r-after-delete` | `P1u` route, deliberate failure right after the delete |
| `P3` | `P1u` route for `LT_FbUdt`; if created, release its in-work version as `0.0.1` |
| `P4` | `LT_Udt` type made in PLC_2 and released `0.0.1`; export the PLC_2 instance, delete it and `CreateFromDocuments` again into PLC_2 in one transaction |
| `P5` | Version GUID and number after create, across release `0.0.1`, edit and release `0.0.2` (also P13a) |
| `P6` | Chain (released `0.0.1`, `0.0.2`); `SetAsDefault` on `0.0.1` |
| `P7a` | Chain; `LibraryType.Delete` |
| `P7b` | Chain; delete version `0.0.2` |
| `P7c` | Chain plus a project master copy of the PLC_2 instance; `LibraryType.Delete` |
| `P8` | Setup: `LT_Udt` type made in PLC_2 and released `0.0.1`, synchronized into the global library copy, then its PLC_2 instance and project type deleted. Act: instantiate the global default version into PLC_2 |
| `P9` | Chain; `UpdateLibrary` into a fresh empty library: a) initial, b) after releasing `0.0.3` and setting `0.0.1` as default, c) after releasing `0.0.4` |
| `P10` | Export `LT_FbKhp` |
| `P11` | Released `0.0.1` plus an in-work version; `Discard` it |
| `P12` | `LT_Fb` type made in PLC_2, released `0.0.1`, edited; re-import the in-work test instance's XML with only the block title changed (whitespace kept, `DocumentInfo` removed as `plc_read` does), through the worker's `update_block_logic` XML call (`Blocks.Import(file, ImportOptions.Override)`) |
| `P13b` | Crash risk. Release the new in-work version with a null number, author and comment |
| `P14a` | Crash risk. Release the new in-work version as `1.0.0` |
| `P14b` | Crash risk. Release as `0.1.0` |
| `P14c` | Release as `0.0.1` (control) |

Setup helpers: `MakeTypeInPlc2` exports the PLC_1 object and runs
`ProjectLibrary.TypeFolder.Types.CreateFromDocuments(dir, name, PLC_2 type or block group, None)`
(the PR 2-proven route). `Chain` adds release `0.0.1`, edit through the PLC_2 instance and release
`0.0.2`. Releases use `DoNotAutomaticallyCreateOrReleaseDependencies`. A setup failure is reported as
`setupError` and the act is skipped.

## What each case record holds

- `before` and `after`: open libraries under the case folder, the project library (type and version
  trees with number, GUID, state, default flag, dependencies and master copies containing instances;
  master copies), both PLCs' blocks and types with their type-version links and `instanceOfName` for
  instance DBs, and the files in the case's `out` folder and library folder.
- `nativeReturned`, `exception` (full type, message and inner chain), `failureStage`, and whether the
  exception was an `EngineeringTargetInvocationException`, a probe setup refusal or the deliberate
  rollback failure (`isDeliberateRollback`).
- `setupError` when the case's preparation step failed; the act was then skipped.
- `notes`: `steps` (each named act stage with success or exception, including the transaction's
  `Dispose`), `stateBefore`/`stateAfter` on every case (library types of the role name, PLC_1/PLC_2 presence and link, project master
  copies), `created` (`TransferResultState`, messages, created type and
  versions), `link` (does the new instance point at the created type), compile results (state,
  error and warning counts, first 20 non-success messages), `udtUserMembersAfter`, and the
  case-specific values listed in the brief (GUID comparisons for `P5`, `sync-a/b/c` for `P9`,
  `titleBefore`/`linkAfterImport` for `P12`).
- `confirmationsDuringCase`: every Portal confirmation raised, with the answer given.
- `crashRisk` on crash-risk cases.
- `cleanup` and `persisted`: how the copies were closed, and the library copy reopened read-only.

## Safety

- Refuses to run without `--confirm-disposable`, with paths outside `--disposable-root`, with an
  existing `--work`, while Portal has another project or another library below the root open, or
  while the originals have unsaved changes.
- Attaches only to the Portal given by `--pid`. It never disposes the Portal process object, only the
  attached handle.
- Never saves the project, never releases as `0.0.0`, never uses `CreateOptions.Override` and never
  calls `UpdateProject`. Compiles run outside transactions.
- Crash-risk cases are not in `--all` and are refused unless named alone in `--case`.
- Refuses a disposable root inside a git work tree, project or library names under 6 characters, and
  a case named twice in `--case`.
- Portal loss (a `NonRecoverableException` or the attached handle's `Disposed` event) stops the run
  without cleanup, and the originals are not reopened; reopen them by hand.
- If a transaction or exclusive-access `Dispose` fails, either may still be held: the probe skips the
  case's inspection and cleanup, stops the run and does not reopen the originals. Recover by hand as
  after a watchdog exit (below).
- A cleanup that cannot leave Portal idle stops the run, and the originals are not reopened
  (`originals-not-reopened` record).
- Portal confirmations are answered `No` when offered, otherwise `Cancel`, otherwise `Ok`; if none
  of those is offered the answer is left unset. Every confirmation is recorded.
- A 900-second watchdog exits only the probe (code 124); it never kills Portal. It is re-armed for
  each case, its cleanup, and the reopening of the originals.
- A watchdog exit can leave Portal in exclusive access or with an uncommitted transaction (`P1u`,
  `P1f`, `P1r-*`, `P3`, `P4`), the case copy open and the originals closed. Recover by hand: wait for
  or check that Portal is no longer in exclusive access (its UI responds), close the case copy without
  saving, then reopen the originals.
- Exit codes: 0 done, 2 stopped early, 3 fixture check failed, 1 fatal, 124 watchdog.

## Known limits

- Only the S7-1500 PLC data type and FB paths are covered: no Software Units, HMI types, other block
  kinds or nested groups.
- `P1u-notx` deletes the original without a transaction; if the import then fails, the copy keeps the
  deletion (it is never saved).
- The member, FB-call and title edits read and write SIMATIC ML XML by element name; an export
  layout that differs from that (for example no block-level `Title`/`Comment` text) makes `P12` fail
  with a probe setup exception and the fixture rows fail with a reason.
- Each act stops at its first failing stage (for example `P9` step b is not attempted when step a
  fails); `notes.steps` shows where.
