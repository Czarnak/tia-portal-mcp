using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;

namespace TiaMcpServer.Batch;

public enum BatchOperationCategory
{
    Read,
    Write
}

public sealed record BatchOperationSpec(
    string Name,
    BatchOperationCategory Category,
    IReadOnlyList<string> RequiredFields,
    IReadOnlyList<string> OptionalFields);

public sealed record BatchValidationResult(bool IsValid, string Error)
{
    public static BatchValidationResult Valid() => new(true, string.Empty);

    public static BatchValidationResult Invalid(string error) => new(false, error);
}

/// <summary>
/// Whitelists the read and write operations that may run inside a batch and validates the
/// structural rules a batch must satisfy. Pure logic — no worker access — so it is fully
/// unit-testable and runs before any worker call.
/// </summary>
public static class BatchOperationCatalog
{
    public const int MaxBatchSize = 50;

    private static readonly IReadOnlyList<string> None = Array.Empty<string>();

    private static readonly IReadOnlyDictionary<string, BatchOperationSpec> Specs = BuildSpecs();

    private static readonly IReadOnlySet<string> UniversalFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "operationId",
        "operation",
        "projectPath",
    };

    // Cached once: every settable field on the flat request DTO, keyed by its camelCase wire name.
    private static readonly (string Name, Func<BatchOperationRequest, bool> IsSet)[] AllRequestFields =
        typeof(BatchOperationRequest)
            .GetProperties()
            .Select(property => (
                Name: char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1),
                IsSet: new Func<BatchOperationRequest, bool>(op => property.GetValue(op) is not null)))
            .ToArray();

    public static IReadOnlyList<string> ReadOperationNames { get; } = NamesByCategory(BatchOperationCategory.Read);

    public static IReadOnlyList<string> WriteOperationNames { get; } = NamesByCategory(BatchOperationCategory.Write);

    /// <summary>Every registered spec. Used by the field-forwarding invariant test.</summary>
    public static IReadOnlyCollection<BatchOperationSpec> All { get; } = Specs.Values.ToArray();

    /// <summary>
    /// Validates that all operations in a batch are allowed under the given access mode.
    /// Returns null when all operations are allowed, or a list of per-operation errors.
    /// </summary>
    public static IReadOnlyList<string> ValidateAccessMode(
        IReadOnlyList<BatchOperationRequest> operations,
        McpAccessMode mode)
    {
        if (mode == McpAccessMode.ReadWrite)
        {
            return Array.Empty<string>();
        }

        var errors = new List<string>();
        foreach (var op in operations)
        {
            if (op is null || string.IsNullOrWhiteSpace(op.Operation))
            {
                continue;
            }

            if (!OperationPolicyCatalog.IsAllowed(mode, op.Operation))
            {
                errors.Add(
                    $"Operation '{op.Operation}' (operationId '{op.OperationId}') is not permitted in read-only mode.");
            }
        }

        return errors;
    }

    // Real single tools that exist but are intentionally not available inside a batch, so the
    // caller gets a precise message instead of a generic "unknown operation".
    private static readonly IReadOnlySet<string> NonBatchableOperations = new HashSet<string>(StringComparer.Ordinal)
    {
        "open_project",
        "create_project",
        "save_project",
        "save_project_as",
        "archive_project",
        "close_project",
    };

    public static bool TryGetSpec(string operation, out BatchOperationSpec? spec)
        => Specs.TryGetValue(operation, out spec);

    public static BatchValidationResult ValidateReadBatch(IReadOnlyList<BatchOperationRequest>? operations)
        => Validate(operations, BatchOperationCategory.Read);

    public static BatchValidationResult ValidateWriteBatch(IReadOnlyList<BatchOperationRequest>? operations)
        => Validate(operations, BatchOperationCategory.Write);

    private static BatchValidationResult Validate(
        IReadOnlyList<BatchOperationRequest>? operations,
        BatchOperationCategory expected)
    {
        if (operations is null || operations.Count == 0)
        {
            return BatchValidationResult.Invalid("Batch must contain at least one operation.");
        }

        if (operations.Count > MaxBatchSize)
        {
            return BatchValidationResult.Invalid(
                $"Batch exceeds the maximum of {MaxBatchSize} operations (received {operations.Count}).");
        }

        var errors = new List<string>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var op in operations)
        {
            if (op is null)
            {
                errors.Add("Batch contains a null operation.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(op.OperationId))
            {
                errors.Add("Each operation requires a unique operationId.");
                continue;
            }

            if (!seenIds.Add(op.OperationId))
            {
                errors.Add($"Duplicate operationId '{op.OperationId}'.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(op.Operation))
            {
                errors.Add($"Operation name is required for operationId '{op.OperationId}'.");
                continue;
            }

            var categoryResult = ResolveSpec(op, expected, out var spec);
            if (!categoryResult.IsValid)
            {
                errors.Add(categoryResult.Error);
                continue;
            }

            var missing = spec!.RequiredFields.Where(field => !IsFieldPresent(op, field)).ToArray();
            if (missing.Length > 0)
            {
                errors.Add(
                    $"Operation '{op.Operation}' (operationId '{op.OperationId}') is missing required field(s): {string.Join(", ", missing)}.");
            }

            foreach (var field in FindInapplicableFields(op, spec))
            {
                var valid = spec.OptionalFields.Count > 0
                    ? string.Join(", ", spec.OptionalFields)
                    : "(none)";
                errors.Add(
                    $"Operation '{op.Operation}' (operationId '{op.OperationId}'): '{field}' is not valid for "
                    + $"{op.Operation}. Valid optional fields: {valid}.");
            }

            foreach (var boundsError in ValidateBounds(op))
            {
                errors.Add($"Operation '{op.Operation}' (operationId '{op.OperationId}'): {boundsError}");
            }
        }

        if (expected == BatchOperationCategory.Write)
        {
            var distinctPaths = operations
                .Where(op => op is not null && !string.IsNullOrWhiteSpace(op.ProjectPath))
                .Select(op => WriteSafetyService.NormalizeProjectPath(op!.ProjectPath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (distinctPaths.Count > 1)
            {
                errors.Add("All write operations in a batch must target the same project path.");
            }
        }

        return errors.Count > 0
            ? BatchValidationResult.Invalid(string.Join("\n", errors))
            : BatchValidationResult.Valid();
    }

    private static BatchValidationResult ResolveSpec(
        BatchOperationRequest op,
        BatchOperationCategory expected,
        out BatchOperationSpec? spec)
    {
        if (!Specs.TryGetValue(op.Operation, out spec))
        {
            if (NonBatchableOperations.Contains(op.Operation))
            {
                return BatchValidationResult.Invalid(
                    $"Operation '{op.Operation}' is a project-lifecycle operation and is not available in batch operations; use its single tool.");
            }

            var validNames = expected == BatchOperationCategory.Read ? ReadOperationNames : WriteOperationNames;
            var categoryLabel = expected == BatchOperationCategory.Read ? "read" : "write";
            return BatchValidationResult.Invalid(
                $"Unknown operation '{op.Operation}' for operationId '{op.OperationId}'. "
                + $"Valid {categoryLabel} operations: {string.Join(", ", validNames)}.");
        }

        if (spec.Category != expected)
        {
            var actual = spec.Category == BatchOperationCategory.Read ? "read" : "write";
            var container = expected == BatchOperationCategory.Read ? "execute_read_batch" : "a write batch";
            return BatchValidationResult.Invalid(
                $"Operation '{op.Operation}' is a {actual} operation and cannot run in {container}.");
        }

        return BatchValidationResult.Valid();
    }

    private static bool IsFieldPresent(BatchOperationRequest op, string field) => field switch
    {
        "blockPath" => !string.IsNullOrWhiteSpace(op.BlockPath),
        "yamlContent" => !string.IsNullOrWhiteSpace(op.YamlContent),
        "query" => !string.IsNullOrWhiteSpace(op.Query),
        "tableName" => !string.IsNullOrWhiteSpace(op.TableName),
        "name" => !string.IsNullOrWhiteSpace(op.Name),
        "dataType" => !string.IsNullOrWhiteSpace(op.DataType),
        "value" => !string.IsNullOrWhiteSpace(op.Value),
        "typeIdentifier" => !string.IsNullOrWhiteSpace(op.TypeIdentifier),
        "deviceName" => !string.IsNullOrWhiteSpace(op.DeviceName),
        "blockType" => !string.IsNullOrWhiteSpace(op.BlockType),
        "typePath" => !string.IsNullOrWhiteSpace(op.TypePath),
        "sourceContent" => !string.IsNullOrWhiteSpace(op.SourceContent),
        _ => false,
    };

    private static IEnumerable<string> FindInapplicableFields(BatchOperationRequest op, BatchOperationSpec spec)
    {
        foreach (var field in AllRequestFields)
        {
            if (UniversalFields.Contains(field.Name) ||
                spec.RequiredFields.Contains(field.Name) ||
                spec.OptionalFields.Contains(field.Name) ||
                !field.IsSet(op))
            {
                continue;
            }

            yield return field.Name;
        }
    }

    private static IEnumerable<string> ValidateBounds(BatchOperationRequest op)
    {
        if (op.Depth is < 1)
        {
            yield return "'depth' must be 1 or greater.";
        }

        if (op.MaxResults is < 1)
        {
            yield return "'maxResults' must be 1 or greater.";
        }
    }

    private static IReadOnlyList<string> NamesByCategory(BatchOperationCategory category)
        => Specs.Values.Where(s => s.Category == category).Select(s => s.Name).ToArray();

    private static IReadOnlyDictionary<string, BatchOperationSpec> BuildSpecs()
    {
        var specs = new[]
        {
            // Reads
            new BatchOperationSpec("browse_project_tree", BatchOperationCategory.Read, None, new[] { "depth", "startPath" }),
            new BatchOperationSpec("read_hardware_config", BatchOperationCategory.Read, None, None),
            new BatchOperationSpec("search_equipment_catalog", BatchOperationCategory.Read, new[] { "query" }, new[] { "maxResults" }),
            new BatchOperationSpec("read_cross_references", BatchOperationCategory.Read, None, new[] { "plcName", "filter", "maxResults" }),
            new BatchOperationSpec("get_block_content", BatchOperationCategory.Read, new[] { "blockPath" }, new[] { "format", "withDependencies" }),
            new BatchOperationSpec("list_tag_tables", BatchOperationCategory.Read, None, new[] { "plcName" }),
            new BatchOperationSpec("compile_check", BatchOperationCategory.Read, None, new[] { "blockPath", "plcName" }),
            new BatchOperationSpec("get_project_status", BatchOperationCategory.Read, None, None),
            new BatchOperationSpec("get_type_content", BatchOperationCategory.Read, new[] { "typePath" }, new[] { "format", "withDependencies" }),

            // Data writes
            new BatchOperationSpec("update_block_logic", BatchOperationCategory.Write, new[] { "blockPath", "yamlContent" }, new[] { "format" }),
            new BatchOperationSpec("create_tag_table", BatchOperationCategory.Write, new[] { "tableName" }, new[] { "plcName", "folderPath" }),
            new BatchOperationSpec("delete_tag_table", BatchOperationCategory.Write, new[] { "tableName" }, new[] { "plcName", "folderPath" }),
            new BatchOperationSpec("create_tag", BatchOperationCategory.Write, new[] { "tableName", "name", "dataType" }, new[] { "plcName", "folderPath", "logicalAddress" }),
            new BatchOperationSpec("update_tag", BatchOperationCategory.Write, new[] { "tableName", "name" }, new[] { "plcName", "folderPath", "newName", "dataType", "logicalAddress", "externalAccessible", "externalVisible", "externalWritable", "isSafety" }),
            new BatchOperationSpec("delete_tag", BatchOperationCategory.Write, new[] { "tableName", "name" }, new[] { "plcName", "folderPath" }),
            new BatchOperationSpec("create_user_constant", BatchOperationCategory.Write, new[] { "tableName", "name", "dataType", "value" }, new[] { "plcName", "folderPath" }),
            new BatchOperationSpec("update_user_constant", BatchOperationCategory.Write, new[] { "tableName", "name" }, new[] { "plcName", "folderPath", "dataType", "value" }),
            new BatchOperationSpec("delete_user_constant", BatchOperationCategory.Write, new[] { "tableName", "name" }, new[] { "plcName", "folderPath" }),
            new BatchOperationSpec("add_network_device", BatchOperationCategory.Write, new[] { "typeIdentifier", "deviceName" }, new[] { "deviceItemName" }),
            new BatchOperationSpec("configure_network_device", BatchOperationCategory.Write, new[] { "deviceName" }, new[] { "ipAddress", "subnetMask", "pnDeviceName", "subnetName", "ioSystemName" }),
            new BatchOperationSpec("create_block", BatchOperationCategory.Write, new[] { "blockPath", "blockType" }, new[] { "language", "obEventClass" }),
            new BatchOperationSpec("delete_block", BatchOperationCategory.Write, new[] { "blockPath" }, None),
            new BatchOperationSpec("create_block_group", BatchOperationCategory.Write, new[] { "blockPath" }, None),
            new BatchOperationSpec("delete_block_group", BatchOperationCategory.Write, new[] { "blockPath" }, None),
            new BatchOperationSpec("start_plc", BatchOperationCategory.Write, None, new[] { "plcName" }),
            new BatchOperationSpec("stop_plc", BatchOperationCategory.Write, None, new[] { "plcName" }),
            new BatchOperationSpec("update_type_content", BatchOperationCategory.Write, new[] { "typePath", "sourceContent" }, new[] { "format" }),
        };

        return specs.ToDictionary(spec => spec.Name, StringComparer.Ordinal);
    }
}
