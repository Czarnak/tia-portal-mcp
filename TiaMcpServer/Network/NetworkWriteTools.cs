using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Network;

[McpServerToolType]
public class NetworkWriteTools
{
    private const string ToolName = "network_write";

    /// <summary>Tool an agent should call to recover an omitted write result. Deliberately the READ
    /// tool: re-running a write to see what it returned would perform the write a second time.</summary>
    private const string EvidenceTool = "network_read";

    private const string PartialWriteWarning =
        "This network_write call stopped here. This operation and any earlier operation in the same "
        + "call may already have changed TIA state; no batch-wide rollback was attempted. Re-read the "
        + "hardware configuration with network_read before retrying.";

    [McpServerTool(
        Name = ToolName,
        ReadOnly = false,
        Destructive = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(NetworkWriteResponse))]
    [Description("Preview or apply up to 50 dedicated network write operations. Valid operations: add_network_device (typeIdentifier, deviceName), configure_network_device (target, changes), create_subnet (subnet), update_subnet (target, subnetChanges), and delete_subnet (target). configure_network_device names an existing node exactly — target.deviceName plus the target.nodeId reported by network_read — and requests at least one change; an omitted change member means leave it unchanged. create_subnet defines a new Ethernet or PROFIBUS subnet from subnet.name and subnet.networkType. update_subnet and delete_subnet name an existing subnet exactly by target.subnetId; update_subnet requests at least one change in subnetChanges. Deleting a subnet that still has connected nodes is allowed and does not delete any device; networkDeviceCountUnchanged reports whether the project's root device count stayed the same (devices nested in device user groups are outside that count). network_write never saves the project or compiles it — call save_project and compile_check separately when persistence or a build check is needed. Call without confirm or safetyToken to receive a preview, then call the same network_write tool with the identical ordered operations, confirm=true, and the returned safetyToken. The response is a discriminated envelope whose phase field is preview, apply, or error. Writes stop on first failure; later items are skipped. No batch-wide rollback is attempted: the failed operation and every earlier operation in the same call may already have changed TIA state, and the caller must re-read the current state with network_read before retrying.")]
    public static async Task<CallToolResult> NetworkWrite(
        OpennessWorkerClient workerClient,
        WriteSafetyService safety,
        [Description("Ordered list of dedicated network write operations. All items must target the same project path, and apply must use the exact list passed for preview.")] NetworkOperationRequest[] operations,
        [Description("Leave false for preview. Set true only after reviewing the preview and supplying its safetyToken.")] bool confirm = false,
        [Description("Safety token returned by the preview from this same network_write tool. Omit during preview and supply unchanged during apply.")] string? safetyToken = null)
    {
        var validation = NetworkOperationCatalog.ValidateWrite(operations);
        if (!validation.IsValid)
        {
            return Error(WorkerFailureCategories.ValidationError, validation.Error);
        }

        var mode = workerClient.AccessPolicy?.Mode ?? McpAccessMode.ReadWrite;
        var accessErrors = NetworkOperationCatalog.ValidateAccessMode(operations, mode);
        if (accessErrors.Count != 0)
        {
            return Error(WorkerFailureCategories.AccessDenied, string.Join("\n", accessErrors));
        }

        if (!confirm && !string.IsNullOrWhiteSpace(safetyToken))
        {
            return Error(
                WorkerFailureCategories.ValidationError,
                "A safetyToken cannot be supplied with confirm=false. Omit the token to request a new preview.");
        }

        if (confirm && string.IsNullOrWhiteSpace(safetyToken))
        {
            return Error(
                WorkerFailureCategories.ValidationError,
                "Safety token required. Call network_write without confirm to preview the exact operations first, then retry with confirm=true and its safetyToken.");
        }

        var projectPath = NetworkSafetySnapshot.ResolveProjectPath(operations);
        if (confirm)
        {
            return await ApplyAsync(workerClient, safety, operations, safetyToken, projectPath)
                .ConfigureAwait(false);
        }

        var bindingGate = await workerClient.RequireVerifiedWriteBindingAsync(projectPath).ConfigureAwait(false);
        if (!bindingGate.Success)
        {
            return Error(
                bindingGate.FailureCategory ?? WorkerFailureCategories.BindingConflict,
                bindingGate.Error ?? "A verified project binding is required.");
        }

        var previewExecution = await workerClient.ExecuteWithPinnedBindingAsync(
            workerClient.BindingSnapshot,
            () => PreviewAsync(workerClient, safety, operations, projectPath)).ConfigureAwait(false);
        return previewExecution.Success
            ? previewExecution.Value!
            : Error(
                previewExecution.Failure!.FailureCategory ?? WorkerFailureCategories.BindingConflict,
                previewExecution.Failure.Error ?? "The project binding changed before the network preview could run.");
    }

