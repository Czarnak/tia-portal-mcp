namespace TiaMcpServer.Contracts.CrossReferences;

/// <summary>
/// Member kinds addressable under a <c>TagTable</c> path. Each is a verified cross-reference owner
/// (spec Appendix B); unverified kinds are deliberately absent.
/// </summary>
public static class CrossReferenceMemberKinds
{
    public const string Tag = "Tag";
    public const string SystemConstant = "SystemConstant";
    public const string UserConstant = "UserConstant";

    public static readonly IReadOnlyList<string> All = new[] { Tag, SystemConstant, UserConstant };
}
