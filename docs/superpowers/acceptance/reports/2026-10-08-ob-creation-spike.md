# OB creation Phase 0 spike (issue #75)

*2026-10-08. Spec: [OB creation design](../../specs/2026-10-08-ob-creation-design.md) §5.*

## Setup

- TIA Portal V21, disposable project `DisposableProjects/OB_Fixtures/OB_Fixtures.ap21`: one
  S7-1500 F-CPU (`PLC_1`, safety program present, OB123 already used by the F-OB `FOB_RTG1`), the
  16 maintainer-created fixture OBs in `Program blocks/OBs`.
- Maintainer default import/export setting: **SimaticSD** (LAD) and `.scl` (SCL).
- Vehicle: a throwaway Windows PowerShell 5.1 script attached via Openness (`Assembly.LoadFrom` of
  `Siemens.Engineering.Base`/`Step7`; `Add-Type` fails with `ReflectionTypeLoadException`),
  importing generated documents with `PlcBlockComposition.Import(FileInfo, ImportOptions.None)`
  into a new `_Phase0` group. Test documents use a fixture's attribute list and the object list
  `BlockSourceGenerator` emits today. Compilation and exports used the installed `tia-portal`
  tools (`compile_check`, `plc_read`), and the existing product path used `plc_write create_block`.

## Results

| Case | Document | Import | Number at import | After full PLC compile |
| --- | --- | --- | --- | --- |
| T1 | ProgramCycle SCL, `Number=200`, no `AutoNumber` | ok | 200 | OB200 |
| T2a | ProgramCycle SCL, `Number=1` (taken), no `AutoNumber` | ok | 1 (duplicate) | renumbered OB127 |
| T2b | ProgramCycle SCL, `Number=1` (taken), `AutoNumber=true` | ok | 1 (duplicate) | renumbered OB126 |
| T2c | CyclicInterrupt SCL, `Number=30` (taken), `AutoNumber=true` | ok | 30 (duplicate) | renumbered OB130 (not 31) |
| T2d | Startup SCL, no `Number`, `AutoNumber=true` | **fails**: "'' is not a supported value for the 'Number' attribute" | — | — |
| T4a | DiagnosticErrorInterrupt, `Number=82` (taken), `AutoNumber=true` | ok | 82 (duplicate) | stays OB82; compile **error** |
| T4b | DiagnosticErrorInterrupt, `Number=82` (taken), no `AutoNumber` | ok | 82 (duplicate) | stays OB82; compile **error** |
| T6 | ProgramCycle SCL, member `<Comment>`s stripped, `AutoNumber=true` | ok | 1 (duplicate) | OB125; TIA restored the member comments |
| T7 | ProgramCycle LAD, no compile unit | ok | 1 (duplicate) | OB128; compiles, zero networks |
| T7b | ProgramCycle FBD, no compile unit | ok | 1 (duplicate) | OB129; compiles |
| T7c | ProgramCycle STL, `SetENOAutomatically` present | **fails**: "The 'SetENOAutomatically' attribute at the 'SW.Blocks.OB' object … is not supported." | — | — |
| T7d | ProgramCycle STL, no `SetENOAutomatically`, comments stripped, `Number=300` | ok | 300 | block compile ok |

Full PLC compile errors: *"The maximum (1) of Diagnostic error interrupt OBs, that can be used in a
program, has been exceeded."* and, for the maintainer's UI-created `SynchronousCycle (OB61)`,
*"No IO system is assigned to OB SynchronousCycle."* Compiling only one block reports neither
duplicates nor renumbers.

Existing product path (`plc_write create_block`, installed build) under the SimaticSD default:
FB LAD, FC SCL and GlobalDB succeed; **FB STL fails** with the same `SetENOAutomatically` error
for `SW.Blocks.FB`.

## Findings against spec §5

1. **SimaticML under SimaticSD default — passes.** Openness `Import` of SimaticML works with the
   SimaticSD default set, for the generated OBs and the existing FB/FC/GlobalDB path. The
   decision rule's SimaticSD fallback is not needed. (The SimaticML-default repeat was not run;
   it is moot because the harder case passed.)
2. **Auto-numbering — import never renumbers.** `<Number>` is mandatory; the imported block takes
   it verbatim even when taken, with or without `AutoNumber`. A full PLC compile later renumbers
   multi-instance duplicates to the lowest free number from 123 upward, ignoring the class's
   classic range. The number reported at import is therefore provisional unless the chosen
   number was free.
3. **Spelling** — `IOAccessError` (fixture review).
4. **Duplicate singleton** — import accepts it silently; only a full compile fails ("maximum (1)
   … exceeded"). Nothing at create time protects the caller.
5. **Unsupported class (S7-1200)** — **not run**: the project has no S7-1200. Inference from (4):
   import likely succeeds and the full compile reports it.
6. **Member comments** — stripped comments import fine and TIA regenerates them; TIA also fills
   the class's default block title. Fixtures can drop comments (no culture dependency).
7. **LAD/FBD body** — an OB without a compile unit imports and compiles (zero networks).
   **STL** additionally requires omitting `SetENOAutomatically`; this is an existing defect for
   FB/FC STL creation as well.

## Cleanup

The `_Phase0` group in the disposable project holds the test blocks, including a duplicate OB82
that makes the full PLC compile fail. The project is disposable and unsaved by the spike.
