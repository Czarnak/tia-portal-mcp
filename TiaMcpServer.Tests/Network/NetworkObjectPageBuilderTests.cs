using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Network;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class NetworkObjectPageBuilderTests
{
    [Theory]
    [InlineData(null, 100)]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(200, 200)]
    public void ResolvePageSize_UsesDefaultAndAcceptedExplicitBounds(int? requested, int expected)
    {
        Assert.Equal(expected, NetworkObjectPageBuilder.ResolvePageSize(requested));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void ResolvePageSize_RejectsValuesOutsideAcceptedBounds(int requested)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NetworkObjectPageBuilder.ResolvePageSize(requested));
    }

    [Fact]
    public void Build_DefaultInitialAndCursorOnlyPagesUseOneHundred()
    {
        var items = Enumerable.Range(0, 251)
            .Select(index => Item(NetworkObjectKinds.DeviceItem, $"item-{index:D3}"))
            .ToArray();
        var first = NetworkObjectPageBuilder.Build(
            items, NetworkObjectPageBuilder.ResolvePageSize(null), offset: 0, Hash('a'), Hash('b'));
        var firstContinuation = NetworkObjectCursorCodec.Decode(first.NextCursor!, Hash('a'), Hash('b'), items.Length);
        var second = NetworkObjectPageBuilder.Build(
            items, NetworkObjectPageBuilder.ResolvePageSize(null), firstContinuation.Offset, Hash('a'), Hash('b'));
        var secondContinuation = NetworkObjectCursorCodec.Decode(second.NextCursor!, Hash('a'), Hash('b'), items.Length);

        Assert.Equal(100, first.ReturnedCount);
        Assert.Equal(100, second.ReturnedCount);
        Assert.Equal(new[] { 0, 100, 200 }, new[] { 0, firstContinuation.Offset, secondContinuation.Offset });
        Assert.Equal(251, first.TotalCount);
        Assert.Equal(251, second.TotalCount);
        Assert.Equal(
            items.Take(200).Select(item => item.Evidence.Name),
            first.Items.Concat(second.Items).Select(item => item.Evidence.Name));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Build_MixedDefaultAndExplicitFiftyPagesCoverEveryItemOnce(bool startsWithDefault)
    {
        var items = Enumerable.Range(0, 351)
            .Select(index => Item(NetworkObjectKinds.DeviceItem, $"item-{index:D3}"))
            .ToArray();
        var requestedSizes = startsWithDefault
            ? new int?[] { null, 50, null, 50, null }
            : new int?[] { 50, null, 50, null, null };
        var expectedCounts = startsWithDefault
            ? new[] { 100, 50, 100, 50, 51 }
            : new[] { 50, 100, 50, 100, 51 };
        var counts = new List<int>();
        var returnedNames = new List<string?>();
        var offset = 0;
        string? cursor = null;

        foreach (var requestedSize in requestedSizes)
        {
            var page = NetworkObjectPageBuilder.Build(
                items, NetworkObjectPageBuilder.ResolvePageSize(requestedSize), offset, Hash('a'), Hash('b'));
            Assert.Equal(351, page.TotalCount);
            counts.Add(page.ReturnedCount);
            returnedNames.AddRange(page.Items.Select(item => item.Evidence.Name));
            cursor = page.NextCursor;
            if (cursor is not null)
            {
                offset = NetworkObjectCursorCodec.Decode(cursor, Hash('a'), Hash('b'), items.Length).Offset;
            }
        }

        Assert.Equal(expectedCounts, counts);
        Assert.Equal(items.Select(item => item.Evidence.Name), returnedNames);
        Assert.Equal(351, returnedNames.Distinct(StringComparer.Ordinal).Count());
        Assert.Null(cursor);
    }

    [Fact]
    public void Build_PreservesStableInputOrderAndReportsExactCounts()
    {
        var items = new[] { Item(NetworkObjectKinds.DeviceItem, "item"), Item(NetworkObjectKinds.Node, "node") };

        var page = NetworkObjectPageBuilder.Build(items, pageSize: 1, offset: 0, Hash('a'), Hash('b'));

        Assert.Equal(NetworkObjectKinds.DeviceItem, page.Items[0].Kind);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(1, page.ReturnedCount);
        Assert.NotNull(page.NextCursor);
    }

    [Fact]
    public void Build_LastPageHasNoCursor()
    {
        var page = NetworkObjectPageBuilder.Build(
            new[] { Item(NetworkObjectKinds.Subnet, "subnet") }, pageSize: 1, offset: 0, Hash('a'), Hash('b'));

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(1, page.ReturnedCount);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public void Build_CanBeginAfterUnselectableSummary()
    {
        var page = NetworkObjectPageBuilder.Build(
            new[]
            {
                new NetworkObjectSummaryInfo
                {
                    Kind = NetworkObjectKinds.Node,
                    Evidence = new NetworkObjectEvidenceInfo { Name = "unselectable" },
                    Diagnostics = new List<string> { "Node identity unavailable." },
                },
                Item(NetworkObjectKinds.Subnet, "selectable"),
            },
            pageSize: 1,
            offset: 1,
            Hash('a'),
            Hash('b'));

        Assert.Equal("selectable", page.Items[0].Evidence.Name);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(1, page.ReturnedCount);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public void Build_PagesCommunicationConnectionsInOwnerPathThenCompositionIndexOrder()
    {
        var items = new[]
        {
            Connection("CPU_1", ownerIndex: 0, connectionIndex: 0),
            Connection("CPU_1", ownerIndex: 0, connectionIndex: 1),
            Connection("CPU_2", ownerIndex: 1, connectionIndex: 0),
        };

        var page = NetworkObjectPageBuilder.Build(items, pageSize: 2, offset: 0, Hash('a'), Hash('b'));

        Assert.Equal(new int?[] { 0, 1 }, page.Items.Select(item => item.Selector!.ConnectionIndex));
        Assert.Equal(new[] { "CPU_1", "CPU_1" }, page.Items.Select(item => item.Selector!.ItemPath![0].Name));
        Assert.NotNull(page.NextCursor);
    }

    [Fact]
    public void Build_RejectsOffsetPastEnd()
    {
        var exception = Assert.Throws<NetworkCursorException>(() =>
            NetworkObjectPageBuilder.Build(Array.Empty<NetworkObjectSummaryInfo>(), 1, 1, Hash('a'), Hash('b')));

        Assert.Equal(WorkerFailureCategories.CursorOutOfRange, exception.Category);
    }

    private static NetworkObjectSummaryInfo Item(string kind, string name) => new()
    {
        Kind = kind,
        Selectable = true,
        Selector = new NetworkObjectSelectorInfo { Kind = kind },
        Evidence = new NetworkObjectEvidenceInfo { Name = name },
    };

    private static NetworkObjectSummaryInfo Connection(string ownerName, int ownerIndex, int connectionIndex) => new()
    {
        Kind = NetworkObjectKinds.CommunicationConnection,
        Selectable = true,
        Selector = new NetworkObjectSelectorInfo
        {
            Kind = NetworkObjectKinds.CommunicationConnection,
            DeviceName = "PLC_1",
            ItemPath = new List<DeviceItemPathSegmentInfo>
            {
                new()
                {
                    Index = ownerIndex,
                    Name = ownerName,
                    PositionNumber = ownerIndex,
                    TypeIdentifier = "OrderNumber:CPU",
                },
            },
            ConnectionIndex = connectionIndex,
            ConnectionType = "S7Connection",
            LocalConnectionName = "S7_Connection_1",
            LocalConnectionId = "1",
        },
        Evidence = new NetworkObjectEvidenceInfo
        {
            Name = "S7_Connection_1",
            TypeIdentifier = "S7Connection",
            ConnectionIsValid = true,
        },
    };

    private static string Hash(char value) => new(value, 64);
}
