using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

public sealed class ProjectBindingDecisionTests
{
    private const string A = "C:/Projects/A.ap21";
    private const string B = "C:/Projects/B.ap21";
    internal static ProjectBindingSnapshot Snapshot(string state, string? path = A)
        => new(state, "binding", 4, path, "worker", 2, 42, null);

    [Fact]
    public void Decide_UnboundNoPath_Lists() => Assert.IsType<BindingStep.ListThenSelect>(ProjectBindingDecision.Decide(Snapshot(ProjectBindingSnapshot.UnboundState, null), null, false));

    [Fact]
    public void Decide_UnboundPath_Selects() => Assert.Equal(new BindingStep.Select(ProjectPathNormalization.Canonicalize(B)!, false), ProjectBindingDecision.Decide(Snapshot(ProjectBindingSnapshot.UnboundState, null), B, false));

    [Theory]
    [InlineData(ProjectBindingSnapshot.ConfiguredUnverifiedState, null)]
    [InlineData(ProjectBindingSnapshot.ConfiguredUnverifiedState, A)]
    [InlineData(ProjectBindingSnapshot.InvalidatedState, null)]
    [InlineData(ProjectBindingSnapshot.InvalidatedState, A)]
    public void Decide_RetainedPath_Selects(string state, string? requested) => Assert.Equal(new BindingStep.Select(ProjectPathNormalization.Canonicalize(A)!, false), ProjectBindingDecision.Decide(Snapshot(state), requested, false));

    [Theory]
    [InlineData(ProjectBindingSnapshot.ConfiguredUnverifiedState)]
    [InlineData(ProjectBindingSnapshot.InvalidatedState)]
    [InlineData(ProjectBindingSnapshot.VerifiedState)]
    public void Decide_DifferentPath_RequiresForce(string state)
    {
        Assert.Equal(WorkerFailureCategories.BindingConflict, Assert.IsType<BindingStep.Reject>(ProjectBindingDecision.Decide(Snapshot(state), B, false)).Category);
        Assert.Equal(new BindingStep.Select(ProjectPathNormalization.Canonicalize(B)!, state == ProjectBindingSnapshot.VerifiedState), ProjectBindingDecision.Decide(Snapshot(state), B, true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(A)]
    public void Decide_VerifiedSamePath_Reverifies(string? requested) => Assert.IsType<BindingStep.Reverify>(ProjectBindingDecision.Decide(Snapshot(ProjectBindingSnapshot.VerifiedState), requested, false));

    [Fact]
    public void Decide_PathCaseOnlyDifference_IsSameProject() => Assert.IsType<BindingStep.Reverify>(ProjectBindingDecision.Decide(Snapshot(ProjectBindingSnapshot.VerifiedState), A.ToLowerInvariant(), true));
    [Fact]
    public void Decide_UnknownState_Rejects() => Assert.IsType<BindingStep.Reject>(ProjectBindingDecision.Decide(Snapshot("unknown"), null, false));
    [Fact]
    public void ChooseFromListing_OneProjectAmongEmptyPortals_Selects() => Assert.Equal(new ListingChoice.Select(ProjectPathNormalization.Canonicalize(A)!, 42), ProjectBindingDecision.ChooseFromListing(new[] { new TiaPortalProcessInfo { ProcessId = 41 }, new TiaPortalProcessInfo { ProcessId = 42, ProjectPath = A } }));
    [Fact]
    public void ChooseFromListing_SolePortalWithoutAdvertisedPath_InspectsOwner() => Assert.Equal(new ListingChoice.Select(null, 42), ProjectBindingDecision.ChooseFromListing(new[] { new TiaPortalProcessInfo { ProcessId = 42 } }));
    [Fact]
    public void ChooseFromListing_ExplicitPidWithoutOwnerPath_InspectsAllOwners()
        => Assert.Equal(new ListingChoice.Select(null, 42), ProjectBindingDecision.ChooseFromListing(
            new[] { new TiaPortalProcessInfo { ProcessId = 42, ProjectPath = A } }, 42));
    [Fact]
    public void ChooseFromListing_NoPortals_NotFound() => Assert.IsType<ListingChoice.NotFound>(ProjectBindingDecision.ChooseFromListing(Array.Empty<TiaPortalProcessInfo>()));
    [Fact]
    public void ChooseFromListing_TwoUnadvertisedPortals_Ambiguous()
        => Assert.IsType<ListingChoice.Ambiguous>(ProjectBindingDecision.ChooseFromListing(new[]
        {
            new TiaPortalProcessInfo { ProcessId = 41 }, new TiaPortalProcessInfo { ProcessId = 42 }
        }));
    [Fact]
    public void ChooseFromListing_TwoProjects_Ambiguous() => Assert.IsType<ListingChoice.Ambiguous>(ProjectBindingDecision.ChooseFromListing(new[] { new TiaPortalProcessInfo { ProjectPath = A }, new TiaPortalProcessInfo { ProjectPath = B } }));
    [Fact]
    public void ChooseFromListing_SamePathTwoPortals_Ambiguous() => Assert.IsType<ListingChoice.Ambiguous>(ProjectBindingDecision.ChooseFromListing(new[] { new TiaPortalProcessInfo { ProjectPath = A }, new TiaPortalProcessInfo { ProjectPath = A } }));
    [Fact]
    public void PreDetachSet_ExcludesBindingConflict()
    {
        Assert.Equal(3, ProjectBindingDecision.PreDetachRefusalCategories.Count);
        Assert.DoesNotContain(WorkerFailureCategories.BindingConflict, ProjectBindingDecision.PreDetachRefusalCategories);
        Assert.Contains(WorkerFailureCategories.TargetNotFound, ProjectBindingDecision.PreDetachRefusalCategories);
        Assert.Contains(WorkerFailureCategories.TargetAmbiguous, ProjectBindingDecision.PreDetachRefusalCategories);
        Assert.Contains(WorkerFailureCategories.GuardBlocked, ProjectBindingDecision.PreDetachRefusalCategories);
    }
}
