using System.Runtime.Versioning;
using System.Security.Principal;
using TiaMcpServer.Diagnostics;
using TiaMcpServer.Diagnostics.Checks;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

public class OpennessGroupCheckTests
{
    [Fact]
    public void NotWindows_ReturnsFailed()
    {
        var identity = new FakeWindowsIdentityService { IsWindows = false };
        var check = new OpennessGroupCheck(identity);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("Not running on Windows", result.Message);
    }

    [Fact]
    public void GroupDoesNotExist_ReturnsFailed()
    {
        var identity = new FakeWindowsIdentityService
        {
            IsWindows = true,
            Membership = new OpennessGroupMembership(GroupExists: false, IsMember: false, GroupSid: null, ErrorMessage: null)
        };
        var check = new OpennessGroupCheck(identity);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("does not exist", result.Message);
    }

    [Fact]
    public void MembershipCouldNotBeVerified_ReturnsWarningWithError()
    {
        var identity = new FakeWindowsIdentityService
        {
            IsWindows = true,
            Membership = new OpennessGroupMembership(GroupExists: false, IsMember: false, GroupSid: null, ErrorMessage: "Access is denied.")
        };
        var check = new OpennessGroupCheck(identity);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Equal("Group membership could not be verified: Access is denied.", result.Message);
        Assert.Equal("Access is denied.", result.Evidence!["error"]);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void FirstMappedSid_ReturnsNullOnlyWhenEveryFormIsUnmapped()
    {
        Assert.Null(WindowsIdentityService.FirstMappedSid(
            () => throw new IdentityNotMappedException(),
            () => throw new IdentityNotMappedException()));
        Assert.Equal("S-1-5-32-544", WindowsIdentityService.FirstMappedSid(
            () => throw new IdentityNotMappedException(),
            () => "S-1-5-32-544"));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void FirstMappedSid_PropagatesOtherTranslationFailures()
    {
        var error = Assert.Throws<SystemException>(() => WindowsIdentityService.FirstMappedSid(
            () => throw new IdentityNotMappedException(),
            () => throw new SystemException("RPC server unavailable.")));
        Assert.Equal("RPC server unavailable.", error.Message);
    }

    [Fact]
    public void UserNotMember_ReturnsFailed()
    {
        var identity = new FakeWindowsIdentityService
        {
            IsWindows = true,
            Membership = new OpennessGroupMembership(GroupExists: true, IsMember: false, GroupSid: "S-1-5-32-544", ErrorMessage: null)
        };
        var check = new OpennessGroupCheck(identity);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Failed, result.Status);
        Assert.Contains("not a member", result.Message);
    }

    [Fact]
    public void UserIsMember_ReturnsPassed()
    {
        var identity = new FakeWindowsIdentityService
        {
            IsWindows = true,
            UserInfo = new WindowsUserInfo("DOMAIN\\testuser", "S-1-5-21-999"),
            Membership = new OpennessGroupMembership(GroupExists: true, IsMember: true, GroupSid: "S-1-5-32-544", ErrorMessage: null)
        };
        var check = new OpennessGroupCheck(identity);

        var result = check.Run();

        Assert.Equal(DiagnosticStatus.Passed, result.Status);
        Assert.Contains("member", result.Message);
    }

    [Fact]
    public void IncludesUserEvidence()
    {
        var identity = new FakeWindowsIdentityService
        {
            IsWindows = true,
            UserInfo = new WindowsUserInfo("DOMAIN\\admin", "S-1-5-21-111"),
            Membership = new OpennessGroupMembership(GroupExists: true, IsMember: true, GroupSid: "S-1-5-32-544", ErrorMessage: null)
        };
        var check = new OpennessGroupCheck(identity);

        var result = check.Run();

        Assert.Equal("DOMAIN\\admin", result.Evidence!["user"]);
        Assert.Equal("S-1-5-21-111", result.Evidence!["userSid"]);
        Assert.Equal("true", result.Evidence!["isMember"]);
    }
}
