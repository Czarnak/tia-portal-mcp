# E-1 / E-2 Worker Context Gates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Status:** Implemented, independently reviewed and offline-qualified on 2026-10-25. The maintainer approved and committed the plans as `0f7162c`, then authorized isolated implementation and an E-1/E-2 branch push/PR if offline qualification sufficed. No live TIA testing, remote merge or deployment is authorized. See the [execution record](#execution-record-2026-10-25).

**Goal:** Consolidate worker project-open policy and active-context checks while preserving each caller's binding, failure, and container-kind behavior.

**Architecture:** Add one pure worker-side `ProjectOpenGate.Require` that reuses `ProjectOpenPolicy` and throws categorized failures. Retain the session's already-parsed access mode for lifecycle guidance, then strengthen `WithActiveContext` and remove the duplicated subnet wrapper. Keep a narrow response adapter where the qualification probe requires its existing return/normalization path; introduce no generic wrapper framework.

**Tech Stack:** C#, .NET Framework 4.8 worker, .NET 10 source-linked xUnit tests, existing Siemens V21 references/stubs, PowerShell.

**Spec:** [Authoritative simplification review, E-1/E-2](2026-10-17-codebase-simplification-review.md) and [findings appendix, WORKERCORE-6/7](2026-10-17-codebase-simplification-review-findings.md). Read the verifier-corrected proposals; the original one-flag gate proposal is insufficient.

**Sibling plans:** [E-4 typed network connections](2026-10-25-e4-typed-network-connections.md); [E-5 shared network vocabulary](2026-10-25-e5-shared-network-vocabulary.md).

## Global Constraints

- Baseline observed for planning: clean `main` at `7c04b44`. The coordinator must record and revalidate the approved base before execution; do not silently adopt a newer base.
- Windows/TIA Portal V21; Siemens calls remain in the net48 worker. No dependencies, public tool changes, wire fields, implicit opens/binds, or lifecycle-confirmation changes.
- Status may report no project; content requires an active context. Preserve `access_denied`, `binding_conflict`, and `worker_operation_failed` distinctions.
- Preserve freshness checks, capability enforcement, Portal-loss mapping, local-session subnet support, standalone-only VCI, and standalone-only internal qualification probes.
- Leave IO qualification remapping and catalog project requirements unchanged. Do not retire probes or split `Program.cs` here.
- No remote pushes, PRs, remote merges, or live actions are authorized by this plan. Any later live smoke needs exact target and mutation/restoration authorization.

## Ownership, Worktrees, and Integration

| Workstream | Owner scope | Branch | Managed worktree name |
|---|---|---|---|
| A: E-1/E-2 | One worker owns the files listed below; two sequential implementation commits | `refactor/worker-context-gates` | `e1-e2-worker-context-gates` |
| B: E-4 | Typed connections and `NetworkDeviceConfigurator` | `refactor/typed-network-connections` | `e4-typed-network-connections` |
| C: E-5 | Shared constants and other consumers; defer Configurator substitutions until B is integrated | `refactor/shared-network-vocabulary` | `e5-shared-network-vocabulary` |

All three start from the same coordinator-recorded approved base. At execution time, use `superpowers:using-git-worktrees`, call `list_artifacts`, then use native managed `create_worktree` as needed. Use its returned absolute workspace as every command's CWD, and create the named branch there. Do not create worktrees during draft review.

The reviewed plans are committed at `0f7162c` and are present in each execution worktree. Read that approved copy and preserve unrelated working changes; do not copy the coordinator's entire checkout. Implementation and evidence commits follow the maintainer's later execution authorization.

The workspace is shared. Preserve and accommodate other agents' changes; edit only the owner file set. The coordinator owns `docs/README.md`, original review status, plan evidence updates, integration, final review, and one machine-wide build/test slot, including focused tests. Request that slot before every build/test command; never overlap checks across worktrees.

Default local integration order is **A -> B -> C**. A has no semantic dependency on B/C. B must precede C's finalization: rebase/review C onto integrated B, then finish its Configurator substitutions. Test-project edits are a mechanical coordination point; merge additions without dropping others' links. Do not start E-6's `Program.cs` split concurrently with A.

