using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

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
/// Verifies succeeded plc_write items with ordinary reads: one tag inventory, one project-tree read
/// per touched PLC and one re-export per content item. Presence, absence and values are checked by
/// resolving probe requests against a fresh <see cref="PlcWorkingState"/>; content is not compared
/// with the submitted text, which TIA normalizes.
/// </summary>
public sealed class PlcWriteVerifier(OpennessWorkerClient client, IReadOnlyList<PlcOperationRequest> items,
    IReadOnlyDictionary<string, PlcWriteEffect> effects)
{
    private readonly PlcWritePlanner _reader = new(client);

    public async Task<PlcWriteVerification> VerifyAsync(string? projectPath, StructuredOperationBatch batch)
    {
        var succeeded = batch.Operations.Where(o => o.Status == OperationBatchStatus.Succeeded)
            .Select(o => o.OperationId).ToHashSet(StringComparer.Ordinal);
        PlcWritePlanner.Evidence? inventory = null;
        var states = new Dictionary<string, (PlcWorkingState? State, string? Error)>(StringComparer.Ordinal);
        var results = new List<PlcOperationVerification>();
        foreach (var item in items.Where(i => succeeded.Contains(i.OperationId)))
        {
            if (item.Operation is "update_block_logic" or "update_type_content")
            {
                results.Add(await VerifyContentAsync(projectPath, item).ConfigureAwait(false));
                continue;
            }

            inventory ??= await _reader.ReadInventoryAsync(projectPath).ConfigureAwait(false);
            var target = effects[item.OperationId].Target;
            var key = $"{target.DeviceName}\n{target.PlcName}";
            if (!states.TryGetValue(key, out var entry))
                states[key] = entry = await ReadStateAsync(projectPath, inventory, target).ConfigureAwait(false);
            results.Add(entry.State is null
                ? Fail(item, Expectation(item), entry.Error!)
                : VerifyStructural(item, entry.State));
        }

        return new PlcWriteVerification(results.All(r => r.Success), results, null);
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

    private static PlcOperationVerification VerifyStructural(PlcOperationRequest item, PlcWorkingState state)
    {
        var expectation = Expectation(item);
        var resolution = state.Resolve(Probe(item));
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

    private async Task<PlcOperationVerification> VerifyContentAsync(string? projectPath, PlcOperationRequest item)
    {
        // The caller's own path: the canonical path can be ambiguous across PLCs.
        var format = PlcFormatNames.Normalize(item.Operation, item.Format);
        var export = item.Operation == "update_block_logic"
            ? await client.GetBlockContentAsync(item.BlockPath!, projectPath, format).ConfigureAwait(false)
            : await client.GetTypeContentAsync(item.TypePath!, format, projectPath).ConfigureAwait(false);
        if (!export.Success || string.IsNullOrEmpty(export.Payload))
            return Fail(item, "contentHash", $"Operation '{item.OperationId}': the written content could not be re-exported ({(export.Success ? "the export was empty" : export.Error)}).");
        var hash = PlcContentHashes.Compute(format, export.Payload);
        return new(item.OperationId, true, "contentHash", null, hash, hash, null);
    }

    /// <summary>A request that resolves the item's final target: a delete probe for presence, an update probe for values.</summary>
    private static PlcOperationRequest Probe(PlcOperationRequest item)
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
                PlcName = item.PlcName, TableName = item.TableName, FolderPath = item.FolderPath, Name = item.Name, BlockPath = item.BlockPath },
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
