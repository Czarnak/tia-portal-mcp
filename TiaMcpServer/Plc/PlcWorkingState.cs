using TiaMcpServer.Contracts;
using TiaMcpServer.ProjectTree;
using TiaMcpServer.Safety.Pipeline;
using static TiaMcpServer.Plc.PlcGuardDefinitions;

namespace TiaMcpServer.Plc;

/// <summary>
/// The planner's in-call picture of one PLC: its tag inventory and block tree, with every earlier
/// planned item applied. Resolution of a missing or ambiguous target is an error; incomplete or
/// unreadable evidence only fires <see cref="PlcGuardDefinitions.StateUnverifiable"/>.
/// </summary>
internal sealed class PlcWorkingState
{
    private sealed record Table(string Folder, string Name, bool IsDefault, List<TagInfo> Tags, List<UserConstantInfo> Constants);

    private sealed record Block(string Name, string Type, string? Language, bool IsSystem);

    private sealed record Group(string Name, string Path, Group? Parent, IReadOnlyList<ProjectTreeSelectorSegment> Selector, bool IsSystem)
    {
        public List<Group> Groups { get; } = new();
        public List<Block> Blocks { get; } = new();
    }

    /// <summary><paramref name="hidden"/>: the miss may be caused by unreadable evidence, so it is not proof of absence.</summary>
    private sealed class ResolutionException(string category, string message, bool hidden = false) : Exception(message)
    {
        public string Category { get; } = category;
        public bool Hidden { get; } = hidden;
    }

    private static readonly StringComparer Names = PlcNameRules.Comparer;
    private readonly string _plc;
    private readonly string? _device;
    private readonly bool _inventoryComplete;
    private readonly List<Table> _tables;
    private readonly HashSet<string> _tableFolders = new(Names) { "/" };
    private readonly Group _root;
    private readonly Dictionary<string, Group> _unitRoots = new(Names);
    private readonly IReadOnlyList<ProjectTreeSkippedNodeInfo> _skipped;
    private readonly IReadOnlyList<ProjectTreeSelectorSegment> _basePath;
    private readonly Dictionary<string, (int Sequence, string OperationId)> _touches = new(StringComparer.Ordinal);
    private int _sequence;

    private PlcWorkingState(PlcTagInventoryPlcInfo plc, bool inventoryComplete, ProjectTreeObservation tree)
    {
        _plc = plc.PlcName;
        _device = plc.DeviceName;
        _inventoryComplete = inventoryComplete;
        _tables = plc.Tables.Select(t => new Table(NormalizeFolder(t.FolderPath), t.Name, t.IsDefault,
            t.Tags.Select(Copy).ToList(),
            t.UserConstants.Select(c => new UserConstantInfo { Name = c.Name, DataType = c.DataType, Value = c.Value }).ToList())).ToList();
        foreach (var table in _tables) _tableFolders.Add(table.Folder);

        var basePath = new[] { Segment(ProjectTreeNodeTypes.Device, _device ?? string.Empty), Segment(ProjectTreeNodeTypes.PlcSoftware, _plc) };
        _basePath = basePath;
        _skipped = tree.Skipped.Where(s => s.ParentPath.Count >= 2 && SameSelector(s.ParentPath.Take(2).ToArray(), basePath)).ToArray();
        var software = tree.Roots.FirstOrDefault(r => r.NodeType == ProjectTreeNodeTypes.PlcSoftware && Names.Equals(r.Name, _plc));
        var children = software?.Children ?? new List<ProjectTreeNode>();
        _root = OwnerRoot(children, $"{_plc}/Blocks", basePath);
        foreach (var folder in children.Where(c => c.NodeType == ProjectTreeNodeTypes.TagTableFolder))
            CollectTableFolders(folder, "/");
        foreach (var unit in children.Where(c => c.NodeType == ProjectTreeNodeTypes.SoftwareUnit))
            _unitRoots[unit.Name] = OwnerRoot(unit.Children ?? new(), $"{_plc}/Units/{unit.Name}/Blocks",
                basePath.Append(Segment(ProjectTreeNodeTypes.SoftwareUnit, unit.Name)).ToArray());
    }

    public static PlcWorkingState Create(PlcTagInventoryPlcInfo plc, bool inventoryComplete, ProjectTreeObservation tree)
        => new(plc, inventoryComplete, tree);

