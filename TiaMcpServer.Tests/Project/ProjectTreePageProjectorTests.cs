using TiaMcpServer.Contracts;
using TiaMcpServer.Cursors;
using TiaMcpServer.ProjectTree;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public sealed class ProjectTreePageProjectorTests
{
    [Fact]
    public void Project_ReturnsRequestedFullPageWhenItFits()
    {
        var page = CreateProjector(maxResponseChars: 2_000)
            .Project(SnapshotWithNodes(4, detailChars: 20), offset: 0, requestedPageSize: 3);

        Assert.True(page.IsSuccess, page.CanonicalText);
        Assert.True(page.HasNextPage);
        Assert.Equal(new[] { 0, 1, 2 }, page.Response.Result!.Nodes.Select(node => node.Sequence));
        Assert.Equal(3, page.Response.Result.Pagination.ReturnedCount);
        Assert.InRange(page.CanonicalText.Length, 1, 2_000);
    }

    [Fact]
    public void Project_TrimsOnlyTheTrailingSuffixAndAdvancesByReturnedCount()
    {
        var projector = CreateProjector(maxResponseChars: 1_350);
        var snapshot = SnapshotWithNodes(10, detailChars: 120);

        var page = projector.Project(snapshot, offset: 2, requestedPageSize: 5);

        Assert.True(page.IsSuccess);
        Assert.InRange(page.CanonicalText.Length, 1, 1_350);
        Assert.Equal(new[] { 2, 3 }, page.Response.Result!.Nodes.Select(node => node.Sequence));
        Assert.Equal(2, page.Response.Result.Pagination.ReturnedCount);
        Assert.Equal(4, Decode(page.Response.Result.Pagination.NextCursor!).Offset);
    }

    [Fact]
    public void Project_ContinuationAllowsAChangedRequestedPageSize()
    {
        var snapshot = SnapshotWithNodes(8, detailChars: 20);
        var projector = CreateProjector(maxResponseChars: 2_000);
        var first = projector.Project(snapshot, offset: 0, requestedPageSize: 2);

        var continuation = projector.Project(
            snapshot,
            offset: Decode(first.Response.Result!.Pagination.NextCursor!).Offset,
            requestedPageSize: 4);

        Assert.True(continuation.IsSuccess);
        Assert.Equal(new[] { 2, 3, 4, 5 }, continuation.Response.Result!.Nodes.Select(node => node.Sequence));
        Assert.Equal(4, continuation.Response.Result.Pagination.ReturnedCount);
    }

    [Fact]
    public void Project_EmptySnapshotReturnsOneEmptyTerminalSuccess()
    {
        var page = CreateProjector(maxResponseChars: 1_000)
            .Project(SnapshotWithNodes(0, detailChars: 0), offset: 0, requestedPageSize: 1);

        Assert.True(page.IsSuccess);
        Assert.False(page.HasNextPage);
        Assert.Empty(page.Response.Result!.Nodes);
        Assert.Null(page.Response.Result.Pagination.NextCursor);
        Assert.Equal(0, page.Response.Result.Pagination.ReturnedCount);
    }

    [Fact]
    public void Project_FinalPageHasNoCursor()
    {
        var page = CreateProjector(maxResponseChars: 2_000)
            .Project(SnapshotWithNodes(3, detailChars: 20), offset: 1, requestedPageSize: 5);

        Assert.True(page.IsSuccess);
        Assert.False(page.HasNextPage);
        Assert.Equal(new[] { 1, 2 }, page.Response.Result!.Nodes.Select(node => node.Sequence));
        Assert.Null(page.Response.Result.Pagination.NextCursor);
    }

    [Fact]
    public void Project_CanSplitParentAndChildAcrossPagesWithoutSplittingEitherNode()
    {
        var page = CreateProjector(maxResponseChars: 1_050)
            .Project(SnapshotWithParentAndChild(detailChars: 100), offset: 0, requestedPageSize: 2);

        Assert.True(page.IsSuccess);
        Assert.Single(page.Response.Result!.Nodes);
        Assert.Equal("parent", page.Response.Result.Nodes[0].NodeId);
        Assert.Equal(1, Decode(page.Response.Result.Pagination.NextCursor!).Offset);
    }

    [Fact]
    public void Project_RejectsOffsetsOutsideTheSnapshotRange()
    {
        var page = CreateProjector(maxResponseChars: 1_000)
            .Project(SnapshotWithNodes(2, detailChars: 0), offset: 2, requestedPageSize: 1);

        Assert.False(page.IsSuccess);
        Assert.Equal(WorkerFailureCategories.CursorOutOfRange, page.Response.Failure!.Category);
        Assert.Null(page.Response.Result);
        Assert.Empty(page.Response.Warnings);
    }

    [Fact]
    public void Project_MetadataOnlyOverflowTakesPrecedenceOverItemOverflow()
    {
        var page = CreateProjector(maxResponseChars: 250)
            .Project(SnapshotWithNodes(1, detailChars: 2_000), offset: 0, requestedPageSize: 1);

        Assert.False(page.IsSuccess);
        Assert.Equal(WorkerFailureCategories.ResultMetadataTooLarge, page.Response.Failure!.Category);
        Assert.DoesNotContain("sss", page.CanonicalText, StringComparison.Ordinal);
    }

    [Fact]
    public void OversizedNextNode_ReturnsItemFailureWithoutEchoingTheNode()
    {
        var marker = new string('s', 2_000);
        var projector = CreateProjector(maxResponseChars: 850);

        var page = projector.Project(SnapshotWithSingleNode(marker), 0, 1);

        Assert.False(page.IsSuccess);
        Assert.Equal(WorkerFailureCategories.ResultItemTooLarge, page.Response.Failure!.Category);
        Assert.DoesNotContain(marker, page.CanonicalText, StringComparison.Ordinal);
        Assert.True(page.CanonicalText.Length <= 850);
    }

    [Fact]
    public void Project_WarningsContributeToTheResponseBudget()
    {
        var projector = CreateProjector(maxResponseChars: 700);
        var withoutWarning = projector.Project(SnapshotWithNodes(1, detailChars: 20), 0, 1);
        var withWarning = projector.Project(
            SnapshotWithNodes(1, detailChars: 20, warnings: new[] { new string('w', 500) }),
            0,
            1);

        Assert.True(withoutWarning.IsSuccess);
        Assert.False(withWarning.IsSuccess);
        Assert.Equal(WorkerFailureCategories.ResultMetadataTooLarge, withWarning.Response.Failure!.Category);
    }

    [Fact]
    public void ProjectionUsesNoMoreThanTwoHundredExactSerializations()
    {
        var attempts = 0;
        var projector = CreateProjector(
            maxResponseChars: 900,
            observeSerialization: _ => attempts++);

        projector.Project(SnapshotWithNodes(500, detailChars: 300), 0, 200);

        Assert.InRange(attempts, 1, 200);
    }

    [Fact]
    public void Project_BoundsSerializationAttemptsPerRequestWhenProjectorIsReused()
    {
        var projector = CreateProjector(maxResponseChars: 900);
        var snapshot = SnapshotWithNodes(500, detailChars: 300);

        for (var request = 0; request < 25; request++)
        {
            var page = projector.Project(snapshot, offset: 0, requestedPageSize: 200);
            Assert.False(page.IsSuccess);
        }
    }

    private static ProjectTreePageProjector CreateProjector(
        int maxResponseChars,
        Action<int>? observeSerialization = null)
        => new(Codec(), maxResponseChars, observeSerialization);

    private static ProjectTreeCursorCodec Codec()
        => new(new AuthenticatedCursorProtector(new byte[32], "p"));

    private static ProjectTreeCursorState Decode(string cursor) => Codec().Decode(cursor);

    private static ProjectTreeSnapshotView SnapshotWithSingleNode(string marker)
        => SnapshotWithNodes(1, detailChars: 0, nodeDetails: new Dictionary<string, string>
        {
            ["marker"] = marker,
        });

    private static ProjectTreeSnapshotView SnapshotWithNodes(
        int count,
        int detailChars,
        IReadOnlyList<string>? warnings = null,
        IReadOnlyDictionary<string, string>? nodeDetails = null)
    {
        var details = nodeDetails ?? new Dictionary<string, string>
        {
            ["detail"] = new string('d', detailChars),
        };
        var nodes = Enumerable.Range(0, count)
            .Select(index => new ProjectTreeFlatNode(
                $"n{index}",
                ParentNodeId: null,
                Sequence: index,
                Name: $"n{index}",
                NodeType: "t",
                Details: details))
            .ToArray();
        return Snapshot(nodes, warnings ?? Array.Empty<string>());
    }

    private static ProjectTreeSnapshotView SnapshotWithParentAndChild(int detailChars)
        => Snapshot(
            new[]
            {
                new ProjectTreeFlatNode("parent", null, 0, "parent", "folder", new Dictionary<string, string>
                {
                    ["detail"] = new string('p', detailChars),
                }),
                new ProjectTreeFlatNode("child", "parent", 1, "child", "block", new Dictionary<string, string>
                {
                    ["detail"] = new string('c', detailChars),
                }),
                new ProjectTreeFlatNode("tail", null, 2, "tail", "folder", new Dictionary<string, string>
                {
                    ["detail"] = new string('t', detailChars),
                }),
            },
            Array.Empty<string>());

    private static ProjectTreeSnapshotView Snapshot(
        IReadOnlyList<ProjectTreeFlatNode> nodes,
        IReadOnlyList<string> warnings)
        => new(
            SnapshotId: "s",
            CreatedAt: new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero),
            IdleExpiresAt: new DateTimeOffset(2026, 9, 7, 12, 10, 0, TimeSpan.Zero),
            QueryHash: new string('a', 64),
            Content: new ProjectTreeSnapshotContent(
                new ProjectTreeQuery("p", StartSelector: null, Depth: null),
                nodes),
            Warnings: warnings,
            SerializedChars: 0,
            HostBinding: new ProjectBindingSnapshot(ProjectBindingSnapshot.UnboundState,
                "unbound", 0, null, null, null, null, null));
}
