# E-5 Shared Network Vocabulary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Status:** Implemented, independently reviewed and offline-qualified on 2026-10-25 after A -> B -> C integration. The maintainer subsequently authorized rebasing E-5 onto current `main`, reconciling documentation and publishing its own PR, with existing qualification reused if production source stayed unchanged. The rebase preserved the qualified source; no standalone E-5 live TIA run is required. See the [execution record](#execution-record-2026-10-25) and [delivery record](#delivery-and-remaining-limits).

**Goal:** Give host, worker and valid FakeWorker generators one exact vocabulary for Network verification statuses, setting keys and approved subnet type identifiers without changing their wire contract.

**Architecture:** Put string constants in the existing Siemens-free Contracts assembly and extend `SubnetLifecycleContract` with the duplicated subnet mapping. Consumers retain their current validation, status aggregation, exception filters and mutation order; the host continues to validate worker evidence independently.

**Tech Stack:** C#, .NET 10 host/tests/FakeWorker, .NET Framework 4.8 worker, netstandard2.0 Contracts, xUnit, Coverlet, Windows PowerShell, TIA Portal V21 reference stubs.

**Spec:** [2026-10-17 simplification review, E-5](2026-10-17-codebase-simplification-review.md) and its [verifier-corrected WORKERNET-10 appendix](2026-10-17-codebase-simplification-review-findings.md). The maintainer's subsequent discussion limits this change to shared vocabulary; the optional shared status algorithm is excluded.

**Planning baseline:** `main` at `7c04b44`, revalidated on 2026-10-25. Revalidate and record one approved starting SHA with the coordinator before execution; all three worktrees start from that same recorded base.

## Global Constraints

- Preserve exact ordinal wire strings, accepted inputs, explicit nulls, canonical response/schema shapes and existing failure categories.
- Keep `failed > unverified > passed` aggregation and the existing contexts that allow empty evidence to mean `not_required`. A check itself cannot use `not_required`.
- Sparse result keys remain `Address`, `SubnetMask`, `PnDeviceNameAutoGeneration`, `PnDeviceName`, `Subnet`, `IoSystem`; scalar evidence keys remain `IoSystemSubnet`, `IoSystemNumber`.
- No enums, JSON converters, shared status roll-up, broad attribute-key registry, new dependencies or unrelated Network cleanup.
- Keep intentional malformed fixtures and representative literal wire assertions independent of the new constants.
- Preserve `SubnetLifecycleService.ResolveTypeIdentifier(string)`'s `WorkerOperationException(WorkerFailureCategories.ValidationError)` and exact existing unsupported-type message.
- No live TIA operations, remote writes or deployment are authorized by this plan. E-5 does not establish E-4 native runtime acceptance.
- The maintainer committed the approved plans as `0f7162c`; later execution authorization covers implementation and coordinator evidence commits. C has no remote-write or live-testing authorization.

## Coordination and Isolation

| Stream | Plan | Branch | Managed worktree name |
| --- | --- | --- | --- |
| A: E-1/E-2 | [worker context gates](2026-10-25-e1-e2-worker-context-gates.md) | `refactor/worker-context-gates` | `e1-e2-worker-context-gates` |
| B: E-4 | [typed network connections](2026-10-25-e4-typed-network-connections.md) | `refactor/typed-network-connections` | `e4-typed-network-connections` |
| C: E-5 | this plan | `refactor/shared-network-vocabulary` | `e5-shared-network-vocabulary` |

- The coordinator owns `docs/README.md`, status/evidence updates to the original review, final integration and the single global build/test slot, including focused tests. Independent source reads and edits may overlap; builds/tests never do.
- Each worker is sharing the workspace with others: preserve unrelated edits and adapt to integrated changes. Do not edit another stream's files.
- E-4 owns `TiaMcpServer.OpennessWorker/Openness/Network/NetworkDeviceConfigurator.cs` during its typed migration. E-5 must leave that file untouched until E-4 is integrated and the coordinator explicitly transfers ownership after rebase.
- E-5's Contracts, host, other worker and FakeWorker changes can proceed while A and B execute. No E-5 edit to worker `Program.cs` is required.
- Default local integration order is **A -> B -> C**. A is independent; B before C's final Configurator migration is a hard dependency. The coordinator records the integration SHA used for C's rebase.
- The reviewed plans are committed at `0f7162c` and present in each worktree. Read the approved copy and preserve unrelated working changes; do not copy a dirty checkout.

