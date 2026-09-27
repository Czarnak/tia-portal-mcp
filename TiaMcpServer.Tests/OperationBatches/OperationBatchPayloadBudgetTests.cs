using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;
using Xunit;

namespace TiaMcpServer.Tests.OperationBatches;

public class OperationBatchPayloadBudgetTests
{
    // The legacy generic budget engine tested here is not what Network uses (Network went through
    // StructuredOperationBatchPayloadBudget in Phase 2); this is still exercised by execute_read_batch
    // and friends, so the tool name is a real generic one rather than a Network name that would
    // wrongly imply Network's results are still string-valued.
    private const string ToolName = "execute_read_batch";
    private const string Hint = "Use query/maxResults or split the batch.";

    private static OperationBatchResult Ok(string id, string payload, IReadOnlyList<string>? warnings = null)
        => new(id, "read_hardware_config", OperationBatchStatus.Succeeded, payload, warnings);

    private static IReadOnlyList<OperationBatchResult> Apply(
        IReadOnlyList<OperationBatchResult> results,
        int maxItemChars = 600,
        int maxBatchChars = 3_000)
        => OperationBatchPayloadBudget.Apply(results, ToolName, ToolName, Hint, maxItemChars, maxBatchChars);

    [Fact]
    public void OversizedItem_IsTruncatedWithCallerSpecificMarkerAndWarnings()
    {
        var warnings = new[] { "warning" };
        var budgeted = OperationBatchPayloadBudget.Apply(
            new[] { Ok("a", new string('x', 100), warnings) },
            toolName: ToolName,
            retryToolName: ToolName,
            narrowingHint: Hint,
            maxItemChars: 80,
            maxBatchChars: 500);

        Assert.True(budgeted[0].Result!.Length <= 80);
        Assert.Contains("TRUNCATED", budgeted[0].Result);
        Assert.Same(warnings, budgeted[0].Warnings);
    }

    [Fact]
    public void CombinedOverflow_IsRepresentedAsOmitted()
    {
        var budgeted = OperationBatchPayloadBudget.Apply(
            new[]
            {
                Ok("a", new string('x', 100)),
                Ok("b", new string('y', 100)),
                Ok("c", new string('z', 100))
            },
            toolName: ToolName,
            retryToolName: ToolName,
            narrowingHint: Hint,
            maxItemChars: 80,
            maxBatchChars: 650);

        Assert.Equal(OperationBatchStatus.Omitted, budgeted[2].Status);
        Assert.True(OperationBatchResultFormatter.Read(ToolName, budgeted).Length <= 650);
    }

    [Fact]
    public void FullOmissionMarker_UsesCallerSuppliedRetryToolName()
    {
        var marker = OperationBatchPayloadBudget.OmissionMarker(ToolName, Hint, maxBatchChars: 500);

        Assert.Contains(ToolName, marker);
    }

    [Fact]
    public void Apply_DoesNotMutateInputRecords()
    {
        var original = Ok("a", new string('x', 100));

        OperationBatchPayloadBudget.Apply(
            new[] { original },
            toolName: ToolName,
            retryToolName: ToolName,
            narrowingHint: Hint,
            maxItemChars: 80,
            maxBatchChars: 500);

        Assert.Equal(new string('x', 100), original.Result);
    }

    [Fact]
    public void FailedItem_RemainsRepresentedAsFailed()
    {
        var budgeted = OperationBatchPayloadBudget.Apply(
            new[]
            {
                Ok("a", new string('x', 80)),
                new OperationBatchResult("b", "read_hardware_config", OperationBatchStatus.Failed, "boom")
            },
            toolName: ToolName,
            retryToolName: ToolName,
            narrowingHint: Hint,
            maxItemChars: 80,
            maxBatchChars: 500);

        Assert.Equal(OperationBatchStatus.Failed, budgeted[1].Status);
    }

    [Fact]
    public void SmallResults_PassThroughUnchanged()
    {
        var budgeted = Apply(new[] { Ok("a", "short"), Ok("b", "also short") });

        Assert.Equal("short", budgeted[0].Result);
        Assert.Equal("also short", budgeted[1].Result);
        Assert.All(budgeted, result => Assert.Equal(OperationBatchStatus.Succeeded, result.Status));
    }

