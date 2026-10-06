using System.Globalization;
using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Network;

/// <summary>Plans ordinary-read evidence; the shared runner owns sequencing and all mutation.</summary>
public sealed class NetworkWritePlanner(OpennessWorkerClient client)
{
    public async Task<WritePlan<NetworkWriteEffect>> PlanAsync(string? projectPath, IReadOnlyList<NetworkOperationRequest> items)
    {
        var snapshot = await ReadCurrentStateAsync(client, projectPath).ConfigureAwait(false);
        if (!snapshot.Success) return WritePlan<NetworkWriteEffect>.Fail(snapshot.FailureCategory!, snapshot.Error!);
        var plans = new List<ItemPlan<NetworkWriteEffect>>();
        foreach (var item in items)
        {
            var resolved = await ResolveAsync(projectPath, item, snapshot.State!).ConfigureAwait(false);
            if (!resolved.Success) return WritePlan<NetworkWriteEffect>.Fail(resolved.Error!.Category, resolved.Error.Message);
            // Every preceding write can affect uniqueness, root counts or relationships. Preserve
            // initial effects, but refresh before each later dispatch (one extra ordinary read).
            plans.Add(resolved.Plan! with { DependsOn = plans.Count == 0 ? null : items[plans.Count - 1].OperationId });
        }
        return WritePlan<NetworkWriteEffect>.Ok(plans);
    }

    public async Task<ItemReplan<NetworkWriteEffect>> ReplanAsync(string? projectPath, NetworkOperationRequest item)
    {
        var snapshot = await ReadCurrentStateAsync(client, projectPath).ConfigureAwait(false);
        return snapshot.Success ? await ResolveAsync(projectPath, item, snapshot.State!).ConfigureAwait(false)
            : ItemReplan<NetworkWriteEffect>.Fail(snapshot.FailureCategory!, snapshot.Error!);
    }

    private async Task<ItemReplan<NetworkWriteEffect>> ResolveAsync(string? projectPath, NetworkOperationRequest item, HardwareConfigInfo state)
    {
        // Only authoritative ordinary project traversal can establish a complete inventory.
        // Optional metadata diagnostics do not describe omitted candidates.
        if (!DiscoveryComplete(state))
            return ItemReplan<NetworkWriteEffect>.Fail(WorkerFailureCategories.WorkerOperationFailed,
                "Network target discovery is incomplete. Inspect the hardware configuration before retrying.");
        var resolution = NetworkIdentityResolver.Resolve(item, state);
        if (!resolution.Success) return ItemReplan<NetworkWriteEffect>.Fail(resolution.FailureCategory!, resolution.Error!);
        var target = resolution.Evidence!;
        var requested = RequestedSettings(item);
        var current = new Dictionary<string, NetworkAttributeInfo>(StringComparer.Ordinal);
        var affected = new List<NetworkNodeIdentityInfo>();
        bool? complete = true;
        if (item.Operation == "configure_network_device")
        {
            var node = NetworkIdentityResolver.PreparedNode(state, target);
            // Connection keys are actions with no attribute behind them; the snapshot is their only evidence.
            var inspectNames = requested.Keys.Where(key => !IsConnectionKey(key)).ToList();
            if (requested.ContainsKey("PnDeviceName") && !requested.ContainsKey("PnDeviceNameAutoGeneration")) inspectNames.Add("PnDeviceNameAutoGeneration");
            var inspected = inspectNames.Count == 0 ? new()
                : await ReadAttributesAsync(client, projectPath, NodeTarget(target.DeviceName!, target.NodeId!, target.InterfacePath!), inspectNames).ConfigureAwait(false);
            foreach (var key in requested.Keys) current[key] = inspected.GetValueOrDefault(key) ?? Attribute(key, NodeValue(node, key),
                IsConnectionKey(key) ? node.ConnectionEvidence?.Complete == true : NodeValue(node, key) is not null);
            if (PnDeviceNameNotSettable(requested, inspected))
                return ItemReplan<NetworkWriteEffect>.Fail(WorkerFailureCategories.ValidationError,
                    $"Operation '{item.OperationId}': node '{target.NodeId}' of device '{target.DeviceName}' generates its PROFINET device name "
                    + "automatically or reports it as not writable, so changes.pnDeviceName cannot be set. Pass "
                    + "changes.pnDeviceNameAutoGeneration:false together with changes.pnDeviceName to set the name explicitly. No change was made.");
            affected.Add(new() { DeviceName = target.DeviceName!, NodeId = target.NodeId!, InterfacePath = ClonePath(target.InterfacePath!),
                InterfaceName = item.Target?.InterfaceName });
            if (requested.Keys.Any(IsConnectionKey)) complete = node.ConnectionEvidence?.Complete == true;
        }
        else if (item.Operation is "update_subnet" or "delete_subnet")
        {
            var subnet = state.Subnets.Single(value => value.SubnetId == target.SubnetId);
            if (item.Operation == "delete_subnet")
            {
                complete = subnet.ConnectionEvidence?.Complete == true;
                foreach (var identity in subnet.ConnectionEvidence?.Nodes ?? new())
                {
                    // A legacy identity is upgraded only from this fresh, complete ordinary read.
                    if (TryResolveAffected(state, identity, out var qualified, out _)) affected.Add(qualified!);
                    else { complete = false; affected.Add(CloneIdentity(identity)); }
                }
                complete &= affected.Distinct(NetworkNodeIdentityComparer.Instance).Count() == affected.Count;
            }
            var attributes = requested.Count == 0 ? new()
                : await ReadAttributesAsync(client, projectPath, new() { Kind = NetworkObjectKinds.Subnet, SubnetId = target.SubnetId }, requested.Keys.ToArray()).ConfigureAwait(false);
            foreach (var key in requested.Keys)
                current[key] = attributes.GetValueOrDefault(key) ?? Attribute(key, key == "Name" ? subnet.Name : null, key == "Name");
        }
        if (item.Operation is "create_subnet" or "update_subnet" or "delete_subnet")
            complete &= state.RootDeviceCount.HasValue;
        return ItemReplan<NetworkWriteEffect>.Ok(ItemPlan<NetworkWriteEffect>.Resolved(new(
            item.Operation, target, requested, current,
            affected.OrderBy(n => n.DeviceName, StringComparer.Ordinal).ThenBy(n => n.NodeId, StringComparer.Ordinal).ToArray(),
            complete, state.RootDeviceCount), new[]
            {
                new CheckedPrecondition("networkDiscoveryComplete", "true", "true", true),
                new CheckedPrecondition("networkConsequencesReadable", "true", complete == true ? "true" : "false", complete == true)
            }));
    }

