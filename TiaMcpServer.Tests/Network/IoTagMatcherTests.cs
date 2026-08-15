using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// Pure, TIA-free tests for <see cref="IoTagMatcher"/>: exact interval and I/O-area equality,
/// multiple tags per channel, and the conservative no-overlap / no-first-match rules.
/// </summary>
public class IoTagMatcherTests
{
    private static IoAbsoluteIoAddress Address(string text)
    {
        Assert.True(IoLogicalAddressFormatter.TryParse(text, out var address), $"'{text}' should parse.");
        return address!.Value;
    }

    [Fact]
    public void DigitalInputTag_MatchesDigitalInputChannel()
    {
        Assert.True(IoTagMatcher.MatchesChannel(Address("%I4.0"), "Input", 32, 1));
    }

    [Fact]
    public void DigitalOutputTag_MatchesDigitalOutputChannel()
    {
        Assert.True(IoTagMatcher.MatchesChannel(Address("%Q4.0"), "Output", 32, 1));
    }

    [Fact]
    public void AnalogInputTag_MatchesAnalogInputWordChannel()
    {
        Assert.True(IoTagMatcher.MatchesChannel(Address("%IW64"), "Input", 512, 16));
    }

    [Fact]
    public void AnalogOutputTag_MatchesAnalogOutputDwordChannel()
    {
        Assert.True(IoTagMatcher.MatchesChannel(Address("%QD64"), "Output", 512, 32));
    }

    [Fact]
    public void MultipleTags_WithTheSameNormalizedInterval_AllMatchTheOneChannel()
    {
        var channelIoType = "Input";
        var channelStartBit = 32;
        var channelWidthBits = 1u;

        Assert.True(IoTagMatcher.MatchesChannel(Address("%I4.0"), channelIoType, channelStartBit, channelWidthBits));
        Assert.True(IoTagMatcher.MatchesChannel(Address("%I4.0"), channelIoType, channelStartBit, channelWidthBits));
        Assert.True(IoTagMatcher.MatchesChannel(Address("%IB4"), channelIoType, 32, 8u));

        // A byte tag at the same byte as a bit channel is a DIFFERENT interval: no overlap match.
        Assert.False(IoTagMatcher.MatchesChannel(Address("%IB4"), channelIoType, channelStartBit, channelWidthBits));
    }

    [Theory]
    [InlineData("%I5.0", "Input", 32, 1u)]
    [InlineData("%I4.1", "Input", 32, 1u)]
    [InlineData("%IW62", "Input", 504, 16u)]
    [InlineData("%I4.0", "Input", 33, 1u)]
    [InlineData("%IB4", "Input", 33, 1u)]
    public void Tag_WithADifferentInterval_DoesNotMatch(string tagText, string channelIoType, int? channelStartBit, uint? channelWidthBits)
    {
        Assert.False(IoTagMatcher.MatchesChannel(Address(tagText), channelIoType, channelStartBit, channelWidthBits));
    }

    [Fact]
    public void SameNumericAddress_InDifferentIoAreas_DoesNotMatch()
    {
        // %I4.0 and %Q4.0 both sit at byte 4, bit 0 — the area is part of the identity.
        Assert.False(IoTagMatcher.MatchesChannel(Address("%I4.0"), "Output", 32, 1));
        Assert.False(IoTagMatcher.MatchesChannel(Address("%Q4.0"), "Input", 32, 1));
        Assert.True(IoTagMatcher.MatchesChannel(Address("%Q4.0"), "Output", 32, 1));
    }

    [Theory]
    [InlineData("Input", null, 1u)]
    [InlineData("Input", 32, null)]
    [InlineData(null, 32, 1u)]
    [InlineData("Complex", 32, 1u)]
    [InlineData("Unrecognized", 32, 1u)]
    [InlineData("Input", -1, 1u)]
    public void ChannelWithoutCompleteNormalizedEvidence_NeverMatches(string? channelIoType, int? channelStartBit, uint? channelWidthBits)
    {
        Assert.False(IoTagMatcher.MatchesChannel(Address("%I4.0"), channelIoType, channelStartBit, channelWidthBits));
    }

    [Fact]
    public void NoFirstMatchFallback_OnlyExactIntervalMatchesAreAccepted()
    {
        // A list of tags with different intervals: every non-exact tag is skipped, and the exact
        // tag still matches. There is no "closest" or "first acceptable" fallback.
        var tags = new[] { "%I3.0", "%I4.1", "%I4.0", "%IB4", "%IW64" };
        var matches = tags
            .Select(Address)
            .Where(tag => IoTagMatcher.MatchesChannel(tag, "Input", 32, 1))
            .ToArray();

        Assert.Single(matches);
        Assert.Equal("I", matches[0].Area);
        Assert.Equal(32, matches[0].Interval.StartBit);
        Assert.Equal(1u, matches[0].Interval.BitCount);
    }
}
