namespace TiaMcpServer.Contracts;

/// <summary>Result of <c>list_connections</c>: every connection ordered by name. An unreadable property is null (see messages).</summary>
public class HmiConnectionListInfo
{
    public bool IsComplete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();

    public List<HmiConnectionInfo> Connections { get; set; } = new List<HmiConnectionInfo>();
}

public class HmiConnectionInfo
{
    public string Name { get; set; } = string.Empty;

    public string? CommunicationDriver { get; set; }

    /// <summary>The raw <c>InitialAddress</c> string and its parsed key/value pairs; null when the address is unset or could not be read (see messages).</summary>
    public HmiInitialAddressInfo? InitialAddress { get; set; }

    public string? Partner { get; set; }

    public string? Station { get; set; }

    public string? Node { get; set; }

    public bool? DisabledAtStartup { get; set; }

    /// <summary>A plain string in Unified (not multilingual).</summary>
    public string? Comment { get; set; }

    /// <summary>Ordered by property name; an unreadable driver-property composition fails the item.</summary>
    public List<HmiDriverPropertyInfo> DriverProperties { get; set; } = new List<HmiDriverPropertyInfo>();
}

/// <summary>The raw address (always kept) and the key/value pairs parsed from it; empty when the raw string does not parse.</summary>
public class HmiInitialAddressInfo
{
    public string? Raw { get; set; }

    public Dictionary<string, string> Parsed { get; set; } = new Dictionary<string, string>();
}

public class HmiDriverPropertyInfo
{
    public string? PropertyName { get; set; }

    public string? Value { get; set; }

    public string? Info { get; set; }
}
