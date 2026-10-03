# Network JSON Contract and Guarded Write Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Align the existing Network JSON envelopes and replace Network tokens with verified, prompt-free guarded writes, preserving sparse outcomes and ordered partial-write evidence.

**Architecture:** Keep the existing Network catalog, invoker, typed decoder, and structured batch. A per-call Network domain implements `IWriteDomain` on the shared `WriteExecution` runner and its ordinary verified-binding gate. The net48 worker supplies exact relationship and immediate postcondition evidence; the host owns validation, guards, presentation budgets, final verification, and audit v2.

**Tech Stack:** Windows, TIA Portal V21, .NET 10 host/tests/FakeWorker, net48 Openness worker, netstandard2.0 contracts, ModelContextProtocol 2.2.0, xUnit, PowerShell 7. No new dependencies.

**Spec:** [Approved Network design](../specs/2026-10-03-network-json-guarded-write-design.md). Read it with this plan. Baseline: `main` at `baf811789893f706b2c7d398afc13e9e6c0e2a72`; worktree branch: `feature/network-contract-write-safety`. The user approved this plan and selected subagent-driven execution on 2026-10-03.

## Global Constraints

- One Network PR combines JSON alignment and guarded writes. Multiuser PR 1 remains separate.
- Retain four reads: `read_hardware_config`, `search_equipment_catalog`, `list_network_objects`, `inspect_network_object`; retain five writes: `add_network_device`, `configure_network_device`, `create_subnet`, `update_subnet`, `delete_subnet`.
- Maximum 50 operations, unique operation IDs, caller order, one project per write call, exact selectors, stop on first write failure, no batch rollback or automatic replay.
- Public inputs: `network_read(operations)` and `network_write(operations, dryRun = false)`. Reject `confirm`, `safetyToken`, `acknowledge`, and other unknown root arguments before worker activity.
- Network `ConfirmsEveryCall = false`, with no Network acknowledge guards and zero Network elicitation in read-write, full, or dry runs. Lifecycle retains `ConfirmsEveryCall = true`.
- `network_delete_connected_subnet` is `info`; `network_state_unverifiable` is `block`. Unknown connection inventory is not an empty inventory. Blocks are non-overridable.
- Writes require a verified binding to an already-open project, under one pinned lease through planning, mutation, verification, composition, and audit. Reads never bind or switch. No implicit project opens.
- Both Network envelopes use `contractVersion: "1.0"`, root `warnings` arrays, explicit declared nulls, strict typed worker decoding, and one canonical text/structured document.
- Write phases are `preview`, `applied`, `blocked`, `error`. Pre-apply rejection has top-level error and `isError:true`; attempted mutation failure has `error:null`, `phase:applied`, `isError:false`.
- Sparse configuration keys retain their existing spelling: `Address`, `SubnetMask`, `PnDeviceName`, `Subnet`, `IoSystem`. Unrequested keys are absent. Any requested skip fails the item/call while retaining its typed result; later items use `earlierOperationFailed`.
- Verify applied settings only; missing or unreadable post-read evidence never passes. Preserve immediate checks before intentional later changes, then verify the effective attempted prefix.
- `networkDeviceCountUnchanged` means root `project.Devices.Count`; exact affected-device/node preservation requires evidence from grouped/ungrouped scopes too.
- One audit v2 record per entered write call: actual read-write confirmation `none`, actual full `policy`, preview/pre-execution denial `none`; info/block guard satisfaction null. SDK argument rejection before entry uses the SDK rejection path.
- Payload limits: 60,000 characters per item value and 180,000 characters for the complete document. Whole-value omissions retain identity, outcome summaries, guards, and read-based recovery guidance.
- Reuse `StructuredToolResult`, `StructuredOperationBatch`, canonical JSON, the worker-payload reader, and shared omission metadata. Add conditional members to their existing register with appearance conditions.
- Retire Network-only token code after checking callers; keep legacy batch `WriteSafetyService`, `WriteSafetyTooling`, and `SafetyRead` behavior. Add no snapshot worker operation or token-bound write surface.
- Siemens calls stay in the worker; do not commit or package Siemens assemblies. Undo, new operations, output-reference syntax, persistence, compile/download, and PLC control are outside this PR.
- Local implementation/fix steps each receive a focused commit after diff review and relevant validation. Use serial builds/tests; final scoped coverage must meet 80% and report coverage of materially changed host/contracts logic.
- No live TIA/PLC actions or remote writes are authorized by this plan. Freeze and inspect the candidate before requesting exact-target live authorization; record live acceptance status separately from offline evidence.

## Review Focus

1. A legacy client sends `confirm:false`, an unknown property, or a string `dryRun`: reject through real MCP binding before any worker activity (Task 6).
2. Discovery succeeds partly but cannot enumerate a connection or its owning grouped/ungrouped device: show degraded evidence and block a write needing it; never infer an empty inventory (Tasks 3 and 5).
3. Requested settings all skip, or an applied field's post-read becomes unreadable: retain sparse attempted evidence, fail appropriately, and stop later items (Tasks 2 and 4).
4. The same target is changed twice, or an earlier connection changes a later delete's consequences: re-plan in order; preserve immediate checks and compare the final effective state (Task 5).
5. A failed partial result, diagnostic, or verification exceeds the output budget: preserve failure/identity/guards, bound the entire canonical document, and audit its exact returned hash (Task 7).

---

## Execution conventions and file boundaries

