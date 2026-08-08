using System.Text.Json;
using System.Text.Json.Serialization;
using TiaMcpServer.Json;
using Xunit;

namespace TiaMcpServer.Tests.Json;

/// <summary>
/// Contract tests for the repository-defined canonical JSON gate. "Canonical" here means a
/// deterministic byte-for-byte rendering this repository controls — recursive ordinal property
/// ordering, preserved array order, compact output, explicit nulls — not RFC 8785 conformance.
/// </summary>
public class CanonicalJsonTests
{
    private sealed record Nested(string Delta, string Beta);

    private sealed record Sample(
        string Zebra,
        Nested Alpha,
        IReadOnlyList<int> Items,
        string? Missing);

    private static Sample NewSample() => new(
        Zebra: "z-value",
        Alpha: new Nested(Delta: "d-value", Beta: "b-value"),
        Items: new[] { 3, 1, 2 },
        Missing: null);

    [Fact]
    public void Serialize_OrdersObjectPropertiesOrdinallyAtEveryDepth()
    {
        var text = CanonicalJson.Serialize(NewSample());

        Assert.Equal(
            """{"alpha":{"beta":"b-value","delta":"d-value"},"items":[3,1,2],"missing":null,"zebra":"z-value"}""",
            text);
    }

    [Fact]
    public void Serialize_PreservesArrayOrderInsteadOfSortingElements()
    {
        var text = CanonicalJson.Serialize(NewSample());

        using var document = JsonDocument.Parse(text);
        Assert.Equal(
            new[] { 3, 1, 2 },
            document.RootElement.GetProperty("items").EnumerateArray()
                .Select(element => element.GetInt32())
                .ToArray());
    }

    [Fact]
    public void Serialize_EmitsCompactJsonWithNoInsignificantWhitespace()
    {
        var text = CanonicalJson.Serialize(NewSample());

        // Every space in this fixture lives inside a string value, so any whitespace outside
        // quotes would be pure formatting overhead the token budget must not pay for.
        Assert.DoesNotContain('\n', text);
        Assert.DoesNotContain('\r', text);
        Assert.DoesNotContain(' ', text);
        Assert.DoesNotContain('\t', text);
    }

    [Fact]
    public void ToElementAndNormalize_ReturnElementsThatOutliveTheirParsedDocument()
    {
        var element = CanonicalJson.ToElement(NewSample());
        var normalized = CanonicalJson.Normalize<Sample>(
            """{"zebra":"z-value","missing":null,"items":[3,1,2],"alpha":{"delta":"d-value","beta":"b-value"}}""");

        // An element still owned by a disposed JsonDocument throws ObjectDisposedException here.
        GC.Collect();
        GC.WaitForPendingFinalizers();

        Assert.Equal("z-value", element.GetProperty("zebra").GetString());
        Assert.Equal("d-value", normalized.Element.GetProperty("alpha").GetProperty("delta").GetString());
        Assert.Equal("z-value", normalized.Value.Zebra);
        Assert.Equal("b-value", normalized.Value.Alpha.Beta);
        Assert.Equal(new[] { 3, 1, 2 }, normalized.Value.Items);
        Assert.Null(normalized.Value.Missing);
        Assert.Equal(CanonicalJson.Serialize(NewSample()), normalized.Text);
        Assert.True(JsonElement.DeepEquals(element, normalized.Element));
    }

    private sealed class Container
    {
        public required string Name { get; set; }

        public List<int> Numbers { get; set; } = new();

        public Container? Child { get; set; }

        public List<Container> Children { get; set; } = new();

        public string? Note { get; set; }
    }

    [Theory]

    // Root object.
    [InlineData("""{"name":"a","name":"b"}""")]

    // Nested object.
    [InlineData("""{"name":"a","child":{"name":"c","name":"d"}}""")]

    // Object inside an array.
    [InlineData("""{"name":"a","children":[{"name":"c","name":"d"}]}""")]
    public void Deserialize_RejectsDuplicateMembersAtEveryDepth(string json)
    {
        var error = Assert.Throws<JsonException>(() => CanonicalJson.Deserialize<Container>(json));

        Assert.Contains("Duplicate property name", error.Message, StringComparison.Ordinal);
    }

    [Theory]

    // Unknown member.
    [InlineData("""{"name":"a","unknown":1}""")]

    // Wrong casing: the contract is camelCase and matching is case-sensitive, so "Name" is
    // an unmapped member rather than a lenient alias for "name".
    [InlineData("""{"Name":"a"}""")]

    // Missing member the CLR contract marks required.
    [InlineData("""{"numbers":[1]}""")]

    // Wrong JSON type for a declared string.
    [InlineData("""{"name":7}""")]

