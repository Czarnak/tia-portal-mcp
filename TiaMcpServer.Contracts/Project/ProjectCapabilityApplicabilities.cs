namespace TiaMcpServer.Contracts.Project;

/// <summary>Descriptive capability applicability vocabulary; these values do not grant authorization.</summary>
public static class ProjectCapabilityApplicabilities
{
    public const string ProjectContent = "projectContent";
    public const string StandaloneOnly = "standaloneOnly";
    public const string LocalSessionConditional = "localSessionConditional";
    public const string ServerProjectOnly = "serverProjectOnly";
    public const string NotYetDelivered = "notYetDelivered";
    public const string Unsupported = "unsupported";

    public static readonly IReadOnlyCollection<string> All = Array.AsReadOnly(new[]
    {
        ProjectContent,
        StandaloneOnly,
        LocalSessionConditional,
        ServerProjectOnly,
        NotYetDelivered,
        Unsupported,
    });
}
