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

        Assert.Equal(WorkerFailureCategories.TargetNotFound, failure.FailureCategory);
    }

    [Fact]
    public void TableNameNarrowsToThatTable()
    {
        var project = new SiemensProject();
        var plc = AddPlc(project.Devices, "Device", "Plc");
        AddTable(plc, "Motors").Tags.Items.Add(new PlcTag { Name = "M1" });
        AddTable(plc, "Valves").Tags.Items.Add(new PlcTag { Name = "V1" });

        var inventory = TagTableReader.ReadInventory(project, null, tableName: "VALVES");

        var table = Assert.Single(Assert.Single(inventory.Plcs).Tables);
        Assert.Equal("Valves", table.Name);
        Assert.True(inventory.IsComplete);
    }

    [Fact]
    public void FolderPathNarrowsToThatFolder()
    {
        var project = new SiemensProject();
        var plc = AddPlc(project.Devices, "Device", "Plc");
        AddTable(plc, "RootTable");
        var line = new PlcTagTableGroup { Name = "Line" };
        line.TagTables.Items.Add(new PlcTagTable { Name = "LineTable" });
        var nested = new PlcTagTableGroup { Name = "Cell" };
        nested.TagTables.Items.Add(new PlcTagTable { Name = "CellTable" });
        line.Groups.Items.Add(nested);
        plc.TagTableGroup.Groups.Items.Add(line);

        var inventory = TagTableReader.ReadInventory(project, null, folderPath: "/line/cell");

        var table = Assert.Single(Assert.Single(inventory.Plcs).Tables);
        Assert.Equal("CellTable", table.Name);
        Assert.Equal("/Line/Cell", table.FolderPath);
    }

    [Fact]
    public void NoMatchingTableOrFolderFailsTargetNotFound()
    {
        var project = new SiemensProject();
        AddTable(AddPlc(project.Devices, "Device", "Plc"), "T");

        var byTable = Assert.Throws<WorkerOperationException>(
            () => TagTableReader.ReadInventory(project, null, tableName: "Missing"));
        var byFolder = Assert.Throws<WorkerOperationException>(
            () => TagTableReader.ReadInventory(project, null, folderPath: "/Missing"));

        Assert.Equal(WorkerFailureCategories.TargetNotFound, byTable.FailureCategory);
        Assert.Contains("Missing", byTable.Message);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, byFolder.FailureCategory);
    }

    [Fact]
    public void MessagesAreCappedAtFiftyPlusSummary()
    {
        var project = new SiemensProject();
        var table = AddTable(AddPlc(project.Devices, "Device", "Plc"), "T");
        for (var i = 0; i < 60; i++)
        {
            table.UserConstants.Items.Add(new PlcUserConstant { Name = $"C{i}", ValueFailure = new EngineeringException("boom") });
        }

        var inventory = TagTableReader.ReadInventory(project, null);

        Assert.False(inventory.IsComplete);
        Assert.Equal(51, inventory.Messages.Count);
        Assert.Contains("C49", inventory.Messages[49]);
        Assert.StartsWith("... and 10 more", inventory.Messages[50]);
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
        // Comments are never read, so the inventory does not claim "no comment" with a null.
        Assert.False(tag.TryGetProperty("comment", out _));
        Assert.Equal(JsonValueKind.True, document.RootElement.GetProperty("isComplete").ValueKind);
    }

    [Fact]
    public void EngineeringNotSupportedFlagIsNullWithoutMessage()
    {
        var project = new SiemensProject();
        AddTable(AddPlc(project.Devices, "Device", "Plc"), "T").Tags.Items.Add(new PlcTag
        {
            Name = "A", ExternalWritableFailure = new EngineeringNotSupportedException("not supported"),
        });

        var inventory = TagTableReader.ReadInventory(project, null);

        Assert.True(inventory.IsComplete);
        Assert.Empty(inventory.Messages);
        Assert.Null(Assert.Single(Assert.Single(Assert.Single(inventory.Plcs).Tables).Tags).ExternalWritable);
    }

    [Fact]
    public void UnreadableTagTableGroupOnOnePlcDoesNotAbortTheOthers()
    {
        var project = new SiemensProject();
        var broken = AddPlc(project.Devices, "BrokenDevice", "BrokenPlc");
        broken.TagTableGroupFailure = new EngineeringException("group boom");
        AddTable(AddPlc(project.Devices, "GoodDevice", "GoodPlc"), "GoodTable");

        var inventory = TagTableReader.ReadInventory(project, null);

        Assert.False(inventory.IsComplete);
        Assert.Contains(inventory.Messages, m => m.Contains("BrokenPlc") && m.Contains("group boom"));
        Assert.Equal(new[] { "BrokenPlc", "GoodPlc" }, inventory.Plcs.Select(p => p.PlcName));
        Assert.Empty(inventory.Plcs[0].Tables);
        Assert.Equal("GoodTable", Assert.Single(inventory.Plcs[1].Tables).Name);
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
