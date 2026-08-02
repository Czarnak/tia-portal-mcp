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
        + "call may already have changed TIA state, and no rollback was attempted. Re-read the "
        + "hardware configuration with network_read before retrying.";

    [McpServerTool(
        Name = ToolName,
        ReadOnly = false,
        Destructive = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(NetworkWriteResponse))]
    [Description("Preview or apply up to 50 dedicated network write operations. Valid operations: add_network_device (typeIdentifier, deviceName) and configure_network_device (target, changes). configure_network_device names an existing node exactly — target.deviceName plus the target.nodeId reported by network_read — and requests at least one change; an omitted change member means leave it unchanged. Call without confirm or safetyToken to receive a preview, then call the same network_write tool with the identical ordered operations, confirm=true, and the returned safetyToken. The response is a discriminated envelope whose phase field is preview, apply, or error. Writes stop on first failure; later items are skipped and no rollback is performed.")]
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
        var targets = NetworkSafetySnapshot.BuildTargets(operations);

        return confirm
            ? await ApplyAsync(workerClient, safety, operations, safetyToken, projectPath, targets)
                .ConfigureAwait(false)
            : await PreviewAsync(workerClient, safety, operations, projectPath, targets)
                .ConfigureAwait(false);
    }

    private static async Task<CallToolResult> PreviewAsync(
        OpennessWorkerClient workerClient,
        WriteSafetyService safety,
        NetworkOperationRequest[] operations,
        string? projectPath,
        IReadOnlyList<NetworkWriteTargetEvidence> targets)
    {
        var state = await NetworkSafetySnapshot.ReadCurrentStateAsync(workerClient, projectPath)
            .ConfigureAwait(false);
        if (!state.Success)
        {
            return Error(
                state.FailureCategory!,
                $"Could not read current hardware state before preview. Error: {state.Error}");
        }

        var canonical = safety.CreateCanonicalPreview(
            ToolName,
            projectPath,
            targets,
            $"Apply {operations.Length} network write operation(s) sequentially; stops on first failure (no rollback).",
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
        string? projectPath,
        IReadOnlyList<NetworkWriteTargetEvidence> targets)
    {
        // Cheap pre-check first: a dead token must be rejected before the expensive state read.
        var envelope = safety.ValidateCanonicalEnvelope(
            safetyToken, ToolName, projectPath, targets, operations, ToolName);
        if (!envelope.IsValid)
        {
            return Error(envelope.FailureCategory ?? WorkerFailureCategories.ValidationError, envelope.Error);
        }

        var freshState = await NetworkSafetySnapshot.ReadCurrentStateAsync(workerClient, projectPath)
            .ConfigureAwait(false);
        if (!freshState.Success)
        {
            return Error(
                freshState.FailureCategory!,
                $"Could not read current hardware state before write. Error: {freshState.Error}");
        }

        var tokenValidation = safety.ValidateAndConsumeCanonical(
            safetyToken, ToolName, projectPath, targets, operations, freshState.State!, ToolName);
        if (!tokenValidation.IsValid)
        {
            return Error(
                tokenValidation.FailureCategory ?? WorkerFailureCategories.ValidationError,
                tokenValidation.Error);
        }

        var batch = await StructuredOperationBatchExecutionEngine.ApplyWritesAsync(
            operations,
            operation => NetworkWorkerInvoker.InvokeWriteAsync(workerClient, operation, projectPath),
            NetworkPayloadContract.Project)
            .ConfigureAwait(false);

        var response = Compose(ApplyBudget(WarnAboutPartialWrite(batch)));

        // The audit entry carries the exact document the caller received — not a re-rendering of it.
        safety.AppendCanonicalAudit(ToolName, projectPath, targets, operations, freshState.State!, response);

        // The batch RAN, so this is a successful MCP call even when an item inside it failed:
        // isError stays reserved for "the tool could not run".
        return StructuredToolResult.Create(response, isError: false);
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
