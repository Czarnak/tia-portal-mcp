# C-4 Non-Interactive Guard Rule Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** All four data-write domains (PLC, Network, HMI, VCI) enforce "no acknowledge guards" through one `WriteGuardCatalog.NonInteractive` helper.

**Architecture:** One static helper on `WriteGuardCatalog` replaces the two copied `Validate` methods in Network and PLC. HMI and VCI adopt it in their `Definitions` initialisers, so a misclassified guard fails at type initialisation.

**Tech Stack:** C#/.NET 10 host (`Safety/Pipeline`), xUnit.

**Spec:**
- Review plan item C-4: [review](2026-10-17-codebase-simplification-review.md), finding HOSTCORE-13.
- [Workstream C plan](2026-10-21-workstream-c-plan.md).

**Branch:** `refactor/c4-noninteractive-guards`, worktree `.worktrees/c-4`.

## Global Constraints

The [Workstream C global constraints](2026-10-21-workstream-c-plan.md#global-constraints-apply-to-every-wave-1-plan) apply. In addition:
- **Static helper only.** Do not enforce the rule in the pipeline from `ConfirmsEveryCall=false`, for two reasons:
  - `IWriteDomain` carries no guard definitions.
  - The check would constrain `FakeWriteDomain` (`PipelineFakes.cs` :45), which `ModeDerivedConfirmationTests`, `WriteConfirmationPipelineTests` and `WriteExecutionTests` use with acknowledge guards.
- **Lifecycle guards are out of scope.** They legitimately use acknowledge.
- **Keep Network and PLC messages verbatim:** `"Network guards cannot require acknowledgement."` and `"PLC guards …"`.

## Review Focus

1. **The checks still run.** `NonInteractive` must also run the existing catalog checks for id, severity, duplicate ids and a redefined pipeline guard. Task 1 tests the duplicate-id and pipeline-guard cases.
2. **Initialiser timing.** A bad definition must surface as a `TypeInitializationException` the first time `Definitions` is touched, as it already does for Network and PLC. `WriteGuardRegistration.ProductionCatalog()` touches all four.
3. **`ProductionCatalog()` is unchanged.** It must still concatenate the same five lists: lifecycle plus the four domains.
4. **Network's literals.** Replace `"info"`/`"block"` with `WriteGuardSeverities.Info`/`Block`. The values are identical, so a typo cannot hide.

**Contract change:** none. No data-write domain defines an acknowledge guard today.

**Live verification:** not needed. Runtime guard behaviour is unchanged. Unit tests and every fixture that calls `ProductionCatalog()` force all four initialisers to run.

## Files

- **Modify `TiaMcpServer/Safety/Pipeline/WriteGuards.cs`** (`WriteGuardCatalog`). It is linked by the `Safety\Pipeline\*.cs` glob.
- **Modify `TiaMcpServer/Network/NetworkGuardDefinitions.cs`.** Use the helper, replace the literals at :11-12, and delete `Validate` (:9-22).
- **Modify `TiaMcpServer/Plc/PlcGuardDefinitions.cs`.** Use the helper at :19 and delete `Validate` (:33-40).
- **Modify `TiaMcpServer/Hmi/HmiGuardDefinitions.cs` (:10) and `TiaMcpServer/Vci/VciGuardDefinitions.cs` (:13).** Replace `Array.AsReadOnly(...)` with the helper.
- **Tests:**
  - Add cases to `TiaMcpServer.Tests/Safety/Pipeline/WriteGuardCatalogTests.cs`.
  - Delete `NetworkGuardedWriteDomainTests.NetworkAcknowledgeRegistration_IsRejected` (:105-107) and `PlcGuardDefinitionsTests.AcknowledgeRegistrationIsRejected` (:33-40). They are the only callers of the deleted `Validate` methods, and the catalog tests replace them.
- **Docs (last):**
  - `docs/ARCHITECTURE.md`, section "Guards and acknowledgement".
  - The review plan's C-4 evidence.

## Interfaces

**Produced:** `public static IReadOnlyList<WriteGuardDefinition> NonInteractive(IEnumerable<WriteGuardDefinition> definitions, string domain)` on `WriteGuardCatalog`. It:
1. materialises the definitions;
2. throws `ArgumentException("{domain} guards cannot require acknowledgement.", nameof(definitions))` on `WriteGuardSeverities.Acknowledge`;
3. constructs a throwaway `WriteGuardCatalog` for the existing checks;
4. returns `Array.AsReadOnly`.

**Removed:** `NetworkGuardDefinitions.Validate` and `PlcGuardDefinitions.Validate`.

---

### Task 1: Helper on `WriteGuardCatalog`

- [ ] Add these cases to `WriteGuardCatalogTests`:
  - `NonInteractive_RejectsAcknowledge`: domain `"HMI"` with one acknowledge definition throws `ArgumentException`. Its `ParamName` is `"definitions"` and its message starts `HMI guards cannot require acknowledgement.`
  - `NonInteractive_ReturnsDefinitionsInOrder`: block then info come back in the same order.
  - `NonInteractive_RejectsDuplicateIds`.
  - `NonInteractive_RejectsPipelineGuardId`: uses id `WriteGuardCatalog.PartialWriteGuardId`.
- [ ] Add `NonInteractive` as a pass-through that only returns the definitions. Run `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~WriteGuardCatalogTests`. The three rejection cases fail.
- [ ] Implement the helper. All tests in the class pass, including the nine existing ones. Commit `feat: add WriteGuardCatalog.NonInteractive`.

### Task 2: All four domains use the helper

- [ ] Delete the two per-domain acknowledge tests.
- [ ] Network: `Definitions = WriteGuardCatalog.NonInteractive(new[] {…}, "Network")`. Replace the severity literals with constants and delete `Validate`.
- [ ] PLC: use `NonInteractive(…, "PLC")` and delete `Validate`.
- [ ] HMI: `NonInteractive(…, "HMI")`. VCI: `NonInteractive(…, "VCI")`.
- [ ] Run with the filter `FullyQualifiedName~GuardDefinitionsTests|FullyQualifiedName~NetworkGuardedWriteDomainTests|FullyQualifiedName~WriteGuardCatalogTests|FullyQualifiedName~PlcGuarded`. Expect green. These existing tests already pin the HMI and VCI severities:
  - `HmiGuardDefinitionsTests.ProductionCatalog_RegistersExactlyFourHmiGuardsWithFixedSeverities`
  - `VciGuardDefinitionsTests.SixGuardsHaveFixedSeveritiesAndProductionCatalogHasNoDuplicates`
- [ ] Sweep:
  - `GuardDefinitions.Validate` has zero hits.
  - `"info"` and `"block"` have zero hits in `NetworkGuardDefinitions.cs`.
  - `NonInteractive(` appears exactly once in each of the four `*GuardDefinitions.cs` files.
- [ ] Commit `refactor: route data-write guard sets through NonInteractive`.

### Task 3: Full verification, docs and evidence (docs last)

- [ ] Build and run the full suite with coverage under `TestResults/c4`. Expected test count: the main baseline − 2 + 4.
- [ ] In `docs/ARCHITECTURE.md` under "Guards and acknowledgement", add one sentence: Network, PLC, HMI and VCI guard sets are built with `WriteGuardCatalog.NonInteractive`, so an acknowledge guard there fails at type initialisation, and only lifecycle uses acknowledge.
- [ ] Add a C-4 evidence sub-bullet to the review plan. Include:
  - why the static helper was chosen over pipeline enforcement;
  - which tests were replaced;
  - the pass count;
  - that no live run was needed, and why.
- [ ] Commit `docs: record C-4 guard rule and evidence`.
