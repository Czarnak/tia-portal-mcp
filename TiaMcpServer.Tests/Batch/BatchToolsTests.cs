using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public class BatchToolsTests
{
    private static BatchOperationRequest Op(string id, string operation, Action<BatchOperationRequest>? configure = null)
    {
        var request = new BatchOperationRequest { OperationId = id, Operation = operation };
        configure?.Invoke(request);
        return request;
    }

    private static OpennessWorkerClient CreateReadWriteClient()
        => new(
            new ProjectSessionBinding(null),
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(McpAccessMode.ReadWrite));

    [Theory]
    [InlineData("ExecuteReadBatch", "execute_read_batch")]
    [InlineData("PreviewWriteBatch", "preview_write_batch")]
    [InlineData("ApplyWriteBatch", "apply_write_batch")]
    public void BatchToolsHaveMcpMetadata(string methodName, string expectedToolName)
    {
        // Tools have been split into ReadBatchTools and WriteBatchTools.
        // BatchTools retains the methods for backward compatibility but no longer
        // carries [McpServerToolType]/[McpServerTool] attributes.
        var type = methodName == "ExecuteReadBatch"
            ? typeof(ReadBatchTools)
            : typeof(WriteBatchTools);

        Assert.NotNull(type.GetCustomAttribute<McpServerToolTypeAttribute>());

        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);

        Assert.NotNull(method);
        var toolAttribute = method!.GetCustomAttribute<McpServerToolAttribute>();
        Assert.NotNull(toolAttribute);
        Assert.Equal(expectedToolName, toolAttribute!.Name);
    }

    [Theory]
    [InlineData(nameof(WriteBatchTools.PreviewWriteBatch), "preview_write_batch", true, false, false)]
    [InlineData(nameof(WriteBatchTools.ApplyWriteBatch), "apply_write_batch", false, true, false)]
    public void WriteBatchTools_RegisteredMethodsExposeExplicitMcpAnnotations(
        string methodName,
        string expectedToolName,
        bool readOnly,
        bool destructive,
        bool openWorld)
    {
        var method = typeof(WriteBatchTools).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);
        var toolAttribute = method!.GetCustomAttribute<McpServerToolAttribute>();
        Assert.NotNull(toolAttribute);
        Assert.Equal(expectedToolName, toolAttribute!.Name);
        Assert.Equal(readOnly, toolAttribute.ReadOnly);
        Assert.Equal(destructive, toolAttribute.Destructive);
        Assert.Equal(openWorld, toolAttribute.OpenWorld);
    }

    [Fact]
    public async Task ExecuteReadBatch_RejectsWriteOperation()
    {
        var result = await BatchTools.ExecuteReadBatch(
            workerClient: null!,
            new[] { Op("a", "update_block_logic", r => { r.BlockPath = "Main"; r.YamlContent = "x"; }) });

        var root = JsonDocument.Parse(result).RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Contains("update_block_logic", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ExecuteReadBatch_RejectsEmptyBatch()
    {
        var result = await BatchTools.ExecuteReadBatch(workerClient: null!, Array.Empty<BatchOperationRequest>());

        var root = JsonDocument.Parse(result).RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Contains("at least one", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ExecuteReadBatch_RejectsDedicatedNetworkReadBeforeWorkerStartup()
    {
        var result = await BatchTools.ExecuteReadBatch(
            workerClient: null!,
            new[] { Op("a", "read_hardware_config") });

        using var document = JsonDocument.Parse(result);
        Assert.Contains(
            "Unknown operation 'read_hardware_config'",
            document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task PreviewWriteBatch_RejectsReadOperation()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();

        var result = await BatchTools.PreviewWriteBatch(
            workerClient: null!,
            safety,
            new[] { Op("a", "get_block_content", r => r.BlockPath = "Main") });

        var root = JsonDocument.Parse(result).RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Contains("get_block_content", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task PreviewWriteBatch_RejectsProjectLifecycleOperation()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();

        var result = await BatchTools.PreviewWriteBatch(
            workerClient: null!,
            safety,
            new[] { Op("a", "close_project") });

        var root = JsonDocument.Parse(result).RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Contains("close_project", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task PreviewWriteBatch_RejectsDedicatedNetworkWriteBeforeWorkerStartup()
    {
        using var audit = new TempAuditDirectory();

        var result = await BatchTools.PreviewWriteBatch(
            workerClient: null!,
            audit.CreateSafety(),
            new[] { Op("a", "add_network_device") });

        using var document = JsonDocument.Parse(result);
        Assert.Contains(
            "Unknown operation 'add_network_device'",
            document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ApplyWriteBatch_RejectsUnconfirmedRequests()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();

        var result = await BatchTools.ApplyWriteBatch(
            workerClient: null!,
            safety,
            new[] { Op("a", "create_tag", r => { r.TableName = "Inputs"; r.Name = "Start"; r.DataType = "Bool"; }) },
            confirm: false);

        var root = JsonDocument.Parse(result).RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Contains("confirm=true", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ApplyWriteBatch_RejectsInvalidBatchBeforeWorker()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();

        var result = await BatchTools.ApplyWriteBatch(
            workerClient: null!,
            safety,
            new[] { Op("a", "get_block_content", r => r.BlockPath = "Main") },
            confirm: true,
            safetyToken: "anything");

        var root = JsonDocument.Parse(result).RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Contains("get_block_content", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ApplyWriteBatch_RejectsMissingSafetyToken()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        using var client = CreateReadWriteClient();

        var result = await BatchTools.ApplyWriteBatch(
            workerClient: client,
            safety,
            new[] { Op("a", "create_tag", r => { r.TableName = "Inputs"; r.Name = "Start"; r.DataType = "Bool"; }) },
            confirm: true);

        Assert.Contains("Safety token required", result);
        Assert.Contains("preview_write_batch", result);
    }

    [Fact]
    public async Task ApplyWriteBatch_RejectsBadTokenBeforeReadingCurrentState()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        using var client = CreateReadWriteClient();

        var operations = new[]
        {
            new BatchOperationRequest { OperationId = "op-1", Operation = "start_plc" }
        };

        var result = await BatchTools.ApplyWriteBatch(
            workerClient: client,
            safety,
            operations,
            confirm: true,
            safetyToken: "bogus-token");

        Assert.Contains("Safety token", result);
        Assert.Contains("preview_write_batch", result);
    }

    [Fact]
    public async Task ApplyWriteBatch_WrapperMatchesRegisteredBadTokenEnvelope()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        using var client = CreateReadWriteClient();
        var operations = new[]
        {
            new BatchOperationRequest
            {
                OperationId = "op-1",
                Operation = "create_user_constant",
                TableName = "Constants",
                Name = "Gain",
                DataType = "Int",
                Value = "1",
                ProjectPath = "type-content-roundtrip"
            }
        };

        var registered = await WriteBatchTools.ApplyWriteBatch(
            workerClient: client,
            safety,
            operations,
            confirm: true,
            safetyToken: "bogus-token");
        var wrapper = await BatchTools.ApplyWriteBatch(
            workerClient: client,
            safety,
            operations,
            confirm: true,
            safetyToken: "bogus-token");

        Assert.Equal(registered, wrapper);
    }
}
