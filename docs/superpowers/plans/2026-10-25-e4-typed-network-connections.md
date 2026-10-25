# E-4 Typed Network Connections Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Status:** Draft, uncommitted; awaiting user review. This document does not authorize implementation, remote writes, or live TIA operations. Do not commit this plan without later explicit approval.

**Goal:** Replace speculative reflection in network attachment with the verified TIA Portal V21 typed APIs while preserving selection, partial-result, and rejection behavior.

**Architecture:** Keep the existing guarded `configure_network_device` path and its `NetworkDeviceConfigurator` implementation. Carry `IoSystem` as its actual SDK type, call the two public instance methods directly, and remove only the obsolete reflection machinery. Extend the source-owned compile stubs and regenerate their existing reference artifacts through the canonical updater.

**Tech Stack:** Windows; .NET SDK pinned by `global.json`; C# worker targeting net48; net10.0 xUnit tests; Siemens TIA Portal Openness V21; `System.Reflection.Metadata` for offline reference inspection.

**Spec:** [Authoritative simplification review, E-4](2026-10-17-codebase-simplification-review.md) and [corrected finding WORKERNET-9](2026-10-17-codebase-simplification-review-findings.md). The user-approved approach is this plan's design input; do not create a separate design document.

## Global Constraints

- Planning baseline: clean `main` at `7c04b44`, observed on 2026-10-25. The coordinator must revalidate and record one approved full base SHA before any execution worktree is created.
- All three workstreams start from that same recorded base; do not substitute a later local HEAD silently.
- Preserve unrelated work. No source, tests, worktrees, builds, staging, commits, or live operations are part of drafting this document.
- Use managed worktrees at execution time after inspecting `list_artifacts` and reading `superpowers:using-git-worktrees`; the returned absolute workspace directory is authoritative for every command.
- E-4 worktree name: `e4-typed-network-connections`; branch: `refactor/typed-network-connections`.
- The reviewed plans may remain uncommitted and therefore absent from new worktrees. Read the coordinator-provided absolute plan paths; do not copy the dirty checkout or assume the plans are committed.
- The coordinator owns `docs/README.md`, the original review's status, evidence updates, integration, and any shared-file conflict resolution.
- One coordinator-granted build/test slot covers all branches, including focused tests, restores, stub regeneration, packaging, and coverage. Do not run those operations concurrently.
- No remote writes, live project attachment, engineering mutation, PLC download, or PLC run/stop is authorized by this plan.
- Commit implementation/fix steps only after implementation is separately authorized and relevant verification passes. Do not stage these draft plans in implementation commits.

## Ownership and integration

| Workstream | Branch / managed worktree | Ownership relevant to E-4 |
| --- | --- | --- |
| A: E-1/E-2 | `refactor/worker-context-gates` / `e1-e2-worker-context-gates` | Context/open-project gates; independent of E-4. |
| B: E-4 | `refactor/typed-network-connections` / `e4-typed-network-connections` | Exact files below; sole owner of `NetworkDeviceConfigurator.cs` throughout the typed-call change. |
| C: E-5 | `refactor/shared-network-vocabulary` / `e5-shared-network-vocabulary` | Constants and other consumers may proceed in parallel; queue configurator substitutions until B is integrated. |

Default integration order is A, then B, then C. Only B before C finalization is a hard dependency; A is independent. C rebases onto the coordinator's integrated B result before making configurator substitutions and rerunning affected checks. Do not let B and C edit that file concurrently.

Sibling plans: [E-1/E-2](2026-10-25-e1-e2-worker-context-gates.md), [E-5](2026-10-25-e5-shared-network-vocabulary.md).

