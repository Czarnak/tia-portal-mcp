using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Plc;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class PlcWriteBudgetTests
{
    private static readonly string Marker = "UNIQUE_EXCERPT_MARKER";

    private static PlcWriteEffect ContentEffect(string id)
    {
        var current = string.Join("\n", Enumerable.Range(0, 60).Select(i => $"{Marker} current {id} line {i} " + new string('a', 200)));
        var requested = string.Join("\n", Enumerable.Range(0, 60).Select(i => $"{Marker} requested {id} line {i} " + new string('b', 200)));
        return new PlcWriteEffect("update_block_logic",
            new PlcTargetIdentity("Block", "PLC_1", "Station_1", null, null, "Main", "PLC_1/Blocks/Main", null),
            Array.Empty<PlcFieldChange>(), null, null, PlcContentDiff.Build(current, requested), null);
    }

    private static PlcGuardedWriteResponse Preview(params PlcWriteEffect[] effects)
        => new("plc_write", "1.0", "preview", true, null, Array.Empty<string>(), Array.Empty<WriteGuardReport>(),
            effects.Select((e, i) => new PlcWriteEffectPresentation("op" + i, e, null)).ToArray(), null, null, null);

    [Fact]
    public void OversizedDiffEvidenceOmittedWhole()
    {
        // Item limit: the diff alone is dropped; the effect and its identity remain.
        var single = PlcWritePayloadBudget.Apply(Preview(ContentEffect("a")), maxItemChars: 4_000);
        var presented = Assert.Single(single.Effects);
        Assert.NotNull(presented.Effect);
        Assert.Null(presented.Effect!.ContentDiff);
        Assert.Equal("PLC_1/Blocks/Main", presented.Effect.Target.BlockPath);
        Assert.Equal(StructuredOperationBatchPayloadBudget.ItemLimitReason, presented.Omission!.Reason);
        Assert.Equal("plc_read", presented.Omission.RetryTool);
        Assert.False(single.Success);
        Assert.DoesNotContain(Marker, CanonicalJson.Serialize(single));

        // Document limit: whole diffs go first, largest first, until the document fits.
        var effects = Enumerable.Range(0, 4).Select(i => ContentEffect("d" + i)).ToArray();
        var original = CanonicalJson.Serialize(Preview(effects)).Length;
        var bounded = PlcWritePayloadBudget.Apply(Preview(effects), maxDocumentChars: original * 3 / 4);
        Assert.True(CanonicalJson.Serialize(bounded).Length <= original * 3 / 4);
        Assert.All(bounded.Effects, e => Assert.NotNull(e.Effect));
        Assert.Contains(bounded.Effects, e => e.Effect!.ContentDiff is null
            && e.Omission!.Reason == StructuredOperationBatchPayloadBudget.DocumentLimitReason);
        Assert.Contains(bounded.Effects, e => e.Effect!.ContentDiff is not null && e.Omission is null);
    }

    [Fact]
    public void FailureImportOutcomeOmittedWholeBeforeDocumentOverflow()
    {
        var outcome = new BlockImportOutcomeInfo
        {
            ImportStage = "completed", ImportResultState = "success", TargetMutationCommitted = true, CompileStage = "unavailable",
            FinalReadStage = "unavailable", TemporarySourceState = "not_applicable", ContentRelation = new string('r', 5_000),
        };
        var failed = new StructuredOperationItem("content", "update_block_logic", OperationBatchStatus.Failed, null,
            new StructuredOperationFailure(WorkerFailureCategories.WorkerOperationFailed, "Compile failed.", outcome), null, null, Array.Empty<string>());
        var response = new PlcGuardedWriteResponse("plc_write", "1.0", "applied", false, null, Array.Empty<string>(), Array.Empty<WriteGuardReport>(),
            Array.Empty<PlcWriteEffectPresentation>(), StructuredOperationBatch.FromItems(new[] { failed }), new(true, Array.Empty<PlcOperationVerification>(), null), null);

        var bounded = PlcWritePayloadBudget.Apply(response, maxDocumentChars: 3_000);

        var item = Assert.Single(bounded.Batch!.Operations);
        Assert.Equal("failed", item.Status);
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, item.Failure!.Category);
        Assert.Null(item.Failure.BlockImportOutcome);
        Assert.Equal(StructuredOperationBatchPayloadBudget.DocumentLimitReason, item.Omission!.Reason);
        Assert.True(CanonicalJson.Serialize(bounded).Length <= 3_000);
    }

    private static PlcGuardedWriteResponse WithDiagnostics(string phase, bool success, WriteToolError? error, params WriteGuardReport[] guards)
        => new("plc_write", "1.0", phase, success, error, Array.Empty<string>(), guards,
            Array.Empty<PlcWriteEffectPresentation>(), null, null, null);

    [Fact]
    public void MultiErrorValidationMessageDeliveredIntactWhenWithinBudget()
    {
        var message = string.Join("\n", Enumerable.Range(0, 4).Select(i => $"Operation 'op{i}': " + new string('e', 300)));
        Assert.True(message.Length > 512);

        var bounded = PlcWritePayloadBudget.Apply(WithDiagnostics("error", false, new(WorkerFailureCategories.ValidationError, message)));

        Assert.Equal(message, bounded.Error!.Message);
        Assert.Null(bounded.Omission);
    }

    [Fact]
    public void AppliedResponseWithLongGuardMessageKeepsSuccess()
    {
        var guard = new WriteGuardReport("plc_deletes_group_contents", "info", "op0", new string('g', 2_000), null);

        var bounded = PlcWritePayloadBudget.Apply(WithDiagnostics("applied", true, null, guard));

        Assert.True(bounded.Success);
        Assert.Null(bounded.Omission);
        Assert.Equal(guard.Message, Assert.Single(bounded.Guards).Message);
    }

    [Fact]
    public void OverBudgetDocumentStillCompactsDiagnostics()
    {
        var guard = new WriteGuardReport("plc_deletes_group_contents", "info", "op0", new string('g', 2_000), null);

        var bounded = PlcWritePayloadBudget.Apply(WithDiagnostics("applied", true, null, guard), maxDocumentChars: 1_000);

        Assert.DoesNotContain("ggggg", Assert.Single(bounded.Guards).Message);
        Assert.Equal("diagnosticDetailsOmitted", bounded.Omission!.Reason);
        Assert.False(bounded.Success);
    }

    [Fact]
    public async Task ProtectedCoreOverDocumentLimitRejectedBeforeWorker()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");
        var before = requests.Methods().Length;
        // '<' is escaped to six characters in every identity copy.
        var items = Enumerable.Range(0, 50)
            .Select(i => PlcGuardedWriteFixture.CreateTag(i.ToString("D3") + new string('<', 253), "Tag" + i)).ToArray();
        Assert.True(PlcWritePayloadBudget.MeasureProtectedCore(items) > StructuredOperationBatchPayloadBudget.MaxDocumentChars);

        var response = await fixture.RunAsync(false, items);

        Assert.Equal("error", response.Phase);
        Assert.Equal(WorkerFailureCategories.ValidationError, response.Error!.Category);
        Assert.Contains("smaller plc_write calls", response.Error.Message);
        Assert.Equal(before, requests.Methods().Length);
        Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
    }
}
