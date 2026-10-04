# PLC Read and Standalone Cross-References (PR A) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `execute_read_batch` with the structured `plc_read` and `read_cross_references` tools, and retire the legacy read-batch path.

**Architecture:** A new host `TiaMcpServer/Plc/` domain mirrors `network_read`: strict request DTO, catalog, typed payload contract, invoker, structured batch envelope. `read_cross_references` is a standalone structured tool over a rewritten worker reader that resolves a project-tree selector (plus a `member` leaf) to one owner or a fan-out of owners. Worker methods keep their names; payloads move off legacy null omission.

**Tech Stack:** .NET 10 host/tests/FakeWorker, net48 Openness worker, netstandard2.0 contracts, ModelContextProtocol C# SDK, xUnit, PowerShell 7 (Windows PowerShell 5.1 for the spike). No new dependencies.

**Spec:** [PLC read/write and cross-references design](../specs/2026-10-05-plc-read-write-and-cross-references-design.md), §3, §5 and §6.1. Read it with this plan. Baseline: `main` at `430a454`; branch `feature/plc-read-and-cross-references`.

## Global Constraints

- PR A only. `preview_write_batch`/`apply_write_batch` keep working unchanged on their token flow. No `plc_write` code.
- Tool counts after PR A: read-only 6, read-write 16, full 16.
- `plc_read(operations)`: at most 50 items, unique non-blank `operationId` of at most 256 characters, caller order, items run independently, registered in every mode, `ReadOnly=true, Destructive=false, OpenWorld=false, UseStructuredContent=true`.
- `plc_read` operations: `get_block_content` (`blockPath`, `format?`, `withDependencies?`), `get_type_content` (`typePath`, `format?`, `withDependencies?`), `list_tag_tables` (`plcName?`). Formats `xml`/`source`; blocks default to xml, types to source.
- `read_cross_references(target, filter?, maxResults?, projectPath?)`, registered in every mode. Filters `AllObjects`, `ObjectsWithReferences` (default), `ObjectsWithoutReferences`, `UnusedObjects`; `maxResults ≥ 1`.
- Both tools: `contractVersion "1.0"`, root `warnings` array, explicit nulls, one `CanonicalJson.Serialize` document as text and `structuredContent`, `isError:true` only for a rejection before anything ran.
- Budgets: 60,000 characters per value, 180,000 per document. Omit whole values; never cut text, XML or JSON.
- `contentHash` is `xml:sha256:<hex>`/`source:sha256:<hex>` over the served text; explicit `null` on `withDependencies`; no hash when the result is omitted.
- Reads never bind, switch or open. A per-item `projectPath` that differs from the binding fails only that item with `binding_conflict`.
- Keep policy entries: `list_tag_tables`, `read_cross_references` = `Observe`; `get_block_content`, `get_type_content` = `TemporaryExport`.
- `network_read` output must not change (it shares `TagTableReader` through `HardwareTagIndexResolver`).
- Siemens calls stay in the worker. Siemens assemblies are never committed.
- Commits: one short conventional line, no body or trailers. Serial builds and tests. 80% scoped coverage gate.
- No live TIA action without the maintainer's explicit authorization naming the target project.

## Review Focus

1. A selector copied verbatim from `browse_project_tree` output resolves to the same object, including software units, system-block folders and nested groups. Matching is case-insensitive as in the tree; siblings that differ only in case are `target_ambiguous`. Pinned by `CrossReferenceTargetResolverTests.ResolvesEverySelectorTheTreeEmits` and `CaseOnlySiblingsAreAmbiguous` (Task 4).
2. Two PLCs, one of them in a device group, and an omitted `plcName`: `list_tag_tables` returns both, each named. Today it returns the first PLC only and never sees grouped devices. Pinned by `TagInventoryReaderTests.OmittedPlcNameReadsEveryPlc` (Task 2).
3. An unreadable tag flag, constant value or table yields `null` or `isComplete:false` with a message. It never yields `""` or a silent drop. Pinned by `TagInventoryReaderTests.UnreadableMembersAreReportedNotDropped` (Task 2).
4. A 70,000-character block export omits that item whole, with no hash and with narrowing guidance; the other items survive. A huge sweep drops tail sources until the report value is at most 60,000 characters and the document at most 180,000. Pinned by `PlcReadBudgetTests.OversizedContentIsOmittedWhole` (Task 3) and `CrossReferenceBudgetTests.DropsTailSourcesToFitValueAndDocument` (Task 5).
5. In one `plc_read` call, an item with a mismatched `projectPath` fails `binding_conflict` and the others succeed. Pinned by `PlcReadToolsTests.MismatchedProjectPathFailsOnlyThatItem` (Task 3).

