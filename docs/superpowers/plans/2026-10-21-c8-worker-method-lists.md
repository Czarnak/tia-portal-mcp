# C-8 Shared HMI/VCI Worker Method Lists Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** HMI worker method names come from shared Contracts lists (`HmiReadMethods.All`/`HmiWriteMethods.All`). The access policy, the capability catalog and the worker dispatch all read them, as VCI already does.

**Architecture:**
- Add one Contracts list, `HmiReadMethods`.
- `OperationPolicyCatalog` registers HMI with loops.
- `ProjectCapabilityCatalog` splices `HmiWriteMethods.All` in at the same position, so `Describe()` stays identical.
- The worker's `Program.cs` switch replaces its literal `or` chains with list guard arms.

**Tech Stack:** netstandard2.0 Contracts, net48 worker, net10.0 host, xUnit.

**Spec:** review plan item C-8 ([review](2026-10-17-codebase-simplification-review.md); findings WORKERCORE-8, CONTRACTS-3) and the [Workstream C plan](2026-10-21-workstream-c-plan.md).

**Branch:** `refactor/c8-worker-method-lists`, in worktree `.worktrees/C-8`.

**Execution:** inline (executing-plans) for Tasks 1–5, then branch verification and one fresh reviewer, then the maintainer-gated live smoke, then Task 6 (docs).

## Global Constraints

- **`Describe(kind)` stays identical** for all three container kinds: the same 53 rows, in the same order, with the same applicability. That includes the 9 raw `hmi_*` names right after `save_project`, kept by maintainer default on 2026-10-21. `ProjectContextPayloadContract` (`TiaMcpServer/Worker/ProjectContextPayloadContract.cs` :24-31) rejects a local-session context whose capabilities differ from the host's `Describe(LocalSession)`.
- **`OperationFor` keeps its `hmi_` prefix rule and its signature.**
- **The inner literal switches stay:** `HmiReadDispatch.cs` :27-59, `HmiWriteDispatch.cs` :15-23 and `VciDispatch.Read`. Each `_` arm throws `validation_error`, so a mis-route fails closed.
- **No `projectPath` work.** That belongs to C-0.
- **Shell:** run `dotnet` from PowerShell, never Bash.
- **Build:** `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true`. Expect no warnings beyond the seven existing test-assembly nullable warnings.
- **Tests:** run only each task's focused filters. Do not run the full suite per task; the orchestrator runs it once per branch, one worktree at a time. Contracts is a `ProjectReference`. `Program.cs` is not linked into the tests.
- **Commits:** commit after each task, as a one-line conventional commit with no body. Expected: one new commit and a clean `git status`.
- **Docs come last.**
- **No TIA project names or paths** in the repo.

## Review Focus

1. **Wrong handler.** A guard arm that routes a list to the wrong handler (for example `HmiReadMethods.All → WriteHmi`) compiles and passes every offline test. Only the live smoke catches it. Not testable offline.
2. **Initializer order.** `ProjectContentOperations` must stay declared above `Standalone`/`Local`/`Server`. Otherwise `Concat(null)` throws `TypeInitializationException`. Pinned by `Describe_MatchesMainSnapshot` (Task 1, rerun in Task 4).
3. **Silent overwrites.** `dict[m] =` silently overwrites duplicate keys. Pinned by `HmiPolicyKeys_AreExactlyTheCatalogWorkerMethods` (Task 1) and `SharedReadAndWriteLists_AreDisjoint` (Task 2).
4. **List drift.** `HmiReadMethods.All` can drift from the host `HmiOperationCatalog.Specs`. Pinned by `WorkerMethods_EqualSharedReadList` (Task 2).
5. **Access split.** HMI writes must stay denied in read-only and allowed in read-write and full. Pinned by `HmiWriteCatalogTests.AccessMode_UsesSharedPolicy` (Task 3).

**Contract change:** none.

**Release note:** "Internal: HMI worker method names now come from shared Contracts lists, as VCI already does."

## After the tasks

These steps are not implementer tasks. They run after Task 5 and before Task 6.

### Branch verification (orchestrator, serialized across worktrees)

Run the stub build, then the full suite with coverage under `TestResults/c8`:
- process-local `TEMP`/`TMP` set to `TestResults/c8/temp`;
- `TiaMcpServer.Tests/coverage.runsettings`;
- the single-thread xUnit settings from `.github/workflows/ci.yml`.

