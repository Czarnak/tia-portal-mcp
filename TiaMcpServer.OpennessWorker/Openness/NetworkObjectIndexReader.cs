using System.Globalization;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

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
        var wantsDeviceTree = requestedKinds.Contains(NetworkObjectKinds.DeviceItem)
            || requestedKinds.Contains(NetworkObjectKinds.NetworkInterface)
            || requestedKinds.Contains(NetworkObjectKinds.Node);

        if (wantsDeviceTree)
        {
            foreach (Device device in project.Devices)
            {
                var currentDeviceName = ReadTypedString(() => device.Name, "Device name");
                if (deviceName is not null
                    && (!currentDeviceName.IsUsable
                        || !string.Equals(currentDeviceName.Value, deviceName, StringComparison.Ordinal)))
                {
                    continue;
                }

                ReadDeviceItems(
                    device.DeviceItems,
                    currentDeviceName,
                    Array.Empty<DeviceItemPathSegmentInfo>(),
                    Array.Empty<string>(),
                    requestedKinds,
                    entries);
                if (deviceName is not null)
                {
                    break;
                }
            }
        }

        if (deviceName is null
            && (requestedKinds.Contains(NetworkObjectKinds.Subnet)
                || requestedKinds.Contains(NetworkObjectKinds.IoSystem)))
        {
            ReadSubnets(project, requestedKinds, entries);
        }

        // communicationConnection deliberately has no branch in this task. Task 7 adds it.
        return entries
            .OrderBy(entry => entry.Summary.Kind, StringComparer.Ordinal)
            .ThenBy(entry => entry.OrderingKey, StringComparer.Ordinal)
            .Select(entry => (NetworkObjectSummaryInfo)entry.Summary)
            .ToList();
    }

    private static void ReadDeviceItems(
        DeviceItemComposition items,
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
                PositionNumber = positionNumber.IsUsable ? positionNumber.Value : null,
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
                        itemName.IsUsable ? itemName.Value : null,
                        selector,
                        diagnostics,
                        EvidenceKey(
                            "deviceItem",
                            itemKey,
                            deviceName.SnapshotToken,
                            itemName.SnapshotToken,
                            positionNumber.SnapshotToken,
                            typeIdentifier.SnapshotToken)),
                    itemKey));
            }

            ReadInterfaceAndNodes(
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
                deviceName.Diagnostic,
                interfaceName.Diagnostic);
            var selector = diagnostics.Count == 0
                ? NetworkSelectorFactory.NetworkInterface(
                    deviceName.Value,
                    itemPath,
                    interfaceName.Value,
                    interfaceType: null,
                    interfaceOperatingMode: null)
                : null;
            entries.Add(new Entry(
                Summary(
                    NetworkObjectKinds.NetworkInterface,
                    interfaceName.IsUsable ? interfaceName.Value : null,
                    selector,
                    diagnostics,
                    EvidenceKey(
                        "networkInterface",
                        itemKey,
                        deviceName.SnapshotToken,
                        interfaceName.SnapshotToken)),
                itemKey));
        }

        if (!requestedKinds.Contains(NetworkObjectKinds.Node))
        {
            return;
        }

        var nodeIndex = 0;
        foreach (Node node in networkInterface.Nodes)
        {
            var nodeName = ReadTypedString(() => node.Name, "Node name");
            var nodeId = ReadTypedString(() => node.NodeId, "Node identity");
            var diagnostics = CombineDiagnostics(
                Array.Empty<string>(),
                deviceName.Diagnostic,
                nodeName.Diagnostic,
                nodeId.Diagnostic);
            var selector = diagnostics.Count == 0
                ? NetworkSelectorFactory.Node(deviceName.Value, nodeId.Value)
                : null;
            var orderingKey = nodeId.IsUsable
                ? (deviceName.IsUsable ? deviceName.Value : string.Empty) + "\u001f" + nodeId.Value
                : itemKey + "\u001f" + string.Format(CultureInfo.InvariantCulture, "{0:D10}", nodeIndex);
            entries.Add(new Entry(
                Summary(
                    NetworkObjectKinds.Node,
                    nodeName.IsUsable ? nodeName.Value : null,
                    selector,
                    diagnostics,
                    EvidenceKey(
                        "node",
                        itemKey,
                        string.Format(CultureInfo.InvariantCulture, "{0:D10}", nodeIndex),
                        deviceName.SnapshotToken,
                        nodeName.SnapshotToken,
                        nodeId.SnapshotToken)),
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
                    subnetName.Diagnostic,
                    subnetId.Diagnostic);
                var selector = diagnostics.Count == 0
                    ? NetworkSelectorFactory.Subnet(subnetId.Value)
                    : null;
                entries.Add(new Entry(
                    Summary(
                        NetworkObjectKinds.Subnet,
                        subnetName.IsUsable ? subnetName.Value : null,
                        selector,
                        diagnostics,
                        EvidenceKey(
                            "subnet",
                            string.Format(CultureInfo.InvariantCulture, "{0:D10}", subnetIndex),
                            subnetName.SnapshotToken,
                            subnetId.SnapshotToken)),
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
                        ioSystemName.Diagnostic,
                        number.Diagnostic);
                    var selector = diagnostics.Count == 0
                        ? NetworkSelectorFactory.IoSystem(subnetId.Value, number.Value)
                        : null;
                    var orderingKey = subnetOrderingKey
                        + "\u001f"
                        + (number.IsUsable
                            ? string.Format(CultureInfo.InvariantCulture, "{0:D10}", number.Value)
                            : "\uffff" + string.Format(CultureInfo.InvariantCulture, "{0:D10}", ioSystemIndex));
                    entries.Add(new Entry(
                        Summary(
                            NetworkObjectKinds.IoSystem,
                            ioSystemName.IsUsable ? ioSystemName.Value : null,
                            selector,
                            diagnostics,
                            EvidenceKey(
                                "ioSystem",
                                string.Format(CultureInfo.InvariantCulture, "{0:D10}", subnetIndex),
                                string.Format(CultureInfo.InvariantCulture, "{0:D10}", ioSystemIndex),
                                subnetId.SnapshotToken,
                                ioSystemName.SnapshotToken,
                                number.SnapshotToken)),
                        orderingKey));
                    ioSystemIndex++;
                }
            }

            subnetIndex++;
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

    private static NetworkObjectIndexedSummaryInfo Summary(
        string kind,
        string? displayName,
        NetworkObjectSelectorInfo? selector,
        IReadOnlyList<string> diagnostics,
        string snapshotEvidenceKey)
        => new()
        {
            Kind = kind,
            DisplayName = displayName,
            Selectable = selector is not null,
            Selector = selector,
            SelectorDiagnostics = diagnostics.ToList(),
            SnapshotEvidenceKey = snapshotEvidenceKey,
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

    private static string EvidenceKey(params string?[] values)
        => string.Concat(values.Select(value =>
        {
            var text = value ?? string.Empty;
            return text.Length + ":" + text + ";";
        }));

    private sealed class Entry
    {
        public Entry(NetworkObjectIndexedSummaryInfo summary, string orderingKey)
        {
            Summary = summary;
            OrderingKey = orderingKey;
        }

        public NetworkObjectIndexedSummaryInfo Summary { get; }
        public string OrderingKey { get; }
    }
}
