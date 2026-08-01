# Network Operations Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the four generic-batch network operations with dedicated `network_read` and self-previewing `network_write` tools while preserving their worker behavior and Phase 1 string-valued results.

**Architecture:** Add an independent `TiaMcpServer.Network` domain with its own request, catalog, invoker, read tool, and write tool. Extract request-agnostic execution, result, payload-budget, and state-composition mechanics into `TiaMcpServer.OperationBatches`; the generic Batch and Network domains both depend on that kernel but never on each other.

**Tech Stack:** C#/.NET 8 host, .NET Framework 4.8 Openness worker, `System.Text.Json`, Model Context Protocol C# SDK, xUnit, `TiaMcpServer.FakeWorker`, PowerShell 7, Coverlet.

## Global Constraints

- Work in the current checkout and preserve unrelated user work. Do not create or switch branches or worktrees unless the user requests it.
- Follow TDD for every behavior change: add the focused test, run it and observe the expected failure, then make the smallest production change.
- Remove `read_hardware_config`, `search_equipment_catalog`, `add_network_device`, and `configure_network_device` from generic batches immediately in the public-cutover task. Do not add aliases, adapters, migration-only recognition, or special rejection entries.
- Keep per-operation `result` values as JSON strings until Phase 2.
- Keep `WorkerRequest`, `OperationPolicyCatalog`, `OpennessWorkerClient`, the worker dispatch handlers, `HardwareConfigReader`, `EquipmentCatalogSearcher`, `NetworkDeviceCreator`, and `NetworkDeviceConfigurator` behavior unchanged.
- Do not modify or validate the unverified subnet and IO-system reflection calls.
- `network_read` is registered in read-only and read-write modes; `network_write` is registered only in read-write mode.
- Bind one `network_write` token to the exact ordered request, normalized common project path, ordered targets, and one hardware snapshot per preview/apply attempt.
- Do not add save, compile, transaction, rollback, exclusive-access, post-read, download, commissioning, or live TIA behavior.
- Do not run live TIA Portal operations. Build/test/coverage evidence must be reported separately from runtime evidence.
- Build the solution serially with `-m:1` and `/p:UseTiaPortalReferenceStubs=true`.
- Add every new host source file explicitly to `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`.
- Do not stage or commit unless the user explicitly authorizes commits during execution. Conditional commit steps below are instructions only after that authorization.

---

## File Structure

### Create

- `TiaMcpServer/OperationBatches/IOperationBatchItem.cs` — common identity/path contract for an ordered operation item.
- `TiaMcpServer/OperationBatches/OperationBatchResult.cs` — shared statuses, per-item result, target, and current-state records.
- `TiaMcpServer/OperationBatches/OperationBatchExecutionEngine.cs` — generic independent-read and sequential-write orchestration.
- `TiaMcpServer/OperationBatches/OperationBatchStateComposer.cs` — stable target/state composition and common-path resolution.
- `TiaMcpServer/OperationBatches/OperationBatchResultFormatter.cs` — tool-name-parameterized read, apply, and error envelopes.
- `TiaMcpServer/OperationBatches/OperationBatchPayloadBudget.cs` — tool-name- and guidance-parameterized read budgeting.
- `TiaMcpServer/Network/NetworkOperationRequest.cs` — strict MCP item schema containing network fields only.
- `TiaMcpServer/Network/NetworkOperationCatalog.cs` — four-operation source of truth and pure validation.
- `TiaMcpServer/Network/NetworkWorkerInvoker.cs` — direct mapping from validated network requests to existing worker-client methods.
- `TiaMcpServer/Network/NetworkSafetySnapshot.cs` — network targets, common path, and single hardware-state acquisition.
- `TiaMcpServer/Network/NetworkReadTools.cs` — decorated `network_read` entry point.
- `TiaMcpServer/Network/NetworkWriteTools.cs` — decorated self-previewing `network_write` entry point.
- `TiaMcpServer.Tests/OperationBatchKernelTests.cs` — shared execution, formatting, state, and tool-name invariants.
- `TiaMcpServer.Tests/OperationBatchPayloadBudgetTests.cs` — shared budget preservation and network/generic marker tests.
- `TiaMcpServer.Tests/NetworkOperationCatalogTests.cs` — network schema/catalog validation.
- `TiaMcpServer.Tests/NetworkOperationRequestJsonTests.cs` — strict JSON contract tests.
- `TiaMcpServer.Tests/NetworkFieldForwardingTests.cs` — declared-field-to-worker forwarding invariant.
- `TiaMcpServer.Tests/NetworkToolsTests.cs` — metadata, preview/apply combination, access, and orchestration tests.
- `TiaMcpServer.Tests/NetworkOperationFakeWorkerTests.cs` — end-to-end host/IPC/safety acceptance.

### Modify

- `TiaMcpServer/Program.cs` — register the two network tool classes by access mode.
- `TiaMcpServer/Batch/BatchOperationRequest.cs` — implement the common item contract and remove network-only fields/descriptions.
- `TiaMcpServer/Batch/BatchOperationCatalog.cs` — remove the four operation specs and network field-presence cases.
- `TiaMcpServer/Batch/BatchWorkerInvoker.cs` — remove all four network dispatch/current-state branches and the device-item fallback.
- `TiaMcpServer/Batch/BatchSafetySnapshot.cs` — remove network targets and delegate common composition.
- `TiaMcpServer/Batch/ReadBatchTools.cs` — use the shared kernel and advertise retained generic reads only.
- `TiaMcpServer/Batch/WriteBatchTools.cs` — use the shared kernel and advertise retained generic writes only.
- `TiaMcpServer/Batch/BatchTools.cs` — keep the undecorated test wrapper aligned with the generic-only surface.
- `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` — replace old batching includes and link all Network/shared files.
- `TiaMcpServer.Tests/BatchOperationCatalogTests.cs` — remove network-positive cases and add hard-removal assertions.
- `TiaMcpServer.Tests/BatchOperationRequestJsonTests.cs` — assert network-only fields are absent/rejected.
- `TiaMcpServer.Tests/BatchFieldForwardingTests.cs` — keep only retained generic operations.
- `TiaMcpServer.Tests/BatchToolMetadataTests.cs` — remove network names from descriptions and add absence checks.
- `TiaMcpServer.Tests/BatchToolsTests.cs` — keep generic tool behavior tests using shared mechanics.
- `TiaMcpServer.Tests/McpToolSchemaTests.cs` — approve exactly 14 tools and cover both network schemas.
- `TiaMcpServer.Tests/ReadOnlyModeTests.cs` — approve exactly four read-only tools and fourteen read-write tools.
- `TiaMcpServer.FakeWorker/Program.cs` — add a deterministic `network-roundtrip` test scenario only.
- `README.md` — document the dedicated surface, counts, examples, and safety flow.
- `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md` — replace generic entry points.
- `docs/NETWORK_OPERATIONS_ROADMAP.md` — mark Phase 1 complete after verification and preserve the Phase 2 gate.
- `docs/ARCHITECTURE.md` — document registration, domains, shared kernel, and safety.
- `AGENTS.md` — update counts and repository conventions without erasing file-specific guidance.
- `CLAUDE.md` — update counts and repository conventions without erasing file-specific guidance.

