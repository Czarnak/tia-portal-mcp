using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;
using SiemensProject = Siemens.Engineering.Project;

namespace TiaMcpServer.Tests.Block;

public sealed class BlockTargetResolverTests
{
    private static PlcSoftware AddPlc(SiemensProject project, string deviceName, string plcName)
    {
        var plc = new PlcSoftware { Name = plcName };
        var device = new Device { Name = deviceName };
        device.DeviceItems.Items.Add(new DeviceItem { Container = new SoftwareContainer { Software = plc } });
        project.Devices.Items.Add(device);
        return plc;
    }

    [Fact]
    public void ImportResolution_MissingBlockIsTargetNotFoundWithoutRootFallback()
    {
        var project = new SiemensProject();
        var plc = AddPlc(project, "Device", "PLC_1");
        var area = new PlcBlockUserGroup { Name = "Area" };
        area.Blocks.Items.Add(new FB { Name = "Elsewhere" });
        plc.BlockGroup.Groups.Items.Add(area);

        var ex = Assert.Throws<WorkerOperationException>(
            () => BlockTargetResolver.ResolveForImport(project, BlockAddress.Parse("PLC_1/Ghost")));

        Assert.Equal(WorkerFailureCategories.TargetNotFound, ex.FailureCategory);
    }

    [Fact]
    public void ImportResolution_NameMatchingTwoBlocksIsTargetAmbiguous()
    {
        var project = new SiemensProject();
        var plc = AddPlc(project, "Device", "PLC_1");
        plc.BlockGroup.Blocks.Items.Add(new FB { Name = "Dup" });
        var area = new PlcBlockUserGroup { Name = "Area" };
        area.Blocks.Items.Add(new FB { Name = "Dup" });
        plc.BlockGroup.Groups.Items.Add(area);

        var ex = Assert.Throws<WorkerOperationException>(
            () => BlockTargetResolver.ResolveForImport(project, BlockAddress.Parse("PLC_1/Dup")));

        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, ex.FailureCategory);
    }

    [Fact]
    public void ImportResolution_DeterministicMissingBlockResolvesToItsGroupWithoutBlock()
    {
        var project = new SiemensProject();
        var plc = AddPlc(project, "Device", "PLC_1");

        var target = BlockTargetResolver.ResolveForImport(project, BlockAddress.Parse("PLC_1/Blocks/Ghost"));

        Assert.Same(plc.BlockGroup, target.Group);
        Assert.Null(target.Block);
    }

    [Theory]
    [InlineData("PLC_1/Blocks/Ghost")]
    [InlineData("PLC_1/Ghost")]
    [InlineData("PLC_1/Blocks/NoFolder/Main")]
    [InlineData("PLC_1/Units/NoUnit/Blocks/Main")]
    public void ResolveForExport_MissingTargetIsTargetNotFound(string path)
    {
        var project = new SiemensProject();
        AddPlc(project, "Device", "PLC_1");

        var ex = Assert.Throws<WorkerOperationException>(
            () => BlockTargetResolver.ResolveForExport(project, BlockAddress.Parse(path)));

        Assert.Equal(WorkerFailureCategories.TargetNotFound, ex.FailureCategory);
    }

    [Fact]
    public void ResolveForExport_AmbiguousLegacyNameIsTargetAmbiguous()
    {
        var project = new SiemensProject();
        var plc = AddPlc(project, "Device", "PLC_1");
        plc.BlockGroup.Blocks.Items.Add(new FB { Name = "Dup" });
        var area = new PlcBlockUserGroup { Name = "Area" };
        area.Blocks.Items.Add(new FB { Name = "Dup" });
        plc.BlockGroup.Groups.Items.Add(area);

        var ex = Assert.Throws<WorkerOperationException>(
            () => BlockTargetResolver.ResolveForExport(project, BlockAddress.Parse("PLC_1/Dup")));

        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, ex.FailureCategory);
    }

    [Fact]
    public void ResolveForExport_AmbiguousPlcIsTargetAmbiguous()
    {
        var project = new SiemensProject();
        AddPlc(project, "A", "Same");
        AddPlc(project, "B", "same");

        var ex = Assert.Throws<WorkerOperationException>(
            () => BlockTargetResolver.ResolveForExport(project, BlockAddress.Parse("Same/Blocks/Main")));

        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, ex.FailureCategory);
    }
}
