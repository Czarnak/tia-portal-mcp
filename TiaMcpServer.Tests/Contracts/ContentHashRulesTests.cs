using TiaMcpServer.Contracts;
using TiaMcpServer.Safety.Pipeline;
using Xunit;

namespace TiaMcpServer.Tests.Contracts;

public sealed class ContentHashRulesTests
{
    private const string AbcHex = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    [Theory]
    [InlineData("xml")]
    [InlineData("source")]
    public void Compute_Abc_MatchesVectorWithFormatTag(string format)
        => Assert.Equal($"{format}:sha256:{AbcHex}", ContentHashRules.Compute(format, "abc"));

    [Fact]
    public void Sha256Hex_MatchesHostImplementation()
    {
        Assert.Equal(AbcHex, ContentHashRules.Sha256Hex("abc"));
        Assert.Equal(ContentHashes.Sha256Hex("ä€"), ContentHashRules.Sha256Hex("ä€"));
    }

    [Fact]
    public void Compute_UnknownFormat_Throws()
        => Assert.Throws<ArgumentException>(() => ContentHashRules.Compute("json", "abc"));

    [Fact]
    public void TryParse_AcceptsWellFormedHash()
    {
        Assert.True(ContentHashRules.TryParse($"xml:sha256:{AbcHex}", out var format, out var hex));
        Assert.Equal("xml", format);
        Assert.Equal(AbcHex, hex);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("xml:sha256:BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")]
    [InlineData("xml:sha256:abc")]
    [InlineData("json:sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("xml:md5:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    public void TryParse_RejectsUppercaseShortAndUnknownTag(string? hash)
        => Assert.False(ContentHashRules.TryParse(hash, out _, out _));
}
