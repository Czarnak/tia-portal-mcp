using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Strict, vendor-free JSON boundary for the internal mutation probe. It applies only to the
/// mutation operation and rejects unknown fields, casing variants, duplicate names, wrong JSON
/// types, and semantically invalid typed requests before worker dispatch.
/// </summary>
public static class VciMutationProbeJsonBoundary
{
    private static readonly HashSet<string> RootAllowedFields = Set(
        "method", "projectPath", "vciMutationProbe");

    private static readonly HashSet<string> ProbeAllowedFields = Set(
        "schemaVersion", "runId", "sessionId", "scenarioId", "caseId", "caseInstanceId",
        "mode", "workspaceRoot", "groupName", "nestedGroupName", "workspaceName",
        "workspaceLanguage", "workspace", "engineeringObject", "mapping", "relativeDirectory",
        "fileName", "fileFormat", "seedRelativePath", "synchronizationMode",
        "rollbackTransaction", "maxGroupDepth", "maxGroups", "maxWorkspaces", "maxMappings",
        "maxEngineeringObjects", "maxCollectionItems");

    private static readonly HashSet<string> RequiredProbeStringFields = Set(
        "schemaVersion", "runId", "sessionId", "scenarioId", "caseId", "caseInstanceId",
        "mode", "workspaceRoot");

    private static readonly HashSet<string> NullableProbeStringFields = Set(
        "groupName", "nestedGroupName", "workspaceName", "workspaceLanguage",
        "relativeDirectory", "fileName", "fileFormat", "seedRelativePath", "synchronizationMode");

    private static readonly HashSet<string> ProbeIntFields = Set(
        "maxGroupDepth", "maxGroups", "maxWorkspaces", "maxMappings",
        "maxEngineeringObjects", "maxCollectionItems");

    private static readonly HashSet<string> WorkspaceAllowedFields = Set(
        "groupPath", "workspaceName", "canonicalRootPath");

    private static readonly HashSet<string> GroupSegmentAllowedFields = Set(
        "index", "name", "sameNameOrdinal");

    private static readonly HashSet<string> EngineeringObjectAllowedFields = Set(
        "stableIdentifier", "structuralPath", "fingerprint");

    private static readonly HashSet<string> EngineeringPathSegmentAllowedFields = Set(
        "index", "name", "objectType");

    private static readonly HashSet<string> MappingAllowedFields = Set(
        "workspace", "engineeringObject", "relativeDirectory", "fileName", "format");

