using System.Security.Principal;
using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;

namespace TiaMcpServer.Diagnostics;

public sealed class WindowsIdentityService : IWindowsIdentityService
{
    public static readonly WindowsIdentityService Instance = new();

    public bool IsWindows => OperatingSystem.IsWindows();

    public WindowsUserInfo? GetCurrentUserInfo()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User?.Value;
            var name = identity.Name;
            return new WindowsUserInfo(name, sid ?? string.Empty);
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public OpennessGroupMembership CheckGroupMembership(string groupName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new OpennessGroupMembership(
                GroupExists: false,
                IsMember: false,
                GroupSid: null,
                ErrorMessage: "Not running on Windows.");
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);

            // Resolve the local group to an NTAccount to confirm it exists and to test membership by SID.
            var groupSid = TryResolveGroupSid(groupName);
            if (groupSid is null)
            {
                return new OpennessGroupMembership(
                    GroupExists: false,
                    IsMember: false,
                    GroupSid: null,
                    ErrorMessage: null);
            }

            var sidIdentity = new SecurityIdentifier(groupSid);
            var isMember = principal.IsInRole(sidIdentity);

            return new OpennessGroupMembership(
                GroupExists: true,
                IsMember: isMember,
                GroupSid: groupSid,
                ErrorMessage: null);
        }
        catch (Exception ex)
        {
            // Membership could not be verified; this is not evidence that the group is missing.
            return new OpennessGroupMembership(
                GroupExists: false,
                IsMember: false,
                GroupSid: null,
                ErrorMessage: ex.Message);
        }
    }

    // Try as a local group first (machine\group), then as a bare name.
    [SupportedOSPlatform("windows")]
    private static string? TryResolveGroupSid(string groupName) =>
        FirstMappedSid(
            () => TranslateToSid($@"{Environment.MachineName}\{groupName}"),
            () => TranslateToSid(groupName));

    [SupportedOSPlatform("windows")]
    private static string? TranslateToSid(string accountName) =>
        (new NTAccount(accountName).Translate(typeof(SecurityIdentifier)) as SecurityIdentifier)?.Value;

    /// <summary>
    /// Returns the first SID a translation yields, or null when every form is unmapped
    /// (<see cref="IdentityNotMappedException"/>), meaning the group does not exist. When no form
    /// maps and one failed otherwise, the first such failure is rethrown: the group could not be
    /// checked, which is not the same as absent.
    /// </summary>
    internal static string? FirstMappedSid(params Func<string?>[] translations)
    {
        ExceptionDispatchInfo? failure = null;
        foreach (var translate in translations)
        {
            try
            {
                if (translate() is { } sid)
                {
                    return sid;
                }
            }
            catch (IdentityNotMappedException)
            {
                // Try the next form.
            }
            catch (Exception ex)
            {
                failure ??= ExceptionDispatchInfo.Capture(ex);
            }
        }

        failure?.Throw();
        return null;
    }
}
