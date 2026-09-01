using System.Text.Json;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class WriteBatchToolsBehaviorTests
{
    private static WriteSafetyService CreateSafety(TempAuditDirectory audit, ProjectSessionBinding binding)
        => new(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);

    private static OpennessWorkerClient CreateClient(
        ProjectSessionBinding binding,
        McpAccessMode mode = McpAccessMode.ReadWrite)
        => new(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(mode));

    private static BatchOperationRequest CreateUserConstantOp(
        string operationId,
        string projectPath = "type-content-roundtrip",
        string name = "Gain") => new()
        {
            OperationId = operationId,
            Operation = "create_user_constant",
            ProjectPath = projectPath,
            TableName = "Constants",
            Name = name,
            DataType = "Int",
            Value = "1",
        };

    private static BatchOperationRequest UpdateTypeContentOp(
        string operationId,
        string projectPath) => new()
        {
            OperationId = operationId,
            Operation = "update_type_content",
            ProjectPath = projectPath,
            TypePath = "PLC_1/Types/AnalogInputSettings",
            SourceContent = "TYPE \"AnalogInputSettings\"\r\nEND_TYPE\r\n",
        };

    private static int CountAuditLines(string directory)
        => Directory.Exists(directory)
            ? Directory.GetFiles(directory).Sum(file => File.ReadAllLines(file).Length)
            : 0;

    [Fact]
    public async Task PreviewWriteBatch_ReadOnlyMode_IsRejectedBeforeTokenIssuance()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding, McpAccessMode.ReadOnly);

        var result = await WriteBatchTools.PreviewWriteBatch(
            client,
            CreateSafety(audit, binding),
            new[] { CreateUserConstantOp("op-1") });

        using var doc = JsonDocument.Parse(result);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("read-only mode", doc.RootElement.GetProperty("error").GetString());
        Assert.False(doc.RootElement.TryGetProperty("safetyToken", out _));
    }

    [Fact]
    public async Task PreviewWriteBatch_UnverifiedBinding_IsRejectedBeforeCurrentStateRead()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        var nonStartableWorkerPath = Path.Combine(audit.Path, "worker-must-not-start.exe");
        Assert.False(File.Exists(nonStartableWorkerPath));
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: nonStartableWorkerPath,
            accessPolicy: new OperationAccessPolicy(McpAccessMode.ReadWrite));

        var result = await WriteBatchTools.PreviewWriteBatch(
            client,
            CreateSafety(audit, binding),
            new[] { CreateUserConstantOp("op-1") });

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("preview_write_batch", doc.RootElement.GetProperty("tool").GetString());
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "A worker-verified project binding is required before previewing or executing a write. "
            + "Configure --project and verify it, or call open_project explicitly first.",
            doc.RootElement.GetProperty("error").GetString());
        Assert.Equal(3, doc.RootElement.EnumerateObject().Count());
        Assert.False(doc.RootElement.TryGetProperty("safetyToken", out _));
    }

    [Fact]
    public async Task ApplyWriteBatch_RegisteredPath_PreservesRequestOrder_StopsOnProtocolFailure_SkipsLaterItems_AndWritesOnlyInjectedAudit()
    {
        var defaultDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TiaMcpServer",
            "audit");
        var defaultBefore = CountAuditLines(defaultDirectory);

        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding);
        var safety = CreateSafety(audit, binding);
        const string scenario = "type-content-ordered-protocol-failure";
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, scenario);

        var operations = new[]
        {
            UpdateTypeContentOp("first", scenario),
            UpdateTypeContentOp("second", scenario),
            UpdateTypeContentOp("third", scenario),
        };

        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
        using var previewDoc = JsonDocument.Parse(preview);
        var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();

        var applied = await WriteBatchTools.ApplyWriteBatch(
            client,
            safety,
            operations,
            confirm: true,
            safetyToken: token);

        using var appliedDoc = JsonDocument.Parse(applied);
        var items = appliedDoc.RootElement.GetProperty("operations");

        Assert.Equal(new[] { "first", "second", "third" }, items.EnumerateArray().Select(i => i.GetProperty("operationId").GetString()).ToArray());
        Assert.Equal("succeeded", items[0].GetProperty("status").GetString());
        Assert.Equal("failed", items[1].GetProperty("status").GetString());
        Assert.Contains("write outcome is unknown", items[1].GetProperty("result").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("this is not json", items[1].GetProperty("result").GetString(), StringComparison.Ordinal);
        Assert.Equal("skipped", items[2].GetProperty("status").GetString());

        Assert.True(Directory.Exists(audit.Path));
        Assert.NotEmpty(Directory.GetFiles(audit.Path));
        Assert.Equal(defaultBefore, CountAuditLines(defaultDirectory));
    }
}
