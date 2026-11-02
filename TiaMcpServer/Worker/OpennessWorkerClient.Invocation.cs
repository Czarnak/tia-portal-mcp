using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TiaMcpServer.Diagnostics;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Worker;

internal static class WorkerTransportFailureGuidance
{
    public const string SafeReadTimeout =
        "The TIA Openness worker did not complete the read before the timeout. No project mutation was requested. The worker session was discarded; retrying the read is safe.";

    public const string SafeReadCrash =
        "The TIA Openness worker stopped before returning the read result. No project mutation was requested. The worker will restart on the next worker call; retrying the read is safe.";

    public const string StateAffectingTimeout =
        "The TIA Openness worker timed out before completion was confirmed. The project state may have changed. Inspect current state before retrying.";

    public const string StateAffectingCrash =
        "The TIA Openness worker stopped before completion was confirmed. The project state may have changed. Inspect current state before retrying.";

    public const string SessionSelectionTimeout =
        "The TIA Openness worker timed out during session selection. The Openness dialog may need an answer in TIA Portal. The previous binding was invalidated; bind_project can be retried.";

    internal static bool IsSafeRead(string? method)
    {
        if (string.IsNullOrWhiteSpace(method))
        {
            return false;
        }

        return OperationPolicyCatalog.GetCapability(method) is
            OperationCapability.Observe or
            OperationCapability.TemporaryExport;
    }

    internal static string TimeoutGuidance(string? method)
        => OperationPolicyCatalog.GetCapability(method ?? string.Empty) == OperationCapability.SessionSelection
            ? SessionSelectionTimeout : IsSafeRead(method) ? SafeReadTimeout : StateAffectingTimeout;

    internal static string CrashGuidance(string? method)
        => IsSafeRead(method) ? SafeReadCrash : StateAffectingCrash;
}

public partial class OpennessWorkerClient
{
    private async Task<WorkerCallResult> SendBoundProjectRequestAsync(
        string method,
        string? projectPath,
        Action<WorkerRequest> configure,
        string emptyPayload,
        BindingTransition transition = BindingTransition.None)
        => await ExecuteSerializedBindingOperationAsync(
            () => SendBoundProjectRequestCoreAsync(
                method,
                projectPath,
                configure,
                emptyPayload,
                transition)).ConfigureAwait(false);

