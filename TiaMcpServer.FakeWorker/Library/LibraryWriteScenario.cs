using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.FakeWorker.Library;

/// <summary>Plan-only library-write fixture; mutation scenarios are added by the protocol suite.</summary>
internal static class LibraryWriteScenario
{
    public static string Handle(string line)
    {
        var request = JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Envelope)!;
        if (request.Method != LibraryWriteMethods.GlobalPlan || request.LibraryWrite is not { } item
            || item.Operation != "create_folder" || LibraryWriteMethods.Validate(item) is not null)
            return JsonSerializer.Serialize(new WorkerResponse { Success = false,
                FailureCategory = WorkerFailureCategories.ValidationError, Error = "Invalid library preview request." }, WorkerJson.Envelope);
        var library = new LibraryIdentityInfo { Scope = "global", Kind = "library", Name = item.LibraryName ?? "Fixture",
            LibraryName = item.LibraryName, LibraryPath = item.LibraryPath ?? "C:/Fixture/Library.al21" };
        var target = new LibraryIdentityInfo { Scope = "global", Kind = "folder", Name = item.Name ?? "Folder",
            LibraryName = library.LibraryName, LibraryPath = library.LibraryPath,
            FolderKind = item.FolderKind, FolderPath = (item.FolderPath ?? []).Concat(new[] { item.Name! }).ToList() };
        return JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = WorkerJson.SerializePayload(
            new LibraryWritePlanInfo { Library = library, Target = target, AlreadyOpen = true }) }, WorkerJson.Envelope);
    }
}