---

## Execution conventions and file boundaries

Inspect `AGENTS.md`, branch, head and status at start, and preserve unrelated work. Use PowerShell for every `dotnet` command that passes `/p:` flags; Bash mangles them on this machine.

```powershell
dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter '<filter>' -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1
```

`TiaMcpServer.Tests.csproj` links host files one by one. Every new host or worker file under test gets a `<Compile Include>` in the task that creates it.

```
TiaMcpServer/Plc/                       PlcOperationRequest, PlcOperationCatalog, PlcPayloadContract,
                                        PlcWorkerInvoker, PlcFormatNames, PlcContentHashes,
                                        PlcReadTools, PlcToolResponses, PlcContractVersion
TiaMcpServer/CrossReferences/           CrossReferenceTarget (host DTO), CrossReferenceReadTools,
                                        CrossReferencePayloadContract, CrossReferenceBudget
TiaMcpServer.Contracts/                 PlcTagInventoryInfo, CrossReferenceSelectorInfo,
                                        CrossReferenceMemberKinds, CrossReferenceAccessNames,
                                        CrossReferenceTypeNames (+ edits to existing payload types)
TiaMcpServer.OpennessWorker/Openness/   TagTableReader (extended), CrossReferenceTargetResolver (new),
                                        CrossReferenceReader (rewritten)
```

---

### Task 1: Cross-reference ownership spike (live, throwaway)

**Files:**
- Create (ignored): `.superpowers/spikes/xref-owner-probe.ps1`, `.superpowers/spikes/xref-owner-probe-<date>.json`
- Modify: spec, new "Appendix B. Ownership spike results"

**Interfaces:**
- Produces: the verified member kinds and container endpoints, consumed by Tasks 4 and 5 as the contents of `CrossReferenceMemberKinds.All` and the resolver's container list.

- [ ] Write a Windows PowerShell 5.1 script. It loads `Siemens.Engineering.dll` from `TiaPortalV21Dir`, attaches to the running TIA Portal holding the authorized project, and opens nothing, saves nothing and modifies nothing. For one instance of each spec §5.4 kind, it records `{kind, path, serviceAvailable, querySucceeded, sourceCount, exceptionType}`.
- [ ] Ask the maintainer for authorization naming the project, then run the script. Expected: one JSON row per kind; an instance missing from the project is recorded as `notPresent`.
- [ ] Append Appendix B to the spec: a table of kind → verified/unverified/notPresent, plus the decided `CrossReferenceMemberKinds.All` and container endpoints. `Tag` and `SystemConstant` stay members even if the probe instance is missing, because they are already verified by code.
- [ ] Commit: `docs: record cross-reference ownership spike results`

Tasks 2–3 do not depend on this task and may run first. Tasks 4–5 wait for it.

### Task 2: Tag inventory for `list_tag_tables`

