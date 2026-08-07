using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public class ProjectOpenPolicyTests
{
    [Fact]
    public void NothingAttached_NoRequest_UsesAttached()
        => Assert.Equal(ProjectOpenDecision.UseAttached, ProjectOpenPolicy.Decide(null, null));

    [Fact]
    public void NothingAttached_WithRequest_OpensIt()
        => Assert.Equal(ProjectOpenDecision.OpenRequested, ProjectOpenPolicy.Decide(null, "C:\\a.ap21"));

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

    [Fact]
    public void RefusalMessage_NamesBothProjectsAndTheEscapeHatch()
    {
        var message = ProjectOpenPolicy.RefusalMessage("C:\\a.ap21", "C:\\b.ap21");

        Assert.Contains("C:\\a.ap21", message);
        Assert.Contains("C:\\b.ap21", message);
        Assert.Contains("open_project", message);
    }
}