Expected: 0 failed and 0 skipped, and `scripts/verify-coverage-threshold.ps1 -MinimumLineRate 0.80` passes.

Then the final whole-branch review follows, per the execution skill.

### Live dispatch smoke (maintainer-authorized, not an implementer task)

Setup:
- Build with the real V21 references (`/p:TiaPortalV21Dir=…`).
- Install the candidate per `docs/development/local-mcp-testing.md`, and record its SHA.
- Use a disposable standalone project copy that contains one Unified HMI, plus a scratch VCI root outside the project folder.
- Run in read-write mode and never save. The access mode only affects policy, which is pinned offline.

Each call below proves one switch arm:
1. `hmi_read [{list_hmi_devices}]` succeeds with at least one device (`ReadHmi`).
2. `hmi_write` creates tag table `C8_Smoke`, then deletes it. Both calls return `applied` with verification success (`WriteHmi`).
3. `vci_read [{list_workspaces}]` succeeds (`ReadVci`).
4. `vci_write dryRun:true [{create_workspace, workspaceName:"C8_Smoke", rootPath:<scratch>}]` returns `preview` (`PlanVci`).
5. `vci_write` creates workspace `C8_Smoke`, then deletes it. Both calls return `applied` (`WriteVci`).

Afterwards, discard the project copy and the scratch directory. Pass the SHA, the mode and each call's outcome to the Task 6 implementer.

---

### Task 1: Pin current behaviour (characterization; passes on main)

**Files:**
- Create: `TiaMcpServer.Tests/Multiuser/Snapshots/capabilities.json`, registered in the test csproj as `<None Include="Multiuser\Snapshots\capabilities.json" CopyToOutputDirectory="PreserveNewest" />`.
- Test: `TiaMcpServer.Tests/Multiuser/LocalSessionContextContractTests.cs`.
- Test: `TiaMcpServer.Tests/Safety/ReadOnlyModeTests.cs`.
- Test: `TiaMcpServer.Tests/Hmi/HmiWriteCatalogTests.cs`.

**Interfaces:**
- Consumes:
  - `ProjectCapabilityCatalog.Describe(string)` and `ProjectContainerKinds.*`. The rows have `Operation` and `Applicability`.
  - `OperationPolicyCatalog.AllOperationNames` and `GetCapability`.
  - `HmiOperationCatalog.OperationNames` and `WorkerMethod`.
  - `HmiWriteCatalog.OperationNames` and `HmiWriteMethods.All`.
- Produces: nothing new in production.

- [ ] **Step 1: Capture the snapshot.** On the unchanged code, write `capabilities.json`. It is a JSON object mapping each of the three `ProjectContainerKinds` values to its ordered array of `{operation, applicability}` pairs from `Describe(kind)`. Expected: 3 keys with 53 entries each. In each array, the 9 `hmi_*` names come right after `save_project`.
- [ ] **Step 2: Write the tests.**
  - `LocalSessionContextContractTests.Describe_MatchesMainSnapshot`: a `[Theory]` over the three kinds. The `(Operation, Applicability)` sequence of `Describe(kind)` equals the snapshot array for that kind.
  - `ReadOnlyModeTests.HmiPolicyKeys_AreExactlyTheCatalogWorkerMethods`:
    - The `hmi_`-prefixed `AllOperationNames` set-equals `(HmiOperationCatalog.OperationNames ∪ HmiWriteCatalog.OperationNames)` mapped through `HmiOperationCatalog.WorkerMethod`. That is 29 names.
    - Every write worker method has `GetCapability == ProjectMutation`.
  - `HmiWriteCatalogTests.WorkerMethods_EqualSharedWriteList`: `HmiWriteCatalog.OperationNames.Select(HmiOperationCatalog.WorkerMethod)` set-equals `HmiWriteMethods.All`, and the count is 9.
- [ ] **Step 3: Run the tests.**
  - Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Describe_MatchesMainSnapshot|FullyQualifiedName~HmiPolicyKeys_AreExactlyTheCatalogWorkerMethods|FullyQualifiedName~WorkerMethods_EqualSharedWriteList"`
  - Expected: PASS. These are characterization pins.
- [ ] **Step 4: Commit.** Message: `test: pin capability catalog and hmi policy keys`

