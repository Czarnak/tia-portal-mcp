using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Block;

public class BlockImportDiagnosticSanitizerTests
{
    public static IEnumerable<object[]> FailureContexts()
    {
        foreach (var context in Enum.GetValues<BlockImportDiagnosticContext>())
            yield return new object[] { (int)context };
    }

    [Theory]
    [MemberData(nameof(FailureContexts))]
    public void FailureSummaryNeverIncludesExceptionDetails(int contextValue)
    {
        var context = (BlockImportDiagnosticContext)contextValue;
        var failure = new InvalidOperationException(
            "C:\\Users\\operator\\secret.ap21 \\\\server\\share\\source.db "
            + "tia-mcp-import-temp InternalNode SECRET_CONTENT");

        var summary = BlockImportDiagnosticSanitizer.Failure(context, failure);

        Assert.InRange(summary.Length, 1, 256);
        Assert.DoesNotContain("InvalidOperationException", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("\\\\server", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("tia-mcp", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InternalNode", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET_CONTENT", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeWarningsKeepsOnlyClosedKnownFactsAndBoundsThem()
    {
        var warnings = BlockImportDiagnosticSanitizer.SanitizeWarnings(new[]
        {
            "C:\\private\\Fixture.ap21 InternalNode SECRET_CONTENT",
            BlockImportDiagnosticSanitizer.GeneratedCountWarning(2),
            BlockImportDiagnosticSanitizer.SourceNodeCleanupWarning(),
            BlockImportDiagnosticSanitizer.StagingCleanupWarning()
        });

        Assert.Contains("TIA Portal reported 2 generated block objects.", warnings);
        Assert.Contains(BlockImportDiagnosticSanitizer.SourceNodeCleanupWarning(), warnings);
        Assert.Contains(BlockImportDiagnosticSanitizer.StagingCleanupWarning(), warnings);
        Assert.DoesNotContain(warnings, warning => warning.Contains("Fixture", StringComparison.Ordinal));
        Assert.DoesNotContain(warnings, warning => warning.Contains("InternalNode", StringComparison.Ordinal));
        Assert.All(warnings, warning => Assert.InRange(warning.Length, 1, 256));
    }

    [Fact]
    public void CoordinatorImportAndStagingCleanupFailuresAreSanitized()
    {
        var exception = Assert.Throws<WorkerOperationException>(() => BlockImportCoordinator.Execute(
            "Main.xml",
            "<Main />",
            (_, _, boundary) =>
            {
                boundary.BeforeSiemensCall();
                throw new IOException("C:\\secret\\Fixture.ap21 SECRET_CONTENT");
            },
            _ => BlockPostconditionEvidence.Import(
                BlockCompileObservation.Unavailable(null),
                "unavailable",
                null),
            cleanupDirectory: _ => throw new IOException("\\\\server\\secret\\temp")));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, exception.FailureCategory);
        Assert.Equal("Block import did not complete.", exception.Message);
        Assert.Contains(BlockImportDiagnosticSanitizer.StagingCleanupWarning(), exception.Warnings);
        Assert.DoesNotContain("secret", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(exception.Warnings, warning =>
            warning.Contains("server", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GenerationThrowRetainsSourceCleanupStateWithoutFabricatingGeneratedCount()
    {
        var exception = Assert.Throws<WorkerOperationException>(() => BlockImportCoordinator.ExecuteSource(
            (boundary, source) =>
            {
                source.BeforeCreateFromFile();
                source.AfterCreateFromFileReturned();
                boundary.BeforeSiemensCall();
                source.AfterDeleteAttempt(removed: true);
                throw new InvalidOperationException("C:\\private\\generated.db");
            },
            _ => BlockPostconditionEvidence.Import(
                BlockCompileObservation.Unavailable(null),
                "succeeded",
                true)));

        Assert.Equal("unknown", exception.BlockImportOutcome!.ImportStage);
        Assert.Equal("removed", exception.BlockImportOutcome.TemporarySourceState);
        Assert.DoesNotContain(exception.Warnings, warning =>
            warning.Contains("0 generated", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Block source generation did not complete.", exception.Message);
    }
}
