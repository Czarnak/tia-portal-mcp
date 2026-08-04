using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

public class IoSystemInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The IO system's number within its subnet. Null when it could not be read; a null number must
    /// never satisfy a write selector.
    /// </summary>
    public int? Number { get; set; }

    public string? IoControllerName { get; set; }

    /// <summary>True when a deterministic selector for this IO system was successfully constructed.</summary>
    public bool Selectable { get; set; }

    /// <summary>
    /// Selector that can be forwarded directly into an inspect_network_object request.
    /// Null when <see cref="Selectable"/> is false.
    /// </summary>
    public NetworkObjectSelectorInfo? Selector { get; set; }

    /// <summary>Diagnostic messages explaining why the selector is absent or degraded.</summary>
    public List<string> SelectorDiagnostics { get; set; } = new();

    public List<string> ConnectedDeviceNames { get; set; } = new List<string>();
}
