using System.Text.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Json;
using TiaMcpServer.Library;
using Xunit;

namespace TiaMcpServer.Tests.Library;

public sealed class LibraryWriteCatalogTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(50, true)]
    [InlineData(51, false)]
    public void CountBounds(int count, bool valid)
        => Assert.Equal(valid, LibraryWriteCatalog.ValidateWrite(Enumerable.Range(0, count).Select(i => Item(i.ToString())).ToArray()).IsValid);

    [Fact]
    public void IdentifiersAreRequiredUniqueAndBounded()
    {
        Assert.False(LibraryWriteCatalog.ValidateWrite(null).IsValid);
        Assert.False(LibraryWriteCatalog.ValidateWrite([null!]).IsValid);
        Assert.False(LibraryWriteCatalog.ValidateWrite([Item(" ")]).IsValid);
        Assert.False(LibraryWriteCatalog.ValidateWrite([Item("a"), Item("a")]).IsValid);
        Assert.True(LibraryWriteCatalog.ValidateWrite([Item("a"), Item("A")]).IsValid);
        Assert.True(LibraryWriteCatalog.ValidateWrite([Item(new string('x', 256))]).IsValid);
        Assert.False(LibraryWriteCatalog.ValidateWrite([Item(new string('x', 257))]).IsValid);
    }

    [Theory]
    [MemberData(nameof(LibraryWriteMethodsTests.ValidRequests), MemberType = typeof(LibraryWriteMethodsTests))]
    public void AllOperationsUseSharedClosedRulesAndAccessPolicy(string operation, string json, string allowed)
    {
        var item = CanonicalJson.Deserialize<LibraryWriteItem>(json);
        item.Operation = operation;
        item.OperationId = "a";
        Assert.True(LibraryWriteCatalog.ValidateWrite([item]).IsValid);
        Assert.Single(LibraryWriteCatalog.ValidateAccessMode([item], McpAccessMode.ReadOnly));
        Assert.Empty(LibraryWriteCatalog.ValidateAccessMode([item], McpAccessMode.ReadWrite));
        Assert.Empty(LibraryWriteCatalog.ValidateAccessMode([item], McpAccessMode.Full));
        var permitted = allowed.Split(' ').Concat(["Operation", "OperationId"]).ToHashSet();
        foreach (var member in typeof(LibraryWriteItem).GetProperties().Where(p => !permitted.Contains(p.Name)))
        {
            var changed = CanonicalJson.Deserialize<LibraryWriteItem>(CanonicalJson.Serialize(item));
            member.SetValue(changed, member.PropertyType == typeof(string) ? "unexpected" : new List<string> { "unexpected" });
            Assert.False(LibraryWriteCatalog.ValidateWrite([changed]).IsValid);
        }
    }

    [Fact]
    public void MappingPreservesEveryFlatMember()
    {
        var item = new LibraryWriteItem();
        foreach (var property in typeof(LibraryWriteItem).GetProperties())
            property.SetValue(item, property.PropertyType == typeof(string) ? property.Name : new List<string> { property.Name });
        var request = LibraryWriteCatalog.ToWorkerRequest(item);
        foreach (var property in typeof(LibraryWriteRequestInfo).GetProperties())
            Assert.Equal(typeof(LibraryWriteItem).GetProperty(property.Name)!.GetValue(item), property.GetValue(request));
    }

    [Theory]
    [InlineData("dependsOn")]
    [InlineData("confirm")]
    [InlineData("unitName")]
    public void UnknownPublicMembersAreRejected(string member)
        => Assert.Throws<JsonException>(() => CanonicalJson.Deserialize<LibraryWriteItem>($"{{\"{member}\":\"unsupported\"}}"));

    private static LibraryWriteItem Item(string id) => new()
        { OperationId = id, Operation = "delete_master_copy", Scope = "project", Name = "Copy" };
}