## Owner File Map

Paths are relative to the returned A worktree; line anchors describe `7c04b44` and must be rechecked.

| Action | File | Responsibility |
|---|---|---|
| Create | `TiaMcpServer.OpennessWorker/Openness/Project/ProjectOpenGate.cs` | Pure categorized gate |
| Modify | `TiaMcpServer.OpennessWorker/Openness/Project/TiaPortalSession.cs` | Retain parsed mode; correct no-project guidance |
| Modify | `TiaMcpServer.OpennessWorker/Openness/Project/ProjectLifecycleService.cs` | Shared status/lifecycle gate calls and accurate comments |
| Modify | `TiaMcpServer.OpennessWorker/Program.cs` | Gate wiring, Portal guarantee, remove subnet wrapper |
| Modify | `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` | Explicit source link for the new gate |
| Create | `TiaMcpServer.Tests/Worker/ProjectOpenGateTests.cs` | Executable decision/flag/category/mode matrix |
| Modify | `TiaMcpServer.Tests/Worker/ActiveProjectContextSessionTests.cs` | Actual-mode and no-owner exception behavior |
| Modify | `TiaMcpServer.Tests/Worker/ActiveProjectContextSourceTests.cs` | Freshness/container wiring assertions |
| Modify | `TiaMcpServer.Tests/Worker/WorkerOpenPolicySourceTests.cs` | One gate; no implicit project opening |
| Modify | `TiaMcpServer.Tests/Network/NetworkSubnetLifecycleWorkerDispatchTests.cs` | Shared-wrapper subnet dispatch |
| Modify if affected | `TiaMcpServer.Tests/Worker/TiaPortalSessionBindingGuidanceSourceTests.cs` | Guidance follows its new gate location |
| Modify | `AGENTS.md` | Clarify the `WithEngineeringRoot` sentence for Portal-aware handlers |

Do not change Contracts, FakeWorker, stubs, network configurator/services, coverage policy, or product project files. Existing `LifecycleSecondIdentityValidationContractTests`, `ProjectMetadataWorkerContractTests`, `ProjectCapabilityCatalogTests`, `IoSystemQualificationWorkerContractTests`, and `VciWriterTests` are regression checks, not permission to expand the file set.

## Review Focus

1. No-project status succeeds with `IsOpen=false`; content rejects before its body (Task 1 flag matrix and source wiring).
2. A divergent project is always `binding_conflict`; no allowance permits switching (Task 1 all-mode/category matrix).
3. Real read-only mode must never offer unavailable `open_project` guidance; no-owner lifecycle exceptions keep their type (Task 1 mode/session checks).
4. Changed/stale identity must fail before handler invocation; Portal-only/no-context state must not execute content (Task 2 source ordering plus existing executable session tests).
5. Subnets remain available on local sessions, while VCI and mutation probes retain their restrictions; IO probe normalization stays intact (Tasks 1/2 source, capability, and VCI regressions).

---

### Task 0: Prepare the shared execution baseline

**Owner:** Coordinator; shared setup for A, B, and C, not a dependency on completing A's implementation.

**Interfaces:** Consumes the three reviewed plans and one approved full base SHA; produces recorded baseline evidence and the three isolated worktree paths described above.

- [x] After implementation is authorized, revalidate the base and working changes, create or attach the three isolated managed worktrees, and record each absolute path/branch/base. Reviewed draft documentation may remain uncommitted in the coordinator checkout; preserve it and all unrelated work.
- [x] Before any implementation edit, acquire the single machine-wide slot and run these existing baseline checks once on the common approved base:

```powershell
dotnet build TiaMcpServer.slnx -m:1 --configuration Release /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests --configuration Release --no-build --filter "FullyQualifiedName~ProjectOpenPolicyTests|FullyQualifiedName~ActiveProjectContextSessionTests|FullyQualifiedName~NetworkDeviceCreatorWorkerContractTests|FullyQualifiedName~NetworkMutationVerificationTests|FullyQualifiedName~NetworkSparseConfigurationTests|FullyQualifiedName~McpToolSchemaTests|FullyQualifiedName~ToolOutputContractConformanceTests"
```

