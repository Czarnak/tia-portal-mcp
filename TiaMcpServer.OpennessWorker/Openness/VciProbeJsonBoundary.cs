using System;
using System.Collections.Generic;
using System.Text.Json;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Strict, vendor-free JSON validation boundary for the internal <c>probe_vci_read_contract</c>
/// worker operation. Runs before normal (permissive, case-insensitive) <see cref="WorkerRequest"/>
/// deserialization and rejects malformed VCI probe arguments with a deterministic message.
///
/// <para>
/// Applies ONLY to requests whose <c>method</c> value case-insensitively equals
/// <see cref="VciReadProbeContract.OperationName"/>. Every other request — including one that
/// happens to carry an unrelated <c>vciProbe</c>-shaped payload under a different method, or the
/// usual write flags (<c>confirm</c>, <c>allowTiaConfirmations</c>) — passes through untouched:
/// <see cref="Validate"/> returns <see langword="null"/>, and every existing worker operation
/// keeps its current permissive JSON behavior.
/// </para>
///
/// <para>
/// Pure System.Text.Json: no Siemens dependency. That is why this file lives in
/// <c>TiaMcpServer.OpennessWorker/Openness</c> but is also linked directly into
/// <c>TiaMcpServer.Tests</c> — it can run without pulling in the net48 Openness worker build.
/// </para>
/// </summary>
public static class VciProbeJsonBoundary
{
    private static readonly HashSet<string> RootAllowedFields =
        new HashSet<string>(StringComparer.Ordinal) { "method", "projectPath", "vciProbe" };