### Delete after callers migrate

- `TiaMcpServer/Batch/BatchOperationResult.cs`
- `TiaMcpServer/Batch/BatchExecutionEngine.cs`
- `TiaMcpServer/Batch/BatchResultFormatter.cs`
- `TiaMcpServer/Batch/BatchPayloadBudget.cs`
- `TiaMcpServer.Tests/BatchExecutionEngineTests.cs`
- `TiaMcpServer.Tests/BatchResultFormatterTests.cs`
- `TiaMcpServer.Tests/BatchPayloadBudgetTests.cs`

---

### Task 1: Extract the shared execution and state-composition kernel

**Files:**
- Create: `TiaMcpServer/OperationBatches/IOperationBatchItem.cs`
- Create: `TiaMcpServer/OperationBatches/OperationBatchResult.cs`
- Create: `TiaMcpServer/OperationBatches/OperationBatchExecutionEngine.cs`
- Create: `TiaMcpServer/OperationBatches/OperationBatchStateComposer.cs`
- Create: `TiaMcpServer.Tests/OperationBatchKernelTests.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj:61-79`

**Interfaces:**
- Consumes: `WorkerCallResult.Ok`, `WorkerCallResult.Fail`, and `WorkerCallResult.ToText()`.
- Produces: `IOperationBatchItem`, `OperationBatchResult`, `OperationBatchStatus`, `OperationBatchTarget`, `OperationBatchCurrentState`, `OperationBatchExecutionEngine.ExecuteReadsAsync<T>`, `OperationBatchExecutionEngine.ApplyWritesAsync<T>`, and `OperationBatchStateComposer`.

- [ ] **Step 1: Link the planned shared files and write failing kernel tests**

Add explicit `<Compile Include>` entries under the existing host-source block:

```xml
<Compile Include="..\TiaMcpServer\OperationBatches\IOperationBatchItem.cs" Link="Host\OperationBatches\IOperationBatchItem.cs" />
<Compile Include="..\TiaMcpServer\OperationBatches\OperationBatchResult.cs" Link="Host\OperationBatches\OperationBatchResult.cs" />
<Compile Include="..\TiaMcpServer\OperationBatches\OperationBatchExecutionEngine.cs" Link="Host\OperationBatches\OperationBatchExecutionEngine.cs" />
<Compile Include="..\TiaMcpServer\OperationBatches\OperationBatchStateComposer.cs" Link="Host\OperationBatches\OperationBatchStateComposer.cs" />
```

Create tests with this local item type and these required assertions:

```csharp
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tests;

public class OperationBatchKernelTests
{
    private sealed record Item(
        string OperationId,
        string Operation,
        string? ProjectPath = null) : IOperationBatchItem;

    [Fact]
    public async Task ExecuteReadsAsync_FailureDoesNotStopLaterItems()
    {
        var items = new[] { new Item("a", "first"), new Item("b", "second") };
        var invoked = new List<string>();

        var results = await OperationBatchExecutionEngine.ExecuteReadsAsync(
            items,
            item =>
            {
                invoked.Add(item.OperationId);
                return Task.FromResult(item.OperationId == "a"
                    ? WorkerCallResult.Fail("validation_error", "bad read")
                    : WorkerCallResult.Ok("{\"ok\":true}"));
            });

        Assert.Equal(new[] { "a", "b" }, invoked);
        Assert.Equal(OperationBatchStatus.Failed, results[0].Status);
        Assert.Equal(OperationBatchStatus.Succeeded, results[1].Status);
    }

    [Fact]
    public async Task ApplyWritesAsync_StopsAndMarksLaterItemsSkipped()
    {
        var items = new[]
        {
            new Item("a", "first"),
            new Item("b", "second"),
            new Item("c", "third")
        };

        var results = await OperationBatchExecutionEngine.ApplyWritesAsync(
            items,
            item => Task.FromResult(item.OperationId == "b"
                ? WorkerCallResult.Fail("worker_operation_failed", "boom")
                : WorkerCallResult.Ok("{}")));

        Assert.Equal(
            new[]
            {
                OperationBatchStatus.Succeeded,
                OperationBatchStatus.Failed,
                OperationBatchStatus.Skipped
            },
            results.Select(result => result.Status));
    }

    [Fact]
    public void StateComposer_IsOrderedAndResolvesOneNormalizedPath()
    {
        var states = new[]
        {
            new OperationBatchCurrentState("a", "first", "one"),
            new OperationBatchCurrentState("b", "second", "two")
        };
        var items = new[]
        {
            new Item("a", "first", @"C:\Projects\Line.ap21"),
            new Item("b", "second")
        };

        Assert.Equal(
            "a::first\none\n--- batch item ---\nb::second\ntwo",
            OperationBatchStateComposer.CombineCurrentState(states));
        Assert.Equal(
            @"C:\Projects\Line.ap21",
            OperationBatchStateComposer.ResolveProjectPath(items));
    }
}
```

- [ ] **Step 2: Run the focused tests and observe the red state**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter FullyQualifiedName~OperationBatchKernelTests
```

Expected: compilation fails because the `TiaMcpServer.OperationBatches` types do not exist.

- [ ] **Step 3: Implement the shared item/result contracts**

Create `IOperationBatchItem.cs`:

```csharp
namespace TiaMcpServer.OperationBatches;

public interface IOperationBatchItem
{
    string OperationId { get; }
    string Operation { get; }
    string? ProjectPath { get; }
}
```

Create `OperationBatchResult.cs`:

```csharp
namespace TiaMcpServer.OperationBatches;

public static class OperationBatchStatus
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
    public const string Omitted = "omitted";
}

