using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Vci;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Vci;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Vci;

public class VciWriteBudgetTests
{
    [Fact]
    public void OversizedProtectedCoreRejectedBeforeBinding()
    {
        using var client = new OpennessWorkerClient(new(null));
        var items = Enumerable.Range(0, 50).Select(i => new VciOperationRequest { OperationId = new string('\u0001', 253) + i.ToString("D3"), Operation = "delete_workspace", WorkspaceName = "Fixture" }).ToArray();
        var validation = new VciWriteDomain(client, new()).Validate(items, McpAccessMode.Full);
        Assert.False(validation.IsValid);
    }

    [Fact]
    public void OmissionsPreserveTrustedStatusesAndVerificationTruth()
    {
        var batch = StructuredOperationBatch.FromItems([new("a", "export", OperationBatchStatus.Succeeded,
            CanonicalJson.ToElement(new VciWriteOutcomeInfo { Notes = [new string('x', 10000)] }), null, null, null, [])]);
        var response = new VciGuardedWriteResponse("vci_write", "1.0", WritePhases.Applied, true, null, [], [],
            [new("a", new("export", "Fixture", null, new string('x', 10000), 0, 0, 0), null)], batch,
            new(true, [new("a", true, "exists", null, null, null)], null), null);
        var bounded = VciWritePayloadBudget.Apply(response, 1000, 5000);
        Assert.True(CanonicalJson.Serialize(bounded).Length <= 5000);
        Assert.False(bounded.Success); Assert.True(bounded.Verification!.Success);
        Assert.Equal("succeeded", bounded.Batch!.Operations[0].Status);
        Assert.Null(bounded.Batch.Operations[0].Result);
        Assert.Equal("vci_read", bounded.Batch.Operations[0].Omission!.RetryTool);
    }
}
