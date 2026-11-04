using Siemens.Engineering.HmiUnified;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;

namespace TiaMcpServer.OpennessWorker.Openness.Hmi;

/// <summary>The name-ordered rows of one paged scan, held so a continuation reads only its page.</summary>
public sealed record HmiSnapshot(HmiSoftware Software, IReadOnlyList<object> Ordered, string Id);

/// <summary>
/// Worker-side snapshots of paged hmi_read scans (spec §4.2) on the shared <see cref="PageSnapshotStore{TSnapshot}"/>:
/// at most <c>capacity</c> entries, least recently used evicted first, each dropped after <c>idle</c> without a
/// <see cref="Take"/>. A snapshot is bound to the project and operation that made it; a binding change is detected
/// at <see cref="Take"/>, which evicts the entry.
/// </summary>
public sealed class HmiSnapshotStore
{
    private readonly PageSnapshotStore<HmiSnapshot> core;

    public HmiSnapshotStore(Func<DateTime> utcNow, int capacity = 4, TimeSpan? idle = null)
        => core = new PageSnapshotStore<HmiSnapshot>(utcNow, capacity, idle);

    public static HmiSnapshotStore Shared { get; } = new(() => DateTime.UtcNow);

    public void DropFor(object project, HmiSoftware software) => core.DropFor(project, software);

    public string Put(object project, HmiSoftware software, string operation, IReadOnlyList<object> ordered)
        => core.Put(project, software, operation, id => new HmiSnapshot(software, ordered, id));

    public HmiSnapshot Take(object project, string snapshotId, string operation) => core.Take(project, snapshotId, operation);
}
