namespace TiaMcpServer.Contracts;

/// <summary>A Project Server group identity; the root group has IsRoot=true and Name=null.</summary>
public sealed class ProjectServerGroupIdentity
{
    public bool IsRoot { get; set; }

    public string? Name { get; set; }
}
