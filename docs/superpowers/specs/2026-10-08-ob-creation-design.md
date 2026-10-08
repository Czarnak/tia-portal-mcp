# `create_block` OB creation — design (issue #75)

*2026-10-08. Branch `fix/ob-create-issue-75`. Status: approved for planning; Phase 0 spike gates
implementation.*

## 1. Intent

`plc_write` `create_block` with `blockType:"OB"` fails for every event class (issue #75). Fix it so
an agent can create any user-creatable OB type by naming its event class, without learning OB
numbering rules, and without the tool description carrying those rules.

**In scope:** OB creation through `create_block`; the `obEventClass` value set; a singleton guard;
the assigned number in the result.

**Out of scope (decided):**

- Renumbering any block (`set_block_number`). Logged as a follow-up in `docs/IMPROVEMENT_LOG.md`.
- Caller-chosen OB numbers. TIA's own auto-numbering assigns the lowest free valid number.
- Event-specific parameters (CyclicInterrupt `CyclicTime`/`PhaseOffset`, priority, time-of-day
  schedule). Created OBs use TIA defaults. Logged as a follow-up.
- Motion Control (`MC_*`), redundancy (OB 70/72) and ProDiag OBs: TIA creates these itself
  (technology objects, R/H CPUs, compiler), so they are not offered.

**Success:** every offered event class creates an OB on a live V21 S7-1500 under the maintainer's
SimaticSD default import/export setting; a duplicate singleton is blocked in `dryRun`; an
unsupported class on an S7-1200 fails with TIA's error text; the result reports the assigned number.

## 2. Root cause and verified facts

- Openness V21 has no `CreateOB` (`PlcBlockComposition` offers `CreateFB`, `CreateInstanceDB`,
  `CreateFrom`, `Import`, `ImportFromDocuments` only — V21 `Siemens.Engineering.Step7.xml` and DLL
  scan). OB creation is SimaticML/SimaticSD import or nothing.
- The OB import checks `<SecondaryType>` against `<Number>` and rejects a missing number ("The
  'Number' attribute is missing in 'OB.ProgramCycle'"), despite the docs' general claim that a
  missing number is auto-assigned. `GenerateObXml` (`BlockSourceGenerator.cs:81-107`) emits no
  `<Number>`.
- Real OBs have type-specific interfaces (ProgramCycle `Initial_Call`/`Remanence`, Startup
  `LostRetentive`/`LostRTC`, diagnostic and hardware-interrupt event inputs). The current template
  emits only `Temp`/`Constant` for every type, so a hand-written template is wrong by construction.
- The advertised names `TimeDelay` and `Diagnostic` (`PlcOperationRequest.cs:80`,
  `PLC_OPERATIONS_SUMMARY.md:165`) are not SecondaryType values; nothing validates `obEventClass`
  today.
- The project-tree snapshot already carries each block's `Number` detail
  (`ProjectTreeSnapshotWalker.BuildBlockNode`), so host planning can see existing OB numbers.

## 3. Offered event classes

| `obEventClass` | Base number | Kind |
| --- | --- | --- |
| `ProgramCycle` (default) | 1 | multi-instance |
| `Startup` | 100 | multi-instance |
| `TimeOfDay` | 10 | multi-instance |
| `TimeDelayInterrupt` | 20 | multi-instance |
| `CyclicInterrupt` | 30 | multi-instance |
| `HardwareInterrupt` | 40 | multi-instance |
| `SynchronousCycle` | 61 | multi-instance (only if Phase 0 can capture a fixture) |
| `Status` / `Update` / `Profile` | 55 / 56 / 57 | singleton |
| `TimeErrorInterrupt` | 80 | singleton |
| `DiagnosticErrorInterrupt` | 82 | singleton |
| `PullOrPlugOfModules` | 83 | singleton |
| `RackOrStationFailure` | 86 | singleton |
| `ProgrammingError` | 121 | singleton |
| `IOAccessError` (spelling confirmed in Phase 0) | 122 | singleton |

Multi-instance classes accept their classic range or any number from 123; TIA auto-numbering picks
the lowest free valid number (maintainer-confirmed; re-checked in Phase 0). Singletons have one
fixed number. CPU support varies (e.g. S7-1200 has no OB 121/122 and no SynchronousCycle); no
Openness API reports it, so the server does not model it.

## 4. Design

### 4.1 Public contract

- `obEventClass` becomes a closed set of the names in §3, advertised as a schema enum. If the MCP
  SDK's schema generation cannot express an enum on this string field, the description lists the
  names instead (≈16 short tokens). Numbers, singleton rules and CPU support are **not** described;
  the server enforces them.
- Host validation (`PlcOperationCatalog`): an unknown value is `validation_error` whose message
  lists the valid names; `obEventClass` with a non-OB `blockType` is `validation_error`;
  `GRAPH` with `blockType:"OB"` is `validation_error`. Omitted with OB means `ProgramCycle`.
- `BlockMutationResultInfo` gains `number` (int, the assigned block number, written for every
  created block) and `obEventClass` (string, null for non-OB). Both are always-written members
  under the worker-payload reader rules; no conditional members.
- New block guard `plc_ob_singleton_exists` (severity `block`): creating a singleton class when the
  PLC already has an OB with that class's fixed number, or an earlier item in the same call creates
  one. Message names the class, the number and the existing block. It fires in `dryRun` and actual
  runs like other block guards.

### 4.2 Host planning

`PlcWorkingState` records each block's `Number` from the tree detail (null when absent) and
records the fixed number for singleton OBs it creates in the working state. Singleton detection is
by number over every OB in the PLC (all groups and software units): an OB already holding a
singleton's fixed number blocks the create. No multi-instance range contains a singleton number,
so number identifies class. Non-OB blocks are ignored (OB 82 and FB 82 coexist). The preview does not predict a
multi-instance number; the applied result carries it.

### 4.3 Worker generation

- One embedded SimaticML fixture per offered class under
  `TiaMcpServer.OpennessWorker/Openness/ObFixtures/`, exported from V21 and minimized: the
  `SW.Blocks.OB` attribute list (interface sections, `SecondaryType`, base `Number`,
  `AutoNumber=true`, type-specific defaults as exported) with name and language as substitution
  points.
- `BlockSourceGenerator` renders an OB from its fixture: substitutes the escaped name and the
  language, and emits the object list (comment, title, and for SCL/STL the existing empty compile
  unit) exactly as it does for FB/FC today. FB/FC/GlobalDB generation is unchanged.
- An `ObEventClasses` table (worker side, shared names with the host via `TiaMcpServer.Contracts`)
  maps class → fixture resource and base number. It is the single source of the §3 list.
- `BlockMutationService.CreateBlock` reads the created block's `Number` after import and returns
  it with the class. The existing worker preconditions and postcondition verification stay.

### 4.4 Errors

- Unsupported class on the CPU, or any other TIA import refusal: the item fails with the existing
  operation-failure path carrying TIA's message; the batch stops per normal `plc_write` rules.
- If Phase 0 shows TIA reports a duplicate singleton clearly by itself, the host guard still stays:
  it is what makes `dryRun` honest.

## 5. Phase 0 — live spike (gate)

No product code. A throwaway PowerShell 5.1 Openness script in the session scratchpad, plus
`plc_read get_block_content` (`format:"xml"`) for export. Run on the maintainer's machine with the
maintainer's SimaticSD default import/export setting.

1. **SimaticML under SimaticSD default.** Import a SimaticML OB via `PlcBlockComposition.Import`
   with the SimaticSD default set; repeat with the SimaticML default. Also confirm the existing
   FB/FC/GlobalDB create path under SimaticSD.
2. **Auto-numbering.** Base `<Number>` with `AutoNumber=true` when the base is taken: does TIA
   move to the lowest free valid number?
3. **Spelling.** Export of OB 122 settles `IOAccessError` vs `IO_AccessError`.
4. **Duplicate singleton.** The error a second OB 82 produces.
5. **Unsupported class.** The error ProgrammingError produces on an S7-1200.
6. **AutoNumber on fixed types.** Whether `AutoNumber=true` is accepted for singleton fixtures.

**Decision rule.** If (1) fails under SimaticSD, OB fixtures move to SimaticSD documents imported
with `ImportFromDocuments`, the existing FB/FC/GlobalDB path is reported as a separate issue, and
this spec is amended before planning continues. Other outcomes adjust fixture content and §3 only.
Findings go in `docs/superpowers/acceptance/reports/2026-10-xx-ob-creation-spike.md`.

**Fixture capture.** The maintainer adds one empty OB of each class from "Add new block" to a
scratch S7-1500 project (SynchronousCycle only if the hardware allows it). They are exported with
`plc_read` in `xml` (and SimaticSD if the decision rule requires it), minimized and committed.

## 6. Testing

- Offline: every `obEventClass` value has a fixture that parses, carries the matching
  `SecondaryType` and base `Number`, and `AutoNumber=true`; generator output per class and language
  passes `BlockSourceValidator`; FB/FC/GlobalDB output unchanged.
- Catalog: unknown class, class on non-OB, GRAPH on OB rejected with the listed messages.
- Planning: `plc_ob_singleton_exists` fires for an existing fixed number, for two singletons of
  one class in one call, and not for multi-instance classes.
- Contract: `number`/`obEventClass` in the payload contract and conformance probes.
- Live acceptance on V21 with the SimaticSD default: create each class on an S7-1500, a second
  ProgramCycle and CyclicInterrupt (numbers advance), a duplicate singleton blocked in `dryRun`,
  ProgrammingError on an S7-1200 failing with TIA's text, `compile_check` clean on the created OBs.

## 7. Documentation (last task, after live acceptance)

`PLC_OPERATIONS_SUMMARY.md` (names, guard, result fields), the `plc_write` tool description,
`docs/IMPROVEMENT_LOG.md` follow-ups (renumbering, OB parameters), `docs/superpowers/README.md`
status, and the live acceptance report.
