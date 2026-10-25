using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Project;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

public sealed class ProjectOpenGateTests
{
    private const string CurrentPath = @"C:\Fixture\A.ap21";
    private const string OtherPath = @"C:\Fixture\B.ap21";

    [Theory]
    [InlineData(McpAccessMode.ReadOnly)]
    [InlineData(McpAccessMode.ReadWrite)]
    [InlineData(McpAccessMode.Full)]
    public void Gate_RequestedNotOpen_IsIndependentOfNoProjectAllowance(McpAccessMode mode)
    {
        var exception = Assert.Throws<WorkerOperationException>(() =>
            ProjectOpenGate.Require(null, CurrentPath, mode, false, true));
        Assert.Equal(WorkerFailureCategories.AccessDenied, exception.FailureCategory);
        Assert.Equal(ProjectOpenPolicy.NotOpenMessage(CurrentPath, mode), exception.Message);
        AssertGuidance(mode, exception.Message);
    }

    [Theory]
    [InlineData(McpAccessMode.ReadOnly)]
    [InlineData(McpAccessMode.ReadWrite)]
    [InlineData(McpAccessMode.Full)]
    public void Gate_NoProject_IsIndependentOfRequestedNotOpenAllowance(McpAccessMode mode)
    {
        var exception = Assert.Throws<WorkerOperationException>(() =>
            ProjectOpenGate.Require(null, null, mode, true, false));
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, exception.FailureCategory);
        Assert.Equal(ProjectOpenPolicy.NoProjectOpenMessage(mode), exception.Message);
        AssertGuidance(mode, exception.Message);
    }

    public static IEnumerable<object[]> Allowances()
    {
        foreach (var mode in new[] { McpAccessMode.ReadOnly, McpAccessMode.ReadWrite, McpAccessMode.Full })
        foreach (var allowRequestedNotOpen in new[] { false, true })
        foreach (var allowNoProject in new[] { false, true })
            yield return new object[] { mode, allowRequestedNotOpen, allowNoProject };
    }

    [Theory]
    [MemberData(nameof(Allowances))]
    public void Gate_NoAttachedProject_EnforcesEachAllowance(McpAccessMode mode,
        bool allowRequestedNotOpen, bool allowNoProject)
    {
        if (allowRequestedNotOpen)
            ProjectOpenGate.Require(null, CurrentPath, mode, allowRequestedNotOpen, allowNoProject);
        else
        {
            var exception = Assert.Throws<WorkerOperationException>(() =>
                ProjectOpenGate.Require(null, CurrentPath, mode, allowRequestedNotOpen, allowNoProject));
            Assert.Equal(WorkerFailureCategories.AccessDenied, exception.FailureCategory);
            Assert.Equal(ProjectOpenPolicy.NotOpenMessage(CurrentPath, mode), exception.Message);
            AssertGuidance(mode, exception.Message);
        }

        if (allowNoProject)
            ProjectOpenGate.Require(null, null, mode, allowRequestedNotOpen, allowNoProject);
        else
        {
            var exception = Assert.Throws<WorkerOperationException>(() =>
                ProjectOpenGate.Require(null, null, mode, allowRequestedNotOpen, allowNoProject));
            Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, exception.FailureCategory);
            Assert.Equal(ProjectOpenPolicy.NoProjectOpenMessage(mode), exception.Message);
            AssertGuidance(mode, exception.Message);
        }
    }

    [Theory]
    [MemberData(nameof(Allowances))]
    public void Gate_AttachedProject_AlwaysRefusesDifferentPath(McpAccessMode mode,
        bool allowRequestedNotOpen, bool allowNoProject)
    {
        var exception = Assert.Throws<WorkerOperationException>(() =>
            ProjectOpenGate.Require(CurrentPath, OtherPath, mode, allowRequestedNotOpen, allowNoProject));
        Assert.Equal(WorkerFailureCategories.BindingConflict, exception.FailureCategory);
        Assert.Equal(ProjectOpenPolicy.RefusalMessage(CurrentPath, OtherPath, mode), exception.Message);
        AssertGuidance(mode, exception.Message);
    }

    [Theory]
    [MemberData(nameof(Allowances))]
    public void Gate_AttachedProject_AllowsSamePathAndNoRequest(McpAccessMode mode,
        bool allowRequestedNotOpen, bool allowNoProject)
    {
        foreach (var requestedPath in new[] { CurrentPath, @"c:\fixture\a.AP21", null })
            ProjectOpenGate.Require(CurrentPath, requestedPath, mode, allowRequestedNotOpen, allowNoProject);
    }

    private static void AssertGuidance(McpAccessMode mode, string message)
        => Assert.Equal(mode != McpAccessMode.ReadOnly, message.Contains("open_project", StringComparison.Ordinal));
}
