using Siemens.Engineering;
using Siemens.Engineering.CrossReference;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;
using TiaMcpServer.Contracts.CrossReferences;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.CrossReferences;
using TiaMcpServer.OpennessWorker.Openness.Project;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Block;

public class CrossReferenceTargetResolverTests
{
    [Fact]
    public void ResolvesEverySelectorTheTreeEmits()
    {
        var project = RichProject();
        var roots = new ProjectTreeSnapshotWalker().WalkSnapshot(project, null, null).Roots;
        var selectors = new List<List<ProjectTreeSelectorSegment>>();
        void Collect(ProjectTreeNode node, List<ProjectTreeSelectorSegment> prefix)
        {
            var path = prefix.Append(new ProjectTreeSelectorSegment { NodeType = node.NodeType, Name = node.Name }).ToList();
            selectors.Add(path);
            foreach (var child in node.Children ?? new List<ProjectTreeNode>()) Collect(child, path);
        }
        foreach (var root in roots) Collect(root, new List<ProjectTreeSelectorSegment>());

        // Guard the fixture: every node type, including units, system-block folders and the grouped device.
        Assert.Equal(ProjectTreeNodeTypes.All.OrderBy(t => t), selectors.Select(s => s[^1].NodeType).Distinct().OrderBy(t => t));
        Assert.Contains(selectors, s => s[0].Name == "Grouped_1" && s.Count == 1);

        foreach (var selector in selectors)
        {
            // Copy the selector verbatim, but upper-cased: matching is case-insensitive as in the tree.
            var upper = selector.Select(s => new ProjectTreeSelectorSegment { NodeType = s.NodeType, Name = s.Name.ToUpperInvariant() }).ToList();
            var resolved = CrossReferenceTargetResolver.Resolve(project, new CrossReferenceSelectorInfo { Path = upper });

            Assert.Equal(selector.Select(s => (s.NodeType, s.Name)), resolved.Canonical.Path.Select(s => (s.NodeType, s.Name)));
            Assert.Null(resolved.Canonical.Member);
            var last = selector[^1];
            if (ProjectTreeNodeTypes.BlockLeaves.Contains(last.NodeType) || last.NodeType == ProjectTreeNodeTypes.Type)
            {
                Assert.Null(resolved.Container);
                Assert.Same(services[last.Name], resolved.LeafService);
            }
            else
            {
                Assert.Null(resolved.LeafService);
                Assert.Equal(last.Name, Assert.IsAssignableFrom<NamedObject>(resolved.Container).Name);
            }
        }
    }