    [Fact]
    public void DefaultLimits_AreGenerousButFinite()
    {
        Assert.Equal(60_000, OperationBatchPayloadBudget.MaxItemChars);
        Assert.Equal(180_000, OperationBatchPayloadBudget.MaxBatchChars);
    }

    [Fact]
    public void WarningHeavyBatch_DoesNotExceedFinalResponseBudget()
    {
        var warnings = Enumerable.Range(0, 4).Select(index => $"warning-{index}: {new string('w', 300)}").ToArray();
        var budgeted = Apply(
            new[] { Ok("a", "payload", warnings) },
            maxItemChars: 500,
            maxBatchChars: 850);

        Assert.True(OperationBatchResultFormatter.Read(ToolName, budgeted).Length <= 850);
        Assert.Empty(budgeted[0].Warnings!);
    }

    [Fact]
    public void OmissionMarker_FallsBackWhenFullMarkerCannotFit()
    {
        var budgeted = Apply(
            new[] { Ok("a", new string('x', 150)), Ok("b", new string('y', 150)) },
            maxItemChars: 200,
            maxBatchChars: 500);

        Assert.Equal(OperationBatchStatus.Omitted, budgeted[1].Status);
        Assert.Equal("[OMITTED]", budgeted[1].Result);
        var response = OperationBatchResultFormatter.Read(ToolName, budgeted);
        using var document = JsonDocument.Parse(response);
        var root = document.RootElement;
        Assert.True(response.Length <= 500);
        Assert.Equal(budgeted.Count(item => item.Status == OperationBatchStatus.Succeeded), root.GetProperty("succeeded").GetInt32());
        Assert.Equal(budgeted.Count(item => item.Status == OperationBatchStatus.Failed), root.GetProperty("failed").GetInt32());
        Assert.Equal(budgeted.Count(item => item.Status == OperationBatchStatus.Omitted), root.GetProperty("omitted").GetInt32());
        Assert.All(root.GetProperty("operations").EnumerateArray(), item =>
            Assert.Equal(JsonValueKind.Null, item.GetProperty("failureCategory").ValueKind));
    }

