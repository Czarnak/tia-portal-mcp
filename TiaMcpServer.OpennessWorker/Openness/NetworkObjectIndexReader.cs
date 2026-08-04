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
                var currentDeviceName = ReadString(device, "Name");
                if (deviceName is not null && !string.Equals(currentDeviceName, deviceName, StringComparison.Ordinal))
                {
                    continue;
                }

                ReadDeviceItems(device.DeviceItems, currentDeviceName, Array.Empty<DeviceItemPathSegmentInfo>(), requestedKinds, entries);
                if (deviceName is not null)
                {
                    break;
                }
            }
        }

        if (deviceName is null && (requestedKinds.Contains(NetworkObjectKinds.Subnet) || requestedKinds.Contains(NetworkObjectKinds.IoSystem)))
        {
            ReadSubnets(project, requestedKinds, entries);
        }

        // communicationConnection deliberately has no branch in this task. Task 7 adds it.
        return entries
            .OrderBy(entry => entry.Summary.Kind, StringComparer.Ordinal)
            .ThenBy(entry => entry.OrderingKey, StringComparer.Ordinal)
            .Select(entry => entry.Summary)
            .ToList();
    }

    private static void ReadDeviceItems(
        DeviceItemComposition items,
        string? deviceName,
        IReadOnlyList<DeviceItemPathSegmentInfo> parentPath,
        ISet<string> requestedKinds,
        List<Entry> entries)
    {
        var siblingIndex = 0;
        foreach (DeviceItem item in items)
        {
            try
            {
                var itemName = ReadString(item, "Name");
                var segment = new DeviceItemPathSegmentInfo
                {
                    Index = siblingIndex,
                    Name = itemName ?? string.Empty,
                    PositionNumber = ReadInt(item, "PositionNumber"),
                    TypeIdentifier = ReadString(item, "TypeIdentifier") ?? string.Empty,
                };
                var itemPath = parentPath.Concat(new[] { segment }).ToList();
                var itemKey = PathKey(deviceName, itemPath);

                if (requestedKinds.Contains(NetworkObjectKinds.DeviceItem))
                {
                    entries.Add(new Entry(
                        new NetworkObjectSummaryInfo
                        {
                            Kind = NetworkObjectKinds.DeviceItem,
                            DisplayName = itemName,
                            Selector = string.IsNullOrWhiteSpace(deviceName)
                                ? null
                                : NetworkSelectorFactory.DeviceItem(deviceName!, itemPath),
                        },
                        itemKey));
                }

                ReadInterfaceAndNodes(item, deviceName, itemPath, itemKey, requestedKinds, entries);
                ReadDeviceItems(item.DeviceItems, deviceName, itemPath, requestedKinds, entries);
            }
            catch (EngineeringException)
            {
                // A failed item must not make unrelated requested items undiscoverable.
            }

            siblingIndex++;
        }
    }

    private static void ReadInterfaceAndNodes(
        DeviceItem item,
        string? deviceName,
        IReadOnlyList<DeviceItemPathSegmentInfo> itemPath,
        string itemKey,
        ISet<string> requestedKinds,
        List<Entry> entries)
    {
        if (!requestedKinds.Contains(NetworkObjectKinds.NetworkInterface) && !requestedKinds.Contains(NetworkObjectKinds.Node))
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

        var interfaceName = ReadString(networkInterface, "Name");
        if (requestedKinds.Contains(NetworkObjectKinds.NetworkInterface))
        {
            entries.Add(new Entry(
                new NetworkObjectSummaryInfo
                {
                    Kind = NetworkObjectKinds.NetworkInterface,
                    DisplayName = interfaceName,
                    Selector = string.IsNullOrWhiteSpace(deviceName)
                        ? null
                        : NetworkSelectorFactory.NetworkInterface(deviceName!, itemPath, interfaceName, null, null),
                },
                itemKey));
        }

        if (!requestedKinds.Contains(NetworkObjectKinds.Node))
        {
            return;
        }

        foreach (Node node in networkInterface.Nodes)
        {
            try
            {
                var nodeId = ReadString(node, "NodeId");
                entries.Add(new Entry(
                    new NetworkObjectSummaryInfo
                    {
                        Kind = NetworkObjectKinds.Node,
                        DisplayName = ReadString(node, "Name"),
                        Selector = string.IsNullOrWhiteSpace(deviceName) || string.IsNullOrWhiteSpace(nodeId)
                            ? null
                            : NetworkSelectorFactory.Node(deviceName!, nodeId!),
                    },
                    (deviceName ?? string.Empty) + "\u001f" + (nodeId ?? string.Empty)));
            }
            catch (EngineeringException)
            {
                // Continue indexing sibling nodes when one node is unreadable.
            }
        }
    }

    private static void ReadSubnets(Project project, ISet<string> requestedKinds, List<Entry> entries)
    {
        foreach (Subnet subnet in project.Subnets)
        {
            try
            {
                var subnetId = ReadString(subnet, "SubnetId");
                var subnetName = ReadString(subnet, "Name");
                if (requestedKinds.Contains(NetworkObjectKinds.Subnet))
                {
                    entries.Add(new Entry(
                        new NetworkObjectSummaryInfo
                        {
                            Kind = NetworkObjectKinds.Subnet,
                            DisplayName = subnetName,
                            Selector = string.IsNullOrWhiteSpace(subnetId) ? null : NetworkSelectorFactory.Subnet(subnetId!),
                        },
                        subnetId ?? string.Empty));
                }

                if (requestedKinds.Contains(NetworkObjectKinds.IoSystem))
                {
                    foreach (var ioSystem in ReadEnumerableProperty(subnet, "IoSystems"))
                    {
                        var name = ReadString(ioSystem, "Name");
                        var number = ReadInt(ioSystem, "Number");
                        entries.Add(new Entry(
                            new NetworkObjectSummaryInfo
                            {
                                Kind = NetworkObjectKinds.IoSystem,
                                DisplayName = name,
                                Selector = string.IsNullOrWhiteSpace(subnetId) || number is null
                                    ? null
                                    : NetworkSelectorFactory.IoSystem(subnetId!, number.Value),
                            },
                            (subnetId ?? string.Empty) + "\u001f" + (number?.ToString("D10", CultureInfo.InvariantCulture) ?? string.Empty)));
                    }
                }
            }
            catch (EngineeringException)
            {
                // Continue indexing sibling subnets when one subnet is unreadable.
            }
        }
    }

    private static IEnumerable<object> ReadEnumerableProperty(object value, string propertyName)
        => OpennessReflection.ReadEnumerableProperty(value, propertyName, $"network object {propertyName}");

    private static string? ReadString(object value, string propertyName)
        => OpennessReflection.ReadPropertyOrAttribute(value, propertyName);

    private static int? ReadInt(object value, string propertyName)
    {
        var propertyValue = OpennessReflection.ReadProperty(value, propertyName, $"network object {propertyName}");
        return propertyValue is null ? null : Convert.ToInt32(propertyValue, CultureInfo.InvariantCulture);
    }

    private static string PathKey(string? deviceName, IEnumerable<DeviceItemPathSegmentInfo> path)
        => (deviceName ?? string.Empty) + "\u001f" + string.Join(".", path.Select(segment => segment.Index.ToString("D10", CultureInfo.InvariantCulture)));

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
