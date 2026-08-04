# Network Operations Phase 3 Identity and Introspection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add snapshot-scoped network-object discovery and typed read-only inspection to `network_read`, while preserving the Phase 2 structured JSON contract, the existing MCP tool count, and the network-write safety model.

**Architecture:** Keep the public surface typed and discriminated, route all Siemens Openness access through the .NET Framework worker, and isolate dynamic `IEngineeringObject` inspection behind per-kind modeled adapters plus a narrow generic attribute reader. Enrich `read_hardware_config` with reusable selectors, add provisional bounded `list_network_objects` and targeted `inspect_network_object` operations, and treat live TIA Portal V21 evidence as the stabilization gate rather than inferring runtime support from the installed metadata or stub build.

**Tech Stack:** C# 12/.NET 8 host, C#/.NET Framework 4.8 Openness worker, `TiaMcpServer.Contracts` targeting .NET Standard 2.0, System.Text.Json canonical structured results, xUnit, FakeWorker IPC tests, PowerShell 7 live harness, Siemens TIA Portal V21 Openness.

## Global Constraints

- Phase 3 is read-only. Do not add a write operation, safety token path, audit entry, save, compile, download, or commissioning action.
- Keep `network_read` as the only public read tool for this domain. `list_network_objects` and `inspect_network_object` are operation names inside its existing `operations` array; the MCP tool count must not increase.
- Reuse `StructuredToolResult`, `StructuredOperationBatch`, `CanonicalJson`, and the existing per-item 60,000-character and aggregate 180,000-character budgets. Do not introduce a second serializer or response envelope.
- Every worker success payload has exactly one registered CLR result type and is decoded fail-closed. Malformed payloads return `protocol_error` without echoing rejected worker data.
- Public selectors are snapshot-scoped locators, not persistent IDs. Device-item segments follow the recorded zero-based sibling index and then verify `name`, `positionNumber`, and `typeIdentifier`; evidence drift is a selection failure.
- Preserve the Phase 2 `configure_network_device` JSON shape. Its `target` object may accept optional `kind: "node"` so a discovered node selector can be copied directly, but the original `{ deviceName, nodeId }` input remains valid.
- `list_network_objects` is provisional. It stays only if the separately authorized live run satisfies at least one approved value gate. Otherwise remove the operation completely before declaring Phase 3 stable; do not leave an alias or deprecated shell.
- All public attribute values use the approved lossless vocabulary: `null`, `string`, `boolean`, `integer`, `number`, or `enum`. Never publish an arbitrary CLR object's `ToString()` result as a value.
- Access, availability, and per-attribute diagnostics are separate fields. An unknown or unreadable requested attribute must not fail or suppress later attributes.
- Keep Siemens types and `IEngineeringObject` calls in `TiaMcpServer.OpennessWorker`. The host and Contracts projects must remain Siemens-free.
- Use TDD wherever the behavior is executable without TIA: write the focused test, run it and observe the expected failure, make the smallest production change, then rerun it. For the thin Siemens shell, the narrow pre-live proof is a stub build plus Siemens-free policy/normalization tests; state that this is not runtime proof.
- Build the solution serially with `-m:1`. Never run the Phase 3 live script without separate user authorization and a prepared TIA project.
- Add every new linked host or Siemens-free worker source explicitly to `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`; do not replace the explicit list with a wildcard.
- Work on the current branch and preserve unrelated user changes. Commit only the files named by the current task after its focused tests pass.

---

## Locked Public Contract

The implementation tasks below use this vocabulary. A live finding may cause a reviewed revision, but ordinary implementation must not silently change these names or semantics.

### New `network_read` operations

```json
{
  "operations": [
    {
      "operationId": "list-1",
      "operation": "list_network_objects",
      "objectKinds": ["deviceItem", "networkInterface"],
      "deviceName": "PLC_1",
      "pageSize": 50,
      "cursor": null
    },
    {
      "operationId": "inspect-1",
      "operation": "inspect_network_object",
      "target": {
        "kind": "node",
        "deviceName": "PLC_1",
        "nodeId": "X1"
      },
      "attributeNames": ["Name", "Address"]
    }
  ]
}
```

`objectKinds` is required, non-empty, duplicate-free, and contains only:

```text
deviceItem
networkInterface
node
subnet
ioSystem
communicationConnection
```

`deviceName` is allowed only when every requested kind is device-scoped (`deviceItem`, `networkInterface`, `node`, or `communicationConnection`). `pageSize` defaults to 50 and accepts 1 through 200. `cursor` is opaque and is bound to the normalized filter, deterministic ordering, and current snapshot fingerprint.

`inspect_network_object` accepts exactly one `target`. `attributeNames`, when present, is non-empty, duplicate-free, case-sensitive, and limited to 200 names until live evidence justifies changing that number.

### Shared selector DTO shape

```csharp
public sealed class DeviceItemPathSegmentInfo
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public int PositionNumber { get; set; }
    public string TypeIdentifier { get; set; } = string.Empty;
}

public sealed class NetworkObjectSelectorInfo
{
    public string Kind { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public List<DeviceItemPathSegmentInfo>? ItemPath { get; set; }
    public string? InterfaceName { get; set; }
    public string? InterfaceType { get; set; }
    public string? InterfaceOperatingMode { get; set; }
    public string? NodeId { get; set; }
    public string? SubnetId { get; set; }
    public int? Number { get; set; }
    public int? ConnectionIndex { get; set; }
    public string? ConnectionType { get; set; }
    public string? LocalConnectionName { get; set; }
    public string? LocalConnectionId { get; set; }
}
```

Selector requirements by `kind`:

| Kind | Required fields | Forbidden selector fields |
| --- | --- | --- |
| `deviceItem` | `deviceName`, non-empty `itemPath` | interface, node, subnet, IO-system, and connection fields |
| `networkInterface` | `deviceName`, non-empty `itemPath`; optional captured interface name/type/mode evidence | node, subnet, IO-system, and connection fields |
| `node` | `deviceName`, `nodeId` | item-path, interface, subnet, IO-system, and connection fields |
| `subnet` | `subnetId` | device, item-path, interface, node, IO-system, and connection fields |
| `ioSystem` | `subnetId`, `number` | device, item-path, interface, node, and connection fields |
| `communicationConnection` | `deviceName`, non-empty `itemPath`, `connectionIndex`, `connectionType`, `localConnectionName`; `localConnectionId` when the concrete API exposes it | interface, node, subnet, and IO-system fields |

### Discovery and inspection result shapes

```csharp
public sealed class NetworkObjectSummaryInfo
{
    public string Kind { get; set; } = string.Empty;
    public bool Selectable { get; set; }
    public NetworkObjectSelectorInfo? Selector { get; set; }
    public NetworkObjectEvidenceInfo Evidence { get; set; } = new();
    public List<string> Diagnostics { get; set; } = new();
}

public sealed class NetworkObjectEvidenceInfo
{
    public string? Name { get; set; }
    public string? TypeIdentifier { get; set; }
    public int? PositionNumber { get; set; }
    public string? Address { get; set; }
    public List<string> DeviceItemPath { get; set; } = new();
    public string? InterfaceName { get; set; }
    public string? InterfaceType { get; set; }
    public string? InterfaceOperatingMode { get; set; }
    public string? NodeName { get; set; }
    public string? NodeType { get; set; }
    public string? SubnetName { get; set; }
    public string? NetworkType { get; set; }
    public string? IoSystemName { get; set; }
    public string? IoControllerName { get; set; }
    public bool? ConnectionIsValid { get; set; }
    public string? LocalEndpointName { get; set; }
    public string? PartnerEndpointName { get; set; }
    public string? LocalSubnetName { get; set; }
    public string? PartnerSubnetName { get; set; }
}

public sealed class NetworkObjectListInfo
{
    public List<NetworkObjectSummaryInfo> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int ReturnedCount { get; set; }
    public string? NextCursor { get; set; }
}

public sealed class NetworkObjectInspectionInfo
{
    public NetworkObjectSelectorInfo Target { get; set; } = new();
    public NetworkObjectEvidenceInfo Evidence { get; set; } = new();
    public List<NetworkAttributeInfo> Attributes { get; set; } = new();
    public List<string> Messages { get; set; } = new();
}

public sealed class NetworkAttributeInfo
{
    public string Name { get; set; } = string.Empty;
    public string? Source { get; set; }
    public string Access { get; set; } = string.Empty;
    public List<string> SupportedTypes { get; set; } = new();
    public string Availability { get; set; } = string.Empty;
    public NetworkAttributeValueInfo? Value { get; set; }
    public NetworkAttributeDiagnosticInfo? Diagnostic { get; set; }
}

public sealed class NetworkAttributeValueInfo
{
    public string Kind { get; set; } = string.Empty;
    public object? Value { get; set; }
    public string? TypeName { get; set; }
}

public sealed class NetworkEnumValueInfo
{
    public string TypeName { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public long NumericValue { get; set; }
}

public sealed class NetworkAttributeDiagnosticInfo
{
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ClrTypeName { get; set; }
}
```

