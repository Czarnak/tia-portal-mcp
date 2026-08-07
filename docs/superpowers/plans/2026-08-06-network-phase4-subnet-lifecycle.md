# Network Operations Phase 4 Subnet Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add guarded Ethernet and PROFIBUS subnet creation, update, and deletion to the existing `network_write` tool, with exact `subnetId` targeting, minimal typed results, per-operation Openness transactions, and a verified invariant that root network-device count does not change.

**Architecture:** Extend the existing strict Network request/catalog, canonical preview/apply safety envelope, sequential batch engine, typed worker payload projection, and read-write access policy. Keep Siemens APIs in the .NET Framework worker behind a focused `SubnetLifecycleService`; each lifecycle operation opens one exclusive-access transaction, commits only after every requested setter succeeds, rereads the project, and returns only subnet identity plus the unchanged root-device-count assertion. Deleting a connected subnet is supported and does not inspect or block on nodes, IO systems, communication connections, or GSD-derived device-item names.

**Tech Stack:** C# 12/.NET 8 host, C#/.NET Framework 4.8 Openness worker, `TiaMcpServer.Contracts` targeting .NET Standard 2.0, System.Text.Json canonical structured results, xUnit, FakeWorker IPC tests, PowerShell 7 live harness, Siemens TIA Portal V21 Openness.

## Global Constraints

- Work on the current checkout and current branch. Do not create or switch branches or worktrees.
- Do not stage, commit, push, open a pull request, or publish anything unless the user explicitly requests it. The review checkpoints in this plan replace automatic commit steps.
- Follow strict TDD for every executable behavior change: add the narrow test first, run it and observe the expected failure, implement the smallest production change, rerun to green, then refactor without changing behavior.
- Use `apply_patch` for edits. Preserve unrelated user work and the existing Phase 4 probe files and artifacts.
- Keep the public MCP tool count unchanged. Add `create_subnet`, `update_subnet`, and `delete_subnet` as operations of `network_write`; do not add `subnet_write`, aliases, deprecated names, or generic attribute-write operations.
- Reuse `StructuredToolResult`, `StructuredOperationBatch`, `CanonicalJson`, `CanonicalWriteSafety`, the existing ten-minute single-use token policy, the audit path, and the 60,000-character item / 180,000-character response budgets.
- Treat a `network_write` batch as sequential and non-atomic. A subnet operation is transaction-scoped, but an earlier successful operation remains applied if a later operation fails. Never add an automatic retry.
- Do not call `Project.Save`, compile hardware, or trigger TIA confirmation dialogs from the lifecycle service. Save and compile remain separate explicit operations.
- Keep Phase 4 strictly about subnet lifecycle. Do not add node connect/disconnect, IO-system editing, connection editing, integrated PROFIBUS handling, Ethernet `DefaultSubnet`, PROFIBUS `BusProfile`, isochronous settings, generic attributes, or device lifecycle.
- Allow deletion of empty and connected subnets. Do not enumerate or return dependent nodes, IO systems, or communication connections, and do not introduce `dependency_evidence_incomplete` for these operations.
- Never use device-item names or GSD-derived attributes as subnet identity, safety evidence, or result data. The repeated ABB VFD `IE1` observation is recorded for later hardware-introspection work only.
- Preserve the internal `probe_subnet_lifecycle_mutations` operation and `SubnetLifecycleMutationProbeService` as evidence fixtures; do not expose them through `network_write` and do not refactor them into the production service during this phase.
- A stub build, real-reference build, unit test, source-contract test, or FakeWorker test is not live TIA evidence. Task 10 requires a new, explicit authorization before any public-path mutation run.
- Build the solution serially with `-m:1`. Do not run build and test processes concurrently because the worker-copy target is shared.

## Locked Public Contract

### Supported operations and request shapes

`create_subnet`:

```json
{
  "operationId": "create-pb-1",
  "operation": "create_subnet",
  "projectPath": "C:\\Projects\\Fixture.ap21",
  "subnet": {
    "name": "PROFIBUS_LINE_2",
    "networkType": "Profibus",
    "highestAddress": 126,
    "transmissionSpeed": "Baud1500000"
  }
}
```

`update_subnet`:

```json
{
  "operationId": "update-pb-1",
  "operation": "update_subnet",
  "projectPath": "C:\\Projects\\Fixture.ap21",
  "target": {
    "kind": "subnet",
    "subnetId": "590-5"
  },
  "subnetChanges": {
    "name": "PROFIBUS_LINE_3",
    "highestAddress": 62,
    "transmissionSpeed": "Baud93750"
  }
}
```

`delete_subnet`:

```json
{
  "operationId": "delete-pb-1",
  "operation": "delete_subnet",
  "projectPath": "C:\\Projects\\Fixture.ap21",
  "target": {
    "kind": "subnet",
    "subnetId": "590-5"
  }
}
```

### Writable values

- `networkType` is exact and case-sensitive: `Ethernet` or `Profibus`.
- `name` is required and nonblank on create and optional but nonblank on update.
- `highestAddress` is PROFIBUS-only and must be in `0..126`.
- `transmissionSpeed` is PROFIBUS-only and must be exactly one of:
  `Baud9600`, `Baud19200`, `Baud45450`, `Baud93750`, `Baud187500`,
  `Baud500000`, `Baud1500000`, `Baud3000000`, `Baud6000000`, or
  `Baud12000000`.
- `None`, unknown baud symbols, Ethernet PROFIBUS fields, network-type changes, and writable `subnetId` members are rejected.

### Minimal result

Every successful subnet lifecycle item returns exactly:

```json
{
  "subnetId": "590-5",
  "name": "PROFIBUS_LINE_3",
  "networkDeviceCount": 10,
  "networkDeviceCountUnchanged": true
}
```

The result never contains network type, configured attributes, connected-node names, IO systems,
connections, device names, device-item names, or raw Openness values. For deletion, `subnetId` and
`name` are the identity captured before deletion and the post-read proves the ID is absent.

---

## Task 1: Add the Strict Request Contract and Static Catalog Validation

**Files:**

