# E-5 Shared Network Vocabulary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Status:** Draft for maintainer review; uncommitted. This document does not authorize implementation, worktree creation, commits, remote writes or live TIA operations.

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
- These draft plans stay uncommitted until the user separately approves committing them. Future implementation commits below apply only after execution is authorized.

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
- The reviewed plans are uncommitted in the coordinator checkout and will not appear in new worktrees. Executors read the coordinator-provided absolute plan paths; do not copy the dirty checkout or assume a plan commit exists.

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

- [ ] Confirm the user has approved executing the reviewed plans. The current draft-writing request is insufficient.
- [ ] Revalidate `git status --short`, `git branch --show-current` and `git rev-parse HEAD` in the coordinator checkout. Record the common approved base, initially `7c04b44`; do not independently substitute a newer `main`.
- [ ] Read `superpowers:using-git-worktrees`, call `list_artifacts`, and reuse a suitable C worktree or create a native managed worktree named `e5-shared-network-vocabulary` at the recorded base. Use the returned absolute workspace directory explicitly, then create `refactor/shared-network-vocabulary` there if needed.
- [ ] Read this plan from its coordinator-provided absolute path. Record that Configurator remains owned by B, and request the coordinator's single verification slot before every test/build command below.
- [ ] Consume the coordinator's successful [shared baseline check](2026-10-25-e1-e2-worker-context-gates.md#task-0-prepare-the-shared-execution-baseline) before editing. This setup does not require A's implementation to finish; do not duplicate the same baseline checks in every identical starting tree.

### Task 2: Add and Pin the Shared Vocabulary

**Files:** Three Contracts files and new `TiaMcpServer.Tests/Network/NetworkVocabularyContractTests.cs`.

**Interfaces produced:**

- `public static class NetworkVerificationStatuses` with `public const string Passed`, `Failed`, `Unverified`, `NotRequired`, respectively `"passed"`, `"failed"`, `"unverified"`, `"not_required"`.
- `public static class NetworkSettingKeys` with `public const string Address`, `SubnetMask`, `PnDeviceNameAutoGeneration`, `PnDeviceName`, `Subnet`, `IoSystem`, `IoSystemSubnet`, `IoSystemNumber`; each value exactly equals its member name.
- In existing `SubnetLifecycleContract`: `public const string EthernetTypeIdentifier = "System:Subnet.Ethernet"`, `ProfibusTypeIdentifier = "System:Subnet.Profibus"`, and `public static string? TypeIdentifierFor(string? networkType)`.
- The mapper uses ordinal exact matching for existing `Ethernet`/`Profibus`; null, empty, case variants and unsupported values return null. It does not concatenate arbitrary input or throw a new boundary exception.

- [ ] Add `VerificationStatuses_HaveExactWireValues` and `SettingKeys_HaveExactWireValues`, asserting ordered literal expected arrays against the corresponding constants. Expected values must remain literals.
- [ ] Add theory `TypeIdentifierFor_MapsOnlyApprovedNetworkTypes(string? input, string? expected)` with `Ethernet` -> `System:Subnet.Ethernet`, `Profibus` -> `System:Subnet.Profibus`, and null results for null, `""`, `"ethernet"`, `"PROFIBUS"`, `" Ethernet"`, `"Unknown"`, `"System:Subnet.Ethernet"`. Assert the two identifier constants equal the literal expected identifiers too.
- [ ] Ask the coordinator to run the new tests before production edits. Expected red result: missing new classes/members at compilation; record that evidence rather than treating it as a behavior failure.

```powershell
dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~NetworkVocabularyContractTests"
```

- [ ] Implement only the declared constants and mapper. Preserve existing `IsSupportedNetworkType`, address bounds and transmission-speed vocabulary.
- [ ] Rerun the same command through the coordinator; require all cases to pass. Inspect the exact diff and `git diff --check`.
- [ ] When implementation execution is approved, commit only Task 2 files: `git commit -m "refactor: centralize network contract vocabulary"`. Do not include draft plans or coordinator documentation changes.

### Task 3: Migrate Consumers Without Changing Behavior

**Files:** All C-owned consumer files and the two existing test files in the ownership table. Configurator is excluded until Task 4.

