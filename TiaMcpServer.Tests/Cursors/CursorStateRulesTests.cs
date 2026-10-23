using TiaMcpServer.Cursors;
using Xunit;

namespace TiaMcpServer.Tests.Cursors;

public class CursorStateRulesTests
{
    private static readonly string Hash = new string('a', 32) + "0123456789abcdef0123456789abcdef";

    [Fact]
    public void IsLowerSha256_AcceptsSixtyFourLowercaseHexCharacters()
    {
        Assert.Equal(64, Hash.Length);
        Assert.True(CursorStateRules.IsLowerSha256(Hash));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("63")]
    [InlineData("65")]
    [InlineData("upper")]
    [InlineData("g")]
    [InlineData("trailing-newline")]
    [InlineData("leading-newline")]
    public void IsLowerSha256_RejectsAnythingElse(string? shape)
    {
        var value = shape switch
        {
            "63" => Hash[..63],
            "65" => Hash + "a",
            "upper" => Hash.ToUpperInvariant(),
            "g" => "g" + Hash[1..],
            "trailing-newline" => Hash + "\n",
            "leading-newline" => "\n" + Hash[..63],
            _ => shape,
        };

        Assert.False(CursorStateRules.IsLowerSha256(value));
    }

    [Fact]
    public void IsValidSnapshotId_AcceptsUpToTheLimit()
    {
        Assert.True(CursorStateRules.IsValidSnapshotId(new string('s', CursorStateRules.MaxSnapshotIdChars)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too-long")]
    public void IsValidSnapshotId_RejectsBlankNullOrOversized(string? shape)
    {
        var value = shape == "too-long" ? new string('s', 129) : shape;

        Assert.False(CursorStateRules.IsValidSnapshotId(value));
    }

    [Theory]
    [InlineData(null, @"C:\Projects\Sample.ap21")]
    [InlineData(@"C:\Projects\Sample.ap21", null)]
    [InlineData(null, null)]
    public void SameProject_IsFalseWhenEitherSideIsNull(string? first, string? second)
    {
        Assert.False(CursorStateRules.SameProject(first, second));
    }

    [Fact]
    public void SameProject_IgnoresCase()
    {
        Assert.True(CursorStateRules.SameProject(@"C:\Projects\Sample.ap21", @"c:\projects\SAMPLE.AP21"));
    }
}
