using System.Text.Json;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class ProjectTreeSafetyDedupTests
{
    private const string ProjectPath = "tree-safety-dedup";

    [Fact]
    public async Task ReadCurrentStatesForTestingAsync_ExpandsTwoIdenticalSelectorsBackIntoOrderedOperationStates()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, ProjectPath);
        var operations = new[] { Operation("first", "create_block_group"), Operation("second", "create_block_group") };

        var snapshot = await WriteBatchTools.ReadCurrentStatesForTestingAsync(client, operations);
        Assert.Null(snapshot.Error);
        Assert.Equal(new[] { "first", "second" }, snapshot.States.Select(state => state.OperationId));
        Assert.Equal(new[] { "create_block_group", "create_block_group" }, snapshot.States.Select(state => state.Operation));
        Assert.Equal(snapshot.States[0].CurrentState, snapshot.States[1].CurrentState);
        var payload = snapshot.States[0].CurrentState;
        Assert.Equal($"first::create_block_group\n{payload}\n--- batch item ---\nsecond::create_block_group\n{payload}",
            snapshot.CombinedState);
        var reversed = await WriteBatchTools.ReadCurrentStatesForTestingAsync(client, operations.Reverse().ToArray());
        Assert.Null(reversed.Error);
        Assert.Equal($"second::create_block_group\n{payload}\n--- batch item ---\nfirst::create_block_group\n{payload}",
            reversed.CombinedState);
        Assert.NotEqual(snapshot.CombinedState, reversed.CombinedState);
        var single = await WriteBatchTools.ReadCurrentStatesForTestingAsync(client, operations.Take(1).ToArray());
        Assert.Null(single.Error);
        Assert.Equal($"first::create_block_group\n{payload}", single.CombinedState);
        Assert.NotEqual(snapshot.CombinedState, single.CombinedState);
        Assert.Equal(3, (await ProbeAsync(client, "preview"))["read_create_block_group_safety_snapshot.preview"]);
    }

    [Theory]
    [InlineData("path")]
    [InlineData("path-case")]
    [InlineData("unit")]
    [InlineData("blockType")]
    [InlineData("language")]
    [InlineData("obEventClass")]
    [InlineData("projectPath")]
    public async Task WriteBatchTools_DifferentExactSelectors_ReadEachSelectorInEachPhase(string changedField)
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, ProjectPath);
        var safety = new WriteSafetyService(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);
        var first = Operation("first", "create_block");
        var second = Operation("second", "create_block");
        switch (changedField)
        {
            case "path": second.BlockPath = "PLC_1/Blocks/Main/AreaB"; break;
            case "path-case": second.BlockPath = "PLC_1/Blocks/Main/areaa"; break;
            case "unit": second.BlockPath = "PLC_1/Units/Line1/Blocks/Main/AreaA"; break;
            case "blockType": second.BlockType = "FC"; break;
            case "language": second.Language = "LAD"; break;
            case "obEventClass": second.ObEventClass = "ProgramCycle"; break;
            case "projectPath": second.ProjectPath = null; break;
        }
        var operations = new[] { first, second };
        using var preview = JsonDocument.Parse(await WriteBatchTools.PreviewWriteBatch(client, safety, operations));
        var token = preview.RootElement.GetProperty("safetyToken").GetString();
        var afterPreview = await ProbeAsync(client, "apply");
        var apply = await WriteBatchTools.ApplyWriteBatch(client, safety, operations, true, token);
        using var applyDoc = JsonDocument.Parse(apply);
        Assert.True(applyDoc.RootElement.GetProperty("success").GetBoolean(), apply);
        var afterApply = await ProbeAsync(client, "apply");
        Assert.Equal(2, afterPreview["read_create_block_safety_snapshot.preview"]);
        Assert.Equal(2, afterApply["read_create_block_safety_snapshot.apply"]);
    }

    [Fact]
    public async Task WriteBatchTools_MixedOperations_PreserveEachRouteAndDoNotDeduplicateOtherReads()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, ProjectPath);
        var safety = new WriteSafetyService(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);
        var operations = new[]
        {
            Operation("create", "create_block"), Operation("group", "create_block_group"),
            Operation("delete", "delete_block_group"), Operation("again", "create_block_group"),
            Operation("block1", "delete_block"), Operation("block2", "delete_block")
        };
        using var preview = JsonDocument.Parse(await WriteBatchTools.PreviewWriteBatch(client, safety, operations));
        var token = preview.RootElement.GetProperty("safetyToken").GetString();
        await ProbeAsync(client, "apply");
        var apply = await WriteBatchTools.ApplyWriteBatch(client, safety, operations, true, token);
        using var applyDoc = JsonDocument.Parse(apply);
        Assert.True(applyDoc.RootElement.GetProperty("success").GetBoolean(), apply);
        var counters = await ProbeAsync(client, "apply");
        foreach (var phase in new[] { "preview", "apply" })
        {
            Assert.Equal(1, counters["read_create_block_safety_snapshot." + phase]);
            Assert.Equal(1, counters["read_create_block_group_safety_snapshot." + phase]);
            Assert.Equal(1, counters["read_delete_block_group_safety_snapshot." + phase]);
            Assert.Equal(2, counters["get_block_content." + phase]);
        }
    }

    [Fact]
    public async Task WriteBatchTools_RepeatedPreview_PerformsFreshRead()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, ProjectPath);
        var safety = new WriteSafetyService(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);
        var operations = new[] { Operation("first", "create_block_group"), Operation("second", "create_block_group") };
        using var first = JsonDocument.Parse(await WriteBatchTools.PreviewWriteBatch(client, safety, operations));
        Assert.Equal(1, (await ProbeAsync(client, "preview"))["read_create_block_group_safety_snapshot.preview"]);
        using var second = JsonDocument.Parse(await WriteBatchTools.PreviewWriteBatch(client, safety, operations));
        Assert.Equal(2, (await ProbeAsync(client, "apply"))["read_create_block_group_safety_snapshot.preview"]);
        var apply = await WriteBatchTools.ApplyWriteBatch(client, safety, operations, true,
            second.RootElement.GetProperty("safetyToken").GetString());
        using var applyDoc = JsonDocument.Parse(apply);
        Assert.True(applyDoc.RootElement.GetProperty("success").GetBoolean(), apply);
        Assert.Equal(1, (await ProbeAsync(client, "apply"))["read_create_block_group_safety_snapshot.apply"]);
    }

    [Theory]
    [InlineData("create_block")]
    [InlineData("create_block_group")]
    [InlineData("delete_block_group")]
    public async Task WriteBatchTools_DeduplicatesOnePreviewReadButPerformsAFreshApplyRead(string operation)
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding);
        var safety = new WriteSafetyService(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, ProjectPath);
        var operations = new[] { Operation("first", operation), Operation("second", operation) };

        using var preview = JsonDocument.Parse(await WriteBatchTools.PreviewWriteBatch(client, safety, operations));
        var token = preview.RootElement.GetProperty("safetyToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal(new[] { "first", "second" }, preview.RootElement.GetProperty("target").EnumerateArray()
            .Select(target => target.GetProperty("operationId").GetString()));
        var afterPreview = await ProbeAsync(client, "apply");

        var apply = await WriteBatchTools.ApplyWriteBatch(client, safety, operations, confirm: true, safetyToken: token);
        using var applyDoc = JsonDocument.Parse(apply);
        Assert.True(applyDoc.RootElement.GetProperty("success").GetBoolean(), apply);
        var afterApply = await ProbeAsync(client, "apply");

        var method = "read_" + operation + "_safety_snapshot";
        Assert.Equal(new[] { 1, 1 }, new[]
        {
            afterPreview.GetValueOrDefault(method + ".preview"),
            afterApply.GetValueOrDefault(method + ".apply")
        });
        Assert.Equal(0, afterPreview.GetValueOrDefault(method + ".apply"));
        Assert.Equal(1, afterApply.GetValueOrDefault(method + ".preview"));
        Assert.Equal(1, afterApply.GetValueOrDefault(method + ".apply"));
        Assert.Equal(2, afterApply.GetValueOrDefault(operation + ".apply"));
    }

    private static OpennessWorkerClient CreateClient(ProjectSessionBinding binding)
        => new(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());

    private static BatchOperationRequest Operation(string id, string operation) => new()
    {
        OperationId = id, Operation = operation, ProjectPath = ProjectPath,
        BlockPath = "PLC_1/Blocks/Main/AreaA", BlockType = operation == "create_block" ? "FB" : null,
        Language = operation == "create_block" ? "SCL" : null
    };

    private static async Task<Dictionary<string, int>> ProbeAsync(OpennessWorkerClient client, string nextPhase)
    {
        var response = await client.ReadCrossReferencesAsync(ProjectPath, nextPhase, filter: null);
        Assert.True(response.Success, response.Error);
        return JsonSerializer.Deserialize<Dictionary<string, int>>(response.Payload)!;
    }
}
