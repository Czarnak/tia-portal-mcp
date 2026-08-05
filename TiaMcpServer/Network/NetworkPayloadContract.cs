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
        "list_network_objects" => DecodeObjectList(payload),
        "inspect_network_object" => DecodeObjectInspection(payload),
        _ => throw new JsonException($"No declared result contract for network operation '{operation}'."),
    };

    private static JsonElement Decode<T>(string payload, Action<T> validate)
        => CanonicalJson.Normalize(payload, validate).Element;

    private static JsonElement DecodeObjectList(string payload)
    {
        ValidateRequiredPathIndexMembers(payload, listPayload: true);
        return Decode<NetworkObjectListInfo>(payload, ValidateObjectList);
    }

    private static JsonElement DecodeObjectInspection(string payload)
    {
        ValidateRequiredPathIndexMembers(payload, listPayload: false);
        return Decode<NetworkObjectInspectionInfo>(payload, ValidateObjectInspection);
    }

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
        if (string.IsNullOrWhiteSpace(item.Kind) || !NetworkObjectKinds.All.Contains(item.Kind))
        {
            throw new JsonException(
                $"'items[].kind' value '{item.Kind ?? "null"}' is not a recognised network object kind.");
        }

        RequireNotNull(item.SelectorDiagnostics, "items[].selectorDiagnostics");
        foreach (var diagnostic in item.SelectorDiagnostics)
        {
            RequireNotNull(diagnostic, "items[].selectorDiagnostics[]");
        }

        if (item.Selector is not null)
        {
            ValidateSelector(item.Selector, "items[].selector", item.Kind);
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
        ValidateSelector(value.Target, "target");

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

    private static void ValidateSelector(
        NetworkObjectSelectorInfo target,
        string prefix,
        string? expectedKind = null)
    {
        if (string.IsNullOrWhiteSpace(target.Kind) || !NetworkObjectKinds.All.Contains(target.Kind))
        {
            throw new JsonException(
                $"'{prefix}.kind' value '{target.Kind ?? "null"}' is not a recognised network object kind.");
        }

        if (expectedKind is not null && !string.Equals(target.Kind, expectedKind, StringComparison.Ordinal))
        {
            throw new JsonException(
                $"'{prefix}.kind' value '{target.Kind}' does not match summary kind '{expectedKind}'.");
        }

        switch (target.Kind)
        {
            case NetworkObjectKinds.DeviceItem:
                RequireSelectorText(target.DeviceName, $"{prefix}.deviceName", target.Kind);
                RequireSelectorPath(target, prefix, target.Kind);
                RejectSelectorFields(target, prefix, target.Kind,
                    interfaceFields: true, nodeField: true, subnetField: true,
                    numberField: true, connectionFields: true);
                break;

            case NetworkObjectKinds.NetworkInterface:
                RequireSelectorText(target.DeviceName, $"{prefix}.deviceName", target.Kind);
                RequireSelectorPath(target, prefix, target.Kind);
                RequireOptionalSelectorText(target.InterfaceName, $"{prefix}.interfaceName", target.Kind);
                RequireOptionalSelectorText(target.InterfaceType, $"{prefix}.interfaceType", target.Kind);
                RequireOptionalSelectorText(target.InterfaceOperatingMode, $"{prefix}.interfaceOperatingMode", target.Kind);
                RejectSelectorFields(target, prefix, target.Kind,
                    nodeField: true, subnetField: true, numberField: true, connectionFields: true);
                break;

            case NetworkObjectKinds.Node:
                RequireSelectorText(target.DeviceName, $"{prefix}.deviceName", target.Kind);
                RequireSelectorText(target.NodeId, $"{prefix}.nodeId", target.Kind);
                RejectSelectorFields(target, prefix, target.Kind,
                    itemPathField: true, interfaceFields: true, subnetField: true,
                    numberField: true, connectionFields: true);
                break;

            case NetworkObjectKinds.Subnet:
                RequireSelectorText(target.SubnetId, $"{prefix}.subnetId", target.Kind);
                RejectSelectorFields(target, prefix, target.Kind,
                    deviceField: true, itemPathField: true, interfaceFields: true,
                    nodeField: true, numberField: true, connectionFields: true);
                break;

            case NetworkObjectKinds.IoSystem:
                RequireSelectorText(target.SubnetId, $"{prefix}.subnetId", target.Kind);
                if (target.Number is null)
                {
                    throw new JsonException($"'{prefix}.number' is required for kind '{target.Kind}'.");
                }

                RejectSelectorFields(target, prefix, target.Kind,
                    deviceField: true, itemPathField: true, interfaceFields: true,
                    nodeField: true, connectionFields: true);
                break;

            case NetworkObjectKinds.CommunicationConnection:
                RequireSelectorText(target.DeviceName, $"{prefix}.deviceName", target.Kind);
                RequireSelectorPath(target, prefix, target.Kind);
                if (target.ConnectionIndex is null)
                {
                    throw new JsonException(
                        $"'{prefix}.connectionIndex' is required for kind '{target.Kind}'.");
                }

                RequireSelectorText(target.ConnectionType, $"{prefix}.connectionType", target.Kind);
                RequireSelectorText(target.LocalConnectionName, $"{prefix}.localConnectionName", target.Kind);
                RequireOptionalSelectorText(target.LocalConnectionId, $"{prefix}.localConnectionId", target.Kind);
                RejectSelectorFields(target, prefix, target.Kind,
                    interfaceFields: true, nodeField: true, subnetField: true, numberField: true);
                break;
        }
    }

    private static void RequireSelectorPath(NetworkObjectSelectorInfo target, string prefix, string kind)
    {
        if (target.ItemPath is null || target.ItemPath.Count == 0)
        {
            throw new JsonException($"'{prefix}.itemPath' must be non-empty for kind '{kind}'.");
        }

        foreach (var segment in target.ItemPath)
        {
            RequireNotNull(segment, $"{prefix}.itemPath[]");
            if (segment!.Index < 0)
            {
                throw new JsonException($"'{prefix}.itemPath[].index' must not be negative.");
            }

            RequireSelectorText(segment.Name, $"{prefix}.itemPath[].name", kind);
            if (segment.PositionNumber is null)
            {
                throw new JsonException($"'{prefix}.itemPath[].positionNumber' is required for kind '{kind}'.");
            }

            RequireSelectorText(segment.TypeIdentifier, $"{prefix}.itemPath[].typeIdentifier", kind);
        }
    }

    private static void RequireSelectorText(string? value, string member, string kind)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException($"'{member}' is required for kind '{kind}'.");
        }
    }

    private static void RequireOptionalSelectorText(string? value, string member, string kind)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException($"'{member}' must be nonblank when supplied for kind '{kind}'.");
        }
    }

    private static void RejectSelectorFields(
        NetworkObjectSelectorInfo target,
        string prefix,
        string kind,
        bool deviceField = false,
        bool itemPathField = false,
        bool interfaceFields = false,
        bool nodeField = false,
        bool subnetField = false,
        bool numberField = false,
        bool connectionFields = false)
    {
        RejectSelectorField(deviceField && target.DeviceName is not null, prefix, "deviceName", kind);
        RejectSelectorField(itemPathField && target.ItemPath is not null, prefix, "itemPath", kind);
        RejectSelectorField(interfaceFields && target.InterfaceName is not null, prefix, "interfaceName", kind);
        RejectSelectorField(interfaceFields && target.InterfaceType is not null, prefix, "interfaceType", kind);
        RejectSelectorField(interfaceFields && target.InterfaceOperatingMode is not null, prefix, "interfaceOperatingMode", kind);
        RejectSelectorField(nodeField && target.NodeId is not null, prefix, "nodeId", kind);
        RejectSelectorField(subnetField && target.SubnetId is not null, prefix, "subnetId", kind);
        RejectSelectorField(numberField && target.Number is not null, prefix, "number", kind);
        RejectSelectorField(connectionFields && target.ConnectionIndex is not null, prefix, "connectionIndex", kind);
        RejectSelectorField(connectionFields && target.ConnectionType is not null, prefix, "connectionType", kind);
        RejectSelectorField(connectionFields && target.LocalConnectionName is not null, prefix, "localConnectionName", kind);
        RejectSelectorField(connectionFields && target.LocalConnectionId is not null, prefix, "localConnectionId", kind);
    }

    private static void RejectSelectorField(bool isPresent, string prefix, string member, string kind)
    {
        if (isPresent)
        {
            throw new JsonException($"'{prefix}.{member}' is not applicable for kind '{kind}'.");
        }
    }

    private static void ValidateRequiredPathIndexMembers(string payload, bool listPayload)
    {
        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (listPayload)
        {
            if (!document.RootElement.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("selector", out var selector)
                    && selector.ValueKind == JsonValueKind.Object)
                {
                    ValidateRequiredPathIndexMembers(selector, "items[].selector");
                }
            }

            return;
        }

        if (document.RootElement.TryGetProperty("target", out var target)
            && target.ValueKind == JsonValueKind.Object)
        {
            ValidateRequiredPathIndexMembers(target, "target");
        }
    }

    private static void ValidateRequiredPathIndexMembers(JsonElement selector, string prefix)
    {
        if (!selector.TryGetProperty("itemPath", out var itemPath)
            || itemPath.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var segment in itemPath.EnumerateArray())
        {
            if (segment.ValueKind == JsonValueKind.Object && !segment.TryGetProperty("index", out _))
            {
                throw new JsonException($"'{prefix}.itemPath[].index' is required.");
            }
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