## File Ownership

All paths below are relative to the returned implementation worktree; use its actual absolute directory for commands.

| Owner | Exact paths | Responsibility |
| --- | --- | --- |
| C | `TiaMcpServer.Contracts/Network/NetworkVerificationStatuses.cs` (new) | Four exact status constants |
| C | `TiaMcpServer.Contracts/Network/NetworkSettingKeys.cs` (new) | Six result keys and two scalar evidence keys |
| C | `TiaMcpServer.Contracts/Network/SubnetLifecycleContract.cs` | Two type identifiers and exact nullable mapping |
| C | `TiaMcpServer/Network/NetworkPayloadContract.cs` | Independent worker-payload validation uses vocabulary |
| C | `TiaMcpServer/Network/NetworkWritePlanner.cs` | Requested setting/evidence keys and subnet mapping |
| C | `TiaMcpServer/Network/NetworkWriteVerifier.cs` | Status comparisons and final scalar checks |
| C | `TiaMcpServer/Network/NetworkWritePayloadBudget.cs` | Omitted/diagnostic evidence statuses |
| C | `TiaMcpServer.OpennessWorker/Openness/Network/NetworkPostconditionChecks.cs` | Compare/Complete string substitutions only |
| C | `TiaMcpServer.OpennessWorker/Openness/Network/NetworkMutationVerifier.cs` | Setting check names and subnet mapping |
| C | `TiaMcpServer.OpennessWorker/Openness/Network/SubnetLifecycleService.cs` | Shared identifiers/mapping with existing validation wrapper |
| C | `TiaMcpServer.OpennessWorker/Openness/Network/SubnetLifecycleMutationProbeService.cs` | Shared valid type identifiers; preserve intentional invalid type |
| B, then C | `TiaMcpServer.OpennessWorker/Openness/Network/NetworkDeviceConfigurator.cs` | After handoff, replace setting strings only |
| C | `TiaMcpServer.FakeWorker/Program.cs` | Valid Network fixture generators use shared vocabulary |
| C | `TiaMcpServer.Tests/Network/NetworkVocabularyContractTests.cs` (new) | Exact-value and mapping regressions |
| C | `TiaMcpServer.Tests/Network/NetworkMutationVerificationTests.cs` | Aggregate precedence regression; existing protocol checks retained |
| C | `TiaMcpServer.Tests/Network/NetworkSubnetLifecycleWorkerServiceContractTests.cs` | Replace source assertion demanding private type constants |
| C, coordinator-expanded | `TiaMcpServer.Tests/Network/NetworkSubnetLifecyclePayloadContractTests.cs` | Preserve direct-projection rejection for missing/unsupported request types and explicit-null evidence |
| C after B handoff | `TiaMcpServer.Tests/Network/NetworkDeviceCreatorWorkerContractTests.cs` | Migrate the existing setter-order source assertion with the key substitutions |

No `.csproj` change is expected: Contracts and tests include new local `.cs` files automatically, and all consumers reference Contracts. Existing tests source-link `NetworkPostconditionChecks` and the four host files. If execution reveals missing project wiring, coordinate ownership with A and the coordinator before editing `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`.

## Review Focus

1. Case variants, null and unsupported subnet types must not expand the accepted set; exact mapping tests and existing catalog rejection tests cover this.
2. A failed check must dominate an unverified check, and empty evidence must keep its existing contextual meaning; direct `Complete` tests and host rejection tests cover this.
3. `IoSystem` in applied/skipped results must still expand to the two scalar evidence keys; existing `IoSystemVerification_RequiresScalarSubnetAndNumberChecks` covers this.
4. Unknown/contradictory worker evidence must remain `protocol_error` without echoing rejected data; existing malformed-evidence tests cover this independently.
5. E-4's typed native calls and exception filters must survive C's final edit unchanged; inspect the final diff against B's integrated Configurator and rerun B's offline regressions.

---

### Task 1: Establish the Execution Boundary

**Files:** Read the three reviewed plans, applicable `AGENTS.md`, project files and the ownership table; modify no product file yet.

**Interfaces:** Consumes coordinator-approved base SHA, absolute plan paths and build/test-slot coordination; produces an isolated C checkout and recorded ownership gates.

