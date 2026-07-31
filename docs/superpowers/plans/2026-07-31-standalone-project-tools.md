# Standalone Project Tools Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose `get_project_status`, `browse_project_tree`, and `compile_check` as standalone project tools and remove all three from `execute_read_batch` without changing worker protocol or TIA Openness behavior.

**Architecture:** Keep the existing `OpennessWorkerClient` and .NET Framework worker methods unchanged. Add thin standalone host-tool adapters, conditionally register the engineering-only compile tool, preserve the existing 60,000-character per-result cap, and narrow the generic read-batch catalog and schema to six non-project operations.

**Tech Stack:** C#/.NET 8 host and tests, ModelContextProtocol 1.2.0, xUnit 2.9.0, FakeWorker newline-delimited JSON integration tests, PowerShell 7 verification, .NET Framework 4.8 Openness worker unchanged.

## Global Constraints

- Work on the existing `codex/project-lifecycle-separation` branch; do not create a worktree or switch branches.
- Follow TDD for every behavior change: add the focused test, run it and observe RED, then edit production code.
- Immediate breaking removal: no aliases, deprecation period, warnings, or migration-specific errors for removed batch operations.
- `compile_check` remains unavailable in read-only mode and does not gain the lifecycle preview/apply safety-token flow.
- Keep project binding, timeout/crash handling, warning propagation, worker request method names, and worker handlers unchanged.
- Keep successful standalone tree and compile payloads capped at exactly `BatchPayloadBudget.MaxItemChars` (60,000 characters).
- Do not edit `TiaMcpServer.OpennessWorker/` or `TiaMcpServer.Contracts/WorkerRequest.cs`.
- Do not run live TIA Portal operations; this host-surface refactor is verified with unit tests, FakeWorker integration tests, the stub build, and coverage.
- Serialize solution builds with `-m:1` and use `/p:UseTiaPortalReferenceStubs=true`.
- Do not commit unless the user explicitly authorizes commits during execution. Each task's commit step is conditional on that authorization.

---

## File Structure

### Create

- `TiaMcpServer/Tools/StandaloneToolResultFormatter.cs` — cap successful standalone payloads and serialize the existing structured envelope.
- `TiaMcpServer/Tools/ProjectEngineeringTools.cs` — read-write-mode-only `compile_check` MCP adapter.
- `TiaMcpServer.Tests/StandaloneToolResultFormatterTests.cs` — payload cap and failure/warning preservation tests.
- `TiaMcpServer.Tests/ProjectStandaloneToolTests.cs` — standalone tool metadata, validation, and FakeWorker forwarding tests.

### Modify

- `TiaMcpServer/Tools/ProjectReadTools.cs` — add standalone `browse_project_tree`.
- `TiaMcpServer/Program.cs` — register `ProjectEngineeringTools` only in read-write mode.
- `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` — link the two new host source files into the test project.
- `TiaMcpServer.Tests/McpToolSchemaTests.cs` — pin standalone model-facing schemas and the 12-tool surface.
- `TiaMcpServer.Tests/ReadOnlyModeTests.cs` — pin the 3-tool read-only and 12-tool read-write surfaces.
- `TiaMcpServer/Batch/BatchOperationRequest.cs` — remove tree-only batch fields and project-operation description claims.
- `TiaMcpServer/Batch/BatchOperationCatalog.cs` — remove three project reads and depth validation.
- `TiaMcpServer/Batch/BatchWorkerInvoker.cs` — remove the three read-dispatch arms only.
- `TiaMcpServer/Batch/ReadBatchTools.cs` — publish the exact six-operation generic read list.
- `TiaMcpServer/Batch/BatchTools.cs` — keep the backward-compatible wrapper description aligned.
- `TiaMcpServer/Batch/BatchPayloadBudget.cs` — remove standalone-only narrowing hints from batch markers.
- `TiaMcpServer.Tests/BatchOperationCatalogTests.cs` — pin immediate removal and the reduced field contract.
- `TiaMcpServer.Tests/BatchOperationRequestJsonTests.cs` — reject removed batch fields.
- `TiaMcpServer.Tests/BatchToolMetadataTests.cs` — pin both batch descriptions and DTO descriptions.
- `TiaMcpServer.Tests/BatchPayloadBudgetTests.cs` — pin generic-batch narrowing guidance.
- `README.md`, `docs/ARCHITECTURE.md`, and affected `docs/SupportedOperations/*.md` — document the 12-tool standalone project surface.

---

### Task 1: Bound standalone result envelopes

**Files:**

- Create: `TiaMcpServer/Tools/StandaloneToolResultFormatter.cs`
- Create: `TiaMcpServer.Tests/StandaloneToolResultFormatterTests.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` (host `Tools` compile links)

**Interfaces:**

- Consumes: `WorkerCallResult`, `WorkerCallResult.ToEnvelopeText()`, and `BatchPayloadBudget.MaxItemChars`.
- Produces: `StandaloneToolResultFormatter.Format(WorkerCallResult result, string narrowingHint) : string`.
- Contract: cap only successful `Payload`; never alter failure category, error, warnings, or success state.

- [ ] **Step 1: Write the failing formatter tests**

Create `TiaMcpServer.Tests/StandaloneToolResultFormatterTests.cs`:

