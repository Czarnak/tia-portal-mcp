using Siemens.Engineering;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.MasterCopies;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.Multiuser;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Openness.Block;
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
        if (LibraryWriteMethods.TypeOperations.Contains(item.Operation, StringComparer.Ordinal))
        {
            Observe(plan, () => TypePlan(portal, project, item, plan));
            return plan;
        }
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
                Folder(library, identity, item.FolderKind!, pathSegments, plan);
            }
            else
            {
                var folder = Folder(library, identity, item.FolderKind!, targetPath, plan);
                if (folder is null) return plan;
                if (folder is not (MasterCopyUserFolder or LibraryTypeUserFolder))
                    throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, "Only user folders can be deleted.");
                Observe(plan, () => ReadContents(library, identity, item.FolderKind!, targetPath, plan));
            }
            return plan;
        }

        var name = item.Operation == "create_master_copy"
            ? LibraryWriteTargets.PlcSourceName(LibraryWriteTargets.PlcSource(project, item)) : item.Name!;
        if (item.Operation == "instantiate_master_copy") _ = LibraryWriteTargets.PlcDestination(project, item);
        var copies = Folder(library, identity, "masterCopies", pathSegments, plan) as MasterCopyFolder;
        plan.Target = LibraryIdentities.Child(identity, "masterCopies", pathSegments, "masterCopy", name);
        switch (item.Operation)
        {
            case "copy_master_copy":
                var source = LibraryWriteTargets.Library(portal, project, item.SourceScope, item.SourceLibraryPath, item.SourceLibraryName);
                var sourceIdentity = LibraryIdentities.Identity(project, source);
                var sourceCopies = Folder(source, sourceIdentity, "masterCopies", item.SourceFolderPath ?? new(), plan) as MasterCopyFolder;
                Missing(plan, LibraryIdentities.Child(sourceIdentity, "masterCopies", item.SourceFolderPath ?? new(), "masterCopy", name),
                    () => LibraryReader.Exact((sourceCopies?.MasterCopies ?? Enumerable.Empty<MasterCopy>()).Where(x => x.Name == name), "source master copy"));
                break;
            case "instantiate_master_copy":
                Missing(plan, plan.Target, () => LibraryReader.Exact((copies?.MasterCopies ?? Enumerable.Empty<MasterCopy>()).Where(x => x.Name == name), "master copy"));
                return plan;
            case "delete_master_copy":
                Missing(plan, plan.Target, () => LibraryReader.Exact((copies?.MasterCopies ?? Enumerable.Empty<MasterCopy>()).Where(x => x.Name == name), "master copy"));
                return plan;
        }
        if (copies is not null) Observe(plan, () => plan.MasterCopyExists = copies.MasterCopies.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)));
        return plan;
    }

    private static T? Missing<T>(LibraryWritePlanInfo plan, LibraryIdentityInfo target, Func<T> resolve) where T : class
    {
        try { return resolve(); }
        catch (WorkerOperationException ex) when (ex.FailureCategory == WorkerFailureCategories.TargetNotFound)
        {
            (plan.MissingTargets ??= new()).Add(target);
            return null;
        }
    }

    private static object? Folder(ILibrary library, LibraryIdentityInfo root, string kind, List<string> path, LibraryWritePlanInfo plan)
    {
        object? folder = kind == "types" ? library.TypeFolder : library.MasterCopyFolder;
        for (var i = 1; i <= path.Count; i++)
        {
            var prefix = path.Take(i).ToList();
            folder = Missing(plan, LibraryIdentities.Child(root, kind, prefix, "folder", prefix.Last()),
                () => kind == "types" ? (object)LibraryReader.TypeFolder(library, prefix) : LibraryReader.CopyFolder(library, prefix));
        }
        return folder;
    }

    private static void TypePlan(TiaPortal portal, ProjectBase project, LibraryWriteRequestInfo item, LibraryWritePlanInfo plan)
    {
        var library = item.Operation is "create_type" or "create_type_version" or "instantiate_type_version"
            ? ((Siemens.Engineering.Project)project).ProjectLibrary!
            : LibraryWriteTargets.Library(portal, project, item.Scope, item.LibraryPath, item.LibraryName);
        var root = plan.Library = LibraryIdentities.Identity(project, library);
        void Guard(string id) => (plan.GuardIds ??= new()).Add(id);
        if (item.Operation == "create_type")
        {
            var source = LibraryWriteTargets.PlcSource(project, item);
            if (source is not (FB or FC or PlcType))
                throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, "Only FB, FC and PLC data types can be converted to library types.");
            var name = LibraryWriteTargets.PlcSourceName(source);
            plan.Target = LibraryIdentities.Child(root, "types", item.FolderPath ?? new(), "type", name);
            Folder(library, root, "types", item.FolderPath ?? new(), plan);
            Observe(plan, () =>
            {
                try { _ = LibraryTypeTargets.Type(library, name, null, null); Guard("library_type_name_exists"); }
                catch (WorkerOperationException ex) when (ex.FailureCategory == WorkerFailureCategories.TargetNotFound) { }
                catch (WorkerOperationException ex) when (ex.FailureCategory == WorkerFailureCategories.TargetAmbiguous) { Guard("library_type_name_exists"); }
                if (((IEngineeringServiceProvider)source).GetService<LibraryTypeInstanceInfo>() is not null) Guard("library_source_is_type_instance");
                plan.Type = new() { TypeName = name, FolderPath = item.FolderPath ?? new(),
                    InstancePaths = new() { LibraryTypeTargets.InstancePath((IEngineeringServiceProvider)source) } };
                plan.Notes = new() { "The source PLC object will be replaced by a linked type instance." };
            });
            return;
        }
        LibraryType type;
        LibraryTypeVersion? version = null;
        if (item.Operation == "create_type_version")
        {
            var instance = LibraryTypeTargets.Instance(project, item);
            LibraryTypeWriter.RequireContentHash(project, item, instance.instance);
            type = instance.version.TypeObject;
            plan.Type = Describe(type);
            plan.Type.InstancePaths.Add(LibraryTypeTargets.InstancePath(instance.instance));
            Observe(plan, () =>
            {
                var inWork = type.Versions.FirstOrDefault(x => x.State == LibraryTypeVersionState.InWork);
                if (inWork is not null) SetVersion(plan.Type, inWork);
                plan.Type.ReplacedInWork = inWork is not null;
                var plcs = Instances(project, type.Versions, plan, null);
                if (plcs > 1) plan.Notes = new() { "The type has instances in more than one PLC; the test instance is not predictable." };
            });
            return;
        }
        var sourceLibrary = item.Operation is "synchronize_type" or "instantiate_type_version"
            ? LibraryWriteTargets.Library(portal, project, item.SourceScope, item.SourceLibraryPath, item.SourceLibraryName) : library;
        var sourceRoot = LibraryIdentities.Identity(project, sourceLibrary);
        // Resolve the PLC independently even when the requested library type is not present yet.
        if (item.Operation == "instantiate_type_version")
        {
            var plc = PlcSoftwareLocator.FindUnique(project, item.PlcName).Software;
            try { _ = BlockTargetResolver.FindBlockGroup(plc.BlockGroup, item.PlcFolderPath ?? new()); }
            catch (WorkerOperationException ex) when (ex.FailureCategory == WorkerFailureCategories.TargetNotFound)
            { _ = PlcTypeTargetResolver.FindTypeGroup(plc.TypeGroup, item.PlcFolderPath ?? new()); }
        }
        if (item.FolderPath is not null) Folder(sourceLibrary, sourceRoot, "types", item.FolderPath, plan);
        var missing = LibraryIdentities.Child(sourceRoot, "types", item.FolderPath ?? new(), "type", item.TypeName ?? "");
        missing.TypeGuid = item.TypeGuid;
        var resolved = Missing(plan, missing, () => LibraryTypeTargets.Type(sourceLibrary, item.TypeName, item.FolderPath, item.TypeGuid));
        if (resolved is null) return;
        type = resolved;
        plan.Type = Describe(type);
        if (item.Operation is not ("delete_type" or "synchronize_type"))
        {
            if (item.Operation == "release_type_version")
            {
                var missingVersion = LibraryIdentities.Child(sourceRoot, "types", plan.Type.FolderPath, "inWorkTypeVersion", type.Name);
                missingVersion.TypeGuid = plan.Type.TypeGuid;
                version = Missing(plan, missingVersion, () => LibraryTypeTargets.InWork(type));
            }
            else version = LibraryTypeTargets.Version(type, item.VersionNumber, item.VersionGuid, item.InWork);
            if (version is not null)
            {
                _ = LibraryTypeTargets.Kind(version);
                SetVersion(plan.Type, version);
            }
        }
        else foreach (var selected in type.Versions) _ = LibraryTypeTargets.Kind(selected);
        if (item.Operation == "instantiate_type_version")
        {
            var plc = PlcSoftwareLocator.FindUnique(project, item.PlcName).Software;
            if (version is CodeBlockLibraryTypeVersion) _ = BlockTargetResolver.FindBlockGroup(plc.BlockGroup, item.PlcFolderPath ?? new());
            else _ = PlcTypeTargetResolver.FindTypeGroup(plc.TypeGroup, item.PlcFolderPath ?? new());
        }
        Observe(plan, () =>
        {
            plan.Type.DefaultBefore = type.Versions.FirstOrDefault(x => x.IsDefault)?.VersionNumber.ToString();
            if (item.Operation == "release_type_version")
            {
                var number = item.VersionNumber is null ? version?.VersionNumber : System.Version.Parse(item.VersionNumber);
                plan.Type.DefaultAfter = number?.ToString();
                if (number is not null && type.Versions.Any(x => x.State == LibraryTypeVersionState.Committed && number.CompareTo(x.VersionNumber) <= 0)) Guard("library_version_not_higher");
            }
            if (item.Operation is "delete_type" or "delete_type_version")
            {
                var versions = version is null ? type.Versions.ToList() : new List<LibraryTypeVersion> { version };
                plan.Type.DeletedVersions = versions.Select(x => x.VersionNumber.ToString()).ToList();
                plan.Type.TypeDeleted = version is null || type.Versions.Count() == 1;
                plan.BlockingInstances = new();
                Instances(project, versions, plan, plan.BlockingInstances);
                plan.MasterCopiesContainingInstances = versions.SelectMany(x => x.MasterCopiesContainingInstances).Select(x => x.Name).Distinct().ToList();
            }
            if (item.Operation is "instantiate_type_version" or "synchronize_type")
            {
                plan.Dependencies = new();
                var seen = new HashSet<Guid>();
                void Dependencies(LibraryTypeVersion current)
                {
                    if (!seen.Add(current.Guid)) return;
                    foreach (var dependency in current.Dependencies)
                    {
                        if (!seen.Contains(dependency.Guid)) plan.Dependencies.Add($"{dependency.TypeObject.Name} {dependency.VersionNumber} ({dependency.Guid:D})");
                        Dependencies(dependency);
                    }
                }
                foreach (var selected in version is null ? type.Versions : new[] { version }.AsEnumerable()) Dependencies(selected);
                if (item.Operation == "instantiate_type_version" && item.SourceScope == "global")
                    plan.Notes = new() { "The global library version and dependencies also enter the project library." };
                if (item.Operation == "synchronize_type")
                {
                    LibraryType? target = null;
                    try { target = LibraryTypeTargets.Type(library, null, null, type.Guid.ToString("D")); }
                    catch (WorkerOperationException ex) when (ex.FailureCategory == WorkerFailureCategories.TargetNotFound) { }
                    plan.Type.DefaultBefore = target?.Versions.FirstOrDefault(x => x.IsDefault)?.VersionNumber.ToString();
                }
            }
        });
    }

    private static LibraryTypeResultInfo Describe(LibraryType type) => new()
    { TypeName = type.Name, TypeGuid = type.Guid.ToString("D"), FolderPath = LibraryTypeTargets.FolderPath(type) };
    private static void SetVersion(LibraryTypeResultInfo result, LibraryTypeVersion version)
    { result.VersionGuid = version.Guid.ToString("D"); result.VersionNumber = version.VersionNumber.ToString(); result.VersionState = version.State.ToString(); }
    private static int Instances(ProjectBase project, IEnumerable<LibraryTypeVersion> versions, LibraryWritePlanInfo plan, List<string>? paths)
    {
        var failures = new List<string>();
        var plcs = 0;
        var selected = versions.ToList();
        foreach (var plc in PlcSoftwareLocator.FindEveryPlc(project, null, failures))
        {
            var found = false;
            foreach (var version in selected)
                foreach (var instance in version.FindInstances(plc.Software))
                { found = true; paths?.Add(LibraryTypeTargets.InstancePath((IEngineeringServiceProvider)instance.LibraryTypeInstance)); }
            if (found) plcs++;
        }
        if (failures.Count != 0) plan.Unverifiable = "Required PLC instance inventory could not be read.";
        return plcs;
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
