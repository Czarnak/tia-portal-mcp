using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;

namespace TiaMcpServer.Worker;

/// <summary>
/// The only decoder of <c>probe_open_project_rebind</c> payloads. A payload that does not decode
/// strictly as <see cref="ProjectRebindStateInfo"/>, or that describes a different source,
/// destination, or close decision than the pinned request, is rejected rather than trusted.
/// </summary>
internal static class ProjectRebindStatePayloadContract
{
    public static ProjectRebindStateInfo Decode(string payload, string source, string destination)
    {
        var state = CanonicalJson.Deserialize<ProjectRebindStateInfo>(payload);
        var willCloseSource = state.SourceOpenedByWorker && !SamePath(source, destination);
        if (state.SourceProjectPath is null
            || state.SourceIsModified is null
            || !SamePath(state.SourceProjectPath, source)
            || !SamePath(state.DestinationProjectPath, destination)
            || state.WillCloseSource != willCloseSource)
        {
            throw new JsonException(
                "The rebind-state payload does not describe the pinned source and destination.");
        }

        return state;
    }

    private static bool SamePath(string first, string second)
        => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
}
