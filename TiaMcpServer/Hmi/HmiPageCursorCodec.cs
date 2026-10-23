using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TiaMcpServer.Cursors;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Hmi;

internal sealed record HmiPageCursorState(
    string SnapshotId,
    string Operation,
    string QueryHash,
    int Offset,
    ProjectBindingCursorState HostBinding);

/// <summary>
/// Seals hmi_read page cursors with the process-scoped protector. Public only so the tool method can
/// receive it from DI; every member is internal.
/// </summary>
public sealed class HmiPageCursorCodec
{
    private static readonly CursorKind Kind = new(
        "hmi-page",
        "The supplied hmi_read cursor is invalid.",
        WorkerFailureCategories.InvalidCursor);
    private readonly AuthenticatedCursorProtector _protector;

    internal HmiPageCursorCodec(AuthenticatedCursorProtector protector)
    {
        ArgumentNullException.ThrowIfNull(protector);
        _protector = protector;
    }

    internal string Encode(HmiPageCursorState state)
        => _protector.Seal(Kind, state, IsValid);

    /// <exception cref="CursorException">Malformed, tampered, other key or other process: invalid_cursor.</exception>
    internal HmiPageCursorState Decode(string cursor)
        => _protector.Open<HmiPageCursorState>(Kind, cursor, IsValid);

    /// <summary>Hash of the operation and every filter that shapes the row set; excludes paging.</summary>
    internal static string QueryHash(HmiOperationRequest operation)
    {
        var key = JsonSerializer.Serialize(new[]
        {
            operation.Operation, operation.HmiName, operation.TableName, operation.Language, operation.AlarmKind,
            operation.DataLogName, operation.TagName, operation.ScreenName, operation.Category, operation.Name,
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
    }

    private static bool IsValid(HmiPageCursorState state)
        => CursorStateRules.IsValidSnapshotId(state.SnapshotId)
            && HmiOperationCatalog.IsPaged(state.Operation)
            && CursorStateRules.IsLowerSha256(state.QueryHash)
            && state.Offset >= 0
            && state.HostBinding is not null
            && state.HostBinding.IsValid();
}
