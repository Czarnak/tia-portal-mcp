using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Plc;

/// <summary>
/// Fail-closed checks the worker applies at mutation time (spec Appendix A rules 3, 4, 6, 8, 9).
/// A collision or occupied target means the project changed after planning, so it is <c>state_changed</c>;
/// unreadable evidence is <c>worker_operation_failed</c>.
/// </summary>
internal static class PlcWritePreconditions
{
    /// <summary>
    /// The name must be free among every tag, user constant, block and system block of the CPU.
    /// <paramref name="currentName"/>/<paramref name="currentContainer"/> identify the tag being renamed,
    /// which does not collide with itself.
    /// </summary>
    internal static void RequireCpuNameFree(
        PlcSoftware plc,
        string name,
        string? currentName,
        string? currentContainer = null)
    {
        var self = currentName is null
            ? null
            : new PlcNamedObject(PlcNameRules.Tag, currentName, currentContainer);
        var collision = PlcNameRules.FindCollision(name, CpuOccupants(plc), self);
        if (collision is not null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.StateChanged,
                $"The name '{name}' is already used by a {Describe(collision.Kind)} in PLC '{plc.Name}'. The project changed after the write was planned.");
        }
    }

    /// <summary>A tag table name must be unused in every folder of the PLC.</summary>
    internal static void RequireTableNameFree(PlcSoftware plc, string tableName)
    {
        var tables = new List<PlcNamedObject>();
        CollectTables(plc.TagTableGroup, "/", tables);
        if (PlcNameRules.FindCollision(tableName, tables) is { } collision)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.StateChanged,
                $"Tag table '{tableName}' already exists in folder '{collision.Container}' of PLC '{plc.Name}'. The project changed after the write was planned.");
        }
    }

    /// <summary>
    /// The target name must be unused in the parent group by a block or a group. A create finding one
    /// means the project changed after planning, so it is <c>state_changed</c>.
    /// </summary>
    internal static void RequireBlockAbsent(PlcBlockGroup group, string name)
        => RequireNameUnoccupied(group, name, "Block");

    /// <summary>A new block group's name must be unused in the parent group by a block or a group.</summary>
    internal static void RequireGroupNameFree(PlcBlockGroup group, string name)
        => RequireNameUnoccupied(group, name, "Block group");

    private static void RequireNameUnoccupied(PlcBlockGroup group, string name, string what)
    {
        string? occupant = null;
        if (group.Blocks.Find(name) is not null)
        {
            occupant = "block";
        }
        else
        {
            foreach (PlcBlockGroup child in group.Groups)
            {
                if (string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    occupant = "block group";
                    break;
                }
            }
        }

        if (occupant is not null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.StateChanged,
                $"{what} '{name}' cannot be created: its parent group '{group.Name}' already contains a {occupant} with that name. The project changed after the write was planned.");
        }
    }

    /// <summary>
    /// Every descendant block and group of a group about to be deleted must be enumerable and named,
    /// otherwise the consequence of the delete cannot be stated.
    /// </summary>
    internal static void RequireReadableDescendants(PlcBlockGroup group)
    {
        try
        {
            foreach (PlcBlock block in group.Blocks)
            {
                _ = block.Name;
            }

            foreach (PlcBlockGroup child in group.Groups)
            {
                _ = child.Name;
                RequireReadableDescendants(child);
            }
        }
        catch (EngineeringException ex)
        {
            throw Unreadable($"The contents of block group '{SafeName(group)}' could not be read, so deleting it is not safe: {ex.Message}");
        }
    }

    private static string SafeName(PlcBlockGroup group)
    {
        try
        {
            return group.Name;
        }
        catch (EngineeringException)
        {
            return "<unreadable>";
        }
    }

    /// <summary>
    /// The hash a content write was planned against must match the fresh export in the write's own
    /// format. A missing, malformed or differently formatted hash is a <c>validation_error</c>; a
    /// different hash means the document changed after planning, so it is <c>state_changed</c>.
    /// </summary>
    internal static void RequireContentHash(string? expected, string format, string freshExport)
    {
        if (!ContentHashRules.TryParse(expected, out var expectedFormat, out _))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "ExpectedContentHash is required and must be '<format>:sha256:<64 hex>'.");
        }

        if (!string.Equals(expectedFormat, format, StringComparison.Ordinal))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                $"ExpectedContentHash is a '{expectedFormat}' hash but the write format is '{format}'.");
        }

        if (!string.Equals(ContentHashRules.Compute(format, freshExport), expected, StringComparison.Ordinal))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.StateChanged,
                "The content changed after the write was planned; the expected content hash no longer matches the project. Nothing was imported.");
        }
    }

    internal static void RequireReadableValue(PlcUserConstant constant)
    {
        object? value;
        try
        {
            value = constant.Value;
        }
        catch (EngineeringException ex)
        {
            throw Unreadable($"The value of user constant '{constant.Name}' could not be read: {ex.Message}");
        }

        if (value is null)
        {
            throw Unreadable($"The value of user constant '{constant.Name}' is unavailable.");
        }
    }

    /// <summary>Every external-access flag the request sets must be readable on the current tag.</summary>
    internal static void RequireReadableFlags(PlcTag tag, bool? accessible, bool? visible, bool? writable)
    {
        RequireReadable(tag, accessible, "ExternalAccessible", () => tag.ExternalAccessible);
        RequireReadable(tag, visible, "ExternalVisible", () => tag.ExternalVisible);
        RequireReadable(tag, writable, "ExternalWritable", () => tag.ExternalWritable);
    }

    private static void RequireReadable(PlcTag tag, bool? requested, string attribute, Func<bool> read)
    {
        if (!requested.HasValue)
        {
            return;
        }

        try
        {
            read();
        }
        catch (Exception ex) when (ex is NotSupportedException or EngineeringNotSupportedException)
        {
            throw Unreadable($"{attribute} cannot be read on tag '{tag.Name}', so it cannot be changed safely.");
        }
    }

    private static WorkerOperationException Unreadable(string message)
        => new(WorkerFailureCategories.WorkerOperationFailed, message);

    private static string Describe(string kind) => kind switch
    {
        PlcNameRules.Tag => "tag",
        PlcNameRules.UserConstant => "user constant",
        PlcNameRules.SystemBlock => "system block",
        PlcNameRules.Block => "block",
        _ => kind
    };

    /// <summary>Container of a tag or constant occupant: the table's folder path plus its name.</summary>
    internal static string TableContainer(string folderPath, string tableName)
        => (folderPath == "/" ? "/" : folderPath + "/") + tableName;

    private static IEnumerable<PlcNamedObject> CpuOccupants(PlcSoftware plc)
    {
        var tables = new List<(PlcTagTable Table, string Folder)>();
        CollectTableObjects(plc.TagTableGroup, "/", tables);
        foreach (var (table, folder) in tables)
        {
            var container = TableContainer(folder, table.Name);
            foreach (PlcTag tag in table.Tags)
            {
                yield return new PlcNamedObject(PlcNameRules.Tag, tag.Name, container);
            }

            foreach (PlcUserConstant constant in table.UserConstants)
            {
                yield return new PlcNamedObject(PlcNameRules.UserConstant, constant.Name, container);
            }
        }

        foreach (var block in BlockOccupants(plc.BlockGroup, "/"))
        {
            yield return block;
        }

        // Software-unit namespaces need separate effective-name resolution and are not guessed here.
    }

    private static IEnumerable<PlcNamedObject> BlockOccupants(PlcBlockGroup group, string path)
    {
        foreach (PlcBlock block in group.Blocks)
        {
            yield return new PlcNamedObject(PlcNameRules.Block, block.Name, path);
        }

        foreach (PlcBlockGroup child in group.Groups)
        {
            foreach (var block in BlockOccupants(child, path + child.Name + "/"))
            {
                yield return block;
            }
        }

        if (group is PlcBlockSystemGroup root)
        {
            foreach (PlcSystemBlockGroup child in root.SystemBlockGroups)
            {
                foreach (var block in SystemBlockOccupants(child, "/System blocks/" + child.Name + "/"))
                {
                    yield return block;
                }
            }
        }
    }

    private static IEnumerable<PlcNamedObject> SystemBlockOccupants(PlcSystemBlockGroup group, string path)
    {
        foreach (PlcBlock block in group.Blocks)
        {
            yield return new PlcNamedObject(PlcNameRules.SystemBlock, block.Name, path);
        }

        foreach (PlcSystemBlockGroup child in group.Groups)
        {
            foreach (var block in SystemBlockOccupants(child, path + child.Name + "/"))
            {
                yield return block;
            }
        }
    }

    private static void CollectTables(PlcTagTableGroup group, string folder, List<PlcNamedObject> into)
    {
        var tables = new List<(PlcTagTable Table, string Folder)>();
        CollectTableObjects(group, folder, tables);
        into.AddRange(tables.Select(t => new PlcNamedObject(PlcNameRules.TagTable, t.Table.Name, t.Folder)));
    }

    private static void CollectTableObjects(
        PlcTagTableGroup group,
        string folder,
        List<(PlcTagTable Table, string Folder)> into)
    {
        foreach (PlcTagTable table in group.TagTables)
        {
            into.Add((table, folder));
        }

        foreach (PlcTagTableGroup child in group.Groups)
        {
            CollectTableObjects(child, (folder == "/" ? "/" : folder + "/") + child.Name, into);
        }
    }
}
