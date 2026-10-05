# Network JSON contract and guarded writes — offline validation

Report prepared 2026-10-04 for the approved 2026-10-03 design and plan. **The corrected candidate passes offline qualification. Live TIA V21 acceptance remains pending.**

## Candidate and review sequence

- Whole-branch baseline: `baf811789893f706b2c7d398afc13e9e6c0e2a72`.
- Original qualified/reviewed code candidate: `7407d277bf4e7857c5934062eff4e7f9a1eb94c3`.
- Corrected frozen code candidate: **`b97b9ee9173905fa9c19649a73d4f8a29bd6c682`** on `feature/network-contract-write-safety`.
- Worktree used: `C:/Users/LCZ/.codex/worktrees/network-contract-write-safety/tia-portal-mcp`.
- [Approved design](../../specs/2026-10-03-network-json-guarded-write-design.md) and [implementation plan](../../plans/2026-10-03-network-json-guarded-write.md).

Task 1–8 independent reviews preceded original qualification. The **one whole-branch review** of `baf8117..7407d27` found two Important findings and one deferred Minor. The consolidated correction used separate commits: `758240e` observed regression tests; `6a2573d` producer-owned relationship diagnostics; `b97b9ee` complete Phase 4 acceptance/evidence. The **one scoped re-review** of `7407d27..b97b9ee` marked both Important findings addressed and found zero new Critical/Important/Minor issues. No second broad review or second fix wave was performed.

| Finding | Correction and evidence | Final disposition |
| --- | --- | --- |
| I1: production relationship diagnostics became root discovery errors before the required guard | Existing connection-evidence capture owns typed and legacy relationship/display diagnostics and completeness; structural/selector failures remain independent. Executable capture-helper and FakeWorker/host cases cover initial/late blocks, both modes, dry runs, earlier effects and audit. | Addressed; scoped review approved |
| I2: Phase 4 accepted incomplete verification and omitted applied evidence | Exact nonempty batch/immediate/effective-final evidence, fresh supplied-attribute readback and complete applied document retention; 31 isolated synthetic cases, including five positive controls. | Addressed; scoped review approved |
| M1: repeated all-scope connected-node owner traversal | Source establishes repeated work, without representative latency/timeout measurements. | Explicitly deferred, not measured or optimized |

The intended final-fix RED had 13 failures/0 passes: one source-wiring assertion, eleven malformed-document acceptance regressions and one missing-artifact control. Earlier test-authoring build failures were setup errors, not product RED. I1's first 105/109 gate exposed four contradictory selector fixtures; the corrected gate passed 109/109. I2's separate 70/70 and 31/31 runs preceded the final 89/89 both-harness gate. These counts are separate retained runs, not one combined test total. The scoped reviewer independently checked retained source/log/TRX hashes and outcomes. Source-wiring RED and executable helper GREEN do not imply executed Siemens traversal.

## Final qualification results

| Evidence | Original `7407d277` | Corrected `b97b9ee` |
| --- | --- | --- |
| Full Release tests | 5120 passed; 0 failed/skipped | 5174 passed; 0 failed/skipped |
| Overall coverage | 10356 / 11032; 93.87% | 10356 / 11032; 93.87% |
| Changed executable host/contracts lines | 504 / 517; 97.49% | 504 / 517; 97.49% |
| New executable host core | 368 / 377; 97.61% | 368 / 377; 97.61% |
| Debug fixture / Release stub / installed-reference builds | All exit 0, zero warnings/errors | All exit 0, zero warnings/errors |
| Package static verification | 15 required worker files, 0 Siemens DLLs | 15 required worker files, 0 Siemens DLLs |
| Package byte comparisons | 38 matched | 38 matched |

Worker/FakeWorker and acceptance corrections invalidated the earlier compiled dependency/package evidence. The controller therefore authorized one fresh corrected-head full qualification. The original `7407d277` logs, manifests, TRX, coverage and nupkg remain historical; none was relabeled as `b97b9ee`. Documentation-only publication uses link/whitespace/diff checks and does not trigger another build/test/package cycle.

