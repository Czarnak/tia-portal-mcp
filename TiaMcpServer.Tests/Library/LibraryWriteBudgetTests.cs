using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Json;
using TiaMcpServer.Library;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using Xunit;

namespace TiaMcpServer.Tests.Library;

public sealed class LibraryWriteBudgetTests
{
    [Fact]
    public void CreateMetadata_OmissionPreservesExecutedAndVerified_RaisedLimitPreservesComment()
    {
        var comment = new string('c', StructuredOperationBatchPayloadBudget.MaxItemChars + 1000);
        var item = LibraryWritePayloadContract.Project(new() { OperationId = "a", Operation = "create_global" },
            TiaMcpServer.Worker.WorkerCallResult.Ok(TiaMcpServer.Contracts.Json.WorkerJson.SerializePayload(
                new LibraryWriteResultInfo { Metadata = new() { Comments = [new() { Text = comment }] } })));
        Assert.Equal("succeeded", item.Status);
        var response = Response([]) with { Phase = "applied", Batch = StructuredOperationBatch.FromItems([item]),
            Verification = new(true, [new("a", true, "trusted", null, null, null)], null) };
        var bounded = LibraryWritePayloadBudget.Apply(response);
        Assert.False(bounded.Success);
        Assert.True(bounded.Verification!.Success);
        Assert.True(Assert.Single(bounded.Verification.Operations).Success);
        Assert.Equal("trusted", bounded.Verification.Operations[0].Check);
        Assert.Equal(1, bounded.Batch!.Counts.Succeeded);
        Assert.Equal("succeeded", bounded.Batch.Operations[0].Status);
        Assert.Null(bounded.Batch.Operations[0].Result);
        Assert.NotNull(bounded.Batch.Operations[0].Omission);
        Assert.DoesNotContain(comment[..100], CanonicalJson.Serialize(bounded));
        var raised = LibraryWritePayloadBudget.Apply(response, comment.Length + 10000, comment.Length + 30000);
        Assert.True(raised.Success);
        Assert.Null(raised.Batch!.Operations[0].Omission);
        Assert.Equal(comment, raised.Batch.Operations[0].Result!.Value.GetProperty("metadata")
            .GetProperty("comments")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void Budget_GuardCountIncludesNewGuard()
    {
        var items = new[] { new LibraryWriteItem { OperationId = "a", Operation = "create_type" } };
        var budget = new GuardedWriteResponseBudget("library_read", "guidance", "diagnostics");
        var expected = budget.MeasureProtectedCore(items.Select(i => (i.OperationId, i.Operation)).ToArray(), LibraryGuardDefinitions.Definitions.Count, 3, true,
            core => new LibraryGuardedWriteResponse("library_write", LibraryWriteContractVersion.Current, WritePhases.Applied, false,
                core.Error, core.Warnings, core.Guards, items.Select(i => new LibraryWriteEffectPresentation(i.OperationId, null, core.Omission)).ToArray(),
                core.Batch, new(false, [], core.Omission), core.Omission));
        Assert.True(LibraryWritePayloadBudget.MeasureProtectedCore(items) >= expected);
    }

    [Fact]
    public void OversizedTypeContent_ValidationError()
    {
        var item = new LibraryWriteItem { OperationId = "a", Operation = "create_type_version", PlcName = "PLC", PlcKind = "block", PlcObjectName = "Motor",
            Content = new string('x', StructuredOperationBatchPayloadBudget.MaxItemChars + 1), ExpectedContentHash = "xml:sha256:" + new string('a', 64) };
        Assert.False(LibraryWriteCatalog.ValidateWrite([item]).IsValid);
    }
    [Theory]
    [InlineData(1000, 20000)]
    [InlineData(20000, 2000)]
    public void OversizedFolderPreviewIsOmittedWhole(int itemLimit, int documentLimit)
    {
        var contents = Enumerable.Range(0, 20).Select(i => new LibraryIdentityInfo { Name = new string('x', 150) + i }).ToArray();
        var response = Response([new("a", new(null, null, null, contents, true), null)]);
        var original = CanonicalJson.Serialize(response);
        var bounded = LibraryWritePayloadBudget.Apply(response, itemLimit, documentLimit);
        Assert.Null(bounded.Effects[0].Effect);
        Assert.NotNull(bounded.Effects[0].Omission);
        Assert.Equal("library_read", bounded.Effects[0].Omission!.RetryTool);
        Assert.False(bounded.Success);
        Assert.Equal(original, CanonicalJson.Serialize(response));
        Assert.InRange(CanonicalJson.Serialize(bounded).Length, 0, documentLimit);
    }

    [Fact]
    public void ResultOmissionPreservesExecutionAndVerificationTruth()
    {
        var batch = StructuredOperationBatch.FromItems([new("a", "create_folder", "succeeded",
            CanonicalJson.ToElement(new { name = new string('x', 10000) }), null, null, null, [])]);
        var response = Response([]) with { Phase = "applied", Batch = batch,
            Verification = new(true, [new("a", true, "trusted", null, null, null)], null) };
        var bounded = LibraryWritePayloadBudget.Apply(response, 1000, 5000);
        Assert.False(bounded.Success);
        Assert.True(bounded.Verification!.Success);
        Assert.Equal("succeeded", bounded.Batch!.Operations[0].Status);
        Assert.Null(bounded.Batch.Operations[0].Result);
        Assert.Equal("library_read", bounded.Batch.Operations[0].Omission!.RetryTool);
    }

    [Fact]
    public void OmittedInstantiationDiagnosticsDistinguishLibraryObjectsAndPlcDestinations()
    {
        var batch = StructuredOperationBatch.FromItems([new("a", "instantiate_master_copy", "failed",
            null, new("worker_operation_failed", new string('x', 10000)), null, null, [])]);
        var response = Response([]) with { Phase = "applied", Success = false, Batch = batch,
            Verification = new(true, [], null) };
        var bounded = LibraryWritePayloadBudget.Apply(response, 1000, 5000);

        var failed = Assert.Single(bounded.Batch!.Operations);
        Assert.Equal("failed", failed.Status);
        Assert.Contains("library_read for library objects", failed.Failure!.Message);
        Assert.Contains("plc_read for PLC destinations", failed.Failure.Message);
        Assert.Equal("library_read", bounded.Omission!.RetryTool);
        Assert.Contains("library_read for library objects", bounded.Omission.Guidance);
        Assert.Contains("plc_read for PLC destinations", bounded.Omission.Guidance);
        Assert.Contains("Do not replay the write", bounded.Omission.Guidance);
        Assert.True(bounded.Verification!.Success);
        Assert.False(bounded.Success);
        Assert.InRange(CanonicalJson.Serialize(bounded).Length, 0, 5000);
    }

    [Fact]
    public void EncodedIdentifiersReserveProtectedSummaries()
    {
        var items = Enumerable.Range(0, 50).Select(i => new LibraryWriteItem
            { OperationId = new string('\u0001', 253) + i.ToString("D3"), Operation = "delete_folder" }).ToArray();
        Assert.True(LibraryWritePayloadBudget.MeasureProtectedCore(items) > StructuredOperationBatchPayloadBudget.MaxDocumentChars);
    }

    [Fact]
    public void CompleteResponseIsUnchanged()
    {
        var response = Response([]);
        Assert.Equal(CanonicalJson.Serialize(response), CanonicalJson.Serialize(LibraryWritePayloadBudget.Apply(response)));
        Assert.Equal("2.0", response.ContractVersion);
    }

    private static LibraryGuardedWriteResponse Response(IReadOnlyList<LibraryWriteEffectPresentation> effects)
        => new("library_write", LibraryWriteContractVersion.Current, "preview", true, null, [], [], effects, null, null, null);
}
