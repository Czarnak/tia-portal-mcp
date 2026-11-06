using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Json;
using TiaMcpServer.Library;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using Xunit;

namespace TiaMcpServer.Tests.Library;

public sealed class LibraryWriteBudgetTests
{
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
