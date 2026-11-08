using Siemens.Engineering;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Library;

internal static class LibraryTypeTargets
{
    private const int MaxDepth = 64;

    internal static LibraryType Type(ILibrary library, string? typeName, List<string>? folderPath, string? typeGuid)
    {
        if (LibrarySelectorRules.CheckType(typeName, typeGuid, folderPath) is { } error)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, error);

        var guid = typeGuid is null ? (Guid?)null : Guid.Parse(typeGuid);
        var types = folderPath is null
            ? Tree(library.TypeFolder, new List<LibraryTypeFolder>())
            : LibraryReader.TypeFolder(library, folderPath).Types;
        var matches = types.Where(x => guid.HasValue ? x.Guid == guid.Value : string.Equals(x.Name, typeName, StringComparison.Ordinal)).ToList();
        return Exact(matches, "library type", x => $"/{FormatPath(FolderPath(x))} (typeGuid {x.Guid:D})");
    }

    internal static LibraryTypeVersion Version(LibraryType type, string? versionNumber, string? versionGuid, bool? inWork)
    {
        if (LibrarySelectorRules.CheckVersion(versionNumber, versionGuid, inWork, false) is { } error)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, error);
        if (inWork == true) return InWork(type);
        var guid = versionGuid is null ? (Guid?)null : Guid.Parse(versionGuid);
        System.Version.TryParse(versionNumber, out var number);
        var matches = type.Versions.Where(x => guid.HasValue ? x.Guid == guid.Value
            : versionNumber is not null ? x.VersionNumber.Equals(number) : x.IsDefault).ToList();
        return Exact(matches, "library type version", x => $"{x.VersionNumber} (versionGuid {x.Guid:D})");
    }

    internal static LibraryTypeVersion InWork(LibraryType type)
        => Exact(type.Versions.Where(x => x.State == LibraryTypeVersionState.InWork).ToList(), "in-work library type version",
            x => $"{x.VersionNumber} (versionGuid {x.Guid:D})");

    internal static (IEngineeringServiceProvider instance, LibraryTypeVersion version) Instance(ProjectBase project, LibraryWriteRequestInfo item)
    {
        var instance = LibraryWriteTargets.PlcSource(project, item) as IEngineeringServiceProvider;
        var info = instance?.GetService<LibraryTypeInstanceInfo>();
        if (info is null)
            throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, "The selected PLC object is not a library type instance.");
        _ = Kind(info.LibraryTypeVersion);
        return (instance!, info.LibraryTypeVersion);
    }

    internal static string Kind(LibraryTypeVersion version) => version switch
    {
        CodeBlockLibraryTypeVersion => "codeBlock",
        PlcTypeLibraryTypeVersion => "plcType",
        _ => throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
            "Only code-block and PLC data-type library versions are supported."),
    };

    internal static List<string> FolderPath(LibraryType type) => LibraryReader.Owner(type).Path;

    internal static string InstancePath(IEngineeringServiceProvider instance)
    {
        var path = new List<string>();
        var seen = new List<IEngineeringObject>();
        var current = instance as IEngineeringObject
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, "The instance has no engineering object path.");
        while (current is not null)
        {
            if (seen.Count >= MaxDepth || seen.Any(x => ReferenceEquals(x, current)))
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, "The instance owner path is cyclic or exceeds 64 levels.");
            seen.Add(current);
            // The PLC's root (system) groups are not plcFolderPath segments.
            var name = current switch
            {
                PlcBlock block => block.Name,
                PlcType type => type.Name,
                PlcBlockGroup group when current.Parent is not PlcSoftware => group.Name,
                PlcTypeGroup group when current.Parent is not PlcSoftware => group.Name,
                PlcSoftware plc => plc.Name,
                _ => null,
            };
            if (!string.IsNullOrEmpty(name)) path.Add(name!);
            if (current is PlcSoftware) break;
            current = current.Parent;
        }
        path.Reverse();
        return FormatPath(path);
    }

    private static IEnumerable<LibraryType> Tree(LibraryTypeFolder folder, List<LibraryTypeFolder> ancestors)
    {
        if (ancestors.Count >= MaxDepth || ancestors.Any(x => ReferenceEquals(x, folder)))
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, "The library type tree is cyclic or exceeds 64 levels.");
        ancestors.Add(folder);
        foreach (var type in folder.Types) yield return type;
        foreach (var child in folder.Folders)
            foreach (var type in Tree(child, ancestors)) yield return type;
        ancestors.RemoveAt(ancestors.Count - 1);
    }

    private static T Exact<T>(List<T> matches, string subject, Func<T, string> candidate)
    {
        if (matches.Count == 1) return matches[0];
        throw new WorkerOperationException(matches.Count == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
            matches.Count == 0 ? $"No {subject} matches the selector."
                : $"Multiple {subject}s match the selector: {string.Join("; ", matches.Select(candidate))}.");
    }

    private static string FormatPath(IEnumerable<string> segments) => string.Join("/", segments.Select(Uri.EscapeDataString));
}
