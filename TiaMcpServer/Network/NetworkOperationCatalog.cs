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
    public const int MaxPageSize = 200;
    public const int MaxAttributeNames = 200;

    private static readonly IReadOnlyList<string> None = Array.Empty<string>();

    private static readonly IReadOnlyDictionary<string, NetworkOperationSpec> Specs = BuildSpecs();

    private static readonly IReadOnlySet<string> UniversalFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "operationId",
        "operation",
        "projectPath",
    };

    /// <summary>
    /// Every operation-scoped wire field and how to tell whether the caller supplied it. Declared
    /// explicitly rather than reflected: the request now mixes flat creation fields with nested
    /// configure selectors, and a reflected sweep cannot tell the two apart or name a nested member.
    /// </summary>
    private static readonly (string Name, Func<NetworkOperationRequest, bool> IsSet)[] AllRequestFields =
    {
        ("query", operation => operation.Query is not null),
        ("maxResults", operation => operation.MaxResults is not null),
        ("typeIdentifier", operation => operation.TypeIdentifier is not null),
        ("deviceName", operation => operation.DeviceName is not null),
        ("deviceItemName", operation => operation.DeviceItemName is not null),
        ("target", operation => operation.Target is not null),
        ("changes", operation => operation.Changes is not null),
        ("objectKinds", operation => operation.ObjectKinds is not null),
        ("pageSize", operation => operation.PageSize is not null),
        ("cursor", operation => operation.Cursor is not null),
        ("attributeNames", operation => operation.AttributeNames is not null),
    };

    // ---------------------------------------------------------------------------
    // Selector shapes: required and applicable fields per kind.
    // Applicable = required ∪ optional; inapplicable = present but outside applicable.
    // ---------------------------------------------------------------------------

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> SelectorRequiredFields =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [NetworkObjectKinds.DeviceItem] = new HashSet<string>(StringComparer.Ordinal) { "deviceName", "itemPath" },
            [NetworkObjectKinds.NetworkInterface] = new HashSet<string>(StringComparer.Ordinal) { "deviceName", "itemPath" },
            [NetworkObjectKinds.Node] = new HashSet<string>(StringComparer.Ordinal) { "deviceName", "nodeId" },
            [NetworkObjectKinds.Subnet] = new HashSet<string>(StringComparer.Ordinal) { "subnetId" },
            [NetworkObjectKinds.IoSystem] = new HashSet<string>(StringComparer.Ordinal) { "subnetId", "number" },
            [NetworkObjectKinds.CommunicationConnection] = new HashSet<string>(StringComparer.Ordinal)
                { "deviceName", "itemPath", "connectionIndex", "connectionType", "localConnectionName" },
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> SelectorApplicableFields =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [NetworkObjectKinds.DeviceItem] = new HashSet<string>(StringComparer.Ordinal)
                { "kind", "deviceName", "itemPath" },
            [NetworkObjectKinds.NetworkInterface] = new HashSet<string>(StringComparer.Ordinal)
                { "kind", "deviceName", "itemPath", "interfaceName", "interfaceType", "interfaceOperatingMode" },
            [NetworkObjectKinds.Node] = new HashSet<string>(StringComparer.Ordinal)
                { "kind", "deviceName", "nodeId" },
            [NetworkObjectKinds.Subnet] = new HashSet<string>(StringComparer.Ordinal)
                { "kind", "subnetId" },
            [NetworkObjectKinds.IoSystem] = new HashSet<string>(StringComparer.Ordinal)
                { "kind", "subnetId", "number" },
            [NetworkObjectKinds.CommunicationConnection] = new HashSet<string>(StringComparer.Ordinal)
                { "kind", "deviceName", "itemPath", "connectionIndex", "connectionType", "localConnectionName", "localConnectionId" },
        };

    /// <summary>All target fields that are inapplicable to configure_network_device (Phase 3 additions only).</summary>
    private static readonly IReadOnlySet<string> ConfigureInapplicableSelectorFields =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "itemPath", "interfaceName", "interfaceType", "interfaceOperatingMode",
            "subnetId", "number", "connectionIndex", "connectionType",
            "localConnectionName", "localConnectionId",
        };

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

            // Deterministic order: inapplicable fields → required fields → cardinality → selector shape.
            foreach (var field in FindInapplicableFields(operation, spec))
            {
                var valid = spec.OptionalFields.Count > 0 ? string.Join(", ", spec.OptionalFields) : "(none)";
                errors.Add(
                    $"Operation '{operation.Operation}' (operationId '{operation.OperationId}'): '{field}' is not valid for "
                    + $"{operation.Operation}. Valid optional fields: {valid}.");
            }

            var missing = spec.RequiredFields.Where(field => !IsFieldPresent(operation, field)).ToArray();
            if (missing.Length > 0)
            {
                errors.Add(
                    $"Operation '{operation.Operation}' (operationId '{operation.OperationId}') is missing required field(s): {string.Join(", ", missing)}.");
            }

            // Cardinality and bounds checks.
            if (operation.MaxResults is < 1)
            {
                errors.Add($"Operation '{operation.Operation}' (operationId '{operation.OperationId}'): 'maxResults' must be 1 or greater.");
            }

            if (spec.Name == "list_network_objects")
            {
                ValidateListNetworkObjects(operation, errors);
            }

            if (spec.Name == "inspect_network_object")
            {
                ValidateInspectNetworkObject(operation, errors);
            }

            if (spec.Name == "configure_network_device")
            {
                ValidateConfigureNetworkDevice(operation, errors);
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
        "target" => operation.Target is not null,
        "changes" => operation.Changes is not null,
        "objectKinds" => operation.ObjectKinds is not null,
        "attributeNames" => operation.AttributeNames is not null,
        _ => false,
    };

    // ---------------------------------------------------------------------------
    // Operation-specific validation
    // ---------------------------------------------------------------------------

    private static void ValidateListNetworkObjects(NetworkOperationRequest operation, List<string> errors)
    {
        var prefix = $"Operation '{operation.Operation}' (operationId '{operation.OperationId}'):";

        if (operation.ObjectKinds is { } kinds)
        {
            if (kinds.Count == 0)
            {
                errors.Add($"{prefix} 'objectKinds' must not be empty.");
            }
            else
            {
                var seenKinds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var kind in kinds)
                {
                    if (!seenKinds.Add(kind))
                    {
                        errors.Add($"{prefix} 'objectKinds' contains duplicate value '{kind}'.");
                    }
                    else if (!NetworkObjectKinds.All.Contains(kind))
                    {
                        errors.Add(
                            $"{prefix} 'objectKinds' contains unknown kind '{kind}'. "
                            + $"Valid kinds: {string.Join(", ", NetworkObjectKinds.All)}.");
                    }
                }
            }
        }

        if (operation.DeviceName is not null && string.IsNullOrWhiteSpace(operation.DeviceName))
        {
            errors.Add($"{prefix} 'deviceName' must not be blank when supplied.");
        }

        if (operation.PageSize is { } pageSize)
        {
            if (pageSize < 1 || pageSize > MaxPageSize)
            {
                errors.Add($"{prefix} 'pageSize' must be between 1 and {MaxPageSize} (received {pageSize}).");
            }
        }
    }

    private static void ValidateInspectNetworkObject(NetworkOperationRequest operation, List<string> errors)
    {
        var prefix = $"Operation '{operation.Operation}' (operationId '{operation.OperationId}'):";

        if (operation.AttributeNames is { } names)
        {
            if (names.Count == 0)
            {
                errors.Add($"{prefix} 'attributeNames' must not be empty when supplied.");
            }
            else if (names.Count > MaxAttributeNames)
            {
                errors.Add($"{prefix} 'attributeNames' must not exceed {MaxAttributeNames} entries (received {names.Count}).");
            }
            else
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var name in names)
                {
                    if (!seen.Add(name))
                    {
                        errors.Add($"{prefix} 'attributeNames' contains duplicate value '{name}'.");
                        break;
                    }
                }
            }
        }

        if (operation.Target is { } target)
        {
            ValidateSelectorShape(operation.Operation, operation.OperationId, target, errors);
        }
    }

    private static void ValidateSelectorShape(
        string operationName,
        string operationId,
        NetworkObjectTarget target,
        List<string> errors)
    {
        var prefix = $"Operation '{operationName}' (operationId '{operationId}'):";

        if (string.IsNullOrWhiteSpace(target.Kind))
        {
            errors.Add($"{prefix} 'target.kind' is required.");
            return;
        }

        if (!SelectorRequiredFields.TryGetValue(target.Kind, out var required))
        {
            errors.Add(
                $"{prefix} 'target.kind' value '{target.Kind}' is not a recognised network object kind. "
                + $"Valid kinds: {string.Join(", ", NetworkObjectKinds.All)}.");
            return;
        }

        var applicable = SelectorApplicableFields[target.Kind];

        // Missing required selector fields.
        foreach (var field in required)
        {
            if (!IsSelectorFieldPresent(target, field))
            {
                errors.Add($"{prefix} 'target.{field}' is required for kind '{target.Kind}'.");
            }
        }

        // Inapplicable selector fields.
        foreach (var (name, isSet) in AllSelectorFields)
        {
            if (name == "kind" || applicable.Contains(name) || !isSet(target))
            {
                continue;
            }

            errors.Add($"{prefix} 'target.{name}' is not applicable for kind '{target.Kind}'.");
        }
    }

    private static readonly (string Name, Func<NetworkObjectTarget, bool> IsSet)[] AllSelectorFields =
    {
        ("kind", t => !string.IsNullOrWhiteSpace(t.Kind)),
        ("deviceName", t => !string.IsNullOrWhiteSpace(t.DeviceName)),
        ("itemPath", t => t.ItemPath is not null),
        ("interfaceName", t => !string.IsNullOrWhiteSpace(t.InterfaceName)),
        ("interfaceType", t => !string.IsNullOrWhiteSpace(t.InterfaceType)),
        ("interfaceOperatingMode", t => !string.IsNullOrWhiteSpace(t.InterfaceOperatingMode)),
        ("nodeId", t => !string.IsNullOrWhiteSpace(t.NodeId)),
        ("subnetId", t => !string.IsNullOrWhiteSpace(t.SubnetId)),
        ("number", t => t.Number is not null),
        ("connectionIndex", t => t.ConnectionIndex is not null),
        ("connectionType", t => !string.IsNullOrWhiteSpace(t.ConnectionType)),
        ("localConnectionName", t => !string.IsNullOrWhiteSpace(t.LocalConnectionName)),
        ("localConnectionId", t => !string.IsNullOrWhiteSpace(t.LocalConnectionId)),
    };

    private static bool IsSelectorFieldPresent(NetworkObjectTarget target, string field) => field switch
    {
        "deviceName" => !string.IsNullOrWhiteSpace(target.DeviceName),
        "itemPath" => target.ItemPath is not null,
        "interfaceName" => !string.IsNullOrWhiteSpace(target.InterfaceName),
        "interfaceType" => !string.IsNullOrWhiteSpace(target.InterfaceType),
        "interfaceOperatingMode" => !string.IsNullOrWhiteSpace(target.InterfaceOperatingMode),
        "nodeId" => !string.IsNullOrWhiteSpace(target.NodeId),
        "subnetId" => !string.IsNullOrWhiteSpace(target.SubnetId),
        "number" => target.Number is not null,
        "connectionIndex" => target.ConnectionIndex is not null,
        "connectionType" => !string.IsNullOrWhiteSpace(target.ConnectionType),
        "localConnectionName" => !string.IsNullOrWhiteSpace(target.LocalConnectionName),
        "localConnectionId" => !string.IsNullOrWhiteSpace(target.LocalConnectionId),
        _ => false,
    };

    /// <summary>
    /// The rules that make a configure request name exactly one existing thing and ask for at
    /// least one concrete change. Anything short of that is rejected here rather than resolved
    /// by guesswork further down.
    /// </summary>
    private static void ValidateConfigureNetworkDevice(NetworkOperationRequest operation, List<string> errors)
    {
        var prefix = $"Operation '{operation.Operation}' (operationId '{operation.OperationId}'):";

        if (operation.Target is { } target)
        {
            // Phase 3: kind is accepted only when absent or exactly 'node'.
            if (target.Kind is not null && !string.Equals(target.Kind, NetworkObjectKinds.Node, StringComparison.Ordinal))
            {
                errors.Add(
                    $"{prefix} 'target.kind' must be '{NetworkObjectKinds.Node}' or absent for configure_network_device "
                    + $"(received '{target.Kind}').");
            }

            // Phase 3: new selector fields are not applicable to configure.
            foreach (var field in ConfigureInapplicableSelectorFields)
            {
                if (IsSelectorFieldPresent(target, field))
                {
                    errors.Add($"{prefix} 'target.{field}' is not applicable for configure_network_device.");
                }
            }

            if (string.IsNullOrWhiteSpace(target.DeviceName))
            {
                errors.Add($"{prefix} 'target.deviceName' must not be blank.");
            }

            if (string.IsNullOrWhiteSpace(target.NodeId))
            {
                errors.Add(
                    $"{prefix} 'target.nodeId' must not be blank. Read the exact nodeId with network_read "
                    + "(read_hardware_config); a device may expose several interfaces and nodes.");
            }
        }

        if (operation.Changes is not { } changes)
        {
            return;
        }

        var requested = 0;
        foreach (var (name, value) in new[]
        {
            ("ipAddress", changes.IpAddress),
            ("subnetMask", changes.SubnetMask),
            ("pnDeviceName", changes.PnDeviceName),
        })
        {
            if (value is null)
            {
                continue;
            }

            requested++;
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"{prefix} 'changes.{name}' must not be blank. Omit it to leave the setting unchanged.");
            }
        }

        var subnet = changes.Subnet;
        if (subnet is not null)
        {
            requested++;
            if (string.IsNullOrWhiteSpace(subnet.SubnetId))
            {
                errors.Add($"{prefix} 'changes.subnet.subnetId' is required when a subnet change is requested.");
            }
        }

        if (changes.IoSystem is { } ioSystem)
        {
            requested++;
            if (string.IsNullOrWhiteSpace(ioSystem.SubnetId))
            {
                errors.Add($"{prefix} 'changes.ioSystem.subnetId' is required when an IO-system change is requested.");
            }

            if (ioSystem.Number is null)
            {
                errors.Add($"{prefix} 'changes.ioSystem.number' is required when an IO-system change is requested.");
            }

            // A node connects to one subnet, so two selectors naming different subnets describe an
            // outcome that cannot exist. Refuse it instead of letting write order decide.
            if (subnet is not null
                && !string.IsNullOrWhiteSpace(subnet.SubnetId)
                && !string.IsNullOrWhiteSpace(ioSystem.SubnetId)
                && !string.Equals(subnet.SubnetId, ioSystem.SubnetId, StringComparison.Ordinal))
            {
                errors.Add(
                    $"{prefix} 'changes.subnet.subnetId' and 'changes.ioSystem.subnetId' must name the same subnet.");
            }
        }

        if (requested == 0)
        {
            errors.Add($"{prefix} 'changes' must request at least one change.");
        }
    }

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
            new NetworkOperationSpec("configure_network_device", NetworkOperationCategory.Write, new[] { "target", "changes" }, None),
            new NetworkOperationSpec("list_network_objects", NetworkOperationCategory.Read, new[] { "objectKinds" }, new[] { "deviceName", "pageSize", "cursor" }),
            new NetworkOperationSpec("inspect_network_object", NetworkOperationCategory.Read, new[] { "target" }, new[] { "attributeNames" }),
        };

        return specs.ToDictionary(spec => spec.Name, StringComparer.Ordinal);
    }
}
