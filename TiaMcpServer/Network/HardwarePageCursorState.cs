using TiaMcpServer.Contracts;

namespace TiaMcpServer.Network;

internal sealed record HardwarePageCursorState(
    int Version,
    string ResolvedProjectPath,
    WorkerSessionIdentity SessionIdentity,
    ProjectBindingCursorState HostBinding,
    string QueryHash,
    int OrderingVersion,
    string SnapshotHash,
    int Offset);

internal sealed record ProjectBindingCursorState(
    bool IsBound,
    string? BindingId,
    long? Revision,
    string? NormalizedProjectPath)
{
    // Project-tree cursors retain unbound epochs; existing Network cursors keep their wire shape.
    internal static ProjectBindingCursorState FromSnapshot(ProjectBindingSnapshot snapshot, bool preserveUnboundEpoch = false)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (string.Equals(snapshot.State, ProjectBindingSnapshot.UnboundState, StringComparison.Ordinal))
        {
            return new ProjectBindingCursorState(false,
                preserveUnboundEpoch ? snapshot.BindingId : null,
                preserveUnboundEpoch ? snapshot.Revision : null,
                null);
        }

        return new ProjectBindingCursorState(
            true,
            snapshot.BindingId,
            snapshot.Revision,
            ProjectPathNormalization.Canonicalize(snapshot.ProjectPath));
    }

    internal bool Matches(ProjectBindingSnapshot snapshot, bool preserveUnboundEpoch = false)
    {
        var current = FromSnapshot(snapshot, preserveUnboundEpoch);
        if (IsBound != current.IsBound)
        {
            return false;
        }
        if (!IsBound && !preserveUnboundEpoch)
            return true;

        return string.Equals(BindingId, current.BindingId, StringComparison.Ordinal)
            && Revision == current.Revision
            && string.Equals(
                NormalizedProjectPath,
                current.NormalizedProjectPath,
                StringComparison.OrdinalIgnoreCase);
    }
}
