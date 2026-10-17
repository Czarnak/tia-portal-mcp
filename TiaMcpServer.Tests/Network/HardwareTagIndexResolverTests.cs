using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Tags;
using TiaMcpServer.OpennessWorker.Openness.Network;
using Xunit;
using SiemensProject = Siemens.Engineering.Project;

namespace TiaMcpServer.Tests.Network;

public class HardwareTagIndexResolverTests
{
    [Fact]
    public void OmittedPlcNameUsesTheOnlyPlcEvenInADeviceGroup()
    {
        var project = new SiemensProject();
        var group = new DeviceUserGroup { Name = "Line 1" };
        AddPlc(group.Devices, "GroupedDevice", "GroupedPlc");
        project.DeviceGroups.Items.Add(group);
        var messages = new List<string>();

        var index = HardwareTagIndexResolver.Resolve(project, null, messages);

        Assert.Equal("GroupedDevice", index?.PlcDeviceName);
        Assert.Equal("Input_1", Assert.Single(index!.Candidates).Name);
        Assert.Empty(messages);
    }

    [Fact]
    public void OmittedPlcNameWithRootAndGroupedPlcIsAmbiguous()
    {
        var project = new SiemensProject();
        AddPlc(project.Devices, "RootDevice", "RootPlc");
        var group = new DeviceUserGroup { Name = "Line 1" };
        AddPlc(group.Devices, "GroupedDevice", "GroupedPlc");
        project.DeviceGroups.Items.Add(group);
        var messages = new List<string>();

        var index = HardwareTagIndexResolver.Resolve(project, null, messages);

        Assert.Null(index);
        Assert.Contains("More than one PLC", Assert.Single(messages));
    }

    [Fact]
    public void UnreadableDeviceItemIsReported()
    {
        var project = new SiemensProject();
        AddPlc(project.Devices, "RootDevice", "RootPlc");
        var bad = new Device { Name = "BadDevice" };
        bad.DeviceItems.Items.Add(new DeviceItem { Name = "CPU", ServiceFailure = new EngineeringException("item unreadable") });
        project.Devices.Items.Add(bad);
        var messages = new List<string>();

        HardwareTagIndexResolver.Resolve(project, "RootPlc", messages);

        Assert.Contains(messages, m => m.Contains("item unreadable"));
    }

    private static void AddPlc(Composition<Device> devices, string deviceName, string softwareName)
    {
        var plc = new PlcSoftware { Name = softwareName };
        var table = new PlcTagTable { Name = "T" };
        table.Tags.Items.Add(new PlcTag { Name = "Input_1", DataTypeName = "Bool", LogicalAddress = "%I0.0" });
        plc.TagTableGroup.TagTables.Items.Add(table);
        var device = new Device { Name = deviceName };
        device.DeviceItems.Items.Add(new DeviceItem { Name = "CPU", Container = new SoftwareContainer { Software = plc } });
        devices.Items.Add(device);
    }
}
