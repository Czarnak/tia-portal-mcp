using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Tags;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

// Strict safety reads: failures propagate. Public TagTableReader remains best-effort.
internal static class TagOperationSafetySnapshotReader
{
    internal static CreateTagTableSafetySnapshotInfo ReadCreateTagTable(Project project, WorkerRequest request)
    {
        RequireName(request.TableName, "TableName");
        var group = ResolveGroup(project, request);
        var occupied = group.Group.TagTables.Find(request.TableName!);
        var collisions = occupied is null ? Array.Empty<TagCollisionProbeInfo>() :
            new[] { new TagCollisionProbeInfo("table-name", occupied.Name,
                TagOperationSafetySnapshotBuilder.BuildTableIdentity(group.PlcName, group.FolderPath, occupied.Name).CanonicalPath,
                null, false) };
        return new CreateTagTableSafetySnapshotInfo(group.PlcName, group.FolderPath, request.TableName!, collisions);
    }

    internal static DeleteTagTableSafetySnapshotInfo ReadDeleteTagTable(Project project, WorkerRequest request)
    {
        var resolved = ResolveTable(project, request);
        var exportPath = Path.Combine(Path.GetTempPath(), "tia-mcp-tag-safety-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            // Verified in installed V21 Base.xml and Step7.xml: None exports no document info.
            resolved.Table.Export(new FileInfo(exportPath), ExportOptions.None, DocumentInfoOptions.None);
            return TagOperationSafetySnapshotBuilder.BuildDeleteTagTableSnapshot(resolved.Identity, File.ReadAllText(exportPath));
        }
        finally
        {
            if (File.Exists(exportPath))
                File.Delete(exportPath);
        }
    }

    internal static CreateTagSafetySnapshotInfo ReadCreateTag(Project project, WorkerRequest request)
    {
        RequireName(request.Name, "Name");
        RequireName(request.DataType, "DataType");
        var resolved = ResolveTable(project, request);
        var address = request.LogicalAddress ?? string.Empty;
        var candidates = TagCandidates(resolved).ToArray();
        return new CreateTagSafetySnapshotInfo(resolved.Identity, request.Name!, address,
            TagOperationSafetySnapshotBuilder.SelectCollisions("tag-name", candidates, request.Name, null),
            TagOperationSafetySnapshotBuilder.SelectCollisions("logical-address", candidates, address, null));
    }

    internal static UpdateTagSafetySnapshotInfo ReadUpdateTag(Project project, WorkerRequest request)
    {
        var resolved = ResolveTable(project, request);
        var target = ReadTag(resolved, request.Name);
        var name = string.IsNullOrWhiteSpace(request.NewName) ? target.TagName : request.NewName!;
        var address = request.LogicalAddress ?? target.LogicalAddress;
        var candidates = TagCandidates(resolved).ToArray();
        return new UpdateTagSafetySnapshotInfo(resolved.Identity, target, name, address,
            TagOperationSafetySnapshotBuilder.SelectCollisions("tag-name", candidates, name, target.CanonicalPath),
            TagOperationSafetySnapshotBuilder.SelectCollisions("logical-address", candidates, address, target.CanonicalPath));
    }

    internal static DeleteTagSafetySnapshotInfo ReadDeleteTag(Project project, WorkerRequest request)
    {
        var resolved = ResolveTable(project, request);
        return new DeleteTagSafetySnapshotInfo(resolved.Identity, ReadTag(resolved, request.Name));
    }

    internal static CreateUserConstantSafetySnapshotInfo ReadCreateUserConstant(Project project, WorkerRequest request)
    {
        RequireName(request.Name, "Name");
        var resolved = ResolveTable(project, request);
        return new CreateUserConstantSafetySnapshotInfo(resolved.Identity, request.Name!,
            TagOperationSafetySnapshotBuilder.SelectCollisions("user-constant-name", ConstantCandidates(resolved), request.Name, null));
    }

    internal static UpdateUserConstantSafetySnapshotInfo ReadUpdateUserConstant(Project project, WorkerRequest request)
    {
        var resolved = ResolveTable(project, request);
        var target = ReadConstant(resolved, request.Name);
        return new UpdateUserConstantSafetySnapshotInfo(resolved.Identity, target, target.ConstantName,
            TagOperationSafetySnapshotBuilder.SelectCollisions("user-constant-name", ConstantCandidates(resolved), target.ConstantName, target.CanonicalPath));
    }

    internal static DeleteUserConstantSafetySnapshotInfo ReadDeleteUserConstant(Project project, WorkerRequest request)
    {
        var resolved = ResolveTable(project, request);
        return new DeleteUserConstantSafetySnapshotInfo(resolved.Identity, ReadConstant(resolved, request.Name));
    }

    private static TagSafetyIdentityInfo ReadTag(ResolvedTable resolved, string? name)
    {
        RequireName(name, "Name");
        var tag = resolved.Table.Tags.Find(name!)
            ?? throw new InvalidOperationException($"Tag '{name}' was not found in tag table '{resolved.Table.Name}'.");
        return TagOperationSafetySnapshotBuilder.BuildTagIdentity(resolved.Identity, tag.Name, tag.DataTypeName, tag.LogicalAddress,
            ReadOptionalFlag(() => tag.ExternalAccessible),
            ReadOptionalFlag(() => tag.ExternalVisible),
            ReadOptionalFlag(() => tag.ExternalWritable));
    }

