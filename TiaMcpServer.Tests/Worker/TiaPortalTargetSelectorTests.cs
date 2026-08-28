using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

public class TiaPortalTargetSelectorTests
{
    [Fact]
    public void SelectProcessId_NoRunningPortal_FailsWithoutSelecting()
    {
        var error = Assert.Throws<WorkerOperationException>(() =>
            TiaPortalTargetSelector.SelectProcessId(Array.Empty<TiaPortalProcessCandidate>(), null));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, error.FailureCategory);
        Assert.Contains("No running TIA Portal", error.Message);
    }

    [Fact]
    public void SelectProcessId_OnePortal_IsTheOnlySafeFallback()
    {
        var candidates = new[]
        {
            new TiaPortalProcessCandidate(4100, @"C:\Projects\Open.ap21")
        };

        var selected = TiaPortalTargetSelector.SelectProcessId(
            candidates,
            @"C:\Projects\NotOpenYet.ap21");

        Assert.Equal(4100, selected);
    }

    [Fact]
    public void SelectProcessId_ExactPathSelectsOnePortalRegardlessOfEnumerationOrder()
    {
        var candidates = new[]
        {
            new TiaPortalProcessCandidate(9200, @"C:\Projects\B.ap21"),
            new TiaPortalProcessCandidate(4100, @"C:\Projects\A.ap21")
        };

        var selected = TiaPortalTargetSelector.SelectProcessId(
            candidates,
            @"C:\Projects\Other\..\A.ap21");

        Assert.Equal(4100, selected);
    }

    [Fact]
    public void SelectProcessId_MultiplePortalsWithoutPath_FailsClosedAndListsSortedPids()
    {
        var candidates = new[]
        {
            new TiaPortalProcessCandidate(9200, @"C:\Projects\B.ap21"),
            new TiaPortalProcessCandidate(4100, @"C:\Projects\A.ap21")
        };

        var error = Assert.Throws<WorkerOperationException>(() =>
            TiaPortalTargetSelector.SelectProcessId(candidates, null));

        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, error.FailureCategory);
        Assert.True(error.Message.IndexOf("PID 4100", StringComparison.Ordinal)
            < error.Message.IndexOf("PID 9200", StringComparison.Ordinal));
        Assert.Contains("No process was attached", error.Message);
    }

    [Fact]
    public void SelectProcessId_MultiplePortalsWithoutExactMatch_DoesNotFallback()
    {
        var candidates = new[]
        {
            new TiaPortalProcessCandidate(4100, @"C:\Projects\A.ap21"),
            new TiaPortalProcessCandidate(9200, @"C:\Projects\B.ap21")
        };

        var error = Assert.Throws<WorkerOperationException>(() =>
            TiaPortalTargetSelector.SelectProcessId(candidates, @"C:\Projects\C.ap21"));

        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, error.FailureCategory);
        Assert.Contains("No running TIA Portal process uniquely exposes", error.Message);
    }

    [Fact]
    public void SelectProcessId_DuplicateExactMatches_AreAmbiguous()
    {
        var candidates = new[]
        {
            new TiaPortalProcessCandidate(4100, @"C:\Projects\A.ap21"),
            new TiaPortalProcessCandidate(9200, @"C:\Projects\A.ap21")
        };

        var error = Assert.Throws<WorkerOperationException>(() =>
            TiaPortalTargetSelector.SelectProcessId(candidates, @"C:\Projects\A.ap21"));

        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, error.FailureCategory);
        Assert.Contains("Multiple TIA Portal processes", error.Message);
    }

    [Fact]
    public void SelectProjectIndex_ExactPathSelectsOnlyThatProject()
    {
        string?[] projects =
        {
            @"C:\Projects\B.ap21",
            @"C:\Projects\A.ap21"
        };

        var selected = TiaPortalTargetSelector.SelectProjectIndex(
            projects,
            @"C:\Projects\Other\..\A.ap21");

        Assert.Equal(1, selected);
    }

    [Fact]
    public void SelectProjectIndex_MissingExactPath_DoesNotFallbackToAnotherProject()
    {
        string?[] projects = { @"C:\Projects\B.ap21" };

        var selected = TiaPortalTargetSelector.SelectProjectIndex(
            projects,
            @"C:\Projects\A.ap21");

        Assert.Null(selected);
    }

    [Fact]
    public void SelectProjectIndex_OneProjectWithoutExpectedPath_IsSafe()
    {
        string?[] projects = { @"C:\Projects\A.ap21" };

        Assert.Equal(0, TiaPortalTargetSelector.SelectProjectIndex(projects, null));
    }

    [Fact]
    public void SelectProjectIndex_MultipleProjectsWithoutExpectedPath_FailsClosed()
    {
        string?[] projects =
        {
            @"C:\Projects\A.ap21",
            @"C:\Projects\B.ap21"
        };

        var error = Assert.Throws<WorkerOperationException>(() =>
            TiaPortalTargetSelector.SelectProjectIndex(projects, null));

        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, error.FailureCategory);
        Assert.Contains("No project was selected", error.Message);
    }

    [Fact]
    public void SelectProjectIndex_DuplicateExactPaths_AreAmbiguous()
    {
        string?[] projects =
        {
            @"C:\Projects\A.ap21",
            @"C:\Projects\A.ap21"
        };

        var error = Assert.Throws<WorkerOperationException>(() =>
            TiaPortalTargetSelector.SelectProjectIndex(projects, @"C:\Projects\A.ap21"));

        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, error.FailureCategory);
    }
}