- [x] Record the base, commands, nonzero test count, failures/skips and warnings. Require successful commands and no unexplained failures/skips before implementation; a restore/toolchain failure is unresolved evidence. Reuse this baseline for all three identical starting trees, and refresh only when the approved base changes. Do not reproduce historical full-suite counts solely for setup.

### Task 1: Consolidate the project-open gate (E-1)

**Interfaces:**

- Produce `internal static void ProjectOpenGate.Require(string? currentPath, string? requestedPath, McpAccessMode mode, bool allowRequestedNotOpen, bool allowNoProject)` in the worker Project namespace.
- Produce read-only `internal McpAccessMode TiaPortalSession.AccessMode { get; }`, assigned from the mode already parsed in its constructor; no second parse and no new constructor option.
- Keep lifecycle signatures unchanged. Keep `EnsureRequestedContext(TiaPortalSession session, string? projectPath)` and `ResolveProjectForRead(TiaPortalSession session, string? requestedProjectPath, McpAccessMode mode)`.

- [x] **Step 1: Add executable red tests and link the gate.** Add the new gate's explicit `Compile Include` beside linked Project files. If needed to reach behavioral red, add only its declared signature throwing `NotImplementedException`; do not treat compilation failure as proof of the desired behavior.

Use all three `McpAccessMode` values. Add these named cases, with the following assertions:

```csharp
// Gate_RequestedNotOpen_IsIndependentOfNoProjectAllowance
var ex = Assert.Throws<WorkerOperationException>(() =>
    ProjectOpenGate.Require(null, @"C:\Fixture\A.ap21", mode, false, true));
Assert.Equal(WorkerFailureCategories.AccessDenied, ex.FailureCategory);
Assert.Equal(ProjectOpenPolicy.NotOpenMessage(@"C:\Fixture\A.ap21", mode), ex.Message);
// Gate_NoProject_IsIndependentOfRequestedNotOpenAllowance
ex = Assert.Throws<WorkerOperationException>(() =>
    ProjectOpenGate.Require(null, null, mode, true, false));
Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, ex.FailureCategory);
Assert.Equal(ProjectOpenPolicy.NoProjectOpenMessage(mode), ex.Message);
```

Also assert: `(false,false)` rejects both null/no-request and null/requested; `(false,true)` permits null/no-request but rejects null/requested; `(true,true)` permits both; `(true,false)` permits the explicitly allowed requested-not-open case but rejects null/no-request. For every allowance combination, different attached/requested paths throw `binding_conflict` with `RefusalMessage`; same path, case-only variants, and attached/no-request pass. Assert message `open_project` presence equals `mode != McpAccessMode.ReadOnly`.

In `ActiveProjectContextSessionTests`, add `AccessMode_RetainsConstructorParse` asserting `session.AccessMode == WorkerOperationAuthorization.ParseAccessMode(Environment.GetCommandLineArgs())`. Add `NoOwner_PreservesInvalidOperationAndActualModeGuidance` asserting `Assert.Throws<InvalidOperationException>(() => session.RequireStandaloneOwner())` and exact `NoProjectOpenMessage(session.AccessMode)` equality. Reuse the existing disconnected/session fixture; do not add production test options.

- [x] **Step 2: Have the coordinator run the focused red command in A's absolute CWD.** Expect failing gate/session assertions, then capture the specific failure before implementation.

```powershell
dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~ProjectOpenGateTests|FullyQualifiedName~ActiveProjectContextSessionTests" /p:UseTiaPortalReferenceStubs=true
```

- [x] **Step 3: Implement the gate and actual-mode guidance.** Reuse `ProjectOpenPolicy.Decide` and its existing message helpers. `RequestedNotOpen` returns when explicitly allowed; otherwise throw `access_denied`. `Refuse` always throws `binding_conflict`. For `UseAttached` with no current project, throw `worker_operation_failed` only when `allowNoProject` is false. The gate performs no connection, open, identity, or Siemens calls.

