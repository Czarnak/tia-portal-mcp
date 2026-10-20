using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Hmi;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;
using static TiaMcpServer.Tests.Hmi.HmiTestProjects;

namespace TiaMcpServer.Tests.Hmi;

[Collection(HmiSnapshotStoreCollection.Name)]
public class HmiSoftwareLocatorTests
{
    public HmiSoftwareLocatorTests() => HmiSoftwareLocator.ResetCache();

    [Theory]
    [InlineData("deviceName", false)]
    [InlineData("deviceName", true)]
    [InlineData("itemEnumeration", false)]
    [InlineData("itemEnumeration", true)]
    [InlineData("itemService", false)]
    [InlineData("itemService", true)]
    [InlineData("softwareName", false)]
    [InlineData("softwareName", true)]
    public void StrictDiscovery_EngineeringFailureUsesDisposalObservation(string failingRead, bool disposed)
    {
        var project = UnreadableProject(failingRead, new EngineeringException("unreadable"));
        var revision = 0L;

        var result = WorkerExecution.Execute(() =>
        {
            if (disposed) revision++;
            HmiSoftwareLocator.FindUnique(project, "Good_RT");
            return new WorkerResponse { Success = true };
        }, () => revision);

        Assert.False(result.Success);
        Assert.Equal(disposed ? WorkerFailureCategories.PortalLost : WorkerFailureCategories.WorkerOperationFailed,
            result.FailureCategory);
        Assert.Contains("unreadable", result.Error);
    }

    [Theory]
    [InlineData("deviceName")]
    [InlineData("itemEnumeration")]
    [InlineData("itemService")]
    [InlineData("softwareName")]
    public void StrictDiscovery_PreservesOriginalEngineeringFailure(string failingRead)
    {
        var failure = new EngineeringException("unreadable");
        var project = UnreadableProject(failingRead, failure);

        Assert.Same(failure,
            Assert.Throws<EngineeringException>(() => HmiSoftwareLocator.FindUnique(project, "Good_RT")));
    }

    [Theory]
    [InlineData("deviceName")]
    [InlineData("itemEnumeration")]
    [InlineData("itemService")]
    [InlineData("softwareName")]
    public void LenientDiscovery_NonRecoverableFailureEscapes(string failingRead)
    {
        var failure = new NonRecoverableException("closed");
        var project = UnreadableProject(failingRead, failure);
        var messages = new List<string>();

        Assert.Same(failure,
            Assert.Throws<NonRecoverableException>(() => HmiSoftwareLocator.EnumerateAll(project, messages).ToList()));
        Assert.Empty(messages);
    }

