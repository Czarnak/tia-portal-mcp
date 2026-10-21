# C-4 Non-Interactive Guard Rule Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** All four data-write domains (PLC, Network, HMI, VCI) enforce "no acknowledge guards" through one `WriteGuardCatalog.NonInteractive` helper.

**Architecture:** A static helper on `WriteGuardCatalog` replaces the two copied `Validate` methods in the Network and PLC guard definitions. HMI and VCI adopt the same helper in their `Definitions` initialisers. From then on, a misclassified guard in any of these domains fails at type initialisation.

**Tech Stack:** C#/.NET 10 host (`Safety/Pipeline`), xUnit.

**Spec:** Review plan item C-4 ([review](2026-10-17-codebase-simplification-review.md), finding HOSTCORE-13) and the [Workstream C plan](2026-10-21-workstream-c-plan.md).

**Branch:** `refactor/c4-noninteractive-guards`, worktree `.worktrees/c-4`.

**Execution:** inline (executing-plans), then one fresh reviewer.

## Global Constraints

- **Static helper only.** Do not enforce the rule in the pipeline from `ConfirmsEveryCall=false`. There are two reasons:
  - `IWriteDomain` carries no guard definitions.
  - It would constrain `FakeWriteDomain` (`PipelineFakes.cs` :45), which pipeline tests use with acknowledge guards.
- **Lifecycle guards are out of scope.** They legitimately use acknowledge.
- **Network and PLC messages stay verbatim:** `"Network guards cannot require acknowledgement."` and `"PLC guards cannot require acknowledgement."`.
- **Shell:** run `dotnet` from PowerShell, never Bash.
- **Build:** `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true`. Expect no warnings beyond the seven existing test-assembly nullable warnings.
- **Tests:** run only each task's focused filters, not the full suite. The orchestrator runs the full suite once per branch, one worktree at a time. `Safety/Pipeline/*.cs` is glob-linked into the tests, so no csproj change is needed.
- **Commits:** commit after each task, as a one-line conventional commit with no body. After each commit, expect one new commit and a clean `git status`.
- **Docs come last.**
- **No TIA project names or paths** anywhere in the repo.

## Review Focus

1. **The catalog checks still run.** `NonInteractive` must still apply the id, severity, duplicate-id and redefined-pipeline-guard checks. Pinned by `NonInteractive_RejectsDuplicateIds` and `NonInteractive_RejectsPipelineGuardId` (Task 1).
2. **HMI and VCI really use the helper.** If they are never switched over, nothing rejects an acknowledge guard there. Pinned by the Task 2 sweep, which expects `NonInteractive(` exactly once in each of the four `*GuardDefinitions.cs` files.
3. **HMI and VCI severities are unchanged.** Pinned by `HmiGuardDefinitionsTests.ProductionCatalog_RegistersExactlyFourHmiGuardsWithFixedSeverities` and `VciGuardDefinitionsTests.SixGuardsHaveFixedSeveritiesAndProductionCatalogHasNoDuplicates` (Task 2).
4. **Network severity literals become constants.** Pinned by the Task 2 sweep.
5. **Null elements.** A `null` definition must fail with the catalog's `ArgumentException`, not a `NullReferenceException`. Pinned by `NonInteractive_RejectsNullDefinition` (Task 1).

**Contract change:** none.

**Live verification:** not needed. Runtime guard behaviour is unchanged.

## After the tasks

These are not implementer tasks. They run after Task 2 and before Task 3.

### Branch verification (orchestrator, serialized across worktrees)

Run the stub build, then the full suite with coverage under `TestResults/c4`:
- process-local `TEMP`/`TMP` set to `TestResults/c4/temp`;
- `TiaMcpServer.Tests/coverage.runsettings`;
- the single-thread xUnit settings from `.github/workflows/ci.yml`.

Expected:
- 0 failed and 0 skipped;
- `scripts/verify-coverage-threshold.ps1 -MinimumLineRate 0.80` passes.

Then the final whole-branch review follows, per the execution skill.

---

### Task 1: Helper on `WriteGuardCatalog`

**Files:**
- Modify: `TiaMcpServer/Safety/Pipeline/WriteGuards.cs` (`WriteGuardCatalog`).
- Test: `TiaMcpServer.Tests/Safety/Pipeline/WriteGuardCatalogTests.cs`.

**Interfaces:**
- Consumes: `WriteGuardSeverities.Acknowledge`, the existing `WriteGuardCatalog` constructor checks, and `WriteGuardCatalog.PartialWriteGuardId`.
- Produces: `public static IReadOnlyList<WriteGuardDefinition> NonInteractive(IEnumerable<WriteGuardDefinition> definitions, string domain)`. It works in four steps:
  1. Materialise the definitions.
  2. Throw `ArgumentException("{domain} guards cannot require acknowledgement.", nameof(definitions))` for any non-null element whose severity is `Acknowledge`. Null elements are left for step 3.
  3. Construct a throwaway `WriteGuardCatalog`, so the existing checks run.
  4. Return `Array.AsReadOnly`.

