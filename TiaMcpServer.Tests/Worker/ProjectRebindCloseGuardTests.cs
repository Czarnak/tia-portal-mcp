using TiaMcpServer.OpennessWorker.Openness;
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
                closeCurrent: () => throw new InvalidOperationException("close failed"),
                openReplacement: () => openedReplacement = true));

        Assert.Contains("close failed", exception.Message);
        Assert.False(openedReplacement);
    }

    [Fact]
    public void SuccessfulClose_OpensTheReplacementProject()
    {
        var openedReplacement = false;

        ProjectRebindCloseGuard.CloseBeforeRebind(
            closeCurrent: () => { },
            openReplacement: () => openedReplacement = true);

        Assert.True(openedReplacement);
    }
}
