namespace TiaMcpServer.Diagnostics;

public sealed record WindowsUserInfo(string DomainUser, string Sid);

/// <summary>
/// A non-null <c>ErrorMessage</c> means membership could not be verified; <c>GroupExists</c> and
/// <c>IsMember</c> are then meaningless. It is null for every verified outcome, including a group
/// that does not exist.
/// </summary>
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
