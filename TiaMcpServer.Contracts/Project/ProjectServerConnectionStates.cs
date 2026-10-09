namespace TiaMcpServer.Contracts.Project;

/// <summary>Canonical connection observations; unknown must never be treated as healthy.</summary>
public static class ProjectServerConnectionStates
{
    public const string Connected = "connected";
    public const string Unavailable = "unavailable";
    public const string Unknown = "unknown";

    public static readonly IReadOnlyCollection<string> All = Array.AsReadOnly(new[]
    {
        Connected,
        Unavailable,
        Unknown,
    });
}
