using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiConnections;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness.Hmi;

/// <summary>
/// Operation 6: connections. Reads enumerate the composition (never <c>Find</c>); each connection is read in one
/// pass over an explicit property list. An unreadable composition (the connections or a connection's driver
/// properties) fails the item; an unreadable scalar property is null plus a message.
/// </summary>
public static class HmiConnectionReader
{
    public static HmiConnectionListInfo ListConnections(HmiSoftware software)
    {
        var log = new HmiReadLog();
        var connections = HmiReadLog.Guard(() => software.Connections.ToList(), "The connections");
        var named = connections
            .Select(c => (Connection: c, Name: HmiReadLog.Guard(() => c.Name, "A connection name")))
            .ToList();
        var rows = HmiPager.InNameOrder(named, n => n.Name)
            .Select(n => ReadRow(n.Connection, n.Name, log))
            .ToList();

        return new HmiConnectionListInfo { IsComplete = log.IsComplete, Messages = log.Messages, Connections = rows };
    }

    private static HmiConnectionInfo ReadRow(HmiConnection connection, string name, HmiReadLog log)
    {
        string What(string property) => $"Property {property} of connection '{name}'";
        var raw = log.Try(() => connection.InitialAddress, What("InitialAddress"));
        return new HmiConnectionInfo
        {
            Name = name,
            CommunicationDriver = log.Try(() => connection.CommunicationDriver, What("CommunicationDriver")),
            InitialAddress = raw is null
                ? null
                : new HmiInitialAddressInfo
                {
                    Raw = raw,
                    Parsed = HmiInitialAddressParser.Parse(raw).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
                },
            Partner = log.Try(() => connection.Partner, What("Partner")),
            Station = log.Try(() => connection.Station, What("Station")),
            Node = log.Try(() => connection.Node, What("Node")),
            DisabledAtStartup = log.Try(() => (bool?)connection.DisabledAtStartup, What("DisabledAtStartup")),
            Comment = log.Try(() => connection.Comment, What("Comment")),
            DriverProperties = ReadDriverProperties(connection, name, log),
        };
    }

    private static List<HmiDriverPropertyInfo> ReadDriverProperties(HmiConnection connection, string name, HmiReadLog log)
    {
        var properties = HmiReadLog.Guard(() => connection.DriverProperties.ToList(), $"The driver properties of connection '{name}'");
        var rows = properties.Select(p =>
        {
            string What(string property) => $"Property {property} of a driver property of connection '{name}'";
            return new HmiDriverPropertyInfo
            {
                PropertyName = log.Try(() => p.PropertyName, What("PropertyName")),
                Value = log.Try(() => p.Value, What("Value")),
                Info = log.Try(() => p.Info, What("Info")),
            };
        });
        return HmiPager.InNameOrder(rows, r => r.PropertyName ?? string.Empty).ToList();
    }
}
