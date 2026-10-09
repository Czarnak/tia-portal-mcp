using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Cursors;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.ProjectTree;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Project;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class ProjectTreeBrowseCoordinatorTests
{
    private static readonly DateTimeOffset Instant = new(2026, 9, 8, 8, 0, 0, TimeSpan.Zero);
    private static readonly string ProjectPath = ProjectPathNormalization.Canonicalize(@"C:\Projects\Tree.ap21")!;
    private static readonly ProjectBindingSnapshot DefaultUnbound = new ProjectSessionBinding(null).CaptureSnapshot();

    [Theory]
    [InlineData("id")]
    [InlineData("revision")]
    [InlineData("path")]
    [InlineData("unbind")]
    public async Task Cursor_AfterBindingChange_BindingMismatch(string change)
    {
        var binding = Bound();
        var clock = new ManualTimeProvider(Instant);
        var calls = 0;
        using var fixture = Fixture((path, selector, depth) =>
        {
            calls++;
            return Task.FromResult(Success(path, selector, depth, 3));
        }, clock, captureBinding: () => binding);
        var first = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(PageSize: 1));
        var cursor = NextCursor(first);
        var original = binding;
        clock.Advance(TimeSpan.FromMinutes(9));
        binding = change switch
        {
            "id" => Bound(id: "binding-b"),
            "revision" => Bound(revision: 2),
            "path" => Bound(path: @"C:\Projects\Other.ap21"),
            _ => Unbound(),
        };

        var failed = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(Cursor: cursor));

        AssertFailure(failed, WorkerFailureCategories.CursorBindingMismatch);
        Assert.Equal("The project binding changed after this project-tree cursor was issued; start again without a cursor.",
            failed.Response.Failure!.Message);
        Assert.Equal(1, calls);
        clock.Advance(TimeSpan.FromMinutes(2));
        // Binding mismatch wins even after the snapshot expires; it is checked before lookup.
        AssertFailure(await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(Cursor: cursor)),
            WorkerFailureCategories.CursorBindingMismatch);
        binding = original;
        AssertFailure(await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(Cursor: cursor)),
            WorkerFailureCategories.SnapshotUnavailable);
    }

    [Fact]
    public async Task BrowseProjection_IgnoresSkipped()
    {
        var skipped = new[]
        {
            new ProjectTreeSkippedNodeInfo
            {
                ParentPath = Selector((ProjectTreeNodeTypes.Device, "Node_0")).ToList(),
                NodeType = ProjectTreeNodeTypes.Block,
                Reason = "hidden"
            }
        };
        using var plain = Fixture((path, selector, depth) =>
            Task.FromResult(Success(path, selector, depth, 3)));
        using var withSkipped = Fixture((path, selector, depth) =>
            Task.FromResult(Success(path, selector, depth, 3, skipped: skipped)));

        var expected = await plain.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(PageSize: 5));
        var actual = await withSkipped.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(PageSize: 5));

        Assert.True(actual.IsSuccess, actual.CanonicalText);
        Assert.Equal(
            expected.Response.Result!.Nodes.Select(n => (n.Sequence, n.Name)).ToArray(),
            actual.Response.Result!.Nodes.Select(n => (n.Sequence, n.Name)).ToArray());
        Assert.DoesNotContain("hidden", actual.CanonicalText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cursor_SameBinding_Continues()
    {
        var binding = Bound();
        using var fixture = Fixture((path, selector, depth) =>
            Task.FromResult(Success(path, selector, depth, 3)), captureBinding: () => binding);
        var first = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(PageSize: 1));
        binding = Bound(path: ProjectPath.ToLowerInvariant());

        var next = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(Cursor: NextCursor(first)));

        Assert.True(next.IsSuccess, next.CanonicalText);
        Assert.Equal(new[] { 1, 2 }, Sequences(next));
        Assert.True(fixture.Codec.Decode(NextCursor(first)).HostBinding.Matches(binding));
    }

    [Fact]
    public async Task Cursor_Unbound_Continues()
    {
        var binding = new ProjectSessionBinding(null);
        using var fixture = Fixture((path, selector, depth) =>
            Task.FromResult(Success(path, selector, depth, 3)), captureBinding: binding.CaptureSnapshot);
        var first = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(PageSize: 1));

        var next = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(Cursor: NextCursor(first)));

        Assert.True(next.IsSuccess, next.CanonicalText);
        Assert.Equal(new[] { 1, 2 }, Sequences(next));
        Assert.Equal(new ProjectBindingCursorState(false, binding.CaptureSnapshot().BindingId, binding.CaptureSnapshot().Revision, null),
            fixture.Codec.Decode(NextCursor(first)).HostBinding);
    }

    [Fact]
    public async Task Cursor_UnboundThenBoundAndCleared_RejectsBeforeStoreAccessOrRenewal()
    {
        var binding = new ProjectSessionBinding(null);
        var before = binding.CaptureSnapshot();
        var clock = new ManualTimeProvider(Instant);
        var calls = 0;
        using var fixture = Fixture((path, selector, depth) =>
        {
            calls++;
            return Task.FromResult(Success(path, selector, depth, 3));
        }, clock, captureBinding: binding.CaptureSnapshot);
        var first = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(PageSize: 1));
        var cursor = NextCursor(first);
        var snapshotId = first.Response.Result!.Snapshot.SnapshotId;
        Assert.True(binding.BindVerified(new WorkerSessionIdentity
        {
            WorkerSessionId = "worker-b", PortalProcessId = 43, SessionGeneration = 1,
            ProjectPath = @"C:\Projects\B.ap21"
        }, false, out var error), error);
        Assert.True(binding.Clear(@"C:\Projects\B.ap21", out error), error);
        var after = binding.CaptureSnapshot();
        Assert.Equal(ProjectBindingSnapshot.UnboundState, after.State);
        Assert.NotEqual(before.BindingId, after.BindingId);
        Assert.True(after.Revision > before.Revision);
        clock.Advance(TimeSpan.FromMinutes(9));

        AssertFailure(await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(Cursor: cursor)),
            WorkerFailureCategories.CursorBindingMismatch);
        Assert.True(fixture.Store.Access(snapshotId, _ => true, _ => false).Found);
        Assert.Equal(1, calls);
        clock.Advance(TimeSpan.FromMinutes(2));
        AssertFailure(await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(Cursor: cursor)),
            WorkerFailureCategories.CursorBindingMismatch);
        // Expiry at the original ten-minute deadline proves the rejected replay did not renew TTL.
        Assert.False(fixture.Store.Access(snapshotId, _ => true, _ => false).Found);
        // A disposed store throws on Access, so this proves the mismatch bypasses lookup entirely.
        fixture.Store.Dispose();
        AssertFailure(await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(Cursor: cursor)),
            WorkerFailureCategories.CursorBindingMismatch);
    }

    [Fact]
    public async Task Cursor_ConfiguredBindingPromotedByInitialBrowse_Continues()
    {
        const string scenario = "project-tree-v3-small";
        var path = Path.Combine(Path.GetDirectoryName(FakeWorkerLocator.Locate())!, scenario);
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, path));
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath(scenario);
        var binding = new ProjectSessionBinding(path);
        using var worker = new OpennessWorkerClient(binding, logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(McpAccessMode.ReadOnly));
        using var fixture = Fixture(worker, scenario);
        Assert.Equal(ProjectBindingSnapshot.ConfiguredUnverifiedState, binding.BindingState);

        var first = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(PageSize: 1));

        Assert.True(first.IsSuccess, first.CanonicalText);
        Assert.Equal(ProjectBindingSnapshot.VerifiedState, binding.BindingState);
        Assert.True(fixture.Codec.Decode(NextCursor(first)).HostBinding.Matches(binding.CaptureSnapshot()));
        var next = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(Cursor: NextCursor(first)));
        Assert.True(next.IsSuccess, next.CanonicalText);
        Assert.Equal(new[] { 1, 2, 3 }, Sequences(next));
        Assert.DoesNotContain("hostBinding", first.CanonicalText);
        Assert.DoesNotContain("workerResult", first.CanonicalText);
    }

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
            Offset: 999, ProjectBindingCursorState.FromSnapshot(Unbound(), preserveUnboundEpoch: true)));

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
            Offset: 0, ProjectBindingCursorState.FromSnapshot(Unbound(), preserveUnboundEpoch: true)));
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
            1, ProjectBindingCursorState.FromSnapshot(Unbound(), preserveUnboundEpoch: true)));
        AssertFailure(
            await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(Cursor: foreign)),
            WorkerFailureCategories.SnapshotUnavailable);

        var mismatched = fixture.Codec.Encode(new ProjectTreeCursorState(
            result.Snapshot.SnapshotId,
            new string('a', 64),
            1, ProjectBindingCursorState.FromSnapshot(Unbound(), preserveUnboundEpoch: true)));
        AssertFailure(
            await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(Cursor: mismatched)),
            WorkerFailureCategories.CursorFilterMismatch);

        var outOfRange = fixture.Codec.Encode(new ProjectTreeCursorState(
            result.Snapshot.SnapshotId,
            ProjectTreeBrowseRequest.CreateQueryHash(result.Query),
            99, ProjectBindingCursorState.FromSnapshot(Unbound(), preserveUnboundEpoch: true)));
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

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidDepthIsRejectedBeforeWorkerObservation(int depth)
    {
        var calls = 0;
        using var fixture = Fixture((projectPath, startSelector, requestedDepth) =>
        {
            calls++;
            return Task.FromResult(Success(projectPath, startSelector, requestedDepth, 1));
        });

        var response = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(Depth: depth));

        AssertFailure(response, WorkerFailureCategories.ValidationError);
        Assert.Equal(0, calls);
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
            projectorMaxResponseChars: 1_100);
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
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath("project-tree-v3-one-shot");
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
        var block = Assert.Single(
            second.Response.Result!.Nodes,
            node => node.NodeType == ProjectTreeNodeTypes.Fb);
        Assert.Equal("Main", block.Name);
        Assert.Equal("Fake Vendor", block.Details["HeaderAuthor"]);
        Assert.Equal("2.3", block.Details["HeaderVersion"]);
        Assert.Equal("Motion", block.Details["HeaderFamily"]);
        Assert.Equal("Reusable main cycle", block.Details["HeaderName"]);
    }

    [Theory]
    [InlineData('x', 60_000, false)]
    [InlineData('\u0001', 10_000, false)]
    [InlineData('x', 60_000, true)]
    [InlineData('\u0001', 10_000, true)]
    public async Task OversizedWorkerDiagnosticsReturnBoundedNonEchoingCanonicalFailure(char character, int count, bool warning)
    {
        var diagnostic = "DIAGNOSTIC_MARKER" + new string(character, count);
        using var fixture = Fixture((_, _, _) => Task.FromResult(WorkerCallResult.Fail(
            WorkerFailureCategories.TargetNotFound,
            warning ? "Missing target." : diagnostic,
            warning ? new[] { diagnostic } : Array.Empty<string>())));

        var response = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest());

        Assert.True(response.CanonicalText.Length <= ProjectTreeContract.MaximumResponseChars);
        AssertFailure(response, WorkerFailureCategories.ResultMetadataTooLarge);
        Assert.Equal(CanonicalJson.Serialize(response.Response), response.CanonicalText);
        Assert.Empty(response.Response.Warnings);
        Assert.DoesNotContain("DIAGNOSTIC_MARKER", response.CanonicalText);
    }

    private static CoordinatorFixture Fixture(
        Func<string?, IReadOnlyList<ProjectTreeSelectorSegment>?, int?, Task<WorkerCallResult>> read,
        ManualTimeProvider? clock = null,
        ProjectTreeSnapshotStore? store = null,
        int projectorMaxResponseChars = ProjectTreeContract.MaximumResponseChars,
        Func<ProjectBindingSnapshot>? captureBinding = null)
    {
        clock ??= new ManualTimeProvider(Instant);
        var codec = Codec();
        store ??= new ProjectTreeSnapshotStore(clock);
        var projector = new ProjectTreePageProjector(codec, projectorMaxResponseChars);
        captureBinding ??= Unbound;
        return new CoordinatorFixture(
            new ProjectTreeBrowseCoordinator(codec, store, projector, clock,
                async (path, selector, depth) => new ProjectTreeSnapshotCallResult(
                    await read(path, selector, depth), captureBinding()), captureBinding),
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
        IReadOnlyDictionary<string, string>? details = null,
        IReadOnlyList<ProjectTreeSkippedNodeInfo>? skipped = null)
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
            Skipped = skipped?.ToList() ?? new List<ProjectTreeSkippedNodeInfo>(),
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

    private static ProjectBindingSnapshot Unbound()
        => DefaultUnbound;

    private static ProjectBindingSnapshot Bound(string id = "binding-a", long revision = 1, string? path = null)
        => new(ProjectBindingSnapshot.VerifiedState, id, revision, path ?? ProjectPath, "worker-a", 1, 123, null);

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