    public PlcResolution Resolve(PlcOperationRequest item)
    {
        var guards = new List<FiredGuard>();
        if (!_inventoryComplete)
            guards.Add(Fire(StateUnverifiable, item, $"The tag inventory is incomplete, so the state of PLC '{_plc}' cannot be verified. Inspect the list_tag_tables messages."));
        try
        {
            return new PlcResolution(ResolveEffect(item, guards), guards, null);
        }
        catch (ResolutionException ex) when (ex.Hidden && ex.Category == WorkerFailureCategories.TargetNotFound)
        {
            // Unreadable evidence never becomes absence: the call is blocked instead of failed.
            if (!guards.Any(g => g.Id == StateUnverifiable))
                guards.Add(Fire(StateUnverifiable, item, $"{ex.Message} Part of the evidence is unreadable, so the target may exist but cannot be verified."));
            return new PlcResolution(Unresolved(item, _plc, _device), guards, null);
        }
        catch (ResolutionException ex)
        {
            return new PlcResolution(null, Array.Empty<FiredGuard>(), new WriteToolError(ex.Category, ex.Message));
        }
        catch (ArgumentException ex)
        {
            return new PlcResolution(null, Array.Empty<FiredGuard>(), new WriteToolError(WorkerFailureCategories.ValidationError, $"Operation '{item.OperationId}': {ex.Message}"));
        }
    }

    /// <summary>Applies a resolvable item's creation, deletion, rename or value change to the working state.</summary>
    public void Apply(PlcOperationRequest item)
    {
        if (Resolve(item).Error is not null) return;
        _sequence++;
        try
        {
            ApplyResolved(item);
        }
        catch (ResolutionException)
        {
            // An unverifiable target cannot be applied; its block guard stops the call anyway.
        }
    }

    /// <summary>The requested identity of an item whose target could not be verified.</summary>
    public static PlcWriteEffect Unresolved(PlcOperationRequest item, string plcName, string? deviceName)
    {
        var tableOperation = item.Operation.Contains("tag_table", StringComparison.Ordinal);
        var target = item switch
        {
            { BlockPath: { } path } => new PlcTargetIdentity(item.Operation.Contains("group", StringComparison.Ordinal) ? PlcNameRules.BlockGroup : PlcNameRules.Block,
                plcName, deviceName, null, null, path.Split('/')[^1], path, null),
            { TypePath: { } type } => new PlcTargetIdentity("Type", plcName, deviceName, null, null, type.Split('/')[^1], null, type),
            _ => new PlcTargetIdentity(tableOperation ? PlcNameRules.TagTable : item.Operation.Contains("user_constant", StringComparison.Ordinal) ? PlcNameRules.UserConstant : PlcNameRules.Tag,
                plcName, deviceName, NormalizeFolder(item.FolderPath), item.TableName, tableOperation ? null : item.Name, null, null),
        };
        return new PlcWriteEffect(item.Operation, target, Array.Empty<PlcFieldChange>(), null, null, null, null);
    }

