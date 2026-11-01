using System.Text.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Vci;
using Xunit;

namespace TiaMcpServer.Tests.Vci;

public class VciVerificationPolicyTests
{
    [Fact]
    public async Task DistinctWorkspaces_AreTrusted()
    {
        var items = new[] { Item("a", "W1"), Item("b", "W2") };
        var v = await Verify(items, Ok(items[0]), Ok(items[1]));
        Assert.All(v.Operations, o => Assert.Equal(WriteVerificationChecks.Trusted, o.Check));
        Assert.True(v.Success);
    }

    [Fact]
    public async Task LaterItemSameWorkspace_Supersedes()
    {
        var items = new[] { Item("a", "W"), Item("b", "W") };
        var v = await Verify(items, Ok(items[0]), Ok(items[1]));
        Assert.Equal([WriteVerificationChecks.Superseded, WriteVerificationChecks.Trusted], v.Operations.Select(o => o.Check));
    }

    [Fact]
    public void CaseVariantWorkspace_KeysMatch()
        => Assert.Equal(new VciVerificationPolicy().ObjectKeys(Item("a", "work"), false),
            new VciVerificationPolicy().ObjectKeys(Item("b", "WORK"), true));

    [Fact]
    public async Task FailedItemWithOutcome_SupersedesEarlier()
    {
        var items = new[] { Item("a", "W"), Item("b", "w") };
        var failed = new StructuredOperationItem("b", "export", OperationBatchStatus.Failed, JsonSerializer.SerializeToElement(new { failed = 1 }),
            new("engineering_rejected", "rejected"), null, null, []);
        var v = await Verify(items, Ok(items[0]), failed);
        var entry = Assert.Single(v.Operations);
        Assert.Equal(("a", WriteVerificationChecks.Superseded), (entry.OperationId, entry.Check));
    }

    [Fact]
    public async Task FailedItems_HaveNoEntry()
    {
        var items = new[] { Item("a", "W1"), Item("b", "W2") };
        var v = await Verify(items, Ok(items[0]), StructuredOperationItem.Failed(items[1], "worker_operation_failed", "x", []));
        var entry = Assert.Single(v.Operations);
        Assert.Equal("a", entry.OperationId);
    }

    [Fact]
    public async Task PreconditionRefusal_DoesNotSupersede()
    {
        var items = new[] { Item("a", "W"), Item("b", "W", "create_workspace") };
        var v = await Verify(items, Ok(items[0]), StructuredOperationItem.Failed(items[1], "state_changed", "late check", []));
        var entry = Assert.Single(v.Operations);
        Assert.Equal(("a", WriteVerificationChecks.Trusted), (entry.OperationId, entry.Check));
    }

    private static Task<WriteVerification> Verify(VciOperationRequest[] items, params StructuredOperationItem[] outcomes)
        => new TrustedWriteVerifier<VciOperationRequest>(items, new VciVerificationPolicy()).VerifyAsync(StructuredOperationBatch.FromItems(outcomes));

    private static StructuredOperationItem Ok(VciOperationRequest i) => new(i.OperationId, i.Operation, OperationBatchStatus.Succeeded, null, null, null, null, []);
    private static VciOperationRequest Item(string id, string workspace, string op = "export") => new() { OperationId = id, Operation = op, WorkspaceName = workspace };
}
