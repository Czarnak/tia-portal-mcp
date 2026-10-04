# Network Discovery and Interface-Qualified Node Identity Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make valid Network writes usable despite optional metadata diagnostics, while selecting and verifying each node within its owning interface.

**Architecture:** Extend the existing ordinary hardware payload with typed structural discovery evidence, and extend existing node selectors with an interface owner path. Keep the shared guarded-write runner, typed worker decoder and net48 mutation boundary; use the same qualified identity for discovery, planning, dispatch, immediate verification, final verification and recovery. Shared Siemens-free path matching and identity comparison prevent host/worker divergence.

**Tech Stack:** Windows, TIA Portal V21, .NET 10 host/tests/FakeWorker, net48 Openness worker, netstandard2.0 Contracts, System.Text.Json, xUnit, PowerShell public-MCP harnesses.

**Spec:** [Approved repair design](../specs/2026-10-04-network-discovery-and-interface-node-identity-repair-design.md). Read it together with the [original Network design](../specs/2026-10-03-network-json-guarded-write-design.md).

**Status (2026-10-04):** Written spec approved; this implementation plan awaits user review. No repair implementation or live acceptance has started. Starting documentation head: `1df1c621e102dc325bb2a75c58637e9ee93c64b3`; existing qualified code head: `b97b9ee9173905fa9c19649a73d4f8a29bd6c682`. Historical offline evidence and the failed live run do not qualify the repaired candidate.

## Global Constraints

- Branch: `feature/network-contract-write-safety`. Keep this repair in the existing Network PR.
- “No Network elicitation or acknowledge guard, connected deletion informational, unknown consequences blocking, sparse applied/skipped settings, ordered stop on failure, pinned verified binding, no rollback or replay.”
- “Keep the two tools, operation names, `operations`, actual-by-default `dryRun`, access tiers and contract version 1.0.” Preserve the 50-item and 256-character operation-ID limits, 60,000-character item limit and 180,000-character document limit.
- “Continue refusing every guarded Network write on an incomplete ordinary project traversal in this repair. Operation-specific tolerance of actual traversal loss is deferred.”
- “Paged/legacy payloads may omit this conditional member. A page and an assembled public-page result never substitute for the guarded domain's ordinary full read.”
- “Device-name comparison retains existing ordinal-ignore-case behavior; path names, interface-name constraints and node IDs use ordinal comparison.”
- “Root-device-count availability remains a separate typed requirement for the operations and final checks that use it. A missing count cannot be synthesized from the all-scope device count.”
- “Execution must commit after each implementation/fix step and retain RED/GREEN evidence.” Do this for both test steps and product fixes; do not accumulate several completed fixes before committing.
- “Network and Multiuser build/test runs must never overlap; use the existing shared verification lock and serial build/xUnit/VSTest settings.” Apply the lock to restore/build/test/pack for their full process/collector lifetime.
- “Older host/worker combinations are not a supported guarded-write deployment.” Ship matching host, Contracts and worker binaries; keep compatible reads and unknown-evidence write refusal.
- “It does not add a new snapshot worker operation, SafetyRead entry, token surface, automatic undo, performance redesign, PLC control or Multiuser runtime behavior.” No dependency additions or Siemens DLL redistribution.
- Preserve ignored execution evidence, unrelated edits and the historical failed live capture. No remote push, PR publication, merge, installation or live mutation is authorized by writing this plan.

## Review Focus

1. Reordered siblings and a missing generic parent type identifier must leave a name/position-qualified node selectable; a hidden sibling with unreadable required identity must refuse selection (Tasks 2–3).
2. Names containing `/`, quotes, backslashes or Unicode must not collide in identity maps; case rules must agree across host and worker (Tasks 3–5).
3. A legacy bare request that was unique at preparation must never move to another interface after replanning; conflicting top-level worker fields must not override a selector (Task 4).
4. Missing, wrong-scope, unknown-stage or contradictory discovery evidence must not become complete by default; unrelated metadata warnings must not fail final verification (Tasks 1–2, 5).
5. Many long qualified affected-node paths must be admitted or refused before the first mutation; late evidence degradation must preserve already-applied outcomes and exact recovery identity (Tasks 5–6).

## Ownership, files and execution order

Run Tasks 1–8 in order. These are one dependent repair across the existing Network boundary, not independent subsystem projects. Each task owns the files named below for its turn; subsequent tasks may extend the same files after the earlier task is committed and reviewed. Workers share the checkout, must preserve others' edits, and must not start concurrent implementers on these overlapping files.

| Task | Responsibility | Main production paths |
| --- | --- | --- |
| 1 | DTOs, strict selector/payload shapes and schemas | `TiaMcpServer.Contracts/HardwareConfigInfo.cs`, `NetworkObjectSelectorInfo.cs`, `NetworkConnectionEvidenceInfo.cs`; `TiaMcpServer/Network/NetworkOperationRequest.cs`, `NetworkOperationCatalog.cs`, `NetworkPayloadContract.cs` |
| 2 | Authoritative traversal evidence and common host gate | `TiaMcpServer.OpennessWorker/Openness/HardwareConfigReader.cs`; `TiaMcpServer/Network/NetworkWritePlanner.cs`, `NetworkWriteVerifier.cs` |
| 3 | Owner-path matching, read selectors and worker resolution | new `TiaMcpServer.Contracts/NetworkInterfacePathMatcher.cs`; worker `NetworkSelectorFactory.cs`, `Openness/NetworkObjectSelectorResolver.cs`, hardware/index readers |
| 4 | Configuration targeting through host and worker | `NetworkIdentityResolver.cs`, `NetworkWorkerInvoker.cs`, `NetworkWriteDomain.cs`; worker client, dispatch and configurator |
| 5 | Qualified relationships and postconditions | new `TiaMcpServer.Contracts/NetworkNodeIdentityComparer.cs`; hardware relationship producer, worker mutation verifier, host final verifier |
| 6 | Prepared recovery admission and delivered core | `TiaMcpServer/Network/NetworkWritePayloadBudget.cs`, `NetworkWriteDomain.cs`, payload validation |
| 7 | Public harness consumers and maintained documentation | `scripts/network-live-mcp-helpers.ps1`, both Network write harnesses, current Network/architecture docs |
| 8 | Independent review, frozen offline qualification and later live gate | acceptance report under `docs/superpowers/acceptance/reports/`, execution ledger and retained artifacts |

