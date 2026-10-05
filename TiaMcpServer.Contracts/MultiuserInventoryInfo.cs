namespace TiaMcpServer.Contracts;

/// <summary>Configured endpoints; enumeration does not prove connectivity or authentication.</summary>
public sealed class MultiuserServerConnectionsInfo
{
    public List<MultiuserServerConnectionInfo> Connections { get; set; } = new();
}

public sealed class MultiuserServerConnectionInfo
{
    public string ServerAlias { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
}

public sealed class MultiuserServerGroupsInfo
{
    public MultiuserRemoteIdentity RemoteIdentity { get; set; } = new();
    public ProjectServerConnectionObservation ConnectionObservation { get; set; } = new();
    public List<ProjectServerGroupIdentity> Groups { get; set; } = new();
}

public sealed class MultiuserServerProjectsInfo
{
    public MultiuserRemoteIdentity RemoteIdentity { get; set; } = new();
    public ProjectServerConnectionObservation ConnectionObservation { get; set; } = new();
    public List<MultiuserServerProjectInfo> Projects { get; set; } = new();
}

public sealed class MultiuserServerProjectInfo
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>Sessions on this machine for the current user; an empty list has no broader meaning.</summary>
public sealed class MultiuserLocalSessionsInfo
{
    public MultiuserRemoteIdentity RemoteIdentity { get; set; } = new();
    public ProjectServerConnectionObservation ConnectionObservation { get; set; } = new();
    public string Scope { get; set; } = "currentMachineCurrentUser";
    public List<MultiuserLocalSessionInfo> Sessions { get; set; } = new();
}

public sealed class MultiuserLocalSessionInfo
{
    public int SessionId { get; set; }
    public string ProjectPath { get; set; } = string.Empty;
}

/// <summary>Non-atomic lock observation; owner is absent when the project was observed unlocked.</summary>
public sealed class MultiuserLockStateInfo
{
    public MultiuserRemoteIdentity RemoteIdentity { get; set; } = new();
    public ProjectServerConnectionObservation ConnectionObservation { get; set; } = new();
    public bool IsLocked { get; set; }
    public string? Owner { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
}
