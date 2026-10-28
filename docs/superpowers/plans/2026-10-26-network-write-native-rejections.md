# network_write Native Rejections and Shared Trusted Verification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let TIA Portal be the only checker of `network_write`: one hardware read per call, no replan or post-write reads, every Portal rejection typed as `engineering_rejected` with its verbatim `engineeringError`, and a trusted/superseded verifier shared by `plc_write` and `network_write` in `Safety/Pipeline`.

**Architecture:** The guarded single-call pipeline is unchanged. A new `TrustedWriteVerifier<TItem>` plus a per-domain `IWriteVerificationPolicy<TItem>` replace `PlcWriteVerifier`, `NetworkWriteVerifier` and the worker's `NetworkMutationVerifier`. The Network planner shrinks to one `read_hardware_config` and selector resolution; `configure_network_device` stops at the first Siemens rejection and carries the applied subset on the failure; `WorkerExecution` keeps `portal_lost` precedence for wrapped Siemens errors. `network_write` moves to write contract `2.0`.

**Tech Stack:** C#/.NET 10 host, netstandard2.0 contracts, net48 worker, xUnit, FakeWorker (`TiaMcpServer.FakeWorker`), Windows/PowerShell for live steps. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-10-26-network-write-native-rejections-design.md` (approved 2026-10-26). Section numbers below (§6.1 …) refer to it.

**Analysis baseline:** `main` at `8090ded`; branch `refactor/network-write-native-rejections`. Line numbers are navigation aids; revalidate before editing.

## Global Constraints

- Preserve the 8/19/19 tool surface, the `network_write(operations, dryRun=false)` arguments, the binding lease, access-mode gates, stop-at-first-failure, no rollback, audit v2, and one `CanonicalJson.Serialize` call for text and `structuredContent`.
- `ProjectCapabilityCatalog.OperationFor` and `OperationPolicyCatalog` are unchanged; no worker method is added or removed.
- `plc_write` document members and check names (`trusted`, `superseded`, `contentHash`, member `contentHash`) are byte-for-byte unchanged; its `output-schemas.json` row is unchanged. Message text may change only where this plan says so (it does not).
- `network_read` stays `contractVersion:"1.0"`; its probes and schema row are unchanged.
- New conditional JSON members are registered in `ConditionalMemberRegisterTests`; removed ones are unregistered. Worker payloads write explicit nulls and decode through `CanonicalJson.DeserializeWorkerPayload`/`NormalizeWorkerPayload`. No `[LegacyNullOmission]` change.
- `TiaMcpServer.Tests.csproj` links host and worker sources by file: add every new `Network/`, `Plc/` and worker file it must compile; remove rows for deleted files. `Safety/Pipeline/*.cs` is a glob.
- Build with `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true`; run `dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"` at the end of every task. No product `.csproj` changes, so packaging tests do not run.
- Commit after each task with a short one-line message; push only to `refactor/network-write-native-rejections`.
- Task 0 runs before any code change and Task 7 repeats it unchanged on the candidate. Task 8 is written from Task 7 evidence only.
- No server-side check that re-implements a Portal rule is added in any task. An O3 live finding (§6.8) is improvement-log evidence, not code, unless the maintainer decides otherwise in Task 7.

## Review Focus

1. `configure_network_device` requesting `ioSystem` on an interface without an IO connector: fail `target_not_found` before the first setter, nothing applied, later items skipped. (Task 2 source contract test; Task 3 FakeWorker scenario `network-guarded-no-io-connector`.)
2. A rejected configure whose carried outcome lists an applied key the request never asked for: item `protocol_error`, payload never echoed. (Task 3 `RejectedConfigureOutcome_ContradictingRequest_IsProtocolError`.)
3. Two configure items on one node whose device names differ only in case: the first is `superseded`, the second `trusted`, keys compare case-insensitively. (Task 3 `SameNodeDifferentCasing_FirstIsSupersededSecondTrusted`.)
4. `dryRun:true`: phase `preview`, current values from the snapshot, zero mutation methods and zero `inspect_network_object` calls reach the worker. (Task 4 `DryRun_ReadsHardwareOnceAndInspectsNothing`.)
5. Portal disposed during a configure after `Address` applied: `portal_lost`, no `engineeringError`, the applied subset still on the response. (Task 2 `WrappedEngineeringFailure_AfterPortalDisposal_MapsToPortalLostAndKeepsOutcome`.)

---

## File structure

| Path | Responsibility after this plan |
| --- | --- |
| `TiaMcpServer/Safety/Pipeline/WriteVerification.cs` (new) | Shared verification records, check names, `IWriteVerificationPolicy<TItem>` |
| `TiaMcpServer/Safety/Pipeline/TrustedWriteVerifier.cs` (new) | The one trusted/superseded/read-back algorithm |
| `TiaMcpServer/Plc/PlcVerificationPolicy.cs` (new) | PLC object keys, content read-back, PLC composition helper |
| `TiaMcpServer/Plc/PlcWriteModels.cs` | gains `PlcOperationVerification`, `PlcWriteVerification` (+`From`) |
| `TiaMcpServer/Plc/PlcWriteVerifier.cs` | deleted |
| `TiaMcpServer/Network/NetworkVerificationPolicy.cs` (new) | Network object keys; no read-back |
| `TiaMcpServer/Network/NetworkWriteVerifier.cs` | deleted |
| `TiaMcpServer/Network/NetworkGuardedWriteModels.cs` | effect and response records for 2.0; verification is the shared record |
| `TiaMcpServer/Network/NetworkWriteDomain.cs` | per-call state: prepared targets, effects, attempted ids |
| `TiaMcpServer/Network/NetworkWritePlanner.cs` | one hardware read, selector resolution, delete consequences |
| `TiaMcpServer/Network/NetworkWritePayloadBudget.cs` | effects, verification entries and batch omission only |
| `TiaMcpServer/Network/NetworkPayloadContract.cs` | strict decode of 2.0 results, failed configure with outcome |
| `TiaMcpServer/Network/NetworkContractVersion.cs` | gains `NetworkWriteContractVersion` |
| `TiaMcpServer.OpennessWorker/Worker/WorkerExecution.cs` | `Classify`, `FailureMessage`, wrapped-error disposal re-check |
| `TiaMcpServer.OpennessWorker/Worker/WorkerOperationException.cs` | `ConfigureNetworkDeviceOutcome` |
| `TiaMcpServer.OpennessWorker/Openness/Network/NetworkDeviceConfigurator.cs` | stop at first rejection, outcome on the failure |
| `TiaMcpServer.OpennessWorker/Openness/Network/SubnetLifecycleService.cs` | one resolve per transaction, no counts, no capture |
| `TiaMcpServer.OpennessWorker/Openness/Network/NetworkMutationVerifier.cs` | deleted |
| `TiaMcpServer.Contracts/Network/*ResultInfo.cs` | 2.0 members (§6.5) |
| `TiaMcpServer.Contracts/Network/NetworkMutationVerificationInfo.cs`, `NetworkVerificationStatuses.cs` | deleted |
| `TiaMcpServer.Contracts/Worker/WorkerResponse.cs`, `TiaMcpServer/Worker/WorkerCallResult.cs` | `ConfigureNetworkDeviceOutcome` |

---

### Task 0: Baseline call timing (maintainer's machine, before any code change)

**Files:**
- Create: `docs/superpowers/acceptance/reports/inputs/<date>-network-write-timing-before.md`

Protocol is §6.7 of the spec. Mirror the header, "Build under test", "Project size", "Protocol" and "Median table" sections of `docs/superpowers/acceptance/reports/inputs/2026-10-25-plc-write-timing-before.md`, with the raw audit records appended as in that file.

- [x] **Step 1: Install the baseline build** from `main` at `8090ded` with `scripts/install-local-tool.ps1`; record the installed version string.
- [x] **Step 2: Record project size** from `network_read read_hardware_config`: device count, node count, subnet count, and the `read_hardware_config` document length in characters.
- [x] **Step 3: Run the protocol**: `full` mode, bound disposable copy; per actual run: close without saving, reopen, one untimed `read_hardware_config`, then the case; cases N1–N5 in order; one discarded warm-up run, then three measured runs; N4 needs no reopen.
- [x] **Step 4: Write the input document** with the median table (root `durationMs`, item sum, remainder per case) and the raw records (fields as the PLC input keeps them).
- [x] **Step 5: Commit**

```bash
git add docs/superpowers/acceptance/reports/inputs/
git commit -m "docs: network write task 0 baseline timing"
```

---

### Task 1: Shared trusted verifier, PLC migrated onto it

**Files:**
- Create: `TiaMcpServer/Safety/Pipeline/WriteVerification.cs`, `TiaMcpServer/Safety/Pipeline/TrustedWriteVerifier.cs`
- Create: `TiaMcpServer/Plc/PlcVerificationPolicy.cs`
- Modify: `TiaMcpServer/Plc/PlcWriteModels.cs` (add the two PLC verification records), `TiaMcpServer/Plc/PlcWriteDomain.cs:74-76`
- Delete: `TiaMcpServer/Plc/PlcWriteVerifier.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj:205` (replace the `PlcWriteVerifier.cs` row with `PlcVerificationPolicy.cs`)
- Test: `TiaMcpServer.Tests/Safety/Pipeline/TrustedWriteVerifierTests.cs` (new), `TiaMcpServer.Tests/Plc/PlcWriteVerifierTests.cs:36` (one-line change)

**Interfaces:**
- Produces, in `TiaMcpServer.Safety.Pipeline`:

```csharp
public static class WriteVerificationChecks { public const string Trusted = "trusted"; public const string Superseded = "superseded"; }

public sealed record WriteOperationVerification(string OperationId, bool Success, string Check,
    string? Expected, string? Actual, string? Message);

public sealed record WriteVerification(bool Success, IReadOnlyList<WriteOperationVerification> Operations,
    StructuredOperationOmission? Omission);

public interface IWriteVerificationPolicy<TItem> where TItem : IOperationBatchItem
{
    /// <summary>Public read tool named in the "may have changed" message, e.g. "plc_read".</summary>
    string ReadTool { get; }
    /// <summary>Normalized keys the item owns (asLater:false) or offers to later items (asLater:true); "\n"-joined segments, container prefixes member.</summary>
    IReadOnlyList<string> ObjectKeys(TItem item, bool asLater);
    bool HasReadBack(TItem item);
    Task<WriteOperationVerification> ReadBackAsync(TItem item, StructuredOperationFailure? failure);
}

public sealed class TrustedWriteVerifier<TItem>(IReadOnlyList<TItem> items, IWriteVerificationPolicy<TItem> policy)
    where TItem : IOperationBatchItem
{
    public Task<WriteVerification> VerifyAsync(StructuredOperationBatch batch);
}
```

