using System.Text.Json;
using TiaMcpServer.Json;
using Xunit;

namespace TiaMcpServer.Tests;

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
}
