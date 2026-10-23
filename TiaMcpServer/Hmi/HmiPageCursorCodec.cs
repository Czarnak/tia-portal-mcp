using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TiaMcpServer.Cursors;
using TiaMcpServer.Network;
using TiaMcpServer.ProjectTree;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Hmi;

internal sealed record HmiPageCursorState(
    string SnapshotId,
    string Operation,
    string QueryHash,
    int Offset,
    ProjectBindingCursorState HostBinding);

internal sealed class HmiPageCursorException : Exception
{
    internal HmiPageCursorException(string category)
        : base("The supplied hmi_read cursor is invalid.")
    {
        Category = category;
    }

    internal string Category { get; }
}

/// <summary>
/// Seals hmi_read page cursors with the process-scoped protector. Public only so the tool method can
/// receive it from DI; every member is internal.
/// </summary>
public sealed class HmiPageCursorCodec
{
    private const string Purpose = "hmi-page";
    private readonly AuthenticatedCursorProtector _protector;

    internal HmiPageCursorCodec(AuthenticatedCursorProtector protector)
    {
        ArgumentNullException.ThrowIfNull(protector);
        _protector = protector;
    }

    internal string Encode(HmiPageCursorState state)
    {
        try
        {
            Validate(state);
            return _protector.Protect(Purpose, state);
        }
        catch (Exception exception) when (exception is ArgumentException
            or AuthenticatedCursorException
            or AuthenticatedCursorSizeException
            or JsonException)
        {
            throw new HmiPageCursorException(WorkerFailureCategories.InvalidCursor);
        }
    }

    /// <exception cref="HmiPageCursorException">Malformed, tampered, other key or other process: invalid_cursor.</exception>
    internal HmiPageCursorState Decode(string cursor)
    {
        try
        {
            var result = _protector.Unprotect<HmiPageCursorState>(Purpose, cursor);
            if (result.Status != AuthenticatedCursorStatus.Success)
            {
                throw new JsonException("Cursor was issued by another process.");
            }

            Validate(result.State);
            return result.State!;
        }
        catch (Exception exception) when (exception is AuthenticatedCursorException
            or ArgumentException
            or JsonException)
        {
            throw new HmiPageCursorException(WorkerFailureCategories.InvalidCursor);
        }
    }

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

    private static void Validate(HmiPageCursorState? state)
    {
        if (state is null
            || !ProjectTreeCursorCodec.IsValidSnapshotId(state.SnapshotId)
            || !HmiOperationCatalog.IsPaged(state.Operation)
            || !ProjectTreeCursorCodec.IsValidQueryHash(state.QueryHash)
            || state.Offset < 0
            || state.HostBinding is null
            || !state.HostBinding.IsValid())
        {
            throw new JsonException("hmi_read cursor payload values are invalid.");
        }
    }
}
