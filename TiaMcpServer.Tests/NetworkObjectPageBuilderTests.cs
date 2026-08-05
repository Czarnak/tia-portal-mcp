using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using Xunit;

namespace TiaMcpServer.Tests;

public class NetworkObjectPageBuilderTests
{
    [Theory]
    [InlineData(null, 50)]
    [InlineData(1, 1)]
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
