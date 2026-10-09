using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.OpennessWorker.Openness.Project;

namespace TiaMcpServer.OpennessWorker.Openness.Network;

using Project = Siemens.Engineering.Project;

public static class HardwareConfigReader
{
    /// <summary>
    /// Ordinary unfiltered hardware read, including exact relationship evidence, without I/O maps.
    /// </summary>
    public static HardwareConfigInfo Read(Project project)
        => Read(project, deviceName: null, plcName: null, includeIoDetails: false, includeTagMatches: false);

    public static HardwareConfigInfo Read(
        Project project,
        string? deviceName,
        string? plcName,
        bool includeIoDetails,
        bool includeTagMatches)
    {
        var result = new HardwareConfigInfo();
        var capture = new HardwareDiscoveryEvidenceCapture(deviceName is null ? "project" : "device", result.Messages.Add);
        result.DiscoveryEvidence = capture.Evidence;
        try { result.RootDeviceCount = project.Devices.Count; }
        catch (Exception exception)
        {
            result.Messages.Add($"Could not read root device count: {exception.Message}");
        }

        IoTagIndex? tagIndex = null;
        if (includeTagMatches)
        {
            tagIndex = ResolveTagIndex(project, plcName, result.Messages);
        }

        var selectedDevices = SelectDevices(project, deviceName, result.Messages, capture, out var deviceNamespaceVerified);
        capture.Traverse(() => selectedDevices,
            selected => result.Devices.Add(ReadDevice(selected.Device, selected.NameEvidence,
                result.Messages, includeIoDetails, tagIndex, capture)),
            "deviceEnumeration", "deviceMaterialization");
        capture.Traverse(() => project.Subnets.Cast<Subnet>(),
            subnet => result.Subnets.Add(ReadSubnet(subnet, result.Messages, capture)),
            "subnetEnumeration", "subnetMaterialization");
        // Use final evidence: a later traversal failure must invalidate earlier certifications.
        NetworkNodeReadSelectorBuilder.ApplyInventory(result, deviceNamespaceVerified);

        result.Devices = result.Devices
            .OrderBy(device => device.Name, StringComparer.Ordinal)
            .ToList();
        result.Subnets = result.Subnets
            .OrderBy(subnet => subnet.SubnetId, StringComparer.Ordinal)
            .ToList();
        result.Messages = result.Messages.Distinct(StringComparer.Ordinal).ToList();

        return result;
    }

    internal static HardwarePageCandidateMaterialization ReadDevicePageCandidate(
        Device device,
        NetworkObjectDiscoveryEvidenceValue<string> nameEvidence,
        bool includeIoDetails,
        IoTagIndex? tagIndex,
        bool deviceNamespaceVerified)
    {
        var messages = new List<string>();
        // Page materialization keeps diagnostics but cannot emit ordinary project evidence.
        var capture = new HardwareDiscoveryEvidenceCapture("device", messages.Add);
        var materialized = ReadDevice(device, nameEvidence, messages, includeIoDetails, tagIndex, capture);
        NetworkNodeReadSelectorBuilder.ApplyPage(materialized, deviceNamespaceVerified, capture.Evidence.Complete);
        return HardwarePageCandidateMaterialization.ForDevice(materialized, messages);
    }

    internal static HardwarePageCandidateMaterialization ReadSubnetPageCandidate(
        Subnet subnet,
        NetworkObjectDiscoveryEvidenceValue<string> subnetId)
    {
        var messages = new List<string>();
        var materialized = ReadSubnet(subnet, subnetId, messages,
            new HardwareDiscoveryEvidenceCapture("device", messages.Add));
        return HardwarePageCandidateMaterialization.ForSubnet(materialized, messages);
    }

    internal static IoTagIndex? ResolvePageTagIndex(Project project, string? plcName, List<string> messages)
    {
        return ResolveTagIndex(project, plcName, messages);
    }

    private static IoTagIndex? ResolveTagIndex(Project project, string? plcName, List<string> messages)
    {
        try
        {
            return HardwareTagIndexResolver.Resolve(project, plcName, messages);
        }
        catch (EngineeringException exception)
        {
            messages.Add($"Could not build the PLC tag index: {exception.Message}; no tag matches are reported.");
            return null;
        }
    }

