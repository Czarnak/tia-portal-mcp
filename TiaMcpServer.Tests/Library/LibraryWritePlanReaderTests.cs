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

public sealed class LibraryWritePlanReaderTests
{
    private readonly TiaPortal portal = new();
    private readonly Siemens.Engineering.Project project = new() { Name = "Bound", ProjectLibrary = new ProjectLibrary() };

    private UserGlobalLibrary AddLibrary(string path = @"C:\Libs\Fixture.al21")
    {
        var library = new UserGlobalLibrary { Name = "Fixture", Path = new FileInfo(path) };
        portal.GlobalLibraries.Items.Add(library);
        return library;
    }

    private static LibraryWriteRequestInfo Item(string operation = "create_folder") => new()
    {
        Operation = operation, Scope = "global", LibraryName = "Fixture", FolderKind = "masterCopies", Name = "Target",
    };

    private LibraryWritePlanInfo Plan(LibraryWriteRequestInfo item, ProjectBase? root = null, string? method = null)
        => LibraryWritePlanReader.Plan(portal, root ?? project, method ?? LibraryWriteMethods.PlanFor(item), item);

    private string Failure(LibraryWriteRequestInfo item, ProjectBase? root = null, string? method = null)
        => Assert.Throws<WorkerOperationException>(() => Plan(item, root, method)).FailureCategory;

    private PlcSoftware AddPlc()
    {
        var plc = new PlcSoftware { Name = "PLC" };
        var device = new Device { Name = "Device" };
        device.DeviceItems.Items.Add(new DeviceItem { Container = new SoftwareContainer { Software = plc } });
        project.Devices.Items.Add(device);
        return plc;
    }

    [Fact]
    public void Plan_MasterCopyNameDiffersOnlyInCase_SetsMasterCopyExists()
    {
        var library = AddLibrary();
        library.MasterCopyFolder.AddCopy("TARGET");
        var item = Item("copy_master_copy");
        item.FolderKind = null;
        item.SourceScope = "global";
        item.SourceLibraryName = "Fixture";
        item.Name = "target";
        var source = library.MasterCopyFolder.AddFolder("Source");
        source.AddCopy("target");
        item.SourceFolderPath = new() { "Source" };

        Assert.True(Plan(item).MasterCopyExists);
    }

    [Fact]
    public void Plan_CreateMasterCopy_UsesPlcSelectorNameForCollision()
    {
        var library = AddLibrary();
        library.MasterCopyFolder.AddCopy("SOURCE");
        AddPlc().BlockGroup.Blocks.Items.Add(new FB { Name = "Source" });
        var item = new LibraryWriteRequestInfo
        {
            Operation = "create_master_copy", Scope = "global", LibraryName = "Fixture",
            PlcName = "PLC", PlcKind = "block", PlcObjectName = "Source",
        };

        var plan = Plan(item);
        Assert.True(plan.MasterCopyExists);
        Assert.Equal("Source", plan.Target!.Name);
    }

    [Theory]
    [InlineData("block")]
    [InlineData("plcType")]
    [InlineData("tagTable")]
    public void Plan_CreateMasterCopy_CaseAliasUsesActualSourceSpelling(string kind)
    {
        var library = AddLibrary();
        library.MasterCopyFolder.AddCopy("SOURCE");
        var plc = AddPlc();
        if (kind == "block") plc.BlockGroup.Blocks.Items.Add(new FB { Name = "Source" });
        else if (kind == "plcType") plc.TypeGroup.Types.Items.Add(new PlcType { Name = "Source" });
        else plc.TagTableGroup.TagTables.Items.Add(new PlcTagTable { Name = "Source" });
        var plan = Plan(new()
        {
            Operation = "create_master_copy", Scope = "global", LibraryName = "Fixture",
            PlcName = "PLC", PlcKind = kind, PlcObjectName = "source",
        });
        Assert.Equal("Source", plan.Target!.Name);
        Assert.True(plan.MasterCopyExists);
    }