    // Wrong JSON type for a declared collection.
    [InlineData("""{"name":"a","numbers":{"first":1}}""")]

    // Comments are not part of the contract.
    [InlineData("""{"name":"a"/*note*/}""")]

    // Neither are trailing commas.
    [InlineData("""{"name":"a",}""")]

    // A whole-document null is not a Container.
    [InlineData("null")]
    public void Deserialize_RejectsPayloadsThatDoNotMatchTheDeclaredContract(string json)

        // ThrowsAny: the reader reports lexical violations as JsonReaderException, a JsonException
        // subclass. Callers catch the base type, so the exact subclass is not part of the contract.
        => Assert.ThrowsAny<JsonException>(() => CanonicalJson.Deserialize<Container>(json));

    [Fact]
    public void Deserialize_KeepsNonNullCollectionDefaultsWhenMembersAreAbsent()
    {
        var value = CanonicalJson.Deserialize<Container>("""{"name":"a"}""");

        Assert.Empty(value.Numbers);
        Assert.Empty(value.Children);
        Assert.Null(value.Child);
        Assert.Null(value.Note);
    }

    [Fact]
    public void SerializeAndDeserialize_TreatExplicitNullAsDataRatherThanAbsence()
    {
        var value = CanonicalJson.Deserialize<Container>("""{"name":"a","note":null}""");
        var text = CanonicalJson.Serialize(value);

        Assert.Null(value.Note);

        // An absent field and a null field must not be indistinguishable to a schema consumer,
        // so the canonical writer always emits the null rather than dropping the member.
        Assert.Equal("""{"child":null,"children":[],"name":"a","note":null,"numbers":[]}""", text);
    }

    private sealed record NumberSample(long Big, decimal Exact, double Fraction, bool Flag);

    [Fact]
    public void SerializeAndDeserialize_PreserveNumericAndBooleanValuesExactly()
    {
        // 9007199254740993 is the first integer that loses precision as an IEEE-754 double, and
        // 1.10m carries a trailing zero that a re-formatting writer would silently drop.
        var text = CanonicalJson.Serialize(
            new NumberSample(Big: 9007199254740993L, Exact: 1.10m, Fraction: 0.1d, Flag: true));

        Assert.Equal("""{"big":9007199254740993,"exact":1.10,"flag":true,"fraction":0.1}""", text);
        Assert.Equal(text, CanonicalJson.Serialize(CanonicalJson.Deserialize<NumberSample>(text)));
    }

    private sealed record UnicodeSample(
        [property: JsonPropertyName("\u00C4")] string Umlaut,
        [property: JsonPropertyName("Z")] string Zed,
        [property: JsonPropertyName("a")] string Lower,
        [property: JsonPropertyName("\u0179")] string AccentedZed);

    [Fact]
    public void Serialize_OrdersNonAsciiPropertyNamesByOrdinalCodeUnit()
    {
        var text = CanonicalJson.Serialize(new UnicodeSample("u", "z", "l", "az"));

        using var document = JsonDocument.Parse(text);

        // Ordinal (UTF-16 code unit) order, not culture order. A culture-aware comparer would
        // sort these as Z, Z-acute, a; ordinal keeps U+005A < U+0061 < U+00C4 < U+0179.
        Assert.Equal(
            new[] { "Z", "a", "\u00C4", "\u0179" },
            document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
    }

    [Fact]
    public void Serialize_IsStableAcrossRepeatedCanonicalizeParseCanonicalizeCycles()
    {
        var first = CanonicalJson.Serialize(NewSample());
        var second = CanonicalJson.Serialize(CanonicalJson.Deserialize<Sample>(first));
        var third = CanonicalJson.Serialize(CanonicalJson.Deserialize<Sample>(second));

        Assert.Equal(first, second);
        Assert.Equal(second, third);
    }

    [Fact]
    public void Normalize_RunsTheCallerSuppliedValidatorInsideTheStrictGate()
    {
        // Required-member rules a CLR initializer cannot express (an explicit null defeating a
        // non-null collection default) belong to the owning contract, not to CanonicalJson.
        static void Validate(Container container)
        {
            if (container.Numbers is null)
            {
                throw new JsonException("'numbers' must not be null.");
            }
        }

        var accepted = CanonicalJson.Normalize<Container>("""{"name":"a","numbers":[1]}""", Validate);
        Assert.Equal(new[] { 1 }, accepted.Value.Numbers);

        var error = Assert.Throws<JsonException>(
            () => CanonicalJson.Normalize<Container>("""{"name":"a","numbers":null}""", Validate));
        Assert.Equal("'numbers' must not be null.", error.Message);
    }
}