    private static async Task<CallToolResult> PreviewAsync(
        OpennessWorkerClient workerClient,
        WriteSafetyService safety,
        NetworkOperationRequest[] operations,
        string? projectPath)
    {
        var state = await NetworkSafetySnapshot.ReadCurrentStateAsync(workerClient, projectPath)
            .ConfigureAwait(false);
        if (!state.Success)
        {
            return Error(
                state.FailureCategory!,
                $"Could not read current hardware state before preview. Error: {state.Error}");
        }

        // Resolved against THIS snapshot: node/subnet/IO-system identities are exact matches against
        // the hardware configuration just read, never a first-match or name-only guess.
        var resolution = NetworkSafetySnapshot.BuildTargets(operations, state.State);
        if (!resolution.Success)
        {
            return Error(resolution.FailureCategory!, resolution.Error!);
        }

        var canonical = safety.CreateCanonicalPreview(
            ToolName,
            projectPath,
            resolution.Targets!,
            $"Apply {operations.Length} network write operation(s) sequentially; stops on first failure (no batch-wide rollback).",
            operations,
            state.State!,
            "Preview only — nothing was changed. To apply, call network_write with the identical operations list, confirm=true, and this safetyToken.");

        return StructuredToolResult.Create(
            new NetworkWriteResponse(
                ToolName,
                NetworkWritePhases.Preview,
                Success: true,
                new NetworkWritePreview(
                    canonical.Target,
                    canonical.Summary,
                    canonical.CurrentStateHash,
                    canonical.RequestedInputHash,
                    canonical.ExpiresAtUtc,
                    canonical.SafetyToken,
                    canonical.ProjectBinding,
                    canonical.Diff,
                    canonical.Instructions),
                Batch: null,
                Error: null),
            isError: false);
    }

