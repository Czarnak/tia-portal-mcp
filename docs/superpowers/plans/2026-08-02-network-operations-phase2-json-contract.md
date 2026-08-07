# Network Operations Phase 2 JSON Contract Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the Phase 2 single-layer JSON contract gate for `network_read` and `network_write`, with reusable opt-in canonical JSON/MCP response infrastructure, exact node/subnet/IO-system identity, and safe support for multi-homed devices.

**Architecture:** Keep the host/worker newline-delimited JSON boundary and all non-network public contracts unchanged. Add a reusable host-level canonical JSON service and structured MCP-result builder, then let Network opt into a shared structured operation-batch model through an operation-to-CLR-payload registry. Network writes bind canonical typed intent and typed hardware state, resolve exact Openness identities at preview and apply, and forward those identities to a worker that never falls back to the first interface, first node, or a presentation name.

**Tech Stack:** C#/.NET 8 host and tests, .NET Framework 4.8 Openness worker, `System.Text.Json`, ModelContextProtocol 1.2.0, xUnit 2.9.0, Siemens TIA Portal V21 Openness/stubs, PowerShell 7 acceptance harness.

## Global Constraints

- Work on the current branch. Do not create or switch branches or worktrees.
- Follow TDD for every behavior change: add the focused test, run it and record the expected failure, implement the smallest production change, then rerun it green.
- Preserve the two-process architecture. Siemens Openness code stays in `TiaMcpServer.OpennessWorker` and is reached only through `OpennessWorkerClient`.
- Keep the JSON gate reusable and opt-in. Phase 2 migrates only Network; generic batches, lifecycle tools, standalone tools, diagnostics, PLC, and HMI keep their current text contracts.
- Do not claim RFC 8785 compliance. This contract guarantees repository-defined deterministic JSON, not the RFC's complete I-JSON, ECMAScript-number, Unicode, and UTF-8 conformance surface.
- Preserve preview-before-apply, exact ordered-input binding, current-state binding, ten-minute expiry, single-use tokens, audit logging, sequential apply, stop-on-first-failure, and no rollback.
- A compile, stub build, FakeWorker run, or contract test is not evidence of live TIA behavior. Run the live harness only under the separate authorization gate in Task 8.
- The test project links host files explicitly. Add every new host source file to `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`.
- Build the solution serially with `-m:1`.

---

## File Map

### New reusable host infrastructure

- `TiaMcpServer/Json/CanonicalJson.cs` — strict typed parsing and repository-defined canonical serialization.
- `TiaMcpServer/Tools/StructuredToolResult.cs` — constructs one canonical text block plus the identical detached MCP `structuredContent`.
- `TiaMcpServer/OperationBatches/StructuredOperationBatch.cs` — shared item, failure, omission, count, truncation, and batch models.
- `TiaMcpServer/OperationBatches/StructuredOperationBatchExecutionEngine.cs` — read/write execution whose stop decision is based on the projected item contract, including `protocol_error`.
- `TiaMcpServer/OperationBatches/StructuredOperationBatchPayloadBudget.cs` — whole-value omission and canonical-document budgeting.
- `TiaMcpServer/Safety/CanonicalWriteSafety.cs` — typed preview data and canonical entry points used by opt-in tools.

### New Network-owned host infrastructure

- `TiaMcpServer/Network/NetworkPayloadContract.cs` — operation-to-result-type registry and strict worker-payload projection.
- `TiaMcpServer/Network/NetworkToolResponses.cs` — declared `network_read` and discriminated `network_write` output-schema types.
- `TiaMcpServer/Network/NetworkIdentityResolver.cs` — pure host-side resolution of node, subnet, and IO-system identities from `HardwareConfigInfo`.

### Existing production files changed

- `TiaMcpServer/Network/NetworkReadTools.cs`
- `TiaMcpServer/Network/NetworkWriteTools.cs`
- `TiaMcpServer/Network/NetworkOperationRequest.cs`
- `TiaMcpServer/Network/NetworkOperationCatalog.cs`
- `TiaMcpServer/Network/NetworkWorkerInvoker.cs`
- `TiaMcpServer/Network/NetworkSafetySnapshot.cs`
- `TiaMcpServer/Safety/WriteSafetyService.cs`
- `TiaMcpServer/Worker/WorkerCallResult.cs`
- `TiaMcpServer.Contracts/WorkerFailureCategories.cs`
- `TiaMcpServer.Contracts/WorkerRequest.cs`
- `TiaMcpServer.Contracts/HardwareConfigInfo.cs`
- `TiaMcpServer.Contracts/DeviceItemInfo.cs`
- `TiaMcpServer.Contracts/NodeInfo.cs`
- `TiaMcpServer.Contracts/SubnetInfo.cs`
- `TiaMcpServer.Contracts/IoSystemInfo.cs`
- `TiaMcpServer/Worker/OpennessWorkerClient.cs`
- `TiaMcpServer.OpennessWorker/Program.cs`
- `TiaMcpServer.OpennessWorker/Openness/HardwareConfigReader.cs`
- `TiaMcpServer.OpennessWorker/Openness/NetworkDeviceConfigurator.cs`
- `TiaMcpServer.FakeWorker/Program.cs`
- `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`

### Tests and documentation added or changed

- `TiaMcpServer.Tests/McpProtocolTestHarness.cs`
- `TiaMcpServer.Tests/NetworkStructuredProtocolTests.cs`
- `TiaMcpServer.Tests/CanonicalJsonTests.cs`
- `TiaMcpServer.Tests/StructuredOperationBatchTests.cs`
- `TiaMcpServer.Tests/StructuredOperationBatchPayloadBudgetTests.cs`
- `TiaMcpServer.Tests/NetworkPayloadContractTests.cs`
- `TiaMcpServer.Tests/NetworkIdentityResolverTests.cs`
- `TiaMcpServer.Tests/NetworkLiveHarnessContractTests.cs`
- Existing Network, batch, safety, schema, forwarding, access-mode, and FakeWorker tests listed in the tasks below.
- `scripts/live-test-network-phase2.ps1`
- `README.md`
- `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md`
- `docs/NETWORK_OPERATIONS_ROADMAP.md`
- `docs/ARCHITECTURE.md`
- `AGENTS.md`

