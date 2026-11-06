using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.MasterCopies;
using Siemens.Engineering.Multiuser;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Library;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Library;

public sealed class LibraryWriterTests
{
    private readonly TiaPortal portal = new();
    private readonly Siemens.Engineering.Project project = new() { Name = "Bound", ProjectLibrary = new ProjectLibrary() };

    private UserGlobalLibrary AddLibrary(string path = @"C:\Libraries\Fixture.al21")
    {
        var library = new UserGlobalLibrary { Name = "Fixture", Path = new FileInfo(path) };
        portal.GlobalLibraries.Items.Add(library);
        return library;
    }

    private static LibraryWriteRequestInfo Item(string operation) => new()
        { Operation = operation, Scope = "global", LibraryName = "Fixture" };

    private LibraryWriteResultInfo Write(LibraryWriteRequestInfo item, string? method = null, ProjectBase? root = null)
        => LibraryWriter.Write(portal, root ?? project, method ?? LibraryWriteMethods.ForItem(item), item);

    private PlcSoftware AddPlc()
    {
        var plc = new PlcSoftware { Name = "PLC" };
        var device = new Device { Name = "Device" };
        device.DeviceItems.Items.Add(new DeviceItem { Container = new SoftwareContainer { Software = plc } });
        project.Devices.Items.Add(device);
        return plc;
    }

    [Fact]
    public void CreateGlobal_UsesReturnedPath()
    {
        portal.GlobalLibraries.CreateAction = (directory, name) =>
        {
            Assert.Equal(@"C:\Output", directory.FullName);
            Assert.Equal("New", name);
            return new UserGlobalLibrary { Name = name, Path = new FileInfo(@"C:\Output\Native\New.al21") };
        };
        var result = Write(new() { Operation = "create_global", Directory = @"C:\Output", Name = "New" });
        Assert.Equal(@"C:\Output\Native\New.al21", result.Library.LibraryPath);
    }

    [Theory]
    [InlineData(false, "readOnly", OpenMode.ReadOnly)]
    [InlineData(true, "readWrite", OpenMode.ReadWrite)]
    public void OpenGlobal_AlwaysCallsOpen_ReportsObservationAndReturnedPath(bool alreadyOpen, string mode, OpenMode nativeMode)
    {
        if (alreadyOpen) AddLibrary();
        var calls = 0;
        portal.GlobalLibraries.OpenAction = (path, actualMode) =>
        {
            calls++;
            Assert.Equal(@"C:\Libraries\Fixture.al21", path.FullName);
            Assert.Equal(nativeMode, actualMode);
            return new UserGlobalLibrary { Name = "Returned", Path = new FileInfo(@"C:\Native\Returned.al21") };
        };
        var result = Write(new() { Operation = "open_global", FilePath = @"C:\Libraries\Fixture.al21", OpenMode = mode });
        Assert.Equal(1, calls);
        Assert.Equal(alreadyOpen, result.AlreadyOpen);
        Assert.Equal(@"C:\Native\Returned.al21", result.Library.LibraryPath);
    }

    [Fact]
    public void RetrieveGlobal_UsesReturnedPath()
    {
        portal.GlobalLibraries.RetrieveAction = (path, directory, mode) =>
        {
            Assert.Equal(@"C:\Input\Fixture.zal21", path.FullName);
            Assert.Equal(@"C:\Output", directory.FullName);
            Assert.Equal(OpenMode.ReadWrite, mode);
            return new UserGlobalLibrary { Name = "Returned", Path = new FileInfo(@"C:\Output\Native\Returned.al21") };
        };
        var result = Write(new() { Operation = "retrieve_global", FilePath = @"C:\Input\Fixture.zal21", Directory = @"C:\Output", OpenMode = "readWrite" });
        Assert.Equal(@"C:\Output\Native\Returned.al21", result.Library.LibraryPath);
    }