- Produces, in `TiaMcpServer.Plc`:

```csharp
public sealed record PlcOperationVerification(string OperationId, bool Success, string Check,
    string? Expected, string? Actual, string? ContentHash, string? Message);   // moved verbatim from PlcWriteVerifier.cs
public sealed record PlcWriteVerification(bool Success, IReadOnlyList<PlcOperationVerification> Operations, StructuredOperationOmission? Omission)
{
    /// <summary>ContentHash = Actual for "contentHash" entries, null otherwise.</summary>
    public static PlcWriteVerification From(WriteVerification shared);
}
public sealed class PlcVerificationPolicy(OpennessWorkerClient client, IReadOnlyDictionary<string, PlcWriteEffect> effects)
    : IWriteVerificationPolicy<PlcOperationRequest>
{
    public static Task<PlcWriteVerification> VerifyAsync(OpennessWorkerClient client, IReadOnlyList<PlcOperationRequest> items,
        IReadOnlyDictionary<string, PlcWriteEffect> effects, StructuredOperationBatch batch);   // the composition both the domain and the tests call
}
```

Algorithm of `TrustedWriteVerifier.VerifyAsync`, lifted from today's `PlcWriteVerifier.VerifyAsync`/`SupersededBy` (`TiaMcpServer/Plc/PlcWriteVerifier.cs:31-69`) with these exact texts:

- attempted = items whose batch status is `succeeded` or `failed`, in item order.
- for each attempted item: if `Failure is null` and a later attempted item supersedes it → `Superseded`; else if `policy.HasReadBack(item)` → `await policy.ReadBackAsync(item, failure)`; else if `Failure is null` → `Trusted`; else no entry.
- "later supersedes": later item's failure is null or not `WorkerFailureCategories.IsPreconditionRefusal`, and any `ObjectKeys(later, asLater:true)` key `k` satisfies `own == k || own.StartsWith(k + "\n", Ordinal)` for some `own` in `ObjectKeys(item, asLater:false)`. Take the last such later item.
- Trusted message: `Operation '{id}': the Openness call completed without rejection; nothing was read back.`
- Superseded message: `Operation '{id}': superseded by later operation '{later}' on the same object; ` followed by `the later operation failed and may have changed the object; its final state is not verified. Inspect it with {policy.ReadTool} before retrying.` when the later item failed and `!policy.HasReadBack(later)`, otherwise `the final state is verified there.`
- `Success` = all entries' `Success`; `Omission` null.

- [x] **Step 1: Write the failing shared-verifier tests** in `TiaMcpServer.Tests/Safety/Pipeline/TrustedWriteVerifierTests.cs`, using `FakeWriteItem` from `PipelineFakes.cs` and a test-local `FakePolicy : IWriteVerificationPolicy<FakeWriteItem>` with `Dictionary<string, string[]> Keys`, `HashSet<string> ReadBack`, `List<string> ReadBackCalls`, `ReadTool => "fake_read"`:

```csharp
[Fact] public async Task SucceededItemsOnDistinctObjects_AreTrusted()
// two succeeded items, keys a / b → two entries, both Check == "trusted", Success true,
// Message == "Operation 'a': the Openness call completed without rejection; nothing was read back."
[Fact] public async Task LaterItemOnSameObject_SupersedesEarlier()
// keys x / x → first Check "superseded", Message ends "the final state is verified there.", second "trusted"
[Fact] public async Task ContainerKey_SupersedesMemberKeys()
// own "x\ny\nz", later offers "x\ny" → superseded; later offers "x\nyz" → not superseded
[Fact] public async Task LaterPreconditionRefusal_DoesNotSupersede()
// later failed target_not_found → first trusted; later has no entry
[Fact] public async Task LaterEngineeringRejected_SupersedesWithUncertaintyMessage()
// later failed engineering_rejected, same key → first superseded, Message contains
// "may have changed the object" and "Inspect it with fake_read before retrying."; later has no entry
[Fact] public async Task ReadBackItems_AreReadBackEvenWhenFailed()
// ReadBack contains the id; batch status failed → ReadBackAsync called once with that failure; entry is what the policy returned
[Fact] public async Task SupersededWins_OverReadBackForASucceededItem()
// item with read-back superseded by later same key → "superseded", ReadBackAsync not called for it
[Fact] public async Task SkippedAndFailedItemsWithoutReadBack_HaveNoEntry()
[Fact] public async Task Success_IsTheConjunctionOfEntries()
// a read-back entry with Success false → verification.Success false
```

- [x] **Step 2: Run them to verify they fail**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~TrustedWriteVerifierTests"`
Expected: build error, `TrustedWriteVerifier` not defined.

- [x] **Step 3: Implement `WriteVerification.cs` and `TrustedWriteVerifier.cs`** with the signatures and texts above.

- [x] **Step 4: Run the shared tests**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~TrustedWriteVerifierTests"`
Expected: 9 passed.

- [x] **Step 5: Move the PLC records** `PlcOperationVerification` and `PlcWriteVerification` from `PlcWriteVerifier.cs:14-18` to `PlcWriteModels.cs` unchanged, add `PlcWriteVerification.From`.

- [x] **Step 6: Create `PlcVerificationPolicy`**: `ReadTool => "plc_read"`; `ObjectKeys` is today's `Keys`/`BlockKeys` (`PlcWriteVerifier.cs:72-104`) verbatim; `HasReadBack(item) => item.Operation is "update_block_logic" or "update_type_content"`; `ReadBackAsync` is today's `VerifyContentAsync` (`:106-121`) returning `new WriteOperationVerification(id, true, "contentHash", null, hash, message)` on success and `new(id, false, "contentHash", null, null, message)` on a failed re-export; static `VerifyAsync` composes `TrustedWriteVerifier` and `From`.

- [x] **Step 7: Switch `PlcWriteDomain.VerifyAsync`** to `PlcVerificationPolicy.VerifyAsync(client, _items, _effects, attemptedBatch)`; delete `PlcWriteVerifier.cs`; in `PlcWriteVerifierTests.cs:36` replace `new PlcWriteVerifier(fixture.Client, items, effects).VerifyAsync(Succeeded(items))` with `PlcVerificationPolicy.VerifyAsync(fixture.Client, items, effects, Succeeded(items))`; update the csproj row.

- [x] **Step 8: Prove PLC is unchanged**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Plc|FullyQualifiedName~McpToolSchemaTests|FullyQualifiedName~ToolOutputContractConformanceTests"`
Expected: all pass with no edit to any PLC test assertion or to `output-schemas.json`.

- [x] **Step 9: Full default suite, then commit**

Run: `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true && dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"`
Expected: 0 errors; all pass.

```bash
git add TiaMcpServer/Safety/Pipeline TiaMcpServer/Plc TiaMcpServer.Tests
git commit -m "refactor: network write task 1 shared trusted verifier"
```

---

### Task 2: Worker rejection surfacing, outcome carrier and `portal_lost` precedence

**Files:**
- Modify: `TiaMcpServer.OpennessWorker/Worker/WorkerExecution.cs:15-40`
- Modify: `TiaMcpServer.OpennessWorker/Worker/WorkerOperationException.cs` (new optional member)
- Modify: `TiaMcpServer.Contracts/Worker/WorkerResponse.cs:30-32` (new conditional member), `TiaMcpServer/Worker/WorkerCallResult.cs:46-50`
- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs:2182, 2202, 2227, 2240, 2296, 2314` (copy the member in every `with` block that copies `HmiWriteOutcome`)
- Modify: `TiaMcpServer.OpennessWorker/Program.cs:229-232, 1543-1560` (forward the member through `Failure`)
- Modify: `TiaMcpServer.OpennessWorker/Openness/Network/NetworkDeviceConfigurator.cs:44-120, 148-186, 234-250`
- Modify: `TiaMcpServer.OpennessWorker/Openness/Network/NetworkPostconditionChecks.cs:16-19` (remove `IoSystemSkipReason`)
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj:13` (also link `..\TiaMcpServer.OpennessWorker\Worker\WorkerOperationException.cs`)
- Test: `TiaMcpServer.Tests/Worker/WorkerExecutionTests.cs`, `TiaMcpServer.Tests/Network/NetworkDeviceCreatorWorkerContractTests.cs:9-19`, `TiaMcpServer.Tests/Network/NetworkMutationVerificationTests.cs:46-55` (delete `Preflight_ExistingIoSkipPrecedence`), `TiaMcpServer.Tests/Json/ConditionalMemberRegisterTests.cs:43`

**Interfaces:**
- Produces, in `WorkerExecution`:

```csharp
internal static string Classify(EngineeringException exception, bool mutates);       // engineering_rejected only for EngineeringTargetInvocationException && mutates
internal static string FailureMessage(EngineeringException exception, string category); // "TIA Portal rejected the operation: {msg}" | "TIA Portal operation failed: {msg}"
```
  `Execute` keeps its signature; its `EngineeringException` catch uses both helpers; it gains `catch (WorkerOperationException ex) when (ex.EngineeringError is not null)`: disposal revision moved → return `PortalLost(ex)` with `ConfigureNetworkDeviceOutcome = ex.ConfigureNetworkDeviceOutcome`; otherwise `throw;`.
- Produces: `WorkerOperationException(..., ConfigureNetworkDeviceResultInfo? configureNetworkDeviceOutcome = null)` and property `ConfigureNetworkDeviceOutcome`; `WorkerResponse.ConfigureNetworkDeviceOutcome` (`[JsonIgnore(WhenWritingNull)]`); `WorkerCallResult.ConfigureNetworkDeviceOutcome { get; init; }`.
- Produces, in `NetworkDeviceConfigurator`: the first `EngineeringException` from a setter or connect is rethrown as `new WorkerOperationException(WorkerExecution.Classify(ex, true), WorkerExecution.FailureMessage(ex, category), engineeringError: WorkerExecution.Describe(ex), configureNetworkDeviceOutcome: result)`; a missing IO connector throws `WorkerOperationException(TargetNotFound, "Device '{deviceName}': the selected network interface exposes no IO connector, so ioSystem cannot be attached. No configuration was attempted.")` before the first setter. `SkippedSettings` is no longer written anywhere.

