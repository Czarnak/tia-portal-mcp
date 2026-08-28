using System;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Immutable host-side binding snapshot. Safety tokens retain this complete value so a worker
/// restart, Portal switch, project close/reopen, or host binding transition invalidates them.
/// </summary>
public sealed class ProjectBindingSnapshot
{
    public const string UnboundState = "unbound";
    public const string ConfiguredUnverifiedState = "configured_unverified";
    public const string VerifiedState = "verified";
    public const string InvalidatedState = "invalidated";

    public ProjectBindingSnapshot(
        string state,
        string bindingId,
        long revision,
        string? projectPath,
        string? workerSessionId,
        long? sessionGeneration,
        int? portalProcessId,
        string? invalidatedReason)
    {
        State = state;
        BindingId = bindingId;
        Revision = revision;
        ProjectPath = projectPath;
        WorkerSessionId = workerSessionId;
        SessionGeneration = sessionGeneration;
        PortalProcessId = portalProcessId;
        InvalidatedReason = invalidatedReason;
    }

    public string State { get; }

    public string BindingId { get; }

    public long Revision { get; }

    public string? ProjectPath { get; }

    public string? WorkerSessionId { get; }

    public long? SessionGeneration { get; }

    public int? PortalProcessId { get; }

    public string? InvalidatedReason { get; }

    public bool IsVerified => string.Equals(State, VerifiedState, StringComparison.Ordinal);

    public WorkerSessionIdentity? ToWorkerIdentity()
    {
        if (!IsVerified ||
            string.IsNullOrWhiteSpace(WorkerSessionId) ||
            SessionGeneration is null ||
            PortalProcessId is null ||
            string.IsNullOrWhiteSpace(ProjectPath))
        {
            return null;
        }

        return new WorkerSessionIdentity
        {
            WorkerSessionId = WorkerSessionId!,
            SessionGeneration = SessionGeneration.Value,
            PortalProcessId = PortalProcessId.Value,
            ProjectPath = ProjectPath
        };
    }

    public bool SameBinding(ProjectBindingSnapshot other)
        => other is not null
           && string.Equals(State, other.State, StringComparison.Ordinal)
           && string.Equals(BindingId, other.BindingId, StringComparison.Ordinal)
           && Revision == other.Revision
           && string.Equals(ProjectPath, other.ProjectPath, StringComparison.OrdinalIgnoreCase)
           && string.Equals(WorkerSessionId, other.WorkerSessionId, StringComparison.Ordinal)
           && SessionGeneration == other.SessionGeneration
           && PortalProcessId == other.PortalProcessId;
}
