using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.FakeWorker.Library;

/// <summary>Scripted library plans and outcomes for protocol tests; no native library operations.</summary>
internal static class LibraryWriteScenario
{
    private static bool _createdType;

    public static string Handle(string line, string scenario)
    {
        var request = JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Envelope)!;
        if (request.LibraryWrite is not { } item || LibraryWriteMethods.Validate(item) is not null
            || !(LibraryWriteMethods.LifecycleOperations.Contains(item.Operation, StringComparer.Ordinal)
                || item.Operation is "create_folder" or "copy_master_copy"
                || LibraryWriteMethods.TypeOperations.Contains(item.Operation, StringComparer.Ordinal))
            || request.Method != LibraryWriteMethods.PlanFor(item) && request.Method != LibraryWriteMethods.ForItem(item)
            || scenario == "library-write-roundtrip.amc21"
                && (request.Method != LibraryWriteMethods.GlobalPlan || item.Operation != "create_folder"))
            return Failure(WorkerFailureCategories.ValidationError, "Invalid scripted library write request.");

        var planning = request.Method == LibraryWriteMethods.PlanFor(item);
        if (item.LibraryName == "Missing" || item.SourceLibraryName == "Missing")
            return Failure(WorkerFailureCategories.TargetNotFound, "The selected library was not found.");
        if (!planning && scenario == "library-write-portal-lost")
            return Failure(WorkerFailureCategories.PortalLost, "The Portal connection was lost during the library write.");
        if (LibraryWriteMethods.TypeOperations.Contains(item.Operation, StringComparer.Ordinal))
            return TypeOperation(item, planning);

        var library = new LibraryIdentityInfo { Scope = item.Scope ?? "global", Kind = "library",
            Name = item.LibraryName ?? item.Name ?? "Fixture", LibraryName = item.LibraryName ?? item.Name ?? "Fixture",
            LibraryPath = item.LibraryPath ?? item.FilePath ?? "C:/Fixture/Library.al21" };
        var target = new LibraryIdentityInfo { Scope = library.Scope,
            Kind = item.Operation == "create_folder" ? "folder" : "masterCopy", Name = item.Name ?? "Folder",
            LibraryName = library.LibraryName, LibraryPath = library.LibraryPath,
            FolderKind = item.FolderKind, FolderPath = (item.FolderPath ?? []).Concat(new[] { item.Name! }).ToList() };
        var lifecycle = LibraryWriteMethods.LifecycleOperations.Contains(item.Operation, StringComparer.Ordinal);
        if (planning)
            return Success(new LibraryWritePlanInfo { Library = library, Target = lifecycle ? library : target,
                AlreadyOpen = true, MasterCopyExists = item.Name == "Taken" });

        if (item.Name == "Taken")
            return Failure(WorkerFailureCategories.GuardBlocked, "library_master_copy_exists");
        if (item.Name == "Renamed")
            return Failure(WorkerFailureCategories.PostconditionFailed, "The created master copy has a different name.",
                new LibraryWriteOutcomeInfo { ActualName = "Renamed_1" });
        if (item.Name == "MalformedOutcome")
            return """{"success":false,"failureCategory":"postcondition_failed","error":"PRIVATE_LIBRARY_OUTCOME_ERROR","libraryWriteOutcome":{"saved":null,"actualName":"PRIVATE_LIBRARY_OUTCOME_VALUE","PRIVATE_LIBRARY_OUTCOME_MEMBER":true}}""";

        return Success(new LibraryWriteResultInfo { Library = library, Target = lifecycle ? library : target,
            Metadata = item.Operation == "create_global" ? new LibraryMetadataInfo { Author = item.Author ?? "Native default", Complete = true } : null,
            AlreadyOpen = true, ActualName = lifecycle ? null : item.Name,
            Saved = item.Operation == "save_global" ? true : null,
            ArchivePath = item.Operation == "archive_global" ? "C:/Fixture/Archive.zal21" : null });
    }

    private static string TypeOperation(LibraryWriteRequestInfo item, bool planning)
    {
        var name = item.PlcObjectName ?? item.TypeName ?? "FixtureType";
        var library = new LibraryIdentityInfo { Scope = item.Scope ?? item.SourceScope ?? "project", Kind = "library" };
        var type = new LibraryTypeResultInfo { TypeName = name,
            TypeGuid = name == "CreatedType" ? "11111111-1111-1111-1111-111111111111" : "22222222-2222-2222-2222-222222222222",
            FolderPath = item.FolderPath ?? [], VersionNumber = item.VersionNumber ?? "0.0.1",
            VersionGuid = "33333333-3333-3333-3333-333333333333",
            VersionState = item.Operation is "create_type" or "create_type_version" ? "InWork" : "Committed" };
        var target = new LibraryIdentityInfo { Scope = library.Scope, Kind = "type", Name = name,
            TypeGuid = type.TypeGuid, FolderKind = "types", FolderPath = type.FolderPath };
        var missing = name == "CreatedType" && item.Operation != "create_type" && !_createdType;
        if (planning)
            return Success(new LibraryWritePlanInfo { Library = library, Target = missing ? null : target,
                Type = type, MissingTargets = missing ? [target] : null });
        if (missing)
            return Failure(WorkerFailureCategories.TargetNotFound, "The scripted type has not been created.");
        if (item.Operation == "create_type_version"
            && item.ExpectedContentHash != ContentHashRules.Compute("xml", "<Document />"))
            return Failure(WorkerFailureCategories.StateChanged, "The scripted PLC content has changed.");
        if (item.Operation == "create_type") _createdType = true;
        if (item.Operation == "create_type_version") type.ReplacedInWork = item.ReplaceInWork == true;
        if (item.Operation == "set_default_type_version")
        { type.DefaultBefore = "0.0.1"; type.DefaultAfter = type.VersionNumber; }
        if (item.Operation == "instantiate_type_version") type.InstancePaths = [item.PlcName + "/FixtureType"];
        if (item.Operation is "delete_type" or "delete_type_version")
        { type.TypeDeleted = true; type.DeletedVersions = [type.VersionNumber!]; }
        return Success(new LibraryWriteResultInfo { Library = library, Target = target, Type = type });
    }

    private static string Success<T>(T payload)
        => JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = WorkerJson.SerializePayload(payload) }, WorkerJson.Envelope);

    private static string Failure(string category, string message, LibraryWriteOutcomeInfo? outcome = null)
        => JsonSerializer.Serialize(new WorkerResponse { Success = false, FailureCategory = category, Error = message,
            LibraryWriteOutcome = outcome }, WorkerJson.Envelope);
}
