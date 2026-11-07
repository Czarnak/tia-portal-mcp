using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Library;
using TiaMcpServer.OpennessWorker.Openness.Library;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Library;

public sealed class LibraryTypeTargetsTests
{
    [Fact]
    public void Type_ByName_SearchesSubfolders()
    {
        var library = new ProjectLibrary();
        var type = library.TypeFolder.AddFolder("Area").AddFolder("Nested").AddType("Motor");
        Assert.Same(type, LibraryTypeTargets.Type(library, "Motor", null, null));
        Assert.Same(type, LibraryTypeTargets.Type(library, null, null, type.Guid.ToString("D")));
        Assert.Equal(new[] { "Area", "Nested" }, LibraryTypeTargets.FolderPath(type));
        AssertFailure(WorkerFailureCategories.TargetNotFound, () => LibraryTypeTargets.Type(library, "motor", null, null));
    }

    [Fact]
    public void Type_DuplicateName_Ambiguous_ListsGuids()
    {
        var library = new ProjectLibrary();
        var first = library.TypeFolder.AddFolder("A").AddType("Motor");
        var second = library.TypeFolder.AddFolder("B").AddType("Motor");
        var error = AssertFailure(WorkerFailureCategories.TargetAmbiguous, () => LibraryTypeTargets.Type(library, "Motor", null, null));
        Assert.Contains(first.Guid.ToString("D"), error.Message);
        Assert.Contains(second.Guid.ToString("D"), error.Message);
        Assert.Contains("A", error.Message);
        Assert.Contains("B", error.Message);
    }

    [Fact]
    public void Type_FolderPathNarrows()
    {
        var library = new ProjectLibrary();
        var root = library.TypeFolder.AddType("Motor");
        var nested = library.TypeFolder.AddFolder("Area").AddType("Motor");
        Assert.Same(root, LibraryTypeTargets.Type(library, "Motor", new(), null));
        Assert.Same(nested, LibraryTypeTargets.Type(library, "Motor", new() { "Area" }, null));
        Assert.Same(nested, LibraryTypeTargets.Type(library, null, new() { "Area" }, nested.Guid.ToString("D")));
        AssertFailure(WorkerFailureCategories.TargetNotFound, () => LibraryTypeTargets.Type(library, null, new(), nested.Guid.ToString("D")));
    }

    [Fact]
    public void Type_NativeFatalEnumeration_IsPreserved()
    {
        var library = new ProjectLibrary();
        var failure = new NonRecoverableException("fatal");
        library.TypeFolder.AddFolder("Area").Types.EnumerationFailure = failure;
        Assert.Same(failure, Assert.Throws<NonRecoverableException>(() => LibraryTypeTargets.Type(library, "Motor", null, null)));
    }

    [Fact]
    public void Type_CyclicTree_IsWorkerOperationFailed()
    {
        var library = new ProjectLibrary();
        var folder = library.TypeFolder.AddFolder("Area");
        folder.Folders.Items.Add(folder);
        AssertFailure(WorkerFailureCategories.WorkerOperationFailed, () => LibraryTypeTargets.Type(library, "Motor", null, null));
    }

    [Fact]
    public void Version_Omitted_IsDefault()
    {
        var type = new ProjectLibrary().TypeFolder.AddType("Motor");
        type.AddVersion("1.0.0");
        AssertFailure(WorkerFailureCategories.TargetNotFound, () => LibraryTypeTargets.Version(type, null, null, null));
        var expected = type.AddVersion("2.0.0", true);
        Assert.Same(expected, LibraryTypeTargets.Version(type, null, null, null));
        Assert.Same(expected, LibraryTypeTargets.Version(type, "2.0.0", null, null));
        Assert.Same(expected, LibraryTypeTargets.Version(type, null, expected.Guid.ToString("D"), null));
    }

    [Fact]
    public void Version_DuplicateNumber_Ambiguous()
    {
        var type = new ProjectLibrary().TypeFolder.AddType("Motor");
        var first = type.AddVersion("1.0.0");
        var second = type.AddVersion("1.0.0");
        var error = AssertFailure(WorkerFailureCategories.TargetAmbiguous, () => LibraryTypeTargets.Version(type, "1.0.0", null, null));
        Assert.Contains(first.Guid.ToString("D"), error.Message);
        Assert.Contains(second.Guid.ToString("D"), error.Message);
    }

