using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Network;
using TiaMcpServer.OpennessWorker.Worker;
using static TiaMcpServer.FakeWorker.ScenarioSupport;

namespace TiaMcpServer.FakeWorker.Network;

internal sealed partial class NetworkScenarios
{
    HardwareConfigInfo RoundtripHardwareConfig()
    {
        var state = JsonSerializer.Deserialize<HardwareConfigInfo>(HardwareConfigPayload(), WorkerJson.Envelope)!;
        state.DiscoveryEvidence = new() { Scope = "project", Complete = true };
        state.RootDeviceCount = state.Devices.Count;
        foreach (var node in GuardedNodes(state))
            node.ConnectionEvidence = new() { Complete = true,
                SubnetId = state.Subnets.SingleOrDefault(s => s.Name == node.SubnetName)?.SubnetId };
        foreach (var subnet in state.Subnets)
            subnet.ConnectionEvidence = new() { Complete = true,
                Nodes = state.Devices.SelectMany(d => d.Items.SelectMany(i => i.NetworkInterfaces).SelectMany(i => i.Nodes)
                    .Where(n => n.ConnectionEvidence!.SubnetId == subnet.SubnetId)
                    .Select(n => new NetworkNodeIdentityInfo { DeviceName = d.Name!, NodeId = n.NodeId! })).ToList() };
        return state;
    }

    // An ordinary-read fixture with explicit all-scope identities and a distinct root count.
    // Older/page scenarios intentionally keep their conditional omissions.
    HardwareConfigInfo ConnectionEvidenceHardwareConfig(bool degraded)
    {
        var result = SingleNodeHardwareConfig("PLC_Grouped", "Interface", "Interface", "Same display name", "node-2");
        // Explicit synthetic ordinary project traversal; relationship loss is modeled independently.
        result.DiscoveryEvidence = new() { Scope = "project", Complete = true };
        result.RootDeviceCount = 2;
        var ungrouped = SingleNodeHardwareConfig("PLC_Ungrouped", "Interface", "Interface", "Same display name", "node-3");
        result.Devices.Add(ungrouped.Devices[0]);
        var node = result.Devices[0].Items[0].NetworkInterfaces[0].Nodes[0];
        node.SubnetName = "Network";
        node.ConnectionEvidence = new NetworkNodeConnectionInfo { Complete = true, SubnetId = "subnet-1" };
        var ungroupedNode = result.Devices[1].Items[0].NetworkInterfaces[0].Nodes[0];
        ungroupedNode.SubnetName = "Network";
        ungroupedNode.ConnectionEvidence = new NetworkNodeConnectionInfo { Complete = true, SubnetId = "subnet-1" };
        var disconnected = SelectableNode("PLC_Grouped", "Disconnected port", "node-disconnected", "Ethernet");
        disconnected.ConnectionEvidence = new NetworkNodeConnectionInfo { Complete = true };
        result.Devices[0].Items[0].NetworkInterfaces[0].Nodes.Add(disconnected);
        var subnet = SelectableSubnet("Network", "subnet-1", "Ethernet", "Ethernet",
            Array.Empty<IoSystemInfo>(), new[] { "Same display name", "Same display name" });
        subnet.ConnectionEvidence = new NetworkSubnetConnectionsInfo
        {
            Complete = !degraded,
            Nodes = new() { new() { DeviceName = "PLC_Grouped", NodeId = "node-2" },
                new() { DeviceName = "PLC_Ungrouped", NodeId = "node-3" } },
            Messages = degraded ? new() { "Could not complete connected-node enumeration: unavailable" } : new()
        };
        result.Subnets.Add(subnet);
        return result;
    }

    /// <summary>
    /// Dispatches one subnet lifecycle write against the shared mutable state and returns the exact
    /// four-member <see cref="SubnetLifecycleResultInfo"/> JSON. Shared by every scenario key that
    /// performs a REAL (non-switched) mutation, so create/update/delete behave identically no matter
    /// which key reached them.
    /// </summary>
    string DispatchSubnetLifecycleWrite(string requestLine, List<SubnetLifecycleSubnetState> subnets)
        => ReadMethod(requestLine) switch
        {
            "create_subnet" => HandleCreateSubnet(requestLine, subnets),
            "update_subnet" => HandleUpdateSubnet(requestLine, subnets),
            "delete_subnet" => HandleDeleteSubnet(requestLine, subnets),
            _ => $$"""{"success":false,"error":"unexpected subnet lifecycle method '{{ReadMethod(requestLine)}}'"}""",
        };

