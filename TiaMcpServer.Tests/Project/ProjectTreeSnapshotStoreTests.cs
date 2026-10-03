using TiaMcpServer.ProjectTree;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public class ProjectTreeSnapshotStoreTests
{
    private static readonly DateTimeOffset Instant = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ProjectInitial_OnePageSuccessIsNotRetained()
    {
        using var store = SmallStore();

        store.ProjectInitial(Candidate("one"), _ => new Projection(true, false), result => result.HasNextPage);

        Assert.False(store.Access("one", _ => new Projection(true, false), result => result.IsSuccess).Found);
    }

    [Fact]
    public void Access_SuccessfulReplayRenewsAndFinalPageRemainsCached()
    {
        var clock = new ManualTimeProvider(Instant);
        using var store = SmallStore(clock);
        store.ProjectInitial(Candidate("a"), _ => new Projection(true, true), result => result.HasNextPage);
        clock.Advance(TimeSpan.FromMinutes(9));

        var replay = store.Access("a", view => new Projection(view.IdleExpiresAt > Instant, false), result => result.IsSuccess);
        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.True(replay.Found);
        Assert.True(replay.Value!.IsSuccess);
        Assert.True(store.Access("a", _ => new Projection(true, false), result => result.IsSuccess).Found);
    }

    [Fact]
    public void FailedAccess_DoesNotRenewSlidingExpiry()
    {
        var clock = new ManualTimeProvider(Instant);
        using var store = SmallStore(clock);
        store.ProjectInitial(Candidate("a"), _ => new Projection(true, true), result => result.HasNextPage);
        clock.Advance(TimeSpan.FromMinutes(9));

        var access = store.Access("a", _ => new Projection(false, false), result => result.IsSuccess);
        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.True(access.Found);
        Assert.False(store.Access("a", _ => new Projection(true, false), result => result.IsSuccess).Found);
    }

    [Fact]
    public void ExpiryIsRemovedBeforeInsertionAndCountAndAggregateLimitsEvictLru()
    {
        var clock = new ManualTimeProvider(Instant);
        using var store = new ProjectTreeSnapshotStore(
            clock,
            maxSnapshots: 2,
            maxSnapshotChars: 100,
            maxAggregateChars: 150,
            slidingTtl: TimeSpan.FromMinutes(10));
        store.ProjectInitial(Candidate("a", 75), _ => new Projection(true, true), result => result.HasNextPage);
        clock.Advance(TimeSpan.FromSeconds(1));
        store.ProjectInitial(Candidate("b", 75), _ => new Projection(true, true), result => result.HasNextPage);
        clock.Advance(TimeSpan.FromSeconds(1));
        store.Access("a", _ => new Projection(true, false), result => result.IsSuccess);

        store.ProjectInitial(Candidate("c", 75), _ => new Projection(true, true), result => result.HasNextPage);

        Assert.True(store.Access("a", _ => new Projection(true, false), result => result.IsSuccess).Found);
        Assert.False(store.Access("b", _ => new Projection(true, false), result => result.IsSuccess).Found);
        Assert.True(store.Access("c", _ => new Projection(true, false), result => result.IsSuccess).Found);

        clock.Advance(TimeSpan.FromMinutes(10));
        store.ProjectInitial(Candidate("d", 75), _ => new Projection(true, true), result => result.HasNextPage);

        Assert.False(store.Access("a", _ => new Projection(true, false), result => result.IsSuccess).Found);
        Assert.False(store.Access("c", _ => new Projection(true, false), result => result.IsSuccess).Found);
    }

    [Fact]
    public void EvictionUsesCreationThenOrdinalSnapshotIdForLastAccessTies()
    {
        using var store = new ProjectTreeSnapshotStore(
            new ManualTimeProvider(Instant),
            maxSnapshots: 2,
            maxSnapshotChars: 100,
            maxAggregateChars: 200,
            slidingTtl: TimeSpan.FromMinutes(10));
        store.ProjectInitial(Candidate("b", 50), _ => new Projection(true, true), result => result.HasNextPage);
        store.ProjectInitial(Candidate("a", 50), _ => new Projection(true, true), result => result.HasNextPage);
        store.ProjectInitial(Candidate("c", 50), _ => new Projection(true, true), result => result.HasNextPage);

        Assert.False(store.Access("a", _ => new Projection(true, false), result => result.IsSuccess).Found);
        Assert.True(store.Access("b", _ => new Projection(true, false), result => result.IsSuccess).Found);
    }

    [Fact]
    public void ProjectInitial_RejectsAggregateOversizedCandidateWithoutEvictingExistingEntry()
    {
        using var store = new ProjectTreeSnapshotStore(
            new ManualTimeProvider(Instant),
            maxSnapshots: 4,
            maxSnapshotChars: 100,
            maxAggregateChars: 50,
            slidingTtl: TimeSpan.FromMinutes(10));
        store.ProjectInitial(Candidate("existing", 25), _ => new Projection(true, true), result => result.HasNextPage);

        var error = Assert.Throws<InvalidOperationException>(() => store.ProjectInitial(
            Candidate("too-large", 75), _ => new Projection(true, true), result => result.HasNextPage));

        Assert.Contains("aggregate", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(store.Access("existing", _ => new Projection(true, false), result => result.IsSuccess).Found);
    }

    [Fact]
    public async Task EntryCannotBeEvictedWhileItsPageIsProjected()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var store = new ProjectTreeSnapshotStore(
            new ManualTimeProvider(Instant), 1, 100, 100, TimeSpan.FromMinutes(10));
        store.ProjectInitial(Candidate("a", 100), _ => new Projection(true, true), result => result.HasNextPage);

        var access = Task.Run(() => store.Access("a", _ =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(5));
            return new Projection(true, false);
        }, result => result.IsSuccess));
        Task<Projection>? insertion = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            insertion = Task.Run(() => store.ProjectInitial(
                Candidate("b", 100), _ => new Projection(true, true), result => result.HasNextPage));

            Assert.NotSame(insertion, await Task.WhenAny(insertion, Task.Delay(TimeSpan.FromMilliseconds(100))));
        }
        finally
        {
            release.Set();
            await access.WaitAsync(TimeSpan.FromSeconds(5));
            if (insertion is not null)
            {
                await insertion.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    [Fact]
    public void Dispose_ClearsAndRejectsAllLaterOperations()
    {
        var store = SmallStore();
        store.ProjectInitial(Candidate("a"), _ => new Projection(true, true), result => result.HasNextPage);
        store.Dispose();

        Assert.Throws<ObjectDisposedException>(() => store.CreateSnapshotId());
        Assert.Throws<ObjectDisposedException>(() => store.ProjectInitial(
            Candidate("b"), _ => new Projection(true, true), result => result.HasNextPage));
        Assert.Throws<ObjectDisposedException>(() => store.Access(
            "a", _ => new Projection(true, false), result => result.IsSuccess));
    }

    [Fact]
    public void Dispose_RejectsInitialProjectionBeforeInspectingItsCandidate()
    {
        var store = SmallStore();
        store.Dispose();
        var invalid = Candidate("valid") with { SnapshotId = "" };

        Assert.Throws<ObjectDisposedException>(() => store.ProjectInitial(
            invalid, _ => new Projection(true, true), result => result.HasNextPage));
    }

    private static ProjectTreeSnapshotStore SmallStore(ManualTimeProvider? clock = null)
        => new(clock ?? new ManualTimeProvider(Instant), 4, 1_000, 4_000, TimeSpan.FromMinutes(10));

    private static ProjectTreeSnapshotCandidate Candidate(string snapshotId, int chars = 100)
        => new(
            snapshotId,
            Instant,
            new string('a', 64),
            new ProjectTreeSnapshotContent(
                new ProjectTreeQuery(@"C:\Projects\Sample.ap21", null, null),
                Array.Empty<ProjectTreeFlatNode>()),
            Array.Empty<string>(),
            chars,
            new ProjectBindingSnapshot(ProjectBindingSnapshot.UnboundState,
                "unbound", 0, null, null, null, null, null));

    private sealed record Projection(bool IsSuccess, bool HasNextPage);

    private sealed class ManualTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
