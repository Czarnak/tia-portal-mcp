using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;

namespace TiaMcpServer.Worker;

/// <summary>Rejects untrusted block-import evidence that exceeds the closed I2 contract.</summary>
public static class BlockImportOutcomeValidator
{
    private const int MaxPlcs = 8;
    private const int MaxMessages = 20;
    private const int MaxNotes = 8;
    private const int MaxFieldChars = 256;
    private const int MaxDiagnosticChars = 1_024;
    private const int MaxSerializedChars = 12_000;

    public static bool Validate(BlockImportOutcomeInfo? outcome, string? normalizedFormatOrNull)
    {
        if (outcome is null
            || !OneOf(outcome.ImportStage, "not_started", "unknown", "completed")
            || !OneOf(outcome.ImportResultState, "success", "non_success", "unavailable")
            || !OneOf(outcome.CompileStage, "not_started", "succeeded", "failed", "unavailable")
            || !OneOf(outcome.FinalReadStage, "not_started", "succeeded", "unavailable")
            || !OneOf(outcome.TemporarySourceState,
                "not_applicable", "not_created", "removed", "residue_possible", "unknown")
            || outcome.ContentRelation != "unknown")
        {
            return false;
        }

        if (outcome.ImportStage switch
            {
                "not_started" => outcome.TargetMutationCommitted != false
                    || outcome.ImportResultState != "unavailable",
                "unknown" => outcome.TargetMutationCommitted is not null
                    || outcome.ImportResultState != "unavailable",
                _ => outcome.TargetMutationCommitted != true
                    || !OneOf(outcome.ImportResultState, "success", "non_success")
            })
        {
            return false;
        }

        if (outcome.ImportStage switch
            {
                "not_started" => outcome.CompileStage != "not_started"
                    || outcome.FinalReadStage != "not_started",
                "unknown" => outcome.CompileStage != "unavailable"
                    || outcome.FinalReadStage == "not_started",
                _ => outcome.CompileStage == "not_started"
                    || outcome.FinalReadStage == "not_started"
            })
        {
            return false;
        }

        if (normalizedFormatOrNull == SourceFormatNames.Xml)
        {
            if (outcome.TemporarySourceState != "not_applicable")
                return false;
        }
        else if (normalizedFormatOrNull == SourceFormatNames.Source)
        {
            if (outcome.TemporarySourceState == "not_applicable"
                || (outcome.TemporarySourceState == "not_created"
                    && outcome.ImportStage != "not_started"))
                return false;
        }
        else if (outcome.TemporarySourceState != "unknown")
        {
            return false;
        }

        if (outcome.CompileStage == "not_started" && outcome.CompileReport is not null)
            return false;
        if (outcome.ImportStage != "completed" && outcome.CompileReport is not null)
            return false;
        if (OneOf(outcome.CompileStage, "succeeded", "failed")
            && (outcome.CompileReport is null || outcome.ImportStage != "completed"))
            return false;
        if (outcome.FinalReadStage == "not_started" && outcome.TargetPresent is not null)
            return false;
        if (outcome.FinalReadStage == "succeeded" && outcome.TargetPresent is null)
            return false;
        if (outcome.FinalReadStage == "unavailable" && outcome.TargetPresent == false)
            return false;

        if (outcome.CompileReport is { } report && !ValidReport(report, outcome.CompileStage))
            return false;

        try
        {
            return JsonSerializer.Serialize(outcome, TiaJson.Presentation).Length <= MaxSerializedChars;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException
            or ArgumentException)
        {
            return false;
        }
    }

    private static bool ValidReport(CompileCheckReport report, string stage)
    {
        if (report.Plcs is null || report.Plcs.Count > MaxPlcs
            || report.TotalErrorCount < 0 || report.TotalWarningCount < 0
            || !OneOf(report.OverallState, "Success", "Warning", "Error")
            || !ValidRequiredField(report.Scope) || !ValidOptionalField(report.BlockPath))
            return false;

        var hasErrors = report.TotalErrorCount > 0 || report.OverallState == "Error";
        if ((stage == "succeeded" && hasErrors)
            || (stage == "failed" && !hasErrors))
            return false;

        var messages = 0;
        var notes = 0;
        var diagnosticChars = 0;
        foreach (var plc in report.Plcs)
        {
            if (plc is null || plc.Messages is null || plc.DiagnosticNotes is null
                || !ValidRequiredField(plc.PlcName) || !ValidOptionalField(plc.DeviceName)
                || !ValidRequiredField(plc.State) || plc.ErrorCount < 0 || plc.WarningCount < 0)
                return false;

            messages += plc.Messages.Count;
            notes += plc.DiagnosticNotes.Count;
            if (messages > MaxMessages || notes > MaxNotes)
                return false;

            foreach (var message in plc.Messages)
            {
                if (message is null || !ValidRequiredField(message.Description)
                    || !ValidRequiredField(message.Path) || !ValidRequiredField(message.Severity))
                    return false;
                diagnosticChars += message.Description.Length + message.Path.Length;
                if (diagnosticChars > MaxDiagnosticChars)
                    return false;
            }

            foreach (var note in plc.DiagnosticNotes)
            {
                if (!ValidRequiredField(note))
                    return false;
                diagnosticChars += note.Length;
                if (diagnosticChars > MaxDiagnosticChars)
                    return false;
            }
        }

        return true;
    }

    private static bool ValidRequiredField(string? value)
        => value is not null && value.Length <= MaxFieldChars;

    private static bool ValidOptionalField(string? value)
        => value is null || ValidRequiredField(value);

    private static bool OneOf(string? value, params string[] allowed)
        => allowed.Contains(value, StringComparer.Ordinal);
}
