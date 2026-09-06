using System.Text.Json;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class ProjectTreeSafetyBehaviorTests
{
    [Theory]
    [InlineData("create_block_group", "tree-safety-duplicate-group-occupancy")]
    [InlineData("delete_block_group", "tree-safety-conflicting-descendants")]
    public async Task WriteBatchTools_AmbiguousSnapshotCandidates_RejectsBeforeTokenWithoutRawEcho(string operation, string scenario)
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
        var safety = new WriteSafetyService(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, scenario);
        var operations = new[] { new BatchOperationRequest
        {
            OperationId = "group", Operation = operation, BlockPath = "PLC_1/Blocks/Main/AreaA", ProjectPath = scenario
        } };

        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
        using var result = JsonDocument.Parse(preview);
        Assert.False(result.RootElement.TryGetProperty("safetyToken", out _));
        Assert.False(result.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(WorkerFailureCategories.ProtocolError, result.RootElement.GetProperty("failureCategory").GetString());
        Assert.False(result.RootElement.TryGetProperty("safetyToken", out _));
        Assert.DoesNotContain("PRIVATE_TREE_SNAPSHOT_CONTENT", preview);
        Assert.DoesNotContain("occupancies", preview);
        Assert.DoesNotContain("descendants", preview);
    }

    [Fact]
    public async Task WriteBatchTools_AuthoritativeExportFailure_StopsReadAndPreviewBeforeTokenIssuance()
    {
        const string scenario = "tree-safety-authoritative-export-failure";
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
        var safety = new WriteSafetyService(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, scenario);
        var operations = new[] { new BatchOperationRequest
        {
            OperationId = "create", Operation = "create_block", BlockPath = "PLC_1/Blocks/Main/Mixer",
            BlockType = "FB", Language = "SCL", ProjectPath = scenario
        } };

        var read = await BatchWorkerInvoker.ReadCurrentStateAsync(client, operations[0]);
        Assert.False(read.Success);
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, read.FailureCategory);
        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
        using var result = JsonDocument.Parse(preview);
        Assert.False(result.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, result.RootElement.GetProperty("failureCategory").GetString());
        Assert.False(result.RootElement.TryGetProperty("safetyToken", out _));
        Assert.DoesNotContain("PRIVATE_COMPANION", preview);
    }

    [Theory]
    [InlineData("create_block_group", "tree-safety-create-group-collision-drift")]
    [InlineData("delete_block_group", "tree-safety-delete-group-descendant-drift")]
    public async Task WriteBatchTools_BlockGroup_RelevantDrift_InvalidatesTheToken(string operation, string scenario)
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
        var safety = new WriteSafetyService(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, scenario);
        var operations = new[] { new BatchOperationRequest
        {
            OperationId = "group", Operation = operation,
            BlockPath = "PLC_1/Blocks/Main/AreaA", ProjectPath = scenario
        } };
        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
        using var previewDoc = JsonDocument.Parse(preview);
        Assert.True(previewDoc.RootElement.TryGetProperty("safetyToken", out var tokenElement), preview);
        var token = tokenElement.GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        var apply = await WriteBatchTools.ApplyWriteBatch(client, safety, operations, confirm: true, safetyToken: token);
        using var applyDoc = JsonDocument.Parse(apply);
        Assert.False(applyDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("state_changed", applyDoc.RootElement.GetProperty("failureCategory").GetString());
    }

    [Fact]
    public async Task WriteBatchTools_CreateBlock_MalformedSnapshotPayload_BecomesProtocolErrorWithoutRawEcho()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
        var safety = new WriteSafetyService(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, "tree-safety-malformed-payload");

        var operations = new[]
        {
            new BatchOperationRequest
            {
                OperationId = "create",
                Operation = "create_block",
                BlockPath = "PLC_1/Blocks/Main/Mixer",
                BlockType = "FB",
                Language = "SCL",
                ProjectPath = "tree-safety-malformed-payload"
            }
        };

        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
        using var previewDoc = JsonDocument.Parse(preview);

        Assert.False(previewDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("protocol_error", previewDoc.RootElement.GetProperty("failureCategory").GetString());
        Assert.DoesNotContain("content\":\"", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE_TREE_SNAPSHOT_CONTENT", preview, StringComparison.Ordinal);
        Assert.False(previewDoc.RootElement.TryGetProperty("safetyToken", out _));
    }

    [Fact]
    public async Task WriteBatchTools_CreateBlock_OccupiedTargetContentDrift_InvalidatesTheToken()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
        var safety = new WriteSafetyService(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, "tree-safety-create-block-content-drift");

        var operations = new[]
        {
            new BatchOperationRequest
            {
                OperationId = "create",
                Operation = "create_block",
                BlockPath = "PLC_1/Blocks/Main/Mixer",
                BlockType = "FB",
                Language = "SCL",
                ProjectPath = "tree-safety-create-block-content-drift"
            }
        };

        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
        using var previewDoc = JsonDocument.Parse(preview);
        var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        var apply = await WriteBatchTools.ApplyWriteBatch(client, safety, operations, confirm: true, safetyToken: token);
        using var applyDoc = JsonDocument.Parse(apply);

        Assert.False(applyDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("state_changed", applyDoc.RootElement.GetProperty("failureCategory").GetString());
    }

    [Fact]
    public async Task WriteBatchTools_CreateBlockGroup_UnitScopedUnrelatedSiblingDrift_DoesNotInvalidateTheToken()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
        var safety = new WriteSafetyService(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, "tree-safety-unit-unrelated-sibling-drift");

        var operations = new[]
        {
            new BatchOperationRequest
            {
                OperationId = "create-group",
                Operation = "create_block_group",
                BlockPath = "PLC_1/Units/Line1/Blocks/Motion/AreaA",
                ProjectPath = "tree-safety-unit-unrelated-sibling-drift"
            }
        };

        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
        using var previewDoc = JsonDocument.Parse(preview);
        var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        var apply = await WriteBatchTools.ApplyWriteBatch(client, safety, operations, confirm: true, safetyToken: token);
        using var applyDoc = JsonDocument.Parse(apply);

        Assert.True(applyDoc.RootElement.GetProperty("success").GetBoolean(), apply);
    }
}