- Create: `TiaMcpServer.Contracts/SubnetLifecycleContract.cs`
- Modify: `TiaMcpServer/Network/NetworkOperationRequest.cs`
- Modify: `TiaMcpServer/Network/NetworkOperationCatalog.cs`
- Create: `TiaMcpServer.Tests/NetworkPhase4SubnetRequestContractTests.cs`
- Modify: `TiaMcpServer.Tests/NetworkOperationRequestJsonTests.cs`
- Modify: `TiaMcpServer.Tests/NetworkOperationCatalogTests.cs`

- [ ] **Step 1: Write and run the first catalog registration test.**

Add a test that compiles against the current public API and fails because the three names are not
registered yet:

```csharp
[Fact]
public void WriteCatalog_RegistersOnlyTheThreePhase4SubnetOperations()
{
    var phase4 = NetworkOperationCatalog.WriteOperationNames
        .Where(name => name.EndsWith("_subnet", StringComparison.Ordinal))
        .ToArray();

    Assert.Equal(new[] { "create_subnet", "update_subnet", "delete_subnet" }, phase4);
}
```

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --nologo --filter FullyQualifiedName~NetworkPhase4SubnetRequestContractTests
```

Expected RED: the assertion reports an empty Phase 4 operation list. Do not change production code
before observing this failure.

- [ ] **Step 2: Add the shared closed vocabulary and strict request DTOs.**

Create a Siemens-free shared vocabulary in `TiaMcpServer.Contracts`:

```csharp
namespace TiaMcpServer.Contracts;

public static class SubnetLifecycleContract
{
    public const string Ethernet = "Ethernet";
    public const string Profibus = "Profibus";
    public const int MinimumHighestAddress = 0;
    public const int MaximumHighestAddress = 126;

    public static IReadOnlyList<string> TransmissionSpeeds { get; } = new[]
    {
        "Baud9600", "Baud19200", "Baud45450", "Baud93750", "Baud187500",
        "Baud500000", "Baud1500000", "Baud3000000", "Baud6000000", "Baud12000000",
    };

    public static bool IsSupportedNetworkType(string? value)
        => string.Equals(value, Ethernet, StringComparison.Ordinal)
            || string.Equals(value, Profibus, StringComparison.Ordinal);

    public static bool IsSupportedTransmissionSpeed(string? value)
        => value is not null && TransmissionSpeeds.Contains(value, StringComparer.Ordinal);
}
```

In `NetworkOperationRequest.cs`, add top-level `Subnet` and `SubnetChanges` properties and the two
nested DTOs below. Apply the same strict unmapped-member handling already used by the Network request
classes:

```csharp
public sealed class NetworkSubnetDefinition
{
    public string? Name { get; set; }
    public string? NetworkType { get; set; }
    public int? HighestAddress { get; set; }
    public string? TransmissionSpeed { get; set; }
}

public sealed class NetworkSubnetChanges
{
    public string? Name { get; set; }
    public int? HighestAddress { get; set; }
    public string? TransmissionSpeed { get; set; }
}
```

Do not add `subnetId` to either DTO and do not add `networkType` to `NetworkSubnetChanges`.

- [ ] **Step 3: Register the three write specs and rerun the registration test.**

Add `subnet` and `subnetChanges` to `AllRequestFields` and `IsFieldPresent`, then add:

```csharp
new NetworkOperationSpec("create_subnet", NetworkOperationCategory.Write, new[] { "subnet" }, None),
new NetworkOperationSpec("update_subnet", NetworkOperationCategory.Write, new[] { "target", "subnetChanges" }, None),
new NetworkOperationSpec("delete_subnet", NetworkOperationCategory.Write, new[] { "target" }, None),
```

Run the focused test again. Expected GREEN: the three names appear in the declared order.

- [ ] **Step 4: Add strict JSON and validation tests, then observe the validation RED.**

Cover all of these cases before adding operation-specific validation:

- valid Ethernet create;
- valid PROFIBUS create with both optional attributes;
- valid rename-only update;
- valid PROFIBUS attribute update;
- valid delete;
- nested unknown member rejected by JSON deserialization;
- writable `subnetId` under `subnet` or `subnetChanges` rejected as unmapped;
- writable `networkType` under `subnetChanges` rejected as unmapped;
- missing or blank create name;
- missing or unknown create network type;
- Ethernet create carrying either PROFIBUS attribute;
- empty `subnetChanges`;
- blank update name;
- highest address `-1` and `127`;
- every accepted baud symbol plus `None`, `Baud9375`, and a lowercase symbol;
- update/delete target missing, wrong `kind`, blank `subnetId`, or extra selector members;
- `subnet`, `subnetChanges`, `changes`, device creation fields, and paging fields rejected when inapplicable.

Use a shared assertion helper:

```csharp
private static NetworkValidationResult Validate(NetworkOperationRequest request)
    => NetworkOperationCatalog.ValidateWrite(new[] { request });
```

Run the focused test. Expected RED: at least the blank-name, empty-update, Ethernet PROFIBUS-field,
range, and baud-symbol cases are incorrectly accepted.

- [ ] **Step 5: Implement the smallest static validators and rerun to green.**

Add `ValidateCreateSubnet`, `ValidateUpdateSubnet`, and `ValidateDeleteSubnet` calls inside the
existing deterministic validation loop. Reuse `ValidateSelector` for the target and require the
exact `subnet` selector shape. Keep deterministic error order:

1. inapplicable top-level fields;
2. missing required top-level fields;
3. nested DTO shape and required members;
4. target selector shape;
5. type applicability;
6. numeric range and enum value.

Static update validation checks field shape, nonblank values, range, and enum vocabulary, but does
not decide whether PROFIBUS-only fields apply to the current target. That check belongs to Task 2,
where the current subnet type is known.

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~NetworkPhase4SubnetRequestContractTests|FullyQualifiedName~NetworkOperationRequestJsonTests|FullyQualifiedName~NetworkOperationCatalogTests"
```

Expected GREEN: all request, strict JSON, catalog, and negative-boundary tests pass.

- [ ] **Step 6: Refactor and review Task 1.**

Remove duplicated string comparisons in favor of `SubnetLifecycleContract`. Review that the public
request has no generic attribute bag, no dependency fields, and no path by which a caller can write
`subnetId` or change `networkType`. Run `git diff --check`; do not stage or commit.

