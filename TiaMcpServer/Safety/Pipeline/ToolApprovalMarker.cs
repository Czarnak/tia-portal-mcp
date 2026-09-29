using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace TiaMcpServer.Safety.Pipeline;

/// <summary>
/// Marks a tool so Claude Code shows its permission prompt on every call in every permission mode.
/// Other clients ignore the marker.
/// </summary>
public static class ToolApprovalMarker
{
    public const string MetaKey = "anthropic/requiresUserInteraction";

    /// <summary>Sets the marker on the tool's <c>tools/list</c> metadata, keeping other keys, and returns the same tool.</summary>
    public static McpServerTool Apply(McpServerTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var meta = tool.ProtocolTool.Meta ?? new JsonObject();
        meta[MetaKey] = true;
        tool.ProtocolTool.Meta = meta;
        return tool;
    }
}
