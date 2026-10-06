using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;
using SiemensProject = Siemens.Engineering.Project;

namespace TiaMcpServer.Tests.Worker;

public sealed class PlcWritePreconditionsTests
{
    private static PlcSoftware AddPlc(Composition<Device> devices, string deviceName, string plcName)
    {
        var plc = new PlcSoftware { Name = plcName };
        var device = new Device { Name = deviceName };
        device.DeviceItems.Items.Add(new DeviceItem { Container = new SoftwareContainer { Software = plc } });
        devices.Items.Add(device);
        return plc;
    }

    private static (SiemensProject Project, PlcSoftware Plc, PlcTagTable Table) Single()
    {
        var project = new SiemensProject { Path = new FileInfo(@"C:\p\p.ap21") };
        var plc = AddPlc(project.Devices, "Device", "PLC_1");
        var table = plc.TagTableGroup.TagTables.Create("Default");
        return (project, plc, table);
    }

    private static WorkerOperationException Fails(Action action, string category)
    {
        var ex = Assert.Throws<WorkerOperationException>(action);
        Assert.Equal(category, ex.FailureCategory);
        return ex;
    }

    // ---- FindUnique

    [Fact]
    public void FindUnique_TwoMatchesIsTargetAmbiguous()
    {
        var project = new SiemensProject();
        AddPlc(project.Devices, "A", "Same");
        AddPlc(project.Devices, "B", "same");
        Fails(() => PlcSoftwareLocator.FindUnique(project, null), WorkerFailureCategories.TargetAmbiguous);
        Fails(() => PlcSoftwareLocator.FindUnique(project, "SAME"), WorkerFailureCategories.TargetAmbiguous);
    }

    [Fact]
    public void FindUnique_GroupedDevicePlcIsFound()
    {
        var project = new SiemensProject();
        var group = new DeviceUserGroup { Name = "Line" };
        AddPlc(group.Devices, "GroupedDevice", "GroupedPlc");
        project.DeviceGroups.Items.Add(group);
        Assert.Equal("GroupedPlc", PlcSoftwareLocator.FindUnique(project, "groupeddevice").Software.Name);
    }

    [Fact]
    public void FindUnique_NoMatchIsTargetNotFound()
    {
        var project = new SiemensProject();
        AddPlc(project.Devices, "A", "One");
        Fails(() => PlcSoftwareLocator.FindUnique(project, "Missing"), WorkerFailureCategories.TargetNotFound);
    }

    [Fact]
    public void FindUnique_UnreadableDeviceItemPropagates()
    {
        var project = new SiemensProject();
        var device = new Device { Name = "A" };
        device.DeviceItems.Items.Add(new DeviceItem { ServiceFailure = new EngineeringException("hidden") });
        project.Devices.Items.Add(device);
        Assert.Throws<EngineeringException>(() => PlcSoftwareLocator.FindUnique(project, null));
    }

    // ---- Collisions

    [Fact]
    public void CreateTag_CollidesWithConstantBlockOrSystemBlockCaseInsensitive()
    {
        var (project, plc, table) = Single();
        table.UserConstants.Create("Const");
        plc.BlockGroup.Blocks.Items.Add(new PlcBlock { Name = "Main" });
        var system = new PlcSystemBlockGroup { Name = "Sys" };
        system.Blocks.Items.Add(new PlcBlock { Name = "SysBlock" });
        plc.BlockGroup.SystemBlockGroups.Items.Add(system);
        var nested = new PlcBlockGroup { Name = "Area" };
        nested.Blocks.Items.Add(new PlcBlock { Name = "Deep" });
        plc.BlockGroup.Groups.Items.Add(nested);

        foreach (var name in new[] { "CONST", "main", "sysblock", "DEEP" })
        {
            Fails(() => TagMutationService.CreateTag(project, "PLC_1", "Default", null, name, "Bool", null),
                WorkerFailureCategories.StateChanged);
        }

        Assert.Empty(table.Tags);
    }

