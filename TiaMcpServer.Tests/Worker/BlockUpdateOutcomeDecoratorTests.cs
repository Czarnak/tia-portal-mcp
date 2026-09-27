using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

public sealed class BlockUpdateOutcomeDecoratorTests
{
    private const string ControlledWarning =
        "Project state may have changed; inspect the project before retrying.";

    [Theory]
    [InlineData(WorkerFailureCategories.ValidationError, SourceFormatNames.Xml, "not_applicable")]
    [InlineData(WorkerFailureCategories.ValidationError, SourceFormatNames.Source, "not_created")]
    [InlineData(WorkerFailureCategories.AccessDenied, SourceFormatNames.Source, "not_created")]
    [InlineData(WorkerFailureCategories.WorkerOperationFailed, SourceFormatNames.Xml, "not_applicable")]
    [InlineData(WorkerFailureCategories.TargetNotFound, SourceFormatNames.Source, "not_created")]
    [InlineData(WorkerFailureCategories.BindingConflict, null, "unknown")]
    public void BeforeImporter_ResponseFailure_PreservesCategoryAndWarning_SanitizesText_AndAttachesNotStarted(
        string category,
        string? normalizedFormat,
        string expectedTemporarySourceState)
    {
        var response = new WorkerResponse
        {
            Success = false,
            FailureCategory = category,
            Error = "C:\\secret\\submitted-source.scl: rejected private payload",
            Warnings = new List<string> { ControlledWarning }
        };

        var decorated = BlockUpdateOutcomeDecorator.Decorate(
            response,
            normalizedFormat,
            importerEntered: false);

        Assert.Same(response, decorated);
        Assert.Equal(category, decorated.FailureCategory);
        Assert.Equal("Block import did not complete.", decorated.Error);
        Assert.Equal(new[] { ControlledWarning }, decorated.Warnings);
        Assert.NotNull(decorated.BlockImportOutcome);
        Assert.Equal("not_started", decorated.BlockImportOutcome.ImportStage);
        Assert.False(decorated.BlockImportOutcome.TargetMutationCommitted);
        Assert.Equal("not_started", decorated.BlockImportOutcome.CompileStage);
        Assert.Equal("not_started", decorated.BlockImportOutcome.FinalReadStage);
        Assert.Equal(expectedTemporarySourceState, decorated.BlockImportOutcome.TemporarySourceState);
    }

    [Fact]
    public void BeforeImporter_WorkerException_PreservesCategoryAndControlledWarnings()
    {
        var exception = new WorkerOperationException(
            WorkerFailureCategories.WorkerOperationFailed,
            "private project path",
            new[] { ControlledWarning });

        var decorated = BlockUpdateOutcomeDecorator.Decorate(
            exception,
            SourceFormatNames.Xml,
            importerEntered: false);

        Assert.False(decorated.Success);
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, decorated.FailureCategory);
        Assert.Equal("Block import did not complete.", decorated.Error);
        Assert.Equal(new[] { ControlledWarning }, decorated.Warnings);
        Assert.False(decorated.BlockImportOutcome!.TargetMutationCommitted);
        Assert.Equal("not_applicable", decorated.BlockImportOutcome.TemporarySourceState);
    }

    [Fact]
    public void AfterImporter_UnannotatedException_AttachesConservativeUnknown()
    {
        var decorated = BlockUpdateOutcomeDecorator.Decorate(
            new InvalidOperationException("C:\\private\\input.scl"),
            SourceFormatNames.Source,
            importerEntered: true);

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, decorated.FailureCategory);
        Assert.Equal("Block import did not complete.", decorated.Error);
        Assert.NotNull(decorated.BlockImportOutcome);
        Assert.Equal("unknown", decorated.BlockImportOutcome.ImportStage);
        Assert.Null(decorated.BlockImportOutcome.TargetMutationCommitted);
        Assert.Equal("unavailable", decorated.BlockImportOutcome.CompileStage);
        Assert.Equal("unavailable", decorated.BlockImportOutcome.FinalReadStage);
        Assert.Equal("unknown", decorated.BlockImportOutcome.TemporarySourceState);
        Assert.DoesNotContain("private", decorated.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnnotatedImporterException_PreservesClosedSanitizedResultTextAndOutcome()
    {
        var outcome = new BlockImportOutcomeInfo
        {
            ImportStage = "completed",
            ImportResultState = "non_success",
            TargetMutationCommitted = true,
            CompileStage = "unavailable",
            FinalReadStage = "unavailable",
            TemporarySourceState = "not_applicable"
        };
        var exception = new WorkerOperationException(
            WorkerFailureCategories.PostconditionFailed,
            "Temporary source cleanup could not be confirmed.",
            new[] { ControlledWarning },
            outcome);

        var decorated = BlockUpdateOutcomeDecorator.Decorate(
            exception,
            SourceFormatNames.Xml,
            importerEntered: true);

        Assert.Equal("Temporary source cleanup could not be confirmed.", decorated.Error);
        Assert.Same(outcome, decorated.BlockImportOutcome);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, decorated.FailureCategory);
    }
}
