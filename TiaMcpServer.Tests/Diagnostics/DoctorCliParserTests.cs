using TiaMcpServer.Cli;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

public class DoctorCliParserTests
{
    [Theory]
    [InlineData("--worker-timeout-minutes", "10")]
    [InlineData("--worker-timeout-minutes=10")]
    [InlineData("--WORKER-TIMEOUT-MINUTES=10")]
    public void Parse_WorkerTimeoutOption_ResolvesTimeoutAndFollowingProject(params string[] timeoutArgs)
    {
        var result = DoctorCliParser.Parse(
            timeoutArgs.Concat(new[] { "--project", "test.ap21" }).ToArray(), _ => "abc");

        Assert.True(result.Valid);
        Assert.Equal("test.ap21", result.ProjectPath);
        Assert.NotNull(result.WorkerTimeout);
        Assert.Equal(10, result.WorkerTimeout.Minutes);
        Assert.Equal(WorkerTimeoutSource.Option, result.WorkerTimeout.Source);
    }

    [Theory]
    [InlineData(null, 5, WorkerTimeoutSource.Default)]
    [InlineData("30", 30, WorkerTimeoutSource.Environment)]
    public void Parse_WorkerTimeoutEnvironment_ResolvesTimeout(string? environmentValue, int minutes, WorkerTimeoutSource source)
    {
        var result = DoctorCliParser.Parse(Array.Empty<string>(),
            name => name == WorkerTimeoutParser.EnvVar ? environmentValue : null);

        Assert.True(result.Valid);
        Assert.NotNull(result.WorkerTimeout);
        Assert.Equal(minutes, result.WorkerTimeout.Minutes);
        Assert.Equal(source, result.WorkerTimeout.Source);
    }

    [Theory]
    [InlineData("61")]
    [InlineData("abc")]
    public void Parse_InvalidWorkerTimeoutOption_ReturnsSharedError(string value)
    {
        var result = DoctorCliParser.Parse(new[] { "--worker-timeout-minutes", value }, _ => null);

        Assert.False(result.Valid);
        Assert.Equal($"Invalid --worker-timeout-minutes value '{value}'. Expected a whole number of minutes from 5 to 60.", result.ParseError);
    }

    [Fact]
    public void Parse_InvalidWorkerTimeoutEnvironment_ReturnsSharedError()
    {
        var result = DoctorCliParser.Parse(new[] { "--json" },
            name => name == WorkerTimeoutParser.EnvVar ? "abc" : null);

        Assert.False(result.Valid);
        Assert.True(result.Json);
        Assert.Equal("Invalid TIA_MCP_WORKER_TIMEOUT_MINUTES value 'abc'. Expected a whole number of minutes from 5 to 60.", result.ParseError);
    }

    [Theory]
    [InlineData("--worker-timeout-minutes")]
    [InlineData("--worker-timeout-minutes", "--project", "test.ap21")]
    [InlineData("--worker-timeout-minutes=")]
    public void Parse_MissingWorkerTimeoutValue_ReturnsSharedError(params string[] args)
    {
        var result = DoctorCliParser.Parse(args, _ => null);

        Assert.False(result.Valid);
        Assert.Equal("--worker-timeout-minutes requires a value.", result.ParseError);
    }

    [Fact]
    public void Parse_DuplicateWorkerTimeoutOption_ReturnsSharedError()
    {
        var result = DoctorCliParser.Parse(new[] { "--worker-timeout-minutes", "10", "--worker-timeout-minutes=20" }, _ => null);

        Assert.False(result.Valid);
        Assert.Equal("--worker-timeout-minutes may be specified only once.", result.ParseError);
    }

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
    public void Parse_DuplicateProjectArguments_ReturnsInvalidInsteadOfCheckingDifferentPathThanHost()
    {
        var result = DoctorCliParser.Parse(new[]
        {
            "--project", @"C:\Projects\A.ap21",
            @"--project=C:\Projects\B.ap21"
        });

        Assert.False(result.Valid);
        Assert.Contains("only once", result.ParseError, StringComparison.OrdinalIgnoreCase);
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
