using TiaMcpServer.Contracts;
using TiaMcpServer.Plc;
using TiaMcpServer.ProjectTree;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

public class PlcWorkingStateTests
{
    private const string Device = "Station_1";
    private const string Plc = "PLC_1";

    private static PlcTagInventoryPlcInfo Inventory() => new()
    {
        PlcName = Plc,
        DeviceName = Device,
        Tables =
        {
            new TagTableInfo
            {
                Name = "Default tag table", FolderPath = "/", IsDefault = true,
                Tags = { new TagInfo { Name = "Start", DataType = "Bool", LogicalAddress = "%I0.0", ExternalAccessible = true, ExternalVisible = true, ExternalWritable = true } },
                UserConstants = { new UserConstantInfo { Name = "MaxSpeed", DataType = "Int", Value = "100" } },
            },
            new TagTableInfo
            {
                Name = "Motors", FolderPath = "/Line",
                Tags = { new TagInfo { Name = "Motor1", DataType = "Bool", LogicalAddress = "%Q0.0" } },
                UserConstants = { new UserConstantInfo { Name = "Unreadable", DataType = "Int", Value = null } },
            },
        },
    };

    private static ProjectTreeNode Node(string type, string name, params ProjectTreeNode[] children)
        => new() { NodeType = type, Name = name, Children = children.ToList() };

    private static ProjectTreeNode Block(string type, string name, string language, bool system = false) => new()
    {
        NodeType = type, Name = name, Children = new(),
        Details = system
            ? new() { ["Number"] = "1", ["ProgrammingLanguage"] = language, ["IsSystemBlock"] = "true" }
            : new() { ["Number"] = "1", ["ProgrammingLanguage"] = language },
    };

    private static ProjectTreeObservation Tree(params ProjectTreeSkippedNodeInfo[] skipped) => new(
        @"C:\Projects\Fake.ap21",
        new[] { Seg(ProjectTreeNodeTypes.Device, Device), Seg(ProjectTreeNodeTypes.PlcSoftware, Plc) },
        null,
        new[]
        {
            Node(ProjectTreeNodeTypes.PlcSoftware, Plc,
                Node(ProjectTreeNodeTypes.BlockFolder, "Program blocks",
                    Block(ProjectTreeNodeTypes.Ob, "Main", "LAD"),
                    Node(ProjectTreeNodeTypes.BlockFolder, "Motors",
                        Block(ProjectTreeNodeTypes.Fb, "FB_Motor", "SCL"),
                        Node(ProjectTreeNodeTypes.BlockFolder, "Legacy", Block(ProjectTreeNodeTypes.Fc, "FC_Old", "LAD"))),
                    Node(ProjectTreeNodeTypes.SystemBlockFolder, "System blocks", Block(ProjectTreeNodeTypes.Fb, "TCON", "STL", system: true))),
                Node(ProjectTreeNodeTypes.TagTableFolder, "PLC tags",
                    Node(ProjectTreeNodeTypes.TagTableFolder, "Line", Node(ProjectTreeNodeTypes.TagTable, "Motors")),
                    Node(ProjectTreeNodeTypes.TagTable, "Default tag table")),
                Node(ProjectTreeNodeTypes.TypeFolder, "PLC data types")),
        },
        skipped,
        Array.Empty<string>());

    private static ProjectTreeSelectorSegment Seg(string type, string name) => new() { NodeType = type, Name = name };

    private static PlcWorkingState State(bool complete = true, params ProjectTreeSkippedNodeInfo[] skipped)
        => PlcWorkingState.Create(Inventory(), complete, Tree(skipped));

    private static PlcOperationRequest Tag(string id, string operation, string name, string table = "Default tag table", string? folder = null) => new()
    {
        OperationId = id, Operation = operation, Name = name, TableName = table, FolderPath = folder,
        DataType = operation is "create_tag" or "create_user_constant" ? "Bool" : null,
        Value = operation == "create_user_constant" ? "1" : null,
    };

    private static PlcOperationRequest Blocks(string id, string operation, string path, string? blockType = null)
        => new() { OperationId = id, Operation = operation, BlockPath = path, BlockType = blockType };

    /// <summary>Runs the planner's overlay loop: resolve, record the dependency, apply.</summary>
    private static List<(PlcResolution Resolution, string? DependsOn)> Run(PlcWorkingState state, params PlcOperationRequest[] items)
    {
        var results = new List<(PlcResolution, string?)>();
        foreach (var item in items)
        {
            var resolution = state.Resolve(item);
            Assert.Null(resolution.Error);
            results.Add((resolution, state.LastTouchedBy(resolution.Effect!.Target)));
            state.Apply(item);
        }

        return results;
    }

    private static IEnumerable<string> GuardIds(PlcResolution resolution) => resolution.Guards.Select(guard => guard.Id);

