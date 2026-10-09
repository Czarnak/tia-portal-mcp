using System.Globalization;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Project;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Network;

using Project = Siemens.Engineering.Project;

/// <summary>Immediate observations in the worker, before a later operation can supersede them.</summary>
internal static class NetworkMutationVerifier
{
    public static NetworkMutationVerificationInfo VerifyAddedDevice(Project project, WorkerRequest request, AddDeviceResultInfo result)
    {
        var evidence = new NetworkMutationVerificationInfo
        {
            Identity = new NetworkMutationIdentityInfo { DeviceName = result.DeviceName, DeviceItemName = result.RootItemName },
        };
        Device? device = null;
        DeviceItem? item = null;
        Observe(evidence, "deviceName", request.DeviceName, () => (device = ResolveDevice(project, result.DeviceName)).Name);
        Observe(evidence, "deviceItemName", request.DeviceItemName ?? request.DeviceName, () =>
        {
            if (device is null) throw new InvalidOperationException("Device identity was not verified.");
            item = NetworkPostconditionChecks.SelectCreatedItem(device.DeviceItems.Cast<DeviceItem>(), candidate => candidate.Name, result.RootItemName)
                ?? throw new InvalidOperationException("Created item did not resolve uniquely.");
            return Required(item.Name);
        });
        Observe(evidence, "typeIdentifier", request.TypeIdentifier, () =>
        {
            if (item is null) throw new InvalidOperationException("Created item identity was not verified.");
            return Required(item.TypeIdentifier);
        });
        return NetworkPostconditionChecks.Complete(evidence);
    }

    public static NetworkMutationVerificationInfo VerifyConfiguration(Project project, WorkerRequest request, ConfigureNetworkDeviceResultInfo result)
    {
        var selector = NetworkConfigurationTargetBinding.Resolve(request);
        var path = selector.InterfacePath?.Select(x =>
                new NetworkInterfacePathSegmentInfo { Name = x.Name, PositionNumber = x.PositionNumber, TypeIdentifier = x.TypeIdentifier }).ToList()
            ?? selector.ItemPath?.Select(x =>
                new NetworkInterfacePathSegmentInfo { Name = x.Name, PositionNumber = x.PositionNumber, TypeIdentifier = x.TypeIdentifier }).ToList();
        if (path is not null) _ = NetworkInterfacePathEncoding.Encode(path); // Validates the owner path.
        var evidence = new NetworkMutationVerificationInfo { Identity = new NetworkMutationIdentityInfo
            { DeviceName = result.DeviceName, NodeId = selector.NodeId!, InterfacePath = path, InterfaceName = selector.InterfaceName } };
        // Resolve only when a setting was applied. A fully skipped request has no successful
        // setting to verify; the host still classifies the skipped request as a failure.
        (NetworkInterface Interface, Node Node)? target = null;
        if (result.AppliedSettings.Count > 0)
        {
            try
            {
                var selected = NetworkObjectSelectorResolver.ResolveNode(project, selector);
                if (!selected.Success) throw new InvalidOperationException(selected.Error);
                target = (selected.Resolved!.OwningInterface!, (Node)selected.Resolved.Value);
            }
            catch (Exception) { /* Each applied check below explicitly becomes unverified. */ }
        }
        foreach (var setting in result.AppliedSettings)
        {
            if (setting.Key == "IoSystem")
            {
                // One applied IO relationship is verified as two scalar checks: its subnet and number.
                Observe(evidence, "IoSystemSubnet", request.IoSystemSubnetId ?? request.SubnetId, () => ReadIoSystem(target).SubnetId);
                Observe(evidence, "IoSystemNumber", request.IoSystemNumber is { } number ? Number(number) : null,
                    () => ReadIoSystem(target).Number is { } observed ? Number(observed) : null);
                continue;
            }
            Observe(evidence, setting.Key, setting.Value, () =>
            {
                if (target is null) throw new InvalidOperationException("Target could not be resolved uniquely.");
                switch (setting.Key)
                {
                    case "Address": case "SubnetMask": case "PnDeviceName":
                        return Required(((IEngineeringObject)target.Value.Node).GetAttribute(setting.Key) as string);
                    case "PnDeviceNameAutoGeneration":
                        return ((IEngineeringObject)target.Value.Node).GetAttribute(setting.Key) is bool generated
                            ? Boolean(generated) : throw new InvalidOperationException("Missing attribute.");
                    case "Subnet":
                        return target.Value.Node.ConnectedSubnet is { } subnet ? HardwareConfigReader.RequireSubnetIdentity(subnet) : null;
                    default: throw new InvalidOperationException("Unknown applied setting.");
                }
            });
        }
        return NetworkPostconditionChecks.Complete(evidence);
    }