public sealed record OperationBatchResult(
    string OperationId,
    string Operation,
    string Status,
    string? Result,
    IReadOnlyList<string>? Warnings = null);

public sealed record OperationBatchTarget(
    string OperationId,
    string Operation,
    string Summary);

public sealed record OperationBatchCurrentState(
    string OperationId,
    string Operation,
    string CurrentState);
```

- [ ] **Step 4: Implement generic execution and state composition**

Create `OperationBatchExecutionEngine.cs` with generic methods constrained to
`IOperationBatchItem`. Preserve order, use `WorkerCallResult.Success` rather than text
prefixes, copy warnings, and produce skipped items without invoking the delegate:

```csharp
using TiaMcpServer.Worker;

namespace TiaMcpServer.OperationBatches;

public static class OperationBatchExecutionEngine
{
    public static async Task<IReadOnlyList<OperationBatchResult>> ExecuteReadsAsync<T>(
        IReadOnlyList<T> operations,
        Func<T, Task<WorkerCallResult>> invoke)
        where T : IOperationBatchItem
    {
        var results = new List<OperationBatchResult>(operations.Count);
        foreach (var operation in operations)
        {
            results.Add(ToResult(operation, await invoke(operation).ConfigureAwait(false)));
        }

        return results;
    }

    public static async Task<IReadOnlyList<OperationBatchResult>> ApplyWritesAsync<T>(
        IReadOnlyList<T> operations,
        Func<T, Task<WorkerCallResult>> invoke)
        where T : IOperationBatchItem
    {
        var results = new List<OperationBatchResult>(operations.Count);
        var stopped = false;
        foreach (var operation in operations)
        {
            if (stopped)
            {
                results.Add(new OperationBatchResult(
                    operation.OperationId,
                    operation.Operation,
                    OperationBatchStatus.Skipped,
                    null));
                continue;
            }

            var workerResult = await invoke(operation).ConfigureAwait(false);
            stopped = !workerResult.Success;
            results.Add(ToResult(operation, workerResult));
        }

        return results;
    }

    private static OperationBatchResult ToResult(
        IOperationBatchItem operation,
        WorkerCallResult workerResult)
        => new(
            operation.OperationId,
            operation.Operation,
            workerResult.Success ? OperationBatchStatus.Succeeded : OperationBatchStatus.Failed,
            workerResult.ToText(),
            workerResult.Warnings.Count == 0 ? null : workerResult.Warnings);
}
```

Create `OperationBatchStateComposer.cs` by moving the current separator/composition logic
from `BatchSafetySnapshot`, using `IOperationBatchItem` for path resolution.

- [ ] **Step 5: Run the focused tests and verify green**

Run the Task 1 command again.

Expected: all `OperationBatchKernelTests` pass.

- [ ] **Step 6: Conditional commit**

Only if the user authorized commits:

```powershell
git add TiaMcpServer/OperationBatches TiaMcpServer.Tests/OperationBatchKernelTests.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
git commit -m "refactor: extract operation batch execution kernel"
```

Otherwise leave the verified files unstaged.

---

### Task 2: Share response formatting and payload budgeting, then migrate generic batches

**Files:**
- Create: `TiaMcpServer/OperationBatches/OperationBatchResultFormatter.cs`
- Create: `TiaMcpServer/OperationBatches/OperationBatchPayloadBudget.cs`
- Create: `TiaMcpServer.Tests/OperationBatchPayloadBudgetTests.cs`
- Modify: `TiaMcpServer.Tests/OperationBatchKernelTests.cs`
- Modify: `TiaMcpServer/Batch/BatchOperationRequest.cs:15-121`
- Modify: `TiaMcpServer/Batch/BatchSafetySnapshot.cs:15-60`
- Modify: `TiaMcpServer/Batch/ReadBatchTools.cs:14-44`
- Modify: `TiaMcpServer/Batch/WriteBatchTools.cs:18-161`
- Modify: `TiaMcpServer/Batch/BatchTools.cs:19-119`
- Modify: `TiaMcpServer.Tests/BatchToolsTests.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj:61-79`
- Delete: `TiaMcpServer/Batch/BatchOperationResult.cs`
- Delete: `TiaMcpServer/Batch/BatchExecutionEngine.cs`
- Delete: `TiaMcpServer/Batch/BatchResultFormatter.cs`
- Delete: `TiaMcpServer/Batch/BatchPayloadBudget.cs`
- Delete: `TiaMcpServer.Tests/BatchExecutionEngineTests.cs`
- Delete: `TiaMcpServer.Tests/BatchResultFormatterTests.cs`
- Delete: `TiaMcpServer.Tests/BatchPayloadBudgetTests.cs`

**Interfaces:**
- Consumes: all Task 1 shared types.
- Produces: `OperationBatchResultFormatter.Error`, `.Read`, `.Apply`, and `OperationBatchPayloadBudget.Apply` with tool-specific marker arguments. Makes `BatchOperationRequest` an `IOperationBatchItem`.

- [ ] **Step 1: Write failing tool-name and budget tests**

Add formatter assertions to `OperationBatchKernelTests`:

```csharp
[Fact]
public void ReadFormatter_UsesCallerSuppliedToolAndKeepsResultAsString()
{
    var json = OperationBatchResultFormatter.Read(
        "network_read",
        new[]
        {
            new OperationBatchResult(
                "a",
                "read_hardware_config",
                OperationBatchStatus.Succeeded,
                "{\"devices\":[]}")
        });

    using var document = JsonDocument.Parse(json);
    Assert.Equal("network_read", document.RootElement.GetProperty("tool").GetString());
    Assert.Equal(
        JsonValueKind.String,
        document.RootElement.GetProperty("operations")[0].GetProperty("result").ValueKind);
}
```

Create budget tests that call:

```csharp
var budgeted = OperationBatchPayloadBudget.Apply(
    results,
    toolName: "network_read",
    retryToolName: "network_read",
    narrowingHint: "Use query/maxResults or split the batch.",
    maxItemChars: 80,
    maxBatchChars: 500);
```

Assert that oversized results are truncated, combined overflow becomes `omitted`, the
marker names `network_read`, warnings survive, input records are not mutated, and a failed
item remains represented with `status == "failed"`.

- [ ] **Step 2: Run the focused tests and observe the red state**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~OperationBatchKernelTests|FullyQualifiedName~OperationBatchPayloadBudgetTests"
```

Expected: compilation fails because the shared formatter and budget do not exist.