    [Fact]
    public void RenameTag_CollidesAcrossTables()
    {
        var (project, plc, table) = Single();
        table.Tags.Create("Motor", "Bool", "%I0.0");
        plc.TagTableGroup.TagTables.Create("Other").Tags.Create("Valve", "Bool", "%I0.1");

        Fails(() => TagMutationService.UpdateTag(project, "PLC_1", "Default", null, "Motor", "VALVE",
            null, null, null, null, null, null), WorkerFailureCategories.StateChanged);

        // Case-only rename of itself is not a collision.
        TagMutationService.UpdateTag(project, "PLC_1", "Default", null, "Motor", "MOTOR",
            null, null, null, null, null, null);
        Assert.Equal("MOTOR", Assert.Single(table.Tags).Name);
    }

    [Fact]
    public void CreateUserConstant_CollidesWithTag()
    {
        var (project, plc, table) = Single();
        plc.TagTableGroup.TagTables.Create("Other").Tags.Create("Motor", "Bool", "%I0.0");
        Fails(() => TagMutationService.CreateUserConstant(project, "PLC_1", "Default", null, "motor", "Int", "1"),
            WorkerFailureCategories.StateChanged);
        Assert.Empty(table.UserConstants);
    }

    [Fact]
    public void CreateTagTable_NameTakenInOtherFolderIsStateChanged()
    {
        var (project, plc, _) = Single();
        var folder = new PlcTagTableGroup { Name = "Elsewhere" };
        plc.TagTableGroup.Groups.Items.Add(folder);
        folder.TagTables.Create("Shared");

        Fails(() => TagMutationService.CreateTagTable(project, "PLC_1", "SHARED", null),
            WorkerFailureCategories.StateChanged);
    }

    // ---- Readability / default table

    [Fact]
    public void UpdateConstant_NullValueRefused()
    {
        var (project, _, table) = Single();
        var constant = table.UserConstants.Create("K");
        constant.Value = null!;
        Fails(() => TagMutationService.UpdateUserConstant(project, "PLC_1", "Default", null, "K", null, "2"),
            WorkerFailureCategories.WorkerOperationFailed);
        Fails(() => TagMutationService.DeleteUserConstant(project, "PLC_1", "Default", null, "K"),
            WorkerFailureCategories.WorkerOperationFailed);
        Assert.Single(table.UserConstants);
    }

    [Fact]
    public void UpdateTag_UnreadableRequestedFlagRefused()
    {
        var (project, _, table) = Single();
        var tag = table.Tags.Create("Motor", "Bool", "%I0.0");
        tag.ExternalVisibleFailure = new EngineeringNotSupportedException("no such attribute");

        Fails(() => TagMutationService.UpdateTag(project, "PLC_1", "Default", null, "Motor", null,
            null, null, null, true, null, null), WorkerFailureCategories.WorkerOperationFailed);

        // A flag that was not requested may stay unreadable.
        TagMutationService.UpdateTag(project, "PLC_1", "Default", null, "Motor", null,
            null, null, true, null, null, null);
        Assert.True(tag.ExternalAccessible);
    }

    [Fact]
    public void DeleteDefaultTable_Refused()
    {
        var (project, plc, table) = Single();
        table.IsDefault = true;
        Fails(() => TagMutationService.DeleteTagTable(project, "PLC_1", "Default", null),
            WorkerFailureCategories.WorkerOperationFailed);
        Assert.Single(plc.TagTableGroup.TagTables);
    }

    [Fact]
    public void MissingTargetsAreTargetNotFound()
    {
        var (project, _, _) = Single();
        Fails(() => TagMutationService.DeleteTag(project, "PLC_1", "Default", null, "Nope"), WorkerFailureCategories.TargetNotFound);
        Fails(() => TagMutationService.DeleteTag(project, "PLC_1", "NoTable", null, "Nope"), WorkerFailureCategories.TargetNotFound);
        Fails(() => TagMutationService.DeleteTag(project, "PLC_1", "Default", "/NoFolder", "Nope"), WorkerFailureCategories.TargetNotFound);
        Fails(() => TagMutationService.DeleteUserConstant(project, "PLC_1", "Default", null, "Nope"), WorkerFailureCategories.TargetNotFound);
        Fails(() => TagMutationService.UpdateTag(project, "PLC_1", "Default", null, "Nope", null, null, null, null, null, null, null),
            WorkerFailureCategories.TargetNotFound);
        Fails(() => TagMutationService.CreateTag(project, "Ghost", "Default", null, "X", "Bool", null), WorkerFailureCategories.TargetNotFound);
    }

    // ---- Blocks and groups

