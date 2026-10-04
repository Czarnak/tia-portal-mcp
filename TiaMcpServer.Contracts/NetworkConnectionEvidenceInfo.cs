using System.Text.Json.Serialization;

namespace TiaMcpServer.Contracts;

public class NetworkNodeIdentityInfo
{
    /// <summary>Owner path on new ordinary relationship reads; omission leaves ownership unknown.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<NetworkInterfacePathSegmentInfo>? InterfacePath { get; set; }
    /// <summary>Optional exact service-name consistency constraint when an owner path is present.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InterfaceName { get; set; }

    public string DeviceName { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;
}

/// <summary>Complete with null identities means reliably disconnected or not applicable.</summary>
public class NetworkNodeConnectionInfo
{
    public bool Complete { get; set; }
    public string? SubnetId { get; set; }
    public string? IoSystemSubnetId { get; set; }
    public int? IoSystemNumber { get; set; }
    public List<string> Messages { get; set; } = new();
}

/// <summary>An incomplete inventory remains unknown, even if no nodes were collected.</summary>
public class NetworkSubnetConnectionsInfo
{
    public bool Complete { get; set; }
    public List<NetworkNodeIdentityInfo> Nodes { get; set; } = new();
    public List<string> Messages { get; set; } = new();
}
