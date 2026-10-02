using TiaMcpServer.Contracts;

namespace TiaMcpServer.ProjectTree;

internal sealed record ProjectTreeSnapshotContent(
    ProjectTreeQuery Query,
    IReadOnlyList<ProjectTreeFlatNode> Nodes);

internal sealed record ProjectTreeSnapshotCandidate(
    string SnapshotId,
    DateTimeOffset CreatedAt,
    string QueryHash,
    ProjectTreeSnapshotContent Content,
    IReadOnlyList<string> Warnings,
    int SerializedChars,
    ProjectBindingSnapshot HostBinding);

internal sealed record ProjectTreeSnapshotAccess<T>(bool Found, T? Value);

internal sealed record ProjectTreeSnapshotView(
    string SnapshotId,
    DateTimeOffset CreatedAt,
    DateTimeOffset IdleExpiresAt,
    string QueryHash,
    ProjectTreeSnapshotContent Content,
    IReadOnlyList<string> Warnings,
    int SerializedChars,
    ProjectBindingSnapshot HostBinding);

internal sealed class ProjectTreeSnapshotStore : IDisposable
{
    private const int DefaultMaxSnapshots = 4;
    private const int DefaultMaxSnapshotChars = 4_000_000;
    private const int DefaultMaxAggregateChars = 16_000_000;
    private static readonly TimeSpan DefaultSlidingTtl = TimeSpan.FromMinutes(10);

    private readonly object _gate = new();
    private readonly Dictionary<string, ProjectTreeSnapshotEntry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly int _maxSnapshots;
    private readonly int _maxSnapshotChars;
    private readonly int _maxAggregateChars;
    private readonly TimeSpan _slidingTtl;
    private readonly Func<string> _snapshotIdFactory;
    private int _aggregateChars;
    private bool _disposed;

    internal ProjectTreeSnapshotStore(
        TimeProvider? timeProvider = null,
        int maxSnapshots = DefaultMaxSnapshots,
        int maxSnapshotChars = DefaultMaxSnapshotChars,
        int maxAggregateChars = DefaultMaxAggregateChars,
        TimeSpan? slidingTtl = null,
        Func<string>? snapshotIdFactory = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _maxSnapshots = RequirePositive(maxSnapshots, nameof(maxSnapshots));
        _maxSnapshotChars = RequirePositive(maxSnapshotChars, nameof(maxSnapshotChars));
        _maxAggregateChars = RequirePositive(maxAggregateChars, nameof(maxAggregateChars));
        _slidingTtl = slidingTtl ?? DefaultSlidingTtl;
        if (_slidingTtl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(slidingTtl));
        }

