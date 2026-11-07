using System.Text.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Json;
using TiaMcpServer.Library;
using Xunit;

namespace TiaMcpServer.Tests.Library;

public sealed class LibraryTypeContractTests
{
    private const string TypeGuid = "11111111-1111-1111-1111-111111111111";
    private const string VersionGuid = "22222222-2222-2222-2222-222222222222";
    private const string Hash = "xml:sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    public static TheoryData<string, string, string> Requests => new()
    {
        { "create_type", """{"plcName":"PLC","plcKind":"block","plcObjectName":"Source"}""", "PlcName PlcKind PlcObjectName PlcFolderPath FolderPath" },
        { "create_type_version", """{"plcName":"PLC","plcKind":"plcType","plcObjectName":"Source","content":"<Document/>","expectedContentHash":"xml:sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}""", "PlcName PlcKind PlcObjectName Content ExpectedContentHash PlcFolderPath ReplaceInWork" },
        { "release_type_version", """{"scope":"project","typeName":"Type"}""", "Scope TypeName TypeGuid FolderPath VersionNumber Author Comment" },
        { "set_default_type_version", """{"scope":"project","typeName":"Type","versionNumber":"1.0.0"}""", "Scope LibraryPath LibraryName TypeName TypeGuid FolderPath VersionNumber VersionGuid InWork" },
        { "synchronize_type", """{"sourceScope":"project","scope":"global","libraryName":"Fixture","typeName":"Type"}""", "SourceScope SourceLibraryPath SourceLibraryName Scope LibraryPath LibraryName TypeName TypeGuid FolderPath" },
        { "instantiate_type_version", """{"sourceScope":"project","typeName":"Type","plcName":"PLC"}""", "SourceScope SourceLibraryPath SourceLibraryName TypeName TypeGuid FolderPath VersionNumber VersionGuid InWork PlcName PlcFolderPath" },
        { "delete_type", """{"scope":"project","typeName":"Type"}""", "Scope LibraryPath LibraryName TypeName TypeGuid FolderPath" },
        { "delete_type_version", """{"scope":"project","typeName":"Type","inWork":true}""", "Scope LibraryPath LibraryName TypeName TypeGuid FolderPath VersionNumber VersionGuid InWork" },
    };

    [Theory]
    [MemberData(nameof(Requests))]
    public void Validate_TypeOperation_RejectsMemberNotInOperation(string operation, string json, string allowed)
    {
        Assert.Null(LibraryWriteMethods.Validate(Request(operation, json)));
        var permitted = allowed.Split(' ').Concat(["Operation", "OperationId"]).ToHashSet();
        foreach (var property in typeof(LibraryWriteRequestInfo).GetProperties().Where(p => !permitted.Contains(p.Name)))
        {
            var item = Request(operation, json);
            property.SetValue(item, property.PropertyType == typeof(string) ? "unexpected"
                : property.PropertyType == typeof(bool?) ? true : new List<string> { "unexpected" });
            Assert.NotNull(LibraryWriteMethods.Validate(item));
        }
        var publicItem = CanonicalJson.Deserialize<LibraryWriteItem>(json);
        publicItem.OperationId = "item";
        publicItem.Operation = operation;
        Assert.True(LibraryWriteCatalog.ValidateWrite([publicItem]).IsValid);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("Type", null, true)]
    [InlineData(null, TypeGuid, true)]
    [InlineData("Type", TypeGuid, false)]
    [InlineData(" ", null, false)]
    [InlineData(null, "not-a-guid", false)]
    public void Validate_TypeSelector_RequiresExactlyOne(string? name, string? guid, bool valid)
        => Assert.Equal(valid, LibraryWriteMethods.Validate(new()
            { Operation = "delete_type", Scope = "project", TypeName = name, TypeGuid = guid }) is null);

    [Theory]
    [InlineData(null, null, null, true)]
    [InlineData("1.0.0", null, null, true)]
    [InlineData(null, VersionGuid, null, true)]
    [InlineData(null, null, true, true)]
    [InlineData(null, null, false, false)]
    [InlineData("1.0.0", VersionGuid, null, false)]
    [InlineData("1.0.0", null, true, false)]
    [InlineData(null, VersionGuid, true, false)]
    public void Validate_VersionSelector_AtMostOne(string? number, string? guid, bool? inWork, bool valid)
        => Assert.Equal(valid, LibraryWriteMethods.Validate(new()
            { Operation = "delete_type_version", Scope = "project", TypeName = "Type", VersionNumber = number, VersionGuid = guid, InWork = inWork }) is null);