    private void ApplyResolved(PlcOperationRequest item)
    {
        switch (item.Operation)
        {
            case "create_tag_table":
                _tables.Add(new Table(NormalizeFolder(item.FolderPath), item.TableName!, false, new(), new()));
                Touch(TableKey(item.TableName!), item);
                break;
            case "delete_tag_table":
                var removed = RequireTable(item);
                _tables.Remove(removed);
                Touch(TableKey(removed.Name), item);
                foreach (var name in removed.Tags.Select(t => t.Name).Concat(removed.Constants.Select(c => c.Name))) Touch(CpuKey(name), item);
                break;
            case "create_tag":
                // ponytail: new tags assume TIA's default external-access flags (all true).
                RequireTable(item).Tags.Add(new TagInfo { Name = item.Name!, DataType = item.DataType!, LogicalAddress = item.LogicalAddress ?? string.Empty,
                    ExternalAccessible = item.ExternalAccessible ?? true, ExternalVisible = item.ExternalVisible ?? true, ExternalWritable = item.ExternalWritable ?? true });
                Touch(CpuKey(item.Name!), item);
                break;
            case "update_tag":
                var tag = RequireTag(item, RequireTable(item));
                tag.DataType = item.DataType ?? tag.DataType;
                tag.LogicalAddress = item.LogicalAddress ?? tag.LogicalAddress;
                tag.ExternalAccessible = item.ExternalAccessible ?? tag.ExternalAccessible;
                tag.ExternalVisible = item.ExternalVisible ?? tag.ExternalVisible;
                tag.ExternalWritable = item.ExternalWritable ?? tag.ExternalWritable;
                if (item.NewName is { } newName)
                {
                    Touch(CpuKey(tag.Name), item);
                    Touch(CpuKey(newName), item);
                    tag.Name = newName;
                }
                break;
            case "delete_tag":
                var tags = RequireTable(item).Tags;
                tags.Remove(RequireTag(item, RequireTable(item)));
                Touch(CpuKey(item.Name!), item);
                break;
            case "create_user_constant":
                RequireTable(item).Constants.Add(new UserConstantInfo { Name = item.Name!, DataType = item.DataType!, Value = item.Value });
                Touch(CpuKey(item.Name!), item);
                break;
            case "update_user_constant":
                var constant = RequireConstant(item, RequireTable(item));
                constant.DataType = item.DataType ?? constant.DataType;
                constant.Value = item.Value ?? constant.Value;
                break;
            case "delete_user_constant":
                RequireTable(item).Constants.Remove(RequireConstant(item, RequireTable(item)));
                Touch(CpuKey(item.Name!), item);
                break;
            case "create_block":
                var address = BlockAddress.Parse(item.BlockPath!);
                CreationParent(item, address).Blocks.Add(new Block(address.BlockName, BlockNodeType(item.BlockType!),
                    item.BlockType == "GlobalDB" ? "DB" : item.Language ?? "LAD", false));
                Touch(CpuKey(address.BlockName), item);
                break;
            case "delete_block":
                var (group, block) = FindBlock(item, BlockAddress.Parse(item.BlockPath!));
                group.Blocks.Remove(block);
                Touch(CpuKey(block.Name), item);
                break;
            case "create_block_group":
                var target = BlockAddress.Parse(item.BlockPath!);
                var parent = CreationParent(item, target);
                parent.Groups.Add(new Group(target.BlockName, $"{parent.Path}/{target.BlockName}", parent,
                    parent.Selector.Append(Segment(ProjectTreeNodeTypes.BlockFolder, target.BlockName)).ToArray(), false));
                Touch(GroupKey($"{parent.Path}/{target.BlockName}"), item);
                break;
            case "delete_block_group":
                var doomed = DeletionGroup(item, BlockAddress.Parse(item.BlockPath!));
                doomed.Parent!.Groups.Remove(doomed);
                foreach (var g in Descendants(doomed).Prepend(doomed)) Touch(GroupKey(g.Path), item);
                foreach (var b in DescendantBlocks(doomed)) Touch(CpuKey(b.Block.Name), item);
                break;
        }
    }

    /// <summary>The latest earlier item that created, deleted or renamed the target or a container it needs.</summary>
    public string? LastTouchedBy(PlcTargetIdentity target)
    {
        IEnumerable<string> keys = target.Kind switch
        {
            PlcNameRules.Tag or PlcNameRules.UserConstant => new[] { CpuKey(target.Name!), TableKey(target.TableName!) },
            PlcNameRules.TagTable => new[] { TableKey(target.TableName!) },
            PlcNameRules.Block => GroupKeys(ParentPath(target.BlockPath!)).Append(CpuKey(target.Name!)),
            PlcNameRules.BlockGroup => GroupKeys(target.BlockPath!),
            _ => Array.Empty<string>(),
        };
        return keys.Where(_touches.ContainsKey).Select(k => _touches[k]).OrderByDescending(t => t.Sequence)
            .Select(t => t.OperationId).FirstOrDefault();
    }

