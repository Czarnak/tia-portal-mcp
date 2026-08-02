using System.Globalization;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

public static class NetworkDeviceConfigurator
{
    /// <param name="nodeId">
    /// The exact node the caller selected. Exact node resolution — traversing every interface and
    /// matching this ID — is not implemented here yet; until then the interface's single node is
    /// used, which is only correct for a device that exposes one node.
    /// </param>
    public static ConfigureNetworkDeviceResultInfo Configure(
        Project project,
        string deviceName,
        string nodeId,
        string? ipAddress,
        string? subnetMask,
        string? pnDeviceName,
        string? subnetId,
        string? ioSystemSubnetId,
        int? ioSystemNumber)
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

        var subnetRequested = !string.IsNullOrWhiteSpace(subnetId);
        object? connectedSubnet = null;
        if (subnetRequested)
        {
            connectedSubnet = ConnectSubnet(project, node, subnetId!, result);
        }

        if (ioSystemNumber.HasValue)
        {
            if (connectedSubnet is null)
            {
                if (subnetRequested)
                {
                    result.SkippedSettings["IoSystem"] = "Requested subnet was not connected, so IO system lookup was skipped.";
                    return FinalizeResult(result, deviceName);
                }

                connectedSubnet = FindSubnet(project, ioSystemSubnetId);
            }

            if (connectedSubnet is null)
            {
                result.SkippedSettings["IoSystem"] = $"Subnet '{ioSystemSubnetId}' was not found, so its IO systems could not be searched.";
            }
            else
            {
                ConnectIoSystem(networkInterface, connectedSubnet, ioSystemNumber.Value, result);
            }
        }

        if (result.AppliedSettings.Count == 0 && result.SkippedSettings.Count == 0)
        {
            result.Messages.Add("No network settings were provided.");
        }

        return FinalizeResult(result, deviceName);
    }

    /// <summary>
    /// A result where every requested setting was skipped is a failed operation, not a success
    /// with fine print — throw so Program.Execute reports WorkerResponse.Success=false.
    /// </summary>
    private static ConfigureNetworkDeviceResultInfo FinalizeResult(
        ConfigureNetworkDeviceResultInfo result,
        string deviceName)
    {
        if (result.AppliedSettings.Count == 0 && result.SkippedSettings.Count > 0)
        {
            var reasons = string.Join(" ", result.SkippedSettings.Select(kv => $"{kv.Key}: {kv.Value}"));
            throw new InvalidOperationException(
                $"No requested settings could be applied to device '{deviceName}'. {reasons}");
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
        string subnetId,
        ConfigureNetworkDeviceResultInfo result)
    {
        var subnet = FindSubnet(project, subnetId);
        if (subnet is null)
        {
            result.SkippedSettings["Subnet"] = $"Subnet '{subnetId}' was not found.";
            return null;
        }

        try
        {
            // UNVERIFIED SDK CALL: V21 subnet connection API may be Node.ConnectToSubnet(Subnet) or equivalent.
            InvokeFirstAvailable(node, new[] { "ConnectToSubnet", "Connect" }, subnet);
            result.AppliedSettings["Subnet"] = subnetId;
            return subnet;
        }
        catch (EngineeringException ex)
        {
            result.SkippedSettings["Subnet"] = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            result.SkippedSettings["Subnet"] = ex.Message;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is EngineeringException engineeringException)
        {
            result.SkippedSettings["Subnet"] = engineeringException.Message;
        }

        return null;
    }

    /// <summary>
    /// Matches a subnet by its own identity rather than its display name. A subnet whose identity
    /// cannot be read matches nothing, so an unresolvable selector fails closed.
    /// </summary>
    private static Subnet? FindSubnet(Project project, string? subnetId)
    {
        if (string.IsNullOrWhiteSpace(subnetId))
        {
            return null;
        }

        foreach (Subnet subnet in project.Subnets)
        {
            var candidateId = ReadProperty(subnet, "SubnetId")?.ToString();
            if (!string.IsNullOrWhiteSpace(candidateId)
                && string.Equals(candidateId, subnetId, StringComparison.Ordinal))
            {
                return subnet;
            }
        }

        return null;
    }

    private static void ConnectIoSystem(
        NetworkInterface networkInterface,
        object connectedSubnet,
        int ioSystemNumber,
        ConfigureNetworkDeviceResultInfo result)
    {
        var ioSystem = FindNumberedItem(ReadEnumerableProperty(connectedSubnet, "IoSystems"), ioSystemNumber);
        if (ioSystem is null)
        {
            result.SkippedSettings["IoSystem"] = $"IO system number {ioSystemNumber} was not found on the connected subnet.";
            return;
        }

        var ioConnector = ReadEnumerableProperty(networkInterface, "IoConnectors").FirstOrDefault();
        if (ioConnector is null)
        {
            result.SkippedSettings["IoSystem"] = "The network interface does not expose an IO connector.";
            return;
        }

        try
        {
            // UNVERIFIED SDK CALL: V21 IO connector attachment may be ConnectToIoSystem(IoSystem) or equivalent.
            InvokeFirstAvailable(ioConnector, new[] { "ConnectToIoSystem", "Connect" }, ioSystem);
            result.AppliedSettings["IoSystem"] = ioSystemNumber.ToString(CultureInfo.InvariantCulture);
        }
        catch (EngineeringException ex)
        {
            result.SkippedSettings["IoSystem"] = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            result.SkippedSettings["IoSystem"] = ex.Message;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is EngineeringException engineeringException)
        {
            result.SkippedSettings["IoSystem"] = engineeringException.Message;
        }
    }

    /// <summary>
    /// Matches an IO system by its modeled number within its subnet. An unreadable or
    /// non-numeric number matches nothing, so an unresolvable selector fails closed.
    /// </summary>
    private static object? FindNumberedItem(IEnumerable<object> items, int number)
    {
        foreach (var item in items)
        {
            var candidate = ReadProperty(item, "Number");
            if (candidate is int candidateNumber && candidateNumber == number)
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
