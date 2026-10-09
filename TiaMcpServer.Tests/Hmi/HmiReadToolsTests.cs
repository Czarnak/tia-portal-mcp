using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Hmi;
using TiaMcpServer.Safety;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Hmi;

public class HmiReadToolsTests
{
    [Fact]
    public async Task ValidationFailureIsRootErrorWithIsErrorTrue()
    {
        using var client = new OpennessWorkerClient(
            new ProjectSessionBinding(null),
            logger: null,
            workerExecutablePath: "never-started.exe",
            accessPolicy: new OperationAccessPolicy(McpAccessMode.ReadOnly));

        var result = await HmiReadTools.HmiRead(client, new[]
        {
            new HmiOperationRequest { OperationId = "a", Operation = "get_tag" },
        });

        Assert.True(result.IsError);
        var root = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("hmi_read", root.GetProperty("tool").GetString());
        Assert.Equal("1.0", root.GetProperty("contractVersion").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(WorkerFailureCategories.ValidationError, root.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("batch").ValueKind);
    }

    [Fact]
    public void UnmappedPayloadIsItemProtocolErrorWithoutEcho()
    {
        var request = new HmiOperationRequest { OperationId = "a", Operation = "list_hmi_devices" };
        var diagnostics = new List<string>();

        var item = HmiPayloadContract.Project(
            request,
            WorkerCallResult.Ok("{\"secret\":\"SENTINEL-PAYLOAD\"}"),
            diagnostics.Add);

        Assert.Equal(TiaMcpServer.OperationBatches.OperationBatchStatus.Failed, item.Status);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
        Assert.DoesNotContain("SENTINEL-PAYLOAD", item.Failure.Message);
        Assert.DoesNotContain(diagnostics, line => line.Contains("SENTINEL-PAYLOAD"));
        Assert.Single(diagnostics);
    }

    [Fact]
    public void WorkerFailureIsProjectedWithItsCategory()
    {
        var request = new HmiOperationRequest { OperationId = "a", Operation = "list_hmi_devices" };

        var item = HmiPayloadContract.Project(
            request, WorkerCallResult.Fail(WorkerFailureCategories.TargetNotFound, "no such HMI"));

        Assert.Equal(WorkerFailureCategories.TargetNotFound, item.Failure!.Category);
        Assert.Equal("no such HMI", item.Failure.Message);
    }

    [Fact]
    public void QueryCarriesResolvedDefaultLimitAndOffset()
    {
        var query = HmiOperationCatalog.ToQuery(
            new HmiOperationRequest { OperationId = "a", Operation = "list_tags", HmiName = "HMI_1", TableName = "T" });

        Assert.Equal(0, query.Offset);
        Assert.Equal(HmiOperationCatalog.DefaultLimit, query.Limit);
        Assert.Equal(100, HmiOperationCatalog.DefaultLimit);
        Assert.Equal("HMI_1", query.HmiName);
        Assert.Equal("T", query.TableName);
    }

    [Fact]
    public void QueryKeepsExplicitPagingAndLeavesUnpagedOperationsWithoutPaging()
    {
        var paged = HmiOperationCatalog.ToQuery(
            new HmiOperationRequest { OperationId = "a", Operation = "list_tags", Offset = 7, Limit = 3 });
        var unpaged = HmiOperationCatalog.ToQuery(
            new HmiOperationRequest { OperationId = "b", Operation = "list_connections" });

        Assert.Equal(7, paged.Offset);
        Assert.Equal(3, paged.Limit);
        Assert.Null(unpaged.Offset);
        Assert.Null(unpaged.Limit);
    }

    [Fact]
    public void ApplyBudgetOmitsAnOversizedItemWithPagedRetryGuidance()
    {
        var item = new TiaMcpServer.OperationBatches.StructuredOperationItem(
            "a", "list_tags", TiaMcpServer.OperationBatches.OperationBatchStatus.Succeeded,
            JsonSerializer.SerializeToElement(new string('x', 500)),
            Failure: null, Omission: null, SkipReason: null, Array.Empty<string>());
        var batch = TiaMcpServer.OperationBatches.StructuredOperationBatch.FromItems(new[] { item });

        var budgeted = HmiReadTools.ApplyBudget(batch, maxItemChars: 100, maxDocumentChars: 10_000);

        var omission = budgeted.Operations[0].Omission;
        Assert.NotNull(omission);
        Assert.Contains("Lower limit or add a narrowing field", omission!.Guidance);
    }
}
