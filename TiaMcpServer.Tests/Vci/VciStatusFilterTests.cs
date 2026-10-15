using TiaMcpServer.Contracts.Vci;
using Xunit;

namespace TiaMcpServer.Tests.Vci;

public class VciStatusFilterTests
{
    [Theory]
    [InlineData("Equal", "None", VciSyncDecision.SkipEqual, VciSyncDecision.SkipEqual)]
    [InlineData("Unequal", "ProjectObjectChanged", VciSyncDecision.Sync, VciSyncDecision.SkipTargetChanged)]
    [InlineData("Unequal", "WorkspaceFileChanged", VciSyncDecision.SkipTargetChanged, VciSyncDecision.Sync)]
    [InlineData("Unequal", "ProjectObjectChanged, WorkspaceFileChanged", VciSyncDecision.SkipConflict, VciSyncDecision.SkipConflict)]
    [InlineData("Unequal", "WorkspaceFileChanged,ProjectObjectChanged", VciSyncDecision.SkipConflict, VciSyncDecision.SkipConflict)]
    [InlineData("WorkspaceFileMissing", "None", VciSyncDecision.SkipTargetChanged, VciSyncDecision.FailMissing)]
    [InlineData("Unknown", "None", VciSyncDecision.SkipConflict, VciSyncDecision.SkipConflict)]
    [InlineData("Unequal", "None", VciSyncDecision.SkipConflict, VciSyncDecision.SkipConflict)]
    [InlineData("Unequal", "ProjectObjectChanged, Unknown", VciSyncDecision.SkipConflict, VciSyncDecision.SkipConflict)]
    public void EveryStatusRow_IsFilteredInBothDirections(string compareState, string details,
        VciSyncDecision toWorkspace, VciSyncDecision toProject)
    {
        Assert.Equal(toWorkspace, VciStatusFilter.Decide(VciSyncDirection.ToWorkspace, compareState, details));
        Assert.Equal(toProject, VciStatusFilter.Decide(VciSyncDirection.ToProject, compareState, details));
    }
}
