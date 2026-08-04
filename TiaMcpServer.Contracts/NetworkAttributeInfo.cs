namespace TiaMcpServer.Contracts;

/// <summary>
/// One attribute name-value pair returned by an <c>inspect_network_object</c> operation.
/// The value is always a string; type parsing is left to the caller.
/// </summary>
public class NetworkAttributeInfo
{
    /// <summary>Attribute name as reported by TIA Portal Openness.</summary>
    public string? Name { get; set; }

    /// <summary>String representation of the attribute's current value. Null when the attribute exists but has no value.</summary>
    public string? Value { get; set; }
}
