using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiConnections;
using TiaMcpServer.Contracts.Hmi;

namespace TiaMcpServer.OpennessWorker.Openness.Hmi;

/// <summary>
/// Operation 6: connections. Reads enumerate the composition (never <c>Find</c>); each connection is read in one
/// pass over an explicit property list. An unreadable composition (the connections or a connection's driver
/// properties) fails the item; an unreadable scalar property is null plus a message.
/// </summary>
public static class HmiConnectionReader
{
    // Inference, not observed: only S7-1200/1500 drivers were seen live, so other drivers' credential names are guessed.
    private static readonly string[] SecretNameParts =
        { "password", "passwd", "passphrase", "pwd", "secret", "token", "credential", "privatekey" };

    /// <summary>True when <paramref name="name"/> contains a credential-like word (case-insensitive); such values never leave the worker.</summary>
    internal static bool IsSecretName(string name)
        => SecretNameParts.Any(part => name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0);

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

    /// <summary>Every connection with its name, for <c>validate</c>.</summary>
    internal static List<(object Item, string Name)> NamedConnections(HmiSoftware software)
        => HmiReadLog.Guard(() => software.Connections.ToList(), "The connections")
            .Select(c => ((object)c, HmiReadLog.Guard(() => c.Name, "A connection name"))).ToList();

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
                    // A secret-named key always names a secret in the raw string too, so this also covers
                    // an unparsable address that mentions one.
                    Raw = IsSecretName(raw) ? null : raw,
                    Parsed = HmiInitialAddressParser.Parse(raw)
                        .Where(p => !IsSecretName(p.Key))
                        .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
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
            var propertyName = log.Try(() => p.PropertyName, What("PropertyName"));
            // Secrets never leave the worker: a secret-named property is dropped before its value is read.
            if (propertyName is not null && IsSecretName(propertyName))
            {
                return null;
            }

            return new HmiDriverPropertyInfo
            {
                PropertyName = propertyName,
                // Fail closed: an unreadable name cannot be shown not to be a secret, so its value is not read.
                Value = propertyName is null ? null : log.Try(() => p.Value, What("Value")),
                Info = log.Try(() => p.Info, What("Info")),
            };
        }).OfType<HmiDriverPropertyInfo>()
          .GroupBy(r => (r.PropertyName, r.Value)) // Openness returns every driver property twice
          .Select(g => g.First());
        return HmiPager.InNameOrder(rows, r => r.PropertyName ?? string.Empty).ToList();
    }
}
