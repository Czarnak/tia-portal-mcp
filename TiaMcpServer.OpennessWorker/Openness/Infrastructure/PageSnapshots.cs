using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Infrastructure;

/// <summary>
/// Worker-side snapshots of paged scans: at most <c>capacity</c> entries, least recently used evicted first,
/// each dropped after <c>idle</c> without a <see cref="Take"/>. An entry is keyed by the project and operation that
/// made it and pins an owner object (an HMI software, a library); a binding change is detected at
/// <see cref="Take"/>, which evicts the entry. Each domain keeps its own instance so neither evicts the other.
/// </summary>
public sealed class PageSnapshotStore<TSnapshot>
    where TSnapshot : class
{
    private const string Restart = "Restart the listing without a cursor.";

    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly Func<DateTime> utcNow;
    private readonly int capacity;
    private readonly TimeSpan idle;

    public PageSnapshotStore(Func<DateTime> utcNow, int capacity = 4, TimeSpan? idle = null)
    {
        this.utcNow = utcNow;
        this.capacity = capacity;
        this.idle = idle ?? TimeSpan.FromMinutes(10);
    }

    public void DropFor(object project, object owner)
    {
        lock (gate)
        {
            foreach (var id in entries.Where(e => object.Equals(e.Value.Project, project)
                && object.Equals(e.Value.Owner, owner)).Select(e => e.Key).ToList())
            {
                entries.Remove(id);
            }
        }
    }

    /// <summary>Holds the snapshot <paramref name="create"/> builds from the new id, and returns that id.</summary>
    public string Put(object project, object owner, string operation, Func<string, TSnapshot> create)
    {
        var id = Guid.NewGuid().ToString("N");
        lock (gate)
        {
            var now = utcNow();
            RemoveExpired(now);
            while (entries.Count >= capacity)
            {
                entries.Remove(entries.OrderBy(e => e.Value.LastUsed).First().Key);
            }

            entries.Add(id, new Entry(project, owner, operation, create(id)) { LastUsed = now });
        }

        return id;
    }

    public TSnapshot Take(object project, string snapshotId, string operation)
    {
        lock (gate)
        {
            var now = utcNow();
            RemoveExpired(now);
            if (!entries.TryGetValue(snapshotId, out var entry))
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.SnapshotUnavailable,
                    $"The page snapshot has expired, was evicted or was lost with a worker restart. {Restart}");
            }

            if (!object.Equals(entry.Project, project))
            {
                entries.Remove(snapshotId);
                throw Mismatch("another project");
            }

            if (entry.Operation != operation)
            {
                throw Mismatch("another operation");
            }

            entry.LastUsed = now;
            return entry.Snapshot;
        }
    }

    private void RemoveExpired(DateTime now)
    {
        foreach (var id in entries.Where(e => now - e.Value.LastUsed >= idle).Select(e => e.Key).ToList())
        {
            entries.Remove(id);
        }
    }

    private static WorkerOperationException Mismatch(string what)
        => new(WorkerFailureCategories.CursorSnapshotMismatch, $"The cursor belongs to {what}. {Restart}");

    private sealed class Entry(object project, object owner, string operation, TSnapshot snapshot)
    {
        public object Project { get; } = project;

        public object Owner { get; } = owner;

        public string Operation { get; } = operation;

        public TSnapshot Snapshot { get; } = snapshot;

        public DateTime LastUsed { get; set; }
    }
}

/// <summary>One page of an ordered row set, shared by the HMI and library readers.</summary>
public static class SnapshotPager
{
    /// <summary>
    /// The rows from <paramref name="offset"/>; an offset past the total is an empty page. When more rows follow,
    /// the page continues <paramref name="heldId"/> or holds the ordered rows through <paramref name="hold"/>,
    /// so <c>SnapshotId</c> is set exactly when <c>NextOffset</c> is.
    /// </summary>
    public static (IReadOnlyList<T> Items, int Total, int? NextOffset, string? SnapshotId) Page<T>(
        IReadOnlyList<T> ordered, int offset, int limit, string? heldId, Func<IReadOnlyList<object>, string> hold)
    {
        var items = ordered.Skip(offset).Take(limit).ToList();
        var end = (long)offset + limit; // long: offset is not bounded above, so int addition could wrap negative
        if (end >= ordered.Count)
        {
            return (items, ordered.Count, null, null);
        }

        return (items, ordered.Count, (int)end, heldId ?? hold(ordered.Cast<object>().ToList()));
    }
}