| File owned by B | Responsibility |
| --- | --- |
| `TiaMcpServer.OpennessWorker/Openness/Network/NetworkDeviceConfigurator.cs` | Typed calls, typed `IoSystem` flow, removal of obsolete reflection code. |
| `reference-stubs/Siemens.Engineering.Base/Hardware.cs` | Exactly two public SDK method declarations. |
| `ref/Siemens.Engineering.Base.dll` | Regenerated compiler reference artifact. |
| `ref/Siemens.Engineering.Step7.dll` | Regenerated artifact with the aggregate source hash. |
| `ref/Siemens.Engineering.WinCCUnified.dll` | Regenerated artifact with the aggregate source hash. |
| `TiaMcpServer.Tests/Diagnostics/ReferenceStubArtifactTests.cs` | Executable metadata regression for both exact method signatures. |

No `TiaMcpServer.Tests.csproj` change, new dependency, runtime adapter, or extra compile-probe project is planned. Ask the coordinator to resolve any newly discovered ownership overlap before editing another file.

## Verified API and behavior decisions

Read-only installed-DLL metadata inspection on 2026-10-25 established these public, instance, nonvirtual methods:

```csharp
// Receiver: Siemens.Engineering.HW.Node
public void ConnectToSubnet(Siemens.Engineering.HW.Subnet subnet);
// Receiver: Siemens.Engineering.HW.IoConnector
public void ConnectToIoSystem(Siemens.Engineering.HW.IoSystem ioSystem);
```

Source: `C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48\Siemens.Engineering.Base.dll`; assembly `21.0.0.0`, file `2100.0.121.1`, SHA256 `8C12B0FA70C298F1CD1221105880752AB204ED63C9F6D9DCB68833D977BCB5D0`. `ReflectionOnlyLoadFrom` read metadata without creating a Portal object. Recheck metadata if the installed assembly changes.

Official V21 references: [connecting a node to a subnet](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-on-networks/connecting-a-node-to-a-subnet), [IoConnector actions and signature](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-on-networks/get-an-io-connector), and [IO attachment prerequisites](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-on-networks/connecting-an-io-connector-to-an-io-system). The last page's sample omits the argument; its prose and installed metadata identify the one-argument method. It requires an unattached connector and the controller's subnet.

- Keep direct `EngineeringException` handling: record the message under the requested sparse skipped-setting key; report prior applied settings accurately.
- Remove the direct `InvalidOperationException` catches along with `TargetInvocationException` catches. The former handled the reflection helper's missing-method error; a native `InvalidOperationException` was previously wrapped and escaped. Catching it after direct invocation would newly turn it into a skipped setting. It remains a propagated call failure; direct exception diagnostics may differ from the former reflection wrapper, so do not promise byte-identical unexpected-failure text.
- Do not add a replacement missing-method fallback, exception unwrap helper, or speculative `Connect` alternative.
- Keep dependency/selector validation before the first scalar setter, exact IO-system selection, subnet-failure IO suppression, and immediate post-mutation verification.
- Do not widen this work into portal-loss classification or other catch-policy cleanup.

## Review Focus

- Missing or mistyped stub methods must fail executable signature checks and worker compilation; Task 1 pins both declarations.
- Native engineering rejection must preserve sparse applied/skipped results; Task 2 reviews the unchanged catch/result paths, and Task 4 requires a later authorized rejection case.
- A failed requested subnet attachment must suppress IO attachment; Task 2 runs the existing skip-precedence test and preserves its caller wiring; Task 4 covers the native case if safely reproducible.
- Invalid or ambiguous dependencies must still deny before scalar mutation; Task 2 runs the existing source-contract and selection tests; Task 4 checks unchanged target state after an authorized denial.
- Direct native `InvalidOperationException` must propagate rather than become a skipped setting; Task 2 checks the absence of that catch in the two wrappers. Native reproduction is an explicit deferred limitation, not an offline test claim.

---

### Task 0: Establish the approved isolated execution context

**Files:** Read the authoritative review, appendix, this plan, `AGENTS.md`, and relevant exact source files; do not change them during setup.

