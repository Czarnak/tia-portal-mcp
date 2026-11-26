using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Plc;
using TiaMcpServer.Safety;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Tests.Worker;
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

    private static PlcOperationRequest CreateTag(string id, string? plcName, string table = "Default tag table", string name = "Fresh") => new()
    {
        OperationId = id, Operation = "create_tag", PlcName = plcName, TableName = table, Name = name, DataType = "Bool",
    };

    private static PlcOperationRequest UpdateBlock(string id, string path, string hash) => new()
    {
        OperationId = id, Operation = "update_block_logic", BlockPath = path, Format = "xml",
        Content = "<Block changed=\"true\"/>", ExpectedContentHash = hash,
    };

    private static async Task<string> HashOf(OpennessWorkerClient client, string blockPath)
    {
        var read = await client.GetBlockContentAsync(blockPath, format: "xml");
        Assert.True(read.Success, read.Error);
        return ContentHashes.Compute("xml", read.Payload);
    }

    private const string Inventory = "list_tag_tables";
    private const string TreeRead = "browse_project_tree_v3_snapshot";

    private static PlcOperationRequest Blocks(string id, string operation, string path, string? blockType = null)
        => new() { OperationId = id, Operation = operation, BlockPath = path, BlockType = blockType };

    /// <summary>Plans <paramref name="items"/> and returns the worker methods planning issued.</summary>
    private static async Task<string[]> PlanningReadsAsync(Func<OpennessWorkerClient, Task<PlcOperationRequest[]>> items)
    {
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        using var log = new FakeWorkerRequestLog(directory.Path);
        using var client = await FakeWorkerClients.CreateBoundAsync(Scenario);
        var plan = await items(client);
        var before = log.Methods().Length;

        var result = await new PlcWritePlanner(client).PlanAsync(plan);

        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        return log.Methods().Skip(before).ToArray();
    }

    [Fact]
    public async Task CreatesReadOnlyThePlcInventory()
    {
        var reads = await PlanningReadsAsync(_ => Task.FromResult(new[]
        {
            Blocks("group", "create_block_group", "Station_1/Blocks/NewGroup"),
            Blocks("fc", "create_block", "Station_1/NewFC", "FC"),
            new PlcOperationRequest { OperationId = "table", Operation = "create_tag_table", PlcName = "PLC_2", TableName = "New" },
            CreateTag("tag", "Station_1"),
            CreateTag("tag2", "PLC_2"),
        }));

        Assert.Equal(new[] { Inventory }, reads);
    }

    [Fact]
    public async Task DeleteAndObCreateReadTheTreeOnceOnlyForTheirPlc()
    {
        var reads = await PlanningReadsAsync(_ => Task.FromResult(new[]
        {
            Blocks("block", "delete_block", "Station_1/Blocks/Locked"),
            Blocks("group", "delete_block_group", "Station_1/Blocks/Motors"),
            Blocks("ob", "create_block", "Station_1/Cycle2", "OB"),
            new PlcOperationRequest { OperationId = "tag", Operation = "delete_tag", PlcName = "PLC_2", TableName = "Default tag table", Name = "Start" },
        }));

        Assert.Equal(new[] { Inventory, TreeRead }, reads);
    }

    [Fact]
    public async Task ContentUpdateReadsOnlyItsExport()
    {
        var reads = await PlanningReadsAsync(async client => new[] { UpdateBlock("content", "Station_1/Main", await HashOf(client, "Station_1/Main")) });

        Assert.Equal(new[] { Inventory, "get_block_content" }, reads);
    }

    [Fact]
    public async Task XmlOnlySubmissionPlansOneLineDiff()
    {
        using var client = await FakeWorkerClients.CreateBoundAsync(Scenario);
        const string xml = "<Document>\r\n  <Network>A</Network>\r\n</Document>\r\n";
        var bundle = BlockBundleText.Compose(new[]
        {
            new BlockImportDocument("Main.xml", "Main.xml", xml),
            new BlockImportDocument("Main.s7dcl", "Main.s7dcl", "BLOCK Main\r\n"),
        });
        var seeded = await client.UpdateBlockLogicAsync("Station_1/Main", bundle, format: "xml",
            expectedContentHash: await HashOf(client, "Station_1/Main"));
        Assert.True(seeded.Success, seeded.Error);

        var result = await new PlcWritePlanner(client).PlanAsync(new[]
        {
            new PlcOperationRequest
            {
                OperationId = "xml-only", Operation = "update_block_logic", BlockPath = "Station_1/Main", Format = "xml",
                Content = xml.Replace("<Network>A</Network>", "<Network>B</Network>"),
                ExpectedContentHash = ContentHashes.Compute("xml", bundle),
            },
        });

        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        var diff = result.Plan.Items[0].Effect!.ContentDiff!;
        Assert.Equal(1, diff.CurrentChangedLineCount);
        Assert.Equal(1, diff.RequestedChangedLineCount);
    }

    [Fact]
    public async Task CreatesDoNotResolveTheirContainer()
    {
        using var client = await FakeWorkerClients.CreateBoundAsync(Scenario);

        var result = await new PlcWritePlanner(client).PlanAsync(new[]
        {
            Blocks("block", "create_block", "Station_1/Blocks/NoSuchGroup/NewFC", "FC"),
            Blocks("group", "create_block_group", "Station_1/Blocks/NoSuchGroup/Sub"),
            new PlcOperationRequest { OperationId = "table", Operation = "create_tag_table", PlcName = "PLC_2", FolderPath = "/NoSuchFolder", TableName = "New" },
        });

        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Equal("PLC_1/Blocks/NoSuchGroup/NewFC", result.Plan.Items[0].Effect!.Target.BlockPath);
        Assert.Equal("PLC_1/Blocks/NoSuchGroup", result.Plan.Items[1].Effect!.Placement);
        Assert.Equal("/NoSuchFolder", result.Plan.Items[2].Effect!.Placement);
    }

    [Fact]
    public async Task DeleteOfGroupCreatedEarlierPassesPreflightThenReplansFromRealState()
    {
        using var client = await FakeWorkerClients.CreateBoundAsync(Scenario);
        var planner = new PlcWritePlanner(client);
        var delete = Blocks("delete", "delete_block_group", "Station_1/Blocks/Motors/Fresh");

        var result = await planner.PlanAsync(new[] { Blocks("create", "create_block_group", "Station_1/Blocks/Motors/Fresh"), delete });

        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Equal("create", result.Plan.Items[1].DependsOn);
        Assert.Equal("create", result.Plan.Items[1].Effect!.DependsOn);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, (await planner.ReplanAsync(delete)).Replan.Error?.Category);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("plc_1")]
    public async Task OmittedPlcNameWithTwoPlcsIsAmbiguous(string? plcName)
    {
        using var client = await FakeWorkerClients.CreateBoundAsync(Scenario);

        var result = await new PlcWritePlanner(client).PlanAsync(new[] { CreateTag("a", "PLC_2"), CreateTag("b", plcName) });

        Assert.False(result.Plan.Success);
        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, result.Plan.Error!.Category);
        Assert.Empty(result.Guards);
    }

    [Fact]
    public async Task StaleContentHashIsStateChangedBeforeAnyWrite()
    {
        using var client = await FakeWorkerClients.CreateBoundAsync(Scenario);
        var stale = ContentHashes.Compute("xml", "<Block edited-elsewhere=\"true\"/>");

        var result = await new PlcWritePlanner(client).PlanAsync(new[]
        {
            CreateTag("first", "PLC_2"),
            UpdateBlock("content", "Station_1/Main", stale),
        });

        Assert.False(result.Plan.Success);
        Assert.Equal(WorkerFailureCategories.StateChanged, result.Plan.Error!.Category);
        Assert.Contains("content", result.Plan.Error.Message);
        var inventory = await client.ListTagTablesAsync(plcName: "PLC_2");
        Assert.DoesNotContain("Fresh", inventory.Payload);
    }

    [Fact]
    public async Task UnreadableOrEmptyContentExportFiresUnverifiable()
    {
        using var client = await FakeWorkerClients.CreateBoundAsync(Scenario);
        var anyHash = ContentHashes.Compute("xml", "anything");

        var result = await new PlcWritePlanner(client).PlanAsync(new[]
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
        using var client = await FakeWorkerClients.CreateBoundAsync(incomplete);
        var items = new[] { CreateTag("hidden", "PLC_9"), CreateTag("known", "PLC_2") };

        var result = await new PlcWritePlanner(client).PlanAsync(items);

        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Contains(result.Guards["hidden"], guard => guard.Id == PlcGuardDefinitions.StateUnverifiable);
        Assert.Equal("PLC_9", result.Plan.Items[0].Effect!.Target.PlcName);
        Assert.Contains(result.Guards["known"], guard => guard.Id == PlcGuardDefinitions.StateUnverifiable);
    }

    [Fact]
    public async Task MissingTargetIsTargetNotFound()
    {
        using var client = await FakeWorkerClients.CreateBoundAsync(Scenario);
        var planner = new PlcWritePlanner(client);

        var tag = await planner.PlanAsync(new[]
        {
            new PlcOperationRequest { OperationId = "d", Operation = "delete_tag", PlcName = "PLC_2", TableName = "Default tag table", Name = "Nope" },
        });
        var block = await planner.PlanAsync(new[] { UpdateBlock("u", "Station_1/Blocks/Nope", ContentHashes.Compute("xml", "x")) });

        Assert.Equal(WorkerFailureCategories.TargetNotFound, tag.Plan.Error?.Category);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, block.Plan.Error?.Category);
    }

    [Fact]
    public async Task RootLevelBlockPathResolves()
    {
        using var client = await FakeWorkerClients.CreateBoundAsync(Scenario);
        var hash = await HashOf(client, "Station_1/Main");

        var result = await new PlcWritePlanner(client).PlanAsync(new[]
        {
            UpdateBlock("content", "Station_1/Main", hash),
            new PlcOperationRequest { OperationId = "create", Operation = "create_block", BlockPath = "Station_1/NewFC", BlockType = "FC" },
            Blocks("delete", "delete_block", "Station_1/Locked"),
        });

        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        var content = result.Plan.Items[0];
        // The export resolves a content target, so its identity keeps the caller's undetermined path.
        Assert.Equal("PLC_1/Main", content.Effect!.Target.BlockPath);
        Assert.Equal("PLC_1/Blocks/Locked", result.Plan.Items[2].Effect!.Target.BlockPath);
        Assert.NotNull(content.Effect.ContentDiff);
        Assert.Contains(content.Preconditions, p => p is { Name: "contentHash", Satisfied: true });
        Assert.Equal("PLC_1/Blocks", result.Plan.Items[1].Effect!.Placement);
        Assert.Empty(result.Guards["create"]);
    }

    [Fact]
    public async Task DependentPlanKeepsEffectAndDependsOn()
    {
        using var client = await FakeWorkerClients.CreateBoundAsync(Scenario);
        var planner = new PlcWritePlanner(client);
        var table = new PlcOperationRequest { OperationId = "t1", Operation = "create_tag_table", PlcName = "PLC_2", TableName = "New" };
        var tag = CreateTag("t2", "PLC_2", "New");

        var result = await planner.PlanAsync(new[] { table, tag });

        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Null(result.Plan.Items[0].DependsOn);
        var dependent = result.Plan.Items[1];
        Assert.Equal("t1", dependent.DependsOn);
        Assert.Equal("t1", dependent.Effect!.DependsOn);
        Assert.Equal("PLC_2", dependent.Effect.Target.PlcName);
        Assert.Equal("PLC_1", dependent.Effect.Target.DeviceName);

        // The late re-plan reads real state, where the table was never created.
        var replan = await planner.ReplanAsync(tag);
        Assert.False(replan.Replan.Success);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, replan.Replan.Error!.Category);
    }

    private static PlcOperationRequest PatchBlock(string id, string hash, string oldText = "<Network>A</Network>",
        string newText = "<Network>B</Network>", string? format = null, string path = "Station_1/Main") => new()
    {
        OperationId = id, Operation = "patch_block_logic", BlockPath = path, Format = format,
        ExpectedContentHash = hash, Edits = new[] { new PlcTextEdit { OldText = oldText, NewText = newText } },
    };

    private const string PatchXml = "<Document>\r\n  <Network>A</Network>\r\n</Document>\r\n";

    private static async Task<string> SeedPatchBundle(OpennessWorkerClient client)
    {
        var bundle = BlockBundleText.Compose(new[]
        {
            new BlockImportDocument("Main.xml", "Main.xml", PatchXml),
            new BlockImportDocument("Main.s7dcl", "Main.s7dcl", "BLOCK Main\r\n"),
        });
        var seeded = await client.UpdateBlockLogicAsync("Station_1/Main", bundle, format: "xml",
            expectedContentHash: await HashOf(client, "Station_1/Main"));
        Assert.True(seeded.Success, seeded.Error);
        return bundle;
    }

    [Fact]
    public async Task PatchXmlBundle_UsesWholeExportHashAndOneExport_AndResolvesXmlOnly()
    {
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        using var log = new FakeWorkerRequestLog(directory.Path);
        using var client = await FakeWorkerClients.CreateBoundAsync(Scenario);
        var bundle = await SeedPatchBundle(client);
        var hash = await HashOf(client, "Station_1/Main");
        var request = PatchBlock("patch", hash);
        var before = log.Methods().Length;

        var result = await new PlcWritePlanner(client).PlanAsync(new[] { request });

        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Equal(new[] { Inventory, "get_block_content" }, log.Methods().Skip(before));
        var effect = Assert.Single(result.Plan.Items).Effect!;
        Assert.Equal("patch_block_logic", effect.Operation);
        Assert.Equal(1, effect.ContentDiff!.CurrentChangedLineCount);
        Assert.Equal(1, effect.ContentDiff.RequestedChangedLineCount);
        var precondition = Assert.Single(result.Plan.Items[0].Preconditions);
        Assert.Equal(ContentHashes.Compute("xml", bundle), precondition.Actual);
        Assert.Equal(hash, precondition.Expected);
        Assert.True(precondition.Satisfied);
        var execution = Assert.Single(result.ResolvedOperations).Value;
        Assert.Equal("patch", execution.OperationId);
        Assert.Equal("update_block_logic", execution.Operation);
        Assert.Equal("Station_1/Main", execution.BlockPath);
        Assert.Equal("xml", execution.Format);
        Assert.Equal(hash, execution.ExpectedContentHash);
        Assert.Equal(PatchXml.Replace("<Network>A</Network>", "<Network>B</Network>"), execution.Content);
        Assert.Null(execution.Edits);
        Assert.Null(request.Content);
        Assert.Null(request.Format);
        Assert.Equal("patch_block_logic", request.Operation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PatchHashCheckPrecedesAnchorLookup_AndRejectsXmlPartHash(bool xmlPartHash)
    {
        using var client = await FakeWorkerClients.CreateBoundAsync(Scenario);
        await SeedPatchBundle(client);
        var hash = ContentHashes.Compute("xml", xmlPartHash ? PatchXml : "stale");

        var result = await new PlcWritePlanner(client).PlanAsync(new[]
        {
            CreateTag("first", "PLC_2"), PatchBlock("patch", hash, oldText: "absent anchor"),
        });

        Assert.False(result.Plan.Success);
        Assert.Equal(WorkerFailureCategories.StateChanged, result.Plan.Error!.Category);
        Assert.Contains("patch", result.Plan.Error.Message);
        Assert.Empty(result.ResolvedOperations);
        Assert.DoesNotContain("Fresh", (await client.ListTagTablesAsync(plcName: "PLC_2")).Payload);
    }

    [Theory]
    [InlineData("absent anchor")]
    [InlineData("Network")]
    public async Task PatchInvalidAnchorOnCurrentBase_RejectsWholePlanWithoutCreatingEarlierItem(string anchor)
    {
        using var client = await FakeWorkerClients.CreateBoundAsync(Scenario);
        await SeedPatchBundle(client);

        var result = await new PlcWritePlanner(client).PlanAsync(new[]
        {
            CreateTag("first", "PLC_2"), PatchBlock("patch", await HashOf(client, "Station_1/Main"), oldText: anchor),
        });

        Assert.False(result.Plan.Success);
        Assert.Equal(WorkerFailureCategories.ValidationError, result.Plan.Error!.Category);
        Assert.Contains("patch", result.Plan.Error.Message);
        Assert.Contains("edits[0]", result.Plan.Error.Message);
        Assert.Empty(result.ResolvedOperations);
        Assert.DoesNotContain("Fresh", (await client.ListTagTablesAsync(plcName: "PLC_2")).Payload);
    }

    [Fact]
    public async Task PatchSource_ResolvesWholeSourceAndExplicitFormat()
    {
        using var client = await FakeWorkerClients.CreateBoundAsync(Scenario);
        const string source = "ORGANIZATION_BLOCK Main\r\nBEGIN\r\n  x := FALSE;\r\nEND_ORGANIZATION_BLOCK\r\n";
        var current = await client.GetBlockContentAsync("Station_1/Main", format: "source");
        Assert.True(current.Success, current.Error);
        var seed = await client.UpdateBlockLogicAsync("Station_1/Main", source, format: "source",
            expectedContentHash: ContentHashes.Compute("source", current.Payload));
        Assert.True(seed.Success, seed.Error);

        var result = await new PlcWritePlanner(client).PlanAsync(new[]
        {
            PatchBlock("source", ContentHashes.Compute("source", source), "x := FALSE;", "x := TRUE;", "source"),
        });

        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        var resolved = result.ResolvedOperations["source"];
        Assert.Equal("source", resolved.Format);
        Assert.Equal(source.Replace("x := FALSE;", "x := TRUE;"), resolved.Content);
        Assert.Equal("patch_block_logic", result.Plan.Items[0].Effect!.Operation);
    }

    [Fact]
    public async Task PatchUnreadableOrEmptyExport_FiresGuardWithoutRunnableResolution()
    {
        using var client = await FakeWorkerClients.CreateBoundAsync(Scenario);
        var result = await new PlcWritePlanner(client).PlanAsync(new[]
        {
            PatchBlock("empty", ContentHashes.Compute("xml", "x"), path: "Station_1/Blocks/Empty_DB"),
            PatchBlock("locked", ContentHashes.Compute("xml", "x"), path: "Station_1/Blocks/Locked"),
        });

        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Empty(result.ResolvedOperations);
        foreach (var id in new[] { "empty", "locked" })
            Assert.Contains(result.Guards[id], g => g.Id == PlcGuardDefinitions.StateUnverifiable);
        Assert.All(result.Plan.Items, item => Assert.Null(item.Effect!.ContentDiff));
        Assert.All(result.Plan.Items, item => Assert.Empty(item.Preconditions));
    }

    [Fact]
    public async Task PatchIncompleteInventoryUnknownPlc_IsGuardWithoutRunnableResolution()
    {
        using var client = await FakeWorkerClients.CreateBoundAsync("plc-write-incomplete");
        var result = await new PlcWritePlanner(client).PlanAsync(new[]
        {
            PatchBlock("hidden", ContentHashes.Compute("xml", "x"), path: "PLC_9/Blocks/Main"),
        });

        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Contains(result.Guards["hidden"], g => g.Id == PlcGuardDefinitions.StateUnverifiable);
        Assert.Empty(result.ResolvedOperations);
    }

    [Fact]
    public async Task PatchIncompleteInventoryKnownPlcAndReadableExport_IsGuardWithoutRunnableResolution()
    {
        using var client = await FakeWorkerClients.CreateBoundAsync("plc-write-incomplete");
        var bundle = await SeedPatchBundle(client);
        var result = await new PlcWritePlanner(client).PlanAsync(new[]
        {
            PatchBlock("known", ContentHashes.Compute("xml", bundle)),
        });

        Assert.True(result.Plan.Success, result.Plan.Error?.Message);
        Assert.Equal("patch_block_logic", Assert.Single(result.Plan.Items).Effect!.Operation);
        Assert.Contains(result.Guards["known"], g => g.Id == PlcGuardDefinitions.StateUnverifiable);
        Assert.Empty(result.ResolvedOperations);
    }

    [Theory]
    [InlineData("Station_1/Blocks/Missing", WorkerFailureCategories.TargetNotFound)]
    [InlineData("PLC_1/Main", WorkerFailureCategories.TargetAmbiguous)]
    public async Task PatchTargetLookup_PreservesCategoryAndCannotRun(string path, string category)
    {
        using var client = await FakeWorkerClients.CreateBoundAsync(Scenario);
        var result = await new PlcWritePlanner(client).PlanAsync(new[]
        {
            PatchBlock("patch", ContentHashes.Compute("xml", "x"), path: path),
        });

        Assert.False(result.Plan.Success);
        Assert.Equal(category, result.Plan.Error!.Category);
        Assert.Empty(result.ResolvedOperations);
    }

    [Fact]
    public async Task PatchLateReplan_ExportsOnceAndReturnsFreshExecutionOrStateChanged()
    {
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        using var log = new FakeWorkerRequestLog(directory.Path);
        using var client = await FakeWorkerClients.CreateBoundAsync(Scenario);
        await SeedPatchBundle(client);
        var hash = await HashOf(client, "Station_1/Main");
        var patch = PatchBlock("patch", hash);
        var planner = new PlcWritePlanner(client);
        var initial = await planner.PlanAsync(new[] { CreateTag("touch", "Station_1", name: "Main"), patch });
        Assert.True(initial.Plan.Success, initial.Plan.Error?.Message);
        Assert.Equal("touch", initial.Plan.Items[1].DependsOn);
        var before = log.Methods().Length;

        var replan = await planner.ReplanAsync(patch);

        Assert.True(replan.Replan.Success, replan.Replan.Error?.Message);
        Assert.Equal(new[] { Inventory, "get_block_content" }, log.Methods().Skip(before));
        Assert.NotSame(initial.ResolvedOperations["patch"], replan.ResolvedOperation);
        Assert.Equal(initial.ResolvedOperations["patch"].Content, replan.ResolvedOperation!.Content);
        Assert.Null(replan.Replan.Plan!.DependsOn);
        var drift = await client.UpdateBlockLogicAsync("Station_1/Main", "<Document>changed externally</Document>",
            format: "xml", expectedContentHash: hash);
        Assert.True(drift.Success, drift.Error);
        before = log.Methods().Length;

        var failed = await planner.ReplanAsync(patch);

        Assert.False(failed.Replan.Success);
        Assert.Equal(WorkerFailureCategories.StateChanged, failed.Replan.Error!.Category);
        Assert.Null(failed.ResolvedOperation);
        Assert.Equal(new[] { Inventory, "get_block_content" }, log.Methods().Skip(before));
    }
}