`NetworkAttributeValueInfo.Value` contains only `null`, a JSON string, boolean, integer, number, or the `NetworkEnumValueInfo` object. After host deserialization, primitive/object values may be represented by `JsonElement`; `NetworkPayloadContract` must validate the element against `Kind` before returning it.

`NetworkAttributeInfo.Source` is null only for a specifically requested name whose availability is `unknownAttribute`, because neither modeled nor dynamic inspection recognizes it. For every recognized attribute it is exactly `modeled`, `dynamic`, or `modeledAndDynamic`. An unknown entry also has `access: "unknown"`, an empty `supportedTypes` list, no value, and an `unknown_attribute` diagnostic. This closes the otherwise impossible combination of an explicit unknown attribute with a source that claims to recognize it.

---

## Task 1: Define and Validate the Phase 3 Public Contract

**Files:**

- Create: `TiaMcpServer.Contracts/NetworkObjectKinds.cs`
- Create: `TiaMcpServer.Contracts/NetworkObjectSelectorInfo.cs`
- Create: `TiaMcpServer.Contracts/NetworkObjectEvidenceInfo.cs`
- Create: `TiaMcpServer.Contracts/NetworkObjectSummaryInfo.cs`
- Create: `TiaMcpServer.Contracts/NetworkObjectListInfo.cs`
- Create: `TiaMcpServer.Contracts/NetworkObjectInspectionInfo.cs`
- Create: `TiaMcpServer.Contracts/NetworkAttributeInfo.cs`
- Modify: `TiaMcpServer/Network/NetworkOperationRequest.cs`
- Modify: `TiaMcpServer/Network/NetworkOperationCatalog.cs`
- Create: `TiaMcpServer.Tests/NetworkPhase3ContractTests.cs`
- Modify: `TiaMcpServer.Tests/NetworkOperationRequestJsonTests.cs`
- Modify: `TiaMcpServer.Tests/NetworkOperationCatalogTests.cs`

- [ ] **Step 1: Write the failing DTO and request-schema tests.**

Add tests that construct every selector kind, serialize it with the repository JSON options, and assert exact camel-case property names. Add a round-trip test proving a serialized `NetworkObjectSelectorInfo` can deserialize into the strict host `NetworkObjectTarget` with no extra-field tolerance.

```csharp
[Theory]
[InlineData("deviceItem")]
[InlineData("networkInterface")]
[InlineData("node")]
[InlineData("subnet")]
[InlineData("ioSystem")]
[InlineData("communicationConnection")]
public void Selector_output_round_trips_into_strict_request_target(string kind)
{
    var selector = Phase3Fixtures.ValidSelector(kind);
    var json = CanonicalJson.Serialize(selector);
    var target = CanonicalJson.Deserialize<NetworkObjectTarget>(json);

    Assert.NotNull(target);
    Assert.Equal(kind, target!.Kind);
}
```

Add catalog cases for missing/empty/duplicate/unknown `objectKinds`, illegal global `deviceName`, `pageSize` 0 and 201, cursor without list, missing inspect target, empty/duplicate/over-200 `attributeNames`, every valid selector, every missing required selector field, and every inapplicable selector field.

- [ ] **Step 2: Run the focused tests and observe the intended red result.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~NetworkPhase3ContractTests|FullyQualifiedName~NetworkOperationRequestJsonTests|FullyQualifiedName~NetworkOperationCatalogTests"
```

Expected: compilation fails because the new contract types, request fields, and operation specifications do not exist.

- [ ] **Step 3: Add the shared contract vocabulary exactly as locked above.**

`NetworkObjectKinds` is a constant holder plus an ordinal set used by both host validation and worker dispatch:

```csharp
public static class NetworkObjectKinds
{
    public const string DeviceItem = "deviceItem";
    public const string NetworkInterface = "networkInterface";
    public const string Node = "node";
    public const string Subnet = "subnet";
    public const string IoSystem = "ioSystem";
    public const string CommunicationConnection = "communicationConnection";

    public static readonly IReadOnlyCollection<string> All = new[]
    {
        DeviceItem,
        NetworkInterface,
        Node,
        Subnet,
        IoSystem,
        CommunicationConnection,
    };
}
```

Use ordinary mutable DTO properties and initialized lists, matching the existing Contracts style. Do not add Siemens references or System.Text.Json attributes to the Contracts project.

- [ ] **Step 4: Generalize the existing host target without breaking Phase 2 JSON.**

Rename the CLR type `NetworkDeviceTarget` to `NetworkObjectTarget`, keep the request property named `Target`, and add the selector fields from the locked shape. Add a strict `NetworkDeviceItemPathSegment` request type with `[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]`.

```csharp
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class NetworkObjectTarget
{
    public string? Kind { get; set; }
    public string? DeviceName { get; set; }
    public IReadOnlyList<NetworkDeviceItemPathSegment>? ItemPath { get; set; }
    public string? InterfaceName { get; set; }
    public string? InterfaceType { get; set; }
    public string? InterfaceOperatingMode { get; set; }
    public string? NodeId { get; set; }
    public string? SubnetId { get; set; }
    public int? Number { get; set; }
    public int? ConnectionIndex { get; set; }
    public string? ConnectionType { get; set; }
    public string? LocalConnectionName { get; set; }
    public string? LocalConnectionId { get; set; }
}
```

Add `ObjectKinds`, `PageSize`, `Cursor`, and `AttributeNames` to `NetworkOperationRequest`. Reuse its existing top-level `DeviceName` for list filtering and existing `Target` for inspection.

- [ ] **Step 5: Register the two operations and implement operation-specific validation.**

Use these exact allowed-field sets:

```csharp
list_network_objects:
    operationId, operation, projectPath, objectKinds, deviceName, pageSize, cursor

inspect_network_object:
    operationId, operation, projectPath, target, attributeNames
```

Validation order is deterministic: common fields, operation allowed fields, required fields, collection cardinality/duplicates, then selector shape. Error messages name the failing field and operation. `configure_network_device` accepts `target.kind` only when it is absent or exactly `node`; all other new target fields remain inapplicable there.

- [ ] **Step 6: Run the focused tests to green.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~NetworkPhase3ContractTests|FullyQualifiedName~NetworkOperationRequestJsonTests|FullyQualifiedName~NetworkOperationCatalogTests"
```

Expected: all selected tests pass.

- [ ] **Step 7: Review and commit Task 1.**

```powershell
git diff --check
git diff -- TiaMcpServer.Contracts TiaMcpServer/Network TiaMcpServer.Tests
git add TiaMcpServer.Contracts/NetworkObjectKinds.cs TiaMcpServer.Contracts/NetworkObjectSelectorInfo.cs TiaMcpServer.Contracts/NetworkObjectEvidenceInfo.cs TiaMcpServer.Contracts/NetworkObjectSummaryInfo.cs TiaMcpServer.Contracts/NetworkObjectListInfo.cs TiaMcpServer.Contracts/NetworkObjectInspectionInfo.cs TiaMcpServer.Contracts/NetworkAttributeInfo.cs TiaMcpServer/Network/NetworkOperationRequest.cs TiaMcpServer/Network/NetworkOperationCatalog.cs TiaMcpServer.Tests/NetworkPhase3ContractTests.cs TiaMcpServer.Tests/NetworkOperationRequestJsonTests.cs TiaMcpServer.Tests/NetworkOperationCatalogTests.cs
git commit -m "feat: define network phase 3 contracts"
```

