using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>Shared fail-closed capture; Siemens traversal is supplied by the ordinary reader.</summary>
internal static class NetworkConnectionEvidenceCapture
{
    public static NetworkSubnetConnectionsInfo CaptureSubnet<T>(
        Func<IEnumerable<T>> enumerateNodes,
        Func<T, NetworkNodeIdentityInfo> readIdentity)
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
            }
        }
        catch (Exception exception)
        {
            result.Complete = false;
            result.Messages.Add($"Could not complete connected-node enumeration: {exception.Message}");
        }
        result.Nodes = result.Nodes.OrderBy(node => node.DeviceName, StringComparer.Ordinal)
            .ThenBy(node => node.NodeId, StringComparer.Ordinal).ToList();
        return result;
    }

    public static NetworkNodeConnectionInfo CaptureNode(
        Func<string?> readSubnetId,
        Func<(string? SubnetId, int? Number)> readIoSystem)
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
        return result;
    }
}
