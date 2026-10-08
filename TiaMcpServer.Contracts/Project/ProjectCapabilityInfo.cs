namespace TiaMcpServer.Contracts;

/// <summary>Describes an operation's applicability; capability information is not authorization.</summary>
public sealed class ProjectCapabilityInfo
{
    public string Operation { get; set; } = string.Empty;

    public string Applicability { get; set; } = string.Empty;
}
