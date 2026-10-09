using TiaMcpServer.Contracts.Plc;
using Xunit;

namespace TiaMcpServer.Tests.Contracts;

public sealed class PlcNameRulesTests
{
    [Fact]
    public void CpuNamesCollideCaseInsensitively()
    {
        var occupant = new PlcNamedObject("Tag", "Motor_Run", "Table_1");

        var collision = PlcNameRules.FindCollision("motor_run", new[] { occupant });

        Assert.Equal(occupant, collision);
    }

    [Fact]
    public void SelfIsNotACollision()
    {
        var self = new PlcNamedObject("Block", "Main", "Blocks");
        var other = new PlcNamedObject("Block", "Other", "Blocks");

        Assert.Null(PlcNameRules.FindCollision("MAIN", new[] { self, other }, self));
    }

    [Fact]
    public void DifferentNamesDoNotCollide()
        => Assert.Null(PlcNameRules.FindCollision("A", new[] { new PlcNamedObject("Tag", "B", null) }));

    [Fact]
    public void Comparer_IsOrdinalIgnoreCase()
        => Assert.Same(StringComparer.OrdinalIgnoreCase, PlcNameRules.Comparer);
}
