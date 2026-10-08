# `create_block` OB creation — design (issue #75)

*2026-10-08. Branch `fix/ob-create-issue-75`. Status: implemented; live-accepted 2026-10-09
([spike](../acceptance/reports/2026-10-08-ob-creation-spike.md),
[live acceptance](../acceptance/reports/2026-10-09-ob-creation-live-acceptance.md)).*

## 1. Intent

`plc_write` `create_block` with `blockType:"OB"` fails for every event class (issue #75). Fix it so
an agent can create any user-creatable OB type by naming its event class, without learning OB
numbering rules, and without the tool description carrying those rules.

**In scope:** OB creation through `create_block`; the `obEventClass` value set; server-chosen OB
numbers; a singleton guard; the assigned number in the result; STL creation for FB/FC/OB (the
same generator defect, found in Phase 0).

**Out of scope (decided):**

- Renumbering any block (`set_block_number`). Logged as a follow-up in `docs/IMPROVEMENT_LOG.md`.
- Caller-chosen OB numbers.
- Event-specific parameters (CyclicInterrupt `CyclicTime`/`PhaseOffset`, priority, time-of-day
  schedule, hardware-interrupt triggers). Created OBs use TIA defaults. Logged as a follow-up.
- `SynchronousCycle`: it fails to compile on every CPU that accepts it until an isochronous IO
  system is assigned, which no tool here can configure.
- Motion Control (`MC_*`), redundancy (OB 70/72) and ProDiag OBs: TIA creates these itself.
- Modelling CPU support: TIA's import refuses an unsupported class with a clear message.

**Success:** every offered class creates an OB with a final, non-colliding number on a live V21
S7-1500 under the maintainer's SimaticSD default; `dryRun` shows the planned number; a duplicate
singleton is blocked in `dryRun` and in actual runs; an unsupported class on an S7-1200 fails with
TIA's message; STL FB/FC/OB creation works; full PLC compile of the created OBs is clean.

## 2. Verified facts

From research and the Phase 0 spike (V21, S7-1500 F-CPU, S7-1200, S7-1200 G2):

- Openness V21 has no `CreateOB`; OB creation is `PlcBlockComposition.Import` of SimaticML.
- SimaticML import works with the SimaticSD default import/export setting (OBs and the existing
  FB/FC/GlobalDB path).
- `<Number>` is mandatory, and import uses it verbatim even when another OB holds it, with or
  without `AutoNumber`. A full PLC compile later renumbers multi-instance duplicates to the lowest
  free number from 123 upward (ignoring classic ranges); compiling one block does not.
- A duplicate singleton imports silently; only the full compile fails ("The maximum (1) of …
  OBs … has been exceeded").
- An unsupported class fails at import: "Cannot create an organization block of type '<class>'."
  (S7-1200: ProgrammingError, IOAccessError, SynchronousCycle.)
- Real OBs have class-specific `Informative="true"` Input members. Comments on them can be
  omitted: TIA regenerates them and the class's default title.
- LAD/FBD OBs without a compile unit import and compile. STL blocks reject
  `SetENOAutomatically` ("… attribute at the 'SW.Blocks.FB' object … is not supported"); this
  breaks `create_block` FB/FC in STL today too.
- The advertised names `TimeDelay` and `Diagnostic` (`PlcOperationRequest.cs:80`,
  `PLC_OPERATIONS_SUMMARY.md:165`) are not SecondaryType values; nothing validates `obEventClass`.
- The project-tree snapshot carries each block's `Number` detail
  (`ProjectTreeSnapshotWalker.BuildBlockNode`), so host planning sees existing OB numbers.

## 3. Offered event classes

| `obEventClass` | Base number | Kind |
| --- | --- | --- |
| `ProgramCycle` (default) | 1 | multi-instance |
| `Startup` | 100 | multi-instance |
| `TimeOfDay` | 10 | multi-instance |
| `TimeDelayInterrupt` | 20 | multi-instance |
| `CyclicInterrupt` | 30 | multi-instance |
| `HardwareInterrupt` | 40 | multi-instance |
| `Status` / `Update` / `Profile` | 55 / 56 / 57 | singleton |
| `TimeErrorInterrupt` | 80 | singleton |
| `DiagnosticErrorInterrupt` | 82 | singleton |
| `PullOrPlugOfModules` | 83 | singleton |
| `RackOrStationFailure` | 86 | singleton |
| `ProgrammingError` | 121 | singleton |
| `IOAccessError` | 122 | singleton |

**Number rule.** A singleton gets its base number. A multi-instance class gets its base number if
no OB in the PLC holds it, otherwise the lowest number from 123 to 32767 that no OB holds. Numbers
of non-OB blocks are ignored (OB 82 and FB 82 coexist). No multi-instance base or the 123+ range
contains a singleton number, so a number identifies a singleton class.

## 4. Design

### 4.1 Shared definition (`TiaMcpServer.Contracts`)

One `ObEventClasses` table — name, base number, singleton flag — is the single source of §3, and
one pure `ObNumberRules` function applies the number rule to a set of used OB numbers. Host
planning and the worker both use them, so the previewed and executed numbers come from the same
code. No Siemens dependency.

### 4.2 Public contract

- `obEventClass` is a closed set of the §3 names, advertised as a schema enum. If the MCP SDK's
  schema generation cannot express an enum on this string field, the description lists the names
  instead. Numbers, singleton rules and CPU support are **not** described.
- Host validation (`PlcOperationCatalog`): an unknown value is `validation_error` whose message
  lists the valid names; `obEventClass` with a non-OB `blockType` is `validation_error`; `GRAPH`
  with `blockType:"OB"` is `validation_error`. Omitted with OB means `ProgramCycle`.
- The `create_block` effect (preview and applied) adds `obEventClass` and the planned `number` to
  its field changes for OBs.
- `BlockMutationResultInfo` gains `number` (int; the created block's number, for every created
  block) and `obEventClass` (string; null for non-OB). Both are always-written members under the
  worker-payload reader rules.
- New block guard `plc_ob_singleton_exists` (severity `block`): the PLC already has an OB holding
  the singleton's number, or an earlier item in the same call creates one. The message names the
  class, the number and the existing block. It fires in `dryRun` and actual runs.

### 4.3 Host planning

`PlcWorkingState` keeps each block's `Number` from the tree detail and the set of OB numbers in the
PLC (all groups and software units). Resolving an OB `create_block` fires the singleton guard or
computes the planned number with `ObNumberRules`; applying it adds the number to the working
state, so later items in the same call see it.

### 4.4 Worker

- **Fixtures.** One embedded resource per offered class under
  `TiaMcpServer.OpennessWorker/Openness/ObFixtures/`: the class's interface `Sections` element as
  exported from V21 (Input members, empty `Temp`/`Constant`), with member comments removed. Number,
  class, name and language are not in the fixture.
- **Generation.** `BlockSourceGenerator` builds the OB document: `Interface` from the fixture,
  `MemoryLayout=Optimized`, `Name`, `Namespace`, `Number` (passed in), `ProgrammingLanguage`,
  `SecondaryType`, and `SetENOAutomatically=false` except for STL; no `AutoNumber`. The object list
  is the existing one (empty comment and title, the empty compile unit for SCL/STL only). FB/FC
  omit `SetENOAutomatically` for STL; otherwise FB/FC/GlobalDB output is unchanged.
- **Execution.** `BlockMutationService.CreateBlock` collects the PLC's used OB numbers, re-applies
  `ObNumberRules`, and treats a singleton number now held as `state_changed` (the plan saw it free),
  next to the existing absence preconditions. It imports with the chosen number, then reads back
  the block's `Number` and returns it with the class. A multi-instance number that differs from the
  plan is not an error; the result is authoritative.

### 4.5 Errors

- Unsupported class on the CPU, or any other TIA import refusal: the item fails through the
  existing operation-failure path carrying TIA's message; the call stops per `plc_write` rules.
- No free number from 123 to 32767: `validation_error` from planning (and the worker re-check).

## 5. Phase 0 — done

The [spike report](../acceptance/reports/2026-10-08-ob-creation-spike.md) records the setup,
every case and the findings behind §2. Fixtures were captured from the maintainer's V21 exports
(`DisposableProjects/OB_workspace/PLC_1/Program blocks/OBs`, one empty SCL OB per class plus a LAD
ProgramCycle reference) and are minimized and committed with the implementation.

## 6. Testing

- Contracts: `ObNumberRules` — base free, base taken, 123+ gaps, singleton, non-OB numbers
  ignored, exhaustion.
- Worker generation: every §3 class has a fixture that parses and has an Input section; generated
  documents per class and language have the class's `SecondaryType`, the given `Number`, no
  `AutoNumber`, no `SetENOAutomatically` for STL, and pass `BlockSourceValidator`; FB/FC STL omit
  `SetENOAutomatically`; other FB/FC/GlobalDB output unchanged.
- Catalog: unknown class, class on non-OB, GRAPH on OB rejected with the listed messages.
- Planning: planned numbers in effects; `plc_ob_singleton_exists` for an existing number and for
  two singletons in one call; two multi-instance creates in one call get distinct numbers.
- Contract: `number`/`obEventClass` in the payload contract, register tests and conformance probes.
- Live acceptance (V21, SimaticSD default, project with S7-1500, S7-1200, S7-1200 G2): each class
  on the S7-1500 with planned = actual number; a second ProgramCycle and CyclicInterrupt land on
  free 123+ numbers; duplicate singleton blocked in `dryRun` and actual; ProgrammingError on the
  S7-1200 fails with TIA's text; STL FB/FC/OB created; full PLC compile clean except the
  HardwareInterrupt trigger warning.

## 7. Documentation (last task, after live acceptance)

`PLC_OPERATIONS_SUMMARY.md` (names, number rule in one line, guard, result fields), the
`plc_write` tool description, `docs/IMPROVEMENT_LOG.md` follow-ups (renumbering, OB parameters,
SynchronousCycle), `docs/superpowers/README.md` status, and the live acceptance report.
