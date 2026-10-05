using TiaMcpServer.OperationBatches;
using TiaMcpServer.Plc;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

public class PlcReadBudgetTests
{
    private static StructuredOperationItem Read(string id, string content)
        => PlcPayloadContract.Project(
            new PlcOperationRequest { OperationId = id, Operation = "get_block_content", BlockPath = "PLC_1/" + id },
            WorkerCallResult.Ok(content));

    [Fact]
    public void OversizedContentIsOmittedWhole()
    {
        var batch = StructuredOperationBatch.FromItems(new[] { Read("big", new string('x', 70_000)), Read("small", "<a/>") });

        var bounded = PlcReadTools.ApplyBudget(batch);

        var big = bounded.Operations[0];
        Assert.Equal(OperationBatchStatus.Omitted, big.Status);
        Assert.Null(big.Result);
        Assert.Equal("plc_read", big.Omission!.RetryTool);
        Assert.Contains("plc_read", big.Omission.Guidance);
        Assert.Equal(OperationBatchStatus.Succeeded, bounded.Operations[1].Status);
        Assert.NotNull(bounded.Operations[1].Result);
    }
}