    /// <summary>
    /// Scripted verification for deterministic FakeWorker outcomes, not live Siemens read-back.
    /// </summary>
    IEnumerable<NodeInfo> GuardedNodes(HardwareConfigInfo state) => state.Devices.SelectMany(d => d.Items)
        .SelectMany(i => i.NetworkInterfaces).SelectMany(i => i.Nodes);

    DeviceItemInfo GuardedItem(string device, int index, string name, string type, params DeviceItemPathSegmentInfo[] parents) => new()
    {
        Name = name, TypeIdentifier = type, PositionNumber = index, Selectable = true,
        Selector = new() { Kind = "deviceItem", DeviceName = device, ItemPath = parents.Concat(new[]
            { new DeviceItemPathSegmentInfo { Index = index, Name = name, PositionNumber = index, TypeIdentifier = type } }).ToList() }
    };

    string HandleGuardedNetwork(string request, HardwareConfigInfo state, string scenario)
    {
        var method = ReadMethod(request);
        if (method == "inspect_network_object")
        {
            var decoded = JsonSerializer.Deserialize<WorkerRequest>(request, WorkerJson.Envelope)!;
            if (decoded.NetworkObjectTarget!.Kind == "node")
            {
                // Node attributes are dynamic Openness attributes; the auto-generation flag is scripted per scenario.
                if (scenario == "network-guarded-node-inspect-unavailable") return "{\"success\":false,\"error\":\"inspection unavailable\"}";
                var inspectedNode = GuardedNodes(state).Single(n => n.NodeId == decoded.NetworkObjectTarget.NodeId);
                return Success(WorkerJson.SerializePayload(new NetworkObjectInspectionInfo
                {
                    Target = decoded.NetworkObjectTarget,
                    Attributes = decoded.NetworkAttributeNames!.Select(name => name == NetworkSettingKeys.PnDeviceNameAutoGeneration
                        ? new NetworkAttributeInfo { Name = name, Source = "dynamic", Access = "readWrite", SupportedTypes = new() { "System.Boolean" },
                            Availability = "available", Value = new() { Kind = "boolean", Value = guardedPnAutoGeneration.GetValueOrDefault(inspectedNode.NodeId,
                                scenario == "network-guarded-pn-autogeneration") } }
                        : new NetworkAttributeInfo { Name = name, Source = "dynamic", SupportedTypes = new() { "System.String" }, Availability = "available",
                            Access = name == NetworkSettingKeys.PnDeviceName && scenario == "network-guarded-pn-readonly" ? "readOnly" : "readWrite",
                            Value = (name switch { NetworkSettingKeys.Address => inspectedNode.IpAddress, NetworkSettingKeys.SubnetMask => inspectedNode.SubnetMask, _ => inspectedNode.PnDeviceName }) is { } text
                                ? new() { Kind = "string", Value = text } : new() { Kind = "null" } }).ToList()
                }));
            }
            var id = decoded.NetworkObjectTarget!.SubnetId!;
            return Success(WorkerJson.SerializePayload(new NetworkObjectInspectionInfo
            {
                Target = decoded.NetworkObjectTarget,
                Attributes = decoded.NetworkAttributeNames!.Select(name => new NetworkAttributeInfo
                {
                    Name = name, Source = "dynamic", Access = "readWrite", Availability = "available",
                    Value = name == "Name" ? new() { Kind = "string", Value = state.Subnets.Single(s => s.SubnetId == id).Name }
                        : name == "TransmissionSpeed"
                        ? new() { Kind = "enum", Value = new NetworkEnumValueInfo { TypeName = "Fixture.Speed", Symbol = guardedSubnetAttributes[(id, name)], NumericValue = 1 } }
                        : new() { Kind = "integer", Value = int.Parse(guardedSubnetAttributes[(id, name)], System.Globalization.CultureInfo.InvariantCulture) }
                }).ToList()
            }));
        }
        if (method == "read_hardware_config")
        {
            if (scenario == "network-guarded-late-traversal" && guardedNetworkWrites > 0)
                state.DiscoveryEvidence = new() { Scope = "project", Complete = false,
                    Failures = new() { new() { Stage = "deviceEnumeration", Message = "Synthetic late ungrouped-device traversal failure." } } };
            if (scenario == "network-guarded-late-unreadable-subnet" && guardedNetworkWrites > 0 && state.Subnets.Count == 0)
                state.Subnets.Add(new() { SubnetId = "", SelectorDiagnostics = new() { "Unreadable subnet ID" } });
            if (scenario == "network-guarded-late-unreadable-attribute" && guardedNetworkWrites > 0)
                state.Subnets.Add(new() { SubnetId = "", SelectorDiagnostics = new() { "Unreadable subnet ID" } });
            if (scenario == "network-guarded-late-io-number" && guardedNetworkWrites > 0)
                state.Subnets[0].IoSystems.Add(new() { Number = null, SelectorDiagnostics = new() { "Unreadable IO number" } });
            if (scenario == "network-guarded-late-io-subnet" && guardedNetworkWrites > 0)
                state.Subnets.Add(new() { SubnetId = "", SelectorDiagnostics = new() { "Unreadable subnet ID" } });
            if (scenario == "network-guarded-root-drift" && guardedNetworkWrites > 0) state.RootDeviceCount = 1;
            if (scenario == "network-guarded-postread-failure" && guardedNetworkWrites > 0)
                return "{\"success\":false,\"error\":\"postread unavailable\"}";
            if (scenario == "network-guarded-late-block" && guardedNetworkWrites > 0 && state.Subnets.Count > 0)
                state.Subnets[0].ConnectionEvidence = new() { Complete = false, Messages = new() { "late inventory failure" } };
            if (scenario == "network-guarded-late-node-block" && guardedNetworkWrites > 0)
                GuardedNodes(state).First().ConnectionEvidence = new() { Complete = false,
                    Messages = new() { "Could not read connected subnet identity: unavailable", "Could not read node 'Same display name' IO system: unavailable" } };
            return Success(WorkerJson.SerializePayload(state));
        }
        guardedNetworkWrites++;
        if (method is "create_subnet" or "update_subnet")
        {
            var id = method == "create_subnet" ? "subnet-created-" + guardedNetworkWrites : ReadField(request, "subnetId")!;
            var subnet = method == "create_subnet"
                ? SelectableSubnet(ReadField(request, "subnetName")!, id, ReadField(request, "subnetNetworkType")!, SubnetLifecycleContract.TypeIdentifierFor(ReadField(request, "subnetNetworkType")), Array.Empty<IoSystemInfo>(), Array.Empty<string>())
                : state.Subnets.Single(s => s.SubnetId == id);
            if (method == "create_subnet")
            {
                subnet.ConnectionEvidence = new() { Complete = true };
                state.Subnets.Add(subnet);
            }
            if (ReadField(request, "subnetName") is { } name) subnet.Name = name;
            if (ReadIntField(request, "subnetHighestAddress") is { } address) guardedSubnetAttributes[(id, "HighestAddress")] = address.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (ReadField(request, "subnetTransmissionSpeed") is { } speed) guardedSubnetAttributes[(id, "TransmissionSpeed")] = speed;
            return Success(WorkerJson.SerializePayload(new SubnetLifecycleResultInfo
            { SubnetId = id, Name = subnet.Name }));
        }
        if (method == "configure_network_device")
        {
            var node = GuardedNodes(state).Single(n => n.NodeId == ReadField(request, "nodeId"));
            var applied = new Dictionary<string, string>();
            if (scenario == "network-guarded-no-io-connector" && ReadIntField(request, "ioSystemNumber") is not null)
                return "{\"success\":false,\"failureCategory\":\"target_not_found\",\"error\":\"The selected interface exposes no IO connector.\"}";
            var rejectedSettingScenario = scenario is "network-guarded-rejected-setting" or "network-guarded-rejected-contradiction";
            var rejectionMessage = rejectedSettingScenario && ReadField(request, "ipAddress") == "not-an-ip"
                ? LiveAddressRejectionMessage : null;
            if (rejectionMessage is null)
            {
                if (ReadField(request, "ipAddress") is { } address) { node.IpAddress = address; applied[NetworkSettingKeys.Address] = address; }
                if (ReadField(request, "subnetMask") is { } mask) { node.SubnetMask = mask; applied[NetworkSettingKeys.SubnetMask] = mask; }
                if (rejectedSettingScenario && ReadField(request, "subnetId") == "590-3")
                    rejectionMessage = LiveConfigureRejectionMessage;
            }
            if (rejectionMessage is not null)
            {
                var outcome = new ConfigureNetworkDeviceResultInfo { DeviceName = ReadField(request, "deviceName")!,
                    AppliedSettings = scenario == "network-guarded-rejected-contradiction"
                        ? new() { [NetworkSettingKeys.SubnetMask] = "255.0.0.0" } : applied };
                return WorkerJson.SerializePayload(new { success = false, failureCategory = "engineering_rejected",
                    error = "TIA Portal rejected the operation: " + rejectionMessage,
                    engineeringError = new { type = "EngineeringTargetInvocationException", message = rejectionMessage, inner = Array.Empty<object>() },
                    configureNetworkDeviceOutcome = outcome });
            }
            if (ReadBoolField(request, "pnDeviceNameAutoGeneration") is { } generated)
            { guardedPnAutoGeneration[node.NodeId] = generated; applied[NetworkSettingKeys.PnDeviceNameAutoGeneration] = generated ? "true" : "false"; }
            if (ReadField(request, "pnDeviceName") is { } pn) { node.PnDeviceName = pn; applied[NetworkSettingKeys.PnDeviceName] = pn; }
            if (ReadField(request, "subnetId") is { } id)
            {
                node.ConnectionEvidence = new() { Complete = true, SubnetId = id };
                applied[NetworkSettingKeys.Subnet] = id;
                foreach (var previous in state.Subnets) previous.ConnectionEvidence?.Nodes.RemoveAll(n => n.DeviceName == "PLC_Grouped" && n.NodeId == node.NodeId);
                var subnet = state.Subnets.Single(s => s.SubnetId == id);
                subnet.ConnectionEvidence!.Nodes.Add(new() { DeviceName = "PLC_Grouped", NodeId = node.NodeId });
            }
            if (ReadIntField(request, "ioSystemNumber") is { } ioNumber)
            {
                if (scenario == "network-guarded-io-move" || scenario.StartsWith("network-guarded-late-io-", StringComparison.Ordinal))
                {
                    node.ConnectionEvidence!.IoSystemSubnetId = ReadField(request, "ioSystemSubnetId");
                    node.ConnectionEvidence.IoSystemNumber = ioNumber;
                    applied[NetworkSettingKeys.IoSystem] = ioNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            if (scenario == "network-guarded-unknown-result") return "{\"success\":false,\"error\":\"outcome unavailable after mutation\"}";
            return Success(WorkerJson.SerializePayload(new ConfigureNetworkDeviceResultInfo
            {
                DeviceName = ReadField(request, "deviceName")!, AppliedSettings = applied,
            }));
        }
        if (method == "add_network_device")
        {
            var name = ReadField(request, "deviceName")!;
            var itemName = ReadField(request, "deviceItemName") ?? name;
            var type = ReadField(request, "typeIdentifier")!;
            state.RootDeviceCount++;
            // ET200SP shape: the head module is top-level, and its sub-item repeats its name without a type.
            var head = GuardedItem(name, 1, itemName, type);
            head.Items.Add(new() { Name = itemName, PositionNumber = 0, Selectable = false,
                SelectorDiagnostics = new() { "Could not read device item type identifier." } });
            state.Devices.Add(new() { Name = name, TypeIdentifier = "Device:Station", Items = new()
            {
                GuardedItem(name, 0, "Rack", "Rack:TEST"), head, GuardedItem(name, 2, "PowerSupply", "Supply:TEST")
            } });
            return Success(WorkerJson.SerializePayload(new AddDeviceResultInfo
            {
                DeviceName = name, RootItemName = itemName, TypeIdentifier = type,
            }));
        }
        if (method == "delete_subnet")
        {
            var id = ReadField(request, "subnetId")!;
            var subnet = state.Subnets.Single(s => s.SubnetId == id);
            state.Subnets.Remove(subnet);
            foreach (var node in GuardedNodes(state))
                if (node.ConnectionEvidence?.SubnetId == id) node.ConnectionEvidence = new() { Complete = true };
            if (scenario == "network-guarded-lost-node") state.Devices.RemoveAt(1);
            if (scenario == "network-guarded-unknown-result") return "{\"success\":false,\"error\":\"delete outcome unavailable\"}";
            return Success(WorkerJson.SerializePayload(new SubnetLifecycleResultInfo
            {
                SubnetId = id, Name = subnet.Name,
            }));
        }
        return "{\"success\":false,\"error\":\"unsupported guarded fixture operation\"}";
    }

}
