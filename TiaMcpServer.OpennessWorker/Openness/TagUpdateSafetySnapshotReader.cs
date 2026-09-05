using Siemens.Engineering;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

internal static class TagUpdateSafetySnapshotReader
{
    internal static TagUpdateSafetySnapshot Read(
        Project project,
        string? plcName,
        string tableName,
        string? folderPath,
        string name)
    {
        var resolved = TagTargetResolver.Resolve(project, plcName, tableName, folderPath, name);
        return new TagUpdateSafetySnapshot(
            resolved.PlcName,
            resolved.FolderPath,
            resolved.Table.Name,
            resolved.Tag.Name,
            resolved.Tag.DataTypeName,
            resolved.Tag.LogicalAddress,
            ReadOptionalFlag(() => resolved.Tag.ExternalAccessible),
            ReadOptionalFlag(() => resolved.Tag.ExternalVisible),
            ReadOptionalFlag(() => resolved.Tag.ExternalWritable));
    }

    private static bool? ReadOptionalFlag(Func<bool> read)
    {
        try
        {
            return read();
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
