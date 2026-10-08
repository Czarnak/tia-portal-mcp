using Siemens.Engineering.HW.CommunicationConnections;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Typed V21 communication-connection property readers keyed by the Siemens-free catalog.
/// </summary>
public static class ConnectionModeledAttributeAdapters
{
    public static bool TryCreateReader(
        ResolvedNetworkObject resolved,
        string adapterKey,
        out Func<object?>? reader)
        => resolved.Value is Connection connection
            ? TryCreateReader(connection, adapterKey, out reader)
            : NoReader(out reader);

    internal static bool TryCreateReader(
        Connection connection,
        string adapterKey,
        out Func<object?>? reader)
    {
        switch (adapterKey)
        {
            case "connection.ConnectionType":
                reader = () => connection.ConnectionType;
                return true;
            case "connection.IsValid":
                reader = () => connection.IsValid;
                return true;
            case "connection.LocalEndpointName":
                reader = () => connection.LocalInterface?.Name;
                return true;
            case "connection.LocalSubnetName":
                reader = () => connection.LocalSubnetName;
                return true;
            case "connection.LocalTargetName":
                reader = () => connection.LocalTarget?.Name;
                return true;
            case "connection.PartnerEndpointName":
                reader = () => connection.PartnerInterface?.Name;
                return true;
            case "connection.PartnerSubnetName":
                reader = () => connection.PartnerSubnetName;
                return true;
            case "connection.PartnerTargetName":
                reader = () => connection.PartnerTarget?.Name;
                return true;
            case "s7Connection.LocalConnectionId" when connection is S7Connection s7Connection:
                reader = () => s7Connection.LocalConnectionId;
                return true;
            case "s7Connection.LocalConnectionName" when connection is S7Connection s7Connection:
                reader = () => s7Connection.LocalConnectionName;
                return true;
            case "s7Connection.LocalConnectionResourceId" when connection is S7Connection s7Connection:
                reader = () => s7Connection.LocalConnectionResourceId;
                return true;
            case "fdlConnection.LocalConnectionId" when connection is FdlConnection fdlConnection:
                reader = () => fdlConnection.LocalConnectionId;
                return true;
            case "fdlConnection.LocalConnectionName" when connection is FdlConnection fdlConnection:
                reader = () => fdlConnection.LocalConnectionName;
                return true;
            case "isoConnection.LocalConnectionId" when connection is IsoConnection isoConnection:
                reader = () => isoConnection.LocalConnectionId;
                return true;
            case "isoConnection.LocalConnectionName" when connection is IsoConnection isoConnection:
                reader = () => isoConnection.LocalConnectionName;
                return true;
            case "isoOnTcpConnection.LocalConnectionId" when connection is IsoOnTcpConnection isoOnTcpConnection:
                reader = () => isoOnTcpConnection.LocalConnectionId;
                return true;
            case "isoOnTcpConnection.LocalConnectionName" when connection is IsoOnTcpConnection isoOnTcpConnection:
                reader = () => isoOnTcpConnection.LocalConnectionName;
                return true;
            case "ptpConnection.LocalConnectionId" when connection is PtpConnection ptpConnection:
                reader = () => ptpConnection.LocalConnectionId;
                return true;
            case "ptpConnection.LocalConnectionName" when connection is PtpConnection ptpConnection:
                reader = () => ptpConnection.LocalConnectionName;
                return true;
            case "tcpConnection.LocalConnectionId" when connection is TcpConnection tcpConnection:
                reader = () => tcpConnection.LocalConnectionId;
                return true;
            case "tcpConnection.LocalConnectionName" when connection is TcpConnection tcpConnection:
                reader = () => tcpConnection.LocalConnectionName;
                return true;
            case "udpConnection.LocalConnectionId" when connection is UdpConnection udpConnection:
                reader = () => udpConnection.LocalConnectionId;
                return true;
            case "udpConnection.LocalConnectionName" when connection is UdpConnection udpConnection:
                reader = () => udpConnection.LocalConnectionName;
                return true;
            case "hmiConnection.LocalConnectionName" when connection is HmiConnection hmiConnection:
                reader = () => hmiConnection.LocalConnectionName;
                return true;
            default:
                return NoReader(out reader);
        }
    }

    private static bool NoReader(out Func<object?>? reader)
    {
        reader = null;
        return false;
    }
}