    /// <summary>
    /// Applies the optional device filter. Unfiltered reads traverse every device. An unreadable
    /// device name produces degradation evidence and preserves the device with Name = null.
    /// Filtered reads match readable candidate names ordinal-ignore-case; exactly one match reads
    /// only that device, while zero or multiple matches report a non-fatal message and no devices.
    /// </summary>
    private static IReadOnlyList<(Device Device, NetworkObjectDiscoveryEvidenceValue<string> NameEvidence)> SelectDevices(
        Project project,
        string? deviceName,
        List<string> messages,
        HardwareDiscoveryEvidenceCapture capture,
        out bool deviceNamespaceVerified)
    {
        var candidates = new List<(Device Device, NetworkObjectDiscoveryEvidenceValue<string> NameEvidence)>();
        capture.Traverse(() => ProjectDeviceEnumerator.Enumerate(project), device =>
        {
            var nameEvidence = ReadTypedIdentityString(() => device.Name, "Device name");
            candidates.Add((device, nameEvidence));
        }, "deviceEnumeration", "deviceMaterialization");
        deviceNamespaceVerified = capture.Evidence.Complete && candidates.All(candidate => candidate.NameEvidence.IsUsable);

        if (deviceName is null)
        {
            return candidates;
        }

        var matches = candidates
            .Where(candidate => candidate.NameEvidence.IsUsable
                && string.Equals(candidate.NameEvidence.Value, deviceName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 1)
        {
            return matches;
        }

        capture.RecordFailure("deviceSelection", matches.Count == 0
            ? $"No device named '{deviceName}' was found; no devices are reported."
            : $"More than one device matches '{deviceName}'; no devices are reported because the device filter is ambiguous.");
        return Array.Empty<(Device, NetworkObjectDiscoveryEvidenceValue<string>)>();
    }

    private static DeviceInfo ReadDevice(
        Device device,
        NetworkObjectDiscoveryEvidenceValue<string> deviceName,
        List<string> messages,
        bool includeIoDetails,
        IoTagIndex? tagIndex,
        HardwareDiscoveryEvidenceCapture capture)
    {
        NetworkObjectDiscoveryEvidence.AddReadMessage(messages, deviceName);
        var failuresBefore = capture.Evidence.Failures.Count;
        var deviceDescription = deviceName.IsUsable ? deviceName.Value : "(unnamed)";
        var typeIdentifier = ReadOptionalString(
            () => device.TypeIdentifier,
            $"device '{deviceDescription}' type identifier",
            messages);
        var deviceInfo = new DeviceInfo
        {
            Name = deviceName.IsUsable ? deviceName.Value : null,
            TypeIdentifier = typeIdentifier,
        };
        deviceInfo.Items = ReadDeviceItems(
            () => device.DeviceItems.Cast<DeviceItem>(),
            $"device '{deviceDescription}'",
            messages,
            deviceName,
            Array.Empty<DeviceItemPathSegmentInfo>(),
            Array.Empty<string>(),
            includeIoDetails,
            tagIndex,
            capture);
        NetworkNodeReadSelectorBuilder.Apply(deviceInfo, capture.Evidence.Failures.Count == failuresBefore);
        return deviceInfo;
    }

    private static List<DeviceItemInfo> ReadDeviceItems(
        Func<IEnumerable<DeviceItem>> enumerateItems,
        string ownerDescription,
        List<string> messages,
        NetworkObjectDiscoveryEvidenceValue<string> deviceName,
        IReadOnlyList<DeviceItemPathSegmentInfo> parentPath,
        IReadOnlyList<string> parentPathDiagnostics,
        bool includeIoDetails,
        IoTagIndex? tagIndex,
        HardwareDiscoveryEvidenceCapture capture)
    {
        var result = new List<DeviceItemInfo>();
        var siblingIndex = 0;

        capture.Traverse(enumerateItems, item =>
        {
            // Preserve the source sibling index even when this candidate is skipped.
            var currentIndex = siblingIndex++;
            result.Add(ReadDeviceItem(
                item,
                messages,
                deviceName,
                parentPath,
                parentPathDiagnostics,
                currentIndex,
                includeIoDetails,
                tagIndex,
                capture));
        }, "deviceItemEnumeration", "deviceItemMaterialization");

        return result;
    }

    private static DeviceItemInfo ReadDeviceItem(
        DeviceItem item,
        List<string> messages,
        NetworkObjectDiscoveryEvidenceValue<string> deviceName,
        IReadOnlyList<DeviceItemPathSegmentInfo> parentPath,
        IReadOnlyList<string> parentPathDiagnostics,
        int siblingIndex,
        bool includeIoDetails,
        IoTagIndex? tagIndex,
        HardwareDiscoveryEvidenceCapture capture)
    {
        var itemName = ReadTypedIdentityString(() => item.Name, "Device item name");
        var itemDescription = itemName.IsUsable ? itemName.Value : "(unnamed)";
        var typeIdentifier = ReadTypedIdentityString(
            () => item.TypeIdentifier,
            $"Device item '{itemDescription}' type identifier");
        var positionNumber = ReadTypedIdentityInt(
            () => item.PositionNumber,
            $"Device item '{itemDescription}' position number");
        NetworkObjectDiscoveryEvidence.AddReadMessage(messages, itemName);
        NetworkObjectDiscoveryEvidence.AddReadMessage(messages, typeIdentifier, reportNull: false);
        NetworkObjectDiscoveryEvidence.AddReadMessage(messages, positionNumber);

        var segment = new DeviceItemPathSegmentInfo
        {
            Index = siblingIndex,
            Name = itemName.IsUsable ? itemName.Value : string.Empty,
            PositionNumber = positionNumber.IsUsable ? positionNumber.Value : -1,
            TypeIdentifier = typeIdentifier.IsUsable ? typeIdentifier.Value : string.Empty,
        };
        var itemPath = parentPath.Concat(new[] { segment }).ToList();
        var pathDiagnostics = CombineDiagnostics(
            parentPathDiagnostics,
            itemName.Diagnostic,
            positionNumber.Diagnostic,
            typeIdentifier.Diagnostic);
        var selectorDiagnostics = CombineDiagnostics(pathDiagnostics, deviceName.Diagnostic);

        var itemInfo = new DeviceItemInfo
        {
            Name = itemName.IsUsable ? itemName.Value : null,
            TypeIdentifier = typeIdentifier.IsUsable ? typeIdentifier.Value : null,
            PositionNumber = positionNumber.IsUsable ? positionNumber.Value : null,
            // DeviceItem has no 'Address' attribute; I/O addresses are in ioDetails.addresses.
            Address = null,
            Selectable = selectorDiagnostics.Count == 0,
            SelectorDiagnostics = selectorDiagnostics,
        };
        if (itemInfo.Selectable)
        {
            itemInfo.Selector = NetworkSelectorFactory.DeviceItem(deviceName.Value, itemPath);
        }

        if (includeIoDetails)
        {
            itemInfo.IoDetails = HardwareIoMapReader.Read(item, itemDescription, messages, tagIndex);
        }

        itemInfo.CommunicationConnections = CommunicationConnectionReader
            .Read(item, deviceName.IsUsable ? deviceName.Value : null, itemPath, messages)
            .Select(readResult => readResult.Summary)
            .ToList();
        itemInfo.NetworkInterfaces = ReadNetworkInterfaces(
            item,
            itemDescription,
            messages,
            deviceName,
            itemPath,
            pathDiagnostics,
            capture);
        itemInfo.Items = ReadDeviceItems(
            () => item.DeviceItems.Cast<DeviceItem>(),
            $"device item '{itemDescription}'",
            messages,
            deviceName,
            itemPath,
            pathDiagnostics,
            includeIoDetails,
            tagIndex,
            capture);

        return itemInfo;
    }

    private static List<NetworkInterfaceInfo> ReadNetworkInterfaces(
        DeviceItem item,
        string itemDescription,
        List<string> messages,
        NetworkObjectDiscoveryEvidenceValue<string> deviceName,
        IReadOnlyList<DeviceItemPathSegmentInfo> itemPath,
        IReadOnlyList<string> itemPathDiagnostics,
        HardwareDiscoveryEvidenceCapture capture)
    {
        var result = new List<NetworkInterfaceInfo>();

        capture.Traverse(() =>
        {
            var networkInterface = ((IEngineeringServiceProvider)item).GetService<NetworkInterface>();
            return networkInterface is null ? Array.Empty<NetworkInterface>() : new[] { networkInterface };
        }, networkInterface => result.Add(ReadNetworkInterface(
            networkInterface,
            messages,
            deviceName,
            itemPath,
            itemPathDiagnostics,
            capture)), "interfaceDiscovery", "interfaceDiscovery");

        return result;
    }

    private static NetworkInterfaceInfo ReadNetworkInterface(
        NetworkInterface networkInterface,
        List<string> messages,
        NetworkObjectDiscoveryEvidenceValue<string> deviceName,
        IReadOnlyList<DeviceItemPathSegmentInfo> itemPath,
        IReadOnlyList<string> itemPathDiagnostics,
        HardwareDiscoveryEvidenceCapture capture)
    {
        var interfaceName = ReadExactStringAttribute(
            (IEngineeringObject)networkInterface,
            "Name",
            "network interface name",
            messages);
        var selectorDiagnostics = CombineDiagnostics(itemPathDiagnostics, deviceName.Diagnostic);
        var interfaceInfo = new NetworkInterfaceInfo
        {
            Name = interfaceName ?? string.Empty,
            Selectable = selectorDiagnostics.Count == 0,
            SelectorDiagnostics = selectorDiagnostics,
        };
        if (interfaceInfo.Selectable)
        {
            interfaceInfo.Selector = NetworkSelectorFactory.NetworkInterface(
                deviceName.Value,
                itemPath,
                interfaceName,
                interfaceType: null,
                interfaceOperatingMode: null);
        }

        interfaceInfo.Nodes = NetworkNodeReadSelectorBuilder.ReadNodes(
            () => networkInterface.Nodes.Cast<Node>(),
            node => ReadNode(node, networkInterface, messages, deviceName), capture);
        return interfaceInfo;
    }

    private static NodeInfo ReadNode(
        Node node,
        NetworkInterface networkInterface,
        List<string> messages,
        NetworkObjectDiscoveryEvidenceValue<string> deviceName)
    {
        var nodeName = ReadOptionalString(() => node.Name, "node name", messages);
        var nodeDescription = nodeName ?? "(unnamed)";
        var nodeId = ReadTypedIdentityString(
            () => node.NodeId,
            $"Node '{nodeDescription}' identity");
        NetworkObjectDiscoveryEvidence.AddReadMessage(messages, nodeId);
        var selectorDiagnostics = CombineDiagnostics(
            Array.Empty<string>(),
            deviceName.Diagnostic,
            nodeId.Diagnostic);
        var nodeInfo = new NodeInfo
        {
            Name = nodeName ?? string.Empty,
            NodeId = nodeId.IsUsable ? nodeId.Value : string.Empty,
            NodeType = ReadOptionalEnumName(
                () => node.NodeType,
                $"node '{nodeDescription}' node type",
                messages),
            IpAddress = ReadExactStringAttribute(
                (IEngineeringObject)node,
                "Address",
                $"node '{nodeDescription}' IP address",
                messages),
            SubnetMask = ReadExactStringAttribute(
                (IEngineeringObject)node,
                "SubnetMask",
                $"node '{nodeDescription}' subnet mask",
                messages),
            PnDeviceName = ReadExactStringAttribute(
                (IEngineeringObject)node,
                "PnDeviceName",
                $"node '{nodeDescription}' PROFINET device name",
                messages),
            Selectable = selectorDiagnostics.Count == 0,
            SelectorDiagnostics = selectorDiagnostics,
        };
        nodeInfo.ConnectionEvidence = NetworkConnectionEvidenceCapture.CaptureNode(
            () => node.ConnectedSubnet is { } subnet ? RequireSubnetIdentity(subnet) : null,
            () => ReadIoSystemIdentity(networkInterface),
            relationshipMessages =>
            {
                nodeInfo.SubnetName = ReadConnectedSubnetName(node, nodeDescription, relationshipMessages);
                nodeInfo.IoSystemName = ReadIoSystemName(networkInterface, nodeDescription, relationshipMessages);
            });
        if (!nodeId.IsUsable || !deviceName.IsUsable)
        {
            nodeInfo.ConnectionEvidence.Complete = false;
            nodeInfo.ConnectionEvidence.Messages.Add("Node identity or device owner identity was unreadable.");
        }
        if (nodeInfo.Selectable)
        {
            nodeInfo.Selector = NetworkSelectorFactory.Node(deviceName.Value, nodeId.Value);
        }

        return nodeInfo;
    }

    private static SubnetInfo ReadSubnet(Subnet subnet, List<string> messages, HardwareDiscoveryEvidenceCapture capture)
    {
        var subnetName = ReadOptionalString(() => subnet.Name, "subnet name", messages);
        var subnetDescription = subnetName ?? "(unnamed)";
        var subnetId = ReadExactStringIdentityAttribute(
            (IEngineeringObject)subnet,
            "SubnetId",
            $"Subnet '{subnetDescription}' identity");
        return ReadSubnet(subnet, subnetName, subnetDescription, subnetId, messages, capture);
    }

    private static SubnetInfo ReadSubnet(
        Subnet subnet,
        NetworkObjectDiscoveryEvidenceValue<string> subnetId,
        List<string> messages,
        HardwareDiscoveryEvidenceCapture capture)
    {
        var subnetName = ReadOptionalString(() => subnet.Name, "subnet name", messages);
        var subnetDescription = subnetName ?? "(unnamed)";
        return ReadSubnet(subnet, subnetName, subnetDescription, subnetId, messages, capture);
    }

    private static SubnetInfo ReadSubnet(
        Subnet subnet,
        string? subnetName,
        string subnetDescription,
        NetworkObjectDiscoveryEvidenceValue<string> subnetId,
        List<string> messages,
        HardwareDiscoveryEvidenceCapture capture)
    {
        NetworkObjectDiscoveryEvidence.AddReadMessage(messages, subnetId);
        var selectorDiagnostics = CombineDiagnostics(
            Array.Empty<string>(),
            subnetId.Diagnostic);
        var subnetInfo = new SubnetInfo
        {
            Name = subnetName ?? string.Empty,
            SubnetId = subnetId.IsUsable ? subnetId.Value : string.Empty,
            NetworkType = ReadOptionalEnumName(
                () => subnet.NetType,
                $"subnet '{subnetDescription}' network type",
                messages),
            TypeIdentifier = ReadOptionalString(
                () => subnet.TypeIdentifier,
                $"subnet '{subnetDescription}' type identifier",
                messages),
            Selectable = selectorDiagnostics.Count == 0,
            SelectorDiagnostics = selectorDiagnostics,
        };
        if (subnetInfo.Selectable)
        {
            subnetInfo.Selector = NetworkSelectorFactory.Subnet(subnetId.Value);
        }

        subnetInfo.ConnectionEvidence = NetworkConnectionEvidenceCapture.CaptureSubnet(
            () => subnet.Nodes.Cast<Node>(),
            node => ReadConnectedNodeIdentity(node),
            (node, relationshipMessages) =>
            {
                var connectedNodeName = ReadOptionalString(() => node.Name,
                    $"subnet '{subnetDescription}' connected node name", relationshipMessages);
                if (!string.IsNullOrWhiteSpace(connectedNodeName))
                    subnetInfo.ConnectedNodeNames.Add(connectedNodeName!);
            });
        if (!subnetId.IsUsable)
        {
            subnetInfo.ConnectionEvidence.Complete = false;
            subnetInfo.ConnectionEvidence.Messages.Add(subnetId.Diagnostic);
        }

        capture.Traverse(() => subnet.IoSystems.Cast<IoSystem>(),
            ioSystem => subnetInfo.IoSystems.Add(ReadIoSystem(ioSystem, subnetId, messages)),
            "ioSystemEnumeration", "ioSystemMaterialization");

        subnetInfo.IoSystems = subnetInfo.IoSystems
            .OrderBy(ioSystem => ioSystem.Number)
            .ThenBy(ioSystem => ioSystem.Name, StringComparer.Ordinal)
            .ToList();
        return subnetInfo;
    }

    internal static string RequireSubnetIdentity(Subnet subnet)
    {
        var identity = ReadExactStringIdentityAttribute(subnet, "SubnetId", "Connected subnet identity");
        if (!identity.IsUsable) throw new InvalidOperationException(identity.Diagnostic);
        return identity.Value;
    }

    internal static NetworkNodeIdentityInfo ReadConnectedNodeIdentity(Node node)
    {
        var nodeId = ReadTypedIdentityString(() => node.NodeId, "Connected node identity");
        if (!nodeId.IsUsable) throw new InvalidOperationException(nodeId.Diagnostic);
        IEngineeringObject? current = node;
        var ancestors = new List<IEngineeringObject>();
        Project? project = null;
        while (current is not null)
        {
            if (ancestors.Any(value => ReferenceEquals(value, current))) throw new InvalidOperationException("Connected node hierarchy contains a cycle.");
            ancestors.Add(current);
            if (current is Project owningProject) { project = owningProject; break; }
            current = current.Parent;
        }
        if (project is null) throw new InvalidOperationException("Connected node project was unavailable.");
        return NetworkConnectionEvidenceCapture.CaptureOwner<IEngineeringObject>(node, nodeId.Value,
            value => value.Parent,
            value => value is DeviceItem item ? new NetworkInterfacePathSegmentInfo { Name = item.Name, PositionNumber = item.PositionNumber } : null,
            value => value is Device device ? device.Name : null,
            identity =>
            {
                var name = ReadTypedIdentityString(() => identity.DeviceName, "Connected node owner name");
                if (!name.IsUsable) throw new InvalidOperationException(name.Diagnostic);
                var unreadableOwner = false;
                var matches = ProjectDeviceNameMatcher.FindMatches(project, name.Value, _ => unreadableOwner = true);
                if (unreadableOwner || matches.Count != 1 || !NetworkConnectionEvidenceCapture.ContainsSameObject(ancestors, (IEngineeringObject)matches[0].Device))
                    throw new InvalidOperationException("Connected node owner could not be resolved uniquely across all device scopes.");
                var resolved = NetworkObjectSelectorResolver.ResolveNode(project, NetworkSelectorFactory.QualifiedNode(identity.DeviceName, identity.NodeId, identity.InterfacePath!));
                if (!resolved.Success) throw new InvalidOperationException(resolved.Error);
                return (IEngineeringObject)resolved.Resolved!.Value;
            });
    }

    internal static (string? SubnetId, int? Number) ReadIoSystemIdentity(NetworkInterface networkInterface)
    {
        var systems = new List<IoSystem>();
        foreach (IoController controller in networkInterface.IoControllers)
            if (controller.IoSystem is { } system) systems.Add(system);
        foreach (IoConnector connector in networkInterface.IoConnectors)
            if (connector.ConnectedToIoSystem is { } system) systems.Add(system);
        (string? SubnetId, int? Number) identity = (null, null);
        foreach (var system in systems)
        {
            var candidate = (SubnetId: RequireSubnetIdentity(system.Subnet), Number: (int?)system.Number);
            if (candidate.Number < 0 || identity.Number is not null && identity != candidate)
                throw new InvalidOperationException("IO system relationship was ambiguous or invalid.");
            identity = candidate;
        }
        return identity;
    }

    private static IoSystemInfo ReadIoSystem(
        IoSystem ioSystem,
        NetworkObjectDiscoveryEvidenceValue<string> subnetId,
        List<string> messages)
    {
        var ioSystemName = ReadOptionalString(() => ioSystem.Name, "IO system name", messages);
        var number = ReadTypedIdentityInt(
            () => ioSystem.Number,
            $"IO system '{ioSystemName ?? "(unnamed)"}' number");
        NetworkObjectDiscoveryEvidence.AddReadMessage(messages, number);
        var selectorDiagnostics = CombineDiagnostics(
            Array.Empty<string>(),
            subnetId.Diagnostic,
            number.Diagnostic);
        var ioSystemInfo = new IoSystemInfo
        {
            Name = ioSystemName ?? string.Empty,
            Number = number.IsUsable ? number.Value : null,
            IoControllerName = FindParentDeviceName(ioSystem.Parent, messages),
            Selectable = selectorDiagnostics.Count == 0,
            SelectorDiagnostics = selectorDiagnostics,
        };
        if (ioSystemInfo.Selectable)
        {
            ioSystemInfo.Selector = NetworkSelectorFactory.IoSystem(subnetId.Value, number.Value);
        }

        foreach (IoConnector connectedDevice in ioSystem.ConnectedIoDevices)
        {
            var connectedDeviceName = FindParentDeviceName(connectedDevice, messages);
            if (!string.IsNullOrWhiteSpace(connectedDeviceName))
            {
                ioSystemInfo.ConnectedDeviceNames.Add(connectedDeviceName!);
            }
        }

        return ioSystemInfo;
    }

    private static string? ReadConnectedSubnetName(
        Node node,
        string nodeDescription,
        List<string> messages)
    {
        try
        {
            var connectedSubnet = node.ConnectedSubnet;
            return connectedSubnet is null
                ? null
                : ReadOptionalString(
                    () => connectedSubnet.Name,
                    $"node '{nodeDescription}' connected subnet name",
                    messages);
        }
        catch (EngineeringException exception)
        {
            messages.Add(
                $"Could not read node '{nodeDescription}' connected subnet: {exception.Message}");
            return null;
        }
    }

    private static string? ReadIoSystemName(
        NetworkInterface networkInterface,
        string nodeDescription,
        List<string> messages)
    {
        try
        {
            foreach (IoController ioController in networkInterface.IoControllers)
            {
                var ioSystem = ioController.IoSystem;
                if (ioSystem is null)
                {
                    continue;
                }

                var name = ReadOptionalString(
                    () => ioSystem.Name,
                    $"node '{nodeDescription}' IO system name",
                    messages);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }

            foreach (IoConnector ioConnector in networkInterface.IoConnectors)
            {
                var ioSystem = ioConnector.ConnectedToIoSystem;
                if (ioSystem is null)
                {
                    continue;
                }

                var name = ReadOptionalString(
                    () => ioSystem.Name,
                    $"node '{nodeDescription}' IO system name",
                    messages);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }
        }
        catch (EngineeringException exception)
        {
            messages.Add($"Could not read node '{nodeDescription}' IO system: {exception.Message}");
        }

        return null;
    }

    internal static string? FindParentDeviceName(
        IEngineeringObject? candidate,
        List<string> messages)
    {
        var current = candidate;
        while (current is not null)
        {
            if (current is Device device)
            {
                return ReadOptionalString(() => device.Name, "device name", messages);
            }

            try
            {
                current = current.Parent;
            }
            catch (EngineeringException exception)
            {
                messages.Add($"Could not read parent device: {exception.Message}");
                return null;
            }
        }

        return null;
    }

    internal static NetworkObjectDiscoveryEvidenceValue<string> ReadTypedIdentityString(
        Func<string?> read,
        string field)
    {
        try
        {
            return NetworkObjectDiscoveryEvidence.ReadString(read(), field);
        }
        catch (EngineeringException)
        {
            return NetworkObjectDiscoveryEvidence.UnreadableString(field);
        }
    }

    private static NetworkObjectDiscoveryEvidenceValue<int> ReadTypedIdentityInt(
        Func<int> read,
        string field)
    {
        try
        {
            var value = NetworkObjectDiscoveryEvidence.ReadInt(read(), field);
            return value.IsUsable && value.Value < 0
                ? NetworkObjectDiscoveryEvidenceValue<int>.Unusable(
                    $"{field} was negative; selector not available.",
                    "negative")
                : value;
        }
        catch (EngineeringException)
        {
            return NetworkObjectDiscoveryEvidence.UnreadableInt(field);
        }
    }

    internal static NetworkObjectDiscoveryEvidenceValue<string> ReadExactStringIdentityAttribute(
        IEngineeringObject engineeringObject,
        string attributeName,
        string field)
    {
        try
        {
            return NetworkObjectDiscoveryEvidence.ReadString(
                engineeringObject.GetAttribute(attributeName),
                field);
        }
        catch (EngineeringException)
        {
            return NetworkObjectDiscoveryEvidence.UnreadableString(field);
        }
    }

    private static string? ReadOptionalString(
        Func<string?> read,
        string description,
        List<string> messages)
    {
        try
        {
            return read();
        }
        catch (EngineeringException exception)
        {
            messages.Add($"Could not read {description}: {exception.Message}");
            return null;
        }
    }

    private static string? ReadOptionalEnumName<TEnum>(
        Func<TEnum> read,
        string description,
        List<string> messages)
        where TEnum : struct, Enum
    {
        try
        {
            return Enum.Format(typeof(TEnum), read(), "G");
        }
        catch (Exception exception)
        {
            messages.Add($"Could not read {description}: {exception.Message}");
            return null;
        }
    }

    private static string? ReadExactStringAttribute(
        IEngineeringObject engineeringObject,
        string attributeName,
        string description,
        List<string> messages)
    {
        try
        {
            var value = engineeringObject.GetAttribute(attributeName);
            if (value is null)
            {
                return null;
            }

            if (value is string text)
            {
                return text;
            }

            messages.Add(
                $"Could not read {description}: attribute '{attributeName}' had an unexpected CLR type.");
            return null;
        }
        catch (EngineeringException exception)
        {
            messages.Add($"Could not read {description}: {exception.Message}");
            return null;
        }
    }

    private static List<string> CombineDiagnostics(
        IEnumerable<string> inherited,
        params string[] diagnostics)
        => inherited.Concat(diagnostics)
            .Where(diagnostic => !string.IsNullOrWhiteSpace(diagnostic))
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
