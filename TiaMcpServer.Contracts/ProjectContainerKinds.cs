namespace TiaMcpServer.Contracts;

/// <summary>Canonical project-container kinds and their closed, ordered vocabulary.</summary>
public static class ProjectContainerKinds
{
    public const string StandaloneProject = "standaloneProject";
    public const string LocalSession = "localSession";
    public const string ServerProject = "serverProject";

    public static readonly IReadOnlyCollection<string> All = Array.AsReadOnly(new[]
    {
        StandaloneProject,
        LocalSession,
        ServerProject,
    });
}
