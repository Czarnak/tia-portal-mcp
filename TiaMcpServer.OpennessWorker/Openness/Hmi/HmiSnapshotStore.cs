using Siemens.Engineering.HmiUnified;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Hmi;

/// <summary>The name-ordered rows of one paged scan, held so a continuation reads only its page.</summary>
public sealed record HmiSnapshot(HmiSoftware Software, IReadOnlyList<object> Ordered, string Id);

/// <summary>
/// Worker-side snapshots of paged hmi_read scans (spec §4.2): at most <c>capacity</c> entries, least recently
/// used evicted first, each dropped after <c>idle</c> without a <see cref="Take"/>. A snapshot is bound to the
/// project and operation that made it; a binding change is detected at <see cref="Take"/>, which evicts the entry.
/// </summary>
public sealed class HmiSnapshotStore
{
    private const string Restart = "Restart the listing without a cursor.";

    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly Func<DateTime> utcNow;
    private readonly int capacity;
    private readonly TimeSpan idle;

    public HmiSnapshotStore(Func<DateTime> utcNow, int capacity = 4, TimeSpan? idle = null)
    {
        this.utcNow = utcNow;
        this.capacity = capacity;
        this.idle = idle ?? TimeSpan.FromMinutes(10);
    }

    public static HmiSnapshotStore Shared { get; } = new(() => DateTime.UtcNow);

    public void DropFor(object project, HmiSoftware software)
    {
        lock (gate)
        {
            foreach (var id in entries.Where(e => object.Equals(e.Value.Project, project)
                && object.Equals(e.Value.Snapshot.Software, software)).Select(e => e.Key).ToList())
            {
                entries.Remove(id);
            }
        }
    }

    public string Put(object project, HmiSoftware software, string operation, IReadOnlyList<object> ordered)
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

            entries.Add(id, new Entry(project, operation, new HmiSnapshot(software, ordered, id)) { LastUsed = now });
        }

        return id;
    }

    public HmiSnapshot Take(object project, string snapshotId, string operation)
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

    private sealed class Entry(object project, string operation, HmiSnapshot snapshot)
    {
        public object Project { get; } = project;

        public string Operation { get; } = operation;

        public HmiSnapshot Snapshot { get; } = snapshot;

        public DateTime LastUsed { get; set; }
    }
}
