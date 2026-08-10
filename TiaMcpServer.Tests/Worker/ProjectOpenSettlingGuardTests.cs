using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

public class ProjectOpenSettlingGuardTests
{
    [Fact]
    public void NewlyOpenedProject_WaitsForTheRemainingMinimumDwell()
    {
        var openedAt = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        TimeSpan? observedDelay = null;

        ProjectOpenSettlingGuard.WaitForMinimumDwell(
            openedAt,
            TimeSpan.FromSeconds(60),
            utcNow: () => openedAt.AddSeconds(15),
            delay: value => observedDelay = value);

        Assert.Equal(TimeSpan.FromSeconds(45), observedDelay);
    }

    [Fact]
    public void SettledProject_DoesNotWaitAgain()
    {
        var openedAt = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var delayCalled = false;

        ProjectOpenSettlingGuard.WaitForMinimumDwell(
            openedAt,
            TimeSpan.FromSeconds(60),
            utcNow: () => openedAt.AddSeconds(75),
            delay: _ => delayCalled = true);

        Assert.False(delayCalled);
    }
}
