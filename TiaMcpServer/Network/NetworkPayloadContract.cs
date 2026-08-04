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

        if (value.TotalCount is { } total && value.Items!.Count > total)
        {
            throw new JsonException(
                $"'items' count ({value.Items.Count}) exceeds 'totalCount' ({total}).");
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

    private static void ValidateObjectInspection(NetworkObjectInspectionInfo value)
    {
        RequireNotNull(value.Attributes, "attributes");
        RequireNotNull(value.Messages, "messages");

        if (value.Kind is not null && !NetworkObjectKinds.All.Contains(value.Kind))
        {
            throw new JsonException($"'kind' value '{value.Kind}' is not a recognised network object kind.");
        }

        // Duplicate attribute names are a protocol error: the worker must return each name at most once.
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attr in value.Attributes!)
        {
            RequireNotNull(attr, "attributes[]");
            if (attr!.Name is not null && !seenNames.Add(attr.Name))
            {
                throw new JsonException($"Duplicate attribute name '{attr.Name}' in 'attributes'.");
            }
        }
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
