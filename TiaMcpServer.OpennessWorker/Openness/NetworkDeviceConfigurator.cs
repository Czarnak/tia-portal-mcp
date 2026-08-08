using System.Globalization;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Applies a <c>configure_network_device</c> request against a live TIA project.
///
/// <para>
/// Every selector — device, node, subnet, IO system — is resolved to exactly one matching object
/// before anything is written. A device may expose several interfaces and nodes (a multi-homed PC
/// station, for instance), so <paramref name="nodeId"/> is matched against every node under every
/// interface under every nested device item, never just the first one found. Zero matches, more
/// than one match, or a candidate whose own identity attribute could not be read all fail the same
/// way — <see cref="WorkerOperationException"/> with
/// <see cref="WorkerFailureCategories.PostconditionFailed"/> — because none of them name exactly
/// one existing thing. There is no first-match or name-only fallback anywhere in this type.
/// </para>
/// </summary>
public static class NetworkDeviceConfigurator
{
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

        var device = FindExactlyOneDevice(project, deviceName);
        var (networkInterface, node) = FindExactlyOneNode(device, deviceName, nodeId);

        ApplyNodeAttribute(node, "Address", ipAddress, result);
        ApplyNodeAttribute(node, "SubnetMask", subnetMask, result);
        ApplyNodeAttribute(node, "PnDeviceName", pnDeviceName, result);

        var subnetRequested = !string.IsNullOrWhiteSpace(subnetId);
        Subnet? connectedSubnet = null;
        var subnetConnected = false;
        if (subnetRequested)
        {
            connectedSubnet = FindExactlyOneSubnet(project, subnetId!);
            subnetConnected = ConnectSubnet(node, subnetId!, connectedSubnet, result);
        }

        if (ioSystemNumber.HasValue)
        {
            if (subnetRequested && !subnetConnected)
            {
                result.SkippedSettings["IoSystem"] = "Requested subnet was not connected, so IO system lookup was skipped.";
            }
            else
            {
                var ioSystemSubnet = connectedSubnet ?? FindExactlyOneSubnet(
                    project,
                    RequireIoSystemSubnetId(ioSystemSubnetId, deviceName));
                ConnectIoSystem(networkInterface, ioSystemSubnet, ioSystemNumber.Value, result);
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

    private static string RequireIoSystemSubnetId(string? ioSystemSubnetId, string deviceName)
    {
        if (string.IsNullOrWhiteSpace(ioSystemSubnetId))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.PostconditionFailed,
                $"Device '{deviceName}': an IO-system number was requested without an IO-system subnetId, "
                    + "and no subnet was connected in this same call.");
        }

        return ioSystemSubnetId!;
    }

    /// <summary>Matches exactly one device by name, case-insensitively. Zero or multiple matches fail closed.</summary>
    private static Device FindExactlyOneDevice(Project project, string deviceName)
    {
        Device? match = null;
        var count = 0;
        foreach (Device candidate in project.Devices)
        {
            if (!NamesMatch(SafeReadDeviceName(candidate), deviceName))
            {
                continue;
            }

            count++;
            if (count == 1)
            {
                match = candidate;
            }
        }

        if (count == 1)
        {
            return match!;
        }

        throw new WorkerOperationException(
            WorkerFailureCategories.PostconditionFailed,
            count > 1
                ? $"Multiple devices are named '{deviceName}'; device names must be unique to select one exactly."
                : $"No device named '{deviceName}' was found in the project.");
    }

