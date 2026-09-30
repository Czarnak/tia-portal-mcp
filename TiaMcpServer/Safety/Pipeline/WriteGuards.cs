namespace TiaMcpServer.Safety.Pipeline;

/// <summary>A guard a domain fired for a call; the severity comes from the catalog.</summary>
public sealed record FiredGuard(string Id, string? OperationId, string Message);

/// <summary>
/// A fired guard as reported to the caller. <paramref name="Acknowledged"/> is null for
/// <c>info</c> and <c>block</c> guards, and true/false for <c>acknowledge</c> guards.
/// </summary>
public sealed record WriteGuardReport(
    string Id, string Severity, string? OperationId, string Message, bool? Acknowledged);

/// <summary>A guard's catalog entry; severity is fixed here, never chosen at fire time.</summary>
public sealed record WriteGuardDefinition(string Id, string Severity, string Description);

/// <summary>
/// The closed set of guards a call may fire. The pipeline's own
/// <see cref="PartialWriteGuardId"/> guard is always present and cannot be redefined.
/// </summary>
public sealed class WriteGuardCatalog
{
    /// <summary>Id of the pipeline guard fired when a multi-item call fails part-way.</summary>
    public const string PartialWriteGuardId = "partial_write_no_rollback";

    private static readonly WriteGuardDefinition PartialWrite = new(
        PartialWriteGuardId,
        WriteGuardSeverities.Info,
        "A multi-item call failed part-way; earlier items stay applied and are not rolled back.");

    private readonly Dictionary<string, WriteGuardDefinition> _guards = new(StringComparer.Ordinal);

    /// <summary>The catalog holding only the pipeline's own guard.</summary>
    public static WriteGuardCatalog Production { get; } = new(Array.Empty<WriteGuardDefinition>());

    public WriteGuardCatalog(IEnumerable<WriteGuardDefinition> domainGuards)
    {
        ArgumentNullException.ThrowIfNull(domainGuards);
        _guards[PartialWrite.Id] = PartialWrite;
        foreach (var guard in domainGuards)
        {
            Register(guard);
        }
    }

    public bool TryGet(string id, out WriteGuardDefinition guard)
    {
        if (id is not null && _guards.TryGetValue(id, out var found))
        {
            guard = found;
            return true;
        }

        guard = null!;
        return false;
    }

    /// <summary>Returns the definition, or throws: firing an unregistered guard is a programming error.</summary>
    public WriteGuardDefinition Get(string id)
        => TryGet(id, out var guard)
            ? guard
            : throw new InvalidOperationException($"Write guard '{id}' is not registered in the catalog.");

    private void Register(WriteGuardDefinition guard)
    {
        if (guard is null || string.IsNullOrWhiteSpace(guard.Id))
        {
            throw new ArgumentException("A write guard needs a non-blank id.", nameof(guard));
        }

        if (!WriteGuardSeverities.IsKnown(guard.Severity))
        {
            throw new ArgumentException(
                $"Write guard '{guard.Id}' has unknown severity '{guard.Severity}'.", nameof(guard));
        }

        if (!_guards.TryAdd(guard.Id, guard))
        {
            throw new ArgumentException(
                $"Write guard '{guard.Id}' is already defined; the pipeline guard cannot be redefined.",
                nameof(guard));
        }
    }
}
