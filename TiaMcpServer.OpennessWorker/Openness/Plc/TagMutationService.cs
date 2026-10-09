using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Tags;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Plc;

public static class TagMutationService
{
    public static TagMutationResultInfo CreateTagTable(
        ProjectBase project,
        string? plcName,
        string tableName,
        string? folderPath)
    {
        RequireName(tableName, "TableName");

        var (plc, group) = ResolveGroup(project, plcName, folderPath);
        PlcWritePreconditions.RequireTableNameFree(plc, tableName);

        var table = group.TagTables.Create(tableName);
        return Result("create_tag_table", project, plc.Name, table.Name, NormalizeFolderPath(folderPath), null, null);
    }

    public static TagMutationResultInfo DeleteTagTable(
        ProjectBase project,
        string? plcName,
        string tableName,
        string? folderPath)
    {
        var (plc, table) = ResolveTable(project, plcName, tableName, folderPath);
        if (table.IsDefault)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.WorkerOperationFailed,
                $"Tag table '{tableName}' is the default tag table and cannot be deleted.");
        }

        table.Delete();
        return Result("delete_tag_table", project, plc.Name, tableName, NormalizeFolderPath(folderPath), null, null);
    }

    public static TagMutationResultInfo CreateTag(
        ProjectBase project,
        string? plcName,
        string tableName,
        string? folderPath,
        string name,
        string dataType,
        string? logicalAddress)
    {
        RequireName(name, "Name");
        RequireName(dataType, "DataType");

        var (plc, table) = ResolveTable(project, plcName, tableName, folderPath);
        PlcWritePreconditions.RequireCpuNameFree(plc, name, null);

        var tag = table.Tags.Create(name, dataType, logicalAddress ?? string.Empty);
        return Result("create_tag", project, plc.Name, table.Name, NormalizeFolderPath(folderPath), tag.Name, null);
    }

    public static TagMutationResultInfo UpdateTag(
        ProjectBase project,
        string? plcName,
        string tableName,
        string? folderPath,
        string name,
        string? newName,
        string? dataType,
        string? logicalAddress,
        bool? externalAccessible,
        bool? externalVisible,
        bool? externalWritable,
        bool? isSafety)
    {
        RequireName(name, "Name");

        if (isSafety.HasValue)
        {
            // Reject before any mutation is applied so the operation does not partially succeed.
            throw new InvalidOperationException("Updating IsSafety is not supported by the available TIA Openness API.");
        }

        var resolved = TagTargetResolver.Resolve(project, plcName, tableName, folderPath, name);
        var table = resolved.Table;
        var tag = resolved.Tag;

        if (!string.IsNullOrWhiteSpace(newName))
        {
            PlcWritePreconditions.RequireCpuNameFree(
                resolved.Plc, newName!, tag.Name, PlcWritePreconditions.TableContainer(resolved.FolderPath, table.Name));
        }

        PlcWritePreconditions.RequireReadableFlags(tag, externalAccessible, externalVisible, externalWritable);

        if (!string.IsNullOrWhiteSpace(newName))
        {
            tag.Name = newName!;
        }

        if (!string.IsNullOrWhiteSpace(dataType))
        {
            tag.DataTypeName = dataType!;
        }

        if (logicalAddress is not null)
        {
            tag.LogicalAddress = logicalAddress;
        }

        if (externalAccessible.HasValue)
        {
            tag.ExternalAccessible = externalAccessible.Value;
        }

        if (externalVisible.HasValue)
        {
            tag.ExternalVisible = externalVisible.Value;
        }

        if (externalWritable.HasValue)
        {
            tag.ExternalWritable = externalWritable.Value;
        }

        return Result("update_tag", project, resolved.PlcName, table.Name, resolved.FolderPath, tag.Name, null);
    }

    public static TagMutationResultInfo DeleteTag(
        ProjectBase project,
        string? plcName,
        string tableName,
        string? folderPath,
        string name)
    {
        RequireName(name, "Name");

        var (plc, table) = ResolveTable(project, plcName, tableName, folderPath);
        var tag = table.Tags.Find(name) ??
            throw NotFound($"Tag '{name}' was not found in tag table '{tableName}'.");

        tag.Delete();
        return Result("delete_tag", project, plc.Name, table.Name, NormalizeFolderPath(folderPath), name, null);
    }

    public static TagMutationResultInfo CreateUserConstant(
        ProjectBase project,
        string? plcName,
        string tableName,
        string? folderPath,
        string name,
        string dataType,
        string value)
    {
        RequireName(name, "Name");
        RequireName(dataType, "DataType");

        var (plc, table) = ResolveTable(project, plcName, tableName, folderPath);
        PlcWritePreconditions.RequireCpuNameFree(plc, name, null);

        var constant = table.UserConstants.Create(name);
        constant.DataTypeName = dataType;
        constant.Value = value;

        return Result("create_user_constant", project, plc.Name, table.Name, NormalizeFolderPath(folderPath), null, constant.Name);
    }

    public static TagMutationResultInfo UpdateUserConstant(
        ProjectBase project,
        string? plcName,
        string tableName,
        string? folderPath,
        string name,
        string? dataType,
        string? value)
    {
        RequireName(name, "Name");

        var (plc, table) = ResolveTable(project, plcName, tableName, folderPath);
        var constant = table.UserConstants.Find(name) ??
            throw NotFound($"User constant '{name}' was not found in tag table '{tableName}'.");
        PlcWritePreconditions.RequireReadableValue(constant);

        if (!string.IsNullOrWhiteSpace(dataType))
        {
            constant.DataTypeName = dataType!;
        }

        if (value is not null)
        {
            constant.Value = value;
        }

        return Result("update_user_constant", project, plc.Name, table.Name, NormalizeFolderPath(folderPath), null, constant.Name);
    }

    public static TagMutationResultInfo DeleteUserConstant(
        ProjectBase project,
        string? plcName,
        string tableName,
        string? folderPath,
        string name)
    {
        RequireName(name, "Name");

        var (plc, table) = ResolveTable(project, plcName, tableName, folderPath);
        var constant = table.UserConstants.Find(name) ??
            throw NotFound($"User constant '{name}' was not found in tag table '{tableName}'.");
        PlcWritePreconditions.RequireReadableValue(constant);

        constant.Delete();
        return Result("delete_user_constant", project, plc.Name, table.Name, NormalizeFolderPath(folderPath), null, name);
    }

    private static (PlcSoftware Plc, PlcTagTable Table) ResolveTable(
        ProjectBase project,
        string? plcName,
        string tableName,
        string? folderPath)
    {
        RequireName(tableName, "TableName");

        var (plc, group) = ResolveGroup(project, plcName, folderPath);
        var table = group.TagTables.Find(tableName) ??
            throw NotFound($"Tag table '{tableName}' was not found in '{NormalizeFolderPath(folderPath)}'.");
        return (plc, table);
    }

    private static (PlcSoftware Plc, PlcTagTableGroup Group) ResolveGroup(
        ProjectBase project,
        string? plcName,
        string? folderPath)
    {
        var plcSoftware = PlcSoftwareLocator.FindUnique(project, plcName).Software;
        PlcTagTableGroup group = plcSoftware.TagTableGroup;

        foreach (var segment in SplitFolderPath(folderPath))
        {
            group = group.Groups.Find(segment) ??
                throw NotFound($"Tag table folder '{NormalizeFolderPath(folderPath)}' was not found.");
        }

        return (plcSoftware, group);
    }

    private static WorkerOperationException NotFound(string message)
        => new(WorkerFailureCategories.TargetNotFound, message);

    private static string[] SplitFolderPath(string? folderPath)
    {
        var trimmed = folderPath?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "/")
        {
            return Array.Empty<string>();
        }

        return trimmed!
            .Trim('/')
            .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string NormalizeFolderPath(string? folderPath)
    {
        var segments = SplitFolderPath(folderPath);
        return segments.Length == 0
            ? "/"
            : "/" + string.Join("/", segments);
    }

    private static void RequireName(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{fieldName} is required.");
        }
    }

    private static TagMutationResultInfo Result(
        string operation,
        ProjectBase project,
        string plcName,
        string tableName,
        string folderPath,
        string? tagName,
        string? userConstantName)
    {
        return new TagMutationResultInfo
        {
            Operation = operation,
            ProjectPath = project.Path?.FullName,
            PlcName = plcName,
            TableName = tableName,
            FolderPath = folderPath,
            TagName = tagName,
            UserConstantName = userConstantName
        };
    }
}