    public static NetworkMutationVerificationInfo VerifySubnet(Project project, WorkerRequest request,
        SubnetLifecycleResultInfo result, int rootCountBefore, IReadOnlyList<NetworkNodeIdentityInfo> affectedNodes)
    {
        var evidence = new NetworkMutationVerificationInfo { Identity = new NetworkMutationIdentityInfo { SubnetId = result.SubnetId } };
        Subnet? subnet = null;
        var deleting = request.Method == "delete_subnet";
        Observe(evidence, deleting ? "subnetAbsent" : "subnetIdentity", deleting ? "true" : result.SubnetId, () =>
        {
            // Read every identity. An unreadable candidate can hide the target or a duplicate.
            var matches = project.Subnets.Cast<Subnet>()
                .Where(candidate => HardwareConfigReader.RequireSubnetIdentity(candidate) == result.SubnetId).ToList();
            if (deleting) return Boolean(matches.Count == 0);
            if (matches.Count != 1) return null;
            subnet = matches[0];
            return HardwareConfigReader.RequireSubnetIdentity(subnet);
        });
        if (!deleting)
        {
            if (request.SubnetName is not null)
                Observe(evidence, "Name", request.SubnetName, () => Required(RequireSubnet(subnet).Name));
            if (request.Method == "create_subnet")
                Observe(evidence, "TypeIdentifier", "System:Subnet." + request.SubnetNetworkType,
                    () => Required(RequireSubnet(subnet).TypeIdentifier));
            if (request.SubnetHighestAddress is { } address)
                Observe(evidence, "HighestAddress", Number(address), () => Number(Convert.ToInt32(
                    ((IEngineeringObject)RequireSubnet(subnet)).GetAttribute("HighestAddress") ?? throw new InvalidOperationException("Missing attribute."), CultureInfo.InvariantCulture)));
            if (request.SubnetTransmissionSpeed is { } speed)
                Observe(evidence, "TransmissionSpeed", speed, () => Required(
                    ((IEngineeringObject)RequireSubnet(subnet)).GetAttribute("TransmissionSpeed")?.ToString()));
        }
        else
        {
            Observe(evidence, "affectedNodesPreserved", "true", () =>
            {
                foreach (var identity in affectedNodes) _ = ResolveNode(project, identity);
                return "true";
            });
            Observe(evidence, "affectedConnectionsRemoved", "true", () =>
            {
                var removed = true;
                foreach (var identity in affectedNodes)
                {
                    var target = ResolveNode(project, identity);
                    var connectedSubnet = target.Node.ConnectedSubnet;
                    var connectedId = connectedSubnet is null ? null : HardwareConfigReader.RequireSubnetIdentity(connectedSubnet);
                    var ioSystem = HardwareConfigReader.ReadIoSystemIdentity(target.Interface);
                    if (connectedId == result.SubnetId || ioSystem.SubnetId == result.SubnetId) removed = false;
                }
                return Boolean(removed);
            });
        }
        Observe(evidence, "networkDeviceCountUnchanged", Number(rootCountBefore), () =>
        {
            result.NetworkDeviceCount = project.Devices.Count;
            return Number(result.NetworkDeviceCount);
        });
        result.NetworkDeviceCountUnchanged = evidence.Checks.Last().Status == "passed";
        return NetworkPostconditionChecks.Complete(evidence);
    }

    public static IReadOnlyList<NetworkNodeIdentityInfo> CaptureAffectedNodes(Subnet subnet)
    {
        var evidence = NetworkConnectionEvidenceCapture.CaptureSubnet(() => subnet.Nodes.Cast<Node>(), HardwareConfigReader.ReadConnectedNodeIdentity);
        if (!evidence.Complete)
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                "Subnet connection inventory was unreadable. No deletion was attempted.");
        return evidence.Nodes;
    }

    private static void Observe(NetworkMutationVerificationInfo evidence, string name, string? expected, Func<string?> read)
    {
        try { evidence.Checks.Add(NetworkPostconditionChecks.Compare(name, expected, read(), true)); }
        catch (Exception) { evidence.Checks.Add(NetworkPostconditionChecks.Compare(name, expected, null, false)); }
    }

    private static Device ResolveDevice(Project project, string name)
    {
        var unreadable = false;
        var matches = ProjectDeviceNameMatcher.FindMatches(project, name, _ => unreadable = true);
        if (unreadable || matches.Count != 1) throw new InvalidOperationException("Device discovery was incomplete or identity was not unique.");
        return matches[0].Device;
    }

    private static (NetworkInterface Interface, Node Node) ResolveNode(Project project, NetworkNodeIdentityInfo identity)
    {
        if (identity.InterfacePath is null) throw new InvalidOperationException("Affected node has no verified owner path.");
        var resolved = NetworkObjectSelectorResolver.ResolveNode(project, NetworkSelectorFactory.QualifiedNode(
            identity.DeviceName, identity.NodeId, identity.InterfacePath, identity.InterfaceName));
        if (!resolved.Success) throw new InvalidOperationException(resolved.Error);
        return (resolved.Resolved!.OwningInterface!, (Node)resolved.Resolved.Value);
    }

    private static string Required(string? value) => !string.IsNullOrWhiteSpace(value) ? value! : throw new InvalidOperationException("Required value was unreadable.");
    private static Subnet RequireSubnet(Subnet? subnet) => subnet ?? throw new InvalidOperationException("Subnet identity was not verified.");
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Boolean(bool value) => value ? "true" : "false";
    private static (string? SubnetId, int? Number) ReadIoSystem((NetworkInterface Interface, Node Node)? target)
    {
        if (target is null) throw new InvalidOperationException("Target could not be resolved uniquely.");
        var identity = HardwareConfigReader.ReadIoSystemIdentity(target.Value.Interface);
        return (identity.SubnetId, identity.Number);
    }
}