    [Fact]
    public void PriorCategorizedFailure_SurvivesLaterOmissionAndFinalBudget()
    {
        const int maxBatchChars = 520;
        var budgeted = Apply(
            new[]
            {
                new OperationBatchResult(
                    "a", "compile_check", OperationBatchStatus.Failed,
                    $"Error: {new string('e', 140)}",
                    new[] { $"warning: {new string('w', 150)}" },
                    WorkerFailureCategories.WorkerOperationFailed),
                Ok("b", new string('x', 150))
            },
            maxItemChars: 200,
            maxBatchChars: maxBatchChars);

        var response = OperationBatchResultFormatter.Read(ToolName, budgeted);
        using var document = JsonDocument.Parse(response);
        var root = document.RootElement;
        Assert.True(response.Length <= maxBatchChars);
        Assert.Equal(OperationBatchStatus.Failed, budgeted[0].Status);
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, budgeted[0].FailureCategory);
        Assert.Equal(OperationBatchStatus.Omitted, budgeted[1].Status);
        Assert.Equal(1, root.GetProperty("failed").GetInt32());
        Assert.Equal(1, root.GetProperty("omitted").GetInt32());
        Assert.Equal("worker_operation_failed", root.GetProperty("operations")[0].GetProperty("failureCategory").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("operations")[1].GetProperty("failureCategory").ValueKind);
        Assert.True(
            budgeted[0].Result!.Contains("FAILED DETAIL TRUNCATED", StringComparison.Ordinal)
            || budgeted[0].Warnings!.Any(warning => warning.Contains("FAILED WARNINGS TRUNCATED", StringComparison.Ordinal)));
    }

    [Fact]
    public void ImpossibleTinyCap_ThrowsInsteadOfReturningOversizedResponse()
    {
        Assert.Throws<InvalidOperationException>(() => Apply(
            new[] { Ok("a", "payload") },
            maxItemChars: 80,
            maxBatchChars: 8));
    }

    [Fact]
    public void FailedItem_AfterBudgetExhaustion_PreservesDiagnosisAndCount()
    {
        var budgeted = Apply(
            new[]
            {
                Ok("a", new string('x', 600)),
                new OperationBatchResult("b", "compile_check", OperationBatchStatus.Failed, "Error: worker compilation failed")
            },
            maxItemChars: 600,
            maxBatchChars: 620);

        var response = OperationBatchResultFormatter.Read(ToolName, budgeted);
        Assert.Equal(OperationBatchStatus.Failed, budgeted[1].Status);
        Assert.Contains("worker compilation failed", budgeted[1].Result);
        Assert.Contains("\"failed\":1", response);
        Assert.True(response.Length <= 620);
    }

    [Fact]
    public void LargeFailedItems_AndWarningsStayWithinFinalResponseBudget()
    {
        var warnings = Enumerable.Range(0, 4).Select(index => $"warning-{index}: {new string('w', 300)}").ToArray();
        var budgeted = Apply(
            new[]
            {
                new OperationBatchResult("a", "compile_check", OperationBatchStatus.Failed, $"Error: {new string('a', 600)}", warnings),
                new OperationBatchResult("b", "compile_check", OperationBatchStatus.Failed, $"Error: {new string('b', 600)}", warnings),
                new OperationBatchResult("c", "compile_check", OperationBatchStatus.Failed, $"Error: {new string('c', 600)}", warnings)
            },
            maxItemChars: 600,
            maxBatchChars: 3_000);

        var response = OperationBatchResultFormatter.Read(ToolName, budgeted);
        Assert.All(budgeted, result => Assert.Equal(OperationBatchStatus.Failed, result.Status));
        Assert.Contains(budgeted[0].Warnings!, warning => warning.Contains("FAILED WARNINGS TRUNCATED"));
        Assert.Contains("FAILED DETAIL TRUNCATED", budgeted[0].Result);
        Assert.Contains("\"failed\":3", response);
        Assert.True(response.Length <= 3_000);
    }

    [Fact]
    public void FailedItem_CategoryAndTruncationSurviveNearCapReadResponse()
    {
        const int maxBatchChars = 620;
        var warnings = Enumerable.Range(0, 4)
            .Select(index => $"warning-{index}: {new string('w', 300)}")
            .ToArray();
        var budgeted = Apply(
            new[]
            {
                Ok("a", new string('x', 600)),
                new OperationBatchResult(
                    "b", "compile_check", OperationBatchStatus.Failed,
                    $"Error: {new string('e', 600)}", warnings,
                    WorkerFailureCategories.WorkerOperationFailed)
            },
            maxItemChars: 600,
            maxBatchChars: maxBatchChars);

        var response = OperationBatchResultFormatter.Read(ToolName, budgeted);
        using var document = JsonDocument.Parse(response);
        Assert.Equal(OperationBatchStatus.Failed, budgeted[1].Status);
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, budgeted[1].FailureCategory);
        Assert.Equal("worker_operation_failed", document.RootElement.GetProperty("operations")[1].GetProperty("failureCategory").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("failed").GetInt32());
        Assert.Contains("FAILED DETAIL TRUNCATED", budgeted[1].Result);
        Assert.Contains(budgeted[1].Warnings!, warning => warning.Contains("FAILED WARNINGS TRUNCATED"));
        Assert.True(response.Length <= maxBatchChars);
    }

    [Fact]
    public void MarkersUseOnlyCallerProvidedNarrowingGuidance()
    {
        var text = OperationBatchPayloadBudget.TruncationTrailer(OperationBatchPayloadBudget.MaxItemChars, Hint)
            + OperationBatchPayloadBudget.OmissionMarker(ToolName, Hint, OperationBatchPayloadBudget.MaxBatchChars);
        var normalized = text.ToLowerInvariant();

        Assert.DoesNotContain("startpath", normalized);
        Assert.DoesNotContain("depth", normalized);
    }
}