**Files:**
- Create: `TiaMcpServer.Contracts/PlcTagInventoryInfo.cs`
- Modify: `TiaMcpServer.Contracts/TagInfo.cs`, `UserConstantInfo.cs`, `TagTableInfo.cs`; `TiaMcpServer.OpennessWorker/Openness/TagTableReader.cs`, `Openness/PlcSoftwareLocator.cs`; worker `Program.cs` (`list_tag_tables` handler); `TiaMcpServer.Tests.csproj` (link `TagTableReader.cs`, which is not linked today)
- Test: `TiaMcpServer.Tests/Block/TagInventoryReaderTests.cs` (new), `TestUtilities/TagSafetySiemensDoubles.cs` (add `PlcTagTable.IsDefault`, and throw hooks on tag flags and `PlcUserConstant.Value`), `Worker/WorkerPayloadNullPolicyRegisterTests.cs`. No new conditional members, so `Json/ConditionalMemberRegisterTests.cs` is unchanged unless one is added.

**Interfaces:**
- Produces: `PlcTagInventoryInfo { bool IsComplete; List<string> Messages; List<PlcTagInventoryPlcInfo> Plcs }`, and `PlcTagInventoryPlcInfo { string PlcName; string? DeviceName; List<TagTableInfo> Tables }`.
- `TagInfo` gains `bool? ExternalAccessible`, `ExternalVisible`, `ExternalWritable` (`null` = unreadable or not supported).
- `UserConstantInfo.Value` becomes `string?` (`null` = unreadable).
- `PlcSoftwareLocator.FindEveryPlc(Project project, string? plcName) -> IEnumerable<DiscoveredPlcSoftware>`. It enumerates devices through `ProjectDeviceEnumerator.Enumerate` (root, grouped and ungrouped, as `browse_project_tree` does) plus `FindInDevice`, with the same name matching as `FindAll`. `FindAll` stays unchanged for its current callers. Log an IMPROVEMENT_LOG follow-up in Task 8 for Network's grouped-device gap.
- `TagTableReader.ReadInventory(Project project, string? plcName) -> PlcTagInventoryInfo` uses `FindEveryPlc`.
- `TagTableReader.ReadAll(PlcSoftware)` is unchanged for `HardwareTagIndexResolver`: it reads no flags and does no completeness accounting.

- [ ] Write failing tests:
  - `OmittedPlcNameReadsEveryPlc`: one PLC at the root and one in a device group.
  - `PlcNameFiltersToMatchingPlcs`: matches the software name or the device name.
  - `TagsCarryExternalAccessFlags`
  - `UnreadableMembersAreReportedNotDropped`: a throwing table, tag or constant value gives `isComplete:false`, one message each, and `null` values.
  - `NoMatchingPlcFails`: `WorkerOperationFailed`.
  - `InventoryRootWritesExplicitNulls`
- [ ] Run them. Expected: FAIL (types missing).
- [ ] Implement the inventory types and `ReadInventory`, then switch the worker handler to `Success(TagTableReader.ReadInventory(project, request.PlcName))`. Remove `[LegacyNullOmission]` from `TagTableInfo` and update the null-policy register.
- [ ] Run the new tests plus `IoTagIndexTests`, `HardwareDeviceSelectionTests`, `NetworkIoMap*` and `NetworkStructuredProtocolTests`. Expected: PASS, with Network output unchanged.
- [ ] Commit: `feat: add complete multi-PLC tag inventory read`

### Task 3: `plc_read` tool

**Files:**
- Create: the `TiaMcpServer/Plc/` files listed above, except write-only concerns
- Modify: `TiaMcpServer/Batch/BatchWorkerInvoker.cs` (call `PlcFormatNames.Normalize`), `Tools/McpToolRegistration.cs`, `TiaMcpServer.Tests.csproj`, `TiaMcpServer.FakeWorker/Program.cs` (scenario `plc-read-roundtrip`)
- Test: `Plc/PlcOperationCatalogTests.cs`, `Plc/PlcPayloadContractTests.cs`, `Plc/PlcReadToolsTests.cs`, `Plc/PlcReadBudgetTests.cs`; count, discovery and conformance tests (§ below)

