using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>Shared fail-closed capture; Siemens traversal is supplied by the ordinary reader.</summary>
internal static class NetworkConnectionEvidenceCapture
{
    /// <summary>Captures actual ancestors, then proves the qualified path resolves back to the same node.</summary>
    public static NetworkNodeIdentityInfo CaptureOwner<T>(T node, string nodeId,
        Func<T, T?> parent, Func<T, NetworkInterfacePathSegmentInfo?> itemSegment,
        Func<T, string?> deviceName, Func<NetworkNodeIdentityInfo, T?> resolveBack) where T : class
    {
        if (string.IsNullOrWhiteSpace(nodeId)) throw new InvalidOperationException("Connected node identity was unreadable.");
        var path = new List<NetworkInterfacePathSegmentInfo>();
        var visited = new List<T>();
        var current = parent(node);
        while (current is not null)
        {
            if (visited.Any(value => ReferenceEquals(value, current))) throw new InvalidOperationException("Owner hierarchy contains a cycle.");
            visited.Add(current);
            if (itemSegment(current) is { } segment) path.Add(segment);
            if (deviceName(current) is { } owner)
            {
                path.Reverse();
                _ = NetworkInterfacePathEncoding.Encode(path);
                if (string.IsNullOrWhiteSpace(owner)) throw new InvalidOperationException("Connected device name was unreadable.");
                var identity = new NetworkNodeIdentityInfo { DeviceName = owner, NodeId = nodeId, InterfacePath = path };
                if (!object.Equals(resolveBack(identity), node)) throw new InvalidOperationException("Owner hierarchy did not resolve back to the actual node.");
                return identity;
            }
            current = parent(current);
        }
        throw new InvalidOperationException("Connected node owner hierarchy was unavailable.");
    }

    /// <summary>Siemens Openness may return different CLR wrappers for the same TIA object, so live identity uses Equals.</summary>
    public static bool ContainsSameObject<T>(IEnumerable<T> values, T target) where T : class
        => values.Any(value => object.Equals(value, target));

    public static NetworkSubnetConnectionsInfo CaptureSubnet<T>(
        Func<IEnumerable<T>> enumerateNodes,
        Func<T, NetworkNodeIdentityInfo> readIdentity,
        Action<T, List<string>>? readDisplay = null)
    {
        var result = new NetworkSubnetConnectionsInfo { Complete = true };
        try
        {
            foreach (var node in enumerateNodes())
            {
                try
                {
                    var identity = readIdentity(node);
                    if (identity is null || string.IsNullOrWhiteSpace(identity.DeviceName)
                        || string.IsNullOrWhiteSpace(identity.NodeId))
                        throw new InvalidOperationException("Connected node identity or owner was unavailable.");
                    result.Nodes.Add(identity);
                }
                catch (Exception exception)
                {
                    result.Complete = false;
                    result.Messages.Add($"Could not identify a connected node: {exception.Message}");
                }
                // Display reads share this producer-owned relationship scope, but cannot
                // discard an identity already captured successfully.
                try { readDisplay?.Invoke(node, result.Messages); }
                catch (Exception exception)
                {
                    result.Messages.Add($"Could not read connected-node display information: {exception.Message}");
                }
            }
        }
        catch (Exception exception)
        {
            result.Complete = false;
            result.Messages.Add($"Could not complete connected-node enumeration: {exception.Message}");
        }
        if (result.Nodes.Distinct(NetworkNodeIdentityComparer.Instance).Count() != result.Nodes.Count)
        {
            result.Complete = false;
            result.Messages.Add("Connected node inventory contains duplicate qualified identities.");
        }
        result.Complete &= result.Messages.Count == 0;
        result.Nodes = result.Nodes.OrderBy(node => node.DeviceName, StringComparer.Ordinal)
            .ThenBy(node => node.NodeId, StringComparer.Ordinal).ToList();
        return result;
    }

    public static NetworkNodeConnectionInfo CaptureNode(
        Func<string?> readSubnetId,
        Func<(string? SubnetId, int? Number)> readIoSystem,
        Action<List<string>>? readDisplay = null)
    {
        var result = new NetworkNodeConnectionInfo { Complete = true };
        try { result.SubnetId = readSubnetId(); }
        catch (Exception exception)
        {
            result.Complete = false;
            result.Messages.Add($"Could not read connected subnet identity: {exception.Message}");
        }
        try
        {
            var identity = readIoSystem();
            if ((identity.SubnetId is null) != (identity.Number is null)
                || identity.Number < 0 || identity.SubnetId is not null && string.IsNullOrWhiteSpace(identity.SubnetId))
                throw new InvalidOperationException("IO system identity was incomplete.");
            result.IoSystemSubnetId = identity.SubnetId;
            result.IoSystemNumber = identity.Number;
        }
        catch (Exception exception)
        {
            result.Complete = false;
            result.Messages.Add($"Could not read IO system identity: {exception.Message}");
        }
        if (result.SubnetId is not null && string.IsNullOrWhiteSpace(result.SubnetId))
        {
            result.Complete = false;
            result.Messages.Add("Connected subnet identity was blank.");
        }
        try { readDisplay?.Invoke(result.Messages); }
        catch (Exception exception)
        {
            result.Messages.Add($"Could not read relationship display information: {exception.Message}");
        }
        result.Complete &= result.Messages.Count == 0;
        return result;
    }
}
