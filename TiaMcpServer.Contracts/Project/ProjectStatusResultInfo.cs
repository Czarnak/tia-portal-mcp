namespace TiaMcpServer.Contracts.Project;

/// <summary>Explicit-null wire root for the direct status read; lifecycle probes keep their legacy root.</summary>
public class ProjectStatusResultInfo
{
    public bool Success { get; set; } = true;
    public string Operation { get; set; } = string.Empty;
    public string? ProjectPath { get; set; }
    public ProjectStatusInfo? Project { get; set; }
}