- [x] Confirm the user has approved executing the reviewed plans. The current draft-writing request is insufficient.
- [x] Revalidate `git status --short`, `git branch --show-current` and `git rev-parse HEAD` in the coordinator checkout. Record the common approved base, initially `7c04b44`; do not independently substitute a newer `main`.
- [x] Read `superpowers:using-git-worktrees`, call `list_artifacts`, and reuse a suitable C worktree or create a native managed worktree named `e5-shared-network-vocabulary` at the recorded base. Use the returned absolute workspace directory explicitly, then create `refactor/shared-network-vocabulary` there if needed.
- [x] Read this plan from its coordinator-provided absolute path. Record that Configurator remains owned by B, and request the coordinator's single verification slot before every test/build command below.
- [x] Consume the coordinator's successful [shared baseline check](2026-10-25-e1-e2-worker-context-gates.md#task-0-prepare-the-shared-execution-baseline) before editing. This setup does not require A's implementation to finish; do not duplicate the same baseline checks in every identical starting tree.

### Task 2: Add and Pin the Shared Vocabulary

**Files:** Three Contracts files and new `TiaMcpServer.Tests/Network/NetworkVocabularyContractTests.cs`.

**Interfaces produced:**

- `public static class NetworkVerificationStatuses` with `public const string Passed`, `Failed`, `Unverified`, `NotRequired`, respectively `"passed"`, `"failed"`, `"unverified"`, `"not_required"`.
- `public static class NetworkSettingKeys` with `public const string Address`, `SubnetMask`, `PnDeviceNameAutoGeneration`, `PnDeviceName`, `Subnet`, `IoSystem`, `IoSystemSubnet`, `IoSystemNumber`; each value exactly equals its member name.
- In existing `SubnetLifecycleContract`: `public const string EthernetTypeIdentifier = "System:Subnet.Ethernet"`, `ProfibusTypeIdentifier = "System:Subnet.Profibus"`, and `public static string? TypeIdentifierFor(string? networkType)`.
- The mapper uses ordinal exact matching for existing `Ethernet`/`Profibus`; null, empty, case variants and unsupported values return null. It does not concatenate arbitrary input or throw a new boundary exception.

- [x] Add `VerificationStatuses_HaveExactWireValues` and `SettingKeys_HaveExactWireValues`, asserting ordered literal expected arrays against the corresponding constants. Expected values must remain literals.
- [x] Add theory `TypeIdentifierFor_MapsOnlyApprovedNetworkTypes(string? input, string? expected)` with `Ethernet` -> `System:Subnet.Ethernet`, `Profibus` -> `System:Subnet.Profibus`, and null results for null, `""`, `"ethernet"`, `"PROFIBUS"`, `" Ethernet"`, `"Unknown"`, `"System:Subnet.Ethernet"`. Assert the two identifier constants equal the literal expected identifiers too.
- [x] Ask the coordinator to run the new tests before production edits. Expected red result: missing new classes/members at compilation; record that evidence rather than treating it as a behavior failure.

```powershell
dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~NetworkVocabularyContractTests"
```

- [x] Implement only the declared constants and mapper. Preserve existing `IsSupportedNetworkType`, address bounds and transmission-speed vocabulary.
- [x] Rerun the same command through the coordinator; require all cases to pass. Inspect the exact diff and `git diff --check`.
- [x] When implementation execution is approved, commit only Task 2 files: `git commit -m "refactor: centralize network contract vocabulary"`. Do not include draft plans or coordinator documentation changes.

### Task 3: Migrate Consumers Without Changing Behavior

**Files:** All C-owned consumer files and the two existing test files in the ownership table. Configurator is excluded until Task 4.

**Interfaces consumed:** Task 2 constants and `SubnetLifecycleContract.TypeIdentifierFor(string?)`.

