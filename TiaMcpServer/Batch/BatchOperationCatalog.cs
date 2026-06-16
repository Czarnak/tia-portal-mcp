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
    IReadOnlyList<string> RequiredFields);

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

    public static IReadOnlyList<string> ReadOperationNames { get; } = NamesByCategory(BatchOperationCategory.Read);

    public static IReadOnlyList<string> WriteOperationNames { get; } = NamesByCategory(BatchOperationCategory.Write);

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

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var op in operations)
        {
            if (op is null)
            {
                return BatchValidationResult.Invalid("Batch contains a null operation.");
            }

            if (string.IsNullOrWhiteSpace(op.OperationId))
            {
                return BatchValidationResult.Invalid("Each operation requires a unique operationId.");
            }

            if (!seenIds.Add(op.OperationId))
            {
                return BatchValidationResult.Invalid($"Duplicate operationId '{op.OperationId}'.");
            }

            if (string.IsNullOrWhiteSpace(op.Operation))
            {
                return BatchValidationResult.Invalid(
                    $"Operation name is required for operationId '{op.OperationId}'.");
            }

            var categoryResult = ResolveSpec(op, expected, out var spec);
            if (!categoryResult.IsValid)
            {
                return categoryResult;
            }

            var missing = spec!.RequiredFields.Where(field => !IsFieldPresent(op, field)).ToArray();
            if (missing.Length > 0)
            {
                return BatchValidationResult.Invalid(
                    $"Operation '{op.Operation}' (operationId '{op.OperationId}') is missing required field(s): {string.Join(", ", missing)}.");
            }
        }

        if (expected == BatchOperationCategory.Write)
        {
            var distinctPaths = operations
                .Where(op => !string.IsNullOrWhiteSpace(op!.ProjectPath))
                .Select(op => WriteSafetyService.NormalizeProjectPath(op!.ProjectPath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (distinctPaths.Count > 1)
            {
                return BatchValidationResult.Invalid(
                    "All write operations in a batch must target the same project path.");
            }
        }

        return BatchValidationResult.Valid();
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

            return BatchValidationResult.Invalid(
                $"Unknown operation '{op.Operation}' for operationId '{op.OperationId}'.");
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
        _ => false,
    };

    private static IReadOnlyList<string> NamesByCategory(BatchOperationCategory category)
        => Specs.Values.Where(s => s.Category == category).Select(s => s.Name).ToArray();

    private static IReadOnlyDictionary<string, BatchOperationSpec> BuildSpecs()
    {
        var specs = new[]
        {
            // Reads
            new BatchOperationSpec("browse_project_tree", BatchOperationCategory.Read, None),
            new BatchOperationSpec("read_hardware_config", BatchOperationCategory.Read, None),
            new BatchOperationSpec("search_equipment_catalog", BatchOperationCategory.Read, new[] { "query" }),
            new BatchOperationSpec("read_cross_references", BatchOperationCategory.Read, None),
            new BatchOperationSpec("get_block_content", BatchOperationCategory.Read, new[] { "blockPath" }),
            new BatchOperationSpec("list_tag_tables", BatchOperationCategory.Read, None),
            new BatchOperationSpec("compile_check", BatchOperationCategory.Read, None),
            new BatchOperationSpec("get_project_status", BatchOperationCategory.Read, None),

            // Data writes
            new BatchOperationSpec("update_block_logic", BatchOperationCategory.Write, new[] { "blockPath", "yamlContent" }),
            new BatchOperationSpec("create_tag_table", BatchOperationCategory.Write, new[] { "tableName" }),
            new BatchOperationSpec("delete_tag_table", BatchOperationCategory.Write, new[] { "tableName" }),
            new BatchOperationSpec("create_tag", BatchOperationCategory.Write, new[] { "tableName", "name", "dataType" }),
            new BatchOperationSpec("update_tag", BatchOperationCategory.Write, new[] { "tableName", "name" }),
            new BatchOperationSpec("delete_tag", BatchOperationCategory.Write, new[] { "tableName", "name" }),
            new BatchOperationSpec("create_user_constant", BatchOperationCategory.Write, new[] { "tableName", "name", "dataType", "value" }),
            new BatchOperationSpec("update_user_constant", BatchOperationCategory.Write, new[] { "tableName", "name" }),
            new BatchOperationSpec("delete_user_constant", BatchOperationCategory.Write, new[] { "tableName", "name" }),
            new BatchOperationSpec("add_network_device", BatchOperationCategory.Write, new[] { "typeIdentifier", "deviceName" }),
            new BatchOperationSpec("configure_network_device", BatchOperationCategory.Write, new[] { "deviceName" }),
        };

        return specs.ToDictionary(spec => spec.Name, StringComparer.Ordinal);
    }
}
