namespace TiaMcpServer.Diagnostics;

public sealed record WindowsUserInfo(string DomainUser, string Sid);

public sealed record OpennessGroupMembership(
    bool GroupExists,
    bool IsMember,
    string? GroupSid,
    string? ErrorMessage);

public interface IWindowsIdentityService
{
    bool IsWindows { get; }

    WindowsUserInfo? GetCurrentUserInfo();

    OpennessGroupMembership CheckGroupMembership(string groupName);
}
