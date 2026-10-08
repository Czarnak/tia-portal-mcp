using System.Text.Json;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

internal static class BlockImportOutcomeProjection
{
    private const int MaxPlcs = 8;
    private const int MaxMessages = 20;
    private const int MaxNotes = 8;
    private const int MaxFieldChars = 256;
    private const int MaxDiagnosticChars = 1_024;
    private const int MaxSerializedChars = 12_000;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        // Match the host validator's TiaJson.Presentation representation: camelCase with nulls.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static BlockImportOutcomeInfo Project(BlockImportOutcomeInfo source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));

        var omitted = source.CompileDetailsOmitted;
        var report = source.CompileReport is null ? null : CopyReport(source.CompileReport, ref omitted);
        var result = CopyOutcome(source, report, omitted);

        while (JsonSerializer.Serialize(result, JsonOptions).Length > MaxSerializedChars)
        {
            if (report is null)
                throw new InvalidOperationException("Block import outcome metadata exceeds its response limit.");

            if (RemoveLastNote(report) || RemoveLastMessage(report) || ShortenMetadata(report))
            {
                omitted = true;
                result = CopyOutcome(source, report, omitted);
                continue;
            }

            if (report.Plcs.Count > 0)
            {
                report.Plcs.RemoveAt(report.Plcs.Count - 1);
                omitted = true;
                result = CopyOutcome(source, report, omitted);
                continue;
            }

            throw new InvalidOperationException("Block import outcome metadata exceeds its response limit.");
        }