- [ ] **Step 3: Implement the shared formatter**

Create a formatter with these exact public signatures:

```csharp
public static string Error(string toolName, string error);
public static string Read(string toolName, IReadOnlyList<OperationBatchResult> results);
public static string Apply(string toolName, IReadOnlyList<OperationBatchResult> results);
```

Move the current JSON projections unchanged except for the supplied `toolName` and shared
types. `Read` counts `failed` and `omitted`; `Apply` counts `failed` and `skipped`. Continue
serializing with `TiaJson.Presentation`.

- [ ] **Step 4: Implement the parameterized payload budget**

Move the complete existing budget algorithm to `OperationBatchPayloadBudget`. Replace
hard-coded envelope and retry strings with the method parameters:

```csharp
public static IReadOnlyList<OperationBatchResult> Apply(
    IReadOnlyList<OperationBatchResult> results,
    string toolName,
    string retryToolName,
    string narrowingHint)
    => Apply(
        results,
        toolName,
        retryToolName,
        narrowingHint,
        MaxItemChars,
        MaxBatchChars);
```

The internal final-length check must call
`OperationBatchResultFormatter.Read(toolName, candidateResults).Length`. Build full markers
from `retryToolName` and `narrowingHint`; retain the existing compact fallback markers when
the configured caps cannot fit the full message.

- [ ] **Step 5: Migrate generic batching to the shared kernel**

Make the request declaration:

```csharp
public sealed class BatchOperationRequest : IOperationBatchItem
```

Replace generic tool calls as follows:

```csharp
var results = await OperationBatchExecutionEngine.ExecuteReadsAsync(
    operations,
    operation => BatchWorkerInvoker.InvokeAsync(workerClient, operation)).ConfigureAwait(false);

var budgeted = OperationBatchPayloadBudget.Apply(
    results,
    toolName: "execute_read_batch",
    retryToolName: "execute_read_batch",
    narrowingHint: "Use plcName/filter/maxResults or split the batch.");

return OperationBatchResultFormatter.Read("execute_read_batch", budgeted);
```

Use `OperationBatchExecutionEngine.ApplyWritesAsync` and
`OperationBatchResultFormatter.Apply("apply_write_batch", results)` for generic writes.
Use `OperationBatchResultFormatter.Error` at every generic validation/safety error site.
Replace batch-specific target/current-state records with `OperationBatchTarget` and
`OperationBatchCurrentState`; delegate path and state composition to
`OperationBatchStateComposer`.

Delete the old shared-mechanics files and replace their test-project includes with the new
OperationBatches includes. Move every still-relevant legacy assertion into the new shared
test files before deleting the three legacy test files.

- [ ] **Step 6: Run focused and full generic batch regression tests**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~OperationBatch|FullyQualifiedName~BatchToolsTests|FullyQualifiedName~BatchOperationCatalogTests"
```

Expected: all selected tests pass; generic public JSON remains byte-shape compatible.

- [ ] **Step 7: Conditional commit**

Only if authorized:

```powershell
git add TiaMcpServer/OperationBatches TiaMcpServer/Batch TiaMcpServer.Tests
git commit -m "refactor: share batch formatting and budgeting"
```

Otherwise leave the verified files unstaged.

---

### Task 3: Add the independent network request, catalog, and invoker

**Files:**
- Create: `TiaMcpServer/Network/NetworkOperationRequest.cs`
- Create: `TiaMcpServer/Network/NetworkOperationCatalog.cs`
- Create: `TiaMcpServer/Network/NetworkWorkerInvoker.cs`
- Create: `TiaMcpServer/Network/NetworkSafetySnapshot.cs`
- Create: `TiaMcpServer.Tests/NetworkOperationCatalogTests.cs`
- Create: `TiaMcpServer.Tests/NetworkOperationRequestJsonTests.cs`
- Create: `TiaMcpServer.Tests/NetworkFieldForwardingTests.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`

**Interfaces:**
- Consumes: shared operation-batch types, `OperationPolicyCatalog`, and the four existing `OpennessWorkerClient` methods.
- Produces: validated `NetworkOperationRequest[]`, `NetworkOperationCatalog.ValidateRead`, `.ValidateWrite`, `.ValidateAccessMode`, `NetworkWorkerInvoker.InvokeReadAsync`, `.InvokeWriteAsync`, and `NetworkSafetySnapshot`.

- [ ] **Step 1: Write failing strict-JSON and exact-catalog tests**

Define expected catalog membership exactly:

```csharp
var expected = new Dictionary<string, (NetworkOperationCategory Category, string[] Required, string[] Optional)>
{
    ["read_hardware_config"] = (NetworkOperationCategory.Read, Array.Empty<string>(), Array.Empty<string>()),
    ["search_equipment_catalog"] = (NetworkOperationCategory.Read, new[] { "query" }, new[] { "maxResults" }),
    ["add_network_device"] = (NetworkOperationCategory.Write, new[] { "typeIdentifier", "deviceName" }, new[] { "deviceItemName" }),
    ["configure_network_device"] = (NetworkOperationCategory.Write, new[] { "deviceName" }, new[] { "ipAddress", "subnetMask", "pnDeviceName", "subnetName", "ioSystemName" })
};
```

Add tests for empty/51-item batches, duplicate IDs, wrong category, missing fields,
inapplicable fields, `maxResults == 0`, mixed normalized write paths, and a valid
no-settings `configure_network_device` item.

Add JSON tests using `JsonSerializerOptions(JsonSerializerDefaults.Web)` that prove
`ipAddress` binds and misspelled `ip_adress` throws `JsonException`.

- [ ] **Step 2: Write the failing forwarding invariant**

For every catalog spec, populate each required/optional field with a unique sentinel,
set `ProjectPath = "echo"`, call the appropriate Network invoker method against
`TiaMcpServer.FakeWorker`, and parse the echoed `WorkerRequest`. Assert each declared field
occurs once with the expected value. Add a dedicated assertion that omitted
`deviceItemName` becomes `deviceName` only for `add_network_device`.

- [ ] **Step 3: Run the network contract tests and observe the red state**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~NetworkOperationCatalogTests|FullyQualifiedName~NetworkOperationRequestJsonTests|FullyQualifiedName~NetworkFieldForwardingTests"
```

Expected: compilation fails because the Network domain does not exist.

- [ ] **Step 4: Implement the strict request DTO**

Create the complete property surface:

