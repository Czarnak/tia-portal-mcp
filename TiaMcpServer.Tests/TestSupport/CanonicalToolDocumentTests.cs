using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Json;
using Xunit;
using Xunit.Sdk;

namespace TiaMcpServer.Tests.TestSupport;

public sealed class CanonicalToolDocumentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_ReturnsADetachedDocumentForSuccessOrRejection(bool isError)
    {
        JsonElement actual;
        using (var source = JsonDocument.Parse("""{"value":1}"""))
        {
            var result = Result(source.RootElement, """{"value":1}""");
            result.IsError = isError;
            actual = CanonicalToolDocument.Read(result);
        }

        Assert.Equal(1, actual.GetProperty("value").GetInt32());
    }

    [Theory]
    [InlineData("{\"userValue\":1}")]
    [InlineData("[1,2]")]
    public void Read_AllowsJsonTextInAContentString(string content)
    {
        var source = CanonicalJson.ToElement(new { content });

        var actual = CanonicalToolDocument.Read(Result(source, CanonicalJson.Serialize(source)));

        Assert.Equal(content, actual.GetProperty("content").GetString());
    }

    [Theory]
    [InlineData("{\"a\":2,\"b\":1}")]
    [InlineData("{ \"a\": 1, \"b\": 2 }")]
    [InlineData("{\"b\":2,\"a\":1}")]
    public void Read_RejectsDifferentOrNoncanonicalText(string text)
    {
        var source = CanonicalJson.ToElement(new { a = 1, b = 2 });

        Assert.ThrowsAny<XunitException>(() => CanonicalToolDocument.Read(Result(source, text)));
    }

    [Fact]
    public void Read_RequiresStructuredContent()
    {
        var result = Result(null, "{}");

        Assert.ThrowsAny<XunitException>(() => CanonicalToolDocument.Read(result));
    }

    [Theory]
    [InlineData("{\"b\":2,\"a\":1}")]
    [InlineData("{ \"a\": 1, \"b\": 2 }")]
    public void Read_RequiresStructuredContentToUseTheDeliveredRendering(string json)
    {
        using var source = JsonDocument.Parse(json);
        var text = CanonicalJson.Serialize(source.RootElement);

        Assert.ThrowsAny<XunitException>(() => CanonicalToolDocument.Read(Result(source.RootElement, text)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Read_RequiresExactlyOneContentBlock(int count)
    {
        var result = Result(CanonicalJson.ToElement(new { }), "{}");
        result.Content = Enumerable.Range(0, count)
            .Select(_ => (ContentBlock)new TextContentBlock { Text = "{}" }).ToList();

        Assert.ThrowsAny<XunitException>(() => CanonicalToolDocument.Read(result));
    }

    [Fact]
    public void Read_RequiresTheSingleBlockToBeText()
    {
        var result = Result(CanonicalJson.ToElement(new { }), "{}");
        result.Content = [new ImageContentBlock { Data = new byte[] { 0 }, MimeType = "image/png" }];

        Assert.ThrowsAny<XunitException>(() => CanonicalToolDocument.Read(result));
    }

    private static CallToolResult Result(JsonElement? structured, string text) => new()
    {
        StructuredContent = structured,
        Content = [new TextContentBlock { Text = text }]
    };
}