- [ ] **Step 1: Write the tests.**
  - `NonInteractive_RejectsAcknowledge`: domain `"HMI"` with one acknowledge definition throws `ArgumentException`. `ParamName` is `"definitions"`, and the message starts with `HMI guards cannot require acknowledgement.`
  - `NonInteractive_ReturnsDefinitionsInOrder`: block then info come back in that order.
  - `NonInteractive_RejectsDuplicateIds`: two definitions with the same id throw `ArgumentException`.
  - `NonInteractive_RejectsPipelineGuardId`: id `WriteGuardCatalog.PartialWriteGuardId` throws `ArgumentException`.
  - `NonInteractive_RejectsNullDefinition`: `new WriteGuardDefinition?[] { null }` throws `ArgumentException`, not `NullReferenceException`.
- [ ] **Step 2: Add `NonInteractive` as a pass-through that returns `Array.AsReadOnly(definitions.ToArray())`, then run the tests.**
  - Run: `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~WriteGuardCatalogTests`
  - Expected: 12 passed and 4 failed. The four rejection tests fail. The order test and the nine existing test methods pass; the existing methods are 10 cases, because `BlankId_Throws` has two rows.
- [ ] **Step 3: Implement the helper as specified, then run the tests again.**
  - Run: same filter.
  - Expected: all 16 PASS.
- [ ] **Step 4: Commit:** `feat: add WriteGuardCatalog.NonInteractive`

**Task test command:** `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~WriteGuardCatalogTests`

### Task 2: All four domains use the helper

**Files:**
- Modify: `TiaMcpServer/Network/NetworkGuardDefinitions.cs`. Make the `Definitions` property (:9-13) use the helper, replace the `"info"`/`"block"` literals at :11-12, and delete `Validate` (:15-22).
- Modify: `TiaMcpServer/Plc/PlcGuardDefinitions.cs`. Use the helper at :19 and delete `Validate` (:33-40).
- Modify: `TiaMcpServer/Hmi/HmiGuardDefinitions.cs` (:10) and `TiaMcpServer/Vci/VciGuardDefinitions.cs` (:13).
- Test: delete `NetworkGuardedWriteDomainTests.NetworkAcknowledgeRegistration_IsRejected` (:105-107) and `PlcGuardDefinitionsTests.AcknowledgeRegistrationIsRejected` (:33-40). They are the only callers of the deleted methods, and Task 1's tests replace them.

**Interfaces:**
- Consumes (Task 1): `WriteGuardCatalog.NonInteractive(IEnumerable<WriteGuardDefinition>, string)`.
- Consumes (existing): `WriteGuardSeverities.Info` and `WriteGuardSeverities.Block`.
- Produces: the removal of `NetworkGuardDefinitions.Validate` and `PlcGuardDefinitions.Validate`.

- [ ] **Step 1: Record a green baseline.**
  - Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~GuardDefinitionsTests|FullyQualifiedName~NetworkGuardedWriteDomainTests|FullyQualifiedName~WriteGuardCatalogTests|FullyQualifiedName~PlcGuarded"`
  - Expected: PASS. Record the count.
- [ ] **Step 2: Delete the two per-domain acknowledge tests.**
- [ ] **Step 3: Switch each domain to the helper.**
  - Network: `Definitions = WriteGuardCatalog.NonInteractive(new[] {…}, "Network")`, with the severity constants. Delete `Validate`.
  - PLC: `NonInteractive(…, "PLC")`. Delete `Validate`.
  - HMI: `NonInteractive(…, "HMI")`.
  - VCI: `NonInteractive(…, "VCI")`.
- [ ] **Step 4: Rerun, then sweep.**
  - Run the same filter. Then run:
    - `git grep -n "GuardDefinitions.Validate" -- '*.cs'`
    - `git grep -nE '"(info|block)"' -- TiaMcpServer/Network/NetworkGuardDefinitions.cs`
    - `git grep -c "NonInteractive(" -- TiaMcpServer/Network/NetworkGuardDefinitions.cs TiaMcpServer/Plc/PlcGuardDefinitions.cs TiaMcpServer/Hmi/HmiGuardDefinitions.cs TiaMcpServer/Vci/VciGuardDefinitions.cs`
  - Expected: PASS, with the baseline count minus 2. The first two greps print nothing. The last one reports `1` for each of the four files.
- [ ] **Step 5: Commit:** `refactor: route data-write guard sets through NonInteractive`

**Task test command:** the Step 1 filter.

### Task 3: Docs and evidence

The controller supplies the branch-verification count. Use it as given; do not run or invent it.

**Files:**
- Modify: `docs/ARCHITECTURE.md`, section "Guards and acknowledgement".
- Modify: `docs/superpowers/plans/2026-10-17-codebase-simplification-review.md`, item C-4.

**Interfaces:** none.

- [ ] **Step 1: ARCHITECTURE.md.** Add one sentence: Network, PLC, HMI and VCI guard sets are built with `WriteGuardCatalog.NonInteractive`, so an acknowledge guard there fails at type initialisation; only lifecycle uses acknowledge.
- [ ] **Step 2: Review plan.** Add a C-4 evidence sub-bullet in the B-9 format:
  - why the static helper was chosen over pipeline enforcement;
  - the tests that were replaced;
  - the full-suite count;
  - "No live TIA verification was performed; runtime guard behaviour is unchanged."
- [ ] **Step 3: Check.**
  - Run: `git grep -n "NonInteractive" -- docs/ARCHITECTURE.md`
  - Expected: one hit, in "Guards and acknowledgement".
- [ ] **Step 4: Commit:** `docs: record C-4 guard rule and evidence`

**Task test command:** the Step 3 grep.
