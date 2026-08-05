using System.Globalization;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

public static class HardwareConfigReader
{
    public static HardwareConfigInfo Read(Project project)
    {
        var result = new HardwareConfigInfo();

        foreach (Device device in project.Devices)
        {
            try
            {
                result.Devices.Add(ReadDevice(device, result.Messages));
            }
            catch (EngineeringException ex)
            {
                result.Messages.Add($"Skipped a device while reading hardware configuration: {ex.Message}");
            }
        }

        foreach (Subnet subnet in project.Subnets)
        {
            try
            {
                result.Subnets.Add(ReadSubnet(subnet, result.Messages));
            }
            catch (EngineeringException ex)
            {
                result.Messages.Add($"Skipped a subnet while reading hardware configuration: {ex.Message}");
            }
        }

        // Stable ordering: devices by name, subnets by subnet-id.
        result.Devices = result.Devices
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .ToList();
        result.Subnets = result.Subnets
            .OrderBy(s => s.SubnetId, StringComparer.Ordinal)
            .ToList();

        return result;
    }

    private static DeviceInfo ReadDevice(Device device, List<string> messages)
    {
        var deviceInfo = new DeviceInfo
        {
            Name = ReadString(() => device.Name, "device name", messages)
        };
        var deviceDescription = deviceInfo.Name ?? "(unnamed)";
        deviceInfo.TypeIdentifier = ReadString(
            () => device.TypeIdentifier, $"device '{deviceDescription}' type identifier", messages);

        // Device name is used when building selectors in the item tree. Pass an empty string
        // when the name is unreadable so the selector builder can detect the gap and mark items
        // as unselectable rather than inventing a placeholder identity.
        var deviceName = deviceInfo.Name ?? string.Empty;
        deviceInfo.Items = ReadDeviceItems(
            device.DeviceItems, $"device '{deviceDescription}'", messages,
            deviceName, new List<DeviceItemPathSegmentInfo>());

        return deviceInfo;
    }

    /// <summary>
    /// Enumerates the composition with a zero-based sibling index. The index is stable for the
    /// lifetime of the project and is used to distinguish identically-named siblings. The
    /// composition order is NOT changed here — the index is only meaningful relative to this
    /// enumeration order.
    /// </summary>
    private static List<DeviceItemInfo> ReadDeviceItems(
        DeviceItemComposition items,
        string ownerDescription,
        List<string> messages,
        string deviceName,
        IReadOnlyList<DeviceItemPathSegmentInfo> parentPath)
    {
        var result = new List<DeviceItemInfo>();
        var siblingIndex = 0;

        foreach (DeviceItem item in items)
        {
            try
            {
                result.Add(ReadDeviceItem(item, messages, deviceName, parentPath, siblingIndex));
            }
            catch (EngineeringException ex)
            {
                messages.Add($"Skipped a device item while reading {ownerDescription}: {ex.Message}");
            }

            siblingIndex++;
        }

        return result;
    }

    private static DeviceItemInfo ReadDeviceItem(
        DeviceItem item,
        List<string> messages,
        string deviceName,
        IReadOnlyList<DeviceItemPathSegmentInfo> parentPath,
        int siblingIndex)
    {
        var itemName = ReadString(() => item.Name, "device item name", messages);
        var itemDescription = itemName ?? "(unnamed)";
        var typeIdentifier = ReadString(
            () => item.TypeIdentifier, $"device item '{itemDescription}' type identifier", messages);
        var positionNumber = ReadInt(
            () => item.PositionNumber, $"device item '{itemDescription}' position number", messages);

        // Build the path segment for this item and the full path from the device root.
        var segment = new DeviceItemPathSegmentInfo
        {
            Index = siblingIndex,
            Name = itemName ?? string.Empty,
            PositionNumber = positionNumber,
            TypeIdentifier = typeIdentifier ?? string.Empty,
        };
        var itemPath = new List<DeviceItemPathSegmentInfo>(parentPath) { segment };

        var itemInfo = new DeviceItemInfo
        {
            Name = itemName,
            TypeIdentifier = typeIdentifier,
            PositionNumber = positionNumber,
            Address = ReadAttribute(
                (IEngineeringObject)item, "Address",
                $"device item '{itemDescription}' address", messages)
        };

        // Build the deviceItem selector when the device name is known.
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            itemInfo.Selectable = false;
            itemInfo.SelectorDiagnostics.Add(
                "Device name could not be read; deviceItem selector not available.");
        }
        else
        {
            itemInfo.Selectable = true;
            itemInfo.Selector = NetworkSelectorFactory.DeviceItem(deviceName, itemPath);
        }