---

## Task 2: Resolve Exact Subnet Targets and Bind Them Into Write Safety

**Files:**

- Modify: `TiaMcpServer/Network/NetworkIdentityResolver.cs`
- Modify: `TiaMcpServer/Network/NetworkSafetySnapshot.cs`
- Modify: `TiaMcpServer/Network/NetworkToolResponses.cs`
- Create: `TiaMcpServer.Tests/NetworkPhase4SubnetSafetyTests.cs`
- Modify: `TiaMcpServer.Tests/NetworkPhase3SafetySnapshotTests.cs`

- [ ] **Step 1: Write resolver tests and observe the unsupported-operation RED.**

Build small `HardwareConfigInfo` fixtures directly in tests. Cover:

- create resolves with `state: null`, `DeviceName: null`, requested `SubnetName`, and no invented ID;
- update/delete resolve one subnet by ordinal, case-sensitive `subnetId` only;
- a different-cased ID does not match;
- zero and duplicate IDs fail closed;
- blank or unsupported `NetworkType` fails closed;
- Ethernet rename succeeds;
- Ethernet update carrying `highestAddress` or `transmissionSpeed` is rejected;
- PROFIBUS name and attribute updates succeed;
- connected node names and IO-system contents do not change resolution or create a deletion blocker.

Representative assertion:

```csharp
var resolution = NetworkIdentityResolver.Resolve(request, state);

Assert.True(resolution.Success);
Assert.Null(resolution.Evidence!.DeviceName);
Assert.Equal("LINE_1", resolution.Evidence.SubnetName);
Assert.Null(resolution.Evidence.SubnetId);
```

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --nologo --filter FullyQualifiedName~NetworkPhase4SubnetSafetyTests
```

Expected RED: `create_subnet`, `update_subnet`, and `delete_subnet` return the resolver's unsupported
operation failure.

- [ ] **Step 2: Extend canonical target resolution.**

Change `NetworkWriteTargetEvidence.DeviceName` from `string` to `string?`; existing device operation
results remain non-null. Extend `NetworkIdentityResolver.Resolve` with:

```csharp
"create_subnet" => ResolveSubnetCreation(operation),
"update_subnet" => ResolveExistingSubnet(operation, state, validateChanges: true),
"delete_subnet" => ResolveExistingSubnet(operation, state, validateChanges: false),
```

Creation evidence is request-derived: set `SubnetName` from `operation.Subnet!.Name`, leave
`SubnetId` and every device member null, and use an empty item path. Existing-target resolution:

- requires a non-null typed hardware snapshot;
- matches exactly one `HardwareConfigInfo.Subnets` element whose nonblank `SubnetId` equals the
  requested value using `StringComparison.Ordinal`;
- records the matched `Name` and `SubnetId` in evidence;
- accepts only current `NetworkType` values `Ethernet` and `Profibus`;
- rejects PROFIBUS-only updates against an Ethernet subnet with `validation_error`;
- uses `postcondition_failed` when current identity/type is missing, no target exists, or multiple
  targets report the same ID.

Do not consult `ConnectedNodeNames`, `IoSystems`, devices, interfaces, or connections.

- [ ] **Step 3: Make update/delete state-dependent and rerun resolver tests.**

Update `NetworkSafetySnapshot.RequiresHardwareState` so it returns true for
`configure_network_device`, `update_subnet`, and `delete_subnet`, but false for pure
`add_network_device` / `create_subnet` batches. Update comments that currently describe only the two
device operations.

Run the Task 2 tests. Expected GREEN.

- [ ] **Step 4: Prove unchanged safety-token semantics.**

Add tests around the existing safety snapshot/token helpers for:

- create target evidence changes when name or network type changes;
- update/delete target evidence changes when the resolved subnet name or ID changes;
- request order remains token-bound;
- project path remains token-bound;
- hardware snapshot changes invalidate update/delete apply;
- connected relationship-only changes remain part of the existing full current-state hash but do
  not become target dependencies or blockers;
- missing/duplicate subnet identity prevents token issuance.

Do not add a new token implementation or a subnet-specific token payload.

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~NetworkPhase4SubnetSafetyTests|FullyQualifiedName~NetworkPhase3SafetySnapshotTests"
```

Expected GREEN: exact-state and ordered-input binding are preserved.

- [ ] **Step 5: Review checkpoint after Tasks 1-2.**

Review the diff against the locked request shapes and safety rules. Specifically verify there is no
name fallback, case-insensitive ID match, dependency inventory, device-name invention, safety-token
fork, or weakening of state validation. Run `git diff --check`; do not stage or commit.

---

## Task 3: Add Worker Protocol Fields, Authorization, and Host Forwarding

**Files:**

- Modify: `TiaMcpServer.Contracts/WorkerRequest.cs`
- Modify: `TiaMcpServer.Contracts/OperationPolicyCatalog.cs`
- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs`
- Modify: `TiaMcpServer/Network/NetworkWorkerInvoker.cs`
- Create: `TiaMcpServer.Tests/NetworkPhase4SubnetForwardingTests.cs`
- Modify: `TiaMcpServer.Tests/NetworkFieldForwardingTests.cs`
- Modify: `TiaMcpServer.Tests/ReadOnlyModeTests.cs`

- [ ] **Step 1: Write access and invocation tests and observe RED.**

Use `OperationPolicyCatalog.GetCapability` to assert all three operation names are
`ProjectMutation`, denied in `McpAccessMode.ReadOnly`, and allowed in read-write mode. Exercise
`NetworkWorkerInvoker.InvokeWriteAsync` with a create request through the existing echo FakeWorker
and assert that it does not return `validation_error`.

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~NetworkPhase4SubnetForwardingTests|FullyQualifiedName~ReadOnlyModeTests"
```

Expected RED: the operations are unclassified and the invoker reports an unsupported network write
operation.

- [ ] **Step 2: Add production worker-request fields without changing probe fields.**

Reuse `WorkerRequest.SubnetId` for update/delete target identity. Add these production fields near
the existing network fields, not inside the probe region:

```csharp
public string? SubnetName { get; set; }
public string? SubnetNetworkType { get; set; }
public int? SubnetHighestAddress { get; set; }
public string? SubnetTransmissionSpeed { get; set; }
```