Retain `InvalidOperationException` in `RequireStandaloneOwner`'s no-context branch; change only its message's mode source. Retain `target_kind_unsupported` for a local owner.

- [x] **Step 4: Replace the three production policy implementations.** `WithActiveContext` uses `(false,false)` and `_accessMode`; `EnsureRequestedContext` uses `(false,true)` and `session.AccessMode`; `ResolveProjectForRead` uses `(true,true)` and its existing `mode`. Preserve `EnsureConnected` and second identity validation ordering; status still returns `session.ActiveContext`, including null.

Keep `EnsureRequestedProjectOpen` only as a thin adapter around the same strict gate for existing response-return callers: catch only `WorkerOperationException` and return `Failure(exception.FailureCategory, exception.Message)`; remove its decision switch. This preserves the IO probe's unavailable-project response followed by `NormalizeSessionResponse`. Do not route that probe through the common context wrapper or alter its outer catches. Catalog requirements stay unchanged. `Program.HandleLine`'s existing categorized catch handles direct gate exceptions.

- [x] **Step 5: Update focused source assertions.** Pin the shared gate/flags and retained no-open behavior instead of three `Decide` implementations. Assert constructor mode retention and no hardcoded `ReadWrite` in the touched guidance paths. Preserve lifecycle identity-adjacency tests and metadata/basic-status behavior; do not weaken assertions to filename presence alone.

- [x] **Step 6: Request the machine-wide slot and run the focused green checks.** Require all tests passing, with no new skips; the gate's executable matrix must pass independently of Program source assertions.

```powershell
dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~ProjectOpenGateTests|FullyQualifiedName~ProjectOpenPolicyTests|FullyQualifiedName~ActiveProjectContext|FullyQualifiedName~WorkerOpenPolicySourceTests|FullyQualifiedName~TiaPortalSessionBindingGuidanceSourceTests|FullyQualifiedName~LifecycleSecondIdentityValidationContractTests|FullyQualifiedName~ProjectMetadataWorkerContractTests|FullyQualifiedName~IoSystemQualificationWorkerContractTests" /p:UseTiaPortalReferenceStubs=true
dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true
git diff --check
```

- [x] **Step 7: Review the owner diff and commit E-1 after authorized execution and successful checks.** Stage only Task 1 implementation/test files; leave this draft and others' changes unstaged. Commit message: `refactor: centralize worker project-open policy`.

### Task 2: Consolidate the active-context wrapper (E-2)

**Interfaces:** Consume Task 1's strict gate and retained access mode. Keep `WithActiveContext(WorkerRequest request, Func<WorkerTiaPortalSession, WorkerResponse> body)` and `WithEngineeringRoot` signatures unchanged. The body receives a session with both active context and Portal checked after freshness validation.

- [x] **Step 1: Add failing source-contract assertions.** In `ActiveProjectContextSourceTests`, assert this order: strict gate -> `ValidateExpectedAfterProjectResolution` -> active-context/Portal null guard -> `return body(session)`. Assert `WithEngineeringRoot` delegates to `WithActiveContext`, `WithSubnetLifecycleProject` is absent, and `WriteVci` uses the common wrapper. In `NetworkSubnetLifecycleWorkerDispatchTests`, assert create/update/delete-subnet use `WithActiveContext` and `session.EngineeringRoot`, with no `RequireStandaloneOwner` in those handlers. Assert the subnet probe still requires a standalone owner. Retain the IO probe normalization and custom catches.

- [x] **Step 2: Request the test slot and run the focused red checks.** Expect missing Portal guard/remaining subnet wrapper assertions to fail.

```powershell
dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~ActiveProjectContextSourceTests|FullyQualifiedName~NetworkSubnetLifecycleWorkerDispatchTests" /p:UseTiaPortalReferenceStubs=true
```

