using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Network;

/// <summary>A tool-level failure that prevented the batch from running at all.</summary>
public sealed record NetworkToolError(string Category, string Message);

/// <summary>
/// Declared output schema of <c>network_read</c>.
///
/// <para>
/// Exactly one of <see cref="Batch"/> and <see cref="Error"/> is populated: <see cref="Error"/>
/// when validation or access control rejected the call before any worker ran, otherwise
/// <see cref="Batch"/>. <see cref="Success"/> describes the whole call — a batch that ran but
/// contains failed items reports <c>false</c> here while remaining a successful MCP result.
/// Root <see cref="Warnings"/> contains call-level diagnostics; per-operation warnings remain
/// on the items in <see cref="Batch"/>.
/// </para>
/// </summary>
public sealed record NetworkReadResponse(
    string Tool,
    string ContractVersion,
    bool Success,
    NetworkToolError? Error,
    IReadOnlyList<string> Warnings,
    StructuredOperationBatch? Batch);

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
/// <see cref="TiaMcpServer.Contracts.HardwareConfigInfo.Subnets"/>; <see cref="DeviceName"/> stays
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
    int? IoSystemNumber);
