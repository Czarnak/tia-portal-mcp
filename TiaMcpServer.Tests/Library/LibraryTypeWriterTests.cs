using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Openness.Library;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Library;

public sealed partial class LibraryTypeWriterTests
{
    private const string Xml = "<Document><SW.Blocks.FB /></Document>";
    private readonly TiaPortal portal = new();
    private readonly Siemens.Engineering.Project project = new() { ProjectLibrary = new() };
    private readonly PlcSoftware plc = new() { Name = "PLC" };
    private readonly FB source = new() { Name = "Motor" };
    private string? temporaryDirectory;

    public LibraryTypeWriterTests()
    {
        var device = new Device { Name = "Device" };
        device.DeviceItems.Items.Add(new DeviceItem { Container = new SoftwareContainer { Software = plc } });
        project.Devices.Items.Add(device);
        plc.BlockGroup.Parent = plc;
        source.Parent = plc.BlockGroup;
        plc.BlockGroup.Blocks.Items.Add(source);
        source.ExportAction = (file, _) => { temporaryDirectory = file.DirectoryName; File.WriteAllText(file.FullName, Xml); };
        source.ExportAsDocumentsAction = (_, _) => new();
    }

    private LibraryWriteRequestInfo Item(string operation) => operation == "release_type_version"
        ? new() { Operation = operation, Scope = "project", TypeName = "Motor" }
        : new() { Operation = operation, PlcName = "PLC", PlcKind = "block", PlcObjectName = "Motor",
            Content = operation == "create_type_version" ? Xml : null,
            ExpectedContentHash = operation == "create_type_version" ? ContentHashRules.Compute("xml", Bundle()) : null };
    private static string Bundle() => BlockBundleFormat.Compose(new[] { new BlockImportDocument("Motor.xml", "Motor.xml", Xml) });
    private LibraryWriteResultInfo Write(LibraryWriteRequestInfo item)
        => LibraryWriter.Write(portal, project, LibraryWriteMethods.ForItem(item), item);
    private static WorkerOperationException Failure(string category, Action action, string? guard = null)
    {
        var error = Assert.Throws<WorkerOperationException>(action);
        Assert.Equal(category, error.FailureCategory);
        if (guard is not null) Assert.Contains(guard, error.Message);
        return error;
    }
    private CodeBlockLibraryTypeVersion Version(bool inWork = true)
    {
        var type = project.ProjectLibrary!.TypeFolder.AddType("Motor");
        var version = new CodeBlockLibraryTypeVersion { TypeObject = type, Parent = type, Guid = Guid.NewGuid(),
            VersionNumber = new(1, 0, 1), State = inWork ? LibraryTypeVersionState.InWork : LibraryTypeVersionState.Committed };
        type.Versions.Items.Add(version);
        source.LibraryTypeInstanceInfo = new() { LibraryTypeVersion = version, LibraryTypeInstance = source };
        return version;
    }
    private void Conversion(bool linked = true)
    {
        portal.TransactionAction = transaction => transaction.RollbackAction = () =>
        {
            plc.BlockGroup.Blocks.Items.Clear();
            plc.BlockGroup.Blocks.Items.Add(source);
            project.ProjectLibrary!.TypeFolder.Types.Items.Clear();
        };
        project.ProjectLibrary!.TypeFolder.Types.CreateFromDocumentsAction = (directory, name, group, options) =>
        {
            temporaryDirectory = directory.FullName;
            Assert.Equal("Motor", name);
            Assert.Same(plc.BlockGroup, group);
            Assert.Equal(LibraryImportOptions.None, options);
            Assert.True(File.Exists(Path.Combine(directory.FullName, "Motor.xml")));
            Assert.DoesNotContain(source, plc.BlockGroup.Blocks);
            var version = Version();
            var replacement = new FB { Name = "Motor", Parent = plc.BlockGroup };
            if (linked) replacement.LibraryTypeInstanceInfo = new() { LibraryTypeVersion = version, LibraryTypeInstance = replacement };
            plc.BlockGroup.Blocks.Items.Add(replacement);
            return new() { CreatedType = version.TypeObject };
        };
    }
    private CodeBlockLibraryTypeVersion VersionImport(bool inWork)
    {
        var old = Version(inWork);
        old.TypeObject.Versions.CreateFromDocumentsAction = (directory, name, _, _) =>
        {
            temporaryDirectory = directory.FullName;
            Assert.Equal(Xml, File.ReadAllText(Path.Combine(directory.FullName, name + ".xml")));
            var created = new CodeBlockLibraryTypeVersion { TypeObject = old.TypeObject, Parent = old.TypeObject,
                Guid = Guid.NewGuid(), VersionNumber = new(2, 0, 0), State = LibraryTypeVersionState.InWork };
            created.Instances[plc] = new List<LibraryTypeInstanceInfo> { new() { LibraryTypeInstance = source, LibraryTypeVersion = created } };
            return new() { CreatedVersion = created };
        };
        return old;
    }

