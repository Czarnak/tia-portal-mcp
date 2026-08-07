using TiaMcpServer.Cli.Install;
using Xunit;

namespace TiaMcpServer.Tests.Cli;

public class InstallCliParserTests
{
    [Theory]
    [InlineData("claude-code", ClientKind.ClaudeCode)]
    [InlineData("claude", ClientKind.ClaudeCode)]
    [InlineData("codex", ClientKind.Codex)]
    [InlineData("opencode", ClientKind.OpenCode)]
    [InlineData("mimocode", ClientKind.MiMoCode)]
    [InlineData("mimo", ClientKind.MiMoCode)]
    public void Parse_CanonicalClientNames_SetsClient(string name, ClientKind expected)
    {
        var result = InstallCliParser.Parse(new[] { name });

        Assert.True(result.Valid);
        Assert.Equal(expected, result.Client);
    }

    [Theory]
    [InlineData("CLAUDE")]
    [InlineData("Codex")]
    [InlineData("MIMO")]
    [InlineData("OpenCode")]
    public void Parse_CaseInsensitiveClientNames_Succeeds(string name)
    {
        var result = InstallCliParser.Parse(new[] { name });

        Assert.True(result.Valid);
        Assert.NotNull(result.Client);
    }

    [Fact]
    public void Parse_UnknownClient_ReturnsInvalid()
    {
        var result = InstallCliParser.Parse(new[] { "unknown-client" });

        Assert.False(result.Valid);
        Assert.Contains("Unsupported MCP client", result.ParseError);
    }

    [Fact]
    public void Parse_MissingClient_ReturnsInvalid()
    {
        var result = InstallCliParser.Parse(System.Array.Empty<string>());

        Assert.False(result.Valid);
        Assert.Contains("No MCP client specified", result.ParseError);
    }

    [Fact]
    public void Parse_DefaultServerName_IsTiaPortal()
    {
        var result = InstallCliParser.Parse(new[] { "codex" });

        Assert.True(result.Valid);
        Assert.Equal("tia-portal", result.ServerName);
    }

    [Fact]
    public void Parse_CustomServerName_SetsValue()
    {
        var result = InstallCliParser.Parse(new[] { "codex", "--name", "my-server" });

        Assert.True(result.Valid);
        Assert.Equal("my-server", result.ServerName);
    }

    [Fact]
    public void Parse_CustomServerNameWithEquals_SetsValue()
    {
        var result = InstallCliParser.Parse(new[] { "codex", "--name=my-server" });

        Assert.True(result.Valid);
        Assert.Equal("my-server", result.ServerName);
    }

    [Fact]
    public void Parse_DefaultAccessMode_IsReadOnly()
    {
        var result = InstallCliParser.Parse(new[] { "codex" });

        Assert.True(result.Valid);
        Assert.Equal("read-only", result.AccessMode);
    }

    [Fact]
    public void Parse_ExplicitReadWrite_SetsAccessMode()
    {
        var result = InstallCliParser.Parse(new[] { "codex", "--access-mode", "read-write" });

        Assert.True(result.Valid);
        Assert.Equal("read-write", result.AccessMode);
    }

    [Fact]
    public void Parse_InvalidAccessMode_ReturnsInvalid()
    {
        var result = InstallCliParser.Parse(new[] { "codex", "--access-mode", "invalid" });

        Assert.False(result.Valid);
        Assert.Contains("Invalid access mode", result.ParseError);
    }

    [Fact]
    public void Parse_TiaProject_SetsValue()
    {
        var result = InstallCliParser.Parse(new[] { "codex", "--tia-project", @"C:\Projects\Line.ap21" });

        Assert.True(result.Valid);
        Assert.Equal(@"C:\Projects\Line.ap21", result.TiaProject);
    }

    [Fact]
    public void Parse_ServerPath_SetsValue()
    {
        var result = InstallCliParser.Parse(new[] { "codex", "--server-path", @"C:\tools\tia-mcp.exe" });

        Assert.True(result.Valid);
        Assert.Equal(@"C:\tools\tia-mcp.exe", result.ServerPath);
    }

    [Fact]
    public void Parse_DryRun_SetsFlag()
    {
        var result = InstallCliParser.Parse(new[] { "codex", "--dry-run" });

        Assert.True(result.Valid);
        Assert.True(result.DryRun);
    }

    [Fact]
    public void Parse_Json_SetsFlag()
    {
        var result = InstallCliParser.Parse(new[] { "codex", "--json" });

        Assert.True(result.Valid);
        Assert.True(result.Json);
    }

    [Fact]
    public void Parse_Help_SetsFlag()
    {
        var result = InstallCliParser.Parse(new[] { "--help" });

        Assert.True(result.Valid);
        Assert.True(result.Help);
    }

    [Fact]
    public void Parse_UnknownOption_ReturnsInvalid()
    {
        var result = InstallCliParser.Parse(new[] { "codex", "--unknown" });

        Assert.False(result.Valid);
        Assert.Contains("Unknown install argument", result.ParseError);
    }

    [Fact]
    public void Parse_NameMissingValue_ReturnsInvalid()
    {
        var result = InstallCliParser.Parse(new[] { "codex", "--name" });

        Assert.False(result.Valid);
        Assert.Contains("--name requires a value", result.ParseError);
    }

    [Fact]
    public void Parse_AccessModeMissingValue_ReturnsInvalid()
    {
        var result = InstallCliParser.Parse(new[] { "codex", "--access-mode" });

        Assert.False(result.Valid);
        Assert.Contains("--access-mode requires a value", result.ParseError);
    }

    [Fact]
    public void Parse_TiaProjectMissingValue_ReturnsInvalid()
    {
        var result = InstallCliParser.Parse(new[] { "codex", "--tia-project" });

        Assert.False(result.Valid);
        Assert.Contains("--tia-project requires a value", result.ParseError);
    }

    [Fact]
    public void Parse_ServerPathMissingValue_ReturnsInvalid()
    {
        var result = InstallCliParser.Parse(new[] { "codex", "--server-path" });

        Assert.False(result.Valid);
        Assert.Contains("--server-path requires a value", result.ParseError);
    }

    [Fact]
    public void Parse_MiMoCodeWithJson_ReturnsInvalid()
    {
        var result = InstallCliParser.Parse(new[] { "mimo", "--json" });

        Assert.False(result.Valid);
        Assert.Contains("does not support --json", result.ParseError);
    }

    [Fact]
    public void Parse_AllOptionsCombined_Succeeds()
    {
        var result = InstallCliParser.Parse(new[]
        {
            "codex",
            "--name", "my-tia",
            "--access-mode", "read-write",
            "--tia-project", @"C:\Projects\Line.ap21",
            "--server-path", @"C:\tools\tia-mcp.exe",
            "--dry-run",
            "--json"
        });

        Assert.True(result.Valid);
        Assert.Equal(ClientKind.Codex, result.Client);
        Assert.Equal("my-tia", result.ServerName);
        Assert.Equal("read-write", result.AccessMode);
        Assert.Equal(@"C:\Projects\Line.ap21", result.TiaProject);
        Assert.Equal(@"C:\tools\tia-mcp.exe", result.ServerPath);
        Assert.True(result.DryRun);
        Assert.True(result.Json);
    }
}