        return result;
    }

    private static BlockImportOutcomeInfo CopyOutcome(
        BlockImportOutcomeInfo source,
        CompileCheckReport? report,
        bool omitted) => new BlockImportOutcomeInfo
    {
        ImportStage = source.ImportStage,
        ImportResultState = source.ImportResultState,
        TargetMutationCommitted = source.TargetMutationCommitted,
        CompileStage = source.CompileStage,
        CompileReport = report,
        CompileDetailsOmitted = omitted,
        FinalReadStage = source.FinalReadStage,
        TargetPresent = source.TargetPresent,
        ContentRelation = "unknown",
        TemporarySourceState = source.TemporarySourceState
    };

    private static CompileCheckReport CopyReport(CompileCheckReport source, ref bool omitted)
    {
        var report = new CompileCheckReport
        {
            Scope = SafeMetadata(source.Scope, required: true, ref omitted) ?? string.Empty,
            BlockPath = null,
            TotalErrorCount = source.TotalErrorCount,
            TotalWarningCount = source.TotalWarningCount,
            OverallState = SafeMetadata(source.OverallState, required: true, ref omitted) ?? string.Empty
        };
        if (source.BlockPath is not null)
            omitted = true;

        var messageCount = 0;
        var noteCount = 0;
        var diagnosticChars = 0;
        var sourcePlcs = source.Plcs ?? new List<PlcCompileInfo>();
        for (var plcIndex = 0; plcIndex < sourcePlcs.Count; plcIndex++)
        {
            if (report.Plcs.Count >= MaxPlcs)
            {
                omitted = true;
                break;
            }

            var sourcePlc = sourcePlcs[plcIndex];
            if (sourcePlc is null)
            {
                omitted = true;
                continue;
            }

            var plc = new PlcCompileInfo
            {
                PlcName = SafeMetadata(sourcePlc.PlcName, required: true, ref omitted) ?? string.Empty,
                DeviceName = SafeMetadata(sourcePlc.DeviceName, required: false, ref omitted),
                State = SafeMetadata(sourcePlc.State, required: true, ref omitted) ?? string.Empty,
                ErrorCount = sourcePlc.ErrorCount,
                WarningCount = sourcePlc.WarningCount
            };

            foreach (var sourceMessage in sourcePlc.Messages ?? new List<CompileMessageInfo>())
            {
                if (messageCount >= MaxMessages)
                {
                    omitted = true;
                    break;
                }
                if (sourceMessage is null)
                {
                    omitted = true;
                    continue;
                }

                var description = FixedDiagnostic(
                    "Compiler diagnostic omitted.",
                    ref diagnosticChars,
                    ref omitted);
                var path = FixedDiagnostic(
                    string.Empty,
                    ref diagnosticChars,
                    ref omitted);
                plc.Messages.Add(new CompileMessageInfo
                {
                    Description = description,
                    Path = path,
                    Severity = SafeMetadata(sourceMessage.Severity, required: true, ref omitted) ?? string.Empty
                });
                messageCount++;
            }

            foreach (var sourceNote in sourcePlc.DiagnosticNotes ?? new List<string>())
            {
                if (noteCount >= MaxNotes)
                {
                    omitted = true;
                    break;
                }
                _ = sourceNote;
                plc.DiagnosticNotes.Add(FixedDiagnostic(
                    "Compiler note omitted.",
                    ref diagnosticChars,
                    ref omitted));
                noteCount++;
            }

            report.Plcs.Add(plc);
        }

        if (sourcePlcs.Count > report.Plcs.Count)
            omitted = true;
        return report;
    }

    private static string? SafeMetadata(string? value, bool required, ref bool omitted)
    {
        if (value is null)
            return required ? string.Empty : null;

        var sanitized = ReplaceControls(value).Trim();
        if (sanitized.Length > MaxFieldChars)
        {
            omitted = true;
            sanitized = TakeWithoutSplittingSurrogate(sanitized, MaxFieldChars);
        }
        return sanitized;
    }

    private static string FixedDiagnostic(
        string replacement,
        ref int usedChars,
        ref bool omitted)
    {
        omitted = true;
        var sanitized = replacement;

        var remaining = MaxDiagnosticChars - usedChars;
        if (sanitized.Length > remaining)
        {
            sanitized = TakeWithoutSplittingSurrogate(sanitized, Math.Max(0, remaining));
            omitted = true;
        }

        usedChars += sanitized.Length;
        return sanitized;
    }

    private static string ReplaceControls(string value)
    {
        var chars = value.ToCharArray();
        for (var index = 0; index < chars.Length; index++)
        {
            if (char.IsControl(chars[index]))
                chars[index] = ' ';
        }
        return new string(chars);
    }

    private static string TakeWithoutSplittingSurrogate(string value, int length)
    {
        length = Math.Min(length, value.Length);
        if (length > 0 && length < value.Length
            && char.IsHighSurrogate(value[length - 1]) && char.IsLowSurrogate(value[length]))
            length--;
        return value.Substring(0, length);
    }

    private static bool RemoveLastNote(CompileCheckReport report)
    {
        for (var index = report.Plcs.Count - 1; index >= 0; index--)
        {
            var notes = report.Plcs[index].DiagnosticNotes;
            if (notes.Count == 0) continue;
            notes.RemoveAt(notes.Count - 1);
            return true;
        }
        return false;
    }

    private static bool RemoveLastMessage(CompileCheckReport report)
    {
        for (var index = report.Plcs.Count - 1; index >= 0; index--)
        {
            var messages = report.Plcs[index].Messages;
            if (messages.Count == 0) continue;
            messages.RemoveAt(messages.Count - 1);
            return true;
        }
        return false;
    }

    private static bool ShortenMetadata(CompileCheckReport report)
    {
        if (CanShorten(report.Scope))
        {
            report.Scope = Shortened(report.Scope);
            return true;
        }
        if (CanShorten(report.BlockPath))
        {
            report.BlockPath = Shortened(report.BlockPath!);
            return true;
        }

        foreach (var plc in report.Plcs)
        {
            if (CanShorten(plc.PlcName))
            {
                plc.PlcName = Shortened(plc.PlcName);
                return true;
            }
            if (CanShorten(plc.DeviceName))
            {
                plc.DeviceName = Shortened(plc.DeviceName!);
                return true;
            }
        }
        return false;
    }

    private static bool CanShorten(string? value) => value is { Length: > 32 };

    private static string Shortened(string value) =>
        TakeWithoutSplittingSurrogate(value, Math.Max(32, value.Length / 2));
}