**Interfaces:**
- `PlcOperationRequest : IOperationBatchItem` with `[JsonUnmappedMemberHandling(Disallow)]` and members `OperationId`, `Operation`, `ProjectPath`, `BlockPath`, `TypePath`, `Format`, `WithDependencies`, `PlcName`. PR B adds write members.
- `PlcOperationCatalog.ValidateRead(IReadOnlyList<PlcOperationRequest>?) -> (bool IsValid, string Error)`, plus `ValidateAccessMode(ops, McpAccessMode) -> IReadOnlyList<string>`. Constants: `MaxBatchSize = 50`, `MaxOperationIdLength = 256`.
- `PlcFormatNames.Normalize(string operation, string? format) -> string`: the logic moves from `BatchWorkerInvoker.NormalizeFormat(BatchOperationRequest)`, with the same errors. That method stays as a one-line delegate, because the write arms still use it.
- `PlcWorkerInvoker.InvokeReadAsync(OpennessWorkerClient, PlcOperationRequest, CancellationToken) -> Task<WorkerCallResult>`.
- `PlcPayloadContract.Project(PlcOperationRequest, WorkerCallResult) -> StructuredOperationItem`. The result types are:
  - content operations: `PlcContentResult(string Format, string Content, string? ContentHash, IReadOnlyList<string> Warnings)`, built from the raw worker text;
  - `list_tag_tables`: `PlcTagInventoryInfo`, decoded through `CanonicalJson.DeserializeWorkerPayload`.
  - A decode failure is `protocol_error` and the payload is not echoed. A worker failure keeps its category.
- `PlcContentHashes.Compute(string format, string content) -> string`: moved `BatchContentHashes` logic over `ContentHashes`.
- `PlcReadResponse(string Tool, string ContractVersion, bool Success, StructuredOperationFailure? Error, IReadOnlyList<string> Warnings, StructuredOperationBatch? Batch)`. `PlcContractVersion.Current = "1.0"`.
- `PlcReadTools.PlcRead(OpennessWorkerClient workerClient, PlcOperationRequest[] operations) -> Task<CallToolResult>`. Same flow as `NetworkReadTools.NetworkRead`: validate → access → `StructuredOperationBatchExecutionEngine.ExecuteReadsAsync` → `StructuredOperationBatchPayloadBudget.Apply` with retry tool `plc_read` → `StructuredToolResult.Create`.

- [ ] Write failing tests:
  - Catalog: `RejectsMoreThanFiftyItems`, `RejectsDuplicateOrBlankOperationId`, `RejectsOperationIdOver256Chars`, `RejectsInapplicableField` (`typePath` on `get_block_content`), `RejectsUnknownMember`, `RejectsWriteOperationName`.
  - Payload: `ContentResultCarriesFormatAndHash`, `WithDependenciesHashIsNull`, `TagInventoryDecodesTyped`, `MalformedInventoryIsProtocolErrorWithoutEcho`, `WorkerFailureKeepsCategory`.
  - Tool: `FailedItemDoesNotStopLaterItems`, `MismatchedProjectPathFailsOnlyThatItem`, `ValidationFailureIsErrorEnvelope` (`isError:true`, `batch:null`), `TextEqualsStructuredContent`.
  - Budget: `OversizedContentIsOmittedWhole` (70,000 chars: `omitted`, `result:null`, guidance names `plc_read`).
- [ ] Run them. Expected: FAIL.
- [ ] Implement the `Plc/` read side and register `.WithTools<PlcReadTools>()` unconditionally in `McpToolRegistration`.
- [ ] Update `AccessModeDiscoveryTests` (Reads gains `plc_read`; counts 6/16/16), `ReadOnlyModeTests` name lists and reflection, `McpToolSchemaTests`, `WriteToolMcpAnnotationProtocolTests`, and `StandaloneToolsProtocolTests` counts. In `ToolOutputContractConformanceTests`, add `plc_read` success (`plc-read-roundtrip`) and rejection probes.
- [ ] Run the `Plc` and `Tools` filters plus `ReadOnlyMode`. Expected: PASS.
- [ ] Commit: `feat: add structured plc_read tool`