**Interfaces:** Consumes the coordinator's approved base SHA and absolute reviewed-plan paths; produces the attached absolute B worktree directory and the exact branch above.

- [ ] Confirm the user has approved implementation, the draft's file/behavior decisions, and the coordinator-recorded full base SHA.
- [ ] Inspect `list_artifacts`, follow `superpowers:using-git-worktrees`, reuse a suitable attached B worktree or call `create_worktree` with the approved base and `e4-typed-network-connections`; complete attachment if required.
- [ ] Use the returned absolute directory explicitly; create/switch to `refactor/typed-network-connections` without overwriting an existing branch or user changes. Check `git status --short`, `git rev-parse HEAD`, and `git branch --show-current`.
- [ ] Read this uncommitted reviewed plan from the coordinator-provided absolute path and the two authoritative specification files. Record base SHA, branch, directory, and ownership in the coordinator's execution record.
- [ ] Consume the coordinator's successful [shared baseline check](2026-10-25-e1-e2-worker-context-gates.md#task-0-prepare-the-shared-execution-baseline) before editing. It is shared setup, not a dependency on A's implementation; do not rerun the same checks in every identical starting tree.

### Task 1: Pin the typed SDK surface and refresh source-owned references

**Files:** Modify `ReferenceStubArtifactTests.cs`, `Hardware.cs`, and all three owned `ref` artifacts listed above.

**Interfaces:** Consumes the verified SDK declarations; produces reference assemblies exposing the two exact methods with current shared provenance.

- [ ] Add `BaseReferenceExposesTypedNetworkConnectionMethod(string receiver, string method, string parameter)` as a theory in `ReferenceStubArtifactTests.cs`, with these exact rows:

```csharp
[InlineData("Siemens.Engineering.HW.Node", "ConnectToSubnet", "Siemens.Engineering.HW.Subnet")]
[InlineData("Siemens.Engineering.HW.IoConnector", "ConnectToIoSystem", "Siemens.Engineering.HW.IoSystem")]
```

Use the file's existing `PEReader`, `MetadataReader`, `FullName`, and `BlobReader` pattern. Assert exactly one named method, public visibility, nonstatic/nonvirtual attributes, an instance nongeneric method signature, parameter count `1`, return `SignatureTypeCode.Void`, one `TypeHandle` matching the row's parameter type, and no remaining signature bytes. Do not load or execute the reference DLL.

- [ ] Acquire the global build/test slot. Restore if needed with `dotnet restore TiaMcpServer.slnx --disable-parallel`; run the new theory before editing stubs:

```powershell
dotnet test TiaMcpServer.Tests -c Release -m:1 --no-restore --filter "FullyQualifiedName~BaseReferenceExposesTypedNetworkConnectionMethod"
```

Expected: both rows fail because the methods are absent. A toolchain/restore failure is not the intended red result; resolve it before proceeding.

- [ ] Add `public void ConnectToSubnet(global::Siemens.Engineering.HW.Subnet subnet)` to the stub `Node` and `public void ConnectToIoSystem(global::Siemens.Engineering.HW.IoSystem ioSystem)` to stub `IoConnector`; use the surrounding `throw new global::System.NotSupportedException()` placeholder bodies.
- [ ] Regenerate through the existing updater, then verify without `-Update`:

```powershell
pwsh -NoProfile -File scripts/verify-reference-stubs.ps1 -Configuration Release -Update
pwsh -NoProfile -File scripts/verify-reference-stubs.ps1 -Configuration Release -NoRestore
dotnet test TiaMcpServer.Tests -c Release -m:1 --no-restore --filter "FullyQualifiedName~ReferenceStubArtifactTests"
```

Expected: exit `0`, both signature rows pass, and all three provenance checks pass. The first updater invocation restores the dedicated reference-stub solution, which is not restored by the product solution; do not add `-NoRestore` to that first invocation in a fresh worktree. The aggregate source hash covers all three stub projects (`verify-reference-stubs.ps1:53-74`), so all three tracked DLLs must change. Never hand-edit hashes or copy implementation `bin` DLLs.