Keep `ProbeRunId`, `ProbeConnectedEthernetSubnetId`, `ProbeConnectedProfibusSubnetId`,
`ProbeProfibusHighestAddress`, and `ProbeProfibusTransmissionSpeed` unchanged.

- [ ] **Step 3: Classify and forward all three operations.**

Add the three names to `OperationPolicyCatalog` as `ProjectMutation`. Add explicit worker-client
methods:

```csharp
Task<WorkerCallResult> CreateSubnetAsync(
    string name, string networkType, int? highestAddress, string? transmissionSpeed, string? projectPath)

Task<WorkerCallResult> UpdateSubnetAsync(
    string subnetId, string? name, int? highestAddress, string? transmissionSpeed, string? projectPath)

Task<WorkerCallResult> DeleteSubnetAsync(string subnetId, string? projectPath)
```

Each uses `SendBoundProjectRequestAsync`, sets only its declared production fields, and sets both
`Confirm = true` and `AllowTiaConfirmations = true`, matching existing network-write transport.
Extend `NetworkWorkerInvoker.InvokeWriteAsync` to map the validated DTOs to these methods and use
the common project path.

- [ ] **Step 4: Assert the exact serialized worker request.**

Through the echo scenario, assert create forwards name/type/optional PROFIBUS attributes with no
target ID; update forwards target ID plus only requested changes; delete forwards only target ID;
all three forward `confirm`, `allowTiaConfirmations`, and the normalized common project path.
Explicitly assert the production calls do not populate any `Probe*` member.

Run the focused tests again. Expected GREEN.

- [ ] **Step 5: Run authorization and transport regressions.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~NetworkPhase4SubnetForwardingTests|FullyQualifiedName~NetworkFieldForwardingTests|FullyQualifiedName~ReadOnlyModeTests|FullyQualifiedName~ReadOnlyModeHardeningTests"
```

Expected GREEN: read-only mode rejects every subnet mutation before a worker call, read-write mode
forwards exact fields, and existing network operations are unchanged.

---

## Task 4: Declare and Enforce the Minimal Typed Success Payload

**Files:**

- Create: `TiaMcpServer.Contracts/SubnetLifecycleResultInfo.cs`
- Modify: `TiaMcpServer/Network/NetworkPayloadContract.cs`
- Create: `TiaMcpServer.Tests/NetworkPhase4SubnetPayloadContractTests.cs`
- Modify: `TiaMcpServer.Tests/NetworkPayloadContractTests.cs`

- [ ] **Step 1: Write payload projection tests and observe protocol RED.**

Pass the locked four-member JSON result to `NetworkPayloadContract.Project` for each operation and
assert a succeeded structured item. Before production registration, the operation must become
`protocol_error` because no result contract is declared.

Also prepare negative cases for:

- missing, null, or blank `subnetId`;
- missing, null, or blank `name`;
- negative `networkDeviceCount`;
- missing or false `networkDeviceCountUnchanged`;
- any extra member such as `networkType`, `highestAddress`, `devices`, or `connections`;
- JSON of the wrong root type;
- rejected payload never echoed into the public item or diagnostic text.

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --nologo --filter FullyQualifiedName~NetworkPhase4SubnetPayloadContractTests
```

Expected RED: the valid payload is rejected because the operation has no declared decoder.

- [ ] **Step 2: Add the shared result DTO.**

Create exactly:

```csharp
namespace TiaMcpServer.Contracts;

public sealed class SubnetLifecycleResultInfo
{
    public string SubnetId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int NetworkDeviceCount { get; set; }
    public bool NetworkDeviceCountUnchanged { get; set; }
}
```

Do not add messages, warnings, network type, attributes, relationships, or device collections.

- [ ] **Step 3: Register one decoder for all three operations.**

Map each operation to:

```csharp
Decode<SubnetLifecycleResultInfo>(payload, ValidateSubnetLifecycleResult)
```

`ValidateSubnetLifecycleResult` requires nonblank identity strings, a nonnegative count, and
`NetworkDeviceCountUnchanged == true`. `CanonicalJson` already uses
`JsonUnmappedMemberHandling.Disallow`, so extra result members fail closed without another parser.
Retain the existing stable `protocol_error` response and diagnostic truncation behavior.

- [ ] **Step 4: Rerun payload tests and canonical mirror assertions.**

Assert each valid result is a real JSON object, not a nested JSON string, and that the MCP text block
and `structuredContent` are produced from the same canonical document. Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~NetworkPhase4SubnetPayloadContractTests|FullyQualifiedName~NetworkPayloadContractTests|FullyQualifiedName~NetworkStructuredProtocolTests"
```

Expected GREEN: valid minimal results project identically; malformed or verbose success payloads
become `protocol_error` and are not echoed.

- [ ] **Step 5: Review checkpoint after Tasks 3-4.**

Review the protocol diff for exact field ownership, deny-by-default read-only behavior, no probe
field reuse, one typed result shape, no nested JSON, and no extra result detail. Run
`git diff --check`; do not stage or commit.

---

## Task 5: Implement the Transactional Openness Subnet Lifecycle Service

**Files:**

- Create: `TiaMcpServer.OpennessWorker/Openness/SubnetLifecycleService.cs`
- Modify: `TiaMcpServer.OpennessWorker/Program.cs`
- Create: `TiaMcpServer.Tests/NetworkPhase4SubnetWorkerServiceContractTests.cs`
- Create: `TiaMcpServer.Tests/NetworkPhase4SubnetWorkerDispatchTests.cs`

- [ ] **Step 1: Write source-contract and dispatch tests and observe RED.**

The worker project cannot instantiate Siemens objects in ordinary unit tests. Add focused tests
that read the production source and require:

- dispatch cases for `create_subnet`, `update_subnet`, and `delete_subnet`;
- a distinct production `SubnetLifecycleService` file;
- `System:Subnet.Ethernet` and `System:Subnet.Profibus` mappings;
- ordinal `SubnetId` lookup with exact-one enforcement;
- `ExclusiveAccess`, `Transaction(project, ...)`, and `CommitOnDispose()`;
- dynamic `HighestAddress` and `TransmissionSpeed` setters;
- enum parsing from the current attribute CLR type;
- root `project.Devices.Count` capture and post-read comparison;
- create/update/delete postcondition branches;
- `WorkerFailureCategories.PostconditionFailed` on mismatch;
- no `project.Save`, compile call, dependency traversal, device deletion, retry loop, or catch that
  swallows `NonRecoverableException`.

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~NetworkPhase4SubnetWorkerServiceContractTests|FullyQualifiedName~NetworkPhase4SubnetWorkerDispatchTests"
```

