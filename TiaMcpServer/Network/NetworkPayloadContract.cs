using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Network;

/// <summary>
/// The only decoder of Network worker success payloads. Every network operation declares exactly
/// one result contract here; a payload that does not match it is rejected rather than forwarded.
///
/// <para>
/// Rejection is deliberate. Forwarding an unrecognized payload would publish worker-shaped data
/// under a declared output schema that does not describe it, so malformed, unknown, incorrectly
/// cased, incorrectly typed, and structurally invalid payloads all become failed items with
/// category <see cref="WorkerFailureCategories.ProtocolError"/>. The rejected payload is never
/// echoed back — the caller asked for contract-shaped data and must not be handed the raw bytes
/// that failed the contract.
/// </para>
/// </summary>
public static class NetworkPayloadContract
{
    /// <summary>Projects one worker outcome into its structured batch item.</summary>
    public static StructuredOperationItem Project(
        NetworkOperationRequest operation,
        WorkerCallResult workerResult)
    {
        var warnings = workerResult.Warnings ?? Array.Empty<string>();

        if (!workerResult.Success)
        {
            return Failed(
                operation,
                workerResult.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                workerResult.Error ?? $"Network operation '{operation.Operation}' failed.",
                warnings);
        }

        JsonElement result;
        try
        {
            result = Decode(operation.Operation, workerResult.Payload);
        }
        catch (JsonException)
        {
            return Failed(
                operation,
                WorkerFailureCategories.ProtocolError,
                $"The worker payload for '{operation.Operation}' did not match its declared result "
                    + "contract and was rejected.",
                warnings);
        }

        return new StructuredOperationItem(
            operation.OperationId,
            operation.Operation,
            OperationBatchStatus.Succeeded,
            result,
            Failure: null,
            Omission: null,
            SkipReason: null,
            warnings);
    }

    private static JsonElement Decode(string operation, string payload) => operation switch
    {
        "read_hardware_config" => Decode<HardwareConfigInfo>(payload),
        "search_equipment_catalog" => Decode<CatalogEntryInfo[]>(payload),
        "add_network_device" => Decode<AddDeviceResultInfo>(payload),
        "configure_network_device" => Decode<ConfigureNetworkDeviceResultInfo>(payload),
        _ => throw new JsonException($"No declared result contract for network operation '{operation}'."),
    };

    private static JsonElement Decode<T>(string payload) => CanonicalJson.Normalize<T>(payload).Element;

    private static StructuredOperationItem Failed(
        NetworkOperationRequest operation,
        string category,
        string message,
        IReadOnlyList<string> warnings)
        => new(
            operation.OperationId,
            operation.Operation,
            OperationBatchStatus.Failed,
            Result: null,
            new StructuredOperationFailure(category, message),
            Omission: null,
            SkipReason: null,
            warnings);
}