- [x] **Step 1: Write the failing `WorkerExecutionTests`** (append to the existing class):

```csharp
[Theory]
[InlineData(true, true, WorkerFailureCategories.EngineeringRejected)]
[InlineData(true, false, WorkerFailureCategories.WorkerOperationFailed)]
[InlineData(false, true, WorkerFailureCategories.WorkerOperationFailed)]
public void Classify_UsesExceptionTypeAndMutationFlag(bool targetInvocation, bool mutates, string expected)
// exception = targetInvocation ? new EngineeringTargetInvocationException("m") : new EngineeringException("m")

[Fact] public void WrappedEngineeringFailure_AfterPortalDisposal_MapsToPortalLostAndKeepsOutcome()
// body: revision++; throw new WorkerOperationException(EngineeringRejected, "x",
//   engineeringError: new EngineeringErrorInfo { Type = "EngineeringTargetInvocationException", Message = "gone" },
//   configureNetworkDeviceOutcome: new ConfigureNetworkDeviceResultInfo { DeviceName = "PLC_1", AppliedSettings = { ["Address"] = "10.0.0.1" } });
// Assert: FailureCategory PortalLost; EngineeringError null; ConfigureNetworkDeviceOutcome.AppliedSettings["Address"] == "10.0.0.1"

[Fact] public void WrappedEngineeringFailure_WithoutDisposal_IsRethrown()
// same exception, revision constant → Assert.Throws<WorkerOperationException>; the thrown instance is the same object

[Fact] public void WrappedFailureWithoutEngineeringError_IsNeverIntercepted()
// WorkerOperationException(ValidationError, "v") with revision++ → still thrown (not portal_lost)
```

- [x] **Step 2: Run them to verify they fail**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~WorkerExecutionTests"`
Expected: build errors (`Classify`, `ConfigureNetworkDeviceOutcome` missing).

- [x] **Step 3: Implement the contract members** (`WorkerOperationException`, `WorkerResponse`, `WorkerCallResult`, client `with` blocks, `Program.Failure` and the catch at `Program.cs:229`), link `WorkerOperationException.cs` into the test project, register `"WorkerResponse.ConfigureNetworkDeviceOutcome"` in `ConditionalMemberRegisterTests` with the comment `// network_write: present on a rejected configure_network_device item; carries the settings applied before the rejection.`

- [x] **Step 4: Implement `Classify`, `FailureMessage` and the new catch** in `WorkerExecution.Execute`; the existing `EngineeringException` catch must produce exactly the strings it produces today (the existing tests pin them).

- [x] **Step 5: Run `WorkerExecutionTests` and `ConditionalMemberRegisterTests`**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~WorkerExecutionTests|FullyQualifiedName~ConditionalMemberRegisterTests"`
Expected: all pass, including the pre-existing `MutationTargetInvocationFailure_AfterPortalDisposal_StillMapsToPortalLost`.

- [x] **Step 6: Rewrite the configurator source contract tests** in `NetworkDeviceCreatorWorkerContractTests`:
  - `Preflight_AllDependencySelectorsPrecedeFirstScalarSetter`: replace `Assert.Contains("NetworkPostconditionChecks.IoSystemSkipReason(", source)` with `Assert.Contains("WorkerFailureCategories.TargetNotFound", preflight)` and `Assert.DoesNotContain("SkippedSettings", source)`.
  - add `Rejection_StopsAtFirstSiemensExceptionAndCarriesAppliedSubset`: `Assert.Contains("WorkerExecution.Classify(", source)`, `Assert.Contains("configureNetworkDeviceOutcome: result", source)`, `Assert.DoesNotContain("IoSystemSkipReason", source)`.
  - delete `NetworkMutationVerificationTests.Preflight_ExistingIoSkipPrecedence`.

- [x] **Step 7: Run them to verify they fail**, then **rewrite `NetworkDeviceConfigurator`** per the Interfaces block: `ApplyNodeAttribute(Node, string, object, string, ConfigureNetworkDeviceResultInfo)`, `ConnectSubnet` (now `void`) and `ConnectIoSystem` rethrow; the pre-flight block throws `target_not_found` when `ioConnector is null`; remove `subnetConnected` and the skip branch; remove `IoSystemSkipReason` from `NetworkPostconditionChecks`. Keep setter order and the "No network settings were provided." message.

- [x] **Step 8: Build with stubs and run the full default suite**

Run: `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true && dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"`
Expected: 0 errors; all pass (the host still treats a success payload with an empty `skippedSettings` as success, so FakeWorker-driven tests are unaffected).

- [x] **Step 9: Commit**

```bash
git add TiaMcpServer.OpennessWorker TiaMcpServer.Contracts TiaMcpServer/Worker TiaMcpServer.Tests
git commit -m "refactor: network write task 2 typed configure rejections"
```

---

### Task 3: Host Network domain onto the shared verifier (contract 2.0)

**Files:**
- Create: `TiaMcpServer/Network/NetworkVerificationPolicy.cs` (+csproj row)
- Delete: `TiaMcpServer/Network/NetworkWriteVerifier.cs` (−csproj row `TiaMcpServer.Tests.csproj:219`)
- Modify: `TiaMcpServer/Network/NetworkContractVersion.cs`, `TiaMcpServer/Network/NetworkGuardedWriteModels.cs:17-43`, `TiaMcpServer/Network/NetworkWriteDomain.cs`, `TiaMcpServer/Network/NetworkWritePayloadBudget.cs`, `TiaMcpServer/Network/NetworkPayloadContract.cs:27-100, 158-163, 174-260, 262-298`
- Modify: `TiaMcpServer.FakeWorker/Program.cs:3723-3757` (guarded configure handler) and the scenario switch at `:1452-1470`
- Modify: `TiaMcpServer.Tests/Tools/Snapshots/output-schemas.json` (`network_write` key only), `TiaMcpServer.Tests/Tools/ToolOutputContractConformanceTests.cs:176-190`
- Test: `TiaMcpServer.Tests/Network/NetworkMutationVerificationTests.cs`, `NetworkGuardedWriteOrderingTests.cs`, `NetworkGuardedWriteBudgetTests.cs`, `NetworkGuardedWriteDomainTests.cs`, `NetworkGuardedWriteMcpTests.cs`

**Interfaces:**
- Consumes: Task 1's `TrustedWriteVerifier<TItem>`, `IWriteVerificationPolicy<TItem>`, `WriteVerification`; Task 2's `WorkerCallResult.ConfigureNetworkDeviceOutcome`.
- Produces:

```csharp
public static class NetworkWriteContractVersion { public const string Current = "2.0"; }   // NetworkContractVersion.Current stays "1.0"

public sealed class NetworkVerificationPolicy(IReadOnlyDictionary<string, NetworkWriteEffect> effects)
    : IWriteVerificationPolicy<NetworkOperationRequest>
// ReadTool "network_read"; HasReadBack false; ReadBackAsync throws NotSupportedException.
// ObjectKeys(item, _) from effects[item.OperationId].Target:
//   add_network_device        → [$"device\n{DeviceName.ToUpperInvariant()}"]
//   configure_network_device  → [$"device\n{DeviceName.ToUpperInvariant()}\nnode\n{NodeId}"]
//   update_subnet, delete_subnet → [$"subnet\n{SubnetId}"]
//   create_subnet             → []

public sealed record NetworkGuardedWriteResponse(string Tool, string ContractVersion, string Phase, bool Success,
    WriteToolError? Error, IReadOnlyList<string> Warnings, IReadOnlyList<WriteGuardReport> Guards,
    IReadOnlyList<NetworkWriteEffectPresentation> Effects, StructuredOperationBatch? Batch, WriteVerification? Verification,
    StructuredOperationOmission? Omission = null);
// NetworkOperationVerification, NetworkWriteVerification and NetworkFinalCheck are deleted.

public sealed class NetworkWriteDomain(OpennessWorkerClient client)
    : IWriteDomain<NetworkOperationRequest, NetworkWriteEffect, WriteVerification, NetworkGuardedWriteResponse>
// state: _prepared (unchanged), _effects (replaces _initial/_current), _attempted, _items; _immediate is gone.
// ContractVersion => NetworkWriteContractVersion.Current
// Project(item, result) => NetworkPayloadContract.Project(_prepared[...] ?? item, result)
// VerifyAsync(batch) => new TrustedWriteVerifier<NetworkOperationRequest>(_items, new NetworkVerificationPolicy(_effects)).VerifyAsync(attempted-only batch)
// VerificationSucceeded(v) => v?.Success == true
// ReplanAsync: still delegates to the planner this task; on success replaces _effects[id] and re-measures MeasurePreparedCore over current effects (Task 4 removes it)

// NetworkPayloadContract
public static StructuredOperationItem Project(NetworkOperationRequest operation, WorkerCallResult workerResult);   // the requireVerification overloads are removed
```

Payload rules (§6.1, §6.5): a failed `configure_network_device` result whose `WorkerCallResult.ConfigureNetworkDeviceOutcome` is set is projected as `status: failed`, `failure` from the worker category/message (the pipeline adds `engineeringError`), and `result` = the outcome serialized with `WorkerJson.SerializePayload` and decoded through the same `Decode<ConfigureNetworkDeviceResultInfo>` path as a success payload; a `JsonException` there makes the item `protocol_error` with the message `The worker outcome for 'configure_network_device' did not match its declared result contract and was rejected.` and no echo. `ValidateAddedDevice` only requires non-blank `deviceName`, `rootItemName`, `typeIdentifier` (no request equality). `ValidateConfiguration` requires `deviceName == target.deviceName` and every `appliedSettings` key to be requested with the requested value, no duplicates; nothing about skipped or unaccounted keys. `ValidateSubnetMutation` keeps `ValidateSubnetLifecycleResult` and the `subnetId == target.subnetId` rule for update/delete; count and verification checks go. A success payload is always `succeeded`.

