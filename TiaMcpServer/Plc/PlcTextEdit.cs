using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TiaMcpServer.Plc;

/// <summary>One unique replacement against the unchanged original block document.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PlcTextEdit
{
    private string _oldText = string.Empty;
    private string _newText = string.Empty;

    [JsonRequired]
    [Description("Nonblank text matching exactly once in the original block document. CRLF and LF compare equally; all edits use the original base and their spans must not overlap.")]
    public string OldText
    {
        get => _oldText;
        set => _oldText = value ?? throw new JsonException("oldText must not be null.");
    }

    [JsonRequired]
    [Description("Replacement text; an empty string deletes the matched span. Replacement line endings follow the matched span, then the base document, then CRLF.")]
    public string NewText
    {
        get => _newText;
        set => _newText = value ?? throw new JsonException("newText must not be null.");
    }
}
