using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Cursors;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Network;

internal sealed class NetworkObjectPaginationCoordinator(
    OpennessWorkerClient workerClient,
    NetworkObjectPageCursorCodec cursorCodec)
{
    internal async Task<StructuredOperationItem> ExecuteAsync(NetworkOperationRequest operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        NetworkObjectPageCursorState? state = null;
        if (operation.Cursor is not null)
        {
            try
            {
                state = cursorCodec.Decode(operation.Cursor);
            }
            catch (CursorException exception)
            {
                return Failed(operation, exception.Category, exception.Message);
            }
        }

        var binding = workerClient.BindingSnapshot;
        var pinned = await workerClient.ExecuteWithPinnedBindingAsync(binding, async () =>
        {
            var current = workerClient.BindingSnapshot;
            if (!current.IsVerified || state is not null
                && (!state.HostBinding.Matches(current) || !SameIdentity(state.SessionIdentity, current.ToWorkerIdentity())))
            {
                return Failed(operation, WorkerFailureCategories.CursorBindingMismatch,
                    "The supplied network object cursor does not match the current host binding or worker session.");
            }

            var request = new NetworkOperationRequest
            {
                OperationId = operation.OperationId, Operation = operation.Operation,
                ObjectKinds = operation.ObjectKinds?.ToArray(), DeviceName = operation.DeviceName,
                PageSize = operation.PageSize, Cursor = state?.WorkerCursor,
            };
            var worker = await NetworkWorkerInvoker.InvokeReadAsync(workerClient, request).ConfigureAwait(false);
            if (worker.IsPostOperationFailure && !IsCompleteIdentity(worker.SessionIdentity))
            {
                return Failed(operation, WorkerFailureCategories.ProtocolError,
                    "The network object worker response did not include a complete coherent session identity.", worker.Warnings);
            }
            if (state is not null && worker.FailureCategory == WorkerFailureCategories.BindingConflict)
            {
                worker = worker with { FailureCategory = WorkerFailureCategories.CursorBindingMismatch };
            }
            var item = NetworkPayloadContract.Project(operation, worker);
            if (item.Status != OperationBatchStatus.Succeeded) return item;

            if (!IsCompleteIdentity(worker.SessionIdentity)
                || !SameIdentity(current.ToWorkerIdentity(), worker.SessionIdentity)
                || worker.ResolvedProjectPath is not null
                    && !CursorStateRules.SameProject(worker.ResolvedProjectPath, current.ProjectPath))
            {
                return Failed(operation, WorkerFailureCategories.ProtocolError,
                    "The network object worker response did not include a complete coherent session identity.", worker.Warnings);
            }

            var payload = CanonicalJson.DeserializeWorkerPayload<NetworkObjectListInfo>(item.Result!.Value.GetRawText());
            if (payload.NextCursor is not null)
            {
                try
                {
                    payload.NextCursor = cursorCodec.Encode(new NetworkObjectPageCursorState(
                        1, payload.NextCursor, ProjectBindingCursorState.FromSnapshot(current), worker.SessionIdentity!));
                }
                catch (CursorException exception) when (exception.InnerException is AuthenticatedCursorSizeException size)
                {
                    return item with
                    {
                        Status = OperationBatchStatus.Omitted, Result = null,
                        Omission = new StructuredOperationOmission(
                            "networkObjectCursorExceededCharLimit", size.LimitChars, size.CursorChars, "network_read",
                            "Shorten the project path and start a new sequence, or narrow the request until it fits one terminal page."),
                    };
                }
            }
            return item with { Result = CanonicalJson.ToElement(payload) };
        }).ConfigureAwait(false);

        return pinned.Success ? pinned.Value! : Failed(operation,
            pinned.Failure!.FailureCategory == WorkerFailureCategories.BindingConflict
                ? WorkerFailureCategories.CursorBindingMismatch : pinned.Failure.FailureCategory!,
            pinned.Failure.Error!, pinned.Failure.Warnings);
    }

    private static bool IsCompleteIdentity(WorkerSessionIdentity? identity)
    {
        var path = ProjectPathNormalization.Canonicalize(identity?.ProjectPath);
        return identity is not null && !string.IsNullOrWhiteSpace(identity.WorkerSessionId)
            && identity.SessionGeneration >= 0 && identity.PortalProcessId is > 0
            && path is not null && string.Equals(path, identity.ProjectPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameIdentity(WorkerSessionIdentity? expected, WorkerSessionIdentity? actual)
        => expected is not null && actual is not null
            && !string.IsNullOrWhiteSpace(actual.WorkerSessionId)
            && actual.SessionGeneration >= 0 && actual.PortalProcessId is > 0
            && string.Equals(expected.WorkerSessionId, actual.WorkerSessionId, StringComparison.Ordinal)
            && expected.SessionGeneration == actual.SessionGeneration
            && expected.PortalProcessId == actual.PortalProcessId
            && CursorStateRules.SameProject(expected.ProjectPath, actual.ProjectPath);

    private static StructuredOperationItem Failed(NetworkOperationRequest operation, string category, string message,
        IReadOnlyList<string>? warnings = null)
        => HardwarePagePayloadContract.Failed(operation, category, message, warnings ?? Array.Empty<string>());
}
