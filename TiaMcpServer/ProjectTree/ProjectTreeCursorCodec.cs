using TiaMcpServer.Cursors;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.ProjectTree;

internal sealed record ProjectTreeCursorState(
    string SnapshotId,
    string QueryHash,
    int Offset,
    ProjectBindingCursorState HostBinding);

internal sealed class ProjectTreeCursorCodec
{
    private static readonly CursorKind Kind = new(
        "project-tree",
        "The supplied project-tree cursor is invalid.",
        WorkerFailureCategories.SnapshotUnavailable);
    private readonly AuthenticatedCursorProtector _protector;

    internal ProjectTreeCursorCodec(AuthenticatedCursorProtector protector)
    {
        ArgumentNullException.ThrowIfNull(protector);
        _protector = protector;
    }

    internal string Encode(ProjectTreeCursorState state)
        => _protector.Seal(Kind, state, IsValid);

    internal ProjectTreeCursorState Decode(string cursor)
        => _protector.Open<ProjectTreeCursorState>(Kind, cursor, IsValid);

    private static bool IsValid(ProjectTreeCursorState state)
        => CursorStateRules.IsValidSnapshotId(state.SnapshotId)
            && CursorStateRules.IsLowerSha256(state.QueryHash)
            && state.Offset >= 0
            && state.HostBinding is not null
            && state.HostBinding.IsValid();
}