    [Fact]
    public void Plan_FolderWithSameName_IsNotMasterCopyCollision()
    {
        var library = AddLibrary();
        library.MasterCopyFolder.AddFolder("Source");
        AddPlc().BlockGroup.Blocks.Items.Add(new FB { Name = "Source" });
        Assert.False(Plan(new()
        {
            Operation = "create_master_copy", Scope = "global", LibraryName = "Fixture",
            PlcName = "PLC", PlcKind = "block", PlcObjectName = "Source",
        }).MasterCopyExists);
    }

    [Fact]
    public void Plan_DuplicateGlobalName_NameOnly_TargetAmbiguous()
    {
        AddLibrary();
        AddLibrary(@"C:\Other\Fixture.al21");
        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, Failure(Item()));
    }

    [Fact]
    public void Plan_DuplicateGlobalName_PathSelector_Resolves()
    {
        AddLibrary();
        AddLibrary(@"C:\Other\Fixture.al21");
        var item = Item();
        item.LibraryPath = @"c:\libs\nested\..\FIXTURE.al21";
        Assert.Equal(@"C:\Libs\Fixture.al21", Plan(item).Library!.LibraryPath);
    }

    [Fact]
    public void Plan_UnopenedGlobalLibrary_TargetNotFound()
    {
        portal.GlobalLibraries.Available.Add(new() { Name = "Fixture", Path = new FileInfo(@"C:\Libs\Fixture.al21") });
        Assert.Equal(WorkerFailureCategories.TargetNotFound, Failure(Item()));
    }

    [Fact]
    public void Plan_SaveOnCorporateLibrary_TargetKindUnsupported()
    {
        portal.GlobalLibraries.Items.Add(new GlobalLibrary { Name = "Fixture" });
        Assert.Equal(WorkerFailureCategories.TargetKindUnsupported, Failure(new()
            { Operation = "save_global", Scope = "global", LibraryName = "Fixture" }));
    }

    [Theory]
    [InlineData("masterCopies")]
    [InlineData("types")]
    public void Plan_DeleteFolder_ListsNestedContents_TruncatesAt200(string kind)
    {
        var library = AddLibrary();
        if (kind == "masterCopies")
        {
            var folder = library.MasterCopyFolder.AddFolder("Target").AddFolder("Nested");
            for (var i = 0; i < 205; i++) folder.AddCopy($"Entry{i:000}");
        }
        else
        {
            var folder = library.TypeFolder.AddFolder("Target").AddFolder("Nested");
            for (var i = 0; i < 205; i++) folder.AddType($"Entry{i:000}");
        }
        var item = Item("delete_folder");
        item.FolderKind = kind;
        var plan = Plan(item);

        Assert.Equal(200, plan.FolderContents.Count);
        Assert.True(plan.ContentsTruncated);
        Assert.Null(plan.Unverifiable);
        Assert.Equal(new[] { "Target", "Nested" }, plan.FolderContents[0].FolderPath);
        Assert.Equal("folder", plan.FolderContents[0].Kind);
        Assert.Equal("Entry000", plan.FolderContents[1].Name);
        if (kind == "types") Assert.NotNull(plan.FolderContents[1].TypeGuid);
    }

    [Fact]
    public void Plan_DeleteFolder_Exactly200Contents_IsComplete()
    {
        var folder = AddLibrary().MasterCopyFolder.AddFolder("Target");
        for (var i = 0; i < 200; i++) folder.AddCopy($"Entry{i:000}");
        var plan = Plan(Item("delete_folder"));
        Assert.Equal(200, plan.FolderContents.Count);
        Assert.False(plan.ContentsTruncated);
    }

    [Fact]
    public void Plan_UnreadableFolder_SetsUnverifiable()
    {
        var folder = AddLibrary().MasterCopyFolder.AddFolder("Target");
        folder.MasterCopies.EnumerationFailure = new EngineeringNotSupportedException("Unavailable");
        Assert.Contains(nameof(EngineeringNotSupportedException), Plan(Item("delete_folder")).Unverifiable);
    }

    [Fact]
    public void Plan_FatalFolderFailure_Propagates()
    {
        var folder = AddLibrary().MasterCopyFolder.AddFolder("Target");
        var fatal = new NonRecoverableException("Portal lost");
        folder.MasterCopies.EnumerationFailure = fatal;
        Assert.Same(fatal, Assert.Throws<NonRecoverableException>(() => Plan(Item("delete_folder"))));
    }

    [Fact]
    public void Plan_IdentityFailure_Propagates()
    {
        var folder = AddLibrary().MasterCopyFolder.AddFolder("Target");
        var failure = new WorkerOperationException(WorkerFailureCategories.BindingConflict, "Owner changed");
        folder.MasterCopies.EnumerationFailure = failure;
        Assert.Same(failure, Assert.Throws<WorkerOperationException>(() => Plan(Item("delete_folder"))));
    }

    [Fact]
    public void Plan_OpenAlreadyOpenPath_AlreadyOpenTrue()
    {
        AddLibrary();
        var plan = Plan(new() { Operation = "open_global", FilePath = @"c:\libs\sub\..\FIXTURE.al21", OpenMode = "readWrite" });
        Assert.True(plan.AlreadyOpen);
        Assert.Equal("Fixture", plan.Library!.LibraryName);
    }

    [Fact]
    public void Plan_OpenMatchedLibrary_UnreadableIdentity_Propagates()
    {
        var library = AddLibrary();
        var failure = new EngineeringNotSupportedException("Identity unavailable");
        library.NameFailure = failure;
        Assert.Same(failure, Assert.Throws<EngineeringNotSupportedException>(() => Plan(new()
            { Operation = "open_global", FilePath = @"C:\Libs\Fixture.al21", OpenMode = "readWrite" })));
    }

    [Fact]
    public void Plan_UnreadableCollisionListing_SetsUnverifiable()
    {
        var library = AddLibrary();
        library.MasterCopyFolder.MasterCopies.EnumerationFailure = new EngineeringTargetInvocationException("Unavailable");
        AddPlc().BlockGroup.Blocks.Items.Add(new FB { Name = "Source" });
        Assert.NotNull(Plan(new()
        {
            Operation = "create_master_copy", Scope = "global", LibraryName = "Fixture",
            PlcName = "PLC", PlcKind = "block", PlcObjectName = "Source",
        }).Unverifiable);
    }

    [Fact]
    public void Plan_OpenClosedPath_AlreadyOpenFalseWithoutInventedIdentity()
    {
        var plan = Plan(new() { Operation = "open_global", FilePath = @"C:\Libs\Fixture.al21", OpenMode = "readWrite" });
        Assert.False(plan.AlreadyOpen);
        Assert.Null(plan.Library);
        Assert.Null(plan.Target);
    }

    [Fact]
    public void Plan_CreateAndRetrieve_LeaveUnknownNativeIdentityNull()
    {
        foreach (var item in new[]
        {
            new LibraryWriteRequestInfo { Operation = "create_global", Directory = @"C:\Libs", Name = "New" },
            new LibraryWriteRequestInfo { Operation = "retrieve_global", Directory = @"C:\Libs", FilePath = @"C:\Libs\Old.zal21", OpenMode = "readWrite" },
        })
        {
            var plan = Plan(item);
            Assert.Null(plan.AlreadyOpen);
            Assert.Null(plan.Library);
            Assert.Null(plan.Target);
        }
    }

    [Fact]
    public void Plan_ProjectScope_LocalSession_UnsupportedCapability()
    {
        var item = Item();
        item.Scope = "project";
        item.LibraryName = null;
        Assert.Equal(WorkerFailureCategories.UnsupportedCapability, Failure(item, new MultiuserProject()));
    }

    private sealed class UnknownProject : ProjectBase { }

    [Fact]
    public void Plan_UnknownRoot_UnsupportedCapabilityBeforePortalAccess()
    {
        portal.GlobalLibrariesFailure = new NonRecoverableException("Must not touch Portal");
        Assert.Equal(WorkerFailureCategories.UnsupportedCapability, Failure(Item(), new UnknownProject()));
    }

    [Fact]
    public void Plan_MethodItemMismatch_ValidationErrorBeforeNativeAccess()
    {
        portal.GlobalLibrariesFailure = new NonRecoverableException("Must not touch Portal");
        Assert.Equal(WorkerFailureCategories.ValidationError, Failure(Item(), method: LibraryWriteMethods.ProjectPlan));
    }

    [Fact]
    public void Plan_MissingAndDuplicateMasterCopy_KeepTargetFailures()
    {
        var folder = AddLibrary().MasterCopyFolder;
        var item = new LibraryWriteRequestInfo { Operation = "delete_master_copy", Scope = "global", LibraryName = "Fixture", Name = "Copy" };
        Assert.Equal(WorkerFailureCategories.TargetNotFound, Failure(item));
        folder.AddCopy("Copy");
        folder.AddCopy("Copy");
        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, Failure(item));
    }

    [Theory]
    [InlineData("block", "blockGroup")]
    [InlineData("plcType", "typeGroup")]
    [InlineData("tagTable", "tagTableGroup")]
    public void PlcTargets_KeepLiteralSegmentsAndCaseInsensitiveMatching(string sourceKind, string destinationKind)
    {
        var plc = AddPlc();
        var blocks = new PlcBlockUserGroup { Name = "Area/One" };
        var types = new PlcTypeUserGroup { Name = "Area/One" };
        var tags = new PlcTagTableGroup { Name = "Area/One" };
        var block = new FB { Name = "Source" };
        var type = new PlcType { Name = "Source" };
        var table = new PlcTagTable { Name = "Source" };
        blocks.Blocks.Items.Add(block);
        types.Types.Items.Add(type);
        tags.TagTables.Items.Add(table);
        plc.BlockGroup.Groups.Items.Add(blocks);
        plc.TypeGroup.Groups.Items.Add(types);
        plc.TagTableGroup.Groups.Items.Add(tags);
        var item = new LibraryWriteRequestInfo { PlcName = "plc", PlcKind = sourceKind, PlcFolderPath = new() { "area/one" }, PlcObjectName = "source" };
        var expected = sourceKind == "block" ? (IMasterCopySource)block : sourceKind == "plcType" ? type : table;
        Assert.Same(expected, LibraryWriteTargets.PlcSource(project, item));
        item.PlcKind = destinationKind;
        var (group, kind) = LibraryWriteTargets.PlcDestination(project, item);
        Assert.Same(destinationKind == "blockGroup" ? (object)blocks : destinationKind == "typeGroup" ? types : tags, group);
        Assert.Equal(destinationKind, kind);
    }

    [Fact]
    public void PlcSource_BaseBlockWithoutMarker_TargetKindUnsupported()
    {
        AddPlc().BlockGroup.Blocks.Items.Add(new PlcBlock { Name = "Source" });
        var item = new LibraryWriteRequestInfo { PlcName = "PLC", PlcKind = "block", PlcObjectName = "Source" };
        Assert.Equal(WorkerFailureCategories.TargetKindUnsupported,
            Assert.Throws<WorkerOperationException>(() => LibraryWriteTargets.PlcSource(project, item)).FailureCategory);
    }

    [Fact]
    public void Plan_CallsNoMutatingMemberOrUnusedFlag()
    {
        var library = AddLibrary();
        library.ReadOnlyFailure = new NonRecoverableException("Unused flag must not be read");
        var folder = library.MasterCopyFolder.AddFolder("Target");
        var copy = folder.AddCopy("Copy");
        Assert.NotNull(Plan(Item()).Target);
        Assert.Single(Plan(Item("delete_folder")).FolderContents);
        Assert.True(Plan(new() { Operation = "close_global", Scope = "global", LibraryName = "Fixture", CloseAction = "discard" }).AlreadyOpen);
        Assert.False(folder.Disposed);
        Assert.False(copy.Disposed);
        Assert.Single(library.MasterCopyFolder.Folders.Items);
    }
}
