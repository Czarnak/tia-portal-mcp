using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Safety.Pipeline;

public sealed class WriteGuardCatalogTests
{
    [Fact]
    public void Production_ContainsPartialWriteGuardAsInfo()
    {
        var guard = new WriteGuardCatalog(Array.Empty<WriteGuardDefinition>()).Get("partial_write_no_rollback");

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
    public void NonInteractive_RejectsAcknowledge()
    {
        var ex = Assert.Throws<ArgumentException>(() => WriteGuardCatalog.NonInteractive(new[]
        {
            new WriteGuardDefinition("g", WriteGuardSeverities.Acknowledge, "x")
        }, "HMI"));
        Assert.Equal("definitions", ex.ParamName);
        Assert.StartsWith("HMI guards cannot require acknowledgement.", ex.Message);
    }

    [Fact]
    public void NonInteractive_ReturnsDefinitionsInOrder()
    {
        var result = WriteGuardCatalog.NonInteractive(new[]
        {
            new WriteGuardDefinition("a", WriteGuardSeverities.Block, "x"),
            new WriteGuardDefinition("b", WriteGuardSeverities.Info, "y")
        }, "HMI");
        Assert.Equal(new[] { "a", "b" }, result.Select(d => d.Id));
    }

    [Fact]
    public void NonInteractive_RejectsDuplicateIds()
        => Assert.Throws<ArgumentException>(() => WriteGuardCatalog.NonInteractive(new[]
        {
            new WriteGuardDefinition("g", WriteGuardSeverities.Info, "x"),
            new WriteGuardDefinition("g", WriteGuardSeverities.Block, "y")
        }, "HMI"));

    [Fact]
    public void NonInteractive_RejectsPipelineGuardId()
        => Assert.Throws<ArgumentException>(() => WriteGuardCatalog.NonInteractive(new[]
        {
            new WriteGuardDefinition(WriteGuardCatalog.PartialWriteGuardId, WriteGuardSeverities.Info, "x")
        }, "HMI"));

    [Fact]
    public void NonInteractive_RejectsNullDefinition()
        => Assert.Throws<ArgumentException>(() => WriteGuardCatalog.NonInteractive(
            new WriteGuardDefinition?[] { null }!, "HMI"));
    [Fact]
    public void Get_UnknownId_Throws()
        => Assert.Throws<InvalidOperationException>(() => new WriteGuardCatalog(Array.Empty<WriteGuardDefinition>()).Get("nope"));

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