Expected RED: the production service and dispatch cases do not exist.

- [ ] **Step 2: Add worker dispatch and repeat boundary validation.**

Add the three method names to the main worker switch. Each handler:

- requires `Confirm == true`;
- repeats request-derived name, type, range, baud, and target-ID validation before opening a
  transaction;
- establishes the requested project with `WithSession`, `EnsureConnected`, and
  `EnsureRequestedProjectOpen`;
- requires both `session.TiaPortal` and `session.Project`;
- calls one typed production service method and wraps it with `Success`.

For update, current-type applicability necessarily requires an Openness read of the exact target;
the service performs that read and rejects an inapplicable request before opening the mutation
transaction.

Use a small private helper in `Program.cs` for the repeated session/project requirement, but keep
the three public worker method strings explicit in the dispatch switch and policy catalog.

- [ ] **Step 3: Implement exact subnet lookup and type mapping.**

In `SubnetLifecycleService`, map only:

```csharp
private const string EthernetTypeIdentifier = "System:Subnet.Ethernet";
private const string ProfibusTypeIdentifier = "System:Subnet.Profibus";
```

Enumerate `project.Subnets`, read dynamic `SubnetId`, and require exactly one ordinal match for
update/delete. Never fall back to `Name`, collection index, connected device, or first match. Read
the current subnet type and fail closed if it is unavailable or outside Ethernet/PROFIBUS.

- [ ] **Step 4: Implement one transaction per operation.**

Expose these production entry points:

```csharp
public static SubnetLifecycleResultInfo Create(
    TiaPortal tiaPortal, Project project, string name, string networkType,
    int? highestAddress, string? transmissionSpeed)

public static SubnetLifecycleResultInfo Update(
    TiaPortal tiaPortal, Project project, string subnetId, string? name,
    int? highestAddress, string? transmissionSpeed)

public static SubnetLifecycleResultInfo Delete(
    TiaPortal tiaPortal, Project project, string subnetId)
```

For every method:

1. capture `var deviceCountBefore = project.Devices.Count`;
2. open `tiaPortal.ExclusiveAccess(...)`;
3. open `exclusiveAccess.Transaction(project, ...)`;
4. perform every requested mutation;
5. call `transaction.CommitOnDispose()` only after all setters succeed;
6. dispose transaction and exclusive access;
7. reread subnets and `project.Devices.Count`;
8. verify the operation-specific postcondition and unchanged device count;
9. return `SubnetLifecycleResultInfo` only after verification.

Create uses `project.Subnets.Create(typeIdentifier, name)`, then applies optional PROFIBUS fields in
the same transaction. Update preserves the same ID, applies `Name`, then PROFIBUS attributes.
Delete captures name and ID before calling `subnet.Delete()`.

Set `HighestAddress` through `IEngineeringObject.SetAttribute`. For transmission speed, read the
current attribute object, parse the approved symbol into `currentValue.GetType()` with case-sensitive
`Enum.Parse`, then set that enum object. Do not bind the worker to a guessed Siemens enum type.

- [ ] **Step 5: Implement postconditions and failure policy.**

- Create: exactly one post-read subnet has the returned nonblank ID; name, type, and every requested
  PROFIBUS attribute match.
- Update: exactly one post-read subnet has the same ID; every requested field matches.
- Delete: no post-read subnet has the deleted ID.
- All three: `project.Devices.Count == deviceCountBefore`.

On any mismatch, throw `WorkerOperationException` with
`WorkerFailureCategories.PostconditionFailed` and inspect-before-retry guidance. Do not retry. Let
the existing outer worker handling map ordinary Siemens exceptions and terminate on nonrecoverable
exceptions.

- [ ] **Step 6: Run source contracts and both worker builds.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~NetworkPhase4SubnetWorkerServiceContractTests|FullyQualifiedName~NetworkPhase4SubnetWorkerDispatchTests|FullyQualifiedName~NetworkPhase4SubnetMutationProbeContractTests"
dotnet build TiaMcpServer.sln --no-restore -m:1 /p:UseTiaPortalReferenceStubs=true --nologo
dotnet build TiaMcpServer.sln --no-restore -m:1 /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48" --nologo
```

Expected GREEN: source contracts pass; both serialized builds complete with zero errors. The real
reference build proves API compatibility only; it does not attach to TIA or prove runtime behavior.

- [ ] **Step 7: Review Task 5.**

Compare the production service against the accepted mutation probe evidence. Verify transaction
scope, type identifiers, dynamic enum parsing, empty-name rejection at the application boundary,
exact ID lookup, no save/compile, no dependency traversal, unchanged device count, and no automatic
retry. Run `git diff --check`; do not stage or commit.

---

## Task 6: Prove the Public Preview/Apply Protocol With a Stateful FakeWorker

**Files:**

- Modify: `TiaMcpServer.FakeWorker/Program.cs`
- Create: `TiaMcpServer.Tests/NetworkPhase4SubnetFakeWorkerTests.cs`
- Modify: `TiaMcpServer.Tests/NetworkOperationFakeWorkerTests.cs`
- Modify: `TiaMcpServer.Tests/NetworkToolsTests.cs`

- [ ] **Step 1: Write the first public-path test and observe RED.**

Create a stateful FakeWorker scenario named `network-subnet-lifecycle`. Begin with one Ethernet and
one PROFIBUS subnet and a stable root device count. First write a test that calls `network_write`
preview for `create_subnet` and asserts a token plus request-derived target evidence. Before adding
the scenario and remaining host integration, the test must fail with an unexpected worker method or
missing target result.

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --nologo --filter FullyQualifiedName~NetworkPhase4SubnetFakeWorkerTests
```

Expected RED: the FakeWorker does not implement the Phase 4 lifecycle scenario.