    [Fact]
    public void CaseOnlySiblingsAreAmbiguous()
    {
        var (project, plc) = Fixture();
        plc.BlockGroup.Blocks.Items.Add(Leaf(new FC(), "Motor"));
        plc.BlockGroup.Blocks.Items.Add(Leaf(new FC(), "MOTOR"));

        var error = Assert.Throws<WorkerOperationException>(() => CrossReferenceTargetResolver.Resolve(project,
            Selector((ProjectTreeNodeTypes.Device, "Station_1"), (ProjectTreeNodeTypes.PlcSoftware, "PLC_1"),
                (ProjectTreeNodeTypes.BlockFolder, "Program blocks"), (ProjectTreeNodeTypes.Fc, "motor"))));

        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, error.FailureCategory);
    }

    [Theory]
    [InlineData(CrossReferenceMemberKinds.Tag)]
    [InlineData(CrossReferenceMemberKinds.SystemConstant)]
    [InlineData(CrossReferenceMemberKinds.UserConstant)]
    public void MemberTagUnderTagTableResolves(string kind)
    {
        var (project, plc) = Fixture();
        var table = new PlcTagTable { Name = "Default tag table" };
        table.Tags.Items.Add(Leaf(new PlcTag(), "Start"));
        table.SystemConstants.Items.Add(Leaf(new PlcSystemConstant(), "Start"));
        table.UserConstants.Items.Add(Leaf(new PlcUserConstant(), "Start"));
        plc.TagTableGroup.TagTables.Items.Add(table);
        var expected = kind switch
        {
            CrossReferenceMemberKinds.Tag => table.Tags.Items[0].CrossReferenceService,
            CrossReferenceMemberKinds.SystemConstant => table.SystemConstants.Items[0].CrossReferenceService,
            _ => table.UserConstants.Items[0].CrossReferenceService
        };
        var selector = Selector((ProjectTreeNodeTypes.Device, "Station_1"), (ProjectTreeNodeTypes.PlcSoftware, "PLC_1"),
            (ProjectTreeNodeTypes.TagTableFolder, "PLC tags"), (ProjectTreeNodeTypes.TagTable, "default TAG table"));
        selector.Member = new CrossReferenceMemberSelectorInfo { Kind = kind, Name = "start" };

        var resolved = CrossReferenceTargetResolver.Resolve(project, selector);

        Assert.Same(expected, resolved.LeafService);
        Assert.Equal("Default tag table", resolved.Canonical.Path[^1].Name);
        Assert.Equal(kind, resolved.Canonical.Member!.Kind);
        Assert.Equal("Start", resolved.Canonical.Member.Name);
    }

    [Theory]
    [InlineData("Unknown", "Start")]
    [InlineData("Tag", "Missing")]
    public void MemberMustNameAnExistingKindAndObject(string kind, string name)
    {
        var (project, plc) = Fixture();
        var table = new PlcTagTable { Name = "T" };
        table.Tags.Items.Add(Leaf(new PlcTag(), "Start"));
        plc.TagTableGroup.TagTables.Items.Add(table);
        var selector = Selector((ProjectTreeNodeTypes.Device, "Station_1"), (ProjectTreeNodeTypes.PlcSoftware, "PLC_1"),
            (ProjectTreeNodeTypes.TagTableFolder, "PLC tags"), (ProjectTreeNodeTypes.TagTable, "T"));
        selector.Member = new CrossReferenceMemberSelectorInfo { Kind = kind, Name = name };

        var error = Assert.Throws<WorkerOperationException>(() => CrossReferenceTargetResolver.Resolve(project, selector));

        Assert.Equal(kind == "Unknown" ? WorkerFailureCategories.InvalidSelector : WorkerFailureCategories.TargetNotFound,
            error.FailureCategory);
    }

    [Fact]
    public void MemberOutsideTagTableIsInvalid()
    {
        var (project, _) = Fixture();
        var selector = Selector((ProjectTreeNodeTypes.Device, "Station_1"), (ProjectTreeNodeTypes.PlcSoftware, "PLC_1"));
        selector.Member = new CrossReferenceMemberSelectorInfo { Kind = CrossReferenceMemberKinds.Tag, Name = "Start" };

        var error = Assert.Throws<WorkerOperationException>(() => CrossReferenceTargetResolver.Resolve(project, selector));

        Assert.Equal(WorkerFailureCategories.InvalidSelector, error.FailureCategory);
    }

    [Theory]
    [InlineData("Station_2", "PLC_1")]
    [InlineData("Station_1", "PLC_2")]
    public void MissingSegmentIsTargetNotFound(string device, string plc)
    {
        var (project, _) = Fixture();

        var error = Assert.Throws<WorkerOperationException>(() => CrossReferenceTargetResolver.Resolve(project,
            Selector((ProjectTreeNodeTypes.Device, device), (ProjectTreeNodeTypes.PlcSoftware, plc))));

        Assert.Equal(WorkerFailureCategories.TargetNotFound, error.FailureCategory);
    }

    [Fact]
    public void ImpossibleTransitionIsInvalidSelector()
    {
        var (project, _) = Fixture();

        var error = Assert.Throws<WorkerOperationException>(() => CrossReferenceTargetResolver.Resolve(project,
            Selector((ProjectTreeNodeTypes.Device, "Station_1"), (ProjectTreeNodeTypes.Fc, "x"))));

        Assert.Equal(WorkerFailureCategories.InvalidSelector, error.FailureCategory);
    }

    [Fact]
    public void LeafWithoutServiceIsTargetKindUnsupported()
    {
        var (project, plc) = Fixture();
        plc.BlockGroup.Blocks.Items.Add(new FC { Name = "NoService" });

        var error = Assert.Throws<WorkerOperationException>(() => CrossReferenceTargetResolver.Resolve(project,
            Selector((ProjectTreeNodeTypes.Device, "Station_1"), (ProjectTreeNodeTypes.PlcSoftware, "PLC_1"),
                (ProjectTreeNodeTypes.BlockFolder, "Program blocks"), (ProjectTreeNodeTypes.Fc, "NoService"))));

        Assert.Equal(WorkerFailureCategories.TargetKindUnsupported, error.FailureCategory);
    }

    private readonly Dictionary<string, CrossReferenceService> services = new();

    private T Leaf<T>(T leaf, string name) where T : NamedObject
    {
        leaf.Name = name;
        leaf.CrossReferenceService = new CrossReferenceService();
        services[name] = leaf.CrossReferenceService;
        return leaf;
    }

    private Siemens.Engineering.Project RichProject()
    {
        var (project, plc) = Fixture();
        plc.BlockGroup.Blocks.Items.Add(Leaf(new OB(), "RootOB"));
        plc.BlockGroup.Blocks.Items.Add(Leaf(new PlcBlock(), "OtherBlock"));
        var nested = new PlcBlockUserGroup { Name = "Outer" };
        var inner = new PlcBlockUserGroup { Name = "Inner" };
        nested.Groups.Items.Add(inner);
        inner.Blocks.Items.Add(Leaf(new FB(), "InnerFB"));
        inner.Blocks.Items.Add(Leaf(new FC(), "InnerFC"));
        plc.BlockGroup.Groups.Items.Add(nested);
        var system = new PlcSystemBlockGroup { Name = "System blocks" };
        var systemChild = new PlcSystemBlockGroup { Name = "Program resources" };
        system.Groups.Items.Add(systemChild);
        systemChild.Blocks.Items.Add(Leaf(new GlobalDB(), "SysDB"));
        systemChild.Blocks.Items.Add(Leaf(new PlcBlock(), "SysOther"));
        plc.BlockGroup.SystemBlockGroups.Items.Add(system);
        var tableGroup = new PlcTagTableGroup { Name = "TagGroup" };
        tableGroup.TagTables.Items.Add(new PlcTagTable { Name = "Nested table" });
        plc.TagTableGroup.Groups.Items.Add(tableGroup);
        plc.TagTableGroup.TagTables.Items.Add(new PlcTagTable { Name = "Default tag table" });
        var typeGroup = new PlcTypeGroup { Name = "TypeGroup" };
        typeGroup.Types.Items.Add(Leaf(new PlcType(), "NestedUDT"));
        plc.TypeGroup.Groups.Items.Add(typeGroup);
        var unit = new PlcUnit { Name = "Unit1" };
        unit.BlockGroup.Name = "Unit blocks";
        unit.BlockGroup.Blocks.Items.Add(Leaf(new InstanceDB(), "UnitIDB"));
        unit.BlockGroup.Blocks.Items.Add(Leaf(new ArrayDB(), "UnitArrayDB"));
        unit.TagTableGroup.Name = "Unit tags";
        unit.TypeGroup.Name = "Unit types";
        unit.TypeGroup.Types.Items.Add(Leaf(new PlcType(), "UnitUDT"));
        plc.UnitProvider = new PlcUnitProvider();
        plc.UnitProvider.UnitGroup.Units.Items.Add(unit);

        var grouped = new PlcSoftware { Name = "PLC_G" };
        grouped.BlockGroup.Name = "Program blocks";
        grouped.TagTableGroup.Name = "PLC tags";
        grouped.TypeGroup.Name = "PLC data types";
        grouped.BlockGroup.Blocks.Items.Add(Leaf(new FC(), "GroupedFC"));
        var groupedDevice = new Device { Name = "Grouped_1" };
        groupedDevice.DeviceItems.Items.Add(new DeviceItem { Container = new SoftwareContainer { Software = grouped } });
        var deviceGroup = new DeviceUserGroup { Name = "Line A" };
        deviceGroup.Devices.Items.Add(groupedDevice);
        project.DeviceGroups.Items.Add(deviceGroup);
        return project;
    }

    internal static (Siemens.Engineering.Project Project, PlcSoftware Software) Fixture()
    {
        var project = new Siemens.Engineering.Project();
        var software = new PlcSoftware { Name = "PLC_1" };
        software.BlockGroup.Name = "Program blocks";
        software.TagTableGroup.Name = "PLC tags";
        software.TypeGroup.Name = "PLC data types";
        var device = new Device { Name = "Station_1" };
        device.DeviceItems.Items.Add(new DeviceItem { Container = new SoftwareContainer { Software = software } });
        project.Devices.Items.Add(device);
        return (project, software);
    }

    internal static CrossReferenceSelectorInfo Selector(params (string NodeType, string Name)[] segments) => new()
    {
        Path = segments.Select(s => new ProjectTreeSelectorSegment { NodeType = s.NodeType, Name = s.Name }).ToList()
    };
}