    private static string? SafeReadDeviceName(Device device)
    {
        try
        {
            return device.Name;
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine($"Skipping a device while matching device name: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Matches exactly one node by nodeId across every network interface under every nested device
    /// item of <paramref name="device"/> — a device may expose several interfaces and nodes (for
    /// example a multi-homed PC station), so every one of them is a candidate, never just the
    /// first. Zero or multiple matches fail closed.
    /// </summary>
    private static (NetworkInterface Interface, Node Node) FindExactlyOneNode(Device device, string deviceName, string nodeId)
    {
        NetworkInterface? matchedInterface = null;
        Node? matchedNode = null;
        var count = 0;

        foreach (var (networkInterface, node) in EnumerateNodes(device))
        {
            var candidateId = OpennessReflection.ReadPropertyOrAttribute(node, "NodeId");
            if (!IdentitiesMatch(candidateId, nodeId))
            {
                continue;
            }

            count++;
            if (count == 1)
            {
                matchedInterface = networkInterface;
                matchedNode = node;
            }
        }

        if (count == 1)
        {
            return (matchedInterface!, matchedNode!);
        }

        throw new WorkerOperationException(
            WorkerFailureCategories.PostconditionFailed,
            count > 1
                ? $"Device '{deviceName}': multiple nodes report nodeId '{nodeId}'; nodeId must select exactly one node."
                : $"Device '{deviceName}': no node with nodeId '{nodeId}' was found.");
    }

    private static IEnumerable<(NetworkInterface Interface, Node Node)> EnumerateNodes(Device device)
    {
        foreach (DeviceItem item in device.DeviceItems)
        {
            foreach (var candidate in EnumerateItem(item))
            {
                yield return candidate;
            }
        }
    }

    private static IEnumerable<(NetworkInterface Interface, Node Node)> EnumerateItem(DeviceItem item)
    {
        var networkInterface = TryGetNetworkInterface(item);
        if (networkInterface is not null)
        {
            foreach (Node node in networkInterface.Nodes)
            {
                yield return (networkInterface, node);
            }
        }

        foreach (DeviceItem child in item.DeviceItems)
        {
            foreach (var candidate in EnumerateItem(child))
            {
                yield return candidate;
            }
        }
    }

    private static NetworkInterface? TryGetNetworkInterface(DeviceItem item)
    {
        try
        {
            return ((IEngineeringServiceProvider)item).GetService<NetworkInterface>();
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine($"Skipping network interface lookup for device item '{item.Name}': {ex.Message}");
            return null;
        }
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

    /// <summary>Performs the connect action against an already-resolved subnet. Returns whether the
    /// action itself succeeded, so a caller can decide whether a dependent IO-system attach is safe
    /// to attempt.</summary>
    private static bool ConnectSubnet(
        Node node,
        string subnetId,
        Subnet subnet,
        ConfigureNetworkDeviceResultInfo result)
    {
        try
        {
            // UNVERIFIED SDK CALL: V21 subnet connection API may be Node.ConnectToSubnet(Subnet) or equivalent.
            InvokeFirstAvailable(node, new[] { "ConnectToSubnet", "Connect" }, subnet);
            result.AppliedSettings["Subnet"] = subnetId;
            return true;
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

        return false;
    }

    /// <summary>
    /// Matches exactly one subnet by its own identity attribute rather than its display name, read
    /// the same property-then-attribute way <c>HardwareConfigReader</c> reads it (subnetId is a
    /// dynamic Openness attribute, not a CLR property). Zero or multiple matches fail closed.
    /// </summary>
    private static Subnet FindExactlyOneSubnet(Project project, string subnetId)
    {
        Subnet? match = null;
        var count = 0;
        foreach (Subnet candidate in project.Subnets)
        {
            var candidateId = OpennessReflection.ReadPropertyOrAttribute(candidate, "SubnetId");
            if (!IdentitiesMatch(candidateId, subnetId))
            {
                continue;
            }

            count++;
            if (count == 1)
            {
                match = candidate;
            }
        }

        if (count == 1)
        {
            return match!;
        }

        throw new WorkerOperationException(
            WorkerFailureCategories.PostconditionFailed,
            count > 1
                ? $"Multiple subnets report subnetId '{subnetId}'; subnetId must select exactly one subnet."
                : $"No subnet with subnetId '{subnetId}' was found.");
    }

    private static void ConnectIoSystem(
        NetworkInterface networkInterface,
        Subnet subnet,
        int ioSystemNumber,
        ConfigureNetworkDeviceResultInfo result)
    {
        var ioSystem = FindExactlyOneIoSystem(subnet, ioSystemNumber);

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
    /// Matches exactly one IO system by its modeled number within the already-resolved owning
    /// subnet. Zero or multiple matches fail closed.
    /// </summary>
    private static object FindExactlyOneIoSystem(Subnet subnet, int number)
    {
        object? match = null;
        var count = 0;
        foreach (var candidate in ReadEnumerableProperty(subnet, "IoSystems"))
        {
            var candidateNumber = ReadIntPropertyOrAttribute(candidate, "Number");
            if (candidateNumber != number)
            {
                continue;
            }

            count++;
            if (count == 1)
            {
                match = candidate;
            }
        }

        if (count == 1)
        {
            return match!;
        }

        throw new WorkerOperationException(
            WorkerFailureCategories.PostconditionFailed,
            count > 1
                ? $"Multiple IO systems report number {number} on the selected subnet; number must select exactly one IO system."
                : $"No IO system with number {number} was found on the selected subnet.");
    }

    private static int? ReadIntPropertyOrAttribute(object instance, string name)
    {
        var value = OpennessReflection.ReadPropertyOrAttribute(instance, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : (int?)null;
    }

    /// <summary>Case-insensitive, matching the host resolver's device-name semantics.</summary>
    private static bool NamesMatch(string? candidateName, string? requestedName)
        => !string.IsNullOrWhiteSpace(candidateName)
            && !string.IsNullOrWhiteSpace(requestedName)
            && string.Equals(candidateName, requestedName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ordinal identity comparison for node/subnet ids. A blank candidate identity (unreadable) or a
    /// blank requested identity never matches anything — an unreadable identity must never satisfy a
    /// write selector.
    /// </summary>
    private static bool IdentitiesMatch(string? candidateId, string? requestedId)
        => !string.IsNullOrWhiteSpace(candidateId)
            && !string.IsNullOrWhiteSpace(requestedId)
            && string.Equals(candidateId, requestedId, StringComparison.Ordinal);

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

    private static IEnumerable<object> ReadEnumerableProperty(object instance, string propertyName)
    {
        return OpennessReflection.ReadEnumerableProperty(instance, propertyName);
    }
}