    [Fact]
    public void CreateTableThenTagDependsOnTable()
    {
        var results = Run(State(),
            new PlcOperationRequest { OperationId = "t1", Operation = "create_tag_table", TableName = "New" },
            Tag("t2", "create_tag", "Fresh", "New"));

        Assert.Null(results[0].DependsOn);
        Assert.Equal("t1", results[1].DependsOn);
        Assert.Empty(results[1].Resolution.Guards);
    }

    [Fact]
    public void CreateThenUpdateTagDependsOnCreate()
    {
        var update = Tag("u", "update_tag", "Fresh");
        update.DataType = "Int";
        var results = Run(State(), Tag("c", "create_tag", "Fresh"), update);

        Assert.Equal("c", results[1].DependsOn);
        Assert.Contains(results[1].Resolution.Effect!.Changes, change => change is { Field: "dataType", Current: "Bool", Requested: "Int" });
    }

    [Fact]
    public void RenameThenUseNewNameDependsOnRename()
    {
        var state = State();
        var use = Tag("use", "update_tag", "Go");
        use.DataType = "Int";
        Assert.Equal("target_not_found", state.Resolve(use).Error?.Category);

        var rename = Tag("r1", "update_tag", "Start");
        rename.NewName = "Go";
        var results = Run(state, rename, use);

        Assert.Equal("r1", results[1].DependsOn);
        Assert.Contains(results[0].Resolution.Effect!.Changes, change => change is { Field: "name", Current: "Start", Requested: "Go" });
    }

    [Fact]
    public void DeleteThenRecreateDependsOnDelete()
    {
        var results = Run(State(), Tag("d", "delete_tag", "Start"), Tag("c", "create_tag", "Start"));

        Assert.Equal("d", results[1].DependsOn);
        Assert.DoesNotContain(PlcGuardDefinitions.NameCollision, GuardIds(results[1].Resolution));
    }

    [Fact]
    public void TwoCreatesOfSameNameCollide()
    {
        var results = Run(State(), Tag("a", "create_tag", "Twin"), Tag("b", "create_user_constant", "twin", "Motors", "/Line"));

        Assert.DoesNotContain(PlcGuardDefinitions.NameCollision, GuardIds(results[0].Resolution));
        Assert.Contains(PlcGuardDefinitions.NameCollision, GuardIds(results[1].Resolution));
    }

    [Fact]
    public void TagNameCollidesWithBlockCaseInsensitive()
    {
        var state = State();

        Assert.Contains(PlcGuardDefinitions.NameCollision, GuardIds(state.Resolve(Tag("a", "create_tag", "main"))));
        Assert.Contains(PlcGuardDefinitions.NameCollision, GuardIds(state.Resolve(Tag("b", "create_tag", "tcon"))));
        var rename = Tag("c", "update_tag", "Start");
        rename.NewName = "FB_MOTOR";
        Assert.Contains(PlcGuardDefinitions.NameCollision, GuardIds(state.Resolve(rename)));
    }

    [Fact]
    public void TableNameCollidesAcrossFolders()
    {
        var resolution = State().Resolve(new PlcOperationRequest { OperationId = "t", Operation = "create_tag_table", TableName = "motors" });

        Assert.Contains(PlcGuardDefinitions.NameCollision, GuardIds(resolution));
    }

    [Fact]
    public void CreateBlockNameCollidesWithTagOrConstant()
    {
        var state = State();

        Assert.Contains(PlcGuardDefinitions.NameCollision, GuardIds(state.Resolve(Blocks("a", "create_block", "PLC_1/start", "FC"))));
        Assert.Contains(PlcGuardDefinitions.NameCollision, GuardIds(state.Resolve(Blocks("b", "create_block", "PLC_1/Blocks/Motors/MAXSPEED", "FC"))));
        Assert.Empty(state.Resolve(Blocks("c", "create_block", "PLC_1/Blocks/Motors/Fresh", "FC")).Guards);
    }

    [Fact]
    public void CreateBlockGroupNameTakenInParentCollides()
    {
        var state = State();

        Assert.Contains(PlcGuardDefinitions.NameCollision, GuardIds(state.Resolve(Blocks("a", "create_block_group", "PLC_1/Blocks/motors"))));
        Assert.Contains(PlcGuardDefinitions.NameCollision, GuardIds(state.Resolve(Blocks("b", "create_block_group", "PLC_1/Main"))));
        var fresh = state.Resolve(Blocks("c", "create_block_group", "PLC_1/Blocks/Motors/Fresh"));
        Assert.Empty(fresh.Guards);
        Assert.Equal("PLC_1/Blocks/Motors", fresh.Effect!.Placement);
    }

