using System.Globalization;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>Resolves the complete node selector and all requested subnet/IO dependencies before
/// the first scalar setter. Bare signatures forward to the same exact owner/node resolver.
/// Completed attempts retain sparse applied and skipped maps for recovery.</summary>
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
        => Configure(project, new NetworkObjectSelectorInfo { Kind = NetworkObjectKinds.Node, DeviceName = deviceName, NodeId = nodeId },
            ipAddress, subnetMask, pnDeviceName, subnetId, ioSystemSubnetId, ioSystemNumber);

    public static ConfigureNetworkDeviceResultInfo Configure(Project project, NetworkObjectSelectorInfo target,
        string? ipAddress, string? subnetMask, string? pnDeviceName, string? subnetId, string? ioSystemSubnetId, int? ioSystemNumber)
    {
        var selection = NetworkObjectSelectorResolver.ResolveNode(project, target);
        if (!selection.Success) throw new WorkerOperationException(selection.FailureCategory!, selection.Error!);
        var resolved = selection.Resolved!;
        var deviceName = target.DeviceName!;
        var result = new ConfigureNetworkDeviceResultInfo { DeviceName = deviceName };
        var networkInterface = resolved.OwningInterface!;
        var node = (Node)resolved.Value;

        // Prove dependency selection before the first setter. An invalid IO selector is denied
        // even if a later subnet connection might have failed and made that IO attach a skip.
        var subnetRequested = !string.IsNullOrWhiteSpace(subnetId);
        Subnet? connectedSubnet = null;
        object? ioSystem = null;
        IoConnector? ioConnector = null;
        try
        {
            if (subnetRequested) connectedSubnet = FindExactlyOneSubnet(project, subnetId!);
            if (ioSystemNumber.HasValue)
            {
                var ioSystemSubnet = connectedSubnet ?? FindExactlyOneSubnet(
                    project, RequireIoSystemSubnetId(ioSystemSubnetId, deviceName));
                ioSystem = FindExactlyOneIoSystem(ioSystemSubnet, ioSystemNumber.Value);
                // Preserve selector-before-connector ordering. A normal empty collection is a
                // supported skip; a failed collection read must not masquerade as that skip.
                ioConnector = networkInterface.IoConnectors.Cast<IoConnector>().FirstOrDefault();
            }
        }
        catch (EngineeringException)
        {
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                "Requested network dependency discovery was unreadable. No configuration was attempted.");
        }

        ApplyNodeAttribute(node, "Address", ipAddress, result);
        ApplyNodeAttribute(node, "SubnetMask", subnetMask, result);
        ApplyNodeAttribute(node, "PnDeviceName", pnDeviceName, result);

        var subnetConnected = false;
        if (subnetRequested)
        {
            subnetConnected = ConnectSubnet(node, subnetId!, connectedSubnet!, result);
        }

        if (ioSystemNumber.HasValue)
        {
            var skip = NetworkPostconditionChecks.IoSystemSkipReason(subnetRequested, subnetConnected, ioConnector is not null);
            if (skip is not null)
            {
                result.SkippedSettings["IoSystem"] = skip;
            }
            else
            {
                ConnectIoSystem(ioConnector!, ioSystem!, ioSystemNumber.Value, result);
            }
        }

        if (result.AppliedSettings.Count == 0 && result.SkippedSettings.Count == 0)
        {
            result.Messages.Add("No network settings were provided.");
        }

        // Return completed attempts even when every requested setting was skipped. The host
        // classifies skips as failures while retaining these sparse maps for recovery. Program
        // attaches immediate applied-setting verification before serializing this result.
        return result;
    }

    private static string RequireIoSystemSubnetId(string? ioSystemSubnetId, string deviceName)
    {
        if (string.IsNullOrWhiteSpace(ioSystemSubnetId))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.WorkerOperationFailed,
                $"Device '{deviceName}': an IO-system number was requested without an IO-system subnetId, "
                    + "and no subnet was connected in this same call.");
        }

        return ioSystemSubnetId!;
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
        var unreadable = false;
        foreach (Subnet candidate in project.Subnets)
        {
            var candidateId = OpennessReflection.ReadPropertyOrAttribute(candidate, "SubnetId");
            if (string.IsNullOrWhiteSpace(candidateId)) unreadable = true;
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

        var failure = NetworkPostconditionChecks.ClassifyDependencySelection(count, !unreadable);
        if (failure is null)
        {
            return match!;
        }

        throw new WorkerOperationException(
            failure,
            unreadable ? "Subnet identity discovery was unreadable. No configuration was attempted."
                : count > 1
                ? $"Multiple subnets report subnetId '{subnetId}'; subnetId must select exactly one subnet."
                : $"No subnet with subnetId '{subnetId}' was found.");
    }

    private static void ConnectIoSystem(
        IoConnector ioConnector,
        object ioSystem,
        int ioSystemNumber,
        ConfigureNetworkDeviceResultInfo result)
    {
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
        var unreadable = false;
        foreach (IoSystem candidate in subnet.IoSystems)
        {
            var candidateNumber = ReadIntPropertyOrAttribute(candidate, "Number");
            if (candidateNumber is null || candidateNumber < 0) unreadable = true;
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

        var failure = NetworkPostconditionChecks.ClassifyDependencySelection(count, !unreadable);
        if (failure is null)
        {
            return match!;
        }

        throw new WorkerOperationException(
            failure,
            unreadable ? "IO system identity discovery was unreadable. No configuration was attempted."
                : count > 1
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

}
