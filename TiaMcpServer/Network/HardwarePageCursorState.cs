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
    internal static ProjectBindingCursorState FromSnapshot(ProjectBindingSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (string.Equals(snapshot.State, ProjectBindingSnapshot.UnboundState, StringComparison.Ordinal))
        {
            return new ProjectBindingCursorState(false, null, null, null);
        }

        return new ProjectBindingCursorState(
            true,
            snapshot.BindingId,
            snapshot.Revision,
            ProjectPathNormalization.Canonicalize(snapshot.ProjectPath));
    }

    internal bool Matches(ProjectBindingSnapshot snapshot)
    {
        var current = FromSnapshot(snapshot);
        if (!IsBound || !current.IsBound)
        {
            return !IsBound && !current.IsBound;
        }

        return string.Equals(BindingId, current.BindingId, StringComparison.Ordinal)
            && Revision == current.Revision
            && string.Equals(
                NormalizedProjectPath,
                current.NormalizedProjectPath,
                StringComparison.OrdinalIgnoreCase);
    }
}
