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
        if (!result.Success)
            return new(operation.OperationId, operation.Operation, OperationBatchStatus.Failed,
                result.HmiWriteOutcome is null ? null : CanonicalJson.ToElement(result.HmiWriteOutcome),
                new(result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed, result.Error ?? "The HMI write failed."), null, null, warnings);
        try
        {
            var (outcome, _, element) = CanonicalJson.NormalizeWorkerPayload<HmiWriteOutcomeInfo>(result.Payload);
            if (string.IsNullOrEmpty(outcome.ObjectName) || outcome.AppliedProperties is null || outcome.AppliedProperties.Any(p => p is null))
                throw new JsonException();
            return new(operation.OperationId, operation.Operation, OperationBatchStatus.Succeeded, element, null, null, null, warnings);
        }
        catch (JsonException)
        {
            return new(operation.OperationId, operation.Operation, OperationBatchStatus.Failed, null,
                new(WorkerFailureCategories.ProtocolError, "The HMI worker payload did not match its declared result contract and was rejected."), null, null, warnings);
        }
    }
}
