using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Project;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

public class ProjectRebindCloseGuardTests
{
    [Fact]
    public void CloseFailure_PreservesTheFailureAndPreventsOpeningTheReplacementProject()
    {
        var openedReplacement = false;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ProjectRebindCloseGuard.CloseBeforeRebind(
                isCurrentModified: () => false,
                closeCurrent: () => throw new InvalidOperationException("close failed"),
                openReplacement: () => openedReplacement = true));

        Assert.Contains("close failed", exception.Message);
        Assert.False(openedReplacement);
    }

    [Fact]
    public void ModifiedCurrentProject_DoesNotCloseOrOpenAndReportsStateChanged()
    {
        var calls = new List<string>();

        var exception = Assert.Throws<WorkerOperationException>(() =>
            ProjectRebindCloseGuard.CloseBeforeRebind(
                isCurrentModified: () =>
                {
                    calls.Add("read");
                    return true;
                },
                closeCurrent: () => calls.Add("close"),
                openReplacement: () => calls.Add("open")));

        Assert.Equal(WorkerFailureCategories.StateChanged, exception.FailureCategory);
        Assert.Equal(new[] { "read" }, calls);
    }

    [Fact]
    public void ModifiedStateReadFailure_DoesNotCloseOrOpen()
    {
        var calls = new List<string>();

        var exception = Assert.Throws<WorkerOperationException>(() =>
            ProjectRebindCloseGuard.CloseBeforeRebind(
                isCurrentModified: () =>
                {
                    calls.Add("read");
                    throw new InvalidOperationException("sensitive Siemens detail");
                },
                closeCurrent: () => calls.Add("close"),
                openReplacement: () => calls.Add("open")));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, exception.FailureCategory);
        Assert.DoesNotContain("sensitive Siemens detail", exception.Message);
        Assert.Equal(new[] { "read" }, calls);
    }

    [Fact]
    public void UnmodifiedCurrentProject_ClosesThenOpens()
    {
        var calls = new List<string>();

        ProjectRebindCloseGuard.CloseBeforeRebind(
            isCurrentModified: () =>
            {
                calls.Add("read");
                return false;
            },
            closeCurrent: () => calls.Add("close"),
            openReplacement: () => calls.Add("open"));

        Assert.Equal(new[] { "read", "close", "open" }, calls);
    }
}
