using TiaMcpServer.Cursors;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Network;

internal sealed class HardwarePageCursorCodec
{
    private const int CurrentVersion = 1;
    private static readonly CursorKind Kind = new(
        "hardware-page",
        "The supplied hardware page cursor is invalid.",
        WorkerFailureCategories.InvalidCursor);
    private readonly AuthenticatedCursorProtector _protector;

    internal HardwarePageCursorCodec(AuthenticatedCursorProtector protector)
    {
        ArgumentNullException.ThrowIfNull(protector);
        _protector = protector;
    }

    internal string Encode(HardwarePageCursorState state)
        => _protector.Seal(Kind, state, IsValid);

    internal HardwarePageCursorState Decode(string cursor)
        => _protector.Open<HardwarePageCursorState>(Kind, cursor, IsValid);

    private static bool IsValid(HardwarePageCursorState state)
    {
        var normalizedResolvedPath = ProjectPathNormalization.Canonicalize(state.ResolvedProjectPath);
        return state.Version == CurrentVersion
            && normalizedResolvedPath is not null
            && string.Equals(state.ResolvedProjectPath, normalizedResolvedPath, StringComparison.OrdinalIgnoreCase)
            && state.SessionIdentity is not null
            && !string.IsNullOrWhiteSpace(state.SessionIdentity.WorkerSessionId)
            && state.SessionIdentity.SessionGeneration >= 0
            && state.SessionIdentity.PortalProcessId is > 0
            && CursorStateRules.SameProject(state.SessionIdentity.ProjectPath, state.ResolvedProjectPath)
            && state.HostBinding is not null
            && state.HostBinding.IsValid()
            && (!state.HostBinding.IsBound
                || CursorStateRules.SameProject(state.HostBinding.NormalizedProjectPath, state.ResolvedProjectPath))
            && CursorStateRules.IsLowerSha256(state.QueryHash)
            && state.OrderingVersion > 0
            && CursorStateRules.IsLowerSha256(state.SnapshotHash)
            && state.Offset >= 0;
    }
}

internal static class HardwarePageCursorValidator
{
    internal static string? Validate(
        HardwarePageCursorState state,
        NetworkOperationRequest request,
        ProjectBindingSnapshot currentBinding)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(currentBinding);

        var incomingQueryHash = HardwarePageEvidence.CreateQueryHash(
            request.DeviceName,
            request.PlcName,
            request.IncludeIoDetails,
            request.IncludeTagMatches);
        if (!string.Equals(state.QueryHash, incomingQueryHash, StringComparison.Ordinal))
        {
            return WorkerFailureCategories.CursorFilterMismatch;
        }

        return state.HostBinding.Matches(currentBinding)
            ? null
            : WorkerFailureCategories.CursorBindingMismatch;
    }
}