```csharp
using System.Text.Json;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

public class StandaloneToolResultFormatterTests
{
    [Fact]
    public void OversizedSuccess_IsCappedWithHintAndKeepsWarnings()
    {
        var result = WorkerCallResult.Ok(
            new string('x', BatchPayloadBudget.MaxItemChars + 100),
            new[] { "keep this warning" });

        var text = StandaloneToolResultFormatter.Format(
            result,
            "Narrow with depth or startPath.");

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        var payload = root.GetProperty("payload").GetString()!;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(BatchPayloadBudget.MaxItemChars, payload.Length);
        Assert.Contains("[TRUNCATED", payload);
        Assert.Contains("depth or startPath", payload);
        Assert.Equal(
            "keep this warning",
            root.GetProperty("warnings")[0].GetString());
    }

    [Fact]
    public void Failure_IsNotRewrittenOrTruncated()
    {
        var result = WorkerCallResult.Fail(
            WorkerFailureCategories.ValidationError,
            "invalid input",
            new[] { "keep this warning" });

        var text = StandaloneToolResultFormatter.Format(result, "unused hint");

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(string.Empty, root.GetProperty("payload").GetString());
        Assert.Equal(
            WorkerFailureCategories.ValidationError,
            root.GetProperty("failureCategory").GetString());
        Assert.Equal("invalid input", root.GetProperty("error").GetString());
        Assert.Equal(
            "keep this warning",
            root.GetProperty("warnings")[0].GetString());
    }
}
```

- [ ] **Step 2: Run the formatter tests and observe RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~StandaloneToolResultFormatterTests"
```

Expected: build/test failure because `StandaloneToolResultFormatter` does not exist.

- [ ] **Step 3: Implement the minimal formatter**

Create `TiaMcpServer/Tools/StandaloneToolResultFormatter.cs`:

```csharp
using TiaMcpServer.Batch;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tools;

internal static class StandaloneToolResultFormatter
{
    public static string Format(WorkerCallResult result, string narrowingHint)
    {
        if (result.Success && result.Payload.Length > BatchPayloadBudget.MaxItemChars)
        {
            var fullTrailer = $"\n[TRUNCATED — payload exceeded "
                + $"{BatchPayloadBudget.MaxItemChars} characters. {narrowingHint}]";
            var trailer = fullTrailer.Length <= BatchPayloadBudget.MaxItemChars
                ? fullTrailer
                : fullTrailer.Substring(0, BatchPayloadBudget.MaxItemChars);
            var retainedLength = Math.Max(
                0,
                BatchPayloadBudget.MaxItemChars - trailer.Length);

            result = result with
            {
                Payload = result.Payload.Substring(0, retainedLength) + trailer
            };
        }

        return result.ToEnvelopeText();
    }
}
```

Add this compile link beside `ProjectReadTools.cs` in `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`:

```xml
    <Compile Include="..\TiaMcpServer\Tools\StandaloneToolResultFormatter.cs"
      Link="Host\StandaloneToolResultFormatter.cs" />
```

- [ ] **Step 4: Run the formatter tests and observe GREEN**

Run the command from Step 2.

Expected: both `StandaloneToolResultFormatterTests` pass.

- [ ] **Step 5: Commit only if commits were explicitly authorized**

If authorized:

```powershell
git add TiaMcpServer/Tools/StandaloneToolResultFormatter.cs TiaMcpServer.Tests/StandaloneToolResultFormatterTests.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
git commit -m "refactor: bound standalone tool results"
```

Otherwise leave the verified changes uncommitted and continue.

---

### Task 2: Expose `browse_project_tree` as a standalone read tool

**Files:**

- Create: `TiaMcpServer.Tests/ProjectStandaloneToolTests.cs`
- Modify: `TiaMcpServer/Tools/ProjectReadTools.cs:10-19`
- Modify: `TiaMcpServer.Tests/McpToolSchemaTests.cs` (standalone read schema tests)

**Interfaces:**

- Consumes: `OpennessWorkerClient.BrowseProjectTreeAsync(string? projectPath, int? depth, string? startPath)` and `StandaloneToolResultFormatter.Format`.
- Produces: `ProjectReadTools.BrowseProjectTree(OpennessWorkerClient workerClient, string? projectPath = null, int? depth = null, string? startPath = null) : Task<string>`.
- Validation: `depth < 1` returns `WorkerFailureCategories.ValidationError` without worker access.

- [ ] **Step 1: Write failing metadata, validation, forwarding, and schema tests**

Create `TiaMcpServer.Tests/ProjectStandaloneToolTests.cs`:

```csharp
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

public class ProjectStandaloneToolTests
{
    private static OpennessWorkerClient CreateClient(string workerPath)
        => new(
            new ProjectSessionBinding(null),
            logger: null,
            workerExecutablePath: workerPath);

    private static JsonElement WorkerRequestFromEnvelope(string response)
    {
        using var envelope = JsonDocument.Parse(response);
        var payload = envelope.RootElement.GetProperty("payload").GetString();
        Assert.False(string.IsNullOrWhiteSpace(payload));
        using var request = JsonDocument.Parse(payload!);
        return request.RootElement.Clone();
    }

    [Fact]
    public void BrowseProjectTree_HasReadOnlyMcpMetadata()
    {
        var method = typeof(ProjectReadTools).GetMethod(
            nameof(ProjectReadTools.BrowseProjectTree),
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);
        var attribute = method!.GetCustomAttribute<McpServerToolAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal("browse_project_tree", attribute!.Name);
        Assert.True(attribute.ReadOnly);
        Assert.False(attribute.Destructive);
        Assert.False(attribute.OpenWorld);
    }

