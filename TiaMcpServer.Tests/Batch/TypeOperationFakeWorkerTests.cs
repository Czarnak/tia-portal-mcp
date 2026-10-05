using System.Text.Json;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

/// <summary>
/// End-to-end coverage of update_type_content through the real batch tools and a FakeWorker
/// process. Proves what the pure BuildRequest unit tests in TypeOperationInvokerTests cannot
/// reach: the safety-token preview/apply/single-use round trip works for update_type_content
/// exactly like it does for the other write operations.
/// </summary>
[Collection(RealWorkerProcessCollection.Name)]
public class TypeOperationFakeWorkerTests
{
    private const string TypePath = "PLC_1/Types/AnalogInputSettings";

    // Scripted in TiaMcpServer.FakeWorker/Program.cs: dispatches by request method rather than
    // by this scenario key, so the same key drives both the get_type_content current-state read
    // and the update_type_content write within one preview/apply round trip.
    private const string Scenario = "type-content-roundtrip";

    private static OpennessWorkerClient CreateClient(ProjectSessionBinding binding)
        => new(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());

    private static WriteSafetyService CreateSafety(TempAuditDirectory audit, ProjectSessionBinding binding)
        => new(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);

    private static async Task VerifyBindingAsync(OpennessWorkerClient client, ProjectSessionBinding binding)
    {
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, Scenario);
        Assert.True(binding.IsVerified);
    }

    private static BatchOperationRequest UpdateTypeContentOp(string operationId) => new()
    {
        OperationId = operationId,
        Operation = "update_type_content",
        TypePath = TypePath,
        SourceContent = "TYPE \"AnalogInputSettings\"\r\nEND_TYPE\r\n",
        ProjectPath = Scenario,
    };

    [Fact]
    public async Task PreviewWriteBatch_UpdateTypeContent_ReturnsTokenAndDescriptivePreview()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(Scenario);
        var safety = CreateSafety(audit, binding);
        using var client = CreateClient(binding);
        await VerifyBindingAsync(client, binding);

        var result = await WriteBatchTools.PreviewWriteBatch(
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
        var binding = new ProjectSessionBinding(Scenario);
        var safety = CreateSafety(audit, binding);
        using var client = CreateClient(binding);
        await VerifyBindingAsync(client, binding);

        var operations = new[] { UpdateTypeContentOp("w1") };

        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
        var token = JsonDocument.Parse(preview).RootElement.GetProperty("safetyToken").GetString();

        var firstApply = await WriteBatchTools.ApplyWriteBatch(client, safety, operations, confirm: true, safetyToken: token);
        using (var firstDoc = JsonDocument.Parse(firstApply))
        {
            Assert.True(firstDoc.RootElement.GetProperty("success").GetBoolean());
        }

        // Tokens are single-use: replaying the same token must be rejected, not re-applied.
        var secondApply = await WriteBatchTools.ApplyWriteBatch(client, safety, operations, confirm: true, safetyToken: token);
        using var secondDoc = JsonDocument.Parse(secondApply);
        Assert.False(secondDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("Safety token", secondDoc.RootElement.GetProperty("error").GetString());
    }
}