    private PlcWriteEffect ResolveEffect(PlcOperationRequest item, List<FiredGuard> guards)
    {
        switch (item.Operation)
        {
            case "create_tag_table":
            {
                var folder = NormalizeFolder(item.FolderPath);
                if (!_tableFolders.Contains(folder)) throw NotFound(item, $"tag table folder '{folder}' was not found in PLC '{_plc}'.",
                    !_inventoryComplete || _skipped.Any(s => s.NodeType is ProjectTreeNodeTypes.TagTable or ProjectTreeNodeTypes.TagTableFolder));
                if (_tables.FirstOrDefault(t => Names.Equals(t.Name, item.TableName)) is { } taken)
                    guards.Add(Fire(NameCollision, item, $"Tag table name '{item.TableName}' is already used by the table in '{taken.Folder}'; table names are unique across all folders."));
                if (_skipped.Any(s => s.NodeType is ProjectTreeNodeTypes.TagTable or ProjectTreeNodeTypes.TagTableFolder)) guards.Add(SkippedGuard(item));
                return Effect(item, new(PlcNameRules.TagTable, _plc, _device, folder, item.TableName, null, null, null), Change("tableName", null, item.TableName), folder);
            }
            case "delete_tag_table":
            {
                var table = RequireTable(item);
                if (table.IsDefault) guards.Add(Fire(DefaultTagTable, item, $"Tag table '{table.Name}' is the default tag table of PLC '{_plc}' and cannot be deleted."));
                if (table.Tags.Count + table.Constants.Count > 0)
                    guards.Add(Fire(DeletesTableContents, item, $"Deleting tag table '{table.Name}' in '{table.Folder}' removes {table.Tags.Count} tags and {table.Constants.Count} user constants."));
                return Effect(item, TableTarget(table), Array.Empty<PlcFieldChange>(), null, new(table.Tags.Count, table.Constants.Count, Array.Empty<string>(), Array.Empty<string>(), null, null));
            }
            case "create_tag":
            case "create_user_constant":
            {
                var table = RequireTable(item);
                RequireCpuNameFree(item, item.Name!, null, guards);
                if (item.LogicalAddress is not null) AddressOverlapGuard(item, null, guards);
                var kind = item.Operation == "create_tag" ? PlcNameRules.Tag : PlcNameRules.UserConstant;
                return Effect(item, MemberTarget(kind, table, item.Name!),
                    Change("name", null, item.Name).Concat(Change("dataType", null, item.DataType)).Concat(Change("logicalAddress", null, item.LogicalAddress))
                        .Concat(Change("value", null, item.Value)).ToArray(), TablePath(table));
            }
            case "update_tag":
            {
                var table = RequireTable(item);
                var tag = RequireTag(item, table);
                if (item.NewName is not null) RequireCpuNameFree(item, item.NewName, new PlcNamedObject(PlcNameRules.Tag, tag.Name, TablePath(table)), guards);
                if (item.LogicalAddress is not null) AddressOverlapGuard(item, tag, guards);
                foreach (var (field, requested, current) in new[] { ("externalAccessible", item.ExternalAccessible, tag.ExternalAccessible),
                             ("externalVisible", item.ExternalVisible, tag.ExternalVisible), ("externalWritable", item.ExternalWritable, tag.ExternalWritable) })
                {
                    if (requested is not null && current is null)
                        guards.Add(Fire(AttributeUnreadable, item, $"The current '{field}' flag of tag '{tag.Name}' is unreadable, so the change cannot be verified."));
                }
                return Effect(item, MemberTarget(PlcNameRules.Tag, table, tag.Name),
                    Change("name", tag.Name, item.NewName).Concat(Change("dataType", tag.DataType, item.DataType))
                        .Concat(Change("logicalAddress", tag.LogicalAddress, item.LogicalAddress))
                        .Concat(Change("externalAccessible", Flag(tag.ExternalAccessible), Flag(item.ExternalAccessible)))
                        .Concat(Change("externalVisible", Flag(tag.ExternalVisible), Flag(item.ExternalVisible)))
                        .Concat(Change("externalWritable", Flag(tag.ExternalWritable), Flag(item.ExternalWritable)))
                        .Concat(Change("isSafety", null, Flag(item.IsSafety))).ToArray(), null);
            }
            case "delete_tag":
            {
                var table = RequireTable(item);
                var tag = RequireTag(item, table);
                return Effect(item, MemberTarget(PlcNameRules.Tag, table, tag.Name), Array.Empty<PlcFieldChange>(), null,
                    new(1, 0, Array.Empty<string>(), Array.Empty<string>(), null, null));
            }
            case "update_user_constant":
            case "delete_user_constant":
            {
                var table = RequireTable(item);
                var constant = RequireConstant(item, table);
                if (constant.Value is null)
                    guards.Add(Fire(StateUnverifiable, item, $"The value of user constant '{constant.Name}' is unreadable, so its current state cannot be verified."));
                var delete = item.Operation == "delete_user_constant";
                return Effect(item, MemberTarget(PlcNameRules.UserConstant, table, constant.Name),
                    delete ? Array.Empty<PlcFieldChange>() : Change("dataType", constant.DataType, item.DataType).Concat(Change("value", constant.Value, item.Value)).ToArray(),
                    null, delete ? new(0, 1, Array.Empty<string>(), Array.Empty<string>(), null, null) : null);
            }
            case "create_block":
            {
                var address = BlockAddress.Parse(item.BlockPath!);
                var parent = CreationParent(item, address);
                var name = address.BlockName;
                if (parent.Blocks.Any(b => Names.Equals(b.Name, name)))
                    guards.Add(Fire(BlockExists, item, $"Block '{parent.Path}/{name}' already exists; create_block never overwrites a block."));
                else if (parent.Groups.Any(g => Names.Equals(g.Name, name)))
                    guards.Add(Fire(NameCollision, item, $"'{name}' is already used by block group '{parent.Path}/{name}'."));
                else RequireCpuNameFree(item, name, null, guards);
                return Effect(item, new(PlcNameRules.Block, _plc, _device, null, null, name, $"{parent.Path}/{name}", null),
                    Change("blockType", null, item.BlockType).Concat(Change("language", null, item.Language)).Concat(Change("obEventClass", null, item.ObEventClass)).ToArray(),
                    parent.Path);
            }
            case "delete_block":
            case "update_block_logic":
            {
                var (group, block) = FindBlock(item, BlockAddress.Parse(item.BlockPath!), guards);
                var path = $"{group.Path}/{block.Name}";
                var target = new PlcTargetIdentity(PlcNameRules.Block, _plc, _device, null, null, block.Name, path, null);
                if (item.Operation == "update_block_logic") return Effect(item, target, Array.Empty<PlcFieldChange>(), null);
                guards.Add(Fire(DeletesBlock, item, $"Deleting block '{path}' ({block.Type}, {block.Language ?? "unknown language"}) removes it and its content."));
                return Effect(item, target, Array.Empty<PlcFieldChange>(), null, new(0, 0, new[] { path }, Array.Empty<string>(), block.Type, block.Language));
            }
            case "create_block_group":
            {
                var address = BlockAddress.Parse(item.BlockPath!);
                var parent = CreationParent(item, address);
                if (parent.Groups.Any(g => Names.Equals(g.Name, address.BlockName)) || parent.Blocks.Any(b => Names.Equals(b.Name, address.BlockName)))
                    guards.Add(Fire(NameCollision, item, $"'{address.BlockName}' is already used by a block or block group in '{parent.Path}'."));
                if (_skipped.Any(s => SameSelector(s.ParentPath, parent.Selector))) guards.Add(SkippedGuard(item));
                return Effect(item, new(PlcNameRules.BlockGroup, _plc, _device, null, null, address.BlockName, $"{parent.Path}/{address.BlockName}", null),
                    Change("name", null, address.BlockName), parent.Path);
            }
            case "delete_block_group":
            {
                var group = DeletionGroup(item, BlockAddress.Parse(item.BlockPath!));
                var blocks = DescendantBlocks(group).Select(b => $"{b.Group.Path}/{b.Block.Name}").ToArray();
                var groups = Descendants(group).Select(g => g.Path).ToArray();
                if (blocks.Length + groups.Length > 0)
                    guards.Add(Fire(DeletesGroupContents, item, $"Deleting block group '{group.Path}' removes {blocks.Length} blocks and {groups.Length} groups; the full list is in the effect's removes."));
                if (_skipped.Any(s => StartsWith(s.ParentPath, group.Selector))) guards.Add(SkippedGuard(item));
                return Effect(item, new(PlcNameRules.BlockGroup, _plc, _device, null, null, group.Name, group.Path, null), Array.Empty<PlcFieldChange>(), null,
                    new(0, 0, blocks, groups, null, null));
            }
            case "update_type_content":
            {
                var address = PlcTypeAddress.Parse(item.TypePath!);
                return Effect(item, new("Type", _plc, _device, null, null, address.TypeName, null, item.TypePath), Array.Empty<PlcFieldChange>(), null);
            }
            default:
                throw new ResolutionException(WorkerFailureCategories.ValidationError, $"Unsupported PLC write operation '{item.Operation}'.");
        }
    }