Every dotnet invocation below used the synchronous `run-network-tests.ps1` wrapper and held `C:/Users/LCZ/AppData/Local/Temp/tia-mcp-01a102eb-32fb-7f62-89bb-7f1c848d0998-tests.lock` as FileStream OpenOrCreate/ReadWrite/FileShare.None for the entire process lifetime. Root granted one continuous Network slot; no competing runner or busy-lock bypass occurred. No arbitrary process command lines were inspected, decoded or retained.

The local ignored evidence directory is `.superpowers/sdd/2026-10-03-network-json-guarded-write/`; log names below are relative to it. All commands exited 0:

| Log | UTC start | UTC process exit |
| --- | --- | --- |
| `task-9-b97b9ee-restore.log` | 2026-10-04T11:29:43.5349623Z | 2026-10-04T11:29:45.9847277Z |
| `task-9-b97b9ee-debug-fixture-build.log` | 2026-10-04T11:29:58.2073640Z | 2026-10-04T11:30:04.6390306Z |
| `task-9-b97b9ee-release-stub-build.log` | 2026-10-04T11:30:27.7759854Z | 2026-10-04T11:30:43.8966231Z |
| `task-9-b97b9ee-full-release-coverage.log` | 2026-10-04T11:31:49.7240968Z | 2026-10-04T11:38:38.0276236Z |
| `task-9-b97b9ee-installed-reference-build.log` | 2026-10-04T11:39:35.1223187Z | 2026-10-04T11:39:44.0600355Z |
| `task-9-b97b9ee-package.log` | 2026-10-04T11:40:05.4914565Z | 2026-10-04T11:40:07.2943316Z |

```text
dotnet restore TiaMcpServer.slnx -m:1
dotnet build TiaMcpServer.FakeWorker/TiaMcpServer.FakeWorker.csproj --configuration Debug --no-restore -m:1
dotnet build TiaMcpServer.slnx --configuration Release -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --configuration Release --no-build --no-restore -m:1 --collect:XPlat Code Coverage --settings TiaMcpServer.Tests/coverage.runsettings --results-directory TestResults/network-final-b97b9ee-20261004-1132 --logger trx;LogFileName=network-final-b97b9ee.trx -- xUnit.ParallelizeTestCollections=false xUnit.ParallelizeAssembly=false xUnit.MaxParallelThreads=1 RunConfiguration.MaxCpuCount=1
dotnet build TiaMcpServer.slnx --configuration Release -m:1 /p:UseTiaPortalReferenceStubs=false /p:TiaPortalV21Dir=C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48
dotnet pack TiaMcpServer/TiaMcpServer.csproj --configuration Release --no-build --no-restore -m:1 -o artifacts/network-final-b97b9ee /p:UseTiaPortalReferenceStubs=false
```

Collector and logger values were passed as single arguments even though logs flatten the argument array. Full tests used all four controls: `xUnit.ParallelizeTestCollections=false`, `xUnit.ParallelizeAssembly=false`, `xUnit.MaxParallelThreads=1`, `RunConfiguration.MaxCpuCount=1`; builds/tests used `-m:1`. The test process, including collector completion after the summary, fully exited before the next build.

## Coverage and machine-readable artifacts

Final TRX: `TestResults/network-final-b97b9ee-20261004-1132/network-final-b97b9ee.trx`. All 5174 named outcomes are Passed; failed, error, timeout, aborted, inconclusive, notRunnable, notExecuted, disconnected, warning, pending and inProgress counters are zero. Named IDs/timestamps/durations/outcomes are retained in `task-9-b97b9ee-test-results.json`; counters, exact input paths and per-file coverage are in `task-9-b97b9ee-coverage-summary.json`.

The canonical coverage attachment is `TestResults/network-final-b97b9ee-20261004-1132/68455e54-1942-44e5-be01-630792ce60f3/coverage.cobertura.xml`. The collector also copied the same file under its VSTest attachment directory. These are **one run with byte-identical copies**, not independent coverage runs:

