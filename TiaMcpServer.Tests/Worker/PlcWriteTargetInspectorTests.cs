using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;
using SiemensProject = Siemens.Engineering.Project;

namespace TiaMcpServer.Tests.Worker;

public sealed class PlcWriteTargetInspectorTests
{
    public static IEnumerable<object?[]> InvalidRequests()
    {
        yield return new object?[] { null };
        yield return new object?[] { Request("unknown", "Device/Blocks/Probe") };
        yield return new object?[] { Request(PlcWriteGuardOperation.UpdateBlockLogic, null) };
        yield return new object?[] { Request(PlcWriteGuardOperation.DeleteBlock, " ") };
        yield return new object?[] { Request(PlcWriteGuardOperation.DeleteBlockGroup, null, "Device/Types/Probe") };
        yield return new object?[] { Request(PlcWriteGuardOperation.UpdateTypeContent, "Device/Blocks/Probe", "Device/Types/Probe") };
        yield return new object?[] { Request(PlcWriteGuardOperation.UpdateTypeContent, "Device/Blocks/Probe") };
        yield return new object?[] { Request(PlcWriteGuardOperation.UpdateBlockLogic, "Device/Blocks/Probe", "") };
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public void InvalidRequestIsRejectedBeforeResolvingNativeTargets(PlcWriteGuardRequestInfo? request)
    {
        var project = new SiemensProject();
        project.Devices.EnumerationFailure = new EngineeringException("must not inspect devices for an invalid request");

        var failure = Assert.Throws<WorkerOperationException>(() => PlcWriteTargetInspector.Inspect(project, request));

        Assert.Equal(WorkerFailureCategories.ValidationError, failure.FailureCategory);
        Assert.Equal(0, project.Devices.YieldedItemCount);
    }

    [Theory]
    [InlineData("FB")]
    [InlineData("FC")]
    [InlineData("UDT")]
    public void LinkedContentUsesTheResolvedBlockOrType(string kind)
    {
        var (project, plc) = ProjectWithPlc();
        var target = AddTarget(plc, kind);
        target.LibraryTypeInstanceInfo = Linked(LibraryTypeVersionState.Committed);
        var request = kind == "UDT"
            ? Request(PlcWriteGuardOperation.UpdateTypeContent, typePath: "Device/Types/Probe")
            : Request(PlcWriteGuardOperation.UpdateBlockLogic, "Device/Blocks/Probe");

        var result = PlcWriteTargetInspector.Inspect(project, request);

        Assert.True(result.Blocked);
        Assert.Equal("plc_library_instance_edit", result.GuardId);
        Assert.Equal(1, target.LibraryTypeInstanceInfoRequests);
    }

    [Theory]
    [InlineData(LibraryTypeVersionState.InWork, true)]
    [InlineData(LibraryTypeVersionState.Committed, false)]
    public void BlockDeleteUsesTheCurrentLinkedVersionState(LibraryTypeVersionState state, bool blocked)
    {
        var (project, plc) = ProjectWithPlc();
        var block = (PlcBlock)AddTarget(plc, "FB");
        block.LibraryTypeInstanceInfo = Linked(state);

        var result = PlcWriteTargetInspector.Inspect(project, Request(PlcWriteGuardOperation.DeleteBlock, "Device/Blocks/Probe"));

        Assert.Equal(blocked, result.Blocked);
        Assert.Equal(blocked ? "plc_library_test_instance_delete" : null, result.GuardId);
        Assert.False(block.Deleted);
        Assert.Equal(1, block.LibraryTypeInstanceInfoRequests);
    }

    [Fact]
    public void PlainContentAndDeletionAreAllowedWithoutMutation()
    {
        var (project, plc) = ProjectWithPlc();
        var block = (PlcBlock)AddTarget(plc, "FC");

        var update = PlcWriteTargetInspector.Inspect(project, Request(PlcWriteGuardOperation.UpdateBlockLogic, "Device/Blocks/Probe"));
        var delete = PlcWriteTargetInspector.Inspect(project, Request(PlcWriteGuardOperation.DeleteBlock, "Device/Blocks/Probe"));

        Assert.False(update.Blocked);
        Assert.Null(update.GuardId);
        Assert.Null(update.Message);
        Assert.False(delete.Blocked);
        Assert.Null(delete.GuardId);
        Assert.Null(delete.Message);
        Assert.Equal(2, block.LibraryTypeInstanceInfoRequests);
        Assert.False(block.Deleted);
        Assert.Empty(plc.BlockGroup.Blocks.ImportCalls);
    }

    [Fact]
    public void GroupInspectionUsesTheSameDeviceAliasAndSoftwareUnitPathAsDeletion()
    {
        var (project, plc) = ProjectWithPlc();
        plc.UnitProvider = new PlcUnitProvider();
        var unit = new PlcUnit { Name = "UnitA" };
        plc.UnitProvider.UnitGroup.Units.Items.Add(unit);
        var outer = unit.BlockGroup.Groups.Create("Outer");
        var nested = outer.Groups.Create("Nested");
        nested.Blocks.Items.Add(new FB { Name = "Probe", LibraryTypeInstanceInfo = Linked(LibraryTypeVersionState.InWork) });

        var result = PlcWriteTargetInspector.Inspect(project,
            Request(PlcWriteGuardOperation.DeleteBlockGroup, "Device/Units/UnitA/Blocks/outer"));

        Assert.True(result.Blocked);
        Assert.Equal("plc_library_test_instance_delete", result.GuardId);
        Assert.False(outer.Deleted);
        Assert.False(nested.Deleted);
    }

    [Theory]
    [InlineData(PlcWriteGuardOperation.UpdateBlockLogic)]
    [InlineData(PlcWriteGuardOperation.DeleteBlock)]
    [InlineData(PlcWriteGuardOperation.DeleteBlockGroup)]
    [InlineData(PlcWriteGuardOperation.UpdateTypeContent)]
    public void MissingTargetRemainsTargetNotFound(string operation)
    {
        var (project, _) = ProjectWithPlc();
        var request = operation == PlcWriteGuardOperation.UpdateTypeContent
            ? Request(operation, typePath: "Device/Types/Missing")
            : Request(operation, "Device/Blocks/Missing");

        var failure = Assert.Throws<WorkerOperationException>(() => PlcWriteTargetInspector.Inspect(project, request));

        Assert.Equal(WorkerFailureCategories.TargetNotFound, failure.FailureCategory);
    }

    [Theory]
    [InlineData(PlcWriteGuardOperation.UpdateBlockLogic)]
    [InlineData(PlcWriteGuardOperation.DeleteBlock)]
    [InlineData(PlcWriteGuardOperation.DeleteBlockGroup)]
    [InlineData(PlcWriteGuardOperation.UpdateTypeContent)]
    public void FatalLibraryInspectionEscapesAsTheOriginalException(string operation)
    {
        var (project, plc) = ProjectWithPlc();
        var kind = operation == PlcWriteGuardOperation.UpdateTypeContent ? "UDT" : "FB";
        var target = AddTarget(plc, kind);
        var fatal = new NonRecoverableException("portal lost while inspecting the selected target");
        target.LibraryTypeInstanceInfoFailure = fatal;
        var request = kind == "UDT"
            ? Request(operation, typePath: "Device/Types/Probe")
            : Request(operation, "Device/Blocks/Probe");
        if (operation == PlcWriteGuardOperation.DeleteBlockGroup)
        {
            var group = plc.BlockGroup.Groups.Create("Group");
            plc.BlockGroup.Blocks.Items.Remove((PlcBlock)target);
            group.Blocks.Items.Add((PlcBlock)target);
            request = Request(operation, "Device/Blocks/Group");
        }

        var failure = Record.Exception(() => PlcWriteTargetInspector.Inspect(project, request));

        Assert.Same(fatal, failure);
    }

    private static PlcWriteGuardRequestInfo Request(string operation, string? blockPath = null, string? typePath = null)
        => new() { Operation = operation, BlockPath = blockPath, TypePath = typePath };

    private static LibraryTypeInstanceInfo Linked(LibraryTypeVersionState state)
        => new() { LibraryTypeVersion = new LibraryTypeVersion { State = state } };

    private static NamedObject AddTarget(PlcSoftware plc, string kind)
    {
        if (kind == "UDT")
        {
            var type = new PlcType { Name = "Probe" };
            plc.TypeGroup.Types.Items.Add(type);
            return type;
        }
        PlcBlock block = kind == "FB" ? new FB() : new FC();
        block.Name = "Probe";
        plc.BlockGroup.Blocks.Items.Add(block);
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
