# C-8 Shared HMI/VCI Worker Method Lists Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** HMI worker method names come from shared Contracts lists, `HmiReadMethods.All` and `HmiWriteMethods.All`. The access policy, the capability catalog and the worker dispatch all use them, matching how VCI already works.

**Architecture:**
- Add one Contracts list, `HmiReadMethods`.
- `OperationPolicyCatalog` registers HMI with loops, the same way VCI does.
- `ProjectCapabilityCatalog` splices `HmiWriteMethods.All` in at the same position, so `Describe()` stays identical.
- The worker `Program.cs` switch replaces its literal `or` chains with list guard arms.

**Tech Stack:** netstandard2.0 Contracts, net48 worker, net10.0 host, xUnit.

**Spec:**
- Review plan item C-8: [review](2026-10-17-codebase-simplification-review.md), findings WORKERCORE-8 and CONTRACTS-3.
- [Workstream C plan](2026-10-21-workstream-c-plan.md)

**Branch:** `refactor/c8-worker-method-lists`, worktree `.worktrees/C-8`.

## Global Constraints

The [Workstream C global constraints](2026-10-21-workstream-c-plan.md#global-constraints-apply-to-every-wave-1-plan) apply, plus the following:

- **`Describe(kind)` must stay identical** for all three container kinds: the same 53 rows, in the same order, with the same applicability. That includes the 9 raw `hmi_*` names right after `save_project`. This was the maintainer default on 2026-10-21: keeping them means no contract change. The order matters on the wire, because `ProjectContextPayloadContract` (`TiaMcpServer/Worker/ProjectContextPayloadContract.cs` :24-31) rejects a local-session context whose capabilities differ from the host's `Describe(LocalSession)`.
- **`OperationFor` keeps its `hmi_` prefix rule and its signature.**
- **The inner literal switches stay.** They are in `HmiReadDispatch.cs` :27-59, `HmiWriteDispatch.cs` :15-23 and `VciDispatch.Read`. Each one maps a name to a distinct reader or writer, and each `_` arm throws `validation_error`, so drift fails closed.
- **No `projectPath` work in this PR.** That is C-0.

## Review Focus

1. **A guard arm that routes a list to the wrong handler still compiles and passes every offline test.** For example, `HmiReadMethods.All → WriteHmi`. `Program.cs` is not linked into the tests, even though `Openness/Hmi/*.cs` and `Openness/Vci/*.cs` are. Only the Task 6 smoke catches this. It fails closed, with `validation_error`.
2. **Static field order in `ProjectCapabilityCatalog`.** `ProjectContentOperations` must stay declared above `Standalone`, `Local` and `Server`. If it moves below them, `Concat(null)` throws a loud `TypeInitializationException`.
3. **`dict[m] =` silently overwrites duplicate keys.** The policy-key pin and the disjointness test catch stray, missing or overlapping names.
4. **`HmiReadMethods.All` duplicates the keys of the host `HmiOperationCatalog.Specs`.** The worker cannot see the host catalog, so the two lists cannot be derived from each other. `WorkerMethods_EqualSharedReadList` guards against drift.

**Contract change:** none. Release note: "Internal: HMI worker method names now come from shared Contracts lists, as VCI already does."

**Live verification:** a light dispatch smoke in read-write mode (Task 6). It is needed because the worker switch has no offline coverage. Read-write is enough: access mode only affects the policy, which is pinned offline in Task 1.

## Files

- Create `TiaMcpServer.Contracts/Hmi/HmiReadMethods.cs`. Contracts is a `ProjectReference`, so no csproj change is needed.
- Modify `TiaMcpServer.Contracts/Safety/OperationPolicyCatalog.cs`:
  - remove the literals at :109-128 (20 Observe) and :149-157 (9 ProjectMutation);
  - add loops next to the VCI loops at :186-190;
  - add `using TiaMcpServer.Contracts.Hmi;`.
- Modify `TiaMcpServer.Contracts/Project/ProjectCapabilityCatalog.cs`: `ProjectContentOperations` at :40-52.
- Modify `TiaMcpServer.OpennessWorker/Program.cs` :188-200.
- Tests:
  - `TiaMcpServer.Tests/Multiuser/LocalSessionContextContractTests.cs`
  - `Safety/ReadOnlyModeTests.cs`
  - `Hmi/HmiOperationCatalogTests.cs`
  - `Hmi/HmiWriteCatalogTests.cs`
- Docs (last):
  - `AGENTS.md`: the "HMI worker methods" bullet
  - `docs/ARCHITECTURE.md`: the HMI worker-method paragraph
  - the C-8 evidence in the review plan

## Interfaces

- Produced: `public static class HmiReadMethods { public static IReadOnlyList<string> All { get; } }` in `TiaMcpServer.Contracts.Hmi`.
  - It uses the same `Array.AsReadOnly(new[]{…})` style as `HmiWriteMethods`.
  - It holds 20 `hmi_`-prefixed names, from `hmi_list_hmi_devices` through `hmi_validate`, in `HmiOperationCatalog.Specs` order.
- Consumed:
  - `HmiWriteMethods.All`, `VciMethods.Read`, `VciMethods.Plan` (const) and `VciMethods.Write`
  - `HmiOperationCatalog.OperationNames`, `HmiOperationCatalog.WorkerMethod(string)` and `HmiWriteCatalog.OperationNames`
  - `OperationPolicyCatalog.GetCapability`, `OperationPolicyCatalog.AllOperationNames` and `ProjectCapabilityCatalog.Describe`

---

### Task 1: Pin current behaviour (characterization; passes on main)

- [ ] Add `LocalSessionContextContractTests.Describe_OperationOrderIsPinned`, a `[Theory]` over the three `ProjectContainerKinds`.
  - Expected value: one ordered literal of the 53 operation names, built in this order:
    - `BasicLocalOperations` (3)
    - `ProjectContentReadOperations` (11)
    - `ProjectContentOperations` (33, with the 9 `hmi_*` names after `save_project`)
    - `StandaloneLifecycleOperations` (4)
    - `StandaloneVciOperations` (2)
  - Applicability is already pinned per segment by existing tests.
- [ ] Add `ReadOnlyModeTests.HmiPolicyKeys_AreExactlyTheCatalogWorkerMethods`:
  - The `hmi_`-prefixed `AllOperationNames` must set-equal `(HmiOperationCatalog.OperationNames ∪ HmiWriteCatalog.OperationNames)` mapped through `WorkerMethod`. That is 29 names.
  - Every write worker method must have `GetCapability == ProjectMutation`.
- [ ] Add `HmiWriteCatalogTests.WorkerMethods_EqualSharedWriteList`: `HmiWriteCatalog.OperationNames.Select(WorkerMethod)` set-equals `HmiWriteMethods.All`, with a count of 9.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Describe_OperationOrderIsPinned|FullyQualifiedName~HmiPolicyKeys_AreExactlyTheCatalogWorkerMethods|FullyQualifiedName~WorkerMethods_EqualSharedWriteList"`. All of them pass.
- [ ] Commit `test: pin capability order and hmi policy keys`.

### Task 2: Shared read list

- [ ] Add two tests to `HmiOperationCatalogTests`:
  - `WorkerMethods_EqualSharedReadList`: set equality with `HmiReadMethods.All`, count 20.
  - `SharedReadAndWriteLists_AreDisjoint`: the concatenation of both lists has 29 distinct ordinal names.
- [ ] Create `HmiReadMethods` with an empty list. The two tests fail.
- [ ] Fill in the 20 names. Both tests pass.
- [ ] Commit `refactor: add shared HMI read worker method list`.

### Task 3: Policy loops

- [ ] In `BuildClassifications`, delete the 29 `hmi_*` entries. Next to the VCI loops, add loops that map `HmiReadMethods.All` to Observe and `HmiWriteMethods.All` to ProjectMutation.
- [ ] Run these and confirm they stay green:
  - `ReadOnlyModeTests` (including the Task 1 pin)
  - `AccessModeTierTests`
  - `HmiOperationCatalogTests.EveryWorkerMethodIsObserveAndAllowedReadOnly`
  - `HmiWriteCatalogTests.AccessMode_UsesSharedPolicy` (9 denials in ReadOnly, 0 in ReadWrite, 0 in Full)
- [ ] Commit `refactor: register hmi policy from shared lists`.

### Task 4: Capability splice

- [ ] Rebuild `ProjectContentOperations` as three parts in order:
  1. the head, `{plc_write … save_project}`;
  2. `HmiWriteMethods.All`;
  3. the tail, `{update_block_logic … delete_subnet}`.

  Keep it declared above `Standalone`, `Local` and `Server`.
- [ ] Run these and confirm they stay green:
  - `Describe_OperationOrderIsPinned`
  - `CapabilityCatalog_CoversEveryRegisteredProjectScopedOperation`
  - `OperationFor_MapsWorkerMethodToCapability`
  - `HmiWorkerMethods_AreSupported`
  - `LocalSessionCapabilityTests`
  - `ActiveProjectContextTests`
- [ ] Commit `refactor: splice hmi write list into capability catalog`.

### Task 5: Worker guard arms and offline verification

- [ ] In `HandleLine`, remove the literal chains at :188-200. Before the `_` arm and after `close_project`, add these arms in this order:
  - `HmiWriteMethods.All` → `WriteHmi`
  - `HmiReadMethods.All` → `ReadHmi`
  - `VciMethods.Read` → `ReadVci`
  - `VciMethods.Plan` → `PlanVci` (constant pattern)
  - `VciMethods.Write` → `WriteVci`

  The list arms use `var m when <list>.Contains(m, StringComparer.Ordinal)`.
- [ ] Build the stub solution. It should finish with 0 errors and no new warnings.
- [ ] Run the full suite with coverage under `TestResults/c8`.
- [ ] Run `git grep -c '"hmi_' -- TiaMcpServer.Contracts TiaMcpServer.OpennessWorker/Program.cs`. Expected results:
  - `HmiReadMethods.cs` 20
  - `HmiWriteMethods.cs` 9
  - `ProjectCapabilityCatalog.cs` 4
  - no hits in `OperationPolicyCatalog.cs` or `Program.cs`
- [ ] Run `git grep -c '"vci_' -- TiaMcpServer.OpennessWorker/Program.cs`. It should report no hits.
- [ ] Commit `refactor: dispatch hmi and vci worker methods by shared lists`.

### Task 6: Live dispatch smoke (needs maintainer authorization)

- [ ] Build with the real V21 references (`/p:TiaPortalV21Dir=…`). Install the candidate following `docs/development/local-mcp-testing.md`, and record its SHA.
- [ ] Set up a disposable standalone project copy that contains one Unified HMI, plus a scratch VCI root outside the project folder. Use read-write mode and never save. Each call below proves one switch arm:
  1. `hmi_read [{list_hmi_devices}]` succeeds with at least one device (`ReadHmi`).
  2. `hmi_write` creates tag table `C8_Smoke` and then deletes it. Both calls return `applied` with verification success (`WriteHmi`).
  3. `vci_read [{list_workspaces}]` succeeds (`ReadVci`).
  4. `vci_write dryRun:true [{create_workspace, workspaceName:"C8_Smoke", rootPath:<scratch>}]` returns `preview` (`PlanVci`).
  5. `vci_write` creates workspace `C8_Smoke` and then deletes it. Both calls return `applied` (`WriteVci`).
- [ ] Discard the project copy and the scratch directory. Do not write project names or paths into the repo.

### Task 7: Docs and evidence (last)

- [ ] In `AGENTS.md` (the "HMI worker methods" bullet) and in the matching `docs/ARCHITECTURE.md` paragraph, add one clause. It should say that the worker method names live in `TiaMcpServer.Contracts/Hmi` (`HmiReadMethods.All`/`HmiWriteMethods.All`) and feed `OperationPolicyCatalog`, `ProjectCapabilityCatalog` and the worker switch. It should also say that `HmiOperationCatalog.WorkerMethod` stays the public-to-worker map, with tests pinning the two against each other.
- [ ] Add a C-8 evidence sub-bullet to the review plan covering:
  - the test counts with 0 skips;
  - "Describe order and applicability unchanged (pinned)";
  - the smoke scope: SHA, mode, the five arms, no save;
  - what was left untested.
- [ ] Commit `docs: record C-8 shared worker method lists`.