    [Fact]
    public async Task BrowseProjectTree_ForwardsEveryArgument()
    {
        using var client = CreateClient(FakeWorkerLocator.Locate());

        var response = await ProjectReadTools.BrowseProjectTree(
            client,
            projectPath: "echo",
            depth: 2,
            startPath: "PLC_1/Blocks");
        var request = WorkerRequestFromEnvelope(response);

        Assert.Equal("browse_project_tree", request.GetProperty("method").GetString());
        Assert.Equal("echo", request.GetProperty("projectPath").GetString());
        Assert.Equal(2, request.GetProperty("depth").GetInt32());
        Assert.Equal("PLC_1/Blocks", request.GetProperty("startPath").GetString());
    }

    [Fact]
    public async Task BrowseProjectTree_InvalidDepthFailsBeforeWorkerAccess()
    {
        using var client = CreateClient("missing-worker.exe");

        var response = await ProjectReadTools.BrowseProjectTree(client, depth: 0);

        using var document = JsonDocument.Parse(response);
        var root = document.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(
            WorkerFailureCategories.ValidationError,
            root.GetProperty("failureCategory").GetString());
        Assert.Contains("depth", root.GetProperty("error").GetString());
    }
}
```

Add this test to `McpToolSchemaTests.cs`:

```csharp
    [Fact]
    public void BrowseProjectTree_SchemaExposesOnlyModelInputs()
    {
        var properties = SchemaPropertyNames(
            typeof(ProjectReadTools),
            nameof(ProjectReadTools.BrowseProjectTree));

        Assert.Equal(
            new[] { "depth", "projectPath", "startPath" },
            properties.OrderBy(name => name).ToArray());
        Assert.DoesNotContain("workerClient", properties);
    }
```

- [ ] **Step 2: Run the focused tests and observe RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~ProjectStandaloneToolTests|FullyQualifiedName~BrowseProjectTree_SchemaExposesOnlyModelInputs"
```

Expected: compilation failure because `ProjectReadTools.BrowseProjectTree` does not exist.

- [ ] **Step 3: Add the standalone method**

Replace `TiaMcpServer/Tools/ProjectReadTools.cs` with:

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tools;

