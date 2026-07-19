using System.Security.Principal;
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
                    ErrorMessage: $"The local group \"{groupName}\" does not exist.");
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
            return new OpennessGroupMembership(
                GroupExists: false,
                IsMember: false,
                GroupSid: null,
                ErrorMessage: ex.Message);
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? TryResolveGroupSid(string groupName)
    {
        try
        {
            // Try as a local group first (machine\group), then as a bare name.
            var machine = Environment.MachineName;
            var ntAccounts = new[]
            {
                new NTAccount($@"{machine}\{groupName}"),
                new NTAccount(groupName)
            };

            foreach (var account in ntAccounts)
            {
                try
                {
                    var translated = account.Translate(typeof(SecurityIdentifier));
                    if (translated is SecurityIdentifier sid)
                    {
                        return sid.Value;
                    }
                }
                catch (IdentityNotMappedException)
                {
                    // Try the next form.
                }
                catch (SystemException)
                {
                    // Try the next form.
                }
            }

            return null;
        }
        catch (IdentityNotMappedException)
        {
            return null;
        }
        catch (SystemException)
        {
            return null;
        }
    }
}