Budget: follow `PlcWritePayloadBudget` (`TiaMcpServer/Plc/PlcWritePayloadBudget.cs`): item roots are whole effects and the verification `Operations` list (omitted as a whole, `Omission` recorded on the verification); batch results go through `Budget.ApplyBatch`; `Diagnostics` compacts error, warnings, guards, verification entry messages and batch results via `CompactWriteDiagnostics`. `MeasureProtectedCore` reserves `"2.0"` and `new WriteVerification(false, items.Select(i => new WriteOperationVerification(i.OperationId, false, "trusted", null, null, DiagnosticSummary)).ToArray(), core.Omission)`. `MeasurePreparedCore(items, plans)` = protected core + Σ `Size(effect)`, `int.MaxValue` if any effect exceeds `MaxItemChars`.

FakeWorker: add scenario `network-guarded-rejected-setting` (same state as `network-guarded`; a configure with `pnDeviceName` responds `{"success":false,"failureCategory":"engineering_rejected","error":"TIA Portal rejected the operation: scripted","engineeringError":{"type":"EngineeringTargetInvocationException","message":"scripted","inner":[]},"configureNetworkDeviceOutcome":{"deviceName":"PLC_Grouped","appliedSettings":{"Address":"<requested>"},"skippedSettings":{},"messages":[]}}` after applying the address to its state), `network-guarded-rejected-contradiction` (same, but `appliedSettings` contains `"SubnetMask":"255.0.0.0"` that was never requested) and `network-guarded-no-io-connector` (an `ioSystemNumber` request responds `{"success":false,"failureCategory":"target_not_found","error":"…exposes no IO connector…"}`). The existing guarded configure handler stops writing `skipped`.

- [x] **Step 1: Write the failing host tests.** In `NetworkMutationVerificationTests.cs` delete every test about `ValidateVerification`, identities, counts and `Complete/Compare` precedence (`Complete_PreservesStatusPrecedence`, `QualifiedImmediateIdentity_*`, `AppliedSubsetOnly_IsVerified`, `UnreadableAppliedValue_IsUnverified`, `WrongIoSystemOnOtherSubnet_Fails`, `IoSystemVerification_RequiresScalarSubnetAndNumberChecks`, `MissingOrContradictoryVerification_IsProtocolError`, `UnknownOrUnaccountedSetting_IsProtocolError`, `PostconditionFailure_FailsItemAndRetainsResult`, `PresentEvidence_IsStrictOnBothOverloads`, `NotRequired_IsOnlyValidForNoAppliedConfiguration`, `Deletion_PreservesAffectedNodesAcrossScopes`, `UninspectableSubnetId_IsNotAbsence`, `MalformedEvidence_IsRejectedWithoutEcho`, `AddedDevice_RequiresExactReturnedIdentitiesAndType`, `Subnet_RequiresRequestedAttributesAndImmediateCount`); keep the selection/classification tests. Add, against `NetworkPayloadContract.Project` with hand-built `WorkerCallResult`s:

```csharp
[Fact] public void RejectedConfigure_ProjectsOutcomeAsResultAndTypedFailure()
// Fail(engineering_rejected) with ConfigureNetworkDeviceOutcome { DeviceName "PLC_1", AppliedSettings { Address } } for a request changing Address+PnDeviceName
// → Status failed; Failure.Category engineering_rejected; Result.appliedSettings.Address == requested; no "verification" property
[Fact] public void RejectedConfigureOutcome_ContradictingRequest_IsProtocolError()
// outcome applied SubnetMask never requested → Failure.Category protocol_error; Result null; Failure.Message does not contain "255.0.0.0"
[Fact] public void RejectedConfigure_WithoutOutcome_HasNullResult()
[Fact] public void AddedDevice_ReportedIdentityIsAcceptedAsIs()
// payload deviceName "PLC_1_1" for request "PLC_1" → Status succeeded, Result.deviceName "PLC_1_1"
[Fact] public void AddedDevice_BlankIdentity_IsProtocolError()
[Fact] public void SubnetResult_RequiresOnlyIdentityAndName()
// payload {"subnetId":"s1","name":"PN/IE"} for update_subnet target s1 → succeeded; target s2 → protocol_error
[Fact] public void ConfigureSuccess_WithPartialAppliedSubset_IsSucceeded()
// a worker success payload listing fewer applied keys than requested is succeeded (nothing is skipped any more)
```

  In `NetworkGuardedWriteOrderingTests.cs` delete `RepeatedSettings_PreserveImmediateChecks`, `PostReadFailure_IsUnverifiedAndNeverReplayed`, `AddedDevice_VerifiesTopLevelItemDespiteSameNamedChild`, `DeletedSubnet_AggregatePassCannotHideLostUngroupedNode`, `SubnetOnlyMove_DoesNotInferSupersessionOfEarlierExplicitIoTuple`, `ProfibusAttributes_UseTypedInspectionAndEnumSymbol`, `DifferentDeviceCasing_DeleteSupersedesConnectedRelationships`, `UnknownDelete_StillInspectsExactPlannedNodes`; rewrite:

```csharp
[Fact] public async Task SameNodeDifferentCasing_FirstIsSupersededSecondTrusted()      // was DifferentDeviceCasing_RepeatedSettingsSupersede…
// two configures on PLC_Grouped/node-2, second with "plc_grouped" → verification.operations: [superseded, trusted]; success true
[Fact] public async Task SubnetUpdateThenDelete_UpdateIsSuperseded()                    // was SubnetUpdateThenDelete_SupersedesExistenceAndName
[Fact] public async Task RejectedConfiguration_StopsCallWithTypedFailureAndAppliedSubset() // was PartialConfiguration_…; scenario network-guarded-rejected-setting
// items: configure Address+PnDeviceName, then Delete() → batch: [failed engineering_rejected with result.appliedSettings.Address and failure.engineeringError.type, skipped];
// root success false, error null, phase applied; verification.operations empty, verification.success true; one audit line
[Fact] public async Task UnknownAttempt_FailsItemSkipsRestAndHasNoVerificationEntry()  // was UnknownAttempt_RetainsUnverifiedRecord…
[Fact] public async Task IoSystemAttach_RequestedSettingsAreScalarSubnetAndNumber()    // was IoSystemAttach_ReportsScalar…; asserts effect.requestedSettings only
[Fact] public async Task MissingIoConnector_FailsTargetNotFoundWithoutMutation()        // scenario network-guarded-no-io-connector; later item skipped
```

  Any other ordering or budget test that still references `FinalChecks`, `Evidence`, `NetworkFinalCheck` or
  `NetworkMutationVerificationInfo` and is listed for rewrite or deletion in Task 4 (`LateBlock_PreservesEarlierChanges`,
  `ProducerScopedLateBlock_RetainsEarlierMutationAndOneAudit`, `EarlierAdd_RefreshesRootCount`,
  `ReplannedRootCount_DoesNotEraseEarlierPreservationExpectation`, `ConnectThenDelete_ReplansLateInfo`,
  `ReplanningOccursBetweenDispatches`, the three `*Preview_Reports*` tests, `PnDeviceName*`) is reduced to compile in this
  task: only the assertions on those members are removed, nothing else changes, and Task 4 replaces each one.

  In `NetworkGuardedWriteBudgetTests.cs` delete `EqualSizedEvidenceYieldsInRequestOrderBeforeEffects`, `Supplemental_SmallDiagnosticsTogetherOverflowItem_KeepExactCore`, `Supplemental_WorkerGeneratedIdentityOverflow_RetainsKnownQualifiedRecoveryAndExecutionStatus`, `KnownPriorValues_FinalItemOverflow_RefusesBeforeMutation`; rewrite `WholeEnvelope_IncludesEffectsGuardsAndVerification` for the shared verification shape, `LatePartialFailure_PreservesExactRecoveryCore` for a rejected configure with outcome, and `ProtectedCore_MeasuresEveryEncodedIdentityCopy_AndBoundsAdversarialFallback` for `"2.0"` and one `trusted` entry per item; keep the rest.

  In `NetworkGuardedWriteMcpTests.cs` add `RegisteredSchema_NetworkWriteIsContractTwo` asserting `contractVersion == "2.0"` on a preview and that the structured `verification` has exactly the members `omission`, `operations`, `success`.

- [x] **Step 2: Run them to verify they fail**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~TiaMcpServer.Tests.Network"`
Expected: build errors against the old types.

- [x] **Step 3: Implement models, policy, domain, payload contract and budget** per the Interfaces block; delete `NetworkWriteVerifier.cs`; update the csproj rows.

- [x] **Step 4: Update the FakeWorker** scenarios per the block above.

- [x] **Step 5: Regenerate the `network_write` schema row.** Run `McpToolSchemaTests.OutputSchemasMatchMain` (expected: fails naming only `network_write`); start the production surface in Full mode through `McpProtocolTestHarness`, dump each tool's `ProtocolTool.OutputSchema`, and replace only the `network_write` value in `TiaMcpServer.Tests/Tools/Snapshots/output-schemas.json`. `git diff --stat` on the snapshot must show only that key's lines; the `plc_write` and `network_read` rows are unchanged. Update the two `network_write` probes in `ToolOutputContractConformanceTests` for `"2.0"`.

- [x] **Step 6: Run the Network, Tools and pipeline suites**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~TiaMcpServer.Tests.Network|FullyQualifiedName~TiaMcpServer.Tests.Tools|FullyQualifiedName~TiaMcpServer.Tests.Safety"`
Expected: all pass.

- [x] **Step 7: Build with stubs, full default suite, commit**

Run: `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true && dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"`
Expected: 0 errors; all pass.

```bash
git add TiaMcpServer/Network TiaMcpServer.FakeWorker TiaMcpServer.Tests
git commit -m "refactor: network write task 3 trusted verification"
```

---

### Task 4: Shrink planning to one hardware read