    [Fact]
    public void CreateBlockOnExistingFiresBlockExists()
    {
        var resolution = State().Resolve(Blocks("a", "create_block", "PLC_1/Blocks/Main", "OB"));

        Assert.Contains(PlcGuardDefinitions.BlockExists, GuardIds(resolution));
    }

    [Fact]
    public void DeleteBlockFiresDeletesBlockWithPathTypeAndLanguage()
    {
        var resolution = State().Resolve(Blocks("d", "delete_block", "PLC_1/FB_Motor"));

        var guard = Assert.Single(resolution.Guards);
        Assert.Equal(PlcGuardDefinitions.DeletesBlock, guard.Id);
        Assert.Contains("PLC_1/Blocks/Motors/FB_Motor", guard.Message);
        Assert.Contains("FB", guard.Message);
        Assert.Contains("SCL", guard.Message);
        Assert.Equal("PLC_1/Blocks/Motors/FB_Motor", resolution.Effect!.Target.BlockPath);
        Assert.Equal(("FB", "SCL"), (resolution.Effect.Removes!.BlockType, resolution.Effect.Removes.Language));
    }

    [Fact]
    public void DeleteDefaultTableFires()
    {
        var resolution = State().Resolve(new PlcOperationRequest { OperationId = "d", Operation = "delete_tag_table", TableName = "Default tag table" });

        Assert.Contains(PlcGuardDefinitions.DefaultTagTable, GuardIds(resolution));
    }

    [Fact]
    public void UnreadableFlagFiresAttributeUnreadable()
    {
        var state = State();
        var unreadable = Tag("u", "update_tag", "Motor1", "Motors", "/Line");
        unreadable.ExternalWritable = true;
        var readable = Tag("r", "update_tag", "Start");
        readable.ExternalWritable = false;

        Assert.Contains(PlcGuardDefinitions.AttributeUnreadable, GuardIds(state.Resolve(unreadable)));
        Assert.Empty(state.Resolve(readable).Guards);
    }

    [Fact]
    public void AddressOverlapIsInfoOnly()
    {
        var item = Tag("a", "create_tag", "Fresh");
        item.LogicalAddress = "%i0.0";

        var guard = Assert.Single(State().Resolve(item).Guards);
        Assert.Equal(PlcGuardDefinitions.AddressOverlap, guard.Id);
        Assert.Equal("info", PlcGuardDefinitions.Definitions.Single(d => d.Id == guard.Id).Severity);
    }

    [Fact]
    public void DeleteGroupListsDescendants()
    {
        var resolution = State().Resolve(Blocks("d", "delete_block_group", "PLC_1/Blocks/Motors"));

        var removes = resolution.Effect!.Removes!;
        Assert.Equal(new[] { "PLC_1/Blocks/Motors/FB_Motor", "PLC_1/Blocks/Motors/Legacy/FC_Old" }, removes.Blocks);
        Assert.Equal(new[] { "PLC_1/Blocks/Motors/Legacy" }, removes.Groups);
        Assert.Contains(PlcGuardDefinitions.DeletesGroupContents, GuardIds(resolution));
        // The full list is in effect.removes; the guard message carries counts only.
        var message = resolution.Guards.Single(g => g.Id == PlcGuardDefinitions.DeletesGroupContents).Message;
        Assert.Contains("removes 2 blocks and 1 groups", message);
        Assert.DoesNotContain("FB_Motor", message);
    }

    [Fact]
    public void DeleteTableCountsMembers()
    {
        var resolution = State().Resolve(new PlcOperationRequest { OperationId = "d", Operation = "delete_tag_table", TableName = "Motors", FolderPath = "/Line" });

        Assert.Equal((1, 1), (resolution.Effect!.Removes!.TagCount, resolution.Effect.Removes.UserConstantCount));
        var guard = Assert.Single(resolution.Guards);
        Assert.Equal(PlcGuardDefinitions.DeletesTableContents, guard.Id);
    }

    [Fact]
    public void IncompleteInventoryFiresUnverifiable()
    {
        var resolution = State(complete: false).Resolve(Tag("a", "delete_tag", "Start"));

        Assert.Null(resolution.Error);
        Assert.Contains(PlcGuardDefinitions.StateUnverifiable, GuardIds(resolution));
    }

    [Fact]
    public void SkippedNodeUnderTargetFiresUnverifiable()
    {
        var skipped = new ProjectTreeSkippedNodeInfo
        {
            ParentPath = new()
            {
                Seg(ProjectTreeNodeTypes.Device, Device), Seg(ProjectTreeNodeTypes.PlcSoftware, Plc),
                Seg(ProjectTreeNodeTypes.BlockFolder, "Program blocks"), Seg(ProjectTreeNodeTypes.BlockFolder, "Motors"),
            },
            NodeType = ProjectTreeNodeTypes.Block,
            Reason = "access denied",
        };
        var state = State(true, skipped);

        var resolution = state.Resolve(Blocks("d", "delete_block_group", "PLC_1/Blocks/Motors"));

        Assert.Null(resolution.Error);
        Assert.Contains(PlcGuardDefinitions.StateUnverifiable, GuardIds(resolution));
        Assert.DoesNotContain(PlcGuardDefinitions.StateUnverifiable,
            GuardIds(state.Resolve(Blocks("g", "create_block_group", "PLC_1/Blocks/Other"))));
    }

