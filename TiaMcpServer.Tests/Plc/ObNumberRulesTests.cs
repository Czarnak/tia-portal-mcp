using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

public sealed class ObNumberRulesTests
{
    private static ObEventClass Get(string name)
    {
        Assert.True(ObEventClasses.TryGet(name, out var cls));
        return cls;
    }

    [Fact]
    public void AllMatchesSpecTable()
    {
        var actual = ObEventClasses.All.Select(c => (c.Name, c.BaseNumber, c.IsSingleton)).ToArray();

        Assert.Equal(
            new (string, int, bool)[]
            {
                ("ProgramCycle", 1, false),
                ("Startup", 100, false),
                ("TimeOfDay", 10, false),
                ("TimeDelayInterrupt", 20, false),
                ("CyclicInterrupt", 30, false),
                ("HardwareInterrupt", 40, false),
                ("Status", 55, true),
                ("Update", 56, true),
                ("Profile", 57, true),
                ("TimeErrorInterrupt", 80, true),
                ("DiagnosticErrorInterrupt", 82, true),
                ("PullOrPlugOfModules", 83, true),
                ("RackOrStationFailure", 86, true),
                ("ProgrammingError", 121, true),
                ("IOAccessError", 122, true),
            },
            actual);
        Assert.DoesNotContain(ObEventClasses.All, c => c.Name == "SynchronousCycle");
        Assert.Equal("ProgramCycle", ObEventClasses.Default);
    }

    [Fact]
    public void TryGetIsOrdinal()
    {
        Assert.True(ObEventClasses.TryGet("ProgramCycle", out _));
        Assert.False(ObEventClasses.TryGet("programcycle", out _));
        Assert.False(ObEventClasses.TryGet("SynchronousCycle", out _));
    }

    [Fact]
    public void NamesForMessageListsEveryClassInOrder()
        => Assert.Equal(
            string.Join(", ", ObEventClasses.All.Select(c => c.Name)),
            ObEventClasses.NamesForMessage());

    [Fact]
    public void PickReturnsBaseWhenFree()
        => Assert.Equal(30, ObNumberRules.Pick(Get("CyclicInterrupt"), new[] { 1 }));

    [Fact]
    public void PickSkipsToFirstFreeFrom123()
        => Assert.Equal(125, ObNumberRules.Pick(Get("ProgramCycle"), new[] { 1, 123, 124 }));

    [Fact]
    public void PickSingletonHeldReturnsNull()
        => Assert.Null(ObNumberRules.Pick(Get("DiagnosticErrorInterrupt"), new[] { 82 }));

    [Fact]
    public void PickSingletonFreeReturnsBase()
        => Assert.Equal(82, ObNumberRules.Pick(Get("DiagnosticErrorInterrupt"), new[] { 1, 30 }));

    [Fact]
    public void PickReturnsNullWhenRangeExhausted()
    {
        var used = Enumerable.Range(ObNumberRules.FirstFree, ObNumberRules.Max - ObNumberRules.FirstFree + 1)
            .Append(1)
            .ToHashSet();

        Assert.Null(ObNumberRules.Pick(Get("ProgramCycle"), used));
    }
}
