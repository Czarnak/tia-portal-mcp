using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>One device candidate, with its stable offset and candidate-scoped degradation messages.</summary>
public sealed record HardwareDevicePageCandidateInfo(
    int Offset,
    DeviceInfo Device,
    IReadOnlyList<string> Messages);

/// <summary>One subnet candidate, with its stable offset and candidate-scoped degradation messages.</summary>
public sealed record HardwareSubnetPageCandidateInfo(
    int Offset,
    SubnetInfo Subnet,
    IReadOnlyList<string> Messages);

/// <summary>
/// Worker-internal candidate enumeration result. Session identity stays in the WorkerResponse
/// envelope and is deliberately not duplicated in this payload.
/// </summary>
public sealed record HardwarePageCandidateResultInfo(
    int OrderingVersion,
    string QueryHash,
    string SnapshotHash,
    int StartOffset,
    int TotalDevices,
    int TotalSubnets,
    IReadOnlyList<string> Messages,
    IReadOnlyList<HardwareDevicePageCandidateInfo> DeviceCandidates,
    IReadOnlyList<HardwareSubnetPageCandidateInfo> SubnetCandidates);
