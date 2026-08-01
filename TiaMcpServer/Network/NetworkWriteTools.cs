using System.ComponentModel;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Network;

[McpServerToolType]
public class NetworkWriteTools
{
    private const string ToolName = "network_write";

    [McpServerTool(
        Name = ToolName,
        ReadOnly = false,
        Destructive = true,
        OpenWorld = false)]
    [Description("Preview or apply up to 50 dedicated network write operations. Valid operations: add_network_device (typeIdentifier, deviceName) and configure_network_device (deviceName). Call without confirm or safetyToken to receive a preview, then call the same network_write tool with the identical ordered operations, confirm=true, and the returned safetyToken. Writes stop on first failure; later items are skipped and no rollback is performed.")]
    public static async Task<string> NetworkWrite(
        OpennessWorkerClient workerClient,
        WriteSafetyService safety,
        [Description("Ordered list of dedicated network write operations. All items must target the same project path, and apply must use the exact list passed for preview.")] NetworkOperationRequest[] operations,
        [Description("Leave false for preview. Set true only after reviewing the preview and supplying its safetyToken.")] bool confirm = false,
        [Description("Safety token returned by the preview from this same network_write tool. Omit during preview and supply unchanged during apply.")] string? safetyToken = null)
    {
        var validation = NetworkOperationCatalog.ValidateWrite(operations);
        if (!validation.IsValid)
        {
            return OperationBatchResultFormatter.Error(ToolName, validation.Error);
        }

        var mode = workerClient.AccessPolicy?.Mode ?? McpAccessMode.ReadWrite;
        var accessErrors = NetworkOperationCatalog.ValidateAccessMode(operations, mode);
        if (accessErrors.Count != 0)
        {
            return OperationBatchResultFormatter.Error(
                ToolName,
                string.Join("\n", accessErrors));
        }

        if (!confirm && !string.IsNullOrWhiteSpace(safetyToken))
        {
            return OperationBatchResultFormatter.Error(
                ToolName,
                "A safetyToken cannot be supplied with confirm=false. Omit the token to request a new preview.");
        }

        if (confirm && string.IsNullOrWhiteSpace(safetyToken))
        {
            return OperationBatchResultFormatter.Error(
                ToolName,
                "Safety token required. Call network_write without confirm to preview the exact operations first, then retry with confirm=true and its safetyToken.");
        }

        var projectPath = NetworkSafetySnapshot.ResolveProjectPath(operations);
        var targets = NetworkSafetySnapshot.BuildTargets(operations);

        if (!confirm)
        {
            var state = await NetworkSafetySnapshot.ReadCurrentStateAsync(workerClient, projectPath)
                .ConfigureAwait(false);
            if (!state.Success)
            {
                return OperationBatchResultFormatter.Error(
                    ToolName,
                    $"Could not read current hardware state before preview. Error: {state.Error}");
            }

            var summary = $"Apply {operations.Length} network write operation(s) sequentially; stops on first failure (no rollback).";
            return safety.CreatePreview(
                ToolName,
                projectPath,
                targets,
                summary,
                operations,
                state.Payload,
                diff: null,
                instructions: "Preview only — nothing was changed. To apply, call network_write with the identical operations list, confirm=true, and this safetyToken.");
        }

        var envelope = safety.ValidateEnvelope(
            safetyToken,
            ToolName,
            projectPath,
            targets,
            operations,
            ToolName);
        if (!envelope.IsValid)
        {
            return OperationBatchResultFormatter.Error(ToolName, envelope.Error);
        }

        var freshState = await NetworkSafetySnapshot.ReadCurrentStateAsync(workerClient, projectPath)
            .ConfigureAwait(false);
        if (!freshState.Success)
        {
            return OperationBatchResultFormatter.Error(
                ToolName,
                $"Could not read current hardware state before write. Error: {freshState.Error}");
        }

        var tokenValidation = safety.ValidateAndConsume(
            safetyToken,
            ToolName,
            projectPath,
            targets,
            operations,
            freshState.Payload,
            ToolName);
        if (!tokenValidation.IsValid)
        {
            return OperationBatchResultFormatter.Error(ToolName, tokenValidation.Error);
        }

        var results = await OperationBatchExecutionEngine.ApplyWritesAsync(
            operations,
            operation => NetworkWorkerInvoker.InvokeWriteAsync(workerClient, operation, projectPath))
            .ConfigureAwait(false);
        var resultJson = OperationBatchResultFormatter.Apply(ToolName, results);
        safety.AppendAudit(ToolName, projectPath, targets, operations, freshState.Payload, resultJson);
        return resultJson;
    }
}
