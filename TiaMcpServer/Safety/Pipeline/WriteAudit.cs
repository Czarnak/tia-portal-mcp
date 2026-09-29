using System.Text;
using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;

namespace TiaMcpServer.Safety.Pipeline;

/// <summary>
/// A guard that fired during a call. <see cref="SatisfiedBy"/> is a <see cref="GuardSatisfactions"/>
/// value when an acknowledge guard was satisfied, and null for a guard that was never satisfied.
/// </summary>
public sealed record WriteAuditGuard(
    string Id,
    string Severity,
    string? OperationId,
    string Message,
    bool? Acknowledged,
    string? SatisfiedBy);

/// <summary>One operation of the call, in request order.</summary>
public sealed record WriteAuditItem(
    string OperationId,
    string Operation,
    string? Target,
    IReadOnlyList<CheckedPrecondition> Preconditions,
    string Status,
    string? FailureCategory,
    string? FailureMessage,
    IReadOnlyList<string> Warnings,
    long? DurationMs);

/// <summary>Serializable projection of the <see cref="ProjectBindingSnapshot"/> a call ran under.</summary>
public sealed record WriteAuditBinding(
    string State,
    string BindingId,
    long Revision,
    string? ProjectPath,
    string? WorkerSessionId,
    long? SessionGeneration,
    int? PortalProcessId,
    string? InvalidatedReason)
{
    public static WriteAuditBinding From(ProjectBindingSnapshot snapshot) => new(
        snapshot.State,
        snapshot.BindingId,
        snapshot.Revision,
        snapshot.ProjectPath,
        snapshot.WorkerSessionId,
        snapshot.SessionGeneration,
        snapshot.PortalProcessId,
        snapshot.InvalidatedReason);
}

/// <summary>The one canonical audit record a guarded write call produces, whatever its phase.</summary>
public sealed record WriteAuditRecord(
    string RecordKind,
    int RecordVersion,
    DateTimeOffset Timestamp,
    string Tool,
    string ContractVersion,
    string AccessMode,
    string? ProjectPath,
    WriteAuditBinding? Binding,
    JsonElement? RequestedOperations,
    string Phase,
    string ResponseText,
    string ResponseHash,
    IReadOnlyList<WriteAuditGuard> Guards,
    IReadOnlyList<WriteAuditItem> Items,
    long? DurationMs)
{
    /// <summary>Value of <see cref="RecordKind"/> for every write record.</summary>
    public const string Kind = "write";

    /// <summary>Value of <see cref="RecordVersion"/> this build writes.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Spells the access mode the way the rest of the server does.</summary>
    public static string ModeName(McpAccessMode mode)
        => mode == McpAccessMode.ReadOnly ? "read-only" : "read-write";
}

/// <summary>Receives one record per guarded write call. Implementations must never throw.</summary>
public interface IWriteAuditSink
{
    void Append(WriteAuditRecord record);
}

/// <summary>
/// Appends each record as one canonical JSON line to <c>writes-yyyy-MM-dd.jsonl</c> (UTC date,
/// UTF-8 without a byte-order mark) beside the legacy daily audit files, which it never touches.
/// A failed append is reported on stderr and swallowed so it cannot hide the write result.
/// </summary>
public sealed class JsonlWriteAuditSink : IWriteAuditSink
{
    private static readonly object AppendLock = new();
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _directory;

    /// <param name="directory">Audit directory; null resolves the same default as the legacy audit.</param>
    public JsonlWriteAuditSink(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TiaMcpServer",
            "audit");
    }

    /// <summary>The file a record stamped <paramref name="timestamp"/> is appended to.</summary>
    public static string FileNameFor(DateTimeOffset timestamp)
        => $"writes-{timestamp.UtcDateTime:yyyy-MM-dd}.jsonl";

    public void Append(WriteAuditRecord record)
    {
        try
        {
            var line = CanonicalJson.Serialize(record) + "\n";
            lock (AppendLock)
            {
                Directory.CreateDirectory(_directory);
                File.AppendAllText(Path.Combine(_directory, FileNameFor(record.Timestamp)), line, Utf8NoBom);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"TiaMcpServer: failed to write audit record for '{record.Tool}': {ex.Message}");
        }
    }
}
