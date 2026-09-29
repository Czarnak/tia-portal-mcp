using System.Text.Json;
using TiaMcpServer.Batch;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public class BatchContentHashTests
{
    private const string Text = "<Document/>";

    private static BatchOperationRequest Request(string operation, string? format = null, bool? withDependencies = null) => new()
    {
        OperationId = "op",
        Operation = operation,
        BlockPath = operation == "get_block_content" ? "PLC_1/Main" : null,
        TypePath = operation == "get_type_content" ? "PLC_1/Types/T" : null,
        Format = format,
        WithDependencies = withDependencies,
    };

    private static OperationBatchResult Ok(string operation, string? text = Text)
        => new("op", operation, OperationBatchStatus.Succeeded, text);

    private static string? HashOf(BatchOperationRequest request, OperationBatchResult result)
        => BatchContentHashes.Attach(new[] { request }, new[] { result })[0].ContentHash;

    [Theory]
    [InlineData("get_block_content", null, "xml")]
    [InlineData("get_block_content", "source", "source")]
    [InlineData("get_block_content", "xml", "xml")]
    [InlineData("get_type_content", null, "source")]
    [InlineData("get_type_content", "xml", "xml")]
    [InlineData("get_type_content", "source", "source")]
    public void Attach_ContentRead_TagsServedFormat(string operation, string? format, string served)
        => Assert.Equal(ContentHashes.Compute(served, Text), HashOf(Request(operation, format), Ok(operation)));

    [Fact]
    public void Attach_WithDependenciesTrue_HasNoHash()
        => Assert.Null(HashOf(Request("get_type_content", "source", true), Ok("get_type_content")));

    [Fact]
    public void Attach_WithDependenciesFalse_HasHash()
        => Assert.NotNull(HashOf(Request("get_type_content", "source", false), Ok("get_type_content")));

    [Fact]
    public void Attach_FailedRead_HasNoHash()
    {
        var failed = new OperationBatchResult("op", "get_block_content", OperationBatchStatus.Failed, "boom");
        Assert.Null(HashOf(Request("get_block_content"), failed));
    }

    [Fact]
    public void Attach_NullResultText_HasNoHash()
        => Assert.Null(HashOf(Request("get_block_content"), Ok("get_block_content", null)));

    [Fact]
    public void Attach_OtherOperation_HasNoHash()
        => Assert.Null(HashOf(Request("list_tag_tables"), Ok("list_tag_tables")));

    [Fact]
    public void Attach_MismatchedCounts_Throws()
        => Assert.Throws<ArgumentException>(() =>
            BatchContentHashes.Attach(new[] { Request("get_block_content") }, Array.Empty<OperationBatchResult>()));

    [Fact]
    public void Budget_TruncatedItem_ClearsHash()
    {
        var big = new string('x', 500_000);
        var results = new[] { Ok("get_block_content", big) with { ContentHash = "xml:sha256:abc" } };
        var budgeted = OperationBatchPayloadBudget.Apply(results, "execute_read_batch", "execute_read_batch", "narrow");
        Assert.NotEqual(big, budgeted[0].Result);
        Assert.Null(budgeted[0].ContentHash);
    }

    [Fact]
    public void Budget_OmittedItem_ClearsHash()
    {
        var big = new string('x', 60_000);
        var results = Enumerable.Range(0, 40)
            .Select(i => new OperationBatchResult($"op{i}", "get_block_content", OperationBatchStatus.Succeeded, big)
            { ContentHash = "xml:sha256:abc" })
            .ToArray();
        var budgeted = OperationBatchPayloadBudget.Apply(results, "execute_read_batch", "execute_read_batch", "narrow");
        var changed = budgeted.Where(r => r.Status == OperationBatchStatus.Omitted || r.Result != big).ToArray();
        Assert.NotEmpty(changed);
        Assert.All(changed, r => Assert.Null(r.ContentHash));
    }

    [Fact]
    public void Formatter_EmitsHashOnlyWhenPresent()
    {
        var results = new[]
        {
            Ok("get_block_content") with { ContentHash = "xml:sha256:abc" },
            new OperationBatchResult("b", "list_tag_tables", OperationBatchStatus.Succeeded, "[]"),
        };
        using var doc = JsonDocument.Parse(OperationBatchResultFormatter.Read("execute_read_batch", results));
        var ops = doc.RootElement.GetProperty("operations");
        Assert.Equal("xml:sha256:abc", ops[0].GetProperty("contentHash").GetString());
        Assert.False(ops[1].TryGetProperty("contentHash", out _));
    }

    [Fact]
    public async Task ExecuteReadBatch_GetTypeContent_ReturnsHashOverServedText()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, "type-content-roundtrip");

        var json = await BatchTools.ExecuteReadBatch(client, new[]
        {
            new BatchOperationRequest
            {
                OperationId = "r1", Operation = "get_type_content",
                TypePath = "PLC_1/Types/AnalogInputSettings", ProjectPath = "type-content-roundtrip",
            }
        });

        using var doc = JsonDocument.Parse(json);
        var op = doc.RootElement.GetProperty("operations")[0];
        Assert.Equal(ContentHashes.Compute("source", op.GetProperty("result").GetString()!),
            op.GetProperty("contentHash").GetString());
    }
}
