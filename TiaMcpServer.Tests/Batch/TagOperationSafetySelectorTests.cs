using TiaMcpServer.Batch;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class TagOperationSafetySelectorTests
{
    [Fact]
    public void Build_UpdateTag_UsesRequestedRenameAndLogicalAddress()
    {
        var key = TagOperationSafetySelector.Build(new BatchOperationRequest
        {
            OperationId = "u1",
            Operation = "update_tag",
            ProjectPath = @"C:\Plant\Demo.ap21",
            PlcName = "PLC_1",
            TableName = "Inputs",
            Name = "Start",
            NewName = "Start_1",
            LogicalAddress = "%I0.1"
        });

        Assert.Equal("update_tag", key.SelectorKind);
        Assert.Equal(@"C:\Plant\Demo.ap21", key.NormalizedProjectPath);
        Assert.Equal("Start_1", key.EffectiveName);
        Assert.Equal("%I0.1", key.EffectiveLogicalAddress);
    }

    [Fact]
    public void Build_DeleteTag_DoesNotCollapseIntoUpdateTagKey()
    {
        var update = TagOperationSafetySelector.Build(new BatchOperationRequest
        {
            OperationId = "u1",
            Operation = "update_tag",
            ProjectPath = @"C:\Plant\Demo.ap21",
            PlcName = "PLC_1",
            TableName = "Inputs",
            Name = "Start"
        });
        var delete = TagOperationSafetySelector.Build(new BatchOperationRequest
        {
            OperationId = "d1",
            Operation = "delete_tag",
            ProjectPath = @"C:\Plant\Demo.ap21",
            PlcName = "PLC_1",
            TableName = "Inputs",
            Name = "Start"
        });

        Assert.NotEqual(update.SelectorKind, delete.SelectorKind);
    }

    [Fact]
    public void Build_EquivalentProjectPathSpellings_UsesOneNormalizedKey()
    {
        var canonical = TagOperationSafetySelector.Build(new BatchOperationRequest
        {
            OperationId = "u1",
            Operation = "update_tag",
            ProjectPath = @"C:\Plant\Demo.ap21",
            PlcName = "PLC_1",
            TableName = "Inputs",
            Name = "Start"
        });
        var equivalent = TagOperationSafetySelector.Build(new BatchOperationRequest
        {
            OperationId = "u2",
            Operation = "update_tag",
            ProjectPath = @" C:\Plant\.\Demo.ap21 ",
            PlcName = "PLC_1",
            TableName = "Inputs",
            Name = "Start"
        });

        Assert.Equal(canonical.NormalizedProjectPath, equivalent.NormalizedProjectPath);
        Assert.Equal(canonical, equivalent);
    }
}
