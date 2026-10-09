using TiaMcpServer.Contracts.Project;
using TiaMcpServer.ProjectTree;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public sealed class ProjectTreeFlattenerTests
{
    // Production bug caught: depth-first projection could assign a child before its parent.
    [Fact]
    public void Flatten_AssignsContiguousPreorderAndParentFirstOpaqueIds()
    {
        var nodes = ProjectTreeFlattener.Flatten(SampleSelectedTree());

        Assert.Equal(Enumerable.Range(0, nodes.Count), nodes.Select(node => node.Sequence));
        Assert.Equal(nodes.Select(node => $"n{node.Sequence}"), nodes.Select(node => node.NodeId));
        Assert.All(nodes.Skip(1), node =>
            Assert.True(nodes.Single(parent => parent.NodeId == node.ParentNodeId).Sequence < node.Sequence));
    }

    [Fact]
    public void PagedFlatNodes_ReconstructTheOriginalTypedSelector()
    {
        var nodes = ProjectTreeFlattener.Flatten(SampleSelectedTree());
        var target = nodes.Single(node => node.Name == "Motor_1");
        var selector = ReconstructParentFirstSelector(nodes, target);

        Assert.Equal(
            new[] { "Device:PLC_1", "PlcSoftware:PLC_1", "BlockFolder:Program blocks", "FB:Motor_1" },
            selector.Select(segment => $"{segment.NodeType}:{segment.Name}"));
    }

    [Fact]
    public void Flatten_MultipleSelectedRootsArePromotedToNullParents()
    {
        var roots = new[]
        {
            Node("PLC_1", ProjectTreeNodeTypes.Device),
            Node("PLC_2", ProjectTreeNodeTypes.Device)
        };

        var nodes = ProjectTreeFlattener.Flatten(roots);

        Assert.Equal(new[] { "PLC_1", "PLC_2" }, nodes.Select(node => node.Name));
        Assert.All(nodes, node => Assert.Null(node.ParentNodeId));
    }

    [Fact]
    public void Flatten_SelectedSubtreeRootHasNoParent()
    {
        var selected = Node(
            "Program blocks",
            ProjectTreeNodeTypes.BlockFolder,
            Node("Motor_1", ProjectTreeNodeTypes.Fb));

        var nodes = ProjectTreeFlattener.Flatten(new[] { selected });

        Assert.Null(nodes[0].ParentNodeId);
        Assert.Equal(nodes[0].NodeId, nodes[1].ParentNodeId);
    }

    [Fact]
    public void Flatten_UsesEmptyCopiedDetailsDictionaries()
    {
        var sourceDetails = new Dictionary<string, string> { ["Language"] = "SCL" };
        var roots = new[]
        {
            Node("PLC_1", ProjectTreeNodeTypes.Device, details: null),
            Node("Motor_1", ProjectTreeNodeTypes.Fb, sourceDetails)
        };

        var nodes = ProjectTreeFlattener.Flatten(roots);
        sourceDetails["Language"] = "changed-after-flatten";

        Assert.Empty(nodes[0].Details);
        Assert.Equal("SCL", nodes[1].Details["Language"]);
        Assert.NotSame(sourceDetails, nodes[1].Details);
    }

    [Fact]
    public void Flatten_EmptyTreeProducesNoNodes()
    {
        var nodes = ProjectTreeFlattener.Flatten(Array.Empty<ProjectTreeNode>());

        Assert.Empty(nodes);
    }

    // Production bug caught: page slicing could erase the stable parent reference of a later child.
    [Fact]
    public void Flatten_ParentReferenceSurvivesAcrossPageSlices()
    {
        var nodes = ProjectTreeFlattener.Flatten(SampleSelectedTree());
        var firstPage = nodes.Take(3).ToArray();
        var secondPage = nodes.Skip(3).Take(1).ToArray();

        var child = Assert.Single(secondPage);
        Assert.Equal("Motor_1", child.Name);
        Assert.DoesNotContain(secondPage, node => node.NodeId == child.ParentNodeId);
        Assert.Contains(firstPage, node => node.NodeId == child.ParentNodeId);
    }

    private static IReadOnlyList<ProjectTreeNode> SampleSelectedTree()
        => new[]
        {
            Node(
                "PLC_1",
                ProjectTreeNodeTypes.Device,
                Node(
                    "PLC_1",
                    ProjectTreeNodeTypes.PlcSoftware,
                    Node(
                        "Program blocks",
                        ProjectTreeNodeTypes.BlockFolder,
                        Node("Motor_1", ProjectTreeNodeTypes.Fb),
                        Node("Motor_2", ProjectTreeNodeTypes.Fb))))
        };

    private static ProjectTreeNode Node(
        string name,
        string nodeType,
        params ProjectTreeNode[] children)
        => Node(name, nodeType, new Dictionary<string, string>(), children);

    private static ProjectTreeNode Node(
        string name,
        string nodeType,
        Dictionary<string, string>? details,
        params ProjectTreeNode[] children)
        => new()
        {
            Name = name,
            NodeType = nodeType,
            Details = details,
            Children = children.ToList()
        };

    private static IReadOnlyList<ProjectTreeSelectorSegment> ReconstructParentFirstSelector(
        IReadOnlyList<ProjectTreeFlatNode> nodes,
        ProjectTreeFlatNode target)
    {
        var byId = nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);
        var segments = new Stack<ProjectTreeSelectorSegment>();
        ProjectTreeFlatNode? current = target;
        while (current is not null)
        {
            segments.Push(new ProjectTreeSelectorSegment
            {
                NodeType = current.NodeType,
                Name = current.Name
            });
            current = current.ParentNodeId is null ? null : byId[current.ParentNodeId];
        }

        return segments.ToArray();
    }
}
