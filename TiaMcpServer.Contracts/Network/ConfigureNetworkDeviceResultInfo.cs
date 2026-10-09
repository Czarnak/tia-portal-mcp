namespace TiaMcpServer.Contracts.Network;

public class ConfigureNetworkDeviceResultInfo
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public NetworkMutationVerificationInfo? Verification { get; set; }

    public string DeviceName { get; set; } = string.Empty;

    public Dictionary<string, string> AppliedSettings { get; set; } = new Dictionary<string, string>();

    public Dictionary<string, string> SkippedSettings { get; set; } = new Dictionary<string, string>();

    public List<string> Messages { get; set; } = new List<string>();
}
