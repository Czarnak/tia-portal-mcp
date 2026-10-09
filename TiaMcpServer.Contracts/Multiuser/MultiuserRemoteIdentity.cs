using TiaMcpServer.Contracts.Project;

namespace TiaMcpServer.Contracts.Multiuser;

/// <summary>Passive remote-project and local-session identity, with explicit nulls for absent information.</summary>
public sealed class MultiuserRemoteIdentity
{
    public string ServerAlias { get; set; } = string.Empty;

    public string? Host { get; set; }

    public int? Port { get; set; }

    public string? Protocol { get; set; }

    public ProjectServerGroupIdentity? Group { get; set; }

    public string? ServerProjectName { get; set; }

    public int? LocalSessionId { get; set; }

    public string? LocalSessionPath { get; set; }
}
