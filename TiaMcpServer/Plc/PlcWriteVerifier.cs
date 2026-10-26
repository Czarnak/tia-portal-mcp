using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

/// <summary>
/// One post-write check of one attempted item. <see cref="Check"/> is <c>contentHash</c> (a content
/// item reports its new <see cref="ContentHash"/>), <c>trusted</c> (the Openness call completed
/// without rejection; nothing was read back) or <c>superseded</c>.
/// </summary>
public sealed record PlcOperationVerification(string OperationId, bool Success, string Check,
    string? Expected, string? Actual, string? ContentHash, string? Message);

public sealed record PlcWriteVerification(bool Success, IReadOnlyList<PlcOperationVerification> Operations,
    StructuredOperationOmission? Omission);

/// <summary>
/// Verifies the attempted plc_write items. Only content is read back: one re-export per
/// <c>update_block_logic</c>/<c>update_type_content</c> item, whose hash TIA normalizes and so cannot
/// be predicted. Every other succeeded item is <c>trusted</c>: a non-throwing Openness call already
/// proved it, so no inventory or tree is re-read. Only the last effective item on an object asserts
/// the final state; earlier items on it pass as superseded. A failed content item reports its
/// observed hash; a failed item of any other kind has nothing to verify and is omitted.
/// </summary>
public sealed class PlcWriteVerifier(OpennessWorkerClient client, IReadOnlyList<PlcOperationRequest> items,
    IReadOnlyDictionary<string, PlcWriteEffect> effects)
{
    public async Task<PlcWriteVerification> VerifyAsync(StructuredOperationBatch batch)
    {
        var outcomes = batch.Operations.Where(o => o.Status is OperationBatchStatus.Succeeded or OperationBatchStatus.Failed)
            .ToDictionary(o => o.OperationId, StringComparer.Ordinal);
        var attempted = items.Where(i => outcomes.ContainsKey(i.OperationId)).ToArray();
        var results = new List<PlcOperationVerification>();
        for (var index = 0; index < attempted.Length; index++)
        {
            var item = attempted[index];
            var failure = outcomes[item.OperationId].Failure;
            if (failure is null && SupersededBy(attempted, outcomes, index) is { } later)
            {
                results.Add(new(item.OperationId, true, "superseded", null, null, null,
                    $"Operation '{item.OperationId}': superseded by later operation '{later}' on the same object; the final state is verified there."));
            }
            else if (item.Operation is "update_block_logic" or "update_type_content")
            {
                // A failed import may still have committed: its observed hash is reported either way.
                results.Add(await VerifyContentAsync(item, failure).ConfigureAwait(false));
            }
            else if (failure is null)
            {
                results.Add(new(item.OperationId, true, "trusted", null, null, null,
                    $"Operation '{item.OperationId}': the Openness call completed without rejection; nothing was read back."));
            }
        }

        return new PlcWriteVerification(results.All(r => r.Success), results, null);
    }

    /// <summary>The latest later item that may have changed one of this item's objects, or null.</summary>
    private string? SupersededBy(IReadOnlyList<PlcOperationRequest> attempted,
        IReadOnlyDictionary<string, StructuredOperationItem> outcomes, int index)
    {
        var own = Keys(attempted[index], asLater: false);
        return attempted.Skip(index + 1)
            .Where(later => outcomes[later.OperationId].Failure is not { } failure || !WorkerFailureCategories.IsPreconditionRefusal(failure.Category))
            .LastOrDefault(later => Keys(later, asLater: true).Any(k => own.Any(o => o == k || o.StartsWith(k + "\n", StringComparison.Ordinal))))
            ?.OperationId;
    }

    /// <summary>Hierarchical object keys: a container's key prefixes its members' keys.</summary>
    private IReadOnlyList<string> Keys(PlcOperationRequest item, bool asLater)
    {
        var target = effects[item.OperationId].Target;
        var scope = $"{target.DeviceName}\n{target.PlcName}\n";
        var table = $"{scope}table\n{target.FolderPath}\n{target.TableName}";
        var keys = target.Kind switch
        {
            PlcNameRules.Tag or PlcNameRules.UserConstant => new[] { $"{table}\n{target.Name}", $"{table}\n{item.NewName ?? target.Name}" },
            PlcNameRules.TagTable => new[] { table },
            "Type" => new[] { $"{scope}type\n{target.TypePath}" },
            _ => BlockKeys(item, target, scope, asLater),
        };
        return keys.Select(k => k.ToUpperInvariant()).Distinct().ToArray();
    }

    /// <summary>
    /// A block's path key. An undetermined <c>PLC/Name</c> content target (never resolved through the
    /// tree) also owns a name key and a PLC-wide group-delete key, and every later block item offers
    /// its name and every later group delete offers that key: a later item on the same block name, or
    /// any later group delete on the PLC (the target's folder is unknown), supersedes it. Canonical
    /// targets never own those keys, so their matching stays path-exact.
    /// </summary>
    private static string[] BlockKeys(PlcOperationRequest item, PlcTargetIdentity target, string scope, bool asLater)
    {
        var path = target.BlockPath ?? item.BlockPath ?? "";
        var keys = new List<string> { $"{scope}block\n" + path.Replace('/', '\n') };
        // PlcWorkingState.Unresolved builds the 2-segment "PLC/Name" target from the caller's path.
        var undetermined = target.Kind == PlcNameRules.Block && path.Split('/').Length == 2;
        if (asLater ? target.Kind == PlcNameRules.Block : undetermined) keys.Add($"{scope}blockname\n{target.Name}");
        if (asLater ? item.Operation == "delete_block_group" : undetermined) keys.Add($"{scope}groupdelete");
        return keys.ToArray();
    }

    private async Task<PlcOperationVerification> VerifyContentAsync(PlcOperationRequest item, StructuredOperationFailure? failure)
    {
        // The caller's own path: the canonical path can be ambiguous across PLCs.
        var format = PlcFormatNames.Normalize(item.Operation, item.Format);
        var export = item.Operation == "update_block_logic"
            ? await client.GetBlockContentAsync(item.BlockPath!, format: format).ConfigureAwait(false)
            : await client.GetTypeContentAsync(item.TypePath!, format: format).ConfigureAwait(false);
        if (!export.Success || string.IsNullOrEmpty(export.Payload))
            return Fail(item, $"Operation '{item.OperationId}': the written content could not be re-exported ({(export.Success ? "the export was empty" : export.Error)}).");
        var hash = ContentHashes.Compute(format, export.Payload);
        return new(item.OperationId, true, "contentHash", null, hash, hash, failure is null ? null
            : $"Operation '{item.OperationId}' failed with {failure.Category}; the current content hash is reported.");
    }

    private static PlcOperationVerification Fail(PlcOperationRequest item, string message)
        => new(item.OperationId, false, "contentHash", null, null, null, message);
}
