# Acceptance Test Report — Network Operations Phase 1 Rerun

**Target:** `HEAD` plus the uncommitted working tree
**HEAD:** `f75b1197563b673a36c909c8b6d8f54446859434` (requested `f75b119`)
**Acceptance source:** `docs/superpowers/plans/2026-08-01-network-operations-phase1.md` — Global Constraints and Task 7 Steps 1–9, including the final correctness/security checklist
**Date:** 2026-08-01 16:56 Europe/Warsaw
**Rerun basis:** Production code is unchanged. Broad build, full-suite, coverage, focused, and generic-removal commands were not duplicated; their prior fresh acceptance evidence is carried forward. The two previously failing criteria were re-evaluated using the explicit user scope approval and fresh targeted roadmap/metadata evidence supplied for this rerun.
**Runtime boundary:** No live TIA Portal operation was run.

## Results

| ID | Criterion | Result | Evidence |
|---|---|---|---|
| AC-001 | Current checkout; preserve unrelated work; no branch/worktree change. | PASS | HEAD remains `f75b119...`; no branch/worktree/staging/commit action occurred. All implementation paths are task-related, including the seven explicitly approved integration paths. |
| AC-002 | TDD for behavior changes. | PASS | Six Task 1–6 reports contain fail-first/RED then PASS/GREEN evidence; final full suite passed 985/985. |
| AC-003 | Remove four network operations from generic batches without aliases/adapters/rejection entries. | PASS | Seven-file removal search: 0 matches. |
| AC-004 | Per-operation `result` values remain JSON strings. | PASS | `OperationBatchResult.Result` is `string?`; FakeWorker assertions confirm JSON string values. |
| AC-005 | Protected WorkerRequest, policy, worker client/dispatch, and existing Openness handlers remain unchanged. | PASS | Required protected 8-file diff was empty. |
| AC-006 | Do not modify/validate unverified subnet and IO-system reflection calls. | PASS | Protected `NetworkDeviceConfigurator.cs` diff was empty; no worker/Openness behavior changed. |
| AC-007 | `network_read` in both modes; `network_write` only in read-write mode. | PASS | Registration source and passing schema/mode tests confirm 14 read-write / 4 read-only tools. |
| AC-008 | Bind write token to exact ordered request, normalized common path, ordered targets, and one snapshot per attempt. | PASS | Static orchestration and passing FakeWorker acceptance confirm the contract under the approved stable-snapshot-payload/global-counter override; production exact hashing remains unchanged. |
| AC-009 | No save, compile, transaction, rollback, exclusive-access, post-read, download, commissioning, or live-TIA behavior. | PASS | New-host-source identifier scan returned 0 forbidden implementation hits; protected worker diff is empty. |
| AC-010 | No live TIA Portal operation; static evidence reported separately. | PASS | Only stub build, .NET tests, coverage validation, Git/static inspection, and local indexing were used. |
| AC-011 | Serialized stub build. | PASS | `dotnet build TiaMcpServer.sln -m:1 --no-restore --configuration Release /p:UseTiaPortalReferenceStubs=true`: exit 0, 0 warnings, 0 errors, `00:00:04.46`. |
| AC-012 | Explicitly link all new host sources into the test project. | PASS | 12 explicit links: all six `OperationBatches` and all six `Network` host files. |
| AC-013 | No staging/commit without authorization. | PASS | Staged path count was 0; no commit/push/PR action occurred. |
| AC-014 | Restore only if dependencies are unavailable. | PASS | Restore was not needed; the exact `--no-restore` build succeeded. |
| AC-015 | Task 7 Step 2 serialized Release stub build succeeds. | PASS | Exit 0, 0 warnings, 0 errors. |
| AC-016 | Task 7 Step 3 full Release suite and unique coverage artifact. | PASS | Full suite: 985 passed, 0 failed, 0 skipped; the isolated results directory contains exactly one `coverage.cobertura.xml`. |
| AC-017 | Task 7 Step 4 coverage threshold at least 0.80. | PASS | Exact Cobertura line rate `0.8884000000000001`; threshold script reported `0.8884` meets `0.80`, exit 0. |
| AC-018 | Task 7 Step 5 focused public-surface/removal/FakeWorker filter. | PASS | 119 passed, 0 failed, 0 skipped; exact schema surface 14/4. |
| AC-019 | Task 7 Step 5 bounded generic removal search. | PASS | Exact seven-file search returned 0 matches. |
| AC-020 | Task 7 Step 6 `git diff --check`. | PASS | Prior acceptance: exit 0 with normalization warnings only. Fresh targeted rerun: exit 0. |
| AC-021 | Task 7 Step 6 changed-file scope. | PASS | The user explicitly approved the seven-file variance. Final review classified each as necessary integration fallout: `TiaMcpServer.Tests/BatchSafetySnapshotTests.cs`, `TiaMcpServer.Tests/BatchSafetyTokenTests.cs`, `TiaMcpServer.Tests/ProjectStandaloneToolTests.cs`, `TiaMcpServer.Tests/StandaloneToolResultFormatterTests.cs`, `TiaMcpServer.Tests/TypeOperationFakeWorkerTests.cs`, `TiaMcpServer/Json/TiaJson.cs`, `TiaMcpServer/Tools/StandaloneToolResultFormatter.cs`. All 49 formal plan paths were also present. |
| AC-022 | Task 7 Step 6 protected 8-file diff is empty. | PASS | Protected diff exit 0, output 0 bytes. |
| AC-023 | Separate decorated `NetworkReadTools` and `NetworkWriteTools` types. | PASS | Fresh indexed source confirmed two distinct `[McpServerToolType]` classes and methods. |
| AC-024 | `network_write` cannot be registered read-only. | PASS | Registration is inside the read-write branch; mode/schema tests pass. |
| AC-025 | Unknown/inapplicable fields fail before worker access. | PASS | Strict unmapped-member handling and catalog validation occur before any invoker/state read; tests pass. |
| AC-026 | Token-envelope validation precedes apply state read. | PASS | `ValidateEnvelope` returns on failure before `ReadCurrentStateAsync`. |
| AC-027 | State validation and token consumption precede first write. | PASS | Fresh state is passed to `ValidateAndConsume`; writes begin only after validation succeeds. |
| AC-028 | One hardware snapshot per preview/apply attempt. | PASS | Each branch has exactly one snapshot read; FakeWorker acceptance passes under the approved override. |
| AC-029 | Stop writes after first failure; mark later results skipped. | PASS | Static engine ordering and targeted test confirm one invocation, failed first item, skipped second. |
| AC-030 | Generic batches contain no network recognition. | PASS | Bounded seven-file search returned 0 matches. |
| AC-031 | Per-operation results remain strings. | PASS | `string? Result` contract and FakeWorker JSON-kind assertions pass. |
| AC-032 | No secrets, machine fixture, Siemens DLL, or generated TestResults artifact included. | PASS | Hygiene scans found no secret/DLL/absolute-machine-path artifact; no project fixture file in status; TestResults is untracked/ignored with 0 status paths. |
| AC-033 | Task 7 Step 8 exact evidence and runtime gate reported. | PASS | This report records exact build/test/coverage/schema/diff evidence and the explicit no-live-TIA boundary. |
| AC-034 | Roadmap says Phase 1/review complete, Phase 2 open, with no pending Task 7 text. | PASS | Fresh targeted validation: roadmap status is Phase 1 complete / Phase 2 open; `pending_hits=0`; metadata/schema tests passed 38/38. |
| AC-035 | Task 7 Step 9 conditional commit behavior. | PASS | No commit authorization was given; nothing was staged or committed. |

## Summary

**Total criteria:** 35
**Passed:** 35
**Failed:** 0
**Blocked:** 0

## Evidence summary

- Serialized Release stub build: PASS — exit 0, 0 warnings, 0 errors.
- Full Release suite: PASS — 985/985, 0 failed, 0 skipped.
- Coverage: PASS — exact line rate `0.8884000000000001`, threshold `>= 0.80`, exactly one isolated Cobertura report.
- Focused public-surface/removal/FakeWorker suite: PASS — 119/119.
- Generic seven-file removal search: PASS — 0 matches.
- Fresh roadmap/metadata rerun: PASS — `pending_hits=0`, `git diff --check` exit 0, metadata/schema 38/38.
- Scope: PASS under explicit user approval of the seven necessary integration paths.

## Remaining concerns and runtime boundary

- The whole-plan review reported no Critical or Important findings and one non-blocking Minor about the independent strength of a snapshot-failure test. The approved FakeWorker override is recorded; production exact hashing remains unchanged.
- No live TIA Portal operation was run. Phase 1 changes only host contracts and orchestration; existing worker/Openness network behavior remains runtime-unverified by this delivery.

## Overall verdict

**PASS** — all 35 acceptance criteria are satisfied; none failed or were blocked.
