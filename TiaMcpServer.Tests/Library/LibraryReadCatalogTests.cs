using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using Xunit;

namespace TiaMcpServer.Tests.Library;

public sealed class LibraryReadCatalogTests
{
    private static readonly string[] AllKinds =
    {
        ProjectContainerKinds.StandaloneProject, ProjectContainerKinds.LocalSession, ProjectContainerKinds.ServerProject
    };

    [Fact]
    public void Methods_AreUniqueAndPartitioned()
    {
        Assert.Equal(9, LibraryReadMethods.All.Length);
        Assert.Equal(LibraryReadMethods.All.Length, LibraryReadMethods.All.Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(LibraryReadMethods.ProjectLibraryReads.Intersect(LibraryReadMethods.GlobalLibraryReads));
        Assert.True(LibraryReadMethods.All.ToHashSet(StringComparer.Ordinal)
            .SetEquals(LibraryReadMethods.ProjectLibraryReads.Concat(LibraryReadMethods.GlobalLibraryReads)));
        Assert.Equal(4, LibraryReadMethods.ProjectLibraryReads.Length);
        Assert.Equal(5, LibraryReadMethods.GlobalLibraryReads.Length);

        foreach (var operation in LibraryReadMethods.Operations.Skip(1))
            foreach (var scope in new[] { "project", "global" })
            {
                var method = LibraryReadMethods.ForOperation(operation, scope);
                Assert.Contains(method, LibraryReadMethods.All);
                Assert.Equal(operation, LibraryReadMethods.OperationFor(method));
            }
        var list = LibraryReadMethods.ForOperation("list_global_libraries", null);
        Assert.Equal("library_list_global_libraries", list);
        Assert.Equal("list_global_libraries", LibraryReadMethods.OperationFor(list));
        Assert.Null(LibraryReadMethods.OperationFor("hmi_list_tags"));
    }

    [Fact]
    public void AllMethods_AllowedInEveryAccessMode()
    {
        foreach (var mode in Enum.GetValues<McpAccessMode>())
            foreach (var method in LibraryReadMethods.All)
            {
                Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(method));
                Assert.True(OperationPolicyCatalog.IsAllowed(mode, method), $"{mode} {method}");
            }
    }

    [Fact]
    public void AllMethods_NeedBindingAndIdentity()
        => Assert.All(LibraryReadMethods.All, method =>
        {
            Assert.True(ProjectCapabilityCatalog.RequiresBoundProject(method), method);
            Assert.True(OperationPolicyCatalog.RequiresExpectedSessionIdentity(method), method);
        });