    private Table RequireTable(PlcOperationRequest item)
    {
        var folder = NormalizeFolder(item.FolderPath);
        return _tables.FirstOrDefault(t => Names.Equals(t.Folder, folder) && Names.Equals(t.Name, item.TableName))
            ?? throw NotFound(item, $"tag table '{item.TableName}' was not found in '{folder}' of PLC '{_plc}'.", !_inventoryComplete);
    }

    private TagInfo RequireTag(PlcOperationRequest item, Table table)
        => table.Tags.FirstOrDefault(t => Names.Equals(t.Name, item.Name))
            ?? throw NotFound(item, $"tag '{item.Name}' was not found in tag table '{table.Name}'.", !_inventoryComplete);

    private UserConstantInfo RequireConstant(PlcOperationRequest item, Table table)
        => table.Constants.FirstOrDefault(c => Names.Equals(c.Name, item.Name))
            ?? throw NotFound(item, $"user constant '{item.Name}' was not found in tag table '{table.Name}'.", !_inventoryComplete);

    private void RequireCpuNameFree(PlcOperationRequest item, string name, PlcNamedObject? self, List<FiredGuard> guards)
    {
        if (PlcNameRules.FindCollision(name, CpuOccupants(), self) is { } occupant)
            guards.Add(Fire(NameCollision, item, $"'{name}' collides with {occupant.Kind} '{occupant.Name}' in '{occupant.Container}'; tags, user constants and blocks share one case-insensitive namespace."));
        // A block the walker could not read may hold the name.
        if (_skipped.Any(IsBlockTreeNode)) guards.Add(SkippedGuard(item));
    }

