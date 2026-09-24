using System;
using System.Linq;
using System.Text;
using System.Text.Json;
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
        result.Messages.Add(text.Length <= 512 ? text : text.Substring(0, 512));
    }

    public static string SerializeBounded(IoSystemQualificationResultInfo result)
    {
        var serialized = JsonSerializer.Serialize(result, JsonOptions);
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