```csharp
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class NetworkOperationRequest : IOperationBatchItem
{
    public string OperationId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string? ProjectPath { get; set; }
    public string? Query { get; set; }
    public int? MaxResults { get; set; }
    public string? TypeIdentifier { get; set; }
    public string? DeviceName { get; set; }
    public string? DeviceItemName { get; set; }
    public string? IpAddress { get; set; }
    public string? SubnetMask { get; set; }
    public string? PnDeviceName { get; set; }
    public string? SubnetName { get; set; }
    public string? IoSystemName { get; set; }
}
```

Add exact `[Description]` attributes naming the owning operations and the role of
`operationId`/`projectPath`.

- [ ] **Step 5: Implement the pure network catalog**

Use `MaxBatchSize = 50`, exact read/write specs from Step 1, and reflection over the strict
DTO to reject populated fields outside the selected spec. Treat `operationId`, `operation`,
and `projectPath` as universal. Validate required fields with a closed switch covering
`query`, `typeIdentifier`, and `deviceName`. Validate explicit write paths with
`WriteSafetyService.NormalizeProjectPath` and `StringComparer.OrdinalIgnoreCase`.

`ValidateAccessMode` must call `OperationPolicyCatalog.IsAllowed(mode, operation)` for
every named operation and return one error per denied item.

- [ ] **Step 6: Implement direct worker invocation and the safety snapshot seam**

Create these methods:

```csharp
public static Task<WorkerCallResult> InvokeReadAsync(
    OpennessWorkerClient client,
    NetworkOperationRequest operation);

public static Task<WorkerCallResult> InvokeWriteAsync(
    OpennessWorkerClient client,
    NetworkOperationRequest operation,
    string? commonProjectPath);
```

Dispatch reads directly to `ReadHardwareConfigAsync` and
`SearchEquipmentCatalogAsync`. Dispatch writes directly to `AddNetworkDeviceAsync` and
`ConfigureNetworkDeviceAsync`. Use `commonProjectPath ?? operation.ProjectPath` for writes.
Return `validation_error` for an unsupported switch arm.

`NetworkSafetySnapshot` must expose:

```csharp
public static IReadOnlyList<OperationBatchTarget> BuildTargets(
    IReadOnlyList<NetworkOperationRequest> operations);

public static string? ResolveProjectPath(
    IReadOnlyList<NetworkOperationRequest> operations);

public static async Task<WorkerCallResult> ReadCurrentStateAsync(
    OpennessWorkerClient client,
    string? projectPath);
```

Target summaries must name the device and, for add, the catalog type. Current-state
acquisition calls `ReadHardwareConfigAsync` exactly once.

- [ ] **Step 7: Run the Task 3 tests and verify green**

Run the Task 3 command again.

Expected: all Network contract, JSON, and forwarding tests pass without registering any new MCP tool.

- [ ] **Step 8: Conditional commit**

Only if authorized:

```powershell
git add TiaMcpServer/Network TiaMcpServer.Tests/NetworkOperationCatalogTests.cs TiaMcpServer.Tests/NetworkOperationRequestJsonTests.cs TiaMcpServer.Tests/NetworkFieldForwardingTests.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
git commit -m "feat: add network operation contracts"
```

Otherwise leave the verified files unstaged.

---

### Task 4: Atomically expose dedicated network tools and remove the generic surface

**Files:**
- Create: `TiaMcpServer/Network/NetworkReadTools.cs`
- Create: `TiaMcpServer/Network/NetworkWriteTools.cs`
- Create: `TiaMcpServer.Tests/NetworkToolsTests.cs`
- Modify: `TiaMcpServer/Program.cs:55-75`
- Modify: `TiaMcpServer/Batch/BatchOperationRequest.cs:21-102`
- Modify: `TiaMcpServer/Batch/BatchOperationCatalog.cs:180-305`
- Modify: `TiaMcpServer/Batch/BatchWorkerInvoker.cs:14-87`
- Modify: `TiaMcpServer/Batch/BatchSafetySnapshot.cs:24-39`
- Modify: `TiaMcpServer/Batch/ReadBatchTools.cs:14-44`
- Modify: `TiaMcpServer/Batch/WriteBatchTools.cs:18-141`
- Modify: `TiaMcpServer/Batch/BatchTools.cs:19-119`
- Modify: `TiaMcpServer.Tests/BatchOperationCatalogTests.cs`
- Modify: `TiaMcpServer.Tests/BatchOperationRequestJsonTests.cs`
- Modify: `TiaMcpServer.Tests/BatchFieldForwardingTests.cs`
- Modify: `TiaMcpServer.Tests/BatchToolMetadataTests.cs`
- Modify: `TiaMcpServer.Tests/BatchToolsTests.cs`
- Modify: `TiaMcpServer.Tests/McpToolSchemaTests.cs:91-139`
- Modify: `TiaMcpServer.Tests/ReadOnlyModeTests.cs:464-609`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`

**Interfaces:**
- Consumes: all Task 1-3 interfaces and `WriteSafetyService`.
- Produces: MCP tools `network_read` and `network_write`; generic batch catalogs contain no network operations.

- [ ] **Step 1: Write failing atomic-cutover tests before production edits**

Add tests that assert:

```csharp
Assert.Equal(
    new[] { "read_hardware_config", "search_equipment_catalog" },
    NetworkOperationCatalog.ReadOperationNames);
Assert.Equal(
    new[] { "add_network_device", "configure_network_device" },
    NetworkOperationCatalog.WriteOperationNames);

foreach (var operation in new[]
{
    "read_hardware_config",
    "search_equipment_catalog",
    "add_network_device",
    "configure_network_device"
})
{
    Assert.DoesNotContain(operation, BatchOperationCatalog.ReadOperationNames);
    Assert.DoesNotContain(operation, BatchOperationCatalog.WriteOperationNames);
}
```

Add reflection assertions that `BatchOperationRequest` has none of these CLR properties:
`Query`, `TypeIdentifier`, `DeviceName`, `DeviceItemName`, `IpAddress`, `SubnetMask`,
`PnDeviceName`, `SubnetName`, `IoSystemName`. Deserialize one payload containing
`deviceName` and assert `JsonException`.

Add tool metadata tests for exact names and method annotations. Update the approved
read-write tool list to exactly:

```csharp
var expected = new[]
{
    "get_project_status",
    "browse_project_tree",
    "execute_read_batch",
    "compile_check",
    "open_project",
    "create_project",
    "save_project",
    "save_project_as",
    "archive_project",
    "close_project",
    "preview_write_batch",
    "apply_write_batch",
    "network_read",
    "network_write"
};
```

The exact read-only list is `get_project_status`, `browse_project_tree`,
`execute_read_batch`, and `network_read`.

- [ ] **Step 2: Write failing `network_read` behavior tests**

Using a FakeWorker-backed client, assert an empty batch is rejected before worker startup,
a write operation is rejected as the wrong category, successful payloads remain JSON
strings, warnings are copied, one read failure does not stop a later operation, and
budget markers instruct retry through `network_read`.

- [ ] **Step 3: Write failing `network_write` combination and safety tests**

Cover all four tool-level combinations:

```csharp
[Theory]
[InlineData(false, null, "safetyToken")]
[InlineData(false, "supplied", "confirm=false")]
[InlineData(true, null, "preview")]
public async Task NetworkWrite_RejectsInvalidConfirmationCombinations(
    bool confirm,
    string? token,
    string expectedText)
