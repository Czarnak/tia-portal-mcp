namespace TiaMcpServer.Contracts.Network;

public sealed class NetworkVerificationCheckInfo
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Expected { get; set; }
    public string? Observed { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Typed subject of mutation evidence. A device carries deviceName/deviceItemName, a node
/// deviceName/nodeId with its optional owner interfacePath/interfaceName, a subnet subnetId.
/// Members that do not apply are written as explicit nulls.
/// </summary>
public sealed class NetworkMutationIdentityInfo
{
    public string? DeviceName { get; set; }
    public string? DeviceItemName { get; set; }
    public string? NodeId { get; set; }
    public List<NetworkInterfacePathSegmentInfo>? InterfacePath { get; set; }
    public string? InterfaceName { get; set; }
    public string? SubnetId { get; set; }
}

public sealed class NetworkMutationVerificationInfo
{
    public string Status { get; set; } = string.Empty;
    public NetworkMutationIdentityInfo Identity { get; set; } = new NetworkMutationIdentityInfo();
    public List<NetworkVerificationCheckInfo> Checks { get; set; } = new List<NetworkVerificationCheckInfo>();
    public string? Message { get; set; }
}