    [Fact]
    public void Apply_LaterItemUsesTypeCreatedByEarlierItem()
    {
        Conversion();
        var created = Write(Item("create_type"));
        var released = Write(Item("release_type_version"));
        Assert.Equal(created.Type!.TypeGuid, released.Type!.TypeGuid);
        Assert.Equal("Committed", released.Type.VersionState);
        Assert.Equal("1.0.1", released.Type.VersionNumber);
    }

    [Fact]
    public void CreateType_ConvertsInTransaction_LinksInstance()
    {
        Conversion();
        var result = Write(Item("create_type"));
        Assert.Equal("Motor", result.Type!.TypeName);
        Assert.Equal("PLC/Motor", Assert.Single(result.Type.InstancePaths));
        Assert.True(Assert.Single(portal.Transactions).Committed);
        Assert.Same(project, portal.Transactions[0].Persistence);
    }
    [Fact]
    public void CreateType_NameExists_GuardBlocked_NoChange()
    {
        project.ProjectLibrary!.TypeFolder.AddFolder("Nested").AddType("Motor");
        Failure(WorkerFailureCategories.GuardBlocked, () => Write(Item("create_type")), "library_type_name_exists");
        Assert.False(source.Deleted); Assert.Empty(portal.Transactions);
    }
    [Fact]
    public void CreateType_SourceIsTypeInstance_GuardBlocked()
    {
        var version = Version(); version.TypeObject.Name = "Different";
        Failure(WorkerFailureCategories.GuardBlocked, () => Write(Item("create_type")), "library_source_is_type_instance");
        Assert.False(source.Deleted); Assert.Empty(portal.Transactions);
    }
    [Fact]
    public void CreateType_LinkCheckFails_RollsBack_PostconditionFailedWithOutcome()
    {
        Conversion(false);
        var error = Failure(WorkerFailureCategories.PostconditionFailed, () => Write(Item("create_type")));
        Assert.Equal("Motor", error.LibraryWriteOutcome!.Type!.TypeName);
        Assert.False(Assert.Single(portal.Transactions).CommitRequested);
        Assert.Same(source, Assert.Single(plc.BlockGroup.Blocks));
        Assert.Empty(project.ProjectLibrary!.TypeFolder.Types);
        Assert.False(Directory.Exists(temporaryDirectory));
    }
    [Fact]
    public void CreateType_TempFolderDeleted()
    {
        Conversion(); Write(Item("create_type")); Assert.False(Directory.Exists(temporaryDirectory));
    }
    [Theory]
    [InlineData("OB")]
    [InlineData("DB")]
    public void CreateType_UnsupportedSource_NoExportOrDelete(string kind)
    {
        plc.BlockGroup.Blocks.Items.Clear();
        PlcBlock unsupported = kind == "OB" ? new OB() : new GlobalDB();
        unsupported.Name = "Motor";
        plc.BlockGroup.Blocks.Items.Add(unsupported);
        Failure(WorkerFailureCategories.TargetKindUnsupported, () => Write(Item("create_type")));
        Assert.False(unsupported.Deleted); Assert.Empty(portal.Transactions);
    }
    [Fact]
    public void CreateType_ExportFails_NoChange_CleansTemp()
    {
        source.ExportAction = (file, _) => { temporaryDirectory = file.DirectoryName; throw new EngineeringException("Export failure"); };
        Assert.Throws<EngineeringException>(() => Write(Item("create_type")));
        Assert.False(source.Deleted); Assert.Empty(portal.Transactions); Assert.False(Directory.Exists(temporaryDirectory));
    }
    [Fact]
    public void CreateTypeVersion_NoInWork_UsesNone()
    {
        var old = VersionImport(false); var result = Write(Item("create_type_version"));
        Assert.Equal(CreateOptions.None, Assert.Single(old.TypeObject.Versions.CreateFromDocumentsCalls).CreateOptions);
        Assert.False(result.Type!.ReplacedInWork);
    }
    [Fact]
    public void CreateTypeVersion_ReplaceInWork_UsesOverride_ReportsReplaced()
    {
        var old = VersionImport(true); var item = Item("create_type_version"); item.ReplaceInWork = true;
        var result = Write(item);
        Assert.Equal(CreateOptions.Override, Assert.Single(old.TypeObject.Versions.CreateFromDocumentsCalls).CreateOptions);
        Assert.True(result.Type!.ReplacedInWork);
    }
    [Fact]
    public void CreateTypeVersion_NeverPassesTestEnvironment()
    {
        var old = VersionImport(false); Write(Item("create_type_version"));
        Assert.Equal(LibraryImportOptions.None, Assert.Single(old.TypeObject.Versions.CreateFromDocumentsCalls).ImportOptions);
    }
    [Fact]
    public void CreateTypeVersion_PassesAbsoluteDirectoryInfo()
    {
        var old = VersionImport(false); Write(Item("create_type_version"));
        var directory = Assert.Single(old.TypeObject.Versions.CreateFromDocumentsCalls).Directory;
        Assert.Equal(directory.FullName, directory.ToString()); Assert.False(Directory.Exists(directory.FullName));
    }
    [Fact]
    public void CreateTypeVersion_StaleHash_StateChanged_NoNativeCall()
    {
        var old = VersionImport(false); var item = Item("create_type_version");
        item.ExpectedContentHash = ContentHashRules.Compute("xml", "different");
        Failure(WorkerFailureCategories.StateChanged, () => Write(item)); Assert.Empty(old.TypeObject.Versions.CreateFromDocumentsCalls);
    }
    [Fact]
    public void CreateTypeVersion_ExportFails_StateUnverifiable()
    {
        var old = VersionImport(false); source.ExportAction = (_, _) => throw new EngineeringException("Export failure");
        source.ExportAsDocumentsAction = (_, _) => throw new EngineeringException("Export failure");
        Failure(WorkerFailureCategories.GuardBlocked, () => Write(Item("create_type_version")), "library_state_unverifiable");
        Assert.Empty(old.TypeObject.Versions.CreateFromDocumentsCalls);
    }
    [Fact]
    public void CreateTypeVersion_ReportsLinkedInstancesReadBack()
    {
        VersionImport(false); var result = Write(Item("create_type_version"));
        Assert.Equal("PLC/Motor", Assert.Single(result.Type!.InstancePaths)); Assert.Equal("2.0.0", result.Type.VersionNumber);
        Assert.Equal("InWork", result.Type.VersionState); Assert.False(Directory.Exists(temporaryDirectory));
    }
    [Fact]
    public void CreateTypeVersion_InWorkWithoutOverride_LeavesRefusalToNative()
    {
        var old = VersionImport(true);
        old.TypeObject.Versions.CreateFromDocumentsAction = (_, _, _, _) => throw new EngineeringTargetInvocationException("Native refusal");
        var failure = Failure(WorkerFailureCategories.EngineeringRejected, () => Write(Item("create_type_version")));
        Assert.Equal(nameof(EngineeringTargetInvocationException), failure.EngineeringError!.Type);
        Assert.Equal("Motor", failure.LibraryWriteOutcome!.Type!.TypeName);
        Assert.Equal(CreateOptions.None, Assert.Single(old.TypeObject.Versions.CreateFromDocumentsCalls).CreateOptions);
    }
    [Fact]
    public void CreateTypeVersion_FatalExport_PreservesPortalLoss()
    {
        VersionImport(false); var fatal = new NonRecoverableException("Lost"); source.ExportAction = (_, _) => throw fatal;
        Assert.Same(fatal, Assert.Throws<NonRecoverableException>(() => Write(Item("create_type_version"))));
    }
    [Fact]
    public void Release_OwnPrefilledNumberGiven_Succeeds()
    {
        var version = Version(); var item = Item("release_type_version"); item.VersionNumber = "1.0.1";
        Assert.Equal("1.0.1", Write(item).Type!.VersionNumber);
        Assert.Equal(new Version(1, 0, 1), Assert.Single(version.ReleaseCalls).Number);
    }
    [Fact]
    public void Release_NumberOmitted_PassesNull_ChecksPrefilled()
    {
        var version = Version(); version.TypeObject.AddVersion("1.0.0"); Write(Item("release_type_version"));
        Assert.Null(Assert.Single(version.ReleaseCalls).Number);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("1.0.1")]
    public void Release_EqualToReleased_GuardBlocked(string? number)
    {
        var version = Version(); version.TypeObject.AddVersion("1.0.1"); var item = Item("release_type_version"); item.VersionNumber = number;
        Failure(WorkerFailureCategories.GuardBlocked, () => Write(item), "library_version_not_higher"); Assert.Empty(version.ReleaseCalls);
    }
    [Fact]
    public void Release_ReportsDefaultBeforeAfterAndNewGuid()
    {
        var version = Version(); var previous = version.TypeObject.AddVersion("1.0.0", true); var guid = Guid.NewGuid();
        version.ReleaseAction = (_, _, author, comment) =>
        {
            Assert.Equal("Author", author); Assert.Equal("Comment", comment);
            version.Guid = guid; version.State = LibraryTypeVersionState.Committed; previous.IsDefault = false; version.IsDefault = true;
        };
        var item = Item("release_type_version"); item.Author = "Author"; item.Comment = "Comment";
        var result = Write(item).Type!;
        Assert.Equal("1.0.0", result.DefaultBefore); Assert.Equal("1.0.1", result.DefaultAfter);
        Assert.Equal(guid.ToString("D"), result.VersionGuid); Assert.Equal("Committed", result.VersionState);
    }
    [Fact]
    public void Release_UsesDoNotAutomaticallyCreateOrRelease()
    {
        var version = Version(); Write(Item("release_type_version"));
        Assert.Equal(CreateOrReleaseDependenciesMode.DoNotAutomaticallyCreateOrReleaseDependencies, Assert.Single(version.ReleaseCalls).Mode);
    }

