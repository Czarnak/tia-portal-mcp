using TiaMcpServer.Cli.Install;
using Xunit;

namespace TiaMcpServer.Tests.Cli;

public class ClientAliasResolverTests
{
    [Theory]
    [InlineData("claude-code", ClientKind.ClaudeCode)]
    [InlineData("claude", ClientKind.ClaudeCode)]
    [InlineData("codex", ClientKind.Codex)]
    [InlineData("opencode", ClientKind.OpenCode)]
    [InlineData("mimocode", ClientKind.MiMoCode)]
    [InlineData("mimo", ClientKind.MiMoCode)]
    public void MapToClientKind_KnownAliases_ReturnsExpected(string alias, ClientKind expected)
    {
        var result = ClientAliasResolver.MapToClientKind(alias);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("CLAUDE")]
    [InlineData("Claude")]
    [InlineData("CODEX")]
    [InlineData("Codex")]
    [InlineData("MIMO")]
    [InlineData("Mimo")]
    [InlineData("OPENCODE")]
    [InlineData("OpenCode")]
    public void MapToClientKind_CaseInsensitive_ReturnsExpected(string alias)
    {
        var result = ClientAliasResolver.MapToClientKind(alias);

        Assert.NotNull(result);
    }

    [Theory]
    [InlineData("vscode")]
    [InlineData("unknown")]
    [InlineData("emacs")]
    [InlineData("")]
    [InlineData("  ")]
    public void MapToClientKind_UnknownOrNull_ReturnsNull(string alias)
    {
        var result = ClientAliasResolver.MapToClientKind(alias);

        Assert.Null(result);
    }

    [Fact]
    public void MapToClientKind_WithWhitespace_TrimsCorrectly()
    {
        var result = ClientAliasResolver.MapToClientKind("  claude  ");

        Assert.Equal(ClientKind.ClaudeCode, result);
    }
}
