using System.Globalization;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.OpennessWorker.Openness.Project;

namespace TiaMcpServer.OpennessWorker.Openness.Network;

using Project = Siemens.Engineering.Project;

/// <summary>
/// Reads a compact, selector-bearing index of the requested network object kinds. This reader
/// deliberately does not read attribute values: the follow-up inspection operation owns that work.
/// </summary>
public static class NetworkObjectIndexReader
{
    public static IReadOnlyList<NetworkObjectSummaryInfo> Read(
        Project project,
        IReadOnlyList<string> objectKinds,
        string? deviceName)
    {
        var requestedKinds = new HashSet<string>(objectKinds, StringComparer.Ordinal);
        var entries = new List<Entry>();
        var allDeviceNames = new List<string?>();
        var wantsDeviceTree = requestedKinds.Contains(NetworkObjectKinds.DeviceItem)
            || requestedKinds.Contains(NetworkObjectKinds.NetworkInterface)
            || requestedKinds.Contains(NetworkObjectKinds.Node)
            || requestedKinds.Contains(NetworkObjectKinds.CommunicationConnection);

        if (wantsDeviceTree)
        {
            foreach (Device device in ProjectDeviceEnumerator.Enumerate(project))
            {
                var currentDeviceName = ReadTypedString(() => device.Name, "Device name");
                allDeviceNames.Add(currentDeviceName.IsUsable ? currentDeviceName.Value : null);
                if (deviceName is not null
                    && (!currentDeviceName.IsUsable
                        || !string.Equals(currentDeviceName.Value, deviceName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                ReadDeviceItems(
                    device.DeviceItems,
                    device.DeviceItems,
                    currentDeviceName,
                    Array.Empty<DeviceItemPathSegmentInfo>(),
                    Array.Empty<string>(),
                    requestedKinds,
                    entries);
            }
        }

        if (deviceName is null
            && (requestedKinds.Contains(NetworkObjectKinds.Subnet)
                || requestedKinds.Contains(NetworkObjectKinds.IoSystem)))
        {
            ReadSubnets(project, requestedKinds, entries);
        }

        foreach (var entry in entries.Where(entry => entry.Summary.Kind == NetworkObjectKinds.Node && entry.Summary.Selector is not null))
        {
            if (NetworkNodeReadSelectorBuilder.DeviceNameIsUnique(allDeviceNames, entry.Summary.Selector!.DeviceName)) continue;
            entry.Summary.Selectable = false;
            entry.Summary.Selector = null;
            entry.Summary.Diagnostics.Add("Device name namespace is unreadable or ambiguous; selector uniqueness is unknown.");
        }

        return entries
            .OrderBy(entry => entry.Summary.Kind, StringComparer.Ordinal)
            .ThenBy(entry => entry.OrderingKey, StringComparer.Ordinal)
            .Select(entry => entry.Summary)
            .ToList();
    }

    private static void ReadDeviceItems(
        DeviceItemComposition items,
        DeviceItemComposition roots,
        NetworkObjectDiscoveryEvidenceValue<string> deviceName,
        IReadOnlyList<DeviceItemPathSegmentInfo> parentPath,
        IReadOnlyList<string> parentPathDiagnostics,
        ISet<string> requestedKinds,
        List<Entry> entries)
    {
        var siblingIndex = 0;
        foreach (DeviceItem item in items)
        {
            var itemName = ReadTypedString(() => item.Name, "Device item name");
            var positionNumber = ReadTypedInt(() => item.PositionNumber, "Device item position number");
            var typeIdentifier = ReadTypedString(() => item.TypeIdentifier, "Device item type identifier");
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
            var itemKey = PathKey(deviceName.IsUsable ? deviceName.Value : null, itemPath);

            if (requestedKinds.Contains(NetworkObjectKinds.DeviceItem))
            {
                var diagnostics = CombineDiagnostics(pathDiagnostics, deviceName.Diagnostic);
                var selector = diagnostics.Count == 0
                    ? NetworkSelectorFactory.DeviceItem(deviceName.Value, itemPath)
                    : null;
                entries.Add(new Entry(
                    Summary(
                        NetworkObjectKinds.DeviceItem,
                        selector,
                        new NetworkObjectEvidenceInfo
                        {
                            Name = itemName.IsUsable ? itemName.Value : null,
                            TypeIdentifier = typeIdentifier.IsUsable ? typeIdentifier.Value : null,
                            PositionNumber = positionNumber.IsUsable ? positionNumber.Value : null,
                            DeviceItemPath = itemPath.Select(pathSegment => pathSegment.Name).ToList(),
                        },
                        diagnostics),
                    itemKey));
            }

            ReadInterfaceAndNodes(
                item,
                roots,
                deviceName,
                itemPath,
                pathDiagnostics,
                itemKey,
                requestedKinds,
                entries);

            ReadCommunicationConnections(
                item,
                deviceName,
                itemPath,
                pathDiagnostics,
                itemKey,
                requestedKinds,
                entries);

            try
            {
                ReadDeviceItems(
                    item.DeviceItems,
                    roots,
                    deviceName,
                    itemPath,
                    pathDiagnostics,
                    requestedKinds,
                    entries);
            }
            catch (EngineeringException)
            {
                // A failed child composition must not make sibling items undiscoverable.
            }

            siblingIndex++;
        }
    }

    private static void ReadInterfaceAndNodes(
        DeviceItem item,
        DeviceItemComposition roots,
        NetworkObjectDiscoveryEvidenceValue<string> deviceName,
        IReadOnlyList<DeviceItemPathSegmentInfo> itemPath,
        IReadOnlyList<string> itemPathDiagnostics,
        string itemKey,
        ISet<string> requestedKinds,
        List<Entry> entries)
    {
        if (!requestedKinds.Contains(NetworkObjectKinds.NetworkInterface)
            && !requestedKinds.Contains(NetworkObjectKinds.Node))
        {
            return;
        }

        NetworkInterface? networkInterface;
        try
        {
            networkInterface = ((IEngineeringServiceProvider)item).GetService<NetworkInterface>();
        }
        catch (EngineeringException)
        {
            return;
        }

        if (networkInterface is null)
        {
            return;
        }

        var interfaceName = ReadInterfaceName(networkInterface);
        if (requestedKinds.Contains(NetworkObjectKinds.NetworkInterface))
        {
            var diagnostics = CombineDiagnostics(
                itemPathDiagnostics,
                deviceName.Diagnostic);
            var selector = diagnostics.Count == 0
                ? NetworkSelectorFactory.NetworkInterface(
                    deviceName.Value,
                    itemPath,
                    interfaceName.IsUsable ? interfaceName.Value : null,
                    interfaceType: null,
                    interfaceOperatingMode: null)
                : null;
            entries.Add(new Entry(
                Summary(
                    NetworkObjectKinds.NetworkInterface,
                    selector,
                    new NetworkObjectEvidenceInfo
                    {
                        Name = interfaceName.IsUsable ? interfaceName.Value : null,
                        DeviceItemPath = itemPath.Select(pathSegment => pathSegment.Name).ToList(),
                        InterfaceName = interfaceName.IsUsable ? interfaceName.Value : null,
                    },
                    diagnostics),
                itemKey));
        }

        if (!requestedKinds.Contains(NetworkObjectKinds.Node))
        {
            return;
        }

        var ownerPath = itemPath.Select(x => new NetworkInterfacePathSegmentInfo
            { Name = x.Name, PositionNumber = x.PositionNumber,
                TypeIdentifier = string.IsNullOrWhiteSpace(x.TypeIdentifier) ? null : x.TypeIdentifier }).ToList();
        var owner = NetworkInterfacePathMatcher.Match(roots.Cast<DeviceItem>(), ownerPath,
            x => x.DeviceItems.Cast<DeviceItem>(), x => x.Name, x => x.PositionNumber, x => x.TypeIdentifier);
        var nodes = new List<Node>();
        var nodeEnumerationComplete = true;
        try { foreach (Node node in networkInterface.Nodes) nodes.Add(node); }
        catch (EngineeringException) { nodeEnumerationComplete = false; }
        var nodeIndex = 0;
        foreach (var node in nodes)
        {
            var nodeName = ReadTypedString(() => node.Name, "Node name");
            var nodeId = ReadTypedString(() => node.NodeId, "Node identity");
            var nodeMatch = NetworkNodeReadSelectorBuilder.MatchNode(nodes, nodeId.IsUsable ? nodeId.Value : null,
                null, x => x.NodeId);
            var diagnostics = CombineDiagnostics(
                Array.Empty<string>(),
                deviceName.Diagnostic,
                nodeId.Diagnostic,
                NetworkNodeReadSelectorBuilder.LiveOwnerDiagnostic(owner, item),
                nodeMatch.Success ? string.Empty : nodeMatch.Error!,
                nodeEnumerationComplete ? string.Empty : "Node discovery is incomplete; uniqueness is unknown.");
            var selector = diagnostics.Count == 0
                ? NetworkSelectorFactory.QualifiedNode(deviceName.Value, nodeId.Value, ownerPath,
                    interfaceName.IsUsable ? interfaceName.Value : null)
                : null;
            var orderingKey = nodeId.IsUsable
                ? (deviceName.IsUsable ? deviceName.Value : string.Empty) + "\u001f" + nodeId.Value
                : itemKey + "\u001f" + string.Format(CultureInfo.InvariantCulture, "{0:D10}", nodeIndex);
            entries.Add(new Entry(
                Summary(
                    NetworkObjectKinds.Node,
                    selector,
                    new NetworkObjectEvidenceInfo
                    {
                        Name = nodeName.IsUsable ? nodeName.Value : null,
                        DeviceItemPath = itemPath.Select(pathSegment => pathSegment.Name).ToList(),
                        NodeName = nodeName.IsUsable ? nodeName.Value : null,
                    },
                    diagnostics),
                orderingKey));
            nodeIndex++;
        }
    }

    private static void ReadSubnets(Project project, ISet<string> requestedKinds, List<Entry> entries)
    {
        var subnetIndex = 0;
        foreach (Subnet subnet in project.Subnets)
        {
            var subnetName = ReadTypedString(() => subnet.Name, "Subnet name");
            var subnetId = ReadSubnetId(subnet);
            var subnetOrderingKey = subnetId.IsUsable
                ? subnetId.Value
                : "\uffff" + string.Format(CultureInfo.InvariantCulture, "{0:D10}", subnetIndex);

            if (requestedKinds.Contains(NetworkObjectKinds.Subnet))
            {
                var diagnostics = CombineDiagnostics(
                    Array.Empty<string>(),
                    subnetId.Diagnostic);
                var selector = diagnostics.Count == 0
                    ? NetworkSelectorFactory.Subnet(subnetId.Value)
                    : null;
                entries.Add(new Entry(
                    Summary(
                        NetworkObjectKinds.Subnet,
                        selector,
                        new NetworkObjectEvidenceInfo
                        {
                            Name = subnetName.IsUsable ? subnetName.Value : null,
                            SubnetName = subnetName.IsUsable ? subnetName.Value : null,
                        },
                        diagnostics),
                    subnetOrderingKey));
            }

            if (requestedKinds.Contains(NetworkObjectKinds.IoSystem))
            {
                var ioSystemIndex = 0;
                foreach (IoSystem ioSystem in subnet.IoSystems)
                {
                    var ioSystemName = ReadTypedString(() => ioSystem.Name, "IO system name");
                    var number = ReadTypedInt(() => ioSystem.Number, "IO system number");
                    var diagnostics = CombineDiagnostics(
                        Array.Empty<string>(),
                        subnetId.Diagnostic,
                        number.Diagnostic);
                    var selector = diagnostics.Count == 0
                        ? NetworkSelectorFactory.IoSystem(
                            subnetId.Value,
                            number.Value,
                            ioSystemIndex,
                            ioSystemName.IsUsable ? ioSystemName.Value : null)
                        : null;
                    var orderingKey = subnetOrderingKey
                        + "\u001f"
                        + (number.IsUsable
                            ? string.Format(CultureInfo.InvariantCulture, "{0:D10}", number.Value)
                            : "\uffff" + string.Format(CultureInfo.InvariantCulture, "{0:D10}", ioSystemIndex));
                    entries.Add(new Entry(
                        Summary(
                            NetworkObjectKinds.IoSystem,
                            selector,
                            new NetworkObjectEvidenceInfo
                            {
                                Name = ioSystemName.IsUsable ? ioSystemName.Value : null,
                                SubnetName = subnetName.IsUsable ? subnetName.Value : null,
                                IoSystemName = ioSystemName.IsUsable ? ioSystemName.Value : null,
                            },
                            diagnostics),
                        orderingKey));
                    ioSystemIndex++;
                }
            }

            subnetIndex++;
        }
    }

    private static void ReadCommunicationConnections(
        DeviceItem item,
        NetworkObjectDiscoveryEvidenceValue<string> deviceName,
        IReadOnlyList<DeviceItemPathSegmentInfo> itemPath,
        IReadOnlyList<string> itemPathDiagnostics,
        string itemKey,
        ISet<string> requestedKinds,
        List<Entry> entries)
    {
        if (!requestedKinds.Contains(NetworkObjectKinds.CommunicationConnection))
        {
            return;
        }

        foreach (var result in CommunicationConnectionReader.Read(
            item,
            deviceName.IsUsable ? deviceName.Value : null,
            itemPath))
        {
            var connection = result.Summary;
            var diagnostics = CombineDiagnostics(
                itemPathDiagnostics.Concat(connection.SelectorDiagnostics),
                deviceName.Diagnostic);
            var selector = diagnostics.Count == 0 ? connection.Selector : null;
            var indexText = string.Format(
                CultureInfo.InvariantCulture,
                "{0:D10}",
                result.ConnectionIndex);
            entries.Add(new Entry(
                Summary(
                    NetworkObjectKinds.CommunicationConnection,
                    selector,
                    new NetworkObjectEvidenceInfo
                    {
                        Name = string.IsNullOrWhiteSpace(connection.LocalConnectionName)
                            ? null
                            : connection.LocalConnectionName,
                        TypeIdentifier = connection.ConnectionType,
                        DeviceItemPath = itemPath.Select(pathSegment => pathSegment.Name).ToList(),
                        ConnectionIsValid = connection.IsValid,
                        PartnerEndpointName = connection.PartnerName,
                    },
                    diagnostics),
                itemKey + "\u001f" + indexText));
        }
    }

    private static NetworkObjectDiscoveryEvidenceValue<string> ReadTypedString(
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

    private static NetworkObjectDiscoveryEvidenceValue<int> ReadTypedInt(
        Func<int> read,
        string field)
    {
        try
        {
            return NetworkObjectDiscoveryEvidence.ReadInt(read(), field);
        }
        catch (EngineeringException)
        {
            return NetworkObjectDiscoveryEvidence.UnreadableInt(field);
        }
    }

    private static NetworkObjectDiscoveryEvidenceValue<string> ReadInterfaceName(
        NetworkInterface networkInterface)
    {
        try
        {
            return NetworkObjectDiscoveryEvidence.ReadString(
                ((IEngineeringObject)networkInterface).GetAttribute("Name"),
                "Network interface name");
        }
        catch (EngineeringException)
        {
            return NetworkObjectDiscoveryEvidence.UnreadableString("Network interface name");
        }
    }

    private static NetworkObjectDiscoveryEvidenceValue<string> ReadSubnetId(Subnet subnet)
    {
        try
        {
            return NetworkObjectDiscoveryEvidence.ReadString(
                ((IEngineeringObject)subnet).GetAttribute("SubnetId"),
                "Subnet identity");
        }
        catch (EngineeringException)
        {
            return NetworkObjectDiscoveryEvidence.UnreadableString("Subnet identity");
        }
    }

    private static NetworkObjectSummaryInfo Summary(
        string kind,
        NetworkObjectSelectorInfo? selector,
        NetworkObjectEvidenceInfo evidence,
        IReadOnlyList<string> diagnostics)
        => new()
        {
            Kind = kind,
            Selectable = selector is not null,
            Selector = selector,
            Evidence = evidence,
            Diagnostics = diagnostics.ToList(),
        };

    private static List<string> CombineDiagnostics(
        IEnumerable<string> inherited,
        params string[] diagnostics)
        => inherited.Concat(diagnostics)
            .Where(diagnostic => !string.IsNullOrEmpty(diagnostic))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static string PathKey(
        string? deviceName,
        IEnumerable<DeviceItemPathSegmentInfo> path)
        => (deviceName ?? string.Empty)
            + "\u001f"
            + string.Join(
                ".",
                path.Select(segment => string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:D10}",
                    segment.Index)));

    private sealed class Entry
    {
        public Entry(NetworkObjectSummaryInfo summary, string orderingKey)
        {
            Summary = summary;
            OrderingKey = orderingKey;
        }

        public NetworkObjectSummaryInfo Summary { get; }
        public string OrderingKey { get; }
    }
}