Expected: only Task 1 files are committed.

---

## Task 2: Route Typed Phase 3 Reads Through the Host and FakeWorker

**Files:**

- Modify: `TiaMcpServer.Contracts/WorkerRequest.cs`
- Modify: `TiaMcpServer.Contracts/OperationPolicyCatalog.cs`
- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs`
- Modify: `TiaMcpServer/Network/NetworkWorkerInvoker.cs`
- Modify: `TiaMcpServer/Network/NetworkPayloadContract.cs`
- Modify: `TiaMcpServer/Network/NetworkReadTools.cs`
- Modify: `TiaMcpServer.FakeWorker/Program.cs`
- Modify: `TiaMcpServer.Tests/NetworkFieldForwardingTests.cs`
- Modify: `TiaMcpServer.Tests/NetworkPayloadContractTests.cs`
- Modify: `TiaMcpServer.Tests/NetworkOperationFakeWorkerTests.cs`
- Modify: `TiaMcpServer.Tests/NetworkStructuredProtocolTests.cs`
- Modify: `TiaMcpServer.Tests/ReadOnlyModeTests.cs`
- Modify: `TiaMcpServer.Tests/ReadOnlyModeHardeningTests.cs`
- Modify: `TiaMcpServer.Tests/McpToolSchemaTests.cs`

- [ ] **Step 1: Write failing forwarding, payload, authorization, and protocol tests.**

Assert that:

1. list fields reach a `WorkerRequest` without renaming or reordering their arrays;
2. inspect target and attribute names reach a `WorkerRequest` unchanged;
3. `NetworkPayloadContract` accepts only `NetworkObjectListInfo` for `list_network_objects` and only `NetworkObjectInspectionInfo` for `inspect_network_object`;
4. malformed JSON, wrong top-level types, invalid value-kind/value pairs, and trailing JSON return `protocol_error` without the rejected payload in the envelope;
5. both worker methods are authorized in read-only mode;
6. the server still advertises the same public MCP tool count; and
7. FakeWorker round trips return real nested objects rather than JSON strings.

- [ ] **Step 2: Run the focused tests and observe red.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~NetworkFieldForwardingTests|FullyQualifiedName~NetworkPayloadContractTests|FullyQualifiedName~NetworkOperationFakeWorkerTests|FullyQualifiedName~NetworkStructuredProtocolTests|FullyQualifiedName~ReadOnlyModeTests|FullyQualifiedName~ReadOnlyModeHardeningTests|FullyQualifiedName~McpToolSchemaTests"
```

Expected: failures show unrecognized operations and absent worker request fields/methods.

- [ ] **Step 3: Extend the internal worker request.**

Add only worker-bound representations; do not reuse the host's System.Text.Json-decorated request class.

```csharp
public List<string>? NetworkObjectKinds { get; set; }
public string? NetworkObjectDeviceName { get; set; }
public int? NetworkObjectPageSize { get; set; }
public string? NetworkObjectCursor { get; set; }
public NetworkObjectSelectorInfo? NetworkObjectTarget { get; set; }
public List<string>? NetworkAttributeNames { get; set; }
```

Map host `NetworkObjectTarget` to a fresh `NetworkObjectSelectorInfo`; copy every item-path segment and never retain a mutable caller-owned list.

- [ ] **Step 4: Add host client and invoker routes.**

```csharp
"list_network_objects" => await worker.ListNetworkObjectsAsync(
    operation.ObjectKinds!, operation.DeviceName, operation.PageSize,
    operation.Cursor, operation.ProjectPath, cancellationToken),

"inspect_network_object" => await worker.InspectNetworkObjectAsync(
    MapSelector(operation.Target!), operation.AttributeNames,
    operation.ProjectPath, cancellationToken),
```

The client sends worker method names `list_network_objects` and `inspect_network_object`. Use the existing bound-project request helper and timeout policy; do not add another worker process or transport.

- [ ] **Step 5: Register exact payload decoders and validators.**

Extend the operation switch in `NetworkPayloadContract`:

```csharp
"list_network_objects" => DecodeAndValidate<NetworkObjectListInfo>(
    payload, ValidateObjectList),
"inspect_network_object" => DecodeAndValidate<NetworkObjectInspectionInfo>(
    payload, ValidateObjectInspection),
```

Validation rejects null lists, unknown kinds/vocabulary values, selectable summaries without a selector, unselectable summaries with a selector, selector-shape mismatches, `ReturnedCount != Items.Count`, negative counts, `ReturnedCount > TotalCount`, duplicate attribute names, and value objects inconsistent with their `kind`.

- [ ] **Step 6: Add FakeWorker success and malformed-payload fixtures.**

Use deterministic fixtures containing one object of every kind and attributes covering all six value kinds plus `unknownAttribute`, `readFailed`, and `unrepresentable`. Add a large list scenario for later budget tests, but do not implement pagination logic in FakeWorker beyond returning the scripted cursor supplied by the fixture.

- [ ] **Step 7: Run the focused tests to green.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~NetworkFieldForwardingTests|FullyQualifiedName~NetworkPayloadContractTests|FullyQualifiedName~NetworkOperationFakeWorkerTests|FullyQualifiedName~NetworkStructuredProtocolTests|FullyQualifiedName~ReadOnlyModeTests|FullyQualifiedName~ReadOnlyModeHardeningTests|FullyQualifiedName~McpToolSchemaTests"
```

Expected: all selected tests pass; the real Openness worker does not implement the two methods yet, which is closed in Tasks 4 and 6.

- [ ] **Step 8: Review and commit Task 2.**

```powershell
git diff --check
git add TiaMcpServer.Contracts/WorkerRequest.cs TiaMcpServer.Contracts/OperationPolicyCatalog.cs TiaMcpServer/Worker/OpennessWorkerClient.cs TiaMcpServer/Network/NetworkWorkerInvoker.cs TiaMcpServer/Network/NetworkPayloadContract.cs TiaMcpServer/Network/NetworkReadTools.cs TiaMcpServer.FakeWorker/Program.cs TiaMcpServer.Tests/NetworkFieldForwardingTests.cs TiaMcpServer.Tests/NetworkPayloadContractTests.cs TiaMcpServer.Tests/NetworkOperationFakeWorkerTests.cs TiaMcpServer.Tests/NetworkStructuredProtocolTests.cs TiaMcpServer.Tests/ReadOnlyModeTests.cs TiaMcpServer.Tests/ReadOnlyModeHardeningTests.cs TiaMcpServer.Tests/McpToolSchemaTests.cs
git commit -m "feat: route network discovery and inspection reads"
```

---

## Task 3: Enrich Hardware Reads With Deterministic Selectors

**Files:**

- Modify: `TiaMcpServer.Contracts/HardwareConfigInfo.cs`
- Create: `TiaMcpServer.OpennessWorker/NetworkSelectorFactory.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/HardwareConfigReader.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
- Create: `TiaMcpServer.Tests/NetworkSelectorFactoryTests.cs`
- Modify: `TiaMcpServer.Tests/HardwareConfigInfoTests.cs`
- Modify: `TiaMcpServer.Tests/NetworkPayloadContractTests.cs`
- Create: `TiaMcpServer.Tests/NetworkPhase3SafetySnapshotTests.cs`

- [ ] **Step 1: Write failing selector-factory and hardware-payload tests.**

Cover nested and duplicate-name device items, zero-based sibling indices, name/position/type evidence, one network interface per owner item, node/subnet/IO-system selectors, and explicitly unselectable objects with diagnostics. Add a safety snapshot regression proving two serializations of the same enriched typed hardware state produce the same state hash.

- [ ] **Step 2: Link the new Siemens-free worker source and observe red.**

