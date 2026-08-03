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

/// <summary>
/// Outcome of resolving an entire ordered operation list into target evidence. A single operation's
/// resolution failure fails the whole batch — there is no partial preview or partial apply target.
/// </summary>
public sealed record NetworkTargetResolution(
    bool Success,
    IReadOnlyList<NetworkWriteTargetEvidence>? Targets,
    string? FailureCategory,
    string? Error)
{
    public static NetworkTargetResolution Ok(IReadOnlyList<NetworkWriteTargetEvidence> targets) => new(true, targets, null, null);

    public static NetworkTargetResolution Fail(string failureCategory, string error) => new(false, null, failureCategory, error);
}

/// <summary>Stable preview targets and current-state acquisition for dedicated network writes.</summary>
public static class NetworkSafetySnapshot
{
    /// <summary>
    /// Resolves what each requested operation acts on, in request order, against
    /// <paramref name="state"/>. Ordering is data: the safety token binds this list, so reordering
    /// the operations is a different target.
    ///
    /// <para>
    /// Creation operations (<c>add_network_device</c>) never need <paramref name="state"/> — they
    /// name something that does not exist yet, so <paramref name="state"/> may be <see langword="null"/>
    /// when a batch contains only creation operations (see <see cref="RequiresHardwareState"/>).
    /// Any <c>configure_network_device</c> operation requires a non-null <paramref name="state"/>;
    /// resolution fails closed (<see cref="WorkerFailureCategories.PostconditionFailed"/>) rather than
    /// guess if it is ever asked to resolve one without state.
    /// </para>
    /// </summary>
    public static NetworkTargetResolution BuildTargets(
        IReadOnlyList<NetworkOperationRequest> operations,
        HardwareConfigInfo? state)
    {
        var targets = new List<NetworkWriteTargetEvidence>(operations.Count);
        foreach (var operation in operations)
        {
            var resolution = NetworkIdentityResolver.Resolve(operation, state);
            if (!resolution.Success)
            {
                return NetworkTargetResolution.Fail(resolution.FailureCategory!, resolution.Error!);
            }

            targets.Add(resolution.Evidence!);
        }

        return NetworkTargetResolution.Ok(targets);
    }

    /// <summary>
    /// True when resolving this batch's targets needs a hardware snapshot — i.e. it contains at
    /// least one <c>configure_network_device</c> operation. A purely creation batch can be resolved
    /// (and its safety-token envelope cheaply checked) without paying for a state read first.
    /// </summary>
    public static bool RequiresHardwareState(IReadOnlyList<NetworkOperationRequest> operations)
        => operations.Any(operation => string.Equals(operation.Operation, "configure_network_device", StringComparison.Ordinal));

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
