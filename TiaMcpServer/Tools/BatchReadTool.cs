using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tools;

internal static class BatchReadTool
{
    internal static async Task<CallToolResult> RunAsync<TItem>(
        OpennessWorkerClient workerClient,
        IReadOnlyList<TItem>? operations,
        string toolName,
        string contractVersion,
        Func<IReadOnlyList<TItem>?, OperationValidation> validateRead,
        Func<IReadOnlyList<TItem>, McpAccessMode, IReadOnlyList<string>> validateAccessMode,
        Func<TItem, Task<StructuredOperationItem>> execute,
        Func<StructuredOperationItem, string> retryGuidance,
        CancellationToken cancellationToken = default)
        where TItem : IOperationBatchItem
    {
        var validation = validateRead(operations);
        if (!validation.IsValid)
        {
            return Error(toolName, contractVersion, validation.Category, validation.Error);
        }

        var accessErrors = validateAccessMode(operations!, workerClient.AccessPolicy.Mode);
        if (accessErrors.Count != 0)
        {
            return Error(toolName, contractVersion, WorkerFailureCategories.AccessDenied, string.Join("\n", accessErrors));
        }

        var binding = await workerClient.RequireVerifiedBindingAsync().ConfigureAwait(false);
        if (!binding.Success)
        {
            return Error(toolName, contractVersion, binding.FailureCategory!, binding.Error!);
        }

        var batch = await StructuredOperationBatchExecutionEngine.ExecuteReadsAsync(
            operations!, execute, cancellationToken).ConfigureAwait(false);
        var bounded = ApplyBudget(batch, toolName, contractVersion, retryGuidance);

        // A batch that ran is a successful MCP call even when items failed: isError means "could not run".
        return StructuredToolResult.Create(Compose(toolName, contractVersion, bounded), isError: false);
    }

    internal static StructuredOperationBatch ApplyBudget(
        StructuredOperationBatch batch,
        string toolName,
        string contractVersion,
        Func<StructuredOperationItem, string> retryGuidance,
        int? maxItemChars = null,
        int? maxDocumentChars = null)
        => StructuredOperationBatchPayloadBudget.Apply(
            batch, value => Compose(toolName, contractVersion, value),
            toolName, retryGuidance, maxItemChars, maxDocumentChars);

    private static OperationBatchReadResponse Compose(string toolName, string contractVersion, StructuredOperationBatch batch)
        => new(toolName, contractVersion, batch.IsFullySuccessful,
            Error: null, Warnings: Array.Empty<string>(), Batch: batch);

    internal static CallToolResult Error(string toolName, string contractVersion, string category, string message)
        => StructuredToolResult.Create(
            new OperationBatchReadResponse(toolName, contractVersion, false,
                new OperationBatchReadError(category, message), Warnings: Array.Empty<string>(), Batch: null),
            isError: true);
}
