using System.Text.Json;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

[Collection(RealWorkerProcessCollection.Name)]
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

    private static BatchOperationRequest UpdateBlockLogicOp(
        string operationId,
        string projectPath,
        string blockPath,
        string content) => new()
        {
            OperationId = operationId,
            Operation = "update_block_logic",
            ProjectPath = projectPath,
            BlockPath = blockPath,
            YamlContent = content,
            Format = SourceFormatNames.Source,
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
            + "Call bind_project first, or configure --project and verify it. In read-write/full mode, you can also call open_project explicitly.",
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
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("failureCategory").ValueKind);
        Assert.Equal("failed", items[1].GetProperty("status").GetString());
        Assert.Equal(WorkerFailureCategories.WorkerCrashed, items[1].GetProperty("failureCategory").GetString());
        Assert.Contains(
            "The TIA Openness worker stopped before completion was confirmed. The project or PLC runtime state may have changed. Inspect current state before retrying.",
            items[1].GetProperty("result").GetString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain("this is not json", items[1].GetProperty("result").GetString(), StringComparison.Ordinal);
        Assert.Equal("skipped", items[2].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, items[2].GetProperty("failureCategory").ValueKind);
        Assert.Equal(1, appliedDoc.RootElement.GetProperty("failed").GetInt32());
        Assert.Equal(1, appliedDoc.RootElement.GetProperty("skipped").GetInt32());

        Assert.True(Directory.Exists(audit.Path));
        Assert.NotEmpty(Directory.GetFiles(audit.Path));
        Assert.Equal(defaultBefore, CountAuditLines(defaultDirectory));
    }

    [Fact]
    public async Task ApplyWriteBatch_UpdateBlockLogicFailure_EmitsOutcomeAndSkipsLaterItem()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding);
        var safety = CreateSafety(audit, binding);
        const string scenario = "block-outcome-audit-failure";
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, scenario);

        var operations = new[]
        {
            UpdateBlockLogicOp(
                "first",
                scenario,
                "PLC_1/Blocks/SecretSubmittedTarget",
                "SECRET_SUBMITTED_SOURCE_CONTENT"),
            UpdateBlockLogicOp(
                "second",
                scenario,
                "PLC_1/Blocks/MustBeSkipped",
                "SECOND_SECRET_CONTENT")
        };

        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
        using var previewDoc = JsonDocument.Parse(preview);
        var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();
        var requestedInputHash = previewDoc.RootElement.GetProperty("requestedInputHash").GetString();
        var currentStateHash = previewDoc.RootElement.GetProperty("currentStateHash").GetString();

        var applied = await WriteBatchTools.ApplyWriteBatch(
            client,
            safety,
            operations,
            confirm: true,
            safetyToken: token);

        using var appliedDoc = JsonDocument.Parse(applied);
        var items = appliedDoc.RootElement.GetProperty("operations");
        Assert.Equal("failed", items[0].GetProperty("status").GetString());
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, items[0].GetProperty("failureCategory").GetString());
        Assert.Equal("skipped", items[1].GetProperty("status").GetString());
        var outcome = items[0].GetProperty("blockImportOutcome");
        Assert.Equal("completed", outcome.GetProperty("importStage").GetString());
        Assert.True(outcome.GetProperty("targetMutationCommitted").GetBoolean());
        Assert.Equal("unavailable", outcome.GetProperty("compileStage").GetString());
        Assert.True(outcome.GetProperty("compileDetailsOmitted").GetBoolean());
        Assert.Contains("attempt 1", items[0].GetProperty("result").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(items[0].GetProperty("warnings").EnumerateArray(), warning =>
            warning.GetString() == "Project state may have changed; inspect the project before retrying.");

        var auditFiles = Directory.GetFiles(audit.Path);
        Assert.Single(auditFiles);
        var auditLines = await File.ReadAllLinesAsync(auditFiles[0]);
        Assert.Single(auditLines);
        using var auditDoc = JsonDocument.Parse(auditLines[0]);
        var auditRecord = auditDoc.RootElement;
        Assert.Equal(requestedInputHash, auditRecord.GetProperty("requestedInputHash").GetString());
        Assert.Equal(currentStateHash, auditRecord.GetProperty("currentStateHash").GetString());
        Assert.Equal(WriteSafetyService.HashText(applied), auditRecord.GetProperty("resultHash").GetString());
        var previewText = auditRecord.GetProperty("resultPreview").GetString();
        Assert.NotNull(previewText);
        Assert.Equal(2000, previewText!.Length);
        Assert.Equal(applied[..2000], previewText);
        Assert.DoesNotContain("SECRET_SUBMITTED_SOURCE_CONTENT", previewText, StringComparison.Ordinal);
        Assert.DoesNotContain("SecretSubmittedTarget", previewText, StringComparison.Ordinal);
        Assert.DoesNotContain("SECOND_SECRET_CONTENT", previewText, StringComparison.Ordinal);
        Assert.DoesNotContain("MustBeSkipped", previewText, StringComparison.Ordinal);

        var replay = await WriteBatchTools.ApplyWriteBatch(
            client,
            safety,
            operations,
            confirm: true,
            safetyToken: token);
        Assert.Contains("expired, consumed, or unknown", replay, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountAuditLines(audit.Path));
    }
}
