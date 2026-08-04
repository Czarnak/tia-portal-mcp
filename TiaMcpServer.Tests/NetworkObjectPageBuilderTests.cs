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
                new NetworkObjectSummaryInfo { Kind = NetworkObjectKinds.Node, DisplayName = "unselectable" },
                Item(NetworkObjectKinds.Subnet, "selectable"),
            },
            pageSize: 1,
            offset: 1,
            Hash('a'),
            Hash('b'));

        Assert.Equal("selectable", page.Items[0].DisplayName);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(1, page.ReturnedCount);
        Assert.Null(page.NextCursor);
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
        DisplayName = name,
        Selector = new NetworkObjectSelectorInfo { Kind = kind },
    };

    private static string Hash(char value) => new(value, 64);
}
