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
            var node = Nodes(state).Single(pair => NamesEqual(pair.DeviceName, target.DeviceName) && pair.Node.NodeId == target.NodeId).Node;
            foreach (var key in requested.Keys) current[key] = Attribute(key, NodeValue(node, key),
                key is "Subnet" or "IoSystem" ? node.ConnectionEvidence?.Complete == true : NodeValue(node, key) is not null);
            affected.Add(new() { DeviceName = target.DeviceName!, NodeId = target.NodeId! });
            if (requested.ContainsKey("Subnet") || requested.ContainsKey("IoSystem")) complete = node.ConnectionEvidence?.Complete == true;
        }
        else if (item.Operation is "update_subnet" or "delete_subnet")
        {
            var subnet = state.Subnets.Single(value => value.SubnetId == target.SubnetId);
            if (item.Operation == "delete_subnet")
            {
                complete = subnet.ConnectionEvidence?.Complete == true;
                affected.AddRange((subnet.ConnectionEvidence?.Nodes ?? new()).Select(n => new NetworkNodeIdentityInfo { DeviceName = n.DeviceName, NodeId = n.NodeId }));
                // Cross-check identities against all device scopes, not the root count.
                complete &= affected.All(n => Nodes(state).Count(p => NamesEqual(p.DeviceName, n.DeviceName) && p.Node.NodeId == n.NodeId) == 1);
            }
            foreach (var key in requested.Keys)
                current[key] = Attribute(key, key == "Name" ? subnet.Name : null, key == "Name");
            var inspectNames = requested.Keys.Where(key => key != "Name").ToArray();
            if (inspectNames.Length > 0)
            {
                var attributes = await ReadAttributesAsync(client, projectPath, target.SubnetId!, inspectNames).ConfigureAwait(false);
                foreach (var key in inspectNames) current[key] = attributes.GetValueOrDefault(key) ?? Attribute(key, null, false);
            }
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

    internal static IEnumerable<(string DeviceName, NodeInfo Node)> Nodes(HardwareConfigInfo state) =>
        state.Devices.SelectMany(d => DeviceNodes(d.Items).Select(n => (d.Name!, n)));
    private static IEnumerable<NodeInfo> DeviceNodes(IEnumerable<DeviceItemInfo> items) => items.SelectMany(i =>
        i.NetworkInterfaces.SelectMany(n => n.Nodes).Concat(DeviceNodes(i.Items)));
    internal static bool NamesEqual(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    internal static string? NodeValue(NodeInfo node, string key) => key switch
    {
        "Address" => node.IpAddress, "SubnetMask" => node.SubnetMask, "PnDeviceName" => node.PnDeviceName,
        "Subnet" => node.ConnectionEvidence?.SubnetId,
        "IoSystem" => node.ConnectionEvidence?.IoSystemSubnetId is { } subnet
            ? CanonicalJson.Serialize(new object?[] { subnet, node.ConnectionEvidence.IoSystemNumber }) : null,
        _ => null
    };
    internal static NetworkAttributeInfo Attribute(string name, string? value, bool readable) => new()
    {
        Name = name, Source = "modeled", Access = "readOnly", Availability = readable ? "available" : "unreadable",
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
            Add("Address", changes.IpAddress); Add("SubnetMask", changes.SubnetMask); Add("PnDeviceName", changes.PnDeviceName);
            Add("Subnet", changes.Subnet?.SubnetId);
            if (changes.IoSystem is { } io) Add("IoSystem", CanonicalJson.Serialize(new object?[] { io.SubnetId, io.Number }));
        }
        if (item.Subnet is { } subnet)
        { Add("Name", subnet.Name); Add("TypeIdentifier", "System:Subnet." + subnet.NetworkType); Add("HighestAddress", subnet.HighestAddress?.ToString(CultureInfo.InvariantCulture)); Add("TransmissionSpeed", subnet.TransmissionSpeed); }
        if (item.SubnetChanges is { } update)
        { Add("Name", update.Name); Add("HighestAddress", update.HighestAddress?.ToString(CultureInfo.InvariantCulture)); Add("TransmissionSpeed", update.TransmissionSpeed); }
        return result;
    }

    internal static async Task<Dictionary<string, NetworkAttributeInfo>> ReadAttributesAsync(OpennessWorkerClient client, string? projectPath, string subnetId, IReadOnlyList<string> names)
    {
        var request = new NetworkOperationRequest { OperationId = "verify", Operation = "inspect_network_object", ProjectPath = projectPath,
            Target = new() { Kind = "subnet", SubnetId = subnetId }, AttributeNames = names };
        var projected = NetworkPayloadContract.Project(request, await NetworkWorkerInvoker.InvokeReadAsync(client, request).ConfigureAwait(false));
        if (projected.Result is not { } result) return new();
        var inspection = CanonicalJson.Deserialize<NetworkObjectInspectionInfo>(result.GetRawText());
        if (inspection.Target.SubnetId != subnetId || inspection.Messages.Count != 0) return new();
        return inspection.Attributes.GroupBy(a => a.Name, StringComparer.Ordinal).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
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
