using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Plc;
using TiaMcpServer.Safety.Pipeline;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

/// <summary>The verifier against a bound plc-write-roundtrip FakeWorker; batches are built by hand.</summary>
[Collection(RealWorkerProcessCollection.Name)]
public sealed class PlcWriteVerifierTests
{
    private const string Scenario = "plc-write-roundtrip";

    private static StructuredOperationBatch Succeeded(params PlcOperationRequest[] items)
        => StructuredOperationBatch.FromItems(items.Select(i => new StructuredOperationItem(i.OperationId, i.Operation,
            OperationBatchStatus.Succeeded, null, null, null, null, Array.Empty<string>())).ToArray());

    /// <summary>Plans the items first (the effects carry the resolved PLC), then optionally mutates them.</summary>
    private static async Task<PlcWriteVerification> VerifyAsync(PlcGuardedWriteFixture fixture, bool mutate, params PlcOperationRequest[] items)
    {
        var plan = await new PlcWritePlanner(fixture.Client).PlanAsync(Scenario, items);
        Assert.True(plan.Plan.Success, plan.Plan.Error?.Message);
        if (mutate)
            foreach (var item in items)
                Assert.True((await PlcWorkerInvoker.InvokeWriteAsync(fixture.Client, item)).Success);
        var effects = items.Select((item, i) => (item.OperationId, Effect: plan.Plan.Items[i].Effect!))
            .ToDictionary(p => p.OperationId, p => p.Effect);
        return await new PlcWriteVerifier(fixture.Client, items, effects).VerifyAsync(Scenario, Succeeded(items));
    }

    [Fact]
    public async Task ContentVerificationReportsNewHash()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Scenario);
        var item = PlcGuardedWriteFixture.UpdateBlock("content", "PLC_2/Main", await fixture.ReadHashAsync("PLC_2/Main", "xml"));

        var verification = await VerifyAsync(fixture, mutate: true, item);

        Assert.True(verification.Success);
        var check = Assert.Single(verification.Operations);
        Assert.Equal("contentHash", check.Check);
        Assert.Equal(ContentHashRules.Compute("xml", item.Content!), check.ContentHash);
        Assert.NotEqual(item.ExpectedContentHash, check.ContentHash);
    }

    [Fact]
    public async Task MissingCreatedTagFailsVerification()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Scenario);

        // The batch claims success, but the tag was never created.
        var verification = await VerifyAsync(fixture, mutate: false, PlcGuardedWriteFixture.CreateTag("ghost", "Ghost"));

        Assert.False(verification.Success);
        var check = Assert.Single(verification.Operations);
        Assert.False(check.Success);
        Assert.Equal("values", check.Check);
        Assert.Equal("absent", check.Actual);
        Assert.Contains("Ghost", check.Message);
    }

    [Fact]
    public async Task CreatedTableAndDeletedTagVerify()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Scenario);
        var delete = new PlcOperationRequest { OperationId = "delete", Operation = "delete_tag", PlcName = "PLC_2", TableName = "Default tag table", Name = "Start" };

        var verification = await VerifyAsync(fixture, mutate: true, delete, PlcGuardedWriteFixture.CreateTable("table"));

        Assert.True(verification.Success, string.Join("; ", verification.Operations.Select(o => o.Message)));
        Assert.Equal(new[] { "absent", "exists" }, verification.Operations.Select(o => o.Check));
    }
}
