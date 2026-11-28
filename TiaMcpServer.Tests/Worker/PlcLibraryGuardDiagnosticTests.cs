using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

public sealed class PlcLibraryGuardDiagnosticTests
{
    private const string EditSummary = "plc_library_instance_edit: The selected PLC object is a linked library type instance. Update its library type version instead of replacing its content.";

    public static TheoryData<string> ControlledSummaries => new()
    {
        EditSummary,
        "plc_library_test_instance_delete: The selected PLC object is an in-work library test instance. Finish or discard its library type version before deleting the instance.",
        "plc_state_unverifiable: The PLC object's library linkage could not be inspected.",
        "plc_state_unverifiable: The linked PLC object's library version is unavailable; deletion safety cannot be established.",
        "plc_state_unverifiable: The linked PLC object's library version state is unknown; deletion safety cannot be established.",
        "plc_state_unverifiable: The PLC object's library-instance deletion protection could not be inspected.",
        "plc_state_unverifiable: The block group's complete descendants could not be inspected for library test instances."
    };

    [Theory]
    [MemberData(nameof(ControlledSummaries))]
    public void FixedLibraryGuardSummarySurvivesImportFailureSanitization(string summary)
    {
        var failure = new WorkerOperationException(WorkerFailureCategories.GuardBlocked, summary);

        var actual = BlockImportDiagnosticSanitizer.Failure(BlockImportDiagnosticContext.Generation, failure);

        Assert.Equal(summary, actual);
    }

    [Theory]
    [InlineData("plc_library_instance_edit: injected private source text")]
    [InlineData("plc_library_test_instance_delete: injected private source text")]
    [InlineData("plc_state_unverifiable: injected private source text")]
    [InlineData(EditSummary + " private source tail")]
    [InlineData(" " + EditSummary)]
    [InlineData("PLС_library_instance_edit: The selected PLC object is a linked library type instance. Update its library type version instead of replacing its content.")]
    public void PrefixOrLookalikeGuardTextIsStillSanitized(string summary)
    {
        var failure = new WorkerOperationException(WorkerFailureCategories.GuardBlocked, summary);

        var actual = BlockImportDiagnosticSanitizer.Failure(BlockImportDiagnosticContext.Generation, failure);

        Assert.Equal("Block source generation did not complete.", actual);
    }

    [Fact]
    public void MatchingTextWithDifferentFailureCategoryIsStillSanitized()
    {
        var failure = new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, EditSummary);

        var actual = BlockImportDiagnosticSanitizer.Failure(BlockImportDiagnosticContext.Generation, failure);

        Assert.Equal("Block source generation did not complete.", actual);
    }

    [Fact]
    public void MatchingTextInOrdinaryExceptionIsStillSanitized()
    {
        var actual = BlockImportDiagnosticSanitizer.Failure(BlockImportDiagnosticContext.Generation,
            new InvalidOperationException(EditSummary));

        Assert.Equal("Block source generation did not complete.", actual);
    }
}
