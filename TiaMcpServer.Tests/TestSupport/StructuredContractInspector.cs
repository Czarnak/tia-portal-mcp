using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Json;

namespace TiaMcpServer.Tests.TestSupport;

/// <summary>
/// Checks a <c>tools/call</c> result against the delivery rules of the structured JSON contract
/// (docs/roadmap/json-contract.md): exactly one text block, a <c>structuredContent</c> document, the
/// text being the canonical rendering of that same document, and no string anywhere that holds a
/// JSON object or array.
///
/// <para>
/// Returns every violation found; an empty list means the result conforms. Only a string that
/// parses as a JSON object or array counts as nested JSON: scalars inside strings, prose that
/// merely starts with a bracket, and unparseable fragments are not reported.
/// </para>
/// </summary>
internal static class StructuredContractInspector
{
    public static IReadOnlyList<string> FindViolations(CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var violations = new List<string>();

        var structured = result.StructuredContent;
        if (structured is null)
        {
            violations.Add("structuredContent is missing.");
        }

        JsonElement? text = null;
        if (result.Content.Count == 1 && result.Content[0] is TextContentBlock textBlock)
        {
            text = ReadText(textBlock.Text, structured, violations);
        }
        else
        {
            violations.Add(
                $"Expected exactly one text content block but found {result.Content.Count} content block(s).");
        }

        // Scan the document a client reads: structuredContent when present, otherwise the text.
        if ((structured ?? text) is JsonElement document)
        {
            FindNestedJson(document, "$", violations);
        }

        return violations;
    }

    /// <summary>Parses the text block and checks it against the canonical and structured forms.</summary>
    private static JsonElement? ReadText(string text, JsonElement? structured, List<string> violations)
    {
        JsonElement parsed;
        try
        {
            using var document = JsonDocument.Parse(text);
            parsed = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            violations.Add("The text block is not a JSON document.");
            return null;
        }

        if (!string.Equals(CanonicalJson.Serialize(parsed), text, StringComparison.Ordinal))
        {
            violations.Add("The text block is not the canonical rendering of its document.");
        }

        if (structured is JsonElement structuredDocument && !JsonElement.DeepEquals(parsed, structuredDocument))
        {
            violations.Add("The text block and structuredContent are different documents.");
        }

        return parsed;
    }

    private static void FindNestedJson(JsonElement element, string path, List<string> violations)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    FindNestedJson(property.Value, $"{path}.{property.Name}", violations);
                }

                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    FindNestedJson(item, $"{path}[{index++}]", violations);
                }

                break;

            case JsonValueKind.String when HoldsJsonContainer(element.GetString()!):
                violations.Add($"{path} is a string holding a JSON object or array.");
                break;
        }
    }

    private static bool HoldsJsonContainer(string value)
    {
        var trimmed = value.AsSpan().Trim();
        if (trimmed.Length < 2 || (trimmed[0] != '{' && trimmed[0] != '['))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