- [x] Add `Complete_PreservesStatusPrecedence(string statuses, string expected)` to `NetworkMutationVerificationTests`. Build checks from comma-separated literal statuses and call `NetworkPostconditionChecks.Complete`; assert `"passed"` -> `"passed"`, `"passed,unverified"` -> `"unverified"`, both `"unverified,failed"` and `"failed,unverified"` -> `"failed"`, and empty checks -> `"not_required"`. Assert messages remain null for passed/empty and present for failed/unverified.
- [x] Run the precedence cases and existing mutation verification, sparse configuration and subnet request tests through the coordinator before substitutions; they should pass on the established behavior.
- [x] Replace verification-status and setting/evidence-key literals in the four host files, worker postcondition/mutation verification files and valid Network FakeWorker generators. Keep host `ValidateVerification`'s known-value, contradiction and aggregate checks separate; change only the vocabulary they reference.
- [x] Replace valid subnet type literals/prefix concatenation in host planner/payload contract, worker mutation verifier and FakeWorker generators with the shared mapper after the existing request gate. Check each caller's validation path; do not route intentional invalid fixtures through this mapper or add a permissive fallback.
- [x] In `SubnetLifecycleService.ResolveTypeIdentifier(string networkType)`, use the shared nullable mapper and retain the existing categorized exception and exact message when it returns null. Use shared identifiers for supported-current-type checks. In the probe, replace its two private valid identifier constants, keeping `System:Subnet.Phase4Invalid` literal.
- [x] Update `Service_MapsOnlyTheTwoApprovedTypeIdentifiers` to assert the service references `SubnetLifecycleContract.TypeIdentifierFor(networkType)` and shared identifier members, and no longer declares private identifier constants. Keep executable exact-value/mapping assertions in the new test file; retain service validation-category/message assertions or add them to this existing source-contract case.
- [x] Keep existing literal assertions in `Configuration_ReportsOnlyRequestedKeys`, `IoSystemVerification_RequiresScalarSubnetAndNumberChecks`, `NotRequired_IsOnlyValidForNoAppliedConfiguration`, `UnknownOrUnaccountedSetting_IsProtocolError`, `MalformedEvidence_IsRejectedWithoutEcho` and subnet payload/FakeWorker tests. Do not rewrite unrelated batch `failed` statuses or HMI fixtures to Network constants.
- [x] Run the focused command below in the coordinator slot. Require all selected tests to pass; no canonical JSON or schema snapshot change is expected or approved.

```powershell
dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~NetworkVocabularyContractTests|FullyQualifiedName~NetworkMutationVerificationTests|FullyQualifiedName~NetworkSparseConfigurationTests|FullyQualifiedName~NetworkSubnetLifecycle|FullyQualifiedName~NetworkGuardedWrite|FullyQualifiedName~NetworkOperationFakeWorkerTests|FullyQualifiedName~NetworkStructuredProtocolTests|FullyQualifiedName~NetworkPayloadContractTests"
```

- [x] Review only owned changes, confirm Configurator is untouched, and run `git diff --check`. During authorized execution, commit: `git commit -m "refactor: share network vocabulary across consumers"`, staging only Task 3 files.

### Task 4: Integrate E-4 and Finish Configurator Substitutions

**Files:** `TiaMcpServer.OpennessWorker/Openness/Network/NetworkDeviceConfigurator.cs` and the existing PnDeviceName setter-order assertion in `TiaMcpServer.Tests/Network/NetworkDeviceCreatorWorkerContractTests.cs`, after coordinator transfer only.

**Interfaces consumed:** B's integrated typed connections and Task 2 `NetworkSettingKeys`; produces the same result keys around B's final native calls.

- [x] Wait for B to be locally integrated, after A in the default order. Obtain the coordinator-recorded integration SHA, rebase C onto it, resolve only owned conflicts and request explicit Configurator ownership transfer.
- [x] Read B's final Configurator. Replace only settings vocabulary in `Configure`, `ConnectSubnet(Node, string, Subnet, ConfigureNetworkDeviceResultInfo)` and `ConnectIoSystem(IoConnector, IoSystem, int, ConfigureNetworkDeviceResultInfo)`; preserve the actual signatures if B changed them while implementing its reviewed plan.
- [x] Compare against B's integrated file: typed `ConnectToSubnet`/`ConnectToIoSystem`, catch filters, skip precedence and write/verification order must be unchanged. Do not reintroduce reflection or unwrap new exception types.
- [x] Through the coordinator, rerun the Task 3 focused command and B's offline regressions below. Require their prior behavior assertions and exact literal result keys to pass. The metadata/source checks do not execute native connection wrappers.

```powershell
dotnet test TiaMcpServer.Tests --configuration Release /p:UseTiaPortalReferenceStubs=true --filter "FullyQualifiedName~NetworkDeviceCreatorWorkerContractTests|FullyQualifiedName~NetworkMutationVerificationTests|FullyQualifiedName~ReferenceStubArtifactTests"
```
- [x] Review the diff and `git diff --check`. During authorized execution, commit only this handoff: `git commit -m "refactor: use shared network setting keys in configurator"`.

### Task 5: Qualify and Deliver the Integrated Candidate

**Files:** Review all C changes. The coordinator alone updates existing documentation/evidence and integrates C; C creates no extra report or top-level document.

