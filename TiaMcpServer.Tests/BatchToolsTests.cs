using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;
using TiaMcpServer.Batch;
using Xunit;

namespace TiaMcpServer.Tests;

public class BatchToolsTests
{
    private static BatchOperationRequest Op(string id, string operation, Action<BatchOperationRequest>? configure = null)
    {
        var request = new BatchOperationRequest { OperationId = id, Operation = operation };
        configure?.Invoke(request);
        return request;
    }

    [Theory]
    [InlineData("ExecuteReadBatch", "execute_read_batch")]
    [InlineData("PreviewWriteBatch", "preview_write_batch")]
    [InlineData("ApplyWriteBatch", "apply_write_batch")]
    public void BatchToolsHaveMcpMetadata(string methodName, string expectedToolName)
    {
        Assert.NotNull(typeof(BatchTools).GetCustomAttribute<McpServerToolTypeAttribute>());

        var method = typeof(BatchTools).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);
        var toolAttribute = method!.GetCustomAttribute<McpServerToolAttribute>();
        Assert.NotNull(toolAttribute);
        Assert.Equal(expectedToolName, toolAttribute!.Name);
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
    public async Task PreviewWriteBatch_RejectsReadOperation()
    {
        var result = await BatchTools.PreviewWriteBatch(
            workerClient: null!,
            new[] { Op("a", "get_block_content", r => r.BlockPath = "Main") });

        var root = JsonDocument.Parse(result).RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Contains("get_block_content", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task PreviewWriteBatch_RejectsProjectLifecycleOperation()
    {
        var result = await BatchTools.PreviewWriteBatch(
            workerClient: null!,
            new[] { Op("a", "close_project") });

        var root = JsonDocument.Parse(result).RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Contains("close_project", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ApplyWriteBatch_RejectsUnconfirmedRequests()
    {
        var result = await BatchTools.ApplyWriteBatch(
            workerClient: null!,
            new[] { Op("a", "create_tag", r => { r.TableName = "Inputs"; r.Name = "Start"; r.DataType = "Bool"; }) },
            confirm: false);

        var root = JsonDocument.Parse(result).RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Contains("confirm=true", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ApplyWriteBatch_RejectsInvalidBatchBeforeWorker()
    {
        var result = await BatchTools.ApplyWriteBatch(
            workerClient: null!,
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
        var result = await BatchTools.ApplyWriteBatch(
            workerClient: null!,
            new[] { Op("a", "create_tag", r => { r.TableName = "Inputs"; r.Name = "Start"; r.DataType = "Bool"; }) },
            confirm: true);

        Assert.Contains("Safety token required", result);
        Assert.Contains("preview_write_batch", result);
    }

    [Fact]
    public async Task ApplyWriteBatch_RejectsBadTokenBeforeReadingCurrentState()
    {
        var operations = new[]
        {
            new BatchOperationRequest { OperationId = "op-1", Operation = "start_plc" }
        };

        // workerClient is null: if the token envelope were checked AFTER the state read,
        // this call would throw NullReferenceException instead of returning the token error.
        var result = await BatchTools.ApplyWriteBatch(
            workerClient: null!,
            operations,
            confirm: true,
            safetyToken: "bogus-token");

        Assert.Contains("Safety token", result);
        Assert.Contains("preview_write_batch", result);
    }
}
