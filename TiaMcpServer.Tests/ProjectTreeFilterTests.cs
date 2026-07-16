using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests;

public class ProjectTreeFilterTests
{
    private static ProjectTreeNode Node(string name, string path, params ProjectTreeNode[] children)
        => new()
        {
            Name = name,
            NodeType = "Folder",
            Details = new Dictionary<string, string> { ["Path"] = path },
            Children = children.Length == 0 ? new List<ProjectTreeNode>() : new List<ProjectTreeNode>(children)
        };

    private static List<ProjectTreeNode> SampleTree()
        => new()
        {
            Node("PLC_1", "PLC_1",
                Node("Blocks", "PLC_1/Blocks",
                    Node("Main", "PLC_1/Blocks/Main"),
                    Node("Motors", "PLC_1/Blocks/Motors",
                        Node("Motor_1", "PLC_1/Blocks/Motors/Motor_1"))),
                Node("TagTables", "PLC_1/TagTables",
                    Node("Default", "PLC_1/TagTables/Default")))
        };

    [Fact]
    public void NoFilters_ReturnsTreeUnchanged()
    {
        var tree = SampleTree();

        var result = ProjectTreeFilter.Apply(tree, startPath: null, depth: null);

        Assert.Same(tree, result);
    }

    [Fact]
    public void StartPath_SelectsTheMatchingSubtree()
    {
        var result = ProjectTreeFilter.Apply(SampleTree(), startPath: "PLC_1/Blocks", depth: null);

        var root = Assert.Single(result);
        Assert.Equal("Blocks", root.Name);
        Assert.Equal(2, root.Children!.Count);
    }

    [Fact]
    public void StartPath_IsCaseInsensitive()
    {
        var result = ProjectTreeFilter.Apply(SampleTree(), startPath: "plc_1/blocks/motors", depth: null);

        Assert.Equal("Motors", Assert.Single(result).Name);
    }

    [Fact]
    public void UnknownStartPath_ThrowsWithRecoveryHint()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ProjectTreeFilter.Apply(SampleTree(), startPath: "PLC_9/Nope", depth: null));

        Assert.Contains("PLC_9/Nope", ex.Message);
        Assert.Contains("browse_project_tree", ex.Message);
    }

    [Fact]
    public void Depth1_ReturnsRootsWithChildrenOmittedMarker()
    {
        var result = ProjectTreeFilter.Apply(SampleTree(), startPath: null, depth: 1);

        var root = Assert.Single(result);
        Assert.Empty(root.Children!);
        Assert.Equal("2", root.Details!["ChildrenOmitted"]);
    }

    [Fact]
    public void Depth2_KeepsOneLevelOfChildren()
    {
        var result = ProjectTreeFilter.Apply(SampleTree(), startPath: null, depth: 2);

        var root = Assert.Single(result);
        Assert.Equal(2, root.Children!.Count);
        var blocks = root.Children![0];
        Assert.Empty(blocks.Children!);
        Assert.Equal("2", blocks.Details!["ChildrenOmitted"]);
    }

    [Fact]
    public void Depth_DoesNotMutateTheInputTree()
    {
        var tree = SampleTree();

        ProjectTreeFilter.Apply(tree, startPath: null, depth: 1);

        Assert.Equal(2, tree[0].Children!.Count);
        Assert.False(tree[0].Details!.ContainsKey("ChildrenOmitted"));
    }

    [Fact]
    public void StartPathAndDepth_Compose()
    {
        var result = ProjectTreeFilter.Apply(SampleTree(), startPath: "PLC_1/Blocks", depth: 1);

        var root = Assert.Single(result);
        Assert.Equal("Blocks", root.Name);
        Assert.Empty(root.Children!);
        Assert.Equal("2", root.Details!["ChildrenOmitted"]);
    }

    [Fact]
    public void LeafNodes_GetNoOmittedMarker()
    {
        var result = ProjectTreeFilter.Apply(SampleTree(), startPath: "PLC_1/Blocks/Main", depth: 1);

        var leaf = Assert.Single(result);
        Assert.False(leaf.Details!.ContainsKey("ChildrenOmitted"));
    }

    [Fact]
    public void DepthBelowOne_ThrowsWithRecoveryHint()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ProjectTreeFilter.Apply(SampleTree(), startPath: null, depth: 0));

        Assert.Contains("depth must be 1 or greater", ex.Message);
    }

    [Fact]
    public void FilteredOutput_ContainsFreshNodeAndDetailsCopies()
    {
        var tree = SampleTree();

        var result = ProjectTreeFilter.Apply(tree, startPath: "PLC_1/Blocks", depth: null);

        var source = tree[0].Children![0];
        var filtered = Assert.Single(result);
        Assert.NotSame(source, filtered);
        Assert.NotSame(source.Details, filtered.Details);
        Assert.NotSame(source.Children, filtered.Children);
    }
}