    internal static bool DiscoveryComplete(HardwareConfigInfo state) => state.Pagination is null
        && state.DiscoveryEvidence is { Scope: "project", Complete: true, Failures.Count: 0 };

    internal static List<NetworkInterfacePathSegmentInfo> ClonePath(IEnumerable<NetworkInterfacePathSegmentInfo> path) => path.Select(s => new NetworkInterfacePathSegmentInfo
    { Name = s.Name, PositionNumber = s.PositionNumber, TypeIdentifier = s.TypeIdentifier }).ToList();
    internal static NetworkNodeIdentityInfo CloneIdentity(NetworkNodeIdentityInfo identity) => new()
    { DeviceName = identity.DeviceName, NodeId = identity.NodeId, InterfacePath = identity.InterfacePath is null ? null : ClonePath(identity.InterfacePath), InterfaceName = identity.InterfaceName };

    internal static bool TryResolveAffected(HardwareConfigInfo state, NetworkNodeIdentityInfo identity,
        out NetworkNodeIdentityInfo? qualified, out NodeInfo? node)
    {
        qualified = null; node = null;
        if (!DiscoveryComplete(state)) return false;
        var resolution = NetworkIdentityResolver.Resolve(new NetworkOperationRequest { OperationId = "affected", Operation = "configure_network_device",
            Target = new() { DeviceName = identity.DeviceName, NodeId = identity.NodeId, InterfaceName = identity.InterfaceName,
                InterfacePath = identity.InterfacePath?.Select(s => new NetworkInterfacePathSegment
                { Name = s.Name, PositionNumber = s.PositionNumber, TypeIdentifier = s.TypeIdentifier }).ToArray() } }, state);
        if (!resolution.Success || resolution.Evidence!.InterfacePath is null) return false;
        var target = resolution.Evidence;
        node = NetworkIdentityResolver.PreparedNode(state, target);
        qualified = new() { DeviceName = target.DeviceName!, NodeId = target.NodeId!, InterfacePath = ClonePath(target.InterfacePath), InterfaceName = identity.InterfaceName };
        return true;
    }