- [x] **Step 3: Apply the smallest wrapper change.** In `Program.cs`, strengthen the post-validation guard to `session.ActiveContext is null || session.TiaPortal is null`, retaining `worker_operation_failed` and mode-aware guidance. Route create/update/delete-subnet and `ProbeSubnetLifecycleMutations` through `WithActiveContext`; remove their duplicate checks and `WithSubnetLifecycleProject`. Preserve the probe's `RequireStandaloneOwner().Project` and all handler arguments. `WriteVci` already uses the wrapper and requires no dispatch rewrite. Leave IO qualification and catalog behavior as established in Task 1.

- [x] **Step 4: Clarify AGENTS.md's PR6/content sentence.** State that content handlers use `WithEngineeringRoot`, or `WithActiveContext` when they also need the Portal handle for native access/transactions; capability gates still determine eligible container kinds. Do not change the documented support matrix.

- [x] **Step 5: Request the slot and run green focused checks.** Require all passing with no new skips. Existing executable session tests exercise stale-path/identity clearing; `VciWriterTests` pins local-session VCI rejection. Source tests establish Program wiring, not native execution.

```powershell
dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~ProjectOpenGateTests|FullyQualifiedName~ActiveProjectContext|FullyQualifiedName~NetworkSubnetLifecycleWorkerDispatchTests|FullyQualifiedName~ProjectCapabilityCatalogTests|FullyQualifiedName~IoSystemQualificationWorkerContractTests|FullyQualifiedName~VciWriterTests" /p:UseTiaPortalReferenceStubs=true
git diff --check
```

- [x] **Step 6: Complete the coordinator-run serial qualification below, review the diff, then commit E-2.** Stage only Task 2 implementation/test/AGENTS files. Commit message: `refactor: consolidate worker active-context dispatch`. No plan staging or remote write.

### Task 3: Qualification, Review, and Handoff

The coordinator owns these steps and records evidence outside this draft until plan evidence updates are authorized.

- [x] **Step 1: Acquire the single machine-wide build/test slot and run serial qualification in A's absolute CWD.** Use the pinned `dotnet` SDK, and run each command only after the preceding one finishes.

```powershell
dotnet build TiaMcpServer.slnx -m:1 --configuration Release /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build --configuration Release --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory TestResults/worker-context-gates --filter "Category!=Packaging"
$reports = @(Get-ChildItem -LiteralPath TestResults/worker-context-gates -Recurse -Filter coverage.cobertura.xml)
if ($reports.Count -ne 1) { throw "Expected exactly one fresh Cobertura report." }
./scripts/verify-coverage-threshold.ps1 -CoveragePath $reports[0].FullName -MinimumLineRate 0.80
git diff --check
```

Use an empty, uniquely recorded results directory if that path already contains a report. If Coverlet needs a writable temp directory, set process-local `TEMP`/`TMP` under this worktree's ignored `TestResults/.../temp`, restoring them afterward. A zero-hit or stale report is invalid evidence.

The ordinary suite/80% gate above uses existing CI scope. `TiaMcpServer.Tests/coverage.runsettings` excludes worker namespaces, including linked worker code; do not claim this measures the new gate. Report executable gate/session tests separately. `Program.cs` and full lifecycle service are not compiled into the net10 test assembly; their source assertions are wiring evidence only. Do not add a coverage framework or production abstractions solely to raise the number.

- [x] **Step 2: Compile against installed V21 references when available.** Under the same slot, run `dotnet build TiaMcpServer.slnx -m:1 --configuration Release /p:UseTiaPortalReferenceStubs=false /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"`. This is compile evidence, not live acceptance. A's new test-project link alone does not trigger packaging; run Packaging only if the actual diff touches an AGENTS-listed product/packaging trigger.

- [x] **Step 3: Obtain independent review of A's cumulative diff.** Review two-flag semantics, category/message preservation, IO probe normalization, identity order, local-session subnet capability, standalone VCI/probes, explicit test linkage, and unrelated-file exclusions. Resolve findings with narrow checks and a separate fix commit after validation.

- [x] **Step 4: Hand A to the coordinator for default local integration A -> B -> C.** Record exact base/head, commits, checks, warnings, skipped native cases, and worker-coverage limitation. Coordinator handles docs index, original review status, integration checks and plan evidence; owners do not edit sibling workstreams or commit draft plans. After C's final handoff, run the combined Release stub build, ordinary suite/coverage gate, and E-4-triggered stub provenance/packaging checks once for the final source candidate; reuse evidence if that exact candidate was already qualified.

