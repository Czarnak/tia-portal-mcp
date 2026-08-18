using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Network;

/// <summary>Closed vocabulary of <see cref="NetworkWriteResponse.Phase"/> values.</summary>
public static class NetworkWritePhases
{
    /// <summary>Nothing was changed; a safety token was issued.</summary>
    public const string Preview = "preview";

    /// <summary>The batch ran. Individual operations may still have failed.</summary>
    public const string Apply = "apply";

    /// <summary>The call was rejected before any operation ran.</summary>
    public const string Error = "error";
}

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
/// </para>
/// </summary>
public sealed record NetworkReadResponse(
    string Tool,
    bool Success,
    StructuredOperationBatch? Batch,
    NetworkToolError? Error);

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
/// only; the safety token binds this whole record, so a caller cannot satisfy it by echoing back
/// the names.
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

/// <summary>
/// A previewed network write. Nothing has been changed: <see cref="SafetyToken"/> is single-use,
/// expires at <see cref="ExpiresAtUtc"/>, and is bound to this exact ordered request, this target
/// list, and the hardware state the hashes describe.
/// </summary>
public sealed record NetworkWritePreview(
    IReadOnlyList<NetworkWriteTargetEvidence> Target,
    string Summary,
    string CurrentStateHash,
    string RequestedInputHash,
    DateTimeOffset ExpiresAtUtc,
    string SafetyToken,
    ProjectBindingSnapshot ProjectBinding,
    JsonElement? Diff,
    string Instructions);

/// <summary>
/// Declared output schema of <c>network_write</c>: a discriminated envelope where
/// <see cref="Phase"/> names which single member is populated — <c>preview</c> for
/// <see cref="Preview"/>, <c>apply</c> for <see cref="Batch"/>, <c>error</c> for
/// <see cref="Error"/>. The other two are always null.
///
/// <para>
/// <see cref="Success"/> describes the whole call. An applied batch containing a failed operation
/// reports <c>false</c> here while still being a successful MCP result: only the <c>error</c>
/// phase — a call rejected before anything ran — sets the protocol's <c>isError</c>.
/// </para>
/// </summary>
public sealed record NetworkWriteResponse(
    string Tool,
    string Phase,
    bool Success,
    NetworkWritePreview? Preview,
    StructuredOperationBatch? Batch,
    NetworkToolError? Error);
