using System;
using System.Collections.Generic;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

public static class HardwareConfigReader
{
    /// <summary>
    /// Lightweight default read used by the internal network-write state snapshot and the subnet
    /// mutation probe: no device filter and no I/O map, so the snapshot stays byte-identical to
    /// earlier versions and the safety-token hashes never change.
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

        IoTagIndex? tagIndex = null;
        if (includeTagMatches)
        {
            tagIndex = ResolveTagIndex(project, plcName, result.Messages);
        }

        var selectedDevices = SelectDevices(project, deviceName, result.Messages);

        foreach (var (device, nameEvidence) in selectedDevices)
        {
            try
            {
                result.Devices.Add(ReadDevice(device, nameEvidence, result.Messages, includeIoDetails, tagIndex));
            }
            catch (EngineeringException exception)
            {
                result.Messages.Add(
                    $"Skipped a device while reading hardware configuration: {exception.Message}");
            }
        }

        foreach (Subnet subnet in project.Subnets)
        {
            try
            {
                result.Subnets.Add(ReadSubnet(subnet, result.Messages));
            }
            catch (EngineeringException exception)
            {
                result.Messages.Add(
                    $"Skipped a subnet while reading hardware configuration: {exception.Message}");
            }
        }

        result.Devices = result.Devices
            .OrderBy(device => device.Name, StringComparer.Ordinal)
            .ToList();
        result.Subnets = result.Subnets
            .OrderBy(subnet => subnet.SubnetId, StringComparer.Ordinal)
            .ToList();

