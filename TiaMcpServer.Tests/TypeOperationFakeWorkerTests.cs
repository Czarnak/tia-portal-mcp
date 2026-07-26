using System.Text.Json;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// End-to-end coverage of get_type_content / update_type_content through the real batch tools
/// and a FakeWorker process. Proves what the pure BuildRequest unit tests in
/// TypeOperationInvokerTests cannot reach: the scripted worker payload actually flows back
/// through execute_read_batch, and the safety-token preview/apply/single-use round trip works
/// for update_type_content exactly like it does for the other write operations.
/// </summary>
public class TypeOperationFakeWorkerTests
{
    private const string TypePath = "PLC_1/Types/AnalogInputSettings";

    // Scripted in TiaMcpServer.FakeWorker/Program.cs: dispatches by request method rather than
    // by this scenario key, so the same key drives both the get_type_content current-state read
    // and the update_type_content write within one preview/apply round trip.
    private const string Scenario = "type-content-roundtrip";

    private static OpennessWorkerClient CreateClient()
        => new(new ProjectSessionBinding(null), logger: null, workerExecutablePath: FakeWorkerLocator.Locate());

    private static BatchOperationRequest UpdateTypeContentOp(string operationId) => new()
    {
        OperationId = operationId,
        Operation = "update_type_content",
        TypePath = TypePath,
        SourceContent = "TYPE \"AnalogInputSettings\"\r\nEND_TYPE\r\n",
        ProjectPath = Scenario,
    };

    [Fact]
    public async Task ExecuteReadBatch_GetTypeContent_ReturnsScriptedPayloadKeyedByOperationId()
    {
        using var client = CreateClient();

        var result = await BatchTools.ExecuteReadBatch(
            client,
            new[]
            {
                new BatchOperationRequest
                {
                    OperationId = "r1",
                    Operation = "get_type_content",
                    TypePath = TypePath,
                    ProjectPath = Scenario,
                }
            });

        using var doc = JsonDocument.Parse(result);
        var operation = doc.RootElement.GetProperty("operations")[0];
        Assert.Equal("r1", operation.GetProperty("operationId").GetString());
        Assert.Equal("succeeded", operation.GetProperty("status").GetString());
        Assert.Contains("AnalogInputSettings", operation.GetProperty("result").GetString());
    }

    [Fact]
    public async Task PreviewWriteBatch_UpdateTypeContent_ReturnsTokenAndDescriptivePreview()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        using var client = CreateClient();

        var result = await BatchTools.PreviewWriteBatch(
            client,
            safety,
            new[] { UpdateTypeContentOp("w1") });

        using var doc = JsonDocument.Parse(result);
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("safetyToken").GetString()));

        // The default JSON encoder escapes apostrophes ('), so assert on the DECODED preview
        // text rather than doing a raw substring search on the serialized JSON.
        var previewSummary = doc.RootElement.GetProperty("target")[0].GetProperty("summary").GetString();
        Assert.Equal("Update PLC data type 'PLC_1/Types/AnalogInputSettings'.", previewSummary);
    }

    [Fact]
    public async Task ApplyWriteBatch_UpdateTypeContent_SucceedsOnceThenRejectsReplayedToken()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        using var client = CreateClient();

        var operations = new[] { UpdateTypeContentOp("w1") };

        var preview = await BatchTools.PreviewWriteBatch(client, safety, operations);
        var token = JsonDocument.Parse(preview).RootElement.GetProperty("safetyToken").GetString();

        var firstApply = await BatchTools.ApplyWriteBatch(client, safety, operations, confirm: true, safetyToken: token);
        using (var firstDoc = JsonDocument.Parse(firstApply))
        {
            Assert.True(firstDoc.RootElement.GetProperty("success").GetBoolean());
        }

        // Tokens are single-use: replaying the same token must be rejected, not re-applied.
        var secondApply = await BatchTools.ApplyWriteBatch(client, safety, operations, confirm: true, safetyToken: token);
        using var secondDoc = JsonDocument.Parse(secondApply);
        Assert.False(secondDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("Safety token", secondDoc.RootElement.GetProperty("error").GetString());
    }
}
