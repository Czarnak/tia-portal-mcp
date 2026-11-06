using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using Xunit;

namespace TiaMcpServer.Tests.Library;

public sealed class LibraryWriteCatalogRegistrationTests
{
    [Fact]
    public void WriteMethods_AreProjectMutation_PlansAreObserve()
    {
        foreach (var method in LibraryWriteMethods.All)
        {
            var plan = method == LibraryWriteMethods.GlobalPlan || method == LibraryWriteMethods.ProjectPlan;
            Assert.Equal(plan ? OperationCapability.Observe : OperationCapability.ProjectMutation,
                OperationPolicyCatalog.GetCapability(method));
            Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadWrite, method));
            Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.Full, method));
            Assert.True(ProjectCapabilityCatalog.RequiresBoundProject(method));
            Assert.True(OperationPolicyCatalog.RequiresExpectedSessionIdentity(method));
            Assert.Equal(LibraryWriteMethods.OperationFor(method), ProjectCapabilityCatalog.OperationFor(method));
        }
    }

    [Fact]
    public void WriteMethods_DeniedInReadOnly()
    {
        foreach (var method in LibraryWriteMethods.GlobalLibraryWrites.Concat(LibraryWriteMethods.ProjectLibraryWrites))
            Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, method), method);
        Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, LibraryWriteMethods.GlobalPlan));
        Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, LibraryWriteMethods.ProjectPlan));
    }

    [Fact]
    public void GlobalWrites_StandaloneAndLocalSession()
    {
        foreach (var method in LibraryWriteMethods.GlobalLibraryWrites.Append(LibraryWriteMethods.GlobalPlan).Append("library_write"))
        {
            Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.StandaloneProject, method), method);
            Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, method), method);
            Assert.False(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.ServerProject, method), method);
        }
    }

    [Fact]
    public void ProjectWrites_StandaloneOnly()
    {
        foreach (var method in LibraryWriteMethods.ProjectLibraryWrites.Append(LibraryWriteMethods.ProjectPlan).Append("library_project_write"))
        {
            Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.StandaloneProject, method), method);
            Assert.False(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, method), method);
            Assert.False(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.ServerProject, method), method);
        }
        foreach (var kind in new[] { ProjectContainerKinds.StandaloneProject, ProjectContainerKinds.LocalSession, ProjectContainerKinds.ServerProject })
            Assert.Equal(ProjectCapabilityApplicabilities.StandaloneOnly,
                ProjectCapabilityCatalog.Describe(kind).Single(c => c.Operation == "library_project_write").Applicability);
    }

    [Fact]
    public void ServerProject_LibraryWrite_NotYetDelivered()
        => Assert.Equal(ProjectCapabilityApplicabilities.NotYetDelivered,
            ProjectCapabilityCatalog.Describe(ProjectContainerKinds.ServerProject).Single(c => c.Operation == "library_write").Applicability);
}