        return result;
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
        List<string> messages)
    {
        var candidates = new List<(Device Device, NetworkObjectDiscoveryEvidenceValue<string> NameEvidence)>();
        foreach (Device device in project.Devices)
        {
            var nameEvidence = ReadTypedIdentityString(() => device.Name, "Device name");
            candidates.Add((device, nameEvidence));
        }

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

        messages.Add(matches.Count == 0
            ? $"No device named '{deviceName}' was found; no devices are reported."
            : $"More than one device matches '{deviceName}'; no devices are reported because the device filter is ambiguous.");
        return Array.Empty<(Device, NetworkObjectDiscoveryEvidenceValue<string>)>();
    }

    private static DeviceInfo ReadDevice(
        Device device,
        NetworkObjectDiscoveryEvidenceValue<string> deviceName,
        List<string> messages,
        bool includeIoDetails,
        IoTagIndex? tagIndex)
    {
        AddReadMessage(messages, deviceName, "device name");
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
            device.DeviceItems,
            $"device '{deviceDescription}'",
            messages,
            deviceName,
            Array.Empty<DeviceItemPathSegmentInfo>(),
            Array.Empty<string>(),
            includeIoDetails,
            tagIndex);
        return deviceInfo;
    }

    private static List<DeviceItemInfo> ReadDeviceItems(
        DeviceItemComposition items,
        string ownerDescription,
        List<string> messages,
        NetworkObjectDiscoveryEvidenceValue<string> deviceName,
        IReadOnlyList<DeviceItemPathSegmentInfo> parentPath,
        IReadOnlyList<string> parentPathDiagnostics,
        bool includeIoDetails,
        IoTagIndex? tagIndex)
    {
        var result = new List<DeviceItemInfo>();
        var siblingIndex = 0;

        foreach (DeviceItem item in items)
        {
            try
            {
                result.Add(ReadDeviceItem(
                    item,
                    messages,
                    deviceName,
                    parentPath,
                    parentPathDiagnostics,
                    siblingIndex,
                    includeIoDetails,
                    tagIndex));
            }
            catch (EngineeringException exception)
            {
                messages.Add(
                    $"Skipped a device item while reading {ownerDescription}: {exception.Message}");
            }

            siblingIndex++;
        }

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
        IoTagIndex? tagIndex)
    {
        var itemName = ReadTypedIdentityString(() => item.Name, "Device item name");
        var itemDescription = itemName.IsUsable ? itemName.Value : "(unnamed)";
        var typeIdentifier = ReadTypedIdentityString(
            () => item.TypeIdentifier,
            $"Device item '{itemDescription}' type identifier");
        var positionNumber = ReadTypedIdentityInt(
            () => item.PositionNumber,
            $"Device item '{itemDescription}' position number");
        AddReadMessage(messages, itemName, "device item name");
        AddReadMessage(messages, typeIdentifier, $"device item '{itemDescription}' type identifier");
        AddReadMessage(messages, positionNumber, $"device item '{itemDescription}' position number");

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
            Address = ReadExactStringAttribute(
                (IEngineeringObject)item,
                "Address",
                $"device item '{itemDescription}' address",
                messages),
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
            pathDiagnostics);
        itemInfo.Items = ReadDeviceItems(
            item.DeviceItems,
            $"device item '{itemDescription}'",
            messages,
            deviceName,
            itemPath,
            pathDiagnostics,
            includeIoDetails,
            tagIndex);

        return itemInfo;
    }

    private static List<NetworkInterfaceInfo> ReadNetworkInterfaces(
        DeviceItem item,
        string itemDescription,
        List<string> messages,
        NetworkObjectDiscoveryEvidenceValue<string> deviceName,
        IReadOnlyList<DeviceItemPathSegmentInfo> itemPath,
        IReadOnlyList<string> itemPathDiagnostics)
    {
        var result = new List<NetworkInterfaceInfo>();

        try
        {
            var networkInterface = ((IEngineeringServiceProvider)item).GetService<NetworkInterface>();
            if (networkInterface is not null)
            {
                result.Add(ReadNetworkInterface(
                    networkInterface,
                    messages,
                    deviceName,
                    itemPath,
                    itemPathDiagnostics));
            }
        }
        catch (EngineeringException exception)
        {
            messages.Add(
                $"Could not read network interface while reading device item "
                    + $"'{itemDescription}': {exception.Message}");
        }

        return result;
    }

    private static NetworkInterfaceInfo ReadNetworkInterface(
        NetworkInterface networkInterface,
        List<string> messages,
        NetworkObjectDiscoveryEvidenceValue<string> deviceName,
        IReadOnlyList<DeviceItemPathSegmentInfo> itemPath,
        IReadOnlyList<string> itemPathDiagnostics)
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

        foreach (Node node in networkInterface.Nodes)
        {
            try
            {
                interfaceInfo.Nodes.Add(ReadNode(node, networkInterface, messages, deviceName));
            }
            catch (EngineeringException exception)
            {
                messages.Add(
                    $"Skipped a node while reading network interface "
                        + $"'{interfaceInfo.Name}': {exception.Message}");
            }
        }

        interfaceInfo.Nodes = interfaceInfo.Nodes
            .OrderBy(node => node.NodeId, StringComparer.Ordinal)
            .ToList();
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
        AddReadMessage(messages, nodeId, $"node '{nodeDescription}' identity");
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
            SubnetName = ReadConnectedSubnetName(node, nodeDescription, messages),
            IoSystemName = ReadIoSystemName(networkInterface, nodeDescription, messages),
            Selectable = selectorDiagnostics.Count == 0,
            SelectorDiagnostics = selectorDiagnostics,
        };
        if (nodeInfo.Selectable)
        {
            nodeInfo.Selector = NetworkSelectorFactory.Node(deviceName.Value, nodeId.Value);
        }

        return nodeInfo;
    }

    private static SubnetInfo ReadSubnet(Subnet subnet, List<string> messages)
    {
        var subnetName = ReadOptionalString(() => subnet.Name, "subnet name", messages);
        var subnetDescription = subnetName ?? "(unnamed)";
        var subnetId = ReadExactStringIdentityAttribute(
            (IEngineeringObject)subnet,
            "SubnetId",
            $"Subnet '{subnetDescription}' identity");
        AddReadMessage(messages, subnetId, $"subnet '{subnetDescription}' identity");
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

        foreach (Node node in subnet.Nodes)
        {
            var connectedNodeName = ReadOptionalString(
                () => node.Name,
                $"subnet '{subnetDescription}' connected node name",
                messages);
            if (!string.IsNullOrWhiteSpace(connectedNodeName))
            {
                subnetInfo.ConnectedNodeNames.Add(connectedNodeName!);
            }
        }

        foreach (IoSystem ioSystem in subnet.IoSystems)
        {
            try
            {
                subnetInfo.IoSystems.Add(ReadIoSystem(ioSystem, subnetId, messages));
            }
            catch (EngineeringException exception)
            {
                messages.Add(
                    $"Skipped an IO system while reading subnet "
                        + $"'{subnetDescription}': {exception.Message}");
            }
        }

        subnetInfo.IoSystems = subnetInfo.IoSystems
            .OrderBy(ioSystem => ioSystem.Number)
            .ThenBy(ioSystem => ioSystem.Name, StringComparer.Ordinal)
            .ToList();
        return subnetInfo;
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
        AddReadMessage(messages, number, $"IO system '{ioSystemName ?? "(unnamed)"}' number");
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

    private static NetworkObjectDiscoveryEvidenceValue<string> ReadTypedIdentityString(
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

    private static NetworkObjectDiscoveryEvidenceValue<string> ReadExactStringIdentityAttribute(
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

    private static void AddReadMessage<T>(
        List<string> messages,
        NetworkObjectDiscoveryEvidenceValue<T> evidence,
        string description)
    {
        if (!evidence.IsUsable)
        {
            messages.Add($"Could not read {description}: {evidence.Diagnostic}");
        }
    }
}
