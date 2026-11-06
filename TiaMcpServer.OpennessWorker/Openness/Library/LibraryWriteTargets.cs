using Siemens.Engineering;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.MasterCopies;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Library;

internal static class LibraryWriteTargets
{
    internal static ILibrary Library(TiaPortal portal, ProjectBase project, string? scope, string? path, string? name)
        => LibraryReader.Resolve(portal, project, new LibraryQueryInfo { Scope = scope, LibraryPath = path, LibraryName = name });

    internal static UserGlobalLibrary UserLibrary(TiaPortal portal, ProjectBase project, string? scope, string? path, string? name)
        => Library(portal, project, scope, path, name) as UserGlobalLibrary
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                "This operation requires an open user global library.");

    internal static IMasterCopySource PlcSource(ProjectBase project, LibraryWriteRequestInfo item)
    {
        var plc = PlcSoftwareLocator.FindUnique(project, item.PlcName).Software;
        var path = item.PlcFolderPath ?? new List<string>();
        object source = item.PlcKind switch
        {
            "block" => LibraryReader.Exact(BlockTargetResolver.FindBlockGroup(plc.BlockGroup, path).Blocks
                .Where(x => string.Equals(x.Name, item.PlcObjectName, StringComparison.OrdinalIgnoreCase)), "PLC block"),
            "plcType" => LibraryReader.Exact(PlcTypeTargetResolver.FindTypeGroup(plc.TypeGroup, path).Types
                .Where(x => string.Equals(x.Name, item.PlcObjectName, StringComparison.OrdinalIgnoreCase)), "PLC data type"),
            "tagTable" => LibraryReader.Exact(TagMutationService.FindGroup(plc.TagTableGroup, path).TagTables
                .Where(x => string.Equals(x.Name, item.PlcObjectName, StringComparison.OrdinalIgnoreCase)), "PLC tag table"),
            _ => throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "Unknown PLC source kind."),
        };

        // PlcBlock itself is not a source; only concrete code/data blocks implement the native marker.
        return source as IMasterCopySource ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
            "The selected PLC object cannot be used as a master-copy source.");
    }

    internal static (object group, string kind) PlcDestination(ProjectBase project, LibraryWriteRequestInfo item)
    {
        var plc = PlcSoftwareLocator.FindUnique(project, item.PlcName).Software;
        var path = item.PlcFolderPath ?? new List<string>();
        object group = item.PlcKind switch
        {
            "blockGroup" => BlockTargetResolver.FindBlockGroup(plc.BlockGroup, path),
            "typeGroup" => PlcTypeTargetResolver.FindTypeGroup(plc.TypeGroup, path),
            "tagTableGroup" => TagMutationService.FindGroup(plc.TagTableGroup, path),
            _ => throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "Unknown PLC destination kind."),
        };
        return (group, item.PlcKind!);
    }
}
