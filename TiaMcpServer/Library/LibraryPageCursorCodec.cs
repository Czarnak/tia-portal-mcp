using System.Text.Json;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Cursors;

namespace TiaMcpServer.Library;

internal sealed record LibraryPageCursorState(
    string SnapshotId,
    string Operation,
    string QueryHash,
    int Offset,
    ProjectBindingCursorState HostBinding);

/// <summary>
/// Seals library_read page cursors with the process-scoped protector. Public only so the tool method can
/// receive it from DI; every member is internal.
/// </summary>
public sealed class LibraryPageCursorCodec
{
    private static readonly CursorKind Kind = new(
        "library-page",
        "The supplied library_read cursor is invalid.",
        WorkerFailureCategories.InvalidCursor);
    private readonly AuthenticatedCursorProtector _protector;

    internal LibraryPageCursorCodec(AuthenticatedCursorProtector protector)
    {
        ArgumentNullException.ThrowIfNull(protector);
        _protector = protector;
    }

    internal string Encode(LibraryPageCursorState state)
        => _protector.Seal(Kind, state, IsValid);

    /// <exception cref="CursorException">Malformed, tampered, other key or other process: invalid_cursor.</exception>
    internal LibraryPageCursorState Decode(string cursor)
        => _protector.Open<LibraryPageCursorState>(Kind, cursor, IsValid);

    /// <summary>Hash of the operation and every selector; excludes offset, limit and cursor.</summary>
    internal static string QueryHash(LibraryReadItem item)
    {
        var key = JsonSerializer.Serialize(new object?[]
        {
            item.Operation, item.Scope, item.LibraryPath, item.LibraryName, item.FolderKind,
            item.FolderPath, item.TypeGuid, item.VersionGuid, item.Direction,
        });
        return ContentHashRules.Sha256Hex(key);
    }

    private static bool IsValid(LibraryPageCursorState state)
        => CursorStateRules.IsValidSnapshotId(state.SnapshotId)
            && LibraryReadCatalog.IsPaged(state.Operation)
            && CursorStateRules.IsLowerSha256(state.QueryHash)
            && state.Offset >= 0
            && state.HostBinding is not null
            && state.HostBinding.IsValid();
}