    [Fact]
    public void SaveGlobal_CallsSave()
    {
        var calls = 0;
        AddLibrary().SaveAction = () => calls++;
        var result = Write(Item("save_global"));
        Assert.True(result.Saved);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void CloseSave_Dirty_SavesThenClosesWithoutReadingClosedLibrary()
    {
        var library = AddLibrary();
        var calls = new List<string>();
        library.Modified = true;
        library.SaveAction = () => calls.Add("save");
        library.CloseAction = () => { calls.Add("close"); library.Disposed = true; };
        var item = Item("close_global");
        item.CloseAction = "save";
        var result = Write(item);
        Assert.Equal(new[] { "save", "close" }, calls);
        Assert.True(result.Saved);
        Assert.Equal(@"C:\Libraries\Fixture.al21", result.Library.LibraryPath);
    }

    [Fact]
    public void CloseSave_CleanReadOnlyLibrary_DoesNotCallSave()
    {
        var library = AddLibrary();
        Assert.True(library.IsReadOnly);
        var closed = false;
        library.CloseAction = () => closed = true;
        var item = Item("close_global");
        item.CloseAction = "save";
        Assert.False(Write(item).Saved);
        Assert.True(closed);
    }

    [Fact]
    public void CloseSave_CloseThrowsAfterSave_OutcomeSavedTrue()
    {
        var library = AddLibrary();
        library.Modified = true;
        library.SaveAction = () => { };
        library.CloseAction = () => throw new EngineeringTargetInvocationException("Refused");
        var item = Item("close_global");
        item.CloseAction = "save";
        var failure = Assert.Throws<WorkerOperationException>(() => Write(item));
        Assert.True(failure.LibraryWriteOutcome!.Saved);
        Assert.Equal(WorkerFailureCategories.EngineeringRejected, failure.FailureCategory);
        Assert.Equal(nameof(EngineeringTargetInvocationException), failure.EngineeringError!.Type);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CloseSave_PortalLoss_RetainsSavedWithoutEngineeringError(bool disposalEvent)
    {
        var library = AddLibrary();
        library.Modified = true;
        library.SaveAction = () => { };
        long revision = 0;
        library.CloseAction = () =>
        {
            if (disposalEvent) { revision++; throw new EngineeringException("Lost"); }
            throw new NonRecoverableException("Lost");
        };
        var item = Item("close_global");
        item.CloseAction = "save";
        if (!disposalEvent)
        {
            var failure = Assert.Throws<WorkerOperationException>(() => Write(item));
            Assert.Equal(WorkerFailureCategories.PortalLost, failure.FailureCategory);
            Assert.True(failure.LibraryWriteOutcome!.Saved);
            Assert.Null(failure.EngineeringError);
            return;
        }
        var response = WorkerExecution.Execute(() => { Write(item); return new WorkerResponse { Success = true }; }, () => revision, true);
        Assert.Equal(WorkerFailureCategories.PortalLost, response.FailureCategory);
        Assert.True(response.LibraryWriteOutcome!.Saved);
        Assert.Null(response.EngineeringError);
    }

    [Fact]
    public void CloseDiscard_NeverCallsSaveOrReadsDirtyState()
    {
        var library = AddLibrary();
        library.ModifiedFailure = new EngineeringNotSupportedException("Do not read dirty state");
        var closed = false;
        library.CloseAction = () => { closed = true; library.Disposed = true; };
        var item = Item("close_global");
        item.CloseAction = "discard";
        Assert.False(Write(item).Saved);
        Assert.True(closed);
    }

    [Theory]
    [InlineData("Archive", "Archive.zal21")]
    [InlineData("Archive.ZAL21", "Archive.ZAL21")]
    public void Archive_NameWithoutExtension_AppendsZal21(string name, string expected)
    {
        var calls = 0;
        AddLibrary().ArchiveAction = (directory, actualName, mode) =>
        {
            calls++;
            Assert.Equal(@"C:\Output", directory.FullName);
            Assert.Equal(expected, actualName);
            Assert.Equal(LibraryArchivationMode.Compressed, mode);
        };
        var item = Item("archive_global");
        item.Directory = @"C:\Output";
        item.Name = name;
        Assert.Equal(Path.Combine(@"C:\Output", expected), Write(item).ArchivePath);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("masterCopies")]
    [InlineData("types")]
    public void CreateFolder_CreatesInSelectedParent(string kind)
    {
        var library = AddLibrary();
        if (kind == "masterCopies")
        {
            var parent = library.MasterCopyFolder.AddFolder("Parent");
            parent.Folders.CreateAction = name => parent.AddFolder(name);
        }
        else
        {
            var parent = library.TypeFolder.AddFolder("Parent");
            parent.Folders.CreateAction = name => parent.AddFolder(name);
        }
        var item = Item("create_folder");
        item.FolderKind = kind; item.FolderPath = new() { "Parent" }; item.Name = "Child";
        var result = Write(item);
        Assert.Equal(new[] { "Parent", "Child" }, result.Target!.FolderPath);
        Assert.Equal("Child", result.Target.Name);
    }

    [Theory]
    [InlineData("masterCopies")]
    [InlineData("types")]
    public void DeleteFolder_DeletesSelectedUserFolder(string kind)
    {
        var library = AddLibrary();
        var deleted = false;
        if (kind == "masterCopies") library.MasterCopyFolder.AddFolder("Child").DeleteAction = () => deleted = true;
        else library.TypeFolder.AddFolder("Child").DeleteAction = () => deleted = true;
        var item = Item("delete_folder"); item.FolderKind = kind; item.Name = "Child";
        Assert.Equal("Child", Write(item).Target!.Name);
        Assert.True(deleted);
    }

    [Theory]
    [InlineData("block")]
    [InlineData("plcType")]
    [InlineData("tagTable")]
    public void CreateMasterCopy_UsesSelectedPlcSource(string kind)
    {
        var library = AddLibrary();
        var plc = AddPlc();
        IMasterCopySource source;
        if (kind == "block") { var value = new FB { Name = "Source" }; plc.BlockGroup.Blocks.Items.Add(value); source = value; }
        else if (kind == "plcType") { var value = new PlcType { Name = "Source" }; plc.TypeGroup.Types.Items.Add(value); source = value; }
        else { var value = new PlcTagTable { Name = "Source" }; plc.TagTableGroup.TagTables.Items.Add(value); source = value; }
        library.MasterCopyFolder.MasterCopies.CreateAction = actual => { Assert.Same(source, actual); return library.MasterCopyFolder.AddCopy("Source"); };
        var item = Item("create_master_copy"); item.PlcName = "PLC"; item.PlcKind = kind; item.PlcObjectName = "Source";
        var result = Write(item);
        Assert.Equal("Source", result.ActualName);
        Assert.Equal("Source", result.Target!.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateMasterCopy_NativeRename_PostconditionFailedWithActualName(bool copy)
    {
        var library = AddLibrary();
        var item = Item(copy ? "copy_master_copy" : "create_master_copy");
        if (copy)
        {
            item.Name = "Source"; item.SourceScope = "global"; item.SourceLibraryName = "Fixture";
            library.MasterCopyFolder.AddCopy("Source");
            library.MasterCopyFolder.MasterCopies.CreateFromAction = _ => library.MasterCopyFolder.AddCopy("Source_1");
        }
        else
        {
            AddPlc().BlockGroup.Blocks.Items.Add(new FB { Name = "Source" });
            item.PlcName = "PLC"; item.PlcKind = "block"; item.PlcObjectName = "Source";
            library.MasterCopyFolder.MasterCopies.CreateAction = _ => library.MasterCopyFolder.AddCopy("Source_1");
        }
        var failure = Assert.Throws<WorkerOperationException>(() => Write(item));
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, failure.FailureCategory);
        Assert.Equal("Source_1", failure.LibraryWriteOutcome!.ActualName);
    }

    [Fact]
    public void CopyMasterCopy_CopiesFromSelectedLibrary()
    {
        var library = AddLibrary();
        var source = project.ProjectLibrary!.MasterCopyFolder.AddCopy("Source");
        library.MasterCopyFolder.MasterCopies.CreateFromAction = actual => { Assert.Same(source, actual); return library.MasterCopyFolder.AddCopy("Source"); };
        var item = Item("copy_master_copy"); item.SourceScope = "project"; item.Name = "Source";
        Assert.Equal("Source", Write(item).ActualName);
    }

    [Theory]
    [InlineData("blockGroup")]
    [InlineData("typeGroup")]
    [InlineData("tagTableGroup")]
    public void Instantiate_UsesThrowIfExists(string kind)
    {
        var copy = AddLibrary().MasterCopyFolder.AddCopy("Source");
        var plc = AddPlc();
        var calls = 0;
        void Check(MasterCopy actual, MasterCopyMode mode) { calls++; Assert.Same(copy, actual); Assert.Equal(MasterCopyMode.ThrowIfExists, mode); }
        plc.BlockGroup.Blocks.CreateFromAction = (actual, mode) => { Check(actual, mode); return new FB { Name = "NativeBlock" }; };
        plc.TypeGroup.Types.CreateFromAction = (actual, mode) => { Check(actual, mode); return new PlcType { Name = "NativeType" }; };
        plc.TagTableGroup.TagTables.CreateFromAction = (actual, mode) => { Check(actual, mode); return new PlcTagTable { Name = "NativeTable" }; };
        var item = Item("instantiate_master_copy"); item.Name = "Source"; item.PlcName = "PLC"; item.PlcKind = kind;
        Write(item);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void DeleteMasterCopy_DeletesSelectedCopyWithoutReadingDeletedObject()
    {
        var copy = AddLibrary().MasterCopyFolder.AddCopy("Source");
        copy.DeleteAction = () => copy.Disposed = true;
        var item = Item("delete_master_copy"); item.Name = "Source";
        Assert.Equal("Source", Write(item).Target!.Name);
        Assert.True(copy.Disposed);
    }

    [Fact]
    public void EngineeringTargetInvocation_IsEngineeringRejected()
    {
        AddLibrary().SaveAction = () => throw new EngineeringTargetInvocationException("Refused");
        var response = WorkerExecution.Execute(() => { Write(Item("save_global")); return new WorkerResponse { Success = true }; }, () => 0, true);
        Assert.Equal(WorkerFailureCategories.EngineeringRejected, response.FailureCategory);
        Assert.Equal(nameof(EngineeringTargetInvocationException), response.EngineeringError!.Type);
    }

    [Fact]
    public void MethodMismatch_AndInvalidMembers_RefuseBeforeMutation()
    {
        var calls = 0;
        AddLibrary().SaveAction = () => calls++;
        var item = Item("save_global");
        Assert.Equal(WorkerFailureCategories.ValidationError, Assert.Throws<WorkerOperationException>(() => Write(item, LibraryWriteMethods.GlobalPlan)).FailureCategory);
        item.Name = "NotAllowed";
        Assert.Equal(WorkerFailureCategories.ValidationError, Assert.Throws<WorkerOperationException>(() => Write(item)).FailureCategory);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void ProjectOperation_OnLocalSession_RefusedBeforeNativeMutation()
    {
        var item = new LibraryWriteRequestInfo { Operation = "create_folder", Scope = "project", FolderKind = "types", Name = "New" };
        Assert.Equal(WorkerFailureCategories.UnsupportedCapability, Assert.Throws<WorkerOperationException>(() => Write(item, root: new MultiuserProject())).FailureCategory);
    }
}
