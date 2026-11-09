using System.Text.Json;
using System.Text.Json.Serialization;
using TiaMcpServer.Contracts.Library;
using Xunit;

namespace TiaMcpServer.Tests.Library;

public sealed class LibraryWriteMethodsTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static TheoryData<string, string, string> ValidRequests => new()
    {
        { "create_global", """{"directory":"C:\\Libraries","name":"Fixture"}""", "Directory Name Author" },
        { "open_global", """{"filePath":"C:\\Libraries\\Fixture.AL21","openMode":"readOnly"}""", "FilePath OpenMode" },
        { "retrieve_global", """{"filePath":"C:\\Archives\\Fixture.ZAL21","directory":"C:\\Libraries","openMode":"readWrite"}""", "FilePath Directory OpenMode" },
        { "save_global", """{"scope":"global","libraryName":"Fixture"}""", "Scope LibraryPath LibraryName" },
        { "close_global", """{"scope":"global","libraryName":"Fixture","closeAction":"discard"}""", "Scope LibraryPath LibraryName CloseAction" },
        { "archive_global", """{"scope":"global","libraryName":"Fixture","directory":"C:\\Archives","name":"Backup"}""", "Scope LibraryPath LibraryName Directory Name" },
        { "create_folder", """{"scope":"project","folderKind":"types","name":"Folder"}""", "Scope LibraryPath LibraryName FolderKind FolderPath Name" },
        { "delete_folder", """{"scope":"global","libraryName":"Fixture","folderKind":"masterCopies","name":"Folder"}""", "Scope LibraryPath LibraryName FolderKind FolderPath Name" },
        { "create_master_copy", """{"scope":"global","libraryName":"Fixture","plcName":"Controller","plcKind":"block","plcObjectName":"Source"}""", "Scope LibraryPath LibraryName FolderPath PlcName PlcKind PlcFolderPath PlcObjectName" },
        { "copy_master_copy", """{"scope":"global","libraryName":"Destination","sourceScope":"global","sourceLibraryName":"Source","name":"Copy"}""", "Scope LibraryPath LibraryName FolderPath SourceScope SourceLibraryPath SourceLibraryName SourceFolderPath Name" },
        { "instantiate_master_copy", """{"scope":"global","libraryName":"Fixture","name":"Copy","plcName":"Controller","plcKind":"blockGroup"}""", "Scope LibraryPath LibraryName FolderPath Name PlcName PlcKind PlcFolderPath" },
        { "delete_master_copy", """{"scope":"project","name":"Copy"}""", "Scope LibraryPath LibraryName FolderPath Name" },
    };

    public static IEnumerable<object[]> RequiredRequests => ValidRequests.Select(row => new[] { row[0], row[1] });

    [Theory]
    [MemberData(nameof(RequiredRequests))]
    public void Validate_AcceptsRequiredMembers(string operation, string json)
        => Assert.Null(LibraryWriteMethods.Validate(Request(operation, json)));

    [Theory]
    [MemberData(nameof(ValidRequests))]
    public void Validate_RejectsMemberNotInOperation(string operation, string json, string allowed)
    {
        var permitted = allowed.Split(' ').Concat(new[] { "Operation", "OperationId" }).ToHashSet();
        foreach (var member in typeof(LibraryWriteRequestInfo).GetProperties().Where(p => !permitted.Contains(p.Name)))
        {
            var item = Request(operation, json);
            member.SetValue(item, member.PropertyType == typeof(string) ? "unexpected" : member.PropertyType == typeof(bool?) ? true : new List<string> { "unexpected" });
            Assert.NotNull(LibraryWriteMethods.Validate(item));
        }
    }

    [Fact]
    public void Validate_RequiresCloseAction()
    {
        var item = new LibraryWriteRequestInfo { Operation = "close_global", Scope = "global", LibraryName = "Fixture" };
        Assert.Contains("closeAction", LibraryWriteMethods.Validate(item));
        item.CloseAction = "save";
        Assert.Null(LibraryWriteMethods.Validate(item));
        item.CloseAction = "discard";
        Assert.Null(LibraryWriteMethods.Validate(item));
        item.CloseAction = "other";
        Assert.NotNull(LibraryWriteMethods.Validate(item));
    }

    [Theory]
    [InlineData("create_global", "directory", "relative")]
    [InlineData("archive_global", "directory", "relative")]
    [InlineData("open_global", "filePath", "relative.al21")]
    [InlineData("retrieve_global", "filePath", "relative.zal21")]
    [InlineData("retrieve_global", "directory", "relative")]
    public void Validate_RejectsRelativeDirectoryAndFilePath(string operation, string member, string value)
        => Assert.NotNull(LibraryWriteMethods.Validate(Changed(operation, member, value)));

    [Theory]
    [InlineData("open_global", "filePath", "C:\\Libraries\\Fixture.zal21")]
    [InlineData("retrieve_global", "filePath", "C:\\Libraries\\Fixture.al21")]
    [InlineData("open_global", "openMode", "ReadOnly")]
    [InlineData("retrieve_global", "openMode", "other")]
    [InlineData("create_global", "name", " ")]
    [InlineData("create_global", "directory", null)]
    [InlineData("open_global", "filePath", null)]
    [InlineData("open_global", "openMode", null)]
    [InlineData("retrieve_global", "directory", null)]
    [InlineData("create_folder", "folderKind", "other")]
    [InlineData("create_folder", "name", null)]
    [InlineData("delete_folder", "folderKind", null)]
    [InlineData("create_master_copy", "plcName", null)]
    [InlineData("create_master_copy", "plcKind", "blockGroup")]
    [InlineData("create_master_copy", "plcObjectName", " ")]
    [InlineData("instantiate_master_copy", "plcName", " ")]
    [InlineData("instantiate_master_copy", "plcKind", "block")]
    [InlineData("instantiate_master_copy", "name", null)]
    [InlineData("delete_master_copy", "name", null)]
    [InlineData("save_global", "scope", "project")]
    [InlineData("copy_master_copy", "sourceScope", null)]
    [InlineData("copy_master_copy", "name", null)]
    public void Validate_RejectsMissingOrInvalidRequiredValue(string operation, string member, string? value)
        => Assert.NotNull(LibraryWriteMethods.Validate(Changed(operation, member, value)));

    [Theory]
    [InlineData("libraryPath", "C:\\Libraries\\Fixture.al21")]
    [InlineData("libraryName", "Fixture")]
    public void Validate_ProjectScopeRejectsLibraryPathAndName(string member, string value)
        => Assert.NotNull(LibraryWriteMethods.Validate(Changed("delete_master_copy", member, value)));

    [Theory]
    [InlineData("block")]
    [InlineData("plcType")]
    [InlineData("tagTable")]
    public void Validate_CreateMasterCopyAcceptsSourceKindsWithoutName(string kind)
        => Assert.Null(LibraryWriteMethods.Validate(Changed("create_master_copy", "plcKind", kind)));

    [Theory]
    [InlineData("blockGroup")]
    [InlineData("typeGroup")]
    [InlineData("tagTableGroup")]
    public void Validate_InstantiateAcceptsDestinationKinds(string kind)
        => Assert.Null(LibraryWriteMethods.Validate(Changed("instantiate_master_copy", "plcKind", kind)));

    [Fact]
    public void Validate_RequiresBothGlobalSelectorsAndRejectsProjectSourceIdentity()
    {
        Assert.NotNull(LibraryWriteMethods.Validate(Changed("save_global", "libraryName", null)));
        Assert.NotNull(LibraryWriteMethods.Validate(Changed("copy_master_copy", "sourceLibraryName", null)));
        Assert.NotNull(LibraryWriteMethods.Validate(Changed("copy_master_copy", "sourceScope", "project")));
        Assert.NotNull(LibraryWriteMethods.Validate(new() { Operation = "unknown" }));
    }

    [Theory]
    [InlineData("folderPath", 33, "Segment")]
    [InlineData("folderPath", 1, " ")]
    [InlineData("sourceFolderPath", 33, "Segment")]
    [InlineData("sourceFolderPath", 1, " ")]
    [InlineData("plcFolderPath", 33, "Segment")]
    [InlineData("plcFolderPath", 1, " ")]
    public void Validate_RejectsInvalidSegmentedPaths(string member, int count, string segment)
    {
        var operation = member == "plcFolderPath" ? "create_master_copy" : "copy_master_copy";
        Assert.NotNull(LibraryWriteMethods.Validate(Changed(operation, member, Enumerable.Repeat(segment, count).ToArray())));
    }

    [Theory]
    [InlineData("copy_master_copy", "folderPath", 0)]
    [InlineData("copy_master_copy", "folderPath", 32)]
    [InlineData("copy_master_copy", "sourceFolderPath", 32)]
    [InlineData("instantiate_master_copy", "plcFolderPath", 0)]
    [InlineData("create_master_copy", "plcFolderPath", 32)]
    public void Validate_AcceptsSegmentedPathsAtBoundary(string operation, string member, int count)
        => Assert.Null(LibraryWriteMethods.Validate(Changed(operation, member, Enumerable.Repeat("Segment", count).ToArray())));

    [Theory]
    [InlineData("save_global", """{"scope":"global","libraryPath":"\\\\host\\share\\Fixture.al21"}""")]
    [InlineData("copy_master_copy", """{"scope":"project","sourceScope":"global","sourceLibraryPath":"C:\\Libraries\\Fixture.al21","name":"Copy"}""")]
    [InlineData("copy_master_copy", """{"scope":"global","libraryName":"Fixture","sourceScope":"project","name":"Copy"}""")]
    [InlineData("delete_folder", """{"scope":"global","libraryPath":"C:\\Libraries\\Fixture.al21","libraryName":"Fixture","folderKind":"types","folderPath":["Parent"],"name":"Folder"}""")]
    public void Validate_AcceptsOptionalLibrarySelectors(string operation, string json)
        => Assert.Null(LibraryWriteMethods.Validate(Request(operation, json)));

    [Theory]
    [InlineData("project", "global", "copy_master_copy", true)]
    [InlineData("global", "project", "copy_master_copy", true)]
    [InlineData("global", "global", "copy_master_copy", false)]
    [InlineData("global", null, "create_master_copy", true)]
    [InlineData("global", null, "instantiate_master_copy", true)]
    [InlineData("project", null, "create_folder", true)]
    [InlineData("global", null, "delete_folder", false)]
    public void ForItem_UsesProjectVariantWhenItemTouchesProject(string scope, string? sourceScope, string operation, bool project)
    {
        var item = new LibraryWriteRequestInfo { Scope = scope, SourceScope = sourceScope, Operation = operation };
        Assert.Equal(project, LibraryWriteMethods.TouchesProject(item));
        Assert.Equal($"library_{(project ? "project" : "global")}_{operation}", LibraryWriteMethods.ForItem(item));
        Assert.Equal(project ? "library_project_plan" : "library_global_plan", LibraryWriteMethods.PlanFor(item));
    }

    [Fact]
    public void LifecycleOperations_HaveOnlyGlobalMethods()
    {
        var expected = new[] { "create_global", "open_global", "retrieve_global", "save_global", "close_global", "archive_global" };
        Assert.Equal(expected, LibraryWriteMethods.LifecycleOperations);
        foreach (var operation in expected)
        {
            Assert.Contains("library_global_" + operation, LibraryWriteMethods.GlobalLibraryWrites);
            Assert.DoesNotContain("library_project_" + operation, LibraryWriteMethods.All);
        }
        Assert.DoesNotContain("library_global_create_master_copy", LibraryWriteMethods.All);
        Assert.DoesNotContain("library_global_instantiate_master_copy", LibraryWriteMethods.All);
    }

    [Fact]
    public void OperationFor_MapsEveryMethod()
    {
        Assert.Equal(20, LibraryWriteMethods.Operations.Length);
        Assert.Equal(14, LibraryWriteMethods.GlobalLibraryWrites.Length);
        Assert.Equal(14, LibraryWriteMethods.ProjectLibraryWrites.Length);
        Assert.Equal(30, LibraryWriteMethods.All.Distinct(StringComparer.Ordinal).Count());
        foreach (var method in LibraryWriteMethods.GlobalLibraryWrites.Append(LibraryWriteMethods.GlobalPlan))
            Assert.Equal("library_write", LibraryWriteMethods.OperationFor(method));
        foreach (var method in LibraryWriteMethods.ProjectLibraryWrites.Append(LibraryWriteMethods.ProjectPlan))
            Assert.Equal("library_project_write", LibraryWriteMethods.OperationFor(method));
        Assert.Null(LibraryWriteMethods.OperationFor("library_global_unknown"));
        Assert.Null(LibraryWriteMethods.OperationFor("LIBRARY_GLOBAL_PLAN"));
        Assert.Null(LibraryWriteMethods.OperationFor("library_global_create_master_copy"));
    }

    [Fact]
    public void Request_RejectsUnmappedMembers()
        => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LibraryWriteRequestInfo>("""{"unitName":"unsupported"}""", Json));

    [Fact]
    public void Results_KeepNullableMembersWhenNullsAreOtherwiseOmitted()
    {
        var options = new JsonSerializerOptions(Json) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        foreach (var value in new object[] { new LibraryWriteRequestInfo(), new LibraryWritePlanInfo(), new LibraryWriteResultInfo(), new LibraryWriteOutcomeInfo(), new LibraryTypeResultInfo() })
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(value, value.GetType(), options));
            foreach (var member in value.GetType().GetProperties().Where(p => p.GetValue(value) is null && !p.GetCustomAttributes(typeof(JsonIgnoreAttribute), false).Cast<JsonIgnoreAttribute>().Any(a => a.Condition == JsonIgnoreCondition.WhenWritingNull)))
                Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty(JsonNamingPolicy.CamelCase.ConvertName(member.Name)).ValueKind);
        }
    }

    [Theory]
    [InlineData("sourceScope", "other", "sourceScope")]
    [InlineData("sourceLibraryPath", "relative", "sourceLibraryPath")]
    [InlineData("sourceLibraryName", " ", "sourceLibraryName")]
    public void SelectorRules_UsesSourceMemberNames(string member, string value, string expected)
        => Assert.Contains(expected, LibraryWriteMethods.Validate(Changed("copy_master_copy", member, value)));

    public static IEnumerable<object?[]> Authors()
    {
        foreach (var operation in new[] { "create_global", "release_type_version" })
        {
            yield return new object?[] { operation, null, true };
            yield return new object?[] { operation, "", operation != "create_global" };
            yield return new object?[] { operation, " \r\n\t", operation != "create_global" };
            yield return new object?[] { operation, "  First\nSecond\r\nThird 😀  ", true };
            yield return new object?[] { operation, new string('A', 255), true };
            yield return new object?[] { operation, new string('A', 256), false };
            yield return new object?[] { operation, new string('A', 10000), false };
            yield return new object?[] { operation, string.Concat(Enumerable.Repeat("😀", 127)) + "A", true };
            yield return new object?[] { operation, string.Concat(Enumerable.Repeat("😀", 128)), false };
        }
    }

    [Theory]
    [MemberData(nameof(Authors))]
    public void AuthorAdmission_SharedHostAndWorkerRules(string operation, string? author, bool valid)
    {
        var item = AuthorRequest(operation, author);
        var error = LibraryWriteMethods.Validate(item);
        Assert.Equal(valid, error is null);
        if (author?.Length > 255)
        {
            Assert.Contains("author", error);
            Assert.Contains("255", error);
            Assert.Contains(author.Length.ToString(), error);
            Assert.DoesNotContain(author, error!);
        }
        Assert.Equal(author, item.Author);
    }

    public static IEnumerable<object?[]> InvalidAuthors => Authors().Where(row => !(bool)row[2]!).Select(row => row.Take(2).ToArray());
    public static IEnumerable<object?[]> CreationAuthors => Authors().Where(row => (string)row[0]! == "create_global" && (bool)row[2]!)
        .Select(row => new[] { row[1] });
    public static IEnumerable<object?[]> ReleaseAuthors => Authors().Where(row => (string)row[0]! == "release_type_version" && (bool)row[2]!)
        .Select(row => new[] { row[1] });

    internal static LibraryWriteRequestInfo AuthorRequest(string operation, string? author) => operation == "create_global"
        ? new() { Operation = operation, Directory = @"C:\Output", Name = "New", Author = author }
        : new() { Operation = operation, Scope = "project", TypeName = "FixtureType", Author = author };

    private static LibraryWriteRequestInfo Request(string operation, string json)
    {
        var item = JsonSerializer.Deserialize<LibraryWriteRequestInfo>(json, Json)!;
        item.Operation = operation;
        item.OperationId = "item";
        return item;
    }

    private static LibraryWriteRequestInfo Changed(string operation, string member, object? value)
    {
        var fixture = ValidRequests.Single(row => (string)row[0] == operation);
        var members = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>((string)fixture[1])!;
        members[member] = JsonSerializer.SerializeToElement(value);
        return Request(operation, JsonSerializer.Serialize(members));
    }
}
