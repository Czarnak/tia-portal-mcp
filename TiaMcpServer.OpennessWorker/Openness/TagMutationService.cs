using Siemens.Engineering;
using Siemens.Engineering.SW.Tags;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

public static class TagMutationService
{
    public static TagMutationResultInfo CreateTagTable(
        Project project,
        string? plcName,
        string tableName,
        string? folderPath)
    {
        RequireName(tableName, "TableName");

        var group = ResolveGroup(project, plcName, folderPath);
        if (group.TagTables.Find(tableName) is not null)
        {
            throw new InvalidOperationException($"Tag table '{tableName}' already exists in '{NormalizeFolderPath(folderPath)}'.");
        }

        var table = group.TagTables.Create(tableName);
        return Result("create_tag_table", project, plcName, table.Name, NormalizeFolderPath(folderPath), null, null);
    }

    public static TagMutationResultInfo DeleteTagTable(
        Project project,
        string? plcName,
        string tableName,
        string? folderPath)
    {
        var table = ResolveTable(project, plcName, tableName, folderPath);
        if (table.IsDefault)
        {
            throw new InvalidOperationException($"Tag table '{tableName}' is the default tag table and cannot be deleted.");
        }

        table.Delete();
        return Result("delete_tag_table", project, plcName, tableName, NormalizeFolderPath(folderPath), null, null);
    }

    public static TagMutationResultInfo CreateTag(
        Project project,
        string? plcName,
        string tableName,
        string? folderPath,
        string name,
        string dataType,
        string? logicalAddress)
    {
        RequireName(name, "Name");
        RequireName(dataType, "DataType");

        var table = ResolveTable(project, plcName, tableName, folderPath);
        if (table.Tags.Find(name) is not null)
        {
            throw new InvalidOperationException($"Tag '{name}' already exists in tag table '{tableName}'.");
        }

        var tag = table.Tags.Create(name, dataType, logicalAddress ?? string.Empty);
        return Result("create_tag", project, plcName, table.Name, NormalizeFolderPath(folderPath), tag.Name, null);
    }

    public static TagMutationResultInfo UpdateTag(
        Project project,
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

        var table = ResolveTable(project, plcName, tableName, folderPath);
        var tag = table.Tags.Find(name) ??
            throw new InvalidOperationException($"Tag '{name}' was not found in tag table '{tableName}'.");

        if (!string.IsNullOrWhiteSpace(newName) &&
            !string.Equals(name, newName, StringComparison.OrdinalIgnoreCase) &&
            table.Tags.Find(newName) is not null)
        {
            throw new InvalidOperationException($"Tag '{newName}' already exists in tag table '{tableName}'.");
        }

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

        return Result("update_tag", project, plcName, table.Name, NormalizeFolderPath(folderPath), tag.Name, null);
    }

    public static TagMutationResultInfo DeleteTag(
        Project project,
        string? plcName,
        string tableName,
        string? folderPath,
        string name)
    {
        RequireName(name, "Name");

        var table = ResolveTable(project, plcName, tableName, folderPath);
        var tag = table.Tags.Find(name) ??
            throw new InvalidOperationException($"Tag '{name}' was not found in tag table '{tableName}'.");

        tag.Delete();
        return Result("delete_tag", project, plcName, table.Name, NormalizeFolderPath(folderPath), name, null);
    }

    public static TagMutationResultInfo CreateUserConstant(
        Project project,
        string? plcName,
        string tableName,
        string? folderPath,
        string name,
        string dataType,
        string value)
    {
        RequireName(name, "Name");
        RequireName(dataType, "DataType");

        var table = ResolveTable(project, plcName, tableName, folderPath);
        if (table.UserConstants.Find(name) is not null)
        {
            throw new InvalidOperationException($"User constant '{name}' already exists in tag table '{tableName}'.");
        }

        var constant = table.UserConstants.Create(name);
        constant.DataTypeName = dataType;
        constant.Value = value;

        return Result("create_user_constant", project, plcName, table.Name, NormalizeFolderPath(folderPath), null, constant.Name);
    }

    public static TagMutationResultInfo UpdateUserConstant(
        Project project,
        string? plcName,
        string tableName,
        string? folderPath,
        string name,
        string? dataType,
        string? value)
    {
        RequireName(name, "Name");

        var table = ResolveTable(project, plcName, tableName, folderPath);
        var constant = table.UserConstants.Find(name) ??
            throw new InvalidOperationException($"User constant '{name}' was not found in tag table '{tableName}'.");

        if (!string.IsNullOrWhiteSpace(dataType))
        {
            constant.DataTypeName = dataType!;
        }

        if (value is not null)
        {
            constant.Value = value;
        }

        return Result("update_user_constant", project, plcName, table.Name, NormalizeFolderPath(folderPath), null, constant.Name);
    }

    public static TagMutationResultInfo DeleteUserConstant(
        Project project,
        string? plcName,
        string tableName,
        string? folderPath,
        string name)
    {
        RequireName(name, "Name");

        var table = ResolveTable(project, plcName, tableName, folderPath);
        var constant = table.UserConstants.Find(name) ??
            throw new InvalidOperationException($"User constant '{name}' was not found in tag table '{tableName}'.");

        constant.Delete();
        return Result("delete_user_constant", project, plcName, table.Name, NormalizeFolderPath(folderPath), null, name);
    }

    private static PlcTagTable ResolveTable(
        Project project,
        string? plcName,
        string tableName,
        string? folderPath)
    {
        RequireName(tableName, "TableName");

        var group = ResolveGroup(project, plcName, folderPath);
        return group.TagTables.Find(tableName) ??
            throw new InvalidOperationException($"Tag table '{tableName}' was not found in '{NormalizeFolderPath(folderPath)}'.");
    }

    private static PlcTagTableGroup ResolveGroup(Project project, string? plcName, string? folderPath)
    {
        var plcSoftware = PlcSoftwareLocator.Find(project, plcName);
        PlcTagTableGroup group = plcSoftware.TagTableGroup;

        foreach (var segment in SplitFolderPath(folderPath))
        {
            group = group.Groups.Find(segment) ??
                throw new InvalidOperationException($"Tag table folder '{NormalizeFolderPath(folderPath)}' was not found.");
        }

        return group;
    }

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
        Project project,
        string? plcName,
        string tableName,
        string folderPath,
        string? tagName,
        string? userConstantName)
    {
        return new TagMutationResultInfo
        {
            Operation = operation,
            ProjectPath = project.Path?.FullName,
            PlcName = plcName ?? string.Empty,
            TableName = tableName,
            FolderPath = folderPath,
            TagName = tagName,
            UserConstantName = userConstantName
        };
    }
}
