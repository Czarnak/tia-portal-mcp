using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Network;

internal sealed record HardwarePageCandidateCall(
    string? ProjectPath,
    string? DeviceName,
    string? PlcName,
    bool IncludeIoDetails,
    bool IncludeTagMatches,
    int PageSize,
    HardwarePageContinuationInfo? Continuation,
    ProjectBindingSnapshot? RequiredHostBinding,
    WorkerSessionIdentity? ExpectedSessionIdentity);

internal sealed class HardwarePaginationCoordinator
{
    private readonly HardwarePageCursorCodec _cursorCodec;
    private readonly HardwarePageProjector _projector;
    private readonly Func<ProjectBindingSnapshot> _captureBinding;
    private readonly Func<HardwarePageCandidateCall, Task<HardwarePageWorkerCallResult>> _readCandidates;

    internal HardwarePaginationCoordinator(
        OpennessWorkerClient workerClient,
        HardwarePageCursorCodec cursorCodec,
        HardwarePageProjector projector)
        : this(
            cursorCodec,
            projector,
            () => workerClient.BindingSnapshot,
            call => workerClient.ReadHardwarePageCandidatesAsync(
                call.ProjectPath,
                call.DeviceName,
                call.PlcName,
                call.IncludeIoDetails,
                call.IncludeTagMatches,
                call.PageSize,
                call.Continuation,
                call.RequiredHostBinding,
                call.ExpectedSessionIdentity))
    {
    }

    internal HardwarePaginationCoordinator(
        HardwarePageCursorCodec cursorCodec,
        HardwarePageProjector projector,
        Func<ProjectBindingSnapshot> captureBinding,
        Func<HardwarePageCandidateCall, Task<HardwarePageWorkerCallResult>> readCandidates)
    {
        _cursorCodec = cursorCodec ?? throw new ArgumentNullException(nameof(cursorCodec));
        _projector = projector ?? throw new ArgumentNullException(nameof(projector));
        _captureBinding = captureBinding ?? throw new ArgumentNullException(nameof(captureBinding));
        _readCandidates = readCandidates ?? throw new ArgumentNullException(nameof(readCandidates));
    }

    internal async Task<StructuredOperationItem> ExecuteAsync(NetworkOperationRequest operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var currentBinding = _captureBinding();
        HardwarePageCursorState? cursorState = null;
        HardwarePageContinuationInfo? continuation = null;
        if (operation.Cursor is not null)
        {
            try
            {
                cursorState = _cursorCodec.Decode(operation.Cursor);
            }
            catch (HardwarePageCursorException exception)
            {
                return Failed(operation, exception.Category, exception.Message);
            }

            var validationCategory = HardwarePageCursorValidator.Validate(
                cursorState,
                operation,
                currentBinding);
            if (validationCategory is not null)
            {
                return Failed(
                    operation,
                    validationCategory,
                    "The supplied hardware-page cursor does not match the current request or host binding.");
            }

            continuation = new HardwarePageContinuationInfo(
                cursorState.OrderingVersion,
                cursorState.QueryHash,
                cursorState.SnapshotHash,
                cursorState.Offset);
        }

        var pageSize = operation.PageSize ?? (cursorState is not null ? 50 : 0);
        if (pageSize < 1 || pageSize > 200)
        {
            return Failed(
                operation,
                WorkerFailureCategories.ValidationError,
                "Hardware pageSize must be between 1 and 200.");
        }

        var call = new HardwarePageCandidateCall(
            ProjectPath: cursorState?.ResolvedProjectPath ?? operation.ProjectPath,
            operation.DeviceName,
            operation.PlcName,
            operation.IncludeIoDetails ?? false,
            operation.IncludeTagMatches ?? false,
            pageSize,
            continuation,
            RequiredHostBinding: cursorState is null ? null : currentBinding,
            ExpectedSessionIdentity: cursorState?.SessionIdentity);
        var callResult = await _readCandidates(call).ConfigureAwait(false);
        var workerResult = callResult.WorkerResult;
        if (!workerResult.Success)
        {
            return Failed(
                operation,
                workerResult.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                workerResult.Error ?? "The hardware-page worker operation failed.",
                workerResult.Warnings);
        }

        if (cursorState is not null && !cursorState.HostBinding.Matches(callResult.HostBinding))
        {
            return Failed(
                operation,
                WorkerFailureCategories.CursorBindingMismatch,
                "The host project binding changed after the preceding hardware page.",
                workerResult.Warnings);
        }

        if (!TryGetResolvedIdentity(workerResult, out var observedIdentity, out var resolvedPath))
        {
            return Failed(
                operation,
                WorkerFailureCategories.ProtocolError,
                "The hardware-page worker response did not include a complete coherent session identity.",
                workerResult.Warnings);
        }

        var reportedPath = ProjectPathNormalization.Canonicalize(workerResult.ResolvedProjectPath);
        if (reportedPath is not null
            && !string.Equals(reportedPath, resolvedPath, StringComparison.OrdinalIgnoreCase))
        {
            return Failed(
                operation,
                cursorState is null
                    ? WorkerFailureCategories.ProtocolError
                    : WorkerFailureCategories.CursorBindingMismatch,
                cursorState is null
                    ? "The hardware-page worker response reported conflicting project paths."
                    : "The hardware-page continuation resolved to a different project path.",
                workerResult.Warnings);
        }

        if (cursorState is not null && !SameIdentity(cursorState.SessionIdentity, observedIdentity!))
        {
            return Failed(
                operation,
                WorkerFailureCategories.CursorBindingMismatch,
                "The hardware-page continuation no longer matches the live worker session.",
                workerResult.Warnings);
        }

        var decoded = HardwarePagePayloadContract.Decode(operation, workerResult, continuation);
        if (!decoded.IsSuccess)
        {
            return decoded.Item!;
        }

        return _projector.Project(
            operation,
            decoded.Payload!,
            resolvedPath!,
            observedIdentity!,
            callResult.HostBinding,
            workerResult.Warnings ?? Array.Empty<string>());
    }

    private static bool TryGetResolvedIdentity(
        WorkerCallResult workerResult,
        out WorkerSessionIdentity? identity,
        out string? resolvedPath)
    {
        identity = workerResult.SessionIdentity;
        resolvedPath = ProjectPathNormalization.Canonicalize(identity?.ProjectPath);
        return identity is not null
            && !string.IsNullOrWhiteSpace(identity.WorkerSessionId)
            && identity.SessionGeneration >= 0
            && identity.PortalProcessId is > 0
            && resolvedPath is not null;
    }

    private static bool SameIdentity(WorkerSessionIdentity expected, WorkerSessionIdentity observed)
        => string.Equals(expected.WorkerSessionId, observed.WorkerSessionId, StringComparison.Ordinal)
            && expected.SessionGeneration == observed.SessionGeneration
            && expected.PortalProcessId == observed.PortalProcessId
            && string.Equals(
                ProjectPathNormalization.Canonicalize(expected.ProjectPath),
                ProjectPathNormalization.Canonicalize(observed.ProjectPath),
                StringComparison.OrdinalIgnoreCase);

    private static StructuredOperationItem Failed(
        NetworkOperationRequest operation,
        string category,
        string message,
        IReadOnlyList<string>? warnings = null)
        => HardwarePagePayloadContract.Failed(
            operation,
            category,
            message,
            warnings ?? Array.Empty<string>());
}
