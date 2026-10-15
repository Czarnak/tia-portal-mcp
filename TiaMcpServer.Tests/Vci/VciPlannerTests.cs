using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.VersionControl;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Vci;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Vci;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Vci;

public class VciPlannerTests
{
    [Theory]
    [InlineData("create_workspace")]
    [InlineData("delete_workspace")]
    [InlineData("export")]
    [InlineData("sync_to_workspace")]
    [InlineData("sync_to_project")]
    [InlineData("delete_mapping")]
    public void Plan_NeverCallsGetStatus(string operation)
    {
        using var f = new Fixture();
        var mapping = f.AddMapping(f.Leaf);
        mapping.StatusFailure = new InvalidOperationException("Status must not be read by a plan.");

        var result = VciPlanner.Plan(f.Project, f.Write(operation));

        Assert.Null(result.Unverifiable);
        Assert.Equal(0, mapping.GetStatusCalls);
        Assert.Empty(mapping.SynchronizeCalls);
        Assert.Empty(f.Workspace.ExportCalls);
        Assert.Equal(0, mapping.DeleteCalls);
        Assert.Equal(0, f.Workspace.DeleteCalls);
    }

    [Fact]
    public void RerunExport_AlreadyMappedLeavesAreNotCollisions()
    {
        using var f = new Fixture();
        f.AddMapping(f.Leaf);
        f.WriteFile(".xml");
        f.WriteFile(".s7res");
        f.Workspace.ThrowOnLeaf[f.Leaf] = new EngineeringException("Mapped leaves need no format resolution.");

        var plan = VciPlanner.Plan(f.Project, f.Write());

        Assert.Equal(1, plan.Leaves);
        Assert.Equal(1, plan.MappedLeaves);
        Assert.Equal(0, plan.FormatFallbacks);
        Assert.Empty(plan.KeyCollisions);
        Assert.Empty(plan.Unrepresentable);
        Assert.Null(plan.Unverifiable);
        Assert.Empty(f.Workspace.SupportedFormatCalls);
    }

    [Theory]
    [InlineData(".xml")]
    [InlineData(".s7res")]
    [InlineData(".unrelated")]
    public void LeftoverFileOnDisk_IsCollision(string extension)
    {
        using var f = new Fixture();
        f.WriteFile(extension);

        var plan = VciPlanner.Plan(f.Project, f.Write());

        Assert.Contains(f.Leaf.Name, Assert.Single(plan.KeyCollisions));
        Assert.Null(plan.Unverifiable);
    }

    [Fact]
    public void DifferentStemOnDisk_IsNotCollision()
    {
        using var f = new Fixture();
        f.WriteFile("Suffix.xml");
        Assert.Empty(VciPlanner.Plan(f.Project, f.Write()).KeyCollisions);
    }

    [Fact]
    public void ExistingMappingKey_IsCollision()
    {
        using var f = new Fixture();
        var mapping = f.AddMapping(new FC { Name = "OtherFixture" });
        mapping.DirectoryPath = new DirectoryInfo(f.TargetDirectory.ToUpperInvariant());
        mapping.FileNameWithoutExtension = f.Leaf.Name.ToLowerInvariant();

        var plan = VciPlanner.Plan(f.Project, f.Write());

        Assert.Contains(f.Leaf.Name, Assert.Single(plan.KeyCollisions));
        Assert.Equal(0, plan.MappedLeaves);
        Assert.Null(plan.Unverifiable);
    }

    [Fact]
    public void DuplicateKeyWithinItem_IsCollision()
    {
        using var f = new Fixture();
        f.Plc.BlockGroup.Blocks.Items.Add(new FC { Name = f.Leaf.Name.ToLowerInvariant() });

        var plan = VciPlanner.Plan(f.Project, f.Write());

        Assert.Equal(2, plan.Leaves);
        Assert.Contains(f.Leaf.Name.ToLowerInvariant(), Assert.Single(plan.KeyCollisions));
    }

    [Theory]
    [InlineData("delete_workspace")]
    [InlineData("export")]
    [InlineData("sync_to_workspace")]
    [InlineData("sync_to_project")]
    [InlineData("delete_mapping")]
    public void UnknownWorkspace_IsTargetNotFound(string operation)
    {
        using var f = new Fixture();
        var write = f.Write(operation); write.WorkspaceName = "MissingFixture";
        AssertFailure(WorkerFailureCategories.TargetNotFound, () => VciPlanner.Plan(f.Project, write));
    }

