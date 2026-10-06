using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Plc;

/// <summary>
/// The only decoder of PLC read worker payloads. Each operation declares exactly one result type;
/// a payload that does not decode is a <c>protocol_error</c> and is never echoed back.
/// </summary>
public static class PlcPayloadContract
{
    public static StructuredOperationItem Project(PlcOperationRequest operation, WorkerCallResult workerResult)
        => Project(operation, workerResult, Console.Error.WriteLine);

    internal static StructuredOperationItem Project(
        PlcOperationRequest operation,
        WorkerCallResult workerResult,
        Action<string> writeProtocolDiagnostic)
        => Run(operation, workerResult, writeProtocolDiagnostic, (op, payload, warnings, _) => Decode(op, payload, warnings));

    /// <summary>Projects the result of one plc_write operation; each operation decodes exactly one declared type.</summary>
    public static StructuredOperationItem ProjectWrite(PlcOperationRequest operation, WorkerCallResult workerResult)
        => ProjectWrite(operation, workerResult, Console.Error.WriteLine);

    internal static StructuredOperationItem ProjectWrite(
        PlcOperationRequest operation,
        WorkerCallResult workerResult,
        Action<string> writeProtocolDiagnostic)
        => Run(operation, workerResult, writeProtocolDiagnostic, DecodeWrite);

    private static StructuredOperationItem Run(
        PlcOperationRequest operation,
        WorkerCallResult workerResult,
        Action<string> writeProtocolDiagnostic,
        Func<PlcOperationRequest, string, IReadOnlyList<string>, WorkerCallResult, JsonElement> decode)
    {
        var warnings = workerResult.Warnings ?? Array.Empty<string>();
        if (!workerResult.Success)
        {
            return Failed(
                operation,
                workerResult.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                workerResult.Error ?? $"PLC operation '{operation.Operation}' failed.",
                warnings,
                workerResult.BlockImportOutcome);
        }

        try
        {
            return new StructuredOperationItem(
                operation.OperationId,
                operation.Operation,
                OperationBatchStatus.Succeeded,
                decode(operation, workerResult.Payload, warnings, workerResult),
                Failure: null,
                Omission: null,
                SkipReason: null,
                warnings);
        }
        catch (JsonException)
        {
            // Server-side diagnostic only: names the operation, never the rejected payload.
            try
            {
                writeProtocolDiagnostic(
                    $"TiaMcpServer: worker payload contract rejection: operation={operation.Operation}.");
            }
            catch
            {
                // Diagnostics must never replace the stable fail-closed protocol_error response.
            }

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
            "list_tag_tables" => Inventory(payload),
            _ => throw new JsonException($"No declared result contract for PLC operation '{operation.Operation}'."),
        };

    private static readonly IReadOnlySet<string> TagOperations = new HashSet<string>(StringComparer.Ordinal)
    {
        "create_tag_table", "delete_tag_table", "create_tag", "update_tag", "delete_tag",
        "create_user_constant", "update_user_constant", "delete_user_constant",
    };

    private static readonly IReadOnlySet<string> BlockOperations = new HashSet<string>(StringComparer.Ordinal)
    {
        "create_block", "delete_block", "create_block_group", "delete_block_group",
    };

    /// <exception cref="JsonException">The payload does not decode as the operation's one declared type.</exception>
    private static JsonElement DecodeWrite(
        PlcOperationRequest operation,
        string payload,
        IReadOnlyList<string> warnings,
        WorkerCallResult workerResult)
    {
        var name = operation.Operation;
        if (TagOperations.Contains(name))
        {
            return CanonicalJson.NormalizeWorkerPayload<TagMutationResultInfo>(payload).Element;
        }

        if (BlockOperations.Contains(name))
        {
            return CanonicalJson.NormalizeWorkerPayload<BlockMutationResultInfo>(payload).Element;
        }

        switch (name)
        {
            case "update_type_content":
                return CanonicalJson.NormalizeWorkerPayload<PlcTypeImportResultInfo>(payload).Element;
            case "update_block_logic":
                // The worker's text ("Import succeeded.") is not forwarded; the typed outcome is the result.
                var outcome = workerResult.BlockImportOutcome ?? throw new JsonException();
                return CanonicalJson.ToElement(new PlcBlockImportResult(
                    PlcFormatNames.Normalize(name, operation.Format), outcome));
            case "start_plc":
            case "stop_plc":
                // ponytail: the contract still omits nulls on the wire, so the required-member reader refuses
                // it; strict decode until PlcOnlineResultInfo migrates (ProjectPath is always set by the worker).
                return CanonicalJson.ToElement(CanonicalJson.Deserialize<PlcOnlineResultInfo>(payload));
            default:
                throw new JsonException($"No declared result contract for PLC write operation '{name}'.");
        }
    }

    /// <exception cref="JsonException">A list or list element of the inventory is null.</exception>
    private static JsonElement Inventory(string payload)
    {
        var (inventory, _, element) = CanonicalJson.NormalizeWorkerPayload<PlcTagInventoryInfo>(payload);
        if (inventory.Messages is null || inventory.Plcs is null) throw new JsonException();
        StandalonePayloadContract.RejectNullElements(inventory.Messages);
        StandalonePayloadContract.RejectNullElements(inventory.Plcs);
        foreach (var plc in inventory.Plcs)
        {
            if (plc.Tables is null) throw new JsonException();
            StandalonePayloadContract.RejectNullElements(plc.Tables);
            foreach (var table in plc.Tables)
            {
                if (table.Tags is null || table.UserConstants is null) throw new JsonException();
                StandalonePayloadContract.RejectNullElements(table.Tags);
                StandalonePayloadContract.RejectNullElements(table.UserConstants);
            }
        }

        return element;
    }

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
        IReadOnlyList<string> warnings,
        BlockImportOutcomeInfo? blockImportOutcome = null)
        => new(
            operation.OperationId,
            operation.Operation,
            OperationBatchStatus.Failed,
            Result: null,
            new StructuredOperationFailure(category, message, blockImportOutcome),
            Omission: null,
            SkipReason: null,
            warnings);
}