    [Fact]
    public void CreateBlock_ExistingTargetRefused()
    {
        var (project, plc, _) = Single();
        plc.BlockGroup.Blocks.Items.Add(new FB { Name = "Main" });

        Fails(() => BlockMutationService.CreateBlock(project, "PLC_1/main", "FB", "SCL", null),
            WorkerFailureCategories.StateChanged);

        Assert.Empty(plc.BlockGroup.Blocks.ImportCalls);
    }

    [Fact]
    public void CreateBlock_NameTakenByGroupInSameParentRefused()
    {
        var (project, plc, _) = Single();
        plc.BlockGroup.Groups.Items.Add(new PlcBlockUserGroup { Name = "Main" });

        Fails(() => BlockMutationService.CreateBlock(project, "PLC_1/MAIN", "FB", "SCL", null),
            WorkerFailureCategories.StateChanged);

        Assert.Empty(plc.BlockGroup.Blocks.ImportCalls);
    }

    [Theory]
    [InlineData("MOTOR")]
    [InlineData("const")]
    public void CreateBlock_CollidesWithTagOrConstantCaseInsensitive(string name)
    {
        var (project, plc, table) = Single();
        table.Tags.Create("Motor", "Bool", "%I0.0");
        table.UserConstants.Create("Const");

        Fails(() => BlockMutationService.CreateBlock(project, "PLC_1/" + name, "FB", "SCL", null),
            WorkerFailureCategories.StateChanged);

        Assert.Empty(plc.BlockGroup.Blocks.ImportCalls);
    }

