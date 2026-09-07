using TiaMcpServer.Contracts;
using TiaMcpServer.Cursors;
using TiaMcpServer.Json;
using TiaMcpServer.ProjectTree;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public sealed class ProjectTreeBrowseCoordinatorTests
{
    private static readonly DateTimeOffset Instant = new(2026, 9, 8, 8, 0, 0, TimeSpan.Zero);
    private static readonly string ProjectPath = ProjectPathNormalization.Canonicalize(@"C:\Projects\Tree.ap21")!;

    [Fact]
    public async Task InitialRequestObservesOnceAndAllContinuationPagesUseTheCachedSnapshot()
    {
        var calls = 0;
        using var fixture = Fixture(async (projectPath, startSelector, depth) =>
        {
            calls++;
            await Task.Yield();
            return Success(projectPath, startSelector, depth, 5);
        });

        var first = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(PageSize: 2));
        var second = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(
            PageSize: 2,
            Cursor: NextCursor(first)));
        var third = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(
            PageSize: 2,
            Cursor: NextCursor(second)));
        var replay = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(
            PageSize: 2,
            Cursor: NextCursor(first)));

        Assert.Equal(1, calls);
        Assert.Equal(new[] { 0, 1 }, Sequences(first));
        Assert.Equal(new[] { 2, 3 }, Sequences(second));
        Assert.Equal(new[] { 4 }, Sequences(third));
        Assert.Equal(new[] { 2, 3 }, Sequences(replay));
    }

    [Fact]
    public async Task IdenticalConcurrentCursorFreeRequestsAlwaysObserveAfresh()
    {
        var calls = 0;
        using var fixture = Fixture(async (projectPath, startSelector, depth) =>
        {
            Interlocked.Increment(ref calls);
            await Task.Yield();
            return Success(projectPath, startSelector, depth, 2);
        });

        await Task.WhenAll(
            fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(PageSize: 1)),
            fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(PageSize: 1)));

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task DefaultPageSizeAppliesIndependentlyToInitialAndContinuationRequests()
    {
        using var fixture = Fixture((projectPath, startSelector, depth) =>
            Task.FromResult(Success(projectPath, startSelector, depth, 250)));

        var first = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest());
        var second = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(Cursor: NextCursor(first)));

        Assert.Equal(100, first.Response.Result!.Pagination.RequestedPageSize);
        Assert.Equal(100, first.Response.Result.Pagination.ReturnedCount);
        Assert.Equal(100, second.Response.Result!.Pagination.RequestedPageSize);
        Assert.Equal(100, second.Response.Result.Pagination.ReturnedCount);
        Assert.Equal(100, second.Response.Result.Pagination.Offset);
    }

    [Fact]
    public async Task ContinuationAllowsChangedPageSizeAndEquivalentRepeatedQuery()
    {
        var selector = Selector((ProjectTreeNodeTypes.Device, "PLC_1"));
        using var fixture = Fixture((projectPath, startSelector, depth) =>
            Task.FromResult(Success(projectPath, startSelector, depth, 6)));
        var first = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(
            ProjectPath,
            selector,
            Depth: 2,
            PageSize: 2));

        var second = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(
            ProjectPath.ToLowerInvariant(),
            Selector((ProjectTreeNodeTypes.Device, "plc_1")),
            Depth: 2,
            PageSize: 3,
            Cursor: NextCursor(first)));

        Assert.True(second.IsSuccess);
        Assert.Equal(new[] { 2, 3, 4 }, Sequences(second));
        Assert.Equal(3, second.Response.Result!.Pagination.RequestedPageSize);
    }

    [Fact]
    public async Task RepeatedQueryMismatchIsRejectedBeforeRangeProjection()
    {
        using var fixture = Fixture((projectPath, startSelector, depth) =>
            Task.FromResult(Success(projectPath, startSelector, depth, 4)));
        var first = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(Depth: 2, PageSize: 1));
        var result = first.Response.Result!;
        var outOfRange = fixture.Codec.Encode(new ProjectTreeCursorState(
            result.Snapshot.SnapshotId,
            ProjectTreeBrowseRequest.CreateQueryHash(result.Query),
            Offset: 999));

        var mismatch = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(
            Depth: 3,
            Cursor: outOfRange));

        AssertFailure(mismatch, WorkerFailureCategories.CursorFilterMismatch);
    }

    [Fact]
    public async Task OnePageResultIsNotRetainedButFinalMultiPageResultRemainsReplayable()
    {
        using var fixture = Fixture((projectPath, startSelector, depth) => Task.FromResult(
            Success(projectPath, startSelector, depth, projectPath == "one" ? 1 : 3)));
        var one = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest("one", PageSize: 2));
        var oneResult = one.Response.Result!;
        var syntheticOneCursor = fixture.Codec.Encode(new ProjectTreeCursorState(
            oneResult.Snapshot.SnapshotId,
            ProjectTreeBrowseRequest.CreateQueryHash(oneResult.Query),
            Offset: 0));
        AssertFailure(
            await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(Cursor: syntheticOneCursor)),
            WorkerFailureCategories.SnapshotUnavailable);

        var first = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest("many", PageSize: 2));
        var finalCursor = NextCursor(first);
        var final = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(Cursor: finalCursor));
        var replay = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(Cursor: finalCursor));

        Assert.Equal(new[] { 2 }, Sequences(final));
        Assert.Equal(new[] { 2 }, Sequences(replay));
    }

    [Fact]
    public async Task MalformedForeignMismatchedAndOutOfRangeCursorsMapToClosedFailures()
    {
        using var fixture = Fixture((projectPath, startSelector, depth) =>
            Task.FromResult(Success(projectPath, startSelector, depth, 3)));
        var first = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(PageSize: 1));
        var result = first.Response.Result!;

        AssertFailure(
            await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(Cursor: "not-a-cursor")),
            WorkerFailureCategories.InvalidCursor);

        var foreign = Codec(0x80).Encode(new ProjectTreeCursorState(
            result.Snapshot.SnapshotId,
            ProjectTreeBrowseRequest.CreateQueryHash(result.Query),
            1));
        AssertFailure(
            await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(Cursor: foreign)),
            WorkerFailureCategories.SnapshotUnavailable);

        var mismatched = fixture.Codec.Encode(new ProjectTreeCursorState(
            result.Snapshot.SnapshotId,
            new string('a', 64),
            1));
        AssertFailure(
            await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(Cursor: mismatched)),
            WorkerFailureCategories.CursorFilterMismatch);

        var outOfRange = fixture.Codec.Encode(new ProjectTreeCursorState(
            result.Snapshot.SnapshotId,
            ProjectTreeBrowseRequest.CreateQueryHash(result.Query),
            99));
        AssertFailure(
            await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(Cursor: outOfRange)),
            WorkerFailureCategories.CursorOutOfRange);
    }

    [Fact]
    public async Task ExpiredAndEvictedSnapshotsAreUnavailable()
    {
        var clock = new ManualTimeProvider(Instant);
        using var store = new ProjectTreeSnapshotStore(
            clock,
            maxSnapshots: 1,
            maxSnapshotChars: 4_000_000,
            maxAggregateChars: 4_000_000,
            slidingTtl: TimeSpan.FromMinutes(10));
        using var fixture = Fixture((projectPath, startSelector, depth) =>
            Task.FromResult(Success(projectPath, startSelector, depth, 2)), clock, store);

        var expiring = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest("expires", PageSize: 1));
        clock.Advance(TimeSpan.FromMinutes(11));
        AssertFailure(
            await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(Cursor: NextCursor(expiring))),
            WorkerFailureCategories.SnapshotUnavailable);

        var evicted = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest("evicted", PageSize: 1));
        await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest("replacement", PageSize: 1));
        AssertFailure(
            await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(Cursor: NextCursor(evicted))),
            WorkerFailureCategories.SnapshotUnavailable);
    }

    [Fact]
    public async Task SelectionWorkerAndProtocolFailuresAreMappedWithoutCallingOrEchoingUnsafeData()
    {
        var calls = 0;
        using var fixture = Fixture((projectPath, startSelector, depth) =>
        {
            calls++;
            return Task.FromResult(projectPath switch
            {
                "worker" => WorkerCallResult.Fail(
                    WorkerFailureCategories.TargetNotFound,
                    "Target not found.",
                    new[] { "worker warning" }),
                "protocol" => WorkerCallResult.Ok("{\"PROJECT_TREE_SECRET_MARKER\":true}") with
                {
                    ResolvedProjectPath = ProjectPath,
                },
                _ => Success(projectPath, startSelector, depth, 1),
            });
        });

        var invalidSelector = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(
            StartSelector: Selector(("Unknown", "unsafe-name"))));
        AssertFailure(invalidSelector, WorkerFailureCategories.InvalidSelector);
        Assert.Equal(0, calls);

        var worker = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest("worker"));
        AssertFailure(worker, WorkerFailureCategories.TargetNotFound);
        Assert.Equal(new[] { "worker warning" }, worker.Response.Warnings);

        var protocol = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest("protocol"));
        AssertFailure(protocol, WorkerFailureCategories.ProtocolError);
        Assert.DoesNotContain("PROJECT_TREE_SECRET_MARKER", protocol.CanonicalText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OversizedSnapshotAndPageBudgetsMapToClosedFailures()
    {
        using var snapshotFixture = Fixture((projectPath, startSelector, depth) => Task.FromResult(Success(
            projectPath,
            startSelector,
            depth,
            1,
            new Dictionary<string, string> { ["large"] = new string('x', 4_000_000) })));
        AssertFailure(
            await snapshotFixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest()),
            WorkerFailureCategories.SnapshotTooLarge);

        using var itemFixture = Fixture(
            (projectPath, startSelector, depth) => Task.FromResult(Success(
                projectPath,
                startSelector,
                depth,
                1,
                new Dictionary<string, string> { ["large"] = new string('x', 2_000) })),
            projectorMaxResponseChars: 800);
        AssertFailure(
            await itemFixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest()),
            WorkerFailureCategories.ResultItemTooLarge);

        using var metadataFixture = Fixture(
            (projectPath, startSelector, depth) => Task.FromResult(
                Success(projectPath, startSelector, depth, 1)),
            projectorMaxResponseChars: 100);
        AssertFailure(
            await metadataFixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest()),
            WorkerFailureCategories.ResultMetadataTooLarge);
    }

    [Fact]
    public async Task ContinuationUsesCachedSnapshotAfterPersistentWorkerIsDisposed()
    {
        using var worker = new OpennessWorkerClient(
            new ProjectSessionBinding(null),
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());
        using var fixture = Fixture(worker, scenario: "project-tree-v3-one-shot");

        var first = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(
            "project-tree-v3-one-shot",
            PageSize: 2));
        Assert.True(first.IsSuccess, first.CanonicalText);
        worker.Dispose();
        var second = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(Cursor: NextCursor(first)));

        Assert.True(second.IsSuccess);
        Assert.Equal(new[] { 2, 3 }, Sequences(second));
    }

    private static CoordinatorFixture Fixture(
        Func<string?, IReadOnlyList<ProjectTreeSelectorSegment>?, int?, Task<WorkerCallResult>> read,
        ManualTimeProvider? clock = null,
        ProjectTreeSnapshotStore? store = null,
        int projectorMaxResponseChars = ProjectTreeContract.MaximumResponseChars)
    {
        clock ??= new ManualTimeProvider(Instant);
        var codec = Codec();
        store ??= new ProjectTreeSnapshotStore(clock);
        var projector = new ProjectTreePageProjector(codec, projectorMaxResponseChars);
        return new CoordinatorFixture(
            new ProjectTreeBrowseCoordinator(codec, store, projector, clock, read),
            codec,
            store,
            ownsStore: true);
    }

    private static CoordinatorFixture Fixture(OpennessWorkerClient worker, string scenario)
    {
        var clock = new ManualTimeProvider(Instant);
        var codec = Codec();
        var store = new ProjectTreeSnapshotStore(clock);
        return new CoordinatorFixture(
            new ProjectTreeBrowseCoordinator(worker, codec, store, new ProjectTreePageProjector(codec), clock),
            codec,
            store,
            ownsStore: true);
    }

    private static WorkerCallResult Success(
        string? requestedProjectPath,
        IReadOnlyList<ProjectTreeSelectorSegment>? requestedSelector,
        int? requestedDepth,
        int nodeCount,
        IReadOnlyDictionary<string, string>? details = null)
    {
        var path = ProjectPathNormalization.Canonicalize(requestedProjectPath) ?? ProjectPath;
        var payload = new ProjectTreeBrowseResultInfo
        {
            StartSelector = requestedSelector?.Select(segment => new ProjectTreeSelectorSegment
            {
                NodeType = segment.NodeType,
                Name = segment.Name,
            }).ToList(),
            Depth = requestedDepth,
            Roots = Enumerable.Range(0, nodeCount).Select(index => new ProjectTreeNode
            {
                Name = $"Node_{index}",
                NodeType = ProjectTreeNodeTypes.Device,
                Details = details is null ? null : new Dictionary<string, string>(details),
                Children = new List<ProjectTreeNode>(),
            }).ToList(),
        };
        return WorkerCallResult.Ok(CanonicalJson.Serialize(payload)) with { ResolvedProjectPath = path };
    }

    private static IReadOnlyList<ProjectTreeSelectorSegment> Selector(
        params (string NodeType, string Name)[] segments)
        => segments.Select(segment => new ProjectTreeSelectorSegment
        {
            NodeType = segment.NodeType,
            Name = segment.Name,
        }).ToArray();

    private static ProjectTreeCursorCodec Codec(byte start = 0)
        => new(new AuthenticatedCursorProtector(
            Enumerable.Range(start, 32).Select(value => (byte)value).ToArray(),
            $"project-tree-test-{start}"));

    private static string NextCursor(ProjectTreeRenderedResponse response)
        => response.Response.Result!.Pagination.NextCursor!;

    private static int[] Sequences(ProjectTreeRenderedResponse response)
        => response.Response.Result!.Nodes.Select(node => node.Sequence).ToArray();

    private static void AssertFailure(ProjectTreeRenderedResponse response, string category)
    {
        Assert.False(response.IsSuccess);
        Assert.Equal(ProjectTreeStatuses.Failed, response.Response.Status);
        Assert.Null(response.Response.Result);
        Assert.Equal(category, response.Response.Failure!.Category);
    }

    private sealed class CoordinatorFixture : IDisposable
    {
        private readonly bool _ownsStore;

        internal CoordinatorFixture(
            ProjectTreeBrowseCoordinator coordinator,
            ProjectTreeCursorCodec codec,
            ProjectTreeSnapshotStore store,
            bool ownsStore)
        {
            Coordinator = coordinator;
            Codec = codec;
            Store = store;
            _ownsStore = ownsStore;
        }

        internal ProjectTreeBrowseCoordinator Coordinator { get; }
        internal ProjectTreeCursorCodec Codec { get; }
        internal ProjectTreeSnapshotStore Store { get; }

        public void Dispose()
        {
            if (_ownsStore)
            {
                Store.Dispose();
            }
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;

        public override DateTimeOffset GetUtcNow() => _now;

        internal void Advance(TimeSpan duration) => _now += duration;
    }
}
