using TiaMcpServer.OpennessWorker.Openness.Project;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

public sealed class PortalDetachGuardTests
{
    [Theory]
    [InlineData(false, 0, true)]
    [InlineData(true, 0, false)]
    [InlineData(false, 1, false)]
    public void LastHeadlessLocalClient_CannotDetachUnprovenPreservation(
        bool hasUserInterface, int otherClientCount, bool refuses)
    {
        var method = typeof(PortalDetachGuard).GetMethod("EvaluateProjects",
            [typeof(bool), typeof(int), typeof(IReadOnlyList<bool?>), typeof(bool)]);
        Assert.NotNull(method);
        Assert.Equal(refuses, method.Invoke(null, [hasUserInterface, otherClientCount, Array.Empty<bool?>(), true]) is not null);
    }

    [Theory]
    [InlineData("unselected-modified", false, 0, true)]
    [InlineData("unselected-unknown", false, 0, true)]
    [InlineData("unreadable-collection", false, 0, true)]
    [InlineData("empty", false, 0, false)]
    [InlineData("all-unmodified", false, 0, false)]
    [InlineData("unselected-modified", true, 0, false)]
    [InlineData("unselected-unknown", false, 1, false)]
    public void EvaluateProjects_ProtectsEveryAttachedProjectAndUnreadableCollection(
        string scenario, bool hasUserInterface, int otherClientCount, bool refuses)
    {
        bool?[]? modifiedStates = scenario switch
        {
            "unselected-modified" => new bool?[] { false, true },
            "unselected-unknown" => new bool?[] { false, null },
            "unreadable-collection" => null,
            "empty" => Array.Empty<bool?>(),
            "all-unmodified" => new bool?[] { false, false },
            _ => throw new InvalidOperationException()
        };
        var result = PortalDetachGuard.EvaluateProjects(hasUserInterface, otherClientCount, modifiedStates);
        Assert.Equal(refuses, result is not null);
    }

    [Theory]
    [InlineData(false, 0, true, true, true)]
    [InlineData(false, 0, true, null, true)]
    [InlineData(true, 0, true, true, false)]
    [InlineData(true, 0, true, null, false)]
    [InlineData(false, 1, true, true, false)]
    [InlineData(false, 1, true, null, false)]
    [InlineData(false, 0, true, false, false)]
    [InlineData(false, 0, false, null, false)]
    public void Evaluate_OnlyLastHeadlessClientWithUnsavedOrUnknownProjectRefuses(
        bool hasUserInterface, int otherClientCount, bool hasProject, bool? modified, bool refuses)
    {
        var result = PortalDetachGuard.Evaluate(hasUserInterface, otherClientCount, hasProject, modified);
        Assert.Equal(refuses, result is not null);
    }
}