**Interfaces consumed:** Task 2 constants and `SubnetLifecycleContract.TypeIdentifierFor(string?)`.

- [ ] Add `Complete_PreservesStatusPrecedence(string statuses, string expected)` to `NetworkMutationVerificationTests`. Build checks from comma-separated literal statuses and call `NetworkPostconditionChecks.Complete`; assert `"passed"` -> `"passed"`, `"passed,unverified"` -> `"unverified"`, both `"unverified,failed"` and `"failed,unverified"` -> `"failed"`, and empty checks -> `"not_required"`. Assert messages remain null for passed/empty and present for failed/unverified.
- [ ] Run the precedence cases and existing mutation verification, sparse configuration and subnet request tests through the coordinator before substitutions; they should pass on the established behavior.
- [ ] Replace verification-status and setting/evidence-key literals in the four host files, worker postcondition/mutation verification files and valid Network FakeWorker generators. Keep host `ValidateVerification`'s known-value, contradiction and aggregate checks separate; change only the vocabulary they reference.
- [ ] Replace valid subnet type literals/prefix concatenation in host planner/payload contract, worker mutation verifier and FakeWorker generators with the shared mapper after the existing request gate. Check each caller's validation path; do not route intentional invalid fixtures through this mapper or add a permissive fallback.
- [ ] In `SubnetLifecycleService.ResolveTypeIdentifier(string networkType)`, use the shared nullable mapper and retain the existing categorized exception and exact message when it returns null. Use shared identifiers for supported-current-type checks. In the probe, replace its two private valid identifier constants, keeping `System:Subnet.Phase4Invalid` literal.
- [ ] Update `Service_MapsOnlyTheTwoApprovedTypeIdentifiers` to assert the service references `SubnetLifecycleContract.TypeIdentifierFor(networkType)` and shared identifier members, and no longer declares private identifier constants. Keep executable exact-value/mapping assertions in the new test file; retain service validation-category/message assertions or add them to this existing source-contract case.
- [ ] Keep existing literal assertions in `Configuration_ReportsOnlyRequestedKeys`, `IoSystemVerification_RequiresScalarSubnetAndNumberChecks`, `NotRequired_IsOnlyValidForNoAppliedConfiguration`, `UnknownOrUnaccountedSetting_IsProtocolError`, `MalformedEvidence_IsRejectedWithoutEcho` and subnet payload/FakeWorker tests. Do not rewrite unrelated batch `failed` statuses or HMI fixtures to Network constants.
- [ ] Run the focused command below in the coordinator slot. Require all selected tests to pass; no canonical JSON or schema snapshot change is expected or approved.

```powershell
dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~NetworkVocabularyContractTests|FullyQualifiedName~NetworkMutationVerificationTests|FullyQualifiedName~NetworkSparseConfigurationTests|FullyQualifiedName~NetworkSubnetLifecycle|FullyQualifiedName~NetworkGuardedWrite|FullyQualifiedName~NetworkOperationFakeWorkerTests|FullyQualifiedName~NetworkStructuredProtocolTests|FullyQualifiedName~NetworkPayloadContractTests"
```

- [ ] Review only owned changes, confirm Configurator is untouched, and run `git diff --check`. During authorized execution, commit: `git commit -m "refactor: share network vocabulary across consumers"`, staging only Task 3 files.

### Task 4: Integrate E-4 and Finish Configurator Substitutions

**Files:** `TiaMcpServer.OpennessWorker/Openness/Network/NetworkDeviceConfigurator.cs`, after coordinator transfer only.

**Interfaces consumed:** B's integrated typed connections and Task 2 `NetworkSettingKeys`; produces the same result keys around B's final native calls.

- [ ] Wait for B to be locally integrated, after A in the default order. Obtain the coordinator-recorded integration SHA, rebase C onto it, resolve only owned conflicts and request explicit Configurator ownership transfer.
- [ ] Read B's final Configurator. Replace only settings vocabulary in `Configure`, `ConnectSubnet(Node, string, Subnet, ConfigureNetworkDeviceResultInfo)` and `ConnectIoSystem(IoConnector, IoSystem, int, ConfigureNetworkDeviceResultInfo)`; preserve the actual signatures if B changed them while implementing its reviewed plan.
- [ ] Compare against B's integrated file: typed `ConnectToSubnet`/`ConnectToIoSystem`, catch filters, skip precedence and write/verification order must be unchanged. Do not reintroduce reflection or unwrap new exception types.
- [ ] Through the coordinator, rerun the Task 3 focused command and B's offline regressions below. Require their prior behavior assertions and exact literal result keys to pass. The metadata/source checks do not execute native connection wrappers.

