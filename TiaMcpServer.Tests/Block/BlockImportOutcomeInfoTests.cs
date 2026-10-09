using System.Text.Json;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
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
        TargetMutationCommitted = true,
        CompileStage = "unavailable",
        FinalReadStage = "unavailable"
    };

    private static BlockImportOutcomeInfo Unknown() => Valid() with
    {
        ImportStage = "unknown",
        TargetMutationCommitted = null,
        CompileStage = "unavailable",
        FinalReadStage = "unavailable"
    };

    private static BlockImportOutcomeInfo AtImportStage(string stage) => stage switch
    {
        "completed" => Completed(),
        "unknown" => Unknown(),
        _ => Valid()
    };

    private static CompileCheckReport CompleteReport() => new()
    {
        Scope = "plc",
        OverallState = "Success",
        Plcs = new List<PlcCompileInfo>
        {
            new()
            {
                PlcName = "PLC_1",
                State = "Success",
                Messages = new List<CompileMessageInfo>
                {
                    new() { Description = "checked", Path = "Blocks/FB1", Severity = "Information" }
                },
                DiagnosticNotes = new List<string> { "available" }
            }
        }
    };

    [Theory]
    [InlineData("report.scope")]
    [InlineData("plc.plcName")]
    [InlineData("plc.state")]
    [InlineData("message.description")]
    [InlineData("message.path")]
    [InlineData("message.severity")]
    [InlineData("plc.diagnosticNotes[0]")]
    public void Validate_MalformedWorkerJson_NullRequiredReportStringReturnsFalse(string field)
    {
        var report = CompleteReport();
        var plc = report.Plcs[0];
        var message = plc.Messages[0];
        switch (field)
        {
            case "report.scope": report.Scope = null!; break;
            case "plc.plcName": plc.PlcName = null!; break;
            case "plc.state": plc.State = null!; break;
            case "message.description": message.Description = null!; break;
            case "message.path": message.Path = null!; break;
            case "message.severity": message.Severity = null!; break;
            case "plc.diagnosticNotes[0]": plc.DiagnosticNotes[0] = null!; break;
        }

        var wireOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        var wireJson = JsonSerializer.Serialize(report, wireOptions);
        var wireReport = JsonSerializer.Deserialize<CompileCheckReport>(wireJson, wireOptions)!;
        var outcome = Completed() with { CompileStage = "succeeded", CompileReport = wireReport };
        Assert.False(BlockImportOutcomeValidator.Validate(outcome, SourceFormatNames.Xml));
    }

    [Fact]
    public void Validate_NullOptionalBlockPathAndDeviceNameAreAccepted()
    {
        var report = CompleteReport();
        report.BlockPath = null;
        report.Plcs[0].DeviceName = null;
        Assert.True(BlockImportOutcomeValidator.Validate(
            Completed() with { CompileStage = "succeeded", CompileReport = report },
            SourceFormatNames.Xml));
    }

    [Theory]
    [InlineData("not_started", false, "unavailable")]
    [InlineData("unknown", null, "unavailable")]
    [InlineData("completed", true, "success")]
    [InlineData("completed", true, "non_success")]
    public void Validate_AcceptsClosedTargetMatrix(string stage, bool? committed, string result)
    {
        var outcome = AtImportStage(stage) with { TargetMutationCommitted = committed, ImportResultState = result };
        Assert.True(BlockImportOutcomeValidator.Validate(outcome, SourceFormatNames.Xml));
    }

    [Theory]
    [InlineData("not_started", "unavailable", "not_started")]
    [InlineData("not_started", "not_started", "unavailable")]
    [InlineData("unknown", "not_started", "unavailable")]
    [InlineData("unknown", "unavailable", "not_started")]
    [InlineData("completed", "not_started", "unavailable")]
    [InlineData("completed", "unavailable", "not_started")]
    public void Validate_RejectsCrossStageNotStartedOrUnavailableContradictions(
        string importStage, string compileStage, string finalReadStage)
    {
        var outcome = AtImportStage(importStage) with
        {
            CompileStage = compileStage,
            FinalReadStage = finalReadStage
        };
        Assert.False(BlockImportOutcomeValidator.Validate(outcome, SourceFormatNames.Xml));
    }

    [Theory]
    [InlineData("succeeded")]
    [InlineData("failed")]
    public void Validate_UnknownImportRejectsObservedCompileCompletion(string compileStage)
    {
        var report = CompleteReport();
        if (compileStage == "failed")
        {
            report.OverallState = "Error";
            report.TotalErrorCount = 1;
        }
        var outcome = Unknown() with { CompileStage = compileStage, CompileReport = report };
        Assert.False(BlockImportOutcomeValidator.Validate(outcome, SourceFormatNames.Xml));
    }

    [Fact]
    public void Validate_UnavailableFinalReadRejectsObservedAbsence()
        => Assert.False(BlockImportOutcomeValidator.Validate(
            Completed() with { TargetPresent = false }, SourceFormatNames.Xml));

    [Theory]
    [InlineData("unknown", "unavailable", null)]
    [InlineData("unknown", "unavailable", true)]
    [InlineData("unknown", "succeeded", true)]
    [InlineData("unknown", "succeeded", false)]
    [InlineData("completed", "unavailable", null)]
    [InlineData("completed", "unavailable", true)]
    [InlineData("completed", "succeeded", true)]
    [InlineData("completed", "succeeded", false)]
    public void Validate_AcceptsPostImportFinalReadEvidence(
        string importStage, string finalReadStage, bool? targetPresent)
    {
        var outcome = AtImportStage(importStage) with
        {
            FinalReadStage = finalReadStage,
            TargetPresent = targetPresent
        };
        Assert.True(BlockImportOutcomeValidator.Validate(outcome, SourceFormatNames.Xml));
    }

    [Theory]
    [InlineData("succeeded", "Success", 0)]
    [InlineData("failed", "Error", 1)]
    public void Validate_CompletedImportAcceptsObservedCompileResult(
        string compileStage, string overallState, int errors)
    {
        var report = CompleteReport();
        report.OverallState = overallState;
        report.TotalErrorCount = errors;
        var outcome = Completed() with { CompileStage = compileStage, CompileReport = report };
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
        var outcome = AtImportStage(stage) with { TargetMutationCommitted = committed,
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
        var outcome = AtImportStage(stage) with { TargetMutationCommitted = committed, ImportResultState = result };
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
            Completed() with { TemporarySourceState = sourceState }, SourceFormatNames.Source));

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
            Completed(), SourceFormatNames.Xml));

    [Theory]
    [InlineData("not_started", false, "unavailable")]
    [InlineData("unknown", null, "unavailable")]
    public void Validate_NonCompletedImportRejectsCompletedCompile(
        string importStage, bool? committed, string resultState)
    {
        var report = new CompileCheckReport { OverallState = "Success" };
        var outcome = AtImportStage(importStage) with
        {
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
        var outcome = Unknown() with
        {
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
