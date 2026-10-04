using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>Shared fail-closed capture; Siemens traversal is supplied by the ordinary reader.</summary>
internal static class NetworkConnectionEvidenceCapture
{
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
