using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaMcpServer.OpennessWorker;

/// <summary>
/// Siemens-free declaration of typed communication-connection attributes available in the
/// installed V21 API. Unknown future connection types intentionally have no modeled contract.
/// </summary>
public static class ConnectionModeledAttributeCatalog
{
    public static readonly IReadOnlyList<string> SupportedConnectionTypes = new[]
    {
        "S7Connection",
        "FdlConnection",
        "IsoConnection",
        "IsoOnTcpConnection",
        "PtpConnection",
        "TcpConnection",
        "UdpConnection",
        "HmiConnection",
    };

    private static readonly IReadOnlyList<NetworkModeledAttributeDescriptor> BaseDescriptors = new[]
    {
        Descriptor(
            "ConnectionType",
            "Siemens.Engineering.HW.CommunicationConnections.ConnectionType",
            "connection.ConnectionType"),
        Descriptor("IsValid", "System.Boolean", "connection.IsValid"),
        Descriptor("LocalEndpointName", "System.String", "connection.LocalEndpointName"),
        Descriptor("LocalSubnetName", "System.String", "connection.LocalSubnetName"),
        Descriptor("LocalTargetName", "System.String", "connection.LocalTargetName"),
        Descriptor("PartnerEndpointName", "System.String", "connection.PartnerEndpointName"),
        Descriptor("PartnerSubnetName", "System.String", "connection.PartnerSubnetName"),
        Descriptor("PartnerTargetName", "System.String", "connection.PartnerTargetName"),
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<NetworkModeledAttributeDescriptor>>
        TypeSpecificDescriptors =
            new Dictionary<string, IReadOnlyList<NetworkModeledAttributeDescriptor>>(StringComparer.Ordinal)
            {
                ["S7Connection"] = new[]
                {
                    Descriptor("LocalConnectionId", "System.String", "s7Connection.LocalConnectionId"),
                    Descriptor("LocalConnectionName", "System.String", "s7Connection.LocalConnectionName"),
                    Descriptor(
                        "LocalConnectionResourceId",
                        "System.Int64",
                        "s7Connection.LocalConnectionResourceId"),
                },
                ["FdlConnection"] = new[]
                {
                    Descriptor("LocalConnectionId", "System.String", "fdlConnection.LocalConnectionId"),
                    Descriptor("LocalConnectionName", "System.String", "fdlConnection.LocalConnectionName"),
                },
                ["IsoConnection"] = new[]
                {
                    Descriptor("LocalConnectionId", "System.String", "isoConnection.LocalConnectionId"),
                    Descriptor("LocalConnectionName", "System.String", "isoConnection.LocalConnectionName"),
                },
                ["IsoOnTcpConnection"] = new[]
                {
                    Descriptor("LocalConnectionId", "System.String", "isoOnTcpConnection.LocalConnectionId"),
                    Descriptor("LocalConnectionName", "System.String", "isoOnTcpConnection.LocalConnectionName"),
                },
                ["PtpConnection"] = new[]
                {
                    Descriptor("LocalConnectionId", "System.String", "ptpConnection.LocalConnectionId"),
                    Descriptor("LocalConnectionName", "System.String", "ptpConnection.LocalConnectionName"),
                },
                ["TcpConnection"] = new[]
                {
                    Descriptor("LocalConnectionId", "System.String", "tcpConnection.LocalConnectionId"),
                    Descriptor("LocalConnectionName", "System.String", "tcpConnection.LocalConnectionName"),
                },
                ["UdpConnection"] = new[]
                {
                    Descriptor("LocalConnectionId", "System.String", "udpConnection.LocalConnectionId"),
                    Descriptor("LocalConnectionName", "System.String", "udpConnection.LocalConnectionName"),
                },
                ["HmiConnection"] = new[]
                {
                    Descriptor("LocalConnectionName", "System.String", "hmiConnection.LocalConnectionName"),
                },
            };

    public static IReadOnlyList<NetworkModeledAttributeDescriptor> ForConnectionType(string connectionType)
    {
        if (!TypeSpecificDescriptors.TryGetValue(connectionType, out var typeSpecific))
        {
            return Array.Empty<NetworkModeledAttributeDescriptor>();
        }

        return BaseDescriptors
            .Concat(typeSpecific)
            .OrderBy(descriptor => descriptor.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public static bool IsSupportedConnectionType(string? connectionType)
        => connectionType is not null && TypeSpecificDescriptors.ContainsKey(connectionType);

    private static NetworkModeledAttributeDescriptor Descriptor(
        string name,
        string expectedClrTypeName,
        string adapterKey)
        => new NetworkModeledAttributeDescriptor(name, expectedClrTypeName, adapterKey);
}