### Task 4: Cross-reference worker target resolution and reader

**Files:**
- Create: `TiaMcpServer.Contracts/CrossReferenceSelectorInfo.cs` (with `CrossReferenceMemberSelectorInfo`), `CrossReferenceMemberKinds.cs`, `CrossReferenceAccessNames.cs`, `CrossReferenceTypeNames.cs`; worker `Openness/CrossReferenceTargetResolver.cs`
- Modify:
  - Contracts: `CrossReferenceReport.cs`, `CrossReferenceLocationInfo.cs` (delete `PlcCrossReferenceInfo.cs`); `WorkerRequest.cs` (add `CrossReferenceSelector`; doc comments).
  - Worker: `CrossReferenceReader.cs`, the `Program.cs` handler.
  - Host: `Worker/OpennessWorkerClient.cs` (`ReadCrossReferencesAsync`). Also `Batch/BatchWorkerInvoker.cs` and `Batch/BatchOperationCatalog.cs`: drop the batch `read_cross_references` arm and spec now, because the old signature is gone and `execute_read_batch` keeps only its three content and tag reads until Task 6.
  - `reference-stubs/Siemens.Engineering.Base/CrossReference.cs`; the FakeWorker phase probe; `Batch/ProjectTreeSafetyDedupTests.cs`.
- Test: `Block/CrossReferenceReaderTests.cs` (rewritten), `Block/CrossReferenceTargetResolverTests.cs` (new), `Block/CrossReferenceInfoTests.cs`, `Diagnostics/ReferenceStubSourceContractTests.cs`, `TestUtilities/TagSafetySiemensDoubles.cs` (`Location.ReferencedAs` becomes an engineering-object double; `Access`/`ReferenceType` become enums matching the stub), and the batch catalog tests that referenced the cross-reference spec.

**Interfaces:**
- Consumes: the Task 1 Appendix B kinds.
- Selector types:
  - `CrossReferenceSelectorInfo { List<ProjectTreeSelectorSegment> Path; CrossReferenceMemberSelectorInfo? Member }`
  - `CrossReferenceMemberSelectorInfo { string Kind; string Name }`
  - `CrossReferenceMemberKinds.All`: `Tag` and `SystemConstant`, plus the verified kinds.
- `CrossReferenceReport { CrossReferenceSelectorInfo Target; string Filter; bool IsComplete; int OwnerQueryCount; int SuccessfulOwnerQueryCount; List<string> Messages; List<CrossReferenceSourceInfo> Sources; int TotalSourceCount; int TotalReferenceCount; int TotalLocationCount; int OmittedSourceCount }`. No `[LegacyNullOmission]`. `OmittedSourceCount` is written by Task 5.
- `CrossReferenceLocationInfo.ReferencedAs` becomes `CrossReferenceObjectRefInfo? { string Name; string TypeName }`.
- `Access` and `ReferenceType` use closed names, taken from the installed V21 `Siemens.Engineering.Base.xml`/DLL metadata:
  - `CrossReferenceAccessNames.All`: Undefined, Read, Write, RW, Unknown, Definition, Declaration, Interface, Jump, Monitor, Modify, Force, Call, UC, CC, Multiinstance, InstanceDB, Open, Interlock, Supervision, Actions, Transition, ReadAndSymbol, WriteAndSymbol, ReadWriteAndSymbol, InstanceAndSymbol, MultiinstanceAndSymbol, ProDiagSupervision, DefaultValue, ArrayBoundary, StringLength, TypeAlarm, InstanceAlarm, Parameterinstance, ParameterinstanceAndSymbol, CreateReference, CreateReferenceAndSymbol.
  - `CrossReferenceTypeNames.All`: Uses, UsedBy, Undefined, TypeInstance, InstanceType, Assigns, MemberGroup, GroupMember, Defines, DefinedBy, OverlapsWith, Scope, Unknown.
  - Before committing, re-check both lists against the installed metadata.
