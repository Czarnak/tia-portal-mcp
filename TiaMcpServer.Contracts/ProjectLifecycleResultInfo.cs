namespace TiaMcpServer.Contracts;

public class ProjectLifecycleResultInfo
{
    public bool Success { get; set; } = true;

    public string Operation { get; set; } = string.Empty;

    public string? ProjectPath { get; set; }

    public ProjectStatusInfo? Project { get; set; }
}
