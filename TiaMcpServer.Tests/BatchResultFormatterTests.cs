using System.Text.Json;
using TiaMcpServer.Batch;
using Xunit;

namespace TiaMcpServer.Tests;

public class BatchResultFormatterTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Error_ProducesUnsuccessfulEnvelopeWithMessage()
    {
        var root = Parse(BatchResultFormatter.Error("execute_read_batch", "boom"));

        Assert.Equal("execute_read_batch", root.GetProperty("tool").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("boom", root.GetProperty("error").GetString());
    }

    [Fact]
    public void ReadBatch_AllSucceeded_IsSuccessWithCounts()
    {
        var results = new[]
        {
            new BatchOperationResult("a", "browse_project_tree", BatchOperationStatus.Succeeded, "x"),
            new BatchOperationResult("b", "list_tag_tables", BatchOperationStatus.Succeeded, "y"),
        };

        var root = Parse(BatchResultFormatter.ReadBatch(results));

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(2, root.GetProperty("operationCount").GetInt32());
        Assert.Equal(2, root.GetProperty("succeeded").GetInt32());
        Assert.Equal(2, root.GetProperty("operations").GetArrayLength());
    }

    [Fact]
    public void ReadBatch_AnyFailure_IsNotSuccess()
    {
        var results = new[]
        {
            new BatchOperationResult("a", "browse_project_tree", BatchOperationStatus.Succeeded, "x"),
            new BatchOperationResult("b", "get_block_content", BatchOperationStatus.Failed, "Error: nope"),
        };

        var root = Parse(BatchResultFormatter.ReadBatch(results));

        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(1, root.GetProperty("failed").GetInt32());
    }

    [Fact]
    public void ApplyBatch_CountsSucceededFailedAndSkipped()
    {
        var results = new[]
        {
            new BatchOperationResult("a", "create_tag_table", BatchOperationStatus.Succeeded, "ok"),
            new BatchOperationResult("b", "create_tag", BatchOperationStatus.Failed, "Error: boom"),
            new BatchOperationResult("c", "update_tag", BatchOperationStatus.Skipped, null),
        };

        var root = Parse(BatchResultFormatter.ApplyBatch(results));

        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(1, root.GetProperty("succeeded").GetInt32());
        Assert.Equal(1, root.GetProperty("failed").GetInt32());
        Assert.Equal(1, root.GetProperty("skipped").GetInt32());
        Assert.Equal("apply_write_batch", root.GetProperty("tool").GetString());
    }
}
