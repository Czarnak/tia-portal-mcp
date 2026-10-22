using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Network;

public static class NetworkGuardDefinitions
{
    public const string ConnectedDelete = "network_delete_connected_subnet";
    public const string Unverifiable = "network_state_unverifiable";
    public static IReadOnlyList<WriteGuardDefinition> Definitions { get; } = WriteGuardCatalog.NonInteractive(new[]
    {
        new WriteGuardDefinition(ConnectedDelete, WriteGuardSeverities.Info, "Deleting this subnet removes the listed node connections; devices and nodes are preserved."),
        new WriteGuardDefinition(Unverifiable, WriteGuardSeverities.Block, "Required Network consequence evidence could not be verified.")
    }, "Network");

}