- [x] Ask for independent review of the complete C diff, concentrating on wire spelling, nullable mapping callers, independent host trust validation, malformed fixtures and B's preserved native calls. Fix any actionable findings in owned files with focused verification and a conventional fix commit during authorized execution.
- [x] The coordinator runs one serial Release stub build followed by the ordinary non-packaging suite with current CI coverage settings, from C's rebased candidate or the final integrated candidate. Reuse that evidence for the same SHA instead of running duplicate full suites.

```powershell
$taskE5Results = Join-Path (Get-Location).Path ('TestResults/e5/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff'))
dotnet build TiaMcpServer.slnx --configuration Release -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build --configuration Release --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory $taskE5Results --filter "Category!=Packaging"
```

- [x] Require every command to exit successfully. If Windows temp restrictions affect coverage, the coordinator uses process-local `TEMP`/`TMP` under ignored `TestResults/e5/temp`, restores their original values afterward, and records the adjustment. Rerun failed collection into a new timestamped results directory. Require exactly one fresh, nonzero report and enforce the gate:

```powershell
$taskE5Reports = @(Get-ChildItem -LiteralPath $taskE5Results -Recurse -Filter coverage.cobertura.xml)
if ($taskE5Reports.Count -ne 1) { throw 'Expected exactly one current E-5 coverage report.' }
pwsh -NoProfile -File scripts/verify-coverage-threshold.ps1 -CoveragePath ($taskE5Reports[0].FullName) -MinimumLineRate 0.80
```
- [x] Report the existing scoped 80% gate accurately. `TiaMcpServer.Tests/coverage.runsettings` excludes worker/FakeWorker code, including source-linked worker namespaces; passing it does not measure all changed worker lines. Cite executable `NetworkPostconditionChecks` tests, source-contract checks and successful net48 compilation separately.
- [x] E-5 alone has no packaging trigger. The combined candidate includes E-4's `reference-stubs/` changes, so the coordinator's final gate also runs `pwsh -NoProfile -File scripts/verify-reference-stubs.ps1 -Configuration Release -NoRestore` and `dotnet test TiaMcpServer.Tests --configuration Release --no-build --filter "Category=Packaging"`, unless that exact combined source candidate already has those passing checks. Do not omit the combined packaging gate merely because C changes constants only.
- [x] Inspect `git status --short`, the final commit range and `git diff --check`. Confirm no schema/version change, unrelated source, real Siemens implementation DLLs, plans or secrets entered C's commits; B's generated compile-only reference artifacts are expected.
- [x] Coordinator records SHA-specific evidence and locally integrates C after B. Report unchanged wire vocabulary, offline results, worker coverage limits and zero E-5 live testing; do not claim E-4 runtime acceptance. No push, PR, deployment or plan commit follows without the corresponding user authorization.

## Completion Boundary

E-5 is ready for local integration when the shared vocabulary is used by production consumers and valid generators, exact wire/protocol regressions pass, B's typed calls survive the handoff, review findings are resolved and the coordinator's integrated offline qualification passes. No shared status algorithm or broader attribute registry is part of completion.

The original draft checklist is retained above with completed offline steps marked. The execution record below describes the original offline qualification and live-testing stop; the delivery section records the later main rebase and publication authorization.

## Execution record 2026-10-25

The maintainer approved the plans, committed them as `0f7162c6090799d9d12358f94089ed345f3005bb`, and authorized isolated implementation. C used branch `refactor/shared-network-vocabulary` and managed worktree `e5-shared-network-vocabulary`. Source edits ran in parallel; every restore/build/test used one coordinator-controlled slot. During that initial implementation, main remained at the approved plan commit.

### Implementation and integration

- Vocabulary: original `806a88ebbdab9e2c2324b24eac708cdbdc069fd4`, rebased `44af84c`. Added the declared protocol constants and exact nullable two-type mapper. Expected missing-interface compilation errors preceded 11 passing vocabulary tests.
- Consumers: original `b25c5d22b53cf893e1cfb725f93eb18e24d63827`, rebased `374d79c38d43c1a51c341219232fd4ee88672fc8`. A 141-test characterization baseline included five precedence cases. Direct projection needed an explicit null-mapper guard to retain rejection of missing/unsupported requested types: temporarily omitting only that guard made both new cases incorrectly succeed (two expected test failures). Restoring it produced 893 focused passes. Host validation/aggregation and malformed fixture literals remain independent.
- Handoff: after B passed its gate and recorded `b5e29e4558a36653950734d95b6a7c3263fd9c46`, C rebased onto that integrated A+B result. Git range-diff confirmed the first two C increments replayed unchanged; the coordinator then transferred Configurator and its literal-sensitive setter-order assertion.
- Final substitution: `eb8d873fa639cdc92de9e8e348f84d5f7c92e513`. Exactly nine keys changed from literals to constants. All B typed calls, catches, signatures and ordering were preserved. One expected ordering-test red preceded 894 focused and 79 reference/configurator regression passes.