    [Fact]
    public void IncompleteInventoryMissingTableIsGuardNotError()
    {
        var state = State(complete: false);
        var item = Tag("c", "create_tag", "Fresh", "Hidden", "/Line");

        var resolution = state.Resolve(item);

        Assert.Null(resolution.Error);
        Assert.Contains(PlcGuardDefinitions.StateUnverifiable, GuardIds(resolution));
        Assert.Equal(("Tag", "/Line", "Hidden", "Fresh"),
            (resolution.Effect!.Target.Kind, resolution.Effect.Target.FolderPath, resolution.Effect.Target.TableName, resolution.Effect.Target.Name));
        state.Apply(item);
        Assert.Null(state.Resolve(new PlcOperationRequest { OperationId = "t", Operation = "delete_tag_table", TableName = "Gone" }).Error);
    }

    [Fact]
    public void SkippedGroupAboveMissingBlockIsGuardNotError()
    {
        var skipped = new ProjectTreeSkippedNodeInfo
        {
            ParentPath = new() { Seg(ProjectTreeNodeTypes.Device, Device), Seg(ProjectTreeNodeTypes.PlcSoftware, Plc), Seg(ProjectTreeNodeTypes.BlockFolder, "Program blocks") },
            NodeType = ProjectTreeNodeTypes.BlockFolder,
            Reason = "access denied",
        };
        var state = State(true, skipped);

        foreach (var item in new[]
                 {
                     Blocks("hidden-group", "delete_block", "PLC_1/Blocks/Hidden/X"),
                     Blocks("hidden-block", "delete_block", "PLC_1/Blocks/Motors/Nope"),
                     Blocks("unique", "update_block_logic", "PLC_1/Nope"),
                     Blocks("group", "delete_block_group", "PLC_1/Blocks/Hidden"),
                     Blocks("parent", "create_block", "PLC_1/Blocks/Hidden/New", "FC"),
                 })
        {
            var resolution = state.Resolve(item);
            Assert.Null(resolution.Error);
            Assert.Contains(PlcGuardDefinitions.StateUnverifiable, GuardIds(resolution));
            Assert.Equal(item.BlockPath, resolution.Effect!.Target.BlockPath);
            state.Apply(item);
        }
    }

    [Fact]
    public void CompleteEvidenceMissingTargetIsTargetNotFound()
    {
        var state = State();

        Assert.Equal("target_not_found", state.Resolve(Tag("t", "delete_tag", "Nope")).Error?.Category);
        Assert.Equal("target_not_found", state.Resolve(Tag("c", "create_tag", "Fresh", "Hidden")).Error?.Category);
        Assert.Equal("target_not_found", state.Resolve(Blocks("b", "delete_block", "PLC_1/Blocks/Hidden/X")).Error?.Category);
        Assert.Equal("target_not_found", state.Resolve(Blocks("u", "update_block_logic", "PLC_1/Nope")).Error?.Category);
    }

    [Fact]
    public void NullConstantValueFiresUnverifiable()
    {
        var update = Tag("u", "update_user_constant", "Unreadable", "Motors", "/Line");
        update.Value = "5";

        var resolution = State().Resolve(update);

        Assert.Contains(PlcGuardDefinitions.StateUnverifiable, GuardIds(resolution));
        Assert.Contains(PlcGuardDefinitions.StateUnverifiable,
            GuardIds(State().Resolve(Tag("d", "delete_user_constant", "Unreadable", "Motors", "/Line"))));
    }

    [Fact]
    public void EffectShowsCurrentToRequested()
    {
        var update = Tag("u", "update_tag", "Start");
        update.DataType = "Int";
        update.LogicalAddress = "%I1.0";

        var effect = State().Resolve(update).Effect!;

        Assert.Equal("update_tag", effect.Operation);
        Assert.Equal(("Tag", "PLC_1", "Station_1", "/", "Default tag table", "Start"),
            (effect.Target.Kind, effect.Target.PlcName, effect.Target.DeviceName, effect.Target.FolderPath, effect.Target.TableName, effect.Target.Name));
        Assert.Equal(new[]
        {
            new PlcFieldChange("dataType", "Bool", "Int"),
            new PlcFieldChange("logicalAddress", "%I0.0", "%I1.0"),
        }, effect.Changes);
        Assert.Null(effect.DependsOn);
    }
}