    [Theory]
    [InlineData("set_default_type_version")]
    [InlineData("delete_type_version")]
    public void Validate_OmittedVersion_SelectsDefault(string operation)
    {
        var item = new LibraryWriteItem { OperationId = "a", Operation = operation, Scope = "project", TypeName = "Type" };
        Assert.True(LibraryWriteCatalog.ValidateWrite([item]).IsValid);
        Assert.Null(LibraryWriteMethods.Validate(LibraryWriteCatalog.ToWorkerRequest(item)));
    }

    [Theory]
    [InlineData(null, Hash, false)]
    [InlineData("<Document/>", null, false)]
    [InlineData(" ", Hash, false)]
    [InlineData("<Document/>", "bad", false)]
    [InlineData("<Document/>", Hash, true)]
    public void Validate_CreateTypeVersion_RequiresContentAndHash(string? content, string? hash, bool valid)
        => Assert.Equal(valid, LibraryWriteMethods.Validate(new()
        {
            Operation = "create_type_version", PlcName = "PLC", PlcKind = "block", PlcObjectName = "Source",
            Content = content, ExpectedContentHash = hash
        }) is null);

    [Theory]
    [InlineData("0.0.0")]
    [InlineData("00.00.00")]
    [InlineData("1.0")]
    [InlineData("1.0.0.0")]
    [InlineData("-1.0.0")]
    [InlineData("1.a.0")]
    [InlineData(" 1.0.0")]
    [InlineData("2147483648.0.0")]
    [InlineData("999999999999999999999999999.0.0")]
    public void VersionRules_Rejects000AndNonNumeric(string version)
        => Assert.NotNull(LibraryVersionRules.Check(version));

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("0.1.0")]
    [InlineData("0.0.1")]
    [InlineData("2147483647.0.0")]
    public void VersionRules_Accepts100And010And001(string version)
        => Assert.Null(LibraryVersionRules.Check(version));

    [Theory]
    [InlineData("create_type", true)]
    [InlineData("create_type_version", true)]
    [InlineData("release_type_version", true)]
    [InlineData("instantiate_type_version", true)]
    [InlineData("set_default_type_version", false)]
    [InlineData("synchronize_type", false)]
    [InlineData("delete_type", false)]
    [InlineData("delete_type_version", false)]
    public void TouchesProject_TypeOperations(string operation, bool alwaysProject)
    {
        Assert.Equal(alwaysProject, LibraryWriteMethods.TouchesProject(new() { Operation = operation, Scope = "global", SourceScope = "global" }));
        Assert.Equal(alwaysProject, LibraryWriteMethods.GlobalLibraryWrites.Contains("library_global_" + operation) == false);
        Assert.True(LibraryWriteMethods.TouchesProject(new() { Operation = operation, Scope = "project" }));
    }

    public static IEnumerable<object[]> MappingRequests => Requests.Select(row => new object[] { row[0], row[1] });

    [Theory]
    [MemberData(nameof(MappingRequests))]
    public void OperationFor_MapsTypeMethods(string operation, string json)
    {
        var request = Request(operation, json);
        var method = LibraryWriteMethods.ForItem(request);
        Assert.Contains(method, LibraryWriteMethods.All);
        Assert.Equal(LibraryWriteMethods.TouchesProject(request) ? "library_project_write" : "library_write", ProjectCapabilityCatalog.OperationFor(method));
        Assert.DoesNotContain(operation, LibraryWriteMethods.LifecycleOperations);
    }

    [Theory]
    [InlineData("list_versions", "Type", null, null, null, true)]
    [InlineData("list_versions", null, TypeGuid, null, null, true)]
    [InlineData("list_versions", "Type", null, "1.0.0", null, false)]
    [InlineData("list_dependencies", "Type", null, null, null, true)]
    [InlineData("list_dependencies", "Type", null, "1.0.0", null, true)]
    [InlineData("list_dependencies", null, TypeGuid, null, VersionGuid, true)]
    [InlineData("list_dependencies", "Type", null, "1.0.0", VersionGuid, false)]
    public void ReadValidate_ListVersions_AcceptsTypeNameOrGuid(string operation, string? name, string? guid, string? version, string? versionGuid, bool valid)
    {
        var item = new LibraryReadItem { Operation = operation, TypeName = name, TypeGuid = guid, Scope = "project", VersionNumber = version, VersionGuid = versionGuid };
        var query = LibraryReadCatalog.ToQuery(item);
        Assert.Equal(name, query.TypeName);
        Assert.Equal(version, query.VersionNumber);
        Assert.Equal(valid, LibraryReadCatalog.Check(item) is null);
    }

    private static LibraryWriteRequestInfo Request(string operation, string json)
    {
        var item = JsonSerializer.Deserialize<LibraryWriteRequestInfo>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
        item.Operation = operation;
        return item;
    }
}