```

Add preview assertions for `tool == "network_write"`, non-empty `safetyToken`, exact
ordered targets, and no write invocation. Add apply tests for reordered input rejection,
changed field rejection, missing project consistency, read-only defense, successful apply,
first-failure stopping, and later `skipped` results.

- [ ] **Step 4: Run the atomic-cutover tests and observe red**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~NetworkToolsTests|FullyQualifiedName~McpToolSchemaTests|FullyQualifiedName~ReadOnlyModeTests|FullyQualifiedName~BatchOperationCatalogTests|FullyQualifiedName~BatchOperationRequestJsonTests|FullyQualifiedName~BatchToolMetadataTests"
```

Expected: failures show missing tools, old 12/3 counts, and the four operations/fields still present in generic batches.

- [ ] **Step 5: Implement `NetworkReadTools`**

Use one decorated class and method:

```csharp
[McpServerToolType]
public class NetworkReadTools
{
    [McpServerTool(
        Name = "network_read",
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false)]
    public static async Task<string> NetworkRead(
        OpennessWorkerClient workerClient,
        NetworkOperationRequest[] operations)
    {
        var validation = NetworkOperationCatalog.ValidateRead(operations);
        if (!validation.IsValid)
        {
            return OperationBatchResultFormatter.Error("network_read", validation.Error);
        }

        var mode = workerClient.AccessPolicy?.Mode ?? McpAccessMode.ReadWrite;
        var accessErrors = NetworkOperationCatalog.ValidateAccessMode(operations, mode);
        if (accessErrors.Count != 0)
        {
            return OperationBatchResultFormatter.Error(
                "network_read",
                string.Join("\n", accessErrors));
        }

        var results = await OperationBatchExecutionEngine.ExecuteReadsAsync(
            operations,
            operation => NetworkWorkerInvoker.InvokeReadAsync(workerClient, operation))
            .ConfigureAwait(false);
        var budgeted = OperationBatchPayloadBudget.Apply(
            results,
            "network_read",
            "network_read",
            "Use query/maxResults or split the batch.");
        return OperationBatchResultFormatter.Read("network_read", budgeted);
    }
}
```

Add complete `[Description]` attributes to the tool and `operations` parameter.

- [ ] **Step 6: Implement self-previewing `NetworkWriteTools`**

The method signature is:

```csharp
public static async Task<string> NetworkWrite(
    OpennessWorkerClient workerClient,
    WriteSafetyService safety,
    NetworkOperationRequest[] operations,
    bool confirm = false,
    string? safetyToken = null)
```

Implement this exact decision order:

1. Validate the write batch and read-write mode.
2. Reject `confirm == false` with a supplied token.
3. Reject `confirm == true` without a token.
4. Resolve the common project path and ordered targets.
5. For preview, read one hardware state and call `CreatePreview` with tool name
   `network_write`, exact `operations`, and instructions to call the same tool with
   `confirm=true`.
6. For apply, call `ValidateEnvelope` before the worker state read, read one fresh state,
   then call `ValidateAndConsume`.
7. Apply through `OperationBatchExecutionEngine.ApplyWritesAsync`, passing the resolved
   path to `NetworkWorkerInvoker.InvokeWriteAsync`.
8. Format with `OperationBatchResultFormatter.Apply("network_write", results)` and append
   one audit record.

Any snapshot failure returns `OperationBatchResultFormatter.Error` and performs no write.

- [ ] **Step 7: Perform the immediate generic removal in the same production edit**

Remove the four specs, dispatch arms, current-state arms, target descriptions, network-only
request properties, required-field cases, and network descriptions. Keep `MaxResults` for
`read_cross_references`. Do not add the names to `NonBatchableOperations` or any new list.

Update generic tool descriptions so retained reads are exactly
`read_cross_references`, `get_block_content`, `list_tag_tables`, and `get_type_content`;
retained writes exclude both network writes. Update generic narrowing guidance to mention
only `plcName`, `filter`, and `maxResults`.

- [ ] **Step 8: Register the tool classes by access mode**

Change startup registration to:

```csharp
var mcp = builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<ProjectReadTools>()
    .WithTools<ReadBatchTools>()
    .WithTools<NetworkReadTools>();

if (accessMode == McpAccessMode.ReadWrite)
{
    mcp.WithTools<ProjectEngineeringTools>()
       .WithTools<ProjectWriteTools>()
       .WithTools<WriteBatchTools>()
       .WithTools<NetworkWriteTools>();
}
```

- [ ] **Step 9: Run the cutover suite and verify green**

Run the Task 4 command again.

Expected: all selected tests pass; the schema reports 14 read-write tools and read-only
tests report exactly four tools.

- [ ] **Step 10: Run a bounded repository assertion for hard removal**

Run:

```powershell
$genericFiles = @(
  'TiaMcpServer/Batch/BatchOperationRequest.cs',
  'TiaMcpServer/Batch/BatchOperationCatalog.cs',
  'TiaMcpServer/Batch/BatchWorkerInvoker.cs',
  'TiaMcpServer/Batch/BatchSafetySnapshot.cs',
  'TiaMcpServer/Batch/ReadBatchTools.cs',
  'TiaMcpServer/Batch/WriteBatchTools.cs',
  'TiaMcpServer/Batch/BatchTools.cs'
)
Select-String -Path $genericFiles -Pattern 'read_hardware_config|search_equipment_catalog|add_network_device|configure_network_device'
```

Expected: no matches.

- [ ] **Step 11: Conditional commit**

Only if authorized:

