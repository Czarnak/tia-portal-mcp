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
        // The SDK may reserialize structuredContent with different escaping, spacing or
        // property order. Its values must still canonicalize to the original text document.
        Assert.Equal(CanonicalJson.Serialize(structured), text);
        return structured.Clone();
    }
}