Run from the Network worktree. At execution start inspect `AGENTS.md`, branch/head/status, and the approved spec/plan; preserve concurrent work. Record baseline and task evidence under ignored `.superpowers/`, with exact commands, heads, test counts, failures, and review rulings. Build the current solution before subprocess protocol tests; linked host sources alone do not refresh the executable.

Use this focused-test command throughout, replacing `<filter>` with the task's named classes:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter '<filter>' -- xUnit.ParallelizeTestCollections=false xUnit.ParallelizeAssembly=false xUnit.MaxParallelThreads=1 RunConfiguration.MaxCpuCount=1
```

Restore once when required; build with `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true`. The [xUnit VSTest setting](https://xunit.net/docs/config-runsettings) is singular `ParallelizeAssembly`; this corrects the draft's spelling without changing the serial-test requirement. A RED result must be the named assertion or missing new interface, not an environment failure. Record and commit RED tests separately, then validate and commit each implementation/fix step; stage only that step's files. Expected intermediate RED commits do not justify claiming a green candidate. Each task ends with its focused suite green and diff review; independent task review applies to the selected subagent execution. Reserve the complete qualification suite for Task 9.

New contracts live in `TiaMcpServer.Contracts`; worker capture/checks in `TiaMcpServer.OpennessWorker/Openness`; host planning/composition in `TiaMcpServer/Network`; registration in `TiaMcpServer/Tools`. Host/tests must not reference Siemens through new production code. New test helpers belong in the existing Network test directory. The test project links host directories automatically; explicitly link only new Siemens-free worker comparison code needed for executable tests.

### Task 1: Version the active read envelope

**Files:** Modify `TiaMcpServer/Network/NetworkToolResponses.cs`, `NetworkReadTools.cs` in that directory; create `TiaMcpServer/Network/NetworkContractVersion.cs`; modify `TiaMcpServer.Tests/Network/NetworkStructuredProtocolTests.cs`, `NetworkToolsTests.cs`, and `TiaMcpServer.Tests/Tools/ToolOutputContractConformanceTests.cs`.

**Interfaces:** Consume the existing read catalog, `StructuredOperationBatch`, and `StructuredToolResult.Create<T>(T response, bool isError)`. Produce `NetworkContractVersion.Current = "1.0"` and `NetworkReadResponse(string Tool, string ContractVersion, bool Success, NetworkToolError? Error, IReadOnlyList<string> Warnings, StructuredOperationBatch? Batch)`. Keep per-item warnings in their items; root warnings are call-level diagnostics.

- [ ] Write `ReadEnvelope_IsVersionedAndCanonical` and `ReadRejection_HasExplicitNullBatch` on the registered read route, asserting:

```csharp
Assert.Equal("1.0", root.GetProperty("contractVersion").GetString());
Assert.Equal(JsonValueKind.Array, root.GetProperty("warnings").ValueKind);
Assert.Equal(textDocument, structuredDocument);
Assert.Equal(JsonValueKind.Null, rejected.GetProperty("batch").ValueKind);
Assert.True(rejectedIsError);
```

- [ ] Run the focused command with `FullyQualifiedName~NetworkStructuredProtocolTests|FullyQualifiedName~NetworkToolsTests|FullyQualifiedName~ToolOutputContractConformanceTests`; record the expected envelope assertions failing and commit `test: pin versioned network read envelope`.
- [ ] Implement the response signature and read composition/rejection/budget callbacks using the shared version constant. Keep typed item results, read continuation after failure, selectors, and pagination unchanged.
- [ ] Run that focused suite plus existing `NetworkPayloadContractTests`; expect all pass with strict decoding and text/structured equality. Inspect the diff and commit `feat: version network read envelope`.

### Task 2: Preserve sparse completed configuration failures

**Files:** Modify `TiaMcpServer.OpennessWorker/Openness/NetworkDeviceConfigurator.cs`, `TiaMcpServer/Network/NetworkPayloadContract.cs`, `TiaMcpServer.FakeWorker/Program.cs`; modify `TiaMcpServer.Tests/Network/ConfigureNetworkDeviceResultInfoTests.cs`, `NetworkPayloadContractTests.cs`, `NetworkOperationFakeWorkerTests.cs`; create `TiaMcpServer.Tests/Network/NetworkSparseConfigurationTests.cs`. The existing `ConfigureNetworkDeviceResultInfo` fields remain unchanged.

**Interfaces:** Consume worker `NetworkDeviceConfigurator.Configure(Project project, string deviceName, string nodeId, string? ipAddress, string? subnetMask, string? pnDeviceName, string? subnetId, string? ioSystemSubnetId, int? ioSystemNumber) -> ConfigureNetworkDeviceResultInfo` and host `NetworkPayloadContract.Project(NetworkOperationRequest, WorkerCallResult) -> StructuredOperationItem`. Produce completed typed outcomes even when every requested setting is skipped. The projector retains the validated result while setting `status:failed` and `failure.category:worker_operation_failed` whenever `SkippedSettings.Count > 0`.

- [ ] Write `Configuration_ReportsOnlyRequestedKeys`, `PartialConfiguration_RetainsResultAndStopsBatch`, `AllSkipped_RetainsTypedResult`, and `MissingRequiredMap_IsProtocolError`. Use requests for `Address`/`IoSystem`, mixed and all-skipped worker payloads, and the following assertions:

```csharp
Assert.Equal(new[] { "Address" }, applied.Keys);
Assert.Equal(new[] { "IoSystem" }, skipped.Keys);
Assert.Equal("failed", partial.Status);
Assert.Equal("worker_operation_failed", partial.Failure!.Category);
Assert.NotNull(partial.Result);
Assert.Equal("earlierOperationFailed", later.SkipReason);
Assert.Equal("protocol_error", missingMap.Failure!.Category);
Assert.Null(missingMap.Result);
```

- [ ] Run `FullyQualifiedName~NetworkSparseConfigurationTests|FullyQualifiedName~ConfigureNetworkDeviceResultInfoTests|FullyQualifiedName~NetworkPayloadContractTests`; observe the new completed-outcome assertions fail and commit `test: pin sparse network configuration failures`.
- [ ] Change the configurator's finalization to return its attempted result rather than throwing solely because all settings skipped. Keep unexpected exceptions as worker failures. Validate first, then classify skip maps in the one existing projector; never fill absent required DTO members or pad unrequested keys.
- [ ] Run the focused suite and `NetworkOperationFakeWorkerTests`; expect mixed/all-skipped typed evidence, stop/skip behavior, and all existing malformed-payload rules to pass. Review and commit `fix: retain sparse attempted network configuration outcomes`.

### Task 3: Expose reliable relationship evidence through ordinary reads

**Files:** Create `TiaMcpServer.Contracts/NetworkConnectionEvidenceInfo.cs`; modify `NodeInfo.cs`, `SubnetInfo.cs`, `HardwareConfigInfo.cs` in Contracts; modify `TiaMcpServer.OpennessWorker/Openness/HardwareConfigReader.cs`; modify `TiaMcpServer/Network/NetworkPayloadContract.cs`, `TiaMcpServer.FakeWorker/Program.cs`, `TiaMcpServer.Tests/Json/ConditionalMemberRegisterTests.cs`; create `TiaMcpServer.Tests/Network/NetworkConnectionEvidenceTests.cs`; modify existing hardware/Network payload fixtures affected by the additions.

**Interfaces:** Produce shared POCOs with normal explicit-null serialization:

- `NetworkNodeIdentityInfo`: `string DeviceName`, `string NodeId`.
- `NetworkNodeConnectionInfo`: `bool Complete`, `string? SubnetId`, `string? IoSystemSubnetId`, `int? IoSystemNumber`, `List<string> Messages`. Complete with null identities means a reliably disconnected/not-applicable relationship; incomplete means unknown.
- `NetworkSubnetConnectionsInfo`: `bool Complete`, `List<NetworkNodeIdentityInfo> Nodes`, `List<string> Messages`.
- Add conditional `NodeInfo.ConnectionEvidence : NetworkNodeConnectionInfo?`, `SubnetInfo.ConnectionEvidence : NetworkSubnetConnectionsInfo?`, and `HardwareConfigInfo.RootDeviceCount : int?`. New ordinary worker reads populate them; historical/legacy or older paged payloads may omit them. Register those conditions; a write requiring absent evidence fails closed.

- [ ] Write `ConnectedIdentity_UsesDeviceAndNodeId`, `EmptyInventory_RequiresCompleteEvidence`, `DegradedOwnerOrEnumerator_RemainsIncomplete`, `GroupedAndUngroupedOwners_AreRetained`, and `KnownNullRelationship_IsDistinctFromUnreadable`, asserting:

```csharp
Assert.Equal("PLC_Grouped", evidence.Nodes[0].DeviceName);
Assert.Equal("node-2", evidence.Nodes[0].NodeId);
Assert.False(degraded.Complete);
Assert.NotEmpty(degraded.Messages);
Assert.True(disconnected.Complete);
Assert.Null(disconnected.SubnetId);
Assert.Equal(2, read.RootDeviceCount); // root collection, not all-scope total
```

- [ ] Run `FullyQualifiedName~NetworkConnectionEvidenceTests|FullyQualifiedName~HardwareConfigInfoTests|FullyQualifiedName~ConditionalMemberRegisterTests`; observe missing evidence tests fail and commit `test: pin network relationship completeness`.
- [ ] Extend existing node/subnet read capture, using qualified V21 APIs and the current all-scopes device matcher. An enumeration, identity, or owner-read failure makes the relevant inventory incomplete even if some nodes were collected. Sort node evidence by device name then node ID using deterministic comparers; never use display names alone as selectors. Preserve existing discovery diagnostics and page behavior.
- [ ] Run that suite plus `NetworkPayloadContractTests`, `HardwarePaginationFakeWorkerTests`, `ProjectDeviceNameMatcherTests`; compile the worker with stubs and installed V21 references. Expect green contract/fixture tests and both compile paths; record compile as API compatibility evidence, not live behavior. Review and commit `feat: expose complete network connection evidence`.

### Task 4: Capture immediate typed postconditions in mutation outcomes

**Files:** Create `TiaMcpServer.Contracts/NetworkMutationVerificationInfo.cs`, `TiaMcpServer.OpennessWorker/Openness/NetworkMutationVerifier.cs`, `NetworkPostconditionChecks.cs` in that worker directory; modify `AddDeviceResultInfo.cs`, `ConfigureNetworkDeviceResultInfo.cs`, `SubnetLifecycleResultInfo.cs` in Contracts; modify worker `NetworkDeviceCreator.cs`, `NetworkDeviceConfigurator.cs`, `SubnetLifecycleService.cs`, and `Program.cs`; modify host `NetworkPayloadContract.cs`, FakeWorker `Program.cs`, conditional-member tests, `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`; create `TiaMcpServer.Tests/Network/NetworkMutationVerificationTests.cs`; modify existing worker dispatch/service contract tests.

**Interfaces:** Produce shared `NetworkVerificationCheckInfo` with `string Name`, `string Status`, `string? Expected`, `string? Observed`, `string? Message`; and `NetworkMutationVerificationInfo` with `string Status`, `Dictionary<string,string> Identity`, `List<NetworkVerificationCheckInfo> Checks`, `string? Message`. Check statuses are `passed`, `failed`, `unverified`; summary additionally permits `not_required` only when no setting applied. Add conditional `Verification : NetworkMutationVerificationInfo?` to the three mutation result DTOs, registering its appearance for new Network worker outcomes. Identity keys: `deviceName`/`deviceItemName` for add; `deviceName`/`nodeId` for configure; `subnetId` for subnet operations.

Produce worker `NetworkMutationVerifier.VerifyAddedDevice(Project, WorkerRequest, AddDeviceResultInfo)`, `VerifyConfiguration(Project, WorkerRequest, ConfigureNetworkDeviceResultInfo)`, and `VerifySubnet(Project, WorkerRequest, SubnetLifecycleResultInfo, int rootCountBefore, IReadOnlyList<NetworkNodeIdentityInfo> affectedNodes)`, each returning `NetworkMutationVerificationInfo`. `WorkerRequest` is the shared flat worker envelope; the worker never references the host's `NetworkOperationRequest`. Produce Siemens-free `NetworkPostconditionChecks.Compare(string name, string? expected, string? observed, bool readable) -> NetworkVerificationCheckInfo`; link that comparison source into tests. Extend the existing host projector with `Project(NetworkOperationRequest, WorkerCallResult, bool requireVerification) -> StructuredOperationItem`; retain the existing overload for reads/transitional callers.

- [ ] Write `AppliedSubsetOnly_IsVerified`, `UnreadableAppliedValue_IsUnverified`, `WrongIoSystemOnOtherSubnet_Fails`, `Deletion_PreservesAffectedNodesAcrossScopes`, `UninspectableSubnetId_IsNotAbsence`, `MissingOrContradictoryVerification_IsProtocolError`, `UnknownOrUnaccountedSetting_IsProtocolError`, and `PostconditionFailure_StopsLaterItems`, asserting:

```csharp
Assert.Equal(new[] { "Address" }, checks.Select(check => check.Name));
Assert.Equal("unverified", unreadable.Status);
Assert.Equal("failed", wrongIoTuple.Status);
Assert.Equal("protocol_error", missingEvidence.Failure!.Category);
Assert.Null(missingEvidence.Result);
Assert.Equal("postcondition_failed", wrongState.Failure!.Category);
Assert.NotNull(wrongState.Result);
Assert.Equal("earlierOperationFailed", later.SkipReason);
```

- [ ] Run `FullyQualifiedName~NetworkMutationVerificationTests|FullyQualifiedName~NetworkSubnetLifecyclePayloadContractTests|FullyQualifiedName~ConditionalMemberRegisterTests`; record RED and commit `test: pin immediate network mutation postconditions`.
- [ ] Implement comparison/capture and attach verification before returning the existing worker success payload. Cover all five operations using spec §8's postconditions. Capture the reliable affected-node inventory and root count immediately before subnet deletion; deny dispatch if necessary pre-mutation evidence is unreadable. Read back only applied configuration keys, exact subnet/IO tuple, returned device/item/type, supplied subnet attributes, reliable deletion absence, affected node/device preservation, and root count. No applied configuration keys means `not_required`; requested skips still fail execution. Where a contract-valid subnet result exists, carry existing known postcondition failures in typed verification rather than discarding that result in a postcondition exception. Preserve attempted evidence when reads fail; never convert unknown state into success. The shared projector checks result/evidence identities against the request, requires the operation's relevant checks, rejects unknown/unrequested or contradictory setting keys and invalid statuses, and accepts `not_required` only for configuration with no applied keys. Prefer `worker_operation_failed` for skips, otherwise `postcondition_failed` for failed/unverified required checks, and `protocol_error` for invalid evidence without echoing it.
- [ ] Run the focused suite, existing mutation forwarding/worker source-contract tests, and serial stub/installed-reference worker builds. Expect green executable comparison/projection tests and compile paths. Review and commit `feat: return immediate typed network verification`. Record orchestration/source checks as static evidence; live read-back qualification remains pending.

### Task 5: Implement the Network guarded domain and ordered final verification

**Files:** Create `TiaMcpServer/Network/NetworkWriteDomain.cs`, `NetworkWritePlanner.cs`, `NetworkWriteVerifier.cs`, `NetworkGuardDefinitions.cs`, `NetworkGuardedWriteModels.cs`; refactor reusable parts of `NetworkSafetySnapshot.cs` into the planner; modify `NetworkIdentityResolver.cs` only where new evidence is consumed. Create `TiaMcpServer.Tests/Network/NetworkGuardedWriteFixture.cs`, `NetworkGuardedWriteDomainTests.cs`, `NetworkGuardedWriteOrderingTests.cs`; update FakeWorker scenarios and Network fixtures.

**Interfaces:** Consume Tasks 3–4's typed evidence, existing `NetworkIdentityResolver.Resolve(...)`, `NetworkWorkerInvoker.InvokeWriteAsync(OpennessWorkerClient, NetworkOperationRequest, string?)`, and `WriteExecution.RunAsync(...)`. Produce a new `NetworkWriteDomain(OpennessWorkerClient)` instance for every call, implementing `IWriteDomain<NetworkOperationRequest, NetworkWriteEffect, NetworkWriteVerification, NetworkGuardedWriteResponse>` with the exact inherited method signatures in `TiaMcpServer/Safety/Pipeline/IWriteDomain.cs`. `Project` uses Task 4's `requireVerification:true` overload and retains trusted immediate evidence per operation ID. Do not register mutable domain state as a singleton.

Produce these host records:

- `NetworkWriteEffect(string Operation, NetworkWriteTargetEvidence Target, IReadOnlyDictionary<string,string> RequestedSettings, IReadOnlyDictionary<string,NetworkAttributeInfo> CurrentSettings, IReadOnlyList<NetworkNodeIdentityInfo> AffectedNodes, bool? ConnectionsComplete, int? RootDeviceCount)`; include only relevant requested fields; unavailable current values use the existing attribute availability vocabulary.
- `NetworkOperationVerification(string OperationId, string Operation, string Status, NetworkMutationVerificationInfo? Evidence, StructuredOperationOmission? Omission)`; skipped items receive no claim and are excluded.
- `NetworkWriteVerification(bool Success, IReadOnlyList<NetworkOperationVerification> Operations, IReadOnlyList<NetworkVerificationCheckInfo> FinalChecks, StructuredOperationOmission? Omission)`.
- `NetworkWriteEffectPresentation(string OperationId, NetworkWriteEffect? Effect, StructuredOperationOmission? Omission)`.
- `NetworkGuardedWriteResponse(string Tool, string ContractVersion, string Phase, bool Success, WriteToolError? Error, IReadOnlyList<string> Warnings, IReadOnlyList<WriteGuardReport> Guards, IReadOnlyList<NetworkWriteEffectPresentation> Effects, StructuredOperationBatch? Batch, NetworkWriteVerification? Verification)`; keep the old token response until Task 6 cuts over callers.

Produce `NetworkWritePlanner(OpennessWorkerClient).PlanAsync(string?, IReadOnlyList<NetworkOperationRequest>) -> Task<WritePlan<NetworkWriteEffect>>`, `ReplanAsync(string?, NetworkOperationRequest) -> Task<ItemReplan<NetworkWriteEffect>>`, and `NetworkWriteVerifier(OpennessWorkerClient).VerifyAsync(string?, StructuredOperationBatch, IReadOnlyDictionary<string,NetworkMutationVerificationInfo>) -> Task<NetworkWriteVerification>`. Produce `NetworkGuardDefinitions.Definitions : IReadOnlyList<WriteGuardDefinition>` with the two exact guard IDs/severities, materialized through `NetworkGuardDefinitions.Validate(IEnumerable<WriteGuardDefinition>) -> IReadOnlyList<WriteGuardDefinition>`; validation throws `ArgumentException` for Network severity `acknowledge`. Fixture `NetworkGuardedWriteFixture.CreateAsync(TempAuditDirectory, string projectPath, McpAccessMode mode = McpAccessMode.ReadWrite) -> Task<NetworkGuardedWriteFixture>` returns a verified FakeWorker client, shared runner, and inspectable audit sink without depending on token safety.

- [ ] Write `DryRun_ReportsBlocksWithoutMutation`, `ConnectedDelete_IsInformationalInBothModes`, `IncompleteInventory_BlocksBothModes`, `Readonly_DeniesBeforeGate`, `NetworkAcknowledgeRegistration_IsRejected`, and `BindingChange_PreventsDispatch`. Add `RepeatedSettings_PreserveImmediateChecks`, `ConnectThenDelete_ReplansLateInfo`, `EarlierAdd_RefreshesRootCount`, and `LateBlock_PreservesEarlierChanges`, asserting:

```csharp
Assert.Equal("network_delete_connected_subnet", connectedGuard.Id);
Assert.Equal("info", connectedGuard.Severity);
Assert.Null(connectedGuard.Acknowledged);
Assert.Throws<ArgumentException>(() => NetworkGuardDefinitions.Validate(
    new[] { new WriteGuardDefinition("network_test", "acknowledge", "test") }));
