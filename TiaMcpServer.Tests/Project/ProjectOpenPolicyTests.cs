using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public class ProjectOpenPolicyTests
{
    [Theory]
    [InlineData(McpAccessMode.ReadOnly, false)]
    [InlineData(McpAccessMode.ReadWrite, true)]
    [InlineData(McpAccessMode.Full, true)]
    public void RefusalAndNotOpenMessages_NameBindProjectInEveryMode(McpAccessMode mode, bool canOpen)
    {
        var messages = new[]
        {
            ProjectOpenPolicy.RefusalMessage(@"C:\Projects\A.ap21", @"C:\Projects\B.ap21", mode),
            ProjectOpenPolicy.NotOpenMessage(@"C:\Projects\B.ap21", mode),
            ProjectOpenPolicy.NoProjectOpenMessage(mode)
        };

        foreach (var message in messages)
        {
            Assert.Contains("bind_project", message);
            Assert.Equal(canOpen, message.Contains("open_project", StringComparison.Ordinal));
            if (canOpen)
                Assert.True(message.IndexOf("bind_project", StringComparison.Ordinal)
                    < message.IndexOf("open_project", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void NothingAttached_NoRequest_UsesAttached()
        => Assert.Equal(ProjectOpenDecision.UseAttached, ProjectOpenPolicy.Decide(null, null));

    [Fact]
    public void Decide_NothingOpenWithPath_RequestedNotOpen()
        => Assert.Equal(ProjectOpenDecision.RequestedNotOpen, ProjectOpenPolicy.Decide(null, "C:\\a.ap21"));

    [Theory]
    [InlineData(McpAccessMode.ReadOnly, false)]
    [InlineData(McpAccessMode.ReadWrite, true)]
    [InlineData(McpAccessMode.Full, true)]
    public void NotOpenMessage_NamesOpenProjectOnlyWhenRegistered(McpAccessMode mode, bool registered)
    {
        var message = ProjectOpenPolicy.NotOpenMessage("C:\\a.ap21", mode);

        Assert.Contains("C:\\a.ap21", message);
        Assert.Equal(registered, message.Contains("open_project", StringComparison.Ordinal));
    }

    [Fact]
    public void Attached_NoRequest_UsesAttached()
        => Assert.Equal(ProjectOpenDecision.UseAttached, ProjectOpenPolicy.Decide("C:\\a.ap21", null));

    [Fact]
    public void Attached_SameRequest_UsesAttached()
        => Assert.Equal(ProjectOpenDecision.UseAttached, ProjectOpenPolicy.Decide("C:\\a.ap21", "C:\\a.ap21"));

    [Fact]
    public void Attached_SameRequestDifferentCase_UsesAttached()
        => Assert.Equal(ProjectOpenDecision.UseAttached, ProjectOpenPolicy.Decide("C:\\A.ap21", "c:\\a.AP21"));

    [Fact]
    public void Attached_DifferentRequest_Refuses()
        => Assert.Equal(ProjectOpenDecision.Refuse, ProjectOpenPolicy.Decide("C:\\a.ap21", "C:\\b.ap21"));

    [Fact]
    public void Attached_WhitespaceRequest_UsesAttached()
        => Assert.Equal(ProjectOpenDecision.UseAttached, ProjectOpenPolicy.Decide("C:\\a.ap21", "   "));

    [Theory]
    [InlineData(McpAccessMode.ReadOnly, false)]
    [InlineData(McpAccessMode.ReadWrite, true)]
    [InlineData(McpAccessMode.Full, true)]
    public void RefusalMessage_OffersOnlyAvailableSwitchGuidance(McpAccessMode mode, bool canOpen)
    {
        var message = ProjectOpenPolicy.RefusalMessage("C:\\a.ap21", "C:\\b.ap21", mode);

        Assert.Contains("C:\\a.ap21", message);
        Assert.Contains("C:\\b.ap21", message);
        Assert.Contains("This operation does not switch projects implicitly.", message);
        Assert.Equal(canOpen, message.Contains("open_project", StringComparison.Ordinal));
    }
}