- [ ] Review the exact Task 1 diff and run `git diff --check`; stage only its test, stub source, and three generated artifacts. Commit `test: pin typed V21 network connection references`. Release the global slot.

### Task 2: Replace reflection while preserving network outcomes

**Files:** Modify only `NetworkDeviceConfigurator.cs`; read `NetworkDeviceCreatorWorkerContractTests.cs`, `NetworkMutationVerificationTests.cs`, `NetworkPostconditionChecks.cs`, and dispatch verification in `Program.cs:671-693`.

**Interfaces:** Keeps both public `Configure` overloads unchanged. Change the private interfaces to `ConnectIoSystem(IoConnector ioConnector, IoSystem ioSystem, int ioSystemNumber, ConfigureNetworkDeviceResultInfo result)` and `IoSystem FindExactlyOneIoSystem(Subnet subnet, int number)`; use `IoSystem?` for both the selected local and local match.

- [ ] Read the exact file and all `NetworkDeviceConfigurator`/`InvokeFirstAvailable` callers before editing. Confirm the current private helper has only the two known callers and that no test pins its source text.
- [ ] Replace the two reflection invocations with `node.ConnectToSubnet(subnet)` and `ioConnector.ConnectToIoSystem(ioSystem)`. Apply the typed private/local changes above, delete `InvokeFirstAvailable`, its speculative comments, the two wrapped-exception catches, both obsolete direct `InvalidOperationException` catches, and unused `using System.Reflection`.
- [ ] Review each wrapper side by side with the base: success writes the same sparse key/value; engineering failure writes the same skipped key/message; subnet failure returns `false`; prior scalar settings remain in the result; no IO attempt follows a failed requested subnet. Leave all key literals for E-5.
- [ ] Acquire the global slot; build against refreshed stubs and run existing focused checks:

```powershell
dotnet build TiaMcpServer.slnx -c Release -m:1 --no-restore /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests -c Release --no-build --filter "FullyQualifiedName~NetworkDeviceCreatorWorkerContractTests|FullyQualifiedName~NetworkMutationVerificationTests|FullyQualifiedName~ReferenceStubArtifactTests"
```

Expected: exit `0`; source-contract dependency-before-setter checks and `Preflight_ExistingIoSkipPrecedence` pass. These tests do not execute the changed native connection wrappers.

- [ ] Compile against installed V21 references:

```powershell
dotnet build TiaMcpServer.slnx -c Release -m:1 --no-restore /p:UseTiaPortalReferenceStubs=false '/p:TiaPortalV21Dir=C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48'
```

Expected: exit `0`; record actual warnings and assembly provenance. A missing SDK is a blocked real-reference check, not a pass. Do not launch the host or attach to TIA.

- [ ] Review the final source diff, selector ordering, retained verification dispatch, and removed catch behavior; run `git diff --check`. Commit only the configurator as `refactor: use typed V21 network connection calls`. Release the global slot.

### Task 3: Qualify the branch and hand it to integration

**Files:** No additional production files; coordinator owns durable status/evidence updates.

**Interfaces:** Produces the exact tested candidate SHA, check results, known limits, and B integration handoff; C consumes the integrated B result.

- [ ] Acquire the global slot; build the final candidate with stubs once, then run the ordinary suite with the existing CI coverage scope and the required packaging suite:

```powershell
$taskE4Results = Join-Path (Get-Location).Path ('TestResults/e4/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff'))
dotnet build TiaMcpServer.slnx -c Release -m:1 --no-restore /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests -c Release --no-build --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory $taskE4Results --filter "Category!=Packaging"
dotnet test TiaMcpServer.Tests -c Release --no-build --filter "Category=Packaging"
```

