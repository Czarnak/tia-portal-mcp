using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Network;

/// <summary>Certifies read selectors using required namespace identities independently of optional metadata.</summary>
internal static class NetworkNodeReadSelectorBuilder
{
    public static List<NodeInfo> ReadNodes<TNode>(Func<IEnumerable<TNode>> enumerate,
        Func<TNode, NodeInfo> materialize, HardwareDiscoveryEvidenceCapture capture)
    {
        var nodes = new List<NodeInfo>();
        capture.Traverse(enumerate, node => nodes.Add(materialize(node)),
            "nodeEnumeration", "nodeMaterialization");
        // NodeIndex constrains the Siemens source collection, never a presentation order.
        return nodes;
    }

    public static void ApplyPage(DeviceInfo device, bool deviceNamespaceVerified, bool traversalComplete)
        => Apply(device, deviceNamespaceVerified && traversalComplete);
    public static bool DeviceNameIsUnique(IReadOnlyList<string?> names, string? selectedName)
        => !string.IsNullOrWhiteSpace(selectedName) && names.All(name => !string.IsNullOrWhiteSpace(name))
            && names.Count(name => string.Equals(name, selectedName, StringComparison.OrdinalIgnoreCase)) == 1;
    /// <summary>Owner proof for a live Openness item reached by re-enumeration, which may yield a distinct but equal wrapper.</summary>
    public static string LiveOwnerDiagnostic<TItem>(NetworkInterfacePathMatch<TItem> owner, TItem item) where TItem : class
        => owner.Success && object.Equals(owner.Item, item) ? string.Empty : owner.Error ?? "Interface owner is not unique.";
    public static void ApplyInventory(HardwareConfigInfo inventory, bool deviceNamespaceVerified = false)
    {
        var complete = inventory.DiscoveryEvidence is { Complete: true }
            && (inventory.DiscoveryEvidence.Scope == "project" || deviceNamespaceVerified)
            && inventory.Devices.All(device => !string.IsNullOrWhiteSpace(device.Name));
        foreach (var device in inventory.Devices)
            Apply(device, complete && DeviceNameIsUnique(inventory.Devices.Select(candidate => candidate.Name).ToList(), device.Name));
    }
    public static void Apply(DeviceInfo device, bool traversalComplete)
    {
        Visit(device.Items, new List<NetworkInterfacePathSegmentInfo>());
        void Visit(IEnumerable<DeviceItemInfo> items, List<NetworkInterfacePathSegmentInfo> parent)
        {
            foreach (var item in items)
            {
                var path = parent.Concat(new[] { new NetworkInterfacePathSegmentInfo
                {
                    Name = item.Name ?? string.Empty,
                    PositionNumber = item.PositionNumber ?? -1,
                    TypeIdentifier = string.IsNullOrWhiteSpace(item.TypeIdentifier) ? null : item.TypeIdentifier,
                } }).ToList();
                var owner = NetworkInterfacePathMatcher.Match(device.Items, path, x => x.Items,
                    x => x.Name, x => x.PositionNumber, x => x.TypeIdentifier);
                foreach (var networkInterface in item.NetworkInterfaces)
                foreach (var node in networkInterface.Nodes)
                {
                    var match = MatchNode(networkInterface.Nodes, node.NodeId, null, x => x.NodeId);
                    var diagnostic = !traversalComplete ? "Node discovery is incomplete; uniqueness is unknown."
                        : string.IsNullOrWhiteSpace(device.Name) ? "Device name is unreadable."
                        : !owner.Success ? owner.Error : !ReferenceEquals(owner.Item, item) ? "Interface owner evidence does not match."
                        : item.NetworkInterfaces.Count != 1 ? "The selected owner exposes multiple network interfaces."
                        : !match.Success ? match.Error : null;
                    node.Selectable = diagnostic is null;
                    node.Selector = diagnostic is null ? NetworkSelectorFactory.QualifiedNode(device.Name!, node.NodeId,
                        path, string.IsNullOrWhiteSpace(networkInterface.Name) ? null : networkInterface.Name) : null;
                    node.SelectorDiagnostics = diagnostic is null ? new List<string>() : new List<string> { diagnostic };
                }
                Visit(item.Items, path);
            }
        }
    }
    public static NetworkInterfacePathMatch<TNode> MatchNode<TNode>(IEnumerable<TNode> nodes,
        string? nodeId, int? nodeIndex, Func<TNode, string?> identity) where TNode : class
    {
        try
        {
            var matches = new List<(TNode Node, int Index)>();
            var index = 0;
            foreach (var node in nodes)
            {
                var id = identity(node);
                if (string.IsNullOrWhiteSpace(id))
                    return NetworkInterfacePathMatch<TNode>.Fail(WorkerFailureCategories.TargetEvidenceMismatch, "Node namespace has unreadable identity evidence.");
                if (string.Equals(id, nodeId, StringComparison.Ordinal)) matches.Add((node, index));
                index++;
            }
            if (matches.Count == 0) return NetworkInterfacePathMatch<TNode>.Fail(WorkerFailureCategories.TargetNotFound, "No node matches the requested identity in the selected namespace.");
            if (matches.Count != 1) return NetworkInterfacePathMatch<TNode>.Fail(WorkerFailureCategories.TargetAmbiguous, "Multiple nodes match the requested identity in the selected namespace.");
            if (nodeIndex is not null && nodeIndex != matches[0].Index)
                return NetworkInterfacePathMatch<TNode>.Fail(WorkerFailureCategories.TargetEvidenceMismatch, "The uniquely selected node does not match the supplied node index.");
            return NetworkInterfacePathMatch<TNode>.Ok(matches[0].Node);
        }
        catch (Exception)
        {
            return NetworkInterfacePathMatch<TNode>.Fail(WorkerFailureCategories.TargetEvidenceMismatch, "Node namespace could not be read completely.");
        }
    }
}