- `TestResults/network-final-b97b9ee-20261004-1132/68455e54-1942-44e5-be01-630792ce60f3/coverage.cobertura.xml` — SHA256 `04F5C1CBF6F7CA34FA2750CFED56D1019B2D85E2A3DB6482191B3EBC5BAA329F`.
- `TestResults/network-final-b97b9ee-20261004-1132/LCZ_S1614W_2026-10-04_13_31_56/In/S1614W/coverage.cobertura.xml` — SHA256 `04F5C1CBF6F7CA34FA2750CFED56D1019B2D85E2A3DB6482191B3EBC5BAA329F`.

The unchanged [coverage settings](../../../../TiaMcpServer.Tests/coverage.runsettings) include host/contracts and linked host namespaces in Tests; they exclude fixture namespaces, FakeWorker, OpennessWorker assembly/namespace (including linked worker helpers), generated/compiler-generated members and generated/obj files. The unchanged [threshold script](../../../../scripts/verify-coverage-threshold.ps1) passed `-MinimumLineRate 0.80`; it checks only the overall Cobertura root. `task-9-b97b9ee-coverage-threshold.log` records its exact path and exit. No exclusion or threshold was weakened.

Separately, changed executable coverage intersects baseline-to-candidate additions with emitted sequence points and deduplicates file/line identities across classes/assemblies using maximum hit count. Whole mapped changed files total 2689/2834 (94.88%). `task-9-b97b9ee-file-coverage.csv` and `task-9-b97b9ee-method-coverage.json` retain detailed file/method rates; the summary lists every uncovered changed executable line and every source line without a sequence point.

| Changed host/core file | Covered / executable | Changed covered / executable |
| --- | --- | --- |
| `TiaMcpServer/Network/NetworkGuardDefinitions.cs` | 5 / 5 | 5 / 5 |
| `TiaMcpServer/Network/NetworkGuardedWriteModels.cs` | 2 / 2 | 2 / 2 |
| `TiaMcpServer/Network/NetworkPayloadContract.cs` | 535 / 542 | 121 / 125 |
| `TiaMcpServer/Network/NetworkReadTools.cs` | 60 / 67 | 4 / 4 |
| `TiaMcpServer/Network/NetworkToolResponses.cs` | 20 / 20 | 4 / 4 |
| `TiaMcpServer/Network/NetworkWriteDomain.cs` | 48 / 48 | 48 / 48 |
| `TiaMcpServer/Network/NetworkWritePayloadBudget.cs` | 60 / 64 | 60 / 64 |
| `TiaMcpServer/Network/NetworkWritePlanner.cs` | 106 / 107 | 106 / 107 |
| `TiaMcpServer/Network/NetworkWriteTools.cs` | 4 / 4 | 4 / 4 |
| `TiaMcpServer/Network/NetworkWriteVerifier.cs` | 127 / 131 | 127 / 131 |
| `TiaMcpServer/ProjectLifecycle/LifecycleWriteDomain.cs` | 198 / 229 | 0 / 0 |
| `TiaMcpServer/Safety/WriteSafetyService.cs` | 194 / 217 | 0 / 0 |
| `TiaMcpServer/Tools/McpToolRegistration.cs` | 9 / 9 | 1 / 1 |
| `TiaMcpServer/Tools/NetworkWriteArgumentValidatingTool.cs` | 22 / 22 | 22 / 22 |
| `TiaMcpServer/Worker/OpennessWorkerClient.cs` | 1299 / 1367 | 0 / 0 |

Eight changed Contracts DTO/declaration files and constant-only NetworkContractVersion have no emitted coverage here. Program.cs is absent: startup subprocess coverage is not measured by this collector result. Their zero denominators are not 100% coverage. Declaration/comment changes in LifecycleWriteDomain, WriteSafetyService and OpennessWorkerClient also have no changed executable sequence points, despite mapped existing lines in those files. Removed NetworkSafetySnapshot and CanonicalWriteSafety have no candidate denominator.

