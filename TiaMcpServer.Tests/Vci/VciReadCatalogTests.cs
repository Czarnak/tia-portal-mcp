using TiaMcpServer.Vci;
using TiaMcpServer.Contracts.Project;
using Xunit;

namespace TiaMcpServer.Tests.Vci;

public class VciReadCatalogTests
{
    private static VciOperationRequest Mapping() => new() { OperationId = "r", Operation = "list_mappings", WorkspaceName = "Fixture" };

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void InvalidLimitRejected(int limit)
    {
        var request = Mapping(); request.Limit = limit;
        Assert.False(VciReadCatalog.ValidateRead(new[] { request }).IsValid);
    }

    private static VciOperationRequest Workspaces(string id) => new() { OperationId = id, Operation = "list_workspaces" };

    [Theory]
    [InlineData("null batch", "Batch must contain at least one operation.")]
    [InlineData("empty batch", "Batch must contain at least one operation.")]
    [InlineData("51 items", "Batch exceeds the maximum of 50 operations (received 51).")]
    [InlineData("null item", "Batch contains a null operation.")]
    [InlineData("blank id", "Each operation requires a unique operationId.")]
    [InlineData("257-character id", "Each operationId must be at most 256 characters.")]
    [InlineData("duplicate id", "Duplicate operationId 'x'.")]
    public void EnvelopeMessages_UseSharedWording(string batchCase, string expected)
    {
        VciOperationRequest[]? batch = batchCase switch
        {
            "null batch" => null,
            "empty batch" => [],
            "51 items" => Enumerable.Range(0, 51).Select(i => Workspaces($"id{i}")).ToArray(),
            "null item" => [null!],
            "blank id" => [Workspaces(" ")],
            "257-character id" => [Workspaces(new string('i', 257))],
            "duplicate id" => [Workspaces("x"), Workspaces("x")],
            _ => throw new ArgumentOutOfRangeException(nameof(batchCase)),
        };

        Assert.Equal(expected, VciReadCatalog.ValidateRead(batch).Error);
    }

    [Fact]
    public void InvalidBatchAndScopeRejected()
    {
        Assert.False(VciReadCatalog.ValidateRead(null).IsValid);
        Assert.False(VciReadCatalog.ValidateRead(Enumerable.Range(0, 51).Select(i => new VciOperationRequest { OperationId = i.ToString(), Operation = "list_workspaces" }).ToArray()).IsValid);
        Assert.False(VciReadCatalog.ValidateRead(new[] { Mapping(), Mapping() }).IsValid);
        var request = Mapping(); request.Offset = -1;
        Assert.False(VciReadCatalog.ValidateRead(new[] { request }).IsValid);
        request.Offset = null; request.Scope = new();
        Assert.Contains("scope", VciReadCatalog.ValidateRead(new[] { request }).Error);
        request.Scope = new() { new() { NodeType = "Bogus", Name = "Fixture" } };
        Assert.Contains("scope", VciReadCatalog.ValidateRead(new[] { request }).Error);
    }

    [Theory]
    [InlineData("create_workspace")]
    [InlineData("delete_workspace")]
    [InlineData("export")]
    [InlineData("sync_to_workspace")]
    [InlineData("sync_to_project")]
    [InlineData("delete_mapping")]
    public void WriteOperationPointsToWriteTool(string operation)
        => Assert.Contains("use vci_write", VciReadCatalog.ValidateRead(new[] { new VciOperationRequest { OperationId = "r", Operation = operation } }).Error);

    [Fact]
    public void StrictFieldsAndRequiredLeaf()
    {
        var request = new VciOperationRequest { OperationId = "r", Operation = "list_workspaces", WorkspaceName = "Fixture" };
        Assert.False(VciReadCatalog.ValidateRead(new[] { request }).IsValid);
        request = Mapping(); request.Format = "SCL";
        Assert.False(VciReadCatalog.ValidateRead(new[] { request }).IsValid);
        request.Format = null; request.RootPath = "Fixture";
        Assert.False(VciReadCatalog.ValidateRead(new[] { request }).IsValid);
        request.RootPath = null; request.RelativeDirectory = "Fixture";
        Assert.False(VciReadCatalog.ValidateRead(new[] { request }).IsValid);
        request = new() { OperationId = "r", Operation = "supported_formats" };
        Assert.False(VciReadCatalog.ValidateRead(new[] { request }).IsValid);
        request.Scope = new() { new() { NodeType = ProjectTreeNodeTypes.Device, Name = "Fixture" } };
        Assert.False(VciReadCatalog.ValidateRead(new[] { request }).IsValid);
        request.Scope.Add(new() { NodeType = "HmiSoftware", Name = "Fixture" });
        request.Scope.Add(new() { NodeType = "ScriptModule", Name = "Fixture" });
        Assert.True(VciReadCatalog.ValidateRead(new[] { request }).IsValid);
        request.WithStatus = false;
        Assert.False(VciReadCatalog.ValidateRead(new[] { request }).IsValid);
    }

    [Fact]
    public void OptionalMappingScopeAndDefaults()
    {
        var request = Mapping();
        Assert.True(VciReadCatalog.ValidateRead(new[] { request }).IsValid);
        var query = VciReadCatalog.ToQuery(request);
        Assert.Null(query.Scope); Assert.False(query.WithStatus);
        request.OperationId = new string('x', 257);
        Assert.False(VciReadCatalog.ValidateRead(new[] { request }).IsValid);
    }

    [Fact]
    public void NullMembersReject()
    {
        Assert.False(VciReadCatalog.ValidateRead(new VciOperationRequest[] { null! }).IsValid);
        Assert.False(VciReadCatalog.ValidateRead(new[] { new VciOperationRequest { OperationId = "r", Operation = null! } }).IsValid);
        var request = new VciOperationRequest { OperationId = "r", Operation = "supported_formats", Scope = [null!] };
        Assert.False(VciReadCatalog.ValidateRead(new[] { request }).IsValid);
    }
}
