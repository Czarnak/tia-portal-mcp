using TiaMcpServer.Safety.Pipeline;
using Xunit;

namespace TiaMcpServer.Tests.Safety.Pipeline;

public sealed class GuardDecisionsTests
{
    private static readonly WriteGuardCatalog Catalog = new(new[]
    {
        new WriteGuardDefinition("note", WriteGuardSeverities.Info, "Informational."),
        new WriteGuardDefinition("overwrite", WriteGuardSeverities.Acknowledge, "Overwrites."),
        new WriteGuardDefinition("other_ack", WriteGuardSeverities.Acknowledge, "Other."),
        new WriteGuardDefinition("forbidden", WriteGuardSeverities.Block, "Never allowed.")
    });

    private static FiredGuard Fire(string id, string? op = "op1") => new(id, op, $"{id} fired.");

    [Fact]
    public void ValidateAcknowledgeList_NullAndEmpty_AreValid()
    {
        Assert.Null(GuardDecisions.ValidateAcknowledgeList(null, Catalog));
        Assert.Null(GuardDecisions.ValidateAcknowledgeList(Array.Empty<string>(), Catalog));
    }

    [Fact]
    public void ValidateAcknowledgeList_AcknowledgeId_IsValid()
        => Assert.Null(GuardDecisions.ValidateAcknowledgeList(new[] { "overwrite" }, Catalog));

    [Fact]
    public void ValidateAcknowledgeList_RejectsEachFailureKindWithDistinctMessage()
    {
        var messages = new[]
        {
            GuardDecisions.ValidateAcknowledgeList(new[] { " " }, Catalog),
            GuardDecisions.ValidateAcknowledgeList(new[] { "overwrite", "overwrite" }, Catalog),
            GuardDecisions.ValidateAcknowledgeList(new[] { "nope" }, Catalog),
            GuardDecisions.ValidateAcknowledgeList(new[] { "note" }, Catalog),
            GuardDecisions.ValidateAcknowledgeList(new[] { "forbidden" }, Catalog)
        };

        Assert.All(messages, m => Assert.False(string.IsNullOrWhiteSpace(m)));
        Assert.Equal(messages.Length, messages.Distinct().Count());
        Assert.Contains("nope", messages[2]);
        Assert.Contains("note", messages[3]);
        Assert.Contains("forbidden", messages[4]);
    }

    [Fact]
    public void Decide_NoGuards_Proceeds()
    {
        var d = GuardDecisions.Decide(Array.Empty<FiredGuard>(), null, Catalog, dryRun: false);

        Assert.Equal(GuardDecisionKind.Proceed, d.Kind);
        Assert.Empty(d.Guards);
        Assert.Null(d.Message);
    }

    [Fact]
    public void Decide_InfoGuard_ProceedsWithNullAcknowledged()
    {
        var d = GuardDecisions.Decide(new[] { Fire("note") }, null, Catalog, dryRun: false);

        Assert.Equal(GuardDecisionKind.Proceed, d.Kind);
        var report = Assert.Single(d.Guards);
        Assert.Equal(WriteGuardSeverities.Info, report.Severity);
        Assert.Null(report.Acknowledged);
        Assert.Equal("op1", report.OperationId);
    }

    [Fact]
    public void Decide_UnacknowledgedGuard_BlocksAndNamesId()
    {
        var d = GuardDecisions.Decide(new[] { Fire("overwrite") }, null, Catalog, dryRun: false);

        Assert.Equal(GuardDecisionKind.Blocked, d.Kind);
        Assert.Contains("overwrite", d.Message);
        Assert.False(Assert.Single(d.Guards).Acknowledged);
    }

    [Fact]
    public void Decide_AcknowledgedGuard_ProceedsWithAcknowledgedTrue()
    {
        var d = GuardDecisions.Decide(new[] { Fire("overwrite") }, new[] { "overwrite" }, Catalog, dryRun: false);

        Assert.Equal(GuardDecisionKind.Proceed, d.Kind);
        Assert.True(Assert.Single(d.Guards).Acknowledged);
    }

