using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tools;

/// <summary>Strict, operation-specific worker decoding before any public budget projection.</summary>
internal static class StandalonePayloadContract
{
    internal const string ProtocolFailureMessage = "The worker payload did not match the declared standalone result contract.";

    internal static StructuredOperationFailure Failure(WorkerCallResult result)
        => new(result.FailureCategory ?? WorkerFailureCategories.ProtocolError,
            result.Error ?? "The worker operation failed.");

    internal static StructuredOperationFailure? Rejection(WorkerCallResult result)
        => !result.Success && (result.DispatchState == WorkerDispatchState.NotSent
            || result.FailureCategory is WorkerFailureCategories.AccessDenied
                or WorkerFailureCategories.BindingConflict or WorkerFailureCategories.ValidationError)
            ? Failure(result) : null;

    internal static StandaloneToolOutcome<ProjectStatusInfo> DecodeStatus(WorkerCallResult result)
    {
        if (!result.Success)
            return new(OperationBatchStatus.Failed, null, Failure(result), null);

        try
        {
            var payload = CanonicalJson.DeserializeWorkerPayload<ProjectStatusResultInfo>(result.Payload);
            var status = payload.Project;
            if (!payload.Success || payload.Operation != "get_project_status" || status is null
                || !string.Equals(ProjectPathNormalization.Canonicalize(payload.ProjectPath),
                    ProjectPathNormalization.Canonicalize(status.Path), StringComparison.OrdinalIgnoreCase)
                || (status.IsOpen && string.IsNullOrWhiteSpace(status.Path))
                || (!status.IsOpen && status.Path is not null)
                || status.Size < 0)
                throw new JsonException();
            ValidateMetadata(status.Metadata);
            return new(OperationBatchStatus.Succeeded, status, null, null);
        }
        catch (JsonException)
        {
            return ProtocolFailure<ProjectStatusInfo>();
        }
    }

    internal static StandaloneToolOutcome<T> ProtocolFailure<T>() where T : class
        => new(OperationBatchStatus.Failed, null,
            new(WorkerFailureCategories.ProtocolError, ProtocolFailureMessage), null);

    private static void ValidateMetadata(ProjectMetadataInfo? metadata)
    {
        if (metadata is null) return;
        RejectNullElements(metadata.Comment?.Translations);
        RejectNullElements(metadata.LanguageSettings?.Languages);
        RejectNullElements(metadata.LanguageSettings?.ActiveLanguages);
        RejectNullElements(metadata.HistoryEntries);
        RejectNullElements(metadata.UsedProducts);
    }

    internal static void RejectNullElements<T>(IEnumerable<T>? values)
    {
        if (values?.Any(value => value is null) == true) throw new JsonException();
    }
}
