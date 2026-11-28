using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;
using SiemensProject = Siemens.Engineering.Project;

namespace TiaMcpServer.Tests.Worker;

public sealed class PlcLibraryGuardIntegrationTests
{
    [Theory]
    [InlineData("FB")]
    [InlineData("FC")]
    public void DeleteBlock_InWorkLibraryInstanceNeverReachesNativeDelete(string kind)
    {
        var (project, plc) = ProjectWithPlc();
        var block = LinkedBlock(kind, LibraryTypeVersionState.InWork);
        plc.BlockGroup.Blocks.Items.Add(block);
        var deletes = 0;
        block.DeleteAction = () => deletes++;

        var failure = Record.Exception(() => BlockMutationService.DeleteBlock(project, "PLC_1/Blocks/Probe"));

        Assert.Equal(0, deletes);
        Assert.Equal(WorkerFailureCategories.GuardBlocked, Assert.IsType<WorkerOperationException>(failure).FailureCategory);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeleteBlock_PlainOrCommittedInstanceStillReachesNativeDelete(bool linked)
    {
        var (project, plc) = ProjectWithPlc();
        var block = linked ? LinkedBlock("FB", LibraryTypeVersionState.Committed) : new FB { Name = "Probe" };
        plc.BlockGroup.Blocks.Items.Add(block);
        var deletes = 0;
        block.DeleteAction = () => deletes++;

        BlockMutationService.DeleteBlock(project, "PLC_1/Blocks/Probe");

        Assert.Equal(1, deletes);
    }

    [Fact]
    public void DeleteBlock_UnreadableLibraryLinkNeverReachesNativeDelete()
    {
        var (project, plc) = ProjectWithPlc();
        var block = new FB { Name = "Probe", LibraryTypeInstanceInfoFailure = new EngineeringException("link unreadable") };
        plc.BlockGroup.Blocks.Items.Add(block);
        var deletes = 0;
        block.DeleteAction = () => deletes++;

        var failure = Record.Exception(() => BlockMutationService.DeleteBlock(project, "PLC_1/Blocks/Probe"));

        Assert.Equal(0, deletes);
        Assert.Equal(WorkerFailureCategories.GuardBlocked, Assert.IsType<WorkerOperationException>(failure).FailureCategory);
    }

    [Fact]
    public void DeleteBlock_FatalLibraryInspectionPreservesOriginalFailure()
    {
        var (project, plc) = ProjectWithPlc();
        var fatal = new NonRecoverableException("portal lost during link inspection");
        var block = new FB { Name = "Probe", LibraryTypeInstanceInfoFailure = fatal };
        plc.BlockGroup.Blocks.Items.Add(block);
        var deletes = 0;
        block.DeleteAction = () => deletes++;

        var failure = Record.Exception(() => BlockMutationService.DeleteBlock(project, "PLC_1/Blocks/Probe"));

        Assert.Equal(0, deletes);
        Assert.Same(fatal, failure);
    }

    [Theory]
    [InlineData("FB")]
    [InlineData("FC")]
    public void DeleteGroup_NestedInWorkInstancePreventsWholeGroupDeletion(string kind)
    {
        var (project, plc) = ProjectWithPlc();
        var outer = plc.BlockGroup.Groups.Create("Outer");
        var inner = outer.Groups.Create("Inner");
        inner.Blocks.Items.Add(LinkedBlock(kind, LibraryTypeVersionState.InWork));

        var failure = Record.Exception(() => BlockMutationService.DeleteBlockGroup(project, "PLC_1/Blocks/Outer"));

        Assert.False(outer.Deleted);
        Assert.False(inner.Deleted);
        Assert.Equal(WorkerFailureCategories.GuardBlocked, Assert.IsType<WorkerOperationException>(failure).FailureCategory);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeleteGroup_PlainOrCommittedDescendantsStillPermitDelete(bool linked)
    {
        var (project, plc) = ProjectWithPlc();
        var outer = plc.BlockGroup.Groups.Create("Outer");
        var inner = outer.Groups.Create("Inner");
        inner.Blocks.Items.Add(linked ? LinkedBlock("FB", LibraryTypeVersionState.Committed) : new FB { Name = "Probe" });

        BlockMutationService.DeleteBlockGroup(project, "PLC_1/Blocks/Outer");

        Assert.True(outer.Deleted);
    }

    [Fact]
    public void DeleteGroup_UnreadableDescendantLibraryLinkPreventsDelete()
    {
        var (project, plc) = ProjectWithPlc();
        var group = plc.BlockGroup.Groups.Create("Outer");
        group.Blocks.Items.Add(new FB { Name = "Probe", LibraryTypeInstanceInfoFailure = new EngineeringException("link unreadable") });

        var failure = Record.Exception(() => BlockMutationService.DeleteBlockGroup(project, "PLC_1/Blocks/Outer"));

        Assert.False(group.Deleted);
        Assert.Equal(WorkerFailureCategories.GuardBlocked, Assert.IsType<WorkerOperationException>(failure).FailureCategory);
    }

    private static PlcBlock LinkedBlock(string kind, LibraryTypeVersionState state)
    {
        PlcBlock block = kind == "FB" ? new FB() : new FC();
        block.Name = "Probe";
        block.LibraryTypeInstanceInfo = new LibraryTypeInstanceInfo
        {
            LibraryTypeVersion = new LibraryTypeVersion { State = state }
        };
        return block;
    }

    private static (SiemensProject Project, PlcSoftware Plc) ProjectWithPlc()
    {
        var project = new SiemensProject { Path = new FileInfo(@"C:\fixture\p.ap21") };
        var plc = new PlcSoftware { Name = "PLC_1" };
        var device = new Device { Name = "Device" };
        device.DeviceItems.Items.Add(new DeviceItem { Container = new SoftwareContainer { Software = plc } });
        project.Devices.Items.Add(device);
        return (project, plc);
    }
}
