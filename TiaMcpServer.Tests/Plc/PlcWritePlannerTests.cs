using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Plc;
using TiaMcpServer.Safety;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

/// <summary>
/// Planner against the stateful <c>plc-write-roundtrip</c> FakeWorker: a root PLC
/// (software PLC_1, device Station_1) and a grouped PLC (software PLC_2, device PLC_1).
/// </summary>
[Collection(RealWorkerProcessCollection.Name)]
public class PlcWritePlannerTests
{
    private const string Scenario = "plc-write-roundtrip";

    private static OpennessWorkerClient CreateClient()
        => new(
            new ProjectSessionBinding(null),
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(McpAccessMode.ReadWrite));

    private static PlcOperationRequest CreateTag(string id, string? plcName, string table = "Default tag table", string name = "Fresh") => new()
    {
        OperationId = id, Operation = "create_tag", PlcName = plcName, TableName = table, Name = name, DataType = "Bool",
        ProjectPath = Scenario,
    };

    private static PlcOperationRequest UpdateBlock(string id, string path, string hash) => new()
    {
        OperationId = id, Operation = "update_block_logic", BlockPath = path, Format = "xml",
        Content = "<Block changed=\"true\"/>", ExpectedContentHash = hash, ProjectPath = Scenario,
    };

    private static async Task<string> HashOf(OpennessWorkerClient client, string blockPath)
    {
        var read = await client.GetBlockContentAsync(blockPath, Scenario, "xml");
        Assert.True(read.Success, read.Error);
        return ContentHashes.Compute("xml", read.Payload);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("plc_1")]
    public async Task OmittedPlcNameWithTwoPlcsIsAmbiguous(string? plcName)
    {
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath(Scenario);
        using var client = CreateClient();

        var result = await new PlcWritePlanner(client).PlanAsync(Scenario, new[] { CreateTag("a", "PLC_2"), CreateTag("b", plcName) });

        Assert.False(result.Plan.Success);
        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, result.Plan.Error!.Category);
        Assert.Empty(result.Guards);
    }

    [Fact]
    public async Task StaleContentHashIsStateChangedBeforeAnyWrite()
    {
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath(Scenario);
        using var client = CreateClient();
        var stale = ContentHashes.Compute("xml", "<Block edited-elsewhere=\"true\"/>");

        var result = await new PlcWritePlanner(client).PlanAsync(Scenario, new[]
        {
            CreateTag("first", "PLC_2"),
            UpdateBlock("content", "Station_1/Main", stale),
        });

        Assert.False(result.Plan.Success);
        Assert.Equal(WorkerFailureCategories.StateChanged, result.Plan.Error!.Category);
        Assert.Contains("content", result.Plan.Error.Message);
        var inventory = await client.ListTagTablesAsync("PLC_2", Scenario);
        Assert.DoesNotContain("Fresh", inventory.Payload);
    }

    [Fact]
    public async Task UnreadableOrEmptyContentExportFiresUnverifiable()
    {
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath(Scenario);
        using var client = CreateClient();
        var anyHash = ContentHashes.Compute("xml", "anything");

        var result = await new PlcWritePlanner(client).PlanAsync(Scenario, new[]
        {
            UpdateBlock("empty", "Station_1/Blocks/Empty_DB", anyHash),
            UpdateBlock("locked", "Station_1/Blocks/Locked", anyHash),
        });

        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        foreach (var id in new[] { "empty", "locked" })
        {
            Assert.Contains(result.Guards[id], guard => guard.Id == PlcGuardDefinitions.StateUnverifiable);
        }

        Assert.All(result.Plan.Items, item => Assert.Null(item.Effect!.ContentDiff));
        Assert.All(result.Plan.Items, item => Assert.DoesNotContain(item.Preconditions, p => p.Name == ContentHashes.PreconditionName));
    }

    [Fact]
    public async Task IncompleteInventoryUnmatchedPlcIsGuardNotError()
    {
        const string incomplete = "plc-write-incomplete";
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath(incomplete);
        using var client = CreateClient();
        var items = new[] { CreateTag("hidden", "PLC_9"), CreateTag("known", "PLC_2") };
        foreach (var item in items) item.ProjectPath = incomplete;

        var result = await new PlcWritePlanner(client).PlanAsync(incomplete, items);

        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Contains(result.Guards["hidden"], guard => guard.Id == PlcGuardDefinitions.StateUnverifiable);
        Assert.Equal("PLC_9", result.Plan.Items[0].Effect!.Target.PlcName);
        Assert.Contains(result.Guards["known"], guard => guard.Id == PlcGuardDefinitions.StateUnverifiable);
    }

    [Fact]
    public async Task MissingTargetIsTargetNotFound()
    {
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath(Scenario);
        using var client = CreateClient();
        var planner = new PlcWritePlanner(client);

        var tag = await planner.PlanAsync(Scenario, new[]
        {
            new PlcOperationRequest { OperationId = "d", Operation = "delete_tag", PlcName = "PLC_2", TableName = "Default tag table", Name = "Nope", ProjectPath = Scenario },
        });
        var block = await planner.PlanAsync(Scenario, new[] { UpdateBlock("u", "Station_1/Blocks/Nope", ContentHashes.Compute("xml", "x")) });

        Assert.Equal(WorkerFailureCategories.TargetNotFound, tag.Plan.Error?.Category);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, block.Plan.Error?.Category);
    }

    [Fact]
    public async Task RootLevelBlockPathResolves()
    {
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath(Scenario);
        using var client = CreateClient();
        var hash = await HashOf(client, "Station_1/Main");

        var result = await new PlcWritePlanner(client).PlanAsync(Scenario, new[]
        {
            UpdateBlock("content", "Station_1/Main", hash),
            new PlcOperationRequest { OperationId = "create", Operation = "create_block", BlockPath = "Station_1/NewFC", BlockType = "FC", ProjectPath = Scenario },
        });

        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        var content = result.Plan.Items[0];
        Assert.Equal("PLC_1/Blocks/Main", content.Effect!.Target.BlockPath);
        Assert.NotNull(content.Effect.ContentDiff);
        Assert.Contains(content.Preconditions, p => p is { Name: "contentHash", Satisfied: true });
        Assert.Equal("PLC_1/Blocks", result.Plan.Items[1].Effect!.Placement);
        Assert.Empty(result.Guards["create"]);
    }

    [Fact]
    public async Task DependentPlanKeepsEffectAndDependsOn()
    {
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath(Scenario);
        using var client = CreateClient();
        var planner = new PlcWritePlanner(client);
        var table = new PlcOperationRequest { OperationId = "t1", Operation = "create_tag_table", PlcName = "PLC_2", TableName = "New", ProjectPath = Scenario };
        var tag = CreateTag("t2", "PLC_2", "New");

        var result = await planner.PlanAsync(Scenario, new[] { table, tag });

        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Null(result.Plan.Items[0].DependsOn);
        var dependent = result.Plan.Items[1];
        Assert.Equal("t1", dependent.DependsOn);
        Assert.Equal("t1", dependent.Effect!.DependsOn);
        Assert.Equal("PLC_2", dependent.Effect.Target.PlcName);
        Assert.Equal("PLC_1", dependent.Effect.Target.DeviceName);

        // The late re-plan reads real state, where the table was never created.
        var replan = await planner.ReplanAsync(Scenario, tag);
        Assert.False(replan.Replan.Success);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, replan.Replan.Error!.Category);
    }
}
