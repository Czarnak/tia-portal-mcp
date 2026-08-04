using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

public class DeviceItemInfo
{
    public string? Name { get; set; }

    public string? TypeIdentifier { get; set; }

    public int? PositionNumber { get; set; }

    public string? Address { get; set; }

    /// <summary>
    /// True when a deterministic selector for this device item was successfully constructed.
    /// False when required evidence (device name, item path) was missing or unreadable.
    /// </summary>
    public bool Selectable { get; set; }

    /// <summary>
    /// Selector that can be forwarded directly into an inspect_network_object request.
    /// Null when <see cref="Selectable"/> is false.
    /// </summary>
    public NetworkObjectSelectorInfo? Selector { get; set; }

    /// <summary>
    /// Diagnostic messages explaining why the selector is absent or degraded.
    /// Empty when <see cref="Selectable"/> is true.
    /// </summary>
    public List<string> SelectorDiagnostics { get; set; } = new();

    /// <summary>
    /// Always present. An item without network interfaces reports an empty list, so a consumer
    /// walking the hardware tree never has to distinguish "none" from "not modelled".
    /// </summary>
    public List<NetworkInterfaceInfo> NetworkInterfaces { get; set; } = new List<NetworkInterfaceInfo>();

    /// <summary>Always present. A leaf item reports an empty list rather than null.</summary>
    public List<DeviceItemInfo> Items { get; set; } = new List<DeviceItemInfo>();
}
