using System.Text.Json.Serialization;
using TiaMcpServer.Contracts.Network;

namespace TiaMcpServer.Network;

/// <summary>
/// What one previewed network write operation will act on.
///
/// <para>
/// The hardware-identity members (<see cref="NetworkInterfaceName"/> through
/// <see cref="IoSystemNumber"/>) describe objects resolved against the hardware configuration by
/// <see cref="NetworkIdentityResolver"/> — never anything the caller typed verbatim. For
/// <c>add_network_device</c> and <c>create_subnet</c> (creation, which names something that does
/// not exist yet) they stay null and only the request-derived members are populated —
/// <see cref="DeviceName"/>/<see cref="DeviceTypeIdentifier"/> for a device, <see cref="SubnetName"/>
/// for a subnet — with no id invented for either. For <c>configure_network_device</c> they are the
/// canonical matched location: exactly one device, exactly one node (by ordinal <c>nodeId</c>,
/// scoped to that device), and — when requested — exactly one subnet and/or IO system. For
/// <c>update_subnet</c>/<c>delete_subnet</c> only <see cref="SubnetName"/>/<see cref="SubnetId"/>
/// are populated, resolved by exact ordinal <c>subnetId</c> match against
/// <see cref="TiaMcpServer.Contracts.Network.HardwareConfigInfo.Subnets"/>; <see cref="DeviceName"/> stays
/// null because a subnet target never has a device identity. Presentation names here are evidence
/// only; current ordinary reads establish exact identity before mutation.
/// </para>
/// </summary>
public sealed record NetworkWriteTargetEvidence(
    string OperationId,
    string Operation,
    string? DeviceName,
    string? DeviceTypeIdentifier,
    IReadOnlyList<string> DeviceItemPath,
    string? NetworkInterfaceName,
    string? NodeName,
    string? NodeId,
    string? SubnetName,
    string? SubnetId,
    string? IoSystemName,
    int? IoSystemNumber,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<NetworkInterfacePathSegmentInfo>? InterfacePath = null);
