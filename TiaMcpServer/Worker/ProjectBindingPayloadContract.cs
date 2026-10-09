using System.Text.Json;
using TiaMcpServer.Json;
using TiaMcpServer.Contracts.Diagnostics;
using TiaMcpServer.Contracts.Project;

namespace TiaMcpServer.Worker;

internal static class ProjectBindingPayloadContract
{
    internal static TiaPortalProcessListInfo DecodeProcessList(string payload)
    {
        var listing = CanonicalJson.DeserializeWorkerPayload<TiaPortalProcessListInfo>(payload);
        if (listing.Processes is null || listing.AttachedProcessId <= 0
            || listing.Processes.Any(p => p is null || p.ProcessId <= 0)
            || listing.Processes.Select(p => p.ProcessId).Distinct().Count() != listing.Processes.Count)
            throw new JsonException("The Portal listing has inconsistent process identities.");
        var attached = listing.Processes.Where(p => p.AttachedByThisWorker).ToArray();
        if (attached.Length > 1 || (attached.Length == 1 && attached[0].ProcessId != listing.AttachedProcessId))
            throw new JsonException("The Portal listing has inconsistent attachment state.");
        return listing;
    }

    internal static PortalProjectSelectionInfo DecodeSelection(string payload, ProjectBindingSnapshot before)
    {
        var selection = CanonicalJson.DeserializeWorkerPayload<PortalProjectSelectionInfo>(payload);
        var previousPath = ProjectPathNormalization.Canonicalize(selection.PreviousProjectPath);
        if (selection.PreviousProcessId <= 0
            || (previousPath is null && (selection.PreviousProjectWasWorkerOpened || selection.PreviousProjectIsModified is not null))
            || (previousPath is not null && selection.PreviousProcessId is null)
            || (before.IsVerified && (selection.PreviousProcessId != before.PortalProcessId
                || !string.Equals(previousPath, ProjectPathNormalization.Canonicalize(before.ProjectPath), StringComparison.OrdinalIgnoreCase))))
            throw new JsonException("The Portal selection does not describe the previous binding.");
        return selection;
    }
}
