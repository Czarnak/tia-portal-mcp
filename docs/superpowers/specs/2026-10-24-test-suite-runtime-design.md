# Test suite runtime: parallel CI, packaging step and serial-tail trim

Date: 2026-10-24. Status: written design, awaiting maintainer approval.

## Intent

A full `TiaMcpServer.Tests` run takes about 7 minutes and is the main wait in every agent wave.
Agents already queue so that only one full suite runs at a time. The goal is to make that one run
shorter, for agents and CI alike, without losing coverage or test meaning.

Running several full suites at once (per-run memory, parallel worktrees) is a separate, later
project. So is per-worker FakeWorker configuration ("step 2" below).

## Measured starting point

Local, Release, 12 logical cores, CI's coverage settings unless noted:

| Run | Wall | Line / branch coverage |
| --- | --- | --- |
| Fully serial (CI today) | 6:49 | 95.05% / 86.39% |
| xUnit default parallel | 5:23 | 95.05% / 86.39% |
| Parallel, packaging tests filtered out | 4:45 | 95.05% / 86.39% |

- Covered lines are identical file by file across the three runs; the packaging tests add no
  coverage (they exercise MSBuild targets and scripts in child processes).
- In a parallel run (no coverage), the parallel phase lasts ~91 s and ends exactly when
  `DoctorPackageVerificationScriptTests` finishes. The remaining ~4.2 min are the
  `DisableParallelization` collections "Real worker process" (2.5 min) and "Mcp protocol serial"
  (1.7 min), one test at a time.
- One parallel run hit an intermittent failure in
  `TempAuditDirectoryTests.Dispose_RetriesUntilTransientFileLockIsReleased`.

## Goals

1. CI and agents use one parallel test command that keeps coverage collection and the 80% gate.
2. Packaging tests leave the default run and run in their own CI step, only when packaging files
   change.
3. Classes that do not need to be serial leave the two big serial collections.
4. The intermittent `TempAuditDirectoryTests` failure is fixed so the parallel run is reliable.

## Non-goals

- Per-worker environment for FakeWorker (step 2): its own project, decided after this one ships.
- Reducing per-run memory, or enabling concurrent full suites across worktrees.
- Porting the PowerShell scripted workers to FakeWorker.
- Rewriting historical plans or reports that quote the old serial command.

## Anti-goals

- Coverage numbers or the threshold gate weakened in any way.
- A test that keeps passing while no longer testing what it claims (for example, a FakeWorker
  scenario that silently stops applying).
- Packaging regressions that ship because the packaging tests stopped running where they matter.
- A flaky CI made "green" by retries or by raising timeouts across the board.

## Constraints

- Builds stay serialized (`-m:1`); this is about test execution only.
- `CiWorkflowTests` pins CI commands and the building guide. Changed commands update those tests in
  the same PR. The repo prefers not to pin documentation wording in tests beyond what exists.
- One fix per PR: each merged PR is one release-notes line.
- Test project wiring: source files are linked explicitly into `TiaMcpServer.Tests.csproj`; check
  wiring before assuming a new file compiles into the tests.
- Commit messages: short one-line conventional commits.

## Why the serial collections exist (background for change 4)

FakeWorker reads its scenario settings from process environment variables
(`TIA_MCP_FAKE_WORKER_REQUEST_LOG`, `_UI_OPEN_PROJECT`, `_PORTALS`, `_STATUS_GROUNDING`,
`_LAUNCH_LOG`). Test helpers in `TiaMcpServer.Tests/TestSupport` (`FakeWorkerRequestLog`,
`FakeWorkerUiOpenProject`, `FakeWorkerPortals`, plus `LifecycleProtocolFixture` and direct
`Environment.SetEnvironmentVariable` calls) set them on the testhost process, and every FakeWorker
spawned meanwhile inherits them, restarts included. Two such tests running at once would leak
settings into each other's workers. `DisableParallelization` collections run after the parallel
phase, alone, which is what keeps this safe today. The "Real worker process" collection's doc
comment still gives PowerShell contention as the reason; that is now true only for a few classes.

## The changes

### 1. Fix the intermittent `TempAuditDirectoryTests` failure

Observed: `IOException` on `requests.log` after the helper's retry budget (20 × 100 ms) ran out.
The test releases its file lock from `Task.Run(async () => { await Task.Delay(200); ... })` while
`TempAuditDirectory.Dispose` blocks the calling thread with `Thread.Sleep`. Inference (not
proven): under parallel load the thread pool does not run the delayed release within the 2 s
budget.

Required outcome: the test proves the same behaviour (Dispose retries through a transient lock and
then deletes the directory) without depending on thread-pool timing. `TempAuditDirectory` is used
by ~43 test files; its retry behaviour for real callers stays as it is unless the builder finds
evidence that it also fails under load. How to make the test deterministic is left to the builder.

### 2. Packaging tests: tag, exclude by default, separate CI step

- Tag `DoctorPackageVerificationScriptTests` and `ReferenceStubVerificationScriptTests` with
  `[Trait("Category", "Packaging")]` (39 tests).
