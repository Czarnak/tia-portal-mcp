using System.Text.Json;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.Scripts;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Units;
using Siemens.Engineering.VersionControl;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Vci;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Vci;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Vci;

public class VciReaderTests
{
    [Fact]
    public void ListWorkspaces_ReturnsNamesRootsAndCountsWithoutStatus()
    {
        var f = new Fixture();
        f.AddMapping(f.Leaf);
        f.Service.WorkspaceGroup.Workspaces.Create("EmptyFixture", new DirectoryInfo(Path.GetTempPath()));

        var result = VciReader.ListWorkspaces(f.Service);

        Assert.Equal(new[] { f.Workspace.Name, "EmptyFixture" }, result.Workspaces.Select(x => x.Name));
        Assert.Equal(f.Workspace.RootPath.FullName, result.Workspaces[0].RootPath);
        Assert.Equal(new[] { 1, 0 }, result.Workspaces.Select(x => x.MappingCount));
        Assert.Equal(0, Assert.Single(f.Workspace.MappedObjects).GetStatusCalls);
    }

    [Fact]
    public void ListWorkspaces_RootlessEntryPreservesHealthySiblingsAndExplicitNull()
    {
        var f = new Fixture();
        f.AddMapping(f.Leaf);
        f.Service.WorkspaceGroup.Workspaces.Items.Add(new Workspace { Name = "RootlessFixture", RootPath = null! });
        f.Service.WorkspaceGroup.Workspaces.Create("EmptyFixture", new DirectoryInfo(Path.GetTempPath()));

        var result = VciReader.ListWorkspaces(f.Service);

        Assert.Equal(new[] { f.Workspace.Name, "RootlessFixture", "EmptyFixture" }, result.Workspaces.Select(x => x.Name));
        Assert.Equal(f.Workspace.RootPath.FullName, result.Workspaces[0].RootPath);
        Assert.Null(result.Workspaces[1].RootPath);
        Assert.Equal(Path.GetFullPath(Path.GetTempPath()), result.Workspaces[2].RootPath);
        Assert.Equal(new[] { 1, 0, 0 }, result.Workspaces.Select(x => x.MappingCount));
        using var json = JsonDocument.Parse(WorkerJson.SerializePayload(result));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("workspaces")[1].GetProperty("rootPath").ValueKind);
        Assert.Equal(0, Assert.Single(f.Workspace.MappedObjects).GetStatusCalls);
    }

    [Fact]
    public void ListMappings_WithoutStatus_NeverCallsGetStatus()
    {
        var f = new Fixture();
        var mapping = f.AddMapping(f.Leaf);
        mapping.StatusFailure = new InvalidOperationException("Status must not be read.");

        var result = VciReader.ListMappings(f.Service, f.Project, f.Query());

        Assert.Null(Assert.Single(result.Mappings).Status);
        Assert.Equal(0, mapping.GetStatusCalls);
        Assert.Equal(100, result.Limit);
        Assert.Equal(0, result.Offset);
    }

    [Fact]
    public void WithStatus_CallsGetStatusOnlyForThePage()
    {
        var f = new Fixture();
        var mappings = Enumerable.Range(0, 4).Select(_ => f.AddMapping(f.Leaf)).ToArray();
        mappings[0].StatusFailure = new EngineeringException("Outside page.");
        var query = f.Query();
        query.WithStatus = true; query.Offset = 1; query.Limit = 2;

        var result = VciReader.ListMappings(f.Service, f.Project, query);

        Assert.Equal(4, result.Total);
        Assert.Equal(2, result.Mappings.Count);
        Assert.Equal(new[] { 0, 1, 1, 0 }, mappings.Select(x => x.GetStatusCalls));
        Assert.All(result.Mappings, x => Assert.NotNull(x.Status));
    }

    [Fact]
    public void Status_RendersRawEnumStrings()
    {
        var f = new Fixture();
        f.AddMapping(f.Leaf).StatusResult = new IndividualObjectCompareResult
        {
            CompareState = CompareState.Unequal,
            IndividualObjectCompareDetails = IndividualObjectCompareDetails.ProjectObjectChanged
                | IndividualObjectCompareDetails.WorkspaceFileChanged
        };
        var query = f.Query(); query.WithStatus = true;

        var status = Assert.Single(VciReader.ListMappings(f.Service, f.Project, query).Mappings).Status!;

        Assert.Equal("Unequal", status.CompareState);
        Assert.Equal("ProjectObjectChanged, WorkspaceFileChanged", status.Details);
    }

    [Fact]
    public void FileKey_IsRelativeToWorkspaceRoot()
    {
        var f = new Fixture();
        var mapping = f.AddMapping(f.Leaf);
        mapping.DirectoryPath = new DirectoryInfo(Path.Combine(f.Workspace.RootPath.FullName, "FixtureFolder", "Nested"));
        mapping.FileFormat = "SCL";

        var result = Assert.Single(VciReader.ListMappings(f.Service, f.Project, f.Query()).Mappings);

        Assert.Equal(@"\FixtureFolder\Nested", result.FileKey.Directory);
        Assert.Equal(f.Leaf.Name, result.FileKey.FileName);
        Assert.Equal("SCL", result.Format);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GoneObject_ReturnsNullPath(bool disposed)
    {
        var f = new Fixture();
        f.AddMapping(disposed ? new FC { ParentFailure = new EngineeringObjectDisposedException("Gone.") } : null);

        Assert.Null(Assert.Single(VciReader.ListMappings(f.Service, f.Project, f.Query()).Mappings).Path);
    }

    [Fact]
    public void HmiMapping_WithDeviceParent_ListsAndResolvesOwnerPath()
    {
        var f = new Fixture();
        var device = Assert.Single(f.Project.Devices);
        var hmi = new HmiSoftware { Name = "FixtureHmi" };
        device.DeviceItems.Items.Add(new DeviceItem { Parent = device, Container = new SoftwareContainer { Software = hmi } });
        var script = new HmiScriptModule { Name = "FixtureScript", Parent = device };
        hmi.Scripts.Items.Add(script);
        f.AddMapping(script);
        var expected = Scope(("Device", device.Name), ("HmiSoftware", hmi.Name), ("ScriptModule", script.Name));

        var mapping = Assert.Single(VciReader.ListMappings(f.Service, f.Project, f.Query()).Mappings);
        var path = Assert.IsType<List<ProjectTreeSelectorSegment>>(mapping.Path);
        Assert.Equal(expected.Select(x => (x.NodeType, x.Name)), path.Select(x => (x.NodeType, x.Name)));
        var query = f.Query(); query.Scope = expected;
        Assert.Equal(1, VciReader.ListMappings(f.Service, f.Project, query).Total);
        Assert.Same(script, Assert.Single(VciScopeResolver.Expand(f.Project, path)).Object);
    }

    [Fact]
    public void HmiMappings_WithSameModuleNames_KeepDistinctDeviceSelectors()
    {
        var f = new Fixture();
        foreach (var deviceName in new[] { "HmiA", "HmiB" })
        {
            var device = new Device { Name = deviceName };
            f.Project.Devices.Items.Add(device);
            var hmi = new HmiSoftware { Name = "Software" };
            device.DeviceItems.Items.Add(new DeviceItem { Parent = device, Container = new SoftwareContainer { Software = hmi } });
            var script = new HmiScriptModule { Name = "Shared", Parent = device };
            hmi.Scripts.Items.Add(script);
            f.AddMapping(script);
        }

        var mappings = VciReader.ListMappings(f.Service, f.Project, f.Query()).Mappings;
        Assert.Equal(new[] { "HmiA", "HmiB" }, mappings.Select(m => m.Path![0].Name));
        foreach (var mapping in mappings)
        {
            var path = Assert.IsType<List<ProjectTreeSelectorSegment>>(mapping.Path);
            var query = f.Query(); query.Scope = path;
            Assert.Single(VciReader.ListMappings(f.Service, f.Project, query).Mappings);
        }
    }

    [Fact]
    public void HmiMapping_AcceptsEqualDistinctNativeWrappers()
    {
        var f = new Fixture();
        var device = Assert.Single(f.Project.Devices);
        var hmi = new HmiSoftware { Name = "FixtureHmi" };
        device.DeviceItems.Items.Add(new DeviceItem { Parent = device, Container = new SoftwareContainer { Software = hmi } });
        var discovered = new HmiScriptModule { Name = "FixtureScript", Parent = device, NativeIdentity = "native-script" };
        hmi.Scripts.Items.Add(discovered);
        var mapped = new HmiScriptModule { Name = discovered.Name, Parent = device, NativeIdentity = discovered.NativeIdentity };
        f.AddMapping(mapped);

        var path = Assert.IsType<List<ProjectTreeSelectorSegment>>(
            Assert.Single(VciReader.ListMappings(f.Service, f.Project, f.Query()).Mappings).Path);
        Assert.Equal("FixtureHmi", path[1].Name);
        Assert.True(object.Equals(discovered, Assert.Single(VciScopeResolver.Expand(f.Project, path)).Object));
        Assert.False(ReferenceEquals(discovered, mapped));
    }

    [Fact]
    public void HmiMapping_WithAmbiguousOrUnreadableOwnership_HasNoInventedPath()
    {
        var f = new Fixture();
        var device = Assert.Single(f.Project.Devices);
        var script = new HmiScriptModule { Name = "FixtureScript", Parent = device };
        foreach (var name in new[] { "HmiA", "HmiB" })
        {
            var hmi = new HmiSoftware { Name = name };
            device.DeviceItems.Items.Add(new DeviceItem { Parent = device, Container = new SoftwareContainer { Software = hmi } });
            hmi.Scripts.Items.Add(script);
        }
        f.AddMapping(script);
        Assert.Null(Assert.Single(VciReader.ListMappings(f.Service, f.Project, f.Query()).Mappings).Path);

        var g = new Fixture();
        var otherDevice = Assert.Single(g.Project.Devices);
        var otherHmi = new HmiSoftware { Name = "ReadableHmi" };
        otherDevice.DeviceItems.Items.Add(new DeviceItem { Parent = otherDevice,
            Container = new SoftwareContainer { Software = otherHmi } });
        var otherScript = new HmiScriptModule { Name = "ReadableScript", Parent = otherDevice };
        otherHmi.Scripts.Items.Add(otherScript);
        g.AddMapping(otherScript);
        var unreadable = new EngineeringException("Synthetic unreadable owner.");
        otherDevice.DeviceItems.Items.Add(new DeviceItem { ServiceFailure = unreadable });
        Assert.Same(unreadable, Assert.Throws<EngineeringException>(
            () => VciReader.ListMappings(g.Service, g.Project, g.Query())));
    }

    [Fact]
    public void HmiMapping_OutsideProjectOrAmbiguousSoftwareName_HasNullPath()
    {
        var f = new Fixture();
        var device = Assert.Single(f.Project.Devices);
        var libraryScript = new HmiScriptModule { Name = "LibraryScript" };
        f.AddMapping(libraryScript);
        Assert.Null(Assert.Single(VciReader.ListMappings(f.Service, f.Project, f.Query()).Mappings).Path);

        var script = new HmiScriptModule { Name = "FixtureScript", Parent = device };
        for (var i = 0; i < 2; i++)
        {
            var hmi = new HmiSoftware { Name = "DuplicateHmi" };
            device.DeviceItems.Items.Add(new DeviceItem { Parent = device,
                Container = new SoftwareContainer { Software = hmi } });
            if (i == 0) hmi.Scripts.Items.Add(script);
        }
        f.AddMapping(script);
        Assert.Null(VciReader.ListMappings(f.Service, f.Project, f.Query()).Mappings.Last().Path);
    }

    [Fact]
    public void OffsetPastEnd_ReturnsEmptyPageWithTotal()
    {
        var f = new Fixture();
        var mapping = f.AddMapping(f.Leaf);
        var query = f.Query(); query.Offset = 10; query.WithStatus = true;

        var result = VciReader.ListMappings(f.Service, f.Project, query);

        Assert.Equal(1, result.Total);
        Assert.Equal(10, result.Offset);
        Assert.Empty(result.Mappings);
        Assert.Equal(0, mapping.GetStatusCalls);
    }

    [Fact]
    public void ScopeFilter_UsesWholeCaseInsensitiveSegmentsAndKeepsExcludedMappings()
    {
        var f = new Fixture();
        f.AddMapping(f.Leaf);
        var system = new PlcSystemBlockGroup { Name = "FixtureSystem", Parent = f.Plc.BlockGroup };
        f.AddMapping(new FC { Name = "SystemFixture", Parent = system });
        var unit = new PlcUnit { Name = "FixtureUnit", Parent = f.Plc };
        unit.BlockGroup.Parent = unit;
        f.AddMapping(new FC { Name = "UnitFixture", Parent = unit.BlockGroup });
        var other = new PlcSoftware { Name = f.Plc.Name + "Suffix", Parent = f.Plc.Parent };
        f.AddMapping(new FC { Name = "OtherFixture", Parent = other.BlockGroup });
        f.AddMapping(null);
        var query = f.Query();
        query.Scope = f.PlcScope.Select(x => new ProjectTreeSelectorSegment
            { NodeType = x.NodeType, Name = x.Name.ToLowerInvariant() }).ToList();

        var result = VciReader.ListMappings(f.Service, f.Project, query);

        Assert.Equal(3, result.Total);
        Assert.Contains(result.Mappings, x => x.Path!.Any(s => s.NodeType == ProjectTreeNodeTypes.SystemBlockFolder));
        Assert.Contains(result.Mappings, x => x.Path!.Any(s => s.NodeType == ProjectTreeNodeTypes.SoftwareUnit));
    }

    [Fact]
    public void EmptyScopeContainer_ReturnsZeroMappings()
    {
        var f = new Fixture();
        var empty = new PlcBlockUserGroup { Name = "EmptyFixture" };
        f.Plc.BlockGroup.Groups.Items.Add(empty);
        f.AddMapping(f.Leaf);
        var query = f.Query(); query.Scope = f.GroupScope.Concat(Scope(("BlockFolder", empty.Name))).ToList();

        Assert.Equal(0, VciReader.ListMappings(f.Service, f.Project, query).Total);
    }

    [Fact]
    public void UnknownScope_IsTargetNotFound()
    {
        var f = new Fixture();
        var query = f.Query(); query.Scope = Scope(("Device", "MissingFixture"));
        AssertFailure(WorkerFailureCategories.TargetNotFound,
            () => VciReader.ListMappings(f.Service, f.Project, query));
    }

    [Fact]
    public void UnknownWorkspace_IsTargetNotFound()
    {
        var f = new Fixture();
        var query = f.Query(); query.WorkspaceName = "MissingFixture";
        AssertFailure(WorkerFailureCategories.TargetNotFound,
            () => VciReader.ListMappings(f.Service, f.Project, query));
    }

    [Fact]
    public void WorkspaceName_IsCaseInsensitiveButMustBeUnique()
    {
        var f = new Fixture();
        var query = f.Query(); query.WorkspaceName = f.Workspace.Name.ToLowerInvariant();
        Assert.Empty(VciReader.ListMappings(f.Service, f.Project, query).Mappings);
        f.Service.WorkspaceGroup.Workspaces.Items.Add(new Workspace { Name = query.WorkspaceName });
        AssertFailure(WorkerFailureCategories.TargetAmbiguous,
            () => VciReader.ListMappings(f.Service, f.Project, query));
    }

    [Theory]
    [InlineData(-1, 100)]
    [InlineData(0, 0)]
    [InlineData(0, 201)]
    public void InvalidPaging_IsValidationError(int offset, int limit)
    {
        var f = new Fixture();
        var query = f.Query(); query.Offset = offset; query.Limit = limit;
        AssertFailure(WorkerFailureCategories.ValidationError,
            () => VciReader.ListMappings(f.Service, f.Project, query));
    }

    [Fact]
    public void SupportedFormats_ReturnsExactNamesForOneLeafUsingFirstWorkspace()
    {
        var f = new Fixture();
        f.Workspace.SupportedFormats[f.Leaf] = new[] { "SimaticML", "SCL", "liblink" };

        var result = VciReader.SupportedFormats(f.Service, f.Project, new VciQueryInfo { Scope = f.LeafScope });

        Assert.Equal(new[] { "SimaticML", "SCL", "liblink" }, result.Formats);
        Assert.Same(f.Leaf, Assert.Single(f.Workspace.SupportedFormatCalls));
    }

    [Fact]
    public void SupportedFormats_UsesRequestedWorkspace()
    {
        var f = new Fixture();
        var other = f.Service.WorkspaceGroup.Workspaces.Create("OtherFixture", f.Workspace.RootPath);
        other.SupportedFormats[f.Leaf] = new[] { "SCL" };

        Assert.Equal(new[] { "SCL" }, VciReader.SupportedFormats(f.Service, f.Project,
            new VciQueryInfo { WorkspaceName = other.Name, Scope = f.LeafScope }).Formats);
        Assert.Empty(f.Workspace.SupportedFormatCalls);
    }

    [Fact]
    public void SupportedFormats_GroupWithOneLeaf_IsValidationError()
    {
        var f = new Fixture();
        AssertFailure(WorkerFailureCategories.ValidationError,
            () => VciReader.SupportedFormats(f.Service, f.Project, new VciQueryInfo { Scope = f.GroupScope }));
        Assert.Empty(f.Workspace.SupportedFormatCalls);
    }

    [Fact]
    public void SupportedFormats_AmbiguousLeaf_IsTargetAmbiguous()
    {
        var f = new Fixture();
        f.Plc.BlockGroup.Blocks.Items.Add(new FC { Name = f.Leaf.Name });
        AssertFailure(WorkerFailureCategories.TargetAmbiguous,
            () => VciReader.SupportedFormats(f.Service, f.Project, new VciQueryInfo { Scope = f.LeafScope }));
        Assert.Empty(f.Workspace.SupportedFormatCalls);
    }

    [Fact]
    public void SupportedFormats_NoWorkspace_IsTargetNotFoundWithGuidance()
    {
        var f = new Fixture();
        f.Service.WorkspaceGroup.Workspaces.Items.Clear();
        var failure = AssertFailure(WorkerFailureCategories.TargetNotFound,
            () => VciReader.SupportedFormats(f.Service, f.Project, new VciQueryInfo { Scope = f.LeafScope }));
        Assert.Contains("create", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("vci_list_workspaces", typeof(VciWorkspaceListInfo))]
    [InlineData("vci_list_mappings", typeof(VciMappingListInfo))]
    [InlineData("vci_supported_formats", typeof(VciSupportedFormatsInfo))]
    public void Dispatch_ReturnsTheTypedReadResult(string method, Type resultType)
    {
        var f = new Fixture();
        var query = f.Query(); query.Scope = f.LeafScope;
        Assert.IsType(resultType, VciDispatch.Read(f.Project, method, query));
    }

    [Fact]
    public void Dispatch_RejectsNonStandaloneRoot()
    {
        AssertFailure(WorkerFailureCategories.UnsupportedCapability,
            () => VciDispatch.Read(new Siemens.Engineering.Multiuser.MultiuserProject(), "vci_list_workspaces", new VciQueryInfo()));
    }

    [Fact]
    public void Dispatch_RejectsMissingServiceAndUnknownMethod()
    {
        var f = new Fixture();
        AssertFailure(WorkerFailureCategories.ValidationError,
            () => VciDispatch.Read(f.Project, "vci_unknown", f.Query()));
        f.Project.VersionControlInterface = null;
        AssertFailure(WorkerFailureCategories.UnsupportedCapability,
            () => VciDispatch.Read(f.Project, "vci_list_workspaces", f.Query()));
    }

    [Theory]
    [InlineData("service")]
    [InlineData("enumeration")]
    [InlineData("path")]
    [InlineData("status")]
    [InlineData("formats")]
    public void Dispatch_PortalLossIsPortalLost(string site)
    {
        var f = new Fixture();
        var failure = new NonRecoverableException("Synthetic Portal loss.");
        var mapping = f.AddMapping(f.Leaf);
        var query = f.Query(); query.WithStatus = true; query.Scope = f.LeafScope;
        if (site == "service") f.Project.ServiceFailure = failure;
        if (site == "enumeration") f.Workspace.MappedObjects.EnumerationFailure = failure;
        if (site == "path") f.Leaf.ParentFailure = failure;
        if (site == "status") mapping.StatusFailure = failure;
        if (site == "formats") f.Workspace.NonRecoverableOnLeaf.Add(f.Leaf);

        AssertFailure(WorkerFailureCategories.PortalLost,
            () => VciDispatch.Read(f.Project, site == "formats" ? "vci_supported_formats" : "vci_list_mappings", query));
    }

    [Fact]
    public void OrdinaryUnreadablePath_PropagatesInsteadOfReturningNull()
    {
        var f = new Fixture();
        var failure = new EngineeringException("Synthetic unreadable path.");
        f.AddMapping(f.Leaf); f.Leaf.ParentFailure = failure;
        Assert.Same(failure, Assert.Throws<EngineeringException>(
            () => VciReader.ListMappings(f.Service, f.Project, f.Query())));
    }

    private static WorkerOperationException AssertFailure(string category, Action action)
    {
        var failure = Assert.Throws<WorkerOperationException>(action);
        Assert.Equal(category, failure.FailureCategory);
        return failure;
    }

    private static List<ProjectTreeSelectorSegment> Scope(params (string Type, string Name)[] segments)
        => segments.Select(x => new ProjectTreeSelectorSegment { NodeType = x.Type, Name = x.Name }).ToList();

    private sealed class Fixture
    {
        public Siemens.Engineering.Project Project { get; } = new();
        public VersionControlInterface Service { get; } = new();
        public Workspace Workspace { get; }
        public PlcSoftware Plc { get; } = new() { Name = "FixtureSoftware" };
        public FC Leaf { get; } = new() { Name = "FixtureLeaf" };
        public List<ProjectTreeSelectorSegment> PlcScope { get; }
        public List<ProjectTreeSelectorSegment> GroupScope { get; }
        public List<ProjectTreeSelectorSegment> LeafScope { get; }
        public Fixture()
        {
            Project.VersionControlInterface = Service;
            Workspace = Service.WorkspaceGroup.Workspaces.Create("FixtureWorkspace",
                new DirectoryInfo(Path.Combine(Path.GetTempPath(), "VciReaderFixture")));
            var device = new Device { Name = "FixtureDevice" };
            Project.Devices.Items.Add(device);
            var item = new DeviceItem { Parent = device, Container = new SoftwareContainer { Software = Plc } };
            device.DeviceItems.Items.Add(item); Plc.Parent = item;
            Plc.BlockGroup.Name = "FixtureBlocks";
            Plc.BlockGroup.Blocks.Items.Add(Leaf);
            PlcScope = Scope(("Device", device.Name), ("PlcSoftware", Plc.Name));
            GroupScope = PlcScope.Concat(Scope(("BlockFolder", Plc.BlockGroup.Name))).ToList();
            LeafScope = GroupScope.Concat(Scope(("FC", Leaf.Name))).ToList();
        }
        public VciQueryInfo Query() => new() { WorkspaceName = Workspace.Name };
        public MappedObject AddMapping(IEngineeringObject? leaf)
        {
            var mapping = new MappedObject
            {
                EngineeringObject = leaf, FileNameWithoutExtension = Leaf.Name,
                DirectoryPath = Workspace.RootPath, Workspace = Workspace
            };
            Workspace.MappedObjects.Items.Add(mapping);
            return mapping;
        }
    }
}
