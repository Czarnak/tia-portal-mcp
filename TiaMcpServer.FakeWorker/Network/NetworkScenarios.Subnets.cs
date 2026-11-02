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
    // ---------------------------------------------------------------------------
    // Phase 4: subnet lifecycle fixtures (Task 6)
    // ---------------------------------------------------------------------------

    // Complete fixture inventory: both PLC ports survive subnet deletion and expose current membership.
    private static List<DeviceInfo> SubnetLifecycleDevices() => new()
    {
        SingleNodeHardwareConfig("PLC_1", "Interface", "Interface", "X1", "eth-node").Devices[0],
        new() { Name = "HMI_1", TypeIdentifier = "OrderNumber:HMI", Items = new() },
    };

    private static HardwareConfigInfo SubnetLifecycleHardwareConfig(List<SubnetLifecycleSubnetState> subnets)
    {
        var devices = SubnetLifecycleDevices();
        var nodes = devices[0].Items[0].NetworkInterfaces[0].Nodes;
        nodes.Add(SelectableNode("PLC_1", "MPI", "pb-node", "Profibus"));
        foreach (var node in nodes)
        {
            var connected = subnets.SingleOrDefault(s => s.ConnectedNodeNames.Contains("PLC_1." + node.Name));
            node.SubnetName = connected?.Name;
            node.ConnectionEvidence = new() { Complete = true, SubnetId = connected?.SubnetId };
        }
        return new()
        {
            DiscoveryEvidence = new() { Scope = "project", Complete = true },
            RootDeviceCount = SubnetLifecycleDeviceCount,
            Devices = devices,
            Subnets = subnets.Select(subnet =>
            {
                var info = SelectableSubnet(subnet.Name, subnet.SubnetId, subnet.NetworkType,
                    SubnetLifecycleContract.TypeIdentifierFor(subnet.NetworkType), Array.Empty<IoSystemInfo>(), subnet.ConnectedNodeNames);
                info.ConnectionEvidence = new()
                {
                    Complete = true,
                    Nodes = nodes.Where(n => n.ConnectionEvidence!.SubnetId == subnet.SubnetId)
                        .Select(n => new NetworkNodeIdentityInfo { DeviceName = "PLC_1", NodeId = n.NodeId! }).ToList()
                };
                return info;
            }).ToList()
        };
    }

    string InspectSubnetLifecycle(string request, List<SubnetLifecycleSubnetState> subnets)
    {
        var decoded = JsonSerializer.Deserialize<WorkerRequest>(request, WorkerJson.Envelope)!;
        var subnet = subnets.Single(s => s.SubnetId == decoded.NetworkObjectTarget!.SubnetId);
        return Success(WorkerJson.SerializePayload(new NetworkObjectInspectionInfo
        {
            Target = decoded.NetworkObjectTarget!,
            Attributes = decoded.NetworkAttributeNames!.Select(name => new NetworkAttributeInfo
            {
                Name = name, Source = "dynamic", Access = "readWrite", Availability = "available",
                Value = name == "HighestAddress" ? new() { Kind = "integer", Value = subnet.HighestAddress }
                    : new() { Kind = "enum", Value = new NetworkEnumValueInfo { TypeName = "Fixture.Speed", Symbol = subnet.TransmissionSpeed!, NumericValue = 1 } }
            }).ToList()
        }));
    }

    string HandleCreateSubnet(string requestLine, List<SubnetLifecycleSubnetState> subnets)
    {
        var name = ReadField(requestLine, "subnetName") ?? string.Empty;
        var networkType = ReadField(requestLine, "subnetNetworkType") ?? string.Empty;
        var isProfibus = string.Equals(networkType, SubnetLifecycleContract.Profibus, StringComparison.Ordinal);

        var subnetId = $"subnet-created-{subnetLifecycleNextId}";
        subnetLifecycleNextId++;

        subnets.Add(new SubnetLifecycleSubnetState
        {
            SubnetId = subnetId,
            Name = name,
            NetworkType = networkType,
            HighestAddress = isProfibus ? ReadIntField(requestLine, "subnetHighestAddress") : null,
            TransmissionSpeed = isProfibus ? ReadField(requestLine, "subnetTransmissionSpeed") : null,
            ConnectedNodeNames = new List<string>(),
        });

        return Success(WorkerJson.SerializePayload(new SubnetLifecycleResultInfo
        {
            SubnetId = subnetId,
            Name = name,
        }));
    }

    /// <summary>
    /// Applies only the fields present on the request to the EXACT matching SubnetId - every other
    /// subnet in the shared list, and every field the request omitted, is left untouched.
    /// </summary>
    string HandleUpdateSubnet(string requestLine, List<SubnetLifecycleSubnetState> subnets)
    {
        var subnetId = ReadField(requestLine, "subnetId");
        var target = subnets.FirstOrDefault(subnet => subnet.SubnetId == subnetId);
        if (target is null)
        {
            return $$"""{"success":false,"error":"network-subnet-lifecycle has no subnet with subnetId '{{subnetId}}'"}""";
        }

        var name = ReadField(requestLine, "subnetName");
        if (name is not null)
        {
            target.Name = name;
        }

        var highestAddress = ReadIntField(requestLine, "subnetHighestAddress");
        if (highestAddress is not null)
        {
            target.HighestAddress = highestAddress;
        }

        var transmissionSpeed = ReadField(requestLine, "subnetTransmissionSpeed");
        if (transmissionSpeed is not null)
        {
            target.TransmissionSpeed = transmissionSpeed;
        }

        return Success(WorkerJson.SerializePayload(new SubnetLifecycleResultInfo
        {
            SubnetId = target.SubnetId,
            Name = target.Name,
        }));
    }

    /// <summary>
    /// Removes the exact matching subnet from the shared list UNCONDITIONALLY - a non-empty
    /// connectedNodeNames never blocks this, matching production's "connected deletion is allowed, no
    /// dependency inventory" rule. The device collection is never touched.
    /// </summary>
    string HandleDeleteSubnet(string requestLine, List<SubnetLifecycleSubnetState> subnets)
    {
        var subnetId = ReadField(requestLine, "subnetId");
        var target = subnets.FirstOrDefault(subnet => subnet.SubnetId == subnetId);
        if (target is null)
        {
            return $$"""{"success":false,"error":"network-subnet-lifecycle has no subnet with subnetId '{{subnetId}}'"}""";
        }

        subnets.Remove(target);

        return Success(WorkerJson.SerializePayload(new SubnetLifecycleResultInfo
        {
            SubnetId = target.SubnetId,
            Name = target.Name,
        }));
    }

    /// <summary>
    /// The FIRST subnet write against "network-subnet-lifecycle-second-item-failure" performs a REAL
    /// mutation (proving the earlier item stays applied); every later one fails structurally without
    /// touching state, proving the batch stops and later items are skipped.
    /// </summary>
    string HandleSecondItemFailureWrite(string requestLine, List<SubnetLifecycleSubnetState> subnets)
    {
        subnetLifecycleSecondFailureWriteCount++;
        return subnetLifecycleSecondFailureWriteCount == 1
            ? DispatchSubnetLifecycleWrite(requestLine, subnets)
            : $$"""{"success":false,"error":"deliberate second-item failure for network-subnet-lifecycle-second-item-failure"}""";
    }

    /// <summary>Mutable process-local state for one subnet in the Phase 4 lifecycle scenarios.</summary>
    sealed class SubnetLifecycleSubnetState
    {
        public required string SubnetId { get; init; }

        public string Name { get; set; } = string.Empty;

        public string NetworkType { get; set; } = string.Empty;

        public int? HighestAddress { get; set; }

        public string? TransmissionSpeed { get; set; }

        public List<string> ConnectedNodeNames { get; set; } = new();
    }

    /// <summary>Mutable process-local state for one node of the "multi-homed-network" scenario.</summary>
    sealed class MultiHomedNode
    {
        public required string Name { get; init; }

        public required string NodeId { get; init; }

        public string IpAddress { get; set; } = string.Empty;

        public string? SubnetMask { get; set; }

        public string? PnDeviceName { get; set; }
    }

    abstract record HardwarePageFixtureCandidate(IReadOnlyList<string> Messages);

    sealed record HardwarePageFixtureDevice(
        DeviceInfo Device,
        IReadOnlyList<string> Messages) : HardwarePageFixtureCandidate(Messages);

    sealed record HardwarePageFixtureSubnet(
        SubnetInfo Subnet,
        IReadOnlyList<string> Messages) : HardwarePageFixtureCandidate(Messages);

}