    [Fact]
    public void CreateBlock_TwoSegmentPathTargetsRootGroup()
    {
        var (_, plc, _) = Single();
        var area = new PlcBlockUserGroup { Name = "Area" };
        plc.BlockGroup.Groups.Items.Add(area);

        Assert.Same(plc.BlockGroup,
            BlockMutationService.ResolveGroupFromAddress(plc, BlockAddress.Parse("PLC_1/Main")));
        Assert.Same(area,
            BlockMutationService.ResolveGroupFromAddress(plc, BlockAddress.Parse("PLC_1/Blocks/Area/Main")));
        Fails(() => BlockMutationService.ResolveGroupFromAddress(plc, BlockAddress.Parse("Main")),
            WorkerFailureCategories.ValidationError);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CreateBlockGroup_NameTakenInParentRefused(bool takenByBlock)
    {
        var (project, plc, _) = Single();
        if (takenByBlock)
            plc.BlockGroup.Blocks.Items.Add(new FB { Name = "Area" });
        else
            plc.BlockGroup.Groups.Items.Add(new PlcBlockUserGroup { Name = "Area" });
        var before = plc.BlockGroup.Groups.Items.Count;

        Fails(() => BlockMutationService.CreateBlockGroup(project, "PLC_1/AREA"),
            WorkerFailureCategories.StateChanged);

        Assert.Equal(before, plc.BlockGroup.Groups.Items.Count);
    }

    [Fact]
    public void CreateBlockGroup_FreeNameIsCreated()
    {
        var (project, plc, _) = Single();
        BlockMutationService.CreateBlockGroup(project, "PLC_1/Fresh");
        Assert.Equal("Fresh", Assert.Single(plc.BlockGroup.Groups.Items).Name);
    }

    [Fact]
    public void DeleteBlockGroup_UnreadableDescendantRefused()
    {
        var (project, plc, _) = Single();
        var area = new PlcBlockUserGroup { Name = "Area" };
        var nested = new PlcBlockUserGroup { Name = "Nested" };
        nested.Blocks.Items.Add(new FB { Name = "Hidden", NameFailure = new EngineeringException("unreadable") });
        area.Groups.Items.Add(nested);
        plc.BlockGroup.Groups.Items.Add(area);

        var ex = Fails(() => BlockMutationService.DeleteBlockGroup(project, "PLC_1/Area"),
            WorkerFailureCategories.WorkerOperationFailed);

        Assert.False(area.Deleted);
        Assert.Contains("unreadable", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeleteBlockGroup_ReadableGroupIsDeleted()
    {
        var (project, plc, _) = Single();
        var area = new PlcBlockUserGroup { Name = "Area" };
        area.Blocks.Items.Add(new FB { Name = "Fine" });
        plc.BlockGroup.Groups.Items.Add(area);

        BlockMutationService.DeleteBlockGroup(project, "PLC_1/Area");

        Assert.True(area.Deleted);
    }

    [Theory]
    [InlineData("PLC_1/Blocks/Ghost")]
    [InlineData("PLC_1/Ghost")]
    public void DeleteBlock_MissingTargetIsTargetNotFound(string path)
    {
        var (project, _, _) = Single();
        Fails(() => BlockMutationService.DeleteBlock(project, path), WorkerFailureCategories.TargetNotFound);
    }

    [Fact]
    public void DeleteBlock_AmbiguousLegacyNameIsTargetAmbiguous()
    {
        var (project, plc, _) = Single();
        plc.BlockGroup.Blocks.Items.Add(new FB { Name = "Dup" });
        var area = new PlcBlockUserGroup { Name = "Area" };
        area.Blocks.Items.Add(new FB { Name = "Dup" });
        plc.BlockGroup.Groups.Items.Add(area);
        Fails(() => BlockMutationService.DeleteBlock(project, "PLC_1/Dup"), WorkerFailureCategories.TargetAmbiguous);
    }

    [Fact]
    public void DeleteBlockGroup_MissingGroupIsTargetNotFound()
    {
        var (project, _, _) = Single();
        Fails(() => BlockMutationService.DeleteBlockGroup(project, "PLC_1/Ghost"), WorkerFailureCategories.TargetNotFound);
        Fails(() => BlockMutationService.DeleteBlockGroup(project, "PLC_1/Blocks/NoFolder/Ghost"), WorkerFailureCategories.TargetNotFound);
    }

    // ---- Content hash

    [Fact]
    public void RequireContentHash_MatchPasses()
    {
        var hash = ContentHashRules.Compute(SourceFormatNames.Xml, "<doc/>");
        PlcWritePreconditions.RequireContentHash(hash, SourceFormatNames.Xml, "<doc/>");
    }

    [Fact]
    public void RequireContentHash_DifferentContentIsStateChanged()
    {
        var hash = ContentHashRules.Compute(SourceFormatNames.Xml, "<doc/>");
        Fails(() => PlcWritePreconditions.RequireContentHash(hash, SourceFormatNames.Xml, "<doc changed/>"),
            WorkerFailureCategories.StateChanged);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-hash")]
    public void RequireContentHash_MissingOrMalformedIsValidationError(string? hash)
    {
        Fails(() => PlcWritePreconditions.RequireContentHash(hash, SourceFormatNames.Xml, "<doc/>"),
            WorkerFailureCategories.ValidationError);
    }

    [Fact]
    public void RequireContentHash_FormatTagMismatchIsValidationError()
    {
        var hash = ContentHashRules.Compute(SourceFormatNames.Source, "text");
        Fails(() => PlcWritePreconditions.RequireContentHash(hash, SourceFormatNames.Xml, "text"),
            WorkerFailureCategories.ValidationError);
    }

    // ---- Result

    [Fact]
    public void TagMutationResult_CarriesResolvedPlcName()
    {
        var (project, _, _) = Single();
        var result = TagMutationService.CreateTag(project, "device", "Default", null, "Motor", "Bool", null);
        Assert.Equal("PLC_1", result.PlcName);
        result = TagMutationService.CreateTag(project, null, "Default", null, "Valve", "Bool", null);
        Assert.Equal("PLC_1", result.PlcName);
    }

    // ---- Inventory

    [Fact]
    public void Inventory_UnreadableDeviceItemIsIncomplete()
    {
        var project = new SiemensProject();
        AddPlc(project.Devices, "A", "One");
        var hidden = new Device { Name = "B" };
        hidden.DeviceItems.Items.Add(new DeviceItem { ServiceFailure = new EngineeringException("hidden") });
        project.Devices.Items.Add(hidden);

        var inventory = TagTableReader.ReadInventory(project, null);

        Assert.False(inventory.IsComplete);
        Assert.Contains(inventory.Messages, m => m.Contains("hidden", StringComparison.Ordinal));
        Assert.Equal("One", Assert.Single(inventory.Plcs).PlcName);
    }

    [Fact]
    public void Inventory_NoPlcIsTargetNotFound()
    {
        var project = new SiemensProject();
        AddPlc(project.Devices, "A", "One");
        Fails(() => TagTableReader.ReadInventory(project, "Missing"), WorkerFailureCategories.TargetNotFound);
    }
}