- [ ] **Step 2: Implement deterministic mutable subnet state in the FakeWorker.**

The scenario must:

- return a contract-valid `HardwareConfigInfo` with stable device data and current subnet state;
- create deterministic nonblank subnet IDs;
- apply update fields only to the exact ID;
- delete the exact ID while leaving the device collection unchanged;
- return only `SubnetLifecycleResultInfo` fields;
- expose separate switches for a malformed verbose success payload, a worker
  `postcondition_failed`, and a deliberate second-item failure;
- keep the hardware state stable between preview and apply unless a lifecycle write actually
  succeeds.

Do not emulate dependency blockers. Connected subnet fixtures remain deletable.

- [ ] **Step 3: Cover each operation through preview then apply.**

Add tests for:

- Ethernet create, post-read identity, and unchanged device count;
- PROFIBUS create with both optional attributes;
- Ethernet rename;
- PROFIBUS rename/highest-address/baud update;
- empty subnet deletion;
- connected Ethernet and connected PROFIBUS deletion;
- exact canonical text/`structuredContent` equality;
- minimal four-member result objects;
- audit entry on successful apply;
- single-use token rejection on replay;
- request, operation-order, project-path, target-state, and requested-change token tampering;
- worker `postcondition_failed` propagation without success wording;
- verbose/malformed successful worker payload becoming `protocol_error` with no raw echo;
- a later failure stopping the batch while earlier successful items remain applied and later items
  are skipped;
- root device count remains unchanged across every successful lifecycle item.

For the user-facing success summary, assert the canonical result data is sufficient to render:

```text
Created subnets: X. Number of network devices remains unchanged.
Modified subnets: Y. Number of network devices remains unchanged.
Deleted subnets: Z. Number of network devices remains unchanged.
```

Do not add relationship or device-detail text to the protocol.

- [ ] **Step 4: Update partial-application wording.**

In `NetworkWriteTools`, change descriptions that currently imply no rollback at any scope to the
precise phrase `no batch-wide rollback`. Keep the existing stopped-batch warning but state that the
failed operation and earlier operations may already have changed TIA state, no batch-wide rollback
was attempted, and the caller must reread before retrying. Do not promise that the failed Siemens
transaction committed or rolled back.

- [ ] **Step 5: Run focused public protocol tests.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~NetworkPhase4SubnetFakeWorkerTests|FullyQualifiedName~NetworkOperationFakeWorkerTests|FullyQualifiedName~NetworkToolsTests|FullyQualifiedName~NetworkStructuredProtocolTests"
```

Expected GREEN: all lifecycle paths use the existing preview/apply/token/audit engine and preserve
sequential stop-on-first-failure behavior.

- [ ] **Step 6: Review checkpoint after Tasks 5-6.**

Review the whole executable path from JSON request to worker response. Confirm no extra MCP tool,
no generic batch registration, no token bypass, no verbose result, no automatic retry, no batch-wide
atomicity claim, and no device mutation. Run `git diff --check`; do not stage or commit.

---

## Task 7: Close Schema, Access, Budget, and Regression Contracts

**Files:**

- Modify: `TiaMcpServer.Tests/McpToolSchemaTests.cs`
- Modify: `TiaMcpServer.Tests/BatchToolMetadataTests.cs`
- Modify: `TiaMcpServer.Tests/StructuredOperationBatchPayloadBudgetTests.cs`
- Modify: `TiaMcpServer.Tests/ReadOnlyModeHardeningTests.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` only if a newly created host source file must be linked explicitly

- [ ] **Step 1: Add schema and tool-count assertions, then observe RED where metadata is stale.**

Assert:

- read-write mode still exposes 14 tools and read-only mode still exposes 4;
- `network_write` advertises the three lifecycle operation names and exact nested request fields;
- unknown nested members are rejected by schema/runtime deserialization;
- `network_read` does not advertise lifecycle writes;
- generic write batches do not recognize the three names;
- read-only mode rejects them before worker startup;
- descriptions say connected subnet deletion is allowed and devices remain;
- descriptions say no batch-wide rollback and separate save/compile;
- descriptions do not mention dependency inventory, connection deletion, or device deletion.

Run the focused tests and observe the expected stale-description/schema failures before editing tool
metadata.

- [ ] **Step 2: Update public metadata without adding a tool.**

Update the `network_write` description and operation examples. Keep one source of truth for the DTO
schema generated by MCP registration; do not hand-maintain a second permissive JSON schema. If a new
host production file was created contrary to the file list above, add one explicit linked `<Compile
Include>` to `TiaMcpServer.Tests.csproj`; never add a wildcard.

- [ ] **Step 3: Test response-budget behavior with lifecycle results.**

Prove the minimal result survives normal limits, an oversized unexpected payload is rejected by the
typed contract before budgeting, and the existing whole-value omission / whole-document truncation
metadata remains unchanged. Do not raise either budget.

- [ ] **Step 4: Run the full Siemens-free test suite.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --nologo
```

Expected GREEN: the complete suite passes. Investigate any failure; do not weaken existing tests or
production safety to accommodate a fixture.

- [ ] **Step 5: Inspect coverage for materially changed Siemens-free logic.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build --collect:"XPlat Code Coverage" --nologo
```

Expected: coverage output is generated. Request/catalog validation, exact subnet resolution,
safety-state selection, forwarding, and payload projection meet the repository threshold; if no
threshold is configured, target at least 80% branch coverage for those changed Siemens-free paths.
Do not claim meaningful runtime coverage for the Siemens-dependent service from source-contract
tests.

- [ ] **Step 6: Review Task 7.**

Review failures, coverage, MCP schemas, operation lists, descriptions, response budgets, and access
mode. Run `git diff --check`; do not stage or commit.

---

## Task 8: Add a Separately Authorized Public-Path Live Harness

**Files:**

- Create: `scripts/live-test-network-phase4-subnets.ps1`
- Create: `TiaMcpServer.Tests/NetworkPhase4SubnetLiveHarnessContractTests.cs`

- [ ] **Step 1: Write the harness contract test and observe RED.**

The test must require the script to exist and statically prove:

- PowerShell 7 requirement and strict error handling;
- default `Inventory` mode is read-only;
- `Preview` mode does not apply a token;
- `Apply` mode requires `-AllowMutation` plus an exact acknowledgement string;
- an explicit `.ap21` project path is required for mutation;
- output is a timestamped JSON artifact under `artifacts/live-network-phase4`;
- process cleanup occurs in `finally`;
- the script calls the public `network_read` / `network_write` route, not
  `probe_subnet_lifecycle_mutations` or `SubnetLifecycleMutationProbeService`;
- it records the server commit, TIA version, requested operations, previews, tokens redacted from
  persisted output, apply results, post-reads, and device counts;
- it never calls project save or compile.

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --nologo --filter FullyQualifiedName~NetworkPhase4SubnetLiveHarnessContractTests
```

