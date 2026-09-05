using Siemens.Engineering;
using Siemens.Engineering.SW.Tags;

namespace TiaMcpServer.OpennessWorker.Openness;

internal sealed record ResolvedTagTarget(
    string PlcName,
    string FolderPath,
    PlcTagTable Table,
    PlcTag Tag);

internal static class TagTargetResolver
{
    internal static ResolvedTagTarget Resolve(
        Project project,
        string? plcName,
        string tableName,
        string? folderPath,
        string name)
    {
        RequireName(tableName, "TableName");
        RequireName(name, "Name");

        var normalizedFolderPath = NormalizeFolderPath(folderPath);
        var plcSoftware = PlcSoftwareLocator.Find(project, plcName);
        PlcTagTableGroup group = plcSoftware.TagTableGroup;
        foreach (var segment in SplitFolderPath(folderPath))
        {
            group = group.Groups.Find(segment)
                ?? throw new InvalidOperationException($"Tag table folder '{normalizedFolderPath}' was not found.");
        }

        var table = group.TagTables.Find(tableName)
            ?? throw new InvalidOperationException($"Tag table '{tableName}' was not found in '{normalizedFolderPath}'.");
        var tag = table.Tags.Find(name)
            ?? throw new InvalidOperationException($"Tag '{name}' was not found in tag table '{tableName}'.");

        return new ResolvedTagTarget(plcSoftware.Name, normalizedFolderPath, table, tag);
    }

    internal static string NormalizeFolderPath(string? folderPath)
    {
        var segments = SplitFolderPath(folderPath);
        return segments.Length == 0 ? "/" : "/" + string.Join("/", segments);
    }

    private static string[] SplitFolderPath(string? folderPath)
    {
        var trimmed = folderPath?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "/")
        {
            return Array.Empty<string>();
        }

        return trimmed!.Trim('/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static void RequireName(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{fieldName} is required.");
        }
    }
}
