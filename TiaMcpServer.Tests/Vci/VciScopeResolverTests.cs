using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.Scripts;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.TechnologicalObjects;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Vci;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Vci;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Vci;

public class VciScopeResolverTests
{
    [Fact]
    public void DeviceScope_ExpandsAllSupportedCategoriesInTreeOrder()
    {
        var f = new Fixture();
        var leaves = VciScopeResolver.Expand(f.Project, f.DeviceScope);
        Assert.Equal(new[] { "FixtureBlock", "FixtureNestedBlock", "FixtureTags", "FixtureType", "FixtureTechnology", "FixtureScript" },
            leaves.Select(x => x.Path.Last().Name));
        Assert.All(leaves.Take(5), x => Assert.True(x.IsPlc));
        Assert.False(leaves.Last().IsPlc);
        Assert.Equal(VciScope.TechnologyObject, leaves[4].Path.Last().NodeType);
    }

    [Fact]
    public void Expand_NeverYieldsContainers()
    {
        var f = new Fixture();
        Assert.All(VciScopeResolver.Expand(f.Project, f.DeviceScope), x => Assert.True(VciScope.IsLeaf(x.Path.Last().NodeType)));
    }

    [Fact]
    public void PlcSoftwareScope_SkipsUnitsAndSystemBlocks()
    {
        var f = new Fixture();
        var leaves = VciScopeResolver.Expand(f.Project, f.PlcScope);
        Assert.Equal(5, leaves.Count);
        Assert.DoesNotContain(leaves, x => Equals(x.Object, f.UnitBlock) || Equals(x.Object, f.SystemBlock));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsupportedDevice_IsTargetKindUnsupported(bool classic)
    {
        var project = new Siemens.Engineering.Project();
        var device = new Device { Name = "FixtureDevice" };
        project.Devices.Items.Add(device);
        if (classic) device.DeviceItems.Items.Add(new DeviceItem { Container = new SoftwareContainer { Software = new Siemens.Engineering.Hmi.HmiTarget() } });
        AssertFailure(WorkerFailureCategories.TargetKindUnsupported, () => VciScopeResolver.Expand(project, Scope(("Device", device.Name))));
    }

    [Fact]
    public void UnknownDevice_IsTargetNotFoundWordedForScope()
    {
        var f = new Fixture();
        var ex = AssertFailure(WorkerFailureCategories.TargetNotFound, () => VciScopeResolver.Expand(f.Project, Scope(("Device", "MissingFixture"))));
        Assert.Contains("scope", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("startSelector", ex.Message);
    }

    [Fact]
    public void DuplicateDevices_IsTargetAmbiguous()
    {
        var f = new Fixture();
        f.Project.Devices.Items.Add(new Device { Name = f.Device.Name.ToLowerInvariant() });
        AssertFailure(WorkerFailureCategories.TargetAmbiguous, () => VciScopeResolver.Expand(f.Project, f.DeviceScope));
    }

    [Fact]
    public void DuplicateLeaves_IsTargetAmbiguous()
    {
        var f = new Fixture();
        f.Plc.BlockGroup.Blocks.Items.Add(new FC { Name = "fixtureblock" });
        AssertFailure(WorkerFailureCategories.TargetAmbiguous, () => VciScopeResolver.Expand(f.Project, f.BlockScope));
    }

    [Fact]
    public void CaseInsensitiveMatch_PreservesProjectSpelling()
    {
        var f = new Fixture();
        var scope = f.BlockScope.Select(s => new ProjectTreeSelectorSegment { NodeType = s.NodeType, Name = s.Name.ToLowerInvariant() }).ToArray();
        Assert.Equal(f.BlockScope.Select(s => s.Name), Assert.Single(VciScopeResolver.Expand(f.Project, scope)).Path.Select(s => s.Name));
    }

    [Fact]
    public void EmptyContainer_SucceedsWithNoLeaves()
    {
        var f = new Fixture();
        var group = new PlcBlockUserGroup { Name = "EmptyFixture" };
        f.Plc.BlockGroup.Groups.Items.Add(group);
        Assert.Empty(VciScopeResolver.Expand(f.Project, f.PlcScope.Concat(Scope(("BlockFolder", f.Plc.BlockGroup.Name), ("BlockFolder", group.Name))).ToArray()));
    }

    [Fact]
    public void UnknownChild_IsTargetNotFound()
    {
        var f = new Fixture();
        AssertFailure(WorkerFailureCategories.TargetNotFound, () => VciScopeResolver.Expand(f.Project,
            f.PlcScope.Concat(Scope(("BlockFolder", "MissingFixture"))).ToArray()));
    }

    [Fact]
    public void PathOf_RoundTripsEveryExpandedLeaf()
    {
        var f = new Fixture();
        foreach (var leaf in VciScopeResolver.Expand(f.Project, f.DeviceScope))
        {
            var path = Assert.IsType<List<ProjectTreeSelectorSegment>>(VciScopeResolver.PathOf(leaf.Object));
            Assert.Equal(leaf.Path.Select(s => (s.NodeType, s.Name)), path.Select(s => (s.NodeType, s.Name)));
            Assert.Same(leaf.Object, Assert.Single(VciScopeResolver.Expand(f.Project, path)).Object);
        }
    }

    [Fact]
    public void PathOf_DescribesExcludedUnitAndSystemMappings()
    {
        var f = new Fixture();
        Assert.Contains(VciScopeResolver.PathOf(f.UnitBlock)!, s => s.NodeType == ProjectTreeNodeTypes.SoftwareUnit);
        Assert.Contains(VciScopeResolver.PathOf(f.SystemBlock)!, s => s.NodeType == ProjectTreeNodeTypes.SystemBlockFolder);
    }

    [Fact]
    public void PathOf_DeletedLibraryAndParentCycleReturnNull()
    {
        Assert.Null(VciScopeResolver.PathOf(new FC { ParentFailure = new EngineeringObjectDisposedException("Synthetic deleted object.") }));
        Assert.Null(VciScopeResolver.PathOf(new FC { Parent = new PlcBlockUserGroup() }));
        var leaf = new FC(); leaf.Parent = leaf;
        Assert.Null(VciScopeResolver.PathOf(leaf));
    }

    [Fact]
    public void PathOf_UnreadableParentPropagatesInsteadOfHidingMapping()
    {
        var failure = new EngineeringException("Synthetic unreadable parent.");
        var leaf = new FC { ParentFailure = failure };
        Assert.Same(failure, Assert.Throws<EngineeringException>(() => VciScopeResolver.PathOf(leaf)));
    }

    [Fact]
    public void PortalLoss_PropagatesDuringExpansionAndPathLookup()
    {
        var f = new Fixture();
        f.Plc.BlockGroup.Blocks.EnumerationFailure = new NonRecoverableException("Synthetic Portal loss.");
        Assert.Throws<NonRecoverableException>(() => VciScopeResolver.Expand(f.Project, f.PlcScope));
        Assert.Throws<NonRecoverableException>(() => VciScopeResolver.PathOf(new FC { ParentFailure = new NonRecoverableException("Synthetic Portal loss.") }));
    }

    [Theory]
    [InlineData(ProjectTreeNodeTypes.SoftwareUnit)]
    [InlineData(ProjectTreeNodeTypes.SystemBlockFolder)]
    public void UnsupportedScope_IsTypedRefusal(string nodeType)
    {
        var f = new Fixture();
        AssertFailure(WorkerFailureCategories.TargetKindUnsupported, () => VciScopeResolver.Expand(f.Project, f.PlcScope.Concat(Scope((nodeType, "FixtureExcluded"))).ToArray()));
    }

    [Fact]
    public void MissingRequiredScope_IsInvalidSelector()
    {
        var f = new Fixture();
        AssertFailure(WorkerFailureCategories.InvalidSelector, () => VciScopeResolver.Expand(f.Project, null!));
        AssertFailure(WorkerFailureCategories.InvalidSelector, () => VciScopeResolver.Expand(f.Project, Array.Empty<ProjectTreeSelectorSegment>()));
    }

    [Fact]
    public void DiscoveryPortalLoss_PropagatesInsteadOfReturningPartialLeaves()
    {
        var f = new Fixture();
        f.Device.DeviceItems.Items.Add(new DeviceItem { ServiceFailure = new NonRecoverableException("Synthetic discovery loss.") });
        Assert.Throws<NonRecoverableException>(() => VciScopeResolver.Expand(f.Project, f.DeviceScope));
    }

    [Fact]
    public void UnreadableDiscovery_FailsClosedInsteadOfReturningPartialLeaves()
    {
        var f = new Fixture();
        var failure = new EngineeringException("Synthetic unreadable item.");
        f.Device.DeviceItems.Items.Add(new DeviceItem { ServiceFailure = failure });
        Assert.Same(failure, Assert.Throws<EngineeringException>(() => VciScopeResolver.Expand(f.Project, f.DeviceScope)));
    }

    [Fact]
    public void DuplicateSoftware_IsTargetAmbiguous()
    {
        var f = new Fixture();
        f.Device.DeviceItems.Items.Add(new DeviceItem { Container = new SoftwareContainer { Software = new PlcSoftware { Name = f.Plc.Name } } });
        AssertFailure(WorkerFailureCategories.TargetAmbiguous, () => VciScopeResolver.Expand(f.Project, f.PlcScope));
    }

    [Theory]
    [InlineData(ProjectTreeNodeTypes.Ob)]
    [InlineData(ProjectTreeNodeTypes.Fb)]
    [InlineData(ProjectTreeNodeTypes.Fc)]
    [InlineData(ProjectTreeNodeTypes.GlobalDb)]
    [InlineData(ProjectTreeNodeTypes.InstanceDb)]
    [InlineData(ProjectTreeNodeTypes.ArrayDb)]
    [InlineData(ProjectTreeNodeTypes.Block)]
    public void BlockKinds_KeepTheirTreeNodeType(string kind)
    {
        var f = new Fixture();
        PlcBlock block = kind switch
        {
            ProjectTreeNodeTypes.Ob => new OB(), ProjectTreeNodeTypes.Fb => new FB(),
            ProjectTreeNodeTypes.Fc => new FC(), ProjectTreeNodeTypes.GlobalDb => new GlobalDB(),
            ProjectTreeNodeTypes.InstanceDb => new InstanceDB(), ProjectTreeNodeTypes.ArrayDb => new ArrayDB(),
            _ => new PlcBlock()
        };
        block.Name = "FixtureTypedBlock";
        f.Plc.BlockGroup.Blocks.Items.Add(block);
        var leaf = Assert.Single(VciScopeResolver.Expand(f.Project, f.PlcScope),
            x => Equals(x.Object, block));
        Assert.Equal(kind, leaf.Path.Last().NodeType);
        Assert.Equal(kind, VciScopeResolver.PathOf(block)!.Last().NodeType);
    }

    private static WorkerOperationException AssertFailure(string category, Action action)
    { var ex = Assert.Throws<WorkerOperationException>(action); Assert.Equal(category, ex.FailureCategory); return ex; }
    private static List<ProjectTreeSelectorSegment> Scope(params (string Type, string Name)[] segments)
        => segments.Select(s => new ProjectTreeSelectorSegment { NodeType = s.Type, Name = s.Name }).ToList();

    private sealed class Fixture
    {
        public Siemens.Engineering.Project Project { get; } = new();
        public Device Device { get; } = new() { Name = "FixtureDevice" };
        public PlcSoftware Plc { get; } = new() { Name = "FixtureSoftware" };
        public FC UnitBlock { get; } = new() { Name = "FixtureUnitBlock" };
        public FC SystemBlock { get; } = new() { Name = "FixtureSystemBlock" };
        public List<ProjectTreeSelectorSegment> DeviceScope => Scope(("Device", Device.Name));
        public List<ProjectTreeSelectorSegment> PlcScope => DeviceScope.Concat(Scope(("PlcSoftware", Plc.Name))).ToList();
        public List<ProjectTreeSelectorSegment> BlockScope => PlcScope.Concat(Scope(("BlockFolder", Plc.BlockGroup.Name), ("FC", "FixtureBlock"))).ToList();
        public Fixture()
        {
            Project.Devices.Items.Add(Device);
            var item = new DeviceItem { Parent = Device, Container = new SoftwareContainer { Software = Plc } };
            Plc.Parent = item; Device.DeviceItems.Items.Add(item);
            Plc.BlockGroup.Name = "FixtureBlocks"; Plc.TagTableGroup.Name = "FixtureTagTables";
            Plc.TypeGroup.Name = "FixtureTypes"; Plc.TechnologicalObjectGroup.Name = "FixtureTechnologies";
            Plc.BlockGroup.Blocks.Items.Add(new FC { Name = "FixtureBlock" });
            var nested = new PlcBlockUserGroup { Name = "FixtureGroup" };
            nested.Blocks.Items.Add(new FB { Name = "FixtureNestedBlock" }); Plc.BlockGroup.Groups.Items.Add(nested);
            Plc.TagTableGroup.TagTables.Items.Add(new PlcTagTable { Name = "FixtureTags" });
            Plc.TypeGroup.Types.Items.Add(new PlcType { Name = "FixtureType" });
            var technology = new TechnologicalInstanceDBUserGroup { Name = "FixtureTechnologyGroup" };
            technology.TechnologicalObjects.Items.Add(new TechnologicalInstanceDB { Name = "FixtureTechnology" });
            Plc.TechnologicalObjectGroup.Groups.Items.Add(technology);
            var system = new PlcSystemBlockGroup { Name = "FixtureSystem", Parent = Plc.BlockGroup };
            SystemBlock.Parent = system; system.Blocks.Items.Add(SystemBlock); Plc.BlockGroup.SystemBlockGroups.Items.Add(system);
            var unit = new PlcUnit { Name = "FixtureUnit", Parent = Plc };
            unit.BlockGroup.Name = "FixtureUnitBlocks"; unit.BlockGroup.Parent = unit; UnitBlock.Parent = unit.BlockGroup;
            unit.BlockGroup.Blocks.Items.Add(UnitBlock); Plc.UnitProvider = new PlcUnitProvider(); Plc.UnitProvider.UnitGroup.Units.Items.Add(unit);
            var hmi = new HmiSoftware { Name = "FixtureHmi" };
            var hmiItem = new DeviceItem { Parent = Device, Container = new SoftwareContainer { Software = hmi } };
            hmi.Parent = hmiItem; Device.DeviceItems.Items.Add(hmiItem);
            hmi.Scripts.Items.Add(new HmiScriptModule { Name = "FixtureScript" });
        }
    }
}
