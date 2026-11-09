using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.FakeWorker.Library;

/// <summary>
/// Fixture answers for every library read: inspect returns one entry; a paged operation returns two pages, the
/// first carrying snapshot <see cref="SnapshotId"/>. Library name <c>Missing</c> is target_not_found and
/// <c>Malformed</c> a payload that does not decode as <see cref="LibraryPageInfo"/>.
/// </summary>
internal static class LibraryReadScenario
{
    public const string SnapshotId = "library-s1";

    public static string Handle(string line)
    {
        var request = JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Envelope)!;
        var query = request.LibraryQuery;
        var operation = LibraryReadMethods.OperationFor(request.Method);
        if (query is null || operation is null || LibraryReadMethods.Validate(operation, query) is not null)
            return Failure(WorkerFailureCategories.ValidationError, "Invalid library query.");
        if (query.LibraryName == "Missing")
            return Failure(WorkerFailureCategories.TargetNotFound, "Fixture library is not open.");
        if (query.SnapshotId is not null && query.SnapshotId != SnapshotId)
            return Failure(WorkerFailureCategories.SnapshotUnavailable, "Fixture snapshot is not held.");

        var paged = operation != "inspect_library";
        var continuation = query.SnapshotId is not null;
        var page = new LibraryPageInfo
        {
            Items = { Entry(query, continuation ? "Fixture-2" : paged ? "Fixture-1" : "Fixture") },
            Total = paged ? 2 : 1,
            Offset = query.Offset ?? 0,
            NextOffset = paged && !continuation ? 1 : null,
            SnapshotId = paged && !continuation ? SnapshotId : null,
            Complete = !paged || continuation,
        };
        var payload = query.LibraryName == "Malformed" ? "{\"secret-sentinel\":true}" : WorkerJson.SerializePayload(page);
        return JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = payload }, WorkerJson.Envelope);
    }

    private static LibraryEntryInfo Entry(LibraryQueryInfo query, string name) => new()
    {
        Identity = new() { Scope = query.Scope ?? "global", Kind = "library", Name = name, LibraryName = query.Scope == "project" ? null : name },
        IsReadOnly = true,
        IsModified = null,
        LibraryType = query.Scope == "project" ? null : "user",
        IsOpen = query.Scope == "project" ? null : true,
        Metadata = query.IncludeMetadata == true ? new LibraryMetadataInfo { Author = "Fixture author", Complete = true } : null,
        Diagnostics = { "isModified unavailable in fixture" },
    };

    private static string Failure(string category, string error)
        => JsonSerializer.Serialize(new WorkerResponse { Success = false, FailureCategory = category, Error = error }, WorkerJson.Envelope);
}