    private static UserConstantSafetyIdentityInfo ReadConstant(ResolvedTable resolved, string? name)
    {
        RequireName(name, "Name");
        var constant = resolved.Table.UserConstants.Find(name!)
            ?? throw new InvalidOperationException($"User constant '{name}' was not found in tag table '{resolved.Table.Name}'.");
        return TagOperationSafetySnapshotBuilder.BuildConstantIdentity(resolved.Identity, constant.Name, constant.DataTypeName,
            constant.Value?.ToString() ?? throw new InvalidOperationException("The target user-constant value is unavailable."));
    }

    private static bool? ReadOptionalFlag(Func<bool> read)
    {
        try { return read(); }
        catch (NotSupportedException) { return null; }
    }

    private static IEnumerable<TagCollisionProbeInfo> TagCandidates(ResolvedTable resolved)
    {
        foreach (var item in EnumerateTables(resolved.Group.RootGroup, "/"))
        {
            var table = TagOperationSafetySnapshotBuilder.BuildTableIdentity(resolved.Identity.PlcName, item.FolderPath, item.Table.Name);
            foreach (PlcTag tag in item.Table.Tags)
                yield return new TagCollisionProbeInfo("tag-name", tag.Name, table.CanonicalPath + "/" + tag.Name, tag.LogicalAddress, false);
        }
    }

    private static IEnumerable<TagCollisionProbeInfo> ConstantCandidates(ResolvedTable resolved)
    {
        foreach (var item in EnumerateTables(resolved.Group.RootGroup, "/"))
        {
            var table = TagOperationSafetySnapshotBuilder.BuildTableIdentity(resolved.Identity.PlcName, item.FolderPath, item.Table.Name);
            foreach (PlcUserConstant constant in item.Table.UserConstants)
                yield return new TagCollisionProbeInfo("user-constant-name", constant.Name, table.CanonicalPath + "/" + constant.Name, null, false);
        }
    }

    private static IEnumerable<TableInFolder> EnumerateTables(PlcTagTableGroup group, string folderPath)
    {
        foreach (PlcTagTable table in group.TagTables)
            yield return new TableInFolder(table, folderPath);
        foreach (PlcTagTableGroup child in group.Groups)
            foreach (var item in EnumerateTables(child, (folderPath == "/" ? "/" : folderPath + "/") + child.Name))
                yield return item;
    }

    private static ResolvedTable ResolveTable(Project project, WorkerRequest request)
    {
        RequireName(request.TableName, "TableName");
        var group = ResolveGroup(project, request);
        var table = group.Group.TagTables.Find(request.TableName!)
            ?? throw new InvalidOperationException($"Tag table '{request.TableName}' was not found in '{group.FolderPath}'.");
        return new ResolvedTable(group, table,
            TagOperationSafetySnapshotBuilder.BuildTableIdentity(group.PlcName, group.FolderPath, table.Name));
    }

    private static ResolvedGroup ResolveGroup(Project project, WorkerRequest request)
    {
        var plc = TagOperationSafetySnapshotBuilder.ResolveUniquePlc(
            DiscoverPlcSoftwareStrict(project, request.PlcName));
        PlcTagTableGroup group = plc.TagTableGroup;
        var actualSegments = new List<string>();
        foreach (var segment in TagOperationSafetySnapshotBuilder.NormalizeFolderPath(request.FolderPath)
            .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            group = group.Groups.Find(segment)
                ?? throw new InvalidOperationException($"Tag table folder '{request.FolderPath}' was not found.");
            actualSegments.Add(group.Name);
        }
        return new ResolvedGroup(plc.Name, plc.TagTableGroup, group,
            actualSegments.Count == 0 ? "/" : "/" + string.Join("/", actualSegments));
    }

    private static IEnumerable<PlcSoftware> DiscoverPlcSoftwareStrict(Project project, string? plcName)
    {
        // Includes grouped devices and propagates incomplete-discovery errors.
        foreach (var device in ProjectDeviceEnumerator.Enumerate(project))
        {
            foreach (var software in DiscoverPlcSoftwareStrict(device.DeviceItems))
            {
                if (plcName is null ||
                    string.Equals(software.Name, plcName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(device.Name, plcName, StringComparison.OrdinalIgnoreCase))
                    yield return software;
            }
        }
    }

    private static IEnumerable<PlcSoftware> DiscoverPlcSoftwareStrict(DeviceItemComposition items)
    {
        foreach (DeviceItem item in items)
        {
            var container = item.GetService<SoftwareContainer>();
            if (container?.Software is PlcSoftware software)
                yield return software;

            foreach (var child in DiscoverPlcSoftwareStrict(item.DeviceItems))
                yield return child;
        }
    }

    private static void RequireName(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(field + " is required.");
    }

    private sealed record ResolvedGroup(string PlcName, PlcTagTableGroup RootGroup, PlcTagTableGroup Group, string FolderPath);
    private sealed record ResolvedTable(ResolvedGroup Group, PlcTagTable Table, TagTableSafetyIdentityInfo Identity);
    private sealed record TableInFolder(PlcTagTable Table, string FolderPath);
}
