using System.Security.Cryptography;
using System.Text;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

// Siemens-free shaping: no parsing, rewriting, or removal of exported XML content.
internal static class TagOperationSafetySnapshotBuilder
{
    // Consume discovery past the first match: a later match or discovery error must fail closed.
    internal static T ResolveUniquePlc<T>(IEnumerable<T> matches)
    {
        using var enumerator = matches.GetEnumerator();
        if (!enumerator.MoveNext())
            throw new InvalidOperationException("No PLC software matched the safety-read selector.");

        var match = enumerator.Current;
        if (enumerator.MoveNext())
            throw new InvalidOperationException("Multiple PLC software instances matched the safety-read selector. Specify an unambiguous PLC selector.");

        return match;
    }

    internal static string NormalizeFolderPath(string? folderPath)
    {
        var parts = (folderPath ?? string.Empty).Trim().Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "/" : "/" + string.Join("/", parts);
    }

    internal static TagTableSafetyIdentityInfo BuildTableIdentity(string plcName, string? folderPath, string tableName)
    {
        var folder = NormalizeFolderPath(folderPath);
        var path = plcName + "/Tag tables" + (folder == "/" ? "/" : folder + "/") + tableName;
        return new TagTableSafetyIdentityInfo(plcName, folder, tableName, path);
    }

    internal static TagSafetyIdentityInfo BuildTagIdentity(
        TagTableSafetyIdentityInfo table, string name, string dataType, string? logicalAddress,
        bool? externalAccessible, bool? externalVisible, bool? externalWritable)
        => new(table.PlcName, table.FolderPath, table.TableName, name, table.CanonicalPath + "/" + name,
            dataType, logicalAddress, externalAccessible, externalVisible, externalWritable);

    internal static UserConstantSafetyIdentityInfo BuildConstantIdentity(
        TagTableSafetyIdentityInfo table, string name, string dataType, string value)
        => new(table.PlcName, table.FolderPath, table.TableName, name, table.CanonicalPath + "/" + name, dataType, value);

    internal static IReadOnlyList<TagCollisionProbeInfo> OrderCollisions(IEnumerable<TagCollisionProbeInfo> collisions)
        => collisions.OrderBy(x => x.CanonicalPath, StringComparer.Ordinal)
            .ThenBy(x => x.Kind, StringComparer.Ordinal)
            .ThenBy(x => x.CandidateName, StringComparer.Ordinal)
            .ThenBy(x => x.LogicalAddress, StringComparer.Ordinal)
            .ThenBy(x => x.IsTarget).ToArray();

    internal static IReadOnlyList<TagCollisionProbeInfo> SelectCollisions(
        string kind, IEnumerable<TagCollisionProbeInfo> candidates, string? requestedValue, string? targetPath)
        => string.IsNullOrEmpty(requestedValue) ? Array.Empty<TagCollisionProbeInfo>() :
            OrderCollisions(candidates.Where(x => string.Equals(
                    kind == "logical-address" ? x.LogicalAddress : x.CandidateName,
                    requestedValue, StringComparison.OrdinalIgnoreCase))
                .Select(x => x with
                {
                    Kind = kind,
                    IsTarget = string.Equals(x.CanonicalPath, targetPath, StringComparison.Ordinal)
                }));

    internal static DeleteTagTableSafetySnapshotInfo BuildDeleteTagTableSnapshot(TagTableSafetyIdentityInfo table, string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            throw new InvalidOperationException("The tag-table export is empty.");
        using var sha = SHA256.Create();
        var hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(xml))).Replace("-", "").ToLowerInvariant();
        return new DeleteTagTableSafetySnapshotInfo(table, xml, hash, xml.Length);
    }
}
