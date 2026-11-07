using Siemens.Engineering;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.MasterCopies;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.Multiuser;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Library;

internal static class LibraryWriter
{
    internal static LibraryWriteResultInfo Write(TiaPortal portal, ProjectBase project, string method, LibraryWriteRequestInfo item)
    {
        var error = LibraryWriteMethods.Validate(item);
        if (error is not null || method != LibraryWriteMethods.ForItem(item))
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

        if (LibraryWriteMethods.TypeOperations.Contains(item.Operation))
            return LibraryTypeWriter.Write(portal, project, method, item);
        switch (item.Operation)
        {
            case "create_global":
                return Result(portal.GlobalLibraries.Create<UserGlobalLibrary>(new DirectoryInfo(item.Directory!), item.Name!));
            case "open_global":
                var path = LibraryReader.CanonicalPath(item.FilePath!);
                var alreadyOpen = portal.GlobalLibraries.Any(x => x.Path is { } nativePath
                    && string.Equals(LibraryReader.CanonicalPath(nativePath.FullName), path, StringComparison.OrdinalIgnoreCase));
                var opened = Result(portal.GlobalLibraries.Open(new FileInfo(item.FilePath!), Mode(item.OpenMode)));
                opened.AlreadyOpen = alreadyOpen;
                return opened;
            case "retrieve_global":
                return Result(portal.GlobalLibraries.Retrieve(new FileInfo(item.FilePath!), new DirectoryInfo(item.Directory!), Mode(item.OpenMode)));
        }

        var library = item.Operation is "save_global" or "close_global" or "archive_global"
            ? LibraryWriteTargets.UserLibrary(portal, project, item.Scope, item.LibraryPath, item.LibraryName)
            : LibraryWriteTargets.Library(portal, project, item.Scope, item.LibraryPath, item.LibraryName);
        var result = Result(library);
        var segments = item.FolderPath ?? new List<string>();
        switch (item.Operation)
        {
            case "save_global":
                ((UserGlobalLibrary)library).Save();
                result.Saved = true;
                return result;
            case "close_global":
                result.Saved = false;
                var user = (UserGlobalLibrary)library;
                if (item.CloseAction == "save" && user.IsModified)
                {
                    user.Save();
                    result.Saved = true;
                }
                try { user.Close(); }
                catch (Exception ex) when (result.Saved == true)
                {
                    var category = ex is NonRecoverableException ? WorkerFailureCategories.PortalLost
                        : ex is EngineeringException engineering ? WorkerExecution.Classify(engineering, true)
                        : ex is WorkerOperationException failure ? failure.FailureCategory : WorkerFailureCategories.WorkerOperationFailed;
                    throw new WorkerOperationException(category,
                        ex is EngineeringException native ? WorkerExecution.FailureMessage(native, category) : ex.Message,
                        engineeringError: category == WorkerFailureCategories.PortalLost ? null
                            : ex is EngineeringException typed ? WorkerExecution.Describe(typed)
                            : (ex as WorkerOperationException)?.EngineeringError,
                        libraryWriteOutcome: new LibraryWriteOutcomeInfo { Saved = true });
                }
                return result;
            case "archive_global":
                var name = ArchiveModeNames.EnsureExtension(item.Name!, LibraryWriteMethods.ArchiveExtension);
                ((UserGlobalLibrary)library).Archive(new DirectoryInfo(item.Directory!), name, LibraryArchivationMode.Compressed);
                result.ArchivePath = Path.Combine(item.Directory!, name);
                return result;
            case "create_folder":
                var createdName = item.FolderKind == "types"
                    ? LibraryReader.TypeFolder(library, segments).Folders.Create(item.Name!).Name
                    : LibraryReader.CopyFolder(library, segments).Folders.Create(item.Name!).Name;
                result.Target = LibraryIdentities.Child(result.Library, item.FolderKind!, segments.Concat(new[] { createdName }), "folder", createdName);
                return result;
            case "delete_folder":
                var folderPath = segments.Concat(new[] { item.Name! }).ToList();
                object folder = item.FolderKind == "types"
                    ? LibraryReader.TypeFolder(library, folderPath) : LibraryReader.CopyFolder(library, folderPath);
                result.Target = LibraryIdentities.Child(result.Library, item.FolderKind!, folderPath, "folder", item.Name!);
                switch (folder)
                {
                    case MasterCopyUserFolder copyFolder: copyFolder.Delete(); break;
                    case LibraryTypeUserFolder typeFolder: typeFolder.Delete(); break;
                    default: throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, "Only user folders can be deleted.");
                }
                return result;
        }

        var copies = LibraryReader.CopyFolder(library, segments).MasterCopies;
        switch (item.Operation)
        {
            case "create_master_copy":
                var plcSource = LibraryWriteTargets.PlcSource(project, item);
                var expectedName = LibraryWriteTargets.PlcSourceName(plcSource);
                RequireAvailableName(expectedName);
                return Created(copies.Create(plcSource), expectedName);
            case "copy_master_copy":
                var source = LibraryWriteTargets.Library(portal, project, item.SourceScope, item.SourceLibraryPath, item.SourceLibraryName);
                var sourceCopy = LibraryReader.Exact(LibraryReader.CopyFolder(source, item.SourceFolderPath).MasterCopies
                    .Where(x => x.Name == item.Name), "source master copy");
                RequireAvailableName(item.Name!);
                return Created(copies.CreateFrom(sourceCopy), item.Name!);
            case "instantiate_master_copy":
                var masterCopy = LibraryReader.Exact(copies.Where(x => x.Name == item.Name), "master copy");
                var destination = LibraryWriteTargets.PlcDestination(project, item);
                switch (destination.group)
                {
                    case PlcBlockGroup group: group.Blocks.CreateFrom(masterCopy, MasterCopyMode.ThrowIfExists); break;
                    case PlcTypeGroup group: group.Types.CreateFrom(masterCopy, MasterCopyMode.ThrowIfExists); break;
                    case PlcTagTableGroup group: group.TagTables.CreateFrom(masterCopy, MasterCopyMode.ThrowIfExists); break;
                }
                result.Target = LibraryIdentities.Child(result.Library, "masterCopies", segments, "masterCopy", item.Name!);
                return result;
            case "delete_master_copy":
                var target = LibraryReader.Exact(copies.Where(x => x.Name == item.Name), "master copy");
                result.Target = LibraryIdentities.Child(result.Library, "masterCopies", segments, "masterCopy", target.Name);
                target.Delete();
                return result;
            default:
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "Unknown library write operation.");
        }

        LibraryWriteResultInfo Result(ILibrary root) => new() { Library = LibraryIdentities.Identity(project, root) };

        void RequireAvailableName(string name)
        {
            if (copies.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new WorkerOperationException(WorkerFailureCategories.GuardBlocked, "library_master_copy_exists");
        }

        LibraryWriteResultInfo Created(MasterCopy copy, string expectedName)
        {
            var actualName = copy.Name;
            if (!string.Equals(actualName, expectedName, StringComparison.Ordinal))
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                    "TIA Portal created a master copy with a different name; inspect the library before retrying.",
                    libraryWriteOutcome: new LibraryWriteOutcomeInfo { ActualName = actualName });
            result.ActualName = actualName;
            result.Target = LibraryIdentities.Child(result.Library, "masterCopies", segments, "masterCopy", actualName);
            return result;
        }
    }

    private static OpenMode Mode(string? mode) => mode == "readOnly" ? OpenMode.ReadOnly : OpenMode.ReadWrite;
}