## Public Types Locked by the Plan

The implementation may use classes or records according to repository style, but these logical members and JSON names are fixed:

```csharp
public sealed record StructuredOperationFailure(string Category, string Message);

public sealed record StructuredOperationOmission(
    string Reason,
    int? LimitChars,
    int OriginalChars,
    string RetryTool,
    string Guidance);

public sealed record StructuredOperationItem(
    string OperationId,
    string Operation,
    string Status,
    JsonElement? Result,
    StructuredOperationFailure? Failure,
    StructuredOperationOmission? Omission,
    string? SkipReason,
    IReadOnlyList<string> Warnings);

public sealed record StructuredOperationCounts(
    int Succeeded,
    int Failed,
    int Omitted,
    int Skipped);

public sealed record StructuredBatchTruncation(
    bool Truncated,
    int OriginalChars,
    int PresentedChars,
    int OmittedResultCount,
    int OmittedWarningCount,
    IReadOnlyList<string> AffectedOperationIds);

public sealed record StructuredOperationBatch(
    int OperationCount,
    StructuredOperationCounts Counts,
    IReadOnlyList<StructuredOperationItem> Operations,
    StructuredBatchTruncation? Truncation);

public sealed record NetworkToolError(string Category, string Message);

public sealed record NetworkWriteTargetEvidence(
    string OperationId,
    string Operation,
    string DeviceName,
    string? DeviceTypeIdentifier,
    IReadOnlyList<string> DeviceItemPath,
    string? NetworkInterfaceName,
    string? NodeName,
    string? NodeId,
    string? SubnetName,
    string? SubnetId,
    string? IoSystemName,
    int? IoSystemNumber);

public sealed record NetworkWritePreview(
    IReadOnlyList<NetworkWriteTargetEvidence> Target,
    string Summary,
    string CurrentStateHash,
    string RequestedInputHash,
    DateTimeOffset ExpiresAtUtc,
    string SafetyToken,
    JsonElement? Diff,
    string Instructions);

public sealed record CanonicalWritePreview<TTarget>(
    TTarget Target,
    string Summary,
    string CurrentStateHash,
    string RequestedInputHash,
    DateTimeOffset ExpiresAtUtc,
    string SafetyToken,
    JsonElement? Diff,
    string Instructions);

public sealed record NetworkReadResponse(
    string Tool,
    bool Success,
    StructuredOperationBatch? Batch,
    NetworkToolError? Error);

public sealed record NetworkWriteResponse(
    string Tool,
    string Phase,
    bool Success,
    NetworkWritePreview? Preview,
    StructuredOperationBatch? Batch,
    NetworkToolError? Error);
```

`StructuredOperationItem.Result` is always a detached `JsonElement` when present. Status strings are exactly `succeeded`, `failed`, `omitted`, and `skipped`; the only Phase 2 skip reason is `earlierOperationFailed`.

---

### Task 1: Prove the Phase 1 protocol defect and deliver the first reusable structured slice

**Files:**

- Create: `TiaMcpServer.Tests/McpProtocolTestHarness.cs`
- Create: `TiaMcpServer.Tests/NetworkStructuredProtocolTests.cs`
- Create: `TiaMcpServer.Tests/CanonicalJsonTests.cs`
- Create: `TiaMcpServer/Json/CanonicalJson.cs`
- Create: `TiaMcpServer/Tools/StructuredToolResult.cs`
- Create: `TiaMcpServer/OperationBatches/StructuredOperationBatch.cs`
- Create: `TiaMcpServer/OperationBatches/StructuredOperationBatchExecutionEngine.cs`
- Create: `TiaMcpServer/Network/NetworkPayloadContract.cs`
- Create: `TiaMcpServer/Network/NetworkToolResponses.cs`
- Modify: `TiaMcpServer/Network/NetworkReadTools.cs`
- Modify: `TiaMcpServer.Contracts/WorkerFailureCategories.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`

**Interfaces:**

- Consumes: existing `OpennessWorkerClient`, `WorkerCallResult`, `NetworkOperationRequest`, and FakeWorker `network-roundtrip` behavior.
- Produces: `CanonicalJson.Deserialize<T>`, `Serialize<T>`, `ToElement<T>`, and `Normalize<T>`; `StructuredToolResult.Create<TResponse>`; shared structured batch records; `NetworkPayloadContract`; and `Task<CallToolResult> NetworkRead(...)` with `NetworkReadResponse` output schema.

- [ ] Add an in-process MCP protocol harness using paired anonymous streams, `StreamServerTransport`, `StreamClientTransport`, the real attributed `NetworkReadTools.NetworkRead` method, and an `OpennessWorkerClient` pointed at `FakeWorkerLocator.Locate()`. The harness must exercise `tools/list` and `tools/call`; it must not invoke the tool method directly.

- [ ] Add `NetworkRead_AdvertisesAndReturnsSingleLayerStructuredContract`. Its decisive assertions are:

```csharp
var tool = Assert.Single(
    await harness.Client.ListToolsAsync(),
    candidate => candidate.Name == "network_read");
Assert.NotNull(tool.ProtocolTool.OutputSchema);

var result = await harness.Client.CallToolAsync(
    "network_read",
    new Dictionary<string, object?>
    {
        ["operations"] = new[]
        {
            new
            {
                operationId = "hardware",
                operation = "read_hardware_config",
                projectPath = "network-roundtrip"
            }
        }
    });

var structured = Assert.IsType<JsonElement>(result.StructuredContent);
var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
using var textDocument = JsonDocument.Parse(text);
Assert.True(JsonElement.DeepEquals(structured, textDocument.RootElement));
Assert.Equal(
    JsonValueKind.Object,
    structured.GetProperty("batch")
        .GetProperty("operations")[0]
        .GetProperty("result").ValueKind);
```

- [ ] Run only that test before production edits:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~NetworkStructuredProtocolTests.NetworkRead_AdvertisesAndReturnsSingleLayerStructuredContract"
```

Expected Phase 1 failure: `OutputSchema` or `StructuredContent` is null, and the operation `result` is a JSON string rather than an object.

- [ ] Add the first canonical tests before `CanonicalJson.cs`: recursive ordinal property ordering, array-order preservation, compact output, and detached `JsonElement` lifetime. Confirm the test project initially fails to compile because the reusable type is absent.

- [ ] Implement `CanonicalJson` with this surface:

```csharp
public static class CanonicalJson
{
    public static T Deserialize<T>(string json);
    public static string Serialize<T>(T value);
    public static JsonElement ToElement<T>(T value);
    public static (T Value, string Text, JsonElement Element) Normalize<T>(string json);
}
```

The implementation must parse with comments and trailing commas disabled, recursively reject duplicate property names, deserialize with case-sensitive camelCase names and `JsonUnmappedMemberHandling.Disallow`, serialize explicit nulls, sort object properties with `StringComparer.Ordinal`, preserve array order, emit compact UTF-8 JSON, and clone every returned root element before disposing its `JsonDocument`.

- [ ] Implement `StructuredToolResult.Create<TResponse>(TResponse response, bool isError)` so one call to `CanonicalJson.Serialize` produces both representations:

```csharp
var text = CanonicalJson.Serialize(response);
using var document = JsonDocument.Parse(text);
return new CallToolResult
{
    Content = new List<ContentBlock> { new TextContentBlock { Text = text } },
    StructuredContent = document.RootElement.Clone(),
    IsError = isError
};
```

- [ ] Add `protocol_error` to `WorkerFailureCategories` and its known-category set. Do not change the meaning of existing categories.

- [ ] Implement `NetworkPayloadContract` as the only Network worker-success decoder:

```csharp
"read_hardware_config"        => Decode<HardwareConfigInfo>(payload),
"search_equipment_catalog"    => Decode<CatalogEntryInfo[]>(payload),
"add_network_device"          => Decode<AddDeviceResultInfo>(payload),
"configure_network_device"    => Decode<ConfigureNetworkDeviceResultInfo>(payload)
```

Worker failures become failed items with their approved category/message. A successful envelope with malformed, unknown, incorrectly cased, incorrectly typed, or structurally invalid payload becomes a failed item with category `protocol_error`; never echo the rejected payload in the error.

- [ ] Implement the shared structured item/batch types and `ExecuteReadsAsync`. Reads continue after worker failure and after payload projection failure. Counts are derived from final item statuses and warnings are always non-null arrays.

- [ ] Change only `network_read` to the structured contract:

```csharp
[McpServerTool(
    Name = "network_read",
    ReadOnly = true,
    Destructive = false,
    OpenWorld = false,
    UseStructuredContent = true,
    OutputSchemaType = typeof(NetworkReadResponse))]
public static async Task<CallToolResult> NetworkRead(...)
```

Validation/access failures return `NetworkReadResponse(tool, false, null, error)` with MCP `isError: true`. A valid batch containing item failures returns the batch with top-level `success: false` and MCP `isError: false`.

- [ ] Add explicit linked-source entries for all new host files to `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`.

- [ ] Run the focused tests green, then run existing schema and non-network formatter regressions:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~NetworkStructuredProtocolTests|FullyQualifiedName~CanonicalJsonTests|FullyQualifiedName~McpToolSchemaTests|FullyQualifiedName~StandaloneToolResultFormatterTests"
```

- [ ] Review the diff and commit this future implementation task:

```powershell
git add TiaMcpServer TiaMcpServer.Contracts TiaMcpServer.Tests
git commit -m "feat: add reusable structured JSON contract gate"
```

### Task 2: Harden canonical payload validation, batch semantics, and whole-value budgeting

**Files:**

- Modify: `TiaMcpServer.Tests/CanonicalJsonTests.cs`
- Create: `TiaMcpServer.Tests/NetworkPayloadContractTests.cs`
- Create: `TiaMcpServer.Tests/StructuredOperationBatchTests.cs`
- Create: `TiaMcpServer.Tests/StructuredOperationBatchPayloadBudgetTests.cs`
- Create: `TiaMcpServer/OperationBatches/StructuredOperationBatchPayloadBudget.cs`
- Modify: `TiaMcpServer/Json/CanonicalJson.cs`
- Modify: `TiaMcpServer/Network/NetworkPayloadContract.cs`
- Modify: `TiaMcpServer/OperationBatches/StructuredOperationBatchExecutionEngine.cs`
- Modify: `TiaMcpServer/Network/NetworkReadTools.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`

**Interfaces:**

- Consumes: Task 1 canonical JSON, payload registry, structured batch records, and structured `network_read` result.
- Produces: complete strict-payload validation and `StructuredOperationBatchPayloadBudget.Apply(...)`, bounded to 60,000 characters per result and 180,000 characters per canonical response document.

- [ ] Extend canonical tests first to cover duplicate members at every depth, unknown members, wrong casing, missing required members where the CLR contract requires them, wrong JSON types, explicit nulls, non-null collection defaults, numeric/boolean preservation, Unicode property ordering, and canonicalize/parse/canonicalize stability.

