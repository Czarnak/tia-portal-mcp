using System.Text.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Json;
using TiaMcpServer.Library;
using TiaMcpServer.OperationBatches;
using Xunit;

namespace TiaMcpServer.Tests.Library;

[Collection(TiaMcpServer.Tests.OperationBatches.ConfiguredPayloadBudgetCollection.Name)]
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
            member.SetValue(changed, member.PropertyType == typeof(string) ? "unexpected" : member.PropertyType == typeof(bool?) ? true : new List<string> { "unexpected" });
            Assert.False(LibraryWriteCatalog.ValidateWrite([changed]).IsValid);
        }
    }

    [Fact]
    public void MappingPreservesEveryFlatMember()
    {
        var item = new LibraryWriteItem();
        foreach (var property in typeof(LibraryWriteItem).GetProperties())
            property.SetValue(item, property.PropertyType == typeof(string) ? property.Name : property.PropertyType == typeof(bool?) ? true : new List<string> { property.Name });
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

    [Theory]
    [MemberData(nameof(LibraryWriteMethodsTests.Authors), MemberType = typeof(LibraryWriteMethodsTests))]
    public void AuthorAdmission_UsesSharedRules(string operation, string? author, bool valid)
    {
        var item = CanonicalJson.Deserialize<LibraryWriteItem>(CanonicalJson.Serialize(LibraryWriteMethodsTests.AuthorRequest(operation, author)));
        item.OperationId = "a";
        Assert.Equal(valid, LibraryWriteCatalog.ValidateWrite([item]).IsValid);
        Assert.Equal(author, LibraryWriteCatalog.ToWorkerRequest(item).Author);
    }

    [Fact]
    public void AuthorAdmission_UsesEffectiveItemBudgetInAdditionToCap()
    {
        var oldItem = StructuredOperationBatchPayloadBudget.MaxItemChars;
        var oldDocument = StructuredOperationBatchPayloadBudget.MaxDocumentChars;
        try
        {
            StructuredOperationBatchPayloadBudget.Configure(10, oldDocument);
            var item = new LibraryWriteItem { OperationId = "a", Operation = "create_global", Directory = @"C:\Output", Name = "New", Author = new string('A', 10) };
            Assert.True(LibraryWriteCatalog.ValidateWrite([item]).IsValid);
            item.Author += "A";
            Assert.False(LibraryWriteCatalog.ValidateWrite([item]).IsValid);
            StructuredOperationBatchPayloadBudget.Configure(100_000, oldDocument);
            item.Author = new string('A', 256);
            Assert.False(LibraryWriteCatalog.ValidateWrite([item]).IsValid);
        }
        finally { StructuredOperationBatchPayloadBudget.Configure(oldItem, oldDocument); }
    }

    [Fact]
    public void CreationMetadataFailure_IdentityRoundtripsIntoFailedItem()
    {
        var identity = new LibraryIdentityInfo { Scope = "global", Kind = "library", Name = "New", LibraryName = "New", LibraryPath = @"C:\Output\New.al21" };
        var outcome = new LibraryWriteOutcomeInfo { Library = identity };
        var envelope = JsonSerializer.SerializeToElement(new TiaMcpServer.Contracts.Worker.WorkerResponse
        {
            Success = false, FailureCategory = "worker_operation_failed", Error = "The library exists; inspect before retrying.", LibraryWriteOutcome = outcome
        }, TiaMcpServer.Contracts.Json.WorkerJson.Envelope);
        LibraryWritePayloadContract.ValidateEnvelopeOutcome(envelope);
        var decoded = CanonicalJson.DeserializeWorkerPayload<LibraryWriteOutcomeInfo>(envelope.GetProperty("libraryWriteOutcome").GetRawText());
        var item = LibraryWritePayloadContract.Project(new() { OperationId = "a", Operation = "create_global" },
            TiaMcpServer.Worker.WorkerCallResult.Fail("worker_operation_failed", "The library exists; inspect before retrying.") with { LibraryWriteOutcome = decoded });
        Assert.Equal("failed", item.Status);
        Assert.Equal(identity.LibraryPath, item.Result!.Value.GetProperty("library").GetProperty("libraryPath").GetString());
        Assert.Equal("worker_operation_failed", item.Failure!.Category);
    }

    [Fact]
    public void ProjectLibraryIdentity_EnvelopeIncludesExplicitNullMembers()
    {
        var identity = JsonSerializer.SerializeToElement(new LibraryIdentityInfo { Scope = "project", Kind = "library" },
            TiaMcpServer.Contracts.Json.WorkerJson.Envelope);
        foreach (var member in new[] { "libraryPath", "libraryName", "folderKind", "typeGuid", "versionGuid" })
            Assert.Equal(JsonValueKind.Null, identity.GetProperty(member).ValueKind);
        Assert.Equal("project", CanonicalJson.DeserializeWorkerPayload<LibraryIdentityInfo>(identity.GetRawText()).Scope);
    }

    private static LibraryWriteItem Item(string id) => new()
        { OperationId = id, Operation = "delete_master_copy", Scope = "project", Name = "Copy" };
}