    [Fact]
    public void CreateTypeVersion_SlashInFolder_PreservesExactSelection()
    {
        var group = new PlcBlockUserGroup { Name = "Area/One", Parent = plc.BlockGroup };
        plc.BlockGroup.Groups.Items.Add(group);
        plc.BlockGroup.Blocks.Items.Remove(source);
        group.Blocks.Items.Add(source); source.Parent = group;
        VersionImport(false);
        var item = Item("create_type_version"); item.PlcFolderPath = new() { "Area/One" };
        Assert.Equal("PLC/Area%2FOne/Motor", Assert.Single(Write(item).Type!.InstancePaths));
    }

    [Fact]
    public void CreateTypeVersion_HashUsesPublicXmlBundleIncludingCompanions()
    {
        VersionImport(false);
        source.ExportAsDocumentsAction = (directory, _) =>
        {
            var file = new FileInfo(Path.Combine(directory.FullName, "Motor.s7dcl"));
            File.WriteAllText(file.FullName, "companion");
            return new() { ExportedDocuments = new List<FileInfo> { file } };
        };
        var item = Item("create_type_version");
        Failure(WorkerFailureCategories.StateChanged, () => Write(item));
        item.ExpectedContentHash = ContentHashRules.Compute("xml", BlockBundleFormat.Compose(new[]
        {
            new BlockImportDocument("Motor.xml", "Motor.xml", Xml),
            new BlockImportDocument("Motor.s7dcl", "Motor.s7dcl", "companion"),
        }));
        Assert.Equal("2.0.0", Write(item).Type!.VersionNumber);
    }

