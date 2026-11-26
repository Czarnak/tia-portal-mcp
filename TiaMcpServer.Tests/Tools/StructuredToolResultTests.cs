using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Tools;
using Xunit;

namespace TiaMcpServer.Tests.Tools;

public sealed class StructuredToolResultTests
{
    [Fact]
    public void Create_RelaxedTextAndStructuredContentUseOneDocument()
    {
        const string content = "<Network name=\"\u00E4\u0141\u6F22\">&'+</Network>";

        var result = StructuredToolResult.Create(new { Content = content }, isError: false);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("""{"content":"<Network name=\"äŁ漢\">&'+</Network>"}""", text);
        using var document = JsonDocument.Parse(text);
        Assert.Equal(content, document.RootElement.GetProperty("content").GetString());
        Assert.Equal(text, result.StructuredContent!.Value.GetRawText());
        Assert.True(JsonElement.DeepEquals(document.RootElement, result.StructuredContent.Value));
        Assert.False(result.IsError);
    }

    [Fact]
    public void CreateCanonical_UsesTheAcceptedTextForBothMcpRepresentations()
    {
        const string canonical = "{\"contractVersion\":\"3.0\",\"failure\":null,\"result\":null,\"status\":\"succeeded\",\"warnings\":[]}";

        var result = StructuredToolResult.CreateCanonical(canonical, isError: false);

        Assert.Equal(canonical, Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.Equal(canonical, result.StructuredContent!.Value.GetRawText());
        Assert.False(result.IsError);
    }
}
