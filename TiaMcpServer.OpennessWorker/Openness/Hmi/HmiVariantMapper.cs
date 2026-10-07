using System.Globalization;
using System.Text.Json;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness.Hmi;

/// <summary>
/// Encodes a <c>System.Object</c> property value as an <see cref="HmiVariant"/> (spike S3 encoding table):
/// <c>type</c> is the runtime type's FullName (null for null), <c>value</c> a real JSON scalar.
/// A string is a JSON string, a boolean a JSON boolean, integral and floating types a JSON number
/// (NaN and the infinities a JSON string), an enum its name, anything else <c>ToString()</c> with the invariant culture.
/// </summary>
public static class HmiVariantMapper
{
    public static HmiVariant Map(object? value)
    {
        if (value is null)
        {
            return new HmiVariant(null, null);
        }

        return new HmiVariant(value.GetType().FullName, Encode(value));
    }

    /// <summary>
    /// An <c>AlarmParameterTags</c>-style value: the string elements in order. Any other runtime shape is
    /// null plus one message, never a partial list.
    /// </summary>
    public static string[]? MapStringList(object? value, ICollection<string> messages, string what)
    {
        if (value is IEnumerable<string> strings)
        {
            return strings.ToArray();
        }

        messages.Add(value is null
            ? $"{what} was null."
            : $"{what} has an unsupported runtime type '{value.GetType().FullName}'.");
        return null;
    }

    private static JsonElement Encode(object value)
    {
        switch (value)
        {
            case string s:
                return Clone(JsonSerializer.Serialize(s));
            case bool b:
                return Clone(b ? "true" : "false");
            case Enum e:
                return Clone(JsonSerializer.Serialize(e.ToString()));
            case double d:
                return double.IsNaN(d) || double.IsInfinity(d)
                    ? Clone(JsonSerializer.Serialize(d.ToString(CultureInfo.InvariantCulture)))
                    : Clone(JsonSerializer.Serialize(d));
            case float f:
                return float.IsNaN(f) || float.IsInfinity(f)
                    ? Clone(JsonSerializer.Serialize(f.ToString(CultureInfo.InvariantCulture)))
                    : Clone(JsonSerializer.Serialize(f));
            case decimal m:
                return Clone(m.ToString(CultureInfo.InvariantCulture));
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                return Clone(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
            case IFormattable formattable:
                return Clone(JsonSerializer.Serialize(formattable.ToString(null, CultureInfo.InvariantCulture)));
            default:
                return Clone(JsonSerializer.Serialize(value.ToString() ?? string.Empty));
        }
    }

    private static JsonElement Clone(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
