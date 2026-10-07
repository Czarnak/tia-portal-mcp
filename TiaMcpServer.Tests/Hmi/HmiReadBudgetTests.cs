using TiaMcpServer.Contracts;
using TiaMcpServer.Hmi;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Hmi;

public class HmiReadBudgetTests
{
    private static StructuredOperationItem TagPage(string id, int nameChars)
        => HmiPayloadContract.Project(
            new HmiOperationRequest { OperationId = id, Operation = "list_tags" },
            WorkerCallResult.Ok(WorkerJson.SerializePayload(new HmiTagListInfo
            {
                Tags = { new HmiTagRowInfo { Name = new string('t', nameChars) } },
            })));

    [Fact]
    public void OversizedItemOmittedWithLimitGuidance()
    {
        var batch = StructuredOperationBatch.FromItems(new[] { TagPage("big", 70_000), TagPage("small", 10) });

        var bounded = HmiReadTools.ApplyBudget(batch);

        var big = bounded.Operations[0];
        Assert.Equal(OperationBatchStatus.Omitted, big.Status);
        Assert.Null(big.Result);
        Assert.Contains("Lower limit", big.Omission!.Guidance);
        Assert.Equal(OperationBatchStatus.Succeeded, bounded.Operations[1].Status);
    }

    [Fact]
    public void DocumentOverBudgetOmitsLargestItemsFirst()
    {
        var batch = StructuredOperationBatch.FromItems(new[]
        {
            TagPage("a", 40_000), TagPage("b", 55_000), TagPage("c", 50_000), TagPage("d", 45_000),
        });

        var bounded = HmiReadTools.ApplyBudget(batch);

        Assert.Equal(OperationBatchStatus.Omitted, bounded.Operations[1].Status);
        Assert.Equal(OperationBatchStatus.Succeeded, bounded.Operations[0].Status);
        Assert.Equal(OperationBatchStatus.Succeeded, bounded.Operations[2].Status);
        Assert.Equal(OperationBatchStatus.Succeeded, bounded.Operations[3].Status);
    }

    [Fact]
    public void RetryToolIsHmiRead()
    {
        var bounded = HmiReadTools.ApplyBudget(StructuredOperationBatch.FromItems(new[] { TagPage("big", 70_000) }));

        var omission = bounded.Operations[0].Omission!;
        Assert.Equal("hmi_read", omission.RetryTool);
        Assert.Contains("hmi_read", omission.Guidance);
    }

    [Fact]
    public void UnpagedOperationGuidanceSaysSplitTheBatch()
    {
        var item = HmiPayloadContract.Project(
            new HmiOperationRequest { OperationId = "dev", Operation = "list_hmi_devices" },
            WorkerCallResult.Ok(WorkerJson.SerializePayload(new HmiDeviceListInfo
            {
                Devices = { new HmiDeviceInfo { DeviceName = new string('d', 70_000), SoftwareName = "S", Kind = "unified" } },
            })));

        var guidance = HmiReadTools.ApplyBudget(StructuredOperationBatch.FromItems(new[] { item })).Operations[0].Omission!.Guidance;

        Assert.StartsWith("Split the batch", guidance);
    }
}
