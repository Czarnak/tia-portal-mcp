using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

public sealed class PlcVerificationPolicy(OpennessWorkerClient client, IReadOnlyDictionary<string, PlcWriteEffect> effects)
    : IWriteVerificationPolicy<PlcOperationRequest>
{
    public string ReadTool => "plc_read";

    public bool HasReadBack(PlcOperationRequest item)
        => item.Operation is "update_block_logic" or "update_type_content";

    public static async Task<PlcWriteVerification> VerifyAsync(OpennessWorkerClient client,
        IReadOnlyList<PlcOperationRequest> items, IReadOnlyDictionary<string, PlcWriteEffect> effects,
        StructuredOperationBatch batch)
        => PlcWriteVerification.From(await new TrustedWriteVerifier<PlcOperationRequest>(items,
            new PlcVerificationPolicy(client, effects)).VerifyAsync(batch).ConfigureAwait(false));

    /// <summary>Hierarchical object keys: a container's key prefixes its members' keys.</summary>
    public IReadOnlyList<string> ObjectKeys(PlcOperationRequest item, bool asLater)
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

    public async Task<WriteOperationVerification> ReadBackAsync(PlcOperationRequest item, StructuredOperationFailure? failure)
    {
        // The caller's own path: the canonical path can be ambiguous across PLCs.
        var format = PlcFormatNames.Normalize(item.Operation, item.Format);
        var export = item.Operation == "update_block_logic"
            ? await client.GetBlockContentAsync(item.BlockPath!, format: format).ConfigureAwait(false)
            : await client.GetTypeContentAsync(item.TypePath!, format: format).ConfigureAwait(false);
        if (!export.Success || string.IsNullOrEmpty(export.Payload))
            return Fail(item, $"Operation '{item.OperationId}': the written content could not be re-exported ({(export.Success ? "the export was empty" : export.Error)}).");
        var hash = ContentHashes.Compute(format, export.Payload);
        return new(item.OperationId, true, "contentHash", null, hash, failure is null ? null
            : $"Operation '{item.OperationId}' failed with {failure.Category}; the current content hash is reported.");
    }

    private static WriteOperationVerification Fail(PlcOperationRequest item, string message)
        => new(item.OperationId, false, "contentHash", null, null, message);
}
