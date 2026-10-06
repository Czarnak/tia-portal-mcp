using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

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