```powershell
git add TiaMcpServer/Network TiaMcpServer/Program.cs TiaMcpServer/Batch TiaMcpServer.Tests
git commit -m "feat: separate network tools from generic batches"
```

Otherwise leave the verified files unstaged.

---

### Task 5: Add FakeWorker acceptance for single-snapshot safety and token lifecycle

**Files:**
- Modify: `TiaMcpServer.FakeWorker/Program.cs:1-223`
- Create: `TiaMcpServer.Tests/NetworkOperationFakeWorkerTests.cs`

**Interfaces:**
- Consumes: public `NetworkReadTools.NetworkRead`, `NetworkWriteTools.NetworkWrite`, and FakeWorker process sequencing.
- Produces: end-to-end evidence that preview/apply use the existing IPC path, one snapshot per attempt, exact token binding, and string-valued results.

- [ ] **Step 1: Write failing end-to-end tests against a new scenario**

Use a client created by:

```csharp
private static OpennessWorkerClient CreateClient()
    => new(
        new ProjectSessionBinding(null),
        logger: null,
        workerExecutablePath: FakeWorkerLocator.Locate());
```

Set every operation `ProjectPath = "network-roundtrip"`. Add tests that:

- call `network_read` with hardware and catalog items and assert both succeed in order;
- preview add+configure, extract the token, apply unchanged, and assert add result contains
  `"seq":3` and configure result contains `"seq":4`;
- replay the consumed token and assert rejection;
- preview again, change `ipAddress`, and assert input-hash rejection;
- assert each operation result's JSON kind is `String`.

The sequence assertion proves preview used request 1 for one snapshot and apply used request
2 for one snapshot before the two writes. A repeated per-item state read would shift the
write sequence numbers and fail.

- [ ] **Step 2: Run the acceptance tests and observe red**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter FullyQualifiedName~NetworkOperationFakeWorkerTests
```

Expected: tests fail because FakeWorker reports unknown scenario `network-roundtrip`.

- [ ] **Step 3: Add deterministic FakeWorker method dispatch**

Add one case:

```csharp
case "network-roundtrip":
    Respond(ReadMethod(line) switch
    {
        "read_hardware_config" => $$"""{"success":true,"payload":"{\"kind\":\"hardware\",\"seq\":{{seq}}}"}""",
        "search_equipment_catalog" => $$"""{"success":true,"payload":"[{\"typeIdentifier\":\"OrderNumber:TEST\",\"seq\":{{seq}}}]"}""",
        "add_network_device" => $$"""{"success":true,"payload":"{\"operation\":\"add\",\"seq\":{{seq}}}"}""",
        "configure_network_device" => $$"""{"success":true,"payload":"{\"operation\":\"configure\",\"seq\":{{seq}}}"}""",
        _ => $$"""{"success":false,"error":"unexpected network method '{{ReadMethod(line)}}'"}"""
    });
    break;
```

Do not change any existing scenario.

- [ ] **Step 4: Run acceptance and targeted safety tests**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~NetworkOperationFakeWorkerTests|FullyQualifiedName~NetworkToolsTests"
```

Expected: all selected tests pass.

- [ ] **Step 5: Conditional commit**

Only if authorized:

```powershell
git add TiaMcpServer.FakeWorker/Program.cs TiaMcpServer.Tests/NetworkOperationFakeWorkerTests.cs
git commit -m "test: cover network tool safety flow"
```

Otherwise leave the verified files unstaged.

---

### Task 6: Align all current documentation and repository instructions

**Files:**
- Modify: `README.md`
- Modify: `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md`
- Modify: `docs/NETWORK_OPERATIONS_ROADMAP.md`
- Modify: `docs/ARCHITECTURE.md`
- Modify: `AGENTS.md`
- Modify: `CLAUDE.md`
- Test: `TiaMcpServer.Tests/BatchToolMetadataTests.cs`
- Test: `TiaMcpServer.Tests/McpToolSchemaTests.cs`

**Interfaces:**
- Consumes: final 14/4 tool surface and approved Phase 1 behavior.
- Produces: consistent user, architecture, and agent-maintainer documentation with Phase 2 still open.

- [ ] **Step 1: Add failing stale-documentation contract tests where executable**

