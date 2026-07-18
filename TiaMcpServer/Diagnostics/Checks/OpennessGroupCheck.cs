namespace TiaMcpServer.Diagnostics.Checks;

public sealed class OpennessGroupCheck : IDiagnosticCheck
{
    private const string OpennessGroupName = "Siemens TIA Openness";

    private readonly IWindowsIdentityService _identity;

    public OpennessGroupCheck(IWindowsIdentityService identity) => _identity = identity;

    public string Id => "openness-user-group";
    public string Name => "Siemens TIA Openness group";

    public DiagnosticCheckResult Run()
    {
        var evidence = Evidence.Empty();
        evidence["group"] = OpennessGroupName;

        var user = _identity.GetCurrentUserInfo();
        if (user is not null)
        {
            evidence["user"] = user.DomainUser;
            evidence["userSid"] = user.Sid;
        }
        else
        {
            evidence["user"] = null;
        }

        if (!_identity.IsWindows)
        {
            return new DiagnosticCheckResult(
                Id,
                Name,
                DiagnosticStatus.Failed,
                "Not running on Windows; group membership cannot be verified.",
                "Run on Windows and add the current user to the local \"Siemens TIA Openness\" group, then sign out and sign in again.",
                evidence);
        }

        var membership = _identity.CheckGroupMembership(OpennessGroupName);
        evidence["groupExists"] = membership.GroupExists.ToString().ToLowerInvariant();
        evidence["groupSid"] = membership.GroupSid;
        evidence["isMember"] = membership.IsMember.ToString().ToLowerInvariant();

        if (!membership.GroupExists)
        {
            return new DiagnosticCheckResult(
                Id,
                Name,
                DiagnosticStatus.Failed,
                $"The local group \"{OpennessGroupName}\" does not exist.",
                "Install TIA Portal V21 with the Openness option to create the group, then add the current user to it and sign out and sign in again.",
                evidence);
        }

        if (!membership.IsMember)
        {
            return new DiagnosticCheckResult(
                Id,
                Name,
                DiagnosticStatus.Failed,
                $"Current user is not a member of \"{OpennessGroupName}\".",
                $"Add the current user to the local \"{OpennessGroupName}\" group, then sign out and sign in again.",
                evidence);
        }

        return new DiagnosticCheckResult(
            Id,
            Name,
            DiagnosticStatus.Passed,
            $"Current user is a member of \"{OpennessGroupName}\".",
            null,
            evidence);
    }
}