- [ ] Add payload-registry tests for all four operations. For every expected CLR type, prove a valid payload becomes an object/array and an invalid successful payload becomes `protocol_error` without leaking the raw payload.

- [ ] Add batch-engine tests proving:

  - reads continue after worker and protocol errors;
  - order is preserved;
  - worker warnings remain on the matching item;
  - a successful JSON `null` remains `status: succeeded` with `result: null`; and
  - all inapplicable item fields serialize as explicit nulls.

- [ ] Run these new tests and record their failures before hardening production code.

- [ ] Implement the complete canonical validation rules. Required-member checks that are not representable through CLR initialization must live in type-specific validators owned by `NetworkPayloadContract`; do not introduce Network knowledge into `CanonicalJson`.

- [ ] Implement `StructuredOperationBatchPayloadBudget.Apply` with constants `MaxItemChars = 60_000` and `MaxDocumentChars = 180_000`. Budget against the exact canonical response document, never `JsonElement.GetRawText()` from an unrelated serialization.

- [ ] Lock the budget algorithm with tests:

  1. Measure each successful result as canonical JSON.
  2. Replace an oversized result as a whole with `status: omitted`, `result: null`, and structured retry guidance.
  3. If the complete response remains oversized, omit complete successful results while retaining item order and all failure categories.
  4. Remove complete warning entries before shortening any failure message.
  5. If a long message must be shortened, record original character counts, omitted-warning counts, and affected operation IDs in `batch.truncation`.
  6. Recompute statuses, counts, and final canonical length after every presentation change.

- [ ] Add exact tests for the 60,000/180,000 boundaries, deterministic omission order, failure-evidence priority, no invalid JSON substrings, and equality of the bounded text/structured documents.

- [ ] Wire `network_read` through the structured budget. The retry tool is `network_read`; read-hardware guidance says to split the batch, and catalog guidance says to narrow `query`/`maxResults` or split the batch.

- [ ] Run focused and legacy batch tests. Legacy `OperationBatchResultFormatter` and `OperationBatchPayloadBudget` output must remain byte-compatible for non-network callers:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~CanonicalJsonTests|FullyQualifiedName~NetworkPayloadContractTests|FullyQualifiedName~StructuredOperationBatch|FullyQualifiedName~OperationBatchKernelTests|FullyQualifiedName~OperationBatchPayloadBudgetTests"
```

- [ ] Commit this future implementation task:

```powershell
git add TiaMcpServer TiaMcpServer.Tests
git commit -m "feat: enforce typed network payload budgets"
```

### Task 3: Migrate `network_write` to a typed discriminated envelope and canonical safety binding

**Files:**

- Create: `TiaMcpServer/Safety/CanonicalWriteSafety.cs`
- Modify: `TiaMcpServer/Safety/WriteSafetyService.cs`
- Modify: `TiaMcpServer/Network/NetworkToolResponses.cs`
- Modify: `TiaMcpServer/Network/NetworkWriteTools.cs`
- Modify: `TiaMcpServer/Network/NetworkSafetySnapshot.cs`
- Modify: `TiaMcpServer/OperationBatches/StructuredOperationBatchExecutionEngine.cs`
- Modify: `TiaMcpServer.Tests/WriteSafetyServiceTests.cs`
- Modify: `TiaMcpServer.Tests/NetworkToolsTests.cs`
- Modify: `TiaMcpServer.Tests/NetworkStructuredProtocolTests.cs`
- Modify: `TiaMcpServer.Tests/NetworkOperationFakeWorkerTests.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`

**Interfaces:**

- Consumes: Tasks 1–2 canonical serialization, typed payload projection, structured execution, and payload budgeting; existing `WriteSafetyService` token/audit primitives.
- Produces: opt-in canonical safety methods, `StructuredOperationBatchExecutionEngine.ApplyWritesAsync`, and `Task<CallToolResult> NetworkWrite(...)` with `NetworkWriteResponse` output schema.

- [ ] Add failing actual-protocol tests for all `network_write` branches:

  - `phase: preview` has only `preview` non-null;
  - `phase: apply` has only `batch` non-null;
  - `phase: error` has only `error` non-null;
  - text and `structuredContent` are the same canonical document;
  - the tool advertises `NetworkWriteResponse` output schema; and
  - `isError` is true only for whole-tool errors, not an executed batch item failure.

- [ ] Add safety tests before production changes for these exact invariants:

  - property-order-only differences in target, request, or current state validate;
  - array reordering fails;
  - type or value changes fail;
  - expiry remains ten minutes;
  - consumption remains single-use; and
  - existing text-path tests remain unchanged.

- [ ] Add opt-in typed entry points without replacing existing methods:

```csharp
public CanonicalWritePreview<TTarget> CreateCanonicalPreview<TTarget, TInput, TState>(...);
public WriteSafetyValidationResult ValidateCanonicalEnvelope<TTarget, TInput>(...);
public WriteSafetyValidationResult ValidateAndConsumeCanonical<TTarget, TInput, TState>(...);
public void AppendCanonicalAudit<TTarget, TInput, TState, TResult>(...);
```

Each method calls `CanonicalJson.Serialize` exactly once per logical value and passes canonical strings to shared private token/audit primitives. Existing `CreatePreview`, `ValidateEnvelope`, `ValidateAndConsume`, and `AppendAudit` continue to use their existing presentation serializer.

- [ ] Change `NetworkSafetySnapshot.ReadCurrentStateAsync` to decode `HardwareConfigInfo` through `NetworkPayloadContract`, retain the typed value, and compute state from the canonical typed document. A state read or decode failure is a whole-tool error and cannot issue or consume a token.

- [ ] Add `StructuredOperationBatchExecutionEngine.ApplyWritesAsync`. It projects each worker response before deciding whether to continue. A worker failure or `protocol_error` stops execution and marks every later operation `skipped` with `skipReason: earlierOperationFailed`.

- [ ] Change `network_write` to:

```csharp
[McpServerTool(
    Name = "network_write",
    ReadOnly = false,
    Destructive = true,
    OpenWorld = false,
    UseStructuredContent = true,
    OutputSchemaType = typeof(NetworkWriteResponse))]