    private IEnumerable<PlcNamedObject> CpuOccupants()
        => _tables.SelectMany(t => t.Tags.Select(tag => new PlcNamedObject(PlcNameRules.Tag, tag.Name, TablePath(t)))
                .Concat(t.Constants.Select(c => new PlcNamedObject(PlcNameRules.UserConstant, c.Name, TablePath(t)))))
            .Concat(_unitRoots.Values.Prepend(_root).SelectMany(DescendantBlocks)
                .Select(b => new PlcNamedObject(b.Block.IsSystem ? PlcNameRules.SystemBlock : PlcNameRules.Block, b.Block.Name, b.Group.Path)));

    private void AddressOverlapGuard(PlcOperationRequest item, TagInfo? self, List<FiredGuard> guards)
    {
        var wanted = NormalizeAddress(item.LogicalAddress);
        // ponytail: exact address equality; range overlap (%IW0 vs %I0.0) is not computed.
        var other = _tables.SelectMany(t => t.Tags.Select(tag => (Table: t, Tag: tag)))
            .FirstOrDefault(x => !ReferenceEquals(x.Tag, self) && wanted.Length > 0 && NormalizeAddress(x.Tag.LogicalAddress) == wanted);
        if (other.Tag is not null)
            guards.Add(Fire(AddressOverlap, item, $"Logical address '{item.LogicalAddress}' is already used by tag '{other.Tag.Name}' in '{TablePath(other.Table)}'."));
    }

    private (Group Group, Block Block) FindBlock(PlcOperationRequest item, BlockAddress address, List<FiredGuard>? guards = null)
    {
        if (address.IsDeterministic)
        {
            var group = ResolveGroup(item, Owner(item, address), address.FolderPath);
            var block = group.Blocks.FirstOrDefault(b => Names.Equals(b.Name, address.BlockName))
                ?? throw NotFound(item, $"block '{address.BlockName}' was not found at '{group.Path}'.", HiddenAt(group.Selector));
            return (group, block);
        }

        // A unique lookup across the PLC; a skipped block could be a hidden second match.
        if (_skipped.Any(IsBlockTreeNode)) guards?.Add(SkippedGuard(item));
        var matches = _unitRoots.Values.Prepend(_root).SelectMany(UserBlocks).Where(b => Names.Equals(b.Block.Name, address.BlockName)).ToArray();
        return matches.Length switch
        {
            0 => throw NotFound(item, $"block '{address.BlockName}' was not found in PLC '{_plc}'.", _skipped.Any(IsBlockTreeNode)),
            1 => matches[0],
            _ => throw new ResolutionException(WorkerFailureCategories.TargetAmbiguous,
                $"Operation '{item.OperationId}': block '{address.BlockName}' is ambiguous in PLC '{_plc}'. Use the deterministic path, for example '{matches[0].Group.Path}/{address.BlockName}'."),
        };
    }