Add an explicit `<Compile Include="..\TiaMcpServer.OpennessWorker\NetworkSelectorFactory.cs" Link="Network\NetworkSelectorFactory.cs" />` entry to the test project, then run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~NetworkSelectorFactoryTests|FullyQualifiedName~HardwareConfigInfoTests|FullyQualifiedName~NetworkPayloadContractTests|FullyQualifiedName~NetworkPhase3SafetySnapshotTests"
```

Expected: compilation or assertions fail because hardware DTOs do not expose selectors.

- [ ] **Step 3: Add selector metadata to the existing hardware DTOs.**

Add these properties to `DeviceItemInfo`, `NetworkInterfaceInfo`, `NodeInfo`, `SubnetInfo`, and `IoSystemInfo`:

```csharp
public bool Selectable { get; set; }
public NetworkObjectSelectorInfo? Selector { get; set; }
public List<string> SelectorDiagnostics { get; set; } = new();
```

Do not add attribute values. `read_hardware_config` remains a lightweight hierarchy and identity index.

- [ ] **Step 4: Implement the pure selector factory.**

```csharp
public static NetworkObjectSelectorInfo DeviceItem(
    string deviceName,
    IReadOnlyList<DeviceItemPathSegmentInfo> itemPath) => new()
{
    Kind = NetworkObjectKinds.DeviceItem,
    DeviceName = deviceName,
    ItemPath = itemPath.Select(Clone).ToList(),
};

public static NetworkObjectSelectorInfo NetworkInterface(
    string deviceName,
    IReadOnlyList<DeviceItemPathSegmentInfo> itemPath,
    string? interfaceName,
    string? interfaceType,
    string? interfaceOperatingMode) => new()
{
    Kind = NetworkObjectKinds.NetworkInterface,
    DeviceName = deviceName,
    ItemPath = itemPath.Select(Clone).ToList(),
    InterfaceName = interfaceName,
    InterfaceType = interfaceType,
    InterfaceOperatingMode = interfaceOperatingMode,
};

public static NetworkObjectSelectorInfo Node(string deviceName, string nodeId) =>
    new() { Kind = NetworkObjectKinds.Node, DeviceName = deviceName, NodeId = nodeId };

public static NetworkObjectSelectorInfo Subnet(string subnetId) =>
    new() { Kind = NetworkObjectKinds.Subnet, SubnetId = subnetId };

public static NetworkObjectSelectorInfo IoSystem(string subnetId, int number) =>
    new() { Kind = NetworkObjectKinds.IoSystem, SubnetId = subnetId, Number = number };
```

The factory rejects blank evidence, negative indices, and empty item paths with `ArgumentException`; it clones all lists.

- [ ] **Step 5: Refactor `HardwareConfigReader` traversal to carry path evidence.**

At each `DeviceItems` composition, enumerate once with a zero-based index, append this segment, and recurse:

```csharp
var segment = new DeviceItemPathSegmentInfo
{
    Index = siblingIndex,
    Name = item.Name ?? string.Empty,
    PositionNumber = item.PositionNumber,
    TypeIdentifier = item.TypeIdentifier ?? string.Empty,
};
```

Preserve device-item composition order because the index refers to that order. Sort devices by `Name` ordinal, nodes by `NodeId` ordinal, subnets by `SubnetId` ordinal, and IO systems by `Number` then `Name` ordinal before returning. The one-interface-per-item collection remains a zero-or-one list. When required evidence is missing, emit the object with `Selectable = false`, `Selector = null`, and one deterministic diagnostic instead of inventing a selector.

- [ ] **Step 6: Run focused tests and the serial stub build.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~NetworkSelectorFactoryTests|FullyQualifiedName~HardwareConfigInfoTests|FullyQualifiedName~NetworkPayloadContractTests|FullyQualifiedName~NetworkPhase3SafetySnapshotTests"
dotnet build TiaMcpServer.sln --no-restore -m:1 /p:UseTiaPortalReferenceStubs=true
```

Expected: selected tests and stub build pass. This proves contract shape and compile-time API compatibility, not live selector resolution.

- [ ] **Step 7: Review and commit Task 3.**

```powershell
git diff --check
git add TiaMcpServer.Contracts/HardwareConfigInfo.cs TiaMcpServer.OpennessWorker/NetworkSelectorFactory.cs TiaMcpServer.OpennessWorker/Openness/HardwareConfigReader.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj TiaMcpServer.Tests/NetworkSelectorFactoryTests.cs TiaMcpServer.Tests/HardwareConfigInfoTests.cs TiaMcpServer.Tests/NetworkPayloadContractTests.cs TiaMcpServer.Tests/NetworkPhase3SafetySnapshotTests.cs
git commit -m "feat: expose deterministic network object selectors"
```

---

## Task 4: Implement Bounded Discovery and Cursor Validation

**Files:**

- Create: `TiaMcpServer.OpennessWorker/NetworkObjectCursorCodec.cs`
- Create: `TiaMcpServer.OpennessWorker/NetworkObjectPageBuilder.cs`
- Create: `TiaMcpServer.OpennessWorker/Openness/NetworkObjectIndexReader.cs`
- Modify: `TiaMcpServer.OpennessWorker/Program.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
- Create: `TiaMcpServer.Tests/NetworkObjectCursorCodecTests.cs`
- Create: `TiaMcpServer.Tests/NetworkObjectPageBuilderTests.cs`
- Create: `TiaMcpServer.Tests/NetworkPhase3WorkerDispatchTests.cs`

- [ ] **Step 1: Write failing pure paging and cursor tests.**

Test default page size 50, explicit sizes 1 and 200, stable ordinal kind/selector ordering, exact `TotalCount`/`ReturnedCount`, last-page null cursor, filter mismatch, snapshot mismatch, malformed base64, unsupported cursor version, negative/out-of-range offset, and a page that begins after an unselectable summary.

- [ ] **Step 2: Link the two Siemens-free helpers and observe red.**

Add explicit test-project links for `NetworkObjectCursorCodec.cs` and `NetworkObjectPageBuilder.cs`, then run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~NetworkObjectCursorCodecTests|FullyQualifiedName~NetworkObjectPageBuilderTests|FullyQualifiedName~NetworkPhase3WorkerDispatchTests"
```

Expected: compilation fails because paging, cursor, and worker dispatch do not exist.

- [ ] **Step 3: Implement an opaque versioned cursor.**

The decoded internal payload is exactly:

```csharp
internal sealed class NetworkObjectCursorPayload
{
    public int Version { get; set; } = 1;
    public int Offset { get; set; }
    public string QueryHash { get; set; } = string.Empty;
    public string SnapshotHash { get; set; } = string.Empty;
}
```

Encode compact JSON as UTF-8 Base64Url without padding. Decode fail-closed and require version 1, non-negative offset, and 64-character lowercase hexadecimal SHA-256 hashes. Error categories are `invalid_cursor`, `cursor_filter_mismatch`, `cursor_snapshot_mismatch`, and `cursor_out_of_range`; do not expose raw cursor contents in diagnostics.

Normalize the query before hashing: sort `objectKinds` ordinal, retain a case-sensitive `deviceName`, and include the fixed ordering-version string `network-object-order-v1`. Build the snapshot fingerprint from the ordered selector/evidence keys and selectability flags, not attribute values.

- [ ] **Step 4: Implement the pure page builder.**

```csharp
public static NetworkObjectListInfo Build(
    IReadOnlyList<NetworkObjectSummaryInfo> orderedItems,
    int pageSize,
    int offset,
    string queryHash,
    string snapshotHash)
{
    if (offset > orderedItems.Count)
        throw new NetworkCursorException("cursor_out_of_range");

    var page = orderedItems.Skip(offset).Take(pageSize).ToList();
    var nextOffset = offset + page.Count;
    return new NetworkObjectListInfo
    {
        Items = page,
        TotalCount = orderedItems.Count,
        ReturnedCount = page.Count,
        NextCursor = nextOffset < orderedItems.Count
            ? NetworkObjectCursorCodec.Encode(nextOffset, queryHash, snapshotHash)
            : null,
    };
}
```

- [ ] **Step 5: Implement source-filtered worker discovery.**

`NetworkObjectIndexReader` traverses only requested kinds. If `deviceName` is supplied, locate that device first and do not traverse other devices. Build flat summaries without attribute values. Use these stable ordering keys:

```text
deviceItem: deviceName + each itemPath.index
networkInterface: deviceName + each itemPath.index
node: deviceName + nodeId
subnet: subnetId
ioSystem: subnetId + zero-padded number
communicationConnection: deviceName + itemPath indexes + zero-padded connectionIndex
```