        _snapshotIdFactory = snapshotIdFactory ?? (() => Guid.NewGuid().ToString("N"));
    }

    internal string CreateSnapshotId()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var snapshotId = _snapshotIdFactory();
            if (!ProjectTreeCursorCodec.IsValidSnapshotId(snapshotId))
            {
                throw new InvalidOperationException("The snapshot ID factory returned an invalid ID.");
            }

            return snapshotId;
        }
    }

    internal T ProjectInitial<T>(
        ProjectTreeSnapshotCandidate candidate,
        Func<ProjectTreeSnapshotView, T> project,
        Predicate<T> shouldCache)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(shouldCache);

        lock (_gate)
        {
            ThrowIfDisposed();
            ValidateCandidate(candidate);
            var now = _timeProvider.GetUtcNow();
            RemoveExpired(now);
            var entry = ProjectTreeSnapshotEntry.From(candidate, now + _slidingTtl);
            var result = project(entry.View());
            if (shouldCache(result))
            {
                entry.MarkSuccessfulAccess(now, now + _slidingTtl);
                InsertAndEvict(entry, now);
            }

            return result;
        }
    }

    internal ProjectTreeSnapshotAccess<T> Access<T>(
        string snapshotId,
        Func<ProjectTreeSnapshotView, T> project,
        Predicate<T> renewOnSuccess)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(renewOnSuccess);

        lock (_gate)
        {
            ThrowIfDisposed();
            var now = _timeProvider.GetUtcNow();
            RemoveExpired(now);
            if (!_entries.TryGetValue(snapshotId, out var entry))
            {
                return new ProjectTreeSnapshotAccess<T>(false, default);
            }

            var prospectiveExpiry = now + _slidingTtl;
            var result = project(entry.View(prospectiveExpiry));
            if (renewOnSuccess(result))
            {
                entry.MarkSuccessfulAccess(now, prospectiveExpiry);
            }

            return new ProjectTreeSnapshotAccess<T>(true, result);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _entries.Clear();
            _aggregateChars = 0;
            _disposed = true;
        }
    }

    private void InsertAndEvict(ProjectTreeSnapshotEntry entry, DateTimeOffset now)
    {
        if (entry.SerializedChars > _maxSnapshotChars
            || entry.SerializedChars > _maxAggregateChars)
        {
            throw new InvalidOperationException(
                "The project-tree snapshot exceeds a configured snapshot or aggregate character limit.");
        }

        RemoveExpired(now);
        if (_entries.ContainsKey(entry.SnapshotId))
        {
            throw new InvalidOperationException("A snapshot with the same ID is already cached.");
        }

        while (_entries.Count >= _maxSnapshots
            || _aggregateChars > _maxAggregateChars - entry.SerializedChars)
        {
            Remove(OldestEntry());
        }

        _entries.Add(entry.SnapshotId, entry);
        _aggregateChars += entry.SerializedChars;
    }

    private ProjectTreeSnapshotEntry OldestEntry()
        => _entries.Values
            .OrderBy(entry => entry.LastSuccessfulAccess)
            .ThenBy(entry => entry.CreatedAt)
            .ThenBy(entry => entry.SnapshotId, StringComparer.Ordinal)
            .First();

    private void RemoveExpired(DateTimeOffset now)
    {
        foreach (var snapshotId in _entries.Values
                     .Where(entry => entry.IdleExpiresAt <= now)
                     .Select(entry => entry.SnapshotId)
                     .ToArray())
        {
            Remove(_entries[snapshotId]);
        }
    }

    private void Remove(ProjectTreeSnapshotEntry entry)
    {
        _entries.Remove(entry.SnapshotId);
        _aggregateChars -= entry.SerializedChars;
    }

    private void ValidateCandidate(ProjectTreeSnapshotCandidate candidate)
    {
        if (!ProjectTreeCursorCodec.IsValidSnapshotId(candidate.SnapshotId)
            || !ProjectTreeCursorCodec.IsValidQueryHash(candidate.QueryHash)
            || candidate.Content is null
            || candidate.Content.Query is null
            || candidate.Content.Nodes is null
            || candidate.Warnings is null
            || candidate.HostBinding is null
            || candidate.SerializedChars < 0)
        {
            throw new ArgumentException("The project-tree snapshot candidate is internally inconsistent.", nameof(candidate));
        }
    }

    private static int RequirePositive(int value, string parameterName)
        => value > 0 ? value : throw new ArgumentOutOfRangeException(parameterName);

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class ProjectTreeSnapshotEntry
    {
        private ProjectTreeSnapshotEntry(ProjectTreeSnapshotCandidate candidate, DateTimeOffset idleExpiresAt)
        {
            SnapshotId = candidate.SnapshotId;
            CreatedAt = candidate.CreatedAt;
            QueryHash = candidate.QueryHash;
            Content = candidate.Content;
            Warnings = candidate.Warnings;
            SerializedChars = candidate.SerializedChars;
            HostBinding = candidate.HostBinding;
            IdleExpiresAt = idleExpiresAt;
            LastSuccessfulAccess = candidate.CreatedAt;
        }

        internal string SnapshotId { get; }
        internal DateTimeOffset CreatedAt { get; }
        internal string QueryHash { get; }
        internal ProjectTreeSnapshotContent Content { get; }
        internal IReadOnlyList<string> Warnings { get; }
        internal int SerializedChars { get; }
        internal ProjectBindingSnapshot HostBinding { get; }
        internal DateTimeOffset IdleExpiresAt { get; private set; }
        internal DateTimeOffset LastSuccessfulAccess { get; private set; }

        internal static ProjectTreeSnapshotEntry From(ProjectTreeSnapshotCandidate candidate, DateTimeOffset idleExpiresAt)
            => new(candidate, idleExpiresAt);

        internal ProjectTreeSnapshotView View(DateTimeOffset? idleExpiresAt = null)
            => new(
                SnapshotId,
                CreatedAt,
                idleExpiresAt ?? IdleExpiresAt,
                QueryHash,
                Content,
                Warnings,
                SerializedChars,
                HostBinding);

        internal void MarkSuccessfulAccess(DateTimeOffset now, DateTimeOffset idleExpiresAt)
        {
            LastSuccessfulAccess = now;
            IdleExpiresAt = idleExpiresAt;
        }
    }
}
