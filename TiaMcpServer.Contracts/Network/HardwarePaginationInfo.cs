using System.Text.Json.Serialization;

namespace TiaMcpServer.Contracts;

/// <summary>Public paging metadata returned only when a hardware-config read was explicitly paged.</summary>
public sealed record HardwarePaginationInfo(
    int TotalDevices,
    int TotalSubnets,
    int ReturnedDevices,
    int ReturnedSubnets,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NextCursor);

/// <summary>Opaque continuation evidence forwarded only between the host and worker.</summary>
public sealed record HardwarePageContinuationInfo(
    int OrderingVersion,
    string QueryHash,
    string SnapshotHash,
    int Offset);
