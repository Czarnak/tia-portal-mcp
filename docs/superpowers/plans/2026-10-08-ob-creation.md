# `create_block` OB creation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `plc_write create_block` creates every offered OB class with a server-chosen, final,
non-colliding number; STL FB/FC/OB creation works (issue #75).

**Architecture:** A shared Contracts table and number rule drive both host planning (preview number,
singleton guard) and worker execution (re-check, import, read-back). OBs are generated from
per-class interface fixtures captured from V21, embedded in the worker.

**Tech Stack:** .NET 10 host/tests/FakeWorker, net48 worker, netstandard2.0 contracts,
ModelContextProtocol C# SDK 2.2.0, xUnit. No new dependencies.

**Spec:** [`docs/superpowers/specs/2026-10-08-ob-creation-design.md`](../specs/2026-10-08-ob-creation-design.md)
(evidence: [Phase 0 spike](../acceptance/reports/2026-10-08-ob-creation-spike.md)). Baseline
`71625f0` on `fix/ob-create-issue-75`.

## Global Constraints

- Offered classes, base numbers and kinds exactly as spec §3 (15 classes; `SynchronousCycle`
  excluded). Default class `ProgramCycle`. Free range `123`–`32767`.
- Number rule (spec §3): singleton → base number; multi-instance → base number if no OB holds it,
  else lowest number in 123–32767 no OB holds. Only OB numbers count; OBs in software units and
  system groups count.
- Generated OB documents carry no `AutoNumber`, no member comments, and no
  `SetENOAutomatically` when the language is STL. FB/FC omit `SetENOAutomatically` for STL too.
- New guard id `plc_ob_singleton_exists`, severity `block`.
- Result members `number` (`int?`, set for `create_block`, null for the other block operations) and
  `obEventClass` (`string?`, null for non-OB) on `BlockMutationResultInfo`, always written.
- Commits: one short conventional line, no body or trailers, after every implementation or fix
  step. Build and test serially. Use PowerShell for `dotnet` commands that pass `/p:`. The 80%
  scoped coverage gate applies.
- No live TIA action without the maintainer's authorization naming the project. Never name the
  maintainer's project in docs.
- Docs (README, AGENTS, SupportedOperations, ARCHITECTURE, IMPROVEMENT_LOG) change only in the
  last task, after live acceptance.

## Decisions where the code shapes the design

1. **Schema enum via transform, not attributes.** `[AllowedValues]` drops `null` from `enum`, so an
   explicit `"obEventClass": null` would fail strict client validation, and a C# enum would fail at
   SDK binding before the catalog. The property stays `string?`; `PlcWriteToolRegistration` adds
   `enum: [...names, null]` through `TransformSchemaNode` (precedent:
   `ProjectReadTools.AlignSelectorSchemaNullability`). The catalog stays the validator.
2. **No planned number is forwarded to the worker.** The worker re-applies the same rule; its result
   is authoritative. `WorkerRequest` is unchanged.
3. **OB detection is case-insensitive** on `blockType` on the host, matching the worker's
   upper-casing. `obEventClass` matches the canonical names exactly (ordinal).
4. **`number` is `int?`** because `BlockMutationResultInfo` is shared with delete and group
   operations; null there, set for `create_block` (all block types).
5. **Fixture = interface `Sections` element only.** Number, class, name and language come from
   code. Resources use the explicit `LogicalName` `TiaMcpServer.ObFixtures.<Class>.xml` in both the
   worker and the test project, so the linked loader finds them in either assembly.

## Review Focus

1. **Explicit `obEventClass: null` on an FB/FC/DB create** stays valid: schema enum includes null,
   catalog treats null as omitted. Pinned by `PlcGuardedWriteMcpTests.ObEventClassSchemaEnumListsClassesAndNull`
   and `PlcOperationCatalogTests.CreateBlockAcceptsNullObEventClassForFb`.
2. **Lower-case `blockType:"ob"`** gets a planned number and the singleton guard like `"OB"`.
   Pinned by `PlcWorkingStateTests.LowerCaseObTypePlansNumberAndGuards`.
3. **OBs inside software units or system groups** hold their numbers on both sides. Pinned by
   `PlcWorkingStateTests.UnitObHoldsSingletonNumber` and `ObNumberScannerTests.CollectsUnitAndSystemGroupObs`.
4. **Project changed between plan and execution.** A singleton number taken since planning fails
   `state_changed` before import; a multi-instance create re-picks and reports the actual number.
   Pinned by `PlcWritePreconditionsTests.CreateOb_SingletonTakenSincePlanIsStateChanged` and
   `CreateOb_MultiInstanceRepicksWhenPlannedNumberTaken`.
5. **Delete then create in one call:** `delete_block` of the OB holding 82 followed by
   `create_block DiagnosticErrorInterrupt` plans 82 with no guard. Pinned by
   `PlcWorkingStateTests.DeletedObFreesItsNumberForLaterItems`.

## Execution conventions and file boundaries

- Stub build: `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true`.
- Filtered serial run:
  `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter '<filter>' -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1`.
- `TiaMcpServer.Tests.csproj` enumerates host `Plc/*.cs` and worker `Openness/*.cs` files: every
  new host Plc or worker file needs a `<Compile Include … Link=…>` there. Contracts files come
  through the existing project reference.
- Test doubles for linked worker code live in `TiaMcpServer.Tests/TestUtilities/TagSafetySiemensDoubles.cs`.
- Do not edit `reference-stubs/` (the worker compiles against prebuilt `ref/*.dll`); everything
  needed (`PlcBlock.Number`, `OB`, `Import` return value, unit block groups) already exists.

---

### Task 1: Shared OB class table and number rule

**Files:**
- Create: `TiaMcpServer.Contracts/ObEventClasses.cs`, `TiaMcpServer.Contracts/ObNumberRules.cs`
- Test: `TiaMcpServer.Tests/Plc/ObNumberRulesTests.cs` (add `<Compile>` only if the folder is enumerated; Contracts is a project reference)

**Interfaces:**
- Produces:
  - `sealed class ObEventClass { string Name; int BaseNumber; bool IsSingleton; }`
  - `static class ObEventClasses { const string Default = "ProgramCycle"; IReadOnlyList<ObEventClass> All; bool TryGet(string name, out ObEventClass cls); string NamesForMessage(); }` — ordinal lookup; `All` in spec §3 order.
  - `static class ObNumberRules { const int FirstFree = 123; const int Max = 32767; int? Pick(ObEventClass cls, ICollection<int> usedObNumbers); }` — null when the singleton's number is held or nothing is free.

- [ ] Write failing tests: `AllMatchesSpecTable` (15 names, base numbers, kinds; no `SynchronousCycle`),
  `TryGetIsOrdinal` (`"programcycle"` fails), `PickReturnsBaseWhenFree` (CyclicInterrupt, used {1} → 30),
  `PickSkipsToFirstFreeFrom123` (ProgramCycle, used {1,123,124} → 125), `PickSingletonHeldReturnsNull`
  (DiagnosticErrorInterrupt, used {82} → null), `PickSingletonFreeReturnsBase`,
  `PickReturnsNullWhenRangeExhausted` (base and 123–32767 used).
- [ ] Run them. Expected: FAIL (types missing).
- [ ] Implement both files.
- [ ] Run the filter `FullyQualifiedName~ObNumberRulesTests`. Expected: PASS.
- [ ] Commit: `feat: add shared OB event class table and number rule`

### Task 2: OB fixtures and generator (with the STL fix)

**Files:**
- Create: `TiaMcpServer.OpennessWorker/Openness/ObFixtures/<Class>.xml` (15 files), `TiaMcpServer.OpennessWorker/Openness/ObFixtureStore.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/BlockSourceGenerator.cs`, `BlockSourceValidator.cs` (`ValidateTypeLanguage`), `TiaMcpServer.OpennessWorker/TiaMcpServer.OpennessWorker.csproj`, `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
- Test: `TiaMcpServer.Tests/Block/BlockSourceGeneratorTests.cs`, `BlockSourceValidatorTests.cs`, new `TiaMcpServer.Tests/Block/ObFixtureStoreTests.cs`

**Interfaces:**
- Consumes: `ObEventClasses` (Task 1).
- Produces:
  - `internal static class ObFixtureStore { string LoadSections(string obEventClass); }` — returns the `<Sections …v5>` element text; unknown class or missing resource → `validation_error`.
  - `BlockSourceGenerator.Generate(string blockName, string blockType, string language, string? obEventClass, int? obNumber)` — OB requires a known class and a number, else `validation_error`.

- [ ] Create the fixtures from the maintainer's exports in
  `DisposableProjects/OB_workspace/PLC_1/Program blocks/OBs` (outside the repo): for each spec §3
  class take `AttributeList/Interface/Sections` from `<Class>.xml` (`Main_SCL.xml` for
  ProgramCycle, `IO_AccessError.xml` for IOAccessError), remove every `Member/Comment`, keep the
  `Informative="true"` members, save as `ObFixtures/<SecondaryType>.xml`. Skip `SynchronousCycle`.
- [ ] Add `<EmbeddedResource Include="Openness\ObFixtures\*.xml" LogicalName="TiaMcpServer.ObFixtures.%(Filename)%(Extension)" />`
  to the worker csproj, and the linked equivalent plus `<Compile>` for `ObFixtureStore.cs` to the test csproj.
- [ ] Write failing tests:
  - `ObFixtureStoreTests.EveryClassHasFixtureWithInputSection` (all 15; parses; has `Section Name="Input"` with ≥1 member; no `Comment`).
  - `ObFixtureStoreTests.UnknownClassIsValidationError`.
  - `BlockSourceGeneratorTests.ObDocumentCarriesClassNumberAndFixtureInterface` (Theory over all classes × LAD/FBD/SCL/STL, number 200: `SecondaryType`, `Number`=200, `MemoryLayout`=Optimized, Input members equal the fixture's, no `AutoNumber`).
  - `BlockSourceGeneratorTests.StlOmitsSetEnoAutomatically` (Theory FB/FC/OB) and `NonStlKeepsSetEnoAutomaticallyFalse` (FB/FC/OB × LAD/SCL).
  - `BlockSourceGeneratorTests.ObWithoutNumberOrUnknownClassIsValidationError`.
  - `BlockSourceValidatorTests.GraphIsRejectedForOb`.
  - Update the existing OB cases in `Generate_SclBlock_HasCompileUnitWithEmptyNetworkSource` and `Scl_source_contains_a_compile_unit_with_an_empty_network_source` to pass a class and number.
- [ ] Run them. Expected: FAIL.
- [ ] Implement `ObFixtureStore` (`typeof(ObFixtureStore).Assembly.GetManifestResourceStream`), rewrite `GenerateObXml(name, language, cls, number)` to the spec §4.4 attribute list (alphabetical: `Interface`, `MemoryLayout`, `Name`, `Namespace`, `Number`, `ProgrammingLanguage`, `SecondaryType`, `SetENOAutomatically` unless STL), keep the existing object list, drop `SetENOAutomatically` for STL in FB/FC, reject GRAPH for OB in `ValidateTypeLanguage`.
- [ ] Stub build, then run the `Block` tests. Expected: PASS.
- [ ] Commit: `fix: generate OBs from V21 interface fixtures and omit SetENO for STL`

### Task 3: Worker execution, number read-back and result fields

**Files:**
- Create: `TiaMcpServer.OpennessWorker/Openness/ObNumberScanner.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/BlockMutationService.cs` (`CreateBlock`, `ImportBlockFromXml`), `BlockTargetResolver.cs` (`EnumerateOwners` private → internal), `TiaMcpServer.Contracts/BlockMutationResultInfo.cs`, `TiaMcpServer.Tests/TestUtilities/TagSafetySiemensDoubles.cs` (`PlcBlockComposition.Import` returns a test-supplied block list), `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
- Test: new `TiaMcpServer.Tests/Worker/ObNumberScannerTests.cs`, `TiaMcpServer.Tests/Worker/PlcWritePreconditionsTests.cs`, `TiaMcpServer.Tests/Plc/PlcWritePayloadContractTests.cs`

**Interfaces:**
- Consumes: `ObEventClasses`, `ObNumberRules` (Task 1); `Generate(…, obEventClass, obNumber)` (Task 2).
- Produces:
  - `internal static class ObNumberScanner { HashSet<int> Collect(PlcSoftware plc); }` — every `OB` under each owner from `BlockTargetResolver.EnumerateOwners` (root and units), recursing `Groups` and `SystemBlockGroups`; an `EngineeringException` → `worker_operation_failed`.
  - `BlockMutationResultInfo.Number : int?`, `BlockMutationResultInfo.ObEventClass : string?`.
  - `ImportBlockFromXml(…)` returns the imported `PlcBlock` (first of `Import`'s result; none → `worker_operation_failed`).

- [ ] Write failing tests:
  - `ObNumberScannerTests.CollectsUnitAndSystemGroupObs`, `IgnoresNonObBlocks`.
  - `PlcWritePreconditionsTests.CreateOb_DefaultsToProgramCycleAndUsesFreeNumber` (OB1 exists → imported document has `Number` 123, result `Number` read back from the imported block, `ObEventClass` "ProgramCycle").
  - `CreateOb_SingletonTakenSincePlanIsStateChanged` (OB82 exists → `state_changed`, no import call).
  - `CreateOb_MultiInstanceRepicksWhenPlannedNumberTaken` (OB30 exists → document `Number` 123).
  - `CreateOb_UnknownClassIsValidationError` (no import call).
  - `CreateFb_ResultHasNumberAndNullObEventClass`.
  - `PlcWritePayloadContractTests`: `create_block` payload with `number`/`obEventClass` decodes typed; a payload missing `number` is `protocol_error`; `delete_block` with `number: null` decodes.
- [ ] Run them. Expected: FAIL.
- [ ] Implement: in `CreateBlock`, for OB resolve the class (null → `ObEventClasses.Default`; unknown → `validation_error`), `Collect`, singleton held → `state_changed` naming class and number, `Pick` null → `validation_error`; generate, import, read back `Number`; set `ObEventClass` for OB. Update the double and every existing `BlockMutationResultInfo` construction site the compiler flags.
- [ ] Stub build, then run the `Worker` and `Plc` payload tests. Expected: PASS.
- [ ] Commit: `feat: pick OB numbers in the worker and return the created block number`

### Task 4: Host validation and schema enum

**Files:**
- Modify: `TiaMcpServer/Plc/PlcOperationCatalog.cs` (`ValidateWriteItem`), `TiaMcpServer/Plc/PlcOperationRequest.cs` (`ObEventClass` description), `TiaMcpServer/Tools/StrictArgumentsTool.cs` (`PlcWriteToolRegistration` schema options)
- Test: `TiaMcpServer.Tests/Plc/PlcOperationCatalogTests.cs`, `TiaMcpServer.Tests/Plc/PlcGuardedWriteMcpTests.cs`
  (`PlcWriteInvokerTests.ValidatedFieldValues` bypasses the catalog and needs no change)

**Interfaces:**
- Consumes: `ObEventClasses` (Task 1).
- Produces: catalog messages `"{prefix}: obEventClass '{value}' is not valid. Valid values: {names}."`,
  `"{prefix}: obEventClass applies only to blockType OB."`,
  `"{prefix}: language GRAPH is not supported for blockType OB."` (`{prefix}` is the existing
  `Operation '…' (operationId '…')`).

- [ ] Write failing tests: `CreateBlockRejectsUnknownObEventClass` (`"TimeDelay"`; message lists `TimeDelayInterrupt`),
  `CreateBlockRejectsObEventClassForFb`, `CreateBlockRejectsGraphForOb` (also `"graph"`/`"ob"`),
  `CreateBlockAcceptsNullObEventClassForFb`, `CreateBlockAcceptsEveryClassForOb`,
  `PlcGuardedWriteMcpTests.ObEventClassSchemaEnumListsClassesAndNull` (`items.properties.obEventClass.enum` = 15 names + null).
- [ ] Run them. Expected: FAIL.
- [ ] Implement the three checks in `ValidateWriteItem` (OB test case-insensitive), the
  `TransformSchemaNode` for `obEventClass` in the `plc_write` registration only, and the
  description: "OB event class for create_block when blockType=OB; defaults to ProgramCycle. The
  server assigns the OB number."
- [ ] Run the `Plc` tests. Expected: PASS.
- [ ] Commit: `feat: validate obEventClass and advertise it as a schema enum`

### Task 5: Host planning — planned number and singleton guard

**Files:**
- Modify: `TiaMcpServer/Plc/PlcWorkingState.cs` (`Block` record, `Fill`, `ResolveEffect` create_block, `ApplyResolved` create_block), `TiaMcpServer/Plc/PlcGuardDefinitions.cs`, `TiaMcpServer.FakeWorker/PlcWriteRoundtripScenario.cs` (per-block numbers in the fixture, real `Number` details, `create_block` result `Number`/`ObEventClass` via `ObNumberRules`)
- Test: `TiaMcpServer.Tests/Plc/PlcWorkingStateTests.cs` (`Block(...)` helper takes a number), `PlcGuardDefinitionsTests.cs`, `PlcGuardedWriteDomainTests.cs`

**Interfaces:**
- Consumes: `ObEventClasses`, `ObNumberRules` (Task 1); result fields (Task 3).
- Produces: `PlcGuardDefinitions.ObSingletonExists = "plc_ob_singleton_exists"` (block);
  `Block(string Name, string Type, string? Language, bool IsSystem, int? Number)`; effect changes
  `obEventClass` (class, default included) and `number` (planned, decimal string) for OB creates.

- [ ] Write failing tests:
  - `PlcGuardDefinitionsTests.OnlyBlockAndInfoSeverities` gains the new id.
  - `PlcWorkingStateTests`: `ObCreatePlansNumberInEffect` (Main=OB1 → ProgramCycle plans "123"),
    `ObCreateDefaultsObEventClassInEffect`, `SingletonHeldFiresGuardNamingExistingBlock`,
    `TwoSingletonsInOneCallSecondIsGuarded`, `TwoCyclicCreatesGetDistinctNumbers` (30, then 123),
    `UnitObHoldsSingletonNumber`, `LowerCaseObTypePlansNumberAndGuards`,
    `DeletedObFreesItsNumberForLaterItems`, `FbCreateHasNoNumberChange`.
  - `PlcGuardedWriteDomainTests.DryRunListsSingletonGuardThenActualCallBlocks` and
    `ObCreatePreviewNumberMatchesAppliedResult`.
- [ ] Run them. Expected: FAIL.
- [ ] Implement: `Fill` parses the `Number` detail; OB numbers come from
  `_unitRoots.Values.Prepend(_root).SelectMany(DescendantBlocks)` filtered to `ProjectTreeNodeTypes.Ob`;
  singleton held → `Fire(ObSingletonExists, …)` with class, number and holder path; `Pick` null
  otherwise → `ArgumentException` (`validation_error`); `ApplyResolved` stores the planned number;
  the FakeWorker mirrors the rule.
- [ ] Stub build, then the full suite. Expected: PASS.
- [ ] Commit: `feat: plan OB numbers and guard duplicate singleton OBs`

### Task 6: Offline qualification and live acceptance

**Files:**
- Create: `docs/superpowers/acceptance/reports/2026-10-xx-ob-creation-live-acceptance.md`

- [ ] Real-V21 build, full suite, coverage gate (`scripts/verify-coverage-threshold.ps1 -MinimumLineRate 0.80`); record counts. Expected: 0 errors, PASS.
- [ ] Install the candidate (`./scripts/install-local-tool.ps1 -StopRunningServer`, clean tree) and confirm the live `plc_write` schema shows the enum.
- [ ] With the maintainer's authorization naming the disposable project (S7-1500, S7-1200, S7-1200 G2), in read-write:
  each class on the S7-1500 with dryRun number = applied number; a second ProgramCycle and
  CyclicInterrupt land on free 123+ numbers; duplicate DiagnosticErrorInterrupt blocked in `dryRun`
  and actual; ProgrammingError on the S7-1200 fails with "Cannot create an organization block of
  type 'ProgrammingError'."; STL FB, FC and OB created; `obEventClass:"TimeDelay"` rejected with the
  name list; full `compile_check` of the S7-1500 clean except the HardwareInterrupt trigger warning.
- [ ] Write the report (house sections: Scope and authorization, Environment, Results, Defects and observations, Evidence limitations). Fix defects with a test first, one commit each.
- [ ] Commit: `docs: add OB creation live acceptance report`

### Task 7: Documentation (after live acceptance)

**Files:**
- Modify: `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md` (line ~165 prose, guard table, result fields, Known limits), `TiaMcpServer/Plc/PlcWriteTools.cs` (description sentence on guards), `docs/ARCHITECTURE.md` (§7 PLC writes guard table), `AGENTS.md` (PLC writes guard list), `docs/IMPROVEMENT_LOG.md` (new `## Open: create_block OB follow-ups (issue #75, 2026-10-xx)`: renumbering, OB event parameters, SynchronousCycle; completed entry), `docs/superpowers/README.md` (spec/plan/report status rows)

- [ ] Update each file; the summary states the number rule in one line and lists the 15 classes.
- [ ] Run the full suite (descriptions are asserted by schema tests). Expected: PASS.
- [ ] Commit: `docs: document OB creation and its follow-ups`
