using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Plc;

/// <summary>
/// One post-write check of one succeeded item. <see cref="Check"/> is <c>exists</c>, <c>absent</c>,
/// <c>values</c> or <c>contentHash</c>; a content item reports its new <see cref="ContentHash"/>.
/// </summary>
public sealed record PlcOperationVerification(string OperationId, bool Success, string Check,
    string? Expected, string? Actual, string? ContentHash, string? Message);

public sealed record PlcWriteVerification(bool Success, IReadOnlyList<PlcOperationVerification> Operations,
    StructuredOperationOmission? Omission);

/// <summary>
/// Verifies the attempted plc_write items (succeeded, plus the failed one) with ordinary reads: one
/// tag inventory, one project-tree read per touched PLC and one re-export per content item.
/// Presence, absence and values are checked by resolving probe requests against a fresh
/// <see cref="PlcWorkingState"/>; content is not compared with the submitted text, which TIA
/// normalizes. Only the last effective item on an object asserts the final state; earlier items on
/// it pass as superseded. A failed item is observed, never asserted.
/// </summary>
public sealed class PlcWriteVerifier(OpennessWorkerClient client, IReadOnlyList<PlcOperationRequest> items,
    IReadOnlyDictionary<string, PlcWriteEffect> effects)
{
    /// <summary>Failure categories of a precondition refusal: the item never changed TIA state.</summary>
    private static readonly IReadOnlySet<string> NeverRan = new HashSet<string>(StringComparer.Ordinal)
    {
        WorkerFailureCategories.ValidationError, WorkerFailureCategories.TargetNotFound, WorkerFailureCategories.TargetAmbiguous,
        WorkerFailureCategories.StateChanged, WorkerFailureCategories.GuardBlocked, WorkerFailureCategories.BindingConflict,
        WorkerFailureCategories.AccessDenied,
    };

    private readonly PlcWritePlanner _reader = new(client);

    public async Task<PlcWriteVerification> VerifyAsync(string? projectPath, StructuredOperationBatch batch)
    {
        var outcomes = batch.Operations.Where(o => o.Status is OperationBatchStatus.Succeeded or OperationBatchStatus.Failed)
            .ToDictionary(o => o.OperationId, StringComparer.Ordinal);
        var attempted = items.Where(i => outcomes.ContainsKey(i.OperationId)).ToArray();
        PlcWritePlanner.Evidence? inventory = null;
        var states = new Dictionary<string, (PlcWorkingState? State, string? Error)>(StringComparer.Ordinal);
        var results = new List<PlcOperationVerification>();
        for (var index = 0; index < attempted.Length; index++)
        {
            var item = attempted[index];
            var failure = outcomes[item.OperationId].Failure;
            if (failure is null && SupersededBy(attempted, outcomes, index) is { } later)
            {
                results.Add(new(item.OperationId, true, "superseded", null, null, null,
                    $"Operation '{item.OperationId}': superseded by later operation '{later}' on the same object; the final state is verified there."));
                continue;
            }

            if (item.Operation is "update_block_logic" or "update_type_content")
            {
                // A failed import may still have committed: its observed hash is reported either way.
                results.Add(await VerifyContentAsync(projectPath, item, failure).ConfigureAwait(false));
                continue;
            }

            inventory ??= await _reader.ReadInventoryAsync(projectPath).ConfigureAwait(false);
            var target = effects[item.OperationId].Target;
            var key = $"{target.DeviceName}\n{target.PlcName}";
            if (!states.TryGetValue(key, out var entry))
                states[key] = entry = await ReadStateAsync(projectPath, inventory, target).ConfigureAwait(false);
            results.Add(entry.State is null ? Fail(item, failure is null ? Expectation(item) : "observed", entry.Error!)
                : failure is null ? VerifyStructural(item, target, entry.State) : Observe(item, target, failure, entry.State));
        }

        return new PlcWriteVerification(results.All(r => r.Success), results, null);
    }

    /// <summary>The latest later item that may have changed one of this item's objects, or null.</summary>
    private string? SupersededBy(IReadOnlyList<PlcOperationRequest> attempted,
        IReadOnlyDictionary<string, StructuredOperationItem> outcomes, int index)
    {
        var own = Keys(attempted[index]);
        return attempted.Skip(index + 1)
            .Where(later => outcomes[later.OperationId].Failure is not { } failure || !NeverRan.Contains(failure.Category))
            .LastOrDefault(later => Keys(later).Any(k => own.Any(o => o == k || o.StartsWith(k + "\n", StringComparison.Ordinal))))
            ?.OperationId;
    }

    /// <summary>Hierarchical object keys: a container's key prefixes its members' keys.</summary>
    private IReadOnlyList<string> Keys(PlcOperationRequest item)
    {
        var target = effects[item.OperationId].Target;
        var scope = $"{target.DeviceName}\n{target.PlcName}\n";
        var table = $"{scope}table\n{target.FolderPath}\n{target.TableName}";
        var keys = target.Kind switch
        {
            PlcNameRules.Tag or PlcNameRules.UserConstant => new[] { $"{table}\n{target.Name}", $"{table}\n{item.NewName ?? target.Name}" },
            PlcNameRules.TagTable => new[] { table },
            "Type" => new[] { $"{scope}type\n{target.TypePath}" },
            _ => new[] { $"{scope}block\n" + (target.BlockPath ?? item.BlockPath ?? "").Replace('/', '\n') },
        };
        return keys.Select(k => k.ToUpperInvariant()).Distinct().ToArray();
    }

    private async Task<(PlcWorkingState? State, string? Error)> ReadStateAsync(string? projectPath,
        PlcWritePlanner.Evidence inventory, PlcTargetIdentity target)
    {
        if (inventory.Error is { } error) return (null, error.Message);
        var plc = inventory.Inventory!.Plcs.FirstOrDefault(p => PlcNameRules.Comparer.Equals(p.PlcName, target.PlcName)
            && PlcNameRules.Comparer.Equals(p.DeviceName, target.DeviceName));
        if (plc is null) return (null, $"PLC '{target.PlcName}' is missing from the post-write tag inventory.");
        var (tree, treeError) = await _reader.ReadTreeAsync(projectPath, plc).ConfigureAwait(false);
        return tree is null ? (null, treeError!.Message) : (PlcWorkingState.Create(plc, inventory.Inventory.IsComplete, tree), null);
    }

    private static PlcOperationVerification VerifyStructural(PlcOperationRequest item, PlcTargetIdentity target, PlcWorkingState state)
    {
        var expectation = Expectation(item);
        var resolution = state.Resolve(Probe(item, target));
        if (resolution.Guards.FirstOrDefault(g => g.Id == PlcGuardDefinitions.StateUnverifiable) is { } unverifiable)
            return Fail(item, expectation, unverifiable.Message);
        if (expectation == "absent")
            return resolution.Error?.Category == WorkerFailureCategories.TargetNotFound
                ? new(item.OperationId, true, "absent", "absent", "absent", null, null)
                : Fail(item, expectation, resolution.Error?.Message ?? "The deleted object is still present.", "present");
        if (resolution.Error is { } error) return Fail(item, expectation, error.Message, "absent");
        if (expectation == "exists") return new(item.OperationId, true, "exists", "exists", "exists", null, null);
        var changes = resolution.Effect!.Changes;
        var expected = Describe(changes.Select(c => (c.Field, c.Requested)));
        var actual = Describe(changes.Select(c => (c.Field, c.Current)));
        return changes.All(c => string.Equals(c.Current?.Trim(), c.Requested?.Trim(), StringComparison.OrdinalIgnoreCase))
            ? new(item.OperationId, true, "values", expected, actual, null, null)
            : new(item.OperationId, false, "values", expected, actual, null, $"Operation '{item.OperationId}': the observed values differ from the requested values.");
    }

    /// <summary>Reports what a failed item's target looks like now; only unreadable evidence fails.</summary>
    private static PlcOperationVerification Observe(PlcOperationRequest item, PlcTargetIdentity target, StructuredOperationFailure failure, PlcWorkingState state)
    {
        var resolution = state.Resolve(Probe(item, target));
        if (resolution.Guards.FirstOrDefault(g => g.Id == PlcGuardDefinitions.StateUnverifiable) is { } unverifiable)
            return Fail(item, "observed", unverifiable.Message);
        var actual = resolution.Error?.Category == WorkerFailureCategories.TargetNotFound ? "absent"
            : resolution.Error is not null ? null
            : Expectation(item) == "values" ? Describe(resolution.Effect!.Changes.Select(c => (c.Field, c.Current))) : "exists";
        if (actual is null) return Fail(item, "observed", resolution.Error!.Message);
        var ran = NeverRan.Contains(failure.Category)
            ? "the worker reported it never ran"
            : "it may already have changed TIA state";
        return new(item.OperationId, true, "observed", null, actual, null,
            $"Operation '{item.OperationId}' failed with {failure.Category}; {ran}. The observed state is reported.");
    }

    private async Task<PlcOperationVerification> VerifyContentAsync(string? projectPath, PlcOperationRequest item, StructuredOperationFailure? failure)
    {
        // The caller's own path: the canonical path can be ambiguous across PLCs.
        var format = PlcFormatNames.Normalize(item.Operation, item.Format);
        var export = item.Operation == "update_block_logic"
            ? await client.GetBlockContentAsync(item.BlockPath!, projectPath, format).ConfigureAwait(false)
            : await client.GetTypeContentAsync(item.TypePath!, format, projectPath).ConfigureAwait(false);
        if (!export.Success || string.IsNullOrEmpty(export.Payload))
            return Fail(item, "contentHash", $"Operation '{item.OperationId}': the written content could not be re-exported ({(export.Success ? "the export was empty" : export.Error)}).");
        var hash = PlcContentHashes.Compute(format, export.Payload);
        return new(item.OperationId, true, "contentHash", null, hash, hash, failure is null ? null
            : $"Operation '{item.OperationId}' failed with {failure.Category}; the current content hash is reported.");
    }

    /// <summary>A request that resolves the item's final target (structural probes use the canonical resolved block path, not the caller's): a delete probe for presence, an update probe for values.</summary>
    private static PlcOperationRequest Probe(PlcOperationRequest item, PlcTargetIdentity target)
    {
        var finalName = item.NewName ?? item.Name;
        return item.Operation switch
        {
            "create_tag" or "update_tag" => new() { OperationId = item.OperationId, Operation = "update_tag", PlcName = item.PlcName,
                TableName = item.TableName, FolderPath = item.FolderPath, Name = finalName, DataType = item.DataType, LogicalAddress = item.LogicalAddress,
                ExternalAccessible = item.ExternalAccessible, ExternalVisible = item.ExternalVisible, ExternalWritable = item.ExternalWritable },
            "create_user_constant" or "update_user_constant" => new() { OperationId = item.OperationId, Operation = "update_user_constant",
                PlcName = item.PlcName, TableName = item.TableName, FolderPath = item.FolderPath, Name = item.Name, DataType = item.DataType, Value = item.Value },
            _ => new() { OperationId = item.OperationId, Operation = item.Operation.Replace("create_", "delete_", StringComparison.Ordinal),
                PlcName = item.PlcName, TableName = item.TableName, FolderPath = item.FolderPath, Name = item.Name, BlockPath = target.BlockPath ?? item.BlockPath },
        };
    }

    private static string Expectation(PlcOperationRequest item)
        => item.Operation.StartsWith("delete_", StringComparison.Ordinal) ? "absent"
            : item.Operation is "create_tag" or "update_tag" or "create_user_constant" or "update_user_constant" ? "values"
            : item.Operation is "update_block_logic" or "update_type_content" ? "contentHash"
            : "exists";

    private static string Describe(IEnumerable<(string Field, string? Value)> fields)
        => string.Join("; ", fields.Select(f => $"{f.Field}={f.Value ?? "null"}"));

    private static PlcOperationVerification Fail(PlcOperationRequest item, string check, string message, string? actual = null)
        => new(item.OperationId, false, check, check is "exists" or "absent" ? check : null, actual, null, message);
}