The two extra test-file ownership rows were narrow coordinator-approved coverage expansions, not new functionality. Independent per-task and strongest whole-workstream reviews approved the complete 18-file C delta without actionable findings. Public schemas, wire values, accepted inputs, sparse IO scalar evidence, response-budget sizing, omissions and delivery semantics remain unchanged.

### Final combined offline gate

The tested source candidate was `eb8d873fa639cdc92de9e8e348f84d5f7c92e513`, with qualified A and B in its ancestry. All commands ran serially; no TIA runtime was started.

| Check | Observed result |
| --- | --- |
| Release stub solution build | Passed, zero errors; seven existing nullable warnings |
| Canonical reference verifier | Passed; the fresh C worktree restored the dedicated stub solution before verification |
| Ordinary suite, CI coverage settings, `Category!=Packaging` | 7,696/7,696 passed, zero failures/skips; reported duration 6 min 34 sec |
| Existing scoped line-coverage gate | 12,980/13,649 lines, 95.09%; exceeds 80% |
| Required combined packaging suite | 39/39 passed, zero failures/skips; reported duration 1 min 16 sec |
| Installed-V21-reference Release solution build | Passed, zero errors; seven existing nullable warnings |
| Reference-source warnings | Two existing Step7 CS0108 warnings in unchanged members; repeated summary lines are not additional distinct warnings |
| Independent task/whole-source reviews | Approved, no actionable findings |

Evidence remains under ignored `TestResults/e5-qualification-20261025T200516234/`. Process-local `TEMP`/`TMP` were restored and coverage output was separate from temporary files. The CI coverage scope excludes worker/FakeWorker namespaces; 95.09% is not native coverage. Executable postcondition/gate cases, source-wiring checks, metadata tests and SDK compilation are separate evidence.

Final changes after this source gate are documentation only. Source identity, links, whitespace and clean Git state are checked for the evidence commit; passing source qualification is reused without repeating expensive suites.

### Delivery and remaining limits

A and B subsequently merged through [PR #192](https://github.com/Czarnak/tia-portal-mcp/pull/192) and [PR #193](https://github.com/Czarnak/tia-portal-mcp/pull/193). B's separately authorized installed-candidate connection, engineering-rejection and restoration checks passed within the [E-4 native scope and limits](2026-10-25-e4-typed-network-connections.md#native-acceptance-record-2026-10-25). This is separate evidence and is not an E-5 native run.

On 2026-10-25 the maintainer authorized C's rebase, documentation reconciliation, push and own PR, explicitly allowing reuse of existing test evidence when production source remained unchanged. The four local C commits from `944bbdec58bfb45eba605a477c6ad7e8f062310e` were rebased onto current main `232fb57bcd7fc0b206c089bbdf02b9bc3b3fd2f5`. Source commits replayed cleanly. The only conflict was `docs/README.md`; resolution retained main's scoped E-4 native record and C's E-5 implementation entry. The rebased series completed at `078fd5d0d0b20c86207da279e0179ad769dd7092`, followed by documentation-only reconciliation.

Direct tree comparison with the qualified candidate passed: `git diff --exit-code eb8d873fa639cdc92de9e8e348f84d5f7c92e513 HEAD -- . ':!docs/**'` produced no differences. This includes production, tests, build configuration and reference artifacts. The recorded 7,696 ordinary tests, 39 packaging tests, 95.09% scoped coverage, provenance checks and both reference builds therefore remain the applicable evidence; full suites were not rerun. Links, conflict markers, privacy, whitespace and the final PR scope receive focused checks before publication.

E-5 is prepared for its own PR against `main`; the managed worktree is retained. Its exact-vocabulary refactor needs no standalone live run and does not extend B's native acceptance to other equipment, faults or modes. No deployment, installation, save/compile/download, online or PLC runtime action was performed during this rebase/publication work. Worker/FakeWorker coverage exclusions and the original native-evidence limits remain explicit.
