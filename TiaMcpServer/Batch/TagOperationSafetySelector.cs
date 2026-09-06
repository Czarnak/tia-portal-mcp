using TiaMcpServer.Contracts;

namespace TiaMcpServer.Batch;

internal sealed record TagOperationSafetySelectorKey(
    string SelectorKind,
    string? NormalizedProjectPath,
    string PlcName,
    string FolderPath,
    string TableName,
    string? ObjectName,
    string? EffectiveName,
    string? EffectiveLogicalAddress);

internal static class TagOperationSafetySelector
{
    public static TagOperationSafetySelectorKey Build(BatchOperationRequest op)
    {
        if (!TryBuild(op, out var key))
        {
            throw new InvalidOperationException($"Unsupported tag safety operation '{op.Operation}'.");
        }

        return key;
    }

    public static bool TryBuild(BatchOperationRequest op, out TagOperationSafetySelectorKey key)
    {
        if (op.Operation is not ("create_tag_table" or "delete_tag_table" or "create_tag" or "update_tag" or "delete_tag" or "create_user_constant" or "update_user_constant" or "delete_user_constant"))
        {
            key = default!;
            return false;
        }

        var effectiveName = op.Operation switch
        {
            "update_tag" => string.IsNullOrWhiteSpace(op.NewName) ? op.Name : op.NewName,
            "update_user_constant" => op.Name,
            _ => op.Name
        };
        var effectiveLogicalAddress = op.Operation is "create_tag" or "update_tag"
            ? op.LogicalAddress
            : null;

        key = new(
            op.Operation,
            ProjectPathNormalization.Canonicalize(op.ProjectPath),
            op.PlcName ?? string.Empty,
            op.FolderPath ?? string.Empty,
            op.TableName ?? string.Empty,
            op.Operation is "create_tag_table" or "delete_tag_table" ? null : op.Name,
            effectiveName,
            effectiveLogicalAddress);
        return true;
    }
}