**Files:**
- Modify: `TiaMcpServer/Network/NetworkWritePlanner.cs` (whole file), `TiaMcpServer/Network/NetworkWriteDomain.cs` (`ReplanAsync`, state), `TiaMcpServer/Network/NetworkGuardedWriteModels.cs:14-16` (`NetworkWriteEffect`), `TiaMcpServer/Network/NetworkIdentityResolver.cs:107-180` (`ResolveExistingSubnet`)
- Modify: `TiaMcpServer.FakeWorker/Program.cs:1475` (`network-guarded-pn-autogeneration` now dispatches the configure instead of being read-only evidence)
- Test: `NetworkGuardedWriteOrderingTests.cs`, `NetworkGuardedWriteDomainTests.cs`, `NetworkIdentityResolverTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed record NetworkWriteEffect(string Operation, NetworkWriteTargetEvidence Target,
    IReadOnlyDictionary<string, string> RequestedSettings, IReadOnlyDictionary<string, string?> CurrentSettings,
    IReadOnlyList<NetworkNodeIdentityInfo> AffectedNodes, bool? ConnectionsComplete);   // RootDeviceCount removed; CurrentSettings are plain values with explicit nulls

public sealed class NetworkWritePlanner(OpennessWorkerClient client)
{
    public Task<WritePlan<NetworkWriteEffect>> PlanAsync(IReadOnlyList<NetworkOperationRequest> items);  // one ReadCurrentStateAsync; every plan has DependsOn null
    public static Task<NetworkStateSnapshot> ReadCurrentStateAsync(OpennessWorkerClient client);          // unchanged
    // ReplanAsync, ReadAttributesAsync, NodeTarget, Attribute, PnDeviceNameNotSettable are removed
}
```
  `ResolveAsync` (now synchronous `Resolve`) per §6.3: `CurrentSettings[key] = NodeValue(node, key)` for configure (`PnDeviceNameAutoGeneration` → null); for `update_subnet`/`delete_subnet` `Name` → `subnet.Name`, `HighestAddress`/`TransmissionSpeed` → null; `AffectedNodes` and `ConnectionsComplete` computed only for `delete_subnet` (otherwise empty list and `true`); preconditions `networkDiscoveryComplete` always, `networkConsequencesReadable` for `delete_subnet` only.
  `NetworkWriteDomain.ReplanAsync` → `throw new InvalidOperationException("network_write plans declare no dependencies; ReplanAsync must never be called.")`. `EvaluateGuards`: `Unverifiable` only when `effect.Operation == "delete_subnet" && effect.ConnectionsComplete != true`; `ConnectedDelete` unchanged.
  `NetworkIdentityResolver.ResolveExistingSubnet(operation, state)` loses `validateChanges` and the Ethernet `HighestAddress`/`TransmissionSpeed` refusal; the unsupported-network-type refusal stays.

- [x] **Step 1: Write the failing tests.** In `NetworkGuardedWriteOrderingTests.cs` delete `EarlierAdd_RefreshesRootCount`, `ReplannedRootCount_DoesNotEraseEarlierPreservationExpectation`, `ConfigurePreview_ReportsUnknownAccessWhenNodeInspectIsUnavailable`, `SubnetRenamePreview_ReportsInspectedAccess`; rewrite:

```csharp
[Fact] public async Task ThreeItemCall_ReadsHardwareOnceAndInspectsNothing()           // was ReplanningOccursBetweenDispatches
// FakeWorkerRequestLog: Methods().Count(m => m == "read_hardware_config") == 1; no "inspect_network_object"; three mutation methods in order
[Fact] public async Task DryRun_ReadsHardwareOnceAndInspectsNothing()
// same with dryRun true: one read, zero mutation methods, phase preview, effects[0].effect.currentSettings.Address == snapshot value
[Fact] public async Task ConnectThenDelete_InfoGuardListsPlanningSnapshotNodesOnly()   // was ConnectThenDelete_ReplansLateInfo
// documents the §6.2 limitation: the connected-delete guard count equals the pre-call snapshot count
[Fact] public async Task ConfigurePreview_ReportsSnapshotCurrentValues()              // was ConfigurePreview_ReportsInspectedNodeAccess
// currentSettings: Address == node.IpAddress; PnDeviceNameAutoGeneration present with null
[Theory] [InlineData(true)] [InlineData(false)]
public async Task PnDeviceNameWithAutoGenerationOn_IsDispatchedToPortal(bool dryRun)  // was PnDeviceNameWithoutWritableName_FailsAtPlanTimeWithoutMutation
// no host validation_error; dryRun false → "configure_network_device" reaches the worker
[Theory] [InlineData("network-guarded-late-block")] [InlineData("network-guarded-late-node-block")]
public async Task NoLateGuardEvaluation_LaterItemsProceedOnPlanningEvidence(string scenario)   // replaces LateBlock_PreservesEarlierChanges and ProducerScopedLateBlock_RetainsEarlierMutationAndOneAudit
// items: Configure("first","10.0.0.1"), then Delete() (late-block) or Configure("connect", connect:true) (late-node-block), then Configure("last","10.0.0.3")
// → every item succeeded, no network_state_unverifiable guard, phase applied, success true, three trusted/superseded entries, one audit line
```

  In `NetworkGuardedWriteDomainTests.cs` reduce `RelationshipCases` of `ProducerScopedDegradation_PreservesGuardAndIndependentDiscoveryRefusal` to the `subnet` and `selector` kinds, and add `IncompleteNodeConnections_DoNotBlockConfigure` (scenario `network-guarded-incomplete-node`, `Configure("connect", connect: true)` → no `network_state_unverifiable` guard, phase applied, `configure_network_device` reached the worker) and `MissingRootDeviceCount_DoesNotBlockSubnetWrites` (scenario `network-guarded-incomplete-root`, `Delete()` → no `network_state_unverifiable` guard; the delete is dispatched). In `NetworkIdentityResolverTests.cs` replace the Ethernet-applicability refusal test with `UpdateSubnet_ProfibusAttributesOnEthernet_AreResolvedNotRefused` (resolution succeeds; Portal decides).

- [x] **Step 2: Run them to verify they fail**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~NetworkGuardedWriteOrderingTests|FullyQualifiedName~NetworkGuardedWriteDomainTests|FullyQualifiedName~NetworkIdentityResolverTests"`
Expected: the new tests fail (two hardware reads observed, inspection observed, host `validation_error`).

- [x] **Step 3: Rewrite the planner, effect record, domain replan and resolver** per the Interfaces block. `MeasurePreparedCore` and the policy from Task 3 need no change. In `TiaMcpServer/Network/NetworkWriteTools.cs:14` append one sentence to the `network_write` description: `A PROFINET device name is only settable once changes.pnDeviceNameAutoGeneration is false; pass both in the same item.` (the hint the retired host pre-check used to carry).

- [x] **Step 4: Run the three suites, then the full default suite**

Run: `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true && dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"`
Expected: 0 errors; all pass.

- [x] **Step 5: Commit**

```bash
git add TiaMcpServer/Network TiaMcpServer.FakeWorker TiaMcpServer.Tests
git commit -m "refactor: network write task 4 plan from one hardware read"
```

---

### Task 5: Worker and contract removals

**Files:**
- Delete: `TiaMcpServer.OpennessWorker/Openness/Network/NetworkMutationVerifier.cs`, `TiaMcpServer.Contracts/Network/NetworkMutationVerificationInfo.cs`, `TiaMcpServer.Contracts/Network/NetworkVerificationStatuses.cs`
- Modify: `TiaMcpServer.Contracts/Network/AddDeviceResultInfo.cs`, `ConfigureNetworkDeviceResultInfo.cs`, `SubnetLifecycleResultInfo.cs` (§6.5 members)
- Modify: `TiaMcpServer.OpennessWorker/Openness/Network/SubnetLifecycleService.cs:21-140`, `NetworkPostconditionChecks.cs` (remove `Compare`, `Complete`), `TiaMcpServer.OpennessWorker/Program.cs:642, 679`
- Modify: `TiaMcpServer.FakeWorker/Program.cs` (remove `FakePassedVerification`, `FakeConfigurationVerification`, `FakeSubnetVerification`, every `Verification =`, `SkippedSettings =`, `NetworkDeviceCount*` initializer; scenario `network-config-partial` at `:796` becomes a rejected-setting failure like Task 3's; the `"skippedSettings":{}` member leaves Task 3's scripted outcome JSON)
- Test: `TiaMcpServer.Tests/Json/ConditionalMemberRegisterTests.cs:20-23` (remove the three `.Verification` rows), `TiaMcpServer.Tests/Network/NetworkVocabularyContractTests.cs:15-18`, `NetworkMutationVerificationTests.cs` (remaining `Complete`/`Compare` tests), `NetworkDeviceCreatorWorkerContractTests.cs:28-29`, `NetworkSubnetLifecycleWorkerServiceContractTests.cs`, `NetworkOperationFakeWorkerTests.cs` (`network-config-partial` expectations)

**Interfaces:**
- Produces, contracts:

```csharp
public class AddDeviceResultInfo { string DeviceName; string RootItemName; string TypeIdentifier; List<string> Warnings; }
public class ConfigureNetworkDeviceResultInfo { string DeviceName; Dictionary<string,string> AppliedSettings; List<string> Messages; }
public sealed class SubnetLifecycleResultInfo { string SubnetId; string Name; }
```
- Produces, worker: `SubnetLifecycleService.Update` opens the transaction, resolves the subnet once (`ResolveExactSubnetOrThrow`), sets `Name`, applies PROFIBUS attributes, reads the name back for the result (`ReadRequiredSubnetNameOrThrow`), commits; no type/applicability check, no device count. `Delete` resolves once, reads the name, deletes, commits; no `CaptureAffectedNodes`. `Create` loses the device count. `Program.AddNetworkDevice`/`ConfigureNetworkDevice` no longer assign `result.Verification`.

- [x] **Step 1: Write the failing tests.** `NetworkSubnetLifecycleWorkerServiceContractTests` (source-text, same style as `NetworkDeviceCreatorWorkerContractTests`): `Update_ResolvesTheSubnetOnceInsideItsTransaction` (exactly one `ResolveExactSubnetOrThrow(project, subnetId, "update_subnet")` occurrence, after `exclusiveAccess.Transaction(project, "update_subnet")`), `Lifecycle_HasNoDeviceCountOrAffectedNodeCapture` (`DoesNotContain("project.Devices.Count")`, `DoesNotContain("CaptureAffectedNodes")`, `DoesNotContain("NetworkMutationVerifier")`), `Update_DoesNotRejectProfibusAttributesByType` (`DoesNotContain("ProfibusTypeIdentifier, StringComparison.Ordinal))")` in `Update`). Remove the `NetworkMutationVerifier` assertion from `NetworkDeviceCreatorWorkerContractTests.PnDeviceNameAutoGeneration_IsSetBeforePnDeviceName`. Remove the three `.Verification` rows from `ConditionalMemberRegisterTests` and the `NetworkVerificationStatuses` block from `NetworkVocabularyContractTests`. Delete the remaining `Complete`/`Compare` tests in `NetworkMutationVerificationTests`. Update the `network-config-partial` expectation in `NetworkOperationFakeWorkerTests` to a failed item with `engineering_rejected` and `result.appliedSettings`.