    [Theory]
    [InlineData("export")]
    [InlineData("sync_to_workspace")]
    [InlineData("sync_to_project")]
    public void RootlessWorkspace_ExportIsValidationError(string operation)
    {
        using var f = new Fixture();
        f.Workspace.RootPath = null!;
        AssertFailure(WorkerFailureCategories.ValidationError, () => VciPlanner.Plan(f.Project, f.Write(operation)));
    }

    [Theory]
    [InlineData("delete_workspace")]
    [InlineData("delete_mapping")]
    public void RootlessWorkspace_DeleteDoesNotRequireRoot(string operation)
    {
        using var f = new Fixture();
        f.Workspace.RootPath = null!;
        Assert.Null(VciPlanner.Plan(f.Project, f.Write(operation)).Unverifiable);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("Trailing.")]
    [InlineData("Embedded\\Separator")]
    [InlineData("Embedded/Separator")]
    public void UnrepresentableLeaf_ReportedByPlan(string name)
    {
        using var f = new Fixture();
        f.Leaf.Name = name;

        var plan = VciPlanner.Plan(f.Project, f.Write());

        Assert.Contains(name, Assert.Single(plan.Unrepresentable));
        Assert.Empty(plan.KeyCollisions);
        Assert.Null(plan.Unverifiable);
        Assert.Empty(f.Workspace.ExportCalls);
    }

    [Fact]
    public void UnrepresentableAncestor_ReportedWithLeaf()
    {
        using var f = new Fixture();
        var group = new PlcBlockUserGroup { Name = "Bad\\Group" };
        f.Plc.BlockGroup.Blocks.Items.Clear();
        group.Blocks.Items.Add(f.Leaf);
        f.Plc.BlockGroup.Groups.Items.Add(group);

        var plan = VciPlanner.Plan(f.Project, f.Write());

        var problem = Assert.Single(plan.Unrepresentable);
        Assert.Contains(group.Name, problem);
        Assert.Contains(f.Leaf.Name, problem);
    }

    [Theory]
    [InlineData("SCL", "SimaticML", 1)]
    [InlineData("simaticml", "SimaticML", 0)]
    [InlineData(null, "UDT", 1)]
    public void FormatFallbacks_Counted(string? requested, string supported, int expected)
    {
        using var f = new Fixture();
        f.Workspace.SupportedFormats[f.Leaf] = new[] { supported };
        var write = f.Write(); write.Format = requested;
        var plan = VciPlanner.Plan(f.Project, write);
        Assert.Equal(expected, plan.FormatFallbacks);
        Assert.Null(plan.Unverifiable);
    }

    [Fact]
    public void DefaultPlcSdOnly_IsUnverifiableInsteadOfUnsafeFallback()
    {
        using var f = new Fixture();
        f.Workspace.SupportedFormats[f.Leaf] = new[] { "SimaticSD" };
        var plan = VciPlanner.Plan(f.Project, f.Write());
        Assert.NotNull(plan.Unverifiable);
        Assert.Equal(0, plan.FormatFallbacks);
        Assert.Empty(f.Workspace.ExportCalls);
    }

    [Fact]
    public void RootComparison_IgnoresCaseAndTrailingSeparator()
    {
        using var f = new Fixture();
        var write = f.Write("create_workspace");
        write.WorkspaceName = "NewFixture";
        write.RootPath = f.Root.ToUpperInvariant() + Path.DirectorySeparatorChar;

        Assert.NotNull(VciPlanner.Plan(f.Project, write).WorkspaceConflict);
    }

    [Fact]
    public void Create_DuplicateNameIsConflictRegardlessOfRoot()
    {
        using var f = new Fixture();
        var write = f.Write("create_workspace");
        write.WorkspaceName = f.Workspace.Name.ToLowerInvariant();
        write.RootPath = Path.Combine(f.Root, "OtherRoot");
        Assert.NotNull(VciPlanner.Plan(f.Project, write).WorkspaceConflict);
    }

