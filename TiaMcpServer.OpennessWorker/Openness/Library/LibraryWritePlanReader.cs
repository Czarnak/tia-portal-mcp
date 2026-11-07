using Siemens.Engineering;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.MasterCopies;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.Multiuser;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Library;

internal static class LibraryWritePlanReader
{
    private const int MaxFolderContents = 200;

    internal static LibraryWritePlanInfo Plan(TiaPortal portal, ProjectBase project, string method, LibraryWriteRequestInfo item)
    {
        var error = LibraryWriteMethods.Validate(item);
        if (error is not null || method != LibraryWriteMethods.PlanFor(item))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, error ?? "Method and library item do not match.");

        var containerKind = project switch
        {
            Siemens.Engineering.Project => ProjectContainerKinds.StandaloneProject,
            MultiuserProject => ProjectContainerKinds.LocalSession,
            _ => null,
        };
        if (containerKind is null || !ProjectCapabilityCatalog.Supports(containerKind, method))
            throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                "The bound project container does not support this library operation.");

        var plan = new LibraryWritePlanInfo();
        if (item.Operation is "create_global" or "retrieve_global") return plan;
        if (item.Operation == "open_global")
        {
            GlobalLibrary? opened = null;
            Observe(plan, () =>
            {
                var path = LibraryReader.CanonicalPath(item.FilePath!);
                var matches = portal.GlobalLibraries.Where(x => x.Path is { } nativePath
                    && string.Equals(LibraryReader.CanonicalPath(nativePath.FullName), path, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
                plan.AlreadyOpen = matches.Count != 0;
                if (matches.Count != 0) opened = LibraryReader.Exact(matches, "open global library path");
            });
            if (opened is not null) plan.Library = LibraryIdentities.Identity(project, opened);
            return plan;
        }

        var library = item.Operation is "save_global" or "close_global" or "archive_global"
            ? LibraryWriteTargets.UserLibrary(portal, project, item.Scope, item.LibraryPath, item.LibraryName)
            : LibraryWriteTargets.Library(portal, project, item.Scope, item.LibraryPath, item.LibraryName);
        var identity = plan.Library = LibraryIdentities.Identity(project, library);
        if (LibraryWriteMethods.LifecycleOperations.Contains(item.Operation, StringComparer.Ordinal))
        {
            plan.AlreadyOpen = true;
            return plan;
        }

        var pathSegments = item.FolderPath ?? new List<string>();
        if (item.Operation is "create_folder" or "delete_folder")
        {
            var targetPath = pathSegments.Concat(new[] { item.Name! }).ToList();
            plan.Target = LibraryIdentities.Child(identity, item.FolderKind!, targetPath, "folder", item.Name!);
            if (item.Operation == "create_folder")
            {
                if (item.FolderKind == "types") _ = LibraryReader.TypeFolder(library, pathSegments);
                else _ = LibraryReader.CopyFolder(library, pathSegments);
            }
            else
            {
                object folder = item.FolderKind == "types"
                    ? LibraryReader.TypeFolder(library, targetPath) : LibraryReader.CopyFolder(library, targetPath);
                if (folder is not (MasterCopyUserFolder or LibraryTypeUserFolder))
                    throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, "Only user folders can be deleted.");
                Observe(plan, () => ReadContents(library, identity, item.FolderKind!, targetPath, plan));
            }
            return plan;
        }

        var copies = LibraryReader.CopyFolder(library, pathSegments);
        var name = item.Operation == "create_master_copy"
            ? LibraryWriteTargets.PlcSourceName(LibraryWriteTargets.PlcSource(project, item)) : item.Name!;
        plan.Target = LibraryIdentities.Child(identity, "masterCopies", pathSegments, "masterCopy", name);
        switch (item.Operation)
        {
            case "copy_master_copy":
                var source = LibraryWriteTargets.Library(portal, project, item.SourceScope, item.SourceLibraryPath, item.SourceLibraryName);
                _ = LibraryReader.Exact(LibraryReader.CopyFolder(source, item.SourceFolderPath).MasterCopies.Where(x => x.Name == name), "source master copy");
                break;
            case "instantiate_master_copy":
                _ = LibraryReader.Exact(copies.MasterCopies.Where(x => x.Name == name), "master copy");
                _ = LibraryWriteTargets.PlcDestination(project, item);
                return plan;
            case "delete_master_copy":
                _ = LibraryReader.Exact(copies.MasterCopies.Where(x => x.Name == name), "master copy");
                return plan;
        }
        Observe(plan, () => plan.MasterCopyExists = copies.MasterCopies.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)));
        return plan;
    }

    private static void Observe(LibraryWritePlanInfo plan, Action read)
    {
        try { read(); }
        catch (Exception exception) when (OpennessExceptions.IsRecoverablePropertyFailure(exception))
        {
            plan.Unverifiable = "Required library state could not be read: " + exception.GetType().Name;
        }
    }

    private static bool ReadContents(ILibrary library, LibraryIdentityInfo root, string folderKind, List<string> path, LibraryWritePlanInfo plan)
    {
        foreach (var row in LibraryReader.ChildRows(library, new LibraryQueryInfo { FolderKind = folderKind, FolderPath = path }))
        {
            if (plan.FolderContents.Count == MaxFolderContents)
            {
                plan.ContentsTruncated = true;
                return false;
            }
            var name = row switch
            {
                MasterCopyFolder x => x.Name,
                LibraryTypeFolder x => x.Name,
                LibraryType x => x.Name,
                MasterCopy x => x.Name,
                _ => throw new InvalidOperationException("Unexpected library child row."),
            };
            var isFolder = row is MasterCopyFolder or LibraryTypeFolder;
            var childPath = isFolder ? path.Concat(new[] { name }).ToList() : path;
            var identity = LibraryIdentities.Child(root, folderKind, childPath,
                isFolder ? "folder" : row is LibraryType ? "type" : "masterCopy", name);
            if (row is LibraryType type) identity.TypeGuid = type.Guid.ToString("D");
            plan.FolderContents.Add(identity);
            if (isFolder && !ReadContents(library, root, folderKind, childPath, plan)) return false;
        }
        return true;
    }
}