    private async Task<WorkerCallResult> SendBoundProjectRequestCoreAsync(
        string method,
        string? projectPath,
        Action<WorkerRequest> configure,
        string emptyPayload,
        BindingTransition transition)
    {
        var denial = _accessPolicy.Authorize(method);
        if (denial is not null) return denial with { DispatchState = WorkerDispatchState.NotSent };

        var pinnedBinding = _bindingOperationContext.Value?.PinnedBinding;

        // A configured --project is an assertion until the worker proves the live PID/path/session.
        // Ground it with the one method that is allowed to start without ExpectedSessionIdentity
        // before any ordinary read or write is dispatched in read-write mode.
        if (pinnedBinding is null &&
            !string.Equals(method, "get_project_status", StringComparison.Ordinal) &&
            await GroundConfiguredProjectAsync().ConfigureAwait(false) is { } verification)
        {
            return method == "update_block_logic"
                ? verification with { DispatchState = WorkerDispatchState.NotSent }
                : verification;
        }

        if (!_projectSessionBinding.TryResolveWithSnapshot(
                projectPath,
                out var currentBinding,
                out var effectiveProjectPath,
                out var bindingError))
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.BindingConflict, bindingError!) with
            {
                DispatchState = WorkerDispatchState.NotSent
            };
        }

        var bindingBeforeCall = pinnedBinding ?? currentBinding;
        if (CheckPinnedBinding(pinnedBinding, currentBinding) is { } failure)
        {
            return failure with
            {
                DispatchState = WorkerDispatchState.NotSent
            };
        }

        // Evaluated after configured-project grounding, so a startup --project that just verified
        // passes; a pinned unbound lease (open/create) is not exempt.
        if (!bindingBeforeCall.IsVerified && ProjectCapabilityCatalog.RequiresBoundProject(method))
        {
            return WorkerCallResult.Fail(
                WorkerFailureCategories.BindingConflict,
                ProjectSessionBinding.BindFirstMessage) with
            {
                DispatchState = WorkerDispatchState.NotSent
            };
        }

        var request = new WorkerRequest
        {
            Method = method,
            ProjectPath = effectiveProjectPath,
            ExpectedSessionIdentity = bindingBeforeCall.ToWorkerIdentity()
        };
        configure(request);

        var result = await InvokeWorkerAsync(request).ConfigureAwait(false);
        if (result.Success)
        {
            // The only helper caller that binds is save_project_as(rebind:true), which force-rebinds
            // onto the copy by design; None/Clear ignore bindForceRebind.
            result = ApplyBindingTransition(
                transition,
                result,
                bindingBeforeCall,
                bindForceRebind: true);
        }

        return result.Success && string.IsNullOrEmpty(result.Payload)
            ? result with { Payload = emptyPayload }
            : result;
    }

    private static IReadOnlyList<string> AppendWarning(IReadOnlyList<string> warnings, string warning)
    {
        // Route the appended line back through CapWarnings: the incoming warnings may already sit
        // at the 20-line cap (a degraded read), so a bare append would push the surfaced list past
        // the cap with no truncation marker. CapWarnings is the single capping authority and is
        // idempotent for already-capped, short lines.
        var combined = new List<string>(warnings.Count + 1);
        combined.AddRange(warnings);
        combined.Add(warning);
        return CapWarnings(combined);
    }

    private async Task<WorkerCallResult> InvokeWorkerAsync(WorkerRequest request)
    {
        // Defense in depth: authorize BEFORE the worker process is started, before any
        // request is written to stdin, and before TIA Portal is connected.
        var denial = _accessPolicy.Authorize(request.Method);
        if (denial is not null)
        {
            return denial with { DispatchState = WorkerDispatchState.NotSent };
        }

        var capabilityOperation = ProjectCapabilityCatalog.OperationFor(request.Method);
        var verified = BindingSnapshot;
        if (capabilityOperation is not null && verified.IsVerified && verified.Context is { } projectContext
            && !ProjectCapabilityCatalog.Supports(projectContext.ContainerKind, capabilityOperation))
            return WorkerCallResult.Fail(WorkerFailureCategories.UnsupportedCapability,
                $"{capabilityOperation} is unavailable for this project container.")
                with { DispatchState = WorkerDispatchState.NotSent };

        try
        {
            // Exactly one transport request per call: SendAsync neither loops nor retries: on any
            // failure below the transport already killed/will-recreate its process on the NEXT
            // call, and this method never re-invokes SendAsync for the request that just failed.
            var response = await GetOrCreateTransport().SendAsync(request).ConfigureAwait(false);
            var warnings = CapWarnings(response.Warnings);
            foreach (var warning in warnings)
            {
                _logger?.LogWarning("TIA Openness worker warning: {Line}", warning);
            }

            if (response.Success)
            {
                return WorkerCallResult.Ok(response.Payload ?? string.Empty, warnings) with
                {
                    ResolvedProjectPath = response.ResolvedProjectPath,
                    SessionIdentity = response.SessionIdentity,
                    PortalProcessId = response.PortalProcessId,
                    ConfigureNetworkDeviceOutcome = response.ConfigureNetworkDeviceOutcome,
                    HmiWriteOutcome = response.HmiWriteOutcome,
                    VciWriteOutcome = response.VciWriteOutcome,
                    BlockImportOutcome = response.BlockImportOutcome,
                    DispatchState = WorkerDispatchState.Sent
                };
            }

            var failureCategory = WorkerFailureCategories.IsKnown(response.FailureCategory)
                ? response.FailureCategory!
                : WorkerFailureCategories.WorkerOperationFailed;
            var failure = WorkerCallResult.Fail(
                failureCategory,
                response.Error ?? "The TIA Openness worker failed without an error message.",
                warnings) with
            {
                ResolvedProjectPath = response.ResolvedProjectPath,
                SessionIdentity = response.SessionIdentity,
                PortalProcessId = response.PortalProcessId,
                EngineeringError = response.EngineeringError,
                ConfigureNetworkDeviceOutcome = response.ConfigureNetworkDeviceOutcome,
                HmiWriteOutcome = response.HmiWriteOutcome,
                VciWriteOutcome = response.VciWriteOutcome,
                BlockImportOutcome = response.BlockImportOutcome,
                DispatchState = WorkerDispatchState.Sent
            };
            if ((failureCategory is WorkerFailureCategories.BindingConflict or WorkerFailureCategories.PortalLost)
                && _projectSessionBinding.IsVerified)
            {
                _projectSessionBinding.Invalidate(failure.Error ?? "worker rejected the expected session identity");
            }

            return failure;
        }
        catch (Win32Exception ex)
        {
            // The worker process never started (e.g. missing/invalid executable): a distinct,
            // more actionable failure than a mid-request crash, so it keeps its own message and
            // the generic worker-operation-failed category rather than worker_crashed.
            InvalidateVerifiedBinding("the Openness worker could not be started, so its prior session no longer exists");
            return WorkerCallResult.Fail(
                WorkerFailureCategories.WorkerOperationFailed,
                $"Failed to launch the TIA Openness worker process ({ex.Message}). "
                + "Verify that .NET Framework 4.8 is installed and that the 'openness-worker' folder "
                + "beside the MCP server executable is complete; rebuild or reinstall if files are missing.") with
            {
                DispatchState = WorkerDispatchState.NotSent
            };
        }
        catch (TimeoutException)
        {
            InvalidateVerifiedBinding("the Openness worker timed out and its session outcome is unknown");
            return WorkerCallResult.Fail(
                WorkerFailureCategories.WorkerTimeout,
                WorkerTransportFailureGuidance.TimeoutGuidance(request.Method)) with
            {
                DispatchState = WorkerDispatchState.Unknown
            };
        }
        catch (PersistentWorkerTransport.WorkerProtocolMismatchException ex)
        {
            InvalidateVerifiedBinding("the Openness worker protocol is incompatible with this host");
            return WorkerCallResult.Fail(
                WorkerFailureCategories.ProtocolError,
                ex.Message) with
            {
                DispatchState = WorkerDispatchState.NotSent
            };
        }
        catch (PersistentWorkerTransport.WorkerTransportDisposedException)
        {
            // Shutdown rejected the request before it was written: the session is untouched.
            return WorkerCallResult.Fail(
                WorkerFailureCategories.WorkerOperationFailed,
                "The MCP server is shutting down; the request was not sent to the TIA Openness worker.") with
            {
                DispatchState = WorkerDispatchState.NotSent
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or JsonException)
        {
            // Broken pipe (IOException), a crashed/null response or a failed process launch
            // (InvalidOperationException), or malformed/protocol-desynced JSON (JsonException) —
            // all mean the worker cannot be trusted to have completed the request as sent.
            InvalidateVerifiedBinding("the Openness worker crashed or its protocol stream was lost");
            return WorkerCallResult.Fail(
                WorkerFailureCategories.WorkerCrashed,
                WorkerTransportFailureGuidance.CrashGuidance(request.Method)) with
            {
                DispatchState = WorkerDispatchState.Unknown
            };
        }
    }

    private PersistentWorkerTransport GetOrCreateTransport()
    {
        lock (_transportLock)
        {
            if (_disposed)
            {
                throw new PersistentWorkerTransport.WorkerTransportDisposedException();
            }

            var workerArgs = $"--access-mode {McpAccessModeNames.ToName(_accessPolicy.Mode)}";
            _transport ??= new PersistentWorkerTransport(
                _workerExecutablePathOverride ?? LocateWorkerExecutable(),
                _requestTimeout,
                _logger,
                workerArgs);
            return _transport;
        }
    }

    public void Dispose()
    {
        lock (_transportLock)
        {
            _disposed = true;
            _transport?.Dispose();
            _transport = null;
        }
    }

    // A degraded read of a large project can emit hundreds of "Skipping X" lines; cap what
    // reaches the agent so warnings cannot flood a small model's context.
    private const int MaxWarningLines = 20;
    private const int MaxWarningLineChars = 1_000;
    private const string WarningTruncationTrailer = " [TRUNCATED]";

    private static IReadOnlyList<string> CapWarnings(IReadOnlyList<string>? warnings)
    {
        if (warnings is null || warnings.Count == 0)
        {
            return Array.Empty<string>();
        }

        var lines = warnings
            .Select(line => CapWarningLine(line.Trim()))
            .Where(line => line.Length > 0)
            .ToList();

        if (lines.Count > MaxWarningLines)
        {
            var dropped = lines.Count - MaxWarningLines;
            lines = lines.Take(MaxWarningLines).ToList();
            lines.Add($"(+{dropped} more worker warnings truncated)");
        }

        return lines;
    }

    private static string CapWarningLine(string line)
    {
        if (line.Length <= MaxWarningLineChars)
        {
            return line;
        }

        return line.Substring(0, MaxWarningLineChars - WarningTruncationTrailer.Length)
            + WarningTruncationTrailer;
    }

    private static string LocateWorkerExecutable()
        => OpennessWorkerLocator.LocateOrThrow(AppContext.BaseDirectory, FileSystemService.Instance);
}
