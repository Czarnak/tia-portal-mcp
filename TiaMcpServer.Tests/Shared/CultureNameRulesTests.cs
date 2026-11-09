using TiaMcpServer.Contracts.Shared;
using Xunit;

namespace TiaMcpServer.Tests.Shared;

public sealed class CultureNameRulesTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(" \t", null)]
    [InlineData("not a culture", null)]
    [InlineData("en-us", "en-US")]
    [InlineData("en", "en")]
    [InlineData("zz-ZZ", "zz-ZZ")]
    public void CanonicalName_RejectsBlankInvalidAndInvariantNames(string? culture, string? expected)
        => Assert.Equal(expected, CultureNameRules.CanonicalName(culture));
}
