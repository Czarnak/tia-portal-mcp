namespace TiaMcpServer.Contracts.Safety;

/// <summary>Canonical access-mode spelling shared across process and diagnostic boundaries.</summary>
public static class McpAccessModeNames
{
    public static string ToName(McpAccessMode mode) => mode switch
    {
        McpAccessMode.ReadOnly => "read-only",
        McpAccessMode.ReadWrite => "read-write",
        McpAccessMode.Full => "full",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown access mode.")
    };

    public static bool TryParse(string? value, out McpAccessMode mode)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "read-only": mode = McpAccessMode.ReadOnly; return true;
            case "read-write": mode = McpAccessMode.ReadWrite; return true;
            case "full": mode = McpAccessMode.Full; return true;
            default: mode = McpAccessMode.ReadOnly; return false;
        }
    }
}