    private static readonly HashSet<string> ProbeAllowedFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "schemaVersion", "runId", "sessionId", "caseId", "caseInstanceId", "targetName",
        "workspace", "engineeringObject", "secondaryProjectPath",
        "maxGroupDepth", "maxGroups", "maxWorkspaces", "maxMappings",
        "maxEngineeringObjects", "maxCollectionItems",
    };

    private static readonly HashSet<string> WorkspaceSelectorAllowedFields =
        new HashSet<string>(StringComparer.Ordinal) { "groupPath", "workspaceName", "canonicalRootPath" };

    private static readonly HashSet<string> GroupPathSegmentAllowedFields =
        new HashSet<string>(StringComparer.Ordinal) { "index", "name", "sameNameOrdinal" };

    private static readonly HashSet<string> EngineeringObjectSelectorAllowedFields =
        new HashSet<string>(StringComparer.Ordinal) { "stableIdentifier", "structuralPath", "fingerprint" };

    private static readonly HashSet<string> StructuralPathSegmentAllowedFields =
        new HashSet<string>(StringComparer.Ordinal) { "index", "name", "objectType" };

    private static readonly HashSet<string> RequiredProbeStringFields = new HashSet<string>(
        StringComparer.Ordinal) { "schemaVersion", "runId", "sessionId", "caseId", "caseInstanceId" };

    private static readonly HashSet<string> NullableProbeStringFields =
        new HashSet<string>(StringComparer.Ordinal) { "targetName", "secondaryProjectPath" };

    private static readonly HashSet<string> ProbeIntFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "maxGroupDepth", "maxGroups", "maxWorkspaces", "maxMappings",
        "maxEngineeringObjects", "maxCollectionItems",
    };

    private static readonly JsonSerializerOptions StrictDeserializeOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
    };

    /// <summary>
    /// Validates one raw worker-request JSON line. Returns a deterministic, non-null rejection
    /// message when the request targets <c>probe_vci_read_contract</c> and is malformed; returns
    /// <see langword="null"/> when the request is well-formed OR does not target this operation.
    /// Never throws.
    /// </summary>
    public static string? Validate(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json!);
        }
        catch (JsonException)
        {
            // Malformed JSON is not this boundary's concern to diagnose — normal deserialization
            // downstream will fail on it with its own message.
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!TargetsVciReadProbe(root))
            {
                return null;
            }

            return ValidateProbeEnvelope(root);
        }
    }

    private static string? ValidateProbeEnvelope(JsonElement root)
    {
        var rootFieldIssue = CheckFields(root, RootAllowedFields, "root");
        if (rootFieldIssue is not null)
        {
            return rootFieldIssue;
        }

        if (TryGetCaseSensitive(root, "projectPath", out var projectPathElement)
            && projectPathElement.ValueKind != JsonValueKind.String
            && projectPathElement.ValueKind != JsonValueKind.Null)
        {
            return "'projectPath' must be a string or JSON null.";
        }

        if (!TryGetCaseSensitive(root, "vciProbe", out var vciProbeElement))
        {
            return "'vciProbe' is required.";
        }

        if (vciProbeElement.ValueKind != JsonValueKind.Object)
        {
            return $"vciProbe must be an object; found {DescribeKind(vciProbeElement)}.";
        }

        var probeIssue = ValidateProbeObject(vciProbeElement);
        if (probeIssue is not null)
        {
            return probeIssue;
        }

        var request = JsonSerializer.Deserialize<VciProbeRequestInfo>(
            vciProbeElement.GetRawText(), StrictDeserializeOptions);
        if (request is null)
        {
            return "vciProbe must not be JSON null.";
        }

        return VciReadProbeContract.Validate(request);
    }

    private static string? ValidateProbeObject(JsonElement vciProbe)
    {
        var fieldIssue = CheckFields(vciProbe, ProbeAllowedFields, "vciProbe");
        if (fieldIssue is not null)
        {
            return fieldIssue;
        }

        foreach (var field in RequiredProbeStringFields)
        {
            if (!TryGetCaseSensitive(vciProbe, field, out var element))
            {
                continue; // Absence is a structurally valid shape; VciReadProbeContract flags it semantically.
            }

            if (element.ValueKind == JsonValueKind.Null)
            {
                return $"'vciProbe.{field}' must be a string; explicit JSON null is not permitted.";
            }

            if (element.ValueKind != JsonValueKind.String)
            {
                return $"'vciProbe.{field}' must be a string.";
            }
        }

        foreach (var field in NullableProbeStringFields)
        {
            if (TryGetCaseSensitive(vciProbe, field, out var element)
                && element.ValueKind != JsonValueKind.String
                && element.ValueKind != JsonValueKind.Null)
            {
                return $"'vciProbe.{field}' must be a string or JSON null.";
            }
        }

        foreach (var field in ProbeIntFields)
        {
            if (!TryGetCaseSensitive(vciProbe, field, out var element))
            {
                continue;
            }

            if (element.ValueKind == JsonValueKind.Null)
            {
                return $"'vciProbe.{field}' must be an integer; explicit JSON null is not permitted.";
            }

            if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out _))
            {
                return $"'vciProbe.{field}' must be an integer.";
            }
        }

        if (TryGetCaseSensitive(vciProbe, "workspace", out var workspaceElement)
            && workspaceElement.ValueKind != JsonValueKind.Null)
        {
            var issue = ValidateWorkspaceSelector(workspaceElement);
            if (issue is not null)
            {
                return issue;
            }
        }

        if (TryGetCaseSensitive(vciProbe, "engineeringObject", out var engineeringObjectElement)
            && engineeringObjectElement.ValueKind != JsonValueKind.Null)
        {
            var issue = ValidateEngineeringObjectSelector(engineeringObjectElement);
            if (issue is not null)
            {
                return issue;
            }
        }

        return null;
    }

    private static string? ValidateWorkspaceSelector(JsonElement workspace)
    {
        if (workspace.ValueKind != JsonValueKind.Object)
        {
            return $"vciProbe.workspace must be an object; found {DescribeKind(workspace)}.";
        }

        var fieldIssue = CheckFields(workspace, WorkspaceSelectorAllowedFields, "vciProbe.workspace");
        if (fieldIssue is not null)
        {
            return fieldIssue;
        }

        if (TryGetCaseSensitive(workspace, "workspaceName", out var nameElement)
            && nameElement.ValueKind != JsonValueKind.String)
        {
            return "'vciProbe.workspace.workspaceName' must be a string.";
        }

        if (TryGetCaseSensitive(workspace, "canonicalRootPath", out var rootPathElement)
            && rootPathElement.ValueKind != JsonValueKind.String
            && rootPathElement.ValueKind != JsonValueKind.Null)
        {
            return "'vciProbe.workspace.canonicalRootPath' must be a string or JSON null.";
        }

        if (!TryGetCaseSensitive(workspace, "groupPath", out var groupPathElement))
        {
            return null;
        }

        if (groupPathElement.ValueKind != JsonValueKind.Array)
        {
            return $"vciProbe.workspace.groupPath must be an array; found {DescribeKind(groupPathElement)}.";
        }

        foreach (var segment in groupPathElement.EnumerateArray())
        {
            if (segment.ValueKind != JsonValueKind.Object)
            {
                return $"vciProbe.workspace.groupPath[] must contain only objects; found {DescribeKind(segment)}.";
            }

            var segmentIssue = CheckFields(
                segment, GroupPathSegmentAllowedFields, "vciProbe.workspace.groupPath[]");
            if (segmentIssue is not null)
            {
                return segmentIssue;
            }

            if (TryGetCaseSensitive(segment, "index", out var indexElement)
                && (indexElement.ValueKind != JsonValueKind.Number || !indexElement.TryGetInt32(out _)))
            {
                return "'vciProbe.workspace.groupPath[].index' must be an integer.";
            }

            if (TryGetCaseSensitive(segment, "name", out var segmentNameElement)
                && segmentNameElement.ValueKind != JsonValueKind.String)
            {
                return "'vciProbe.workspace.groupPath[].name' must be a string.";
            }

            if (TryGetCaseSensitive(segment, "sameNameOrdinal", out var sameNameOrdinalElement)
                && (sameNameOrdinalElement.ValueKind != JsonValueKind.Number
                    || !sameNameOrdinalElement.TryGetInt32(out _)))
            {
                return "'vciProbe.workspace.groupPath[].sameNameOrdinal' must be an integer.";
            }
        }

        return null;
    }

    private static string? ValidateEngineeringObjectSelector(JsonElement engineeringObject)
    {
        if (engineeringObject.ValueKind != JsonValueKind.Object)
        {
            return $"vciProbe.engineeringObject must be an object; found {DescribeKind(engineeringObject)}.";
        }

        var fieldIssue = CheckFields(
            engineeringObject, EngineeringObjectSelectorAllowedFields, "vciProbe.engineeringObject");
        if (fieldIssue is not null)
        {
            return fieldIssue;
        }

        if (TryGetCaseSensitive(engineeringObject, "stableIdentifier", out var stableIdElement)
            && stableIdElement.ValueKind != JsonValueKind.String
            && stableIdElement.ValueKind != JsonValueKind.Null)
        {
            return "'vciProbe.engineeringObject.stableIdentifier' must be a string or JSON null.";
        }

        if (TryGetCaseSensitive(engineeringObject, "fingerprint", out var fingerprintElement)
            && fingerprintElement.ValueKind != JsonValueKind.String
            && fingerprintElement.ValueKind != JsonValueKind.Null)
        {
            return "'vciProbe.engineeringObject.fingerprint' must be a string or JSON null.";
        }

        if (!TryGetCaseSensitive(engineeringObject, "structuralPath", out var structuralPathElement))
        {
            return null;
        }

        if (structuralPathElement.ValueKind != JsonValueKind.Array)
        {
            return "vciProbe.engineeringObject.structuralPath must be an array; found "
                + $"{DescribeKind(structuralPathElement)}.";
        }

        foreach (var segment in structuralPathElement.EnumerateArray())
        {
            if (segment.ValueKind != JsonValueKind.Object)
            {
                return "vciProbe.engineeringObject.structuralPath[] must contain only objects; found "
                    + $"{DescribeKind(segment)}.";
            }

            var segmentIssue = CheckFields(
                segment, StructuralPathSegmentAllowedFields, "vciProbe.engineeringObject.structuralPath[]");
            if (segmentIssue is not null)
            {
                return segmentIssue;
            }

            if (TryGetCaseSensitive(segment, "index", out var indexElement)
                && (indexElement.ValueKind != JsonValueKind.Number || !indexElement.TryGetInt32(out _)))
            {
                return "'vciProbe.engineeringObject.structuralPath[].index' must be an integer.";
            }

            if (TryGetCaseSensitive(segment, "name", out var segmentNameElement)
                && segmentNameElement.ValueKind != JsonValueKind.String)
            {
                return "'vciProbe.engineeringObject.structuralPath[].name' must be a string.";
            }

            if (TryGetCaseSensitive(segment, "objectType", out var objectTypeElement)
                && objectTypeElement.ValueKind != JsonValueKind.String)
            {
                return "'vciProbe.engineeringObject.structuralPath[].objectType' must be a string.";
            }
        }

        return null;
    }

    /// <summary>
    /// Detects case-insensitive duplicate keys and rejects any key outside
    /// <paramref name="allowedFields"/> (exact case). Runs BEFORE any type-shape validation of
    /// individual field values at this object level, and before deserialization.
    /// </summary>
    private static string? CheckFields(JsonElement obj, HashSet<string> allowedFields, string contextLabel)
    {
        // Pass 1: case-insensitive duplicate detection runs to completion BEFORE any type/shape
        // validation and before deserialization, regardless of exact spelling of either occurrence.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in obj.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                return $"Duplicate {contextLabel} field '{property.Name}' (case-insensitive collision).";
            }
        }

        // Pass 2: every surviving (non-duplicate) key must match an allowed field exactly.
        foreach (var property in obj.EnumerateObject())
        {
            if (!allowedFields.Contains(property.Name))
            {
                return $"Unknown {contextLabel} field '{property.Name}'.";
            }
        }

        return null;
    }

    private static bool TargetsVciReadProbe(JsonElement root)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, "method", StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String
                && string.Equals(
                    property.Value.GetString(),
                    VciReadProbeContract.OperationName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetCaseSensitive(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.Ordinal))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string DescribeKind(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Array => "Array",
        JsonValueKind.String => "String",
        JsonValueKind.Number => "Number",
        JsonValueKind.True or JsonValueKind.False => "Boolean",
        JsonValueKind.Null => "Null",
        _ => element.ValueKind.ToString(),
    };
}
