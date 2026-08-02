using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Network;

/// <summary>
/// Outcome of one hardware-state read. <see cref="State"/> is the decoded typed value on success;
/// a failure carries a closed <see cref="WorkerFailureCategories"/> category so the caller can
/// report it without inferring anything from message text.
/// </summary>
public sealed record NetworkStateSnapshot(
    bool Success,
    HardwareConfigInfo? State,
    string? FailureCategory,
    string? Error)
{
    public static NetworkStateSnapshot Ok(HardwareConfigInfo state) => new(true, state, null, null);

    public static NetworkStateSnapshot Fail(string failureCategory, string error)
        => new(false, null, failureCategory, error);
}

/// <summary>Stable preview targets and current-state acquisition for dedicated network writes.</summary>
public static class NetworkSafetySnapshot
{
    /// <summary>
    /// Describes what each requested operation acts on, in request order. Ordering is data: the
    /// safety token binds this list, so reordering the operations is a different target.
    /// </summary>
    public static IReadOnlyList<NetworkWriteTargetEvidence> BuildTargets(
        IReadOnlyList<NetworkOperationRequest> operations)
        => operations.Select(operation => new NetworkWriteTargetEvidence(
            operation.OperationId,
            operation.Operation,
            operation.DeviceName ?? string.Empty,
            operation.TypeIdentifier,

            // Device item paths and the node/subnet/IO-system identities below come from resolving
            // the hardware configuration, which nothing does yet. Echoing request fields into them
            // would present caller input as resolved evidence, so they stay empty until something
            // actually resolves them.
            Array.Empty<string>(),
            NetworkInterfaceName: null,
            NodeName: null,
            NodeId: null,
            SubnetName: null,
            SubnetId: null,
            IoSystemName: null,
            IoSystemNumber: null)).ToArray();

    public static string? ResolveProjectPath(IReadOnlyList<NetworkOperationRequest> operations)
        => operations.FirstOrDefault(operation => !string.IsNullOrWhiteSpace(operation.ProjectPath))?.ProjectPath;

    /// <summary>
    /// Reads hardware state and decodes it under its declared contract.
    ///
    /// <para>
    /// The decoded value is what a token binds, so a payload that fails its contract has to fail
    /// here rather than survive as an opaque string that would happily hash. A caller cannot issue
    /// or consume a token against state nobody could describe. The rejected payload is never
    /// echoed back.
    /// </para>
    /// </summary>
    public static async Task<NetworkStateSnapshot> ReadCurrentStateAsync(
        OpennessWorkerClient client,
        string? projectPath)
    {
        var workerResult = await client.ReadHardwareConfigAsync(projectPath).ConfigureAwait(false);
        if (!workerResult.Success)
        {
            return NetworkStateSnapshot.Fail(
                workerResult.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                workerResult.Error ?? "The hardware configuration could not be read.");
        }

        try
        {
            return NetworkStateSnapshot.Ok(NetworkPayloadContract.DecodeHardwareConfig(workerResult.Payload));
        }
        catch (JsonException)
        {
            return NetworkStateSnapshot.Fail(
                WorkerFailureCategories.ProtocolError,
                "The hardware configuration payload did not match its declared result contract and "
                    + "was rejected, so no safety token can be bound to the current project state.");
        }
    }
}