        // Always assigned, even when empty: a consumer resolving a write target walks this tree and
        // must not have to tell "no interfaces" apart from "collection omitted".
        itemInfo.CommunicationConnections = CommunicationConnectionReader
            .Read(item, deviceName, itemPath, messages)
            .Select(result => result.Summary)
            .ToList();
        itemInfo.NetworkInterfaces =
            ReadNetworkInterfaces(item, itemDescription, messages, deviceName, itemPath);
        itemInfo.Items = ReadDeviceItems(
            item.DeviceItems, $"device item '{itemDescription}'", messages, deviceName, itemPath);

        return itemInfo;
    }

    private static List<NetworkInterfaceInfo> ReadNetworkInterfaces(
        DeviceItem item,
        string itemDescription,
        List<string> messages,
        string deviceName,
        IReadOnlyList<DeviceItemPathSegmentInfo> itemPath)
    {
        var result = new List<NetworkInterfaceInfo>();

        try
        {
            var networkInterface = ((IEngineeringServiceProvider)item).GetService<NetworkInterface>();
            if (networkInterface is not null)
            {
                result.Add(ReadNetworkInterface(networkInterface, messages, deviceName, itemPath));
            }
        }
        catch (EngineeringException ex)
        {
            messages.Add(
                $"Could not read network interface while reading device item '{itemDescription}': {ex.Message}");
        }

        return result;
    }

    private static NetworkInterfaceInfo ReadNetworkInterface(
        NetworkInterface networkInterface,
        List<string> messages,
        string deviceName,
        IReadOnlyList<DeviceItemPathSegmentInfo> itemPath)
    {
        var interfaceName =
            ReadPropertyOrAttribute(networkInterface, "Name", "network interface name", messages)
            ?? string.Empty;
        var interfaceInfo = new NetworkInterfaceInfo
        {
            Name = interfaceName
        };

        // Build the networkInterface selector when the device name is known.
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            interfaceInfo.Selectable = false;
            interfaceInfo.SelectorDiagnostics.Add(
                "Device name could not be read; networkInterface selector not available.");
        }
        else
        {
            interfaceInfo.Selectable = true;
            interfaceInfo.Selector = NetworkSelectorFactory.NetworkInterface(
                deviceName, itemPath,
                interfaceName.Length > 0 ? interfaceName : null,
                interfaceType: null,
                interfaceOperatingMode: null);
        }

        foreach (Node node in networkInterface.Nodes)
        {
            try
            {
                interfaceInfo.Nodes.Add(ReadNode(node, networkInterface, messages, deviceName));
            }
            catch (EngineeringException ex)
            {
                messages.Add(
                    $"Skipped a node while reading network interface '{interfaceName}': {ex.Message}");
            }
        }

        // Stable ordering: nodes by node-id ordinal.
        interfaceInfo.Nodes = interfaceInfo.Nodes
            .OrderBy(n => n.NodeId, StringComparer.Ordinal)
            .ToList();

        return interfaceInfo;
    }

    private static NodeInfo ReadNode(
        Node node,
        NetworkInterface networkInterface,
        List<string> messages,
        string deviceName)
    {
        var nodeName = ReadString(() => node.Name, "node name", messages);
        var nodeDescription = nodeName ?? "(unnamed)";
        var nodeId = ReadPropertyOrAttribute(
            node, "NodeId", $"node '{nodeDescription}' node id", messages) ?? string.Empty;

        var nodeInfo = new NodeInfo
        {
            Name = nodeName ?? string.Empty,
            NodeId = nodeId,
            NodeType = ReadPropertyOrAttribute(
                node, "NodeType", $"node '{nodeDescription}' node type", messages),
            IpAddress = ReadAttribute(
                (IEngineeringObject)node, "Address",
                $"node '{nodeDescription}' IP address", messages),
            SubnetMask = ReadAttribute(
                (IEngineeringObject)node, "SubnetMask",
                $"node '{nodeDescription}' subnet mask", messages),
            PnDeviceName = ReadAttribute(
                (IEngineeringObject)node, "PnDeviceName",
                $"node '{nodeDescription}' PROFINET device name", messages),
            SubnetName = ReadConnectedSubnetName(node, nodeDescription, messages),
            IoSystemName = ReadIoSystemName(networkInterface, nodeDescription, messages)
        };

        // Build the node selector when device name and node id are both known.
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            nodeInfo.Selectable = false;
            nodeInfo.SelectorDiagnostics.Add(
                "Device name could not be read; node selector not available.");
        }
        else if (string.IsNullOrWhiteSpace(nodeId))
        {
            nodeInfo.Selectable = false;
            nodeInfo.SelectorDiagnostics.Add(
                "Node identity could not be read; node selector not available.");
        }
        else
        {
            nodeInfo.Selectable = true;
            nodeInfo.Selector = NetworkSelectorFactory.Node(deviceName, nodeId);
        }

        return nodeInfo;
    }

    private static SubnetInfo ReadSubnet(Subnet subnet, List<string> messages)
    {
        var subnetName = ReadString(() => subnet.Name, "subnet name", messages);
        var subnetDescription = subnetName ?? "(unnamed)";
        var networkType = ReadPropertyOrAttribute(
            subnet, "NetType", $"subnet '{subnetDescription}' network type", messages);
        var subnetId = ReadPropertyOrAttribute(
            subnet, "SubnetId", $"subnet '{subnetDescription}' subnet id", messages) ?? string.Empty;

        var subnetInfo = new SubnetInfo
        {
            Name = subnetName ?? string.Empty,
            SubnetId = subnetId,
            NetworkType = networkType,
            TypeIdentifier =
                ReadAttribute(
                    (IEngineeringObject)subnet, "TypeIdentifier",
                    $"subnet '{subnetDescription}' type identifier", messages)
                ?? networkType
        };

        // Build the subnet selector when the subnet id is known.
        if (string.IsNullOrWhiteSpace(subnetId))
        {
            subnetInfo.Selectable = false;
            subnetInfo.SelectorDiagnostics.Add(
                "Subnet identity could not be read; subnet selector not available.");
        }
        else
        {
            subnetInfo.Selectable = true;
            subnetInfo.Selector = NetworkSelectorFactory.Subnet(subnetId);
        }

        foreach (var node in ReadEnumerableProperty(
            subnet, "Nodes", $"subnet '{subnetDescription}' nodes"))
        {
            var connectedNodeName = ReadPropertyOrAttribute(
                node, "Name", $"subnet '{subnetDescription}' connected node", messages);
            if (!string.IsNullOrWhiteSpace(connectedNodeName))
            {
                subnetInfo.ConnectedNodeNames.Add(connectedNodeName!);
            }
        }

        foreach (var ioSystem in ReadEnumerableProperty(
            subnet, "IoSystems", $"subnet '{subnetDescription}' IO systems"))
        {
            try
            {
                subnetInfo.IoSystems.Add(ReadIoSystem(ioSystem, subnetId, messages));
            }
            catch (EngineeringException ex)
            {
                messages.Add(
                    $"Skipped an IO system while reading subnet '{subnetDescription}': {ex.Message}");
            }
        }

        // Stable ordering: IO systems by number then name.
        subnetInfo.IoSystems = subnetInfo.IoSystems
            .OrderBy(io => io.Number)
            .ThenBy(io => io.Name, StringComparer.Ordinal)
            .ToList();

        return subnetInfo;
    }

    private static IoSystemInfo ReadIoSystem(
        object ioSystem, string subnetId, List<string> messages)
    {
        var ioSystemName =
            ReadPropertyOrAttribute(ioSystem, "Name", "IO system name", messages) ?? string.Empty;
        var number = ReadIntPropertyOrAttribute(
            ioSystem, "Number", $"IO system '{ioSystemName}' number", messages);

        var ioSystemInfo = new IoSystemInfo
        {
            Name = ioSystemName,
            Number = number,
            IoControllerName = FindParentDeviceName(ReadProperty(ioSystem, "IoController"), messages)
        };

        // Build the IO system selector when both subnet id and number are known.
        if (string.IsNullOrWhiteSpace(subnetId))
        {
            ioSystemInfo.Selectable = false;
            ioSystemInfo.SelectorDiagnostics.Add(
                "Subnet identity could not be read; ioSystem selector not available.");
        }
        else if (number is null)
        {
            ioSystemInfo.Selectable = false;
            ioSystemInfo.SelectorDiagnostics.Add(
                "IO system number could not be read; ioSystem selector not available.");
        }
        else
        {
            ioSystemInfo.Selectable = true;
            ioSystemInfo.Selector = NetworkSelectorFactory.IoSystem(subnetId, number.Value);
        }

        foreach (var connectedDevice in ReadEnumerableProperty(
            ioSystem, "ConnectedIoDevices", $"IO system '{ioSystemName}' connected IO devices"))
        {
            var connectedDeviceName =
                FindParentDeviceName(connectedDevice, messages)
                ?? ReadPropertyOrAttribute(
                    connectedDevice, "Name",
                    $"IO system '{ioSystemName}' connected IO device", messages);
            if (!string.IsNullOrWhiteSpace(connectedDeviceName))
            {
                ioSystemInfo.ConnectedDeviceNames.Add(connectedDeviceName!);
            }
        }

        return ioSystemInfo;
    }

    private static string? ReadConnectedSubnetName(
        Node node, string nodeDescription, List<string> messages)
    {
        var connectedSubnet = ReadProperty(node, "ConnectedSubnet");
        return connectedSubnet is null
            ? null
            : ReadPropertyOrAttribute(
                connectedSubnet, "Name",
                $"node '{nodeDescription}' connected subnet", messages);
    }

    private static string? ReadIoSystemName(
        NetworkInterface networkInterface, string nodeDescription, List<string> messages)
    {
        foreach (var ownerProperty in new[] { "IoControllers", "IoConnectors" })
        {
            foreach (var item in ReadEnumerableProperty(
                networkInterface, ownerProperty, $"node '{nodeDescription}' {ownerProperty}"))
            {
                var ioSystem = ReadProperty(item, "IoSystem") ?? item;
                var name = ReadPropertyOrAttribute(
                    ioSystem, "Name", $"node '{nodeDescription}' IO system", messages);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }
        }

        return null;
    }

    private static string? FindParentDeviceName(object? candidate, List<string> messages)
    {
        var current = candidate;
        while (current is not null)
        {
            if (current is Device device)
            {
                return ReadString(() => device.Name, "device name", messages);
            }

            var name = ReadPropertyOrAttribute(current, "DeviceName", "parent device name", messages);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            current = ReadProperty(current, "Parent");
        }

        return null;
    }

    private static string? ReadString(Func<string> read, string description, List<string> messages)
    {
        try
        {
            return read();
        }
        catch (EngineeringException ex)
        {
            messages.Add($"Could not read {description}: {ex.Message}");
            return null;
        }
    }

    private static int? ReadInt(Func<int> read, string description, List<string> messages)
    {
        try
        {
            return read();
        }
        catch (EngineeringException ex)
        {
            messages.Add($"Could not read {description}: {ex.Message}");
            return null;
        }
    }

    private static string? ReadAttribute(
        IEngineeringObject engineeringObject,
        string attributeName,
        string description,
        List<string> messages)
    {
        try
        {
            return engineeringObject.GetAttribute(attributeName)?.ToString();
        }
        catch (EngineeringException ex)
        {
            messages.Add($"Could not read {description}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Reads an integer identity. A value that is present but not an integer is reported as a
    /// message and read as null: an identity is never guessed, because a guessed identity could
    /// later satisfy a write selector that should not have matched.
    /// </summary>
    private static int? ReadIntPropertyOrAttribute(
        object instance, string name, string description, List<string> messages)
    {
        var value = ReadPropertyOrAttribute(instance, name, description, messages);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        messages.Add($"Could not read {description}: '{value}' is not an integer.");
        return null;
    }

    private static string? ReadPropertyOrAttribute(
        object instance, string name, string description, List<string> messages)
    {
        var value = ReadProperty(instance, name);
        if (value is not null)
        {
            return value.ToString();
        }

        return instance is IEngineeringObject engineeringObject
            ? ReadAttribute(engineeringObject, name, description, messages)
            : null;
    }

    private static object? ReadProperty(object? instance, string propertyName)
    {
        return OpennessReflection.ReadProperty(instance, propertyName);
    }

    private static IEnumerable<object> ReadEnumerableProperty(
        object instance, string propertyName, string description)
    {
        return OpennessReflection.ReadEnumerableProperty(instance, propertyName, description);
    }
}
