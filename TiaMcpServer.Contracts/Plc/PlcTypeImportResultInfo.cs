namespace TiaMcpServer.Contracts.Plc;

/// <summary>Payload of a completed <c>update_type_content</c>.</summary>
public sealed class PlcTypeImportResultInfo
{
    public bool Success { get; set; } = true;

    public string Operation { get; set; } = string.Empty;

    public string TypePath { get; set; } = string.Empty;

    public string TypeName { get; set; } = string.Empty;

    public string Format { get; set; } = string.Empty;

    /// <summary>
    /// False means a temporary external source node is still in the user's project. Reported
    /// rather than hidden: it is a visible change they did not ask for.
    /// </summary>
    public bool ProjectNodeRemoved { get; set; }

    /// <summary>
    /// How many objects TIA Portal reported creating or replacing — generated from the source for
    /// <c>format=source</c>, imported for <c>format=xml</c>. A count other than 1 is the cheapest
    /// signal that a write did something other than update the single type it was addressed to.
    /// </summary>
    public int GeneratedObjectCount { get; set; }
}
