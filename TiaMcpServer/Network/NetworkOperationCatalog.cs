using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;

namespace TiaMcpServer.Network;

public enum NetworkOperationCategory
{
    Read,
    Write,
}

public sealed record NetworkOperationSpec(
    string Name,
    NetworkOperationCategory Category,
    IReadOnlyList<string> RequiredFields,
    IReadOnlyList<string> OptionalFields);

public sealed record NetworkValidationResult(bool IsValid, string Error)
{
    public static NetworkValidationResult Valid() => new(true, string.Empty);

    public static NetworkValidationResult Invalid(string error) => new(false, error);
}

/// <summary>
/// Whitelists the dedicated network operations and performs all structural validation before a
/// worker call. This type is pure and Siemens-free.
/// </summary>
public static class NetworkOperationCatalog
{
    public const int MaxBatchSize = 50;

    private static readonly IReadOnlyList<string> None = Array.Empty<string>();

    private static readonly IReadOnlyDictionary<string, NetworkOperationSpec> Specs = BuildSpecs();

    private static readonly IReadOnlySet<string> UniversalFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "operationId",
        "operation",
        "projectPath",
    };

    private static readonly (string Name, Func<NetworkOperationRequest, bool> IsSet)[] AllRequestFields =
        typeof(NetworkOperationRequest)
            .GetProperties()
            .Select(property => (
                Name: char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1),
                IsSet: new Func<NetworkOperationRequest, bool>(operation => property.GetValue(operation) is not null)))
            .ToArray();

    public static IReadOnlyList<string> ReadOperationNames { get; } = NamesByCategory(NetworkOperationCategory.Read);

    public static IReadOnlyList<string> WriteOperationNames { get; } = NamesByCategory(NetworkOperationCategory.Write);

    public static IReadOnlyCollection<NetworkOperationSpec> All { get; } = Specs.Values.ToArray();

    public static bool TryGetSpec(string operation, out NetworkOperationSpec? spec)
        => Specs.TryGetValue(operation, out spec);

    public static NetworkValidationResult ValidateRead(IReadOnlyList<NetworkOperationRequest>? operations)
        => Validate(operations, NetworkOperationCategory.Read);

    public static NetworkValidationResult ValidateWrite(IReadOnlyList<NetworkOperationRequest>? operations)
        => Validate(operations, NetworkOperationCategory.Write);

    public static IReadOnlyList<string> ValidateAccessMode(
        IReadOnlyList<NetworkOperationRequest> operations,
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
                    $"Operation '{operation.Operation}' (operationId '{operation.OperationId}') is not permitted in read-only mode.");
            }
        }

        return errors;
    }

    private static NetworkValidationResult Validate(
        IReadOnlyList<NetworkOperationRequest>? operations,
        NetworkOperationCategory expectedCategory)
    {
        if (operations is null || operations.Count == 0)
        {
            return NetworkValidationResult.Invalid("Batch must contain at least one operation.");
        }

        if (operations.Count > MaxBatchSize)
        {
            return NetworkValidationResult.Invalid(
                $"Batch exceeds the maximum of {MaxBatchSize} operations (received {operations.Count}).");
        }

        var errors = new List<string>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operation in operations)
        {
            if (operation is null)
            {
                errors.Add("Batch contains a null operation.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(operation.OperationId))
            {
                errors.Add("Each operation requires a unique operationId.");
                continue;
            }

            if (!seenIds.Add(operation.OperationId))
            {
                errors.Add($"Duplicate operationId '{operation.OperationId}'.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(operation.Operation))
            {
                errors.Add($"Operation name is required for operationId '{operation.OperationId}'.");
                continue;
            }

            if (!Specs.TryGetValue(operation.Operation, out var spec))
            {
                var categoryName = expectedCategory == NetworkOperationCategory.Read ? "read" : "write";
                var validNames = expectedCategory == NetworkOperationCategory.Read
                    ? ReadOperationNames
                    : WriteOperationNames;
                errors.Add(
                    $"Unknown operation '{operation.Operation}' for operationId '{operation.OperationId}'. "
                    + $"Valid {categoryName} operations: {string.Join(", ", validNames)}.");
                continue;
            }

            if (spec.Category != expectedCategory)
            {
                var actualCategory = spec.Category == NetworkOperationCategory.Read ? "read" : "write";
                var container = expectedCategory == NetworkOperationCategory.Read ? "a network read request" : "a network write request";
                errors.Add($"Operation '{operation.Operation}' is a {actualCategory} operation and cannot run in {container}.");
                continue;
            }

            var missing = spec.RequiredFields.Where(field => !IsFieldPresent(operation, field)).ToArray();
            if (missing.Length > 0)
            {
                errors.Add(
                    $"Operation '{operation.Operation}' (operationId '{operation.OperationId}') is missing required field(s): {string.Join(", ", missing)}.");
            }

            foreach (var field in FindInapplicableFields(operation, spec))
            {
                var valid = spec.OptionalFields.Count > 0 ? string.Join(", ", spec.OptionalFields) : "(none)";
                errors.Add(
                    $"Operation '{operation.Operation}' (operationId '{operation.OperationId}'): '{field}' is not valid for "
                    + $"{operation.Operation}. Valid optional fields: {valid}.");
            }

            if (operation.MaxResults is < 1)
            {
                errors.Add($"Operation '{operation.Operation}' (operationId '{operation.OperationId}'): 'maxResults' must be 1 or greater.");
            }
        }

        if (expectedCategory == NetworkOperationCategory.Write)
        {
            var distinctPaths = operations
                .Where(operation => operation is not null && !string.IsNullOrWhiteSpace(operation.ProjectPath))
                .Select(operation => WriteSafetyService.NormalizeProjectPath(operation!.ProjectPath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (distinctPaths.Length > 1)
            {
                errors.Add("All write operations in a batch must target the same project path.");
            }
        }

        return errors.Count == 0
            ? NetworkValidationResult.Valid()
            : NetworkValidationResult.Invalid(string.Join("\n", errors));
    }

    private static bool IsFieldPresent(NetworkOperationRequest operation, string field) => field switch
    {
        "query" => !string.IsNullOrWhiteSpace(operation.Query),
        "typeIdentifier" => !string.IsNullOrWhiteSpace(operation.TypeIdentifier),
        "deviceName" => !string.IsNullOrWhiteSpace(operation.DeviceName),
        _ => false,
    };

    private static IEnumerable<string> FindInapplicableFields(
        NetworkOperationRequest operation,
        NetworkOperationSpec spec)
    {
        foreach (var field in AllRequestFields)
        {
            if (UniversalFields.Contains(field.Name)
                || spec.RequiredFields.Contains(field.Name)
                || spec.OptionalFields.Contains(field.Name)
                || !field.IsSet(operation))
            {
                continue;
            }

            yield return field.Name;
        }
    }

    private static IReadOnlyList<string> NamesByCategory(NetworkOperationCategory category)
        => Specs.Values.Where(spec => spec.Category == category).Select(spec => spec.Name).ToArray();

    private static IReadOnlyDictionary<string, NetworkOperationSpec> BuildSpecs()
    {
        var specs = new[]
        {
            new NetworkOperationSpec("read_hardware_config", NetworkOperationCategory.Read, None, None),
            new NetworkOperationSpec("search_equipment_catalog", NetworkOperationCategory.Read, new[] { "query" }, new[] { "maxResults" }),
            new NetworkOperationSpec("add_network_device", NetworkOperationCategory.Write, new[] { "typeIdentifier", "deviceName" }, new[] { "deviceItemName" }),
            new NetworkOperationSpec("configure_network_device", NetworkOperationCategory.Write, new[] { "deviceName" }, new[] { "ipAddress", "subnetMask", "pnDeviceName", "subnetName", "ioSystemName" }),
        };

        return specs.ToDictionary(spec => spec.Name, StringComparer.Ordinal);
    }
}