Expected RED: the public-path Phase 4 harness is absent.

- [ ] **Step 2: Implement Inventory and Preview modes first.**

Reuse the proven process/MCP framing helpers from `scripts/live-test-network-phase2.ps1` rather than
invoking the worker directly. Inventory records the current hardware configuration and root device
count. Preview constructs the exact create/update/delete operation arrays, calls `network_write`
with `confirm=false`, records canonical preview targets and non-secret token metadata, and performs
no apply.

Preview inputs must distinguish harness-created isolated subnets from caller-supplied connected
subnets. Require exact existing `subnetId` values for connected deletion; never accept names.

- [ ] **Step 3: Implement the double-gated Apply sequence.**

Use an exact acknowledgement such as:

```text
DELETE SUBNETS AND KEEP DEVICES
```

Apply must create isolated Ethernet and PROFIBUS subnets, update both, delete the created subnets,
delete explicitly supplied connected Ethernet and PROFIBUS subnet IDs, and post-read after each
group. Every apply call uses the unchanged ordered request and token returned by its immediately
preceding preview. Stop on any mismatch; do not retry.

Record and verify:

- the three result verbs and subnet names/IDs;
- every successful item has the exact four-member minimal result;
- deleted IDs are absent;
- root device count is unchanged;
- connected subnet deletion does not delete devices;
- subnet-related attributes cleared from devices are recorded only as expected project-state
  effects in the artifact, not returned through the public lifecycle result.

- [ ] **Step 4: Run parser and contract tests without attaching to TIA.**

```powershell
$tokens = $errors = $null
[System.Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path scripts/live-test-network-phase4-subnets.ps1),
    [ref]$tokens,
    [ref]$errors) | Out-Null
if ($errors.Count -ne 0) { throw ($errors | Out-String) }
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --nologo --filter FullyQualifiedName~NetworkPhase4SubnetLiveHarnessContractTests
```

Expected GREEN: parser and static harness contract pass. Do not run `Apply` in this task.

- [ ] **Step 5: Review Task 8.**

Review that Inventory is the default, Preview is non-mutating, Apply is double-gated, targets are
exact IDs, tokens are not persisted, cleanup is reliable, and the script uses the public MCP route.
Run `git diff --check`; do not stage or commit.

---

## Task 9: Document the Static Phase 4 Surface and Run Whole-Plan Static Acceptance

**Files:**

- Modify: `docs/NETWORK_OPERATIONS_ROADMAP.md`
- Modify: `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md`
- Modify: `docs/ARCHITECTURE.md`
- Create: `docs/SupportedOperations/NETWORK_PHASE4_SUBNET_LIFECYCLE.md`

- [ ] **Step 1: Update supported-operation documentation.**

Document:

- the three request shapes and exact writable values;
- exact `subnetId` targeting with no name fallback;
- Ethernet/PROFIBUS-only scope;
- connected deletion is supported and does not delete devices;
- subnet deletion may clear network-related device attributes as a logical TIA effect;
- the exact minimal result;
- one transaction per operation, sequential non-atomic batch behavior, and no automatic retry;
- preview/apply, token expiry/single use/current-state binding, auditing, and read-only denial;
- separate save and compile boundary;
- explicit non-goals and the deferred GSD-derived hardware-name issue;
- internal probe evidence versus public-path live acceptance status.

Do not mark Phase 4 live-verified yet. Mark implementation as statically verified and public live
acceptance pending until Task 10 passes.

- [ ] **Step 2: Update architecture and roadmap status precisely.**

Architecture should show:

```text
network_write request
  -> strict catalog validation
  -> current subnet resolution and canonical safety binding
  -> worker request
  -> SubnetLifecycleService transaction
  -> post-read/device-count assertion
  -> minimal typed canonical result
```

The roadmap must replace the future Phase 4 description with implemented static scope without
claiming node, IO-system, connection, generic attribute, integrated PROFIBUS, save, compile, or live
support.

- [ ] **Step 3: Run all automated acceptance gates serially.**

```powershell
dotnet build TiaMcpServer.sln --no-restore -m:1 /p:UseTiaPortalReferenceStubs=true --nologo
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build --nologo
dotnet build TiaMcpServer.sln --no-restore -m:1 /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48" --nologo
git diff --check
```

Expected: both builds have zero errors, all tests pass, and `git diff --check` prints nothing.

- [ ] **Step 4: Perform a whole-plan contract audit.**

Compare the emitted public DTO property sets, operation catalog entries, target evidence, worker
fields, typed result, descriptions, and documentation directly against the Locked Public Contract.
Task-local tests are not sufficient evidence for this comparison.

Audit every changed file for:

- no extra MCP tool or generic batch alias;
- no device delete or dependency blocker;
- no writable network type on update;
- no accepted speed outside the ten-symbol allowlist;
- no name fallback or case-insensitive subnet ID;
- no extra public result fields;
- no token/safety/audit/budget regression;
- no project save, compile, or automatic retry;
- no unsupported live claim;
- no unrelated source or artifact changes.

- [ ] **Step 5: Run documentation integrity checks.**

Check all changed Markdown links, verify every referenced operation/type/file exists, scan for
unresolved planning markers and stale statements that Phase 4 is wholly future work. Confirm
the documentation is ASCII unless an existing file intentionally uses Unicode. Run
`git diff --check` again. Do not stage or commit.

- [ ] **Step 6: Present the static acceptance checkpoint to the user.**

Report exact build/test counts, coverage for changed Siemens-free logic, files changed, review
findings, and the remaining public live gate. Stop before Task 10 unless the user separately
authorizes the live mutation run against an identified disposable project.

