using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Vci;
using Xunit;

namespace TiaMcpServer.Tests.Vci;

public sealed class VciWriteCatalogTests : IDisposable
{
    [Theory]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(200000)]
    public void OperationNameDiagnosticLimit_PreservesBoundaryAndBoundsUnknownNames(int length)
    {
        var name = new string('n', length);
        var item = new VciOperationRequest { OperationId = "id", Operation = name };

        var validation = VciWriteCatalog.ValidateWrite([item]);

        Assert.False(validation.IsValid);
        Assert.Equal("validation_error", validation.Category);
        if (length <= 256)
            Assert.Contains(name, validation.Error);
        else
        {
            Assert.Equal("Each operation name must be at most 256 characters.", validation.Error);
            Assert.DoesNotContain(name, validation.Error);
        }
    }

    private readonly string directory = Path.Combine(Path.GetTempPath(), "vci-catalog-" + Guid.NewGuid().ToString("N"));
    public VciWriteCatalogTests() => Directory.CreateDirectory(directory);
    private VciOperationRequest Item(string operation = "export") => new()
    {
        OperationId = "a", Operation = operation, WorkspaceName = "Fixture",
        RootPath = operation == "create_workspace" ? Path.Combine(directory, "workspace") : null,
        Scope = operation is "create_workspace" or "delete_workspace" ? null : [new() { NodeType = "Device", Name = "Fixture" }],
    };

    [Theory]
    [InlineData("create_workspace")]
    [InlineData("delete_workspace")]
    [InlineData("export")]
    [InlineData("sync_to_workspace")]
    [InlineData("sync_to_project")]
    [InlineData("delete_mapping")]
    public void PerOperationFieldsAreStrict(string operation)
    {
        var item = Item(operation);
        Assert.True(VciWriteCatalog.ValidateWrite([item]).IsValid);
        item.WorkspaceName = " ";
        Assert.False(VciWriteCatalog.ValidateWrite([item]).IsValid);
        item.WorkspaceName = "Fixture";
        item.WithStatus = false;
        Assert.False(VciWriteCatalog.ValidateWrite([item]).IsValid);
        item.WithStatus = null; item.Offset = 0;
        Assert.False(VciWriteCatalog.ValidateWrite([item]).IsValid);
        item.Offset = null; item.Limit = 1;
        Assert.False(VciWriteCatalog.ValidateWrite([item]).IsValid);
        item.Limit = null;
        if (operation != "export")
        {
            item.Format = "SCL";
            Assert.False(VciWriteCatalog.ValidateWrite([item]).IsValid);
            item.Format = null; item.RelativeDirectory = "out";
            Assert.False(VciWriteCatalog.ValidateWrite([item]).IsValid);
            item.RelativeDirectory = null;
        }
        if (operation != "create_workspace")
        {
            item.RootPath = Path.Combine(directory, "workspace");
            Assert.False(VciWriteCatalog.ValidateWrite([item]).IsValid);
            item.RootPath = null;
        }
        if (operation is "create_workspace" or "delete_workspace")
        {
            item.Scope = [new() { NodeType = "Device", Name = "Fixture" }];
            Assert.False(VciWriteCatalog.ValidateWrite([item]).IsValid);
        }
        else
        {
            item.Scope = null;
            Assert.False(VciWriteCatalog.ValidateWrite([item]).IsValid);
        }
    }

    [Theory]
    [InlineData("sCl")]
    [InlineData("FutureFormat")]
    [InlineData(" SCL ")]
    [InlineData(null)]
    public void FormatIsPreservedForWorkerResolution(string? format)
    {
        var item = Item(); item.Format = format; item.RelativeDirectory = "out";
        Assert.True(VciWriteCatalog.ValidateWrite([item]).IsValid);
        var write = VciWriteCatalog.ToWorkerRequest(item, "request", "breadcrumb");
        Assert.Equal(format, write.Format);
        Assert.Equal(item.OperationId, write.OperationId); Assert.Equal(item.Operation, write.Operation);
        Assert.Equal(item.WorkspaceName, write.WorkspaceName); Assert.Equal(item.Scope, write.Scope);
        Assert.Equal("out", write.RelativeDirectory); Assert.Equal("request", write.RequestId);
        Assert.Equal("breadcrumb", write.BreadcrumbPath);
        item.Format = " "; Assert.False(VciWriteCatalog.ValidateWrite([item]).IsValid);
    }

    [Theory]
    [InlineData("../out")]
    [InlineData("out:stream")]
    [InlineData("C:\\out")]
    public void InvalidRelativeDirectoryRejected(string relative)
    {
        var item = Item(); item.RelativeDirectory = relative;
        Assert.False(VciWriteCatalog.ValidateWrite([item]).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("relative")]
    [InlineData("C:\\")]
    [InlineData("\\\\server\\share\\out")]
    [InlineData("C:\\out:stream")]
    public void InvalidRootRejected(string? root)
    {
        var item = Item("create_workspace"); item.RootPath = root;
        Assert.False(VciWriteCatalog.ValidateWrite([item]).IsValid);
    }

    [Theory]
    [InlineData("SoftwareUnit")]
    [InlineData("SystemBlockFolder")]
    public void UnsupportedScopePreservesTypedCategory(string nodeType)
    {
        var item = Item(); item.Scope!.Add(new() { NodeType = "PlcSoftware", Name = "Fixture" });
        item.Scope.Add(new() { NodeType = nodeType, Name = "Fixture" });
        Assert.Equal("target_kind_unsupported", VciWriteCatalog.ValidateWrite([item]).Category);
    }

    [Theory]
    [InlineData("list_workspaces")]
    [InlineData("list_mappings")]
    [InlineData("supported_formats")]
    public void ReadsPointToReadTool(string operation)
        => Assert.Contains("use vci_read", VciWriteCatalog.ValidateWrite([Item(operation)]).Error);

    private VciOperationRequest Export(string id)
    {
        var item = Item();
        item.OperationId = id;
        return item;
    }

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
            "51 items" => Enumerable.Range(0, 51).Select(i => Export($"id{i}")).ToArray(),
            "null item" => [null!],
            "blank id" => [Export(" ")],
            "257-character id" => [Export(new string('i', 257))],
            "duplicate id" => [Export("x"), Export("x")],
            _ => throw new ArgumentOutOfRangeException(nameof(batchCase)),
        };

        Assert.Equal(expected, VciWriteCatalog.ValidateWrite(batch).Error);
    }

    [Fact]
    public void OversizedOperationId_IsNeverEchoed()
    {
        var id = new string('o', 300);
        var item = Export(id);
        item.RootPath = Path.Combine(directory, "not-valid-for-export");

        var error = VciWriteCatalog.ValidateWrite([item]).Error;

        Assert.DoesNotContain(id, error);
        Assert.Equal("Each operationId must be at most 256 characters.", error);
    }

    [Fact]
    public void BatchBoundariesAndAccessMode()
    {
        Assert.False(VciWriteCatalog.ValidateWrite(null).IsValid);
        Assert.False(VciWriteCatalog.ValidateWrite([]).IsValid);
        Assert.False(VciWriteCatalog.ValidateWrite([null!]).IsValid);
        Assert.False(VciWriteCatalog.ValidateWrite([Item(), Item()]).IsValid);
        var item = Item(); item.OperationId = new string('x', 257);
        Assert.False(VciWriteCatalog.ValidateWrite([item]).IsValid);
        item = Item(); item.Scope = [];
        Assert.False(VciWriteCatalog.ValidateWrite([item]).IsValid);
        item.Scope = [null!]; Assert.False(VciWriteCatalog.ValidateWrite([item]).IsValid);
        item = Item(); item.Operation = null!; Assert.False(VciWriteCatalog.ValidateWrite([item]).IsValid);
        var items = Enumerable.Range(0, 50).Select(i => { var r = Item(); r.OperationId = i.ToString(); return r; }).ToList();
        Assert.True(VciWriteCatalog.ValidateWrite(items).IsValid);
        items.Add(Item()); Assert.False(VciWriteCatalog.ValidateWrite(items).IsValid);
        Assert.Single(VciWriteCatalog.ValidateAccessMode([Item()], McpAccessMode.ReadOnly));
        Assert.Empty(VciWriteCatalog.ValidateAccessMode([Item()], McpAccessMode.ReadWrite));
        Assert.Empty(VciWriteCatalog.ValidateAccessMode([Item()], McpAccessMode.Full));
    }
    public void Dispose() => Directory.Delete(directory, true);
}
