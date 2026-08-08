using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Json;

namespace TiaMcpServer.Tools;

/// <summary>
/// Builds an MCP tool result whose text block and <c>structuredContent</c> are the same canonical
/// JSON document.
///
/// <para>
/// Both representations come from a single <see cref="CanonicalJson.Serialize{T}"/> call, so they
/// cannot drift: an agent that reads the text and an agent that reads the structured content see
/// byte-identical data. This is opt-in — tools still on the text contract are unaffected.
/// </para>
/// </summary>
public static class StructuredToolResult
{
    public static CallToolResult Create<TResponse>(TResponse response, bool isError)
    {
        var text = CanonicalJson.Serialize(response);
        using var document = JsonDocument.Parse(text);
        return new CallToolResult
        {
            Content = new List<ContentBlock> { new TextContentBlock { Text = text } },
            StructuredContent = document.RootElement.Clone(),
            IsError = isError
        };
    }
}