- `CrossReferenceTargetResolver.Resolve(Project, CrossReferenceSelectorInfo) -> ResolvedCrossReferenceTarget` (leaf owner or container owner sequence). It throws worker failures `TargetNotFound`, `TargetAmbiguous`, or `TargetKindUnsupported` (leaf without the service). Selector meaning must equal the tree's: device segments match through `ProjectTreeDeviceSelector`; other segments match case-insensitively on the object's `Name`, as `ProjectTreeSnapshotWalker`/`ProjectTreeFilter` do. Siblings that differ only in case are ambiguous. PLC discovery uses `FindEveryPlc` (Task 2).
- `CrossReferenceReader.Read(Project, CrossReferenceSelectorInfo, string filter, int? maxResults) -> CrossReferenceReport`.
- `OpennessWorkerClient.ReadCrossReferencesAsync(string? projectPath, CrossReferenceSelectorInfo selector, string? filter, int? maxResults)`. The worker method name stays `read_cross_references`.

- [ ] Write failing tests:
  - Resolver: `ResolvesEverySelectorTheTreeEmits` (feed every node selector the walker produces over a doubles tree with units, system-block folders, nested groups and a grouped device back into the resolver), `CaseOnlySiblingsAreAmbiguous`, `MemberTagUnderTagTableResolves`, `MissingSegmentIsTargetNotFound`, `LeafWithoutServiceIsTargetKindUnsupported`.
  - Reader: keep the existing traversal, filter-forwarding, no-dedup, maxResults 0/1/2, partial-coverage and infrastructure-propagation theories, re-targeted at a `PlcSoftware` path. Add `LeafTargetQueriesOneOwner`, `TagTableContainerFansOutOverTags`, `EmptyContainerIsCompleteSuccess`, `OwnersExistButNoneSucceedFails`, `ReferencedAsMapsNameAndType`, `AccessAndReferenceTypeUseClosedNames`.
  - Stub contract: the stub enums declare every member in the closed name sets.
- [ ] Run them. Expected: FAIL.
- [ ] Implement it: contracts, stub enum members, resolver, reader rewrite, handler, and client signature. In `ProjectTreeSafetyDedupTests` and the FakeWorker tree-safety router, replace the `ReadCrossReferencesAsync(plcName:"preview"|"apply")` phase probe with `ListTagTablesAsync(plcName:"preview"|"apply")`. Update the null-policy register.
- [ ] Run the `CrossReference`, `ProjectTreeSafety` and `ReferenceStub` filters. Expected: PASS.
- [ ] Commit: `feat: resolve cross-reference targets from project-tree selectors`

### Task 5: `read_cross_references` tool

**Files:**
- Create: the `TiaMcpServer/CrossReferences/` files listed above
- Modify: `Tools/McpToolRegistration.cs`; `Tools/StructuredStandaloneResult.cs` (add the `read_cross_references`/`CrossReferenceReport` response arm and its retry guidance, because `Create` throws for unknown tool/payload pairs); csproj links; FakeWorker (scenarios `xref-roundtrip`, `xref-oversized`)
- Test: `CrossReferences/CrossReferenceReadToolsTests.cs`, `CrossReferences/CrossReferenceBudgetTests.cs`, `CrossReferences/CrossReferencePayloadContractTests.cs`; count, discovery and conformance tests