public static async Task<CallToolResult> NetworkWrite(...)
```

Preview returns typed preview data. Apply returns a structured batch and appends the exact canonical response document to the audit entry. Do not add a hidden post-write read.

- [ ] Preserve warning text on apply failures explaining that earlier operations may already have changed TIA state and no rollback was attempted.

- [ ] Run focused safety/write tests and the actual protocol tests green:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~WriteSafetyServiceTests|FullyQualifiedName~NetworkToolsTests|FullyQualifiedName~NetworkStructuredProtocolTests|FullyQualifiedName~NetworkOperationFakeWorkerTests"
```

- [ ] Commit this future implementation task:

```powershell
git add TiaMcpServer TiaMcpServer.Tests
git commit -m "feat: bind network writes to canonical typed state"
```

### Task 4: Expose authoritative network identities in the read contract

**Files:**

- Modify: `TiaMcpServer.Contracts/DeviceItemInfo.cs`
- Modify: `TiaMcpServer.Contracts/NodeInfo.cs`
- Modify: `TiaMcpServer.Contracts/SubnetInfo.cs`
- Modify: `TiaMcpServer.Contracts/IoSystemInfo.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/HardwareConfigReader.cs`
- Modify: `TiaMcpServer.Tests/HardwareConfigInfoTests.cs`
- Modify: `TiaMcpServer.Tests/NetworkPayloadContractTests.cs`
- Modify: `TiaMcpServer.FakeWorker/Program.cs`
- Modify: `TiaMcpServer.Tests/NetworkOperationFakeWorkerTests.cs`

**Interfaces:**

- Consumes: Task 1 `NetworkPayloadContract` and the existing hardware DTO/device-item hierarchy.
- Produces: `NodeInfo.NodeId`, `NodeInfo.NodeType`, `SubnetInfo.SubnetId`, `SubnetInfo.NetworkType`, `IoSystemInfo.Number`, and non-null hardware collections used by Task 6 selectors.

- [ ] Add failing contract tests for these members and exact JSON names:

```csharp
public class NodeInfo
{
    public string NodeId { get; set; } = string.Empty;
    public string? NodeType { get; set; }
}

public class SubnetInfo
{
    public string SubnetId { get; set; } = string.Empty;
    public string? NetworkType { get; set; }
}

public class IoSystemInfo
{
    public int? Number { get; set; }
}
```

Keep existing human-readable names. Make every collection in the hardware DTO tree non-null by default, including `DeviceItemInfo.NetworkInterfaces` and `DeviceItemInfo.Items`.

- [ ] Add read-fixture tests for two interfaces/nodes under one PC station. The PLC-facing and client-database-facing nodes must have different `nodeId` values and remain separately addressable.

- [ ] Run the tests and record the missing-member/null-collection failures.

- [ ] Update `HardwareConfigReader` to read:

  - `Node.NodeId` and node type;
  - `Subnet.SubnetId` and `NetType`; and
  - modeled IO-system `Number`.

Use existing safe-read helpers so unreadable values add messages instead of being invented. An unreadable identity may remain empty/null in a read result, but it cannot later satisfy a write selector.

- [ ] Update the FakeWorker hardware payloads to be valid complete `HardwareConfigInfo` JSON under the strict registry. Keep all collection properties present and use explicit nulls where applicable.

- [ ] Run contract/FakeWorker tests, then a serialized stub build because the production reader touches Siemens types:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~HardwareConfigInfoTests|FullyQualifiedName~NetworkPayloadContractTests|FullyQualifiedName~NetworkOperationFakeWorkerTests"
dotnet build TiaMcpServer.sln --no-restore -m:1 /p:UseTiaPortalReferenceStubs=true
```

- [ ] Commit this future implementation task:

```powershell
git add TiaMcpServer.Contracts TiaMcpServer.OpennessWorker TiaMcpServer.FakeWorker TiaMcpServer.Tests
git commit -m "feat: expose deterministic network identities"
```

### Task 5: Replace the flat configure request with exact `target` and `changes`

**Files:**

- Modify: `TiaMcpServer/Network/NetworkOperationRequest.cs`
- Modify: `TiaMcpServer/Network/NetworkOperationCatalog.cs`
- Modify: `TiaMcpServer/Network/NetworkWorkerInvoker.cs`
- Modify: `TiaMcpServer.Contracts/WorkerRequest.cs`
- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs`
- Modify: `TiaMcpServer.OpennessWorker/Program.cs`
- Modify: `TiaMcpServer.Tests/NetworkOperationRequestJsonTests.cs`
- Modify: `TiaMcpServer.Tests/NetworkOperationCatalogTests.cs`
- Modify: `TiaMcpServer.Tests/NetworkFieldForwardingTests.cs`
- Modify: `TiaMcpServer.Tests/WorkerResponseJsonTests.cs`
- Modify: `TiaMcpServer.Tests/OpennessWorkerClientIntegrationTests.cs`

**Interfaces:**

- Consumes: Task 4 authoritative identity fields and the existing network operation catalog/worker transport.
- Produces: nested `NetworkDeviceTarget`, `NetworkDeviceChanges`, `NetworkSubnetTarget`, and `NetworkIoSystemTarget`; flattened worker `NodeId`, `SubnetId`, `IoSystemSubnetId`, and `IoSystemNumber`; and the exact `ConfigureNetworkDeviceAsync(...)` signature shown below.