    [Fact]
    public void CreateTypeVersion_PlcUdt_UsesSanitizedXmlHash()
    {
        var type = project.ProjectLibrary!.TypeFolder.AddType("Data");
        var version = new PlcTypeLibraryTypeVersion { TypeObject = type, Parent = type, Guid = Guid.NewGuid() };
        type.Versions.Items.Add(version);
        var udt = new PlcType { Name = "Data", Parent = plc.TypeGroup };
        plc.TypeGroup.Parent = plc;
        plc.TypeGroup.Types.Items.Add(udt);
        udt.LibraryTypeInstanceInfo = new() { LibraryTypeVersion = version, LibraryTypeInstance = udt };
        udt.ExportAction = (file, _) => File.WriteAllText(file.FullName, "<Document><DocumentInfo>volatile</DocumentInfo><SW.Types.PlcStruct /></Document>");
        type.Versions.CreateFromDocumentsAction = (_, _, _, _) => new() { CreatedVersion = version };
        var item = Item("create_type_version"); item.PlcKind = "plcType"; item.PlcObjectName = "Data";
        item.ExpectedContentHash = ContentHashRules.Compute("xml", BlockXmlSanitizer.RemoveDocumentInfo("<Document><DocumentInfo>volatile</DocumentInfo><SW.Types.PlcStruct /></Document>"));
        Assert.Equal("Data", Write(item).Type!.TypeName);
    }

