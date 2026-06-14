using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

public static class NetworkDeviceConfigurator
{
    public static ConfigureNetworkDeviceResultInfo Configure(
        Project project,
        string deviceName,
        string? ipAddress,
        string? subnetMask,
        string? pnDeviceName,
        string? subnetName,
        string? ioSystemName)
    {
        var result = new ConfigureNetworkDeviceResultInfo
        {
            DeviceName = deviceName
        };

        var device = FindDevice(project, deviceName) ??
            throw new InvalidOperationException($"Device '{deviceName}' was not found in the project.");

        var networkInterface = FindNetworkInterface(device);
        if (networkInterface is null)
        {
            throw new InvalidOperationException($"Device '{deviceName}' does not expose a network interface.");
        }

        var node = GetFirstNode(networkInterface) ??
            throw new InvalidOperationException($"Device '{deviceName}' network interface does not expose a node.");

        ApplyNodeAttribute(node, "Address", ipAddress, result);
        ApplyNodeAttribute(node, "SubnetMask", subnetMask, result);
        ApplyNodeAttribute(node, "PnDeviceName", pnDeviceName, result);

        var subnetRequested = !string.IsNullOrWhiteSpace(subnetName);
        object? connectedSubnet = null;
        if (subnetRequested)
        {
            connectedSubnet = ConnectSubnet(project, node, subnetName!, result);
        }

        if (!string.IsNullOrWhiteSpace(ioSystemName))
        {
            if (connectedSubnet is null)
            {
                if (subnetRequested)
                {
                    result.SkippedSettings["IoSystemName"] = "Requested subnet was not connected, so IO system lookup was skipped.";
                    return result;
                }

                connectedSubnet = ReadProperty(node, "ConnectedSubnet");
            }

            if (connectedSubnet is null)
            {
                result.SkippedSettings["IoSystemName"] = "No connected subnet is available for IO system lookup.";
            }
            else
            {
                ConnectIoSystem(networkInterface, connectedSubnet, ioSystemName!, result);
            }
        }

        if (result.AppliedSettings.Count == 0 && result.SkippedSettings.Count == 0)
        {
            result.Messages.Add("No network settings were provided.");
        }

        return result;
    }

    private static Device? FindDevice(Project project, string deviceName)
    {
        foreach (Device device in project.Devices)
        {
            if (string.Equals(device.Name, deviceName, StringComparison.OrdinalIgnoreCase))
            {
                return device;
            }
        }

        return null;
    }

    private static NetworkInterface? FindNetworkInterface(Device device)
    {
        foreach (DeviceItem item in device.DeviceItems)
        {
            var networkInterface = FindNetworkInterface(item);
            if (networkInterface is not null)
            {
                return networkInterface;
            }
        }

        return null;
    }

    private static NetworkInterface? FindNetworkInterface(DeviceItem item)
    {
        try
        {
            var networkInterface = ((IEngineeringServiceProvider)item).GetService<NetworkInterface>();
            if (networkInterface is not null)
            {
                return networkInterface;
            }
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine($"Skipping network interface lookup for device item '{item.Name}': {ex.Message}");
        }

        foreach (DeviceItem child in item.DeviceItems)
        {
            var networkInterface = FindNetworkInterface(child);
            if (networkInterface is not null)
            {
                return networkInterface;
            }
        }

        return null;
    }

    private static Node? GetFirstNode(NetworkInterface networkInterface)
    {
        foreach (Node node in networkInterface.Nodes)
        {
            return node;
        }

        return null;
    }

    private static void ApplyNodeAttribute(
        Node node,
        string attributeName,
        string? value,
        ConfigureNetworkDeviceResultInfo result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        try
        {
            ((IEngineeringObject)node).SetAttribute(attributeName, value);
            result.AppliedSettings[attributeName] = value!;
        }
        catch (EngineeringException ex)
        {
            result.SkippedSettings[attributeName] = ex.Message;
        }
    }

    private static object? ConnectSubnet(
        Project project,
        Node node,
        string subnetName,
        ConfigureNetworkDeviceResultInfo result)
    {
        var subnet = FindSubnet(project, subnetName);
        if (subnet is null)
        {
            result.SkippedSettings["SubnetName"] = $"Subnet '{subnetName}' was not found.";
            return null;
        }

        try
        {
            // UNVERIFIED SDK CALL: V21 subnet connection API may be Node.ConnectToSubnet(Subnet) or equivalent.
            InvokeFirstAvailable(node, new[] { "ConnectToSubnet", "Connect" }, subnet);
            result.AppliedSettings["SubnetName"] = subnetName;
            return subnet;
        }
        catch (EngineeringException ex)
        {
            result.SkippedSettings["SubnetName"] = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            result.SkippedSettings["SubnetName"] = ex.Message;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is EngineeringException engineeringException)
        {
            result.SkippedSettings["SubnetName"] = engineeringException.Message;
        }

        return null;
    }

    private static Subnet? FindSubnet(Project project, string subnetName)
    {
        foreach (Subnet subnet in project.Subnets)
        {
            if (string.Equals(subnet.Name, subnetName, StringComparison.OrdinalIgnoreCase))
            {
                return subnet;
            }
        }

        return null;
    }

    private static void ConnectIoSystem(
        NetworkInterface networkInterface,
        object connectedSubnet,
        string ioSystemName,
        ConfigureNetworkDeviceResultInfo result)
    {
        var ioSystem = FindNamedItem(ReadEnumerableProperty(connectedSubnet, "IoSystems"), ioSystemName);
        if (ioSystem is null)
        {
            result.SkippedSettings["IoSystemName"] = $"IO system '{ioSystemName}' was not found on the connected subnet.";
            return;
        }

        var ioConnector = ReadEnumerableProperty(networkInterface, "IoConnectors").FirstOrDefault();
        if (ioConnector is null)
        {
            result.SkippedSettings["IoSystemName"] = "The network interface does not expose an IO connector.";
            return;
        }

        try
        {
            // UNVERIFIED SDK CALL: V21 IO connector attachment may be ConnectToIoSystem(IoSystem) or equivalent.
            InvokeFirstAvailable(ioConnector, new[] { "ConnectToIoSystem", "Connect" }, ioSystem);
            result.AppliedSettings["IoSystemName"] = ioSystemName;
        }
        catch (EngineeringException ex)
        {
            result.SkippedSettings["IoSystemName"] = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            result.SkippedSettings["IoSystemName"] = ex.Message;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is EngineeringException engineeringException)
        {
            result.SkippedSettings["IoSystemName"] = engineeringException.Message;
        }
    }

    private static object? FindNamedItem(IEnumerable<object> items, string name)
    {
        foreach (var item in items)
        {
            var itemName = ReadProperty(item, "Name")?.ToString();
            if (string.Equals(itemName, name, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

    private static void InvokeFirstAvailable(object target, IEnumerable<string> methodNames, object argument)
    {
        foreach (var methodName in methodNames)
        {
            var method = target.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(candidate =>
                {
                    if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    var parameters = candidate.GetParameters();
                    return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(argument);
                });

            if (method is not null)
            {
                method.Invoke(target, new[] { argument });
                return;
            }
        }

        throw new InvalidOperationException(
            $"No supported connection method was found on '{target.GetType().Name}'.");
    }

    private static object? ReadProperty(object? instance, string propertyName)
    {
        return OpennessReflection.ReadProperty(instance, propertyName);
    }

    private static IEnumerable<object> ReadEnumerableProperty(object instance, string propertyName)
    {
        return OpennessReflection.ReadEnumerableProperty(instance, propertyName);
    }
}
