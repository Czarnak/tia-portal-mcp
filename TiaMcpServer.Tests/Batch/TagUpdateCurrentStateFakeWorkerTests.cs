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
        bool? externalAccessible = null,
        bool? externalVisible = null,
        bool? externalWritable = null) => new()
    {
        OperationId = "update-motor-ready",
        Operation = "update_tag",
        PlcName = "PLC_1",
        TableName = TableName,
        FolderPath = "/",
        Name = TagName,
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
}
