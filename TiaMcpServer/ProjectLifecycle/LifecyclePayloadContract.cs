using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;

namespace TiaMcpServer.ProjectLifecycle;

/// <summary>Strict roots and semantic identity checks before any public projection.</summary>
public static class LifecyclePayloadContract
{
    public const string ProtocolFailureMessage = "The worker payload did not match the declared lifecycle result contract.";

    public static ProjectLifecycleResultInfo Decode(
        WorkerCallResult result, string operation, string? expectedProjectPath, string? destinationDirectory = null)
    {
        var payload = CanonicalJson.DeserializeWorkerPayload<ProjectLifecycleResultInfo>(result.Payload);
        if (!payload.Success || payload.Operation != operation || payload.Project is null)
            throw new JsonException(ProtocolFailureMessage);
        var status = payload.Project;
        var closed = operation == "close_project";
        if (status.IsOpen == closed || string.IsNullOrWhiteSpace(status.Path)
            || !SamePath(payload.ProjectPath, status.Path) || status.Size < 0
            || (expectedProjectPath is not null && !SamePath(status.Path, expectedProjectPath)))
            throw new JsonException(ProtocolFailureMessage);
        if (!closed && (!SamePath(status.Path, result.ResolvedProjectPath)
            || !SamePath(status.Path, result.SessionIdentity?.ProjectPath)))
            throw new JsonException(ProtocolFailureMessage);
        if (destinationDirectory is not null && !IsInside(status.Path!, destinationDirectory))
            throw new JsonException(ProtocolFailureMessage);
        if (status.Metadata is not null) throw new JsonException(ProtocolFailureMessage);
        ProjectContextPayloadContract.Validate(status.Context, status.Path!);
        ProjectContextPayloadContract.ValidateStableOwner(status.Context, result.SessionIdentity?.Context);
        return payload;
    }

    public static ProjectStatusInfo DecodeStatus(
        WorkerCallResult result, string operation, string? expectedProjectPath, bool expectOpen)
    {
        var payload = CanonicalJson.DeserializeWorkerPayload<ProjectLifecycleResultInfo>(result.Payload);
        var status = payload.Project;
        if (!payload.Success || payload.Operation != operation || status is null
            || status.IsOpen != expectOpen || status.Size < 0 || status.Metadata is not null
            || !SamePath(payload.ProjectPath, status.Path)
            || (expectOpen && (string.IsNullOrWhiteSpace(status.Path)
                || !SamePath(status.Path, expectedProjectPath)
                || !SamePath(status.Path, result.ResolvedProjectPath)
                || !SamePath(status.Path, result.SessionIdentity?.ProjectPath)))
            || (!expectOpen && (status.Path is not null || result.ResolvedProjectPath is not null
                || result.SessionIdentity?.ProjectPath is not null)))
            throw new JsonException(ProtocolFailureMessage);
        if (expectOpen)
        {
            ProjectContextPayloadContract.Validate(status.Context, status.Path!);
            ProjectContextPayloadContract.ValidateStableOwner(status.Context, result.SessionIdentity?.Context);
        }
        else if (status.Context is not null) throw new JsonException(ProtocolFailureMessage);
        return status;
    }

    public static bool SamePath(string? first, string? second)
        => string.Equals(ProjectPathNormalization.Canonicalize(first),
            ProjectPathNormalization.Canonicalize(second), StringComparison.OrdinalIgnoreCase);

    private static bool IsInside(string projectPath, string directory)
    {
        try
        {
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return Path.GetFullPath(projectPath).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new JsonException(ProtocolFailureMessage, exception);
        }
    }

    public static StructuredOperationFailure Failure(WorkerCallResult result)
        => new(result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
            result.Error ?? "The lifecycle worker operation failed.");

    public static StandaloneToolOutcome<T> Failed<T>(WorkerCallResult result) where T : class
        => new(OperationBatchStatus.Failed, null, Failure(result), null);

    public static StandaloneToolOutcome<T> ProtocolFailure<T>() where T : class
        => new(OperationBatchStatus.Failed, null,
            new(WorkerFailureCategories.ProtocolError, ProtocolFailureMessage), null);
}
