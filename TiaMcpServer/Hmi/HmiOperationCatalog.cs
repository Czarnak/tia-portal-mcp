using System.Globalization;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.Hmi;

/// <summary>
/// Whitelists the hmi_read operations and validates a batch before any worker call. Pure and
/// Siemens-free. Also holds the single public-operation to worker-method map.
/// </summary>
public static class HmiOperationCatalog
{
    public const int MaxBatchSize = 50;
    public const int MaxOperationIdLength = 256;
    public const int DefaultLimit = 100;
    public const int MaxLimit = 2000;

    private const string WorkerMethodPrefix = "hmi_";

    private sealed record Spec(IReadOnlyList<string> Required, IReadOnlyList<string> Optional, bool IsPaged);

    private static Spec Op(string[] required, string[] optional, bool paged = false) => new(required, optional, paged);

    private static readonly string[] NoFields = Array.Empty<string>();

    private static readonly IReadOnlyDictionary<string, Spec> Specs = new Dictionary<string, Spec>(StringComparer.Ordinal)
    {
        ["list_hmi_devices"] = Op(NoFields, NoFields),
        ["list_tag_tables"] = Op(NoFields, new[] { "hmiName", "groupPath" }),
        ["list_tags"] = Op(NoFields, new[] { "hmiName", "tableName", "language" }, paged: true),
        ["get_tag"] = Op(new[] { "tagName" }, new[] { "hmiName", "language" }),
        ["list_system_tags"] = Op(NoFields, new[] { "hmiName" }, paged: true),
        ["list_connections"] = Op(NoFields, new[] { "hmiName" }),
        ["list_alarms"] = Op(NoFields, new[] { "hmiName", "alarmKind", "language" }, paged: true),
        ["get_alarm"] = Op(new[] { "alarmName", "alarmKind" }, new[] { "hmiName", "language" }),
        ["list_alarm_classes"] = Op(NoFields, new[] { "hmiName" }),
        ["list_logs"] = Op(NoFields, new[] { "hmiName" }),
        ["list_logging_tags"] = Op(NoFields, new[] { "hmiName", "dataLogName", "tagName" }, paged: true),
        ["list_screens"] = Op(NoFields, new[] { "hmiName", "groupPath", "language" }),
        ["list_screen_items"] = Op(new[] { "screenName" }, new[] { "hmiName" }, paged: true),
        ["list_faceplate_instances"] = Op(NoFields, new[] { "hmiName", "screenName" }, paged: true),
        ["get_screen_navigation"] = Op(NoFields, new[] { "hmiName" }),
        ["get_runtime_settings"] = Op(NoFields, new[] { "hmiName" }),
        ["list_script_modules"] = Op(NoFields, new[] { "hmiName" }),
        ["list_text_and_graphic_lists"] = Op(NoFields, new[] { "hmiName" }),
        ["list_project_languages"] = Op(NoFields, NoFields),
        ["validate"] = Op(new[] { "category" }, new[] { "hmiName", "name" }, paged: true),
    };

    private static readonly string[] PagingFields = { "offset", "limit" };

