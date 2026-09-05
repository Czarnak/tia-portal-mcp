using System.Text.Json;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public class TagUpdateCurrentStateFakeWorkerTests
{
    private const string TableName = "Default tag table";
    private const string TagName = "MotorReady";

    private static OpennessWorkerClient CreateClient(ProjectSessionBinding binding)
        => new(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());

    private static WriteSafetyService CreateSafety(TempAuditDirectory audit, ProjectSessionBinding binding)
        => new(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);

    private static BatchOperationRequest UpdateTagOp(
        string projectPath,
        string name = TagName,
        string? dataType = null,
        bool? externalAccessible = null,
        bool? externalVisible = null,
        bool? externalWritable = null) => new()
    {
        OperationId = "update-motor-ready",
        Operation = "update_tag",
        PlcName = "PLC_1",
        TableName = TableName,
        FolderPath = "/",
        Name = name,
        DataType = dataType,
        ExternalAccessible = externalAccessible,
        ExternalVisible = externalVisible,
        ExternalWritable = externalWritable,
        ProjectPath = projectPath,
    };

    [Fact]
    public async Task PreviewWriteBatch_UpdateTagRejectsRequestedUnavailableFlagBeforeTokenIssuance()
    {
        const string scenario = "tag-update-snapshot-unavailable-visible";
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        var safety = CreateSafety(audit, binding);
        using var client = CreateClient(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, scenario);
        var operations = new[] { UpdateTagOp(scenario, externalVisible: true) };

        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);

        Assert.Contains("externalVisible", preview, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"safetyToken\":", preview, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("externalAccessible", true, null, null)]
    [InlineData("externalVisible", null, true, null)]
    [InlineData("externalWritable", null, null, true)]
    public async Task PreviewWriteBatch_UpdateTagRejectsEachRequestedUnavailableFlagBeforeTokenIssuance(
        string expectedFlag,
        bool? externalAccessible,
        bool? externalVisible,
        bool? externalWritable)
    {
        const string scenario = "tag-update-snapshot-unavailable-all";
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        var safety = CreateSafety(audit, binding);
        using var client = CreateClient(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, scenario);
        var operations = new[]
        {
            UpdateTagOp(
                scenario,
                externalAccessible: externalAccessible,
                externalVisible: externalVisible,
                externalWritable: externalWritable)
        };

        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);

        using var document = JsonDocument.Parse(preview);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            WorkerFailureCategories.ValidationError,
            document.RootElement.GetProperty("failureCategory").GetString());
        Assert.Contains(expectedFlag, document.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.False(document.RootElement.TryGetProperty("safetyToken", out _));
    }

    [Fact]
    public async Task PreviewWriteBatch_UpdateTagWithoutUnavailableFlagRequestStillIssuesToken()
    {
        const string scenario = "tag-update-snapshot-unavailable-all";
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        var safety = CreateSafety(audit, binding);
        using var client = CreateClient(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, scenario);

        var preview = await WriteBatchTools.PreviewWriteBatch(
            client,
            safety,
            new[] { UpdateTagOp(scenario, dataType: "DInt") });

        using var document = JsonDocument.Parse(preview);
        Assert.True(document.RootElement.TryGetProperty("safetyToken", out var safetyToken), preview);
        Assert.False(string.IsNullOrWhiteSpace(safetyToken.GetString()));
    }

    public static TheoryData<string> InvalidStrictSnapshotPayloads()
    {
        var cases = new TheoryData<string>
        {
            "empty",
            "malformed",
            "root-array",
            "unknown-member",
            "duplicate-member",
        };
        foreach (var member in new[]
        {
            "plcName",
            "folderPath",
            "tableName",
            "tagName",
            "dataType",
            "logicalAddress",
            "externalAccessible",
            "externalVisible",
            "externalWritable",
        })
        {
            cases.Add($"missing-{member}");
            cases.Add($"wrong-{member}");
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(InvalidStrictSnapshotPayloads))]
    public async Task PreviewWriteBatch_UpdateTagInvalidStrictSnapshotFailsClosedBeforeBroadRead(string payloadVariant)
    {
        const string scenario = "tag-update-snapshot-invalid-payload";
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        var safety = CreateSafety(audit, binding);
        using var client = CreateClient(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, scenario);

        var preview = await WriteBatchTools.PreviewWriteBatch(
            client,
            safety,
            new[] { UpdateTagOp(scenario, name: payloadVariant) });
        var observedState = await client.GetProjectStatusAsync(scenario);

        using var previewDocument = JsonDocument.Parse(preview);
        Assert.False(previewDocument.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            WorkerFailureCategories.ProtocolError,
            previewDocument.RootElement.GetProperty("failureCategory").GetString());
        Assert.False(previewDocument.RootElement.TryGetProperty("safetyToken", out _));
        Assert.True(observedState.Success, observedState.Error);
        using var stateDocument = JsonDocument.Parse(observedState.Payload);
        Assert.Equal(0, stateDocument.RootElement.GetProperty("invalidSnapshotBroadReadCount").GetInt32());
    }

    [Fact]
    public async Task ApplyWriteBatch_UpdateTagFlagOnlyDriftFailsWithStateChanged()
    {
        const string scenario = "tag-update-flag-drift";
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        var safety = CreateSafety(audit, binding);
        using var client = CreateClient(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, scenario);
        var operations = new[] { UpdateTagOp(scenario, externalAccessible: true) };

        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
        var token = JsonDocument.Parse(preview).RootElement.GetProperty("safetyToken").GetString();

        var apply = await WriteBatchTools.ApplyWriteBatch(client, safety, operations, confirm: true, safetyToken: token);

        Assert.Contains("\"failureCategory\":\"state_changed\"", apply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyWriteBatch_UpdateTagBroadOnlyDriftFailsWithStateChangedBeforeMutation()
    {
        const string scenario = "tag-update-broad-drift";
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        var safety = CreateSafety(audit, binding);
        using var client = CreateClient(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, scenario);
        var operations = new[] { UpdateTagOp(scenario, externalVisible: false) };

        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
        using var previewDocument = JsonDocument.Parse(preview);
        var token = previewDocument.RootElement.GetProperty("safetyToken").GetString();

        var apply = await WriteBatchTools.ApplyWriteBatch(
            client,
            safety,
            operations,
            confirm: true,
            safetyToken: token);
        var observedState = await client.GetProjectStatusAsync(scenario);

        using var applyDocument = JsonDocument.Parse(apply);
        Assert.False(applyDocument.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            WorkerFailureCategories.StateChanged,
            applyDocument.RootElement.GetProperty("failureCategory").GetString());
        Assert.True(observedState.Success, observedState.Error);
        using var stateDocument = JsonDocument.Parse(observedState.Payload);
        Assert.Equal(0, stateDocument.RootElement.GetProperty("broadDriftMutationCount").GetInt32());
    }

    [Fact]
    public async Task PreviewWriteBatch_UpdateTagStrictSnapshotFailurePreventsBroadReadAndTokenIssuance()
    {
        const string scenario = "tag-update-snapshot-read-fails";
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        var safety = CreateSafety(audit, binding);
        using var client = CreateClient(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, scenario);

        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, new[] { UpdateTagOp(scenario) });
        var observedState = await client.GetProjectStatusAsync(scenario);

        Assert.Contains("strict update-tag snapshot read failed", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("\"safetyToken\":", preview, StringComparison.Ordinal);
        Assert.True(observedState.Success, observedState.Error);
        using var document = JsonDocument.Parse(observedState.Payload);
        Assert.Equal(0, document.RootElement.GetProperty("strictSnapshotFailureBroadReadCount").GetInt32());
    }

    [Fact]
    public async Task PreviewWriteBatch_UpdateTagAllowsBroadBestEffortWarningAfterStrictSnapshotSucceeds()
    {
        const string scenario = "tag-update-broad-best-effort-omission";
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        var safety = CreateSafety(audit, binding);
        using var client = CreateClient(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, scenario);
        var operations = new[] { UpdateTagOp(scenario, externalVisible: false) };

        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);

        Assert.Contains("\"safetyToken\":", preview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewWriteBatch_UpdateTagMalformedBroadPayloadFailsClosedWithProtocolError()
    {
        const string scenario = "tag-update-broad-malformed-payload";
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        var safety = CreateSafety(audit, binding);
        using var client = CreateClient(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, scenario);

        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, new[] { UpdateTagOp(scenario) });

        Assert.Contains("\"success\":false", preview, StringComparison.Ordinal);
        Assert.Contains("\"failureCategory\":\"protocol_error\"", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("\"safetyToken\":", preview, StringComparison.Ordinal);
    }
}
