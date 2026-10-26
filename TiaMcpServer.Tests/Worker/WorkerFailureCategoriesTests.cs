using TiaMcpServer.Contracts.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

public sealed class WorkerFailureCategoriesTests
{
    private static readonly string[] ExpectedRefusals =
    {
        "validation_error", "target_not_found", "target_ambiguous", "state_changed",
        "guard_blocked", "binding_conflict", "access_denied"
    };

    [Fact]
    public void PreconditionRefusals_HasExactlySevenWireValues()
    {
        Assert.Equal(ExpectedRefusals.OrderBy(x => x), WorkerFailureCategories.PreconditionRefusals.OrderBy(x => x));
    }

    [Fact]
    public void PreconditionRefusals_AreAllKnown()
    {
        Assert.All(WorkerFailureCategories.PreconditionRefusals, c => Assert.True(WorkerFailureCategories.IsKnown(c)));
    }

    [Fact]
    public void EngineeringRejected_IsKnownWithTheDocumentedWireValue()
    {
        Assert.Equal("engineering_rejected", WorkerFailureCategories.EngineeringRejected);
        Assert.True(WorkerFailureCategories.IsKnown(WorkerFailureCategories.EngineeringRejected));
    }

    [Theory]
    [InlineData("validation_error", true)]
    [InlineData("target_not_found", true)]
    [InlineData("target_ambiguous", true)]
    [InlineData("state_changed", true)]
    [InlineData("guard_blocked", true)]
    [InlineData("binding_conflict", true)]
    [InlineData("access_denied", true)]
    [InlineData("portal_lost", false)]
    [InlineData("worker_timeout", false)]
    [InlineData("worker_crashed", false)]
    [InlineData("worker_operation_failed", false)]
    [InlineData("engineering_rejected", false)]
    [InlineData("protocol_error", false)]
    [InlineData("postcondition_failed", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("Validation_Error", false)]
    public void IsPreconditionRefusal_MatchesSetOnly(string? category, bool expected)
    {
        Assert.Equal(expected, WorkerFailureCategories.IsPreconditionRefusal(category));
    }
}