Require exit `0` for each command before proceeding. If Coverlet temp-file access fails, set process-local `TEMP`/`TMP` under an ignored `TestResults/e4/temp` directory, rerun only the failed collection into a fresh timestamped results directory, and restore the original process values afterward. Reject zero-hit or stale reports.

- [ ] Enforce the current coverage gate:

```powershell
$taskE4Reports = @(Get-ChildItem -LiteralPath $taskE4Results -Recurse -Filter coverage.cobertura.xml)
if ($taskE4Reports.Count -ne 1) { throw 'Expected exactly one current E-4 coverage report.' }
pwsh -NoProfile -File scripts/verify-coverage-threshold.ps1 -CoveragePath ($taskE4Reports[0].FullName) -MinimumLineRate 0.80
```

Record numerator/denominator and exit status. `coverage.runsettings` excludes worker code; do not claim this rate covers the changed native helpers.
- [ ] Obtain independent review of the owned diff, especially native exception propagation, aggregate stub provenance, and partial outcomes. Fix only accepted findings, rerun affected checks, and commit each fix conventionally after its verification.
- [ ] Record `git rev-parse HEAD`, `git status --short`, `git diff --check`, actual test counts, warning/SDK provenance, and the deferred live cases. Release the global slot and report the candidate to the coordinator.
- [ ] Coordinator integrates in default A/B/C order, with C rebasing after B and only then substituting configurator constants. Revalidate the combined candidate with the same serialized policy; rerun only checks affected by integration changes or unresolved evidence gaps. Do not push, publish, or archive the worktree without separate authorization.

### Task 4: Deferred native acceptance after explicit target authorization

**Files:** Coordinator-selected existing acceptance/evidence location; no plant identity or real project names in repository text.

**Interfaces:** Consumes an explicitly authorized disposable project copy, exact selectors, operations, access mode, restoration scope, and candidate SHA; produces bounded native acceptance and restoration evidence.

- [ ] Obtain later explicit permission for the exact disposable target and `configure_network_device` cases. Rebuild the accepted SHA against installed V21 references after any stub build; verify host/worker binaries and target identity before attachment.
- [ ] Capture original node subnet, IO attachment, scalar settings, exact selectors, and fixture identity. Approve a restoration route before mutation; use existing networks/IO systems where possible.
- [ ] Attach a selected node to the authorized subnet and its available IO connector to an authorized compatible IO system; inspect both attachments afterward using the original selectors and record typed applied settings plus verification.
- [ ] Exercise a specifically authorized native engineering-rejection case with documented unmet attachment prerequisites; verify the matching sparse skipped key/message, preserved earlier applied settings, and unchanged rejected attachment. If a safe rejection cannot be reproduced, record it as unexecuted.
- [ ] If safely reproducible within the authorized fixture, reject requested subnet attachment and confirm IO attachment is suppressed. Otherwise retain the offline skip-precedence evidence and state the native gap.
- [ ] Check an authorized invalid/ambiguous dependency denial before mutation and compare the post-read with the captured original state. Do not inject undocumented native faults to manufacture `InvalidOperationException`; its native path remains unverified unless separately authorized and reproducible.
- [ ] Restore and verify the captured original attachment/scalar state, or dispose of the authorized fixture copy as explicitly agreed. Record exact candidate SHA, assembly identities, target evidence, executed/unexecuted cases, and restoration proof. No download or PLC state changes.

## Completion boundary

Offline completion requires the exact signature tests, fresh stub provenance, both reference builds, ordinary tests, required packaging tests, scoped coverage gate, reviewed diff, and clean committed implementation candidate. It does not imply native attachment or exception acceptance: the current test project does not source-link this configurator, and its selector doubles use different namespaces from the actual SDK receivers. Do not reshape that unrelated test surface just to execute these two direct calls.

Native completion requires only the later authorized cases actually executed, with post-read and restoration evidence. Keep any native rejection, exception, or fixture limitations explicit. This draft remains uncommitted until the user approves otherwise.
