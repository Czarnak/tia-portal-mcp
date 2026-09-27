using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Block;

public class BlockImportOutcomeInfoTests
{
    private static BlockImportOutcomeInfo Valid() => new()
    {
        ImportStage = "not_started",
        ImportResultState = "unavailable",
        TargetMutationCommitted = false,
        CompileStage = "not_started",
        FinalReadStage = "not_started",
        TemporarySourceState = "not_applicable",
        ContentRelation = "unknown"
    };

    private static BlockImportOutcomeInfo Completed() => Valid() with
    {
        ImportStage = "completed",
        ImportResultState = "success",
        TargetMutationCommitted = true
    };

    [Theory]
    [InlineData("not_started", false, "unavailable")]
    [InlineData("unknown", null, "unavailable")]
    [InlineData("completed", true, "success")]
    [InlineData("completed", true, "non_success")]
    public void Validate_AcceptsClosedTargetMatrix(string stage, bool? committed, string result)
    {
        var outcome = Valid() with { ImportStage = stage, TargetMutationCommitted = committed, ImportResultState = result };
        Assert.True(BlockImportOutcomeValidator.Validate(outcome, SourceFormatNames.Xml));
    }

    [Theory]
    [InlineData("not_started", "not_created")]
    [InlineData("not_started", "removed")]
    [InlineData("not_started", "residue_possible")]
    [InlineData("not_started", "unknown")]
    [InlineData("unknown", "removed")]
    [InlineData("unknown", "residue_possible")]
    [InlineData("unknown", "unknown")]
    [InlineData("completed", "removed")]
    [InlineData("completed", "residue_possible")]
    [InlineData("completed", "unknown")]
    public void Validate_AcceptsSourceLifecycleStates(string stage, string sourceState)
    {
        var (committed, result) = stage switch
        {
            "completed" => ((bool?)true, "success"),
            "unknown" => ((bool?)null, "unavailable"),
            _ => ((bool?)false, "unavailable")
        };
        var outcome = Valid() with { ImportStage = stage, TargetMutationCommitted = committed,
            ImportResultState = result, TemporarySourceState = sourceState };
        Assert.True(BlockImportOutcomeValidator.Validate(outcome, SourceFormatNames.Source));
    }

    [Theory]
    [InlineData("ImportStage", "future")]
    [InlineData("ImportResultState", "future")]
    [InlineData("CompileStage", "future")]
    [InlineData("FinalReadStage", "future")]
    [InlineData("TemporarySourceState", "future")]
    [InlineData("ContentRelation", "future")]
    [InlineData("ContentRelation", "requested")]
    [InlineData("ContentRelation", "prior")]
    public void Validate_RejectsUnknownOrUnprovenClosedValue(string field, string value)
    {
        var outcome = field switch
        {
            "ImportStage" => Valid() with { ImportStage = value },
            "ImportResultState" => Valid() with { ImportResultState = value },
            "CompileStage" => Valid() with { CompileStage = value },
            "FinalReadStage" => Valid() with { FinalReadStage = value },
            "TemporarySourceState" => Valid() with { TemporarySourceState = value },
            _ => Valid() with { ContentRelation = value }
        };
        Assert.False(BlockImportOutcomeValidator.Validate(outcome, SourceFormatNames.Xml));
    }

    [Theory]
    [InlineData("not_started", false, "success")]
    [InlineData("unknown", null, "non_success")]
    [InlineData("completed", true, "unavailable")]
    [InlineData("not_started", true, "unavailable")]
    [InlineData("not_started", null, "unavailable")]
    [InlineData("unknown", false, "unavailable")]
    [InlineData("completed", false, "success")]
    public void Validate_RejectsContradictoryTargetMatrix(string stage, bool? committed, string result)
    {
        var outcome = Valid() with { ImportStage = stage, TargetMutationCommitted = committed, ImportResultState = result };
        Assert.False(BlockImportOutcomeValidator.Validate(outcome, SourceFormatNames.Xml));
    }

    [Theory]
    [InlineData("not_created")]
    [InlineData("removed")]
    [InlineData("residue_possible")]
    [InlineData("unknown")]
    public void Validate_XmlRejectsNonApplicableSourceState(string sourceState)
        => Assert.False(BlockImportOutcomeValidator.Validate(
            Valid() with { TemporarySourceState = sourceState }, SourceFormatNames.Xml));

    [Theory]
    [InlineData("not_applicable")]
    [InlineData("not_created")]
    public void Validate_SourceRejectsImpossibleLifecycle(string sourceState)
        => Assert.False(BlockImportOutcomeValidator.Validate(
            Valid() with { ImportStage = "completed", ImportResultState = "success",
                TargetMutationCommitted = true, TemporarySourceState = sourceState }, SourceFormatNames.Source));

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    public void Validate_UnnormalizedFormatRequiresUnknownSourceState(string? format)
    {
        Assert.False(BlockImportOutcomeValidator.Validate(Valid(), format));
        Assert.True(BlockImportOutcomeValidator.Validate(
            Valid() with { TemporarySourceState = "unknown" }, format));
    }

    [Fact]
    public void Validate_NotStartedCompileRejectsReport()
        => Assert.False(BlockImportOutcomeValidator.Validate(
            Valid() with { CompileReport = new CompileCheckReport() }, SourceFormatNames.Xml));

