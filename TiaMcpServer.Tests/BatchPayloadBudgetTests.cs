using TiaMcpServer.Batch;
using Xunit;

namespace TiaMcpServer.Tests;

public class BatchPayloadBudgetTests
{
    private static BatchOperationResult Ok(string id, string payload)
        => new(id, "browse_project_tree", BatchOperationStatus.Succeeded, payload);

    [Fact]
    public void SmallResults_PassThroughUnchanged()
    {
        var results = new[] { Ok("a", "short"), Ok("b", "also short") };

        var budgeted = BatchPayloadBudget.Apply(results, maxItemChars: 100, maxBatchChars: 1000);

        Assert.Equal("short", budgeted[0].Result);
        Assert.Equal("also short", budgeted[1].Result);
        Assert.All(budgeted, result => Assert.Equal(BatchOperationStatus.Succeeded, result.Status));
    }

    [Fact]
    public void OversizedItem_IsTruncatedWithTrailer()
    {
        var results = new[] { Ok("a", new string('x', 150)) };

        var budgeted = BatchPayloadBudget.Apply(results, maxItemChars: 100, maxBatchChars: 10_000);

        Assert.StartsWith(new string('x', 100), budgeted[0].Result);
        Assert.Contains("TRUNCATED", budgeted[0].Result);
        Assert.Contains("startPath", budgeted[0].Result);
        Assert.Equal(BatchOperationStatus.Succeeded, budgeted[0].Status);
    }

    [Fact]
    public void ItemsBeyondTheBatchBudget_AreOmitted()
    {
        var results = new[]
        {
            Ok("a", new string('x', 90)),
            Ok("b", new string('y', 90)),
            Ok("c", "tiny")
        };

        var budgeted = BatchPayloadBudget.Apply(results, maxItemChars: 100, maxBatchChars: 100);

        Assert.Equal(BatchOperationStatus.Succeeded, budgeted[0].Status);
        Assert.Equal(BatchOperationStatus.Omitted, budgeted[1].Status);
        Assert.Contains("OMITTED", budgeted[1].Result);
        Assert.Contains("execute_read_batch", budgeted[1].Result);
        Assert.Equal(BatchOperationStatus.Succeeded, budgeted[2].Status);
        Assert.Equal("tiny", budgeted[2].Result);
    }

    [Fact]
    public void FailedItems_KeepTheirErrorText()
    {
        var results = new[]
        {
            new BatchOperationResult("a", "compile_check", BatchOperationStatus.Failed, "Error: boom")
        };

        var budgeted = BatchPayloadBudget.Apply(results, maxItemChars: 100, maxBatchChars: 1000);

        Assert.Equal("Error: boom", budgeted[0].Result);
        Assert.Equal(BatchOperationStatus.Failed, budgeted[0].Status);
    }

    [Fact]
    public void InputList_IsNotMutated()
    {
        var original = Ok("a", new string('x', 150));
        var results = new[] { original };

        BatchPayloadBudget.Apply(results, maxItemChars: 100, maxBatchChars: 1000);

        Assert.Equal(new string('x', 150), original.Result);
    }

    [Fact]
    public void BudgetedResults_PreserveExistingWarnings()
    {
        var warnings = new[] { "Use startPath to narrow the read." };
        var results = new[]
        {
            new BatchOperationResult("a", "browse_project_tree", BatchOperationStatus.Succeeded,
                new string('x', 150), warnings),
            new BatchOperationResult("b", "browse_project_tree", BatchOperationStatus.Succeeded,
                new string('y', 90), warnings)
        };

        var budgeted = BatchPayloadBudget.Apply(results, maxItemChars: 10, maxBatchChars: 200);

        Assert.Same(warnings, budgeted[0].Warnings);
        Assert.Same(warnings, budgeted[1].Warnings);
        Assert.Contains("TRUNCATED", budgeted[0].Result);
        Assert.Equal(BatchOperationStatus.Omitted, budgeted[1].Status);
    }

    [Fact]
    public void DefaultLimits_AreGenerousButFinite()
    {
        Assert.Equal(60_000, BatchPayloadBudget.MaxItemChars);
        Assert.Equal(180_000, BatchPayloadBudget.MaxBatchChars);
    }
}
