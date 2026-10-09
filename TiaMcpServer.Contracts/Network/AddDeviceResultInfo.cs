namespace TiaMcpServer.Contracts.Network;

public class AddDeviceResultInfo
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public NetworkMutationVerificationInfo? Verification { get; set; }

    public string DeviceName { get; set; } = string.Empty;

    public string RootItemName { get; set; } = string.Empty;

    public string TypeIdentifier { get; set; } = string.Empty;

    public List<string> Warnings { get; set; } = new List<string>();
}
