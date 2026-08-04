namespace TiaMcpServer.Contracts;

/// <summary>
/// One attribute entry returned by an <c>inspect_network_object</c> operation.
/// Carries provenance, access classification, typed value, and optional diagnostics.
/// </summary>
public sealed class NetworkAttributeInfo
{
    /// <summary>Attribute name as reported by TIA Portal Openness.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Where the attribute definition comes from.
    /// One of <c>modeled</c>, <c>dynamic</c>, <c>modeledAndDynamic</c>, or null when
    /// <see cref="Availability"/> is <c>unknownAttribute</c>.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Read/write accessibility. One of <c>none</c>, <c>readOnly</c>, <c>writeOnly</c>,
    /// <c>readWrite</c>, or <c>unknown</c>.
    /// </summary>
    public string Access { get; set; } = string.Empty;

    /// <summary>CLR type names the worker can report for this attribute, in declaration order.</summary>
    public List<string> SupportedTypes { get; set; } = new();

    /// <summary>
    /// Whether a value is present and could be read. One of <c>available</c>,
    /// <c>notApplicable</c>, <c>unsupported</c>, <c>unreadable</c>, <c>readFailed</c>,
    /// <c>unrepresentable</c>, or <c>unknownAttribute</c>.
    /// </summary>
    public string Availability { get; set; } = string.Empty;

    /// <summary>
    /// Typed value. Null when <see cref="Availability"/> is not <c>available</c>
    /// or when the attribute is present but has no value.
    /// </summary>
    public NetworkAttributeValueInfo? Value { get; set; }

    /// <summary>
    /// Additional diagnostic information when <see cref="Availability"/> is not <c>available</c>.
    /// </summary>
    public NetworkAttributeDiagnosticInfo? Diagnostic { get; set; }
}

/// <summary>
/// The typed value of a network attribute.
/// </summary>
public sealed class NetworkAttributeValueInfo
{
    /// <summary>
    /// Value kind discriminator. One of <c>string</c>, <c>boolean</c>, <c>integer</c>,
    /// <c>number</c>, or <c>enum</c>.
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// The value itself: null, a JSON string, boolean, integer, number, or a
    /// <see cref="NetworkEnumValueInfo"/> object.
    /// </summary>
    public object? Value { get; set; }

    /// <summary>CLR type name the worker resolved for this value, or null.</summary>
    public string? TypeName { get; set; }
}

/// <summary>
/// An enumeration value returned as part of a <see cref="NetworkAttributeValueInfo"/>.
/// </summary>
public sealed class NetworkEnumValueInfo
{
    /// <summary>CLR type name of the enumeration.</summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>Symbolic name of the enumeration member.</summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>Underlying integer value of the enumeration member.</summary>
    public long NumericValue { get; set; }
}

/// <summary>
/// Diagnostic context attached to an attribute whose value could not be read or represented.
/// </summary>
public sealed class NetworkAttributeDiagnosticInfo
{
    /// <summary>Short machine-readable category label (e.g. <c>read_error</c>, <c>type_error</c>).</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Human-readable explanation of the failure.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>CLR type name involved in the failure, or null when not applicable.</summary>
    public string? ClrTypeName { get; set; }
}
