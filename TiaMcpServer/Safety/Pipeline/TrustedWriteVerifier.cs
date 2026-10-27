using TiaMcpServer.OperationBatches;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Safety.Pipeline;

public sealed class TrustedWriteVerifier<TItem>(IReadOnlyList<TItem> items, IWriteVerificationPolicy<TItem> policy)
    where TItem : IOperationBatchItem
{
    public async Task<WriteVerification> VerifyAsync(StructuredOperationBatch batch)
    {
        var outcomes = batch.Operations.Where(o => o.Status is OperationBatchStatus.Succeeded or OperationBatchStatus.Failed)
            .ToDictionary(o => o.OperationId, StringComparer.Ordinal);
        var attempted = items.Where(i => outcomes.ContainsKey(i.OperationId)).ToArray();
        var results = new List<WriteOperationVerification>();
        for (var index = 0; index < attempted.Length; index++)
        {
            var item = attempted[index];
            var failure = outcomes[item.OperationId].Failure;
            if (failure is null && SupersededBy(attempted, outcomes, index) is { } later)
            {
                var evidence = outcomes[later.OperationId].Failure is not null && !policy.HasReadBack(later)
                    ? $"the later operation failed and may have changed the object; its final state is not verified. Inspect it with {policy.ReadTool} before retrying."
                    : "the final state is verified there.";
                results.Add(new(item.OperationId, true, WriteVerificationChecks.Superseded, null, null,
                    $"Operation '{item.OperationId}': superseded by later operation '{later.OperationId}' on the same object; {evidence}"));
            }
            else if (policy.HasReadBack(item))
            {
                // A failed write may still have committed: read back its observed state either way.
                results.Add(await policy.ReadBackAsync(item, failure).ConfigureAwait(false));
            }
            else if (failure is null)
            {
                results.Add(new(item.OperationId, true, WriteVerificationChecks.Trusted, null, null,
                    $"Operation '{item.OperationId}': the Openness call completed without rejection; nothing was read back."));
            }
        }

        return new WriteVerification(results.All(r => r.Success), results, null);
    }

    /// <summary>The latest later item that may have changed one of this item's objects, or null.</summary>
    private TItem? SupersededBy(IReadOnlyList<TItem> attempted,
        IReadOnlyDictionary<string, StructuredOperationItem> outcomes, int index)
    {
        var own = policy.ObjectKeys(attempted[index], asLater: false);
        return attempted.Skip(index + 1)
            .Where(later => outcomes[later.OperationId].Failure is not { } failure || !WorkerFailureCategories.IsPreconditionRefusal(failure.Category))
            .LastOrDefault(later => policy.ObjectKeys(later, asLater: true).Any(k => own.Any(o => o == k || o.StartsWith(k + "\n", StringComparison.Ordinal))));
    }

}
