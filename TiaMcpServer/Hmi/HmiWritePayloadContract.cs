using System.Text.Json;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Hmi;

public static class HmiWritePayloadContract
{
    public static StructuredOperationItem Project(HmiWriteOperationRequest operation, WorkerCallResult result)
    {
        var warnings = result.Warnings ?? Array.Empty<string>();
        try
        {
            if (!result.Success)
            {
                if (result.HmiWriteOutcome is not { } failedOutcome)
                    return StructuredOperationItem.Failed(operation,
                        result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed, result.Error ?? "The HMI write failed.", warnings);
                Validate(failedOutcome);
                return new(operation.OperationId, operation.Operation, OperationBatchStatus.Failed,
                    CanonicalJson.ToElement(failedOutcome),
                    new(result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed, result.Error ?? "The HMI write failed."), null, null, warnings);
            }
            var (outcome, _, element) = CanonicalJson.NormalizeWorkerPayload<HmiWriteOutcomeInfo>(result.Payload);
            Validate(outcome);
            return new(operation.OperationId, operation.Operation, OperationBatchStatus.Succeeded, element, null, null, null, warnings);
        }
        catch (JsonException)
        {
            return StructuredOperationItem.Failed(operation, WorkerFailureCategories.ProtocolError,
                "The HMI worker payload did not match its declared result contract and was rejected.", warnings);
        }
    }

    private static void Validate(HmiWriteOutcomeInfo outcome)
    {
        if (string.IsNullOrEmpty(outcome.ObjectName) || outcome.AppliedProperties is null || outcome.AppliedProperties.Any(p => p is null))
            throw new JsonException();
    }
}