---

## Task 10: Run the Separately Authorized Public Live Gate and Stabilize Phase 4

**Files:**

- Generated evidence pattern: `artifacts/live-network-phase4/*-subnet-lifecycle-public.json`
- Modify after successful evidence: `docs/NETWORK_OPERATIONS_ROADMAP.md`
- Modify after successful evidence: `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md`
- Modify after successful evidence: `docs/SupportedOperations/NETWORK_PHASE4_SUBNET_LIFECYCLE.md`
- Modify only if live evidence contradicts the approved contract: the smallest Phase 4 source/test files identified by the discrepancy

- [ ] **Step 1: Obtain fresh, explicit authorization.**

Plan approval and the earlier internal mutation-probe authorization do not authorize this public
production-path run. Confirm all of the following immediately before execution:

- the exact `.ap21` project path;
- it is a disposable or backed-up prepared fixture;
- TIA Portal V21 is running with that project open;
- the exact connected Ethernet and PROFIBUS `subnetId` values selected for deletion;
- `Inventory`, `Preview`, and double-gated `Apply` are authorized;
- deleting those subnets while retaining devices is intended.

After authorization, capture the exact values interactively so the commands below contain no
invented fixture values:

```powershell
$phase4ProjectPath = Read-Host 'Exact authorized disposable .ap21 project path'
$phase4ConnectedEthernetSubnetId = Read-Host 'Exact connected Ethernet subnetId authorized for deletion'
$phase4ConnectedProfibusSubnetId = Read-Host 'Exact connected PROFIBUS subnetId authorized for deletion'
```

- [ ] **Step 2: Run Inventory and inspect the artifact before mutation.**

```powershell
pwsh -NoProfile -File scripts/live-test-network-phase4-subnets.ps1 `
    -Mode Inventory `
    -ProjectPath $phase4ProjectPath
```

Expected: exit 0, exact project binding, readable hardware state, nonblank selected subnet IDs, and
a recorded root device count.

- [ ] **Step 3: Run Preview and inspect every requested target.**

```powershell
pwsh -NoProfile -File scripts/live-test-network-phase4-subnets.ps1 `
    -Mode Preview `
    -ProjectPath $phase4ProjectPath `
    -ConnectedEthernetSubnetId $phase4ConnectedEthernetSubnetId `
    -ConnectedProfibusSubnetId $phase4ConnectedProfibusSubnetId
```

Expected: no mutation, exact current target evidence for update/delete, request-derived evidence for
create, and safety tokens ready only in process memory.

- [ ] **Step 4: Run the double-gated public apply once.**

```powershell
pwsh -NoProfile -File scripts/live-test-network-phase4-subnets.ps1 `
    -Mode Apply `
    -ProjectPath $phase4ProjectPath `
    -ConnectedEthernetSubnetId $phase4ConnectedEthernetSubnetId `
    -ConnectedProfibusSubnetId $phase4ConnectedProfibusSubnetId `
    -AllowMutation `
    -Acknowledgement "DELETE SUBNETS AND KEEP DEVICES"
```

Expected: create/update/delete pass for Ethernet and PROFIBUS; both empty and connected deletions
pass; every result has exactly four fields; post-reads prove deleted IDs absent and root device count
unchanged. Do not automatically rerun after any ambiguous timeout, worker crash, Siemens exception,
or postcondition failure. Inspect current project state first.

- [ ] **Step 5: Apply the live acceptance decision.**

Pass only if all locked operations, types, attributes, transaction outcomes, minimal results, and
device-count postconditions succeed through the public MCP path. The expected clearing of subnet or
IO-system relationship attributes on retained devices is not a failure.

If live evidence contradicts the approved contract, record the exact discrepancy and return to
design review before changing production behavior. Do not silently broaden the API or weaken a
guardrail.

- [ ] **Step 6: Update live support documentation and rerun static verification.**

Record TIA version, project fixture description without sensitive content, commit hash, commands,
artifact path, operation matrix, device count before/after, expected retained-device attribute
effects, and remaining untested boundaries. Then rerun:

```powershell
dotnet build TiaMcpServer.sln --no-restore -m:1 /p:UseTiaPortalReferenceStubs=true --nologo
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build --nologo
git diff --check
```

Expected: static gates remain green after documentation or any separately reviewed stabilization.
Do not stage, commit, push, or open a pull request unless the user explicitly asks.

---

## Final Acceptance Checklist

- [ ] `network_write` exposes exactly `create_subnet`, `update_subnet`, and `delete_subnet` for subnet lifecycle; MCP tool count is unchanged.
- [ ] Only Ethernet and PROFIBUS are accepted; PROFIBUS highest address and the exact ten transmission-speed symbols are validated at host and worker boundaries.
- [ ] Update/delete use exact ordinal `subnetId`; no name, index, device, GSD-derived, or case-insensitive fallback exists.
- [ ] Empty and connected subnet deletion are allowed without dependency enumeration or device deletion.
- [ ] Each subnet operation owns one Openness exclusive-access transaction and commits only after all requested setters succeed.
- [ ] Create/update/delete post-read their result, and every success proves the root network-device count is unchanged.
- [ ] The successful public result contains exactly `subnetId`, `name`, `networkDeviceCount`, and `networkDeviceCountUnchanged`.
- [ ] Malformed or verbose successful worker payloads fail closed as `protocol_error` and are never echoed.
- [ ] Postcondition mismatches use `postcondition_failed`; no automatic retry occurs.
- [ ] The batch remains sequential, stops on first failure, and promises no batch-wide rollback.
- [ ] Preview/apply, exact current-state binding, ten-minute single-use tokens, audit logging, read-only denial, and canonical text/structured equality remain unchanged.
- [ ] No save, compile, node, IO-system, connection, integrated PROFIBUS, generic attribute, or device lifecycle behavior was added.
- [ ] The internal live probes remain separate evidence fixtures and are not public lifecycle routes.
- [ ] Full tests, stub build, real-reference build, coverage review, schema review, and whole-plan audit pass.
- [ ] Separately authorized public TIA Portal V21 live evidence passes before Phase 4 is marked live-verified.
