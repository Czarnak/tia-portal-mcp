using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Hmi;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using Xunit;

namespace TiaMcpServer.Tests.Hmi;

public class HmiWriteBudgetTests
{
    [Theory]
    [InlineData('x', false)]
    [InlineData('\u0001', true)]
    public void AdmissionMeasuresEncodedIdentities(char character, bool exceeds)
    {
        var items = Enumerable.Range(0, 50).Select(i => HmiGuardedWriteDomainTests.Item(new string(character, 253) + i.ToString("D3"))).ToArray();
        Assert.Equal(exceeds, HmiWritePayloadBudget.MeasureProtectedCore(items) > 180000);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("guard")]
    [InlineData("failure")]
    [InlineData("cleanup")]
    public void OversizedDiagnosticIsCompactedEvenWhenDocumentFits(string location)
    {
        var large = new string('x', 61000);
        var response = Response(1);
        response = location switch
        {
            "root" => response with { Error = new("worker_operation_failed", large) },
            "guard" => response with { Guards = [new("hmi_state_unverifiable", "block", "0", large, null)] },
            "failure" => response with { Batch = StructuredOperationBatch.FromItems([response.Batch!.Operations[0] with { Failure = new("worker_operation_failed", large) }]) },
            _ => response with { Batch = StructuredOperationBatch.FromItems([response.Batch!.Operations[0] with { Result = CanonicalJson.ToElement(new HmiWriteOutcomeInfo { ObjectName = "T", Cleanup = "failed", CleanupMessage = large }) }]) },
        };
        Assert.True(CanonicalJson.Serialize(response).Length < 180000);
        var result = HmiWritePayloadBudget.Apply(response);
        Assert.NotNull(result.Omission);
        Assert.DoesNotContain(large, CanonicalJson.Serialize(result));
        Assert.Equal("failed", result.Batch!.Operations[0].Status);
    }

    [Fact]
    public void FailedOversizedOutcomeAndAggregateEffectsKeepExecutionTruth()
    {
        var response = Response(8);
        var result = HmiWritePayloadBudget.Apply(response);
        Assert.True(CanonicalJson.Serialize(result).Length <= 180000);
        Assert.False(result.Success);
        Assert.Equal(response.Batch!.Counts, result.Batch!.Counts);
        Assert.Equal(response.Batch.Operations.Select(i => i.Status), result.Batch.Operations.Select(i => i.Status));
        Assert.Equal(response.Verification!.Success, result.Verification!.Success);
        Assert.Contains(result.Effects, e => e.Omission is not null);
        Assert.Null(result.Batch.Operations[0].Result);
        Assert.NotNull(result.Batch.Operations[0].Omission);
    }

    [Fact]
    public void OrdinaryDiagnosticsAndOutcomeRemainIntact()
    {
        var response = Response(1) with { Error = new("worker_operation_failed", "ordinary diagnostic") };
        Assert.Equal(CanonicalJson.Serialize(response), CanonicalJson.Serialize(HmiWritePayloadBudget.Apply(response)));
    }

    [Fact]
    public void MaximalLateProtectedShapeFitsReservedCoreAndKeepsEveryIdentity()
    {
        var requests = Enumerable.Range(0, 50).Select(i => HmiGuardedWriteDomainTests.Item(new string('x', 253) + i.ToString("D3"))).ToArray();
        var omission = new StructuredOperationOmission(StructuredOperationBatchPayloadBudget.DocumentLimitReason, 180000, 999999, "hmi_read", "Inspect current state with hmi_read; use smaller hmi_write calls to see every effect. Do not replay the write.");
        var guards = requests.SelectMany(i => new[]
        {
            new WriteGuardReport("hmi_deletes_table_contents", "info", i.OperationId, "Initial table diagnostic", null),
            new WriteGuardReport("hmi_deletes_referenced_tag", "info", i.OperationId, "Initial reference diagnostic", null),
            new WriteGuardReport("hmi_deletes_table_contents", "info", i.OperationId, "Changed table diagnostic", null),
            new WriteGuardReport("hmi_deletes_referenced_tag", "info", i.OperationId, "Changed reference diagnostic", null),
        }).Concat(new[]
        {
            new WriteGuardReport("hmi_state_unverifiable", "block", requests[^1].OperationId, "Incomplete evidence", null),
            new WriteGuardReport("hmi_state_unverifiable", "block", requests[^1].OperationId, "Uncertain references", null),
            new WriteGuardReport("partial_write", "info", requests[^1].OperationId, "Earlier writes remain", null),
        }).ToArray();
        var items = requests.Select((r, i) => new StructuredOperationItem(r.OperationId, r.Operation, i == 49 ? "failed" : "succeeded", null,
            i == 49 ? new("guard_blocked", "Late guard blocked") : null, omission, null, [])).ToArray();
        var response = new HmiGuardedWriteResponse("hmi_write", "1.0", "applied", false, null, [], guards,
            requests.Select(r => new HmiWriteEffectPresentation(r.OperationId, null, omission)).ToArray(),
            StructuredOperationBatch.FromItems(items, new(true, 999999, 180000, 50, 1, requests.Select(r => r.OperationId).ToArray())),
            new(false, [], omission), omission);
        Assert.Equal(203, response.Guards.Count);
        Assert.True(CanonicalJson.Serialize(response).Length <= HmiWritePayloadBudget.MeasureProtectedCore(requests));
        var bounded = HmiWritePayloadBudget.Apply(response);
        Assert.True(CanonicalJson.Serialize(bounded).Length <= 180000);
        Assert.Equal(items.Select(i => i.OperationId), bounded.Batch!.Operations.Select(i => i.OperationId));
        Assert.Equal(response.Batch!.Counts, bounded.Batch.Counts);
    }

    internal static HmiGuardedWriteResponse Response(int count)
    {
        var items = Enumerable.Range(0, count).Select(i => new StructuredOperationItem(i.ToString(), "create_tag", i == 0 ? "failed" : "succeeded",
            CanonicalJson.ToElement(new HmiWriteOutcomeInfo { ObjectName = "T", AppliedProperties = i == 0 && count > 1 ? [new string('x', 61000)] : ["Name"], Cleanup = "failed" }),
            i == 0 ? new("worker_operation_failed", "failed setter") : null, null, null, Array.Empty<string>())).ToArray();
        return new("hmi_write", "1.0", "applied", false, null, [], [],
            items.Select(i => new HmiWriteEffectPresentation(i.OperationId, new("create_tag", new("Tag", "H", "T", null, "Table", null), [new("comment[en-US]", null, new string('x', count > 1 ? 45000 : 1), false)], null, null), null)).ToArray(),
            StructuredOperationBatch.FromItems(items), new(true, [], null), null);
    }
}
