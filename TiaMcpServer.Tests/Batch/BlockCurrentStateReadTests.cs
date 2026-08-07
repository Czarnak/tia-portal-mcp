using System.Text.Json;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

/// <summary>
/// The safety token a write item is bound to is built from
/// <see cref="BatchWorkerInvoker.ReadCurrentStateAsync"/>'s payload, so that read must observe the
/// block through the SAME format the write will use. It did not: update_block_logic's arm called
/// GetBlockContentAsync with no format, which the worker normalizes to xml. While format=source was
/// inert for blocks that was self-consistent; once the write routes through the external-source
/// pipeline it stops being — a concurrent TIA Portal edit visible in the .db source but not in the
/// Simatic ML (S7_Optimized_Access, block or member comments, an attribute-only edit) leaves the
/// token's state hash matching, so the token is accepted and the edit is silently overwritten.
///
/// Nothing covered this: BatchFieldForwardingTests drives InvokeAsync (the write call), never
/// ReadCurrentStateAsync, and no test drove PreviewWriteBatch/ApplyWriteBatch with a format-bearing
/// write item at all. These assert on the format VALUE that reached the worker, so a regression to
/// a default cannot pass.
/// </summary>
public class BlockCurrentStateReadTests
{
    private const string BlockPath = "PLC_1/Blocks/Recipe_DB";
    private const string TypePath = "PLC_1/Types/AnalogInputSettings";

    private const string DbSource = "DATA_BLOCK \"Recipe\"\r\nSTRUCT\r\nEND_STRUCT;\r\nBEGIN\r\nEND_DATA_BLOCK\r\n";

    private static OpennessWorkerClient CreateClient()
        => new(new ProjectSessionBinding(null), logger: null, workerExecutablePath: FakeWorkerLocator.Locate());

    /// <summary>
    /// The "echo" scenario returns the received request verbatim, so the request the current-state
    /// read actually sent is readable as JSON rather than inferred.
    /// </summary>
    private static async Task<JsonElement> ReadCurrentStateRequestAsync(BatchOperationRequest op)
    {
        using var client = CreateClient();
        var result = await BatchWorkerInvoker.ReadCurrentStateAsync(client, op);
        Assert.True(result.Success, result.Error);

        return JsonDocument.Parse(result.Payload).RootElement.Clone();
    }

    private static BatchOperationRequest UpdateBlockLogicOp(string? format, string projectPath = "echo") => new()
    {
        OperationId = "w1",
        Operation = "update_block_logic",
        BlockPath = BlockPath,
        YamlContent = DbSource,
        Format = format,
        ProjectPath = projectPath,
    };

    [Fact]
    public async Task Update_block_logic_current_state_read_uses_the_items_source_format()
    {
        var request = await ReadCurrentStateRequestAsync(UpdateBlockLogicOp(SourceFormatNames.Source));

        Assert.Equal("get_block_content", request.GetProperty("method").GetString());
        Assert.Equal(BlockPath, request.GetProperty("blockPath").GetString());
        Assert.Equal(SourceFormatNames.Source, request.GetProperty("format").GetString());
    }

    /// <summary>
    /// The other half of the same contract: with no format the read must still bind the xml export,
    /// so the fix cannot have silently changed what a format-less write binds to.
    /// </summary>
    [Fact]
    public async Task Update_block_logic_current_state_read_binds_xml_when_no_format_is_requested()
    {
        var request = await ReadCurrentStateRequestAsync(UpdateBlockLogicOp(format: null));

        Assert.Equal("get_block_content", request.GetProperty("method").GetString());
        Assert.Equal(SourceFormatNames.Xml, request.GetProperty("format").GetString());
    }

    /// <summary>
    /// Guards the neighbouring arm the block arm was ported from, so the two cannot drift apart
    /// again without a test failing.
    /// </summary>
    [Fact]
    public async Task Update_type_content_current_state_read_uses_the_items_format()
    {
        var request = await ReadCurrentStateRequestAsync(new BatchOperationRequest
        {
            OperationId = "w1",
            Operation = "update_type_content",
            TypePath = TypePath,
            SourceContent = "TYPE \"AnalogInputSettings\"\r\nEND_TYPE\r\n",
            Format = SourceFormatNames.Xml,
            ProjectPath = "echo",
        });

        Assert.Equal("get_type_content", request.GetProperty("method").GetString());
        Assert.Equal(SourceFormatNames.Xml, request.GetProperty("format").GetString());
    }

    /// <summary>
    /// NormalizeFormat throws for an invalid format. Routing the block arm through it means that
    /// throw now happens on the preview path too, so it must land as a failed result — the batch
    /// contract is that a bad item fails only itself (BatchTools.cs), never the loop.
    /// </summary>
    [Fact]
    public async Task Update_block_logic_current_state_read_rejects_an_invalid_format_without_calling_the_worker()
    {
        using var client = CreateClient();

        var result = await BatchWorkerInvoker.ReadCurrentStateAsync(client, UpdateBlockLogicOp("s7dcl"));

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.ValidationError, result.FailureCategory);
        Assert.Contains("s7dcl", result.Error);
    }

    /// <summary>
    /// The end-to-end half, through the real preview/apply tools rather than the invoker alone:
    /// the "block-source-roundtrip" fake-worker scenario answers get_block_content ONLY when the
    /// request carries format=source, so a current-state read that fell back to xml fails the
    /// preview outright. Closes the gap an earlier review flagged — no regression test drove
    /// PreviewWriteBatch/ApplyWriteBatch with a format-bearing write item.
    /// </summary>
    [Fact]
    public async Task PreviewAndApplyWriteBatch_UpdateBlockLogicWithSourceFormat_ReadsCurrentStateAsSource()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        using var client = CreateClient();

        var operations = new[] { UpdateBlockLogicOp(SourceFormatNames.Source, projectPath: "block-source-roundtrip") };

        var preview = await BatchTools.PreviewWriteBatch(client, safety, operations);
        using var previewDoc = JsonDocument.Parse(preview);
        var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        var apply = await BatchTools.ApplyWriteBatch(client, safety, operations, confirm: true, safetyToken: token);
        using var applyDoc = JsonDocument.Parse(apply);
        Assert.True(
            applyDoc.RootElement.GetProperty("success").GetBoolean(),
            applyDoc.RootElement.ToString());
    }
}
