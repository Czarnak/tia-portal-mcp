using TiaMcpServer.OpennessWorker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

public class WorkerWarningMergerTests
{
    [Fact]
    public void PreservesExistingWarnings_AndAppendsCapturedStderr_WhenBothArePresent()
    {
        // Reproduces the finding: a WorkerOperationException's deliberate, hand-crafted
        // warning (e.g. SaveProjectAs's postcondition_failed guidance) must survive alongside
        // whatever incidental stderr TIA Portal emitted during the same request — not be
        // silently clobbered by it.
        var existing = new List<string> { "Project state may have changed; inspect the open project before retrying." };
        var captured = new List<string> { "Skipping device X: access denied" };

        var merged = WorkerWarningMerger.Merge(existing, captured);

        Assert.NotNull(merged);
        Assert.Equal(
            new[]
            {
                "Project state may have changed; inspect the open project before retrying.",
                "Skipping device X: access denied"
            },
            merged);
    }

    [Fact]
    public void ReturnsCapturedStderr_WhenNoWarningsExistedBeforehand()
    {
        var captured = new List<string> { "Skipping device X: access denied" };

        var merged = WorkerWarningMerger.Merge(existing: null, captured);

        Assert.Equal(captured, merged);
    }

    [Fact]
    public void ReturnsCapturedStderr_WhenExistingWarningsWereAnEmptyList()
    {
        var captured = new List<string> { "Skipping device X: access denied" };

        var merged = WorkerWarningMerger.Merge(existing: new List<string>(), captured);

        Assert.Equal(captured, merged);
    }

    [Fact]
    public void ReturnsExistingWarningsUnchanged_WhenNothingWasCaptured()
    {
        var existing = new List<string> { "Project state may have changed; inspect the open project before retrying." };

        var merged = WorkerWarningMerger.Merge(existing, captured: Array.Empty<string>());

        Assert.Same(existing, merged);
    }

    [Fact]
    public void ReturnsNull_WhenNeitherExistingWarningsNorCapturedStderrArePresent()
    {
        var merged = WorkerWarningMerger.Merge(existing: null, captured: Array.Empty<string>());

        Assert.Null(merged);
    }
}
