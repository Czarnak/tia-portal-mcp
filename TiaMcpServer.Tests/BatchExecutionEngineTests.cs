using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TiaMcpServer.Batch;
using Xunit;

namespace TiaMcpServer.Tests;

public class BatchExecutionEngineTests
{
    private static BatchOperationRequest Op(string id, string operation)
        => new() { OperationId = id, Operation = operation };

    [Fact]
    public async Task ExecuteReadsAsync_AllSucceed_PreservesOrderAndResults()
    {
        var operations = new[] { Op("a", "browse_project_tree"), Op("b", "list_tag_tables") };

        var results = await BatchExecutionEngine.ExecuteReadsAsync(
            operations,
            op => Task.FromResult($"payload-{op.OperationId}"));

        Assert.Equal(new[] { "a", "b" }, results.Select(r => r.OperationId).ToArray());
        Assert.All(results, r => Assert.Equal(BatchOperationStatus.Succeeded, r.Status));
        Assert.Equal("payload-a", results[0].Result);
    }

    [Fact]
    public async Task ExecuteReadsAsync_PerItemFailureDoesNotStopOthers()
    {
        var operations = new[]
        {
            Op("a", "browse_project_tree"),
            Op("b", "get_block_content"),
            Op("c", "list_tag_tables"),
        };

        var results = await BatchExecutionEngine.ExecuteReadsAsync(
            operations,
            op => Task.FromResult(op.OperationId == "b" ? "Error: not found" : "ok"));

        Assert.Equal(BatchOperationStatus.Succeeded, results[0].Status);
        Assert.Equal(BatchOperationStatus.Failed, results[1].Status);
        Assert.Contains("not found", results[1].Result);
        Assert.Equal(BatchOperationStatus.Succeeded, results[2].Status);
    }

    [Fact]
    public async Task ApplyWritesAsync_AllSucceed_MarksAllSucceeded()
    {
        var operations = new[] { Op("a", "create_tag_table"), Op("b", "create_tag") };

        var results = await BatchExecutionEngine.ApplyWritesAsync(
            operations,
            op => Task.FromResult("done"));

        Assert.All(results, r => Assert.Equal(BatchOperationStatus.Succeeded, r.Status));
    }

    [Fact]
    public async Task ApplyWritesAsync_StopsOnFirstFailureAndSkipsRest()
    {
        var invoked = new List<string>();
        var operations = new[]
        {
            Op("a", "create_tag_table"),
            Op("b", "create_tag"),
            Op("c", "update_tag"),
        };

        var results = await BatchExecutionEngine.ApplyWritesAsync(
            operations,
            op =>
            {
                invoked.Add(op.OperationId);
                return Task.FromResult(op.OperationId == "b" ? "Error: boom" : "done");
            });

        Assert.Equal(BatchOperationStatus.Succeeded, results[0].Status);
        Assert.Equal(BatchOperationStatus.Failed, results[1].Status);
        Assert.Contains("boom", results[1].Result);
        Assert.Equal(BatchOperationStatus.Skipped, results[2].Status);

        // The item after the failure must never be invoked.
        Assert.Equal(new[] { "a", "b" }, invoked.ToArray());
    }
}
