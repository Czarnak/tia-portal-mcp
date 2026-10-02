using TiaMcpServer.Contracts;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Safety.Pipeline;

public sealed class WriteGuardCatalogTests
{
    [Fact]
    public void Production_ContainsPartialWriteGuardAsInfo()
    {
        var guard = WriteGuardCatalog.Production.Get("partial_write_no_rollback");

        Assert.Equal(WriteGuardSeverities.Info, guard.Severity);
        Assert.False(string.IsNullOrWhiteSpace(guard.Description));
    }

    [Fact]
    public void DomainGuard_IsFoundWithSeverity()
    {
        var catalog = new WriteGuardCatalog(new[]
        {
            new WriteGuardDefinition("overwrites_block", WriteGuardSeverities.Acknowledge, "Replaces an existing block.")
        });

        Assert.True(catalog.TryGet("overwrites_block", out var guard));
        Assert.Equal(WriteGuardSeverities.Acknowledge, guard.Severity);
        Assert.True(catalog.TryGet("partial_write_no_rollback", out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void BlankId_Throws(string id)
        => Assert.Throws<ArgumentException>(() => new WriteGuardCatalog(new[]
        {
            new WriteGuardDefinition(id, WriteGuardSeverities.Info, "x")
        }));

    [Fact]
    public void UnknownSeverity_Throws()
        => Assert.Throws<ArgumentException>(() => new WriteGuardCatalog(new[]
        {
            new WriteGuardDefinition("g", "loud", "x")
        }));

    [Fact]
    public void DuplicateId_Throws()
        => Assert.Throws<ArgumentException>(() => new WriteGuardCatalog(new[]
        {
            new WriteGuardDefinition("g", WriteGuardSeverities.Info, "x"),
            new WriteGuardDefinition("g", WriteGuardSeverities.Block, "y")
        }));

    [Fact]
    public void RedefiningPipelineGuard_Throws()
        => Assert.Throws<ArgumentException>(() => new WriteGuardCatalog(new[]
        {
            new WriteGuardDefinition("partial_write_no_rollback", WriteGuardSeverities.Block, "x")
        }));

    [Fact]
    public void Get_UnknownId_Throws()
        => Assert.Throws<InvalidOperationException>(() => WriteGuardCatalog.Production.Get("nope"));

    [Fact]
    public void GuardBlocked_IsKnownAndAcceptedByFail()
    {
        Assert.True(WorkerFailureCategories.IsKnown("guard_blocked"));
        var result = WorkerCallResult.Fail(WorkerFailureCategories.GuardBlocked, "blocked");
        Assert.Equal("guard_blocked", result.FailureCategory);
    }

    [Fact]
    public void Vocabulary_ValuesAreStable()
    {
        Assert.Equal("preview", WritePhases.Preview);
        Assert.Equal("applied", WritePhases.Applied);
        Assert.Equal("blocked", WritePhases.Blocked);
        Assert.Equal("error", WritePhases.Error);
        Assert.Equal("policy", GuardSatisfactions.Policy);
        Assert.Equal("user", GuardSatisfactions.User);
        Assert.True(WriteGuardSeverities.IsKnown("block"));
        Assert.False(WriteGuardSeverities.IsKnown(null));
    }
}
