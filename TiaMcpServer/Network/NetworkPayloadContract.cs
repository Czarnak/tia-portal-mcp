using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Network;

/// <summary>
/// The only decoder of Network worker success payloads. Every network operation declares exactly
/// one result contract here; a payload that does not match it is rejected rather than forwarded.
///
/// <para>
/// Rejection is deliberate. Forwarding an unrecognized payload would publish worker-shaped data
/// under a declared output schema that does not describe it, so malformed, unknown, incorrectly
/// cased, incorrectly typed, and structurally invalid payloads all become failed items with
/// category <see cref="WorkerFailureCategories.ProtocolError"/>. The rejected payload is never
/// echoed back — the caller asked for contract-shaped data and must not be handed the raw bytes
/// that failed the contract.
/// </para>
/// </summary>
public static class NetworkPayloadContract
{
    /// <summary>Projects one worker outcome into its structured batch item.</summary>
    public static StructuredOperationItem Project(
        NetworkOperationRequest operation,
        WorkerCallResult workerResult)
    {
        var warnings = workerResult.Warnings ?? Array.Empty<string>();

        if (!workerResult.Success)
        {
            return Failed(
                operation,
                workerResult.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                workerResult.Error ?? $"Network operation '{operation.Operation}' failed.",
                warnings);
        }

        JsonElement result;
        try
        {
            result = Decode(operation.Operation, workerResult.Payload);
        }
        catch (JsonException)
        {
            return Failed(
                operation,
                WorkerFailureCategories.ProtocolError,
                $"The worker payload for '{operation.Operation}' did not match its declared result "
                    + "contract and was rejected.",
                warnings);
        }

        return new StructuredOperationItem(
            operation.OperationId,
            operation.Operation,
            OperationBatchStatus.Succeeded,
            result,
            Failure: null,
            Omission: null,
            SkipReason: null,
            warnings);
    }

    /// <summary>
    /// Decodes a <c>read_hardware_config</c> payload under the same contract the batch path uses,
    /// returning the typed value. Callers that bind safety tokens to hardware state need the value
    /// itself, not a batch item. Throws <see cref="JsonException"/> when the payload does not match.
    /// </summary>
    public static HardwareConfigInfo DecodeHardwareConfig(string payload)
        => CanonicalJson.Normalize<HardwareConfigInfo>(payload, ValidateHardwareConfig).Value;

    private static JsonElement Decode(string operation, string payload) => operation switch
    {
        "read_hardware_config" => Decode<HardwareConfigInfo>(payload, ValidateHardwareConfig),
        "search_equipment_catalog" => Decode<CatalogEntryInfo[]>(payload, ValidateCatalogEntries),
        "add_network_device" => Decode<AddDeviceResultInfo>(payload, ValidateAddDeviceResult),
        "configure_network_device" =>
            Decode<ConfigureNetworkDeviceResultInfo>(payload, ValidateConfigureResult),
        "list_network_objects" => Decode<NetworkObjectListInfo>(payload, ValidateObjectList),
        "inspect_network_object" => Decode<NetworkObjectInspectionInfo>(payload, ValidateObjectInspection),
        _ => throw new JsonException($"No declared result contract for network operation '{operation}'."),
    };

    private static JsonElement Decode<T>(string payload, Action<T> validate)
        => CanonicalJson.Normalize(payload, validate).Element;

    // The contract types initialize their collections and their non-nullable strings, so CLR
    // initialization already covers an ABSENT member. An EXPLICIT null does not go through the
    // initializer, so these validators are what keep a declared non-nullable member non-null.

    private static void ValidateHardwareConfig(HardwareConfigInfo value)
    {
        RequireNotNull(value.Devices, "devices");
        RequireNotNull(value.Subnets, "subnets");
        RequireNotNull(value.Messages, "messages");

        foreach (var device in value.Devices)
        {
            RequireNotNull(device, "devices[]");
            RequireNotNull(device.Items, "devices[].items");
            foreach (var item in device.Items)
            {
                ValidateDeviceItem(item, "devices[].items[]");
            }
        }

        foreach (var subnet in value.Subnets)
        {
            RequireNotNull(subnet, "subnets[]");
            RequireNotNull(subnet.Name, "subnets[].name");
            RequireNotNull(subnet.IoSystems, "subnets[].ioSystems");
            RequireNotNull(subnet.ConnectedNodeNames, "subnets[].connectedNodeNames");
            RequireNotNull(subnet.SelectorDiagnostics, "subnets[].selectorDiagnostics");
            foreach (var ioSystem in subnet.IoSystems)
            {
                RequireNotNull(ioSystem, "subnets[].ioSystems[]");
                RequireNotNull(ioSystem!.SelectorDiagnostics, "subnets[].ioSystems[].selectorDiagnostics");
                RequireNotNull(ioSystem.ConnectedDeviceNames, "subnets[].ioSystems[].connectedDeviceNames");
            }
        }
    }