Assert.Equal("blocked", blocked.Phase);
Assert.Equal("preview", blockedDryRun.Phase);
Assert.True(blockedDryRun.Success);
Assert.Equal(0, dryRunMutationCount);
Assert.Equal("binding_conflict", changedBinding.Error!.Category);
Assert.Equal("passed", firstImmediate.Status); // later value intentionally differs
Assert.True(finalVerification.Success);
Assert.Equal("earlierOperationFailed", afterLateBlock.SkipReason);
```

- [ ] Run `FullyQualifiedName~NetworkGuardedWriteDomainTests|FullyQualifiedName~NetworkGuardedWriteOrderingTests`; record RED and commit `test: pin guarded network planning and ordering`.
- [ ] Implement planner/domain validation and effects. Reuse catalog validation and access policy before the gate; enforce a single common project constraint. Ordinary reads provide exact identities and completeness; no new snapshot operation. Resolve the ordered call and retain effects plus `DependsOn` for affected later items; use the existing `ItemPlan` constructor to retain both initial evidence and dependency information. Re-plan before dependent dispatch, including root counts after earlier additions. Emit info for reliably connected deletion and block when required consequences are unreadable. Review focused planner tests and commit `feat: plan network writes on the shared guarded pipeline`.
- [ ] Implement final verification over the attempted prefix. Keep immediate evidence for each attempt, include an `unverified` record when no trustworthy worker evidence exists, and exclude skipped items. Derive final expectations in order: later applied changes replace earlier expectations for the same field/relationship, and deletion supersedes that target's existence checks. Still verify preserved devices/nodes and surviving effective expectations using ordinary reads. Unknown final evidence makes `VerificationSucceeded` false and adds inspection guidance; never dispatch a retry. `Compose` maps the shared report to the new response without losing partial typed results.
- [ ] Run both focused classes plus `WriteExecutionTests`, `WriteExecutionFakeWorkerTests`, and Network strict payload tests; expect late info allowed, late block stopping, all required identities/checks preserved, and green shared-runner regression tests. Review and commit `feat: verify ordered network write outcomes`.

### Task 6: Cut over the real MCP route and remove Network token callers

**Files:** Modify `TiaMcpServer/Network/NetworkWriteTools.cs`, `NetworkToolResponses.cs`, `TiaMcpServer/Tools/McpToolRegistration.cs`, `TiaMcpServer/Program.cs`, `TiaMcpServer/ProjectLifecycle/LifecycleWriteDomain.cs`; create `TiaMcpServer/Tools/NetworkWriteArgumentValidatingTool.cs`; remove `TiaMcpServer/Safety/CanonicalWriteSafety.cs` and remaining Network-only snapshot/token types only after a caller inventory. Create `TiaMcpServer.Tests/Network/NetworkGuardedWriteMcpTests.cs`, `NetworkGuardedWriteAuditTests.cs`; migrate affected Network token tests and Network branches of shared safety tests to the guarded contract. Preserve generic-batch token tests.

**Interfaces:** Produce `NetworkWrite(OpennessWorkerClient workerClient, WriteExecution execution, NetworkOperationRequest[] operations, bool dryRun = false, CancellationToken cancellationToken = default) -> Task<CallToolResult>`. Its schema exposes only `operations`/`dryRun` and `NetworkGuardedWriteResponse`. Produce registration `WithNetworkWriteTools(IMcpServerBuilder) -> IMcpServerBuilder` using the current explicit-registration/`DelegatingMcpServerTool` pattern from `Tools/ProjectReadTools.cs`; wrapper override `InvokeAsync(RequestContext<CallToolRequestParams>, CancellationToken) -> ValueTask<CallToolResult>` rejects unknown arguments and non-boolean `dryRun` before delegating. Service parameters are not public inputs. Extract `LifecycleWriteDomain.GuardDefinitions : IReadOnlyList<WriteGuardDefinition>` without changing its values; build the runner catalog from lifecycle plus Network definitions once, letting the catalog add its intrinsic partial-write guard once.

- [ ] Write registered-protocol theories `LegacyOrMalformedArguments_DoNotReachWorker`, `NetworkNeverElicits`, `LifecycleStillConfirms`, and `WriteEntry_ProducesExactlyOneAudit`. Cover `confirm:false`, `safetyToken`, `acknowledge`, arbitrary unknown root keys, string/null `dryRun`, unsupported elicitation clients, connected deletion, RW/full actual/dry-run, validation/block/partial/verification failure; assert:

```csharp
Assert.True(legacyReply.IsError);
Assert.Equal(0, legacyWorkerActivity);
Assert.Equal(0, networkElicitationCount);
Assert.Equal(1, lifecycleReadWriteElicitationCount);
Assert.Single(enteredCallAuditRecords);
Assert.Equal(2, audit.RecordVersion);
Assert.Equal("none", readWriteAudit.Confirmation.By);
Assert.Equal("policy", fullActualAudit.Confirmation.By);
Assert.All(audit.Guards, guard => Assert.Null(guard.SatisfiedBy));
Assert.Equal(JsonValueKind.Null, appliedFailure.GetProperty("error").ValueKind);
Assert.False(appliedFailureIsError);
```

- [ ] Build the solution, then run `FullyQualifiedName~NetworkGuardedWriteMcpTests|FullyQualifiedName~NetworkGuardedWriteAuditTests|FullyQualifiedName~LifecycleMcpProtocolTests|FullyQualifiedName~WriteToolMcpAnnotationProtocolTests`; record RED and commit `test: pin prompt-free registered network writes`.
- [ ] Wire the public facade/registration and union guard catalog. Select the first explicit project path as a constraint; domain validation checks all other paths before the gate. Send every entered call through the shared runner so validation failures are audited; do not pass an elicitation adapter or lifecycle preparation strategy. Reject legacy arguments through the registered wrapper and advertise strict schemas/MCP write annotations. Run focused checks, review, and commit `feat: expose guarded network writes without elicitation`.
- [ ] Inventory `CanonicalWriteSafety`, `NetworkWritePreview`, Network-only token/hash/expiry helpers, and all Network `confirm`/`safetyToken` callers. Migrate tests to dry run/actual execution, then delete only unused Network code; retain shared serializers/budget helpers and generic-batch token machinery. Keep internal worker protocol `Confirm` where existing dispatch requires it; removing public Network confirmation does not change that internal boundary. Run Network suites plus `WriteToolSafetyTokenTests`, `ReadWriteModeCeilingTests`, lifecycle protocol/confirmation suites; expect all green. Review and commit `refactor: retire network token execution paths`.

### Task 7: Bound the complete envelope and pin failure/audit boundaries

**Files:** Create `TiaMcpServer/Network/NetworkWritePayloadBudget.cs`, `TiaMcpServer.Tests/Network/NetworkGuardedWriteBudgetTests.cs`; modify `NetworkWriteDomain.cs`, Network guarded audit/MCP tests, and existing `NetworkStructuredProtocolTests.cs`. Reuse `TiaMcpServer/OperationBatches/StructuredOperationBatchPayloadBudget.cs` and its omission types rather than replacing them. Change shared pipeline source only if an executable regression exposes a necessary defect, with independent lifecycle/batch regression coverage.

**Interfaces:** Produce `NetworkWritePayloadBudget.Apply(NetworkGuardedWriteResponse response, int maxItemChars = 60000, int maxDocumentChars = 180000) -> NetworkGuardedWriteResponse`; `Compose` calls it before the runner serializes/audits. Budget callbacks measure the complete response. Omit oversized result/effect/verification values whole, retain operation IDs and summary statuses, and keep required root arrays/nulls. A failed result remains failed if its value is omitted; root success must not become true. Requested evidence omitted from delivery makes root success false even if internal execution/verification passed; retain those internal outcome summaries rather than inventing a mutation failure.

- [ ] Write `WholeEnvelope_IncludesEffectsGuardsAndVerification`, `FailedPartialResult_OmissionRetainsFailure`, `LargeDiagnostic_IsBoundedWithoutPayloadEcho`, `OmissionNeverRequiresWriteReplay`, `ReturnedCanonicalHash_EqualsAuditHash`, and `CrashOrInvalidPayload_RemainsUnverified`, asserting:

```csharp
Assert.True(canonical.Length <= 180000);
Assert.True(presentItemValueLengths.All(length => length <= 60000));
Assert.Equal("failed", omittedPartial.Status);
Assert.Equal("worker_operation_failed", omittedPartial.Failure!.Category);
Assert.NotNull(omittedPartial.Omission);
Assert.False(bounded.Success);
Assert.Equal("sha256:" + ContentHashes.Sha256Hex(canonical), audit.ResponseHash);
Assert.DoesNotContain(rejectedPayloadMarker, canonical);
Assert.Equal("unverified", uncertainVerification.Status);
```

- [ ] Run `FullyQualifiedName~NetworkGuardedWriteBudgetTests|FullyQualifiedName~NetworkGuardedWriteAuditTests|FullyQualifiedName~NetworkStructuredProtocolTests`; record RED and commit `test: pin complete network write budgets and audit hashing`.
- [ ] Implement deterministic budgeting of all new root values and relevant diagnostics, preserving compact guards and summaries. Generate guard messages from compact target identities rather than raw worker diagnostics; replace oversized diagnostic values whole with a bounded summary and shared omission metadata, retaining the failure category. Avoid unbounded duplicate diagnostics across root/items. Use filtered/paged `network_read` recovery guidance; never truncate JSON, budget the internal plan, or replay a write. Convert expected worker/post-read failures to typed failed/unverified evidence inside the domain; unexpected programming exceptions retain the shared runner's diagnostic/audit handling.
- [ ] Run focused tests plus existing structured budget, strict payload, `WriteExecutionFailureAuditTests`, and `JsonlWriteAuditSinkTests`; expect exact single audit per entered path, returned-document hash equality, bounded valid canonical JSON, and no failure-to-success conversion. Review and commit `feat: bound guarded network responses and audit evidence`.

### Task 8: Migrate maintained docs and prepare public live acceptance

**Files:** Modify `AGENTS.md`, `README.md`, `docs/ARCHITECTURE.md`, `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md`, `docs/roadmap/json-contract.md`, `docs/roadmap/network-operations.md`, `docs/guides/mcp-client-configuration.md`, `docs/development/local-mcp-testing.md`, `docs/README.md`, `docs/superpowers/README.md`; modify `scripts/live-test-network-phase4-subnets.ps1`, `TiaMcpServer.Tests/Network/NetworkSubnetLifecycleLiveHarnessScriptTests.cs`; create `scripts/live-test-network-guarded-write.ps1`, `TiaMcpServer.Tests/Network/NetworkGuardedWriteLiveHarnessScriptTests.cs`. Update other maintained Network examples only when caller inventory identifies them; preserve historical acceptance reports and worker-only qualification protocol.

**Interfaces:** Produce a public-MCP harness with existing process/framing helpers, `-Mode Inventory|Preview|Apply` (default Inventory), explicit `-ProjectPath`, `-AccessMode read-write|full`, operation-fixture input, and retained apply-only `-AllowMutation`/exact acknowledgement protections. These harness protections authorize live test execution and are not server elicitation. The new harness covers configuration/ordered partial outcomes; the existing Phase 4 harness retains exact connected Ethernet/PROFIBUS fixture IDs and its restoration boundary. Neither harness saves/closes/downloads or automatically retries uncertain writes.

- [ ] Write static harness contract tests `InventoryAndPreview_NeverMutate`, `Apply_RequiresExactTargetAuthorization`, `PublicCalls_UseDryRunAndNoTokens`, and `Restoration_UsesFreshIdentityInspection`, asserting:

```csharp
Assert.Contains("dryRun", script);
Assert.DoesNotContain("safetyToken", publicWriteArguments);
Assert.DoesNotContain("confirm", publicWriteArguments);
Assert.Contains("AllowMutation", script);
Assert.Contains("ProjectPath", script);
Assert.Contains("bind_project", script);
```

- [ ] Run `FullyQualifiedName~NetworkGuardedWriteLiveHarnessScriptTests|FullyQualifiedName~NetworkSubnetLifecycleLiveHarnessScriptTests`; record RED and commit `test: pin guarded network live harness boundaries`.
- [ ] Implement harness migration using public tools: inspect status, bind the exact already-open disposable target, read identities, preview via `dryRun:true`, apply via `dryRun:false` only inside the authorized Apply path. Retain fresh post-write/restoration reads, typed phase/partial verification assertions, multi-homed node and exact IO tuple checks, connected deletion consequences, zero elicitation checks, and evidence output keyed to frozen head/fixture. Verify PowerShell syntax and focused static tests without launching TIA; review and commit `test: migrate public network acceptance harnesses`.
- [ ] Update maintained docs to implemented behavior, versioned envelopes, sparse partial examples, no server Network prompts, legacy-input rejection, no batch rollback, and inspect-before-retry. Keep lifecycle confirmation and generic-batch tokens accurately scoped; README links use absolute main GitHub URLs. Check local links/examples/whitespace, review the docs diff, and commit `docs: document guarded network contract migration`. Do not rerun expensive suites for this documentation step alone.

### Task 9: Freeze and qualify the combined candidate

**Files:** Create `docs/superpowers/acceptance/reports/2026-10-03-network-json-guarded-write-offline-validation.md`; update both documentation indexes and plan/spec status. Product edits occur only as focused reviewed fixes to a finding; each fix needs its own failing test, focused validation, and commit before refreezing.

**Interfaces:** Consume all prior deliverables. Produce a recorded frozen candidate head, test/build/coverage/package evidence, independent whole-branch review, and explicit live-acceptance status. No push/PR/merge, live execution, or worktree cleanup in this task without later authorization.

- [ ] Inspect the complete baseline-to-candidate diff and active worktree status; resolve task-review findings and verify no uncommitted product edits. Record the candidate head before final qualification.
- [ ] Run the complete Release offline suite with coverage after the same-head solution build:

```powershell
dotnet restore TiaMcpServer.slnx
dotnet build TiaMcpServer.slnx --configuration Release -m:1 /p:UseTiaPortalReferenceStubs=true
$networkResults = Join-Path 'TestResults' ('network-final-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --configuration Release --no-build --no-restore --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory $networkResults -- xUnit.ParallelizeTestCollections=false xUnit.ParallelizeAssembly=false xUnit.MaxParallelThreads=1 RunConfiguration.MaxCpuCount=1
$networkCoverage = Get-ChildItem -LiteralPath $networkResults -Recurse -Filter coverage.cobertura.xml | Select-Object -First 1
./scripts/verify-coverage-threshold.ps1 -CoveragePath $networkCoverage.FullName -MinimumLineRate 0.80
```

Expected: full suite passes with zero failures, coverage threshold passes; list any skips and exclusions. Record meaningful changed host/contracts coverage and worker logic's narrower executable/static evidence. Timeouts/restore failures are limitations, not passing checks.

- [ ] Build installed-reference Release serially and package the same candidate into a task-specific artifact directory:

```powershell
dotnet build TiaMcpServer.slnx --configuration Release -m:1 /p:UseTiaPortalReferenceStubs=false /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
dotnet pack TiaMcpServer/TiaMcpServer.csproj --configuration Release --no-build --no-restore -o artifacts/network-final /p:UseTiaPortalReferenceStubs=false
$networkPackage = Get-ChildItem -LiteralPath artifacts/network-final -Filter '*.nupkg' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
./scripts/verify-doctor-package.ps1 -PackagePath $networkPackage.FullName
```

Expected: both commands succeed, package verifier passes, and archive inventory contains no `Siemens.Engineering*.dll` anywhere. Inspect packaged worker/contracts provenance against the frozen candidate. Do not install/reconfigure the user's active tool or run doctor against live TIA. If installed references are unavailable, retain source/offline evidence and explicitly mark that gate pending.

- [ ] Obtain independent whole-branch review covering the five Review Focus cases, worker/host seam, strict real-MCP binding, guards, partial mutation, audit/budgets, and lifecycle/batch regressions. Resolve findings with focused commits; repeat only gates invalidated by the fix and record the new frozen head.
- [ ] Write the validation report with exact heads/commands/counts/artifact paths, review findings/rulings, scope exclusions, and live V21 status. Index it, check links/whitespace, inspect the docs diff, and commit `docs: record network guarded write qualification`.
- [ ] Present the locally reviewable candidate and prepared live gate. Request fresh authorization separately for the exact disposable project, frozen head, access modes, operation arrays, restoration procedure, and excluded lifecycle/PLC actions. Until authorized, live acceptance remains pending; offline completion is not live qualification.

## Self-review and handoff

Spec coverage: §§2–4 map to Tasks 1/2/6; §§5–6 to Tasks 3/5/6; §§7–8 to Tasks 2–5; §9 to Tasks 6–7; §10 to Tasks 6/8; §11 to Task 9. §12 undo remains deferred. Review Focus cases have named assertions in their owning tasks. This plan selects the per-call interfaces and dependency semantics; implementation must inspect exact existing files before editing and record justified deviations rather than silently weakening constraints.

The user approved the spec and plan and selected subagent-driven execution on 2026-10-03. Independent checks at the worker-evidence, guarded-domain, and real-MCP boundaries precede final review. Network agents use this worktree; Multiuser agents continue in their separate worktree. The coordinator supervises both lanes and serializes any shared-state qualification.
