# Native library-write rejection probe

Standalone .NET Framework 4.8 executable that calls the Siemens V21 library APIs directly, with no
worker pre-checks, to see which `library_write` conditions Portal rejects natively and which it
accepts silently. It runs before the `library_write` plan (issue #218, PR 2). It does not use or
change the MCP server.

Every case runs on fresh copies of the disposable project and global library, taken into
`--work\<case>\`. Cases never save the project. A library copy may be saved only by the cleanup
fallback, and the record says so.

If the originals are open in the attached Portal, the probe closes them first (only when they have
no unsaved changes, otherwise it stops without changing anything) and reopens them at the end in
the mode it found them. Any other open project, or another open library below the disposable root,
stops the run before anything changes.

## Fixture roles

The cases are mapped onto an existing disposable project and a global library that mirrors its
project library (same type GUIDs). `--check-fixture` verifies every assumption below on a copy.

| Role | Object | Assumed |
| --- | --- | --- |
| PLCs | `PLC_1`, `PLC_2` | `PLC_2` holds none of the objects below |
| Leaf UDT type | `UDT_TIME` | no dependencies; PLC_1 instance |
| UDT type | `UDT_COUNTER_BUFFER` | depends on `UDT_TIME`; PLC_1 instance |
| FB type | `WorkTimeCounter` | depends on `UDT_COUNTER_BUFFER`; PLC_1 instance |
| Lone global type | `InvertBit` | one version, no dependents |
| Plain FB | `FB_CALENDAR` | not a type instance; master copy at both library roots |
| Plain UDT | `CALENDAR_CTRL` | not a type instance |
| Global type folders | `05_HMI` (not empty), `UDT` | |
| Master-copy folder | `LP_McFolder` | absent; cases that need it create it first |

Each of the three types has exactly one released default version in both libraries. Version numbers
are taken from that default: setup releases use the next build number, `T7` the one below, `T8` the
same one. The library folder and file must share the library name. Keep the project and library in
separate folders under one disposable root; the work folder must be new and also under that root.

## Build and offline checks

    dotnet build NativeLibraryProbe.csproj -m:1
    bin\Debug\net48\NativeLibraryProbe.exe --self-test
    bin\Debug\net48\NativeLibraryProbe.exe --list

`--self-test` and `--list` do not attach to Portal.

## Live run

Save the disposable project and library (they may stay open), note the Portal process ID, then:

    bin\Debug\net48\NativeLibraryProbe.exe --check-fixture --confirm-disposable --pid <PID> ^
        --disposable-root <root> --project <root>\<proj>\<proj>.ap21 ^
        --library <root>\<lib>\<lib>.al21 --work <root>\probe-check-1

    bin\Debug\net48\NativeLibraryProbe.exe --all --confirm-disposable --pid <PID> ^
        --disposable-root <root> --project <root>\<proj>\<proj>.ap21 ^
        --library <root>\<lib>\<lib>.al21 --work <root>\probe-run-1

Use `--case G9,T17` instead of `--all` for selected cases. Every run needs a new `--work`.
Approve the Openness access dialog when Portal shows it; the probe never answers it.

Results are appended to `--work\results.jsonl` (one JSON line per event) and echoed to stdout.
Paths are reported relative to the work folder.

## What each case record holds

- `before` and `after`: open libraries under the case folder (state flags plus folder, master copy,
  type and version trees), the project library, both PLCs' blocks and types with their type-version
  links, and the files in the case's `out` folder and the library folder.
- `nativeReturned`, `exception` (full type, message and inner chain), `failureStage`, and whether the
  exception was an `EngineeringTargetInvocationException`.
- `setupError` when the case's preparation step failed; the act was then skipped.
- `notes`: returned objects (for example `TypeCreateTransferResults`) and case-specific observations.
- `confirmationsDuringCase`: every Portal confirmation raised, with the answer given.
- `cleanup` and `persisted`: how the copies were closed, and the library copy reopened read-only to
  show what reached disk (for example whether `G9`'s close saved the unsaved folder).

## Safety

- Refuses to run without `--confirm-disposable`, with paths outside `--disposable-root`, with an
  existing `--work`, while Portal has another project or another library below the root open, or
  while the originals have unsaved changes.
- If Portal is lost mid-run the originals are not reopened; reopen them by hand.
- Portal confirmations are answered `No` when offered, otherwise `Cancel`, otherwise `Ok`, and
  recorded. This affects `G9` (close of a dirty library): read its `confirmationsDuringCase`.
- A 900-second watchdog per case exits only the probe (code 124); it never kills Portal.
- Portal loss stops the run without cleanup. A cleanup that cannot leave Portal idle stops the run.
- Exit codes: 0 done, 2 stopped early, 3 fixture check failed, 1 fatal, 124 watchdog.

## Known limits

- Software Units, HMI types, `HarmonizeProject`, `CleanUpLibrary` and structure-conflict
  synchronization are not covered.
- `T7Z` (release as `0.0.0`) crashed Portal in the first run; Portal's error report shows
  `The value 0.0.0 is not allowable, Parameter name: build` thrown inside
  `CheckVersionValuesAreWithinRange`. `--all` skips it; name it in `--case` to reproduce.
- The global library holds no type that the project library lacks, so `T17` synchronizes into a
  fresh empty library it creates, and `T21` instantiates a global version the project library
  already holds.
- `T6` only releases the dependent if editing `UDT_COUNTER_BUFFER` created an in-work
  `WorkTimeCounter`; otherwise its note `dependentInWorkCreated:false` is the observation.
- `T22` picks the first unlinked `PLC_1` data type whose name matches exactly one global type; if
  none exists its note `namesake:null` is the observation.
- `T10`, `T18` and `U1` need a native edit-and-release to the next build number as setup. If that
  fails, the case reports `setupError` and the act is skipped.
