using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Cursors;

namespace TiaMcpServer.Network;

internal sealed record NetworkObjectPageCursorState(
    int Version,
    string WorkerCursor,
    ProjectBindingCursorState HostBinding,
    WorkerSessionIdentity SessionIdentity);

internal sealed class NetworkObjectPageCursorCodec(AuthenticatedCursorProtector protector)
{
    private static readonly CursorKind Kind = new(
        "network-object-page",
        "The supplied network object cursor is not valid for this request.",
        WorkerFailureCategories.InvalidCursor);
    private readonly AuthenticatedCursorProtector _protector = protector
        ?? throw new ArgumentNullException(nameof(protector));

    internal string Encode(NetworkObjectPageCursorState state) => _protector.Seal(Kind, state, IsValid);

    internal NetworkObjectPageCursorState Decode(string cursor)
        => _protector.Open<NetworkObjectPageCursorState>(Kind, cursor, IsValid);

    private static bool IsValid(NetworkObjectPageCursorState state)
    {
        var path = ProjectPathNormalization.Canonicalize(state.SessionIdentity?.ProjectPath);
        return state.Version == 1
            && !string.IsNullOrWhiteSpace(state.WorkerCursor)
            && state.HostBinding is { IsBound: true }
            && state.HostBinding.IsValid()
            && state.SessionIdentity is not null
            && !string.IsNullOrWhiteSpace(state.SessionIdentity.WorkerSessionId)
            && state.SessionIdentity.SessionGeneration >= 0
            && state.SessionIdentity.PortalProcessId is > 0
            && path is not null
            && string.Equals(path, state.SessionIdentity.ProjectPath, StringComparison.OrdinalIgnoreCase)
            && CursorStateRules.SameProject(path, state.HostBinding.NormalizedProjectPath);
    }
}