- [x] **Step 2: Run them to verify they fail**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~NetworkSubnetLifecycleWorkerServiceContractTests|FullyQualifiedName~ConditionalMemberRegisterTests|FullyQualifiedName~NetworkVocabularyContractTests"`
Expected: fail on the current sources/registers.

- [x] **Step 3: Apply the deletions and rewrites** per the Files and Interfaces blocks. `grep -rn "NetworkMutationVerificationInfo\|NetworkVerificationStatuses\|SkippedSettings\|NetworkDeviceCount" --include=*.cs TiaMcpServer*` must return nothing.

- [x] **Step 4: Build with stubs, full default suite**

Run: `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true && dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"`
Expected: 0 errors; all pass; `WorkerPayloadNullPolicyRegisterTests` unchanged and green.

- [x] **Step 5: Commit**

```bash
git add TiaMcpServer.OpennessWorker TiaMcpServer.Contracts TiaMcpServer.FakeWorker TiaMcpServer.Tests
git commit -m "refactor: network write task 5 retire worker verification"
```

---

### Task 6: Offline qualification

**Files:**
- Modify: this plan's "Execution record" section.

- [x] **Step 1: Stub build clean**: `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true` → 0 errors; record the warning count against `main` (`git stash`-free: build `8090ded` in a worktree if needed).
- [x] **Step 2: Default suite**: `dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"` → record passed/failed/skipped.
- [x] **Step 3: Adversarial diff review** of `git diff 8090ded..HEAD -- TiaMcpServer TiaMcpServer.OpennessWorker TiaMcpServer.Contracts`, confirming each line of §2 anti-goals: no Siemens call before the binding gate; a Siemens exception during `read_hardware_config` still maps through read categories (never `engineering_rejected`); `portal_lost` precedence for both the shared catch and the wrapped path; `plc_write`/`network_read` schema rows unchanged (`git diff 8090ded..HEAD -- TiaMcpServer.Tests/Tools/Snapshots/output-schemas.json` touches only the `network_write` key); no new conditional member outside the register; no `[LegacyNullOmission]` change.
- [x] **Step 4: Line counts**: `git diff --numstat 8090ded..HEAD` per production file into the execution record, removed/added.
- [x] **Step 5: Commit**

```bash
git add docs/superpowers/plans/2026-10-26-network-write-native-rejections.md
git commit -m "docs: network write task 6 offline qualification"
```

---

### Task 7: Live verification, timing after, fixtures (maintainer's machine, TIA Portal V21)

**Files:**
- Create: `docs/superpowers/acceptance/reports/inputs/<date>-network-write-native-rejections-live-cases.md`, `inputs/<date>-network-write-timing-after.md`
- Modify: `TiaMcpServer.FakeWorker/Program.cs` (replay the captured configure rejection verbatim in `network-guarded-rejected-setting`), `TiaMcpServer.Tests/Network/NetworkGuardedWriteMcpTests.cs`

Protocol is §6.8: `full` mode, disposable copy, each case followed by the `network_read` landed check, `compile_check` for accepted collisions, then close-without-save and reopen. Classify every case O1/O2/O3.

- [x] **Step 1: Install the Task 5 candidate** with `scripts/install-local-tool.ps1`; record the version string and the Portal UI language.
- [x] **Step 2: Run L1–L14** and save every response document, landed check and compile diagnostic in the live-cases input, one section per case with its O-class. Observations are captured; L11's expected rejected-PN fixture was not established (see the resumed execution record).
- [x] **Step 3: Decide on O3 findings.** For each O3 case with a silently dropped write, record the maintainer's ruling (no change, or a `postcondition_failed` read-back scoped to that write, as PLC R14). Any code change here gets its own commit and offline tests before Step 5. Serial maintainer review is complete; L3 is the sole MCP fix, committed as `75c0568` with the offline evidence below.
- [ ] **Step 4: Re-run Task 0 unchanged** (same project copy, same mode, same reopen and warm-up rule, three runs, median) on the installed candidate; write the timing-after input next to the baseline.
- [ ] **Step 5: Pin the captured documents offline.** Replace the scripted text in the FakeWorker's `network-guarded-rejected-setting` with the L11 `engineeringError.message` verbatim (constant `LiveConfigureRejectionMessage`), and add to `NetworkGuardedWriteMcpTests`: `LiveL11_RejectedPnDeviceName_StopsItemWithAppliedSubset` and `LiveL13_SecondItemRejected_FirstAppliedSecondTypedThirdSkipped`, modelled on `PlcGuardedWriteMcpTests.LiveL1_…`/`LiveL10_…` (`:160-215`), asserting `failure.category`, `failure.message == "TIA Portal rejected the operation: " + message`, `engineeringError` members `inner`/`message`/`type`, `result.appliedSettings`, root `success:false`/`error:null`/`isError:false`, verification entries for the earlier items only, one audit line.
- [ ] **Step 6: Full default suite, commit**

```bash
git add docs/superpowers/acceptance/reports/inputs TiaMcpServer.FakeWorker TiaMcpServer.Tests
git commit -m "test: network write task 7 live fixtures"
```

---

### Task 8: Documentation (last, from Task 7 evidence)

**Files:**
- Modify: `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md:420-560` (the `network_write` envelope section), `docs/SupportedOperations/NETWORK_PHASE4_SUBNET_LIFECYCLE.md`
- Modify: `AGENTS.md` (Network writes bullet; the "Network read/write roots declare `contractVersion:"1.0"`" paragraph; the write-safety line naming categories)
- Modify: `docs/ARCHITECTURE.md:490-` (§7a failure categories), `:812-830` (the `NetworkWriteVerifier` paragraph becomes the shared `TrustedWriteVerifier` paragraph), the pipeline section
- Modify: `docs/IMPROVEMENT_LOG.md:241-262` (open follow-ups: drop the Network item, add "HMI and VCI onto the shared verifier"), append a completed entry
- Modify: `docs/README.md` (rows for this design, this plan, the acceptance report; the "Latest process entry" paragraph), `docs/roadmap/network-operations.md`, `docs/roadmap/json-contract.md:42`
- Create: `docs/superpowers/acceptance/reports/<date>-network-write-native-rejections-live-verification.md`
- Modify: this plan's status line and execution record; `docs/superpowers/specs/2026-10-26-network-write-native-rejections-design.md` status line.

- [ ] **Step 1: Network reference**: rewrite the envelope section for 2.0 (phases unchanged; guards; stop-at-first inside configure; `appliedSettings` on the failure; `engineering_rejected` with one captured L11 example; `trusted`/`superseded`; the §6.8 three-outcome table filled with what each L case showed; remove `skippedSettings`, `finalChecks`, immediate `identity`/`checks`, `networkDeviceCount`). Update the subnet lifecycle document's result and verification text.
- [ ] **Step 2: AGENTS.md and ARCHITECTURE.md** per the files list; the architecture paragraph names `TrustedWriteVerifier`, `IWriteVerificationPolicy`, the two policies and the HMI/VCI follow-up.
- [ ] **Step 3: Improvement log**: completed entry with the production line counts from Task 6, the before/after timing table (root, item sum, remainder per N case) and retained limits (same-call connect-then-delete guard, O3 findings); update the open follow-ups.
- [ ] **Step 4: Acceptance report and index rows**: report with the L matrix and O-classes, the timing comparison and project size, rulings, retained limits; `docs/README.md` rows; roadmap notes; plan and design status lines.
- [ ] **Step 5: Commit**

```bash
git add docs AGENTS.md
git commit -m "docs: network write task 8 documentation"
```

---

## Execution record

Historical checkpoint; the completion record below supersedes this status.

### Task 0 partial record — 2026-10-27

The maintainer requested stopping after measured round 2 to shut down the machine. No round-3 case or implementation task was started. **Task 0 remains incomplete.**

- Installed `4.2.0-local.68.g012d967` with real Siemens assemblies and successful package verification. Production source at `012d967` is identical to baseline `8090ded`; their differences are the approved design/plan documents. The requested current branch was preserved.
- Captured one discarded N1–N5 warm-up round and two complete measured N1–N5 rounds: 15 successful captures, including ten measured calls. All audit-v2 records matched the returned canonical text, fresh timestamps and expected full-mode item counts/statuses.
- Recorded project size, exact replay payloads, per-run figures and selected raw audit records in the [partial timing input](../acceptance/reports/inputs/2026-10-27-network-write-timing-before.md). Three-run medians are pending.
- N2 uses a fresh untimed setup subnet because its native ID cannot be targeted before creation; the measured create uses a separate fresh subnet. N4 also restores/sets up that fixture. Raised response budgets retain the 964,930-character hardware document. The input records these adaptations for unchanged replay.
- After the final round-2 write, closed without saving and reopened the disposable fixture. Final hardware value exactly matched the initial value; the original auto-generation flag was explicitly read back as `true`, and status was open/unmodified. Timing and verification clients were closed.
- Baseline stub build before timing: seven warnings, zero errors. No production/test source changed; no default-suite rerun was needed for this documentation-only checkpoint.

Remaining: revalidate baseline/fixture provenance and warm-up conditions before completing the third measured round. Finish Task 0 before any implementation. Tasks 1–8 have not started.

### Task 0 complete record — 2026-10-27

Completed the [baseline timing input](../acceptance/reports/inputs/2026-10-27-network-write-timing-before.md) before implementation. The maintainer's overnight stop after measured round 2 was honored; after explicit resumption, the original captures were retained, the installed baseline and exact fixture were revalidated, a fresh discarded N1–N5 warm-up ran, and only missing measured round 3 was collected.