    /// <summary>
    /// Recurses into a device item's nested network interfaces, nodes, and child items — the exact
    /// tree <see cref="NetworkIdentityResolver"/> walks to match a configure_network_device target.
    /// A null element anywhere in that walk must fail the contract here rather than reach the
    /// resolver, which would otherwise dereference it.
    /// </summary>
    private static void ValidateDeviceItem(DeviceItemInfo? item, string path)
    {
        RequireNotNull(item, path);
        RequireNotNull(item!.NetworkInterfaces, $"{path}.networkInterfaces");
        RequireNotNull(item.Items, $"{path}.items");
        RequireNotNull(item.SelectorDiagnostics, $"{path}.selectorDiagnostics");

        foreach (var networkInterface in item.NetworkInterfaces)
        {
            RequireNotNull(networkInterface, $"{path}.networkInterfaces[]");
            RequireNotNull(networkInterface!.Nodes, $"{path}.networkInterfaces[].nodes");
            RequireNotNull(networkInterface.SelectorDiagnostics, $"{path}.networkInterfaces[].selectorDiagnostics");
            foreach (var node in networkInterface.Nodes)
            {
                RequireNotNull(node, $"{path}.networkInterfaces[].nodes[]");
                RequireNotNull(node!.SelectorDiagnostics, $"{path}.networkInterfaces[].nodes[].selectorDiagnostics");
            }
        }

        foreach (var child in item.Items)
        {
            ValidateDeviceItem(child, $"{path}.items[]");
        }
    }

    private static void ValidateCatalogEntries(CatalogEntryInfo[] value)
    {
        foreach (var entry in value)
        {
            RequireNotNull(entry, "[]");
            RequireNotNull(entry.TypeName, "[].typeName");
            RequireNotNull(entry.TypeIdentifier, "[].typeIdentifier");
        }
    }

    private static void ValidateAddDeviceResult(AddDeviceResultInfo value)
    {
        RequireNotNull(value.DeviceName, "deviceName");
        RequireNotNull(value.RootItemName, "rootItemName");
        RequireNotNull(value.TypeIdentifier, "typeIdentifier");
        RequireNotNull(value.Warnings, "warnings");
    }

    private static void ValidateConfigureResult(ConfigureNetworkDeviceResultInfo value)
    {
        RequireNotNull(value.DeviceName, "deviceName");
        RequireNotNull(value.AppliedSettings, "appliedSettings");
        RequireNotNull(value.SkippedSettings, "skippedSettings");
        RequireNotNull(value.Messages, "messages");
    }

    private static void ValidateObjectList(NetworkObjectListInfo value)
    {
        RequireNotNull(value.Items, "items");
        RequireNotNull(value.Messages, "messages");

        if (value.TotalCount is < 0)
        {
            throw new JsonException("'totalCount' must not be negative.");
        }

        if (value.ReturnedCount < 0)
        {
            throw new JsonException("'returnedCount' must not be negative.");
        }

        if (value.Items!.Count != value.ReturnedCount)
        {
            throw new JsonException(
                $"'returnedCount' ({value.ReturnedCount}) does not match 'items' count ({value.Items.Count}).");
        }

        if (value.TotalCount is { } totalCount && value.ReturnedCount > totalCount)
        {
            throw new JsonException(
                $"'returnedCount' ({value.ReturnedCount}) exceeds 'totalCount' ({totalCount}).");
        }

        foreach (var item in value.Items!)
        {
            RequireNotNull(item, "items[]");
            ValidateObjectSummary(item!);
        }
    }