    [Fact]
    public void InWork_NoneIsTargetNotFound()
    {
        var type = new ProjectLibrary().TypeFolder.AddType("Motor");
        var version = type.AddVersion("1.0.0");
        AssertFailure(WorkerFailureCategories.TargetNotFound, () => LibraryTypeTargets.InWork(type));
        version.State = LibraryTypeVersionState.InWork;
        Assert.Same(version, LibraryTypeTargets.InWork(type));
        Assert.Same(version, LibraryTypeTargets.Version(type, null, null, true));
        type.AddVersion("2.0.0").State = LibraryTypeVersionState.InWork;
        AssertFailure(WorkerFailureCategories.TargetAmbiguous, () => LibraryTypeTargets.InWork(type));
    }

    [Fact]
    public void Resolver_InvalidSelectors_AreValidationError()
    {
        var library = new ProjectLibrary();
        AssertFailure(WorkerFailureCategories.ValidationError, () => LibraryTypeTargets.Type(library, null, null, null));
        var type = library.TypeFolder.AddType("Motor");
        AssertFailure(WorkerFailureCategories.ValidationError, () => LibraryTypeTargets.Version(type, "1.0.0", Guid.NewGuid().ToString("D"), null));
        AssertFailure(WorkerFailureCategories.ValidationError, () => LibraryTypeTargets.Version(type, null, null, false));
    }

    [Fact]
    public void Instance_PlainObject_KindUnsupported()
    {
        var project = new Siemens.Engineering.Project();
        var plc = new PlcSoftware { Name = "PLC" };
        var device = new Device();
        device.DeviceItems.Items.Add(new DeviceItem { Container = new SoftwareContainer { Software = plc } });
        project.Devices.Items.Add(device);
        var block = new FB { Name = "Motor" };
        plc.BlockGroup.Blocks.Items.Add(block);
        var item = new LibraryWriteRequestInfo { PlcName = "PLC", PlcKind = "block", PlcObjectName = "Motor" };
        AssertFailure(WorkerFailureCategories.TargetKindUnsupported, () => LibraryTypeTargets.Instance(project, item));
        var version = new CodeBlockLibraryTypeVersion();
        block.LibraryTypeInstanceInfo = new LibraryTypeInstanceInfo { LibraryTypeInstance = block, LibraryTypeVersion = version };
        var resolved = LibraryTypeTargets.Instance(project, item);
        Assert.Same(block, resolved.instance);
        Assert.Same(version, resolved.version);
        Assert.Contains("Motor", LibraryTypeTargets.InstancePath(block));
    }

    [Fact]
    public void Kind_HmiType_Unsupported()
    {
        AssertFailure(WorkerFailureCategories.TargetKindUnsupported, () => LibraryTypeTargets.Kind(new LibraryTypeVersion()));
        Assert.Equal("codeBlock", LibraryTypeTargets.Kind(new CodeBlockLibraryTypeVersion()));
        Assert.Equal("plcType", LibraryTypeTargets.Kind(new PlcTypeLibraryTypeVersion()));
    }

    [Fact]
    public void InstancePath_EscapesSegmentsAndRejectsCycles()
    {
        var group = new PlcBlockUserGroup { Name = "Area/One" };
        var block = new FB { Name = "Motor", Parent = group };
        Assert.Equal("Area%2FOne/Motor", LibraryTypeTargets.InstancePath(block));
        group.Parent = block;
        AssertFailure(WorkerFailureCategories.WorkerOperationFailed, () => LibraryTypeTargets.InstancePath(block));
        group.Parent = null;
        var fatal = new NonRecoverableException("fatal");
        group.ParentFailure = fatal;
        Assert.Same(fatal, Assert.Throws<NonRecoverableException>(() => LibraryTypeTargets.InstancePath(block)));
    }

    [Fact]
    public void CursorHash_IncludesTypeNameVersionNumberAndNameFolderPresence()
    {
        var item = new LibraryReadItem { OperationId = "r", Operation = "list_dependencies", Scope = "project", TypeName = "Motor" };
        var baseline = LibraryPageCursorCodec.QueryHash(item);
        item.FolderPath = new();
        Assert.NotEqual(baseline, LibraryPageCursorCodec.QueryHash(item));
        item.FolderPath = null;
        item.TypeName = "Other";
        Assert.NotEqual(baseline, LibraryPageCursorCodec.QueryHash(item));
        item.TypeName = "Motor";
        item.VersionNumber = "1.0.0";
        Assert.NotEqual(baseline, LibraryPageCursorCodec.QueryHash(item));
        item.TypeName = null;
        item.TypeGuid = Guid.NewGuid().ToString("D");
        baseline = LibraryPageCursorCodec.QueryHash(item);
        item.FolderPath = new();
        Assert.NotEqual(baseline, LibraryPageCursorCodec.QueryHash(item));
    }

    private static WorkerOperationException AssertFailure(string category, Action action)
    {
        var error = Assert.Throws<WorkerOperationException>(action);
        Assert.Equal(category, error.FailureCategory);
        return error;
    }
}