- [ ] Add failing JSON and catalog tests for the approved request:

```csharp
public sealed class NetworkDeviceTarget
{
    public string? DeviceName { get; init; }
    public string? NodeId { get; init; }
}

public sealed class NetworkDeviceChanges
{
    public string? IpAddress { get; init; }
    public string? SubnetMask { get; init; }
    public string? PnDeviceName { get; init; }
    public NetworkSubnetTarget? Subnet { get; init; }
    public NetworkIoSystemTarget? IoSystem { get; init; }
}

public sealed class NetworkSubnetTarget
{
    public string? SubnetId { get; init; }
}

public sealed class NetworkIoSystemTarget
{
    public string? SubnetId { get; init; }
    public int? Number { get; init; }
}
```

`NetworkOperationRequest` gains nullable `Target` and `Changes`. Remove the legacy flat configure properties; do not add aliases or a compatibility converter.

- [ ] Test every rule: target/changes required only for configure; nonblank device/node identities; at least one change; null means no change; subnet and IO-system nested requirements; matching subnet IDs when both are present; unknown nested fields rejected; and `add_network_device` retains its flat creation fields.

- [ ] Add actual-protocol negative tests for unknown and incorrectly typed nested input so rejection is proven at the public MCP boundary, not only by direct CLR construction.

- [ ] Run the request/catalog tests and record failures before production edits.

- [ ] Replace reflection-only flat-field validation with explicit per-operation validation while retaining `NetworkOperationSpec` as the schema/catalog source. Annotate Network request/nested types for case-sensitive unknown-member rejection and verify the generated input schema does not advertise legacy fields.

- [ ] Flatten only at the worker boundary. Extend `WorkerRequest` with:

```csharp
public string? NodeId { get; set; }
public string? SubnetId { get; set; }
public string? IoSystemSubnetId { get; set; }
public int? IoSystemNumber { get; set; }
```

Remove the configure-only `SubnetName` and `IoSystemName` fields after proving no other worker operation uses them.

- [ ] Change `ConfigureNetworkDeviceAsync`, `NetworkWorkerInvoker.InvokeWriteAsync`, and worker dispatch to forward the exact device/node/subnet/IO identities plus scalar changes. The signature is:

```csharp
ConfigureNetworkDeviceAsync(
    string deviceName,
    string nodeId,
    string? ipAddress,
    string? subnetMask,
    string? pnDeviceName,
    string? subnetId,
    string? ioSystemSubnetId,
    int? ioSystemNumber,
    string? projectPath)
```

- [ ] Run JSON/catalog/forwarding/IPC tests and the stub build:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~NetworkOperationRequestJsonTests|FullyQualifiedName~NetworkOperationCatalogTests|FullyQualifiedName~NetworkFieldForwardingTests|FullyQualifiedName~WorkerResponseJsonTests|FullyQualifiedName~OpennessWorkerClientIntegrationTests|FullyQualifiedName~NetworkStructuredProtocolTests"
dotnet build TiaMcpServer.sln --no-restore -m:1 /p:UseTiaPortalReferenceStubs=true
```

- [ ] Commit this future implementation task:

```powershell
git add TiaMcpServer TiaMcpServer.Contracts TiaMcpServer.OpennessWorker TiaMcpServer.Tests
git commit -m "feat: require exact network write targets"
```

### Task 6: Resolve selectors fail-closed in both host preflight and Openness worker

**Files:**

- Create: `TiaMcpServer/Network/NetworkIdentityResolver.cs`
- Modify: `TiaMcpServer/Network/NetworkSafetySnapshot.cs`
- Modify: `TiaMcpServer/Network/NetworkToolResponses.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/NetworkDeviceConfigurator.cs`
- Create: `TiaMcpServer.Tests/NetworkIdentityResolverTests.cs`
- Modify: `TiaMcpServer.Tests/NetworkToolsTests.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`

**Interfaces:**

- Consumes: Task 3 canonical preview/state binding, Task 4 hardware identities, and Task 5 nested configure request/flattened worker fields.
- Produces: `NetworkIdentityResolver` and `NetworkWriteTargetEvidence`; host preview/apply preflight; and worker resolution by exact device-scoped node ID, subnet ID, and subnet-scoped IO-system number.

- [ ] Add pure resolver tests first with a synthetic `HardwareConfigInfo` containing:

  - one PC device;
  - nested device items;
  - two interfaces;
  - a PLC-facing node and a database-facing node;
  - duplicate node-ID, missing node-ID, unreadable node-ID, missing subnet-ID, duplicate subnet-ID, and missing/duplicate IO-system-number variants.

- [ ] Populate the already declared success type as typed preview evidence, not a string summary:

```csharp
public sealed record NetworkWriteTargetEvidence(
    string OperationId,
    string Operation,
    string DeviceName,
    string? DeviceTypeIdentifier,
    IReadOnlyList<string> DeviceItemPath,
    string? NetworkInterfaceName,
    string? NodeName,
    string? NodeId,
    string? SubnetName,
    string? SubnetId,
    string? IoSystemName,
    int? IoSystemNumber);