    [Fact]
    public void Create_DifferentNameAndRootHasNoConflict()
    {
        using var f = new Fixture();
        var write = f.Write("create_workspace");
        write.WorkspaceName = "NewFixture";
        write.RootPath = Path.Combine(f.Root, "OtherRoot");
        var plan = VciPlanner.Plan(f.Project, write);
        Assert.Null(plan.WorkspaceConflict);
        Assert.Null(plan.Unverifiable);
        Assert.False(Directory.Exists(write.RootPath));
    }

    [Theory]
    [InlineData("formats")]
    [InlineData("mappings")]
    [InlineData("service")]
    [InlineData("scope")]
    public void OrdinaryReadFailure_IsUnverifiable(string site)
    {
        using var f = new Fixture();
        f.SetFailure(site, new EngineeringException("Synthetic unreadable state."));

        var plan = VciPlanner.Plan(f.Project, f.Write());

        Assert.Contains("Synthetic unreadable state.", plan.Unverifiable);
    }

    [Theory]
    [InlineData("formats")]
    [InlineData("mappings")]
    [InlineData("service")]
    [InlineData("scope")]
    public void FatalReadFailure_EscapesPlannerAndMapsToPortalLostAtDispatch(string site)
    {
        using var f = new Fixture();
        f.SetFailure(site, new NonRecoverableException("Synthetic Portal loss."));

        Assert.Throws<NonRecoverableException>(() => VciPlanner.Plan(f.Project, f.Write()));
        AssertFailure(WorkerFailureCategories.PortalLost, () => VciDispatch.Plan(f.Project, f.Write()));
    }

    [Fact]
    public void Lists_AreCappedAtTen()
    {
        using var f = new Fixture();
        f.Plc.BlockGroup.Blocks.Items.Clear();
        for (var i = 0; i < 15; i++)
        {
            f.Plc.BlockGroup.Blocks.Items.Add(new FC { Name = "Invalid" + i + "." });
            f.Plc.BlockGroup.Blocks.Items.Add(new FC { Name = "Collision" + i });
            f.Plc.BlockGroup.Blocks.Items.Add(new FC { Name = "Collision" + i });
        }

        var plan = VciPlanner.Plan(f.Project, f.Write());

        Assert.Equal(45, plan.Leaves);
        Assert.Equal(10, plan.Unrepresentable.Count);
        Assert.Equal(10, plan.KeyCollisions.Count);
    }

    [Theory]
    [InlineData("sync_to_workspace")]
    [InlineData("sync_to_project")]
    [InlineData("delete_mapping")]
    public void ScopeOperations_CountOnlyMappedLeavesWithoutFormats(string operation)
    {
        using var f = new Fixture();
        f.AddMapping(f.Leaf);
        f.Plc.BlockGroup.Blocks.Items.Add(new FC { Name = "UnmappedFixture" });
        f.Workspace.ThrowOnLeaf[f.Leaf] = new EngineeringException("No format reads on sync/delete.");
        var plan = VciPlanner.Plan(f.Project, f.Write(operation));
        Assert.Equal(2, plan.Leaves);
        Assert.Equal(1, plan.MappedLeaves);
        Assert.Equal(f.Root, plan.WorkspaceRootPath);
        Assert.Null(plan.Unverifiable);
        Assert.Empty(f.Workspace.SupportedFormatCalls);
    }

    [Theory]
    [InlineData("export")]
    [InlineData("sync_to_workspace")]
    [InlineData("sync_to_project")]
    [InlineData("delete_mapping")]
    public void EmptyContainer_HasZeroCounts(string operation)
    {
        using var f = new Fixture();
        f.Plc.BlockGroup.Blocks.Items.Clear();
        var plan = VciPlanner.Plan(f.Project, f.Write(operation));
        Assert.Equal(0, plan.Leaves);
        Assert.Equal(0, plan.MappedLeaves);
        Assert.Null(plan.Unverifiable);
    }

    [Fact]
    public void UnsafeRelativeDirectory_IsValidationError()
    {
        using var f = new Fixture();
        var write = f.Write(); write.RelativeDirectory = "..\\Escape";
        AssertFailure(WorkerFailureCategories.ValidationError, () => VciPlanner.Plan(f.Project, write));
        Assert.Empty(f.Workspace.SupportedFormatCalls);
    }

