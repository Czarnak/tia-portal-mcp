using System.Text.Json;
using TiaMcpServer.Contracts.Vci;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Vci;

public static class VciWritePayloadContract
{
    internal static void ValidateEnvelopeOutcome(JsonElement envelope)
    {
        if (envelope.ValueKind != JsonValueKind.Object) return;
        var members = envelope.EnumerateObject()
            .Where(p => string.Equals(p.Name, "vciWriteOutcome", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (members.Length == 0) return;
        // The envelope is legacy case-insensitive; never validate a different outcome than it decodes.
        if (members.Length != 1 || members[0].Name != "vciWriteOutcome") throw new JsonException();
        Validate(CanonicalJson.DeserializeWorkerPayload<VciWriteOutcomeInfo>(members[0].Value.GetRawText()));
    }

    public static StructuredOperationItem Project(VciOperationRequest item, WorkerCallResult result)
    {
        try
        {
            var outcome = result.Success ? CanonicalJson.DeserializeWorkerPayload<VciWriteOutcomeInfo>(result.Payload) : result.VciWriteOutcome;
            if (outcome is not null) Validate(outcome);
            var failed = !result.Success || outcome!.Failed > 0;
            return new(item.OperationId, item.Operation, failed ? OperationBatchStatus.Failed : OperationBatchStatus.Succeeded,
                outcome is null ? null : CanonicalJson.ToElement(outcome),
                failed ? new(result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed, result.Error ?? "One or more VCI leaves failed.") : null,
                null, null, result.Warnings);
        }
        catch (JsonException)
        {
            return new(item.OperationId, item.Operation, OperationBatchStatus.Failed, null,
                new(WorkerFailureCategories.ProtocolError, "The VCI worker outcome did not match its declared result contract and was rejected."), null, null, result.Warnings);
        }
    }

    internal static void Validate(VciWriteOutcomeInfo outcome)
    {
        if (outcome.Skipped is not { } s || outcome.Notes is null || outcome.Notes.Any(n => n is null)
            || new[] { outcome.Leaves, outcome.Succeeded, outcome.Failed, outcome.FormatFallbacks, s.Equal, s.AlreadyMapped, s.Conflict, s.TargetChanged }.Any(n => n < 0)
            || outcome.FormatFallbacks > outcome.Leaves)
            throw new JsonException();
        if (outcome.LastAttempted is { } last && (last.Path is null || last.Path.Count == 0
            || last.Path.Any(p => p is null || string.IsNullOrWhiteSpace(p.NodeType) || string.IsNullOrWhiteSpace(p.Name))
            || string.IsNullOrWhiteSpace(last.Operation))) throw new JsonException();
    }
}
