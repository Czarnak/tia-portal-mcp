using TiaMcpServer.Safety.Pipeline;
using Xunit;

namespace TiaMcpServer.Tests.Safety.Pipeline;

public sealed class GuardDecisionsTests
{
    private static readonly WriteGuardCatalog Catalog = new([
        new("note", WriteGuardSeverities.Info, "Informational."),
        new("overwrite", WriteGuardSeverities.Acknowledge, "Overwrites."),
        new("forbidden", WriteGuardSeverities.Block, "Never allowed.")]);
    private static FiredGuard Fire(string id) => new(id, "op1", $"{id} fired.");

    [Fact]
    public void Decide_PolicyMode_AutoSatisfiesAcknowledge()
    {
        var result = GuardDecisions.Decide([Fire("overwrite")], ConfirmationMode.Policy, Catalog, false);
        Assert.Equal(GuardDecisionKind.Proceed, result.Kind);
        Assert.True(Assert.Single(result.Guards).Acknowledged);
    }

    [Fact]
    public void Decide_AskUser_AcknowledgeNeedsUser()
    {
        var result = GuardDecisions.Decide([Fire("overwrite")], ConfirmationMode.AskUser, Catalog, false);
        Assert.Equal(GuardDecisionKind.NeedsUser, result.Kind);
        Assert.False(Assert.Single(result.Guards).Acknowledged);
    }

    [Theory]
    [InlineData(ConfirmationMode.Policy)]
    [InlineData(ConfirmationMode.AskUser)]
    public void Decide_BlockGuard_BlocksInBothModes(ConfirmationMode mode)
    {
        var result = GuardDecisions.Decide([Fire("overwrite"), Fire("forbidden")], mode, Catalog, false);
        Assert.Equal(GuardDecisionKind.Blocked, result.Kind);
        Assert.Contains("forbidden", result.Message);
        Assert.Null(result.Guards.Single(g => g.Id == "forbidden").Acknowledged);
    }

    [Theory]
    [InlineData(ConfirmationMode.Policy)]
    [InlineData(ConfirmationMode.AskUser)]
    public void Decide_DryRun_NeverBlocksOrAsks(ConfirmationMode mode)
    {
        var result = GuardDecisions.Decide([Fire("overwrite"), Fire("forbidden")], mode, Catalog, true);
        Assert.Equal(GuardDecisionKind.Proceed, result.Kind);
        Assert.Equal(mode == ConfirmationMode.Policy, result.Guards.Single(g => g.Id == "overwrite").Acknowledged);
    }

    [Fact]
    public void DecideLate_AskUser_AcknowledgeBlocks()
        => Assert.Equal(GuardDecisionKind.Blocked,
            GuardDecisions.DecideLate([Fire("overwrite")], ConfirmationMode.AskUser, Catalog).Kind);

    [Fact]
    public void DecideLate_Policy_AcknowledgeProceeds()
        => Assert.Equal(GuardDecisionKind.Proceed,
            GuardDecisions.DecideLate([Fire("overwrite")], ConfirmationMode.Policy, Catalog).Kind);

    [Theory]
    [InlineData(ConfirmationMode.Policy)]
    [InlineData(ConfirmationMode.AskUser)]
    public void Decide_NoGuardsOrInfo_Proceeds(ConfirmationMode mode)
    {
        Assert.Equal(GuardDecisionKind.Proceed, GuardDecisions.Decide([], mode, Catalog, false).Kind);
        var result = GuardDecisions.Decide([Fire("note")], mode, Catalog, false);
        Assert.Equal(GuardDecisionKind.Proceed, result.Kind);
        Assert.Null(Assert.Single(result.Guards).Acknowledged);
    }
}