    private static void ValidateObjectSummary(NetworkObjectSummaryInfo item)
    {
        if (item.Kind is not null && !NetworkObjectKinds.All.Contains(item.Kind))
        {
            throw new JsonException($"'items[].kind' value '{item.Kind}' is not a recognised network object kind.");
        }

        // When a selector is present the embedded kind must agree with the summary kind.
        if (item.Selector is { Kind: not null } selector && item.Kind is not null
            && !string.Equals(selector.Kind, item.Kind, StringComparison.Ordinal))
        {
            throw new JsonException(
                $"'items[].selector.kind' value '{selector.Kind}' does not match 'items[].kind' value '{item.Kind}'.");
        }
    }

    private static readonly IReadOnlySet<string> ValidAttributeAccess =
        new HashSet<string>(StringComparer.Ordinal)
            { "none", "readOnly", "writeOnly", "readWrite", "unknown" };

    private static readonly IReadOnlySet<string> ValidAttributeAvailability =
        new HashSet<string>(StringComparer.Ordinal)
            { "available", "notApplicable", "unsupported", "unreadable", "readFailed", "unrepresentable", "unknownAttribute" };

    private static readonly IReadOnlySet<string> ValidAttributeSource =
        new HashSet<string>(StringComparer.Ordinal)
            { "modeled", "dynamic", "modeledAndDynamic" };

    private static readonly IReadOnlySet<string> ValidAttributeValueKind =
        new HashSet<string>(StringComparer.Ordinal)
            { "string", "boolean", "integer", "number", "enum" };

