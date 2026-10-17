using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

public class WorkerCallResultTests
{
    [Fact]
    public void Ok_CarriesPayloadWithoutError()
    {
        var result = WorkerCallResult.Ok("{\"a\":1}");

        Assert.True(result.Success);
        Assert.Equal("{\"a\":1}", result.Payload);
        Assert.Null(result.Error);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Ok_HasNoFailureCategory()
    {
        Assert.Null(WorkerCallResult.Ok("data").FailureCategory);
    }

    [Fact]
    public void Fail_CarriesErrorAndEmptyPayload()
    {
        var result = WorkerCallResult.Fail(WorkerFailureCategories.WorkerOperationFailed, "boom");

        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.Payload);
        Assert.Equal("boom", result.Error);
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, result.FailureCategory);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not_an_approved_category")]
    public void Fail_RequiresOneApprovedCategory(string? category)
    {
        Assert.Throws<ArgumentException>(() => WorkerCallResult.Fail(category!, "boom"));
    }

    [Fact]
    public void Ok_PayloadStartingWithErrorPrefixStaysSuccessful()
    {
        // The whole point of item 1.1: payload text must never drive classification.
        var result = WorkerCallResult.Ok("Error: literal block comment content, not a failure");

        Assert.True(result.Success);
    }

    [Fact]
    public void Warnings_AreAttachedToBothShapes()
    {
        Assert.Single(WorkerCallResult.Ok("x", new[] { "w1" }).Warnings);
        Assert.Single(
            WorkerCallResult.Fail(WorkerFailureCategories.WorkerOperationFailed, "e", new[] { "w1" }).Warnings);
    }

    [Fact]
    public void BlockImportOutcome_CarriedBySuccessAndFailureWithoutChangingPayloadOrError()
    {
        var outcome = new BlockImportOutcomeInfo
        {
            ImportStage = "completed", ImportResultState = "success",
            TargetMutationCommitted = true, CompileStage = "unavailable",
            FinalReadStage = "unavailable", TemporarySourceState = "not_applicable",
            ContentRelation = "unknown"
        };
        var ok = WorkerCallResult.Ok("old payload") with { BlockImportOutcome = outcome };
        var failed = WorkerCallResult.Fail(
            WorkerFailureCategories.PostconditionFailed, "bounded failure",
            new[] { "bounded warning" }) with { BlockImportOutcome = outcome };

        Assert.Same(outcome, ok.BlockImportOutcome);
        Assert.Same(outcome, failed.BlockImportOutcome);
        Assert.Equal("old payload", ok.Payload);
        Assert.Equal("bounded failure", failed.Error);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, failed.FailureCategory);
        Assert.Equal("bounded warning", Assert.Single(failed.Warnings));
    }
}
