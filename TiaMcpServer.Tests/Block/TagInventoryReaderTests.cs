using System.Text.Json;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Tags;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;
using SiemensProject = Siemens.Engineering.Project;

namespace TiaMcpServer.Tests.Block;

public class TagInventoryReaderTests
{
    [Fact]
    public void OmittedPlcNameReadsEveryPlc()
    {
        var project = new SiemensProject();
        var rootPlc = AddPlc(project.Devices, "RootDevice", "RootPlc");
        var group = new DeviceUserGroup { Name = "Line 1" };
        var groupedPlc = AddPlc(group.Devices, "GroupedDevice", "GroupedPlc");
        project.DeviceGroups.Items.Add(group);
        AddTable(rootPlc, "RootTable");
        AddTable(groupedPlc, "GroupedTable");

        var inventory = TagTableReader.ReadInventory(project, null);

        Assert.True(inventory.IsComplete);
        Assert.Empty(inventory.Messages);
        Assert.Equal(new[] { "RootPlc", "GroupedPlc" }, inventory.Plcs.Select(p => p.PlcName));
        Assert.Equal(new[] { "RootDevice", "GroupedDevice" }, inventory.Plcs.Select(p => p.DeviceName));
        Assert.Equal("GroupedTable", Assert.Single(inventory.Plcs[1].Tables).Name);
    }

    [Theory]
    [InlineData("rootplc")]
    [InlineData("ROOTDEVICE")]
    public void PlcNameFiltersToMatchingPlcs(string plcName)
    {
        var project = new SiemensProject();
        AddPlc(project.Devices, "RootDevice", "RootPlc");
        AddPlc(project.Devices, "OtherDevice", "OtherPlc");

        var inventory = TagTableReader.ReadInventory(project, plcName);

        Assert.Equal("RootPlc", Assert.Single(inventory.Plcs).PlcName);
    }

    [Fact]
    public void TagsCarryExternalAccessFlags()
    {
        var project = new SiemensProject();
        var plc = AddPlc(project.Devices, "Device", "Plc");
        var table = AddTable(plc, "T");
        table.IsDefault = true;
        table.Tags.Items.Add(new PlcTag
        {
            Name = "Motor", DataTypeName = "Bool", LogicalAddress = "%Q0.1",
            ExternalAccessible = true, ExternalVisible = false, ExternalWritable = true
        });

        var info = Assert.Single(Assert.Single(TagTableReader.ReadInventory(project, null).Plcs).Tables);
        var tag = Assert.Single(info.Tags);

        Assert.True(info.IsDefault);
        Assert.Equal("%Q0.1", tag.LogicalAddress);
        Assert.True(tag.ExternalAccessible);
        Assert.False(tag.ExternalVisible);
        Assert.True(tag.ExternalWritable);
    }

    [Fact]
    public void UnreadableMembersAreReportedNotDropped()
    {
        var project = new SiemensProject();
        var plc = AddPlc(project.Devices, "Device", "Plc");
        plc.TagTableGroup.TagTables.Items.Add(new PlcTagTable { NameFailure = new EngineeringException("table boom") });
        var table = AddTable(plc, "T");
        table.Tags.Items.Add(new PlcTag
        {
            Name = "Flaky", ExternalVisibleFailure = new EngineeringException("flag boom"),
            ExternalAccessible = true, ExternalWritable = true
        });
        table.UserConstants.Items.Add(new PlcUserConstant
        {
            Name = "Limit", ValueFailure = new EngineeringException("value boom")
        });

        var inventory = TagTableReader.ReadInventory(project, null);

        Assert.False(inventory.IsComplete);
        Assert.Equal(3, inventory.Messages.Count);
        Assert.Contains(inventory.Messages, m => m.Contains("table boom"));
        Assert.Contains(inventory.Messages, m => m.Contains("flag boom") && m.Contains("Flaky"));
        Assert.Contains(inventory.Messages, m => m.Contains("value boom") && m.Contains("Limit"));
        var read = Assert.Single(Assert.Single(inventory.Plcs).Tables);
        var tag = Assert.Single(read.Tags);
        Assert.Null(tag.ExternalVisible);
        Assert.True(tag.ExternalAccessible);
        Assert.Null(Assert.Single(read.UserConstants).Value);
    }

    [Fact]
    public void NoMatchingPlcFails()
    {
        var project = new SiemensProject();
        AddPlc(project.Devices, "Device", "Plc");

        var failure = Assert.Throws<WorkerOperationException>(() => TagTableReader.ReadInventory(project, "Missing"));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, failure.FailureCategory);
    }

    [Fact]
    public void InventoryRootWritesExplicitNulls()
    {
        var project = new SiemensProject();
        var plc = AddPlc(project.Devices, "Device", "Plc");
        AddTable(plc, "T").Tags.Items.Add(new PlcTag { Name = "A", ExternalWritableFailure = new NotSupportedException() });

        var json = WorkerJson.SerializePayload(TagTableReader.ReadInventory(project, null));

        using var document = JsonDocument.Parse(json);
        var tag = document.RootElement.GetProperty("plcs")[0].GetProperty("tables")[0].GetProperty("tags")[0];
        Assert.Equal(JsonValueKind.Null, tag.GetProperty("externalWritable").ValueKind);
        Assert.Equal(JsonValueKind.Null, tag.GetProperty("comment").ValueKind);
        Assert.Equal(JsonValueKind.True, document.RootElement.GetProperty("isComplete").ValueKind);
    }

    private static PlcSoftware AddPlc(Composition<Device> devices, string deviceName, string softwareName)
    {
        var plc = new PlcSoftware { Name = softwareName };
        var device = new Device { Name = deviceName };
        device.DeviceItems.Items.Add(new DeviceItem { Name = "CPU", Container = new SoftwareContainer { Software = plc } });
        devices.Items.Add(device);
        return plc;
    }

    private static PlcTagTable AddTable(PlcSoftware plc, string name)
    {
        var table = new PlcTagTable { Name = name };
        plc.TagTableGroup.TagTables.Items.Add(table);
        return table;
    }
}