At this task, the connection branch returns an empty sequence; Task 7 fills it before Phase 3 can be complete. All other branches share selector construction with `HardwareConfigReader` rather than duplicating identity rules.

- [ ] **Step 6: Add real worker dispatch for `list_network_objects`.**

Add the method to `Program.HandleLine` and a narrow handler that obtains the bound `Project`, validates that required worker fields are present, invokes `NetworkObjectIndexReader`, and returns `Success<NetworkObjectListInfo>`. Worker-side validation is defense in depth; host validation remains authoritative for user errors.

- [ ] **Step 7: Run the focused tests and stub build.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~NetworkObjectCursorCodecTests|FullyQualifiedName~NetworkObjectPageBuilderTests|FullyQualifiedName~NetworkPhase3WorkerDispatchTests"
dotnet build TiaMcpServer.sln --no-restore -m:1 /p:UseTiaPortalReferenceStubs=true
```

Expected: tests and stub build pass. Live ordering and snapshot behavior remain unverified.

- [ ] **Step 8: Review and commit Task 4.**

```powershell
git diff --check
git add TiaMcpServer.OpennessWorker/NetworkObjectCursorCodec.cs TiaMcpServer.OpennessWorker/NetworkObjectPageBuilder.cs TiaMcpServer.OpennessWorker/Openness/NetworkObjectIndexReader.cs TiaMcpServer.OpennessWorker/Program.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj TiaMcpServer.Tests/NetworkObjectCursorCodecTests.cs TiaMcpServer.Tests/NetworkObjectPageBuilderTests.cs TiaMcpServer.Tests/NetworkPhase3WorkerDispatchTests.cs
git commit -m "feat: add bounded network object discovery"
```

---

## Task 5: Normalize and Merge Attribute Results Without Siemens Dependencies

**Files:**

- Create: `TiaMcpServer.OpennessWorker/NetworkAttributeValueNormalizer.cs`
- Create: `TiaMcpServer.OpennessWorker/NetworkAttributeResultBuilder.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
- Create: `TiaMcpServer.Tests/NetworkAttributeValueNormalizerTests.cs`
- Create: `TiaMcpServer.Tests/NetworkAttributeResultBuilderTests.cs`

- [ ] **Step 1: Write failing normalization and merge tests.**

Cover null; string; bool; every signed integer width; non-overflowing unsigned integers; `float`, `double`, and `decimal`; enum type/symbol/numeric value; `ulong > long.MaxValue`; arrays; dates; GUIDs; arbitrary objects; throwing modeled readers; duplicate modeled/dynamic names; access-mode combinations; sorted supported type names; and a failed attribute followed by a successful one.

- [ ] **Step 2: Link the Siemens-free helpers and observe red.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~NetworkAttributeValueNormalizerTests|FullyQualifiedName~NetworkAttributeResultBuilderTests"
```

Expected: compilation fails because the normalization and merge helpers do not exist.

- [ ] **Step 3: Implement lossless value normalization.**

Return a discriminated result instead of throwing:

```csharp
public sealed class NetworkAttributeNormalizationResult
{
    public bool IsRepresentable { get; set; }
    public NetworkAttributeValueInfo? Value { get; set; }
    public string? ClrTypeName { get; set; }
}
```

Exact mappings:

| CLR input | Public kind/value |
| --- | --- |
| `null` | `null` / `null` |
| `string`, `char` | `string` / exact text |
| `bool` | `boolean` / boolean |
| `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, safe `ulong` | `integer` / `Int64` |
| `float`, `double`, `decimal` when finite | `number` / original numeric value |
| enum with `Int64`-representable underlying value | `enum` / `NetworkEnumValueInfo` |
| everything else, overflow, NaN, or infinity | unrepresentable; no public value |

Never invoke the source object's `ToString()` except for enum symbol retrieval through `Enum.GetName`; unnamed enum values use an empty symbol and retain the numeric value.

- [ ] **Step 4: Implement modeled/dynamic merging and availability.**

The builder accepts modeled observations and dynamic metadata/read observations and groups by exact ordinal name. When `attributeNames` is absent, emit the merged set in ordinal name order. When it is present, emit exactly one entry per requested name in request order. Source values are `modeled`, `dynamic`, or `modeledAndDynamic` for recognized attributes and null only for `unknownAttribute`. Access values are `none`, `readOnly`, `writeOnly`, `readWrite`, or `unknown`. Availability values are exactly `available`, `notApplicable`, `unsupported`, `unreadable`, `readFailed`, `unrepresentable`, or `unknownAttribute`.

Precedence for the value is: successful modeled read, then successful dynamic read. A source disagreement becomes a diagnostic and retains the modeled value; it never silently overwrites it. Requested names absent from both sources produce an explicit `unknownAttribute` entry at that name's position in the requested sequence.

- [ ] **Step 5: Run focused tests to green.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~NetworkAttributeValueNormalizerTests|FullyQualifiedName~NetworkAttributeResultBuilderTests"
```

Expected: all selected tests pass, including continuation after a throwing reader.

- [ ] **Step 6: Review and commit Task 5.**

```powershell
git diff --check
git add TiaMcpServer.OpennessWorker/NetworkAttributeValueNormalizer.cs TiaMcpServer.OpennessWorker/NetworkAttributeResultBuilder.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj TiaMcpServer.Tests/NetworkAttributeValueNormalizerTests.cs TiaMcpServer.Tests/NetworkAttributeResultBuilderTests.cs
git commit -m "feat: normalize network attribute values"
```

---

## Task 6: Resolve and Inspect the Five Core Network Object Kinds

**Files:**

- Create: `TiaMcpServer.OpennessWorker/NetworkModeledAttributeCatalog.cs`
- Create: `TiaMcpServer.OpennessWorker/Openness/ResolvedNetworkObject.cs`
- Create: `TiaMcpServer.OpennessWorker/Openness/NetworkObjectSelectorResolver.cs`
- Create: `TiaMcpServer.OpennessWorker/Openness/EngineeringAttributeInspector.cs`
- Create: `TiaMcpServer.OpennessWorker/Openness/NetworkModeledAttributeAdapters.cs`
- Create: `TiaMcpServer.OpennessWorker/Openness/NetworkObjectInspector.cs`
- Modify: `TiaMcpServer.OpennessWorker/Program.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
- Create: `TiaMcpServer.Tests/NetworkModeledAttributeCatalogTests.cs`
- Modify: `TiaMcpServer.Tests/NetworkPhase3WorkerDispatchTests.cs`
- Modify: `TiaMcpServer.Tests/NetworkPayloadContractTests.cs`

- [ ] **Step 1: Write failing modeled-catalog and worker-dispatch tests.**

Assert exact modeled names by kind, no duplicate names, deterministic ordering, and that each descriptor declares one supported CLR type and a reader. Add structural dispatch tests for `inspect_network_object`, plus payload cases where one attribute fails and later attributes still appear.

