namespace TiaMcpServer.Contracts;

/// <summary>Structural node identity; optional type and service-name evidence are constraints.</summary>
public sealed class NetworkNodeIdentityComparer : IEqualityComparer<NetworkNodeIdentityInfo>
{
    public static NetworkNodeIdentityComparer Instance { get; } = new();
    private NetworkNodeIdentityComparer() { }
    public bool Equals(NetworkNodeIdentityInfo? x, NetworkNodeIdentityInfo? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null || !StringComparer.OrdinalIgnoreCase.Equals(x.DeviceName, y.DeviceName)
            || !StringComparer.Ordinal.Equals(x.NodeId, y.NodeId)) return false;
        if (x.InterfacePath is null || y.InterfacePath is null) return x.InterfacePath is null && y.InterfacePath is null;
        return x.InterfacePath.Count == y.InterfacePath.Count && x.InterfacePath.Zip(y.InterfacePath,
            (a, b) => a.PositionNumber == b.PositionNumber && StringComparer.Ordinal.Equals(a.Name, b.Name)).All(equal => equal);
    }
    public int GetHashCode(NetworkNodeIdentityInfo obj)
    {
        unchecked
        {
            var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(obj.DeviceName) * 397 ^ StringComparer.Ordinal.GetHashCode(obj.NodeId);
            if (obj.InterfacePath is null) return hash;
            foreach (var segment in obj.InterfacePath) hash = (hash * 397 ^ StringComparer.Ordinal.GetHashCode(segment.Name)) * 397 ^ segment.PositionNumber;
            return hash;
        }
    }
}
