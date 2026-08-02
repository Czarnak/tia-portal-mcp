using System.Text.Json;
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
/// <see cref="IoSystemNumber"/>) describe objects resolved against the hardware configuration
/// rather than anything the caller typed. Nothing resolves them yet, so they are currently null;
/// the members the caller does select — operation, device, and catalog type — are populated and
/// are what the safety token binds as this operation's target.
/// </para>
/// </summary>
public sealed record NetworkWriteTargetEvidence(
    string OperationId,
    string Operation,
    string DeviceName,
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
