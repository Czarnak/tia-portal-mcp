using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Plc;

/// <summary>
/// The only decoder of PLC read worker payloads. Each operation declares exactly one result type;
/// a payload that does not decode is a <c>protocol_error</c> and is never echoed back.
/// </summary>
public static class PlcPayloadContract
{
    public static StructuredOperationItem Project(PlcOperationRequest operation, WorkerCallResult workerResult)
    {
        var warnings = workerResult.Warnings ?? Array.Empty<string>();
        if (!workerResult.Success)
        {
            return Failed(
                operation,
                workerResult.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                workerResult.Error ?? $"PLC operation '{operation.Operation}' failed.",
                warnings);
        }

        try
        {
            return new StructuredOperationItem(
                operation.OperationId,
                operation.Operation,
                OperationBatchStatus.Succeeded,
                Decode(operation, workerResult.Payload, warnings),
                Failure: null,
                Omission: null,
                SkipReason: null,
                warnings);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return Failed(
                operation,
                WorkerFailureCategories.ProtocolError,
                $"The worker payload for '{operation.Operation}' did not match its declared result "
                    + "contract and was rejected.",
                warnings);
        }
    }

    private static JsonElement Decode(PlcOperationRequest operation, string payload, IReadOnlyList<string> warnings)
        => operation.Operation switch
        {
            "get_block_content" or "get_type_content" => CanonicalJson.ToElement(Content(operation, payload, warnings)),
            "list_tag_tables" => CanonicalJson.NormalizeWorkerPayload<PlcTagInventoryInfo>(payload).Element,
            _ => throw new JsonException($"No declared result contract for PLC operation '{operation.Operation}'."),
        };

    private static PlcContentResult Content(PlcOperationRequest operation, string payload, IReadOnlyList<string> warnings)
    {
        var format = PlcFormatNames.Normalize(operation.Operation, operation.Format);
        var hash = operation.WithDependencies == true ? null : PlcContentHashes.Compute(format, payload);
        return new PlcContentResult(format, payload, hash, warnings);
    }

    private static StructuredOperationItem Failed(
        PlcOperationRequest operation,
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
