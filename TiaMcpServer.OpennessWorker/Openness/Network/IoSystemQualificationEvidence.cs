using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

internal static class IoSystemQualificationEvidence
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static MasterPlcAncestorSelection<TItem>? SelectMasterPlcAncestor<TItem, TSoftware>(
        IReadOnlyList<TSoftware> devicePlcSoftware, IReadOnlyList<TItem> strictAncestors,
        Func<TItem, TSoftware?> readDirectPlcSoftware, Func<TItem, bool> hasCompiler,
        Func<int, TItem?> resolveAtDepth)
        where TItem : class where TSoftware : class
    {
        if (devicePlcSoftware is null || devicePlcSoftware.Count != 1 || devicePlcSoftware[0] is null
            || strictAncestors is null || readDirectPlcSoftware is null || hasCompiler is null
            || resolveAtDepth is null)
            return null;
        try
        {
            for (var index = strictAncestors.Count - 1; index >= 0; index--)
            {
                var item = strictAncestors[index];
                if (item is null) return null;
                var software = readDirectPlcSoftware(item);
                if (software is null) continue;
                if (!object.Equals(software, devicePlcSoftware[0]) || !hasCompiler(item))
                    return null;
                var resolved = resolveAtDepth(index + 1);
                return resolved is not null && object.Equals(resolved, item)
                    ? new MasterPlcAncestorSelection<TItem>(resolved, index + 1) : null;
            }
        }
        catch (Exception) { /* Unreadable identity, role, or service is never selection proof. */ }
        return null;
    }

    public static bool SameIndexedDeviceItemPath(NetworkObjectSelectorInfo original,
        NetworkObjectSelectorInfo current)
    {
        if (original is null || current is null
            || original.Kind != NetworkObjectKinds.DeviceItem || current.Kind != NetworkObjectKinds.DeviceItem
            || original.ItemPath is null || current.ItemPath is null
            || original.ItemPath.Count != current.ItemPath.Count)
            return false;
        for (var index = 0; index < original.ItemPath.Count; index++)
        {
            var before = original.ItemPath[index];
            var after = current.ItemPath[index];
            if (before.Index != after.Index || before.PositionNumber != after.PositionNumber
                || !string.Equals(before.TypeIdentifier, after.TypeIdentifier, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

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
        if (structuralLocator.StartsWith("ungroupedDevices/", StringComparison.Ordinal)) return "ungrouped";
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

    public static string? ClassifyPnAssociation<TSystem>(IEnumerable<TSystem?> controllerSystems,
        IEnumerable<TSystem?> connectorSystems, TSystem target) where TSystem : class
    {
        if (controllerSystems is null || connectorSystems is null || target is null)
            throw new InvalidOperationException("IO-system association evidence is incomplete.");
        var controllerMatches = 0;
        var connectorMatches = 0;
        try
        {
            foreach (var system in controllerSystems)
                if (system is not null && object.Equals(system, target)) controllerMatches++;
            foreach (var system in connectorSystems)
                if (system is not null && object.Equals(system, target)) connectorMatches++;
        }
        catch (Exception)
        {
            throw new InvalidOperationException("IO-system association could not be verified.");
        }
        if (controllerMatches + connectorMatches > 1)
            throw new InvalidOperationException("IO-system association is ambiguous.");
        return controllerMatches == 1 ? "controller" : connectorMatches == 1 ? "connector" : null;
    }

    public static (bool Available, string? Value) ObservePnDeviceName(int metadataCount,
        bool supportsString, Func<object?> readValue)
    {
        if (metadataCount == 0) return (false, null);
        if (metadataCount != 1 || !supportsString || readValue is null)
            throw new InvalidOperationException("PN device-name metadata is ambiguous or unsupported.");
        if (readValue() is not string value)
            throw new InvalidOperationException("PN device-name value is unreadable or not a string.");
        return (true, value);
    }

    public static void RequireLinkedPnNodes(int nodeCount)
    {
        if (nodeCount <= 0)
            throw new InvalidOperationException("A linked PN interface has no nodes to observe.");
    }

    public static void ValidatePnDeviceNameSnapshot(IReadOnlyList<IoSystemQualificationPnDeviceNameInfo> nodes,
        bool isProfinet)
    {
        if (nodes is null || nodes.Count > 128 || (isProfinet && nodes.Count == 0)
            || (!isProfinet && nodes.Count != 0))
            throw new InvalidOperationException("PN device-name snapshot is incomplete or too large.");
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (node is null || string.IsNullOrWhiteSpace(node.DeviceLocator)
                || string.IsNullOrWhiteSpace(node.DeviceName) || string.IsNullOrWhiteSpace(node.NodeId)
                || (node.AssociationKind != "controller" && node.AssociationKind != "connector")
                || node.ItemPath is null || node.ItemPath.Count is < 1 or > 16
                || node.Available != (node.Value is not null))
                throw new InvalidOperationException("PN device-name snapshot contains incomplete node evidence.");
            var path = SummarizeOwnerPath(node.DeviceName, node.ItemPath);
            if (path.BlankNameCount != 0 || path.WhitespaceTypeIdentifierCount != 0
                || path.NegativePositionCount != 0 || path.NegativeIndexCount != 0)
                throw new InvalidOperationException("PN device-name snapshot contains an incomplete item path.");
            var identity = JsonSerializer.Serialize(new
            {
                node.DeviceLocator,
                ItemPathIndices = node.ItemPath.Select(segment => segment.Index).ToArray(),
                node.NodeId
            }, JsonOptions);
            if (!identities.Add(identity))
                throw new InvalidOperationException("PN device-name snapshot contains a duplicate node identity.");
        }
        if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(nodes, JsonOptions)) > 16384)
            throw new InvalidOperationException("PN device-name snapshot exceeds its evidence limit.");
    }

    public static bool FitsResultBudget(IoSystemQualificationResultInfo result)
        => Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(result, JsonOptions)) <= 65536;

    public static bool SamePnNodeIdentities(IReadOnlyList<IoSystemQualificationPnDeviceNameInfo> before,
        IReadOnlyList<IoSystemQualificationPnDeviceNameInfo> after)
    {
        if (before is null || after is null || before.Count != after.Count) return false;
        var identities = new HashSet<string>(before.Select(PnNodeIdentity), StringComparer.Ordinal);
        return identities.Count == before.Count && after.All(node => identities.Remove(PnNodeIdentity(node)))
            && identities.Count == 0;
    }

    private static string PnNodeIdentity(IoSystemQualificationPnDeviceNameInfo node)
        => JsonSerializer.Serialize(new
        {
            node.DeviceLocator,
            ItemPathIndices = node.ItemPath.Select(segment => segment.Index).ToArray(),
            node.NodeId,
            node.AssociationKind
        }, JsonOptions);

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
        if (result.Messages.Count >= 32) { result.OmittedMessageCount++; result.EvidenceOmitted = true; return; }
        // Descriptions are untrusted project data. Redact credential assignments and filesystem paths
        // before truncation so a clipped prefix cannot evade recognition.
        text = Regex.Replace(text, @"(?i)\b(password|passwd|token|secret|api[_-]?key|authorization)\s*[:=]\s*(""[^""]*""|'[^']*'|[^\s,;]+)", "$1=[redacted]");
        text = Regex.Replace(text, @"(?i)(?:[a-z]:[\\/]|\\\\)[^\r\n,;<>]*", "[path redacted]");
        text = Regex.Replace(text, @"(?<![\w:])/(?:[^\s,;<>]+/)*[^\s,;<>]+", "[path redacted]");
        if (text.Length > 512) result.EvidenceOmitted = true;
        result.Messages.Add(text.Length <= 512 ? text : text.Substring(0, 512));
    }

    public static string SerializeBounded(IoSystemQualificationResultInfo result)
    {
        var serialized = JsonSerializer.Serialize(result, JsonOptions);
        while (Encoding.UTF8.GetByteCount(serialized) > 65536 && result.Messages.Count > 0)
        {
            result.Messages.RemoveAt(result.Messages.Count - 1);
            result.OmittedMessageCount++;
            result.EvidenceOmitted = true;
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

internal sealed class MasterPlcAncestorSelection<TItem> where TItem : class
{
    public MasterPlcAncestorSelection(TItem item, int depth)
    { Item = item; Depth = depth; }
    public TItem Item { get; }
    public int Depth { get; }
}