/// <summary>Read-only project tools exposed in both access modes.</summary>
[McpServerToolType]
public class ProjectReadTools
{
    [McpServerTool(Name = "get_project_status", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Get status and metadata for the active TIA Portal project.")]
    public static async Task<string> GetProjectStatus(
        OpennessWorkerClient workerClient,
        [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        => (await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false)).ToEnvelopeText();

    [McpServerTool(Name = "browse_project_tree", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Browse the active TIA Portal project hierarchy. Use depth and startPath to bound large projects.")]
    public static async Task<string> BrowseProjectTree(
        OpennessWorkerClient workerClient,
        [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null,
        [Description("Optional maximum tree depth. Must be 1 or greater; 1 returns only top-level nodes.")] int? depth = null,
        [Description("Optional subtree root matching a node Path exactly, case-insensitively, e.g. PLC_1/Blocks.")] string? startPath = null)
    {
        if (depth is < 1)
        {
            return StandaloneToolResultFormatter.Format(
                WorkerCallResult.Fail(
                    WorkerFailureCategories.ValidationError,
                    "'depth' must be 1 or greater."),
                "Use a valid depth or omit it.");
        }

        var result = await workerClient
            .BrowseProjectTreeAsync(projectPath, depth, startPath)
            .ConfigureAwait(false);
        return StandaloneToolResultFormatter.Format(
            result,
            "Narrow the read with a smaller depth or a more specific startPath.");
    }
}
```

- [ ] **Step 4: Run the focused tests and observe GREEN**

Run the command from Step 2.

Expected: metadata, forwarding, validation, and schema tests pass.

- [ ] **Step 5: Commit only if commits were explicitly authorized**

If authorized:

```powershell
git add TiaMcpServer/Tools/ProjectReadTools.cs TiaMcpServer.Tests/ProjectStandaloneToolTests.cs TiaMcpServer.Tests/McpToolSchemaTests.cs
git commit -m "feat: expose project tree as standalone tool"
```

Otherwise leave the verified changes uncommitted and continue.

---

### Task 3: Expose `compile_check` only in read-write mode

**Files:**

- Create: `TiaMcpServer/Tools/ProjectEngineeringTools.cs`
- Modify: `TiaMcpServer/Program.cs:55-68`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` (host `Tools` compile links)
- Modify: `TiaMcpServer.Tests/ProjectStandaloneToolTests.cs`
- Modify: `TiaMcpServer.Tests/McpToolSchemaTests.cs`
- Modify: `TiaMcpServer.Tests/ReadOnlyModeTests.cs:531-601`

**Interfaces:**

- Consumes: `OpennessWorkerClient.CompileCheckAsync(string? blockPath, string? plcName, string? projectPath)` and `StandaloneToolResultFormatter.Format`.
- Produces: `ProjectEngineeringTools.CompileCheck(OpennessWorkerClient workerClient, string? projectPath = null, string? plcName = null, string? blockPath = null) : Task<string>`.
- Registration: `ProjectEngineeringTools` appears only inside the `McpAccessMode.ReadWrite` branch in `Program.Main`.

- [ ] **Step 1: Write the failing engineering-tool tests**

Append to `ProjectStandaloneToolTests.cs`:

```csharp
    [Fact]
    public void CompileCheck_HasEngineeringMcpMetadata()
    {
        var method = typeof(ProjectEngineeringTools).GetMethod(
            nameof(ProjectEngineeringTools.CompileCheck),
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);
        var attribute = method!.GetCustomAttribute<McpServerToolAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal("compile_check", attribute!.Name);
        Assert.False(attribute.ReadOnly);
        Assert.False(attribute.Destructive);
        Assert.False(attribute.OpenWorld);
    }

    [Fact]
    public async Task CompileCheck_ForwardsEveryArgument()
    {
        using var client = CreateClient(FakeWorkerLocator.Locate());

        var response = await ProjectEngineeringTools.CompileCheck(
            client,
            projectPath: "echo",
            plcName: "PLC_1",
            blockPath: "PLC_1/Blocks/Main");
        var request = WorkerRequestFromEnvelope(response);

        Assert.Equal("compile_check", request.GetProperty("method").GetString());
        Assert.Equal("echo", request.GetProperty("projectPath").GetString());
        Assert.Equal("PLC_1", request.GetProperty("plcName").GetString());
        Assert.Equal("PLC_1/Blocks/Main", request.GetProperty("blockPath").GetString());
    }
```

Add to `McpToolSchemaTests.cs`:

```csharp
    [Fact]
    public void CompileCheck_SchemaExposesOnlyModelInputs()
    {
        var properties = SchemaPropertyNames(
            typeof(ProjectEngineeringTools),
            nameof(ProjectEngineeringTools.CompileCheck));

        Assert.Equal(
            new[] { "blockPath", "plcName", "projectPath" },
            properties.OrderBy(name => name).ToArray());
        Assert.DoesNotContain("workerClient", properties);
        Assert.DoesNotContain("confirm", properties);
        Assert.DoesNotContain("safetyToken", properties);
    }
```

Replace `ReadWriteMode_HasAllTenTools` in `ReadOnlyModeTests.cs` with these two tests:

```csharp
    [Fact]
    public void ReadOnlyMode_HasExactlyThreeTools()
    {
        var toolNames = new[] { typeof(ProjectReadTools), typeof(ReadBatchTools) }
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
            .Where(attribute => attribute is not null)
            .Select(attribute => attribute!.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(
            new[] { "browse_project_tree", "execute_read_batch", "get_project_status" },
            toolNames);
    }

    [Fact]
    public void ReadWriteMode_HasExactlyTwelveDistinctTools()
    {
        var toolNames = typeof(ProjectLifecycleTools).Assembly
            .GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
            .Where(attribute => attribute is not null)
            .Select(attribute => attribute!.Name)
            .ToArray();

        Assert.Equal(12, toolNames.Length);
        Assert.Equal(12, toolNames.Distinct().Count());
    }
```

Replace `McpToolSurface_ExposesExactlyTenApprovedTools` in `McpToolSchemaTests.cs` with:

```csharp
    [Fact]
    public void McpToolSurface_ExposesExactlyTwelveApprovedTools()
    {
        var toolTypes = typeof(ProjectLifecycleTools).Assembly
            .GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null);

        var toolNames = toolTypes
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
            .Where(attribute => attribute is not null)
            .Select(attribute => attribute!.Name)
            .ToArray();

        Assert.Equal(12, toolNames.Length);
        Assert.Equal(12, toolNames.Distinct().Count());
        Assert.Contains("browse_project_tree", toolNames);
        Assert.Contains("compile_check", toolNames);
        Assert.DoesNotContain("probe_project_status_for_lifecycle", toolNames);
    }
```

Update the adjacent XML comment from 10 to 12 tools.

- [ ] **Step 2: Run the focused tests and observe RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~ProjectStandaloneToolTests|FullyQualifiedName~McpToolSchemaTests|FullyQualifiedName~ReadOnlyMode_HasExactly|FullyQualifiedName~ReadWriteMode_HasExactly"
```

Expected: compilation failure because `ProjectEngineeringTools` does not exist, plus the tool-count assertion remains at the old surface until production changes land.

- [ ] **Step 3: Implement and conditionally register the engineering tool**

Create `TiaMcpServer/Tools/ProjectEngineeringTools.cs`:

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tools;

/// <summary>Project engineering actions exposed only in read-write mode.</summary>
[McpServerToolType]
public class ProjectEngineeringTools
{
    [McpServerTool(Name = "compile_check", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Compile a PLC or selected block scope and return compiler messages. Available only in read-write mode.")]
    public static async Task<string> CompileCheck(
        OpennessWorkerClient workerClient,
        [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null,
        [Description("Optional PLC software name to compile.")] string? plcName = null,
        [Description("Optional PLC block path to compile only that block.")] string? blockPath = null)
    {
        var result = await workerClient
            .CompileCheckAsync(blockPath, plcName, projectPath)
            .ConfigureAwait(false);
        return StandaloneToolResultFormatter.Format(
            result,
            "Narrow the compile with plcName or blockPath.");
    }
}
```

Add to the read-write registration block in `TiaMcpServer/Program.cs`:

```csharp
            if (accessMode == McpAccessMode.ReadWrite)
            {
                mcp.WithTools<ProjectEngineeringTools>()
                   .WithTools<ProjectWriteTools>()
                   .WithTools<WriteBatchTools>();
            }
```

Add the test-project compile link beside `ProjectReadTools.cs`:

```xml
    <Compile Include="..\TiaMcpServer\Tools\ProjectEngineeringTools.cs"
      Link="Host\ProjectEngineeringTools.cs" />
```

Keep the existing `OperationAccessPolicy` and `OpennessWorkerClient_ReadOnly_DeniesCompileCheck` tests unchanged; they remain the defense-in-depth layer.

- [ ] **Step 4: Run the focused tests and observe GREEN**

Run the command from Step 2.

Expected: standalone compile metadata, forwarding, schema, 3-tool read-only surface, and 12-tool full surface all pass.

- [ ] **Step 5: Commit only if commits were explicitly authorized**

If authorized:

```powershell
git add TiaMcpServer/Tools/ProjectEngineeringTools.cs TiaMcpServer/Program.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj TiaMcpServer.Tests/ProjectStandaloneToolTests.cs TiaMcpServer.Tests/McpToolSchemaTests.cs TiaMcpServer.Tests/ReadOnlyModeTests.cs
git commit -m "feat: expose compile check as standalone tool"
```

Otherwise leave the verified changes uncommitted and continue.

---

### Task 4: Remove project operations and tree fields from `execute_read_batch`

**Files:**

- Modify: `TiaMcpServer/Batch/BatchOperationRequest.cs:15-48`
- Modify: `TiaMcpServer/Batch/BatchOperationCatalog.cs` (`ValidateBounds`, `BuildSpecs`)
- Modify: `TiaMcpServer/Batch/BatchWorkerInvoker.cs:50-90`
- Modify: `TiaMcpServer/Batch/ReadBatchTools.cs:14-43`
- Modify: `TiaMcpServer/Batch/BatchTools.cs:22-40`
- Modify: `TiaMcpServer/Batch/BatchPayloadBudget.cs:220-229`
- Modify: `TiaMcpServer.Tests/BatchOperationCatalogTests.cs`
- Modify: `TiaMcpServer.Tests/BatchOperationRequestJsonTests.cs`
- Modify: `TiaMcpServer.Tests/BatchToolMetadataTests.cs`
- Modify: `TiaMcpServer.Tests/BatchPayloadBudgetTests.cs`

**Interfaces:**

- `BatchOperationCatalog.ReadOperationNames` becomes exactly: `read_hardware_config`, `search_equipment_catalog`, `read_cross_references`, `get_block_content`, `list_tag_tables`, `get_type_content`.
- `BatchOperationRequest` removes `Depth` and `StartPath` from its MCP/JSON schema.
- Removed operation names receive the existing generic unknown-operation batch error.
- Keep `BatchWorkerInvoker.ReadCurrentStateAsync` calls to `GetProjectStatusAsync` for `start_plc`/`stop_plc`; remove only the three read arms in `InvokeAsync`.

- [ ] **Step 1: Add focused failing removal tests without changing production**

Add to `BatchOperationCatalogTests.cs`:

```csharp
    [Theory]
    [InlineData("get_project_status")]
    [InlineData("browse_project_tree")]
    [InlineData("compile_check")]
    public void ValidateReadBatch_RejectsStandaloneProjectOperations(string operation)
    {
        var result = BatchOperationCatalog.ValidateReadBatch(new[] { Op("a", operation) });

        Assert.False(result.IsValid);
        Assert.Contains($"Unknown operation '{operation}'", result.Error);
        Assert.DoesNotContain(operation, BatchOperationCatalog.ReadOperationNames);
    }
```

Add to `BatchOperationRequestJsonTests.cs`:

```csharp
    [Theory]
    [InlineData("""{"operationId":"a","operation":"read_hardware_config","depth":3}""", "depth")]
    [InlineData("""{"operationId":"a","operation":"read_hardware_config","startPath":"PLC_1/Blocks"}""", "startPath")]
    public void RemovedProjectTreeFields_AreRejected(string json, string field)
    {
        var exception = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<BatchOperationRequest>(json, WebOptions));

        Assert.Contains(field, exception.Message);
    }

    [Fact]
    public void BatchRequestType_DoesNotExposeProjectTreeFields()
    {
        Assert.Null(typeof(BatchOperationRequest).GetProperty("Depth"));
        Assert.Null(typeof(BatchOperationRequest).GetProperty("StartPath"));
    }
```

Add an overload to `BatchToolMetadataTests.cs`:

```csharp
    private static string MethodDescription(Type toolType, string methodName)
    {
        var method = toolType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        var description = method!.GetCustomAttribute<DescriptionAttribute>();
        Assert.NotNull(description);
        return description!.Description;
    }
```

Keep the existing helper as:

```csharp
    private static string MethodDescription(string methodName)
        => MethodDescription(typeof(BatchTools), methodName);
```

Then add:

```csharp
    [Fact]
    public void ExecuteReadBatchDescriptions_OmitStandaloneProjectOperations()
    {
        foreach (var toolType in new[] { typeof(BatchTools), typeof(ReadBatchTools) })
        {
            var description = MethodDescription(toolType, "ExecuteReadBatch");
            foreach (var retained in BatchOperationCatalog.ReadOperationNames)
            {
                Assert.Contains(retained, description);
            }

            Assert.DoesNotContain("get_project_status", description);
            Assert.DoesNotContain("browse_project_tree", description);
            Assert.DoesNotContain("compile_check", description);
        }
    }

    [Fact]
    public void BatchRequestDescriptions_OmitStandaloneProjectOperations()
    {
        foreach (var propertyName in new[]
        {
            nameof(BatchOperationRequest.Operation),
            nameof(BatchOperationRequest.BlockPath),
            nameof(BatchOperationRequest.PlcName)
        })
        {
            var description = PropertyDescription(propertyName);
            Assert.DoesNotContain("get_project_status", description);
            Assert.DoesNotContain("browse_project_tree", description);
            Assert.DoesNotContain("compile_check", description);
        }
    }
```

Add to `BatchPayloadBudgetTests.cs`:

```csharp
    [Fact]
    public void BatchMarkers_DoNotRecommendStandaloneProjectFields()
    {
        var text = BatchPayloadBudget.TruncationTrailer(BatchPayloadBudget.MaxItemChars)
            + BatchPayloadBudget.OmissionMarker(BatchPayloadBudget.MaxBatchChars);
        var normalized = text.ToLowerInvariant();

        Assert.DoesNotContain("startpath", normalized);
        Assert.DoesNotContain("depth", normalized);
    }
```

- [ ] **Step 2: Run the new tests and observe RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~ValidateReadBatch_RejectsStandaloneProjectOperations|FullyQualifiedName~RemovedProjectTreeFields_AreRejected|FullyQualifiedName~BatchRequestType_DoesNotExposeProjectTreeFields|FullyQualifiedName~ExecuteReadBatchDescriptions_OmitStandaloneProjectOperations|FullyQualifiedName~BatchRequestDescriptions_OmitStandaloneProjectOperations|FullyQualifiedName~BatchMarkers_DoNotRecommendStandaloneProjectFields"
```

Expected: assertions fail because the catalog, DTO, metadata, and markers still expose the old batch surface.

- [ ] **Step 3: Narrow the production batch contract**

In `BatchOperationRequest.cs`:

1. Replace the read-operation portion of `Operation`'s description with:

```csharp
"Read operations: read_hardware_config, read_cross_references, search_equipment_catalog, get_block_content, list_tag_tables, get_type_content. "
```

2. Remove `compile_check` from the `BlockPath` and `PlcName` descriptions.
3. Delete the `Depth` and `StartPath` properties and their descriptions.

In `BatchOperationCatalog.BuildSpecs`, make the read section exactly:

```csharp
            // Reads
            new BatchOperationSpec("read_hardware_config", BatchOperationCategory.Read, None, None),
            new BatchOperationSpec("search_equipment_catalog", BatchOperationCategory.Read, new[] { "query" }, new[] { "maxResults" }),
            new BatchOperationSpec("read_cross_references", BatchOperationCategory.Read, None, new[] { "plcName", "filter", "maxResults" }),
            new BatchOperationSpec("get_block_content", BatchOperationCategory.Read, new[] { "blockPath" }, new[] { "format" }),
            new BatchOperationSpec("list_tag_tables", BatchOperationCategory.Read, None, new[] { "plcName" }),
            new BatchOperationSpec("get_type_content", BatchOperationCategory.Read, new[] { "typePath" }, new[] { "format" }),
```

Remove the depth branch from `ValidateBounds`, leaving:

```csharp
    private static IEnumerable<string> ValidateBounds(BatchOperationRequest op)
    {
        if (op.MaxResults is < 1)
        {
            yield return "'maxResults' must be 1 or greater.";
        }
    }
```

In `BatchWorkerInvoker.InvokeAsync`, make the read arms exactly:

```csharp
        // Reads
        "read_hardware_config" => client.ReadHardwareConfigAsync(op.ProjectPath),
        "search_equipment_catalog" => client.SearchEquipmentCatalogAsync(op.Query!, op.ProjectPath, op.MaxResults),
        "read_cross_references" => client.ReadCrossReferencesAsync(op.ProjectPath, op.PlcName, op.Filter, op.MaxResults),
        "get_block_content" => InvokeGetBlockContent(client, op),
        "list_tag_tables" => client.ListTagTablesAsync(op.PlcName, op.ProjectPath),
        "get_type_content" => InvokeGetTypeContent(client, op),
```

Do not remove this write-snapshot arm from `ReadCurrentStateAsync`:

```csharp
        "start_plc" or "stop_plc"
            => client.GetProjectStatusAsync(op.ProjectPath),
```

Use the same description text on `ReadBatchTools.ExecuteReadBatch` and `BatchTools.ExecuteReadBatch`:

```csharp
[Description("Run up to 50 non-project read operations in one call. Each item is { operationId (unique), operation, ...that operation's parameters }; projectPath is optional on every item. Reads run independently, so a failing item does not stop the others. "
    + "Valid operations (parentheses list required fields): read_hardware_config, search_equipment_catalog (query), read_cross_references, get_block_content (blockPath), list_tag_tables, get_type_content (typePath). "
    + "Large reads: bound search_equipment_catalog and read_cross_references with maxResults; oversized responses are truncated or omitted server-side with explicit markers.")]
```

In `BatchPayloadBudget.TruncationTrailer` and `OmissionMarker`, replace the narrowing lists with only `plcName`, `filter`, and `maxResults`.

- [ ] **Step 4: Update existing tests to the reduced contract**

In `BatchOperationCatalogTests.cs`:

- Replace `browse_project_tree` fixtures used only as a valid read with `read_hardware_config`.
- Remove `Validate_RejectsDepthAndStartPathOnNonTreeOperations`.
- Change `Validate_RejectsOutOfRangeBounds` to cover only `MaxResults = 0`.
- Change `Validate_AcceptsBoundsOnTheirOperations` to cover `maxResults` on `search_equipment_catalog` and `read_cross_references` only.
- Remove `browse_project_tree`, `compile_check`, and `get_project_status` from the expected dictionary in `All_MatchesTheAuthoritativeOperationFieldContract`.
- Update `ValidateReadBatch_UnknownOperationErrorListsValidReadOperations` to assert a retained name such as `get_type_content` and to assert all three removed names are absent.

In `BatchOperationRequestJsonTests.cs`, replace `Deserializes_BoundingFields` with:

```csharp
    [Fact]
    public void DeserializesRetainedMaxResultsField()
    {
        var json = """{"operationId":"a","operation":"search_equipment_catalog","query":"CPU","maxResults":25}""";

        var request = JsonSerializer.Deserialize<BatchOperationRequest>(json, WebOptions)!;

        Assert.Equal(25, request.MaxResults);
    }
```

In `BatchToolMetadataTests.cs`:

- Rename `BlockPathDescription_CoversAllBlockOperationsAndCompileCheck` to `BlockPathDescription_CoversBatchBlockOperations` and replace the `compile_check` inclusion assertion with `Assert.DoesNotContain("compile_check", description)`.
- Keep `PlcNameDescription_NamesTheOperationsThatHonorIt`, but replace its compile inclusion assertion with `Assert.DoesNotContain("compile_check", description)`.

- [ ] **Step 5: Run the complete batch-focused suite and observe GREEN**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~BatchOperationCatalogTests|FullyQualifiedName~BatchOperationRequestJsonTests|FullyQualifiedName~BatchToolMetadataTests|FullyQualifiedName~BatchPayloadBudgetTests|FullyQualifiedName~BatchToolsTests|FullyQualifiedName~BatchFieldForwardingTests"
```

Expected: all selected tests pass; the valid read-operation list contains exactly six names.

- [ ] **Step 6: Commit only if commits were explicitly authorized**

If authorized:

```powershell
git add TiaMcpServer/Batch/BatchOperationRequest.cs TiaMcpServer/Batch/BatchOperationCatalog.cs TiaMcpServer/Batch/BatchWorkerInvoker.cs TiaMcpServer/Batch/ReadBatchTools.cs TiaMcpServer/Batch/BatchTools.cs TiaMcpServer/Batch/BatchPayloadBudget.cs TiaMcpServer.Tests/BatchOperationCatalogTests.cs TiaMcpServer.Tests/BatchOperationRequestJsonTests.cs TiaMcpServer.Tests/BatchToolMetadataTests.cs TiaMcpServer.Tests/BatchPayloadBudgetTests.cs
git commit -m "refactor: remove project reads from batch"
```

Otherwise leave the verified changes uncommitted and continue.

---

### Task 5: Align current documentation and run full verification

**Files:**

- Modify: `README.md`
- Modify: `docs/ARCHITECTURE.md`
- Modify: `docs/SupportedOperations/README.md`
- Modify: `docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md`
- Modify: `docs/SupportedOperations/DEVICES_OPERATIONS_SUMMARY.md`
- Modify: `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md`
- Verify unchanged context: `docs/SupportedOperations/HMI_OPERATIONS_SUMMARY.md`, `docs/SupportedOperations/TESTSUITE_OPERATIONS_SUMMARY.md`, `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md`
- Verify: `docs/superpowers/specs/2026-07-31-standalone-project-tools-design.md`

**Interfaces:**

- Public read-write surface: 12 tools.
- Standalone project tools: `get_project_status`, `browse_project_tree`, `compile_check`.
- Generic `execute_read_batch` operations: the exact six-name catalog from Task 4.
- Evidence boundary: unit/FakeWorker/stub-build verification only; no fresh live TIA claim.

- [ ] **Step 1: Update the top-level public surface and examples**

In `README.md`:

- Change every current tool-count claim from 10 to 12.
- List these standalone tools separately:

```markdown
- `get_project_status` — read active project metadata without opening or switching projects.
- `browse_project_tree` — browse a bounded project subtree with optional `depth` and `startPath`.
- `compile_check` — compile a PLC or selected block and return compiler messages; available only in read-write mode.
```

- Replace every current `execute_read_batch` operation list with:

```markdown
Available read operations for `execute_read_batch`: `read_hardware_config`, `search_equipment_catalog`, `read_cross_references`, `get_block_content`, `list_tag_tables`, and `get_type_content`.
```

- Replace batch examples containing tree/status/compile items with separate standalone calls followed by a batch containing only retained operations.
- Remove `depth`/`startPath` from generic batch payload-narrowing guidance; retain them on standalone `browse_project_tree` guidance.

In `docs/ARCHITECTURE.md`:

- Add standalone rows for `browse_project_tree` and `compile_check` beside `get_project_status`.
- Mark `compile_check` read-write-only and non-tokenized.
- Replace the read-batch catalog with the exact six retained operations.
- Update the full public-tool count to 12 and remove the statement that compile remains in the read batch.

- [ ] **Step 2: Update supported-operation summaries**

In `docs/SupportedOperations/README.md`, use:

```markdown
`execute_read_batch` supports:

`read_hardware_config`, `search_equipment_catalog`, `read_cross_references`, `get_block_content`, `list_tag_tables`, and `get_type_content`.

Project status, project-tree browsing, and compilation are separate tools: `get_project_status`, `browse_project_tree`, and `compile_check`. The first two are available in both access modes; `compile_check` is available only in read-write mode.
```

In `docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md`, make the read table:

```markdown
| Entry point | Operation | Inputs and behavior |
|---|---|---|
| `get_project_status` | `get_project_status` | Optional `projectPath`; reads status and metadata without opening or switching projects. |
| `browse_project_tree` | `browse_project_tree` | Optional `projectPath`, `depth`, and `startPath`; returns bounded project-tree data. |
| `compile_check` | `compile_check` | Optional `projectPath`, `plcName`, and `blockPath`; compiles the selected scope and returns compiler messages. Available only in read-write mode. |
```

Replace the old batch-classification sentence with:

```markdown
`compile_check` is a standalone engineering operation. It is not marked read-only, does not use a safety token, and is exposed only in read-write mode.
```

In `docs/SupportedOperations/DEVICES_OPERATIONS_SUMMARY.md`, change the `browse_project_tree` entry point from `execute_read_batch` to the standalone `browse_project_tree` tool.

In `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md`, state that tree browsing and compilation are standalone tools and remove the old read-batch/safety classification.

Read the HMI, TestSuite, and network summaries named above; change only wording that still implies `compile_check` or `browse_project_tree` is a batch operation. Do not broaden their capability claims.

- [ ] **Step 3: Run bounded documentation-contract checks**

Run:

```powershell
$currentDocs = @(
    'README.md',
    'docs/ARCHITECTURE.md',
    'docs/SupportedOperations/README.md',
    'docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md',
    'docs/SupportedOperations/DEVICES_OPERATIONS_SUMMARY.md',
    'docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md'
)
$violations = Select-String -Path $currentDocs -Pattern 'execute_read_batch' |
    Where-Object { $_.Line -match 'get_project_status|browse_project_tree|compile_check' }
if ($violations) { $violations | ForEach-Object { Write-Error $_.Line }; throw 'Project operations remain documented as batch operations.' }
rg -n "12 tools|browse_project_tree|compile_check|get_project_status" README.md docs/ARCHITECTURE.md docs/SupportedOperations
```

Expected: no violation error; the search output shows all three standalone tool names in current public documentation and the 12-tool claim in `README.md`.

Run the existing documentation-sensitive tests:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~CiWorkflowTests|FullyQualifiedName~McpToolSchemaTests|FullyQualifiedName~BatchToolMetadataTests"
```

Expected: all selected tests pass.

- [ ] **Step 4: Run the serialized Release stub build**

Run:

```powershell
dotnet build TiaMcpServer.sln -m:1 --no-restore --configuration Release /p:UseTiaPortalReferenceStubs=true
```

Expected: build succeeds with zero errors. This proves compilation against the reference stubs, not live TIA behavior.

- [ ] **Step 5: Run the full suite with scoped coverage and enforce 80%**

Run in PowerShell 7:

```powershell
$coverageRun = Join-Path 'TestResults' ('standalone-project-tools-' + [DateTimeOffset]::UtcNow.ToString('yyyyMMddHHmmss'))
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build --configuration Release --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory $coverageRun
$reports = @(Get-ChildItem -Path $coverageRun -Recurse -Filter coverage.cobertura.xml)
if ($reports.Count -ne 1) { throw "Expected exactly one Cobertura report; found $($reports.Count)." }
./scripts/verify-coverage-threshold.ps1 -CoveragePath $reports[0].FullName -MinimumLineRate 0.80
```

Expected: all tests pass and the script reports a line rate of at least `0.80`.

- [ ] **Step 6: Review the complete diff and whitespace**

Run:

```powershell
git diff --check
git status --short
git diff --stat
```

Review the complete diff and confirm:

- no `TiaMcpServer.OpennessWorker/` or worker request-contract file changed;
- no project operation remains in `BatchOperationCatalog.ReadOperationNames`;
- `start_plc`/`stop_plc` current-state reads still use `GetProjectStatusAsync`;
- no live-TIA evidence claim was added;
- only the approved spec, plan, implementation, tests, and current public documentation changed.

- [ ] **Step 7: Commit only if commits were explicitly authorized**

If authorized:

```powershell
git add README.md docs/ARCHITECTURE.md docs/SupportedOperations/README.md docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md docs/SupportedOperations/DEVICES_OPERATIONS_SUMMARY.md docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md docs/SupportedOperations/HMI_OPERATIONS_SUMMARY.md docs/SupportedOperations/TESTSUITE_OPERATIONS_SUMMARY.md docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md docs/superpowers/specs/2026-07-31-standalone-project-tools-design.md docs/superpowers/plans/2026-07-31-standalone-project-tools.md
git commit -m "docs: document standalone project tools"
```

If earlier tasks were also intentionally left uncommitted, stage and commit their verified files only under the user's explicit commit authorization. Otherwise leave the completed branch uncommitted for user review.
