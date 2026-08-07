# Acceptance Test Report — Network Operations Phase 1

**Target:** `HEAD` plus the uncommitted working tree
**HEAD:** `f75b1197563b673a36c909c8b6d8f54446859434` (requested `f75b119`)
**Acceptance source:** `docs/superpowers/plans/2026-08-01-network-operations-phase1.md` — Global Constraints and Task 7 Steps 1–9, including the final correctness/security checklist
**Date:** 2026-08-01 16:48:48 Europe/Warsaw
**Runtime boundary:** No live TIA Portal operation was run.

## Results

| ID | Criterion | Type | Result | Exact evidence |
|---|---|---|---|---|
| AC-001 | Work in the current checkout; preserve unrelated work; no branch/worktree change. | Git/static | PASS | `git rev-parse HEAD` remained `f75b1197563b673a36c909c8b6d8f54446859434`; no checkout, branch, worktree, reset, staging, or commit command was run. Whole-plan review classified the observed paths as task-related. Formal file-list compliance is assessed separately by AC-021. |
| AC-002 | Follow TDD for every behavior change. | Process artifact | PASS | `.superpowers/sdd/network-phase1-task-{1..6}-report.md` all exist (6 reports); each contains fail-first/RED evidence and later PASS/GREEN evidence. Fresh final tests: 985/985 pass. |
| AC-003 | Remove the four network operations from generic batches with no aliases/adapters/rejection entries. | Static | PASS | Seven-file `Select-String` gate for `read_hardware_config\|search_equipment_catalog\|add_network_device\|configure_network_device` returned `MATCH_COUNT=0`, exit 0. |
| AC-004 | Keep per-operation `result` values as JSON strings. | Static/test | PASS | `OperationBatchResult` declares `string? Result`; focused FakeWorker test asserts `JsonValueKind.String`; focused suite passed 119/119. |
| AC-005 | Keep WorkerRequest, policy, worker client/dispatch, and existing Openness handlers unchanged. | Git | PASS | The required protected 8-file `git diff -- ...` produced 0 bytes. |
| AC-006 | Do not modify or validate unverified subnet/IO-system reflection calls. | Git/static | PASS | Protected `NetworkDeviceConfigurator.cs` diff is empty; no worker/Openness implementation changed. |
| AC-007 | Register `network_read` in both modes and `network_write` only in read-write mode. | Static/test | PASS | `Program.Main`: `WithTools<NetworkReadTools>()` precedes the mode branch; `WithTools<NetworkWriteTools>()` occurs only inside `if (accessMode == McpAccessMode.ReadWrite)`. Fresh focused schema/mode tests passed 119/119 and confirm 14 read-write / 4 read-only. |
| AC-008 | Bind one write token to exact ordered request, normalized common path, ordered targets, and one snapshot per attempt. | Static/test | PASS | `NetworkWriteTools` passes the unchanged `operations` array, resolved project path, ordered `BuildTargets(operations)`, and one state payload to preview/validation; catalog normalizes paths for common-path validation. `NetworkOperationFakeWorkerTests` passed under the approved stable-snapshot-payload/global-counter override; production exact hashing is unchanged. |
| AC-009 | Add no save, compile, transaction, rollback, exclusive-access, post-read, download, commissioning, or live-TIA behavior. | Static | PASS | Bounded identifier scan over the 12 new host source files returned `FORBIDDEN_PHASE1_BEHAVIOR_IDENTIFIER_HITS=0`; worker/Openness protected diff is empty. Descriptions explicitly retain “no rollback” but add no rollback implementation. |
| AC-010 | Do not run live TIA Portal operations; keep static evidence separate. | Boundary | PASS | Only stub build, .NET tests, coverage validation, Git/static inspection, and local indexing ran. No TIA process/tool/Openness runtime operation was invoked. |
| AC-011 | Build serially with stubs. | Build | PASS | `dotnet build TiaMcpServer.sln -m:1 --no-restore --configuration Release /p:UseTiaPortalReferenceStubs=true` → exit 0; `Build succeeded`; 0 warnings; 0 errors; `00:00:04.46`. |
| AC-012 | Explicitly link every new host source file into the test project. | Static/build | PASS | `TiaMcpServer.Tests.csproj` contains 12 explicit links: all 6 `OperationBatches` and all 6 `Network` host files. Release build and 985-test suite compile those links. |
| AC-013 | Do not stage or commit without authorization. | Git | PASS | `git diff --cached --name-only` returned no paths; `STAGED_PATH_COUNT=0`; HEAD stayed at `f75b119...`. No commit was created. |
| AC-014 | Restore only if dependencies are unavailable. | Dependency gate | PASS | Restore was not run. The exact `--no-restore` Release build succeeded, proving required assets/packages were already available. |
| AC-015 | Task 7 Step 2: serialized Release stub build succeeds. | Build | PASS | Exact command/output is recorded in AC-011: exit 0, 0 warnings, 0 errors. A preliminary context-shell attempt translated `/p:` and failed before compilation with MSB1008; the argument-array rerun above is the valid build evidence. |
| AC-016 | Task 7 Step 3: complete Release suite passes and the isolated coverage run has exactly one Cobertura artifact. | Logic/artifact | PASS | Fresh `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build --configuration Release` → 985 passed, 0 failed, 0 skipped, total 985, 15 s, exit 0. Unique directory `TestResults/network-phase1-final-20260801-161214771` contains exactly 1 `coverage.cobertura.xml`. Coverage collection was not duplicated per the acceptance instruction. |
| AC-017 | Task 7 Step 4: coverage threshold is at least 0.80. | Coverage | PASS | Exact XML `line-rate="0.8884000000000001"`; `scripts/verify-coverage-threshold.ps1 ... -MinimumLineRate 0.80` → “Coverage line-rate 0.8884 meets the required minimum 0.8.”, exit 0. |
| AC-018 | Task 7 Step 5: focused public-surface/removal/FakeWorker filter passes. | Logic | PASS | `dotnet test ... --no-build --configuration Release --filter "FullyQualifiedName~McpToolSchemaTests\|FullyQualifiedName~ReadOnlyModeTests\|FullyQualifiedName~NetworkOperationFakeWorkerTests"` → 119 passed, 0 failed, 0 skipped, 220 ms, exit 0; exact surface 14/4. |
| AC-019 | Task 7 Step 5: repeat the bounded seven-file generic removal search. | Static | PASS | Exact seven-file PowerShell `Select-String` returned `MATCH_COUNT=0`, exit 0. |
| AC-020 | Task 7 Step 6: `git diff --check` is clean. | Git | PASS | Exit 0; no whitespace error. Output consisted only of expected LF-to-CRLF working-copy normalization warnings. |
| AC-021 | Task 7 Step 6: only planned files are changed. | Git/scope | **FAIL** | `git status --short --untracked-files=all`: 56 paths. All 49 formal File Structure paths are present, but 7 task-related paths are absent from the formal plan: `TiaMcpServer.Tests/BatchSafetySnapshotTests.cs`, `TiaMcpServer.Tests/BatchSafetyTokenTests.cs`, `TiaMcpServer.Tests/ProjectStandaloneToolTests.cs`, `TiaMcpServer.Tests/StandaloneToolResultFormatterTests.cs`, `TiaMcpServer.Tests/TypeOperationFakeWorkerTests.cs`, `TiaMcpServer/Json/TiaJson.cs`, `TiaMcpServer/Tools/StandaloneToolResultFormatter.cs`. Final review classified these as necessary integration fallout, but no explicit user scope waiver is recorded. |
| AC-022 | Task 7 Step 6: the protected 8-file diff is empty. | Git | PASS | Protected diff exit 0, output bytes 0. |
| AC-023 | Final review: `NetworkReadTools` and `NetworkWriteTools` are separate decorated types. | Static | PASS | Fresh indexed source shows distinct `[McpServerToolType]` classes and decorated `network_read` / `network_write` methods. |
| AC-024 | Final review: `network_write` cannot be registered in read-only mode. | Static/test | PASS | `Program.Main` gates `NetworkWriteTools` inside the read-write branch; fresh read-only/schema tests passed. |
| AC-025 | Final review: unknown and inapplicable fields fail before worker access. | Static/test | PASS | `NetworkOperationRequest` uses `JsonUnmappedMemberHandling.Disallow`; both public methods call pure `NetworkOperationCatalog.ValidateRead/ValidateWrite` before any invoker/state read; catalog rejects unknown operations and inapplicable fields. Focused tests passed. |
| AC-026 | Final review: token-envelope validation precedes the apply state read. | Static | PASS | `NetworkWriteTools.NetworkWrite` calls `safety.ValidateEnvelope(...)`, returns on invalid, and only then calls `NetworkSafetySnapshot.ReadCurrentStateAsync(...)`. |
| AC-027 | Final review: state validation and token consumption precede the first write. | Static/test | PASS | After the fresh state read, `safety.ValidateAndConsume(...)` must pass before `OperationBatchExecutionEngine.ApplyWritesAsync(...)` is reached. Token lifecycle FakeWorker acceptance passed. |
| AC-028 | Final review: acquire one hardware snapshot per preview/apply attempt. | Static/test | PASS | Each branch contains exactly one `NetworkSafetySnapshot.ReadCurrentStateAsync` call; `NetworkSafetySnapshot` delegates to one `ReadHardwareConfigAsync`. Focused FakeWorker acceptance passed under the user-approved stable-payload/global-counter override. |
| AC-029 | Final review: writes stop after the first failure; later results are skipped. | Static/test | PASS | `ApplyWritesAsync` sets `stopped` on first failed worker result and appends `Skipped` thereafter. `NetworkWriteApplyEngine_FirstFailureStopsAndMarksLaterItemsSkipped` asserts one invocation, failed first item, skipped second; suite passed. |
| AC-030 | Final review: generic batches recognize no network operations. | Static | PASS | Same bounded search as AC-003/AC-019: 0 matches across the seven generic files. |
| AC-031 | Final review: per-operation results remain strings. | Static/test | PASS | `OperationBatchResult.Result` is `string?`; FakeWorker assertions confirm JSON string value kind. |
| AC-032 | Final review: no secret, machine fixture, Siemens DLL, or generated TestResults artifact is included. | Static/Git | PASS | Added/untracked hygiene: 0 Siemens DLL-path hits, 0 absolute user/Siemens-program paths, 0 private-key hits, 0 credential-assignment hits; status contains 0 Siemens DLL files and 0 machine project fixture files. `git ls-files -- TestResults` = 0; TestResults status = 0; coverage path is ignored by `.gitignore:51:[Tt]est[Rr]esult*/`. Documentation contains 8 textual `.ap21` references, but no such fixture file is in status. |
| AC-033 | Task 7 Step 8: report exact evidence and the remaining runtime gate. | Report | PASS | This report records Release/stub/serialization, 985/985 full tests, 119/119 focused tests, exact coverage, 14/4 schemas, Git review, failures, and the explicit runtime boundary below. |
| AC-034 | Task 7 documentation/roadmap gate: Phase 1 implementation and whole-plan review complete, Phase 2 open, with no pending-Task-7 text. | Documentation | **FAIL** | `docs/NETWORK_OPERATIONS_ROADMAP.md:3` correctly says Phase 1 implementation/review complete and Phase 2 open, but line 93 still says “Completed in implementation, pending Task 7 whole-plan verification and review”; `PENDING_TASK7_COUNT=1`. |
| AC-035 | Task 7 Step 9: conditional commit behavior follows authorization. | Git | PASS | No commit authorization was given. Nothing is staged, HEAD is unchanged, and no commit/push/PR action occurred. |