    [Fact]
    public void Decide_BlockGuard_BlocksDespiteAcknowledgements()
    {
        var d = GuardDecisions.Decide(
            new[] { Fire("forbidden"), Fire("overwrite") }, new[] { "overwrite" }, Catalog, dryRun: false);

        Assert.Equal(GuardDecisionKind.Blocked, d.Kind);
        Assert.Contains("forbidden", d.Message);
        Assert.Null(d.Guards.Single(g => g.Id == "forbidden").Acknowledged);
    }

    [Fact]
    public void Decide_AcknowledgedIdThatDidNotFire_IsInvalid()
    {
        var d = GuardDecisions.Decide(new[] { Fire("note") }, new[] { "overwrite" }, Catalog, dryRun: false);

        Assert.Equal(GuardDecisionKind.Invalid, d.Kind);
        Assert.Contains("overwrite", d.Message);
    }

    [Fact]
    public void Decide_OneAcknowledgementCoversEveryFiring()
    {
        var d = GuardDecisions.Decide(
            new[] { Fire("overwrite", "a"), Fire("overwrite", "b") }, new[] { "overwrite" }, Catalog, dryRun: false);

        Assert.Equal(GuardDecisionKind.Proceed, d.Kind);
        Assert.Equal(2, d.Guards.Count);
        Assert.All(d.Guards, g => Assert.True(g.Acknowledged));
    }

    [Fact]
    public void Decide_DryRun_NeverBlocksButReportsAcknowledged()
    {
        var d = GuardDecisions.Decide(
            new[] { Fire("overwrite"), Fire("forbidden") }, null, Catalog, dryRun: true);

        Assert.Equal(GuardDecisionKind.Proceed, d.Kind);
        Assert.False(d.Guards.Single(g => g.Id == "overwrite").Acknowledged);
        Assert.Null(d.Guards.Single(g => g.Id == "forbidden").Acknowledged);
    }

    [Fact]
    public void Decide_DryRun_StillRejectsNotFiredAcknowledgement()
    {
        var d = GuardDecisions.Decide(Array.Empty<FiredGuard>(), new[] { "overwrite" }, Catalog, dryRun: true);

        Assert.Equal(GuardDecisionKind.Invalid, d.Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Decide_UnfiredAcknowledgement_IsInvalidEvenWhenAnotherGuardIsUnacknowledged(bool dryRun)
    {
        var d = GuardDecisions.Decide(new[] { Fire("other_ack") }, new[] { "overwrite" }, Catalog, dryRun);

        Assert.Equal(GuardDecisionKind.Invalid, d.Kind);
        Assert.Contains("overwrite", d.Message);
    }

    [Fact]
    public void DecideLate_IgnoresAcknowledgementsForUnfiredGuards()
    {
        var d = GuardDecisions.DecideLate(new[] { Fire("note") }, new[] { "overwrite" }, Catalog);

        Assert.Equal(GuardDecisionKind.Proceed, d.Kind);
    }

    [Fact]
    public void DecideLate_BlocksUnacknowledgedLateGuard()
    {
        var d = GuardDecisions.DecideLate(new[] { Fire("other_ack") }, new[] { "overwrite" }, Catalog);

        Assert.Equal(GuardDecisionKind.Blocked, d.Kind);
        Assert.Contains("other_ack", d.Message);
    }

    [Fact]
    public void Reports_TakeSeverityFromCatalog()
    {
        var d = GuardDecisions.Decide(
            new[] { Fire("note"), Fire("overwrite"), Fire("forbidden") }, new[] { "overwrite" }, Catalog, dryRun: true);

        Assert.Equal(WriteGuardSeverities.Info, d.Guards.Single(g => g.Id == "note").Severity);
        Assert.Equal(WriteGuardSeverities.Acknowledge, d.Guards.Single(g => g.Id == "overwrite").Severity);
        Assert.Equal(WriteGuardSeverities.Block, d.Guards.Single(g => g.Id == "forbidden").Severity);
    }
}
