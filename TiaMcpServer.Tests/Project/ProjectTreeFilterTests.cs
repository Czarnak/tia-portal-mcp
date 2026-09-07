using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public class ProjectTreeFilterTests
{
    private static List<ProjectTreeNode> TypedSampleTree()
        => new()
        {
            TypedNode(ProjectTreeNodeTypes.Device, "PLC_1",
                TypedNode(ProjectTreeNodeTypes.PlcSoftware, "PLC_1",
                    TypedNode(ProjectTreeNodeTypes.SoftwareUnit, "Unit_A",
                        TypedNode(ProjectTreeNodeTypes.BlockFolder, "UnitBlocks",
                            TypedNode(ProjectTreeNodeTypes.Fb, "UnitMotor"))),
                    TypedNode(ProjectTreeNodeTypes.BlockFolder, "Blocks",
                        TypedNode(ProjectTreeNodeTypes.BlockFolder, "Motors",
                            TypedNode(ProjectTreeNodeTypes.Fb, "MotorControl"),
                            TypedNode(ProjectTreeNodeTypes.InstanceDb, "MotorControl_DB")),
                        TypedNode(ProjectTreeNodeTypes.SystemBlockFolder, "SystemBlocks",
                            TypedNode(ProjectTreeNodeTypes.Ob, "Startup"))),
                    TypedNode(ProjectTreeNodeTypes.TagTableFolder, "Tags",
                        TypedNode(ProjectTreeNodeTypes.TagTable, "DefaultTags")),
                    TypedNode(ProjectTreeNodeTypes.TypeFolder, "Types",
                        TypedNode(ProjectTreeNodeTypes.Type, "MotorType"))))
        };

    public static IEnumerable<object[]> ValidTransitions()
    {
        yield return new object[] { "Device", "PlcSoftware" };
        yield return new object[] { "PlcSoftware", "SoftwareUnit" };
        yield return new object[] { "PlcSoftware", "BlockFolder" };
        yield return new object[] { "PlcSoftware", "TagTableFolder" };
        yield return new object[] { "PlcSoftware", "TypeFolder" };
        yield return new object[] { "SoftwareUnit", "BlockFolder" };
        yield return new object[] { "SoftwareUnit", "TagTableFolder" };
        yield return new object[] { "SoftwareUnit", "TypeFolder" };
        yield return new object[] { "BlockFolder", "BlockFolder" };
        yield return new object[] { "BlockFolder", "SystemBlockFolder" };
        foreach (var leaf in new[] { "OB", "FB", "FC", "GlobalDB", "InstanceDB", "ArrayDB", "Block" })
        {
            yield return new object[] { "BlockFolder", leaf };
            yield return new object[] { "SystemBlockFolder", leaf };
        }

        yield return new object[] { "SystemBlockFolder", "SystemBlockFolder" };
        yield return new object[] { "TagTableFolder", "TagTableFolder" };
        yield return new object[] { "TagTableFolder", "TagTable" };
        yield return new object[] { "TypeFolder", "TypeFolder" };
        yield return new object[] { "TypeFolder", "Type" };
    }

    // Production bug caught: omitting the typed selector must return a detached complete tree, not caller-owned nodes.
    [Fact]
    public void NullTypedSelector_ClonesCompleteTreeAndHasNoCanonicalSelector()
    {
        var source = TypedSampleTree();

        var selected = ProjectTreeFilter.Apply(source, startSelector: null, depth: null);

        Assert.Null(selected.CanonicalStartSelector);
        Assert.Equal(CanonicalJson.Serialize(source), CanonicalJson.Serialize(selected.Roots));
        Assert.NotSame(source, selected.Roots);
        Assert.NotSame(source[0], selected.Roots[0]);
        Assert.NotSame(source[0].Details, selected.Roots[0].Details);
        Assert.NotSame(source[0].Children, selected.Roots[0].Children);
    }

    // Production bug caught: any transition omitted from the declared v3 table would reject a structurally valid selector.
    [Theory]
    [MemberData(nameof(ValidTransitions))]
    public void EveryAllowedTransition_ResolvesItsDirectChild(string parentType, string childType)
    {
        var (tree, selector) = TreeForTransition(parentType, childType);

        var selected = ProjectTreeFilter.Apply(tree, selector, depth: null);

        var root = Assert.Single(selected.Roots);
        Assert.Equal(childType, root.NodeType);
        Assert.Equal("SelectedChild", root.Name);
        Assert.Equal(childType, selected.CanonicalStartSelector![^1].NodeType);
        Assert.Equal("SelectedChild", selected.CanonicalStartSelector[^1].Name);
    }

    // Production bug caught: a non-Device first segment could otherwise begin resolution at an arbitrary root type.
    [Fact]
    public void NonDeviceRoot_IsInvalidSelector()
    {
        var error = Assert.Throws<ProjectTreeSelectionException>(() =>
            ProjectTreeFilter.Apply(
                new List<ProjectTreeNode> { TypedNode(ProjectTreeNodeTypes.PlcSoftware, "PLC_1") },
                Selector(ProjectTreeNodeTypes.PlcSoftware, "PLC_1"),
                depth: null));

        Assert.Equal(WorkerFailureCategories.InvalidSelector, error.Category);
    }

    // Production bug caught: unknown or incorrectly cased node types could escape the closed v3 vocabulary.
    [Theory]
    [InlineData("Folder")]
    [InlineData("device")]
    public void UnknownNodeType_IsInvalidSelector(string nodeType)
    {
        var error = Assert.Throws<ProjectTreeSelectionException>(() =>
            ProjectTreeFilter.Apply(
                TypedSampleTree(),
                Selector(nodeType, "PLC_1"),
                depth: null));

        Assert.Equal(WorkerFailureCategories.InvalidSelector, error.Category);
    }

    // Production bug caught: a blank segment name could otherwise participate in target matching.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankSegmentName_IsInvalidSelector(string name)
    {
        var error = Assert.Throws<ProjectTreeSelectionException>(() =>
            ProjectTreeFilter.Apply(
                TypedSampleTree(),
                Selector(ProjectTreeNodeTypes.Device, name),
                depth: null));

        Assert.Equal(WorkerFailureCategories.InvalidSelector, error.Category);
    }

    // Production bug caught: an explicitly empty selector must not silently mean the same thing as omission.
    [Fact]
    public void EmptySelector_IsInvalidSelector()
    {
        var error = Assert.Throws<ProjectTreeSelectionException>(() =>
            ProjectTreeFilter.Apply(TypedSampleTree(), Array.Empty<ProjectTreeSelectorSegment>(), depth: null));

        Assert.Equal(WorkerFailureCategories.InvalidSelector, error.Category);
    }

    // Production bug caught: structurally impossible parent-child pairs could otherwise be searched in the data tree.
    [Fact]
    public void ImpossibleTransition_IsInvalidSelector()
    {
        var error = Assert.Throws<ProjectTreeSelectionException>(() =>
            ProjectTreeFilter.Apply(
                TypedSampleTree(),
                Selector(
                    ProjectTreeNodeTypes.Device, "PLC_1",
                    ProjectTreeNodeTypes.BlockFolder, "Blocks"),
                depth: null));

        Assert.Equal(WorkerFailureCategories.InvalidSelector, error.Category);
    }

    // Production bug caught: leaf node types could otherwise accept impossible continuation segments.
    [Fact]
    public void LeafContinuation_IsInvalidSelector()
    {
        var error = Assert.Throws<ProjectTreeSelectionException>(() =>
            ProjectTreeFilter.Apply(
                TypedSampleTree(),
                Selector(
                    ProjectTreeNodeTypes.Device, "PLC_1",
                    ProjectTreeNodeTypes.PlcSoftware, "PLC_1",
                    ProjectTreeNodeTypes.BlockFolder, "Blocks",
                    ProjectTreeNodeTypes.Fb, "MotorControl",
                    ProjectTreeNodeTypes.BlockFolder, "Nested"),
                depth: null));

        Assert.Equal(WorkerFailureCategories.InvalidSelector, error.Category);
    }

    // Production bug caught: selector resolution must not find a same-named descendant outside the current direct-child set.
    [Fact]
    public void MissingDirectChild_DoesNotFallThroughToDescendants()
    {
        var error = Assert.Throws<ProjectTreeSelectionException>(() =>
            ProjectTreeFilter.Apply(
                TypedSampleTree(),
                Selector(
                    ProjectTreeNodeTypes.Device, "PLC_1",
                    ProjectTreeNodeTypes.PlcSoftware, "PLC_1",
                    ProjectTreeNodeTypes.BlockFolder, "Motors"),
                depth: null));

        Assert.Equal(WorkerFailureCategories.TargetNotFound, error.Category);
    }

    // Production bug caught: direct-child selection must reject duplicate matches instead of choosing traversal order.
    [Fact]
    public void DuplicateDirectChild_IsTargetAmbiguousRatherThanFirstMatch()
    {
        var tree = TreeWithDuplicateMotorFolders();
        var error = Assert.Throws<ProjectTreeSelectionException>(() =>
            ProjectTreeFilter.Apply(
                tree,
                Selector(
                    ProjectTreeNodeTypes.Device, "PLC_1",
                    ProjectTreeNodeTypes.PlcSoftware, "PLC_1",
                    ProjectTreeNodeTypes.BlockFolder, "Motors"),
                depth: null));

        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, error.Category);
    }

    // Production bug caught: the canonical selector must preserve observed spelling while accepting case-insensitive names.
    [Fact]
    public void ResolvedSelector_UsesCanonicalObservedSpelling()
    {
        var selected = ProjectTreeFilter.Apply(
            TypedSampleTree(),
            Selector(
                ProjectTreeNodeTypes.Device, "plc_1",
                ProjectTreeNodeTypes.PlcSoftware, "plc_1",
                ProjectTreeNodeTypes.BlockFolder, "blocks",
                ProjectTreeNodeTypes.BlockFolder, "motors"),
            depth: null);

        Assert.Equal(
            new[] { "PLC_1", "PLC_1", "Blocks", "Motors" },
            selected.CanonicalStartSelector!.Select(segment => segment.Name));
    }

    // Production bug caught: depth one must promote the match, report the exact pruned count, and leave the source untouched.
    [Fact]
    public void DepthOne_PromotesSelectedRootAndReportsExactChildrenOmitted()
    {
        var source = TypedSampleTree();

        var selected = ProjectTreeFilter.Apply(source, MotorsSelector(), depth: 1);

        var root = Assert.Single(selected.Roots);
        Assert.Empty(root.Children!);
        Assert.Equal("2", root.Details!["ChildrenOmitted"]);
        Assert.Equal(2, FindMotors(source).Children!.Count);
        Assert.False(FindMotors(source).Details!.ContainsKey("ChildrenOmitted"));
    }

    // Production bug caught: pruning at depth two must mark only boundary nodes and use each node's observed child count.
    [Fact]
    public void DepthTwo_ReportsExactBoundaryOmissions()
    {
        var selected = ProjectTreeFilter.Apply(TypedSampleTree(), BlocksSelector(), depth: 2);

        var root = Assert.Single(selected.Roots);
        Assert.False(root.Details!.ContainsKey("ChildrenOmitted"));
        Assert.Equal("2", root.Children![0].Details!["ChildrenOmitted"]);
        Assert.Equal("1", root.Children[1].Details!["ChildrenOmitted"]);
    }

    // Production bug caught: typed selection and pruning could diverge from cloning the same complete-tree subtree.
    [Fact]
    public void TypedSelection_EqualsIndependentlyClonedSubtreeFromCompleteTree()
    {
        var full = TypedSampleTree();
        var actual = ProjectTreeFilter.Apply(full, BlocksSelector(), depth: 2).Roots;
        var expected = IndependentlyCloneAndPrune(full[0].Children![0].Children![1], depth: 2);

        Assert.Equal(CanonicalJson.Serialize(new[] { expected }), CanonicalJson.Serialize(actual));
    }

    private static ProjectTreeNode TypedNode(string nodeType, string name, params ProjectTreeNode[] children)
        => new()
        {
            Name = name,
            NodeType = nodeType,
            Details = new Dictionary<string, string> { ["ObservedName"] = name },
            Children = new List<ProjectTreeNode>(children)
        };

    private static IReadOnlyList<ProjectTreeSelectorSegment> Selector(params string[] parts)
    {
        Assert.True(parts.Length % 2 == 0);
        return parts
            .Chunk(2)
            .Select(pair => new ProjectTreeSelectorSegment { NodeType = pair[0], Name = pair[1] })
            .ToList();
    }

    private static IReadOnlyList<ProjectTreeSelectorSegment> MotorsSelector()
        => Selector(
            ProjectTreeNodeTypes.Device, "PLC_1",
            ProjectTreeNodeTypes.PlcSoftware, "PLC_1",
            ProjectTreeNodeTypes.BlockFolder, "Blocks",
            ProjectTreeNodeTypes.BlockFolder, "Motors");

    private static IReadOnlyList<ProjectTreeSelectorSegment> BlocksSelector()
        => Selector(
            ProjectTreeNodeTypes.Device, "PLC_1",
            ProjectTreeNodeTypes.PlcSoftware, "PLC_1",
            ProjectTreeNodeTypes.BlockFolder, "Blocks");

    private static ProjectTreeNode FindMotors(List<ProjectTreeNode> roots)
        => roots[0].Children![0].Children![1].Children![0];

    private static List<ProjectTreeNode> TreeWithDuplicateMotorFolders()
        => new()
        {
            TypedNode(ProjectTreeNodeTypes.Device, "PLC_1",
                TypedNode(ProjectTreeNodeTypes.PlcSoftware, "PLC_1",
                    TypedNode(ProjectTreeNodeTypes.BlockFolder, "Motors"),
                    TypedNode(ProjectTreeNodeTypes.BlockFolder, "MOTORS")))
        };

    private static (List<ProjectTreeNode> Tree, IReadOnlyList<ProjectTreeSelectorSegment> Selector)
        TreeForTransition(string parentType, string childType)
    {
        var prefix = PrefixTo(parentType);
        var child = TypedNode(childType, "SelectedChild");
        var parent = TypedNode(prefix[^1].NodeType, prefix[^1].Name, child);

        for (var index = prefix.Count - 2; index >= 0; index--)
        {
            parent = TypedNode(prefix[index].NodeType, prefix[index].Name, parent);
        }

        var selector = prefix
            .Concat(new[] { new ProjectTreeSelectorSegment { NodeType = childType, Name = "SelectedChild" } })
            .ToList();
        return (new List<ProjectTreeNode> { parent }, selector);
    }

    private static List<ProjectTreeSelectorSegment> PrefixTo(string nodeType)
        => nodeType switch
        {
            ProjectTreeNodeTypes.Device => Segments((ProjectTreeNodeTypes.Device, "RootDevice")),
            ProjectTreeNodeTypes.PlcSoftware => Segments(
                (ProjectTreeNodeTypes.Device, "RootDevice"),
                (ProjectTreeNodeTypes.PlcSoftware, "Software")),
            ProjectTreeNodeTypes.SoftwareUnit => Segments(
                (ProjectTreeNodeTypes.Device, "RootDevice"),
                (ProjectTreeNodeTypes.PlcSoftware, "Software"),
                (ProjectTreeNodeTypes.SoftwareUnit, "Unit")),
            ProjectTreeNodeTypes.BlockFolder => Segments(
                (ProjectTreeNodeTypes.Device, "RootDevice"),
                (ProjectTreeNodeTypes.PlcSoftware, "Software"),
                (ProjectTreeNodeTypes.BlockFolder, "Blocks")),
            ProjectTreeNodeTypes.SystemBlockFolder => Segments(
                (ProjectTreeNodeTypes.Device, "RootDevice"),
                (ProjectTreeNodeTypes.PlcSoftware, "Software"),
                (ProjectTreeNodeTypes.BlockFolder, "Blocks"),
                (ProjectTreeNodeTypes.SystemBlockFolder, "SystemBlocks")),
            ProjectTreeNodeTypes.TagTableFolder => Segments(
                (ProjectTreeNodeTypes.Device, "RootDevice"),
                (ProjectTreeNodeTypes.PlcSoftware, "Software"),
                (ProjectTreeNodeTypes.TagTableFolder, "Tags")),
            ProjectTreeNodeTypes.TypeFolder => Segments(
                (ProjectTreeNodeTypes.Device, "RootDevice"),
                (ProjectTreeNodeTypes.PlcSoftware, "Software"),
                (ProjectTreeNodeTypes.TypeFolder, "Types")),
            _ => throw new InvalidOperationException($"No valid prefix fixture exists for '{nodeType}'.")
        };

    private static List<ProjectTreeSelectorSegment> Segments(params (string NodeType, string Name)[] values)
        => values.Select(value => new ProjectTreeSelectorSegment
        {
            NodeType = value.NodeType,
            Name = value.Name
        }).ToList();

    private static ProjectTreeNode IndependentlyCloneAndPrune(ProjectTreeNode node, int depth)
    {
        var details = node.Details == null
            ? null
            : new Dictionary<string, string>(node.Details);
        var children = node.Children ?? new List<ProjectTreeNode>();

        if (depth == 1 && children.Count > 0)
        {
            details ??= new Dictionary<string, string>();
            details["ChildrenOmitted"] = children.Count.ToString();
            return new ProjectTreeNode
            {
                Name = node.Name,
                NodeType = node.NodeType,
                Details = details,
                Children = new List<ProjectTreeNode>()
            };
        }

        return new ProjectTreeNode
        {
            Name = node.Name,
            NodeType = node.NodeType,
            Details = details,
            Children = node.Children == null
                ? null
                : children.Select(child => IndependentlyCloneAndPrune(child, depth - 1)).ToList()
        };
    }
}
