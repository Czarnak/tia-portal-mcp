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
                result.Devices.Add(ReadDevice(device));
            }
            catch (EngineeringException ex)
            {
                Console.Error.WriteLine($"Skipping device while reading hardware configuration: {ex.Message}");
            }
        }

        foreach (Subnet subnet in project.Subnets)
        {
            try
            {
                result.Subnets.Add(ReadSubnet(subnet));
            }
            catch (EngineeringException ex)
            {
                Console.Error.WriteLine($"Skipping subnet while reading hardware configuration: {ex.Message}");
            }
        }

        return result;
    }

    private static DeviceInfo ReadDevice(Device device)
    {
        var deviceInfo = new DeviceInfo
        {
            Name = ReadString(() => device.Name, "device name"),
            TypeIdentifier = ReadString(() => device.TypeIdentifier, $"device '{device.Name}' type identifier")
        };

        deviceInfo.Items = ReadDeviceItems(device.DeviceItems, $"device '{deviceInfo.Name}'");

        return deviceInfo;
    }

    private static List<DeviceItemInfo> ReadDeviceItems(DeviceItemComposition items, string ownerDescription)
    {
        var result = new List<DeviceItemInfo>();

        foreach (DeviceItem item in items)
        {
            try
            {
                result.Add(ReadDeviceItem(item));
            }
            catch (EngineeringException ex)
            {
                Console.Error.WriteLine($"Skipping a device item while reading {ownerDescription}: {ex.Message}");
            }
        }

        return result;
    }

    private static DeviceItemInfo ReadDeviceItem(DeviceItem item)
    {
        var itemName = ReadString(() => item.Name, "device item name");
        var itemInfo = new DeviceItemInfo
        {
            Name = itemName,
            TypeIdentifier = ReadString(() => item.TypeIdentifier, $"device item '{itemName}' type identifier"),
            PositionNumber = ReadInt(() => item.PositionNumber, $"device item '{itemName}' position number"),
            Address = ReadAttribute((IEngineeringObject)item, "Address", $"device item '{itemName}' address")
        };

        var networkInterfaces = ReadNetworkInterfaces(item, itemName);
        if (networkInterfaces.Count > 0)
        {
            itemInfo.NetworkInterfaces = networkInterfaces;
        }

        var children = ReadDeviceItems(item.DeviceItems, $"device item '{itemName}'");
        if (children.Count > 0)
        {
            itemInfo.Items = children;
        }

        return itemInfo;
    }

    private static List<NetworkInterfaceInfo> ReadNetworkInterfaces(DeviceItem item, string itemName)
    {
        var result = new List<NetworkInterfaceInfo>();

        try
        {
            var networkInterface = ((IEngineeringServiceProvider)item).GetService<NetworkInterface>();
            if (networkInterface is not null)
            {
                result.Add(ReadNetworkInterface(networkInterface));
            }
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine($"Skipping network interface while reading device item '{itemName}': {ex.Message}");
        }

        return result;
    }

    private static NetworkInterfaceInfo ReadNetworkInterface(NetworkInterface networkInterface)
    {
        var interfaceName = ReadPropertyOrAttribute(networkInterface, "Name", "network interface name") ?? string.Empty;
        var interfaceInfo = new NetworkInterfaceInfo
        {
            Name = interfaceName
        };

        foreach (Node node in networkInterface.Nodes)
        {
            try
            {
                interfaceInfo.Nodes.Add(ReadNode(node, networkInterface));
            }
            catch (EngineeringException ex)
            {
                Console.Error.WriteLine($"Skipping a node while reading network interface '{interfaceName}': {ex.Message}");
            }
        }

        return interfaceInfo;
    }

    private static NodeInfo ReadNode(Node node, NetworkInterface networkInterface)
    {
        var nodeName = ReadString(() => node.Name, "node name");
        return new NodeInfo
        {
            Name = nodeName,
            IpAddress = ReadAttribute((IEngineeringObject)node, "Address", $"node '{nodeName}' IP address"),
            SubnetMask = ReadAttribute((IEngineeringObject)node, "SubnetMask", $"node '{nodeName}' subnet mask"),
            PnDeviceName = ReadAttribute((IEngineeringObject)node, "PnDeviceName", $"node '{nodeName}' PROFINET device name"),
            SubnetName = ReadConnectedSubnetName(node, nodeName),
            IoSystemName = ReadIoSystemName(networkInterface, nodeName)
        };
    }

    private static SubnetInfo ReadSubnet(Subnet subnet)
    {
        var subnetName = ReadString(() => subnet.Name, "subnet name");
        var subnetInfo = new SubnetInfo
        {
            Name = subnetName,
            TypeIdentifier = ReadAttribute((IEngineeringObject)subnet, "TypeIdentifier", $"subnet '{subnetName}' type identifier") ??
                ReadPropertyOrAttribute(subnet, "NetType", $"subnet '{subnetName}' network type")
        };

        foreach (var node in ReadEnumerableProperty(subnet, "Nodes", $"subnet '{subnetName}' nodes"))
        {
            var connectedNodeName = ReadPropertyOrAttribute(node, "Name", $"subnet '{subnetName}' connected node");
            if (!string.IsNullOrWhiteSpace(connectedNodeName))
            {
                subnetInfo.ConnectedNodeNames.Add(connectedNodeName!);
            }
        }

        foreach (var ioSystem in ReadEnumerableProperty(subnet, "IoSystems", $"subnet '{subnetName}' IO systems"))
        {
            try
            {
                subnetInfo.IoSystems.Add(ReadIoSystem(ioSystem));
            }
            catch (EngineeringException ex)
            {
                Console.Error.WriteLine($"Skipping an IO system while reading subnet '{subnetName}': {ex.Message}");
            }
        }

        return subnetInfo;
    }

    private static IoSystemInfo ReadIoSystem(object ioSystem)
    {
        var ioSystemName = ReadPropertyOrAttribute(ioSystem, "Name", "IO system name") ?? string.Empty;
        var ioSystemInfo = new IoSystemInfo
        {
            Name = ioSystemName,
            IoControllerName = FindParentDeviceName(ReadProperty(ioSystem, "IoController"))
        };

        foreach (var connectedDevice in ReadEnumerableProperty(ioSystem, "ConnectedIoDevices", $"IO system '{ioSystemName}' connected IO devices"))
        {
            var connectedDeviceName = FindParentDeviceName(connectedDevice) ??
                ReadPropertyOrAttribute(connectedDevice, "Name", $"IO system '{ioSystemName}' connected IO device");
            if (!string.IsNullOrWhiteSpace(connectedDeviceName))
            {
                ioSystemInfo.ConnectedDeviceNames.Add(connectedDeviceName!);
            }
        }

        return ioSystemInfo;
    }

    private static string? ReadConnectedSubnetName(Node node, string nodeName)
    {
        var connectedSubnet = ReadProperty(node, "ConnectedSubnet");
        return connectedSubnet is null
            ? null
            : ReadPropertyOrAttribute(connectedSubnet, "Name", $"node '{nodeName}' connected subnet");
    }

    private static string? ReadIoSystemName(NetworkInterface networkInterface, string nodeName)
    {
        foreach (var ownerProperty in new[] { "IoControllers", "IoConnectors" })
        {
            foreach (var item in ReadEnumerableProperty(networkInterface, ownerProperty, $"node '{nodeName}' {ownerProperty}"))
            {
                var ioSystem = ReadProperty(item, "IoSystem") ?? item;
                var name = ReadPropertyOrAttribute(ioSystem, "Name", $"node '{nodeName}' IO system");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }
        }

        return null;
    }

    private static string? FindParentDeviceName(object? candidate)
    {
        var current = candidate;
        while (current is not null)
        {
            if (current is Device device)
            {
                return ReadString(() => device.Name, "device name");
            }

            var name = ReadPropertyOrAttribute(current, "DeviceName", "parent device name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            current = ReadProperty(current, "Parent");
        }

        return null;
    }

    private static string ReadString(Func<string> read, string description)
    {
        try
        {
            return read();
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine($"Skipping {description}: {ex.Message}");
            return string.Empty;
        }
    }

    private static int ReadInt(Func<int> read, string description)
    {
        try
        {
            return read();
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine($"Skipping {description}: {ex.Message}");
            return 0;
        }
    }

    private static string? ReadAttribute(IEngineeringObject engineeringObject, string attributeName, string description)
    {
        try
        {
            return engineeringObject.GetAttribute(attributeName)?.ToString();
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine($"Skipping {description}: {ex.Message}");
            return null;
        }
    }

    private static string? ReadPropertyOrAttribute(object instance, string name, string description)
    {
        var value = ReadProperty(instance, name);
        if (value is not null)
        {
            return value.ToString();
        }

        return instance is IEngineeringObject engineeringObject
            ? ReadAttribute(engineeringObject, name, description)
            : null;
    }

    private static object? ReadProperty(object? instance, string propertyName)
    {
        return OpennessReflection.ReadProperty(instance, propertyName);
    }

    private static IEnumerable<object> ReadEnumerableProperty(object instance, string propertyName, string description)
    {
        return OpennessReflection.ReadEnumerableProperty(instance, propertyName, description);
    }
}
