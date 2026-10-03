using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Network;

public static class NetworkGuardDefinitions
{
    public const string ConnectedDelete = "network_delete_connected_subnet";
    public const string Unverifiable = "network_state_unverifiable";
    public static IReadOnlyList<WriteGuardDefinition> Definitions { get; } = Validate(new[]
    {
        new WriteGuardDefinition(ConnectedDelete, "info", "Deleting this subnet removes the listed node connections; devices and nodes are preserved."),
        new WriteGuardDefinition(Unverifiable, "block", "Required Network consequence evidence could not be verified.")
    });

    public static IReadOnlyList<WriteGuardDefinition> Validate(IEnumerable<WriteGuardDefinition> definitions)
    {
        var materialized = definitions.ToArray();
        if (materialized.Any(guard => guard.Severity == WriteGuardSeverities.Acknowledge))
            throw new ArgumentException("Network guards cannot require acknowledgement.", nameof(definitions));
        _ = new WriteGuardCatalog(materialized);
        return Array.AsReadOnly(materialized);
    }
}
