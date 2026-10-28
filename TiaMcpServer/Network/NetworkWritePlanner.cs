using System.Globalization;
using System.Text.Json;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Network;

/// <summary>Plans ordinary-read evidence; the shared runner owns sequencing and all mutation.</summary>
public sealed class NetworkWritePlanner(OpennessWorkerClient client)
{
    public async Task<WritePlan<NetworkWriteEffect>> PlanAsync(IReadOnlyList<NetworkOperationRequest> items)
    {
        var snapshot = await ReadCurrentStateAsync(client).ConfigureAwait(false);
        if (!snapshot.Success) return WritePlan<NetworkWriteEffect>.Fail(snapshot.FailureCategory!, snapshot.Error!);
        var plans = new List<ItemPlan<NetworkWriteEffect>>();
        foreach (var item in items)
        {
            var resolved = Resolve(item, snapshot.State!);
            if (!resolved.Success) return WritePlan<NetworkWriteEffect>.Fail(resolved.Error!.Category, resolved.Error.Message);
            plans.Add(resolved.Plan!);
        }
        return WritePlan<NetworkWriteEffect>.Ok(plans);
    }

    private static ItemReplan<NetworkWriteEffect> Resolve(NetworkOperationRequest item, HardwareConfigInfo state)
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
        var current = new Dictionary<string, string?>(StringComparer.Ordinal);
        var affected = new List<NetworkNodeIdentityInfo>();
        bool? complete = true;
        if (item.Operation == "configure_network_device")
        {
            var node = NetworkIdentityResolver.PreparedNode(state, target);
            foreach (var key in requested.Keys) current[key] = NodeValue(node, key);
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
            foreach (var key in requested.Keys) current[key] = key == "Name" ? subnet.Name : null;
        }
        var preconditions = new List<CheckedPrecondition>
        {
            new("networkDiscoveryComplete", "true", "true", true)
        };
        if (item.Operation == "delete_subnet")
            preconditions.Add(new("networkConsequencesReadable", "true", complete == true ? "true" : "false", complete == true));
        return ItemReplan<NetworkWriteEffect>.Ok(ItemPlan<NetworkWriteEffect>.Resolved(new(
            item.Operation, target, requested, current,
            affected.OrderBy(n => n.DeviceName, StringComparer.Ordinal).ThenBy(n => n.NodeId, StringComparer.Ordinal).ToArray(),
            complete), preconditions));
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
        NetworkSettingKeys.Address => node.IpAddress, NetworkSettingKeys.SubnetMask => node.SubnetMask, NetworkSettingKeys.PnDeviceName => node.PnDeviceName,
        NetworkSettingKeys.Subnet => node.ConnectionEvidence?.SubnetId,
        NetworkSettingKeys.IoSystemSubnet => node.ConnectionEvidence?.IoSystemSubnetId,
        NetworkSettingKeys.IoSystemNumber => node.ConnectionEvidence?.IoSystemNumber?.ToString(CultureInfo.InvariantCulture),
        _ => null
    };
    // The requested IoSystem relationship is reported as two scalar keys: its subnet and number.
    internal static bool IsConnectionKey(string key) => key is NetworkSettingKeys.Subnet or NetworkSettingKeys.IoSystemSubnet or NetworkSettingKeys.IoSystemNumber;
    internal static Dictionary<string, string> RequestedSettings(NetworkOperationRequest item)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string key, string? value) { if (value is not null) result.Add(key, value); }
        if (item.Operation == "add_network_device")
        { Add("deviceName", item.DeviceName); Add("deviceItemName", item.DeviceItemName ?? item.DeviceName); Add("typeIdentifier", item.TypeIdentifier); }
        if (item.Changes is { } changes)
        {
            Add(NetworkSettingKeys.Address, changes.IpAddress); Add(NetworkSettingKeys.SubnetMask, changes.SubnetMask);
            Add(NetworkSettingKeys.PnDeviceNameAutoGeneration, changes.PnDeviceNameAutoGeneration is { } generated ? generated ? "true" : "false" : null);
            Add(NetworkSettingKeys.PnDeviceName, changes.PnDeviceName);
            Add(NetworkSettingKeys.Subnet, changes.Subnet?.SubnetId);
            Add(NetworkSettingKeys.IoSystemSubnet, changes.IoSystem?.SubnetId);
            Add(NetworkSettingKeys.IoSystemNumber, changes.IoSystem?.Number?.ToString(CultureInfo.InvariantCulture));
        }
        if (item.Subnet is { } subnet)
        { Add("Name", subnet.Name); Add("TypeIdentifier", SubnetLifecycleContract.TypeIdentifierFor(subnet.NetworkType)); Add("HighestAddress", subnet.HighestAddress?.ToString(CultureInfo.InvariantCulture)); Add("TransmissionSpeed", subnet.TransmissionSpeed); }
        if (item.SubnetChanges is { } update)
        { Add("Name", update.Name); Add("HighestAddress", update.HighestAddress?.ToString(CultureInfo.InvariantCulture)); Add("TransmissionSpeed", update.TransmissionSpeed); }
        return result;
    }

    public static async Task<NetworkStateSnapshot> ReadCurrentStateAsync(OpennessWorkerClient client)
    {
        var result = await client.ReadHardwareConfigAsync().ConfigureAwait(false);
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
