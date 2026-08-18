using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public class ProjectSessionBindingTests
{
    [Fact]
    public void FirstExplicitProjectPathResolvesWithoutBindingTheSession()
    {
        var binding = new ProjectSessionBinding(null);

        Assert.True(binding.TryResolve("C:\\Projects\\Line.ap21", out var effectivePath, out var error));

        Assert.Equal("C:\\Projects\\Line.ap21", effectivePath);
        Assert.Null(error);
        Assert.Null(binding.BoundProjectPath);
    }

    [Fact]
    public void RepeatedSameProjectPath_WithDotDotSegments_IsAcceptedAsTheSameProject()
    {
        // Finding 3 regression: the binding stores TIA's canonical Project.Path.FullName, so a
        // later caller who spells the same project differently (relative segments here, but the
        // same applies to forward-vs-back slashes or trailing separators) must still match -
        // Trim()-only comparison previously rejected this as "already bound to a different project".
        var binding = new ProjectSessionBinding(null);
        binding.Bind("C:\\Projects\\Line.ap21", forceRebind: false, out _);

        Assert.True(binding.TryResolve(
            "C:\\Projects\\Other\\..\\Line.ap21",
            out var effectivePath,
            out var error));

        Assert.Equal("C:\\Projects\\Line.ap21", effectivePath);
        Assert.Null(error);
    }

    [Fact]
    public void RepeatedSameProjectPath_WithForwardSlashes_IsAcceptedAsTheSameProject()
    {
        var binding = new ProjectSessionBinding(null);
        binding.Bind("C:\\Projects\\Line.ap21", forceRebind: false, out _);

        Assert.True(binding.TryResolve(
            "C:/Projects/Line.ap21",
            out var effectivePath,
            out var error));

        Assert.Equal("C:\\Projects\\Line.ap21", effectivePath);
        Assert.Null(error);
    }

    [Fact]
    public void RepeatedSameProjectPathIsAccepted()
    {
        var binding = new ProjectSessionBinding(null);
        binding.Bind("C:\\Projects\\Line.ap21", forceRebind: false, out _);

        Assert.True(binding.TryResolve("C:\\Projects\\Line.ap21", out var effectivePath, out var error));

        Assert.Equal("C:\\Projects\\Line.ap21", effectivePath);
        Assert.Null(error);
    }

    [Fact]
    public void DifferentProjectPathIsRejectedAfterBinding()
    {
        var binding = new ProjectSessionBinding(null);
        binding.Bind("C:\\Projects\\Line.ap21", forceRebind: false, out _);

        Assert.False(binding.TryResolve("C:\\Projects\\Other.ap21", out var effectivePath, out var error));

        Assert.Null(effectivePath);
        Assert.Contains("already bound", error);
    }

    [Fact]
    public void OmittedProjectPathUsesStartupProjectPath()
    {
        var binding = new ProjectSessionBinding("C:\\Projects\\Startup.ap21");

        Assert.True(binding.TryResolve(null, out var effectivePath, out var error));

        Assert.Equal("C:\\Projects\\Startup.ap21", effectivePath);
        Assert.Null(error);
    }

    [Fact]
    public void BindStoresTrimmedProjectPath()
    {
        var binding = new ProjectSessionBinding(null);

        Assert.True(binding.Bind("  C:\\Projects\\Line.ap21  ", forceRebind: false, out var error));

        Assert.Null(error);
        Assert.Equal("C:\\Projects\\Line.ap21", binding.BoundProjectPath);
    }

    [Fact]
    public void BindSetsUnboundProjectPath()
    {
        var binding = new ProjectSessionBinding(null);

        Assert.True(binding.Bind("C:\\Projects\\Line.ap21", forceRebind: false, out var error));

        Assert.Null(error);
        Assert.Equal("C:\\Projects\\Line.ap21", binding.BoundProjectPath);
    }

    [Fact]
    public void BindRejectsDifferentProjectPathWithoutForce()
    {
        var binding = new ProjectSessionBinding("C:\\Projects\\Line.ap21");

        Assert.False(binding.Bind("C:\\Projects\\Other.ap21", forceRebind: false, out var error));

        Assert.Contains("already bound", error);
        Assert.Equal("C:\\Projects\\Line.ap21", binding.BoundProjectPath);
    }

    [Fact]
    public void BindForceRebindsDifferentProjectPath()
    {
        var binding = new ProjectSessionBinding("C:\\Projects\\Line.ap21");

        Assert.True(binding.Bind("C:\\Projects\\Other.ap21", forceRebind: true, out var error));

        Assert.Null(error);
        Assert.Equal("C:\\Projects\\Other.ap21", binding.BoundProjectPath);
    }

    [Fact]
    public void ClearRemovesMatchingProjectBinding()
    {
        var binding = new ProjectSessionBinding("C:\\Projects\\Line.ap21");

        Assert.True(binding.Clear("C:\\Projects\\Line.ap21", out var error));

        Assert.Null(error);
        Assert.Null(binding.BoundProjectPath);
    }

    [Fact]
    public void ClearWithNullPath_ClearsAnyBinding()
    {
        // The Close BindingTransition falls back to Clear(null) so a session always ends unbound
        // after a successful close, regardless of which project was bound. Clear(null) is
        // unconditional: no path is named to conflict with, so it clears whatever is bound.
        var binding = new ProjectSessionBinding("C:\\Projects\\Line.ap21");

        Assert.True(binding.Clear(null, out var error));

        Assert.Null(error);
        Assert.Null(binding.BoundProjectPath);
    }

    [Fact]
    public void ClearRejectsDifferentProjectPath()
    {
        var binding = new ProjectSessionBinding("C:\\Projects\\Line.ap21");

        Assert.False(binding.Clear("C:\\Projects\\Other.ap21", out var error));

        Assert.Contains("already bound", error);
        Assert.Equal("C:\\Projects\\Line.ap21", binding.BoundProjectPath);
    }

    [Fact]
    public void TryResolve_RejectionMentionsForceRebindEscapeHatch()
    {
        var binding = new ProjectSessionBinding(@"C:\Projects\a.ap21");

        var resolved = binding.TryResolve(@"C:\Projects\b.ap21", out _, out var error);

        Assert.False(resolved);
        Assert.Contains("forceRebind", error);
        Assert.Contains("open_project", error);
    }

    [Fact]
    public void CanBindAllowsFirstBindingWithoutMutating()
    {
        var binding = new ProjectSessionBinding(null);

        Assert.True(binding.CanBind("C:\\Projects\\Line.ap21", forceRebind: false, out var error));

        Assert.Null(error);
        Assert.Null(binding.BoundProjectPath);
    }

    [Fact]
    public void CanBindRejectsDifferentProjectPathWithoutMutating()
    {
        var binding = new ProjectSessionBinding("C:\\Projects\\Line.ap21");

        Assert.False(binding.CanBind("C:\\Projects\\Other.ap21", forceRebind: false, out var error));

        Assert.Contains("already bound", error);
        Assert.Equal("C:\\Projects\\Line.ap21", binding.BoundProjectPath);
    }

    [Fact]
    public void CanBindAllowsDifferentProjectPathWhenForced()
    {
        var binding = new ProjectSessionBinding("C:\\Projects\\Line.ap21");

        Assert.True(binding.CanBind("C:\\Projects\\Other.ap21", forceRebind: true, out var error));

        Assert.Null(error);
        Assert.Equal("C:\\Projects\\Line.ap21", binding.BoundProjectPath);
    }

    [Fact]
    public void CanBindRejectsBlankProjectPath()
    {
        var binding = new ProjectSessionBinding(null);

        Assert.False(binding.CanBind("   ", forceRebind: false, out var error));

        Assert.Equal("Project path is required.", error);
    }

    [Fact]
    public void TryResolve_DoesNotAdoptTheRequestedPath()
    {
        var binding = new ProjectSessionBinding(null);

        Assert.True(binding.TryResolve("C:\\a.ap21", out var effective, out var error));

        Assert.Equal("C:\\a.ap21", effective);
        Assert.Null(error);
        Assert.Null(binding.BoundProjectPath);
    }

    [Fact]
    public void TryResolve_LeavesSessionUnboundSoASecondDifferentPathIsStillAccepted()
    {
        var binding = new ProjectSessionBinding(null);

        Assert.True(binding.TryResolve("C:\\a.ap21", out _, out _));
        Assert.True(binding.TryResolve("C:\\b.ap21", out var effective, out var error));

        Assert.Equal("C:\\b.ap21", effective);
        Assert.Null(error);
    }

    [Fact]
    public void TryResolve_StillRejectsADifferentPathOnceBound()
    {
        var binding = new ProjectSessionBinding(null);
        Assert.True(binding.Bind("C:\\a.ap21", forceRebind: false, out _));

        Assert.False(binding.TryResolve("C:\\b.ap21", out _, out var error));

        Assert.Contains("already bound", error);
        Assert.Contains("forceRebind=true", error);
    }

    [Fact]
    public void AllRejectionPathsGiveIdenticalRebindInstructions()
    {
        const string bound = "C:\\Projects\\Line.ap21";
        const string other = "C:\\Projects\\Other.ap21";

        var forTryResolve = new ProjectSessionBinding(bound);
        forTryResolve.TryResolve(other, out _, out var tryResolveError);

        var forBind = new ProjectSessionBinding(bound);
        forBind.Bind(other, forceRebind: false, out var bindError);

        var forCanBind = new ProjectSessionBinding(bound);
        forCanBind.CanBind(other, forceRebind: false, out var canBindError);

        Assert.NotNull(tryResolveError);
        Assert.Equal(tryResolveError, bindError);
        Assert.Equal(tryResolveError, canBindError);
        Assert.Contains("forceRebind=true", tryResolveError);
        Assert.Contains("open_project", tryResolveError);
    }

    [Fact]
    public void StartupPathIsNotWriteReadyUntilWorkerIdentityPromotesIt()
    {
        var binding = new ProjectSessionBinding("C:\\Projects\\Line.ap21");

        Assert.Equal(ProjectBindingSnapshot.ConfiguredUnverifiedState, binding.BindingState);
        Assert.False(binding.TryGetVerified(null, out _, out var beforeError));
        Assert.Contains("worker-verified", beforeError);

        Assert.True(binding.TryPromoteConfigured(
            new WorkerSessionIdentity
            {
                WorkerSessionId = "worker-a",
                SessionGeneration = 7,
                PortalProcessId = 4242,
                ProjectPath = "C:/Projects/Line.ap21"
            },
            out var promoteError));

        Assert.Null(promoteError);
        Assert.True(binding.TryGetVerified(null, out var verified, out var verifiedError));
        Assert.Null(verifiedError);
        Assert.Equal(ProjectBindingSnapshot.VerifiedState, verified!.State);
        Assert.Equal("worker-a", verified.WorkerSessionId);
        Assert.Equal(7, verified.SessionGeneration);
        Assert.Equal(4242, verified.PortalProcessId);
        Assert.Equal("C:\\Projects\\Line.ap21", verified.ProjectPath);
    }

    [Fact]
    public void InvalidatedIdentityFailsClosedEvenForTheSameProjectPath()
    {
        var binding = new ProjectSessionBinding(null);
        Assert.True(binding.BindVerified(
            new WorkerSessionIdentity
            {
                WorkerSessionId = "worker-a",
                SessionGeneration = 1,
                PortalProcessId = 4242,
                ProjectPath = "C:\\Projects\\Line.ap21"
            },
            forceRebind: false,
            out _));

        Assert.False(binding.MatchesVerifiedIdentity(
            new WorkerSessionIdentity
            {
                WorkerSessionId = "worker-b",
                SessionGeneration = 1,
                PortalProcessId = 4242,
                ProjectPath = "C:\\Projects\\Line.ap21"
            },
            out var mismatch));
        Assert.Contains("worker session identity changed", mismatch, StringComparison.OrdinalIgnoreCase);

        binding.Invalidate(mismatch!);

        Assert.Equal(ProjectBindingSnapshot.InvalidatedState, binding.BindingState);
        Assert.False(binding.TryGetVerified("C:\\Projects\\Line.ap21", out _, out var writeError));
        Assert.False(binding.TryResolve("C:\\Projects\\Line.ap21", out _, out var routeError));
        Assert.Contains("invalidated", writeError);
        Assert.Contains("invalidated", routeError);
    }

    [Fact]
    public void ReassertingSamePathDoesNotDiscardVerifiedWorkerIdentity()
    {
        var binding = new ProjectSessionBinding(null);
        Assert.True(binding.BindVerified(
            new WorkerSessionIdentity
            {
                WorkerSessionId = "worker-a",
                SessionGeneration = 3,
                PortalProcessId = 4242,
                ProjectPath = "C:\\Projects\\Line.ap21"
            },
            forceRebind: false,
            out _));
        var before = binding.CaptureSnapshot();

        Assert.True(binding.Bind("C:/Projects/Line.ap21", forceRebind: false, out var error));

        Assert.Null(error);
        var after = binding.CaptureSnapshot();
        Assert.True(after.IsVerified);
        Assert.Equal(before.BindingId, after.BindingId);
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(before.WorkerSessionId, after.WorkerSessionId);
    }

    [Fact]
    public void TryPromoteConfigured_AcceptsTheSameCompleteIdentityIdempotently()
    {
        var binding = new ProjectSessionBinding("C:\\Projects\\Line.ap21");
        var identity = new WorkerSessionIdentity
        {
            WorkerSessionId = "worker-a",
            SessionGeneration = 7,
            PortalProcessId = 4242,
            ProjectPath = "C:/Projects/Line.ap21"
        };

        Assert.True(binding.TryPromoteConfigured(identity, out var firstError));
        var afterFirstPromotion = binding.CaptureSnapshot();

        Assert.True(binding.TryPromoteConfigured(identity, out var secondError));
        var afterSecondPromotion = binding.CaptureSnapshot();

        Assert.Null(firstError);
        Assert.Null(secondError);
        Assert.True(afterSecondPromotion.IsVerified);
        Assert.True(afterFirstPromotion.SameBinding(afterSecondPromotion));
    }

    [Fact]
    public void TryInvalidate_WithAStaleSnapshot_DoesNotInvalidateANewerRebind()
    {
        var binding = new ProjectSessionBinding(null);
        Assert.True(binding.BindVerified(
            new WorkerSessionIdentity
            {
                WorkerSessionId = "worker-a",
                SessionGeneration = 1,
                PortalProcessId = 4242,
                ProjectPath = "C:\\Projects\\A.ap21"
            },
            forceRebind: false,
            out _));
        var stale = binding.CaptureSnapshot();

        Assert.True(binding.BindVerified(
            new WorkerSessionIdentity
            {
                WorkerSessionId = "worker-b",
                SessionGeneration = 1,
                PortalProcessId = 4343,
                ProjectPath = "C:\\Projects\\B.ap21"
            },
            forceRebind: true,
            out _));
        var rebound = binding.CaptureSnapshot();

        Assert.False(binding.TryInvalidate(stale, "late response from project A"));

        var afterLateResponse = binding.CaptureSnapshot();
        Assert.True(afterLateResponse.IsVerified);
        Assert.True(rebound.SameBinding(afterLateResponse));
        Assert.Equal("C:\\Projects\\B.ap21", afterLateResponse.ProjectPath);
        Assert.Equal("worker-b", afterLateResponse.WorkerSessionId);
    }
}