Contracts remain Siemens-free. Put new DTO definitions in `HardwareDiscoveryEvidenceInfo.cs` and `NetworkInterfacePathSegmentInfo.cs`; keep existing strict `DeviceItemPathSegmentInfo` unchanged. The two new shared helpers own only path matching and identity equality. Do not restructure unrelated readers or the shared write runner.

Use a repair ledger under ignored `.superpowers/sdd/2026-10-04-network-discovery-repair/`: record task owner, base/result commits, assertion-level RED, GREEN, review findings, source/artifact hashes and the test-slot owner. With the previously selected subagent method, use a fresh implementer and the skill's fresh review gates per task, then a final whole-branch review. The coordinator owns cross-task interfaces, test serialization and the live boundary. Read-only reviews may overlap independent non-test work; implementations and test processes remain sequential.

## Verification commands and evidence

Work from the Network worktree, `C:/Users/LCZ/.codex/worktrees/network-contract-write-safety/tia-portal-mcp`. Reuse the existing ignored `.superpowers/sdd/2026-10-03-network-json-guarded-write/run-network-tests.ps1` wrapper after checking its exact bytes. Its exclusive `FileStream` uses `OpenOrCreate`, `ReadWrite`, `FileShare.None` on `C:/Users/LCZ/AppData/Local/Temp/tia-mcp-01a102eb-32fb-7f62-89bb-7f1c848d0998-tests.lock` until the child process and collector exit. A busy lock means no launch; a yielded process still owns the slot. Do not inspect arbitrary process command lines.

