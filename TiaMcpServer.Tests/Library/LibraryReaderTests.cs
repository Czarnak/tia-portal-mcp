using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.Types;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Hmi;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.Library;
using TiaMcpServer.OpennessWorker.Worker;
using TiaMcpServer.Tests.Hmi;
using Xunit;

namespace TiaMcpServer.Tests.Library;

public sealed class LibraryReaderTests
{
    private const string ListChildren = "library_global_list_children";
    private const string ListGlobal = "library_list_global_libraries";

    private readonly TiaPortal portal = new();
    private readonly Siemens.Engineering.Project project = new() { Name = "Bound" };
    private readonly PageSnapshotStore<LibrarySnapshot> store = new(() => DateTime.UtcNow);

    private GlobalLibrary AddGlobal(string name, string path)
    {
        var library = new GlobalLibrary { Name = name, Path = new FileInfo(path) };
        portal.GlobalLibraries.Items.Add(library);
        return library;
    }

    private LibraryPageInfo Read(string method, LibraryQueryInfo query, ProjectBase? root = null)
        => LibraryReader.Read(portal, root ?? project, method, query, store);

    private static LibraryQueryInfo Global(string? name = null, string? path = null) => new() { Scope = "global", LibraryName = name, LibraryPath = path };

    private static LibraryQueryInfo Children(string name, int? offset = null, int? limit = null, string kind = "types", params string[] folderPath)
        => new() { Scope = "global", LibraryName = name, FolderKind = kind, FolderPath = folderPath.Length == 0 ? null : folderPath.ToList(), Offset = offset, Limit = limit };

    private WorkerOperationException Fails(string method, LibraryQueryInfo query, ProjectBase? root = null)
        => Assert.Throws<WorkerOperationException>(() => Read(method, query, root));