    [Fact]
    public void Validate_NotStartedFinalReadRejectsPresence()
        => Assert.False(BlockImportOutcomeValidator.Validate(
            Valid() with { TargetPresent = true }, SourceFormatNames.Xml));

    [Fact]
    public void Validate_WarningOnlyReportMaySucceed()
    {
        var report = new CompileCheckReport { OverallState = "Warning", TotalWarningCount = 2 };
        Assert.True(BlockImportOutcomeValidator.Validate(
            Completed() with { CompileStage = "succeeded", CompileReport = report }, SourceFormatNames.Xml));
    }

    [Fact]
    public void Validate_UnavailableCompileMayLackReport()
        => Assert.True(BlockImportOutcomeValidator.Validate(
            Valid() with { CompileStage = "unavailable" }, SourceFormatNames.Xml));

    [Theory]
    [InlineData("not_started", false, "unavailable")]
    [InlineData("unknown", null, "unavailable")]
    public void Validate_NonCompletedImportRejectsCompletedCompile(
        string importStage, bool? committed, string resultState)
    {
        var report = new CompileCheckReport { OverallState = "Success" };
        var outcome = Valid() with
        {
            ImportStage = importStage,
            TargetMutationCommitted = committed,
            ImportResultState = resultState,
            CompileStage = "succeeded",
            CompileReport = report
        };
        Assert.False(BlockImportOutcomeValidator.Validate(outcome, SourceFormatNames.Xml));
    }

    [Fact]
    public void Validate_NonCompletedImportRejectsPartialCompileReport()
    {
        var outcome = Valid() with
        {
            ImportStage = "unknown",
            TargetMutationCommitted = null,
            CompileStage = "unavailable",
            CompileReport = new CompileCheckReport()
        };
        Assert.False(BlockImportOutcomeValidator.Validate(outcome, SourceFormatNames.Xml));
    }

    [Theory]
    [InlineData("succeeded", "Error", 1, 0)]
    [InlineData("succeeded", "Success", 1, 0)]
    [InlineData("failed", "Success", 0, 0)]
    [InlineData("failed", "Warning", 0, 2)]
    public void Validate_RejectsCompileStageContradictingP1TotalsOrState(
        string stage, string overallState, int errors, int warnings)
    {
        var report = new CompileCheckReport
        {
            OverallState = overallState, TotalErrorCount = errors, TotalWarningCount = warnings
        };
        Assert.False(BlockImportOutcomeValidator.Validate(
            Completed() with { CompileStage = stage, CompileReport = report }, SourceFormatNames.Xml));
    }

    [Fact]
    public void Validate_RejectsExcessPlcsMessagesNotesAndDecodedFields()
    {
        var report = new CompileCheckReport();
        for (var i = 0; i < 9; i++) report.Plcs.Add(new PlcCompileInfo { PlcName = $"PLC{i}" });
        Assert.False(BlockImportOutcomeValidator.Validate(
            Completed() with { CompileStage = "succeeded", CompileReport = report }, SourceFormatNames.Xml));

        report.Plcs.RemoveAt(8);
        report.Plcs[0].Messages.AddRange(Enumerable.Range(0, 21)
            .Select(_ => new CompileMessageInfo { Description = "message" }));
        Assert.False(BlockImportOutcomeValidator.Validate(
            Completed() with { CompileStage = "succeeded", CompileReport = report }, SourceFormatNames.Xml));

        report.Plcs[0].Messages.Clear();
        report.Plcs[0].DiagnosticNotes.AddRange(Enumerable.Repeat("note", 9));
        Assert.False(BlockImportOutcomeValidator.Validate(
            Completed() with { CompileStage = "succeeded", CompileReport = report }, SourceFormatNames.Xml));

        report.Plcs[0].DiagnosticNotes.Clear();
        report.Plcs[0].PlcName = new string('x', 257);
        Assert.False(BlockImportOutcomeValidator.Validate(
            Completed() with { CompileStage = "succeeded", CompileReport = report }, SourceFormatNames.Xml));
    }

    [Fact]
    public void Validate_RejectsCombinedDiagnosticCharactersAbove1024()
    {
        var report = new CompileCheckReport();
        report.Plcs.Add(new PlcCompileInfo { Messages = Enumerable.Range(0, 5)
            .Select(_ => new CompileMessageInfo { Description = new string('x', 205) }).ToList() });
        Assert.False(BlockImportOutcomeValidator.Validate(
            Completed() with { CompileStage = "succeeded", CompileReport = report }, SourceFormatNames.Xml));
    }

    [Fact]
    public void Validate_RejectsEscapeExpandedOutcomeAbove12000SerializedCharacters()
    {
        var report = new CompileCheckReport();
        for (var i = 0; i < 8; i++)
            report.Plcs.Add(new PlcCompileInfo { PlcName = new string('"', 256),
                DeviceName = new string('"', 256) });
        var outcome = Completed() with { CompileStage = "succeeded", CompileReport = report };
        Assert.True(JsonSerializer.Serialize(outcome, TiaJson.Presentation).Length > 12_000);
        Assert.False(BlockImportOutcomeValidator.Validate(outcome, SourceFormatNames.Xml));
    }
}
