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
    public void NullMembersAndMultipleProjectsReject()
    {
        Assert.False(VciReadCatalog.ValidateRead(new VciOperationRequest[] { null! }).IsValid);
        Assert.False(VciReadCatalog.ValidateRead(new[] { new VciOperationRequest { OperationId = "r", Operation = null! } }).IsValid);
        var request = new VciOperationRequest { OperationId = "r", Operation = "supported_formats", Scope = [null!] };
        Assert.False(VciReadCatalog.ValidateRead(new[] { request }).IsValid);
        var first = Mapping(); first.ProjectPath = "A";
        var second = Mapping(); second.OperationId = "s"; second.ProjectPath = "B";
        Assert.False(VciReadCatalog.ValidateRead(new[] { first, second }).IsValid);
    }
}