    [Fact]
    public void Global_ExactNameMatch_IsOrdinal()
    {
        AddGlobal("Lib", @"C:\Libs\Lib.al21");

        Assert.Equal("Lib", Assert.Single(Read("library_global_inspect_library", Global("Lib")).Items).Identity.LibraryName);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, Fails("library_global_inspect_library", Global("lib")).FailureCategory);
    }

    [Fact]
    public void Global_PathMatch_IsCanonicalCaseInsensitive()
    {
        AddGlobal("Lib", @"C:\Libs\Lib.al21");

        var entry = Assert.Single(Read("library_global_inspect_library", Global(path: @"c:\libs\sub\..\LIB.al21")).Items);

        Assert.Equal("Lib", entry.Identity.Name);
        Assert.Equal("global", entry.Identity.Scope);
    }

    [Fact]
    public void Global_MalformedLibraryPath_IsValidationErrorEvenWithNoOpenLibrary()
    {
        // "\0" makes Path.GetFullPath throw on net48 and net10 alike; '|' is only rejected on net48.
        Assert.Equal(WorkerFailureCategories.ValidationError,
            Fails("library_global_inspect_library", Global(path: "C:\\Libs\\a\0b.al21")).FailureCategory);
        AddGlobal("Lib", @"C:\Libs\Lib.al21");
        Assert.Equal(WorkerFailureCategories.ValidationError,
            Fails("library_global_inspect_library", Global("Other", "C:\\Libs\\a\0b.al21")).FailureCategory);
    }

    [Fact]
    public void Global_NameAndPathMustBothMatch()
    {
        AddGlobal("Lib", @"C:\Libs\Lib.al21");

        Assert.Equal(WorkerFailureCategories.TargetNotFound,
            Fails("library_global_inspect_library", Global("Lib", @"C:\Other\Lib.al21")).FailureCategory);
        Assert.Single(Read("library_global_inspect_library", Global("Lib", @"C:\Libs\Lib.al21")).Items);
    }

    [Fact]
    public void Global_NotOpen_IsTargetNotFound()
        => Assert.Equal(WorkerFailureCategories.TargetNotFound, Fails("library_global_inspect_library", Global("Lib")).FailureCategory);

    [Fact]
    public void Duplicate_IsTargetAmbiguous()
    {
        AddGlobal("Lib", @"C:\A\Lib.al21");
        AddGlobal("Lib", @"C:\B\Lib.al21");

        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, Fails("library_global_inspect_library", Global("Lib")).FailureCategory);
    }

    [Fact]
    public void Project_NoProjectLibrary_IsUnsupportedCapability()
    {
        var query = new LibraryQueryInfo { Scope = "project" };

        Assert.Equal(WorkerFailureCategories.UnsupportedCapability, Fails("library_project_inspect_library", query).FailureCategory);
        Assert.Equal(WorkerFailureCategories.UnsupportedCapability,
            Fails("library_project_inspect_library", query, new Siemens.Engineering.Multiuser.MultiuserProject()).FailureCategory);

        project.ProjectLibrary = new ProjectLibrary();
        var entry = Assert.Single(Read("library_project_inspect_library", query).Items);
        Assert.Equal("project", entry.Identity.Scope);
        Assert.Equal("Bound", entry.Identity.Name);
        Assert.Null(entry.Identity.LibraryName);
        Assert.Contains(entry.Diagnostics, d => d.Contains("isModified", StringComparison.Ordinal) && d.Contains("bound project", StringComparison.Ordinal));
    }

    [Fact]
    public void MethodAndScopeMismatch_IsValidationError()
        => Assert.Equal(WorkerFailureCategories.ValidationError,
            Fails("library_project_inspect_library", Global("Lib")).FailureCategory);

    [Fact]
    public void FolderPath_SegmentIsLiteral()
    {
        var library = AddGlobal("Lib", @"C:\Libs\Lib.al21");
        library.TypeFolder.AddFolder("A/B").AddType("Inner");
        library.TypeFolder.AddFolder("A").AddFolder("B").AddType("Nested");

        var literal = Read(ListChildren, Children("Lib", folderPath: "A/B"));
        var entry = Assert.Single(literal.Items);
        Assert.Equal("Inner", entry.Identity.Name);
        Assert.Equal(new[] { "A/B" }, entry.Identity.FolderPath);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, Fails(ListChildren, Children("Lib", folderPath: "A\\B")).FailureCategory);
        Assert.Equal("Nested", Assert.Single(Read(ListChildren, Children("Lib", folderPath: new[] { "A", "B" })).Items).Identity.Name);
    }

    [Fact]
    public void Children_MasterCopies_FoldersBeforeEntriesByName()
    {
        var library = AddGlobal("Lib", @"C:\Libs\Lib.al21");
        library.MasterCopyFolder.AddCopy("b");
        library.MasterCopyFolder.AddCopy("a");
        library.MasterCopyFolder.AddFolder("z");

        var page = Read(ListChildren, Children("Lib", kind: "masterCopies"));

        Assert.Equal(new[] { "folder:z", "masterCopy:a", "masterCopy:b" }, page.Items.Select(i => $"{i.Identity.Kind}:{i.Identity.Name}"));
        Assert.All(page.Items, i => Assert.Equal("masterCopies", i.Identity.FolderKind));
    }

    [Fact]
    public void Paging_FirstPageEnumeratesAllInOrder_SetsTotalAndNextOffset()
    {
        var library = AddGlobal("Lib", @"C:\Libs\Lib.al21");
        library.TypeFolder.AddType("c");
        library.TypeFolder.AddType("a");
        library.TypeFolder.AddFolder("f");
        library.TypeFolder.AddType("b");

        var page = Read(ListChildren, Children("Lib", 0, 2));

        Assert.Equal(new[] { "f", "a" }, page.Items.Select(i => i.Identity.Name));
        Assert.Equal(4, page.Total);
        Assert.Equal(2, page.NextOffset);
        Assert.NotNull(page.SnapshotId);
        Assert.False(page.Complete);
        Assert.NotNull(page.Items[1].Identity.TypeGuid);
    }

    [Fact]
    public void Paging_LastPageComplete_NoSnapshot()
    {
        var library = AddGlobal("Lib", @"C:\Libs\Lib.al21");
        library.TypeFolder.AddType("a");
        library.TypeFolder.AddType("b");

        var page = Read(ListChildren, Children("Lib", 0, 2));

        Assert.Equal(2, page.Total);
        Assert.Null(page.NextOffset);
        Assert.Null(page.SnapshotId);
        Assert.True(page.Complete);
    }

    [Fact]
    public void ListGlobalLibraries_OrdersByNameThenPath()
    {
        AddGlobal("B", @"C:\B.al21");
        AddGlobal("A", @"C:\Z\A.al21");
        AddGlobal("A", @"C:\Y\A.al21");

        var page = Read(ListGlobal, new LibraryQueryInfo());

        Assert.Equal(new[] { @"C:\Y\A.al21", @"C:\Z\A.al21", @"C:\B.al21" }, page.Items.Select(i => i.Identity.LibraryPath));
        Assert.All(page.Items, i => Assert.True(i.IsReadOnly));
    }

    [Fact]
    public void Versions_OrderByVersionNumber_DependenciesByOwnerPathTypeVersion()
    {
        var library = AddGlobal("Lib", @"C:\Libs\Lib.al21");
        var type = library.TypeFolder.AddType("T");
        var v2 = type.AddVersion("0.0.10");
        type.AddVersion("0.0.2", isDefault: true);
        var dependency = library.TypeFolder.AddFolder("Z").AddType("A").AddVersion("1.0.0");
        var rootDependencyB = library.TypeFolder.AddType("B").AddVersion("2.0.0");
        var rootDependencyA = library.TypeFolder.AddType("A").AddVersion("3.0.0");
        v2.Dependencies.Items.AddRange(new[] { dependency, rootDependencyB, rootDependencyA });

        var versions = Read("library_global_list_versions", new LibraryQueryInfo { Scope = "global", LibraryName = "Lib", TypeGuid = type.Guid.ToString() });
        var dependencies = Read("library_global_list_dependencies", new LibraryQueryInfo
            { Scope = "global", LibraryName = "Lib", TypeGuid = type.Guid.ToString(), VersionGuid = v2.Guid.ToString() });

        Assert.Equal(new[] { "0.0.2", "0.0.10" }, versions.Items.Select(i => i.VersionNumber));
        Assert.Equal(new bool?[] { true, false }, versions.Items.Select(i => i.IsDefault));
        Assert.All(versions.Items, i => Assert.Equal("version", i.Identity.Kind));
        Assert.Equal(new[] { "A/3.0.0", "B/2.0.0", "A/1.0.0" },
            dependencies.Items.Select(i => $"{i.Identity.Name}/{i.VersionNumber}"));
        Assert.Equal(new[] { "Z" }, dependencies.Items[2].Identity.FolderPath);
    }

    [Fact]
    public void Snapshot_ContinuationReadsFrozenOrder()
    {
        var library = AddGlobal("Lib", @"C:\Libs\Lib.al21");
        library.TypeFolder.AddType("b");
        library.TypeFolder.AddType("d");
        library.TypeFolder.AddType("f");
        var first = Read(ListChildren, Children("Lib", 0, 2));

        library.TypeFolder.AddType("a");
        library.TypeFolder.AddType("c");
        var next = Read(ListChildren, Continue(Children("Lib", 2, 2), first));

        Assert.Equal(new[] { "f" }, next.Items.Select(i => i.Identity.Name));
        Assert.Equal(3, next.Total);
        Assert.Null(next.SnapshotId);
        Assert.True(next.Complete);
    }

    private static LibraryQueryInfo Continue(LibraryQueryInfo query, LibraryPageInfo first)
    {
        query.SnapshotId = first.SnapshotId;
        return query;
    }

    [Fact]
    public void Snapshot_OtherProjectOrOperation_CursorSnapshotMismatch()
    {
        var library = AddGlobal("Lib", @"C:\Libs\Lib.al21");
        library.TypeFolder.AddType("a");
        library.TypeFolder.AddType("b");
        var first = Read(ListChildren, Children("Lib", 0, 1));

        var otherOperation = Fails("library_global_list_versions", new LibraryQueryInfo
            { Scope = "global", LibraryName = "Lib", TypeGuid = Guid.NewGuid().ToString(), Offset = 1, Limit = 1, SnapshotId = first.SnapshotId });
        var otherProject = Fails(ListChildren, Continue(Children("Lib", 1, 1), first), new Siemens.Engineering.Project());

        Assert.Equal(WorkerFailureCategories.CursorSnapshotMismatch, otherOperation.FailureCategory);
        Assert.Equal(WorkerFailureCategories.CursorSnapshotMismatch, otherProject.FailureCategory);
    }

    [Fact]
    public void Snapshot_Evicted_SnapshotUnavailable()
    {
        var library = AddGlobal("Lib", @"C:\Libs\Lib.al21");
        library.TypeFolder.AddType("a");
        library.TypeFolder.AddType("b");
        var first = Read(ListChildren, Children("Lib", 0, 1));
        for (var i = 0; i < 4; i++) Read(ListChildren, Children("Lib", 0, 1));

        Assert.Equal(WorkerFailureCategories.SnapshotUnavailable, Fails(ListChildren, Continue(Children("Lib", 1, 1), first)).FailureCategory);
        Assert.Equal(WorkerFailureCategories.SnapshotUnavailable,
            Fails(ListChildren, Continue(Children("Lib", 1, 1), new LibraryPageInfo { SnapshotId = "unknown" })).FailureCategory);
    }

    [Fact]
    public void Continuation_DeletedRow_FailsOnlyItsRow()
    {
        var library = AddGlobal("Lib", @"C:\Libs\Lib.al21");
        library.TypeFolder.AddType("a");
        var deleted = library.TypeFolder.AddType("b");
        library.TypeFolder.AddType("c");
        var first = Read(ListChildren, Children("Lib", 0, 1));

        deleted.Disposed = true;
        var next = Read(ListChildren, Continue(Children("Lib", 1, 2), first));

        Assert.Equal(2, next.Items.Count);
        Assert.Contains("no longer exists", Assert.Single(next.Items[0].Diagnostics));
        Assert.Equal("type", next.Items[0].Identity.Kind);
        Assert.Equal("c", next.Items[1].Identity.Name);
        Assert.Empty(next.Items[1].Diagnostics);
    }

    [Fact]
    public void Continuation_LibraryClosed_WorkerOperationFailed()
    {
        var library = AddGlobal("Lib", @"C:\Libs\Lib.al21");
        library.TypeFolder.AddType("a");
        library.TypeFolder.AddType("b");
        var first = Read(ListChildren, Children("Lib", 0, 1));

        library.Disposed = true;
        var failure = Fails(ListChildren, Continue(Children("Lib", 1, 1), first));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, failure.FailureCategory);
        Assert.NotNull(failure.EngineeringError);
    }

    [Fact]
    public void Continuation_GlobalListPortalLost_WorkerOperationFailed()
    {
        AddGlobal("A", @"C:\Libs\A.al21");
        AddGlobal("B", @"C:\Libs\B.al21");
        var first = Read(ListGlobal, new LibraryQueryInfo { Limit = 1 });

        portal.GlobalLibrariesFailure = new EngineeringObjectDisposedException("portal gone");
        var failure = Assert.Throws<WorkerOperationException>(() => Read(ListGlobal, Continue(new LibraryQueryInfo { Offset = 1, Limit = 1 }, first)));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, failure.FailureCategory);
        Assert.NotNull(failure.EngineeringError);
    }

    [Fact]
    public void Optional_RecoverableEngineeringFailure_NullWithDiagnostic()
    {
        var library = AddGlobal("Lib", @"C:\Libs\Lib.al21");
        library.ReadOnlyFailure = new EngineeringNotSupportedException("not here");

        var entry = Assert.Single(Read("library_global_inspect_library", Global("Lib")).Items);

        Assert.Null(entry.IsReadOnly);
        Assert.Contains(entry.Diagnostics, d => d.StartsWith("isReadOnly unavailable", StringComparison.Ordinal));
        Assert.False(entry.IsWriteProtected);
    }

    [Fact]
    public void Optional_DisposedOrFatal_Escapes()
    {
        var library = AddGlobal("Lib", @"C:\Libs\Lib.al21");

        library.ReadOnlyFailure = new EngineeringObjectDisposedException("gone");
        Assert.Throws<EngineeringObjectDisposedException>(() => Read("library_global_inspect_library", Global("Lib")));
        library.ReadOnlyFailure = new NonRecoverableException("portal");
        Assert.Throws<NonRecoverableException>(() => Read("library_global_inspect_library", Global("Lib")));
        library.ReadOnlyFailure = new EngineeringException("other");
        Assert.Throws<EngineeringException>(() => Read("library_global_inspect_library", Global("Lib")));
    }

    [Fact]
    public void RecoverablePropertyFailure_IsNarrowAndHmiForwards()
    {
        Assert.True(OpennessExceptions.IsRecoverablePropertyFailure(new EngineeringTargetInvocationException("x")));
        Assert.True(OpennessExceptions.IsRecoverablePropertyFailure(new EngineeringNotSupportedException("x")));
        Assert.False(OpennessExceptions.IsRecoverablePropertyFailure(new EngineeringObjectDisposedException("x")));
        Assert.False(OpennessExceptions.IsRecoverablePropertyFailure(new EngineeringException("x")));
        Assert.True(OpennessExceptions.IsRecoverable(new EngineeringObjectDisposedException("x")));
        Assert.False(HmiReadLog.IsRecoverable(new EngineeringObjectDisposedException("x")));
        Assert.True(HmiReadLog.IsRecoverable(new EngineeringNotSupportedException("x")));
    }

    [Fact]
    public void Owner_DepthCapFails()
    {
        var library = AddGlobal("Lib", @"C:\Libs\Lib.al21");
        LibraryTypeFolder folder = library.TypeFolder;
        for (var i = 0; i < 65; i++) folder = ((LibraryTypeFolder)folder).AddFolder("f" + i);
        var deep = folder.AddType("Deep");

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, Assert.Throws<WorkerOperationException>(() => LibraryReader.Owner(deep)).FailureCategory);

        var shallow = library.TypeFolder.AddFolder("A").AddType("T");
        var (owner, path) = LibraryReader.Owner(shallow);
        Assert.Same(library, owner);
        Assert.Equal(new[] { "A" }, path);
    }

    [Fact]
    public void Reader_CallsNoMutatingMember()
    {
        // Every double throws on SetAttribute; a full read of every operation, including a continuation, must not reach it.
        var library = AddGlobal("Lib", @"C:\Libs\Lib.al21");
        project.ProjectLibrary = new ProjectLibrary();
        var type = library.TypeFolder.AddFolder("F").AddType("T");
        var version = type.AddVersion("1.0.0");
        type.AddVersion("1.0.1");
        version.Dependents.Items.Add(project.ProjectLibrary.TypeFolder.AddType("P").AddVersion("1.0.0"));
        library.MasterCopyFolder.AddCopy("C");

        Read(ListGlobal, new LibraryQueryInfo());
        Read("library_global_inspect_library", Global("Lib"));
        Read("library_project_inspect_library", new LibraryQueryInfo { Scope = "project" });
        Read("library_project_list_children", new LibraryQueryInfo { Scope = "project", FolderKind = "types" });
        Read(ListChildren, Children("Lib", kind: "masterCopies"));
        var first = Read("library_global_list_versions", new LibraryQueryInfo
            { Scope = "global", LibraryName = "Lib", FolderPath = new() { "F" }, TypeGuid = type.Guid.ToString(), Limit = 1 });
        Read("library_global_list_versions", new LibraryQueryInfo
            { Scope = "global", LibraryName = "Lib", FolderPath = new() { "F" }, TypeGuid = type.Guid.ToString(), Offset = 1, Limit = 1, SnapshotId = first.SnapshotId });
        var dependents = Read("library_global_list_dependencies", new LibraryQueryInfo
        {
            Scope = "global", LibraryName = "Lib", FolderPath = new() { "F" }, TypeGuid = type.Guid.ToString(),
            VersionGuid = version.Guid.ToString(), Direction = "dependents",
        });

        Assert.Equal("project", Assert.Single(dependents.Items).Identity.Scope);
    }
}

[Collection(HmiSnapshotStoreCollection.Name)]
public sealed class LibrarySharedSnapshotStoreTests
{
    [Fact]
    public void Snapshot_LibraryAndHmiStoresIndependent()
    {
        var project = new object();
        var software = new HmiSoftware();
        var hmi = HmiSnapshotStore.Shared.Put(project, software, "hmi_list_tags", new object[] { "T" });
        for (var i = 0; i < 4; i++)
        {
            var owner = new object();
            LibraryReader.SharedSnapshots.Put(project, owner, "library_list_global_libraries",
                id => new LibrarySnapshot(owner, new object[] { "L" }, id));
        }

        Assert.Same(software, HmiSnapshotStore.Shared.Take(project, hmi, "hmi_list_tags").Software);
        Assert.NotSame((object)HmiSnapshotStore.Shared, LibraryReader.SharedSnapshots);
    }
}