- [ ] **Step 5: Only after separate target-specific authorization, run a serialized disposable-project smoke.** Cover standalone status with no project (`IsOpen=false`), a bound content read, subnet round trip including a bound local session, and representative standalone VCI write. Verify wrong/stale binding refusal before mutation; retain exact target/session provenance and preservation/restoration evidence. No live step is authorized now, and offline/source checks cannot be reported as native acceptance.


## Execution record 2026-10-25

The maintainer approved the three plans, committed them locally as `0f7162c6090799d9d12358f94089ed345f3005bb`, and authorized isolated implementation while stopping before every live TIA test. All three worktrees started from that exact base. A used branch `refactor/worker-context-gates` in managed worktree `e1-e2-worker-context-gates`; the main checkout was preserved.

### Implementation and review

- E-1: `d4637435c8c2c0562b302f583bab37fbdf16c852` (`refactor: centralize worker project-open policy`). One pure two-allowance gate, immutable actual session mode, and the retained narrow IO response adapter replace the policy copies. The executable red run had 44 expected failures; the focused green run passed 208 tests.
- E-2: `4f9c3a1fd11d36bb65cff3db036862ce1a9ac5af` (`refactor: consolidate worker active-context dispatch`). The common wrapper checks active context and Portal after fresh identity validation; ordinary subnet operations retain local-session support, while VCI/probes retain their existing restrictions. Four expected source-contract failures preceded the change; 253 focused tests passed afterward.
- Independent per-task and whole-workstream reviews found no actionable source findings. A focused exception-boundary follow-up confirmed that direct gate exceptions preserve category/message and failure normalization; the IO probe retains its separate normalization path. The committed E-2 diff exactly matched the frozen reviewed/tested snapshot.

### Offline qualification

| Check | Observed result |
| --- | --- |
| Shared base Release stub build and focused baseline | Build passed; 233/233 tests, zero failures/skips |
| Cumulative A Release stub solution build (`-m:1`) | Passed, zero errors; seven existing nullable warnings in unchanged Network/HMI test-linked sources |
| Ordinary suite with CI coverage settings, `Category!=Packaging` | 7,676/7,676 passed, zero failures/skips; reported test duration 7 min 23 sec |
| Existing scoped line-coverage gate | 12,973/13,642 lines, 95.09%; exceeds 80% |
| Installed-V21-reference Release solution build | Passed, zero errors; the same seven existing nullable warnings |
| Diff/whitespace and snapshot identity | Passed; no schema snapshot or public tool-surface change |

Logs and collector output are retained under the ignored `TestResults/e1-e2-qualification-20261025T193339821/` in A's worktree. Process-local `TEMP`/`TMP` were set under ignored test results and restored. The optional TRX logger produced a byte-identical attachment copy of the Cobertura report; both SHA256 hashes matched, and the 80% gate was run on the direct collector report without rerunning the passing suite. The extra file was logger output, not a second coverage run.

The existing coverage configuration excludes worker namespaces, including the new source-linked gate; 95.09% is the CI scope, not worker/native coverage. Gate/session tests are executable managed evidence. Program/lifecycle source assertions establish wiring only. Packaging was not required for A: its only project-file change is the test project's source link, with no AGENTS-listed product/packaging trigger.

### Acceptance and delivery boundary

Offline qualification is sufficient for this bounded E-1/E-2 refactor: it adds no native engineering call, transaction behavior, mutation argument or container capability. Independent whole-workstream review found no concrete new native uncertainty requiring live testing before publishing the PR. The maintainer's conditional A push/PR authorization therefore applies. Native mutation/restoration, plant behavior, live fault locations and deployment remain unverified and unclaimed; the future smoke step above was not executed.

A's source is ready for the coordinator's A -> B -> C local integration. The mandatory B-before-C Configurator handoff and combined final qualification belong to the sibling plans. No B/C remote write or remote merge follows from A's delivery permission.