**Interfaces:**
- Consumes: Task 4 contracts and client method.
- `CrossReferenceTargetSelector`: host DTO, strict JSON, with `Description`s. It holds `ProjectTreeSelectorSegment[] Path` and `CrossReferenceMemberSelector? Member { Kind, Name }`, and is mapped one-to-one to `CrossReferenceSelectorInfo` (the host-to-worker selector boundary). It is named to stay distinct from the existing result item `CrossReferenceTargetInfo`.
- `CrossReferenceReadTools.ReadCrossReferences(OpennessWorkerClient workerClient, CrossReferenceTargetSelector target, string? filter = null, int? maxResults = null, string? projectPath = null) -> Task<CallToolResult>`, annotated `ReadOnly=true, UseStructuredContent=true, OutputSchemaType=typeof(ReadCrossReferencesResponse)`.
- `ReadCrossReferencesResponse(string ContractVersion, bool Success, StructuredOperationFailure? Error, IReadOnlyList<string> Warnings, StandaloneToolOutcome<CrossReferenceReport>? Result)` with `Tool => "read_cross_references"`. It is rendered through `StructuredStandaloneResult.Create`.
- Validation:
  - path via `ProjectTreeNodeTypes.Validate` → `invalid_selector`, with its `startSelector` wording rewritten to `target.path`;
  - unknown member kind, or a member whose path does not end at that kind's owner segment (`Tag`, `SystemConstant` → `TagTable`; spike kinds per Appendix B) → `invalid_selector`;
  - filter via `CrossReferenceFilterNames.TryNormalize` → `validation_error`;
  - `maxResults < 1` → `validation_error`.
- `CrossReferencePayloadContract.Decode(WorkerCallResult) -> CrossReferenceReport`. It uses `CanonicalJson.DeserializeWorkerPayload` and also rejects `access`/`referenceType` values outside the closed sets → `protocol_error`.
- `CrossReferenceBudget.Apply(CrossReferenceReport, int maxValueChars = 60_000, int maxDocumentChars = 180_000) -> CrossReferenceReport`. It drops whole tail sources until the report value fits `maxValueChars` and the composed document fits `maxDocumentChars`, so `StructuredStandaloneResult` never omits the whole report. It sets `OmittedSourceCount` and `IsComplete=false`, and adds the warning: "Narrow with maxResults, a narrower target path, or a member."
- The tool adds the warning "An incomplete UnusedObjects result is not proof that objects can be deleted." when `filter` is `UnusedObjects` and `IsComplete` is false.

- [ ] Write failing tests:
  - Tool: `ReturnsTypedReportForPlcSweep`, `InvalidPathIsInvalidSelectorRejection`, `UnknownFilterIsValidationError`, `MaxResultsZeroIsValidationError`, `TargetKindUnsupportedIsTypedFailureNotEmptySuccess`, `MismatchedProjectPathIsBindingConflict`, `TextEqualsStructuredContent`.
  - `IncompleteUnusedObjectsCarriesWarning`.
  - Budget: `DropsTailSourcesToFitValueAndDocument` (one case per limit), `FittingReportIsUnchanged`.
  - Payload: `UnknownAccessNameIsProtocolErrorWithoutEcho`.
- [ ] Run them. Expected: FAIL.
- [ ] Implement it and register `.WithTools<CrossReferenceReadTools>()` unconditionally. Update the counts to 7/17/17 and the name lists, and add the conformance success (`xref-roundtrip`), rejection, and omitted-sources (`xref-oversized`) probes.
- [ ] Run the `CrossReferences` and `Tools` filters plus `ReadOnlyMode`. Expected: PASS.
- [ ] Commit: `feat: add standalone read_cross_references tool`

### Task 6: Retire `execute_read_batch`

