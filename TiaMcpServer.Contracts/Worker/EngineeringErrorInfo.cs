namespace TiaMcpServer.Contracts.Worker;

/// <summary>
/// A Siemens exception that caused a worker failure: its type name, verbatim message and the chain
/// of inner exceptions, outermost first. Messages are localized by the Portal UI language, so
/// consumers must never parse them; the category is derived from the exception type only.
/// </summary>
public sealed class EngineeringErrorInfo
{
    /// <summary>Most inner exceptions recorded; deeper links are dropped.</summary>
    public const int MaxInnerCount = 8;

    /// <summary>Longest message recorded for one exception; longer text is cut.</summary>
    public const int MaxMessageChars = 4000;

    /// <summary>Short type name of the exception, for example <c>EngineeringTargetInvocationException</c>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The exception message as Siemens produced it.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Inner exceptions, outermost first. Empty, never null.</summary>
    public List<EngineeringInnerErrorInfo> Inner { get; set; } = new();
}

/// <summary>One inner exception of an <see cref="EngineeringErrorInfo"/> chain.</summary>
public sealed class EngineeringInnerErrorInfo
{
    public string Type { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}
