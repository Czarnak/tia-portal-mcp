# OB creation live acceptance (issue #75)

*2026-10-09. Spec: [OB creation design](../../specs/2026-10-08-ob-creation-design.md) §6. Plan:
[OB creation plan](../../plans/2026-10-08-ob-creation.md) Task 6. Candidate: `33058d8` on
`fix/ob-create-issue-75`.*

## Scope and authorization

The maintainer authorized live testing on the disposable project
`DisposableProjects/OB_Fixtures/OB_Fixtures.ap21`, installed the candidate as the local tool,
restarted the MCP server, and deleted every user OB from the project before the run. All calls
were made through the installed `tia-portal` MCP tools in read-write mode.

## Environment

- TIA Portal V21, maintainer default import/export setting SimaticSD.
- `PLC_1`: S7-1500 F-CPU. The F-runtime OB `FOB_RTG1` holds OB123, and the safety program
  holds FB1 and FC/FB numbers in the system groups.
- `PLC_2`: S7-1200. `G2_PLC_1`: S7-1200 G2 (not exercised; see Evidence limitations).
- Offline qualification on the same candidate:
  - Real-V21 build: 0 errors.
  - Full suite: 5945 passed, 14 failed. The 14 are the reference-stub artifact and
    verification-script tests plus one network subnet FakeWorker test; this branch touches none
    of their files.
  - Scoped line coverage: 93.62% against the 0.80 gate.

## Results

| Case | Call | Expected | Observed |
| --- | --- | --- | --- |
| Schema | live `plc_write` schema | `obEventClass` enum of the 15 names plus null | as expected |
| L1 | `dryRun`, 15 SCL OBs, one per class | each class plans its base number, no guards | 1, 100, 10, 20, 30, 40, 55, 56, 57, 80, 82, 83, 86, 121, 122; no guards |
| L2 | same 15, actual | applied number = planned number; verification `exists` | 15/15 succeeded, every `number` equal to the plan, `obEventClass` echoed |
| L3 | `dryRun`: second ProgramCycle (LAD, class omitted), second CyclicInterrupt (FBD), STL TimeDelayInterrupt OB, STL FB, STL FC, duplicate DiagnosticErrorInterrupt | free numbers from 123 upward, skipping the F-OB's 123; default class shown; singleton guard | planned 124, 125, 126; effect shows `obEventClass: ProgramCycle` for the omitted class; `plc_ob_singleton_exists` (block) naming `PLC_1/Blocks/LT_DiagnosticErrorInterrupt` and number 82 |
| L4 | duplicate DiagnosticErrorInterrupt, actual | blocked before anything runs | `phase: blocked`, `guard_blocked`, same guard message; no block created |
| L5 | L3 without the duplicate, actual | numbers match the plan; STL works | OB124 LAD, OB125 FBD, OB126 STL, FB2 STL, FC1 STL; FB/FC results `obEventClass: null` |
| L6 | ProgrammingError on the S7-1200, actual | fails with TIA's text | `worker_operation_failed` carrying "Cannot create an organization block of type 'ProgrammingError'."; verification observed `absent` |
| L7 | `obEventClass: "TimeDelay"`, `dryRun` | `validation_error` listing the names | "obEventClass 'TimeDelay' is not valid. Valid values: ProgramCycle, …, IOAccessError." |
| L8 | `compile_check` of `PLC_1` | clean except the HardwareInterrupt trigger warning | errors 0, warnings 1: "You have not yet defined triggers for the hardware interrupt." (OB40) |
| L9 | project tree after L8 | no OB renumbered by the compile | all 18 created OBs keep their result numbers |

## Defects and observations

- No defects found.
- The planner and the worker both skipped OB123, held by the F-runtime OB. This confirms that OBs
  TIA created itself count as used numbers.
- STL FB and FC creation, broken before this branch by `SetENOAutomatically`, now works.

## Evidence limitations

- The S7-1200 G2 was not exercised. The spike already recorded its import behaviour, and its
  class support is TIA's own import check, the same path as L6.
- No case had the project change between planning and execution. The worker re-check is covered
  only by `PlcWritePreconditionsTests`.
- No OB was created inside a software unit or a user group live. Unit and system-group OB numbers
  are covered by unit tests; the live F-OB lives in the root group.
- The created blocks were left in the disposable project.