    private Group CreationParent(PlcOperationRequest item, BlockAddress address)
        => address.IsDeterministic ? ResolveGroup(item, Owner(item, address), address.FolderPath) : _root;

    private Group DeletionGroup(PlcOperationRequest item, BlockAddress address)
        => address.IsDeterministic
            ? ResolveGroup(item, Owner(item, address), address.FolderPath.Append(address.BlockName).ToArray())
            : ResolveGroup(item, _root, new[] { address.BlockName });

    private Group Owner(PlcOperationRequest? item, BlockAddress address)
        => address.UnitName is null ? _root
            : _unitRoots.GetValueOrDefault(address.UnitName) ?? throw NotFound(item, $"software unit '{address.UnitName}' was not found in PLC '{_plc}'.", HiddenAt(_basePath));

    private Group ResolveGroup(PlcOperationRequest? item, Group owner, IReadOnlyList<string> folders)
    {
        var current = owner;
        foreach (var folder in folders)
            current = current.Groups.FirstOrDefault(g => !g.IsSystem && Names.Equals(g.Name, folder))
                ?? throw NotFound(item, $"block group '{current.Path}/{folder}' was not found.", HiddenAt(current.Selector));
        return current;
    }

    private static IEnumerable<Group> Descendants(Group group) => group.Groups.SelectMany(g => Descendants(g).Prepend(g));

    private static IEnumerable<(Group Group, Block Block)> DescendantBlocks(Group group)
        => Descendants(group).Prepend(group).SelectMany(g => g.Blocks.Select(b => (g, b)));

    private static IEnumerable<(Group Group, Block Block)> UserBlocks(Group group)
        => group.Blocks.Select(b => (group, b)).Concat(group.Groups.Where(g => !g.IsSystem).SelectMany(UserBlocks));

    private Group OwnerRoot(IEnumerable<ProjectTreeNode> children, string path, IReadOnlyList<ProjectTreeSelectorSegment> selector)
    {
        var node = children.FirstOrDefault(c => c.NodeType == ProjectTreeNodeTypes.BlockFolder);
        var root = new Group(node?.Name ?? "Program blocks", path, null,
            selector.Append(Segment(ProjectTreeNodeTypes.BlockFolder, node?.Name ?? "Program blocks")).ToArray(), false);
        if (node is not null) Fill(root, node);
        return root;
    }

    private static void Fill(Group group, ProjectTreeNode node)
    {
        foreach (var child in node.Children ?? new())
        {
            if (child.NodeType is ProjectTreeNodeTypes.BlockFolder or ProjectTreeNodeTypes.SystemBlockFolder)
            {
                var sub = new Group(child.Name, $"{group.Path}/{child.Name}", group, group.Selector.Append(Segment(child.NodeType, child.Name)).ToArray(),
                    group.IsSystem || child.NodeType == ProjectTreeNodeTypes.SystemBlockFolder);
                Fill(sub, child);
                group.Groups.Add(sub);
            }
            else if (ProjectTreeNodeTypes.BlockLeaves.Contains(child.NodeType))
            {
                group.Blocks.Add(new Block(child.Name, child.NodeType, child.Details?.GetValueOrDefault("ProgrammingLanguage"),
                    group.IsSystem || child.Details?.GetValueOrDefault("IsSystemBlock") == "true"));
            }
        }
    }

    private void CollectTableFolders(ProjectTreeNode folder, string path)
    {
        _tableFolders.Add(path);
        foreach (var child in (folder.Children ?? new()).Where(c => c.NodeType == ProjectTreeNodeTypes.TagTableFolder))
            CollectTableFolders(child, path == "/" ? "/" + child.Name : $"{path}/{child.Name}");
    }

    private PlcWriteEffect Effect(PlcOperationRequest item, PlcTargetIdentity target, IReadOnlyList<PlcFieldChange> changes, string? placement, PlcRemoval? removes = null)
        => new(item.Operation, target, changes, placement, removes, null, null);

    private PlcTargetIdentity TableTarget(Table table) => new(PlcNameRules.TagTable, _plc, _device, table.Folder, table.Name, null, null, null);

    private PlcTargetIdentity MemberTarget(string kind, Table table, string name) => new(kind, _plc, _device, table.Folder, table.Name, name, null, null);