    [Fact]
    public void Release_NativeFatal_RetainsOutcomeWithoutEngineeringError()
    {
        var version = Version(); version.ReleaseAction = (_, _, _, _) => throw new NonRecoverableException("Lost");
        var failure = Failure(WorkerFailureCategories.PortalLost, () => Write(Item("release_type_version")));
        Assert.Equal("Motor", failure.LibraryWriteOutcome!.Type!.TypeName); Assert.Null(failure.EngineeringError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateTypeVersion_ExportWithCurrentPortalDisposal_PortalLost(bool companionIsEngineering)
    {
        var old = VersionImport(false);
        long revision = 0;
        source.ExportAction = (_, _) => { revision++; throw new EngineeringException("Export disconnected"); };
        source.ExportAsDocumentsAction = (_, _) => throw (companionIsEngineering
            ? new EngineeringException("Companion disconnected") : new IOException("No companion document"));
        var response = WorkerExecution.Execute(() => { Write(Item("create_type_version")); return new WorkerResponse { Success = true }; }, () => revision, true);
        Assert.Equal(WorkerFailureCategories.PortalLost, response.FailureCategory);
        Assert.Null(response.EngineeringError);
        Assert.Empty(old.TypeObject.Versions.CreateFromDocumentsCalls);
    }

    private static LibraryWriteRequestInfo Select(string operation, LibraryTypeVersion version) => new()
    {
        Operation = operation, Scope = "project", TypeGuid = version.TypeObject.Guid.ToString("D"),
        VersionGuid = operation is "set_default_type_version" or "delete_type_version" ? version.Guid.ToString("D") : null,
    };

    [Theory]
    [InlineData("set_default_type_version", 0)]
    [InlineData("set_default_type_version", 1)]
    [InlineData("set_default_type_version", 2)]
    [InlineData("delete_type_version", 0)]
    [InlineData("delete_type_version", 1)]
    [InlineData("delete_type_version", 2)]
    public void OmittedVersion_UsesOnlyUniqueDefault(string operation, int defaults)
    {
        var version = Version(false);
        version.IsDefault = defaults > 0;
        if (defaults == 2) version.TypeObject.AddVersion("2.0.0", true);
        var item = Select(operation, version); item.VersionGuid = null;
        if (defaults == 1)
            Assert.Equal(version.Guid.ToString("D"), Write(item).Type!.VersionGuid);
        else
            Failure(defaults == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous, () => Write(item));
    }

    [Fact]
    public void Release_OmittedVersion_UsesInWorkInsteadOfDefault()
    {
        var version = Version();
        version.TypeObject.AddVersion("1.0.0", true);
        Assert.Equal(version.Guid.ToString("D"), Write(Item("release_type_version")).Type!.VersionGuid);
    }

    [Fact]
    public void SetDefault_ReportsBeforeAfter()
    {
        var selected = Version(false);
        selected.TypeObject.AddVersion("2.0.0", true);
        var result = Write(Select("set_default_type_version", selected));
        Assert.Equal("2.0.0", result.Type!.DefaultBefore);
        Assert.Equal("1.0.1", result.Type.DefaultAfter);
        Assert.Equal(selected.Guid.ToString("D"), result.Type.VersionGuid);
        Assert.True(selected.IsDefault);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Synchronize_UsesPinnedModes_ReportsTargetDefaults(bool existing)
    {
        var sourceVersion = Version(false); sourceVersion.IsDefault = true;
        var target = new UserGlobalLibrary { Name = "Target", Path = new FileInfo(@"C:\Libraries\Target.al21") };
        portal.GlobalLibraries.Items.Add(target);
        LibraryType? targetType = existing ? target.TypeFolder.AddFolder("Destination").AddType("TargetName", sourceVersion.TypeObject.Guid) : null;
        targetType?.AddVersion("3.0.0", true);
        sourceVersion.TypeObject.UpdateLibraryAction = (library, delete, structure, force) =>
        {
            Assert.Same(target, library);
            Assert.Equal(DeleteUnusedVersionsMode.DoNotDelete, delete);
            Assert.Equal(StructureConflictResolutionMode.CancelIfStructureConflicts, structure);
            Assert.Equal(ForceUpdateMode.SetOnlyHigherUpdatedVersionAsDefault, force);
            targetType ??= target.TypeFolder.AddFolder("Destination").AddType("TargetName", sourceVersion.TypeObject.Guid);
            if (!existing) targetType.AddVersion("1.0.1", true);
        };
        var item = new LibraryWriteRequestInfo { Operation = "synchronize_type", SourceScope = "project", Scope = "global",
            LibraryName = "Target", TypeName = "Motor" };
        var result = Write(item);
        Assert.Equal("global", result.Library.Scope);
        Assert.Equal(targetType!.Guid.ToString("D"), result.Type!.TypeGuid);
        Assert.Equal("TargetName", result.Type.TypeName);
        Assert.Equal(new[] { "Destination" }, result.Type.FolderPath);
        Assert.Equal(existing ? "3.0.0" : null, result.Type.DefaultBefore);
        Assert.Equal(existing ? "3.0.0" : "1.0.1", result.Type.DefaultAfter);
        Assert.Single(sourceVersion.TypeObject.UpdateLibraryCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Instantiate_CodeBlockAndPlcType(bool udt)
    {
        var library = new UserGlobalLibrary { Name = "Source", Path = new FileInfo(@"C:\Libraries\Source.al21") };
        portal.GlobalLibraries.Items.Add(library);
        var sourceType = library.TypeFolder.AddType("SourceType");
        LibraryTypeVersion selected = udt ? new PlcTypeLibraryTypeVersion() : new CodeBlockLibraryTypeVersion();
        selected.Parent = sourceType; selected.TypeObject = sourceType; selected.Guid = Guid.NewGuid();
        selected.State = LibraryTypeVersionState.Committed; selected.VersionNumber = new(1, 0, 1); selected.IsDefault = true;
        sourceType.Versions.Items.Add(selected);
        var targetType = project.ProjectLibrary!.TypeFolder.AddType("ActualTarget");
        var targetVersion = new CodeBlockLibraryTypeVersion { Parent = targetType, TypeObject = targetType,
            Guid = Guid.NewGuid(), VersionNumber = new(1, 0, 1), State = LibraryTypeVersionState.Committed };
        targetType.Versions.Items.Add(targetVersion);
        var blockFolder = plc.BlockGroup.Groups.Create("Destination/Blocks"); blockFolder.Parent = plc.BlockGroup;
        var typeFolder = new PlcTypeUserGroup { Name = "Destination/Types", Parent = plc.TypeGroup };
        plc.TypeGroup.Groups.Items.Add(typeFolder);
        plc.TypeGroup.Parent = plc;
        void Check(LibraryTypeVersion version, UpdatePathsMode mode)
        { Assert.Same(selected, version); Assert.Equal(UpdatePathsMode.ThrowIfPathsConflict, mode); }
        blockFolder.Blocks.CreateFromVersionAction = (version, mode) =>
        {
            Check(version, mode);
            var block = new FB { Name = "Native", Parent = blockFolder };
            block.LibraryTypeInstanceInfo = new() { LibraryTypeVersion = targetVersion, LibraryTypeInstance = block };
            return block;
        };
        typeFolder.Types.CreateFromVersionAction = (version, mode) =>
        {
            Check(version, mode);
            var type = new PlcType { Name = "Native", Parent = typeFolder };
            type.LibraryTypeInstanceInfo = new() { LibraryTypeVersion = targetVersion, LibraryTypeInstance = type };
            return type;
        };
        var item = new LibraryWriteRequestInfo { Operation = "instantiate_type_version", SourceScope = "global",
            SourceLibraryName = "Source", TypeName = "SourceType", PlcName = "PLC",
            PlcFolderPath = new() { udt ? "Destination/Types" : "Destination/Blocks" } };
        var result = Write(item);
        Assert.Equal("project", result.Library.Scope);
        Assert.Equal(targetType.Guid.ToString("D"), result.Type!.TypeGuid);
        Assert.Equal(targetVersion.Guid.ToString("D"), result.Type.VersionGuid);
        Assert.Equal(new[] { udt ? "PLC/Destination%2FTypes/Native" : "PLC/Destination%2FBlocks/Native" }, result.Type.InstancePaths);
        Assert.Empty(selected.FindInstancesCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeleteType_NoInstanceScan_NativeRefusalIsEngineeringRejected(bool refused)
    {
        var version = Version(false); var guid = version.TypeObject.Guid.ToString("D");
        version.FindInstancesAction = _ => throw new InvalidOperationException("Apply must not scan instances.");
        if (refused) version.TypeObject.DeleteAction = () => throw new EngineeringTargetInvocationException("Native refusal");
        var item = Select("delete_type", version);
        if (refused)
        {
            var failure = Failure(WorkerFailureCategories.EngineeringRejected, () => Write(item));
            Assert.NotNull(failure.EngineeringError);
            Assert.Equal(guid, failure.LibraryWriteOutcome!.Type!.TypeGuid);
            Assert.Empty(failure.LibraryWriteOutcome.Type.DeletedVersions);
        }
        else
        {
            var result = Write(item);
            Assert.Equal("Motor", result.Type!.TypeName);
            Assert.Equal(guid, result.Type.TypeGuid);
            Assert.Equal(new[] { "1.0.1" }, result.Type.DeletedVersions);
            Assert.True(result.Type.TypeDeleted);
            Assert.True(version.TypeObject.Disposed);
        }
        Assert.Empty(version.FindInstancesCalls);
    }

    [Fact]
    public void DeleteVersion_InWork_Discards()
    {
        var version = Version(); var called = false;
        version.DiscardAction = () => { called = true; version.TypeObject.Versions.Items.Remove(version); version.Disposed = true; };
        version.DeleteAction = () => throw new InvalidOperationException("An in-work version must be discarded.");
        var item = Select("delete_type_version", version); item.VersionGuid = null; item.InWork = true;
        var result = Write(item);
        Assert.True(called);
        Assert.True(result.Type!.Discarded);
        Assert.Empty(result.Type.DeletedVersions);
        Assert.False(result.Type.TypeDeleted);
        Assert.Empty(version.FindInstancesCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeleteVersion_LastVersion_ReportsTypeDeleted(bool inWork)
    {
        var version = Version(inWork); var type = version.TypeObject; var guid = type.Guid.ToString("D");
        void Remove() { type.Delete(); version.Disposed = true; }
        version.DeleteAction = Remove; version.DiscardAction = Remove;
        var result = Write(Select("delete_type_version", version));
        Assert.Equal(guid, result.Type!.TypeGuid);
        Assert.Equal("Motor", result.Type.TypeName);
        Assert.True(result.Type.TypeDeleted);
        Assert.Equal(inWork, result.Type.Discarded == true);
        Assert.Equal(inWork ? Array.Empty<string>() : new[] { "1.0.1" }, result.Type.DeletedVersions);
        Assert.Empty(version.FindInstancesCalls);
    }

    [Theory]
    [InlineData("set_default_type_version")]
    [InlineData("delete_type")]
    [InlineData("delete_type_version")]
    [InlineData("synchronize_type")]
    public void GlobalOnlyTypeOperations_WorkOnLocalSession(string operation)
    {
        var library = new UserGlobalLibrary { Name = "Source", Path = new FileInfo(@"C:\Libraries\Source.al21") };
        portal.GlobalLibraries.Items.Add(library);
        var type = library.TypeFolder.AddType("Motor");
        var version = new CodeBlockLibraryTypeVersion { Parent = type, TypeObject = type, Guid = Guid.NewGuid(),
            VersionNumber = new(1, 0, 1), State = LibraryTypeVersionState.Committed, IsDefault = true };
        type.Versions.Items.Add(version);
        var item = Select(operation, version); item.Scope = "global"; item.LibraryName = "Source";
        if (operation == "synchronize_type")
        {
            item.SourceScope = "global"; item.SourceLibraryName = "Source";
            type.UpdateLibraryAction = (_, _, _, _) => { };
        }
        var result = LibraryWriter.Write(portal, new Siemens.Engineering.Multiuser.MultiuserProject(), LibraryWriteMethods.ForItem(item), item);
        Assert.Equal("global", result.Library.Scope);
    }

    [Theory]
    [InlineData("set_default_type_version")]
    [InlineData("delete_type")]
    [InlineData("delete_type_version")]
    [InlineData("synchronize_type")]
    [InlineData("instantiate_type_version")]
    public void RemainingOperations_UnsupportedVersionKind_Refused(string operation)
    {
        var type = project.ProjectLibrary!.TypeFolder.AddType("Unsupported");
        var version = type.AddVersion("1.0.1", true);
        var item = Select(operation, version);
        if (operation is "synchronize_type" or "instantiate_type_version")
        {
            item.SourceScope = "project";
            if (operation == "instantiate_type_version") { item.Scope = null; item.PlcName = "PLC"; }
        }
        Failure(WorkerFailureCategories.TargetKindUnsupported, () => Write(item));
        Assert.Empty(type.UpdateLibraryCalls);
        Assert.Empty(plc.BlockGroup.Blocks.CreateFromVersionCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeleteVersion_FatalAndCurrentDisposal_RetainOutcome(bool fatal)
    {
        var version = Version(false); var item = Select("delete_type_version", version); long revision = 0;
        version.DeleteAction = () => { revision++; throw (fatal ? new NonRecoverableException("Lost") : new EngineeringException("Disconnected")); };
        var response = WorkerExecution.Execute(() => { Write(item); return new WorkerResponse { Success = true }; }, () => revision, true);
        Assert.Equal(WorkerFailureCategories.PortalLost, response.FailureCategory);
        Assert.Null(response.EngineeringError);
        Assert.Equal("Motor", response.LibraryWriteOutcome!.Type!.TypeName);
    }
}
