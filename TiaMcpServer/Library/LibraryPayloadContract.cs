using System.Text.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Cursors;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Library;

/// <summary>The public page of every library_read operation; explicit nulls, no worker snapshot id.</summary>
public sealed record LibraryPageResult(
    List<LibraryEntryInfo> Items,
    int Total,
    int Offset,
    int? NextOffset,
    string? NextCursor,
    bool Complete,
    List<string> Diagnostics);

/// <summary>
/// The only decoder of library_read worker payloads: every operation returns one <see cref="LibraryPageInfo"/>.
/// A payload that does not decode is <c>protocol_error</c> and is never echoed. The worker snapshot id never
/// reaches the caller: it is dropped, and while more rows exist it is sealed into <c>nextCursor</c>.
/// </summary>
public static class LibraryPayloadContract
{
    public static StructuredOperationItem Project(
        LibraryReadItem item, WorkerCallResult result, LibraryPageCursorCodec codec, ProjectBindingSnapshot binding)
        => Project(item, result, Console.Error.WriteLine, (codec, binding));

    internal static StructuredOperationItem Project(
        LibraryReadItem item,
        WorkerCallResult result,
        Action<string> writeDiagnostic,
        (LibraryPageCursorCodec Codec, ProjectBindingSnapshot Binding)? cursorIssuer = null)
    {
        var warnings = result.Warnings ?? Array.Empty<string>();
        if (!result.Success)
            return StructuredOperationItem.Failed(item, result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                result.Error ?? $"Library operation '{item.Operation}' failed.", warnings);
        try
        {
            var page = CanonicalJson.NormalizeWorkerPayload<LibraryPageInfo>(result.Payload).Value;
            var projected = CanonicalJson.ToElement(new LibraryPageResult(
                page.Items, page.Total, page.Offset, page.NextOffset, IssueCursor(item, page, cursorIssuer),
                page.Complete, page.Diagnostics));
            return new(item.OperationId, item.Operation, OperationBatchStatus.Succeeded, projected, null, null, null, warnings);
        }
        catch (CursorException exception) when (exception.InnerException is AuthenticatedCursorSizeException size)
        {
            return new(item.OperationId, item.Operation, OperationBatchStatus.Omitted, null, null,
                new StructuredOperationOmission("libraryPageCursorExceededCharLimit", size.LimitChars, size.CursorChars, "library_read",
                    "Shorten the project path and start a new sequence, or narrow the request until it fits one terminal page."),
                null, warnings);
        }
        catch (Exception exception) when (exception is CursorException or JsonException)
        {
            // An unsealable snapshot id is a worker contract violation like any undecodable payload.
            StructuredPayloadProjection.TryWriteProtocolDiagnostic(item.Operation, writeDiagnostic);
            return StructuredOperationItem.Failed(item, WorkerFailureCategories.ProtocolError,
                $"The worker payload for '{item.Operation}' did not match its declared result contract and was rejected.", warnings);
        }
    }

    private static string? IssueCursor(
        LibraryReadItem item,
        LibraryPageInfo page,
        (LibraryPageCursorCodec Codec, ProjectBindingSnapshot Binding)? cursorIssuer)
        => cursorIssuer is { } issuer && page.SnapshotId is { } snapshotId && page.NextOffset is { } nextOffset
            ? issuer.Codec.Encode(new LibraryPageCursorState(
                snapshotId, item.Operation, LibraryPageCursorCodec.QueryHash(item), nextOffset,
                ProjectBindingCursorState.FromSnapshot(issuer.Binding)))
            : null;
}
