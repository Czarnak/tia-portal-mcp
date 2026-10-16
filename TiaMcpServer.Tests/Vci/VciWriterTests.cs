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

public class VciWriterTests
{
    [Fact]
    public void RefusedExportLeaf_CountsFailedAndContinues()
    {
        using var f = new Fixture();
        f.Workspace.OnExport = leaf => { if (Equals(leaf, f.First)) throw new EngineeringException("Refused fixture."); };
        var outcome = f.Failure("export");
        Assert.Equal(2, outcome.Leaves);
        Assert.Equal(1, outcome.Failed);
        Assert.Equal(1, outcome.Succeeded);
        Assert.True(outcome.ProjectModified);
        Assert.Contains(VciNotes.SaveProject, outcome.Notes);
        Assert.Null(outcome.LastAttempted);
        Assert.Equal(2, f.Workspace.ExportCalls.Count);
    }

    [Fact]
    public void Export_CreatesMirroredDirectoryAndUsesRootedRelativeArgument()
    {
        using var f = new Fixture();
        f.Workspace.OnExport = _ => Assert.True(Directory.Exists(f.Target));
        var outcome = f.Run("export");
        Assert.Equal(2, outcome.Succeeded);
        Assert.All(f.Workspace.ExportCalls, call => Assert.Equal("\\FixtureDevice\\FixtureSoftware\\Program blocks", call.Directory.ToString()));
    }

    [Fact]
    public void AllEqual_OpensNoTransaction()
    {
        using var f = new Fixture();
        var maps = f.MapBoth(CompareState.Equal);
        var outcome = f.Run("sync_to_project");
        Assert.Equal(2, outcome.Skipped.Equal);
        Assert.False(outcome.ProjectModified);
        Assert.Empty(f.Portal.Transactions);
        Assert.Empty(f.Portal.ExclusiveAccessCalls);
        Assert.All(maps, m => { Assert.Equal(1, m.GetStatusCalls); Assert.Empty(m.SynchronizeCalls); });
    }

    [Fact]
    public void SyncToProject_LeafThrows_RollsBackAndCountsAllFailed()
    {
        using var f = new Fixture();
        var maps = f.MapBoth();
        maps[0].SynchronizeFailure = new EngineeringException("Refused fixture.");
        var outcome = f.Failure("sync_to_project");
        Assert.Equal(2, outcome.Failed);
        Assert.Equal(0, outcome.Succeeded);
        Assert.False(outcome.ProjectModified);
        Assert.False(Assert.Single(f.Portal.Transactions).CommitRequested);
        Assert.True(f.Portal.Transactions[0].Disposed);
        Assert.Single(f.Portal.ExclusiveAccessCalls);
    }

