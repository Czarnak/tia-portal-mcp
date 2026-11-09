using System.Text.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Library;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Library;

public sealed class LibraryReadBudgetTests
{
    private const int RaisedItemChars = 100_000;
    private const int RaisedDocumentChars = 300_000;

    private static StructuredOperationBatch BatchWithEntryOfAbout(int characters)
    {
        var page = new LibraryPageInfo
        {
            Items = { new LibraryEntryInfo { Identity = { Scope = "global", Kind = "library", Name = new string('x', characters) } } },
            Total = 1,
            Complete = true,
        };
        var item = LibraryPayloadContract.Project(
            new LibraryReadItem { OperationId = "r", Operation = "list_global_libraries" },
            WorkerCallResult.Ok(TiaMcpServer.Contracts.Json.WorkerJson.SerializePayload(page)),
            _ => { });
        Assert.Equal("succeeded", item.Status);
        return StructuredOperationBatch.FromItems(new[] { item });
    }

    [Fact]
    public void InspectMetadata_OmittedWholeAtEffectiveLimit_RaisedLimitPreservesCompleteComment()
    {
        var comment = new string('c', StructuredOperationBatchPayloadBudget.MaxItemChars + 1000);
        var page = new LibraryPageInfo { Items = [new() { Metadata = new()
            { Comments = [new() { Culture = "en-US", Text = comment }] } }] };
        var item = LibraryPayloadContract.Project(new() { OperationId = "metadata", Operation = "inspect_library" },
            WorkerCallResult.Ok(TiaMcpServer.Contracts.Json.WorkerJson.SerializePayload(page)), _ => { });
        var batch = StructuredOperationBatch.FromItems([item]);
        var bounded = LibraryReadTools.ApplyBudget(batch);
        Assert.False(bounded.IsFullySuccessful);
        Assert.Equal("omitted", bounded.Operations[0].Status);
        Assert.Null(bounded.Operations[0].Result);
        Assert.NotNull(bounded.Operations[0].Omission);
        Assert.DoesNotContain(comment[..100], TiaMcpServer.Json.CanonicalJson.Serialize(bounded));
        var raised = LibraryReadTools.ApplyBudget(batch, comment.Length + 10000, comment.Length + 30000);
        Assert.True(raised.IsFullySuccessful);
        Assert.Equal(comment, raised.Operations[0].Result!.Value.GetProperty("items")[0]
            .GetProperty("metadata").GetProperty("comments")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void Budget_OversizedItemOmitted_AtDefaultAndRaisedLimits()
    {
        // Above the default 60,000 per-item limit, below the raised one.
        var middle = BatchWithEntryOfAbout(StructuredOperationBatchPayloadBudget.DefaultItemChars + 5_000);
        var atDefault = LibraryReadTools.ApplyBudget(middle,
            StructuredOperationBatchPayloadBudget.DefaultItemChars, StructuredOperationBatchPayloadBudget.DefaultDocumentChars).Operations[0];
        var raised = LibraryReadTools.ApplyBudget(middle, RaisedItemChars, RaisedDocumentChars).Operations[0];

        Assert.Equal("omitted", atDefault.Status);
        Assert.Null(atDefault.Result);
        Assert.Equal("library_read", atDefault.Omission!.RetryTool);
        Assert.Equal("succeeded", raised.Status);

        // Above even the raised limit.
        var huge = BatchWithEntryOfAbout(RaisedItemChars + 5_000);
        var stillOmitted = LibraryReadTools.ApplyBudget(huge, RaisedItemChars, RaisedDocumentChars).Operations[0];
        Assert.Equal("omitted", stillOmitted.Status);
        Assert.Contains("library_read", stillOmitted.Omission!.Guidance);
    }

    [Fact]
    public void Budget_PagedGuidance_NamesLimit()
    {
        var omitted = LibraryReadTools.ApplyBudget(BatchWithEntryOfAbout(2_000), maxItemChars: 1_000, maxDocumentChars: 5_000).Operations[0];

        Assert.Equal("omitted", omitted.Status);
        Assert.Contains("limit", omitted.Omission!.Guidance);
    }

    [Fact]
    public void Budget_EffectiveLimitsAreUsedWhenNotOverridden()
    {
        var batch = BatchWithEntryOfAbout(2_000);

        var applied = LibraryReadTools.ApplyBudget(batch).Operations[0];

        Assert.True(StructuredOperationBatchPayloadBudget.MaxItemChars > 2_000);
        Assert.Equal("succeeded", applied.Status);
        Assert.True(JsonSerializer.Serialize(applied).Length > 2_000);
    }
}
