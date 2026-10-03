namespace TiaMcpServer.Contracts;

public sealed class SubnetLifecycleResultInfo
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public NetworkMutationVerificationInfo? Verification { get; set; }

    public string SubnetId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int NetworkDeviceCount { get; set; }
    public bool NetworkDeviceCountUnchanged { get; set; }
}
