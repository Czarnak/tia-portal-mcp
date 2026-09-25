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

    public static IoSystemQualificationOwnerDiagnosticInfo InspectOwner<T>(
        Action<System.Collections.Generic.List<T>> collectMatches,
        Func<T, IoSystemQualificationOwnerPathEvidenceInfo> readPath,
        Func<T, bool> verifyIdentity,
        IoSystemQualificationOwnerDiagnosticInfo? diagnostic = null)
    {
        diagnostic ??= new IoSystemQualificationOwnerDiagnosticInfo();
        var matches = new System.Collections.Generic.List<T>();
        diagnostic.Stage = "traversal";
        try
        {
            collectMatches(matches);
            diagnostic.TraversalCompleted = true;
            diagnostic.MatchCount = matches.Count;
            diagnostic.Stage = "matching";
            if (matches.Count != 1)
            {
                diagnostic.Reason = matches.Count == 0 ? "no_matches" : "multiple_matches";
                return diagnostic;
            }
            diagnostic.Stage = "pathEvidence";
            var path = readPath(matches[0]);
            diagnostic.Path = path;
            if (path.Depth == 0 || path.BlankDeviceNameCount != 0 || path.BlankNameCount != 0
                || path.WhitespaceTypeIdentifierCount != 0
                || path.NegativePositionCount != 0 || path.NegativeIndexCount != 0)
            {
                diagnostic.Reason = "incomplete_path";
                return diagnostic;
            }
            diagnostic.Stage = "verification";
            diagnostic.Reason = verifyIdentity(matches[0]) ? "verified" : "identity_unverified";
        }
        catch (Exception)
        {
            diagnostic.MatchCount = matches.Count;
            diagnostic.Reason = diagnostic.Stage switch
            {
                "traversal" => "traversal_failed",
                "pathEvidence" => "path_read_failed",
                _ => "verification_failed"
            };
        }
        return diagnostic;
    }

    public static IoSystemQualificationOwnerPathEvidenceInfo SummarizeOwnerPath(
        string deviceName, System.Collections.Generic.IReadOnlyList<DeviceItemPathSegmentInfo> path)
    {
        var nullTypes = path.Count(segment => segment.TypeIdentifier is null);
        var emptyTypes = path.Count(segment => segment.TypeIdentifier?.Length == 0);
        var whitespaceTypes = path.Count(segment => segment.TypeIdentifier is { Length: > 0 }
            && string.IsNullOrWhiteSpace(segment.TypeIdentifier));
        return new()
        {
            Depth = path.Count,
            BlankDeviceNameCount = string.IsNullOrWhiteSpace(deviceName) ? 1 : 0,
            BlankNameCount = path.Count(segment => string.IsNullOrWhiteSpace(segment.Name)),
            NullTypeIdentifierCount = nullTypes,
            EmptyTypeIdentifierCount = emptyTypes,
            WhitespaceTypeIdentifierCount = whitespaceTypes,
            BlankTypeIdentifierCount = nullTypes + emptyTypes + whitespaceTypes,
            NegativePositionCount = path.Count(segment => segment.PositionNumber < 0),
            NegativeIndexCount = path.Count(segment => segment.Index < 0)
        };
    }

    public static string ClassifyDeviceLocation(string structuralLocator)
    {
        if (structuralLocator.StartsWith("devices/", StringComparison.Ordinal)) return "direct";
        if (structuralLocator.StartsWith("deviceGroups/", StringComparison.Ordinal)) return "grouped";
        return "unknown";
    }

    public static int CountDirectDeviceNameMatches(
        System.Collections.Generic.IEnumerable<string?> directDeviceNames, string requestedName)
        => directDeviceNames.Count(name => string.Equals(name, requestedName, StringComparison.OrdinalIgnoreCase));

    public static void RecordOwnerResolution(object candidate, object? resolved,
        IoSystemQualificationOwnerDiagnosticInfo diagnostic)
    {
        diagnostic.ResolvedObjectEqualsCandidate = null;
        if (resolved is null)
        {
            diagnostic.ResolverOutcome = "unresolved";
            return;
        }
        if (ReferenceEquals(resolved, candidate))
        {
            diagnostic.ResolverOutcome = "same_reference";
            return;
        }
        diagnostic.ResolverOutcome = "different_reference";
        try { diagnostic.ResolvedObjectEqualsCandidate = object.Equals(resolved, candidate); }
        catch (Exception) { /* Equality is optional diagnostic evidence, never owner proof. */ }
    }

    public static bool VerifyResolvedOwner<TItem, TSystem>(TItem? verified, TItem candidate,
        TSystem target, Func<TItem, System.Collections.Generic.IEnumerable<TSystem>?> readControllerSystems)
        where TItem : class where TSystem : class
    {
        // Siemens Openness may return different CLR wrappers for the same TIA object.
        // The fresh indexed-path resolution must still identify the discovered owner.
        if (verified is null || !object.Equals(verified, candidate)) return false;
        var systems = readControllerSystems(verified);
        if (systems is null) return false;
        var matchingLinks = 0;
        foreach (var system in systems)
        {
            if (system is null || !object.Equals(system, target)) continue;
            matchingLinks++;
            if (matchingLinks > 1) return false;
        }
        return matchingLinks == 1;
    }

    public static WorkerResponse NormalizeSessionResponse(WorkerResponse response, string mode)
    {
        if (response.Success) return response;

        // Execute can already have converted an exception into a response. Do not forward its
        // error, warnings, or payload as qualification diagnostics; retain session-binding metadata.
        return new WorkerResponse
        {
            Success = false,
            FailureCategory = response.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
            Error = string.Equals(mode, "setAndCompile", StringComparison.Ordinal)
                ? "IO-system qualification could not complete. The requested mutation may have committed; inspect current state before restoration or retry."
                : "IO-system qualification could not complete. Refresh session, target, owner, and metadata evidence; inspect current state before retry.",
            ProtocolVersion = response.ProtocolVersion,
            ResolvedProjectPath = response.ResolvedProjectPath,
            SessionIdentity = response.SessionIdentity
        };
    }

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
