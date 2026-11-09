using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Library;
using Xunit;

namespace TiaMcpServer.Tests.Library;

public sealed class LibraryReadItemCatalogTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void IncludeMetadata_MapsToQuery(bool? includeMetadata)
    {
        var item = new LibraryReadItem { Operation = "inspect_library", Scope = "global", LibraryName = "Fixture", IncludeMetadata = includeMetadata };
        Assert.Equal(includeMetadata, LibraryReadCatalog.ToQuery(item).IncludeMetadata);
        Assert.Null(LibraryReadCatalog.Check(item));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncludeMetadata_OtherOperationsRejectExplicitFlag(bool includeMetadata)
    {
        foreach (var operation in new[] { "list_global_libraries", "list_children", "list_versions", "list_dependencies" })
        {
            var item = new LibraryReadItem
            {
                Operation = operation, Scope = operation == "list_global_libraries" ? null : "project",
                FolderKind = operation == "list_children" ? "types" : null,
                TypeName = operation is "list_versions" or "list_dependencies" ? "Type" : null, IncludeMetadata = includeMetadata,
            };
            Assert.Contains("includeMetadata is only valid for inspect_library", LibraryReadCatalog.Check(item));
        }
    }

    private const string Guid1 = "11111111-1111-1111-1111-111111111111";
    private const string Guid2 = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public void Item_MapsToQuery()
    {
        var item = new LibraryReadItem
        {
            OperationId = "deps", Operation = "list_dependencies", Scope = "global", LibraryPath = @"C:\Libs\Fixture.al21",
            LibraryName = "Fixture", FolderPath = new List<string> { "A", "B" }, TypeGuid = Guid1, VersionGuid = Guid2,
            Direction = "dependents", Offset = 3, Limit = 7,
        };

        var query = LibraryReadCatalog.ToQuery(item);

        Assert.Equal("global", query.Scope);
        Assert.Equal(@"C:\Libs\Fixture.al21", query.LibraryPath);
        Assert.Equal("Fixture", query.LibraryName);
        Assert.Equal(new[] { "A", "B" }, query.FolderPath);
        Assert.Equal(Guid1, query.TypeGuid);
        Assert.Equal(Guid2, query.VersionGuid);
        Assert.Equal("dependents", query.Direction);
        Assert.Equal(3, query.Offset);
        Assert.Equal(7, query.Limit);
        Assert.Null(query.FolderKind);
        Assert.Null(query.SnapshotId);
    }

    [Fact]
    public void PagedItem_DefaultsPaging_InspectKeepsNone()
    {
        var paged = LibraryReadCatalog.ToQuery(new LibraryReadItem { Operation = "list_global_libraries" });
        var inspect = LibraryReadCatalog.ToQuery(new LibraryReadItem { Operation = "inspect_library" });

        Assert.Equal(0, paged.Offset);
        Assert.Equal(LibraryReadCatalog.DefaultLimit, paged.Limit);
        Assert.Null(inspect.Offset);
        Assert.Null(inspect.Limit);
    }

    [Fact]
    public void CursorWithOffset_Rejected()
    {
        var item = new LibraryReadItem
        {
            OperationId = "c", Operation = "list_children", Scope = "project", FolderKind = "types",
            Cursor = "x", Offset = 1,
        };

        var validation = LibraryReadCatalog.ValidateRead(new[] { item });

        Assert.False(validation.IsValid);
        Assert.Contains("cursor is not valid with offset", validation.Error);
    }

    [Theory]
    [InlineData("inspect_library", "cursor is only valid for paged operations")]
    [InlineData("nope", "Unknown library read operation")]
    public void CursorOnNonPagedOrUnknownOperation_Rejected(string operation, string message)
    {
        var item = new LibraryReadItem { OperationId = "c", Operation = operation, Scope = "project", Cursor = "x" };

        Assert.Contains(message, LibraryReadCatalog.Check(item));
    }

    [Fact]
    public void ValidateRead_ReportsSelectorRulesAndDuplicateIds()
    {
        var validation = LibraryReadCatalog.ValidateRead(new[]
        {
            new LibraryReadItem { OperationId = "ok", Operation = "list_global_libraries" },
            new LibraryReadItem { OperationId = "ok", Operation = "list_global_libraries" },
            new LibraryReadItem { OperationId = "bad", Operation = "inspect_library", Scope = "project", Offset = 0 },
        });

        Assert.False(validation.IsValid);
        Assert.Contains("inspect_library accepts only library selectors", validation.Error);
    }

    [Fact]
    public void ValidateRead_AcceptsOneItemPerOperation()
    {
        var validation = LibraryReadCatalog.ValidateRead(new[]
        {
            new LibraryReadItem { OperationId = "a", Operation = "list_global_libraries", Limit = 5 },
            new LibraryReadItem { OperationId = "b", Operation = "inspect_library", Scope = "global", LibraryName = "F" },
            new LibraryReadItem { OperationId = "c", Operation = "list_children", Scope = "project", FolderKind = "types" },
            new LibraryReadItem { OperationId = "d", Operation = "list_versions", Scope = "project", TypeGuid = Guid1 },
            new LibraryReadItem { OperationId = "e", Operation = "list_dependencies", Scope = "project", TypeGuid = Guid1, VersionGuid = Guid2 },
        });

        Assert.True(validation.IsValid, validation.Error);
    }

    [Theory]
    [InlineData(McpAccessMode.ReadOnly)]
    [InlineData(McpAccessMode.ReadWrite)]
    [InlineData(McpAccessMode.Full)]
    public void ValidateAccessMode_EveryReadOperation_AllowedInEveryMode(McpAccessMode mode)
    {
        var items = new[]
        {
            new LibraryReadItem { Operation = "list_global_libraries" },
            new LibraryReadItem { Operation = "inspect_library", Scope = "global" },
            new LibraryReadItem { Operation = "list_children", Scope = "project" },
            new LibraryReadItem { Operation = "list_versions", Scope = "global" },
            new LibraryReadItem { Operation = "list_dependencies", Scope = "project" },
        };

        Assert.Empty(LibraryReadCatalog.ValidateAccessMode(items, mode));
    }
}