- Installed `4.2.0-local.68.g012d967` with real Siemens assemblies and successful package verification. Production/test source at the installed `012d967` and resumed `4f0aabd` is identical to baseline `8090ded`. Installed host/worker hashes matched the original local publication; the requested branch was preserved.
- All 25 captures succeeded: ten discarded warm-ups and fifteen measured calls. Every audit-v2 record matched full canonical response text/hash, fresh completion time within five seconds, full mode and expected item counts/statuses. Original timestamp strings are retained verbatim in the input.
- The input records project size, exact replay payloads, per-run figures, independently calculated three-run medians and selected raw audits. The machine restart, repeated discarded warm-up and restored client boundary after the successful N4 warm-up preview are explicit evidence limits; no uninterrupted-series claim is made.
- N2 uses a fresh untimed setup subnet because its native ID cannot be targeted before creation; the measured create uses a separate fresh subnet. N4 also restores/sets up that fixture. The same raised response budgets preserve the 964,930-character hardware document. Task 7 must preserve these payload/setup/read/restoration adaptations.
- Final close-without-save/reopen restored the full original hardware value, original auto-generation flag/name and unchanged project file hash. Public status was open/unmodified, and the direct client exited after verification. Fresh local artifacts are `resume2-final-*` and `resume2-restoration.json` under ignored `artifacts/network-native`; `resume-final-*` preserve the earlier restored client boundary.
- Baseline stub build before timing: seven warnings, zero errors. No production/test source changed; validation used capture/audit/payload/table/raw-record/restoration assertions, Markdown JSON/link/redaction checks and Git whitespace checks. No default-suite rerun was needed for this documentation-only completion.

Task 0 is complete. Candidate offline qualification and live acceptance remain Tasks 6–7.

Tasks 6 and 7 will later add candidate build warnings, test counts, per-file removed/added line counts, O-class per live case and maintainer rulings.

### Tasks 1–6 complete: final offline qualification — 2026-10-28

Tasks **0–6 are complete and offline qualified** on `refactor/network-write-native-rejections`. Implementation candidate `a353ab818b43facf726900f557ef0a02bc5eabe8`, tree `4224fee9b0a6b77ea29a9dba6c3bce75be0a748b`, includes reviewed Task 1 `1e543b0`, Task 2 `5e21891`, Task 3 `8456ae6`, Task 4 `1c5d7f8` and Task 5 `a353ab8`. **Task 7 live qualification/after timing and Task 8 manual documentation are NOT STARTED.** The candidate is **not installed**; the global tool remains the recorded baseline `4.2.0-local.68.g012d967`. No native call, installation, push or PR was performed during Task 6. Task 0's verified fixture restoration and closed timing-client boundary remain the last native evidence.

Fresh qualification used native elevated execution with `login:false`, serialized builds/tests and unchanged source. Logs, TRX and Cobertura are retained under ignored `TestResults/task6-a353ab8-20261028/`:

- Debug: `dotnet build TiaMcpServer.slnx -m:1 --no-incremental /p:UseTiaPortalReferenceStubs=true` — exit 0, seven warnings, zero errors, 54.20 seconds (`build-debug.log`).
- Release: `dotnet build TiaMcpServer.slnx -m:1 --configuration Release --no-incremental /p:UseTiaPortalReferenceStubs=true` — exit 0, seven warnings, zero errors, 22.17 seconds (`build-release.log`). The seven warning identities/messages match `artifacts/network-native/baseline-build.log` exactly, ignoring line offsets: two Network selector nullable warnings and five HMI test nullable warnings; no new warning.
- Default suite and CI coverage in one run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build --configuration Release --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory TestResults/task6-a353ab8-20261028/coverage --filter "Category!=Packaging" --logger "trx;LogFileName=task6-default.trx"` — exit 0, **7,656 passed / 0 failed / 0 skipped**, 8m53s (`coverage-full.log`, `coverage/task6-default.trx`). Only this process's `TEMP` and `TMP` pointed to the task-local ignored `temp/` directory. No global configuration changed and no duplicate plain full suite ran.
- Attached report `coverage/208a89dc-00e1-429b-a0c8-c6d17f35db27/coverage.cobertura.xml`: **95.11% scoped line coverage**, 12,530 covered / 13,174 valid lines. Both generated Cobertura copies have SHA256 `EB790B6253CF9BD33B564D219C1624C113B08D6FD133A8E80BD2FC703F2C12ED`; the attached copy was selected after hash comparison, with no rerun. `scripts/verify-coverage-threshold.ps1 -CoveragePath <attached-report> -MinimumLineRate 0.80` passed (`coverage-threshold.log`). Scope follows CI runsettings: host/contracts, including linked host code; native worker, FakeWorker and test namespaces are excluded. This is not native worker coverage.
- The independent broad adversarial review of `8090ded` through the exact candidate tree passed specification and quality with no actionable findings. Task 6 independently checked HEAD/tree equality and derived production numstats. Raw Git-blob root slices prove that only `network_write` changes in `output-schemas.json`; all 18 other roots, including `plc_write` and `network_read`, are byte-for-byte unchanged. No `[LegacyNullOmission]` change exists; the new conditional outcome is registered and retired conditionals removed. Source review preserves the binding gate, read exceptions never becoming `engineering_rejected`, direct/wrapped `portal_lost` precedence, catalogs/method set and 8/19/19 surface, lease/access/audit and canonical rendering. These are source/offline findings, not new live discovery.

Implementation rulings retained for maintainer review:

- Configure supersedence uses device plus interface name/position ownership and raw node ID, correcting the literal raw-E1 key collision observed in the real baseline fixture. A raw-key substitution failed the regression and the qualified key passed; optional type evidence is excluded from identity. No native read or guard was added.
- The ordinary 512-character diagnostic cap remains. The runtime schema correction permits nullable string `currentSettings` only on `network_write`; an actual-null preview regression and all 18 unchanged roots were verified. Runtime tool help was updated with the behavior; Task 8 manual documentation is deferred.
- Minimal-subnet required-member success tests moved to Task 5 when the DTO/producer fields were retired. Necessary direct callers, raw fixtures, FakeWorker scenarios, constructor/reflection callers and source-contract tests migrated with the removed implementations, preserving applicable selector/read/state assertions. Reflection required the extra explicit null constructor argument.
- An initial unchanged three-second PowerShell read-generation timeout and a separate lifecycle MCP bootstrap transient were retained in earlier logs; each passed its unchanged targeted retry and subsequent unchanged full suite. No harness, package, protocol or timeout workaround was made. Task 6 required no functional retry.

Production line counts below are freshly derived from `git diff --numstat 8090ded..a353ab8 -- TiaMcpServer TiaMcpServer.OpennessWorker TiaMcpServer.Contracts`. FakeWorker and tests are excluded: **423 added / 1,387 removed, net −964 lines**.

| Production path | Removed | Added |
| --- | ---: | ---: |
| `TiaMcpServer.Contracts/Network/AddDeviceResultInfo.cs` | 3 | 0 |
| `TiaMcpServer.Contracts/Network/ConfigureNetworkDeviceResultInfo.cs` | 5 | 0 |
| `TiaMcpServer.Contracts/Network/NetworkMutationVerificationInfo.cs` | 33 | 0 |
| `TiaMcpServer.Contracts/Network/NetworkVerificationStatuses.cs` | 9 | 0 |
| `TiaMcpServer.Contracts/Network/SubnetLifecycleResultInfo.cs` | 5 | 0 |
| `TiaMcpServer.Contracts/Worker/WorkerResponse.cs` | 0 | 5 |
| `TiaMcpServer.OpennessWorker/Openness/Network/NetworkDeviceConfigurator.cs` | 42 | 25 |
| `TiaMcpServer.OpennessWorker/Openness/Network/NetworkMutationVerifier.cs` | 194 | 0 |
| `TiaMcpServer.OpennessWorker/Openness/Network/NetworkPostconditionChecks.cs` | 28 | 1 |
| `TiaMcpServer.OpennessWorker/Openness/Network/SubnetLifecycleService.cs` | 74 | 7 |
| `TiaMcpServer.OpennessWorker/Program.cs` | 6 | 5 |
| `TiaMcpServer.OpennessWorker/Worker/WorkerExecution.cs` | 4 | 21 |
| `TiaMcpServer.OpennessWorker/Worker/WorkerOperationException.cs` | 1 | 6 |
| `TiaMcpServer/Network/NetworkContractVersion.cs` | 0 | 6 |
| `TiaMcpServer/Network/NetworkGuardedWriteModels.cs` | 27 | 3 |
| `TiaMcpServer/Network/NetworkIdentityResolver.cs` | 15 | 3 |
| `TiaMcpServer/Network/NetworkPayloadContract.cs` | 182 | 34 |
| `TiaMcpServer/Network/NetworkVerificationPolicy.cs` | 0 | 34 |
| `TiaMcpServer/Network/NetworkWriteDomain.cs` | 47 | 15 |
| `TiaMcpServer/Network/NetworkWritePayloadBudget.cs` | 223 | 52 |
| `TiaMcpServer/Network/NetworkWritePlanner.cs` | 75 | 13 |
| `TiaMcpServer/Network/NetworkWriteTools.cs` | 1 | 15 |
| `TiaMcpServer/Network/NetworkWriteVerifier.cs` | 286 | 0 |
| `TiaMcpServer/Plc/PlcVerificationPolicy.cs` | 0 | 72 |
| `TiaMcpServer/Plc/PlcWriteDomain.cs` | 1 | 1 |
| `TiaMcpServer/Plc/PlcWriteModels.cs` | 0 | 18 |
| `TiaMcpServer/Plc/PlcWriteVerifier.cs` | 125 | 0 |
| `TiaMcpServer/Safety/Pipeline/TrustedWriteVerifier.cs` | 0 | 52 |
| `TiaMcpServer/Safety/Pipeline/WriteVerification.cs` | 0 | 25 |
| `TiaMcpServer/Tools/McpToolRegistration.cs` | 1 | 1 |
| `TiaMcpServer/Worker/OpennessWorkerClient.cs` | 0 | 6 |
| `TiaMcpServer/Worker/WorkerCallResult.cs` | 0 | 3 |

Retained limits: same-call connect-then-delete information uses the initial planning snapshot; newly connected identities may be absent from that informational guard. Partial outcome retention is established for the explicit wrapped engineering/disposal case; direct fatal errors after a partial mutation remain unverified. Trusted records native completion without read-back and does not establish that Portal accepted every requested value. The Task 0 timing medians span a machine restart and repeated warm-up; no candidate after-timing or performance claim exists yet. Packaging tests were not run because product project/packaging inputs did not change. Broader native rejection, silent-drop, transaction, concurrency and hardware acceptance remain Task 7 evidence.

Only this existing plan was updated for Task 6; its existing index entry remains sufficient. Git whitespace/scope checks and the prescribed plan-only commit complete the task. Stop after Task 6; Tasks 7–8 remain pending.

### Task 7 partial execution — stopped by maintainer, 2026-10-28

The maintainer authorized live testing after reinstalling the branch. Package `4.2.0-local.76.gf1ae806` matched source `f1ae806` and the local Release publish host/worker hashes. Portal UI language English was confirmed by the maintainer. Initial saved-file hash, full hardware equality and clean project status matched Task 0.

[Partial live-cases input](../acceptance/reports/inputs/2026-10-28-network-write-native-rejections-live-cases.md) preserves the captured canonical documents. L1/L2 rejected duplicate creates natively. L3 returned success/trusted while the colliding subnet rename was silently ignored; the mutation result and fresh read both retained the original name. L4 accepted and landed a duplicate IP; software compilation was silent. Public `compile_check` is software-only and provides no hardware-compile acceptance claim. Each L1–L4 case was closed without save, reopened and verified clean.

At the L3 ruling request the maintainer said to stop and return later while monitoring TIA itself. No keep/change ruling was made and no product fix was implemented. L4 was already dispatched while that ruling was pending. L5 had begun when the maintainer closed Portal; it returned `binding_conflict`, so it has no acceptance outcome. Later probes did not run. The client exited; no replay or Portal restart occurred. Post-stop verification found no Portal/owned Task 7 client and the unchanged original AP21 SHA-256 `564a644a3116fe60cba9265e40e5a797fd68b57b0c954dff7e3059c84d702a81`. A final runtime hardware/PN-attribute comparison was not made after Portal closure.

Task 7 remains incomplete: L5–L14, the L13 variant, all candidate timing, live fixtures and the Task 7 default-suite run are pending. No product/FakeWorker/test source changed; no timing was captured, no new build/test suite ran, and nothing was pushed. On resume, revalidate provenance and fixture state, have the maintainer present, and execute one case at a time so a stop takes effect before the next case. Task 8 remains unstarted.

### Task 7 resumed functional observations — 2026-10-28

The maintainer chose to finish the live probes, review uncovered issues together, then consider measurements. After the maintainer reopened the disposable project, fresh preflight verified the unchanged installed package `4.2.0-local.76.gf1ae806`, host/worker equality with the local Release publish, source equivalence with `f1ae806` (current `0fb2296` was documentation only), original AP21 hash, full hardware equality and clean open status. Explicit address/mask/PN name/automatic-name attributes on all four selected targets matched preflight. Each subsequent case ran separately; no future case was queued before the current restoration was checked. Raw requests, canonical responses and exact matching audit-v2 records are under local ignored `artifacts/network-native/task7-20261028-resume1/`; the existing [live-cases input](../acceptance/reports/inputs/2026-10-28-network-write-native-rejections-live-cases.md) retains the observations and relevant canonical documents.

- L5 duplicate PROFINET name was accepted and landed, with software compilation silent. L6 automatic-name-on setter and L7 unsupported Ethernet `HighestAddress` raised `EngineeringNotSupportedException`, correctly mapped to `worker_operation_failed`; L7's name change rolled back. L8 malformed address and L9 incompatible subnet connection raised typed `engineering_rejected`, with their targets unchanged. L10 missing IO connector was a `target_not_found` preflight control; address unchanged, later create skipped.
- L11 attempt A, after a separate automatic-name-off setup on the GSD node, applied the address then failed at the unsupported mask setter; the retained outcome contained Address only, name remained unchanged and the later item was skipped. L11b, after the same setup on the CPU node, accepted and retained Address, Mask and literal `Invalid PN Name!`; the later create also executed, and software compilation had zero errors/warnings. Neither capture is the originally expected rejected-PN/two-key failure. Step 5's named fixture and captured-message assumption require issue review; do not manufacture that native rejection.
- L12 completed all five native operations with four trusted checks and the rename superseded by delete. Its delete info guard used the five initial node identities, as designed; the same-call connected node was not in that initial inventory. The existing connected subnet was used as the connect/rename/delete target, with a separately created subnet for the create item, because a newly assigned subnet ID cannot be targeted at initial planning. Landed reads showed the added device/new subnet and the deleted target's absence. Software compilation of the original connected controller reported three undefined hardware-identifier symbols after deletion; the selected CPU and added CPU compiled without errors. This is an O2 consequence, not compile-clean acceptance.
- L13's five-item variant retained the earlier two creates, rejected the third item's address, skipped the final two items and verified only the successful prefix. The extra three-item variant established first-success/second-rejection/third-skip independently. L14 previewed the exact five-item request without mutation: five effects, `batch:null`, `verification:null`, full hardware equal to original and project unmodified.

No product/FakeWorker/test change, candidate reinstall, build/test suite, timing measurement or remote write was performed in this resumed functional run. Public `compile_check` compiles PLC software; it can expose dependent-symbol consequences but software silence is not hardware compiler acceptance. Task 7 remains incomplete pending joint issue decisions, any authorized fixes/requalification, a truthful fixture plan, measurements and the remaining offline gate. Task 8 remains unstarted. Final cleanup evidence is retained in the live-cases input.

### Task 7 L3 ruling and scoped fix — 2026-10-28

The maintainer requested serial issue review, starting with a single L3 replay and explicitly leaving the fixture as-is for inspection. The replay requested subnet `3547-1` (`PN/IE_2`) be renamed to existing `PN/IE_1`; `network_write` again reported success/trusted while its result contained `name:"PN/IE_2"`. No separate post-write `network_read`, save, compile or restoration ran. The maintainer observed no change in TIA and reported that a manual colliding rename briefly displays a tooltip, cancels the operation and leaves the project unmodified.

The maintainer approved the scoped O3 exception: `SubnetLifecycleService.Update` compares its already-read result name with the requested name using `StringComparison.Ordinal`, before `CommitOnDispose()`. A mismatch throws `postcondition_failed` with requested and actual names and a possible collision explanation; the update is not committed. This adds no native getter, worker call or collision preflight and preserves attribute-only updates. The comparison detects a name mismatch rather than proving its cause.

Offline verification passed: the two ignored-rename cases first failed because no exception was thrown; all 22 focused tests then passed. The changed `Update` method has 25/25 covered lines and 100% reported branch coverage. The serial Release stub solution build passed with seven existing warnings and zero errors; the full non-packaging Release suite passed 7,663 tests with no failures or skips. Host/contracts coverage remains 95.11% (12,530/13,174 lines), above the 80% gate; this aggregate excludes worker code, whose changed method was measured separately. Independent review found no issues. Evidence is under ignored `TestResults/l3-rename-20261028/`.

The corrected public response and native transaction rollback have not been live-tested, and no candidate reinstall or timing is authorized by this ruling. TIA remains as-is for the maintainer. Task 7 and the remaining issue review stay incomplete; Task 8 remains unstarted. The original L3 captures remain unchanged as pre-fix evidence.

### Task 7 serial maintainer review completed — 2026-10-28

The maintainer closed the remaining findings after inspecting TIA and its compilation diagnostics:

- **L3 ignored subnet rename:** the only MCP fix is the scoped name postcondition in `75c0568`, qualified offline as recorded above. At this review checkpoint it had not been reinstalled or live-retested; the subsequent authorized recheck is recorded below.
- **L4/L5 duplicate IP and PROFINET names:** the maintainer confirmed that both writes landed and compilation reports the collisions. No additional MCP handling is needed. For literal `Invalid PN Name!`, the maintainer supplied a screenshot showing the entered name and a converted name and confirmed clean compilation; the input is accepted, so no MCP PN-format check is required.
- **GSD mask rejection and partial address change:** the maintainer confirmed the observed error and closed the point as expected native behavior. The maintainer explained that IO-device masks inherit the controller's mask; this is the explanation for this observed case, not a new general API compatibility claim. The native failure and retained Address outcome need no MCP change.
- **Connected-subnet deletion:** the maintainer confirmed that PLC references to the removed subnet's nodes correctly cause compilation errors and closed the point. No new deletion was sent during this serial review: the interrupted preparation read only local records. The original L12 observations remain the captured native evidence.

The serial review is complete, but Task 7 is not complete. The expected rejected-PN fixture in Step 5 is still invalid and needs a truthful replacement; no replacement has been chosen or fabricated. The corrected L3 response and native rollback, the full live rerun, timing measurements, fixture work and remaining Task 7 offline gate are pending. Task 8 remains unstarted. No new install, live mutation, build/test run or remote write was performed to record these final dispositions; the existing frozen capture report is unchanged.

### Task 7 scoped L3 live recheck after reinstall — 2026-10-28

The maintainer reinstalled the updated build, reopened the disposable project and authorized only L3. Installed package `4.2.0-local.79.g75c0568` matches the local Release publish: host SHA256 `a8be3d6e4f2fc7fec4b280ccf9be8e21539ddae3b31f17afc19575c796dc67a9`, worker SHA256 `6eb1db5801f61e3eb83cdf80b217efaf3a12fdeb042dfd6146036ecf01234d59`. The source is `75c0568`; only this plan had uncommitted documentation changes. The exact disposable project was explicitly bound, open and unmodified; the four subnet identities and names matched the original list.

The single `update_subnet` requested `3547-1` (`PN/IE_2`) become existing `PN/IE_1`. It now fails with `postcondition_failed`, includes both names and states that no transaction was committed. The failed item has `result:null`, no native `engineeringError` and no trusted verification entry; the attempted-failure root is `success:false`, `phase:"applied"`, `error:null`, MCP `isError:false`. A separate post-write subnet read exactly matched all four pre-write items, and a separate status read confirmed the project remained open with `isModified:false`. This is a scoped live PASS for the ignored name-only rename; normal renames and rollback of additional properties were not exercised.

Exact preflight, request, canonical response and post-read/status evidence is retained under ignored `artifacts/network-native/task7-20261028-l3-fixed-live/replay.json`. Independent review confirmed all six canonical text/structured pairs, installed hashes, unchanged subnet items and open/unmodified status. One fresh audit v2/full record exactly matched the delivered response and `sha256:c67da5304afc49e899be7aaeed08c0565b5f1de5475046aef47d6e64c1c4204d`; the derived audit verification is retained beside the capture. No restore, save, compile, additional mutation, full live matrix, timing run or remote write was performed. The original capture report is unchanged. Task 7 remains incomplete pending truthful fixture replacement, the full live rerun, timings and the remaining offline gate; Task 8 remains unstarted.
