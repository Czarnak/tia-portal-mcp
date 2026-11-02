using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Json;
using Xunit;

namespace TiaMcpServer.Tests.TestSupport;

internal static class CanonicalToolDocument
{
    internal static JsonElement Read(CallToolResult result)
    {
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal(CanonicalJson.Serialize(structured), text);
        Assert.Equal(text, structured.GetRawText());
        return structured.Clone();
    }
}
