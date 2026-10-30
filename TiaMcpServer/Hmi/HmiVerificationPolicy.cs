using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Hmi;

/// <summary>
/// Flat per-object keys for hmi_write supersession: a rename owns its old and new names, and a table
/// delete also offers the tags its effect removes. Nothing else crosses kinds (D4).
/// </summary>
public sealed class HmiVerificationPolicy(IReadOnlyDictionary<string, HmiWriteEffect> effects)
    : IWriteVerificationPolicy<HmiWriteOperationRequest>
{
    public string ReadTool => "hmi_read";
    public bool HasReadBack(HmiWriteOperationRequest item) => false;
    public Task<WriteOperationVerification> ReadBackAsync(HmiWriteOperationRequest item, StructuredOperationFailure? failure)
        => throw new NotSupportedException();

    public IReadOnlyList<string> ObjectKeys(HmiWriteOperationRequest item, bool asLater)
    {
        var effect = effects[item.OperationId];
        var target = effect.Target;
        var keys = new[] { target.Name, item.NewName }.OfType<string>().Select(name => Key(target, target.Kind, name));
        if (asLater && item.Operation == "delete_tag_table" && effect.Removes is { } removal)
            keys = keys.Concat(removal.Tags.Select(tag => Key(target, "Tag", tag)));
        return keys.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string Key(HmiTargetIdentity target, string kind, string name)
    {
        var prefix = $"hmi\n{target.HmiName.ToUpperInvariant()}\n";
        var upper = name.ToUpperInvariant();
        return kind switch
        {
            "TagTable" => $"{prefix}table\n{upper}",
            "Tag" => $"{prefix}tag\n{upper}",
            "Alarm" => $"{prefix}alarm\n{target.AlarmKind}\n{upper}",
            _ => throw new NotSupportedException(),
        };
    }
}