    private static readonly IReadOnlySet<string> AlarmKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        "discrete", "analog",
    };

    private static readonly IReadOnlySet<string> Categories = new HashSet<string>(StringComparer.Ordinal)
    {
        "tags", "alarms", "screens", "connections", "logs", "alarmClasses", "all",
    };

    private static readonly (string Name, Func<HmiOperationRequest, bool> IsSet)[] OperationFields =
    {
        ("hmiName", op => op.HmiName is not null),
        ("language", op => op.Language is not null),
        ("offset", op => op.Offset is not null),
        ("limit", op => op.Limit is not null),
        ("groupPath", op => op.GroupPath is not null),
        ("tableName", op => op.TableName is not null),
        ("tagName", op => op.TagName is not null),
        ("dataLogName", op => op.DataLogName is not null),
        ("alarmName", op => op.AlarmName is not null),
        ("alarmKind", op => op.AlarmKind is not null),
        ("screenName", op => op.ScreenName is not null),
        ("category", op => op.Category is not null),
        ("name", op => op.Name is not null),
    };

    private static readonly (string Name, Func<HmiOperationRequest, string?> Get)[] RequiredStringFields =
    {
        ("tagName", op => op.TagName),
        ("alarmName", op => op.AlarmName),
        ("alarmKind", op => op.AlarmKind),
        ("screenName", op => op.ScreenName),
        ("category", op => op.Category),
    };

    private static readonly (string Name, Func<HmiOperationRequest, string?> Get)[] NonblankStringFields =
    {
        ("hmiName", op => op.HmiName),
        ("tableName", op => op.TableName),
        ("tagName", op => op.TagName),
        ("dataLogName", op => op.DataLogName),
        ("alarmName", op => op.AlarmName),
        ("screenName", op => op.ScreenName),
        ("name", op => op.Name),
    };

    public static IReadOnlyList<string> OperationNames { get; } = Specs.Keys.ToArray();

    public static bool IsOperation(string? operation) => operation is not null && Specs.ContainsKey(operation);

    public static bool IsPaged(string? operation)
        => operation is not null && Specs.TryGetValue(operation, out var spec) && spec.IsPaged;

    /// <summary>The worker method for a public operation; the single place the mapping lives.</summary>
    public static string WorkerMethod(string operation) => WorkerMethodPrefix + operation;

    /// <summary>The worker query with paging resolved to concrete values for paged operations.</summary>
    public static HmiQueryInfo ToQuery(HmiOperationRequest operation)
    {
        var paged = Specs.TryGetValue(operation.Operation, out var spec) && spec.IsPaged;
        return new HmiQueryInfo
        {
            HmiName = operation.HmiName,
            Language = operation.Language,
            Offset = paged ? operation.Offset ?? 0 : null,
            Limit = paged ? operation.Limit ?? DefaultLimit : null,
            GroupPath = operation.GroupPath,
            TableName = operation.TableName,
            TagName = operation.TagName,
            DataLogName = operation.DataLogName,
            AlarmName = operation.AlarmName,
            AlarmKind = operation.AlarmKind,
            ScreenName = operation.ScreenName,
            Category = operation.Category,
            Name = operation.Name,
        };
    }

    public static (bool IsValid, string Error) ValidateRead(IReadOnlyList<HmiOperationRequest>? operations)
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
        IReadOnlyList<HmiOperationRequest> operations,
        McpAccessMode mode)
    {
        var errors = new List<string>();
        foreach (var operation in operations)
        {
            if (operation is null || !IsOperation(operation.Operation))
            {
                continue;
            }

            if (!OperationPolicyCatalog.IsAllowed(mode, WorkerMethod(operation.Operation)))
            {
                errors.Add(
                    $"Operation '{operation.Operation}' (operationId '{operation.OperationId}') is not permitted in {McpAccessModeNames.ToName(mode)} mode.");
            }
        }

        return errors;
    }

    private static void ValidateItem(HmiOperationRequest? operation, HashSet<string> seenIds, List<string> errors)
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
                + $"Valid operations: {string.Join(", ", OperationNames)}.");
            return;
        }

        var prefix = $"Operation '{operation.Operation}' (operationId '{operation.OperationId}')";
        var optional = spec.IsPaged ? spec.Optional.Concat(PagingFields).ToArray() : spec.Optional.ToArray();
        foreach (var (name, isSet) in OperationFields)
        {
            if (isSet(operation) && !spec.Required.Contains(name) && !optional.Contains(name))
            {
                var valid = optional.Length > 0 ? string.Join(", ", optional) : "(none)";
                errors.Add($"{prefix}: '{name}' is not valid for {operation.Operation}. Valid optional fields: {valid}.");
            }
        }

        var missing = RequiredStringFields
            .Where(field => spec.Required.Contains(field.Name) && string.IsNullOrWhiteSpace(field.Get(operation)))
            .Select(field => field.Name)
            .ToArray();
        if (missing.Length > 0)
        {
            errors.Add($"{prefix} is missing required field(s): {string.Join(", ", missing)}.");
        }

        foreach (var (name, get) in NonblankStringFields)
        {
            var value = get(operation);
            if (value is not null && string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"{prefix}: '{name}' must be nonblank when supplied.");
            }
        }

        ValidateValues(operation, prefix, errors);
    }

    private static void ValidateValues(HmiOperationRequest operation, string prefix, List<string> errors)
    {
        if (operation.Limit is { } limit && (limit < 1 || limit > MaxLimit))
        {
            errors.Add($"{prefix}: 'limit' must be between 1 and {MaxLimit}.");
        }

        if (operation.Offset is < 0)
        {
            errors.Add($"{prefix}: 'offset' must be 0 or greater.");
        }

        if (operation.Language is not null && !IsValidCulture(operation.Language))
        {
            errors.Add($"{prefix}: 'language' must be a culture name such as en-US.");
        }

        if (operation.AlarmKind is not null && !AlarmKinds.Contains(operation.AlarmKind))
        {
            errors.Add($"{prefix}: 'alarmKind' must be one of: {string.Join(", ", AlarmKinds)}.");
        }

        if (operation.Category is not null && !Categories.Contains(operation.Category))
        {
            errors.Add($"{prefix}: 'category' must be one of: {string.Join(", ", Categories)}.");
        }
        else if (operation.Name is not null && operation.Category == "all")
        {
            errors.Add($"{prefix}: 'name' is not valid with category all.");
        }

        if (operation.GroupPath is not null && operation.GroupPath.Split('/').Any(string.IsNullOrWhiteSpace))
        {
            errors.Add($"{prefix}: 'groupPath' must be '/'-separated group names with no empty segment.");
        }
    }

    private static bool IsValidCulture(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        try
        {
            return CultureInfo.GetCultureInfo(name).Name.Length != 0;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}