    private static async Task<CallToolResult> ApplyAsync(
        OpennessWorkerClient workerClient,
        WriteSafetyService safety,
        NetworkOperationRequest[] operations,
        string? safetyToken,
        string? projectPath)
    {
        // Some targets can only be reconstructed from the fresh hardware snapshot. Validate every
        // token field available now (including the exact worker/Portal/project binding), then use
        // that binding to serialize state read, full target validation, consumption, and mutation.
        var leaseEnvelope = safety.ValidateLeaseEnvelope(
            safetyToken,
            ToolName,
            ToolName);
        if (!leaseEnvelope.IsValid)
        {
            return Error(
                leaseEnvelope.FailureCategory ?? WorkerFailureCategories.ValidationError,
                leaseEnvelope.Error);
        }

        // Cheap pre-check first, when it is possible: a batch of creation operations resolves its
        // target evidence from the request alone, so a dead token is rejected before the expensive
        // state read. A batch containing configure_network_device cannot resolve its evidence
        // without a hardware snapshot (the identities live in it), so it skips straight to the
        // fresh-state read below and is checked in one step by ValidateAndConsumeCanonical instead.
        if (!NetworkSafetySnapshot.RequiresHardwareState(operations))
        {
            var staticResolution = NetworkSafetySnapshot.BuildTargets(operations, state: null);
            if (!staticResolution.Success)
            {
                return Error(staticResolution.FailureCategory!, staticResolution.Error!);
            }

            var envelope = safety.ValidateCanonicalEnvelope(
                safetyToken, ToolName, projectPath, staticResolution.Targets!, operations, ToolName);
            if (!envelope.IsValid)
            {
                return Error(envelope.FailureCategory ?? WorkerFailureCategories.ValidationError, envelope.Error);
            }
        }

        var execution = await workerClient.ExecuteWithPinnedBindingAsync(
            leaseEnvelope.ProjectBinding,
            async () =>
            {
                var bindingGate = await workerClient.RequireVerifiedWriteBindingAsync(projectPath).ConfigureAwait(false);
                if (!bindingGate.Success)
                {
                    return Error(
                        bindingGate.FailureCategory ?? WorkerFailureCategories.BindingConflict,
                        bindingGate.Error ?? "A verified project binding is required.");
                }

                var freshState = await NetworkSafetySnapshot.ReadCurrentStateAsync(workerClient, projectPath)
                    .ConfigureAwait(false);
                if (!freshState.Success)
                {
                    return Error(
                        freshState.FailureCategory!,
                        $"Could not read current hardware state before write. Error: {freshState.Error}");
                }

                // Resolve again against the fresh snapshot inside the same critical section as the
                // write. A concurrent apply cannot validate against the state this one is about to
                // change and then run later with stale evidence.
                var resolution = NetworkSafetySnapshot.BuildTargets(operations, freshState.State);
                if (!resolution.Success)
                {
                    return Error(resolution.FailureCategory!, resolution.Error!);
                }

                var tokenValidation = safety.ValidateAndConsumeCanonical(
                    safetyToken, ToolName, projectPath, resolution.Targets!, operations, freshState.State!, ToolName);
                if (!tokenValidation.IsValid)
                {
                    return Error(
                        tokenValidation.FailureCategory ?? WorkerFailureCategories.ValidationError,
                        tokenValidation.Error);
                }

                var batch = await StructuredOperationBatchExecutionEngine.ApplyWritesAsync(
                    operations,
                    operation => NetworkWorkerInvoker.InvokeWriteAsync(workerClient, operation, projectPath),
                    NetworkPayloadContract.Project).ConfigureAwait(false);

                var response = Compose(ApplyBudget(WarnAboutPartialWrite(batch)));

                // The audit entry carries the exact document the caller received and is appended
                // before the binding lease is released.
                safety.AppendCanonicalAudit(
                    ToolName,
                    projectPath,
                    resolution.Targets!,
                    operations,
                    freshState.State!,
                    response);

                return StructuredToolResult.Create(response, isError: false);
            }).ConfigureAwait(false);
        if (!execution.Success)
        {
            return Error(
                execution.Failure!.FailureCategory ?? WorkerFailureCategories.BindingConflict,
                execution.Failure.Error ?? "The project binding changed before the network write could run.");
        }

        return execution.Value!;
    }

    /// <summary>
    /// Attaches the partial-write warning to the operation that stopped the batch. Applying it to
    /// the failed item rather than the batch keeps it next to the evidence an agent reads when
    /// working out what state the project is now in.
    /// </summary>
    private static StructuredOperationBatch WarnAboutPartialWrite(StructuredOperationBatch batch)
    {
        var items = batch.Operations.ToList();
        var index = items.FindIndex(
            item => string.Equals(item.Status, OperationBatchStatus.Failed, StringComparison.Ordinal));
        if (index < 0)
        {
            return batch;
        }

        items[index] = items[index] with
        {
            Warnings = items[index].Warnings.Append(PartialWriteWarning).ToArray(),
        };

        return StructuredOperationBatch.FromItems(items, batch.Truncation);
    }

    /// <summary>
    /// Bounds a batch against the exact <c>network_write</c> response document. Internal so tests
    /// can drive the real wiring at small limits.
    /// </summary>
    internal static StructuredOperationBatch ApplyBudget(
        StructuredOperationBatch batch,
        int maxItemChars = StructuredOperationBatchPayloadBudget.MaxItemChars,
        int maxDocumentChars = StructuredOperationBatchPayloadBudget.MaxDocumentChars)
        => StructuredOperationBatchPayloadBudget.Apply(
            batch,
            Compose,
            EvidenceTool,
            RetryGuidance,
            maxItemChars,
            maxDocumentChars);

    private static NetworkWriteResponse Compose(StructuredOperationBatch batch)
        => new(
            ToolName,
            NetworkWritePhases.Apply,
            batch.IsFullySuccessful,
            Preview: null,
            batch,
            Error: null);

    private static string RetryGuidance(StructuredOperationItem item)
        => "The write was performed; only its result was too large to return. Do not re-run this "
            + $"operation — read the current hardware configuration with {EvidenceTool} to confirm "
            + "the outcome.";

    private static CallToolResult Error(string category, string message)
        => StructuredToolResult.Create(
            new NetworkWriteResponse(
                ToolName,
                NetworkWritePhases.Error,
                Success: false,
                Preview: null,
                Batch: null,
                new NetworkToolError(category, message)),
            isError: true);
}