Worker linked capture/postcondition helpers have narrower executable test evidence but remain excluded by the approved namespace filter. Siemens orchestration has static source-contract assertions and installed-reference compilation; aggregate host coverage cannot establish its runtime behavior. The injected Network worker_crashed scenarios do not qualify an actual Network worker-child crash after mutation. Existing unrelated WorkerClient/FakeWorker process-exit tests remain a distinct evidence class.

## Selected binaries and package provenance

The tests' existing FakeWorker locators prefer Debug before Release. The approved control rebuilt that selected Debug fixture once at the corrected frozen head; it did not change the locators, remove/move outputs, or invent an environment override. The real SDK fixture runs linked Release host source in-process; startup subprocess tests explicitly choose the Release host. Evidence is therefore **Release tests/host with a same-head Debug FakeWorker dependency**, not a claim that every subprocess is Release.

`task-9-b97b9ee-debug-before.json` records prior hashes/stamps (not retained old binary bytes); `task-9-b97b9ee-debug-selected.json` records the actual first existing locator candidate; `task-9-b97b9ee-suite-inputs.json` fingerprints selected dependencies and Release test/host inputs immediately before the full run. Selected artifacts:

| Selected file | Product version | SHA256 |
| --- | --- | --- |
| `TiaMcpServer.FakeWorker/bin/Debug/net10.0/TiaMcpServer.FakeWorker.exe` | `1.0.0+b97b9ee9173905fa9c19649a73d4f8a29bd6c682` | `E5587F6D8E14F829570611EC679515946906B58FC581037CA200F92B47FD7C69` |
| `TiaMcpServer.FakeWorker/bin/Debug/net10.0/TiaMcpServer.FakeWorker.dll` | `1.0.0+b97b9ee9173905fa9c19649a73d4f8a29bd6c682` | `586388A42CE360364BDE6ACB77E6AA5CEBD0FB2473223165428973CBCEA59C88` |
| `TiaMcpServer.FakeWorker/bin/Debug/net10.0/TiaMcpServer.Contracts.dll` | `1.0.0+b97b9ee9173905fa9c19649a73d4f8a29bd6c682` | `896AE99238842BBEC60C97BD017C19CDE2E2E4ACD94135954A9F039ACE818A0D` |

Final package: `artifacts/network-final-b97b9ee/TiaMcpServer.1.0.0.nupkg`; SHA256 **`EC24354DAEB25DAA8BD39354AF324850A14E4FD13CBB9F17465C9AE5A09D8178`**. The nuspec repository commit equals `b97b9ee9173905fa9c19649a73d4f8a29bd6c682`. Installed-reference Release compilation explicitly used `/p:UseTiaPortalReferenceStubs=false` and `C:/Program Files/Siemens/Automation/Portal V21/PublicAPI/V21/net48`; it passed with zero warnings/errors.

Static [package verification](../../../../scripts/verify-doctor-package.ps1) passed: one canonical worker subtree, 15 required worker files, no worker runtimeconfig, and zero `Siemens.Engineering*.dll` anywhere in the archive. `task-9-b97b9ee-package-provenance.json` records all 62 archive entries and 38 successful SHA256 comparisons: worker ZIP bytes against installed-reference worker output and copied host subtree; contracts ZIP bytes against worker/host copies and Contracts Release output; host/package-root contracts DLL/PDB against Release output. The verifier and provenance check did not launch doctor, Siemens worker or TIA, install the tool, or change configuration.

Historical package remains `artifacts/network-final/TiaMcpServer.1.0.0.nupkg` with SHA256 `A1D9BBB4331EE10019DBB993C465A59D38B8279BDF8C4C81A73A686571700149` and nuspec commit `7407d277bf4e7857c5934062eff4e7f9a1eb94c3`. Build outputs naturally refreshed during correction; old JSON records their earlier hashes/stamps, not archived copies of every old loose binary. Both nupkg files themselves are retained.

