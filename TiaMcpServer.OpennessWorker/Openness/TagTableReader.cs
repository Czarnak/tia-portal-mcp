using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Tags;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

public static class TagTableReader
{
    /// <summary>
    /// Reads every tag table of the PLC software selected by <paramref name="plcName"/> via
    /// <see cref="PlcSoftwareLocator.Find(Project, string)"/>. Kept for the tag-table tools;
    /// the I/O-map path resolves the PLC deterministically first and calls
    /// <see cref="ReadAll(PlcSoftware)"/> directly so it never depends on the first-match lookup.
    /// </summary>
    public static List<TagTableInfo> ReadAll(Project project, string? plcName)
    {
        var plcSoftware = PlcSoftwareLocator.Find(project, plcName);

        return ReadAll(plcSoftware);
    }

    /// <summary>
    /// Reads every tag table of one already-selected <see cref="PlcSoftware"/>. The caller owns
    /// deterministic PLC selection; this method only walks the tag-table tree.
    /// </summary>
    public static List<TagTableInfo> ReadAll(PlcSoftware plcSoftware)
    {
        var result = new List<TagTableInfo>();

        CollectTablesFromGroup(plcSoftware.TagTableGroup, folderPath: "/", result);

        return result;
    }

    /// <summary>
    /// Reads the complete tag inventory of every PLC selected by <paramref name="plcName"/> (all
    /// PLCs when null), including PLCs in device groups. Unlike <see cref="ReadAll(PlcSoftware)"/>
    /// it reads the external-access flags and never skips silently: every table, group, tag or
    /// constant that cannot be read yields one message and <c>IsComplete = false</c>.
    /// </summary>
    public static PlcTagInventoryInfo ReadInventory(Project project, string? plcName)
    {
        var inventory = new PlcTagInventoryInfo();
        foreach (var discovered in PlcSoftwareLocator.FindEveryPlc(project, plcName))
        {
            var plc = new PlcTagInventoryPlcInfo
            {
                PlcName = discovered.Software.Name,
                DeviceName = discovered.DeviceName
            };
            InventoryGroup(discovered.Software.TagTableGroup, "/", plc.Tables, inventory.Messages);
            inventory.Plcs.Add(plc);
        }

        if (inventory.Plcs.Count == 0)
        {
            var detail = plcName is not null ? $" named '{plcName}'" : string.Empty;
            throw new WorkerOperationException(
                WorkerFailureCategories.WorkerOperationFailed,
                $"No PLC software{detail} was found in the project.");
        }

        inventory.IsComplete = inventory.Messages.Count == 0;
        return inventory;
    }

    private static void InventoryGroup(
        PlcTagTableGroup group,
        string folderPath,
        List<TagTableInfo> tables,
        List<string> messages)
    {
        var where = $"tag table group '{folderPath}'";
        ReadEach(group.TagTables, $"a tag table in {where}", messages, table =>
        {
            tables.Add(new TagTableInfo
            {
                Name = table.Name,
                FolderPath = folderPath,
                IsDefault = table.IsDefault,
                Tags = InventoryTags(table, messages),
                UserConstants = InventoryConstants(table, messages)
            });
        });

        ReadEach(group.Groups, $"a nested group in {where}", messages, child =>
        {
            var childPath = folderPath == "/" ? $"/{child.Name}" : $"{folderPath}/{child.Name}";
            InventoryGroup(child, childPath, tables, messages);
        });
    }

    private static List<TagInfo> InventoryTags(PlcTagTable table, List<string> messages)
    {
        var tags = new List<TagInfo>();
        ReadEach(table.Tags, $"a tag in tag table '{table.Name}'", messages, tag =>
        {
            var name = tag.Name;
            var flagContext = $"tag '{name}' in tag table '{table.Name}'";
            tags.Add(new TagInfo
            {
                Name = name,
                DataType = tag.DataTypeName,
                LogicalAddress = tag.LogicalAddress,
                ExternalAccessible = ReadFlag(() => tag.ExternalAccessible, "ExternalAccessible", flagContext, messages),
                ExternalVisible = ReadFlag(() => tag.ExternalVisible, "ExternalVisible", flagContext, messages),
                ExternalWritable = ReadFlag(() => tag.ExternalWritable, "ExternalWritable", flagContext, messages)
            });
        });

        return tags;
    }