    [Fact]
    public void MirroredDirectoryIsFile_IsUnverifiable()
    {
        using var f = new Fixture();
        Directory.CreateDirectory(Path.GetDirectoryName(f.TargetDirectory)!);
        File.WriteAllText(f.TargetDirectory, "fixture");
        Assert.NotNull(VciPlanner.Plan(f.Project, f.Write()).Unverifiable);
    }

    [Fact]
    public void UnknownScope_IsTargetNotFound()
    {
        using var f = new Fixture();
        var write = f.Write(); write.Scope![0].Name = "MissingFixture";
        AssertFailure(WorkerFailureCategories.TargetNotFound, () => VciPlanner.Plan(f.Project, write));
    }

    [Fact]
    public void Dispatch_RequiresStandaloneServiceAndKnownOperation()
    {
        using var f = new Fixture();
        AssertFailure(WorkerFailureCategories.UnsupportedCapability,
            () => VciDispatch.Plan(new Siemens.Engineering.Multiuser.MultiuserProject(), f.Write()));
        var write = f.Write(); write.Operation = "unknown";
        AssertFailure(WorkerFailureCategories.ValidationError, () => VciDispatch.Plan(f.Project, write));
        f.Project.VersionControlInterface = null;
        AssertFailure(WorkerFailureCategories.UnsupportedCapability, () => VciDispatch.Plan(f.Project, f.Write()));
    }

    private static void AssertFailure(string category, Action action)
        => Assert.Equal(category, Assert.Throws<WorkerOperationException>(action).FailureCategory);

    private sealed class Fixture : IDisposable
    {
        public Siemens.Engineering.Project Project { get; } = new();
        public VersionControlInterface Service { get; } = new();
        public Workspace Workspace { get; }
        public PlcSoftware Plc { get; } = new() { Name = "FixtureSoftware" };
        public FC Leaf { get; } = new() { Name = "FixtureLeaf" };
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "VciPlannerFixture-" + Guid.NewGuid().ToString("N"));
        public List<ProjectTreeSelectorSegment> Scope { get; }
        public string TargetDirectory => Path.Combine(Root, "FixtureDevice", Plc.Name, "Program blocks");

        public Fixture()
        {
            Project.VersionControlInterface = Service;
            Workspace = Service.WorkspaceGroup.Workspaces.Create("FixtureWorkspace", new DirectoryInfo(Root));
            var device = new Device { Name = "FixtureDevice" };
            Project.Devices.Items.Add(device);
            var item = new DeviceItem { Parent = device, Container = new SoftwareContainer { Software = Plc } };
            device.DeviceItems.Items.Add(item); Plc.Parent = item;
            Plc.BlockGroup.Name = "FixtureBlocks";
            Plc.BlockGroup.Blocks.Items.Add(Leaf);
            Scope = new[] { ("Device", device.Name), ("PlcSoftware", Plc.Name), ("BlockFolder", Plc.BlockGroup.Name) }
                .Select(x => new ProjectTreeSelectorSegment { NodeType = x.Item1, Name = x.Item2 }).ToList();
        }

        public VciWriteRequestInfo Write(string operation = "export")
            => new() { Operation = operation, WorkspaceName = Workspace.Name, RootPath = Root, Scope = Scope };

        public MappedObject AddMapping(IEngineeringObject leaf)
        {
            var mapping = new MappedObject { EngineeringObject = leaf, DirectoryPath = new DirectoryInfo(TargetDirectory),
                FileNameWithoutExtension = Leaf.Name, Workspace = Workspace };
            Workspace.MappedObjects.Items.Add(mapping);
            return mapping;
        }

        public void WriteFile(string extension)
        {
            Directory.CreateDirectory(TargetDirectory);
            File.WriteAllText(Path.Combine(TargetDirectory, Leaf.Name + extension), "fixture");
        }

        public void SetFailure(string site, Exception failure)
        {
            if (site == "formats") Workspace.ThrowOnLeaf[Leaf] = failure;
            if (site == "mappings") Workspace.MappedObjects.EnumerationFailure = failure;
            if (site == "service") Project.ServiceFailure = failure;
            if (site == "scope") Project.Devices.EnumerationFailure = failure;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
