using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness.Hmi;
using Xunit;
using static TiaMcpServer.Tests.Hmi.HmiTestProjects;

namespace TiaMcpServer.Tests.Hmi;

public class HmiSoftwareLocatorTests
{
    [Fact]
    public void NoUnifiedHmiIsTargetNotFoundNamingListHmiDevices()
    {
        var project = ProjectWith(DeviceWith("Panel", Classic("Classic_RT")));

        var ex = Assert.Throws<WorkerOperationException>(() => HmiSoftwareLocator.FindUnique(project, null));

        Assert.Equal(WorkerFailureCategories.TargetNotFound, ex.FailureCategory);
        Assert.Contains("list_hmi_devices", ex.Message);
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

        var ex = Assert.Throws<WorkerOperationException>(() => HmiSoftwareLocator.FindUnique(project, "Good_RT"));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, ex.FailureCategory);
        Assert.Contains("item unreadable", ex.Message);
    }

    [Fact]
    public void EnumerateAllReportsUnreadableItemsAsMessagesAndKeepsGoing()
    {
        var bad = DeviceWith("Bad", Unified("Bad_RT"));
        bad.DeviceItems.Items[0].ServiceFailure = new EngineeringException("item unreadable");
        var project = ProjectWith(bad, DeviceWith("Good", Unified("Good_RT")));
        var messages = new List<string>();

        var entries = HmiSoftwareLocator.EnumerateAll(project, messages).ToList();

        Assert.Equal("Good_RT", Assert.Single(entries).SoftwareName);
        Assert.Contains("item unreadable", Assert.Single(messages));
    }
}
