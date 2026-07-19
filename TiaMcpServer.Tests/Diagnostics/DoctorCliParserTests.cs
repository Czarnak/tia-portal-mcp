using TiaMcpServer.Cli;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

public class DoctorCliParserTests
{
    [Fact]
    public void Parse_EmptyArgs_ReturnsValidDefaults()
    {
        var result = DoctorCliParser.Parse(Array.Empty<string>());

        Assert.True(result.Valid);
        Assert.False(result.Json);
        Assert.False(result.Verbose);
        Assert.Null(result.ProjectPath);
        Assert.Null(result.ParseError);
    }

    [Fact]
    public void Parse_JsonFlag_SetsJson()
    {
        var result = DoctorCliParser.Parse(new[] { "--json" });

        Assert.True(result.Valid);
        Assert.True(result.Json);
    }

    [Fact]
    public void Parse_VerboseFlag_SetsVerbose()
    {
        var result = DoctorCliParser.Parse(new[] { "--verbose" });

        Assert.True(result.Valid);
        Assert.True(result.Verbose);
    }

    [Fact]
    public void Parse_ProjectFlag_SetsProjectPath()
    {
        var result = DoctorCliParser.Parse(new[] { "--project", @"C:\Projects\Line.ap21" });

        Assert.True(result.Valid);
        Assert.Equal(@"C:\Projects\Line.ap21", result.ProjectPath);
    }

    [Fact]
    public void Parse_ProjectEquals_SetsProjectPath()
    {
        var result = DoctorCliParser.Parse(new[] { "--project=C:\\Projects\\Line.ap21" });

        Assert.True(result.Valid);
        Assert.Equal(@"C:\Projects\Line.ap21", result.ProjectPath);
    }

    [Fact]
    public void Parse_CombinedFlags_AllSet()
    {
        var result = DoctorCliParser.Parse(new[] { "--json", "--verbose", "--project", "test.ap21" });

        Assert.True(result.Valid);
        Assert.True(result.Json);
        Assert.True(result.Verbose);
        Assert.Equal("test.ap21", result.ProjectPath);
    }

    [Fact]
    public void Parse_UnknownArg_ReturnsInvalid()
    {
        var result = DoctorCliParser.Parse(new[] { "--bogus" });

        Assert.False(result.Valid);
        Assert.Contains("Unknown doctor argument", result.ParseError);
    }

    [Fact]
    public void Parse_ProjectFlagWithoutValue_ReturnsInvalid()
    {
        var result = DoctorCliParser.Parse(new[] { "--project" });

        Assert.False(result.Valid);
        Assert.Contains("requires a value", result.ParseError);
    }

    [Fact]
    public void Parse_ProjectFlagFollowedByAnotherFlag_ReturnsInvalid()
    {
        var result = DoctorCliParser.Parse(new[] { "--project", "--json" });

        Assert.False(result.Valid);
        Assert.True(result.Json);
        Assert.Contains("requires a value", result.ParseError);
    }

    [Fact]
    public void Parse_ProjectEqualsWithoutValue_ReturnsInvalid()
    {
        var result = DoctorCliParser.Parse(new[] { "--project=" });

        Assert.False(result.Valid);
        Assert.Contains("requires a value", result.ParseError);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_ProjectFlagWithBlankSeparateValue_ReturnsInvalid(string projectPath)
    {
        var result = DoctorCliParser.Parse(new[] { "--json", "--project", projectPath });

        Assert.False(result.Valid);
        Assert.True(result.Json);
        Assert.Null(result.ProjectPath);
        Assert.Contains("requires a value", result.ParseError);
    }

    [Fact]
    public void Parse_HelpFlag_ReturnsValidHelpRequest()
    {
        var result = DoctorCliParser.Parse(new[] { "--help" });

        Assert.True(result.Valid);
        Assert.True(result.Help);
    }

    [Fact]
    public void Parse_CaseInsensitive_FlagsWork()
    {
        var result = DoctorCliParser.Parse(new[] { "--JSON", "--VERBOSE" });

        Assert.True(result.Valid);
        Assert.True(result.Json);
        Assert.True(result.Verbose);
    }
}