Final corrected-code read-back at `2026-10-04T11:41:03.2083390Z` confirmed exact frozen head, empty full default Git status, clean baseline range whitespace and unchanged selected Debug fixture hashes. `task-9-b97b9ee-final-state.json` retains installed-reference output stamps/hashes and completed session evidence. Source status/report publication is separate from the qualified code/package head.

## Review rulings and evidence boundaries

The chronological controller ledger `progress.md` records each substantive ruling and its cost; these paragraphs summarize those already-recorded decisions, not new approvals:

- **Relationship attribution:** keep root discovery/selector refusal strict while placing attributable typed/legacy relationship diagnostics in existing connection evidence. The narrow internal callback is permitted because production and executable helper tests share it. Cost if wrong: uncertain selection or lost diagnostics; combined negative controls and scoped review address that risk.
- **M1 performance:** defer repeated all-scope owner-traversal optimization until representative measurement establishes material cost; preserve ambiguity/failure handling and add no cross-call cache. Cost if wrong: large-project reads may remain slow or time out. No measured slowdown is claimed.
- **Configuration fixture casing:** retain its deliberately case-exact frozen pre-mutation fixture gates. Cost if wrong: some otherwise valid production selector variants remain unsupported by this narrower fixture; no reachable mutation failure was established by the isolated counterexample.
- **Explicit IO expectation:** preserve conservative verification after a later Subnet-only move without inventing Siemens attach/detach semantics. Cost if wrong: successful mutation may be rejected because of an unqualified API side effect; inspect state before retry and qualify separately live.
- **Budget admission:** judge requests admitted by actual production defaults rather than arbitrary infeasible internal caps. Cost if wrong: an unexamined nondefault caller can still fail; no default admission/mutation-truth guarantee was relaxed. The original 245287-character witness and later protected-core/omission fixes remain distinct retained evidence.
- **Audit durability:** do not claim transactional persistence under arbitrary inherited filesystem failure. Cost if wrong: filesystem failures may limit retained audit evidence; ordinary entered-call exactly-once behavior is the qualified scope.
- **Crash/live limits:** distinguish injected Network error projection, existing process-exit tests and the unperformed Siemens Network mutation/crash/recovery sequence. Cost if wrong: callers could overestimate crash recovery or runtime acceptance. Exact hardware effects/restoration and representative performance remain pending.
- **Refreeze and documentation:** changed worker/FakeWorker bytes justified fresh corrected-head fixture, suite and package evidence; subsequent report/status changes use static documentation checks only. Cost if wrong: the extra qualification consumes time, while replay after documentation edits adds no executable evidence. Both candidates' artifacts remain distinct.

All Task 1–8 reports/reviews/fixes, original RED/intermediate failures, final-review.md, final-fix-report.md, final-fix-review.md, and both Task9 evidence sets remain in the ignored evidence directory. Task5's historical 504/504 console-only evidence retains its UTC/exit provenance; this report does not invent an old TRX. Task8's earlier acceptance/helper corrections and the final Phase4 correction are separate review rounds. Controller-requested report precision corrected three commit separators and the old-hashes/crash wording; it changed no executable source and required no test replay.

## Live acceptance status and prepared gate

**Live TIA V21, ProjectServer and PLC acceptance is pending and was not run.** Neither Inventory/Preview/Apply/Restore harness entrypoints nor real project mutations, restoration, save/close, TIA compile/download or PLC online actions were executed by this qualification. No remote push/PR/merge/deploy, tool install/reconfiguration or cleanup occurred. Historical live reports do not qualify this candidate.

The prepared future gate requires fresh authorization naming the exact disposable project, frozen code/package head and hashes, access modes, ordered operation arrays/fixtures, explicit restoration operations and fresh read-back procedure, and excluded lifecycle/PLC actions. Fixture and harness source/helper hashes must match the frozen candidate. There is no automatic replay, server rollback or inferred restoration. Offline compilation, FakeWorker/SDK behavior, linked-helper tests and isolated synthetic acceptance assertions are separately classified evidence, not plant acceptance.