    [Fact]
    public void SyncToProject_CanCommitFalse_DoesNotCommit()
    {
        using var f = new Fixture();
        var maps = f.MapBoth();
        maps[1].OnSynchronize = _ => f.Portal.Transactions[0].CanCommit = false;
        var outcome = f.Failure("sync_to_project");
        Assert.Equal(2, outcome.Failed);
        Assert.Equal(0, outcome.Succeeded);
        Assert.False(outcome.ProjectModified);
        Assert.False(Assert.Single(f.Portal.Transactions).CommitRequested);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SyncToProject_AccessReleaseFailure_PreservesConfirmedCommit(bool fatal)
    {
        using var f = new Fixture();
        f.MapBoth();
        f.Portal.VciExclusiveAccessDisposeFailure = fatal
            ? new NonRecoverableException("Lost on access release.") : new EngineeringException("Access release failed.");

        var ex = Assert.Throws<WorkerOperationException>(() => f.Run("sync_to_project"));

        Assert.Equal(fatal ? WorkerFailureCategories.PortalLost : WorkerFailureCategories.WorkerOperationFailed, ex.FailureCategory);
        var outcome = Assert.IsType<VciWriteOutcomeInfo>(ex.VciWriteOutcome);
        Assert.True(Assert.Single(f.Portal.Transactions).Committed);
        Assert.Equal(2, outcome.Succeeded);
        Assert.Equal(0, outcome.Failed);
        Assert.True(outcome.ProjectModified);
        Assert.Contains(VciNotes.SaveProject, outcome.Notes);
        Assert.Equal(fatal, outcome.LastAttempted is not null);
        Assert.False(File.Exists(f.Request.BreadcrumbPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SyncToProject_TransactionDisposeFailure_DoesNotClaimCommit(bool fatal)
    {
        using var f = new Fixture();
        f.MapBoth()[1].OnSynchronize = _ => f.Portal.Transactions[0].DisposeFailure = fatal
            ? new NonRecoverableException("Lost on transaction disposal.") : new EngineeringException("Transaction disposal failed.");

        var ex = Assert.Throws<WorkerOperationException>(() => f.Run("sync_to_project"));

        Assert.Equal(fatal ? WorkerFailureCategories.PortalLost : WorkerFailureCategories.WorkerOperationFailed, ex.FailureCategory);
        var outcome = Assert.IsType<VciWriteOutcomeInfo>(ex.VciWriteOutcome);
        Assert.True(Assert.Single(f.Portal.Transactions).CommitRequested);
        Assert.False(f.Portal.Transactions[0].Committed);
        Assert.Equal(0, outcome.Succeeded);
        Assert.Equal(2, outcome.Failed);
        Assert.False(outcome.ProjectModified);
        Assert.DoesNotContain(VciNotes.SaveProject, outcome.Notes);
        Assert.False(File.Exists(f.Request.BreadcrumbPath));
    }

    [Fact]
    public void SyncToProject_CommitsOneTransactionAndResolvesFreshMapping()
    {
        using var f = new Fixture();
        var maps = f.MapBoth();
        MappedObject? replacement = null;
        maps[0].OnSynchronize = _ =>
        {
            f.Workspace.MappedObjects.Items.Remove(maps[1]);
            replacement = f.Map(new FC { Name = f.Second.Name });
            maps[1].SynchronizeFailure = new EngineeringException("Stale mapping used.");
        };
        var outcome = f.Run("sync_to_project");
        Assert.Equal(2, outcome.Succeeded);
        Assert.True(outcome.ProjectModified);
        Assert.True(Assert.Single(f.Portal.Transactions).Committed);
        Assert.Single(replacement!.SynchronizeCalls);
        Assert.Empty(maps[1].SynchronizeCalls);
        Assert.Contains(VciNotes.ImportedNotValidated, outcome.Notes);
        Assert.Contains(VciNotes.SaveProject, outcome.Notes);
    }

    [Theory]
    [InlineData("sync_to_workspace", CompareState.Unequal, IndividualObjectCompareDetails.WorkspaceFileChanged, "target")]
    [InlineData("sync_to_project", CompareState.Unequal, IndividualObjectCompareDetails.ProjectObjectChanged, "target")]
    [InlineData("sync_to_workspace", CompareState.Unequal, IndividualObjectCompareDetails.ProjectObjectChanged | IndividualObjectCompareDetails.WorkspaceFileChanged, "conflict")]
    [InlineData("sync_to_project", CompareState.Unknown, IndividualObjectCompareDetails.None, "conflict")]
    [InlineData("sync_to_workspace", CompareState.WorkspaceFileMissing, IndividualObjectCompareDetails.None, "target")]
    public void Skips_AreCounted(string operation, CompareState state, IndividualObjectCompareDetails details, string bucket)
    {
        using var f = new Fixture();
        var maps = f.MapBoth(state, details);
        var outcome = f.Run(operation);
        Assert.Equal(2, bucket == "target" ? outcome.Skipped.TargetChanged : outcome.Skipped.Conflict);
        Assert.All(maps, m => Assert.Empty(m.SynchronizeCalls));
    }

    [Fact]
    public void MissingImportFile_FailsWithoutTransaction()
    {
        using var f = new Fixture();
        f.MapBoth(CompareState.WorkspaceFileMissing);
        Assert.Equal(2, f.Failure("sync_to_project").Failed);
        Assert.Empty(f.Portal.Transactions);
    }

    [Theory]
    [InlineData("export")]
    [InlineData("sync_to_project")]
    public void NonRecoverable_ThrowsPortalLostWithCountsAndLastAttempted(string operation)
    {
        using var f = new Fixture();
        if (operation == "export")
            f.Workspace.OnExport = leaf => { if (Equals(leaf, f.Second)) throw new NonRecoverableException("Lost fixture."); };
        else
            f.MapBoth()[1].SynchronizeFailure = new NonRecoverableException("Lost fixture.");
        var ex = Assert.Throws<WorkerOperationException>(() => f.Run(operation));
        Assert.Equal(WorkerFailureCategories.PortalLost, ex.FailureCategory);
        Assert.Contains("restart TIA Portal", ex.Message);
        Assert.Contains("do not retry", ex.Message);
        var outcome = Assert.IsType<VciWriteOutcomeInfo>(ex.VciWriteOutcome);
        Assert.Equal(f.Second.Name, outcome.LastAttempted!.Path.Last().Name);
        Assert.Equal(operation, outcome.LastAttempted.Operation);
        Assert.Equal(operation == "export" ? 1 : 0, outcome.Succeeded);
        Assert.Equal(operation == "export" ? 1 : 2, outcome.Failed);
        Assert.False(File.Exists(f.Request.BreadcrumbPath));
    }

    [Fact]
    public void FatalStatusRead_RecordsLeafBeforeTransaction()
    {
        using var f = new Fixture();
        f.MapBoth()[0].StatusFailure = new NonRecoverableException("Lost during status.");
        var ex = Assert.Throws<WorkerOperationException>(() => f.Run("sync_to_project"));
        Assert.Equal(WorkerFailureCategories.PortalLost, ex.FailureCategory);
        Assert.Equal(f.First.Name, ex.VciWriteOutcome!.LastAttempted!.Path.Last().Name);
        Assert.Empty(f.Portal.Transactions);
        Assert.False(File.Exists(f.Request.BreadcrumbPath));
    }

    [Fact]
    public void StatusFiltering_WritesBreadcrumbBeforeEveryReadOutsideTransaction()
    {
        using var f = new Fixture();
        var maps = f.MapBoth();
        var seen = new List<string>();
        foreach (var mapping in maps)
            mapping.OnGetStatus = () =>
            {
                Assert.Empty(f.Portal.Transactions);
                Assert.Empty(f.Portal.ExclusiveAccessCalls);
                var breadcrumb = VciBreadcrumbFile.Take(f.Request.BreadcrumbPath!, f.Request.RequestId!);
                Assert.Equal(mapping.FileNameWithoutExtension, breadcrumb!.Path.Last().Name);
                Assert.Equal("sync_to_project", breadcrumb.Operation);
                Assert.Equal(mapping.FileFormat, breadcrumb.Format);
                seen.Add(breadcrumb.Path.Last().Name);
            };
        f.Run("sync_to_project");
        Assert.Equal(new[] { f.First.Name, f.Second.Name }, seen);
        Assert.False(File.Exists(f.Request.BreadcrumbPath));
    }

    [Fact]
    public void Breadcrumb_WrittenBeforeEachLeaf_RemovedAtEnd()
    {
        using var f = new Fixture();
        var recorded = new List<string>();
        f.Workspace.OnExport = leaf =>
        {
            var breadcrumb = VciBreadcrumbFile.Take(f.Request.BreadcrumbPath!, f.Request.RequestId!);
            Assert.Equal(((FC)leaf).Name, breadcrumb!.Path.Last().Name);
            Assert.Equal("SimaticML", breadcrumb.Format);
            recorded.Add(breadcrumb.Path.Last().Name);
        };
        f.Run("export");
        Assert.Equal(new[] { f.First.Name, f.Second.Name }, recorded);
        Assert.False(File.Exists(f.Request.BreadcrumbPath));
    }

    [Fact]
    public void BreadcrumbWriteFailure_DoesNotFailOperation()
    {
        using var f = new Fixture();
        f.Request.BreadcrumbPath = f.Root;
        Directory.CreateDirectory(f.Root);
        Assert.Equal(2, f.Run("export").Succeeded);
    }

    [Theory]
    [InlineData("delete_workspace")]
    [InlineData("delete_mapping")]
    public void Deletes_KeepFilesAndCarryFixedNote(string operation)
    {
        using var f = new Fixture();
        f.MapBoth();
        Directory.CreateDirectory(f.Target);
        var file = Path.Combine(f.Target, "Fixture.txt"); File.WriteAllText(file, "fixture");
        var outcome = f.Run(operation);
        Assert.Contains(VciNotes.FilesRemain, outcome.Notes);
        Assert.Contains(VciNotes.SaveProject, outcome.Notes);
        Assert.True(File.Exists(file));
        if (operation == "delete_mapping") Assert.Empty(f.Workspace.MappedObjects);
        else Assert.Empty(f.Service.WorkspaceGroup.Workspaces);
    }

    [Fact]
    public void AlreadyMapped_SkipsOwnFilesAndFormatLookup()
    {
        using var f = new Fixture();
        f.MapBoth();
        f.Workspace.ThrowOnLeaf[f.First] = new EngineeringException("Do not resolve formats.");
        Directory.CreateDirectory(f.Target);
        File.WriteAllText(Path.Combine(f.Target, f.First.Name + ".xml"), "fixture");
        var outcome = f.Run("export");
        Assert.Equal(2, outcome.Skipped.AlreadyMapped);
        Assert.Empty(f.Workspace.ExportCalls);
        Assert.False(outcome.ProjectModified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LateFileOrMappingCollision_RefusesLeafWithoutOverwrite(bool mapping)
    {
        using var f = new Fixture();
        Assert.Empty(VciPlanner.Plan(f.Project, f.Request).KeyCollisions);
        f.Workspace.OnExport = leaf =>
        {
            if (!Equals(leaf, f.First)) return;
            if (mapping) f.Map(new FC { Name = "OtherFixture" }).FileNameWithoutExtension = f.Second.Name;
            else File.WriteAllText(Path.Combine(f.Target, f.Second.Name + ".xml"), "retained");
        };
        var outcome = f.Failure("export");
        Assert.Equal(1, outcome.Succeeded);
        Assert.Equal(1, outcome.Failed);
        Assert.Single(f.Workspace.ExportCalls);
        if (!mapping) Assert.Equal("retained", File.ReadAllText(Path.Combine(f.Target, f.Second.Name + ".xml")));
    }

    [Theory]
    [InlineData(0, 3, 2)]
    [InlineData(20, 123, 82)]
    public void Export_MappingWorkGrowsWithExistingMappings(int backgroundMappings, int enumerated, int propertyReads)
    {
        using var f = new Fixture();
        for (var i = 0; i < backgroundMappings; i++) f.Map(new FC { Name = "Background" + i });

        var outcome = f.Run("export");

        Assert.Equal(2, outcome.Succeeded);
        Assert.Equal(2, f.Workspace.MappedObjects.FindCalls);
        Assert.Equal(6, f.Workspace.MappedObjects.EnumerationStarts);
        Assert.Equal(enumerated, f.Workspace.MappedObjects.EnumeratedMappings);
        Assert.Equal(propertyReads, f.Workspace.MappedObjects.Items.Sum(mapping => mapping.DirectoryPathReads));
        Assert.Equal(propertyReads, f.Workspace.MappedObjects.Items.Sum(mapping => mapping.FileNameReads));
        Assert.Equal(2, f.Workspace.ExportCalls.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Export_CollisionAfterDirectoryCreation_IsRefused(bool mapping)
    {
        using var f = new Fixture();
        f.Plc.BlockGroup.Blocks.Items.Remove(f.Second);
        f.Workspace.MappedObjects.OnEnumerationStart = scan =>
        {
            if (scan != 3) return;
            if (mapping) f.Map(new FC { Name = "OtherFixture" }).FileNameWithoutExtension = f.First.Name;
            else File.WriteAllText(Path.Combine(f.Target, f.First.Name + ".xml"), "retained");
        };

        var outcome = f.Failure("export");

        Assert.Equal(1, outcome.Leaves);
        Assert.Equal(1, outcome.Failed);
        Assert.Empty(f.Workspace.ExportCalls);
        Assert.Equal(3, f.Workspace.MappedObjects.EnumerationStarts);
        if (!mapping) Assert.Equal("retained", File.ReadAllText(Path.Combine(f.Target, f.First.Name + ".xml")));
    }

    [Fact]
    public void LateMirroredDirectoryFile_RefusesBeforeExport()
    {
        using var f = new Fixture();
        Assert.Null(VciPlanner.Plan(f.Project, f.Request).Unverifiable);
        Directory.CreateDirectory(Path.GetDirectoryName(f.Target)!);
        File.WriteAllText(f.Target, "retained");
        Assert.Equal(2, f.Failure("export").Failed);
        Assert.Empty(f.Workspace.ExportCalls);
    }

    [Fact]
    public void Create_RechecksRootConflictBeforeDirectoryOrApiMutation()
    {
        using var f = new Fixture();
        f.Request.Operation = "create_workspace";
        f.Request.WorkspaceName = "OtherWorkspace";
        f.Request.RootPath = Path.Combine(f.Root, "NewRoot");
        Assert.Null(VciPlanner.Plan(f.Project, f.Request).WorkspaceConflict);
        f.Workspace.RootPath = new DirectoryInfo(f.Request.RootPath);
        var before = f.Service.WorkspaceGroup.Workspaces.CreateCalls.Count;
        var ex = Assert.Throws<WorkerOperationException>(() => f.Run("create_workspace"));
        Assert.Equal(WorkerFailureCategories.StateChanged, ex.FailureCategory);
        Assert.False(Directory.Exists(f.Request.RootPath));
        Assert.Equal(before, f.Service.WorkspaceGroup.Workspaces.CreateCalls.Count);
    }

    [Fact]
    public void Create_MakesRootAndMarksProjectModified()
    {
        using var f = new Fixture();
        f.Request.WorkspaceName = "NewWorkspace";
        f.Request.RootPath = Path.Combine(f.Root, "NewRoot");
        Directory.CreateDirectory(f.Root);
        var outcome = f.Run("create_workspace");
        Assert.True(Directory.Exists(f.Request.RootPath));
        Assert.True(outcome.ProjectModified);
        Assert.Equal(1, outcome.Succeeded);
    }

    [Fact]
    public void Create_RechecksUnsafeRootBeforeMutation()
    {
        using var f = new Fixture();
        f.Request.WorkspaceName = "NewWorkspace";
        f.Request.RootPath = Path.Combine(f.Root, "NewRoot");
        Directory.CreateDirectory(f.Root);
        f.Request.Operation = "create_workspace";
        Assert.Null(VciPlanner.Plan(f.Project, f.Request).WorkspaceConflict);
        File.WriteAllText(f.Request.RootPath, "retained");
        var before = f.Service.WorkspaceGroup.Workspaces.CreateCalls.Count;
        var ex = Assert.Throws<WorkerOperationException>(() => f.Run("create_workspace"));
        Assert.Equal(WorkerFailureCategories.StateChanged, ex.FailureCategory);
        Assert.Equal(1, ex.VciWriteOutcome!.Failed);
        Assert.False(ex.VciWriteOutcome.ProjectModified);
        Assert.Equal(before, f.Service.WorkspaceGroup.Workspaces.CreateCalls.Count);
        Assert.Equal("retained", File.ReadAllText(f.Request.RootPath));
    }

    [Theory]
    [InlineData("sync_to_project", 0)]
    [InlineData("sync_to_project", 1)]
    [InlineData("sync_to_workspace", 0)]
    [InlineData("sync_to_workspace", 1)]
    [InlineData("delete_mapping", 0)]
    [InlineData("delete_mapping", 1)]
    public void MappedOperations_CountOnlyMappedScopeLeaves(string operation, int mapped)
    {
        using var f = new Fixture();
        if (mapped == 1) f.Map(f.First).StatusResult.CompareState = CompareState.Equal;
        var outcome = f.Run(operation);
        Assert.Equal(mapped, outcome.Leaves);
        Assert.Equal(0, outcome.Failed);
        Assert.Equal(mapped, operation == "delete_mapping" ? outcome.Succeeded : outcome.Skipped.Equal);
        Assert.Empty(f.Portal.Transactions);
    }

    [Theory]
    [InlineData("export")]
    [InlineData("sync_to_project")]
    [InlineData("sync_to_workspace")]
    [InlineData("delete_mapping")]
    public void EmptyContainer_SucceedsWithZeroCounts(string operation)
    {
        using var f = new Fixture();
        f.Plc.BlockGroup.Blocks.Items.Clear();
        var outcome = f.Run(operation);
        Assert.Equal(0, outcome.Leaves);
        Assert.Equal(0, outcome.Succeeded);
        Assert.Equal(0, outcome.Failed);
        Assert.False(outcome.ProjectModified);
        Assert.Empty(f.Portal.Transactions);
    }

    [Fact]
    public void SyncToWorkspace_RefusedLeafContinuesAndNoTransaction()
    {
        using var f = new Fixture();
        var maps = f.MapBoth(CompareState.Unequal, IndividualObjectCompareDetails.ProjectObjectChanged);
        maps[0].SynchronizeFailure = new EngineeringException("Refused fixture.");
        var outcome = f.Failure("sync_to_workspace");
        Assert.Equal(1, outcome.Succeeded);
        Assert.Equal(1, outcome.Failed);
        Assert.False(outcome.ProjectModified);
        Assert.Single(maps[1].SynchronizeCalls);
        Assert.Empty(f.Portal.Transactions);
        Assert.False(File.Exists(f.Request.BreadcrumbPath));
    }

    [Fact]
    public void FormatFallbacks_AreCountedAndSupportedSpellingIsUsed()
    {
        using var f = new Fixture();
        f.Request.Format = "scl";
        f.Workspace.SupportedFormats[f.First] = new[] { "SCL" };
        var outcome = f.Run("export");
        Assert.Equal(1, outcome.FormatFallbacks);
        Assert.Equal(new[] { "SCL", "SimaticML" }, f.Workspace.ExportCalls.Select(c => c.Format));
    }

    [Fact]
    public void Dispatch_RejectsLocalSessionAndMethodMismatch()
    {
        using var f = new Fixture();
        Assert.Equal(WorkerFailureCategories.UnsupportedCapability,
            Assert.Throws<WorkerOperationException>(() => VciDispatch.Write(f.Portal,
                new Siemens.Engineering.Multiuser.MultiuserProject(), "vci_export", f.Request)).FailureCategory);
        Assert.Equal(WorkerFailureCategories.ValidationError,
            Assert.Throws<WorkerOperationException>(() => VciWriter.Write(f.Portal,
                f.Project, "vci_delete_workspace", f.Request)).FailureCategory);
        Assert.Empty(f.Workspace.ExportCalls);
    }

    private sealed class Fixture : IDisposable
    {
        public Siemens.Engineering.Project Project { get; } = new();
        public TiaPortal Portal { get; } = new();
        public VersionControlInterface Service { get; } = new();
        public Workspace Workspace { get; }
        public PlcSoftware Plc { get; } = new() { Name = "FixtureSoftware" };
        public FC First { get; } = new() { Name = "FixtureA" };
        public FC Second { get; } = new() { Name = "FixtureB" };
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "VciWriterFixture-" + Guid.NewGuid().ToString("N"));
        public string Target => Path.Combine(Root, "FixtureDevice", Plc.Name, "Program blocks");
        public VciWriteRequestInfo Request { get; }

        public Fixture()
        {
            Project.VersionControlInterface = Service;
            Workspace = Service.WorkspaceGroup.Workspaces.Create("FixtureWorkspace", new DirectoryInfo(Root));
            var device = new Device { Name = "FixtureDevice" };
            Project.Devices.Items.Add(device);
            var item = new DeviceItem { Parent = device, Container = new SoftwareContainer { Software = Plc } };
            device.DeviceItems.Items.Add(item); Plc.Parent = item;
            Plc.BlockGroup.Name = "FixtureBlocks";
            Plc.BlockGroup.Blocks.Items.Add(First); Plc.BlockGroup.Blocks.Items.Add(Second);
            Request = new VciWriteRequestInfo { Operation = "export", OperationId = "item", WorkspaceName = Workspace.Name,
                RequestId = "request", BreadcrumbPath = Path.Combine(Root, "breadcrumb.json"),
                Scope = new[] { ("Device", device.Name), ("PlcSoftware", Plc.Name), ("BlockFolder", Plc.BlockGroup.Name) }
                    .Select(x => new ProjectTreeSelectorSegment { NodeType = x.Item1, Name = x.Item2 }).ToList() };
        }

        public MappedObject Map(FC leaf)
        {
            var mapping = new MappedObject { EngineeringObject = leaf, DirectoryPath = new DirectoryInfo(Target),
                FileNameWithoutExtension = leaf.Name, Workspace = Workspace, Owner = Workspace.MappedObjects,
                StatusResult = new() { CompareState = CompareState.Unequal, IndividualObjectCompareDetails = IndividualObjectCompareDetails.WorkspaceFileChanged } };
            Workspace.MappedObjects.Items.Add(mapping);
            return mapping;
        }

        public MappedObject[] MapBoth(CompareState state = CompareState.Unequal,
            IndividualObjectCompareDetails details = IndividualObjectCompareDetails.WorkspaceFileChanged)
        {
            var maps = new[] { Map(First), Map(Second) };
            foreach (var map in maps) map.StatusResult = new() { CompareState = state, IndividualObjectCompareDetails = details };
            return maps;
        }

        public VciWriteOutcomeInfo Run(string operation)
        {
            Request.Operation = operation;
            return VciWriter.Write(Portal, Project, VciMethods.ForOperation(operation), Request);
        }

        public VciWriteOutcomeInfo Failure(string operation)
        {
            var ex = Assert.Throws<WorkerOperationException>(() => Run(operation));
            Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, ex.FailureCategory);
            return Assert.IsType<VciWriteOutcomeInfo>(ex.VciWriteOutcome);
        }

        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