    private static Siemens.Engineering.Project UnreadableProject(string failingRead, Exception failure)
    {
        var software = Unified("Bad_RT");
        var bad = DeviceWith("Bad", software);
        switch (failingRead)
        {
            case "deviceName": bad.NameFailure = failure; break;
            case "itemEnumeration":
                bad.DeviceItems.Items.Clear();
                bad.DeviceItems.EnumerationFailure = failure;
                break;
            case "itemService": bad.DeviceItems.Items[0].ServiceFailure = failure; break;
            case "softwareName": software.NameFailure = failure; break;
            default: throw new ArgumentOutOfRangeException(nameof(failingRead));
        }
        return ProjectWith(DeviceWith("Good", Unified("Good_RT")), bad);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CachedLiveness_PortalLossDoesNotRelocateOrHideTheFailure(bool softwareFails)
    {
        var software = Unified("Runtime");
        var device = DeviceWith("Panel", software);
        var project = ProjectWith(device);
        HmiSoftwareLocator.FindUnique(project, "Runtime");
        var walked = project.Devices.YieldedItemCount;
        var failure = new NonRecoverableException("closed");
        if (softwareFails) software.NameFailure = failure;
        else device.NameFailure = failure;
        project.Devices.Items.Clear();
        project.Devices.Items.Add(DeviceWith("Replacement", Unified("Runtime")));

        Assert.Same(failure, Assert.Throws<NonRecoverableException>(() => HmiSoftwareLocator.FindUnique(project, "Runtime")));
        Assert.Equal(walked, project.Devices.YieldedItemCount);
    }

    [Fact]
    public void CachedHit_RechecksDeviceWalkForUniqueness()
    {
        var software = Unified("Runtime");
        var project = ProjectWith(DeviceWith("Panel", software));
        HmiSoftwareLocator.FindUnique(project, "Runtime");
        var walked = project.Devices.YieldedItemCount;
        Assert.Same(software, HmiSoftwareLocator.FindUnique(project, "runtime").Software);
        Assert.True(project.Devices.YieldedItemCount > walked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmSelectorRejectsNewDeviceNameAmbiguity(bool rename)
    {
        var second = DeviceWith("Other", Unified("SoftwareB"));
        var project = ProjectWith(DeviceWith("DeviceA", Unified("Shared")));
        if (rename) project.Devices.Items.Add(second);
        HmiSoftwareLocator.FindUnique(project, "Shared");
        second.Name = "shared";
        if (!rename) project.Devices.Items.Add(second);
        Assert.Equal(WorkerFailureCategories.TargetAmbiguous,
            Assert.Throws<WorkerOperationException>(() => HmiSoftwareLocator.FindUnique(project, "Shared")).FailureCategory);
        HmiSoftwareLocator.ResetCache();
        Assert.Equal(WorkerFailureCategories.TargetAmbiguous,
            Assert.Throws<WorkerOperationException>(() => HmiSoftwareLocator.FindUnique(project, "Shared")).FailureCategory);
    }

    [Fact]
    public void WarmSelectorUsesReplacementEvenWhenRemovedProxyNamesRemainReadable()
    {
        var project = ProjectWith(DeviceWith("Panel", Unified("Runtime")));
        HmiSoftwareLocator.FindUnique(project, "Runtime");
        project.Devices.Items.Clear();
        var replacement = Unified("Runtime");
        project.Devices.Items.Add(DeviceWith("NewPanel", replacement));
        Assert.Same(replacement, HmiSoftwareLocator.FindUnique(project, "Runtime").Software);
        project.Devices.Items.Clear();
        Assert.Equal(WorkerFailureCategories.TargetNotFound,
            Assert.Throws<WorkerOperationException>(() => HmiSoftwareLocator.FindUnique(project, "Runtime")).FailureCategory);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RenamedHmi_MissesAndRelocates(bool selectSoftware)
    {
        var old = Unified("Runtime");
        var device = DeviceWith("Panel", old);
        var project = ProjectWith(device);
        var selector = selectSoftware ? "Runtime" : "Panel";
        HmiSoftwareLocator.FindUnique(project, selector);
        if (selectSoftware) old.Name = "Renamed";
        else device.Name = "Renamed";
        var replacement = Unified(selectSoftware ? selector : "OtherRuntime");
        project.Devices.Items.Add(DeviceWith(selectSoftware ? "OtherPanel" : selector, replacement));
        Assert.Same(replacement, HmiSoftwareLocator.FindUnique(project, selector).Software);
    }

    [Fact]
    public void NullSelector_IsNeverCached()
    {
        var project = ProjectWith(DeviceWith("Panel", Unified("Runtime")));
        HmiSoftwareLocator.FindUnique(project, null);
        project.Devices.Items.Add(DeviceWith("Other", Unified("OtherRuntime")));
        Assert.Equal(WorkerFailureCategories.TargetAmbiguous,
            Assert.Throws<WorkerOperationException>(() => HmiSoftwareLocator.FindUnique(project, null)).FailureCategory);
    }

    [Fact]
    public void DifferentRoot_EmptiesCache()
    {
        var first = ProjectWith(DeviceWith("Panel", Unified("Runtime")));
        var second = ProjectWith(DeviceWith("Panel", Unified("Runtime")));
        HmiSoftwareLocator.FindUnique(first, "Runtime");
        HmiSoftwareLocator.FindUnique(second, "Runtime");
        var walked = first.Devices.YieldedItemCount;
        HmiSoftwareLocator.FindUnique(first, "Runtime");
        Assert.True(first.Devices.YieldedItemCount > walked);
    }

    [Fact]
    public void EqualRoot_KeepsCache()
    {
        var software = Unified("Runtime");
        var first = new EqualRoot();
        first.Devices.Items.Add(DeviceWith("Panel", software));
        HmiSoftwareLocator.FindUnique(first, "Runtime");
        var equivalent = new EqualRoot();
        equivalent.Devices.Items.Add(first.Devices.Items[0]);
        Assert.Same(software, HmiSoftwareLocator.FindUnique(equivalent, "Runtime").Software);
    }

    private sealed class EqualRoot : ProjectBase
    {
        public DeviceSystemGroup UngroupedDevicesGroup { get; } = new();
        public override bool Equals(object? obj) => obj is EqualRoot;
        public override int GetHashCode() => 0;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LivenessReadThrows_Evicts(bool softwareFails)
    {
        var old = Unified("Runtime");
        var device = DeviceWith("Panel", old);
        var project = ProjectWith(device);
        HmiSoftwareLocator.FindUnique(project, "Runtime");
        if (softwareFails) old.NameFailure = new EngineeringObjectDisposedException("deleted");
        else device.NameFailure = new EngineeringObjectDisposedException("deleted");
        var replacement = Unified("Runtime");
        project.Devices.Items.Clear();
        project.Devices.Items.Add(DeviceWith("Replacement", replacement));
        Assert.Same(replacement, HmiSoftwareLocator.FindUnique(project, "Runtime").Software);
    }

    [Fact]
    public void CachedDeviceSelector_SoftwareRenameStillMatches()
    {
        var software = Unified("Runtime");
        var project = ProjectWith(DeviceWith("Panel", software));
        HmiSoftwareLocator.FindUnique(project, "Panel");
        software.Name = "Renamed";
        Assert.Same(software, HmiSoftwareLocator.FindUnique(project, "Panel").Software);
    }

    [Fact]
    public void FindUnique_NoName_NoHmiAtAll_IsTargetNotFound()
    {
        var project = ProjectWith();

        var ex = Assert.Throws<WorkerOperationException>(() => HmiSoftwareLocator.FindUnique(project, null));

        Assert.Equal(WorkerFailureCategories.TargetNotFound, ex.FailureCategory);
        Assert.Contains("list_hmi_devices", ex.Message);
    }

    [Fact]
    public void FindUnique_NoName_OnlyClassic_IsTargetKindUnsupported()
    {
        var project = ProjectWith(DeviceWith("Panel", Classic("Classic_RT")));

        var ex = Assert.Throws<WorkerOperationException>(() => HmiSoftwareLocator.FindUnique(project, null));

        Assert.Equal(WorkerFailureCategories.TargetKindUnsupported, ex.FailureCategory);
    }

    [Fact]
    public void FindUnique_ClassicMessage_DoesNotNameTool()
    {
        var project = ProjectWith(DeviceWith("Panel", Classic("Classic_RT")));

        var ex = Assert.Throws<WorkerOperationException>(() => HmiSoftwareLocator.FindUnique(project, null));

        Assert.DoesNotContain("hmi_read", ex.Message);
        Assert.Contains("WinCC Unified", ex.Message);
    }

    [Fact]
    public void OmittedNameWithTwoUnifiedIsAmbiguous()
    {
        var project = ProjectWith(DeviceWith("A", Unified("A_RT")), DeviceWith("B", Unified("B_RT")));

        var ex = Assert.Throws<WorkerOperationException>(() => HmiSoftwareLocator.FindUnique(project, null));

        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, ex.FailureCategory);
    }

    [Fact]
    public void OmittedNameWithOneUnifiedAndOneClassicPicksTheUnified()
    {
        var project = ProjectWith(DeviceWith("Old", Classic("Old_RT")), DeviceWith("New", Unified("New_RT")));

        var found = HmiSoftwareLocator.FindUnique(project, null);

        Assert.Equal("New", found.DeviceName);
        Assert.Equal("New_RT", found.Software.Name);
    }

    [Fact]
    public void NameMatchingSoftwareOfOneAndDeviceOfAnotherIsAmbiguous()
    {
        var project = ProjectWith(DeviceWith("DeviceA", Unified("Shared")), DeviceWith("shared", Unified("SoftwareB")));

        var ex = Assert.Throws<WorkerOperationException>(() => HmiSoftwareLocator.FindUnique(project, "Shared"));

        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, ex.FailureCategory);
    }

    [Fact]
    public void ClassicMatchIsTargetKindUnsupported()
    {
        var project = ProjectWith(DeviceWith("Old", Classic("Old_RT")), DeviceWith("New", Unified("New_RT")));

        var ex = Assert.Throws<WorkerOperationException>(() => HmiSoftwareLocator.FindUnique(project, "old_rt"));

        Assert.Equal(WorkerFailureCategories.TargetKindUnsupported, ex.FailureCategory);
    }

    [Fact]
    public void GroupedDeviceIsFound()
    {
        var project = ProjectWith();
        var group = new DeviceUserGroup { Name = "G" };
        var child = new DeviceUserGroup { Name = "Child" };
        child.Devices.Items.Add(DeviceWith("Nested", Unified("Nested_RT")));
        group.Groups.Items.Add(child);
        project.DeviceGroups.Items.Add(group);

        var found = HmiSoftwareLocator.FindUnique(project, "Nested_RT");

        Assert.Equal("Nested", found.DeviceName);
    }

    [Fact]
    public void UnreadableDeviceItemIsWorkerOperationFailed()
    {
        var bad = DeviceWith("Bad", Unified("Bad_RT"));
        bad.DeviceItems.Items[0].ServiceFailure = new EngineeringException("item unreadable");
        var project = ProjectWith(DeviceWith("Good", Unified("Good_RT")), bad);

        var result = WorkerExecution.Execute(() =>
        {
            HmiSoftwareLocator.FindUnique(project, "Good_RT");
            return new WorkerResponse { Success = true };
        }, () => 0);

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, result.FailureCategory);
        Assert.Contains("item unreadable", result.Error);
    }

    [Theory]
    [InlineData("deviceName")]
    [InlineData("itemEnumeration")]
    [InlineData("itemService")]
    [InlineData("softwareName")]
    public void EnumerateAllReportsUnreadableItemsAsMessagesAndKeepsGoing(string failingRead)
    {
        var project = UnreadableProject(failingRead, new EngineeringException("item unreadable"));
        var messages = new List<string>();

        var entries = HmiSoftwareLocator.EnumerateAll(project, messages).ToList();

        Assert.Equal("Good_RT", Assert.Single(entries).SoftwareName);
        Assert.Contains("item unreadable", Assert.Single(messages));
    }
}