    private static void ValidateObjectInspection(NetworkObjectInspectionInfo value)
    {
        RequireNotNull(value.Target, "target");
        RequireNotNull(value.Evidence, "evidence");
        RequireNotNull(value.Evidence.DeviceItemPath, "evidence.deviceItemPath");
        foreach (var segment in value.Evidence.DeviceItemPath)
        {
            RequireNotNull(segment, "evidence.deviceItemPath[]");
        }

        RequireNotNull(value.Attributes, "attributes");
        RequireNotNull(value.Messages, "messages");
        ValidateInspectionTarget(value.Target);

        // Duplicate attribute names are a protocol error: the worker must return each name at most once.
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attr in value.Attributes!)
        {
            RequireNotNull(attr, "attributes[]");
            ValidateAttribute(attr!);
            if (attr.Name is not null && !seenNames.Add(attr.Name))
            {
                throw new JsonException($"Duplicate attribute name '{attr.Name}' in 'attributes'.");
            }
        }
    }

    private static void ValidateAttribute(NetworkAttributeInfo attr)
    {
        var prefix = attr.Name is not null ? $"attributes['{attr.Name}']" : "attributes[]";
        RequireNotNull(attr.Name, $"{prefix}.name");
        RequireNotNull(attr.SupportedTypes, $"{prefix}.supportedTypes");
        foreach (var supportedType in attr.SupportedTypes)
        {
            RequireNotNull(supportedType, $"{prefix}.supportedTypes[]");
        }

        if (!ValidAttributeAccess.Contains(attr.Access))
        {
            throw new JsonException(
                $"'{prefix}.access' value '{attr.Access}' is not valid. "
                + $"Valid values: {string.Join(", ", ValidAttributeAccess)}.");
        }

        if (!ValidAttributeAvailability.Contains(attr.Availability))
        {
            throw new JsonException(
                $"'{prefix}.availability' value '{attr.Availability}' is not valid. "
                + $"Valid values: {string.Join(", ", ValidAttributeAvailability)}.");
        }

        var isUnknownAttribute = string.Equals(attr.Availability, "unknownAttribute", StringComparison.Ordinal);
        if (isUnknownAttribute)
        {
            if (attr.Source is not null)
            {
                throw new JsonException(
                    $"'{prefix}.source' must be null when availability is 'unknownAttribute' (received '{attr.Source}').");
            }

            if (!string.Equals(attr.Access, "unknown", StringComparison.Ordinal)
                || attr.SupportedTypes.Count != 0
                || attr.Value is not null
                || attr.Diagnostic is null
                || !string.Equals(attr.Diagnostic.Category, "unknown_attribute", StringComparison.Ordinal))
            {
                throw new JsonException(
                    $"'{prefix}' must use access 'unknown', empty supportedTypes, no value, and an "
                    + "'unknown_attribute' diagnostic when availability is 'unknownAttribute'.");
            }
        }
        else
        {
            if (attr.Source is null || !ValidAttributeSource.Contains(attr.Source))
            {
                throw new JsonException(
                    $"'{prefix}.source' value '{attr.Source ?? "null"}' is not valid when availability is '{attr.Availability}'. "
                    + $"Valid values: {string.Join(", ", ValidAttributeSource)}.");
            }
        }

        if (attr.Value is { Kind: var kind } value)
        {
            if (!ValidAttributeValueKind.Contains(kind))
            {
                throw new JsonException(
                    $"'{prefix}.value.kind' value '{kind}' is not valid. "
                    + $"Valid values: {string.Join(", ", ValidAttributeValueKind)}.");
            }

            if (!string.Equals(attr.Availability, "available", StringComparison.Ordinal))
            {
                throw new JsonException(
                    $"'{prefix}.value' must be null when availability is '{attr.Availability}'.");
            }

            ValidateAttributeValue(value, prefix);
        }

        if (attr.Diagnostic is not null)
        {
            RequireNotNull(attr.Diagnostic.Category, $"{prefix}.diagnostic.category");
            RequireNotNull(attr.Diagnostic.Message, $"{prefix}.diagnostic.message");
        }
    }

    private static void ValidateInspectionTarget(NetworkObjectSelectorInfo target)
    {
        if (string.IsNullOrWhiteSpace(target.Kind) || !NetworkObjectKinds.All.Contains(target.Kind))
        {
            throw new JsonException(
                $"'target.kind' value '{target.Kind ?? "null"}' is not a recognised network object kind.");
        }

        switch (target.Kind)
        {
            case NetworkObjectKinds.DeviceItem:
                RequireSelectorText(target.DeviceName, "target.deviceName", target.Kind);
                RequireSelectorPath(target, target.Kind);
                RejectSelectorFields(target, target.Kind,
                    interfaceFields: true, nodeField: true, subnetField: true,
                    numberField: true, connectionFields: true);
                break;

            case NetworkObjectKinds.NetworkInterface:
                RequireSelectorText(target.DeviceName, "target.deviceName", target.Kind);
                RequireSelectorPath(target, target.Kind);
                RejectSelectorFields(target, target.Kind,
                    nodeField: true, subnetField: true, numberField: true, connectionFields: true);
                break;

            case NetworkObjectKinds.Node:
                RequireSelectorText(target.DeviceName, "target.deviceName", target.Kind);
                RequireSelectorText(target.NodeId, "target.nodeId", target.Kind);
                RejectSelectorFields(target, target.Kind,
                    itemPathField: true, interfaceFields: true, subnetField: true,
                    numberField: true, connectionFields: true);
                break;

            case NetworkObjectKinds.Subnet:
                RequireSelectorText(target.SubnetId, "target.subnetId", target.Kind);
                RejectSelectorFields(target, target.Kind,
                    deviceField: true, itemPathField: true, interfaceFields: true,
                    nodeField: true, numberField: true, connectionFields: true);
                break;

            case NetworkObjectKinds.IoSystem:
                RequireSelectorText(target.SubnetId, "target.subnetId", target.Kind);
                if (target.Number is null)
                {
                    throw new JsonException($"'target.number' is required for kind '{target.Kind}'.");
                }

                RejectSelectorFields(target, target.Kind,
                    deviceField: true, itemPathField: true, interfaceFields: true,
                    nodeField: true, connectionFields: true);
                break;

            case NetworkObjectKinds.CommunicationConnection:
                RequireSelectorText(target.DeviceName, "target.deviceName", target.Kind);
                RequireSelectorPath(target, target.Kind);
                if (target.ConnectionIndex is null)
                {
                    throw new JsonException(
                        $"'target.connectionIndex' is required for kind '{target.Kind}'.");
                }

                RequireSelectorText(target.ConnectionType, "target.connectionType", target.Kind);
                RequireSelectorText(target.LocalConnectionName, "target.localConnectionName", target.Kind);
                RejectSelectorFields(target, target.Kind,
                    interfaceFields: true, nodeField: true, subnetField: true, numberField: true);
                break;
        }
    }

    private static void RequireSelectorPath(NetworkObjectSelectorInfo target, string kind)
    {
        if (target.ItemPath is null || target.ItemPath.Count == 0)
        {
            throw new JsonException($"'target.itemPath' must be non-empty for kind '{kind}'.");
        }

        foreach (var segment in target.ItemPath)
        {
            RequireNotNull(segment, "target.itemPath[]");
            RequireNotNull(segment!.Name, "target.itemPath[].name");
            RequireNotNull(segment.TypeIdentifier, "target.itemPath[].typeIdentifier");
        }
    }

    private static void RequireSelectorText(string? value, string member, string kind)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException($"'{member}' is required for kind '{kind}'.");
        }
    }

    private static void RejectSelectorFields(
        NetworkObjectSelectorInfo target,
        string kind,
        bool deviceField = false,
        bool itemPathField = false,
        bool interfaceFields = false,
        bool nodeField = false,
        bool subnetField = false,
        bool numberField = false,
        bool connectionFields = false)
    {
        RejectSelectorField(deviceField && !string.IsNullOrWhiteSpace(target.DeviceName), "deviceName", kind);
        RejectSelectorField(itemPathField && target.ItemPath is not null, "itemPath", kind);
        RejectSelectorField(interfaceFields && !string.IsNullOrWhiteSpace(target.InterfaceName), "interfaceName", kind);
        RejectSelectorField(interfaceFields && !string.IsNullOrWhiteSpace(target.InterfaceType), "interfaceType", kind);
        RejectSelectorField(interfaceFields && !string.IsNullOrWhiteSpace(target.InterfaceOperatingMode), "interfaceOperatingMode", kind);
        RejectSelectorField(nodeField && !string.IsNullOrWhiteSpace(target.NodeId), "nodeId", kind);
        RejectSelectorField(subnetField && !string.IsNullOrWhiteSpace(target.SubnetId), "subnetId", kind);
        RejectSelectorField(numberField && target.Number is not null, "number", kind);
        RejectSelectorField(connectionFields && target.ConnectionIndex is not null, "connectionIndex", kind);
        RejectSelectorField(connectionFields && !string.IsNullOrWhiteSpace(target.ConnectionType), "connectionType", kind);
        RejectSelectorField(connectionFields && !string.IsNullOrWhiteSpace(target.LocalConnectionName), "localConnectionName", kind);
        RejectSelectorField(connectionFields && !string.IsNullOrWhiteSpace(target.LocalConnectionId), "localConnectionId", kind);
    }

    private static void RejectSelectorField(bool isPresent, string member, string kind)
    {
        if (isPresent)
        {
            throw new JsonException($"'target.{member}' is not applicable for kind '{kind}'.");
        }
    }

    private static void ValidateAttributeValue(NetworkAttributeValueInfo value, string prefix)
    {
        if (value.Value is not JsonElement element)
        {
            throw new JsonException($"'{prefix}.value.value' is not a JSON value.");
        }

        var matchesKind = value.Kind switch
        {
            "string" => element.ValueKind == JsonValueKind.String,
            "boolean" => element.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "integer" => element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out _),
            "number" => element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out _),
            "enum" => ValidateEnumValue(element, prefix),
            _ => false,
        };

        if (!matchesKind)
        {
            throw new JsonException(
                $"'{prefix}.value.value' does not match kind '{value.Kind}'.");
        }
    }

    private static bool ValidateEnumValue(JsonElement element, string prefix)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("typeName", out _)
            || !element.TryGetProperty("symbol", out _)
            || !element.TryGetProperty("numericValue", out _))
        {
            return false;
        }

        var enumValue = CanonicalJson.Deserialize<NetworkEnumValueInfo>(element.GetRawText());
        RequireNotNull(enumValue.TypeName, $"{prefix}.value.value.typeName");
        RequireNotNull(enumValue.Symbol, $"{prefix}.value.value.symbol");
        return true;
    }

    private static void RequireNotNull(object? value, string member)
    {
        if (value is null)
        {
            throw new JsonException($"'{member}' is declared non-nullable but the payload was null.");
        }
    }

    private static StructuredOperationItem Failed(
        NetworkOperationRequest operation,
        string category,
        string message,
        IReadOnlyList<string> warnings)
        => new(
            operation.OperationId,
            operation.Operation,
            OperationBatchStatus.Failed,
            Result: null,
            new StructuredOperationFailure(category, message),
            Omission: null,
            SkipReason: null,
            warnings);
}