    private static readonly JsonSerializerOptions StrictDeserializeOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
    };

    /// <summary>
    /// Returns a deterministic rejection message, or null when the raw line is valid or does not
    /// target the internal mutation operation. This method never throws.
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
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !TargetsMutationProbe(root))
            {
                return null;
            }

            return ValidateEnvelope(root);
        }
    }

    private static string? ValidateEnvelope(JsonElement root)
    {
        var rootError = CheckFields(root, RootAllowedFields, "root");
        if (rootError is not null)
        {
            return rootError;
        }

        if (TryGet(root, "projectPath", out var projectPath)
            && projectPath.ValueKind is not JsonValueKind.String and not JsonValueKind.Null)
        {
            return "'projectPath' must be a string or JSON null.";
        }

        if (!TryGet(root, "vciMutationProbe", out var probe))
        {
            return "'vciMutationProbe' is required.";
        }

        if (probe.ValueKind != JsonValueKind.Object)
        {
            return $"vciMutationProbe must be an object; found {DescribeKind(probe)}.";
        }

        var shapeError = ValidateProbeShape(probe);
        if (shapeError is not null)
        {
            return shapeError;
        }

        try
        {
            var request = JsonSerializer.Deserialize<VciMutationProbeRequestInfo>(
                probe.GetRawText(),
                StrictDeserializeOptions);
            return request is null
                ? "vciMutationProbe must not be JSON null."
                : VciMutationProbeContract.Validate(request);
        }
        catch (JsonException exception)
        {
            return $"vciMutationProbe could not be decoded: {exception.Message}";
        }
    }

    private static string? ValidateProbeShape(JsonElement probe)
    {
        var fieldError = CheckFields(probe, ProbeAllowedFields, "vciMutationProbe");
        if (fieldError is not null)
        {
            return fieldError;
        }

        foreach (var field in RequiredProbeStringFields)
        {
            if (!TryGet(probe, field, out var value))
            {
                return $"'vciMutationProbe.{field}' is required.";
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                return $"'vciMutationProbe.{field}' must be a string.";
            }
        }

        foreach (var field in NullableProbeStringFields)
        {
            if (TryGet(probe, field, out var value)
                && value.ValueKind is not JsonValueKind.String and not JsonValueKind.Null)
            {
                return $"'vciMutationProbe.{field}' must be a string or JSON null.";
            }
        }

        if (TryGet(probe, "rollbackTransaction", out var rollback)
            && rollback.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            return "'vciMutationProbe.rollbackTransaction' must be a boolean.";
        }

        foreach (var field in ProbeIntFields)
        {
            if (TryGet(probe, field, out var value) && !value.TryGetInt32(out _))
            {
                return $"'vciMutationProbe.{field}' must be an integer.";
            }
        }

        var workspaceError = ValidateOptionalObject(
            probe,
            "workspace",
            value => ValidateWorkspace(value, "vciMutationProbe.workspace"));
        if (workspaceError is not null)
        {
            return workspaceError;
        }

        var objectError = ValidateOptionalObject(
            probe,
            "engineeringObject",
            value => ValidateEngineeringObject(value, "vciMutationProbe.engineeringObject"));
        if (objectError is not null)
        {
            return objectError;
        }

        return ValidateOptionalObject(
            probe,
            "mapping",
            value => ValidateMapping(value, "vciMutationProbe.mapping"));
    }

    private static string? ValidateWorkspace(JsonElement workspace, string path)
    {
        var fieldError = CheckFields(workspace, WorkspaceAllowedFields, path);
        if (fieldError is not null)
        {
            return fieldError;
        }

        var stringError = ValidateNullableStrings(workspace, path, "workspaceName", "canonicalRootPath");
        if (stringError is not null)
        {
            return stringError;
        }

        if (!TryGet(workspace, "groupPath", out var groupPath))
        {
            return null;
        }

        if (groupPath.ValueKind != JsonValueKind.Array)
        {
            return $"'{path}.groupPath' must be an array.";
        }

        foreach (var segment in groupPath.EnumerateArray())
        {
            if (segment.ValueKind != JsonValueKind.Object)
            {
                return $"'{path}.groupPath[]' must be an object.";
            }

            var segmentError = CheckFields(segment, GroupSegmentAllowedFields, $"{path}.groupPath[]")
                ?? ValidateInt32(segment, $"{path}.groupPath[]", "index", "sameNameOrdinal")
                ?? ValidateNullableStrings(segment, $"{path}.groupPath[]", "name");
            if (segmentError is not null)
            {
                return segmentError;
            }
        }

        return null;
    }

    private static string? ValidateEngineeringObject(JsonElement engineeringObject, string path)
    {
        var fieldError = CheckFields(engineeringObject, EngineeringObjectAllowedFields, path)
            ?? ValidateNullableStrings(engineeringObject, path, "stableIdentifier", "fingerprint");
        if (fieldError is not null)
        {
            return fieldError;
        }

        if (!TryGet(engineeringObject, "structuralPath", out var structuralPath))
        {
            return null;
        }

        if (structuralPath.ValueKind != JsonValueKind.Array)
        {
            return $"'{path}.structuralPath' must be an array.";
        }

        foreach (var segment in structuralPath.EnumerateArray())
        {
            if (segment.ValueKind != JsonValueKind.Object)
            {
                return $"'{path}.structuralPath[]' must be an object.";
            }

            var segmentError = CheckFields(segment, EngineeringPathSegmentAllowedFields, $"{path}.structuralPath[]")
                ?? ValidateInt32(segment, $"{path}.structuralPath[]", "index")
                ?? ValidateNullableStrings(segment, $"{path}.structuralPath[]", "name", "objectType");
            if (segmentError is not null)
            {
                return segmentError;
            }
        }

        return null;
    }

    private static string? ValidateMapping(JsonElement mapping, string path)
    {
        var fieldError = CheckFields(mapping, MappingAllowedFields, path)
            ?? ValidateNullableStrings(mapping, path, "relativeDirectory", "fileName", "format");
        if (fieldError is not null)
        {
            return fieldError;
        }

        var workspaceError = ValidateOptionalObject(mapping, "workspace", value => ValidateWorkspace(value, $"{path}.workspace"));
        if (workspaceError is not null)
        {
            return workspaceError;
        }

        return ValidateOptionalObject(
            mapping,
            "engineeringObject",
            value => ValidateEngineeringObject(value, $"{path}.engineeringObject"));
    }

    private static string? ValidateOptionalObject(
        JsonElement parent,
        string field,
        Func<JsonElement, string?> validate)
    {
        if (!TryGet(parent, field, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            return $"'vciMutationProbe.{field}' must be an object or JSON null.";
        }

        return validate(value);
    }

    private static string? ValidateNullableStrings(JsonElement parent, string path, params string[] fields)
    {
        foreach (var field in fields)
        {
            if (TryGet(parent, field, out var value)
                && value.ValueKind is not JsonValueKind.String and not JsonValueKind.Null)
            {
                return $"'{path}.{field}' must be a string or JSON null.";
            }
        }

        return null;
    }

    private static string? ValidateInt32(JsonElement parent, string path, params string[] fields)
    {
        foreach (var field in fields)
        {
            if (TryGet(parent, field, out var value) && !value.TryGetInt32(out _))
            {
                return $"'{path}.{field}' must be an integer.";
            }
        }

        return null;
    }

    private static string? CheckFields(JsonElement value, HashSet<string> allowedFields, string path)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in value.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                return $"{path} contains duplicate field '{property.Name}' (field names are case-insensitive for duplicate detection).";
            }
        }

        foreach (var property in value.EnumerateObject())
        {
            if (!allowedFields.Contains(property.Name))
            {
                return $"{path} contains unknown field '{property.Name}'.";
            }
        }

        return null;
    }

    private static bool TargetsMutationProbe(JsonElement root)
        => root.EnumerateObject().Any(property =>
            string.Equals(property.Name, "method", StringComparison.OrdinalIgnoreCase)
            && property.Value.ValueKind == JsonValueKind.String
            && string.Equals(
                property.Value.GetString(),
                VciMutationProbeContract.OperationName,
                StringComparison.OrdinalIgnoreCase));

    private static bool TryGet(JsonElement value, string name, out JsonElement result)
        => value.TryGetProperty(name, out result);

    private static HashSet<string> Set(params string[] values)
        => new HashSet<string>(values, StringComparer.Ordinal);

    private static string DescribeKind(JsonElement value)
        => value.ValueKind.ToString().ToLowerInvariant();
}