    [Fact]
    public void GlobalReads_StandaloneAndLocalSession()
    {
        foreach (var method in LibraryReadMethods.GlobalLibraryReads.Append("library_read"))
        {
            Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.StandaloneProject, method), method);
            Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, method), method);
            Assert.False(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.ServerProject, method), method);
            Assert.False(ProjectCapabilityCatalog.Supports("unknownKind", method), method);
        }
    }

    [Fact]
    public void ProjectLibraryReads_StandaloneOnly()
    {
        foreach (var method in LibraryReadMethods.ProjectLibraryReads.Append("library_project_read"))
        {
            Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.StandaloneProject, method), method);
            Assert.False(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, method), method);
            Assert.False(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.ServerProject, method), method);
            Assert.False(ProjectCapabilityCatalog.Supports("unknownKind", method), method);
        }
        foreach (var kind in AllKinds)
            Assert.Equal(ProjectCapabilityApplicabilities.StandaloneOnly,
                ProjectCapabilityCatalog.Describe(kind).Single(c => c.Operation == "library_project_read").Applicability);
    }

    [Theory]
    [InlineData("library_global_list_children", "library_read")]
    [InlineData("library_list_global_libraries", "library_read")]
    [InlineData("library_project_list_versions", "library_project_read")]
    [InlineData("library_project_inspect_library", "library_project_read")]
    public void OperationFor_MapsOneMethodOfEachSet(string method, string expected)
        => Assert.Equal(expected, ProjectCapabilityCatalog.OperationFor(method));

    private const string Lib = @"C:\Libs\Fixture";
    private const string Guid1 = "11111111-1111-1111-1111-111111111111";
    private const string Guid2 = "22222222-2222-2222-2222-222222222222";

    public static TheoryData<string, LibraryQueryInfo> ValidQueries => new()
    {
        { "list_global_libraries", new() },
        { "list_global_libraries", new() { Offset = 0, Limit = 200 } },
        { "inspect_library", new() { Scope = "project" } },
        { "inspect_library", new() { Scope = "global", LibraryPath = Lib } },
        { "inspect_library", new() { Scope = "global", LibraryName = "Fixture" } },
        { "inspect_library", new() { Scope = "global", LibraryPath = @"\\host\share", LibraryName = "Fixture" } },
        { "list_children", new() { Scope = "project", FolderKind = "types", FolderPath = new() { "A", "B" } } },
        { "list_children", new() { Scope = "global", LibraryPath = Lib, FolderKind = "masterCopies", Limit = 1 } },
        { "list_versions", new() { Scope = "project", TypeGuid = Guid1 } },
        { "list_dependencies", new() { Scope = "project", TypeGuid = Guid1 } },
        { "list_dependencies", new() { Scope = "global", LibraryName = "Fixture", TypeGuid = Guid1, VersionGuid = Guid2, Direction = "dependents" } },
        { "list_dependencies", new() { Scope = "project", TypeGuid = Guid1, VersionGuid = Guid2 } },
    };

    [Theory]
    [MemberData(nameof(ValidQueries))]
    public void Validate_AcceptsValidQueries(string operation, LibraryQueryInfo query)
        => Assert.Null(LibraryReadMethods.Validate(operation, query));

    public static TheoryData<string, LibraryQueryInfo> InvalidQueries => new()
    {
        { "unknown_op", new() },
        { "inspect_library", new() },
        { "inspect_library", new() { Scope = "other" } },
        { "inspect_library", new() { Scope = "project", LibraryPath = Lib } },
        { "inspect_library", new() { Scope = "project", LibraryName = "Fixture" } },
        { "inspect_library", new() { Scope = "global" } },
        { "inspect_library", new() { Scope = "global", LibraryName = " " } },
        { "inspect_library", new() { Scope = "global", LibraryPath = @"Libs\Fixture" } },
        { "inspect_library", new() { Scope = "global", LibraryPath = @"\\host" } },
        { "inspect_library", new() { Scope = "global", LibraryPath = @"\\host\share", LibraryName = "" } },
        { "inspect_library", new() { Scope = "project", Limit = 10 } },
        { "inspect_library", new() { Scope = "project", FolderKind = "types" } },
        { "list_children", new() { Scope = "project" } },
        { "list_children", new() { Scope = "project", FolderKind = "other" } },
        { "list_children", new() { Scope = "project", FolderKind = "types", TypeGuid = Guid1 } },
        { "list_children", new() { Scope = "project", FolderKind = "types", FolderPath = Enumerable.Repeat("a", 33).ToList() } },
        { "list_children", new() { Scope = "project", FolderKind = "types", FolderPath = new() { "a", " " } } },
        { "list_children", new() { Scope = "project", FolderKind = "types", Limit = 0 } },
        { "list_children", new() { Scope = "project", FolderKind = "types", Limit = 201 } },
        { "list_children", new() { Scope = "project", FolderKind = "types", Offset = -1 } },
        { "list_versions", new() { Scope = "project" } },
        { "list_versions", new() { Scope = "project", TypeGuid = "not-a-guid" } },
        { "list_versions", new() { Scope = "project", TypeGuid = Guid.Empty.ToString() } },
        { "list_versions", new() { Scope = "project", TypeGuid = Guid1, FolderKind = "types" } },
        { "list_versions", new() { Scope = "project", TypeGuid = Guid1, VersionGuid = Guid2 } },
        { "list_versions", new() { Scope = "project", TypeGuid = Guid1, Direction = "dependencies" } },
        { "list_dependencies", new() { Scope = "project", TypeGuid = Guid1, VersionGuid = Guid.Empty.ToString() } },
        { "list_dependencies", new() { Scope = "project", TypeGuid = Guid1, VersionGuid = Guid2, Direction = "up" } },
        { "list_global_libraries", new() { Scope = "global" } },
        { "list_global_libraries", new() { LibraryPath = Lib } },
        { "list_global_libraries", new() { LibraryName = "Fixture" } },
        { "list_global_libraries", new() { FolderKind = "types" } },
        { "list_global_libraries", new() { FolderPath = new() { "a" } } },
        { "list_global_libraries", new() { TypeGuid = Guid1 } },
        { "list_global_libraries", new() { VersionGuid = Guid2 } },
        { "list_global_libraries", new() { Direction = "dependencies" } },
        { "list_global_libraries", new() { Limit = 0 } },
        { "list_global_libraries", new() { Limit = 201 } },
    };

    [Theory]
    [MemberData(nameof(InvalidQueries))]
    public void Validate_RejectsInvalidQueries(string operation, LibraryQueryInfo query)
        => Assert.False(string.IsNullOrEmpty(LibraryReadMethods.Validate(operation, query)));

    [Theory]
    [InlineData(@"C:\Libs\X", true)]
    [InlineData("C:/Libs/X", true)]
    [InlineData(@"\\host\share", true)]
    [InlineData(@"\\host", false)]
    [InlineData(@"Libs\X", false)]
    [InlineData("C:", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAbsolutePath_AcceptsDriveAndUncOnly(string? path, bool expected)
        => Assert.Equal(expected, LibraryReadMethods.IsAbsolutePath(path));

    [Fact]
    public void Query_RejectsUnmappedMembers()
        => Assert.ThrowsAny<System.Text.Json.JsonException>(() =>
            System.Text.Json.JsonSerializer.Deserialize<LibraryQueryInfo>("{\"scope\":\"project\",\"bogus\":1}",
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }));
}
