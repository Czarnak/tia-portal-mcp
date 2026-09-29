using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using TiaMcpServer.Safety.Pipeline;
using Xunit;

namespace TiaMcpServer.Tests.Safety.Pipeline;

public class ToolApprovalMarkerTests
{
    private static McpServerTool NewTool() => McpServerTool.Create(() => "ok", new McpServerToolCreateOptions { Name = "probe" });

    [Fact]
    public void Apply_sets_the_requires_user_interaction_marker_to_true()
    {
        var tool = ToolApprovalMarker.Apply(NewTool());

        Assert.Equal("anthropic/requiresUserInteraction", ToolApprovalMarker.MetaKey);
        Assert.True(tool.ProtocolTool.Meta![ToolApprovalMarker.MetaKey]!.GetValue<bool>());
    }

    [Fact]
    public void Apply_returns_the_same_tool_instance()
    {
        var tool = NewTool();

        Assert.Same(tool, ToolApprovalMarker.Apply(tool));
    }

    [Fact]
    public void Apply_keeps_existing_meta_keys()
    {
        var tool = NewTool();
        tool.ProtocolTool.Meta = new JsonObject { ["other"] = "kept" };

        ToolApprovalMarker.Apply(tool);

        Assert.Equal("kept", tool.ProtocolTool.Meta["other"]!.GetValue<string>());
        Assert.True(tool.ProtocolTool.Meta[ToolApprovalMarker.MetaKey]!.GetValue<bool>());
    }
}