## Summary

**Total criteria:** 35
**Passed:** 33
**Failed:** 2
**Blocked:** 0

## Failed criteria

### AC-021 — Only planned files are changed

- **Result:** FAIL
- **Evidence:** 56 status paths = 49/49 formal plan paths plus the seven exact paths listed in AC-021.
- **Reason:** The approved wording requires only plan-listed paths. Although the final review judged the seven additions necessary integration fallout, there is no explicit user waiver for this file-scope variance.
- **Suggested resolution:** Obtain an explicit user scope waiver or update the approved plan/file scope through the proper approval flow; do not silently weaken the criterion.

### AC-034 — Roadmap has no pending Task 7 text

- **Result:** FAIL
- **Evidence:** `docs/NETWORK_OPERATIONS_ROADMAP.md:93` retains “pending Task 7 whole-plan verification and review”.
- **Reason:** This contradicts the completed status at line 3 and the explicit acceptance requirement.
- **Suggested resolution:** Correct the stale Phase 1 sentence, then rerun documentation/scope acceptance.

## Static security and artifact checklist

- Protected contracts/worker/Openness files: unchanged.
- Strict network request JSON and operation applicability validation: confirmed before worker access.
- Preview/apply envelope, fresh-state validation, token consumption, and first-write ordering: confirmed.
- Stop-on-failure/no-rollback behavior: confirmed statically and by fresh tests.
- Secrets, machine fixtures, Siemens DLLs, staged content, and generated TestResults in the diff: none found.
- Review input reported no Critical or Important findings and one non-blocking Minor concerning the independent strength of a snapshot-failure test. The latest user-approved FakeWorker stable-payload/global-counter override is honored; production hashing remains exact.

## Runtime boundary

> No live TIA Portal operation was run. Phase 1 changes only host contracts and orchestration; existing worker/Openness network behavior remains runtime-unverified by this delivery.

## Overall verdict

**FAIL** — AC-021 and AC-034 do not satisfy the approved acceptance wording. There are no blocked criteria.
