using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Multiuser;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Multiuser;

[Collection("Mcp protocol serial")]
public sealed class LocalSessionCapabilityTests
{
    private const string Amc = "C:/Projects/Local.amc21";

    [Theory]
    [InlineData(McpAccessMode.ReadOnly, WorkerFailureCategories.AccessDenied)]
    [InlineData(McpAccessMode.Full, WorkerFailureCategories.UnsupportedCapability)]
    public async Task DirectHostWorkerCall_AccessPolicyPrecedesLocalCapabilityAndTransport(
        McpAccessMode accessMode, string expectedCategory)
    {
        var binding = new ProjectSessionBinding(null);
        Assert.True(binding.BindVerified(new WorkerSessionIdentity
        {
            WorkerSessionId = "synthetic-worker", SessionGeneration = 1, PortalProcessId = 42,
            ProjectPath = Amc,
            Context = new ProjectContextInfo
            {
                ContainerKind = ProjectContainerKinds.LocalSession,
                SessionMode = MultiuserSessionModes.Unknown,
                EngineeringProjectPath = Amc,
                Capabilities = ProjectCapabilityCatalog.Describe(ProjectContainerKinds.LocalSession).ToList()
            }
        }, forceRebind: false, out var error), error);
        using var missingWorkerDirectory = new TempAuditDirectory();
        using var client = new OpennessWorkerClient(binding,
            workerExecutablePath: Path.Combine(missingWorkerDirectory.Path, "missing-worker.exe"),
            accessPolicy: new OperationAccessPolicy(accessMode));

        var result = await client.CloseProjectAsync(Amc, false);

        Assert.False(result.Success);
        Assert.Equal(expectedCategory, result.FailureCategory);
        Assert.Equal(WorkerDispatchState.NotSent, result.DispatchState);
        Assert.True(binding.CaptureSnapshot().IsVerified);
    }

    [Fact]
    public async Task OldStandaloneTreeCursor_AfterLocalSwitchFailsBeforeCapabilityGate()
    {
        const string standalone = "C:/Projects/project-tree-v3-small.ap21";
        using var portals = new FakeWorkerPortals(
            new FakeWorkerPortals.Entry(42, standalone), new FakeWorkerPortals.Entry(43, Amc));
        using var ui = new FakeWorkerUiOpenProject(standalone);
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        using var log = new FakeWorkerRequestLog(directory.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadOnly);
        var firstBinding = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?>
        {
            ["projectPath"] = standalone, ["portalProcessId"] = 42
        });
        Assert.True(firstBinding.StructuredContent!.Value.GetProperty("success").GetBoolean());
        var first = await harness.Client.CallToolAsync("browse_project_tree", new Dictionary<string, object?>
        {
            ["pageSize"] = 1
        });
        var cursor = first.StructuredContent!.Value.GetProperty("result")
            .GetProperty("pagination").GetProperty("nextCursor").GetString();
        Assert.False(string.IsNullOrWhiteSpace(cursor));
        var before = log.Methods().Count(method => method == "browse_project_tree_v3_snapshot");
        Assert.Equal(1, before);