    private static List<UserConstantInfo> InventoryConstants(PlcTagTable table, List<string> messages)
    {
        var constants = new List<UserConstantInfo>();
        ReadEach(table.UserConstants, $"a user constant in tag table '{table.Name}'", messages, constant =>
        {
            var info = new UserConstantInfo { Name = constant.Name, DataType = constant.DataTypeName, Value = null };
            try
            {
                info.Value = constant.Value?.ToString();
            }
            catch (EngineeringException ex)
            {
                messages.Add($"The value of user constant '{info.Name}' in tag table '{table.Name}' could not be read: {ex.Message}");
            }

            constants.Add(info);
        });

        return constants;
    }

    // NotSupportedException means the attribute does not exist for this tag: null without a message.
    private static bool? ReadFlag(Func<bool> read, string flag, string context, List<string> messages)
    {
        try
        {
            return read();
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (EngineeringException ex)
        {
            messages.Add($"{flag} of {context} could not be read: {ex.Message}");
            return null;
        }
    }

    // Reads every item; an item (or the enumeration itself) that throws is reported, never dropped silently.
    private static void ReadEach<T>(IEnumerable<T> items, string what, List<string> messages, Action<T> read)
    {
        try
        {
            foreach (var item in items)
            {
                try
                {
                    read(item);
                }
                catch (EngineeringException ex)
                {
                    messages.Add($"{what} could not be read: {ex.Message}");
                }
            }
        }
        catch (EngineeringException ex)
        {
            messages.Add($"{what} could not be enumerated: {ex.Message}");
        }
    }

    private static void CollectTablesFromGroup(
        PlcTagTableGroup group,
        string folderPath,
        List<TagTableInfo> result)
    {
        foreach (PlcTagTable table in group.TagTables)
        {
            try
            {
                result.Add(ReadTagTable(table, folderPath));
            }
            catch (EngineeringException ex)
            {
                Console.Error.WriteLine(
                    $"Skipping a tag table while reading tag table group '{group.Name}': {ex.Message}");
            }
        }

        foreach (PlcTagTableGroup childGroup in group.Groups)
        {
            try
            {
                var childPath = folderPath == "/"
                    ? $"/{childGroup.Name}"
                    : $"{folderPath}/{childGroup.Name}";

                CollectTablesFromGroup(childGroup, childPath, result);
            }
            catch (EngineeringException ex)
            {
                Console.Error.WriteLine(
                    $"Skipping a nested tag table group while reading tag table group '{group.Name}': {ex.Message}");
            }
        }
    }

    private static TagTableInfo ReadTagTable(PlcTagTable table, string folderPath)
    {
        var tagTableInfo = new TagTableInfo
        {
            Name = table.Name,
            FolderPath = folderPath,
            IsDefault = table.IsDefault,
            Tags = ReadTags(table),
            UserConstants = ReadUserConstants(table)
        };

        return tagTableInfo;
    }

    private static List<TagInfo> ReadTags(PlcTagTable table)
    {
        var tags = new List<TagInfo>();

        foreach (PlcTag tag in table.Tags)
        {
            try
            {
                tags.Add(new TagInfo
                {
                    Name = tag.Name,
                    DataType = tag.DataTypeName,
                    LogicalAddress = tag.LogicalAddress
                });
            }
            catch (EngineeringException ex)
            {
                Console.Error.WriteLine(
                    $"Skipping a tag while reading tag table '{table.Name}': {ex.Message}");
            }
        }

        return tags;
    }

    private static List<UserConstantInfo> ReadUserConstants(PlcTagTable table)
    {
        var constants = new List<UserConstantInfo>();

        foreach (PlcUserConstant c in table.UserConstants)
        {
            try
            {
                constants.Add(new UserConstantInfo
                {
                    Name = c.Name,
                    DataType = c.DataTypeName,
                    Value = c.Value?.ToString() ?? string.Empty
                });
            }
            catch (EngineeringException ex)
            {
                Console.Error.WriteLine(
                    $"Skipping a user constant while reading tag table '{table.Name}': {ex.Message}");
            }
        }

        return constants;
    }
}
