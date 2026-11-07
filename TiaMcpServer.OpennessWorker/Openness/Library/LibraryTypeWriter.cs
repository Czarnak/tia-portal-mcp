using Siemens.Engineering;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Library;

internal static class LibraryTypeWriter
{
    internal static LibraryWriteResultInfo Write(TiaPortal portal, ProjectBase project, string method, LibraryWriteRequestInfo item)
    {
        switch (item.Operation)
        {
            case "create_type": return CreateType(portal, project, item);
            case "create_type_version": return CreateVersion(project, item);
            case "release_type_version": return Release(portal, project, item);
            case "set_default_type_version": return SetDefault(portal, project, item);
            case "synchronize_type": return Synchronize(portal, project, item);
            case "instantiate_type_version": return Instantiate(portal, project, item);
            case "delete_type":
            case "delete_type_version": return Delete(portal, project, item);
            default: throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "Unknown library type write operation.");
        }
    }

    private static LibraryWriteResultInfo CreateType(TiaPortal portal, ProjectBase project, LibraryWriteRequestInfo item)
    {
        var source = LibraryWriteTargets.PlcSource(project, item);
        if (source is not (FB or FC or PlcType))
            throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, "Only FB, FC and PLC data types can be converted to library types.");
        var library = ((Siemens.Engineering.Project)project).ProjectLibrary!;
        var folder = LibraryReader.TypeFolder(library, item.FolderPath);
        var name = LibraryWriteTargets.PlcSourceName(source);
        try
        {
            _ = LibraryWriter.GuardEvidence(() => LibraryTypeTargets.Type(library, name, null, null));
            throw Blocked("library_type_name_exists");
        }
        catch (WorkerOperationException ex) when (ex.FailureCategory == WorkerFailureCategories.TargetNotFound) { }
        catch (WorkerOperationException ex) when (ex.FailureCategory == WorkerFailureCategories.TargetAmbiguous)
        { throw Blocked("library_type_name_exists"); }
        if (LibraryWriter.GuardEvidence(() => ((IEngineeringServiceProvider)source).GetService<LibraryTypeInstanceInfo>()) is not null)
            throw Blocked("library_source_is_type_instance");

        var group = ((IEngineeringObject)source).Parent;
        var directory = TempDirectory();
        LibraryTypeResultInfo? outcome = null;
        try
        {
            var file = new FileInfo(Path.Combine(directory, name + ".xml"));
            switch (source)
            {
                case PlcBlock block: block.Export(file, ExportOptions.None); break;
                case PlcType type: type.Export(file, ExportOptions.None); break;
            }
            if (!file.Exists) throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, "The PLC source export produced no document.");
            using (var access = portal.ExclusiveAccess("Convert PLC object to library type"))
            using (var transaction = access.Transaction(project, "create_type"))
            {
                switch (source)
                {
                    case PlcBlock block: block.Delete(); break;
                    case PlcType type: type.Delete(); break;
                }
                var created = folder.Types.CreateFromDocuments(new DirectoryInfo(directory), name, group!, LibraryImportOptions.None).CreatedType
                    ?? throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, "Type conversion returned no created type.");
                outcome = Describe(created);
                try
                {
                    var instance = LibraryWriteTargets.PlcSource(project, item) as IEngineeringServiceProvider;
                    var link = instance?.GetService<LibraryTypeInstanceInfo>();
                    if (link is null || !Equals(link.LibraryTypeVersion.TypeObject, created)) throw LinkFailure(outcome);
                    SetVersion(outcome, link.LibraryTypeVersion);
                    outcome.InstancePaths.Add(LibraryTypeTargets.InstancePath(instance!));
                }
                catch (Exception ex) when (OpennessExceptions.IsRecoverable(ex))
                { throw LinkFailure(outcome, ex); }
                transaction.CommitOnDispose();
            }
            return new() { Library = LibraryIdentities.Identity(project, library), Type = outcome };
        }
        catch (Exception ex) when (outcome is not null) { throw WithOutcome(ex, outcome); }
        finally { TemporaryDirectoryCleanup.TryDeleteTempDirectory(directory); }
    }

    private static LibraryWriteResultInfo CreateVersion(ProjectBase project, LibraryWriteRequestInfo item)
    {
        var (instance, current) = LibraryTypeTargets.Instance(project, item);
        if (instance is not (FB or FC or PlcType))
            throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, "Only FB, FC and PLC data type instances are supported.");
        var type = current.TypeObject;
        var replaced = type.Versions.Any(x => x.State == LibraryTypeVersionState.InWork);
        var name = RequireContentHash(project, item, instance);
        var outcome = Describe(type);
        var directory = TempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, name + ".xml"), item.Content);
            var version = type.Versions.CreateFromDocuments(new DirectoryInfo(directory), name,
                item.ReplaceInWork == true ? CreateOptions.Override : CreateOptions.None, LibraryImportOptions.None).CreatedVersion
                ?? throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, "Version creation returned no created version.");
            SetVersion(outcome, version);
            outcome.ReplacedInWork = replaced;
            var failures = new List<string>();
            foreach (var plc in PlcSoftwareLocator.FindEveryPlc(project, null, failures))
                foreach (var link in version.FindInstances(plc.Software))
                    outcome.InstancePaths.Add(LibraryTypeTargets.InstancePath((IEngineeringServiceProvider)link.LibraryTypeInstance));
            if (failures.Count != 0)
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, "Created version instance discovery was incomplete.");
            return new() { Library = LibraryIdentities.Identity(project, ((Siemens.Engineering.Project)project).ProjectLibrary!), Type = outcome };
        }
        catch (Exception ex) { throw WithOutcome(ex, outcome); }
        finally { TemporaryDirectoryCleanup.TryDeleteTempDirectory(directory); }
    }

    private static LibraryWriteResultInfo Release(TiaPortal portal, ProjectBase project, LibraryWriteRequestInfo item)
    {
        var library = LibraryWriteTargets.Library(portal, project, item.Scope, item.LibraryPath, item.LibraryName);
        var type = LibraryTypeTargets.Type(library, item.TypeName, item.FolderPath, item.TypeGuid);
        var version = LibraryTypeTargets.InWork(type);
        _ = LibraryTypeTargets.Kind(version);
        var requested = item.VersionNumber is null ? null : System.Version.Parse(item.VersionNumber);
        if (LibraryWriter.GuardEvidence(() =>
        {
            var number = requested ?? version.VersionNumber;
            return type.Versions.Any(x => x.State == LibraryTypeVersionState.Committed && number.CompareTo(x.VersionNumber) <= 0);
        }))
            throw Blocked("library_version_not_higher");
        var outcome = Describe(type);
        outcome.DefaultBefore = DefaultNumber(type);
        try
        {
            version.Release(CreateOrReleaseDependenciesMode.DoNotAutomaticallyCreateOrReleaseDependencies, requested, item.Author, item.Comment);
            SetVersion(outcome, version);
            outcome.DefaultAfter = DefaultNumber(type);
            return new() { Library = LibraryIdentities.Identity(project, library), Type = outcome };
        }
        catch (Exception ex) { throw WithOutcome(ex, outcome); }
    }

    private static LibraryWriteResultInfo SetDefault(TiaPortal portal, ProjectBase project, LibraryWriteRequestInfo item)
    {
        var library = LibraryWriteTargets.Library(portal, project, item.Scope, item.LibraryPath, item.LibraryName);
        var type = LibraryTypeTargets.Type(library, item.TypeName, item.FolderPath, item.TypeGuid);
        var version = LibraryTypeTargets.Version(type, item.VersionNumber, item.VersionGuid, item.InWork);
        _ = LibraryTypeTargets.Kind(version);
        var outcome = Describe(type);
        SetVersion(outcome, version);
        outcome.DefaultBefore = DefaultNumber(type);
        try
        {
            version.SetAsDefault();
            outcome.DefaultAfter = DefaultNumber(type);
            return new() { Library = LibraryIdentities.Identity(project, library), Type = outcome };
        }
        catch (Exception ex) { throw WithOutcome(ex, outcome); }
    }

    private static LibraryWriteResultInfo Synchronize(TiaPortal portal, ProjectBase project, LibraryWriteRequestInfo item)
    {
        var source = LibraryWriteTargets.Library(portal, project, item.SourceScope, item.SourceLibraryPath, item.SourceLibraryName);
        var target = LibraryWriteTargets.Library(portal, project, item.Scope, item.LibraryPath, item.LibraryName);
        var type = LibraryTypeTargets.Type(source, item.TypeName, item.FolderPath, item.TypeGuid);
        foreach (var version in type.Versions) _ = LibraryTypeTargets.Kind(version);
        var guid = type.Guid.ToString("D");
        LibraryType? before = null;
        try { before = LibraryTypeTargets.Type(target, null, null, guid); }
        catch (WorkerOperationException ex) when (ex.FailureCategory == WorkerFailureCategories.TargetNotFound) { }
        var outcome = Describe(before ?? type);
        outcome.DefaultBefore = before is null ? null : DefaultNumber(before);
        try
        {
            type.UpdateLibrary(target, DeleteUnusedVersionsMode.DoNotDelete,
                StructureConflictResolutionMode.CancelIfStructureConflicts, ForceUpdateMode.SetOnlyHigherUpdatedVersionAsDefault);
            var updated = LibraryTypeTargets.Type(target, null, null, guid);
            var defaultBefore = outcome.DefaultBefore;
            outcome = Describe(updated);
            outcome.DefaultBefore = defaultBefore;
            outcome.DefaultAfter = DefaultNumber(updated);
            return new() { Library = LibraryIdentities.Identity(project, target), Type = outcome };
        }
        catch (Exception ex) { throw WithOutcome(ex, outcome); }
    }

    private static LibraryWriteResultInfo Instantiate(TiaPortal portal, ProjectBase project, LibraryWriteRequestInfo item)
    {
        var source = LibraryWriteTargets.Library(portal, project, item.SourceScope, item.SourceLibraryPath, item.SourceLibraryName);
        var type = LibraryTypeTargets.Type(source, item.TypeName, item.FolderPath, item.TypeGuid);
        var version = LibraryTypeTargets.Version(type, item.VersionNumber, item.VersionGuid, item.InWork);
        _ = LibraryTypeTargets.Kind(version);
        var plc = PlcSoftwareLocator.FindUnique(project, item.PlcName).Software;
        var path = item.PlcFolderPath ?? new List<string>();
        var destination = version is CodeBlockLibraryTypeVersion
            ? (object)BlockTargetResolver.FindBlockGroup(plc.BlockGroup, path)
            : PlcTypeTargetResolver.FindTypeGroup(plc.TypeGroup, path);
        var outcome = Describe(type);
        SetVersion(outcome, version);
        try
        {
            IEngineeringServiceProvider instance = version switch
            {
                CodeBlockLibraryTypeVersion block => ((PlcBlockGroup)destination).Blocks.CreateFrom(block, UpdatePathsMode.ThrowIfPathsConflict),
                PlcTypeLibraryTypeVersion data => ((PlcTypeGroup)destination).Types.CreateFrom(data, UpdatePathsMode.ThrowIfPathsConflict),
                _ => throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, "Unsupported library version kind."),
            };
            var link = instance.GetService<LibraryTypeInstanceInfo>()
                ?? throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, "The created instance has no library type link.");
            outcome = Describe(link.LibraryTypeVersion.TypeObject);
            SetVersion(outcome, link.LibraryTypeVersion);
            outcome.InstancePaths.Add(LibraryTypeTargets.InstancePath(instance));
            return new() { Library = LibraryIdentities.Identity(project, ((Siemens.Engineering.Project)project).ProjectLibrary!), Type = outcome };
        }
        catch (Exception ex) { throw WithOutcome(ex, outcome); }
    }

    private static LibraryWriteResultInfo Delete(TiaPortal portal, ProjectBase project, LibraryWriteRequestInfo item)
    {
        var library = LibraryWriteTargets.Library(portal, project, item.Scope, item.LibraryPath, item.LibraryName);
        var type = LibraryTypeTargets.Type(library, item.TypeName, item.FolderPath, item.TypeGuid);
        var outcome = Describe(type);
        var identity = LibraryIdentities.Identity(project, library);
        var folder = LibraryReader.TypeFolder(library, outcome.FolderPath);
        var guid = type.Guid;
        var version = item.Operation == "delete_type_version"
            ? LibraryTypeTargets.Version(type, item.VersionNumber, item.VersionGuid, item.InWork) : null;
        var versions = version is null ? type.Versions.ToList() : new List<LibraryTypeVersion> { version };
        foreach (var selected in versions) _ = LibraryTypeTargets.Kind(selected);
        var numbers = versions.Select(x => x.VersionNumber.ToString()).ToList();
        if (version is not null) SetVersion(outcome, version);
        var discard = version?.State == LibraryTypeVersionState.InWork;
        try
        {
            if (version is null) type.Delete();
            else if (discard) version.Discard();
            else version.Delete();
            if (discard) outcome.Discarded = true;
            else outcome.DeletedVersions = numbers;
            outcome.TypeDeleted = version is null || !folder.Types.Any(x => x.Guid == guid);
            return new() { Library = identity, Type = outcome };
        }
        catch (Exception ex) { throw WithOutcome(ex, outcome); }
    }

    internal static string RequireContentHash(ProjectBase project, LibraryWriteRequestInfo item, IEngineeringServiceProvider instance)
    {
        if (instance is not (FB or FC or PlcType))
            throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, "Only FB, FC and PLC data type instances are supported.");
        var name = LibraryWriteTargets.PlcSourceName((Siemens.Engineering.Library.MasterCopies.IMasterCopySource)instance);
        string fresh;
        try
        {
            var plc = PlcSoftwareLocator.FindUnique(project, item.PlcName).Software;
            fresh = instance switch
            {
                PlcBlock block => BlockExporter.ExportXml(new ResolvedBlockTarget(plc.ExternalSourceGroup,
                    (PlcBlockGroup)((IEngineeringObject)block).Parent!, block, name), LibraryTypeTargets.InstancePath(instance)),
                PlcType plcType => PlcTypeExporter.Export(new ResolvedTypeTarget(plc.ExternalSourceGroup,
                    (PlcTypeGroup)((IEngineeringObject)plcType).Parent!, plcType, name), "xml"),
                _ => throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, "Unsupported PLC type instance."),
            };
        }
        catch (Exception ex) when (OpennessExceptions.IsRecoverable(ex))
        {
            throw new WorkerOperationException(WorkerFailureCategories.GuardBlocked, "library_state_unverifiable",
                engineeringError: ex is EngineeringException native ? WorkerExecution.Describe(native)
                    : (ex as WorkerOperationException)?.EngineeringError);
        }
        PlcWritePreconditions.RequireContentHash(item.ExpectedContentHash, "xml", fresh);
        return name;
    }

    private static string? DefaultNumber(LibraryType type) => type.Versions.FirstOrDefault(x => x.IsDefault)?.VersionNumber.ToString();
    private static LibraryTypeResultInfo Describe(LibraryType type) => new()
    { TypeName = type.Name, TypeGuid = type.Guid.ToString("D"), FolderPath = LibraryTypeTargets.FolderPath(type) };
    private static void SetVersion(LibraryTypeResultInfo result, LibraryTypeVersion version)
    {
        result.VersionNumber = version.VersionNumber.ToString();
        result.VersionGuid = version.Guid.ToString("D");
        result.VersionState = version.State.ToString();
    }
    private static string TempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tia-mcp-library-type-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
    private static WorkerOperationException Blocked(string guard)
        => new(WorkerFailureCategories.GuardBlocked, guard);
    private static WorkerOperationException LinkFailure(LibraryTypeResultInfo outcome, Exception? error = null)
        => new(WorkerFailureCategories.PostconditionFailed, "The converted PLC instance does not link to the created library type; the transaction was not committed.",
            engineeringError: error is EngineeringException native ? WorkerExecution.Describe(native)
                : (error as WorkerOperationException)?.EngineeringError,
            libraryWriteOutcome: new() { Type = outcome });
    private static WorkerOperationException WithOutcome(Exception ex, LibraryTypeResultInfo outcome)
    {
        var category = ex is NonRecoverableException ? WorkerFailureCategories.PortalLost
            : ex is EngineeringException engineering ? WorkerExecution.Classify(engineering, true)
            : ex is WorkerOperationException failure ? failure.FailureCategory : WorkerFailureCategories.WorkerOperationFailed;
        return new(category, ex is EngineeringException native ? WorkerExecution.FailureMessage(native, category) : ex.Message,
            engineeringError: category == WorkerFailureCategories.PortalLost ? null
                : ex is EngineeringException typed ? WorkerExecution.Describe(typed) : (ex as WorkerOperationException)?.EngineeringError,
            libraryWriteOutcome: new() { Type = outcome });
    }
}