- [ ] **Step 2: Observe red.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~NetworkModeledAttributeCatalogTests|FullyQualifiedName~NetworkPhase3WorkerDispatchTests|FullyQualifiedName~NetworkPayloadContractTests"
```

Expected: missing modeled catalog, resolver, inspector, and dispatch. Add an explicit test-project link for the Siemens-free `NetworkModeledAttributeCatalog.cs` before implementing the catalog; do not link the Siemens-dependent adapter.

- [ ] **Step 3: Define the initial modeled attribute catalog.**

Keep the catalog Siemens-free: each descriptor contains the public attribute name, expected CLR type name, and an adapter key. Use these initial modeled names:

```text
deviceItem: Name, TypeIdentifier, PositionNumber, Address, IsBuiltIn, IsPlugged, Classification
networkInterface: Name, InterfaceType, InterfaceOperatingMode
node: Name, NodeId, NodeType
subnet: Name, SubnetId, NetworkType, TypeIdentifier
ioSystem: Name, Number
```

`NetworkModeledAttributeAdapters` maps those keys to typed Siemens-object delegates inside the Openness namespace. If a property is absent from the installed V21 compile-time surface, remove it from both catalog and adapter and rely on dynamic metadata; record that adjustment in the task commit message body. Do not implement a reflection-based public fallback.

- [ ] **Step 4: Implement selector resolution with evidence verification.**

Resolution rules:

1. `deviceItem`: find exactly one device using the existing ordinal-ignore-case device-name semantics; for each segment select the recorded sibling index, then verify exact ordinal name/type identifier and exact position number before descending.
2. `networkInterface`: resolve the owner item exactly as above, then call `GetService<NetworkInterface>()`; verify interface name/type/mode evidence when the emitted selector supplied it.
3. `node`: use the existing Phase 2 node lookup and require exact ordinal `nodeId` after device lookup.
4. `subnet`: use the existing Phase 2 subnet lookup and require exact ordinal `subnetId`.
5. `ioSystem`: resolve subnet first, then exact numeric IO-system number.

Return selection categories `target_not_found`, `target_ambiguous`, `target_evidence_mismatch`, and `target_kind_unsupported`. Never fall back from a failed path segment to a name search.

- [ ] **Step 5: Implement generic dynamic inspection.**

For the resolved `IEngineeringObject`, enumerate `GetAttributeInfos()` once, order metadata by exact name, map `EngineeringAttributeAccessMode` to the approved access vocabulary, order `SupportedTypes` by full type name, and read each selected readable attribute inside its own `try/catch`. Do not call a write method. When `attributeNames` is supplied, read only matching dynamic attributes plus modeled attributes needed for the requested names.

- [ ] **Step 6: Merge modeled and dynamic observations.**

`NetworkObjectInspector` gets the per-kind modeled descriptors and typed adapter readers, invokes each selected descriptor independently, obtains dynamic observations from `EngineeringAttributeInspector`, and delegates normalization/merging to Task 5 helpers. It returns the verified selector as `Target`, current typed `Evidence`, and `Messages`; it never returns a selector rebuilt from unverified request text.

- [ ] **Step 7: Add real worker dispatch for `inspect_network_object`.**

The handler obtains the bound project, invokes the resolver and inspector, and returns `Success<NetworkObjectInspectionInfo>`. Selection failures use the existing worker failure envelope and stable categories; per-attribute failures remain successful inspection payload entries.

- [ ] **Step 8: Run focused tests and stub build.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~NetworkModeledAttributeCatalogTests|FullyQualifiedName~NetworkPhase3WorkerDispatchTests|FullyQualifiedName~NetworkPayloadContractTests|FullyQualifiedName~NetworkAttribute"
dotnet build TiaMcpServer.sln --no-restore -m:1 /p:UseTiaPortalReferenceStubs=true
```

Expected: tests and stub build pass. Dynamic metadata behavior is still live-unverified.

- [ ] **Step 9: Review and commit Task 6.**

```powershell
git diff --check
git add TiaMcpServer.OpennessWorker/NetworkModeledAttributeCatalog.cs TiaMcpServer.OpennessWorker/Openness/ResolvedNetworkObject.cs TiaMcpServer.OpennessWorker/Openness/NetworkObjectSelectorResolver.cs TiaMcpServer.OpennessWorker/Openness/EngineeringAttributeInspector.cs TiaMcpServer.OpennessWorker/Openness/NetworkModeledAttributeAdapters.cs TiaMcpServer.OpennessWorker/Openness/NetworkObjectInspector.cs TiaMcpServer.OpennessWorker/Program.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj TiaMcpServer.Tests/NetworkModeledAttributeCatalogTests.cs TiaMcpServer.Tests/NetworkPhase3WorkerDispatchTests.cs TiaMcpServer.Tests/NetworkPayloadContractTests.cs
git commit -m "feat: inspect typed network objects"
```

---

## Task 7: Add Communication-Connection Identity and Inspection

**Files:**

- Create: `TiaMcpServer.Contracts/CommunicationConnectionInfo.cs`
- Create: `TiaMcpServer.OpennessWorker/CommunicationConnectionSelectorFactory.cs`
- Create: `TiaMcpServer.OpennessWorker/ConnectionModeledAttributeCatalog.cs`
- Create: `TiaMcpServer.OpennessWorker/Openness/CommunicationConnectionReader.cs`
- Create: `TiaMcpServer.OpennessWorker/Openness/ConnectionModeledAttributeAdapters.cs`
- Modify: `TiaMcpServer.Contracts/HardwareConfigInfo.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/HardwareConfigReader.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/NetworkObjectIndexReader.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/NetworkObjectSelectorResolver.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/NetworkObjectInspector.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
- Create: `TiaMcpServer.Tests/CommunicationConnectionSelectorFactoryTests.cs`
- Create: `TiaMcpServer.Tests/ConnectionModeledAttributeCatalogTests.cs`
- Modify: `TiaMcpServer.Tests/HardwareConfigInfoTests.cs`
- Modify: `TiaMcpServer.Tests/NetworkObjectPageBuilderTests.cs`
- Modify: `TiaMcpServer.Tests/NetworkPayloadContractTests.cs`

- [ ] **Step 1: Write failing connection selector, modeled-catalog, hardware, list, and payload tests.**

Cover two same-type/name connections distinguished by composition index, optional local connection ID, missing required evidence becoming unselectable, all installed `ConnectionType` enum values, S7-specific local ID/resource fields, HMI without local ID, and stable ordering by owner path plus composition index.

- [ ] **Step 2: Observe red.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~CommunicationConnection|FullyQualifiedName~ConnectionModeledAttributeCatalogTests|FullyQualifiedName~HardwareConfigInfoTests|FullyQualifiedName~NetworkObjectPageBuilderTests|FullyQualifiedName~NetworkPayloadContractTests"
```

Expected: failures show that connection DTOs, selectors, discovery, and inspection are absent. Add explicit test-project links for the Siemens-free `CommunicationConnectionSelectorFactory.cs` and `ConnectionModeledAttributeCatalog.cs`; do not link the Siemens-dependent reader or adapter.

- [ ] **Step 3: Add lightweight connection summaries to hardware results.**

```csharp
public sealed class CommunicationConnectionInfo
{
    public string ConnectionType { get; set; } = string.Empty;
    public string LocalConnectionName { get; set; } = string.Empty;
    public string? LocalConnectionId { get; set; }
    public string? PartnerName { get; set; }
    public bool IsValid { get; set; }
    public bool Selectable { get; set; }
    public NetworkObjectSelectorInfo? Selector { get; set; }
    public List<string> SelectorDiagnostics { get; set; } = new();
}
```

Add `CommunicationConnections` to `DeviceItemInfo`. Do not add full connection attribute values to `read_hardware_config`.

- [ ] **Step 4: Implement connection enumeration and provisional selector construction.**

Resolve `CommunicationManagement` from each owning device item, enumerate its connections once, and use the composition index from that enumeration. The selector factory requires owner device/item path, non-negative index, enum connection type, and local name. Include `localConnectionId` only for a concrete connection type that exposes it. Missing required evidence yields an unselectable summary; do not synthesize a local name or ID.

- [ ] **Step 5: Add per-concrete-type modeled adapters.**

Keep `ConnectionModeledAttributeCatalog` Siemens-free for unit testing, and implement its reader keys in `ConnectionModeledAttributeAdapters` with typed Siemens delegates. The base adapter reads `ConnectionType`, `IsValid`, local/partner endpoint names, and subnet names through typed API properties. Add typed adapters for installed V21 concrete classes such as S7 and HMI connections; the S7 adapter adds local connection ID/resource fields, while HMI does not claim an unavailable local ID. A generic dynamic reader may supplement the adapter but must not replace its typed identity fields.

- [ ] **Step 6: Integrate connections into list and inspect resolution.**

The list branch filters and pages connection summaries like every other kind. Inspection resolves the owner item, selects the recorded connection index, then verifies connection type, local name, and local ID when supplied. On evidence mismatch, fail the whole target resolution with `target_evidence_mismatch`; do not search other connections for a match.