**Task test command:** the Step 3 filter.

### Task 2: Shared read list

**Files:**
- Create: `TiaMcpServer.Contracts/Hmi/HmiReadMethods.cs`.
- Test: `TiaMcpServer.Tests/Hmi/HmiOperationCatalogTests.cs`. Add `using TiaMcpServer.Contracts.Hmi;`.

**Interfaces:**
- Produces: `public static class HmiReadMethods { public static IReadOnlyList<string> All { get; } }` in `TiaMcpServer.Contracts.Hmi`.
  - It holds 20 names, one per line, each `"hmi_"` + a key of `HmiOperationCatalog.Specs` (`TiaMcpServer/Hmi/HmiOperationCatalog.cs` :26-48), in that order. The list runs from `hmi_list_hmi_devices` to `hmi_validate`.
  - It uses the same `Array.AsReadOnly(new[] { … })` style as `HmiWriteMethods`.

- [ ] **Step 1: Write the tests.**
  - `WorkerMethods_EqualSharedReadList`: `HmiOperationCatalog.OperationNames.Select(HmiOperationCatalog.WorkerMethod)` set-equals `HmiReadMethods.All`, and `Assert.Equal(20, HmiReadMethods.All.Count)`.
  - `SharedReadAndWriteLists_AreDisjoint`: `HmiReadMethods.All.Concat(HmiWriteMethods.All)` has 29 distinct ordinal names.
- [ ] **Step 2: Create `HmiReadMethods` with a stub,** `public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(Array.Empty<string>());`.
  - Run: `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~HmiOperationCatalogTests`
  - Expected: both new tests FAIL on their assertions. The existing tests pass.
- [ ] **Step 3: Fill in the 20 names.**
  - Run: same filter.
  - Expected: all PASS.
- [ ] **Step 4: Commit.** Message: `refactor: add shared HMI read worker method list`

**Task test command:** the Step 2 filter.

### Task 3: Policy loops

**Files:** Modify `TiaMcpServer.Contracts/Safety/OperationPolicyCatalog.cs`:
- remove the literals at :109-128 (20 Observe) and :149-157 (9 ProjectMutation);
- add loops next to the VCI loops at :186-190;
- add `using TiaMcpServer.Contracts.Hmi;`.

**Interfaces:**
- Consumes (Task 2): `HmiReadMethods.All`.
- Consumes (existing): `HmiWriteMethods.All`.
- Produces: no new members.

- [ ] **Step 1: Replace the entries with loops.** Delete the 29 `hmi_*` entries, then add `foreach (var method in HmiReadMethods.All) dict[method] = OperationCapability.Observe;` and the matching `HmiWriteMethods.All` → `OperationCapability.ProjectMutation` loop.
- [ ] **Step 2: Run the tests.**
  - Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~ReadOnlyModeTests|FullyQualifiedName~AccessModeTierTests|FullyQualifiedName~HmiOperationCatalogTests|FullyQualifiedName~HmiWriteCatalogTests"`
  - Expected: PASS, including:
    - `HmiPolicyKeys_AreExactlyTheCatalogWorkerMethods`;
    - `WorkerMethods_EqualSharedWriteList`;
    - `EveryWorkerMethodIsObserveAndAllowedReadOnly`;
    - `AccessMode_UsesSharedPolicy` (9 denials in ReadOnly, 0 in ReadWrite, 0 in Full).
- [ ] **Step 3: Commit.** Message: `refactor: register hmi policy from shared lists`

**Task test command:** the Step 2 filter.

### Task 4: Capability splice

**Files:** Modify `TiaMcpServer.Contracts/Project/ProjectCapabilityCatalog.cs`, `ProjectContentOperations` at :40-52.

**Interfaces:**
- Consumes: `HmiWriteMethods.All`.
- Produces: no new members. `Describe()` output is unchanged.

- [ ] **Step 1: Rebuild `ProjectContentOperations`.** It becomes the head literal `{plc_write, network_write, hmi_write, compile_check, save_project}`, then `HmiWriteMethods.All`, then the tail literal `{update_block_logic … delete_subnet}`.
  - Keep the head literal on one line.
  - Keep the field declared above `Standalone`/`Local`/`Server`.