Each task below supplies an exact filter to this focused command; invoke it through that wrapper, retaining separate RED/GREEN logs and TRX files. Use matching configuration/output for FakeWorker tests; build its selected output when fixtures need it. Build/setup failures are not assertion-level RED.

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --configuration Debug --no-restore -m:1 /p:UseTiaPortalReferenceStubs=true --filter '<task filter>' --results-directory 'TestResults/network-discovery-repair/<task>/<red-or-green>' --logger 'trx;LogFileName=focused.trx' -- xUnit.ParallelizeTestCollections=false xUnit.ParallelizeAssembly=false xUnit.MaxParallelThreads=1 RunConfiguration.MaxCpuCount=1
```

For a new typed interface, introduce only the declarations needed to compile its regression, then observe failing behavioral assertions before implementing the body. Preserve that distinction in the ledger. Existing production-linked tests require explicit `<Compile Include>` entries for new host/worker helpers; Contracts are referenced normally. No live process is started by these tests.

After each RED or GREEN step: inspect the scoped diff, run `git diff --check`, stage only that step's paths and commit the conventional message given below. Every separately resolved review finding receives its own focused evidence and fix commit. Register edits with jCodemunch; use current native source where the index is stale.

## Tasks

### Task 1: Add typed traversal and owner-path contract shapes

**Files:** Create `TiaMcpServer.Contracts/HardwareDiscoveryEvidenceInfo.cs`, `TiaMcpServer.Contracts/NetworkInterfacePathSegmentInfo.cs`; modify `HardwareConfigInfo.cs`, `NetworkObjectSelectorInfo.cs`, `NetworkConnectionEvidenceInfo.cs` in Contracts; modify `TiaMcpServer/Network/NetworkOperationRequest.cs`, `NetworkOperationCatalog.cs`, `NetworkPayloadContract.cs`. Test in `TiaMcpServer.Tests/Network/NetworkOperationRequestJsonTests.cs`, `NetworkOperationCatalogTests.cs`, `NetworkPayloadContractTests.cs`, `NetworkPayloadContractTests.Rules.cs`, `NetworkGuardedWriteMcpTests.cs`, `TiaMcpServer.Tests/Json/ConditionalMemberRegisterTests.cs` and `TiaMcpServer.Tests/Tools/ToolOutputContractConformanceTests.cs`.

**Interfaces:** Produce `HardwareDiscoveryEvidenceInfo { string Scope; bool Complete; List<HardwareDiscoveryFailureInfo> Failures; }`, `HardwareDiscoveryFailureInfo { string Stage; string Message; }`, and `NetworkInterfacePathSegmentInfo { string Name; int PositionNumber; string? TypeIdentifier; }`. Add conditional `HardwareConfigInfo.DiscoveryEvidence`, `NetworkObjectSelectorInfo.InterfacePath`, and `NetworkNodeIdentityInfo.InterfacePath`/`InterfaceName`. The path's optional `TypeIdentifier` is conditional. Add strict host `NetworkInterfacePathSegment { string Name; int? PositionNumber; string? TypeIdentifier; }` and `NetworkObjectTarget.InterfacePath`; retain missing-position detection at input.

- [ ] **Step 1 — write regressions:** Add `DiscoveryEvidence_RequiresAllMembersAndKnownStages`, `DiscoveryEvidence_RejectsContradictions`, `QualifiedNodeTarget_ValidatesOwnerPath`, `LegacyRead_OmitsNewConditionalEvidence` and `QualifiedSelector_RoundTripsRegisteredSchema`. Exercise raw JSON with missing `positionNumber`, negative values, blank name/type, unknown fields, both owner paths, and `interfaceName`/`nodeIndex` without a path. Assert invalid requests fail validation, malformed worker evidence returns `protocol_error` without echoed payload, compatible omitted evidence remains readable, and `contractVersion` stays `1.0`.

```csharp
// DiscoveryEvidence_RejectsContradictions / QualifiedNodeTarget_ValidatesOwnerPath
Assert.Equal("protocol_error", malformedPayload.Failure!.Category);
Assert.False(invalidTargetValidation.IsValid);
Assert.Equal("1.0", publicRoot.GetProperty("contractVersion").GetString());
```

- [ ] **Step 2 — observe RED:** Run the focused command with filter `FullyQualifiedName~NetworkOperationRequestJsonTests|FullyQualifiedName~NetworkOperationCatalogTests|FullyQualifiedName~NetworkPayloadContractTests|FullyQualifiedName~NetworkGuardedWriteMcpTests|FullyQualifiedName~ConditionalMemberRegisterTests|FullyQualifiedName~ToolOutputContractConformanceTests`; require the named behavioral assertions to fail and retain the log/TRX.
- [ ] **Step 3 — commit RED:** Inspect/stage the regression paths and commit `test: pin network discovery and owner path contracts`.
- [ ] **Step 4 — implement:** Extend existing catalog/decoder paths without a new public operation. Node inspect/configure accept preferred `interfacePath`, strict legacy `itemPath + nodeIndex`, or a bare device/node pair. `interfacePath` permits optional `interfaceName` and `nodeIndex`; keep generic selector rules unchanged. Strictly validate `scope` as `project|device` and stages as `deviceEnumeration`, `deviceMaterialization`, `deviceItemEnumeration`, `deviceItemMaterialization`, `interfaceDiscovery`, `nodeEnumeration`, `nodeMaterialization`, `subnetEnumeration`, `subnetMaterialization`, `ioSystemEnumeration`, `ioSystemMaterialization`, `deviceSelection`. `complete:true` requires an empty failures list; `complete:false` requires a nonempty list; `deviceSelection` is device-scoped. Register each conditional field with its occurrence rule; omission never means completeness.
- [ ] **Step 5 — observe GREEN:** Run the same filter and existing introspection/request-contract tests; require all pass, including raw required-member and generated-schema probes.
- [ ] **Step 6 — commit implementation:** Inspect/stage only the implementation step and commit `feat: add typed network discovery and interface paths`.

### Task 2: Record structural traversal at the worker and use it in guarded reads

**Files:** Create `TiaMcpServer.OpennessWorker/Openness/HardwareDiscoveryEvidenceCapture.cs`, `TiaMcpServer.Tests/Network/HardwareDiscoveryEvidenceTests.cs`, `TiaMcpServer.Tests/Network/NetworkDiscoveryRepairFixture.cs`; modify worker `Openness/HardwareConfigReader.cs`, host `Network/NetworkWritePlanner.cs`, `Network/NetworkWriteVerifier.cs`, `TiaMcpServer.FakeWorker/Program.cs`, `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`, `NetworkGuardedWriteDomainTests.cs`, `NetworkGuardedWriteOrderingTests.cs`, `NetworkOperationFakeWorkerTests.cs` and relevant hardware pagination tests.

**Interfaces:** Consume Task 1 DTOs. Produce worker `HardwareDiscoveryEvidenceCapture(string scope, Action<string> reportDiagnostic)`, `Evidence: HardwareDiscoveryEvidenceInfo`, `RecordFailure(string stage, string message): void`, and `Traverse<T>(Func<IEnumerable<T>> enumerate, Action<T> materialize, string enumerationStage, string materializationStage): void`. The actual reader calls the helper at traversal sites. Retain host `NetworkWritePlanner.DiscoveryComplete(HardwareConfigInfo state): bool`, changing its implementation to the strict project/unpaged/complete evidence predicate used by preparation, replanning and final verification.

- [ ] **Step 1 — write regressions:** Create a small regression fixture from the retained live subtree/messages: PLC_DP at position 1, interface_1 at 32768, interface_2 at 33024, both E1 nodes, null interface item types and recorded unsupported metadata diagnostics. Record capture provenance; omit machine/project paths and fabricated sibling indices. Historical pages supply metadata only; synthetic ordinary-read discovery flags are explicit test inputs. Add `OptionalMetadata_DoesNotMakeTraversalIncomplete`, `TraversalFailure_BlocksEveryWrite`, `EnumerationThrowsDuringMoveNext_IsIncomplete`, `ServiceException_IsIncompleteButKnownNullServiceIsComplete`, `FilteredOrPagedRead_CannotProveProjectComplete`, `MissingRootCount_IsIndependentOfTraversal` and `LateTraversalFailure_PreservesEarlierOutcome`. Assert diagnostic preservation and refusal before mutation for every structural stage, including grouped/ungrouped devices and skipped materialization.

```csharp
// OptionalMetadata_DoesNotMakeTraversalIncomplete / EnumerationThrowsDuringMoveNext_IsIncomplete
Assert.NotEmpty(optionalMetadataState.Messages);
Assert.True(optionalMetadataState.DiscoveryEvidence!.Complete);
Assert.False(throwingCapture.Evidence.Complete);
Assert.Contains(throwingCapture.Evidence.Failures, f => f.Stage == "nodeEnumeration");
```

- [ ] **Step 2 — observe RED:** Run the focused command with filter `FullyQualifiedName~HardwareDiscoveryEvidenceTests|FullyQualifiedName~NetworkGuardedWriteDomainTests|FullyQualifiedName~NetworkGuardedWriteOrderingTests|FullyQualifiedName~NetworkOperationFakeWorkerTests`; require the named behavioral assertions to fail and retain the log/TRX.
- [ ] **Step 3 — commit RED:** Inspect/stage the regression paths and commit `test: reproduce network metadata discovery refusal`.
- [ ] **Step 4 — implement:** Thread one evidence capture through ordinary `HardwareConfigReader.Read(Project, string?, string?, bool, bool)`. Wrap collection acquisition, iteration and materialization independently; record interface-service exceptions explicitly, and preserve known absence as healthy. Preserve existing optional scalar/type diagnostics without setting structural failure. Keep authoritative `ProjectDeviceEnumerator` failures; do not parse messages or weaken all-scope enumeration. Emit project/device scope only from ordinary reads; page candidates omit it. Update FakeWorker ordinary fixtures to declare their modeled evidence, retaining explicit absent/malformed/degraded cases. Replace the old global metadata/selector veto in the shared host predicate; root count stays a separate consequence/postcondition requirement.
- [ ] **Step 5 — observe GREEN:** Run the same filter plus hardware pagination/payload regressions. Executable throwing-enumerator tests must exercise the production helper, and catch-site wiring must be reviewed; static checks alone are insufficient. Confirm optional diagnostics pass structural preparation but required identities/unknown consequences still refuse.
- [ ] **Step 6 — commit implementation:** Inspect/stage only the implementation step and commit `fix: separate network traversal evidence from metadata`.

### Task 3: Emit and resolve qualified node selectors on reads

**Files:** Create `TiaMcpServer.Contracts/NetworkInterfacePathMatcher.cs`, `TiaMcpServer.Tests/Network/NetworkInterfacePathMatcherTests.cs`; modify worker `NetworkSelectorFactory.cs`, `Openness/HardwareConfigReader.cs`, `Openness/NetworkObjectIndexReader.cs`, `Openness/NetworkObjectSelectorResolver.cs`, `Openness/ResolvedNetworkObject.cs`, and `TiaMcpServer.FakeWorker/Program.cs`. Test in `NetworkSelectorFactoryTests.cs`, `NetworkObjectDiscoveryEvidenceTests.cs`, `NetworkIntrospectionEndToEndTests.cs`, `NetworkIntrospectionContractTests.cs`, `NetworkIntrospectionWorkerDispatchTests.cs` and the Task 2 fixture.

**Interfaces:** Consume Task 1 path DTO. Produce shared `NetworkInterfacePathMatcher.Match<TItem>(IEnumerable<TItem> roots, IReadOnlyList<NetworkInterfacePathSegmentInfo> path, Func<TItem, IEnumerable<TItem>> children, Func<TItem, string?> name, Func<TItem, int?> position, Func<TItem, string?> type): NetworkInterfacePathMatch<TItem>`, with `Success`, `Item`, `FailureCategory`, `Error`. Match results use existing failure categories; enumeration/access failures are unreadable evidence, not absence. Expose worker `NetworkObjectSelectorResolver.ResolveNode(Project project, NetworkObjectSelectorInfo target): NetworkObjectSelectionResult` for later mutation/verification reuse. Add worker-only `ResolvedNetworkObject.OwningInterface: Siemens.Engineering.HW.Features.NetworkInterface?`, populated for node results; `Value` remains the actual Node and `Target` the preferred qualified selector.

- [ ] **Step 1 — write regressions:** Add `TwoInterfacesWithE1_EmitDistinctRoundTripSelectors`, `NullParentType_DoesNotDisableQualifiedNode`, `OwnerPath_IsIndependentOfSiblingOrder`, `UnreadableSiblingIdentity_RefusesUniqueness`, `QualifiedNamespace_RejectsDuplicateNodeIds`, `Constraints_NeverRetarget` and `LegacyIndexedSelector_RemainsStrict`. Match the spec's two position paths exactly. Check ordinal path/node/interface comparison, case-insensitive device matching, optional supplied type constraints and node-index consistency after unique node selection.

```csharp
// TwoInterfacesWithE1_EmitDistinctRoundTripSelectors
Assert.Equal("E1", x1Selector.NodeId);
Assert.Equal("E1", x2Selector.NodeId);
Assert.Equal(32768, x1Selector.InterfacePath![1].PositionNumber);
Assert.Equal(33024, x2Selector.InterfacePath![1].PositionNumber);
Assert.Null(x1Selector.InterfacePath[1].TypeIdentifier);
```

- [ ] **Step 2 — observe RED:** Run the focused command with filter `FullyQualifiedName~NetworkInterfacePathMatcherTests|FullyQualifiedName~NetworkSelectorFactoryTests|FullyQualifiedName~NetworkObjectDiscoveryEvidenceTests|FullyQualifiedName~NetworkIntrospection`; require the named behavioral assertions to fail and retain the log/TRX.
- [ ] **Step 3 — commit RED:** Inspect/stage the regression paths and commit `test: pin interface scoped network read selectors`.
- [ ] **Step 4 — implement:** Build preferred owner paths from typed name/position evidence during traversal, independently from the existing indexed/type-qualified generic item path. Resolve each level by exactly one sibling; unreadable required candidate identity prevents uniqueness, optional type is consulted only when supplied. Resolve the owner's service and unique node ID; never fall back to another interface. Make hardware/index reads, FakeWorker discovery fixtures and inspection return the same preferred selector, with honest individual selectability. Preserve existing strict legacy paths, generic selector kinds and bare-node compatibility when the selected device's full node namespace is readable and unique.
- [ ] **Step 5 — observe GREEN:** Run the same filter plus payload/schema probes. Test the shared matcher through executable candidate adapters used by production code, not only handcrafted read DTOs. Confirm two E1 read outputs are accepted and independently inspectable.
- [ ] **Step 6 — commit implementation:** Inspect/stage only the implementation step and commit `feat: qualify network node reads by owning interface`.

### Task 4: Carry prepared selectors through configuration and immediate checks

**Files:** Modify `TiaMcpServer/Network/NetworkIdentityResolver.cs`, `NetworkToolResponses.cs`, `NetworkWritePlanner.cs`, `NetworkWriteDomain.cs`, `NetworkWorkerInvoker.cs`, `NetworkPayloadContract.cs`, `TiaMcpServer/Worker/OpennessWorkerClient.cs`, `TiaMcpServer.Contracts/NetworkInterfacePathSegmentInfo.cs`, `TiaMcpServer.Contracts/WorkerRequest.cs` only for necessary selector documentation, worker `Program.cs`, `Openness/NetworkDeviceConfigurator.cs`, `Openness/NetworkMutationVerifier.cs`, FakeWorker `Program.cs`; test in `NetworkIdentityResolverTests.cs`, `NetworkFieldForwardingTests.cs`, `NetworkMutationVerificationTests.cs`, `NetworkSparseConfigurationTests.cs`, `NetworkGuardedWriteOrderingTests.cs`, `NetworkOperationFakeWorkerTests.cs`, `Worker/OpennessWorkerClientIntegrationTests.cs`, conditional-member registers.

**Interfaces:** Consume Task 3 shared matcher/resolver. Extend `NetworkWriteTargetEvidence` with conditional `IReadOnlyList<NetworkInterfacePathSegmentInfo>? InterfacePath = null`. Preserve `NetworkIdentityResolver.Resolve(NetworkOperationRequest, HardwareConfigInfo?): NetworkIdentityResolution`. Add `NetworkIdentityResolver.BindPreparedTarget(NetworkOperationRequest item, NetworkWriteTargetEvidence target): NetworkOperationRequest` to make a detached dispatch request. Add client overload `ConfigureNetworkDeviceAsync(NetworkObjectSelectorInfo target, string? ipAddress, string? subnetMask, string? pnDeviceName, string? subnetId, string? ioSystemSubnetId, int? ioSystemNumber, string? projectPath): Task<WorkerCallResult>` and worker overload `NetworkDeviceConfigurator.Configure(Project project, NetworkObjectSelectorInfo target, string? ipAddress, string? subnetMask, string? pnDeviceName, string? subnetId, string? ioSystemSubnetId, int? ioSystemNumber): ConfigureNetworkDeviceResultInfo`. Existing bare signatures forward to the same resolver. `WorkerRequest.NetworkTarget` is the transport authority. In the shared path-contract file add `NetworkInterfacePathEncoding.Encode(IReadOnlyList<NetworkInterfacePathSegmentInfo>): string` and `Decode(string): IReadOnlyList<NetworkInterfacePathSegmentInfo>` for the scalar verification dictionary: deterministic camel-case typed JSON, strict required-member/unknown-field validation, no dependency on host-only `CanonicalJson`.

- [ ] **Step 1 — write regressions:** Add `ConfigureEachE1_ForwardsItsEntireSelector`, `ConfigureX1_DoesNotChangeX2`, `BareDuplicateE1_IsAmbiguousBeforeDispatch`, `PreparedLegacyTarget_DoesNotSwitchInterfaces`, `WorkerTopLevelIdentityConflict_IsRejected` and `QualifiedPartialSkip_RetainsAppliedIdentityAndStops`. Inspect IPC requests, not just result objects. At this task boundary, test qualified configuration through invoker/client and fresh FakeWorker reads; full guarded-runner final success for two E1 nodes belongs to Task 5. Assert repeated bare IDs refuse without mutation, X1/X2 round-trip selectors work, legacy strict paths retain supplied constraints, all subnet/IO dependencies preflight before any scalar setter, and sparse maps contain only requested keys.

```csharp
// ConfigureEachE1_ForwardsItsEntireSelector / BareDuplicateE1_IsAmbiguousBeforeDispatch
Assert.Equal(32768, request.NetworkTarget!.InterfacePath![1].PositionNumber);
Assert.Equal("192.168.13.20", untouchedX2.Address);
Assert.False(bareResolution.Success);
Assert.Empty(bareRequestMutations);
```

- [ ] **Step 2 — observe RED:** Run the focused command with filter `FullyQualifiedName~NetworkIdentityResolverTests|FullyQualifiedName~NetworkFieldForwardingTests|FullyQualifiedName~NetworkMutationVerificationTests|FullyQualifiedName~NetworkSparseConfigurationTests|FullyQualifiedName~NetworkGuardedWriteOrderingTests|FullyQualifiedName~NetworkOperationFakeWorkerTests|FullyQualifiedName~OpennessWorkerClientIntegrationTests`; require the named behavioral assertions to fail and retain the log/TRX.
- [ ] **Step 3 — commit RED:** Inspect/stage the regression paths and commit `test: pin qualified network configuration dispatch`.
- [ ] **Step 4 — implement:** Resolve the host snapshot using the shared path matcher; prove selected device/node/subnet/IO candidate identities independently of optional metadata. Store qualified target evidence. Normalize a successful bare request immediately, replan against that fixed owner and dispatch a detached prepared target; preserve original qualified/indexed constraints. Do not mutate caller input or re-resolve a legacy bare request into another interface. Forward the complete target through invoker/client, with agreeing legacy fields if retained. Worker dispatch validates agreement, then configurator and immediate verifier use the Task 3 resolver. Return qualified immediate identity and extend its exact-key payload validation together, so the current two-key device/node check does not reject the new evidence. Until Task 5, full final expectations remain conservatively unsupported for the duplicate-ID case. Retain internal worker confirmation/binding policy and preflight ordering; do not expose a public confirmation argument or add a prompt.
- [ ] **Step 5 — observe GREEN:** Run the same filter and existing guarded MCP/domain tests. Require zero mutation dispatch for wrong constraints and late owner changes, and truthful partial results after a requested skip.
- [ ] **Step 6 — commit implementation:** Inspect/stage only the implementation step and commit `fix: configure network nodes with prepared interface selectors`.

### Task 5: Preserve qualified affected nodes and final expectations

**Files:** Create `TiaMcpServer.Contracts/NetworkNodeIdentityComparer.cs`, `TiaMcpServer.Tests/Network/NetworkQualifiedNodeIdentityTests.cs`; modify worker `Openness/HardwareConfigReader.cs`, `Openness/NetworkConnectionEvidenceCapture.cs`, `Openness/NetworkMutationVerifier.cs`; modify host `Network/NetworkWritePlanner.cs`, `NetworkWriteVerifier.cs`, `NetworkPayloadContract.cs` and FakeWorker `Program.cs`. Test in `NetworkConnectionEvidenceTests.cs`, `NetworkMutationVerificationTests.cs`, `NetworkGuardedWriteDomainTests.cs`, `NetworkGuardedWriteOrderingTests.cs`, `NetworkSubnetLifecyclePayloadContractTests.cs` and the new identity tests.

**Interfaces:** Produce `NetworkNodeIdentityComparer.Instance: IEqualityComparer<NetworkNodeIdentityInfo>`, comparing device name ordinal-ignore-case, ordered owner name/position pairs ordinally, and node ID ordinally; optional type/interface-name constraints do not define equality. Worker `HardwareConfigReader.ReadConnectedNodeIdentity(Node): NetworkNodeIdentityInfo` returns qualified actual-owner evidence. Keep `NetworkMutationVerifier.CaptureAffectedNodes(Subnet): IReadOnlyList<NetworkNodeIdentityInfo>` and `NetworkWriteVerifier.VerifyAsync` signatures; extend their identity semantics. Consume Task 4's `NetworkInterfacePathEncoding.Encode/Decode` rather than adding another encoding or a Contracts dependency on host-only `CanonicalJson`.

- [ ] **Step 1 — write regressions:** Add `BothE1Expectations_SurviveEitherConfigurationOrder`, `SlashAndEscapedNames_DoNotCollide`, `DeviceCaseAndPathCase_FollowDeclaredRules`, `ConnectedDeletion_PreservesBothQualifiedNodes`, `LegacyAffectedIdentity_RequiresUniqueFreshUpgrade`, `OwnerHierarchy_MustResolveBackToActualNode`, and `FinalOptionalDiagnostic_DoesNotFailVerifiedWrite`. Assert two keys in sets/maps, each step preserves the other node, two affected identities remain in effects/guards, post-delete qualified nodes survive with required subnet/IO relationships removed, and the root-device count invariant remains independent.

```csharp
// BothE1Expectations_SurviveEitherConfigurationOrder / ConnectedDeletion_PreservesBothQualifiedNodes
Assert.Equal(2, new HashSet<NetworkNodeIdentityInfo>(affectedNodes, NetworkNodeIdentityComparer.Instance).Count);
Assert.True(finalVerification.Success);
Assert.Equal(baseline.RootDeviceCount, finalState.RootDeviceCount);
```

- [ ] **Step 2 — observe RED:** Run the focused command with filter `FullyQualifiedName~NetworkQualifiedNodeIdentityTests|FullyQualifiedName~NetworkConnectionEvidenceTests|FullyQualifiedName~NetworkMutationVerificationTests|FullyQualifiedName~NetworkGuardedWriteDomainTests|FullyQualifiedName~NetworkGuardedWriteOrderingTests|FullyQualifiedName~NetworkSubnetLifecyclePayloadContractTests`; require the named behavioral assertions to fail and retain the log/TRX.
- [ ] **Step 3 — commit RED:** Inspect/stage the regression paths and commit `test: reproduce interface scope loss in network verification`.
- [ ] **Step 4 — implement:** Walk the actual Node parent/service hierarchy to capture the owner path and prove resolution returns that node; unknown/ambiguous hierarchy produces incomplete relationship evidence. Carry qualified identity through initial/replanned effects, detached copies, affected inventories and immediate/final checks. Use typed comparer keys rather than device/node or slash-joined strings. Retain Task 4's canonical typed path encoding within scalar verification identity dictionaries, with strict decoding/comparison and `deviceName`, `nodeId` and optional interface evidence. Upgrade a legacy affected identity only from a fresh complete ordinary read with exactly one match; otherwise fire `network_state_unverifiable`. Final verification uses the Task 2 structural gate and Task 3 qualified selection, with required applied fields/relationships checked independently of unrelated diagnostics.
- [ ] **Step 5 — observe GREEN:** Run the same filter plus all subnet lifecycle payload/forwarding regressions. Inject post-mutation missing evidence, unreadable identity and binding degradation: retain completed/applied evidence, mark required verification failed or unverifiable, skip later operations where applicable and never replay. Confirm unknown consequences block both writable modes and previews expose the block when the target is resolved.
- [ ] **Step 6 — commit implementation:** Inspect/stage only the implementation step and commit `fix: retain interface identity through network postconditions`.

### Task 6: Admit and retain qualified recovery evidence before mutation

**Files:** Modify `TiaMcpServer/Network/NetworkWritePayloadBudget.cs`, `NetworkWriteDomain.cs`, `NetworkPayloadContract.cs`; test in `TiaMcpServer.Tests/Network/NetworkGuardedWriteBudgetTests.cs`, `NetworkGuardedWriteMcpTests.cs`, `NetworkStructuredProtocolTests.cs`, `NetworkPayloadContractTests.cs` and audit tests used by guarded Network calls.

**Interfaces:** Consume qualified `NetworkWriteEffect`/target/affected identities. Add `NetworkWritePayloadBudget.MeasurePreparedCore(IReadOnlyList<NetworkOperationRequest> items, IReadOnlyList<ItemPlan<NetworkWriteEffect>> plans): int`; it computes a conservative encoded recovery/verification-core reservation for all planned operations. `NetworkWriteDomain.PlanAsync` refuses an oversized reservation before any mutation. Keep existing `MeasureProtectedCore` input admission and `Apply` delivery budgeting.

- [ ] **Step 1 — write regressions:** Add `LongPreparedOwnerPaths_RefuseBeforeFirstMutation`, `QualifiedRecoveryCore_IsNeverFlattenedOrOmitted`, `MultipleAffectedE1Nodes_RemainDistinctUnderBudget`, `EscapedPaths_UseEncodedSize`, `LatePartialFailure_PreservesExactRecoveryCore` and `Audit_MatchesDeliveredQualifiedDocument`. Exercise long names, escaped strings, up to 50 IDs at the existing 256-character limit and many connected nodes. Assert limits remain 60,000/180,000, oversized admission has zero mutation dispatch, one audit for an entered denial, and exact canonical text/structured/audit equality.

```csharp
// LongPreparedOwnerPaths_RefuseBeforeFirstMutation / Audit_MatchesDeliveredQualifiedDocument
Assert.Empty(oversizedCallMutations);
Assert.InRange(deliveredText.Length, 0, 180000);
Assert.Single(enteredCallAuditRecords);
Assert.Equal(deliveredText, enteredCallAuditRecords[0].ResponseText);
```

- [ ] **Step 2 — observe RED:** Run the focused command with filter `FullyQualifiedName~NetworkGuardedWriteBudgetTests|FullyQualifiedName~NetworkGuardedWriteMcpTests|FullyQualifiedName~NetworkStructuredProtocolTests|FullyQualifiedName~NetworkPayloadContractTests`; require the named behavioral assertions to fail and retain the log/TRX.
- [ ] **Step 3 — commit RED:** Inspect/stage the regression paths and commit `test: pin qualified network recovery budget admission`.
- [ ] **Step 4 — implement:** Reserve the discriminator-bearing target/affected/verification core after preparation, accounting for encoded strings and repeated presentation copies. Keep initial target identities fixed during replanning; if required late evidence no longer fits, stop before that item's mutation and preserve earlier outcomes. Retain qualified owner paths wherever recovery depends on them; diagnostics may be omitted only through existing declared omission semantics. Do not execute a call whose known required recovery core cannot be delivered, reduce the operation-ID limit, or trim individual path segments.
- [ ] **Step 5 — observe GREEN:** Run the same filter and audit regression suite; require no elicitation/acknowledge additions and truthful execution-versus-delivery success.
- [ ] **Step 6 — commit implementation:** Inspect/stage only the implementation step and commit `fix: reserve qualified network recovery evidence before writes`.

### Task 7: Migrate public harness identity consumers and documentation

**Files:** Modify `scripts/network-live-mcp-helpers.ps1`, `scripts/live-test-network-guarded-write.ps1`, `scripts/live-test-network-phase4-subnets.ps1`, `TiaMcpServer.Tests/Network/NetworkGuardedWriteLiveHarnessScriptTests.cs`, `NetworkSubnetLifecycleLiveHarnessScriptTests.cs`; modify `docs/ARCHITECTURE.md`, `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md`, `docs/roadmap/network-operations.md`, `docs/roadmap/json-contract.md`, `docs/development/local-mcp-testing.md`, `docs/guides/mcp-client-configuration.md`, `docs/IMPROVEMENT_LOG.md`, `AGENTS.md` only where the repair changes current contracts, and both documentation indices.

**Interfaces:** Preserve harness modes and existing mutation/hash/target gates. Helpers consume selectors and qualified affected identities from the public contract; identity maps use device/owner path/node tuples. Fixture requests forward read selectors directly. Harnesses never reinterpret duplicate bare IDs as invalid hardware, infer owner indices from sorted output, or synthesize project evidence from pagination.

- [ ] **Step 1 — write regressions:** Add executable helper tests `QualifiedInventory_KeepsBothE1Nodes`, `InspectionAndRestoration_UseOriginalOwnerSelectors`, `MalformedQualifiedIdentity_IsRejected`, `UnknownTraversalEvidence_BlocksApply`, and `FixtureHashChange_StopsBeforeToolCall`. Use isolated JSON/MCP captures, including raw two-E1 metadata, malformed canonical path encoding and exact post-restoration comparisons; retain existing historical malformed-document tests. Parse PowerShell AST without launching TIA.

```csharp
// QualifiedInventory_KeepsBothE1Nodes / InspectionAndRestoration_UseOriginalOwnerSelectors
Assert.Equal(0, helperExitCode);
Assert.Equal(2, qualifiedNodes.GetArrayLength());
Assert.NotEqual(x1OwnerPathJson, x2OwnerPathJson);
Assert.Equal(originalOwnerSelectorJson, restorationTargetJson);
```

- [ ] **Step 2 — observe RED:** Run the focused command with filter `FullyQualifiedName~NetworkGuardedWriteLiveHarnessScriptTests|FullyQualifiedName~NetworkSubnetLifecycleLiveHarnessScriptTests`; require the named behavioral assertions to fail and retain the log/TRX.
- [ ] **Step 3 — commit RED:** Inspect/stage the regression paths and commit `test: pin qualified network acceptance identities`.
- [ ] **Step 4 — implement:** Extend helpers and fixture assertions for qualified identities and fresh ordinary-read evidence. Keep public Inventory/Preview/Apply/Restore authorization boundaries, matching source/helper/fixture hashes, actual-by-default tool semantics, zero server elicitation, and inspect-before-recovery behavior.
- [ ] **Step 5 — observe GREEN:** Run the same filter and PowerShell parser checks; require all pass.
- [ ] **Step 6 — commit harness implementation:** Inspect/stage the helper/entrypoint/test paths and commit `test: support interface qualified network acceptance`.
- [ ] **Step 7 — document:** Explain preferred `interfacePath`, the unchanged strict legacy form, bare compatibility/ambiguity, additive 1.0 schema updates, matching binary deployment, structural-versus-metadata evidence, unknown-consequence blocking and sparse partial recovery. Include separate X1/X2 examples using the spec's exact names/positions. Preserve historical reports and label live repair acceptance pending.
- [ ] **Step 8 — validate/commit documentation:** Check local links, examples/schema agreement and `git diff --check`; stage the documentation paths and commit `docs: document qualified network discovery and recovery`. Documentation-only edits do not rerun a completed expensive suite.

### Task 8: Review and qualify the frozen repair, then prepare the live gate

**Files:** Create `docs/superpowers/acceptance/reports/2026-10-04-network-discovery-and-interface-node-identity-repair-validation.md`; update this plan's status/checks and `docs/README.md`, `docs/superpowers/README.md`. Keep raw logs/TRX/Cobertura/package/fixture/hash evidence under ignored repair results and ledger paths. This task owns qualification, not unreviewed product edits.

**Interfaces:** Consume completed Tasks 1–7. Produce one recorded code candidate with full commit/tree IDs, matching source/build/package hashes, test/coverage evidence and a separate, initially pending live section. Reuse existing coverage threshold and package verification scripts without weakening exclusions or acceptance checks.

- [ ] **Step 1 — independent review:** Review the whole repair and its interaction with the original Network branch, including authoritative catch sites, helper integration, namespace-readability checks, prepared identity pinning, legacy compatibility, both-E1 maps, guards, budgets and canonical/audit seams. Resolve Critical/Important findings in separate RED/GREEN/fix commits with scoped re-review, following the selected execution skill. Record any deferred Minor with its concrete limit. Freeze the resulting code/tree head before final qualification; source changes afterward invalidate affected evidence.
- [ ] **Step 2 — offline qualification:** Acquire the shared lock for every process. Restore only when necessary, serially build the Debug solution for any selected IPC/FakeWorker locators, build Release with stubs, run one full Release suite with coverage, and require zero failures/skips. Commands below run through the existing wrapper with unique retained log/results paths. Verify exactly the newly generated coverage artifact; do not select an older report. Require overall line rate at least `0.80` and report materially changed host/Contracts logic separately against the project's 80% expectation. Existing coverage excludes the worker; report executable helper test coverage/evidence separately without treating installed-reference compilation as runtime proof.

```powershell
$repairCandidate = git rev-parse HEAD
$repairStamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$repairResults = "TestResults/network-discovery-repair/final-$repairCandidate-$repairStamp"
dotnet build TiaMcpServer.slnx --configuration Debug --no-restore -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet build TiaMcpServer.slnx --configuration Release --no-restore -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --configuration Release --no-build --no-restore -m:1 --collect:'XPlat Code Coverage' --settings TiaMcpServer.Tests/coverage.runsettings --results-directory $repairResults --logger 'trx;LogFileName=final.trx' -- xUnit.ParallelizeTestCollections=false xUnit.ParallelizeAssembly=false xUnit.MaxParallelThreads=1 RunConfiguration.MaxCpuCount=1
$repairCoverage = @(Get-ChildItem -LiteralPath $repairResults -Recurse -Filter coverage.cobertura.xml)
if ($repairCoverage.Count -ne 1) { throw 'Expected exactly one fresh coverage artifact.' }
./scripts/verify-coverage-threshold.ps1 -CoveragePath $repairCoverage[0].FullName -MinimumLineRate 0.80
```

- [ ] **Step 3 — installed-reference/package qualification:** Under the same serialized process policy, build Release against `C:/Program Files/Siemens/Automation/Portal V21/PublicAPI/V21/net48` with `UseTiaPortalReferenceStubs=false`, then pack the host from that build. Verify the exact resulting package with `scripts/verify-doctor-package.ps1 -PackagePath '<exact newly produced nupkg>'`; verify required worker files, no Siemens DLLs, package source commit/tree and host/Contracts/worker SHA256 agreement across build/copy/package paths. Do not launch doctor/worker/TIA or install the tool for this offline gate. Preserve existing original qualification artifacts.

```powershell
$repairArtifacts = "artifacts/network-discovery-repair-$repairCandidate-$repairStamp"
dotnet build TiaMcpServer.slnx --configuration Release --no-restore -m:1 /p:UseTiaPortalReferenceStubs=false /p:TiaPortalV21Dir='C:/Program Files/Siemens/Automation/Portal V21/PublicAPI/V21/net48'
dotnet pack TiaMcpServer/TiaMcpServer.csproj --configuration Release --no-build --no-restore -m:1 /p:UseTiaPortalReferenceStubs=false /p:RepositoryCommit=$repairCandidate -o $repairArtifacts
$repairPackages = @(Get-ChildItem -LiteralPath $repairArtifacts -Filter '*.nupkg')
if ($repairPackages.Count -ne 1) { throw 'Expected exactly one fresh repair package.' }
./scripts/verify-doctor-package.ps1 -PackagePath $repairPackages[0].FullName
```
- [ ] **Step 4 — report/commit:** Record actual commands, test counts, coverage exclusions/changed-logic metrics, candidate/package provenance, review disposition and explicit offline/live limitations. Add the report to both indices. Validate links/whitespace and commit `docs: record network discovery repair qualification`; distinguish this documentation commit from the qualified code/package head. Do not rerun full suites solely because this report was committed.
- [ ] **Step 5 — prepare concrete live fixtures:** At the frozen candidate, prepare exact qualified read/inspect/configure requests for X1 and X2, all five writes, connected deletion involving either/both E1 nodes where the disposable fixture permits it, sparse requested skips, ordered stop, explicit restoration requests and fresh baseline comparisons. Inventory/Preview must prove fixture capabilities first; if a live partial-failure case is unavailable, report it unverified rather than force an arbitrary hardware failure. Record source/helper/fixture/package hashes and expected exclusions. No live gate starts from plan writing or an unfrozen candidate.
- [ ] **Step 6 — later authorized live acceptance:** After the corrected matching binaries are installed and the exact disposable target/scope is authorized for this candidate, freshly verify its full path, Portal/project binding and installed host/Contracts/worker versions/hashes. Preserve the earlier authorization as session context but do not apply it blindly to a changed project or unverified deployment. Use only public MCP tools against the verified disposable target. Test read-only write denial and writable-mode no-elicitation behavior; separately verify previews and all five actual writes. Configure X1 then X2 and inspect both after each step; demonstrate ambiguity refusal for the bare pair. Inspect actual state after any failed/unknown mutation before planning restoration or retry. Restore exact original node values/connections, subnet settings and created objects using approved explicit operations; verify both qualified E1 nodes and inventory/relationships against a fresh baseline. No save/close/TIA compile/download/PLC action or automatic retry/rollback. Preserve captures and one-audit-per-entered-call/text-structured-hash comparisons; commit the honest live report separately only after the gate is actually run.

## Handoff and completion criteria

Written plan review is the next gate. Preserve the user's previously selected subagent-driven execution method; plan approval authorizes Tasks 1–8's implementation/offline work, while corrected installation and exact-target live acceptance remain a separate concrete gate. The coordinator continues to supervise Network/Multiuser serialization and step commits.

Implementation is ready for integration only when structural failures still refuse writes, optional metadata no longer globally vetoes them, qualified selectors round-trip, both E1 identities survive every verification/recovery seam, prepared identities cannot retarget, budget/audit contracts hold, required reviews pass and the repaired candidate has fresh offline qualification. Report live acceptance separately and accurately. Remote publication remains subject to explicit user authorization.

Self-review coverage: spec §§1–3 → constraints/Tasks 2–5; §4 → Tasks 1–2; §5 → Tasks 1, 3–4; §6 → Tasks 4–5; §7 → Tasks 1, 5–7; §8 → task RED/GREEN gates and Task 8; §9 → scope/ownership/handoff. All five Review Focus inputs have named regression tests above.
