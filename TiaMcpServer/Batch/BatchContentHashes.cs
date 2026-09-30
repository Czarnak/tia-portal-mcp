using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Batch;

/// <summary>
/// Tags successful content reads with a format-tagged hash of the exact served text, so a caller
/// can later pass it to a guarded write as an optimistic-concurrency precondition.
/// </summary>
public static class BatchContentHashes
{
    /// <summary>
    /// Returns <paramref name="results"/> with <c>ContentHash</c> set on every succeeded
    /// get_block_content / get_type_content read that returned exactly one object.
    /// </summary>
    public static IReadOnlyList<OperationBatchResult> Attach(
        IReadOnlyList<BatchOperationRequest> requests,
        IReadOnlyList<OperationBatchResult> results)
    {
        if (requests.Count != results.Count)
        {
            throw new ArgumentException(
                $"Request count ({requests.Count}) does not match result count ({results.Count}).",
                nameof(results));
        }

        var tagged = new OperationBatchResult[results.Count];
        for (var i = 0; i < results.Count; i++)
        {
            tagged[i] = HashOrNull(requests[i], results[i]) is { } hash
                ? results[i] with { ContentHash = hash }
                : results[i];
        }

        return tagged;
    }

    private static string? HashOrNull(BatchOperationRequest request, OperationBatchResult result)
    {
        var isContentRead = request.Operation is "get_block_content" or "get_type_content";
        if (!isContentRead
            || result.Status != OperationBatchStatus.Succeeded
            || result.Result is null
            || request.WithDependencies == true)
        {
            return null;
        }

        return ContentHashes.Compute(BatchWorkerInvoker.NormalizeFormat(request), result.Result);
    }
}
