using Microsoft.Extensions.DependencyInjection;
using TiaMcpServer.Cli;
using TiaMcpServer.Safety.Pipeline;
using Xunit;

namespace TiaMcpServer.Tests.Cli;

public class UserConfirmationParserTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("--confirm-with-user", true)]
    [InlineData("--confirm-with-user=true", true)]
    [InlineData("--confirm-with-user=false", false)]
    [InlineData("--CONFIRM-WITH-USER=FALSE", false)]
    [InlineData("--confirm-with-user=TrUe", true)]
    public void AcceptedForms_ResolveImmutableSingleton(string? flag, bool expected)
    {
        var args = flag is null ? Array.Empty<string>() : new[] { flag };
        var parsed = UserConfirmationParser.Parse(args);
        Assert.True(parsed.IsValid, parsed.Error);
        Assert.Equal(expected, parsed.ConfirmWithUser);
        var services = new ServiceCollection();
        services.AddSingleton(new UserConfirmationOptions(parsed.ConfirmWithUser));
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<UserConfirmationOptions>();
        Assert.Equal(expected, options.ConfirmWithUser);
        Assert.Same(options, provider.GetRequiredService<UserConfirmationOptions>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("0")]
    [InlineData("1")]
    public void MalformedValues_AreRejected(string value)
    {
        var parsed = UserConfirmationParser.Parse(new[] { "--confirm-with-user=" + value });
        Assert.False(parsed.IsValid);
        Assert.NotNull(parsed.Error);
    }

    [Fact]
    public void ContradictoryRepeats_AreRejected()
    {
        Assert.False(UserConfirmationParser.Parse(new[] { "--confirm-with-user", "--confirm-with-user=false" }).IsValid);
        Assert.False(UserConfirmationParser.Parse(new[] { "--confirm-with-user=false", "--confirm-with-user=true" }).IsValid);
        Assert.True(UserConfirmationParser.Parse(new[] { "--confirm-with-user", "--confirm-with-user=TRUE" }).IsValid);
        var off = UserConfirmationParser.Parse(new[] { "--confirm-with-user=false", "--confirm-with-user=FALSE" });
        Assert.True(off.IsValid);
        Assert.False(off.ConfirmWithUser);
    }

    [Fact]
    public void BareFlag_DoesNotConsumeProjectOption_AndPositionalBooleanIsRejected()
    {
        var args = new[] { "--confirm-with-user", "--project", "Line.ap21" };
        var parsed = UserConfirmationParser.Parse(args);
        Assert.True(parsed.IsValid);
        Assert.True(parsed.ConfirmWithUser);
        Assert.Equal(new[] { "--project", "Line.ap21" }, HostArgumentFilter.RemoveAccessModeArguments(args));
        Assert.False(UserConfirmationParser.Parse(new[] { "--confirm-with-user", "false" }).IsValid);
        Assert.True(new UserConfirmationOptions().ConfirmWithUser);
    }
}