Extend metadata tests to assert generic tool descriptions exclude all four network names
and both network tool descriptions contain every operation from their catalog category.
Keep the exact-schema test as the executable source for 14/4 counts.

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~BatchToolMetadataTests|FullyQualifiedName~McpToolSchemaTests"
```

Expected before metadata edits: failures name stale generic descriptions or missing network descriptions.

- [ ] **Step 2: Update `README.md`**

Apply all of these concrete changes:

- Replace 12/3 counts with 14/4.
- Add `network_read` and `network_write` to the Tools section.
- List only retained operations under generic read/write batches.
- Move hardware, catalog, add-device, and configure-device JSON examples to the dedicated
  tools.
- Show `network_write` preview as `confirm:false` without a token and apply as
  `confirm:true` with the unchanged list and returned token.
- Update read-only discovery and smoke-test steps.
- Keep the warning that static hardware data does not certify commissioning.

- [ ] **Step 3: Update the network summary and roadmap**

In `NETWORK_OPERATIONS_SUMMARY.md`, change entry points and workflow to the dedicated tools,
retain no-rollback semantics, and keep every current capability limit.

In `NETWORK_OPERATIONS_ROADMAP.md`, mark Phase 1 complete only after Task 7 passes. State
that Phase 1 intentionally retains string-valued results and Phase 2 remains the mandatory
single-layer JSON contract gate.

- [ ] **Step 4: Update `docs/ARCHITECTURE.md`**

Document:

- 14 read-write tools and four read-only tools;
- always-registered `NetworkReadTools` and read-write-only `NetworkWriteTools`;
- separate Batch and Network catalogs/invokers;
- `OperationBatches` as a request-agnostic dependency of both domains;
- self-previewing `network_write`, one topology snapshot per attempt, exact ordered binding,
  sequential no-rollback apply, and audit record;
- network-specific payload-budget hints;
- linked-source and FakeWorker coverage.

- [ ] **Step 5: Update `AGENTS.md` and `CLAUDE.md` carefully**

In both files:

- replace the stale `Exposes 10 tools` statement with the 14/4 mode split;
- add `TiaMcpServer/Network/` and `TiaMcpServer/OperationBatches/` to solution conventions;
- describe generic batch, self-previewing network, and self-previewing lifecycle write flows;
- require network operations to use their own request/catalog/invoker;
- extend linked-source test instructions to the new directories;
- state that a new worker method belongs in its owning domain catalog and is not
  automatically a generic batch operation.

Preserve the extra type-write guidance present in `CLAUDE.md` and every other
file-specific instruction.

- [ ] **Step 6: Run documentation/metadata checks and a bounded stale-claim search**

Run the Task 6 test command again, then:

```powershell
$docs = @(
  'README.md',
  'docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md',
  'docs/NETWORK_OPERATIONS_ROADMAP.md',
  'docs/ARCHITECTURE.md',
  'AGENTS.md',
  'CLAUDE.md'
)
Select-String -Path $docs -Pattern '12-tool|12 tools|3 tools|Exposes 10 tools'
```

Expected: tests pass and the stale-count search returns no matches. Manually inspect every
remaining occurrence of the four operation names and confirm each appears only in the
Network domain, worker/internal explanation, removal note, or historical roadmap context.

- [ ] **Step 7: Conditional commit**

Only if authorized:

```powershell
git add README.md docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md docs/NETWORK_OPERATIONS_ROADMAP.md docs/ARCHITECTURE.md AGENTS.md CLAUDE.md TiaMcpServer.Tests/BatchToolMetadataTests.cs TiaMcpServer.Tests/McpToolSchemaTests.cs
git commit -m "docs: document dedicated network tools"
```

Otherwise leave the verified files unstaged.

---

### Task 7: Run full verification and review the complete Phase 1 diff

**Files:**
- Review: every file listed in this plan
- Verify unchanged: `TiaMcpServer.Contracts/WorkerRequest.cs`
- Verify unchanged: `TiaMcpServer.Contracts/OperationPolicyCatalog.cs`
- Verify unchanged: `TiaMcpServer/Worker/OpennessWorkerClient.cs`
- Verify unchanged: `TiaMcpServer.OpennessWorker/Program.cs`
- Verify unchanged: `TiaMcpServer.OpennessWorker/Openness/HardwareConfigReader.cs`
- Verify unchanged: `TiaMcpServer.OpennessWorker/Openness/EquipmentCatalogSearcher.cs`
- Verify unchanged: `TiaMcpServer.OpennessWorker/Openness/NetworkDeviceCreator.cs`
- Verify unchanged: `TiaMcpServer.OpennessWorker/Openness/NetworkDeviceConfigurator.cs`

**Interfaces:**
- Consumes: the complete Phase 1 implementation.
- Produces: fresh build, suite, coverage, schema, documentation, and scope evidence; no runtime claim.

- [ ] **Step 1: Restore only if dependencies are not already available**

Run only when the assets file or packages are missing:

```powershell
dotnet restore TiaMcpServer.sln
```

Expected: restore succeeds. If it fails with `NU1301`, report dependency reachability and do
not diagnose it as a code failure.

- [ ] **Step 2: Run the serialized Release stub build**

```powershell
dotnet build TiaMcpServer.sln -m:1 --no-restore --configuration Release /p:UseTiaPortalReferenceStubs=true
```

Expected: exit code 0, zero build errors.

- [ ] **Step 3: Run the complete suite with coverage**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build --configuration Release --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory TestResults
```

Expected: all tests pass and exactly one new `coverage.cobertura.xml` is produced under
`TestResults` for this run.

- [ ] **Step 4: Enforce the repository coverage threshold**

```powershell
$reports = @(Get-ChildItem -Path TestResults -Recurse -Filter coverage.cobertura.xml)
if ($reports.Count -ne 1) { throw "Expected exactly one coverage report, found $($reports.Count)." }
./scripts/verify-coverage-threshold.ps1 -CoveragePath $reports[0].FullName -MinimumLineRate 0.80
```

Expected: threshold check passes at or above `0.80` line coverage.

- [ ] **Step 5: Re-run the exact public-surface and removal gates**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build --configuration Release --filter "FullyQualifiedName~McpToolSchemaTests|FullyQualifiedName~ReadOnlyModeTests|FullyQualifiedName~NetworkOperationFakeWorkerTests"
```

Expected: exact 14/4 tool tests, read-only exclusion, and FakeWorker safety acceptance pass.

Run the Task 4 generic-file search again and expect no network-name matches.

- [ ] **Step 6: Review scope and unchanged worker files**

Run:

```powershell
git diff --check
git status --short
git diff --name-only
git diff -- TiaMcpServer.Contracts/WorkerRequest.cs TiaMcpServer.Contracts/OperationPolicyCatalog.cs TiaMcpServer/Worker/OpennessWorkerClient.cs TiaMcpServer.OpennessWorker/Program.cs TiaMcpServer.OpennessWorker/Openness/HardwareConfigReader.cs TiaMcpServer.OpennessWorker/Openness/EquipmentCatalogSearcher.cs TiaMcpServer.OpennessWorker/Openness/NetworkDeviceCreator.cs TiaMcpServer.OpennessWorker/Openness/NetworkDeviceConfigurator.cs
```

Expected: `git diff --check` is clean, only planned files are changed, and the final worker/
Openness diff command prints no patch.

- [ ] **Step 7: Perform the final correctness/security review**

Confirm from the diff:

- `NetworkReadTools` and `NetworkWriteTools` are separate decorated types;
- `network_write` cannot be registered in read-only mode;
- unknown and inapplicable fields fail before worker access;
- token envelope validation happens before the apply state read;
- state validation and token consumption happen before the first write;
- one hardware snapshot is acquired per preview/apply attempt;
- writes stop after the first failure and later results are `skipped`;
- generic batches contain no network operation recognition;
- per-operation results remain strings;
- no secret, machine-specific fixture, Siemens DLL, or generated TestResults artifact is included.

- [ ] **Step 8: Report evidence and remaining runtime gate**

Report exact build configuration, test count, coverage rate, schema counts, and diff-review
result. State explicitly:

> No live TIA Portal operation was run. Phase 1 changes only host contracts and
> orchestration; existing worker/Openness network behavior remains runtime-unverified by
> this delivery.

- [ ] **Step 9: Conditional final commit**

If the user authorized commits and earlier conditional commits were intentionally skipped,
create one reviewed commit:

```powershell
git add TiaMcpServer TiaMcpServer.Tests TiaMcpServer.FakeWorker README.md docs AGENTS.md CLAUDE.md
git commit -m "feat: separate network operations from generic batches"
```

Do not push or open a pull request without separate explicit approval.