    private static PlcFieldChange[] Change(string field, string? current, string? requested)
        => requested is null ? Array.Empty<PlcFieldChange>() : new[] { new PlcFieldChange(field, current, requested) };

    private static string? Flag(bool? value) => value switch { null => null, true => "true", false => "false" };

    private FiredGuard SkippedGuard(PlcOperationRequest item)
        => Fire(StateUnverifiable, item, $"Part of the project tree of PLC '{_plc}' could not be read, so the target's surroundings cannot be verified.");

    private static FiredGuard Fire(string id, PlcOperationRequest item, string message) => new(id, item.OperationId, $"Operation '{item.OperationId}': {message}");

    private static ResolutionException NotFound(PlcOperationRequest? item, string message, bool hidden = false)
        => new(WorkerFailureCategories.TargetNotFound, item is null ? char.ToUpperInvariant(message[0]) + message[1..] : $"Operation '{item.OperationId}': {message}", hidden);

    /// <summary>A skipped node at or above <paramref name="container"/> may be, or hide, the missing object.</summary>
    private bool HiddenAt(IReadOnlyList<ProjectTreeSelectorSegment> container) => _skipped.Any(s => StartsWith(container, s.ParentPath));

    private void Touch(string key, PlcOperationRequest item) => _touches[key] = (_sequence, item.OperationId);

    private static bool IsBlockTreeNode(ProjectTreeSkippedNodeInfo node) => node.NodeType is not (ProjectTreeNodeTypes.TagTable
        or ProjectTreeNodeTypes.TagTableFolder or ProjectTreeNodeTypes.Type or ProjectTreeNodeTypes.TypeFolder);

    private static bool StartsWith(IReadOnlyList<ProjectTreeSelectorSegment> path, IReadOnlyList<ProjectTreeSelectorSegment> prefix)
        => path.Count >= prefix.Count && SameSelector(path.Take(prefix.Count).ToArray(), prefix);

    private static bool SameSelector(IReadOnlyList<ProjectTreeSelectorSegment> left, IReadOnlyList<ProjectTreeSelectorSegment> right)
        => left.Count == right.Count && left.Zip(right).All(p => p.First.NodeType == p.Second.NodeType && Names.Equals(p.First.Name, p.Second.Name));

    private static ProjectTreeSelectorSegment Segment(string type, string name) => new() { NodeType = type, Name = name };

    private static string TablePath(Table table) => table.Folder == "/" ? "/" + table.Name : $"{table.Folder}/{table.Name}";

    private static string CpuKey(string name) => "cpu:" + name.ToUpperInvariant();

    private static string TableKey(string name) => "table:" + name.ToUpperInvariant();

    private static string GroupKey(string path) => "group:" + path.ToUpperInvariant();

    private static string ParentPath(string path) => path.Contains('/') ? path[..path.LastIndexOf('/')] : string.Empty;

    /// <summary>Keys of the group at <paramref name="path"/> and its ancestors below the owner's root group.</summary>
    private static IEnumerable<string> GroupKeys(string path)
    {
        var segments = path.Split('/');
        var rootLength = segments.Length > 1 && Names.Equals(segments[1], "Units") ? 4 : 2;
        for (var length = rootLength + 1; length <= segments.Length; length++)
            yield return GroupKey(string.Join("/", segments.Take(length)));
    }

    private static string NormalizeFolder(string? folderPath)
    {
        var segments = (folderPath ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length == 0 ? "/" : "/" + string.Join("/", segments);
    }

    private static string NormalizeAddress(string? address) => string.Concat((address ?? string.Empty).Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

    private static string BlockNodeType(string blockType) => blockType switch
    {
        "OB" => ProjectTreeNodeTypes.Ob, "FB" => ProjectTreeNodeTypes.Fb, "FC" => ProjectTreeNodeTypes.Fc,
        "GlobalDB" => ProjectTreeNodeTypes.GlobalDb, _ => ProjectTreeNodeTypes.Block,
    };

    private static TagInfo Copy(TagInfo tag) => new()
    {
        Name = tag.Name, DataType = tag.DataType, LogicalAddress = tag.LogicalAddress,
        ExternalAccessible = tag.ExternalAccessible, ExternalVisible = tag.ExternalVisible, ExternalWritable = tag.ExternalWritable,
    };
}