```

Creation operations populate requested device/type evidence and leave existing-object members null. Configure operations populate the canonical matched location.

- [ ] Run resolver tests and record the initial failures.

- [ ] Implement host resolution with these exact rules:

  1. Match exactly one device using the worker's existing case-insensitive name semantics.
  2. Traverse every readable nested device item and network interface.
  3. Match exactly one node by ordinal `nodeId` within that device.
  4. Match subnets by ordinal `subnetId`.
  5. Match an IO system by ordinal subnet ID plus modeled integer number.
  6. Return `postcondition_failed` for zero, multiple, or unreadable identity evidence.

- [ ] Resolve targets against the typed hardware snapshot before preview and again against the fresh typed snapshot before token consumption. Bind the canonical target evidence, ordered `NetworkOperationRequest[]`, and full typed hardware state. Presentation names are evidence only.

- [ ] Replace `FindNetworkInterface`, `GetFirstNode`, `FindSubnet` by name, and IO-system name lookup in `NetworkDeviceConfigurator`. The worker must traverse all interfaces/nodes, find exactly one matching `NodeId`, resolve subnet/IO identities exactly, and throw `WorkerOperationException(WorkerFailureCategories.PostconditionFailed, ...)` on ambiguity or missing identity.

- [ ] Keep `ApplyNodeAttribute`, connection method discovery, and sequential/no-rollback behavior unchanged except that they operate on the resolved objects. Do not introduce a name or first-item fallback.

- [ ] Run resolver/tool tests and a serialized stub build. State explicitly that the stub build proves signatures/compilation only:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~NetworkIdentityResolverTests|FullyQualifiedName~NetworkToolsTests"
dotnet build TiaMcpServer.sln --no-restore -m:1 /p:UseTiaPortalReferenceStubs=true
```

- [ ] Commit this future implementation task:

```powershell
git add TiaMcpServer TiaMcpServer.OpennessWorker TiaMcpServer.Tests
git commit -m "fix: resolve multi-homed network endpoints exactly"
```

### Task 7: Prove the complete multi-homed read-to-write flow and protocol-error stop behavior

**Files:**

- Modify: `TiaMcpServer.FakeWorker/Program.cs`
- Modify: `TiaMcpServer.Tests/NetworkOperationFakeWorkerTests.cs`
- Modify: `TiaMcpServer.Tests/NetworkStructuredProtocolTests.cs`
- Modify: `TiaMcpServer.Tests/NetworkToolsTests.cs`
- Modify: `TiaMcpServer.Tests/NetworkFieldForwardingTests.cs`
- Modify: `TiaMcpServer.Tests/OperationBatchKernelTests.cs`
- Modify: `TiaMcpServer.Tests/OperationBatchPayloadBudgetTests.cs`

**Interfaces:**

- Consumes: the complete structured read/write, identity, request, safety, and worker-forwarding surfaces from Tasks 1–6.
- Produces: a stateful `multi-homed-network` FakeWorker scenario and public MCP evidence for read → select → preview → apply → read, including protocol-error stop behavior.

- [ ] Add a failing `multi-homed-network` FakeWorker scenario test. The scenario keeps process-local hardware state with:

```text
PC_1
  PLC port       nodeId=node-plc  ip=192.168.0.20  subnetId=subnet-plc
  Database port  nodeId=node-db   ip=10.20.30.40   subnetId=subnet-db
```

The scenario must parse the forwarded `nodeId`, mutate only the selected node after configure, and serialize worker payloads through complete contract-shaped objects rather than hand-maintained escaped fragments.

- [ ] Through the actual MCP protocol, implement this test flow:

```text
network_read
  -> select PC_1/node-plc
  -> network_write preview with target + changes
  -> network_write apply with unchanged operations + token
  -> network_read
  -> compare both ports
```

Assert the PLC-facing node changed and the database-facing node is byte-for-byte unchanged in the canonical read model.

- [ ] Add failing cases for changed node ID between preview/apply, property reordering only, reordered operations, wrong subnet/IO pairing, missing identity, ambiguous identity, changed scalar value, and token replay.

- [ ] Add an `invalid-network-success-payload` scenario. For reads, assert later operations still run. For writes, assert the invalid success payload becomes `protocol_error`, later writes are skipped, the response warns that a mutation may already have occurred, and MCP `isError` remains false because a usable batch exists.

- [ ] Run the new tests red before changing FakeWorker or orchestration code.

- [ ] Implement only the fixture/orchestration changes needed for the tests. Do not add a hidden post-write read to production; the explicit final `network_read` belongs to the test/client workflow.

- [ ] Update Phase 1 regression tests that intentionally asserted string-valued Network results. Preserve their sequencing/token assertions while changing only Network expectations to single-layer JSON.