- [ ] **Step 2: Run the tests.**
  - Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~LocalSessionContextContractTests|FullyQualifiedName~LocalSessionCapabilityTests|FullyQualifiedName~ActiveProjectContextTests"`
  - Expected: PASS, including:
    - `Describe_MatchesMainSnapshot`;
    - `CapabilityCatalog_CoversEveryRegisteredProjectScopedOperation`;
    - `OperationFor_MapsWorkerMethodToCapability`;
    - `HmiWorkerMethods_AreSupported`.
- [ ] **Step 3: Commit.** Message: `refactor: splice hmi write list into capability catalog`

**Task test command:** the Step 2 filter.

### Task 5: Worker guard arms

**Files:** Modify `TiaMcpServer.OpennessWorker/Program.cs` :188-200. The usings for `Contracts.Hmi` and `.Vci` are already present.

**Interfaces:**
- Consumes the shared lists: `HmiWriteMethods.All`, `HmiReadMethods.All`, `VciMethods.Read`, `VciMethods.Plan` (a const) and `VciMethods.Write`. The arms must reference these lists, not local copies.
- Consumes the existing handlers: `WriteHmi`, `ReadHmi`, `ReadVci`, `PlanVci`, `WriteVci`.

- [ ] **Step 1: Replace the literal chains in `HandleLine`.** Remove them. Before the `_` arm and after `close_project`, add these arms in this order:
  - `HmiWriteMethods.All` → `WriteHmi`
  - `HmiReadMethods.All` → `ReadHmi`
  - `VciMethods.Read` → `ReadVci`
  - `VciMethods.Plan` → `PlanVci` (a constant pattern)
  - `VciMethods.Write` → `WriteVci`

  The list arms use `var m when <list>.Contains(m, StringComparer.Ordinal)`.
- [ ] **Step 2: Build and sweep.**
  - Run: the stub build, then `git grep -c '"hmi_' -- TiaMcpServer.Contracts TiaMcpServer.OpennessWorker/Program.cs`, then `git grep -c '"vci_' -- TiaMcpServer.OpennessWorker/Program.cs`.
  - Expected:
    - The build has 0 errors and no new warnings.
    - The `"hmi_` counts are `HmiReadMethods.cs` 20, `HmiWriteMethods.cs` 9 and `ProjectCapabilityCatalog.cs` 4. No other file matches.
    - The `"vci_` grep prints nothing.
- [ ] **Step 3: Commit.** Message: `refactor: dispatch hmi and vci worker methods by shared lists`

**Task test command:** the stub build command. `Program.cs` has no unit coverage; the live smoke covers its dispatch.

### Task 6: Docs and evidence

Dispatch this only after the live smoke. The controller passes in the branch-verification counts and the smoke results (SHA, mode, each call's outcome). Use those values; do not run or invent them.

**Files:**
- Modify: `AGENTS.md`, in the "HMI worker methods" bullet.
- Modify: `docs/ARCHITECTURE.md`, in the §"HMI reads (`hmi_read`)" paragraph that describes `hmi_<operation>` naming (around :595-600).
- Modify: `docs/superpowers/plans/2026-10-17-codebase-simplification-review.md`, item C-8.

**Interfaces:** none.

- [ ] **Step 1: Add one clause to both the `AGENTS.md` bullet and the `ARCHITECTURE.md` paragraph.** It says:
  - the worker method names live in `TiaMcpServer.Contracts/Hmi` (`HmiReadMethods.All`/`HmiWriteMethods.All`);
  - they feed `OperationPolicyCatalog`, `ProjectCapabilityCatalog` and the worker switch;
  - `HmiOperationCatalog.WorkerMethod` stays the public-to-worker map;
  - tests pin the two against each other.
- [ ] **Step 2: Add a C-8 evidence sub-bullet to the review plan, in the B-9 format.** Include:
  - the full-suite counts, with 0 skips;
  - "Describe output unchanged (snapshot-pinned)";
  - the smoke scope: SHA, mode, the five arms, nothing saved;
  - what was left untested.

  Do not include project names or paths.
- [ ] **Step 3: Check.**
  - Run: `git grep -c "HmiReadMethods" -- AGENTS.md docs/ARCHITECTURE.md`
  - Expected: at least one hit in each file.
- [ ] **Step 4: Commit.** Message: `docs: record C-8 shared worker method lists`

**Task test command:** the Step 3 grep.
