using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;

namespace TiaMcpServer.Plc;

/// <summary>
/// Whitelists the dedicated PLC read operations and validates a batch before any worker call.
/// Pure and Siemens-free.
/// </summary>
public static class PlcOperationCatalog
{
    public const int MaxBatchSize = 50;
    public const int MaxOperationIdLength = 256;

    private sealed record Spec(IReadOnlyList<string> Required, IReadOnlyList<string> Optional);

    private static readonly IReadOnlyDictionary<string, Spec> Specs = new Dictionary<string, Spec>(StringComparer.Ordinal)
    {
        ["get_block_content"] = new(new[] { "blockPath" }, new[] { "format", "withDependencies" }),
        ["get_type_content"] = new(new[] { "typePath" }, new[] { "format", "withDependencies" }),
        ["list_tag_tables"] = new(Array.Empty<string>(), new[] { "plcName" }),
    };

    private static readonly (string Name, Func<PlcOperationRequest, bool> IsSet)[] OperationFields =
    {
        ("blockPath", op => op.BlockPath is not null),
        ("typePath", op => op.TypePath is not null),
        ("format", op => op.Format is not null),
        ("withDependencies", op => op.WithDependencies is not null),
        ("plcName", op => op.PlcName is not null),
    };

    public static IReadOnlyList<string> ReadOperationNames { get; } = Specs.Keys.ToArray();

    public static (bool IsValid, string Error) ValidateRead(IReadOnlyList<PlcOperationRequest>? operations)
    {
        if (operations is null || operations.Count == 0)
        {
            return (false, "Batch must contain at least one operation.");
        }

        if (operations.Count > MaxBatchSize)
        {
            return (false, $"Batch exceeds the maximum of {MaxBatchSize} operations (received {operations.Count}).");
        }

        var errors = new List<string>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operation in operations)
        {
            ValidateItem(operation, seenIds, errors);
        }

        return errors.Count == 0 ? (true, string.Empty) : (false, string.Join("\n", errors));
    }

    public static IReadOnlyList<string> ValidateAccessMode(
        IReadOnlyList<PlcOperationRequest> operations,
        McpAccessMode mode)
    {
        var errors = new List<string>();
        foreach (var operation in operations)
        {
            if (operation is null || string.IsNullOrWhiteSpace(operation.Operation))
            {
                continue;
            }

            if (!OperationPolicyCatalog.IsAllowed(mode, operation.Operation))
            {
                errors.Add(
                    $"Operation '{operation.Operation}' (operationId '{operation.OperationId}') is not permitted in {McpAccessModeNames.ToName(mode)} mode.");
            }
        }

        return errors;
    }

    private static void ValidateItem(PlcOperationRequest? operation, HashSet<string> seenIds, List<string> errors)
    {
        if (operation is null)
        {
            errors.Add("Batch contains a null operation.");
            return;
        }

        if (string.IsNullOrWhiteSpace(operation.OperationId))
        {
            errors.Add("Each operation requires a unique operationId.");
            return;
        }

        if (operation.OperationId.Length > MaxOperationIdLength)
        {
            errors.Add($"Each operationId must be at most {MaxOperationIdLength} characters.");
            return;
        }

        if (!seenIds.Add(operation.OperationId))
        {
            errors.Add($"Duplicate operationId '{operation.OperationId}'.");
            return;
        }

        if (string.IsNullOrWhiteSpace(operation.Operation))
        {
            errors.Add($"Operation name is required for operationId '{operation.OperationId}'.");
            return;
        }

        if (!Specs.TryGetValue(operation.Operation, out var spec))
        {
            errors.Add(
                $"Unknown operation '{operation.Operation}' for operationId '{operation.OperationId}'. "
                + $"Valid read operations: {string.Join(", ", ReadOperationNames)}.");
            return;
        }

        var prefix = $"Operation '{operation.Operation}' (operationId '{operation.OperationId}')";
        foreach (var (name, isSet) in OperationFields)
        {
            if (isSet(operation) && !spec.Required.Contains(name) && !spec.Optional.Contains(name))
            {
                var valid = spec.Optional.Count > 0 ? string.Join(", ", spec.Optional) : "(none)";
                errors.Add($"{prefix}: '{name}' is not valid for {operation.Operation}. Valid optional fields: {valid}.");
            }
        }

        var missing = spec.Required.Where(field => !IsPresent(operation, field)).ToArray();
        if (missing.Length > 0)
        {
            errors.Add($"{prefix} is missing required field(s): {string.Join(", ", missing)}.");
        }

        if (operation.PlcName is not null && string.IsNullOrWhiteSpace(operation.PlcName))
        {
            errors.Add($"{prefix}: 'plcName' must be nonblank when supplied.");
        }

        if (operation.Format is not null)
        {
            try
            {
                _ = PlcFormatNames.Normalize(operation.Operation, operation.Format);
            }
            catch (ArgumentException exception)
            {
                errors.Add($"{prefix}: {exception.Message}");
            }
        }
    }

    private static bool IsPresent(PlcOperationRequest operation, string field) => field switch
    {
        "blockPath" => !string.IsNullOrWhiteSpace(operation.BlockPath),
        "typePath" => !string.IsNullOrWhiteSpace(operation.TypePath),
        _ => false,
    };
}
