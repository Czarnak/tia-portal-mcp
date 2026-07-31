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

        Assert.True(budgeted[0].Result!.Length <= 100);
        Assert.Contains("TRUNCATED", budgeted[0].Result);
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

        var budgeted = BatchPayloadBudget.Apply(results, maxItemChars: 100, maxBatchChars: 1_000);

        Assert.Equal(BatchOperationStatus.Succeeded, budgeted[0].Status);
        Assert.Equal(BatchOperationStatus.Succeeded, budgeted[1].Status);
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

        var budgeted = BatchPayloadBudget.Apply(results, maxItemChars: 10, maxBatchChars: 1_000);

        Assert.Same(warnings, budgeted[0].Warnings);
        Assert.Same(warnings, budgeted[1].Warnings);
        Assert.Contains("TRUNCATED", budgeted[0].Result);
        Assert.Equal(BatchOperationStatus.Succeeded, budgeted[1].Status);
    }

    [Fact]
    public void DefaultLimits_AreGenerousButFinite()
    {
        Assert.Equal(60_000, BatchPayloadBudget.MaxItemChars);
        Assert.Equal(180_000, BatchPayloadBudget.MaxBatchChars);
    }

    [Fact]
    public void TruncatedItem_IncludingTrailer_DoesNotExceedItemCap()
    {
        var budgeted = BatchPayloadBudget.Apply(
            new[] { Ok("a", new string('x', 150)) },
            maxItemChars: 100,
            maxBatchChars: 2_000);

        Assert.True(budgeted[0].Result!.Length <= 100);
        Assert.Contains("TRUNCATED", budgeted[0].Result);
    }

    [Fact]
    public void WarningHeavyBatch_DoesNotExceedTheFinalResponseBudget()
    {
        var warnings = Enumerable.Range(0, 4)
            .Select(index => $"warning-{index}: {new string('w', 300)}")
            .ToArray();

        var budgeted = BatchPayloadBudget.Apply(
            new[] { new BatchOperationResult("a", "browse_project_tree", BatchOperationStatus.Succeeded, "payload", warnings) },
            maxItemChars: 500,
            maxBatchChars: 850);

        var response = BatchResultFormatter.ReadBatch(budgeted);

        Assert.True(response.Length <= 850);
        Assert.Empty(budgeted[0].Warnings!);
    }

    [Fact]
    public void OmissionMarker_IsShortenedWhenTheFullMarkerWouldExceedTheFinalResponseBudget()
    {
        var budgeted = BatchPayloadBudget.Apply(
            new[] { Ok("a", new string('x', 150)), Ok("b", new string('y', 150)) },
            maxItemChars: 200,
            maxBatchChars: 500);

        var response = BatchResultFormatter.ReadBatch(budgeted);

        Assert.Equal(BatchOperationStatus.Omitted, budgeted[1].Status);
        Assert.Equal("[OMITTED]", budgeted[1].Result);
        Assert.True(response.Length <= 500);
    }

    [Fact]
    public void FailedItem_AfterBudgetExhaustion_PreservesItsDiagnosisAndFailedCount()
    {
        var budgeted = BatchPayloadBudget.Apply(
            new[]
            {
                Ok("a", new string('x', 600)),
                new BatchOperationResult("b", "compile_check", BatchOperationStatus.Failed, "Error: worker compilation failed")
            },
            maxItemChars: 600,
            maxBatchChars: 620);

        var response = BatchResultFormatter.ReadBatch(budgeted);

        Assert.Equal(BatchOperationStatus.Failed, budgeted[1].Status);
        Assert.Contains("worker compilation failed", budgeted[1].Result);
        Assert.Contains("\"failed\":1", response);
        Assert.True(response.Length <= 620);
    }

    [Fact]
    public void LargeFailedItems_StayWithinTheFinalResponseBudget()
    {
        var results = new[]
        {
            new BatchOperationResult("a", "compile_check", BatchOperationStatus.Failed, $"Error: {new string('a', 600)}"),
            new BatchOperationResult("b", "compile_check", BatchOperationStatus.Failed, $"Error: {new string('b', 600)}"),
            new BatchOperationResult("c", "compile_check", BatchOperationStatus.Failed, $"Error: {new string('c', 600)}")
        };

        var budgeted = BatchPayloadBudget.Apply(results, maxItemChars: 600, maxBatchChars: 1_250);
        var response = BatchResultFormatter.ReadBatch(budgeted);

        Assert.Equal(3, budgeted.Count);
        Assert.All(budgeted, result => Assert.Equal(BatchOperationStatus.Failed, result.Status));
        Assert.Contains("FAILED DETAIL TRUNCATED", budgeted[0].Result);
        Assert.Contains("FAILED DETAIL TRUNCATED", budgeted[1].Result);
        Assert.Contains("\"failed\":3", response);
        Assert.True(response.Length <= 1_250);
    }

    [Fact]
    public void WarningHeavyFailedItems_StayWithinTheFinalResponseBudget()
    {
        var warnings = Enumerable.Range(0, 4)
            .Select(index => $"warning-{index}: {new string('w', 300)}")
            .ToArray();
        var results = new[]
        {
            new BatchOperationResult("a", "compile_check", BatchOperationStatus.Failed, $"Error: {new string('a', 600)}", warnings),
            new BatchOperationResult("b", "compile_check", BatchOperationStatus.Failed, $"Error: {new string('b', 600)}", warnings),
            new BatchOperationResult("c", "compile_check", BatchOperationStatus.Failed, $"Error: {new string('c', 600)}", warnings)
        };

        var budgeted = BatchPayloadBudget.Apply(results, maxItemChars: 600, maxBatchChars: 3_000);
        var response = BatchResultFormatter.ReadBatch(budgeted);

        Assert.All(budgeted, result => Assert.Equal(BatchOperationStatus.Failed, result.Status));
        Assert.Contains(budgeted[0].Warnings!, warning => warning.Contains("FAILED WARNINGS TRUNCATED"));
        Assert.Contains(budgeted[1].Warnings!, warning => warning.Contains("FAILED WARNINGS TRUNCATED"));
        Assert.Contains("\"failed\":3", response);
        Assert.True(response.Length <= 3_000);
    }

    [Fact]
    public void BatchMarkers_DoNotRecommendStandaloneProjectFields()
    {
        var text = BatchPayloadBudget.TruncationTrailer(BatchPayloadBudget.MaxItemChars)
            + BatchPayloadBudget.OmissionMarker(BatchPayloadBudget.MaxBatchChars);
        var normalized = text.ToLowerInvariant();

        Assert.DoesNotContain("startpath", normalized);
        Assert.DoesNotContain("depth", normalized);
    }
}
