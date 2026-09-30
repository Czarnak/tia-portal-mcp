# JSON Contract Phase 2 — Offline Validation

Date: 2026-09-30. Status: implementation, offline qualification, and independent review passed; installed-V21 acceptance pending.

## Candidate and Authorization

- Branch: `phase2-json-contract`.
- Base: `ddeeae06d430955a23288accddae6887dbeda98b`.
- Code/test candidate: `66d3693d9cb0c88711c802506c7f8dd928576099`.
- The maintainer authorized the [Phase 2 implementation plan](../../plans/2026-09-30-json-contract-phase2-standalone-tools.md), independent subagent reviews, and a commit after each implementation or fixing step. The maintainer confirmed that `compile_check.success` means compilation passed.
- No live TIA operation, remote write, merge, package tag, or release was authorized or performed. Phase 3 remains a separate discussion draft, to be revalidated after its predecessor merges.

Documentation-only commits following this candidate do not change the executable evidence. Any subsequent code, test, or base change requires the relevant gates to be repeated.

## Verification

Commands ran serially from the repository root in PowerShell. The final full test command ran outside the filesystem sandbox: baseline executable-resolution tests need access to Windows system directories.

```powershell
dotnet restore TiaMcpServer.slnx
dotnet build TiaMcpServer.slnx -m:1 --no-restore --configuration Release /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests --no-build --configuration Release --collect:'XPlat Code Coverage' --settings TiaMcpServer.Tests/coverage.runsettings --results-directory priv/json-contract-phase2/final-coverage --logger:'trx;LogFileName=phase2.trx' --verbosity quiet -- xUnit.ParallelizeTestCollections=false
.\scripts\verify-coverage-threshold.ps1 -CoveragePath 'priv\json-contract-phase2\final-coverage\006face1-0219-42f6-8ec5-b00db5fec4fe\coverage.cobertura.xml' -MinimumLineRate 0.80
git diff --check
```

| Gate | Result |
| --- | --- |
| Restore | Passed after retry outside the sandbox; initial attempt encountered NuGet TLS/credential errors |
| Release solution build with reference stubs | Passed; zero errors, seven existing xUnit2031 warnings |
| Focused standalone, protocol, compatibility, and compiler tests | 244 passed, zero failed |
| Compiler identity null-policy fix regressions | 95 passed, zero failed |
| Final full suite | 4,513 passed, zero failed or skipped; 4 minutes 39 seconds |
| Final coverage threshold | 93.94% line coverage (9,975/10,618); passed the 80% gate |
| Documentation links and final whitespace check | Passed; local file links resolve, package README cross-document links are absolute, and `git diff --check` is clean |

The first full coverage run at `8ae3c08` passed 4,512 tests and failed one pre-existing serialization assertion. `CompileCheckInfoTests.LegacyJsonDoesNotInventADeviceIdentity` still expected `deviceName` omission when using the migrated compiler report's payload options. A focused rerun reproduced one failure and four passes. Commit `66d3693` changes that assertion to require an explicit null while preserving the unknown identity; production code did not change. The final full run supersedes that failed run.

The earlier baseline in the filesystem sandbox passed 4,410 tests and failed four `ExecutableResolutionCoverageTests` cases. All 21 tests in that class, including those four cases, passed when rerun outside the sandbox. These were environment failures and received no production changes.

Coverage uses the repository's existing `coverage.runsettings`. Changed measured executable lines are 133/138 covered (96.38%), derived by intersecting the production diff's added lines with Cobertura line hits and merging duplicate file/line entries. The collector excludes Siemens worker code and does not measure the changed contracts' automatic properties. FakeWorker, reference-stub builds, source inspection, and wire-policy tests do not establish installed-V21 producer compatibility.

Local logs, TRX, and coverage XML are preserved under the ignored `priv/json-contract-phase2/` directory; they are development artifacts rather than tracked runtime acceptance evidence.

## Independent Review

Two read-only subagents reviewed the complete `ddeeae0..8ae3c08` change:

| Review lane | Result |
| --- | --- |
| Typed contracts, compiler truth, canonical budgets, schemas, tests, and documentation | No Critical, Important, or Minor findings |
| Access policy, binding leases, attempted-failure provenance, worker and lifecycle compatibility | No Critical, Important, or Minor findings |

Both reviews explicitly declined installed-V21 acceptance. They made no edits and ran no tests concurrently with the parent's serial verification. The only subsequent executable-tree change is the four-line compiler identity test correction described above.

## Execution Decisions

1. Used the requested branch in the current checkout and a PowerShell-compatible ledger under existing ignored `priv/`, rather than creating another worktree. This follows the requested branch workflow; the cost is less checkout isolation.
2. Treated authorization to execute the plan as acceptance of its proposed standalone wrapper and contract version `1.0`. The cost of a different intended decision is a public schema revision before release.
3. Moved each tool's conformance register and probes with its migration instead of waiting for Task 4. This kept intermediate commits coherent; the cost was revisiting the same test file during later probe expansion.
4. Authorized `compile_check` before checking its binding. This prevents worker reads for prohibited compilation and enforces the capability ceiling. It changes error precedence for direct read-only callers: access denial precedes binding failure.

No review findings were deferred. The two implementation fixes were attempted-operation provenance after identity validation and the compiler identity null-policy assertion; each received focused failing/passing evidence and its own commit.

## Remaining Acceptance Boundary

The direct worker status producer changed, so the plan's installed-V21 compatibility gate remains open. Before a live run, freeze the intended candidate and obtain fresh authorization for an exact disposable project. Qualify direct open/no-project status, passing/error compilation, and legacy lifecycle wire compatibility within that authorization. Inspect an unknown outcome before replaying any operation.

This report establishes offline contract, protocol, access, binding, and compatibility regression evidence only. It provides no live TIA, persistence, PLC runtime, deployment, or plant acceptance evidence.
