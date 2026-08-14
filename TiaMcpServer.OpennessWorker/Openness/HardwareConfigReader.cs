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

        // The tag index is built once, before the device loop, so every channel of every device
        // item shares the exact same PLC tag snapshot (and the PLC selection message appears once).
        TagIndex? tagIndex = null;
        if (includeTagMatches)
        {
            tagIndex = ResolveTagIndex(project, plcName, result.Messages);
        }

        var selectedDevices = SelectDevices(project, deviceName, result.Messages);

        foreach (var (device, description) in selectedDevices)
        {
            try
            {
                result.Devices.Add(ReadDevice(device, description, result.Messages, includeIoDetails, tagIndex));
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

    /// <summary>
    /// Applies the optional device filter. Exactly one ordinal-ignore-case name match reads only
    /// that device; zero or multiple matches report a non-fatal message and no devices — there is
    /// never a first-match fallback.
    /// </summary>
    private static IReadOnlyList<(Device Device, string Description)> SelectDevices(
        Project project,
        string? deviceName,
        List<string> messages)
    {
        var candidates = new List<(Device Device, string Description)>();
        foreach (Device device in project.Devices)
        {
            try
            {
                candidates.Add((device, device.Name));
            }
            catch (EngineeringException exception)
            {
                messages.Add($"Skipped a device while reading hardware configuration: {exception.Message}");
            }
        }

        if (deviceName is null)
        {
            return candidates;
        }

        var matches = candidates
            .Where(candidate => string.Equals(candidate.Description, deviceName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 1)
        {
            return matches;
        }

        messages.Add(matches.Count == 0
            ? $"No device named '{deviceName}' was found; no devices are reported."
            : $"More than one device matches '{deviceName}'; no devices are reported because the device filter is ambiguous.");
        return Array.Empty<(Device, string)>();
    }

    private static DeviceInfo ReadDevice(
        Device device,
        string deviceDescription,
        List<string> messages,
        bool includeIoDetails,
        TagIndex? tagIndex)
    {
        var deviceName = ReadTypedIdentityString(() => device.Name, "Device name");
        AddReadMessage(messages, deviceName, "device name");
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
        TagIndex? tagIndex)
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
        TagIndex? tagIndex)
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
            itemInfo.IoDetails = ReadIoDetails(item, itemDescription, messages, tagIndex);
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

    /// <summary>
    /// Reads the structured I/O map for one device item: addresses (I/O type, start, length,
    /// dynamic context, controller names) and channels (number, I/O type, type, dynamic bit
    /// address and width), each read individually guarded. When a tag index is supplied, every
    /// channel whose controller association deterministically names the selected PLC gets exact
    /// tag matches; the association is never guessed and tags are never matched across controllers.
    /// </summary>
    private static DeviceItemIoDetailsInfo ReadIoDetails(
        DeviceItem item,
        string itemDescription,
        List<string> messages,
        TagIndex? tagIndex)
    {
        var details = new DeviceItemIoDetailsInfo();

        // Distinct controller device names for THIS item, from its addresses' controller
        // associations. Used to decide whether the item's channels belong to the selected PLC.
        var controllerDeviceNames = new HashSet<string>(StringComparer.Ordinal);
        var controllerAssociationReadable = true;

        foreach (Address address in item.Addresses)
        {
            try
            {
                var info = new IoAddressInfo
                {
                    IoType = ReadOptionalEnumName(
                        () => address.IoType,
                        $"device item '{itemDescription}' address I/O type",
                        messages),
                    StartAddress = ReadOptionalInt(
                        () => address.StartAddress,
                        $"device item '{itemDescription}' address start address",
                        messages),
                    Length = ReadOptionalInt(
                        () => address.Length,
                        $"device item '{itemDescription}' address length",
                        messages),
                    Context = ReadAddressContext(address, itemDescription, messages),
                };

                try
                {
                    foreach (var controller in address.AddressControllers)
                    {
                        var controllerName = ReadControllerOwningDeviceName(controller, itemDescription, messages);
                        if (controllerName is null)
                        {
                            controllerAssociationReadable = false;
                            continue;
                        }

                        info.ControllerNames.Add(controllerName);
                        controllerDeviceNames.Add(controllerName);
                    }
                }
                catch (EngineeringException exception)
                {
                    controllerAssociationReadable = false;
                    messages.Add(
                        $"Could not read address controllers while reading device item "
                            + $"'{itemDescription}': {exception.Message}");
                }

                info.ControllerNames = info.ControllerNames
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                details.Addresses.Add(info);
            }
            catch (EngineeringException exception)
            {
                messages.Add(
                    $"Skipped an address while reading device item '{itemDescription}': {exception.Message}");
            }
        }

        foreach (Channel channel in item.Channels)
        {
            try
            {
                var channelInfo = new IoChannelInfo
                {
                    Number = ReadOptionalInt(
                        () => channel.Number,
                        $"device item '{itemDescription}' channel number",
                        messages),
                    IoType = ReadOptionalEnumName(
                        () => channel.IoType,
                        $"device item '{itemDescription}' channel I/O type",
                        messages),
                    Type = ReadOptionalEnumName(
                        () => channel.Type,
                        $"device item '{itemDescription}' channel type",
                        messages),
                    ChannelAddressBits = ReadDynamicIntAttribute(
                        (IEngineeringObject)channel,
                        "ChannelAddress",
                        $"device item '{itemDescription}' channel address",
                        messages),
                    ChannelWidthBits = ReadDynamicUIntAttribute(
                        (IEngineeringObject)channel,
                        "ChannelWidth",
                        $"device item '{itemDescription}' channel width",
                        messages),
                };
                channelInfo.LogicalAddress = IoLogicalAddressFormatter.FormatLogicalAddress(
                    channelInfo.IoType,
                    channelInfo.ChannelAddressBits,
                    channelInfo.ChannelWidthBits);
                channelInfo.TagMatches = ReadTagMatches(channelInfo, controllerDeviceNames, controllerAssociationReadable, tagIndex, itemDescription, messages);
                details.Channels.Add(channelInfo);
            }
            catch (EngineeringException exception)
            {
                messages.Add(
                    $"Skipped a channel while reading device item '{itemDescription}': {exception.Message}");
            }
        }

        details.Addresses = details.Addresses
            .OrderBy(address => address.IoType, StringComparer.Ordinal)
            .ThenBy(address => address.StartAddress)
            .ThenBy(address => address.Length)
            .ToList();
        details.Channels = details.Channels
            .OrderBy(channel => channel.Number)
            .ThenBy(channel => channel.Type, StringComparer.Ordinal)
            .ToList();

        return details;
    }

    /// <summary>
    /// Matches every tag of the selected PLC against one channel. A match requires the controller
    /// association to name exactly the selected PLC (never ambiguous, never a different
    /// controller), the channel evidence to form a normalized interval, and a tag whose normalized
    /// absolute I/O interval is identical. No overlap and no first-match fallback.
    /// </summary>
    private static List<IoTagMatchInfo> ReadTagMatches(
        IoChannelInfo channelInfo,
        HashSet<string> controllerDeviceNames,
        bool controllerAssociationReadable,
        TagIndex? tagIndex,
        string itemDescription,
        List<string> messages)
    {
        var matches = new List<IoTagMatchInfo>();

        if (tagIndex is null)
        {
            return matches;
        }

        var channelArea = IoLogicalAddressFormatter.NormalizeArea(channelInfo.IoType);
        if (channelArea is null || channelInfo.ChannelAddressBits is null || channelInfo.ChannelWidthBits is null)
        {
            // No normalized interval to compare — the channel evidence stays raw, no matches.
            return matches;
        }

        if (!controllerAssociationReadable)
        {
            messages.Add(
                $"Controller association was unreadable for a channel of device item "
                    + $"'{itemDescription}'; no tag matches are reported for it.");
            return matches;
        }

        if (controllerDeviceNames.Count == 0)
        {
            messages.Add(
                $"No controller association was found for a channel of device item "
                    + $"'{itemDescription}'; no tag matches are reported for it.");
            return matches;
        }

        if (controllerDeviceNames.Count > 1
            || !string.Equals(
                controllerDeviceNames.First(),
                tagIndex.PlcDeviceName,
                StringComparison.Ordinal))
        {
            // Ambiguous, or owned by a different controller than the selected PLC: never match
            // across controllers. A different (single) controller is normal operation, not an error.
            if (controllerDeviceNames.Count > 1)
            {
                messages.Add(
                    $"A channel of device item '{itemDescription}' is owned by more than one "
                        + "controller; no tag matches are reported for it.");
            }

            return matches;
        }

        foreach (var candidate in tagIndex.Candidates)
        {
            if (!IoLogicalAddressFormatter.TryParse(candidate.LogicalAddress, out var tagAddress)
                || tagAddress is null
                || !IoTagMatcher.MatchesChannel(
                    tagAddress.Value,
                    channelInfo.IoType,
                    channelInfo.ChannelAddressBits,
                    channelInfo.ChannelWidthBits))
            {
                continue;
            }

            matches.Add(new IoTagMatchInfo
            {
                Name = candidate.Name,
                DataType = candidate.DataType,
                LogicalAddress = candidate.LogicalAddress,
                TableName = candidate.TableName,
                FolderPath = candidate.FolderPath,
            });
        }

        return matches
            .OrderBy(match => match.TableName, StringComparer.Ordinal)
            .ThenBy(match => match.FolderPath, StringComparer.Ordinal)
            .ThenBy(match => match.Name, StringComparer.Ordinal)
            .ToList();
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

    private static string? FindParentDeviceName(
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

    private static int? ReadOptionalInt(
        Func<int> read,
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

    /// <summary>
    /// Reads the dynamic Openness <c>Context</c> attribute of an address (an
    /// <c>AddressContext</c> enum value exposed as a dynamic attribute). Unreadable or absent
    /// context degrades to null with a message; a value is never fabricated.
    /// </summary>
    private static string? ReadAddressContext(
        Address address,
        string itemDescription,
        List<string> messages)
    {
        try
        {
            var value = ((IEngineeringObject)address).GetAttribute("Context");
            return value?.ToString();
        }
        catch (EngineeringException exception)
        {
            messages.Add(
                $"Could not read device item '{itemDescription}' address context: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// Reads a dynamic integer Openness attribute (for example <c>ChannelAddress</c>, which the
    /// SDK exposes as a dynamic attribute rather than a typed CLR property on every channel type).
    /// A null or non-integer value degrades to null with a message; a value is never coerced from
    /// a different CLR type.
    /// </summary>
    private static int? ReadDynamicIntAttribute(
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

            if (value is int intValue)
            {
                return intValue;
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

    /// <summary>
    /// Reads a dynamic unsigned Openness attribute (for example <c>ChannelWidth</c>). Like
    /// <see cref="ReadDynamicIntAttribute"/>, only the declared CLR type is accepted.
    /// </summary>
    private static uint? ReadDynamicUIntAttribute(
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

            if (value is uint uintValue)
            {
                return uintValue;
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

    /// <summary>
    /// Walks from one address controller up to its owning <see cref="Device"/> and returns that
    /// device's name — the controller association evidence used to decide whether a channel
    /// belongs to the selected PLC. Returns null (with a degradation message) when the owning
    /// device cannot be read.
    /// </summary>
    private static string? ReadControllerOwningDeviceName(
        AddressController controller,
        string itemDescription,
        List<string> messages)
    {
        try
        {
            return FindParentDeviceName(controller.OwnedBy, messages);
        }
        catch (EngineeringException exception)
        {
            messages.Add(
                $"Could not read an address controller while reading device item "
                    + $"'{itemDescription}': {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// Resolves the single PLC whose tag tables are matched, deterministically and without a
    /// first-match fallback: an exact (ordinal) <paramref name="plcName"/> match against the PLC
    /// software name or its owning device name, or — when omitted — the only PLC in the project.
    /// Zero or multiple targets report a non-fatal message and return null (no tag matches).
    /// </summary>
    private static TagIndex? ResolveTagIndex(
        Project project,
        string? plcName,
        List<string> messages)
    {
        var discovered = PlcSoftwareLocator.FindAll(project, plcName: null).ToList();

        PlcSoftwareLocator.DiscoveredPlcSoftware? selected;
        if (plcName is not null)
        {
            var exact = discovered
                .Where(software =>
                    string.Equals(software.Software.Name, plcName, StringComparison.Ordinal)
                    || string.Equals(software.DeviceName, plcName, StringComparison.Ordinal))
                .ToList();
            if (exact.Count == 1)
            {
                selected = exact[0];
            }
            else
            {
                messages.Add(exact.Count == 0
                    ? $"No PLC named '{plcName}' was found; no tag matches are reported."
                    : $"More than one PLC matches '{plcName}'; no tag matches are reported because the PLC selection is ambiguous.");
                return null;
            }
        }
        else if (discovered.Count == 1)
        {
            selected = discovered[0];
        }
        else
        {
            messages.Add(discovered.Count == 0
                ? "No PLC software was found in the project; no tag matches are reported."
                : "More than one PLC exists and no plcName was supplied; no tag matches are reported. Supply plcName to select one PLC.");
            return null;
        }

        var tables = TagTableReader.ReadAll(selected.Software);
        var candidates = tables
            .SelectMany(table => table.Tags.Select(tag => new IoTagCandidate(
                tag.Name,
                tag.DataType,
                tag.LogicalAddress,
                table.Name,
                table.FolderPath)))
            .ToList();

        return new TagIndex(selected.DeviceName, candidates);
    }

    /// <summary>Deterministically selected PLC tag index, shared by every channel of the read.</summary>
    private sealed class TagIndex
    {
        public TagIndex(string plcDeviceName, IReadOnlyList<IoTagCandidate> candidates)
        {
            PlcDeviceName = plcDeviceName;
            Candidates = candidates;
        }

        /// <summary>Owning device name of the selected PLC, compared against controller association evidence.</summary>
        public string PlcDeviceName { get; }

        public IReadOnlyList<IoTagCandidate> Candidates { get; }
    }

    /// <summary>One flattened PLC tag used for channel matching.</summary>
    private sealed class IoTagCandidate
    {
        public IoTagCandidate(string name, string dataType, string logicalAddress, string tableName, string folderPath)
        {
            Name = name;
            DataType = dataType;
            LogicalAddress = logicalAddress;
            TableName = tableName;
            FolderPath = folderPath;
        }

        public string Name { get; }

        public string DataType { get; }

        public string LogicalAddress { get; }

        public string TableName { get; }

        public string FolderPath { get; }
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
