namespace TiaMcpServer.Contracts.Project;

/// <summary>Canonical sources of passive Project Server connection observations.</summary>
public static class ProjectServerConnectionObservationSources
{
    public const string SessionOpen = "sessionOpen";

    /// <summary>Adopting an already-open local session; distinct from opening a session.</summary>
    public const string SessionBind = "sessionBind";

    public const string ExplicitRead = "explicitRead";
    public const string OperationPreflight = "operationPreflight";
    public const string PostFailure = "postFailure";

    public static readonly IReadOnlyCollection<string> All = Array.AsReadOnly(new[]
    {
        SessionOpen,
        SessionBind,
        ExplicitRead,
        OperationPreflight,
        PostFailure,
    });
}
