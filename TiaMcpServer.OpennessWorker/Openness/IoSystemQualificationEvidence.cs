using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

internal static class IoSystemQualificationEvidence
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string? ValidateChange(IoSystemQualificationAttributeInfo observed, IoSystemQualificationProbeInfo request)
    {
        var expected = request.ExpectedValue!;
        var clrType = expected.Kind switch { "string" => "System.String", "integer" => "System.Int32", "boolean" => "System.Boolean", _ => "" };
        if (!observed.Available || !observed.Writable || observed.Name != request.AttributeName
            || !observed.SupportedTypes.Contains(clrType, StringComparer.Ordinal))
            return "The exact attribute must be readable, writable, and support the requested CLR type.";
        if (!Equal(observed.Value, expected)) return "The current attribute value does not match the expected value.";
        return null;
    }

    public static bool Equal(IoSystemQualificationScalarInfo? left, IoSystemQualificationScalarInfo? right)
        => left is not null && right is not null && left.Kind == right.Kind
            && left.StringValue == right.StringValue && left.IntegerValue == right.IntegerValue && left.BooleanValue == right.BooleanValue;

    public static void AddMessage(IoSystemQualificationResultInfo result, string text)
    {
        if (result.Messages.Count >= 32) { result.OmittedMessageCount++; return; }
        // Descriptions are untrusted project data. Redact credential assignments and filesystem paths
        // before truncation so a clipped prefix cannot evade recognition.
        text = Regex.Replace(text, @"(?i)\b(password|passwd|token|secret|api[_-]?key|authorization)\s*[:=]\s*(""[^""]*""|'[^']*'|[^\s,;]+)", "$1=[redacted]");
        text = Regex.Replace(text, @"(?i)(?:[a-z]:[\\/]|\\\\)[^\r\n,;<>]*", "[path redacted]");
        text = Regex.Replace(text, @"(?<![\w:])/(?:[^\s,;<>]+/)*[^\s,;<>]+", "[path redacted]");
        result.Messages.Add(text.Length <= 512 ? text : text.Substring(0, 512));
    }

    public static string SerializeBounded(IoSystemQualificationResultInfo result)
    {
        var serialized = JsonSerializer.Serialize(result, JsonOptions);
        while (Encoding.UTF8.GetByteCount(serialized) > 65536 && result.Messages.Count > 0)
        {
            result.Messages.RemoveAt(result.Messages.Count - 1);
            result.OmittedMessageCount++;
            serialized = JsonSerializer.Serialize(result, JsonOptions);
        }
        if (Encoding.UTF8.GetByteCount(serialized) <= 65536) return serialized;
        // Do not truncate selector/value identity: discard detailed evidence and require fresh inspection.
        var bounded = new IoSystemQualificationResultInfo
        {
            Mode = result.Mode, MutationCommitted = result.MutationCommitted,
            CompileState = result.CompileState, ErrorCount = result.ErrorCount, WarningCount = result.WarningCount,
            EvidenceOmitted = true, OmittedMessageCount = result.OmittedMessageCount + result.Messages.Count,
            RestorationGuidance = "Evidence exceeded the result limit. Inspect current state before any restoration; never retry blindly."
        };
        return JsonSerializer.Serialize(bounded, JsonOptions);
    }
}
