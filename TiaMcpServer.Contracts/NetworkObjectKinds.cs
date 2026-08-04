using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Canonical string constants and the ordered set for the six network-object kinds
/// supported by the Phase 3 introspection operations.
/// </summary>
public static class NetworkObjectKinds
{
    public const string DeviceItem = "deviceItem";
    public const string NetworkInterface = "networkInterface";
    public const string Node = "node";
    public const string Subnet = "subnet";
    public const string IoSystem = "ioSystem";
    public const string CommunicationConnection = "communicationConnection";

    public static readonly IReadOnlyCollection<string> All = new[]
    {
        DeviceItem,
        NetworkInterface,
        Node,
        Subnet,
        IoSystem,
        CommunicationConnection,
    };
}
