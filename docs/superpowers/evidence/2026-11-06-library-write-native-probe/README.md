# Native library-write rejection probe

Standalone .NET Framework 4.8 executable that calls the Siemens V21 library APIs directly, with no
worker pre-checks, to see which `library_write` conditions Portal rejects natively and which it
accepts silently. It runs before the `library_write` plan (issue #218, PR 2). It does not use or
change the MCP server.

Every case runs on fresh copies of a pristine project and global library, taken into
`--work\<case>\`. The originals are only copied, never opened. Cases never save the project.
A library copy may be saved only by the cleanup fallback, and the record says so.

## Fixture

Build both in the TIA Portal UI, save them, then close them. Use these exact names; the
`--check-fixture` run verifies all of them.

**Project** (any name, in its own folder):

- Two S7-1500 PLCs whose software is named `PLC_1` and `PLC_2`. `PLC_2` holds no `LP_*` object.
- In `PLC_1`: UDT `LP_Udt` (one member is enough) and FB `LP_Fb` with an input of type `"LP_Udt"`.
  Add both to the project library as types and release them as `1.0.0`, so `PLC_1` holds instances
  of `LP_Udt 1.0.0` and `LP_Fb 1.0.0`, with LP_Fb depending on LP_Udt.
- Also in `PLC_1`, as plain objects (not types): FB `LP_Free`, UDT `LP_PlainUdt`, and UDT `LP_GUdt`.
- Project library master copies: `LP_Free` (drag the block in) at the root.
- Project library types: only `LP_Udt` and `LP_Fb`, each with only version `1.0.0`, at the root.

**Global library** `LP_ProbeLib\LP_ProbeLib.al21` (in a folder separate from the project):

- Types at the root: `LP_Udt` and `LP_Fb`, the same types as in the project (update the global
  library from the project library), only `1.0.0`.
- Type folder `LP_TypeFolder` with types `LP_GUdt` (UDT) and `LP_GFb` (FB with an input of type
  `"LP_GUdt"`), only `1.0.0`, released. These must not exist in the project library. One way:
  create them in a scratch project, release them, update `LP_ProbeLib`, then discard the scratch project.
- Master copies: `LP_Free` at the root, and folder `LP_McFolder` with at least one master copy inside.

Keep the project and library in separate folders under one disposable root. The work folder
must be new and also under that root.

## Build and offline checks

    dotnet build NativeLibraryProbe.csproj -m:1
    bin\Debug\net48\NativeLibraryProbe.exe --self-test
    bin\Debug\net48\NativeLibraryProbe.exe --list

`--self-test` and `--list` do not attach to Portal.

## Live run

Start TIA Portal V21 with **no project open**, and note its process ID. Then:

    bin\Debug\net48\NativeLibraryProbe.exe --check-fixture --confirm-disposable --pid <PID> ^
        --disposable-root <root> --project <root>\<proj>\<proj>.ap21 ^
        --library <root>\LP_ProbeLib\LP_ProbeLib.al21 --work <root>\probe-check-1

    bin\Debug\net48\NativeLibraryProbe.exe --all --confirm-disposable --pid <PID> ^
        --disposable-root <root> --project <root>\<proj>\<proj>.ap21 ^
        --library <root>\LP_ProbeLib\LP_ProbeLib.al21 --work <root>\probe-run-1

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
  existing `--work`, or while Portal has a project open or a library below the root open.
- Portal confirmations are answered `No` when offered, otherwise `Cancel`, otherwise `Ok`, and
  recorded. This affects `G9` (close of a dirty library): read its `confirmationsDuringCase`.
- A 900-second watchdog per case exits only the probe (code 124); it never kills Portal.
- Portal loss stops the run without cleanup. A cleanup that cannot leave Portal idle stops the run.
- Exit codes: 0 done, 2 stopped early, 3 fixture check failed, 1 fatal, 124 watchdog.

## Known limits

- Software Units, HMI types, `HarmonizeProject`, `CleanUpLibrary` and structure-conflict
  synchronization are not covered.
- `T6` only releases the dependent if editing `LP_Udt` created an in-work `LP_Fb`; otherwise its
  note `dependentInWorkCreated:false` is the observation.
- `T10`, `T18` and `U1` need a native edit-and-release of `LP_Udt` to `1.1.0` as setup. If that
  fails, the case reports `setupError` and the act is skipped.
