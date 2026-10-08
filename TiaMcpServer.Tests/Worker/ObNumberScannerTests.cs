using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Units;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

public sealed class ObNumberScannerTests
{
    [Fact]
    public void CollectsUnitAndSystemGroupObs()
    {
        var plc = new PlcSoftware { Name = "PLC_1" };
        plc.BlockGroup.Blocks.Items.Add(new OB { Name = "Main", Number = 1 });
        var area = new PlcBlockUserGroup { Name = "Area" };
        area.Blocks.Items.Add(new OB { Name = "Cyclic", Number = 30 });
        plc.BlockGroup.Groups.Items.Add(area);
        var system = new PlcSystemBlockGroup { Name = "Program resources" };
        var nested = new PlcSystemBlockGroup { Name = "Nested" };
        nested.Blocks.Items.Add(new OB { Name = "Diag", Number = 82 });
        system.Groups.Items.Add(nested);
        plc.BlockGroup.SystemBlockGroups.Items.Add(system);
        var unit = new PlcUnit { Name = "U1" };
        var unitGroup = new PlcBlockUserGroup { Name = "F" };
        unitGroup.Blocks.Items.Add(new OB { Name = "UnitOb", Number = 123 });
        unit.BlockGroup.Groups.Items.Add(unitGroup);
        plc.UnitProvider = new PlcUnitProvider();
        plc.UnitProvider.UnitGroup.Units.Items.Add(unit);

        var numbers = ObNumberScanner.Collect(plc);

        Assert.Equal(new[] { 1, 30, 82, 123 }, numbers.OrderBy(n => n));
    }

    [Fact]
    public void IgnoresNonObBlocks()
    {
        var plc = new PlcSoftware { Name = "PLC_1" };
        plc.BlockGroup.Blocks.Items.Add(new FB { Name = "Fb", Number = 1 });
        plc.BlockGroup.Blocks.Items.Add(new FC { Name = "Fc", Number = 30 });
        plc.BlockGroup.Blocks.Items.Add(new GlobalDB { Name = "Db", Number = 82 });

        Assert.Empty(ObNumberScanner.Collect(plc));
    }

    [Fact]
    public void UnreadableGroupIsWorkerOperationFailed()
    {
        var plc = new PlcSoftware { Name = "PLC_1" };
        plc.BlockGroup.Groups.EnumerationFailure = new EngineeringException("hidden");

        var ex = Assert.Throws<WorkerOperationException>(() => ObNumberScanner.Collect(plc));
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, ex.FailureCategory);
    }
}
