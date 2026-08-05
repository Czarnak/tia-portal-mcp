using System.Diagnostics;
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
        => Project(operation, workerResult, Console.Error.WriteLine);

    internal static StructuredOperationItem Project(
        NetworkOperationRequest operation,
        WorkerCallResult workerResult,
        Action<string> writeProtocolDiagnostic)
    {
        ArgumentNullException.ThrowIfNull(writeProtocolDiagnostic);
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
        catch (JsonException exception)
        {
            TryWriteProtocolDiagnostic(writeProtocolDiagnostic, operation.Operation, exception);
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

    private static void TryWriteProtocolDiagnostic(
        Action<string> writeProtocolDiagnostic,
        string operation,
        JsonException exception)
    {
        try
        {
            var validators = new StackTrace(exception, fNeedFileInfo: false)
                .GetFrames()
                .Select(frame => frame.GetMethod())
                .Where(method => method?.DeclaringType == typeof(NetworkPayloadContract))
                .Select(method => method!.Name)
                .Where(name => !string.Equals(name, nameof(Project), StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Take(6)
                .ToArray();
            var validatorChain = validators.Length == 0
                ? "unavailable"
                : string.Join(">", validators);
            var diagnostic = "TiaMcpServer: worker payload contract rejection: "
                + $"operation={operation}; validators={validatorChain}.";
            writeProtocolDiagnostic(diagnostic.Length <= 512 ? diagnostic : diagnostic[..512]);
        }
        catch
        {
            // Diagnostics must never replace the stable fail-closed protocol_error response.
        }
    }

    /// <summary>
    /// Decodes a <c>read_hardware_config</c> payload under the same contract the batch path uses,
    /// returning the typed value. Callers that bind safety tokens to hardware state need the value
    /// itself, not a batch item. Throws <see cref="JsonException"/> when the payload does not match.
    /// </summary>
    public static HardwareConfigInfo DecodeHardwareConfig(string payload)
    {
        ValidateRequiredHardwarePathMembers(payload);
        return CanonicalJson.Normalize<HardwareConfigInfo>(payload, ValidateHardwareConfig).Value;
    }

    private static JsonElement Decode(string operation, string payload) => operation switch
    {
        "read_hardware_config" => DecodeHardwareConfigElement(payload),
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

    private static JsonElement DecodeHardwareConfigElement(string payload)
    {
        ValidateRequiredHardwarePathMembers(payload);
        return Decode<HardwareConfigInfo>(payload, ValidateHardwareConfig);
    }

    private static JsonElement DecodeObjectList(string payload)
    {
        ValidateRequiredListMembers(payload);
        ValidateRequiredPathIndexMembers(payload, listPayload: true);
        return Decode<NetworkObjectListInfo>(payload, ValidateObjectList);
    }

    private static JsonElement DecodeObjectInspection(string payload)
    {
        ValidateRequiredPathIndexMembers(payload, listPayload: false);
        ValidateRequiredAttributeValueMembers(payload);
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
            ValidateHardwareSelector(
                subnet.Selectable,
                subnet.Selector,
                subnet.SelectorDiagnostics,
                "subnets[]",
                NetworkObjectKinds.Subnet);
            foreach (var ioSystem in subnet.IoSystems)
            {
                RequireNotNull(ioSystem, "subnets[].ioSystems[]");
                RequireNotNull(ioSystem.ConnectedDeviceNames, "subnets[].ioSystems[].connectedDeviceNames");
                ValidateHardwareSelector(
                    ioSystem.Selectable,
                    ioSystem.Selector,
                    ioSystem.SelectorDiagnostics,
                    "subnets[].ioSystems[]",
                    NetworkObjectKinds.IoSystem);
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
        RequireNotNull(item.CommunicationConnections, $"{path}.communicationConnections");
        ValidateHardwareSelector(
            item.Selectable,
            item.Selector,
            item.SelectorDiagnostics,
            path,
            NetworkObjectKinds.DeviceItem);

        foreach (var connection in item.CommunicationConnections)
        {
            RequireNotNull(connection, $"{path}.communicationConnections[]");
            RequireNotNull(connection.ConnectionType, $"{path}.communicationConnections[].connectionType");
            RequireNotNull(
                connection.LocalConnectionName,
                $"{path}.communicationConnections[].localConnectionName");
            ValidateHardwareSelector(
                connection.Selectable,
                connection.Selector,
                connection.SelectorDiagnostics,
                $"{path}.communicationConnections[]",
                NetworkObjectKinds.CommunicationConnection);
        }

        foreach (var networkInterface in item.NetworkInterfaces)
        {
            RequireNotNull(networkInterface, $"{path}.networkInterfaces[]");
            RequireNotNull(networkInterface!.Nodes, $"{path}.networkInterfaces[].nodes");
            ValidateHardwareSelector(
                networkInterface.Selectable,
                networkInterface.Selector,
                networkInterface.SelectorDiagnostics,
                $"{path}.networkInterfaces[]",
                NetworkObjectKinds.NetworkInterface);
            foreach (var node in networkInterface.Nodes)
            {
                RequireNotNull(node, $"{path}.networkInterfaces[].nodes[]");
                ValidateHardwareSelector(
                    node!.Selectable,
                    node.Selector,
                    node.SelectorDiagnostics,
                    $"{path}.networkInterfaces[].nodes[]",
                    NetworkObjectKinds.Node);
            }
        }

        foreach (var child in item.Items)
        {
            ValidateDeviceItem(child, $"{path}.items[]");
        }
    }

    private static void ValidateHardwareSelector(
        bool selectable,
        NetworkObjectSelectorInfo? selector,
        List<string>? diagnostics,
        string path,
        string expectedKind)
    {
        RequireNotNull(diagnostics, $"{path}.selectorDiagnostics");
        foreach (var diagnostic in diagnostics!)
        {
            if (string.IsNullOrWhiteSpace(diagnostic))
            {
                throw new JsonException(
                    $"'{path}.selectorDiagnostics[]' must contain only non-blank strings.");
            }
        }

        if (selectable != (selector is not null))
        {
            throw new JsonException(
                $"'{path}.selectable' must agree exactly with '{path}.selector' presence.");
        }

        if (selectable)
        {
            if (diagnostics.Count != 0)
            {
                throw new JsonException(
                    $"'{path}.selectorDiagnostics' must be empty when the object is selectable.");
            }

            ValidateSelector(selector!, $"{path}.selector", expectedKind);
            return;
        }

        if (diagnostics.Count == 0)
        {
            throw new JsonException(
                $"'{path}.selectorDiagnostics' must explain why the object is unselectable.");
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

        if (value.TotalCount < 0)
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

        if (value.ReturnedCount > value.TotalCount)
        {
            throw new JsonException(
                $"'returnedCount' ({value.ReturnedCount}) exceeds 'totalCount' ({value.TotalCount}).");
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

        RequireNotNull(item.Evidence, "items[].evidence");
        RequireNotNull(item.Evidence.DeviceItemPath, "items[].evidence.deviceItemPath");
        foreach (var segment in item.Evidence.DeviceItemPath)
        {
            RequireNotNull(segment, "items[].evidence.deviceItemPath[]");
        }

        RequireNotNull(item.Diagnostics, "items[].diagnostics");
        foreach (var diagnostic in item.Diagnostics)
        {
            RequireNotNull(diagnostic, "items[].diagnostics[]");
        }

        if (item.Selectable != (item.Selector is not null))
        {
            throw new JsonException(
                "'items[].selectable' must agree with whether 'items[].selector' is present.");
        }

        if (!item.Selectable
            && !item.Diagnostics.Any(diagnostic => !string.IsNullOrWhiteSpace(diagnostic)))
        {
            throw new JsonException(
                "An unselectable item requires at least one nonblank selector diagnostic.");
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

    private static readonly IReadOnlySet<string> SupportedConnectionTypes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "S7Connection",
            "FdlConnection",
            "IsoConnection",
            "IsoOnTcpConnection",
            "PtpConnection",
            "TcpConnection",
            "UdpConnection",
            "HmiConnection",
        };

    private static readonly IReadOnlySet<string> ValidAttributeValueKind =
        new HashSet<string>(StringComparer.Ordinal)
            { "null", "string", "boolean", "integer", "number", "enum" };

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

        if (string.Equals(attr.Availability, "available", StringComparison.Ordinal)
            && attr.Value is null)
        {
            throw new JsonException(
                $"'{prefix}.value' must use a typed value object when availability is 'available'.");
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
                    numberField: true, ioSystemFields: true, connectionFields: true);
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
                if ((target.ItemPath is null) != (target.NodeIndex is null))
                {
                    throw new JsonException(
                        $"'{prefix}.itemPath' and '{prefix}.nodeIndex' must be supplied together for kind '{target.Kind}'.");
                }
                if (target.ItemPath is not null)
                {
                    RequireSelectorPath(target, prefix, target.Kind);
                }
                if (target.NodeIndex < 0)
                {
                    throw new JsonException($"'{prefix}.nodeIndex' must not be negative.");
                }
                RejectSelectorFields(target, prefix, target.Kind,
                    interfaceFields: true, subnetField: true,
                    numberField: true, ioSystemFields: true, connectionFields: true);
                break;

            case NetworkObjectKinds.Subnet:
                RequireSelectorText(target.SubnetId, $"{prefix}.subnetId", target.Kind);
                RejectSelectorFields(target, prefix, target.Kind,
                    deviceField: true, itemPathField: true, interfaceFields: true,
                    nodeField: true, numberField: true, ioSystemFields: true, connectionFields: true);
                break;

            case NetworkObjectKinds.IoSystem:
                RequireSelectorText(target.SubnetId, $"{prefix}.subnetId", target.Kind);
                if (target.Number is null)
                {
                    throw new JsonException($"'{prefix}.number' is required for kind '{target.Kind}'.");
                }
                if (target.Number < 0)
                {
                    throw new JsonException($"'{prefix}.number' must not be negative.");
                }
                if (target.IoSystemIndex < 0)
                {
                    throw new JsonException($"'{prefix}.ioSystemIndex' must not be negative.");
                }
                RequireOptionalSelectorText(target.IoSystemName, $"{prefix}.ioSystemName", target.Kind);

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
                if (target.ConnectionIndex < 0)
                {
                    throw new JsonException($"'{prefix}.connectionIndex' must not be negative.");
                }

                RequireSelectorText(target.ConnectionType, $"{prefix}.connectionType", target.Kind);
                if (!SupportedConnectionTypes.Contains(target.ConnectionType!))
                {
                    throw new JsonException(
                        $"'{prefix}.connectionType' value '{target.ConnectionType}' is not supported.");
                }
                RequireSelectorText(target.LocalConnectionName, $"{prefix}.localConnectionName", target.Kind);
                if (string.Equals(target.ConnectionType, "HmiConnection", StringComparison.Ordinal))
                {
                    if (target.LocalConnectionId is not null)
                    {
                        throw new JsonException(
                            $"'{prefix}.localConnectionId' is not applicable for connection type 'HmiConnection'.");
                    }
                }
                else
                {
                    RequireSelectorText(target.LocalConnectionId, $"{prefix}.localConnectionId", target.Kind);
                }
                RejectSelectorFields(target, prefix, target.Kind,
                    interfaceFields: true, nodeField: true, subnetField: true,
                    numberField: true, ioSystemFields: true);
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
            if (segment.PositionNumber < 0)
            {
                throw new JsonException($"'{prefix}.itemPath[].positionNumber' must not be negative.");
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
        bool ioSystemFields = false,
        bool connectionFields = false)
    {
        RejectSelectorField(deviceField && target.DeviceName is not null, prefix, "deviceName", kind);
        RejectSelectorField(itemPathField && target.ItemPath is not null, prefix, "itemPath", kind);
        RejectSelectorField(interfaceFields && target.InterfaceName is not null, prefix, "interfaceName", kind);
        RejectSelectorField(interfaceFields && target.InterfaceType is not null, prefix, "interfaceType", kind);
        RejectSelectorField(interfaceFields && target.InterfaceOperatingMode is not null, prefix, "interfaceOperatingMode", kind);
        RejectSelectorField(nodeField && target.NodeId is not null, prefix, "nodeId", kind);
        RejectSelectorField(nodeField && target.NodeIndex is not null, prefix, "nodeIndex", kind);
        RejectSelectorField(subnetField && target.SubnetId is not null, prefix, "subnetId", kind);
        RejectSelectorField(numberField && target.Number is not null, prefix, "number", kind);
        RejectSelectorField(ioSystemFields && target.IoSystemIndex is not null, prefix, "ioSystemIndex", kind);
        RejectSelectorField(ioSystemFields && target.IoSystemName is not null, prefix, "ioSystemName", kind);
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

    private static void ValidateRequiredListMembers(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        RequireJsonMembers(
            root,
            "list_network_objects",
            "items",
            "totalCount",
            "returnedCount",
            "nextCursor");

        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                RequireJsonMembers(
                    item,
                    "items[]",
                    "kind",
                    "selectable",
                    "selector",
                    "evidence",
                    "diagnostics");
            }
        }
    }

    private static void ValidateRequiredHardwarePathMembers(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        ValidateRequiredHardwarePathMembers(document.RootElement, "read_hardware_config");
    }

    private static void ValidateRequiredHardwarePathMembers(JsonElement value, string prefix)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                ValidateRequiredHardwarePathMembers(item, $"{prefix}[{index}]");
                index++;
            }

            return;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in value.EnumerateObject())
        {
            var propertyPrefix = $"{prefix}.{property.Name}";
            if (property.NameEquals("selector")
                && property.Value.ValueKind == JsonValueKind.Object)
            {
                ValidateRequiredPathIndexMembers(property.Value, propertyPrefix);
            }

            ValidateRequiredHardwarePathMembers(property.Value, propertyPrefix);
        }
    }

    private static void RequireJsonMembers(
        JsonElement value,
        string prefix,
        params string[] members)
    {
        foreach (var member in members)
        {
            if (!value.TryGetProperty(member, out _))
            {
                throw new JsonException($"'{prefix}.{member}' is required.");
            }
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
            if (segment.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var member in new[] { "index", "name", "positionNumber", "typeIdentifier" })
            {
                if (!segment.TryGetProperty(member, out _))
                {
                    throw new JsonException($"'{prefix}.itemPath[].{member}' is required.");
                }
            }
        }
    }

    private static void ValidateAttributeValue(NetworkAttributeValueInfo value, string prefix)
    {
        if (string.Equals(value.Kind, "null", StringComparison.Ordinal))
        {
            if (value.Value is null
                || value.Value is JsonElement { ValueKind: JsonValueKind.Null })
            {
                return;
            }

            throw new JsonException(
                $"'{prefix}.value.value' does not match kind 'null'.");
        }

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

    private static void ValidateRequiredAttributeValueMembers(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("attributes", out var attributes)
            || attributes.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var attribute in attributes.EnumerateArray())
        {
            if (attribute.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var isAvailable = attribute.TryGetProperty("availability", out var availability)
                && availability.ValueKind == JsonValueKind.String
                && string.Equals(availability.GetString(), "available", StringComparison.Ordinal);
            if (!attribute.TryGetProperty("value", out var value))
            {
                if (isAvailable)
                {
                    throw new JsonException("'attributes[].value' is required when availability is 'available'.");
                }

                continue;
            }

            if (value.ValueKind == JsonValueKind.Object)
            {
                RequireJsonMembers(value, "attributes[].value", "kind", "value");
            }
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
