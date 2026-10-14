using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiTags;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Hmi;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Hmi;

[Collection(HmiSnapshotStoreCollection.Name)]
public class HmiSnapshotStoreTests
{
    private const string Op = "hmi_list_tags";
    private static readonly object[] Rows = { "a", "b" };

    private DateTime now = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);

    private HmiSnapshotStore Store() => new(() => now);

    [Fact]
    public void DropFor_RemovesOnlyThatHmisSnapshots()
    {
        var store = Store();
        var project = new object();
        var otherProject = new object();
        var target = new HmiSoftware();
        var other = new HmiSoftware();
        var tags = store.Put(project, target, Op, Rows);
        var alarms = store.Put(project, target, "hmi_list_alarms", Rows);
        var retainedHmi = store.Put(project, other, Op, Rows);
        var retainedProject = store.Put(otherProject, target, Op, Rows);
        store.DropFor(project, target);
        Assert.Equal(WorkerFailureCategories.SnapshotUnavailable, TakeFails(store, project, tags, Op).FailureCategory);
        Assert.Equal(WorkerFailureCategories.SnapshotUnavailable, TakeFails(store, project, alarms, "hmi_list_alarms").FailureCategory);
        Assert.Same(other, store.Take(project, retainedHmi, Op).Software);
        Assert.Same(target, store.Take(otherProject, retainedProject, Op).Software);
    }

    private static WorkerOperationException TakeFails(HmiSnapshotStore store, object project, string id, string operation)
        => Assert.Throws<WorkerOperationException>(() => store.Take(project, id, operation));

    [Fact]
    public void Put_Fifth_EvictsLeastRecentlyUsed()
    {
        var store = Store();
        var project = new object();
        var ids = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            ids.Add(store.Put(project, new HmiSoftware(), Op, Rows));
            now = now.AddSeconds(1);
        }

        store.Take(project, ids[0], Op); // the first is now the most recently used
        now = now.AddSeconds(1);
        var fifth = store.Put(project, new HmiSoftware(), Op, Rows);

        Assert.Equal(WorkerFailureCategories.SnapshotUnavailable, TakeFails(store, project, ids[1], Op).FailureCategory);
        foreach (var kept in new[] { ids[0], ids[2], ids[3], fifth })
        {
            Assert.Equal(Rows, store.Take(project, kept, Op).Ordered);
        }
    }

    [Fact]
    public void Take_AfterIdleTimeout_IsSnapshotUnavailable()
    {
        var store = Store();
        var project = new object();
        var id = store.Put(project, new HmiSoftware(), Op, Rows);

        now = now.AddMinutes(10);
        var ex = TakeFails(store, project, id, Op);

        Assert.Equal(WorkerFailureCategories.SnapshotUnavailable, ex.FailureCategory);
        Assert.Contains("without a cursor", ex.Message);
        Assert.Equal(WorkerFailureCategories.SnapshotUnavailable, TakeFails(store, project, "unknown", Op).FailureCategory);
    }

    [Fact]
    public void Take_RefreshesSlidingExpiry()
    {
        var store = Store();
        var project = new object();
        var software = new HmiSoftware();
        var id = store.Put(project, software, Op, Rows);

        now = now.AddMinutes(9);
        store.Take(project, id, Op);
        now = now.AddMinutes(9);
        var snapshot = store.Take(project, id, Op);

        Assert.Same(software, snapshot.Software);
        Assert.Equal(id, snapshot.Id);
    }

    [Fact]
    public void Take_OtherProject_IsCursorSnapshotMismatchAndEvicts()
    {
        var store = Store();
        var project = new object();
        var id = store.Put(project, new HmiSoftware(), Op, Rows);

        Assert.Equal(WorkerFailureCategories.CursorSnapshotMismatch, TakeFails(store, new object(), id, Op).FailureCategory);
        Assert.Equal(WorkerFailureCategories.SnapshotUnavailable, TakeFails(store, project, id, Op).FailureCategory);
    }

    [Fact]
    public void Take_OtherOperation_IsCursorSnapshotMismatch()
    {
        var store = Store();
        var project = new object();
        var id = store.Put(project, new HmiSoftware(), Op, Rows);

        Assert.Equal(WorkerFailureCategories.CursorSnapshotMismatch, TakeFails(store, project, id, "hmi_list_alarms").FailureCategory);
        Assert.Equal(Rows, store.Take(project, id, Op).Ordered);
    }

    [Fact]
    public void Continuation_DeletedObject_FailsOnlyItsRow()
    {
        var project = new object();
        var software = new HmiSoftware();
        var tags = new[] { "a", "b", "c", "d" }.Select(n => new HmiTag { Name = n }).ToList();
        software.Tags.Items.AddRange(tags);

        var first = HmiTagReader.ListTags(software, null, null, new HmiPaging(0, 2, Op, null), project);
        Assert.NotNull(first.Page.SnapshotId);

        tags[2].NameFailure = new EngineeringObjectDisposedException("The object has been deleted.");
        var snapshot = HmiSnapshotStore.Shared.Take(project, first.Page.SnapshotId!, Op);
        var second = HmiTagReader.ListTags(snapshot.Software, null, null, new HmiPaging(2, 2, Op, snapshot), project);

        Assert.False(second.IsComplete);
        Assert.Equal(new[] { "d" }, second.Tags.Select(t => t.Name));
        Assert.Contains(second.Messages, m => m.Contains("'c'"));
        Assert.Null(second.Page.SnapshotId);
    }

    [Fact]
    public void Continuation_SoftwareGone_FailsTheItem()
    {
        var project = new object();
        var software = new HmiSoftware();
        software.Tags.Items.AddRange(new[] { "a", "b", "c" }.Select(n => new HmiTag { Name = n }));
        var first = HmiTagReader.ListTags(software, null, null, new HmiPaging(0, 2, Op, null), project);
        var snapshot = HmiSnapshotStore.Shared.Take(project, first.Page.SnapshotId!, Op);

        software.NameFailure = new EngineeringObjectDisposedException("TIA Portal has been disposed.");
        var ex = Assert.Throws<WorkerOperationException>(
            () => HmiTagReader.ListTags(snapshot.Software, null, null, new HmiPaging(2, 2, Op, snapshot), project));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, ex.FailureCategory);
    }
}
