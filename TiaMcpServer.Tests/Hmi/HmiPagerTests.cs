using TiaMcpServer.OpennessWorker.Openness.Hmi;
using Xunit;

namespace TiaMcpServer.Tests.Hmi;

public class HmiPagerTests
{
    [Fact]
    public void CaseOnlyDifferentNamesKeepStableOrderAcrossPages()
    {
        var shuffled = new[] { "b", "a", "B", "A" };
        var ordered = new List<string>();
        int? offset = 0;

        while (offset is { } o)
        {
            var (items, page) = HmiPager.Page(shuffled, n => n, o, limit: 1);
            ordered.AddRange(items);
            offset = page.NextOffset;
        }

        Assert.Equal(new[] { "A", "a", "B", "b" }, ordered);
    }

    [Fact]
    public void OffsetPastTotalIsEmptyPageWithNullNextOffset()
    {
        var (items, page) = HmiPager.Page(new[] { "a", "b", "c" }, n => n, offset: 10, limit: 5);

        Assert.Empty(items);
        Assert.Equal(10, page.Offset);
        Assert.Equal(5, page.Limit);
        Assert.Equal(3, page.Total);
        Assert.Null(page.NextOffset);
    }

    [Fact]
    public void LastPageHasNullNextOffset()
    {
        var names = new[] { "a", "b", "c" };

        var first = HmiPager.Page(names, n => n, 0, 2);
        var last = HmiPager.Page(names, n => n, 2, 2);

        Assert.Equal(2, first.Page.NextOffset);
        Assert.Equal(new[] { "c" }, last.Items);
        Assert.Null(last.Page.NextOffset);
    }
}
