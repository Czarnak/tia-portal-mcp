using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;

namespace TiaMcpServer.Worker;

/// <summary>Strict local-owner evidence shared by binding, status and lifecycle decoding.</summary>
internal static class ProjectContextPayloadContract
{
    internal static void Validate(ProjectContextInfo? context, string bindingPath)
    {
        var path = ProjectPathNormalization.Canonicalize(bindingPath);
        var localPath = string.Equals(Path.GetExtension(path), ".amc21", StringComparison.OrdinalIgnoreCase);
        if (!localPath && context is null) return;
        if (!localPath || context is null
            || context.ContainerKind != ProjectContainerKinds.LocalSession
            || !MultiuserSessionModes.All.Contains(context.SessionMode)
            || context.SessionMode == MultiuserSessionModes.NotApplicable
            || !string.Equals(path, ProjectPathNormalization.Canonicalize(context.EngineeringProjectPath),
                StringComparison.OrdinalIgnoreCase)
            || context.Capabilities is null || context.Capabilities.Any(capability => capability is null)
            || context.Capabilities.Count != ProjectCapabilityCatalog.Describe(ProjectContainerKinds.LocalSession).Count)
            throw new JsonException("The local-session context does not match the engineering binding path.");

        var expected = ProjectCapabilityCatalog.Describe(ProjectContainerKinds.LocalSession);
        if (!context.Capabilities.Zip(expected).All(pair =>
                string.Equals(pair.First.Operation, pair.Second.Operation, StringComparison.Ordinal)
                && string.Equals(pair.First.Applicability, pair.Second.Applicability, StringComparison.Ordinal)))
            throw new JsonException("The local-session capabilities do not match the declared support table.");

        var provenance = context.SessionContainerPath;
        if (context.OpenedByWorker != (provenance is not null)
            || provenance is not null && (!Path.IsPathFullyQualified(provenance)
                || !string.Equals(Path.GetExtension(provenance), ".als21", StringComparison.OrdinalIgnoreCase)))
            throw new JsonException("The local-session opener provenance is inconsistent.");

        var observation = context.ConnectionObservation;
        var remote = context.RemoteIdentity;
        if (remote is not null && (string.IsNullOrWhiteSpace(remote.ServerAlias)
            || string.IsNullOrWhiteSpace(remote.Host) || remote.Port is not (> 0 and <= 65535)
            || remote.Group is null || remote.Group.IsRoot && remote.Group.Name is not null
            || !remote.Group.IsRoot && string.IsNullOrWhiteSpace(remote.Group.Name)
            || string.IsNullOrWhiteSpace(remote.ServerProjectName) || remote.LocalSessionId is < 0 or null))
            throw new JsonException("The local-session remote identity is not an exact scoped session.");
        if (observation is not null && (!ProjectServerConnectionStates.All.Contains(observation.State)
            || !ProjectServerConnectionObservationSources.All.Contains(observation.ObservationSource)
            || observation.State == ProjectServerConnectionStates.Unknown
                && context.RemoteIdentity is null
                && (observation.ObservationSource is not (ProjectServerConnectionObservationSources.SessionBind
                    or ProjectServerConnectionObservationSources.SessionOpen)
                    || observation.PreviousState is not null || observation.Transition)))
            throw new JsonException("The local-session connection observation is inconsistent.");
        if (observation is not null && context.RemoteIdentity is null
            && observation.State != ProjectServerConnectionStates.Unknown)
            throw new JsonException("A scoped remote identity is required for this observation.");
    }

    internal static void ValidateEnvelopeIdentity(string responseLine, WorkerResponse response)
    {
        var identity = response.SessionIdentity;
        var localResolved = string.Equals(Path.GetExtension(response.ResolvedProjectPath), ".amc21",
            StringComparison.OrdinalIgnoreCase);
        if (identity is null)
        {
            if (localResolved) throw new JsonException("The local-session worker identity is missing.");
            return;
        }
        if (!localResolved && !string.Equals(Path.GetExtension(identity.ProjectPath), ".amc21",
                StringComparison.OrdinalIgnoreCase) && identity.Context is null) return;
        using var document = JsonDocument.Parse(responseLine);
        if (!document.RootElement.TryGetProperty("sessionIdentity", out var rawIdentity))
            throw new JsonException("The local-session worker identity is missing.");
        var strict = CanonicalJson.DeserializeWorkerPayload<WorkerSessionIdentity>(rawIdentity.GetRawText());
        Validate(strict.Context, strict.ProjectPath!);
        response.SessionIdentity = strict;
    }
}