- [ ] **Step 7: Run focused tests and stub build.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~CommunicationConnection|FullyQualifiedName~ConnectionModeledAttributeCatalogTests|FullyQualifiedName~HardwareConfigInfoTests|FullyQualifiedName~NetworkObjectPageBuilderTests|FullyQualifiedName~NetworkPayloadContractTests"
dotnet build TiaMcpServer.sln --no-restore -m:1 /p:UseTiaPortalReferenceStubs=true
```

Expected: tests and stub build pass. Specific connection collections and concrete adapters remain provisional until the live matrix.

- [ ] **Step 8: Review and commit Task 7.**

```powershell
git diff --check
git add TiaMcpServer.Contracts/CommunicationConnectionInfo.cs TiaMcpServer.Contracts/HardwareConfigInfo.cs TiaMcpServer.OpennessWorker/CommunicationConnectionSelectorFactory.cs TiaMcpServer.OpennessWorker/ConnectionModeledAttributeCatalog.cs TiaMcpServer.OpennessWorker/Openness/CommunicationConnectionReader.cs TiaMcpServer.OpennessWorker/Openness/ConnectionModeledAttributeAdapters.cs TiaMcpServer.OpennessWorker/Openness/HardwareConfigReader.cs TiaMcpServer.OpennessWorker/Openness/NetworkObjectIndexReader.cs TiaMcpServer.OpennessWorker/Openness/NetworkObjectSelectorResolver.cs TiaMcpServer.OpennessWorker/Openness/NetworkObjectInspector.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj TiaMcpServer.Tests/CommunicationConnectionSelectorFactoryTests.cs TiaMcpServer.Tests/ConnectionModeledAttributeCatalogTests.cs TiaMcpServer.Tests/HardwareConfigInfoTests.cs TiaMcpServer.Tests/NetworkObjectPageBuilderTests.cs TiaMcpServer.Tests/NetworkPayloadContractTests.cs
git commit -m "feat: inspect communication connections"
```

---

## Task 8: Close Static Protocol, Budget, Safety, and Harness Contracts

**Files:**

- Modify: `TiaMcpServer/Network/NetworkReadTools.cs`
- Modify: `TiaMcpServer.Tests/NetworkStructuredProtocolTests.cs`
- Modify: `TiaMcpServer.Tests/StructuredOperationBatchPayloadBudgetTests.cs`
- Create: `TiaMcpServer.Tests/NetworkPhase3SafetyRegressionTests.cs`
- Modify: `TiaMcpServer.Tests/McpToolSchemaTests.cs`
- Create: `TiaMcpServer.Tests/NetworkPhase3EndToEndTests.cs`
- Create: `scripts/live-test-network-phase3.ps1`
- Create: `TiaMcpServer.Tests/NetworkPhase3LiveHarnessContractTests.cs`
- Create: `TiaMcpServer.OpennessWorker/NetworkAttributeProbeInfo.cs`
- Modify: `TiaMcpServer.Contracts/OperationPolicyCatalog.cs`
- Modify: `TiaMcpServer.Contracts/WorkerRequest.cs`
- Modify: `TiaMcpServer.OpennessWorker/Program.cs`

- [ ] **Step 1: Write failing end-to-end and harness contract tests.**

Add tests for:

- mixed Phase 2 and Phase 3 reads in one ordered batch;
- per-item truncation of an oversized inspection result with no partial JSON;
- aggregate truncation at 180,000 characters with the standard retry hint;
- a paged list whose individual pages stay under 60,000 characters;
- cursor rejection after FakeWorker snapshot change;
- exact equality of canonical text content and `structuredContent`;
- unchanged public MCP tool count and read-only mode exposure;
- unchanged Phase 2 write preview/apply token acceptance for identical state and invalidation for changed hardware state; and
- a live script source contract that contains no `network_write`, `preview_write_batch`, `apply_write_batch`, `confirm`, save, compile, download, or write-mode invocation.

- [ ] **Step 2: Observe red.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~NetworkPhase3EndToEndTests|FullyQualifiedName~NetworkStructuredProtocolTests|FullyQualifiedName~StructuredOperationBatchPayloadBudgetTests|FullyQualifiedName~NetworkPhase3SafetyRegressionTests|FullyQualifiedName~McpToolSchemaTests|FullyQualifiedName~NetworkPhase3LiveHarnessContractTests"
```

Expected: failures identify missing retry guidance, missing Phase 3 harness, and unclosed cross-operation cases.

- [ ] **Step 3: Update retry guidance without weakening budgets.**

For an oversized `read_hardware_config`, recommend `list_network_objects` with narrower `objectKinds`/`deviceName` only while that operation is present. For an oversized inspection, recommend fewer `attributeNames`. Keep the actual 60,000/180,000 limits unchanged and never emit partial selectors or partial attribute arrays.

- [ ] **Step 4: Create the read-only live harness.**

The script exposes exactly these modes:

```powershell
[ValidateSet('Matrix', 'Repeatability', 'MeasureListValue', 'RawProbe')]
[string] $Mode = 'Matrix'
```

It attaches to the active TIA Portal V21 project through the normal MCP server for `Matrix`, `Repeatability`, and `MeasureListValue`. `RawProbe` invokes an internal read-only worker diagnostic path that reports raw `EngineeringAttributeInfo` name, access mode, supported CLR type names, observed CLR value type, and exception category; it never becomes an MCP operation and never returns arbitrary object `ToString()` values.

`Matrix` requires one observed example of: nested device item, network interface, Ethernet node, Ethernet subnet, PROFINET IO system, and communication connection. Missing PROFIBUS/DP or additional connection classes are recorded as explicit live-coverage gaps rather than inferred support.

`Repeatability` runs the same discovery and inspection inputs twice without changing the project and compares canonical bytes. `MeasureListValue` records canonical byte counts, elapsed time, selector counts/completeness, omissions/truncation, request counts for discovery followed by inspection, and connection-discovery usability for the approved gates. The script writes timestamped JSON evidence under `artifacts/live-network-phase3/`, a gitignored directory.

- [ ] **Step 5: Add the internal raw-probe worker route.**

Add `probe_network_object_attributes` only to the worker dispatch and read-only operation policy. It accepts the same internal selector as inspection and returns the internal DTO defined in `TiaMcpServer.OpennessWorker/NetworkAttributeProbeInfo.cs`; the script calls it only through a direct worker session. Do not add it to `NetworkOperationCatalog`, `NetworkReadTools`, the MCP schema, or host public dispatch.

