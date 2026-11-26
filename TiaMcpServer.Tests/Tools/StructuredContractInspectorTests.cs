using TiaMcpServer.Tests.TestSupport;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Xunit;

namespace TiaMcpServer.Tests.Tools;

/// <summary>
/// The conformance guard is only as strong as <see cref="StructuredContractInspector"/>: a detector
/// that reports nothing for a nested JSON string or a drifted text block would let every structured
/// tool regress while the guard stays green. These cases pin each rule to a hand-written input.
///
/// <para>
/// Fixtures pin the canonical text separately from the structured rendering, which an SDK may
/// reserialize with its own JSON encoder.
/// </para>
/// </summary>
public sealed class StructuredContractInspectorTests
{
    [Fact]
    public void FindViolations_ReturnsNothingForOneCanonicalDocument()
    {
        const string canonical = """{"a":1,"b":{"c":"plain text","d":[1,"x",null]}}""";

        Assert.Empty(StructuredContractInspector.FindViolations(Result(canonical, canonical)));
    }

    [Fact]
    public void FindViolations_AllowsSdkReserializedStructuredContent()
    {
        const string canonical = """{"content":"<Network name=\"äŁ漢\">&'+</Network>"}""";
        using var document = JsonDocument.Parse(canonical);
        var sdkJson = JsonSerializer.Serialize(document.RootElement);
        Assert.NotEqual(canonical, sdkJson);
        Assert.Contains("\\u003C", sdkJson, StringComparison.Ordinal);
        Assert.Contains("\\u00E4", sdkJson, StringComparison.Ordinal);

        Assert.Empty(StructuredContractInspector.FindViolations(Result(canonical, sdkJson)));
    }

    [Fact]
    public void FindViolations_FlagsAStringHoldingAJsonObject()
    {
        const string canonical = """{"payload":"{\"x\":1}","tool":"t"}""";

        var violation = Assert.Single(StructuredContractInspector.FindViolations(Result(canonical, canonical)));

        Assert.Contains("$.payload", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void FindViolations_FlagsAStringHoldingAJsonArrayInsideAnArrayItem()
    {
        const string canonical = """{"batch":{"operations":[{"result":" [1,2] "}]}}""";

        var violation = Assert.Single(StructuredContractInspector.FindViolations(Result(canonical, canonical)));

        Assert.Contains("$.batch.operations[0].result", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void FindViolations_IgnoresStringsThatAreNotJsonObjectsOrArrays()
    {
        const string canonical =
            """{"brace":"{not json","marker":"[OMITTED]","number":"42","quoted":"\"x\"","truth":"true"}""";

        Assert.Empty(StructuredContractInspector.FindViolations(Result(canonical, canonical)));
    }

    [Fact]
    public void FindViolations_FlagsTextThatDiffersFromStructuredContent()
    {
        Assert.NotEmpty(StructuredContractInspector.FindViolations(Result("""{"a":1}""", """{"a":2}""")));
    }

    [Theory]
    [InlineData("""{"b":1,"a":2}""")]
    [InlineData("""{ "a": 2, "b": 1 }""")]
    public void FindViolations_FlagsTextThatIsNotTheCanonicalRendering(string text)
    {
        Assert.NotEmpty(StructuredContractInspector.FindViolations(Result(text, """{"a":2,"b":1}""")));
    }

    [Fact]
    public void FindViolations_FlagsNoncanonicalTextEscapingWithEquivalentStructuredValues()
    {
        const string text = """{"content":"\u003CNetwork\u003E"}""";
        const string structured = """{"content":"<Network>"}""";

        var violation = Assert.Single(StructuredContractInspector.FindViolations(Result(text, structured)));

        Assert.Contains("not the canonical rendering", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void FindViolations_FlagsAMissingStructuredContent()
    {
        Assert.NotEmpty(StructuredContractInspector.FindViolations(Result("""{"a":1}""", structuredJson: null)));
    }

    [Fact]
    public void FindViolations_FlagsNestedJsonInTheTextWhenStructuredContentIsMissing()
    {
        const string legacy = """{"payload":"{\u0022x\u0022:1}","success":true}""";

        var violations = StructuredContractInspector.FindViolations(Result(legacy, structuredJson: null));

        Assert.Contains(violations, violation => violation.Contains("$.payload", StringComparison.Ordinal));
    }

    [Fact]
    public void FindViolations_FlagsALegacyTextResultWithoutThrowing()
    {
        Assert.NotEmpty(StructuredContractInspector.FindViolations(Result("Error: boom", structuredJson: null)));
    }

    [Fact]
    public void FindViolations_FlagsAnythingOtherThanExactlyOneTextBlock()
    {
        const string canonical = """{"a":1}""";
        var result = Result(canonical, canonical);
        result.Content.Add(new TextContentBlock { Text = canonical });

        Assert.NotEmpty(StructuredContractInspector.FindViolations(result));
    }

    private static CallToolResult Result(string text, string? structuredJson)
    {
        JsonElement? structured = null;
        if (structuredJson is not null)
        {
            using var document = JsonDocument.Parse(structuredJson);
            structured = document.RootElement.Clone();
        }

        return new CallToolResult
        {
            Content = new List<ContentBlock> { new TextContentBlock { Text = text } },
            StructuredContent = structured,
        };
    }
}
