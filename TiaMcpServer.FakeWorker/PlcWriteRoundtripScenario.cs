using System.Text.Json;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.Contracts;

/// <summary>
/// Stateful <c>plc-write-roundtrip</c> fixture: a root PLC (software PLC_1 on device Station_1)
/// and a grouped PLC (software PLC_2 on device PLC_1, so "PLC_1" names both). Reads observe
/// earlier writes in the same FakeWorker process. Block exports of Empty_DB return empty text and
/// of Locked fail, to model unreadable content.
/// With <c>inventoryIncomplete</c> the inventory reports a PLC whose tag tables could not be read.
/// </summary>
sealed class PlcWriteRoundtripScenario(Func<string, BlockImportOutcomeInfo> completedOutcome, bool inventoryIncomplete = false)
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    private sealed class Group(string name, bool isSystem = false)
    {
        public string Name { get; } = name;
        public bool IsSystem { get; } = isSystem;
        public List<Group> Groups { get; } = new();
        public List<(string Name, string Type, string Language)> Blocks { get; } = new();
    }

    private sealed class Plc(string software, string device)
    {
        public string Software { get; } = software;
        public string Device { get; } = device;
        public List<TagTableInfo> Tables { get; } = new();
        public Group Root { get; } = new("Program blocks");
        public List<string> Types { get; } = new();
    }

    private readonly List<Plc> _plcs = Fixture();
    private readonly Dictionary<string, string> _content = new(Names);

    public string Handle(string line, string? fallbackProjectPath)
    {
        var request = JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Envelope)!;
        try
        {
            return request.Method switch
            {
                "list_tag_tables" => Ok(new PlcTagInventoryInfo
                {
                    IsComplete = !inventoryIncomplete,
                    Messages = inventoryIncomplete ? new() { "The tag tables of PLC 'Hidden_PLC' could not be read: access denied." } : new(),
                    Plcs = _plcs.Where(p => request.PlcName is null || Matches(p, request.PlcName)).Select(p => new PlcTagInventoryPlcInfo
                    {
                        PlcName = p.Software, DeviceName = p.Device, Tables = p.Tables,
                    }).ToList(),
                }),
                "browse_project_tree_v3_snapshot" => Tree(request, fallbackProjectPath),
                "get_block_content" => Export(request),
                "get_type_content" => ExportType(request),
                "update_block_logic" => UpdateBlock(request),
                "update_type_content" => UpdateType(request),
                "create_block" or "delete_block" or "create_block_group" or "delete_block_group" => BlockWrite(request),
                _ => TagWrite(request),
            };
        }
        catch (WorkerOperationException ex)
        {
            return JsonSerializer.Serialize(new WorkerResponse { Success = false, FailureCategory = ex.FailureCategory, Error = ex.Message }, WorkerJson.Envelope);
        }
    }

    private static List<Plc> Fixture()
    {
        var plc1 = new Plc("PLC_1", "Station_1");
        plc1.Tables.Add(new TagTableInfo
        {
            Name = "Default tag table", FolderPath = "/", IsDefault = true,
            Tags = { new TagInfo { Name = "Start", DataType = "Bool", LogicalAddress = "%I0.0", ExternalAccessible = true, ExternalVisible = true, ExternalWritable = true } },
            UserConstants = { new UserConstantInfo { Name = "MaxSpeed", DataType = "Int", Value = "100" } },
        });
        plc1.Tables.Add(new TagTableInfo
        {
            Name = "Motors", FolderPath = "/Line",
            Tags = { new TagInfo { Name = "Motor1", DataType = "Bool", LogicalAddress = "%Q0.0" } },
            UserConstants = { new UserConstantInfo { Name = "Unreadable", DataType = "Int", Value = null } },
        });
        plc1.Root.Blocks.AddRange(new[] { ("Main", "OB", "LAD"), ("Empty_DB", "GlobalDB", "DB"), ("Locked", "FC", "LAD") });
        var motors = new Group("Motors");
        motors.Blocks.Add(("FB_Motor", "FB", "SCL"));
        var legacy = new Group("Legacy");
        legacy.Blocks.Add(("FC_Old", "FC", "LAD"));
        motors.Groups.Add(legacy);
        var system = new Group("System blocks", isSystem: true);
        system.Blocks.Add(("TCON", "FB", "STL"));
        plc1.Root.Groups.AddRange(new[] { motors, system });
        plc1.Types.Add("UDT_Settings");

        var plc2 = new Plc("PLC_2", "PLC_1");
        plc2.Tables.Add(new TagTableInfo
        {
            Name = "Default tag table", FolderPath = "/", IsDefault = true,
            Tags = { new TagInfo { Name = "Start", DataType = "Bool", LogicalAddress = "%I0.0", ExternalAccessible = true, ExternalVisible = true, ExternalWritable = true } },
        });
        plc2.Root.Blocks.Add(("Main", "OB", "LAD"));
        return new List<Plc> { plc1, plc2 };
    }

    private string Tree(WorkerRequest request, string? fallbackProjectPath)
    {
        var selector = request.StartSelector;
        if (selector is not { Count: 2 }) Throw(WorkerFailureCategories.ValidationError, "expected a [Device, PlcSoftware] selector");
        var plc = _plcs.SingleOrDefault(p => Names.Equals(p.Device, selector![0].Name) && Names.Equals(p.Software, selector[1].Name))
            ?? Throw<Plc>(WorkerFailureCategories.TargetNotFound, "The typed project-tree selector did not resolve to a target.");
        var tables = new ProjectTreeNode { Name = "PLC tags", NodeType = ProjectTreeNodeTypes.TagTableFolder, Children = new() };
        foreach (var table in plc.Tables)
        {
            var folder = tables;
            foreach (var segment in table.FolderPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                var next = folder.Children!.FirstOrDefault(c => c.NodeType == ProjectTreeNodeTypes.TagTableFolder && Names.Equals(c.Name, segment));
                if (next is null) folder.Children!.Add(next = new ProjectTreeNode { Name = segment, NodeType = ProjectTreeNodeTypes.TagTableFolder, Children = new() });
                folder = next;
            }

            folder.Children!.Add(new ProjectTreeNode { Name = table.Name, NodeType = ProjectTreeNodeTypes.TagTable, Children = new() });
        }

        var software = new ProjectTreeNode
        {
            Name = plc.Software, NodeType = ProjectTreeNodeTypes.PlcSoftware,
            Children = new()
            {
                GroupNode(plc.Root, ProjectTreeNodeTypes.BlockFolder), tables,
                new() { Name = "PLC data types", NodeType = ProjectTreeNodeTypes.TypeFolder,
                    Children = plc.Types.Select(t => new ProjectTreeNode { Name = t, NodeType = ProjectTreeNodeTypes.Type, Children = new() }).ToList() },
            },
        };
        return JsonSerializer.Serialize(new WorkerResponse
        {
            Success = true,
            ResolvedProjectPath = request.ProjectPath ?? fallbackProjectPath,
            Payload = WorkerJson.SerializePayload(new ProjectTreeBrowseResultInfo
            {
                StartSelector = new()
                {
                    new() { NodeType = ProjectTreeNodeTypes.Device, Name = plc.Device },
                    new() { NodeType = ProjectTreeNodeTypes.PlcSoftware, Name = plc.Software },
                },
                Depth = request.Depth,
                Roots = new() { software },
                Skipped = new(),
            }),
        }, WorkerJson.Envelope);
    }

    private static ProjectTreeNode GroupNode(Group group, string nodeType) => new()
    {
        Name = group.Name, NodeType = nodeType,
        Children = group.Blocks.Select(b => new ProjectTreeNode
            {
                Name = b.Name, NodeType = b.Type, Children = new(),
                Details = group.IsSystem
                    ? new() { ["Number"] = "1", ["ProgrammingLanguage"] = b.Language, ["IsSystemBlock"] = "true" }
                    : new() { ["Number"] = "1", ["ProgrammingLanguage"] = b.Language },
            })
            .Concat(group.Groups.Select(g => GroupNode(g, g.IsSystem ? ProjectTreeNodeTypes.SystemBlockFolder : ProjectTreeNodeTypes.BlockFolder)))
            .ToList(),
    };

    private string Export(WorkerRequest request)
    {
        var (plc, _, block, path) = ResolveBlock(request.BlockPath!);
        if (block == "Locked") Throw(WorkerFailureCategories.WorkerOperationFailed, "The block could not be exported: access denied.");
        if (block == "Empty_DB") return Ok(string.Empty);
        return Ok(Content(plc, path, request.Format ?? SourceFormatNames.Xml));
    }

    private string ExportType(WorkerRequest request)
    {
        var (plc, type) = ResolveType(request.TypePath!);
        return Ok(Content(plc, $"{plc.Software}/Types/{type}", request.Format ?? SourceFormatNames.Source));
    }

    private string UpdateBlock(WorkerRequest request)
    {
        var (plc, _, _, path) = ResolveBlock(request.BlockPath!);
        var format = request.Format ?? SourceFormatNames.Xml;
        RequireHash(request, format, Content(plc, path, format));
        _content[Key(path, format)] = request.Content!;
        return JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = "Import succeeded.", BlockImportOutcome = completedOutcome(format) }, WorkerJson.Envelope);
    }

    private string UpdateType(WorkerRequest request)
    {
        var (plc, type) = ResolveType(request.TypePath!);
        var format = request.Format ?? SourceFormatNames.Source;
        var path = $"{plc.Software}/Types/{type}";
        RequireHash(request, format, Content(plc, path, format));
        _content[Key(path, format)] = request.Content!;
        return Ok(new PlcTypeImportResultInfo
        {
            Operation = "update_type_content", TypePath = request.TypePath!, TypeName = type, Format = format,
            ProjectNodeRemoved = true, GeneratedObjectCount = 1,
        });
    }

    private string BlockWrite(WorkerRequest request)
    {
        var address = BlockAddress.Parse(request.BlockPath!);
        var plc = FindPlc(address.PlcName);
        var group = address.IsDeterministic ? Walk(plc.Root, address.FolderPath) : plc.Root;
        string path;
        switch (request.Method)
        {
            case "create_block":
                if (group.Blocks.Any(b => Names.Equals(b.Name, address.BlockName))) Throw(WorkerFailureCategories.StateChanged, "The block already exists.");
                group.Blocks.Add((address.BlockName, request.BlockType!, request.BlockType == "GlobalDB" ? "DB" : request.Language ?? "LAD"));
                path = BlocksPath(plc, address.FolderPath, address.BlockName);
                break;
            case "delete_block":
                var (_, owner, block, blockPath) = ResolveBlock(request.BlockPath!);
                owner.Blocks.RemoveAll(b => b.Name == block);
                path = blockPath;
                break;
            case "create_block_group":
                if (group.Groups.Any(g => Names.Equals(g.Name, address.BlockName))) Throw(WorkerFailureCategories.StateChanged, "The block group already exists.");
                group.Groups.Add(new Group(address.BlockName));
                path = BlocksPath(plc, address.FolderPath, address.BlockName);
                break;
            default:
                var doomed = group.Groups.FirstOrDefault(g => Names.Equals(g.Name, address.BlockName))
                    ?? Throw<Group>(WorkerFailureCategories.TargetNotFound, $"Block group '{address.BlockName}' was not found.");
                group.Groups.Remove(doomed);
                path = BlocksPath(plc, address.FolderPath, address.BlockName);
                break;
        }

        return Ok(new BlockMutationResultInfo
        {
            Operation = request.Method, ProjectPath = request.ProjectPath, PlcName = plc.Software, BlockPath = path,
            BlockType = request.BlockType, Language = request.Language,
        });
    }

    private string TagWrite(WorkerRequest request)
    {
        var plc = FindPlc(request.PlcName);
        var folder = "/" + string.Join("/", (request.FolderPath ?? "/").Split('/', StringSplitOptions.RemoveEmptyEntries));
        var table = plc.Tables.FirstOrDefault(t => Names.Equals(t.FolderPath, folder) && Names.Equals(t.Name, request.TableName));
        switch (request.Method)
        {
            case "create_tag_table":
                if (plc.Tables.Any(t => Names.Equals(t.Name, request.TableName))) Throw(WorkerFailureCategories.StateChanged, "The tag table name is taken.");
                plc.Tables.Add(table = new TagTableInfo { Name = request.TableName!, FolderPath = folder });
                break;
            case "delete_tag_table":
                plc.Tables.Remove(Require(table, "tag table"));
                break;
            case "create_tag":
                Require(table, "tag table").Tags.Add(new TagInfo { Name = request.Name!, DataType = request.DataType!, LogicalAddress = request.LogicalAddress ?? string.Empty,
                    ExternalAccessible = true, ExternalVisible = true, ExternalWritable = true });
                break;
            case "update_tag":
                var tag = Require(Require(table, "tag table").Tags.FirstOrDefault(t => Names.Equals(t.Name, request.Name)), "tag");
                tag.Name = request.NewName ?? tag.Name;
                tag.DataType = request.DataType ?? tag.DataType;
                tag.LogicalAddress = request.LogicalAddress ?? tag.LogicalAddress;
                tag.ExternalAccessible = request.ExternalAccessible ?? tag.ExternalAccessible;
                tag.ExternalVisible = request.ExternalVisible ?? tag.ExternalVisible;
                tag.ExternalWritable = request.ExternalWritable ?? tag.ExternalWritable;
                break;
            case "delete_tag":
                Require(table, "tag table").Tags.Remove(Require(table!.Tags.FirstOrDefault(t => Names.Equals(t.Name, request.Name)), "tag"));
                break;
            case "create_user_constant":
                Require(table, "tag table").UserConstants.Add(new UserConstantInfo { Name = request.Name!, DataType = request.DataType!, Value = request.Value });
                break;
            case "update_user_constant":
                var constant = Require(Require(table, "tag table").UserConstants.FirstOrDefault(c => Names.Equals(c.Name, request.Name)), "user constant");
                constant.DataType = request.DataType ?? constant.DataType;
                constant.Value = request.Value ?? constant.Value;
                break;
            case "delete_user_constant":
                Require(table, "tag table").UserConstants.Remove(Require(table!.UserConstants.FirstOrDefault(c => Names.Equals(c.Name, request.Name)), "user constant"));
                break;
            default:
                Throw(WorkerFailureCategories.ValidationError, $"unexpected plc-write-roundtrip method '{request.Method}'");
                break;
        }

        var isConstant = request.Method.Contains("user_constant", StringComparison.Ordinal);
        return Ok(new TagMutationResultInfo
        {
            Operation = request.Method, ProjectPath = request.ProjectPath, PlcName = plc.Software, TableName = request.TableName!, FolderPath = folder,
            TagName = request.Method.EndsWith("_tag", StringComparison.Ordinal) ? request.NewName ?? request.Name : null,
            UserConstantName = isConstant ? request.Name : null,
        });
    }

    private (Plc Plc, Group Group, string Block, string Path) ResolveBlock(string blockPath)
    {
        var address = BlockAddress.Parse(blockPath);
        var plc = FindPlc(address.PlcName);
        if (address.IsDeterministic)
        {
            var group = Walk(plc.Root, address.FolderPath);
            var block = group.Blocks.FirstOrDefault(b => Names.Equals(b.Name, address.BlockName)).Name
                ?? Throw<string>(WorkerFailureCategories.TargetNotFound, $"Block '{address.BlockName}' was not found.");
            return (plc, group, block, BlocksPath(plc, address.FolderPath, block));
        }

        var matches = UserBlocks(plc.Root, new List<string>()).Where(m => Names.Equals(m.Block, address.BlockName)).ToList();
        if (matches.Count != 1)
            Throw(matches.Count == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous, $"Block '{address.BlockName}' was not resolved uniquely.");
        return (plc, matches[0].Group, matches[0].Block, BlocksPath(plc, matches[0].Folders, matches[0].Block));
    }

    private static IEnumerable<(Group Group, List<string> Folders, string Block)> UserBlocks(Group group, List<string> folders)
        => group.Blocks.Select(b => (group, folders, b.Name))
            .Concat(group.Groups.Where(g => !g.IsSystem).SelectMany(g => UserBlocks(g, folders.Append(g.Name).ToList())));

    private (Plc Plc, string Type) ResolveType(string typePath)
    {
        var address = PlcTypeAddress.Parse(typePath);
        var plc = FindPlc(address.PlcName);
        return (plc, plc.Types.FirstOrDefault(t => Names.Equals(t, address.TypeName))
            ?? Throw<string>(WorkerFailureCategories.TargetNotFound, $"PLC data type '{address.TypeName}' was not found."));
    }

    private Plc FindPlc(string? name)
    {
        var matches = _plcs.Where(p => name is null || Matches(p, name)).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => Throw<Plc>(WorkerFailureCategories.TargetNotFound, $"PLC '{name}' was not found."),
            _ => Throw<Plc>(WorkerFailureCategories.TargetAmbiguous, $"PLC '{name}' is ambiguous."),
        };
    }

    private static Group Walk(Group root, IEnumerable<string> folders)
    {
        var current = root;
        foreach (var folder in folders)
            current = current.Groups.FirstOrDefault(g => !g.IsSystem && Names.Equals(g.Name, folder))
                ?? Throw<Group>(WorkerFailureCategories.TargetNotFound, $"Block folder '{folder}' not found.");
        return current;
    }

    private string Content(Plc plc, string path, string format)
        => _content.TryGetValue(Key(path, format), out var stored) ? stored : $"<Content of=\"{path}\" format=\"{format}\"/>";

    private static void RequireHash(WorkerRequest request, string format, string current)
    {
        if (!string.Equals(request.ExpectedContentHash, ContentHashRules.Compute(format, current), StringComparison.Ordinal))
            Throw(WorkerFailureCategories.StateChanged, "The content changed since it was read.");
    }

    private static T Require<T>(T? value, string what) where T : class
        => value ?? Throw<T>(WorkerFailureCategories.TargetNotFound, $"The {what} was not found.");

    private static bool Matches(Plc plc, string name) => Names.Equals(plc.Software, name) || Names.Equals(plc.Device, name);

    private static string BlocksPath(Plc plc, IEnumerable<string> folders, string name)
        => string.Join("/", new[] { plc.Software, "Blocks" }.Concat(folders).Append(name));

    private static string Key(string path, string format) => $"{format}:{path}";

    private static string Ok(string payload) => JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = payload }, WorkerJson.Envelope);

    private static string Ok<T>(T payload) => Ok(WorkerJson.SerializePayload(payload));

    private static void Throw(string category, string message) => throw new WorkerOperationException(category, message);

    private static T Throw<T>(string category, string message) => throw new WorkerOperationException(category, message);
}
