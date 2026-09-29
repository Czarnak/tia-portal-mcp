using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tools;
using Xunit;

namespace TiaMcpServer.Tests.Tools;

public sealed class ApprovalSpikeToolsTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("true", false)]
    [InlineData(" 1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsEnabled_OnlyForExactlyOne(string? value, bool expected)
    {
        Assert.Equal(expected, ApprovalSpikeTools.IsEnabled(name =>
            name == ApprovalSpikeTools.EnvironmentVariable ? value : null));
    }

    [Fact]
    public void EnvironmentVariable_HasTheDocumentedName()
    {
        Assert.Equal("TIA_MCP_APPROVAL_SPIKE", ApprovalSpikeTools.EnvironmentVariable);
    }

    [Fact]
    public void Create_ReturnsExactlyTheTwoProbes()
    {
        var names = ApprovalSpikeTools.Create().Select(tool => tool.ProtocolTool.Name).ToArray();

        Assert.Equal(new[] { "spike_marked_probe", "spike_elicitation_probe" }, names);
    }

    [Fact]
    public void Create_MarksOnlyTheMarkerProbe()
    {
        var tools = ApprovalSpikeTools.Create().ToDictionary(t => t.ProtocolTool.Name);

        Assert.True((bool?)tools["spike_marked_probe"].ProtocolTool.Meta?[ToolApprovalMarker.MetaKey]);
        Assert.Null(tools["spike_elicitation_probe"].ProtocolTool.Meta?[ToolApprovalMarker.MetaKey]);
    }

    [Fact]
    public void Create_MarksBothProbesReadOnly()
    {
        foreach (var tool in ApprovalSpikeTools.Create())
        {
            Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint);
        }
    }

    [Fact]
    public void MarkedProbe_ReturnsItsFixedString()
    {
        Assert.Equal(ApprovalSpikeTools.MarkedProbeResult, ApprovalSpikeTools.MarkedProbe());
    }
}