- [ ] **Step 6: Make the static suite green.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~NetworkPhase3EndToEndTests|FullyQualifiedName~NetworkStructuredProtocolTests|FullyQualifiedName~StructuredOperationBatchPayloadBudgetTests|FullyQualifiedName~NetworkPhase3SafetyRegressionTests|FullyQualifiedName~McpToolSchemaTests|FullyQualifiedName~NetworkPhase3LiveHarnessContractTests"
dotnet build TiaMcpServer.sln --no-restore -m:1 /p:UseTiaPortalReferenceStubs=true
```

Expected: all selected tests and stub build pass; no live script has been run.

- [ ] **Step 7: Review and commit Task 8.**

```powershell
git diff --check
git add TiaMcpServer/Network/NetworkReadTools.cs TiaMcpServer.Tests/NetworkStructuredProtocolTests.cs TiaMcpServer.Tests/StructuredOperationBatchPayloadBudgetTests.cs TiaMcpServer.Tests/NetworkPhase3SafetyRegressionTests.cs TiaMcpServer.Tests/McpToolSchemaTests.cs TiaMcpServer.Tests/NetworkPhase3EndToEndTests.cs scripts/live-test-network-phase3.ps1 TiaMcpServer.Tests/NetworkPhase3LiveHarnessContractTests.cs TiaMcpServer.OpennessWorker/NetworkAttributeProbeInfo.cs TiaMcpServer.Contracts/OperationPolicyCatalog.cs TiaMcpServer.Contracts/WorkerRequest.cs TiaMcpServer.OpennessWorker/Program.cs
git commit -m "test: add network phase 3 acceptance harness"
```

---

## Task 9: Run the Separately Authorized Live Gate and Stabilize the Contract

**Files:**

- Modify after evidence: files identified by the live discrepancy, limited to the Phase 3 implementation above
- Modify: `docs/NETWORK_OPERATIONS_ROADMAP.md`
- Modify: `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md`
- Modify: `docs/ARCHITECTURE.md`
- Create: `docs/SupportedOperations/NETWORK_PHASE3_LIVE_ACCEPTANCE.md`

- [ ] **Step 1: Stop and obtain explicit authorization for live TIA testing.**

Do not treat prior approval of this plan as authorization to attach to TIA Portal or run the live script. Confirm that the active project is a disposable/prepared acceptance fixture and that read-only attachment is allowed.

- [ ] **Step 2: Run the four read-only live modes.**

```powershell
pwsh -NoProfile -File scripts/live-test-network-phase3.ps1 -Mode Matrix
pwsh -NoProfile -File scripts/live-test-network-phase3.ps1 -Mode Repeatability
pwsh -NoProfile -File scripts/live-test-network-phase3.ps1 -Mode MeasureListValue
pwsh -NoProfile -File scripts/live-test-network-phase3.ps1 -Mode RawProbe
```

Expected: each command exits 0, produces a timestamped JSON evidence file, performs no write, and covers the required minimum matrix. If a required fixture is absent, classify the gate as incomplete rather than passing it.

- [ ] **Step 3: Apply the objective `list_network_objects` retention gate.**

Keep the operation only if evidence proves at least one condition:

1. it returns complete selectors when `read_hardware_config` exceeds the 60,000-character per-item budget;
2. a representative targeted query is at least 50% smaller in canonical JSON while preserving every matching selector; or
3. a connection-only query returns all connection selectors under budget and avoids one otherwise necessary full-tree call in the measured agent flow.

If none passes, remove `list_network_objects` completely from request validation, host/worker routing, policy, payload registry, FakeWorker, retry guidance, tests, and docs. Use `apply_patch` delete-file patches for `NetworkObjectListInfo.cs`, `NetworkObjectSummaryInfo.cs`, `NetworkObjectCursorCodec.cs`, `NetworkObjectPageBuilder.cs`, `NetworkObjectIndexReader.cs`, and their list-only tests. Retain `NetworkObjectEvidenceInfo`, selectors in `read_hardware_config`, and targeted inspection. Run the focused protocol, schema, read-only, and full test commands again, then stage only the reviewed list-removal paths and commit:

```powershell
git diff --check
git add TiaMcpServer.Contracts/NetworkObjectListInfo.cs TiaMcpServer.Contracts/NetworkObjectSummaryInfo.cs TiaMcpServer.Contracts/WorkerRequest.cs TiaMcpServer.Contracts/OperationPolicyCatalog.cs TiaMcpServer/Network/NetworkOperationRequest.cs TiaMcpServer/Network/NetworkOperationCatalog.cs TiaMcpServer/Network/NetworkWorkerInvoker.cs TiaMcpServer/Network/NetworkPayloadContract.cs TiaMcpServer/Network/NetworkReadTools.cs TiaMcpServer/Worker/OpennessWorkerClient.cs TiaMcpServer.OpennessWorker/NetworkObjectCursorCodec.cs TiaMcpServer.OpennessWorker/NetworkObjectPageBuilder.cs TiaMcpServer.OpennessWorker/Openness/NetworkObjectIndexReader.cs TiaMcpServer.OpennessWorker/Program.cs TiaMcpServer.FakeWorker/Program.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj TiaMcpServer.Tests/NetworkPhase3ContractTests.cs TiaMcpServer.Tests/NetworkOperationRequestJsonTests.cs TiaMcpServer.Tests/NetworkOperationCatalogTests.cs TiaMcpServer.Tests/NetworkFieldForwardingTests.cs TiaMcpServer.Tests/NetworkPayloadContractTests.cs TiaMcpServer.Tests/NetworkOperationFakeWorkerTests.cs TiaMcpServer.Tests/NetworkStructuredProtocolTests.cs TiaMcpServer.Tests/ReadOnlyModeTests.cs TiaMcpServer.Tests/ReadOnlyModeHardeningTests.cs TiaMcpServer.Tests/McpToolSchemaTests.cs TiaMcpServer.Tests/NetworkObjectCursorCodecTests.cs TiaMcpServer.Tests/NetworkObjectPageBuilderTests.cs TiaMcpServer.Tests/NetworkPhase3WorkerDispatchTests.cs TiaMcpServer.Tests/StructuredOperationBatchPayloadBudgetTests.cs TiaMcpServer.Tests/NetworkPhase3EndToEndTests.cs
git commit -m "refactor: remove low-value network object listing"
```

- [ ] **Step 4: Evaluate the raw attribute and selector evidence.**

Pass only when:

- every required selector resolves twice without drift in an unchanged project;
- name/position/type evidence matches the installed object graph;
- access mode and supported type mapping match raw `EngineeringAttributeInfo` metadata;
- all observed values fit the approved typed vocabulary or become `unrepresentable` without a synthetic value;
- a read exception affects only its attribute;
- connection identity distinguishes every observed connection; and
- repeated canonical responses are byte-identical.

If evidence contradicts a selector field, adapter, access mapping, supported type, value kind, connection composition, or page-size assumption, stop stabilization, write the mismatch into the acceptance document, and return to design review with the smallest proposed contract revision. Do not patch an unreviewed live-derived contract change in place.

- [ ] **Step 5: Document verified support and explicit gaps.**

Update the roadmap to mark Phase 3 stable only after the gate passes. The operations summary documents request/result examples, selector scope, per-attribute failure semantics, page/budget behavior, and the final list retention decision. Architecture documents the typed public selector → worker resolver → modeled adapter/generic inspector seam. The live acceptance page records TIA version, fixture kinds, pass/fail criteria, canonical byte measurements, and untested PROFIBUS/DP or connection classes without claiming support or absence.

- [ ] **Step 6: Run the complete non-live verification suite.**

```powershell
dotnet build TiaMcpServer.sln --no-restore -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build
git diff --check
```

Expected: build and all tests pass, and `git diff --check` prints nothing. These results supplement but do not replace the live evidence.

- [ ] **Step 7: Inspect coverage for materially changed Siemens-free logic.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build --collect:"XPlat Code Coverage"
```

Expected: coverage output is generated. Cursor, paging, selector-factory, normalization, merge, catalog, validation, and payload-contract branches should meet the repository threshold; if no repository threshold is configured, target at least 80% branch coverage for those changed Siemens-free files. Do not claim meaningful runtime coverage for Siemens-dependent shells.

- [ ] **Step 8: Perform final scope and safety review.**

Review the complete Phase 3 diff for:

- no new MCP tool;
- no public raw probe;
- no write method in Phase 3 code or script;
- no arbitrary-object `ToString()` publication;
- no malformed worker payload echo;
- no weakened payload budgets;
- no changed safety-token binding or single-use semantics;
- no Siemens reference in host/Contracts; and
- no claim that stub/static proof establishes live TIA behavior.

- [ ] **Step 9: Commit the live-reviewed stabilization and documentation.**

```powershell
git add docs/NETWORK_OPERATIONS_ROADMAP.md docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md docs/ARCHITECTURE.md docs/SupportedOperations/NETWORK_PHASE3_LIVE_ACCEPTANCE.md
git diff --cached --check
git commit -m "docs: stabilize network phase 3 introspection"
```

Expected: the commit contains only the final live-acceptance documentation. A live-derived production adjustment must already have followed its reviewed amendment and focused commit; never stage whole source directories here. Do not push or open a pull request unless separately requested.

---

## Final Acceptance Checklist

- [ ] `read_hardware_config` exposes snapshot-scoped typed selectors without attribute-value bloat.
- [ ] `inspect_network_object` resolves one verified target and returns modeled/dynamic attributes with exact source, access, availability, typed value, and diagnostics.
- [ ] Unknown and failed attributes remain explicit per-attribute results and do not suppress later attributes.
- [ ] Communication connections are selectable by owner path, composition index, type, local name, and local ID when available.
- [ ] `list_network_objects` is present only if live measurements prove one approved value gate.
- [ ] Text and `structuredContent` are the same canonical document; malformed worker success payloads fail closed as `protocol_error`.
- [ ] Existing 60,000/180,000 budgets and Phase 2 write-safety behavior remain unchanged.
- [ ] Static tests and serialized stub build pass.
- [ ] Separately authorized TIA Portal V21 live evidence passes the required matrix and repeatability checks.
- [ ] PROFIBUS/DP and unobserved connection classes are named as coverage gaps rather than inferred support.
- [ ] Documentation describes final supported behavior and boundaries, not investigation narration.