    internal static IEnumerable<(string DeviceName, NodeInfo Node)> Nodes(HardwareConfigInfo state) =>
        state.Devices.SelectMany(d => DeviceNodes(d.Items).Select(n => (d.Name!, n)));
    private static IEnumerable<NodeInfo> DeviceNodes(IEnumerable<DeviceItemInfo> items) => items.SelectMany(i =>
        i.NetworkInterfaces.SelectMany(n => n.Nodes).Concat(DeviceNodes(i.Items)));
    internal static bool NamesEqual(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    internal static string? NodeValue(NodeInfo node, string key) => key switch
    {
        "Address" => node.IpAddress, "SubnetMask" => node.SubnetMask, "PnDeviceName" => node.PnDeviceName,
        "Subnet" => node.ConnectionEvidence?.SubnetId,
        "IoSystemSubnet" => node.ConnectionEvidence?.IoSystemSubnetId,
        "IoSystemNumber" => node.ConnectionEvidence?.IoSystemNumber?.ToString(CultureInfo.InvariantCulture),
        _ => null
    };
    // The requested IoSystem relationship is reported as two scalar keys: its subnet and number.
    internal static bool IsConnectionKey(string key) => key is "Subnet" or "IoSystemSubnet" or "IoSystemNumber";
    // Snapshot fallback when inspection is unavailable: the value is known, its access is not.
    internal static NetworkAttributeInfo Attribute(string name, string? value, bool readable) => new()
    {
        Name = name, Source = "modeled", Access = "unknown", Availability = readable ? "available" : "unreadable",
        Value = readable ? new() { Kind = value is null ? "null" : "string", Value = value } : null
    };
    internal static Dictionary<string, string> RequestedSettings(NetworkOperationRequest item)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string key, string? value) { if (value is not null) result.Add(key, value); }
        if (item.Operation == "add_network_device")
        { Add("deviceName", item.DeviceName); Add("deviceItemName", item.DeviceItemName ?? item.DeviceName); Add("typeIdentifier", item.TypeIdentifier); }
        if (item.Changes is { } changes)
        {
            Add("Address", changes.IpAddress); Add("SubnetMask", changes.SubnetMask);
            Add("PnDeviceNameAutoGeneration", changes.PnDeviceNameAutoGeneration is { } generated ? generated ? "true" : "false" : null);
            Add("PnDeviceName", changes.PnDeviceName);
            Add("Subnet", changes.Subnet?.SubnetId);
            Add("IoSystemSubnet", changes.IoSystem?.SubnetId);
            Add("IoSystemNumber", changes.IoSystem?.Number?.ToString(CultureInfo.InvariantCulture));
        }
        if (item.Subnet is { } subnet)
        { Add("Name", subnet.Name); Add("TypeIdentifier", "System:Subnet." + subnet.NetworkType); Add("HighestAddress", subnet.HighestAddress?.ToString(CultureInfo.InvariantCulture)); Add("TransmissionSpeed", subnet.TransmissionSpeed); }
        if (item.SubnetChanges is { } update)
        { Add("Name", update.Name); Add("HighestAddress", update.HighestAddress?.ToString(CultureInfo.InvariantCulture)); Add("TransmissionSpeed", update.TransmissionSpeed); }
        return result;
    }

    // Fails before any mutation when PnDeviceName is requested without opting out of automatic
    // generation and inspection shows the name is generated or not writable. When the flag cannot
    // be read the request proceeds as before: live acceptance must confirm how V21 reports it.
    private static bool PnDeviceNameNotSettable(IReadOnlyDictionary<string, string> requested, IReadOnlyDictionary<string, NetworkAttributeInfo> inspected)
        => requested.ContainsKey("PnDeviceName") && requested.GetValueOrDefault("PnDeviceNameAutoGeneration") != "false"
            && (inspected.GetValueOrDefault("PnDeviceNameAutoGeneration")?.Value?.Value is JsonElement { ValueKind: JsonValueKind.True }
                || inspected.GetValueOrDefault("PnDeviceName")?.Access is "readOnly" or "none");

    internal static NetworkObjectTarget NodeTarget(string deviceName, string nodeId, IEnumerable<NetworkInterfacePathSegmentInfo> path) => new()
    {
        Kind = NetworkObjectKinds.Node, DeviceName = deviceName, NodeId = nodeId,
        InterfacePath = path.Select(s => new NetworkInterfacePathSegment { Name = s.Name, PositionNumber = s.PositionNumber, TypeIdentifier = s.TypeIdentifier }).ToArray()
    };

    /// <summary>Inspects one exact node or subnet. Returns only uniquely named, available attributes;
    /// a diagnostic about another attribute no longer discards the readable ones.</summary>
    internal static async Task<Dictionary<string, NetworkAttributeInfo>> ReadAttributesAsync(OpennessWorkerClient client, string? projectPath, NetworkObjectTarget target, IReadOnlyList<string> names)
    {
        var request = new NetworkOperationRequest { OperationId = "verify", Operation = "inspect_network_object", ProjectPath = projectPath,
            Target = target, AttributeNames = names };
        var projected = NetworkPayloadContract.Project(request, await NetworkWorkerInvoker.InvokeReadAsync(client, request).ConfigureAwait(false));
        if (projected.Result is not { } result) return new();
        var inspection = CanonicalJson.Deserialize<NetworkObjectInspectionInfo>(result.GetRawText());
        if (inspection.Target.Kind != target.Kind || inspection.Target.SubnetId != target.SubnetId || inspection.Target.NodeId != target.NodeId
            || !NamesEqual(inspection.Target.DeviceName, target.DeviceName)) return new();
        return inspection.Attributes.GroupBy(a => a.Name, StringComparer.Ordinal).Where(g => g.Count() == 1 && g.Single().Availability == "available")
            .ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
    }

    public static async Task<NetworkStateSnapshot> ReadCurrentStateAsync(OpennessWorkerClient client, string? projectPath)
    {
        var result = await client.ReadHardwareConfigAsync(projectPath).ConfigureAwait(false);
        if (!result.Success) return NetworkStateSnapshot.Fail(result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
            result.Error ?? "The hardware configuration could not be read.");
        try { return NetworkStateSnapshot.Ok(NetworkPayloadContract.DecodeHardwareConfig(result.Payload)); }
        catch (JsonException)
        {
            return NetworkStateSnapshot.Fail(WorkerFailureCategories.ProtocolError,
                "The hardware configuration payload did not match its declared result contract and was rejected.");
        }
    }
}
