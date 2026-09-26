using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Block;

public class CompileReportProjectionTests
{
    [Fact]
    public void Flatten_PreservesTopLevelFields()
    {
        // Catches field loss at the extracted projection seam independently of Siemens access.
        var projection = Flatten(new[] { new Message("Missing tag", "PLC/Blocks/Main", "Error") });

        var row = Assert.Single(projection.Messages);
        Assert.Equal("Missing tag", row.Description);
        Assert.Equal("PLC/Blocks/Main", row.Path);
        Assert.Equal("Error", row.Severity);
        Assert.False(projection.WasTruncated);
    }

    [Fact]
    public void Flatten_VisitsBlankParentBeforeNestedErrorAndWarning()
    {
        var root = new Message("", "PLC/Blocks", "Information");
        var error = new Message("Missing tag", "PLC/Blocks/Main", "Error");
        error.Children.Add(new Message("Conversion", "PLC/Blocks/Helper", "Warning"));
        root.Children.Add(error);

        var projection = Flatten(new[] { root });

        Assert.Equal(new[] { "", "Missing tag", "Conversion" }, projection.Messages.Select(m => m.Description));
        Assert.Equal(new[] { "Information", "Error", "Warning" }, projection.Messages.Select(m => m.Severity));
        Assert.Equal(new[] { "PLC/Blocks", "PLC/Blocks/Main", "PLC/Blocks/Helper" }, projection.Messages.Select(m => m.Path));
        Assert.False(projection.WasTruncated);
    }

    [Theory]
    [InlineData(16, 16, false)]
    [InlineData(17, 16, true)]
    public void Flatten_BoundsDepth(int depth, int expectedRows, bool truncated)
    {
        var root = new Message("1", "", "Information");
        var current = root;
        for (var level = 2; level <= depth; level++)
        {
            var child = new Message(level.ToString(), "", "Information");
            current.Children.Add(child);
            current = child;
        }

        var projection = Flatten(new[] { root });

        Assert.Equal(expectedRows, projection.Messages.Count);
        Assert.Equal("16", projection.Messages.Last().Description);
        Assert.Equal(truncated, projection.WasTruncated);
    }

    [Theory]
    [InlineData(200, false)]
    [InlineData(201, true)]
    public void Flatten_BoundsWideMessageTrees(int count, bool truncated)
    {
        var projection = Flatten(Enumerable.Range(0, count).Select(i => new Message(i.ToString(), "", "Information")));

        Assert.Equal(200, projection.Messages.Count);
        Assert.Equal("199", projection.Messages.Last().Description);
        Assert.Equal(truncated, projection.WasTruncated);
    }

    [Theory]
    [InlineData(1024, false)]
    [InlineData(1025, true)]
    public void Flatten_BoundsEachField(int length, bool truncated)
    {
        var projection = Flatten(new[] { new Message(new string('d', length), new string('p', length), "Error") });

        var row = Assert.Single(projection.Messages);
        Assert.Equal(new string('d', 1024), row.Description);
        Assert.Equal(new string('p', 1024), row.Path);
        Assert.Equal(truncated, projection.WasTruncated);
    }

    [Fact]
    public void Flatten_SharesCharacterBudgetAcrossPlcs()
    {
        var budget = new CompileReportProjection.Budget();
        var roots = Enumerable.Range(0, 20).Select(_ => new Message(new string('d', 1000), "", "Error"));

        var first = Flatten(roots, budget);
        var second = Flatten(roots, budget);

        Assert.Equal(20, first.Messages.Count);
        Assert.Equal(12, second.Messages.Count);
        Assert.False(first.WasTruncated);
        Assert.True(second.WasTruncated);
        Assert.Equal(32000, first.Messages.Concat(second.Messages).Sum(m => m.Description.Length + m.Path.Length));
    }

    [Fact]
    public void Flatten_SharesMessageBudgetAcrossPlcs()
    {
        var budget = new CompileReportProjection.Budget();
        var roots = Enumerable.Range(0, 150).Select(_ => new Message("d", "p", "Error"));

        var first = Flatten(roots, budget);
        var second = Flatten(roots, budget);

        Assert.Equal(150, first.Messages.Count);
        Assert.Equal(50, second.Messages.Count);
        Assert.False(first.WasTruncated);
        Assert.True(second.WasTruncated);
    }

    [Fact]
    public void Flatten_SanitizesFailedPropertyReadAndRetainsChildren()
    {
        var parent = new Message("parent", "", "Information");
        parent.Children.Add(new Message("child", "child-path", "Error"));

        var projection = CompileReportProjection.Flatten(new[] { parent }, m => m.Description,
            m => m == parent ? throw new InvalidOperationException("private-path-secret") : m.Path,
            m => m.Severity, m => m.Children, new CompileReportProjection.Budget());

        Assert.True(projection.WasTruncated);
        Assert.Equal(new[] { "parent", "child" }, projection.Messages.Select(m => m.Description));
        Assert.Equal(new[] { "", "child-path" }, projection.Messages.Select(m => m.Path));
    }

    [Fact]
    public void Flatten_TruncationDoesNotSplitSurrogatePairs()
    {
        var projection = Flatten(new[] { new Message(new string('d', 1023) + "\U0001F600", "", "Error") });

        var row = Assert.Single(projection.Messages);
        Assert.Equal(new string('d', 1023), row.Description);
        Assert.True(projection.WasTruncated);
    }

    [Fact]
    public void Flatten_StopsAfterTheBudgetWithoutEnumeratingTheWholeSource()
    {
        var readCount = 0;
        IEnumerable<Message> Messages()
        {
            while (readCount < 1000)
            {
                readCount++;
                yield return new Message("d", "p", "Information");
            }
        }

        var projection = Flatten(Messages());

        Assert.Equal(200, projection.Messages.Count);
        Assert.Equal(201, readCount);
        Assert.True(projection.WasTruncated);
    }

    [Fact]
    public void Flatten_PreservesRowsWhenEnumerationFails()
    {
        IEnumerable<Message> Messages()
        {
            yield return new Message("retained", "path", "Error");
            throw new InvalidOperationException("private-project-path");
        }

        var projection = Flatten(Messages());

        Assert.Equal("retained", Assert.Single(projection.Messages).Description);
        Assert.True(projection.WasTruncated);
    }

    [Fact]
    public void Flatten_EmptyPlcAfterBudgetExhaustionIsNotMarkedTruncated()
    {
        var budget = new CompileReportProjection.Budget();
        Flatten(Enumerable.Range(0, 200).Select(_ => new Message("d", "p", "Information")), budget);

        var projection = Flatten(Array.Empty<Message>(), budget);

        Assert.Empty(projection.Messages);
        Assert.False(projection.WasTruncated);
    }

    private static CompileReportProjection.Projection Flatten(IEnumerable<Message> roots,
        CompileReportProjection.Budget? budget = null) =>
        CompileReportProjection.Flatten(roots, m => m.Description, m => m.Path,
            m => m.Severity, m => m.Children, budget ?? new CompileReportProjection.Budget());

    private sealed record Message(string Description, string Path, string Severity)
    {
        public List<Message> Children { get; } = new();
    }
}