```powershell
dotnet test TiaMcpServer.Tests --configuration Release /p:UseTiaPortalReferenceStubs=true --filter "FullyQualifiedName~NetworkDeviceCreatorWorkerContractTests|FullyQualifiedName~NetworkMutationVerificationTests|FullyQualifiedName~ReferenceStubArtifactTests"
```
- [ ] Review the diff and `git diff --check`. During authorized execution, commit only this handoff: `git commit -m "refactor: use shared network setting keys in configurator"`.

### Task 5: Qualify and Deliver the Integrated Candidate

**Files:** Review all C changes. The coordinator alone updates existing documentation/evidence and integrates C; C creates no extra report or top-level document.

- [ ] Ask for independent review of the complete C diff, concentrating on wire spelling, nullable mapping callers, independent host trust validation, malformed fixtures and B's preserved native calls. Fix any actionable findings in owned files with focused verification and a conventional fix commit during authorized execution.
- [ ] The coordinator runs one serial Release stub build followed by the ordinary non-packaging suite with current CI coverage settings, from C's rebased candidate or the final integrated candidate. Reuse that evidence for the same SHA instead of running duplicate full suites.

```powershell
$taskE5Results = Join-Path (Get-Location).Path ('TestResults/e5/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff'))
dotnet build TiaMcpServer.slnx --configuration Release -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build --configuration Release --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory $taskE5Results --filter "Category!=Packaging"
```

- [ ] Require every command to exit successfully. If Windows temp restrictions affect coverage, the coordinator uses process-local `TEMP`/`TMP` under ignored `TestResults/e5/temp`, restores their original values afterward, and records the adjustment. Rerun failed collection into a new timestamped results directory. Require exactly one fresh, nonzero report and enforce the gate:

```powershell
$taskE5Reports = @(Get-ChildItem -LiteralPath $taskE5Results -Recurse -Filter coverage.cobertura.xml)
if ($taskE5Reports.Count -ne 1) { throw 'Expected exactly one current E-5 coverage report.' }
pwsh -NoProfile -File scripts/verify-coverage-threshold.ps1 -CoveragePath ($taskE5Reports[0].FullName) -MinimumLineRate 0.80
```
- [ ] Report the existing scoped 80% gate accurately. `TiaMcpServer.Tests/coverage.runsettings` excludes worker/FakeWorker code, including source-linked worker namespaces; passing it does not measure all changed worker lines. Cite executable `NetworkPostconditionChecks` tests, source-contract checks and successful net48 compilation separately.
- [ ] E-5 alone has no packaging trigger. The combined candidate includes E-4's `reference-stubs/` changes, so the coordinator's final gate also runs `pwsh -NoProfile -File scripts/verify-reference-stubs.ps1 -Configuration Release -NoRestore` and `dotnet test TiaMcpServer.Tests --configuration Release --no-build --filter "Category=Packaging"`, unless that exact combined source candidate already has those passing checks. Do not omit the combined packaging gate merely because C changes constants only.
- [ ] Inspect `git status --short`, the final commit range and `git diff --check`. Confirm no schema/version change, unrelated source, real Siemens implementation DLLs, plans or secrets entered C's commits; B's generated compile-only reference artifacts are expected.
- [ ] Coordinator records SHA-specific evidence and locally integrates C after B. Report unchanged wire vocabulary, offline results, worker coverage limits and zero E-5 live testing; do not claim E-4 runtime acceptance. No push, PR, deployment or plan commit follows without the corresponding user authorization.

## Completion Boundary

E-5 is ready for local integration when the shared vocabulary is used by production consumers and valid generators, exact wire/protocol regressions pass, B's typed calls survive the handoff, review findings are resolved and the coordinator's integrated offline qualification passes. No shared status algorithm or broader attribute registry is part of completion.

This is an unexecuted draft. No source edits, worktrees, commits, builds, tests or live verification were performed while writing it.