- The default test command excludes `Category=Packaging`.
- A separate CI step (or job) runs `--filter Category=Packaging` only when the change touches
  packaging files:
  - `TiaMcpServer/TiaMcpServer.csproj`, `TiaMcpServer.OpennessWorker/*.csproj`,
    `TiaMcpServer.FakeWorker/*.csproj`
  - `Directory.Build.props`, `global.json`
  - `scripts/verify-doctor-package.ps1`, `scripts/verify-reference-stubs.ps1`, `reference-stubs/**`
  - the two packaging test files themselves
  - Not `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` (maintainer's decision): every new test file
    is linked there (37 of 95 merges since 2026-09-24), and it does not shape the package.
- This step does not collect coverage (the tests add none).
- Agents run `--filter Category=Packaging` locally when they touch those files; the building guide
  and AGENTS.md say so.
- `publish.yml` already verifies the real package with `verify-doctor-package.ps1` at release and
  stays unchanged.

Left to the builder: how the CI step detects changed files (for example a path filter on a
separate job, or a diff check inside the job), as long as it works for pull requests and pushes to
`main`, and a skipped step does not block merging.

Design author's call: `CiWorkflowTests` also pins the packaging step (its `Category=Packaging`
filter and that the main coverage step excludes it), in line with how CI commands are already
pinned.

### 3. One parallel test command for CI and agents

- CI's coverage step drops `-- xUnit.ParallelizeTestCollections=false xUnit.ParallelizeAssembly=false
  xUnit.MaxParallelThreads=1 RunConfiguration.MaxCpuCount=1` and adds the packaging exclusion.
  Coverage collection, `coverage.runsettings`, the exactly-one-report check, the 80% threshold
  and the Codecov upload stay as they are.
- Parallelism: xUnit defaults (one thread per core), no cap.
- `CiWorkflowTests.CiCoverage_CollectsThenEnforcesBeforeUpload` (currently pins the serial flags,
  `CiWorkflowTests.cs:177`) changes to pin the new command shape.
- `docs/development/building.md` (Coverage section) and AGENTS.md "Build and test" show the same
  command as the canonical full run, with the packaging filter command next to it.
- First CI run is the real confirmation that parallel coverage works on GitHub's runners (the
  reason for 7a51365 "disable parallel test collection for scoped coverage" was never recorded and
  does not reproduce locally). If it fails there, stop and report before adding workarounds.

### 4. Move classes that set no FakeWorker environment out of the serial collections

- Move the classes in "Real worker process" and "Mcp protocol serial" that start FakeWorker (or
  the in-process MCP harness) but never set the FakeWorker environment variables, directly or
  through the helpers/fixtures above. Measured candidates: 15 in "Real worker process" (~22 s,
  e.g. `NetworkOperationFakeWorkerTests`, `PlcWriteInvokerTests`) and 6 in "Mcp protocol serial"
  (~4 s, e.g. `NetworkStructuredProtocolTests`, `McpToolSchemaTests`). The builder re-derives the
  list from the code; a class also stays if it relies on another shared resource found on review.
- Keep serial: every class that sets the variables, and the three PowerShell classes
  (`NetworkObjectPaginationCoordinatorTests`, `LifecycleIdentityContinuityTests`,
  `WriteSafetyLeaseConcurrencyTests`), which carry the original contention reason.
- Add a guard test: it fails when a test class that uses the FakeWorker environment helpers or sets
  a `TIA_MCP_FAKE_WORKER_*` variable is not in a `DisableParallelization` collection. Detection
  method (reflection, source scan) is left to the builder; it must catch both helper use and direct
  `SetEnvironmentVariable` calls.
- Update the "Real worker process" collection's doc comment and the `FakeWorkerRequestLog` comment
  to state the real reason (process-wide FakeWorker environment) and the guard test.
- Expected gain is small (~25-45 s off the serial tail); the guard matters as much as the time.

## Delivery

Four PRs, in this order, each one release-notes line:

1. `TempAuditDirectoryTests` fix.
2. Packaging tag, default exclusion and the conditional CI step.
3. Parallel test command for CI and agents, with `CiWorkflowTests` and docs.
4. Serial-collection trim and the guard test.

PR 3 depends on 1 and 2. PR 4 is independent of 2 and 3 but should land after 1.

## Verification

- Each PR: full suite passes with the command current at that point; coverage file-by-file
  identical to the 2026-10-24 baseline (95.05% line, 86.39% branch) unless a PR intentionally
  changes tests that contribute coverage.
- PR 1: the fixed test passes in three consecutive parallel full runs (design author's call on
  the count).
- PR 2: `--filter Category=Packaging` runs exactly the 39 tagged tests; the default run excludes
  them; CI shows the step running for a packaging change and skipping otherwise.
- PR 3: CI green on the new command; the measured local wall time is recorded in the PR.
- PR 4: the guard test fails when a known env-setting class is moved out of its serial collection
  (proved once, then reverted), and passes on the final tree.
- Final: one local timing run with the new command, compared with the table above.

## Documentation touched

Design author's call: `docs/development/building.md` and AGENTS.md "Build and test" only, plus
this spec's entry in `docs/README.md`. ARCHITECTURE.md and IMPROVEMENT_LOG.md change only if a PR
changes something they describe; step 2 and the other "Later" items may be logged in
IMPROVEMENT_LOG.md as open follow-ups.

## Left to the builder

- The technique for making the `TempAuditDirectoryTests` test deterministic.
- How CI detects packaging changes.
- The guard test's detection method.
- Exact wording in building.md and AGENTS.md.

## Later (not this project)

- Step 2: per-worker environment for FakeWorker so env-setting classes can run in parallel
  (needs an opt-in extra-environment parameter on `PersistentWorkerTransport`/`OpennessWorkerClient`
  and ~51 test classes; risk analysis in the maintainer's private notes).
- PowerShell scripted workers to FakeWorker replay mode.
- Per-run memory (peaks at ~1.4 GB) and concurrent full suites across worktrees.
