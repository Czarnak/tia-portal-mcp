using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TiaMcpServer.Json;

/// <summary>
/// Strict typed parsing and repository-defined canonical serialization for the opt-in structured
/// JSON contract.
///
/// <para>
/// "Canonical" here means a deterministic rendering this repository defines and controls:
/// recursive ordinal property ordering, preserved array order, explicit nulls, and compact UTF-8
/// output. It is deliberately NOT a claim of RFC 8785 compliance — the RFC's complete I-JSON,
/// ECMAScript-number, Unicode and UTF-8 conformance surface is out of scope.
/// </para>
///
/// <para>
/// Reading is strict on purpose. Comments and trailing commas are rejected, duplicate property
/// names are rejected at every depth, property names are matched case-sensitively against
/// camelCase, and unmapped members are refused. A payload that does not match its declared
/// contract must fail loudly rather than silently decoding to a partially populated object.
/// </para>
/// </summary>
public static class CanonicalJson
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow
    };

    private static readonly JsonSerializerOptions StrictRead = CreateStrictRead();

    private static readonly JsonSerializerOptions CanonicalWrite = CreateCanonicalWrite();

    /// <summary>
    /// Parses <paramref name="json"/> into <typeparamref name="T"/> under the strict rules above.
    /// Throws <see cref="JsonException"/> for anything that does not match the declared contract.
    /// </summary>
    public static T Deserialize<T>(string json)
    {
        using var document = JsonDocument.Parse(json, DocumentOptions);
        RejectDuplicateProperties(document.RootElement);
        return document.Deserialize<T>(StrictRead)
            ?? throw new JsonException($"Expected a {typeof(T).Name} value but the JSON was null.");
    }

    /// <summary>Renders <paramref name="value"/> as canonical JSON text.</summary>
    public static string Serialize<T>(T value)
    {
        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(value, CanonicalWrite),
            DocumentOptions);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            WriteCanonical(document.RootElement, writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// Renders <paramref name="value"/> canonically and returns a detached <see cref="JsonElement"/>
    /// that outlives the <see cref="JsonDocument"/> it was parsed from.
    /// </summary>
    public static JsonElement ToElement<T>(T value) => Detach(Serialize(value));

    /// <summary>
    /// Strictly parses <paramref name="json"/> as <typeparamref name="T"/> and returns the value
    /// together with its canonical text and a detached element of that same text — one round trip,
    /// three representations that cannot drift apart.
    ///
    /// <para>
    /// <paramref name="validate"/> runs inside the same gate for rules the CLR type cannot express
    /// — an explicit null defeating a non-null collection default, for example. It belongs to the
    /// type's owning contract, not here: this class stays domain-free. A validator rejects by
    /// throwing <see cref="JsonException"/>, so callers handle contract violations in one place.
    /// </para>
    /// </summary>
    public static (T Value, string Text, JsonElement Element) Normalize<T>(
        string json,
        Action<T>? validate = null)
    {
        var value = Deserialize<T>(json);
        validate?.Invoke(value);
        var text = Serialize(value);
        return (value, text, Detach(text));
    }

    private static JsonElement Detach(string canonicalText)
    {
        using var document = JsonDocument.Parse(canonicalText, DocumentOptions);

        // Clone before the document is disposed: an element still owned by a disposed document
        // throws ObjectDisposedException the moment a caller reads it.
        return document.RootElement.Clone();
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject()
                    .OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                // Array order is data, never formatting: it is preserved exactly as produced.
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(item, writer);
                }

                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!seen.Add(property.Name))
                    {
                        throw new JsonException(
                            $"Duplicate property name '{property.Name}' is not allowed.");
                    }

                    RejectDuplicateProperties(property.Value);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    RejectDuplicateProperties(item);
                }

                break;
        }
    }

    private static JsonSerializerOptions CreateStrictRead()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    private static JsonSerializerOptions CreateCanonicalWrite()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,

            // Explicit nulls are part of the contract: an absent field and a null field must not
            // be indistinguishable to a consumer reading the declared output schema.
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
