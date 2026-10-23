using TiaMcpServer.Contracts.Project;

namespace TiaMcpServer.Cursors;

/// <summary>The host binding a cursor was issued under. Unbound states keep their epoch.</summary>
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
            return new ProjectBindingCursorState(false, snapshot.BindingId, snapshot.Revision, null);
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
        return IsBound == current.IsBound
            && string.Equals(BindingId, current.BindingId, StringComparison.Ordinal)
            && Revision == current.Revision
            && string.Equals(
                NormalizedProjectPath,
                current.NormalizedProjectPath,
                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Non-blank ID and non-negative revision; unbound has no path, bound an already-canonical one.</summary>
    internal bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(BindingId) || Revision is not >= 0)
        {
            return false;
        }

        if (!IsBound)
        {
            return NormalizedProjectPath is null;
        }

        var path = ProjectPathNormalization.Canonicalize(NormalizedProjectPath);
        return path is not null
            && string.Equals(NormalizedProjectPath, path, StringComparison.OrdinalIgnoreCase);
    }
}
