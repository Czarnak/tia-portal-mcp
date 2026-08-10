using System;
using System.Threading;

namespace TiaMcpServer.OpennessWorker.Openness;

internal static class ProjectOpenSettlingGuard
{
    internal static void WaitForMinimumDwell(DateTimeOffset openedAtUtc, TimeSpan minimumDwell)
        => WaitForMinimumDwell(
            openedAtUtc,
            minimumDwell,
            utcNow: () => DateTimeOffset.UtcNow,
            delay: Thread.Sleep);

    internal static void WaitForMinimumDwell(
        DateTimeOffset openedAtUtc,
        TimeSpan minimumDwell,
        Func<DateTimeOffset> utcNow,
        Action<TimeSpan> delay)
    {
        if (minimumDwell < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumDwell));
        }

        if (utcNow is null)
        {
            throw new ArgumentNullException(nameof(utcNow));
        }

        if (delay is null)
        {
            throw new ArgumentNullException(nameof(delay));
        }

        var elapsed = utcNow() - openedAtUtc;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        var remaining = minimumDwell - elapsed;
        if (remaining > TimeSpan.Zero)
        {
            delay(remaining);
        }
    }
}
