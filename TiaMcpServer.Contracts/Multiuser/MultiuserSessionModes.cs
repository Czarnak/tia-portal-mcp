namespace TiaMcpServer.Contracts.Multiuser;

/// <summary>Canonical session modes; unknown does not establish a healthy or usable session.</summary>
public static class MultiuserSessionModes
{
    public const string Multiuser = "multiuser";
    public const string Exclusive = "exclusive";
    public const string Unknown = "unknown";
    public const string NotApplicable = "notApplicable";

    public static readonly IReadOnlyCollection<string> All = Array.AsReadOnly(new[]
    {
        Multiuser,
        Exclusive,
        Unknown,
        NotApplicable,
    });
}