- [ ] Run all Network, shared-kernel, and legacy-budget tests:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~Network|FullyQualifiedName~OperationBatchKernelTests|FullyQualifiedName~OperationBatchPayloadBudgetTests|FullyQualifiedName~StructuredOperationBatch"
```

- [ ] Commit this future implementation task:

```powershell
git add TiaMcpServer.FakeWorker TiaMcpServer.Tests TiaMcpServer
git commit -m "test: prove multi-homed network contract flow"
```

### Task 8: Add the separately authorized live-TIA harness and update technical documentation

**Files:**

- Create: `scripts/live-test-network-phase2.ps1`
- Create: `TiaMcpServer.Tests/NetworkLiveHarnessContractTests.cs`
- Modify: `README.md`
- Modify: `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md`
- Modify: `docs/NETWORK_OPERATIONS_ROADMAP.md`
- Modify: `docs/ARCHITECTURE.md`
- Modify: `AGENTS.md`

**Interfaces:**

- Consumes: the final public MCP schemas and safety/selector behavior from Tasks 1–7.
- Produces: a PowerShell 7 MCP acceptance harness with read/preview/apply gates and the supported technical contract in existing repository documentation.

- [ ] Before writing the script, add Pester-free static checks in an existing suitable test class or a new `NetworkLiveHarnessContractTests.cs` that read the script and prove: PowerShell 7 is required; default mode is read-only; preview cannot apply; apply requires an explicit switch plus project path, device name, node ID, and safe values; and no ordinary test invokes the script.

- [ ] Run the static harness tests and record the missing-file failure.

- [ ] Implement `scripts/live-test-network-phase2.ps1` with modes `Read`, `Preview`, and `Apply`. `Read` and `Preview` are non-mutating. `Apply` requires both `-Mode Apply` and `-AllowApply`, prints the exact selected identity/values, and requires a separate interactive confirmation unless an equally explicit CI-only confirmation switch is supplied.

- [ ] Make the harness launch the MCP host and perform the MCP initialize/list/call sequence so it validates the public protocol, not only direct worker IPC. The apply path must perform an explicit post-read and compare the selected and non-selected nodes.

- [ ] Do not run the harness during implementation or ordinary verification. Add usage examples that use obvious placeholder values and state that a disposable/backed-up TIA V21 project is required.

- [ ] Update `README.md` with single-layer structured output, `target`/`changes`, exact `nodeId`/`subnetId`/IO number selectors, explicit post-read guidance, and partial-write/no-rollback warnings.

- [ ] Update `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md` with the exact read/write envelopes, all four payload result types, omission/truncation semantics, and the multi-homed example.

- [ ] Update `docs/NETWORK_OPERATIONS_ROADMAP.md` to mark Phase 2 complete only after Tasks 1–7 and their automated gates pass. Record that Phase 3 capability expansion remains separate.

- [ ] Update `docs/ARCHITECTURE.md` with the reusable opt-in `CanonicalJson`/`StructuredToolResult` seam, typed Network payload registry, canonical safety flow, and exact host-to-worker selector boundary.

- [ ] Update `AGENTS.md` with durable repository rules: future structured tools reuse the shared gate; text and structured documents come from the same canonical serialization; worker success payloads are typed; and nested JSON strings are prohibited for migrated tools.

- [ ] Validate script parsing and documentation drift without contacting TIA:

```powershell
$null = [System.Management.Automation.Language.Parser]::ParseFile(
  (Resolve-Path scripts/live-test-network-phase2.ps1),
  [ref]$null,
  [ref]$null)
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~NetworkLiveHarnessContractTests|FullyQualifiedName~McpToolSchemaTests|FullyQualifiedName~BatchToolMetadataTests"
```

- [ ] Commit this future implementation task:

```powershell
git add scripts README.md docs AGENTS.md TiaMcpServer.Tests
git commit -m "docs: publish network phase 2 contract"
```

### Task 9: Run regression, coverage, review, and completion gates

**Files:**

- Review all files changed by Tasks 1–8.
- Modify only files required to resolve findings or verification failures, using a fresh failing test for each behavior correction.

**Interfaces:**

- Consumes: every deliverable and focused test from Tasks 1–8.
- Produces: serialized Release stub-build evidence, full-suite evidence, scoped coverage evidence, regression evidence, review findings/resolutions, and a handoff that keeps live TIA status separate.

- [ ] Confirm the worktree contains no unrelated changes and inspect the complete diff:

```powershell
git status --short
git diff --stat
git diff --check
```

- [ ] Run the serialized Release stub build exactly as approved:

```powershell
dotnet build TiaMcpServer.sln --no-restore -m:1 --configuration Release /p:UseTiaPortalReferenceStubs=true
```

- [ ] Run the complete test suite:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --configuration Release
```

- [ ] Collect coverage and require at least 80% line coverage for materially changed host/contract logic. Worker Openness files remain outside unit coverage and are covered only by stub compilation plus the separately authorized live harness:

```powershell
$results = Join-Path 'TestResults' ('network-phase2-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --configuration Release --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory $results
$coverage = Get-ChildItem -LiteralPath $results -Recurse -Filter coverage.cobertura.xml | Select-Object -First 1
./scripts/verify-coverage-threshold.ps1 -CoveragePath $coverage.FullName -MinimumLineRate 0.80
```

- [ ] Run regression gates explicitly:

  - actual MCP `tools/list` reports 14 tools in read-write mode and 4 in read-only mode;
  - both network tools have output schemas;
  - non-network tool output schemas and text contracts are unchanged;
  - generic batches still reject all four dedicated network operations;
  - access-mode enforcement is unchanged;
  - audit and safety-token tests pass;
  - every new host source file has an explicit test-project link; and
  - Network text and `structuredContent` are identical canonical JSON.

- [ ] Perform a correctness/security/scope review using the approved spec and this plan. Pay particular attention to payload leakage in protocol errors, selector ambiguity, token consumption timing, audit consistency, warning loss, and accidental non-network migration.

- [ ] If review finds a behavior defect, add a focused failing test before changing production code, rerun the focused test green, then rerun the full gates above.

- [ ] Record the final evidence in the implementation handoff with separate sections for:

  - automated contract/build/test/coverage evidence;
  - live TIA evidence, which must say `not run` unless separately authorized and actually performed; and
  - remaining commissioning risk around Siemens runtime behavior.

- [ ] If review corrections changed files, inspect `git diff --name-only`, stage only those exact paths, verify them with `git diff --cached --name-only`, and commit with `test: close network phase 2 acceptance gaps`. If review changed nothing, do not create an empty commit. Do not squash or publish unless the user requests it.

## Definition of Done

- Both network tools advertise output schemas and return one canonical JSON object in both MCP text and `structuredContent`.
- Successful operation payloads are real JSON values validated against the declared CLR type; invalid successful payloads are `protocol_error`.
- Canonical JSON is deterministic under the repository contract and is not described as RFC 8785-compliant.
- Structured budgets never substring a JSON payload and expose explicit omission/truncation evidence.
- `network_write` uses the preview/apply/error discriminated envelope and preserves all safety/audit invariants.
- Configure writes require exact device/node identity and exact subnet/IO identity where applicable.
- A multi-homed PC test proves one selected port changes and the other remains unchanged.
- No first-interface, first-node, or name-only selector fallback remains.
- Non-network public output remains unchanged.
- Release stub build, full tests, coverage threshold, diff check, and regression gates pass.
- Live TIA behavior is either separately verified with the committed harness or explicitly reported as unverified.
