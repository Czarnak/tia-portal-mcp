using TiaMcpServer.Contracts.Network;

namespace TiaMcpServer.Network;

/// <summary>
/// The one selector kind/field matrix, shared by request validation
/// (<see cref="NetworkOperationCatalog"/>) and worker-payload validation
/// (<see cref="NetworkPayloadContract"/>). Applicable = required ∪ optional; a present field outside
/// the applicable set is inapplicable for the kind.
/// </summary>
public static class NetworkSelectorRules
{
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> RequiredFields { get; } =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [NetworkObjectKinds.DeviceItem] = Set("deviceName", "itemPath"),
            [NetworkObjectKinds.NetworkInterface] = Set("deviceName", "itemPath"),
            [NetworkObjectKinds.Node] = Set("deviceName", "nodeId"),
            [NetworkObjectKinds.Subnet] = Set("subnetId"),
            [NetworkObjectKinds.IoSystem] = Set("subnetId", "number"),
            [NetworkObjectKinds.CommunicationConnection] =
                Set("deviceName", "itemPath", "connectionIndex", "connectionType", "localConnectionName"),
        };

    public static IReadOnlyDictionary<string, IReadOnlySet<string>> ApplicableFields { get; } =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [NetworkObjectKinds.DeviceItem] = Set("kind", "deviceName", "itemPath"),
            [NetworkObjectKinds.NetworkInterface] =
                Set("kind", "deviceName", "itemPath", "interfaceName", "interfaceType", "interfaceOperatingMode"),
            [NetworkObjectKinds.Node] =
                Set("kind", "deviceName", "itemPath", "interfacePath", "interfaceName", "nodeId", "nodeIndex"),
            [NetworkObjectKinds.Subnet] = Set("kind", "subnetId"),
            [NetworkObjectKinds.IoSystem] = Set("kind", "subnetId", "number", "ioSystemIndex", "ioSystemName"),
            [NetworkObjectKinds.CommunicationConnection] = Set(
                "kind", "deviceName", "itemPath", "connectionIndex", "connectionType", "localConnectionName",
                "localConnectionId"),
        };

    public static IReadOnlySet<string> SupportedConnectionTypes { get; } = Set(
        "S7Connection",
        "FdlConnection",
        "IsoConnection",
        "IsoOnTcpConnection",
        "PtpConnection",
        "TcpConnection",
        "UdpConnection",
        "HmiConnection");

    private static HashSet<string> Set(params string[] names) => new(names, StringComparer.Ordinal);
}