**Files:**
- Delete: `TiaMcpServer/Batch/ReadBatchTools.cs`, `Batch/BatchContentHashes.cs` (moved in Task 3)
- Modify:
  - `Batch/BatchTools.cs`: drop `ExecuteReadBatch`.
  - `Batch/BatchOperationCatalog.cs`: drop the remaining three read specs, `ValidateReadBatch`, `ReadOperationNames` and the `Read` category. A retired read name sent to a write batch gets the fixed rejection "'<op>' is a read operation; use plc_read or read_cross_references." (today's message names `execute_read_batch`).
  - `Batch/BatchOperationRequest.cs`: drop `Filter`, `MaxResults` and `WithDependencies`, which only reads used.
  - `Batch/BatchWorkerInvoker.cs`: drop the read arms.
  - `OperationBatches/OperationBatchExecutionEngine.cs`, `OperationBatchResultFormatter.cs`, `OperationBatchPayloadBudget.cs`: drop the read paths, keeping what apply uses.
  - `Tools/McpToolRegistration.cs`, csproj.
- Test: delete or trim the read-batch tests under `Batch/` and `OperationBatches/`. Update `ToolOutputContractConformanceTests` (remove the `execute_read_batch` register row), the discovery and count tests (final 6/16/16), `ReadOnlyModeTests` (the read-only batch access region now uses write-op rejection only), `McpToolSchemaTests`, `WriteToolMcpAnnotationProtocolTests`, `BatchToolsTests` and `BatchOperationRequestJsonTests`.

**Interfaces:**
- Consumes: nothing new. Produces: no remaining reference to `execute_read_batch` outside `docs/` and `docs/superpowers/`.

- [ ] Write the failing guards first:
  - `ToolOutputContractConformanceTests` with the register row removed fails while the tool is still registered.
  - `BatchOperationCatalogTests.ReadOperationInWriteBatchNamesNewTools` pins the fixed rejection.
- [ ] Delete and trim as listed, and check that the remaining write-batch tests reference no deleted member.
- [ ] Build with stubs, then run the full suite. Expected: build clean, all tests pass. `git grep -n "execute_read_batch" -- ':!docs'` shows nothing.
- [ ] Commit: `refactor: retire execute_read_batch`

### Task 7: Offline qualification and live acceptance

**Files:**
- Create: `docs/superpowers/acceptance/reports/2026-10-xx-plc-read-and-cross-references-validation.md`

- [ ] Run the stub build and the full suite serially. Run `scripts/verify-coverage-threshold.ps1` (≥80%) and `scripts/verify-reference-stubs.ps1`. If V21 is installed, also build against real assemblies with `/p:TiaPortalV21Dir=...`. Record the counts, coverage and head SHA.
- [ ] Request an independent whole-branch review, fix the findings, and re-run the affected tests.
- [ ] Install the candidate locally (`scripts/install-local-tool.ps1`). The installed plugin binary is separate from the repo build, so confirm the live tool list shows `plc_read` and `read_cross_references` and no `execute_read_batch`.
- [ ] With the maintainer's authorization naming the project, run the spec §6.1 step 6 matrix in read-only mode:
  - every `plc_read` operation, including one `contentHash` and one `withDependencies` null hash;
  - `list_tag_tables` over two PLCs, if the project has them;
  - cross-references for a leaf block, a `Tag` member, a `PlcSoftware` sweep, an unsupported target and an incomplete `UnusedObjects` result.
- [ ] Record the outcomes, any deviation and the frozen candidate SHA in the report. Commit: `docs: record PLC read and cross-reference validation`

### Task 8: Documentation (after live acceptance)

**Files:** `README.md` (absolute links only), `AGENTS.md`, `docs/ARCHITECTURE.md` (§7a tool list, batch read sections), `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md` (`plc_read`, `read_cross_references`, Appendix B unverified kinds, 60,000-character content ceiling), `docs/SupportedOperations/README.md`, `IMPORT_EXPORT_OPTIONS_SUMMARY.md`, `docs/development/local-mcp-testing.md`, `docs/development/building.md`, `docs/guides/installation.md`, `docs/guides/mcp-client-configuration.md`, `.github/ISSUE_TEMPLATE/01-bug_report.yml`, `docs/roadmap/json-contract.md` (Scope row), `docs/IMPROVEMENT_LOG.md` (`totally-integrated-claude` skill follow-up for the new tool names and counts), spec status, `docs/superpowers/README.md` (plan and report rows).

- [ ] Update every listed file to the PR A state: counts 6/16/16, with the batch write pair still documented as legacy.
- [ ] Run the doc-link check if present, and `git grep -n "execute_read_batch" -- ':!docs/superpowers'`. Expected: no hit.
- [ ] Commit: `docs: describe plc_read and read_cross_references`
