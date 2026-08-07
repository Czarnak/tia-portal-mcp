using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Siemens-free closed vocabulary shared by the Phase 4 subnet lifecycle request contract
/// (<c>create_subnet</c>, <c>update_subnet</c>, <c>delete_subnet</c>) and, later, its worker-side
/// implementation. Kept here rather than duplicated as string literals in the host catalog and the
/// worker service.
/// </summary>
public static class SubnetLifecycleContract
{
    public const string Ethernet = "Ethernet";
    public const string Profibus = "Profibus";
    public const int MinimumHighestAddress = 0;
    public const int MaximumHighestAddress = 126;

    public static IReadOnlyList<string> TransmissionSpeeds { get; } = new[]
    {
        "Baud9600", "Baud19200", "Baud45450", "Baud93750", "Baud187500",
        "Baud500000", "Baud1500000", "Baud3000000", "Baud6000000", "Baud12000000",
    };

    public static bool IsSupportedNetworkType(string? value)
        => string.Equals(value, Ethernet, StringComparison.Ordinal)
            || string.Equals(value, Profibus, StringComparison.Ordinal);

    public static bool IsSupportedTransmissionSpeed(string? value)
        => value is not null && TransmissionSpeeds.Contains(value, StringComparer.Ordinal);
}
