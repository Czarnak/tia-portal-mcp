using TiaMcpServer.Contracts.Project;

namespace TiaMcpServer.Contracts.Multiuser;

public sealed class MultiuserServerConnectionsRequest
{
    public int? PortalProcessId { get; set; }
}

public sealed class MultiuserServerGroupsRequest
{
    public int? PortalProcessId { get; set; }
    public string ServerAlias { get; set; } = string.Empty;
}

public sealed class MultiuserServerProjectsRequest
{
    public int? PortalProcessId { get; set; }
    public string ServerAlias { get; set; } = string.Empty;
    public ProjectServerGroupIdentity? Group { get; set; }
}

public sealed class MultiuserLocalSessionsRequest
{
    public int? PortalProcessId { get; set; }
    public string ServerAlias { get; set; } = string.Empty;
    public ProjectServerGroupIdentity? Group { get; set; }
    public string ServerProjectName { get; set; } = string.Empty;
}

public sealed class MultiuserLockStateRequest
{
    public int? PortalProcessId { get; set; }
    public string ServerAlias { get; set; } = string.Empty;
    public ProjectServerGroupIdentity? Group { get; set; }
    public string ServerProjectName { get; set; } = string.Empty;
}
