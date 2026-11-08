using System.Text.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Library;

public static class LibraryWritePayloadContract
{
    public static StructuredOperationItem Project(LibraryWriteItem item, WorkerCallResult result)
    {
        try
        {
            if (!result.Success)
            {
                if (result.LibraryWriteOutcome is not { } outcome)
                    return StructuredOperationItem.Failed(item,
                        result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                        result.Error ?? "The library write failed.", result.Warnings);
                ValidateType(outcome.Type);
                return new(item.OperationId, item.Operation, OperationBatchStatus.Failed, CanonicalJson.ToElement(outcome),
                    new(result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                        result.Error ?? "The library write failed."), null, null, result.Warnings);
            }
            var (_, _, element) = CanonicalJson.NormalizeWorkerPayload<LibraryWriteResultInfo>(result.Payload, value =>
            {
                ValidateIdentity(value.Library);
                ValidateIdentity(value.Target);
                ValidateType(value.Type);
            });
            return new(item.OperationId, item.Operation, OperationBatchStatus.Succeeded, element, null, null, null, result.Warnings);
        }
        catch (JsonException)
        {
            return StructuredOperationItem.Failed(item, WorkerFailureCategories.ProtocolError,
                "The library worker payload did not match its declared result contract and was rejected.", result.Warnings);
        }
    }

    internal static void ValidateIdentity(LibraryIdentityInfo? identity)
    {
        if (identity?.FolderPath.Any(segment => segment is null) == true) throw new JsonException();
    }

    internal static void ValidateType(LibraryTypeResultInfo? type)
    {
        if (type is not null && (type.TypeName is null || type.TypeGuid is null || type.FolderPath is null || type.InstancePaths is null || type.DeletedVersions is null
            || type.FolderPath.Concat(type.InstancePaths).Concat(type.DeletedVersions).Any(value => value is null))) throw new JsonException();
    }

    internal static void ValidateEnvelopeOutcome(JsonElement envelope)
    {
        if (envelope.ValueKind != JsonValueKind.Object) return;
        var members = envelope.EnumerateObject()
            .Where(p => string.Equals(p.Name, "libraryWriteOutcome", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (members.Length == 0) return;
        // Validate raw evidence before the permissive legacy envelope reader normalizes it.
        if (members.Length != 1 || members[0].Name != "libraryWriteOutcome") throw new JsonException();
        if (members[0].Value.ValueKind != JsonValueKind.Null)
            ValidateType(CanonicalJson.DeserializeWorkerPayload<LibraryWriteOutcomeInfo>(members[0].Value.GetRawText()).Type);
    }
}
