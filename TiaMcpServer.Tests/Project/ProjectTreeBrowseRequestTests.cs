using TiaMcpServer.Contracts;
using TiaMcpServer.ProjectTree;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public class ProjectTreeBrowseRequestTests
{
    private static readonly string ProjectPath = ProjectPathNormalization.Canonicalize(@"C:\Projects\Sample.ap21")!;

    [Fact]
    public void ResolvePageSize_UsesDefaultAndAcceptsInclusiveBounds()
    {
        Assert.Equal(100, new ProjectTreeBrowseRequest().ResolvePageSize());
        Assert.Equal(1, new ProjectTreeBrowseRequest(PageSize: 1).ResolvePageSize());
        Assert.Equal(200, new ProjectTreeBrowseRequest(PageSize: 200).ResolvePageSize());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void ResolvePageSize_RejectsValuesOutsideTheInclusiveRange(int pageSize)
    {
        var error = Assert.Throws<ProjectTreeRequestException>(() =>
            new ProjectTreeBrowseRequest(PageSize: pageSize).ResolvePageSize());

        Assert.Equal(WorkerFailureCategories.ValidationError, error.Category);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_RejectsNonPositiveDepth(int depth)
    {
        var error = Assert.Throws<ProjectTreeRequestException>(() =>
            new ProjectTreeBrowseRequest(Depth: depth).Validate());

        Assert.Equal(WorkerFailureCategories.ValidationError, error.Category);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Validate_RejectsPresentButBlankCursor(string cursor)
    {
        var error = Assert.Throws<ProjectTreeRequestException>(() =>
            new ProjectTreeBrowseRequest(Cursor: cursor).Validate());

        Assert.Equal(WorkerFailureCategories.InvalidCursor, error.Category);
    }

    [Fact]
    public void ValidateRepeatedQuery_AllowsEquivalentPathAndSelectorNameCasing()
    {
        var query = new ProjectTreeQuery(ProjectPath, Selector(("Device", "PLC_1")), Depth: 2);

        var error = new ProjectTreeBrowseRequest(
            ProjectPath.ToLowerInvariant(),
            Selector(("Device", "plc_1")),
            Depth: 2).ValidateRepeatedQuery(query);

        Assert.Null(error);
    }

    [Fact]
    public void ValidateRepeatedQuery_RequiresExactNodeTypeAndDepth()
    {
        var query = new ProjectTreeQuery(ProjectPath, Selector(("Device", "PLC_1")), Depth: 2);

        var nodeTypeError = new ProjectTreeBrowseRequest(
            StartSelector: Selector(("device", "PLC_1"))).ValidateRepeatedQuery(query);
        var depthError = new ProjectTreeBrowseRequest(Depth: 3).ValidateRepeatedQuery(query);

        Assert.Equal(WorkerFailureCategories.CursorFilterMismatch, nodeTypeError);
        Assert.Equal(WorkerFailureCategories.CursorFilterMismatch, depthError);
    }

    [Fact]
    public void CreateQueryHash_ExcludesPageSizeButBindsQueryFields()
    {
        var query = new ProjectTreeQuery(ProjectPath, Selector(("Device", "PLC_1")), Depth: 2);

        var first = ProjectTreeBrowseRequest.CreateQueryHash(query);
        var second = ProjectTreeBrowseRequest.CreateQueryHash(query);
        var changedDepth = ProjectTreeBrowseRequest.CreateQueryHash(query with { Depth = 3 });

        Assert.Equal(first, second);
        Assert.NotEqual(first, changedDepth);
        Assert.Matches("^[0-9a-f]{64}$", first);
        Assert.Equal(
            first,
            ProjectTreeBrowseRequest.CreateQueryHash(new ProjectTreeQuery(
                ProjectPath,
                Selector(("Device", "PLC_1")),
                Depth: 2)));
    }

    private static IReadOnlyList<ProjectTreeSelectorSegment> Selector(params (string Type, string Name)[] segments)
        => segments.Select(segment => new ProjectTreeSelectorSegment
        {
            NodeType = segment.Type,
            Name = segment.Name,
        }).ToArray();
}