        var local = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?>
        {
            ["projectPath"] = Amc, ["portalProcessId"] = 43, ["forceRebind"] = true
        });
        Assert.True(local.StructuredContent!.Value.GetProperty("success").GetBoolean(), local.StructuredContent.Value.GetRawText());
        Assert.Equal(ProjectContainerKinds.LocalSession, harness.WorkerClient.BindingSnapshot.Context!.ContainerKind);
        var continuation = await harness.Client.CallToolAsync("browse_project_tree", new Dictionary<string, object?>
        {
            ["cursor"] = cursor
        });
        Assert.Equal(WorkerFailureCategories.CursorBindingMismatch,
            continuation.StructuredContent!.Value.GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal(before, log.Methods().Count(method => method == "browse_project_tree_v3_snapshot"));

        var fresh = await harness.Client.CallToolAsync("browse_project_tree", new Dictionary<string, object?>());
        Assert.DoesNotContain(WorkerFailureCategories.UnsupportedCapability, fresh.StructuredContent!.Value.GetRawText());
        Assert.Equal(before + 1, log.Methods().Count(method => method == "browse_project_tree_v3_snapshot"));
    }

    [Theory]
    [InlineData("browse_project_tree", "browse_project_tree_v3_snapshot")]
    [InlineData("plc_read", "list_tag_tables")]
    [InlineData("network_read", "read_hardware_config")]
    [InlineData("read_cross_references", "read_cross_references")]
    [InlineData("hmi_read", "hmi_list_hmi_devices")]
    public async Task LocalRead_DispatchesToWorker(string tool, string workerMethod)
    {
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, Amc));
        using var ui = new FakeWorkerUiOpenProject(Amc);
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadOnly);
        var bound = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?> { ["projectPath"] = Amc });
        Assert.True(bound.StructuredContent!.Value.GetProperty("success").GetBoolean(), bound.StructuredContent.Value.GetRawText());

        var before = log.Methods().Length;
        var arguments = tool switch
        {
            "plc_read" => new Dictionary<string, object?> { ["operations"] = new[] { new { operationId = "r1", operation = "list_tag_tables" } } },
            "network_read" => new Dictionary<string, object?> { ["operations"] = new[] { new { operationId = "r1", operation = "read_hardware_config" } } },
            "hmi_read" => new Dictionary<string, object?> { ["operations"] = new[] { new { operationId = "r1", operation = "list_hmi_devices" } } },
            "read_cross_references" => new Dictionary<string, object?>
            {
                ["target"] = new { path = new[] { new { nodeType = "Device", name = "PLC_1" } } }
            },
            _ => new Dictionary<string, object?>()
        };
        var response = await harness.Client.CallToolAsync(tool, arguments);

        Assert.DoesNotContain(WorkerFailureCategories.UnsupportedCapability, response.StructuredContent!.Value.GetRawText());
        Assert.Contains(workerMethod, log.Methods().Skip(before));
    }

    [Fact]
    public async Task LocalSaveDryRun_ProbesOwnerWithoutSaving()
    {
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, Amc));
        using var ui = new FakeWorkerUiOpenProject(Amc);
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path);
        var bound = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?> { ["projectPath"] = Amc });
        Assert.True(bound.StructuredContent!.Value.GetProperty("success").GetBoolean(), bound.StructuredContent!.Value.GetRawText());

        var before = log.Methods().Length;
        var result = await harness.Client.CallToolAsync("save_project", new Dictionary<string, object?> { ["dryRun"] = true });
        var document = result.StructuredContent!.Value;
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal("preview", document.GetProperty("phase").GetString());
        Assert.Equal(ProjectContainerKinds.LocalSession, document.GetProperty("effects")
            .GetProperty("sourceContext").GetProperty("containerKind").GetString());
        Assert.Contains("probe_project_status_for_lifecycle", log.Methods().Skip(before));
        Assert.DoesNotContain("save_project", log.Methods().Skip(before));
    }

    [Fact]
    public async Task ReadWriteLocalSave_ConfirmsLocalSessionSaveAndVerifiesSameOwner()
    {
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, Amc));
        using var ui = new FakeWorkerUiOpenProject(Amc);
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        var prompts = new List<string>();
        var options = new McpClientOptions
        {
            Capabilities = new ClientCapabilities { Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() } },
            Handlers = new McpClientHandlers
            {
                ElicitationHandler = (request, _) =>
                {
                    prompts.Add(request!.Message);
                    return ValueTask.FromResult(new ElicitResult
                    {
                        Action = "accept",
                        Content = new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.SerializeToElement(true) }
                    });
                }
            }
        };
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(
            McpAccessMode.ReadWrite, audit.Path, clientOptions: options);
        var bound = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?> { ["projectPath"] = Amc });
        Assert.True(bound.StructuredContent!.Value.GetProperty("success").GetBoolean(), bound.StructuredContent!.Value.GetRawText());

        var result = await harness.Client.CallToolAsync("save_project", new Dictionary<string, object?>());
        var document = result.StructuredContent!.Value;
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal("applied", document.GetProperty("phase").GetString());
        var prompt = Assert.Single(prompts);
        Assert.Contains("local session", prompt, StringComparison.Ordinal);
        Assert.Contains("does not check in", prompt, StringComparison.Ordinal);
        Assert.Equal(1, log.Methods().Count(method => method == "save_project"));
        var verified = document.GetProperty("verification").GetProperty("value");
        Assert.False(verified.GetProperty("isModified").GetBoolean());
        Assert.Equal(ProjectContainerKinds.LocalSession, verified.GetProperty("context").GetProperty("containerKind").GetString());
        Assert.True(harness.WorkerClient.BindingSnapshot.IsVerified);
        Assert.Single(File.ReadAllLines(Assert.Single(Directory.GetFiles(audit.Path, "*.jsonl", SearchOption.AllDirectories))));
    }

    [Theory]
    [InlineData("compile_check", "compile_check")]
    [InlineData("plc_write", null)]
    [InlineData("network_write", null)]
    public async Task LocalWriteAndCompile_PassCapabilityGate(string operation, string? workerMethod)
    {
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, Amc));
        using var ui = new FakeWorkerUiOpenProject(Amc);
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path);
        var bound = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?> { ["projectPath"] = Amc });
        Assert.True(bound.StructuredContent!.Value.GetProperty("success").GetBoolean(), bound.StructuredContent.Value.GetRawText());

        var before = log.Methods().Length;
        var response = await harness.Client.CallToolAsync(operation, LocalWriteArguments(operation));
        Assert.DoesNotContain(WorkerFailureCategories.UnsupportedCapability, response.StructuredContent!.Value.GetRawText());
        if (workerMethod is not null) Assert.Contains(workerMethod, log.Methods().Skip(before));
    }

    private static Dictionary<string, object?> LocalWriteArguments(string operation) => operation switch
    {
        "plc_write" => new Dictionary<string, object?> { ["operations"] = new[] { new { operationId = "w1", operation = "create_block_group", blockPath = "PLC_1/Blocks/New" } }, ["dryRun"] = true },
        "network_write" => new Dictionary<string, object?> { ["operations"] = new[] { new { operationId = "w1", operation = "create_subnet", subnet = new { name = "New", networkType = "Ethernet" } } }, ["dryRun"] = true },
        _ => new Dictionary<string, object?>()
    };

    [Fact]
    public async Task BasicStatusAndIndependentInventory_RemainAvailable()
    {
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, Amc));
        using var ui = new FakeWorkerUiOpenProject(Amc);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadOnly);
        var bound = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?> { ["projectPath"] = Amc });
        Assert.True(bound.StructuredContent!.Value.GetProperty("success").GetBoolean(), bound.StructuredContent!.Value.GetRawText());

        var status = await harness.Client.CallToolAsync("get_project_status", new Dictionary<string, object?>());
        var project = status.StructuredContent!.Value.GetProperty("result").GetProperty("value");
        Assert.Equal(ProjectContainerKinds.LocalSession, project.GetProperty("context").GetProperty("containerKind").GetString());
        Assert.Equal(JsonValueKind.Null, project.GetProperty("metadata").ValueKind);

        var inventory = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?>
        {
            ["action"] = "list_server_connections", ["portalProcessId"] = 42
        });
        Assert.True(inventory.StructuredContent!.Value.GetProperty("success").GetBoolean(),
            inventory.StructuredContent!.Value.GetRawText());
    }

    [Theory]
    [InlineData("create_project")]
    [InlineData("save_project_as")]
    [InlineData("archive_project")]
    [InlineData("close_project")]
    public async Task LocalUnsupportedOperation_DeniesBeforeWorkerDispatch(string operation)
    {
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, Amc));
        using var ui = new FakeWorkerUiOpenProject(Amc);
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path);
        var bound = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?> { ["projectPath"] = Amc });
        Assert.True(bound.StructuredContent!.Value.GetProperty("success").GetBoolean(), bound.StructuredContent.Value.GetRawText());

        var before = log.Methods().Length;
        var arguments = operation switch
        {
            "create_project" => new Dictionary<string, object?> { ["projectDirectory"] = audit.Path, ["projectName"] = "New", ["dryRun"] = true },
            "save_project_as" => new Dictionary<string, object?> { ["targetDirectory"] = audit.Path, ["targetName"] = "Copy", ["dryRun"] = true },
            "archive_project" => new Dictionary<string, object?> { ["archiveDirectory"] = audit.Path, ["archiveName"] = "Copy", ["dryRun"] = true },
            _ => new Dictionary<string, object?> { ["dryRun"] = true }
        };
        var response = await harness.Client.CallToolAsync(operation, arguments);
        Assert.Contains(WorkerFailureCategories.UnsupportedCapability, response.StructuredContent!.Value.GetRawText());
        Assert.DoesNotContain(operation, log.Methods().Skip(before));
        Assert.DoesNotContain("probe_project_status_for_lifecycle", log.Methods().Skip(before));
    }
}
