using TiaMcpServer.Cli;
using Xunit;

namespace TiaMcpServer.Tests.Cli;

public class WorkerTimeoutParserTests
{
    private static WorkerTimeoutParseResult Parse(string[] args, string? environment = null)
        => WorkerTimeoutParser.Parse(args, name => name == WorkerTimeoutParser.EnvVar ? environment : null);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ReturnsDefaultWhenNeitherSet(string? environment)
    {
        var result = Parse([], environment);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(5, result.Minutes);
        Assert.Equal(TimeSpan.FromMinutes(5), result.Timeout);
        Assert.Equal(WorkerTimeoutSource.Default, result.Source);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData("--worker-timeout-minutes", "10")]
    [InlineData("--worker-timeout-minutes=10")]
    [InlineData("--WORKER-TIMEOUT-MINUTES", "10")]
    [InlineData("--WORKER-TIMEOUT-MINUTES=10")]
    public void ReadsCommandLine(params string[] args)
    {
        var result = Parse(args);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(10, result.Minutes);
        Assert.Equal(TimeSpan.FromMinutes(10), result.Timeout);
        Assert.Equal(WorkerTimeoutSource.Option, result.Source);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ReadsEnvironment()
    {
        var result = Parse([], "30");

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(30, result.Minutes);
        Assert.Equal(WorkerTimeoutSource.Environment, result.Source);
        Assert.Null(result.Error);
    }

    [Fact]
    public void CommandLineWinsOverMalformedEnvironment()
    {
        var result = Parse(["--worker-timeout-minutes", "10"], "abc");

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(10, result.Minutes);
        Assert.Equal(WorkerTimeoutSource.Option, result.Source);
    }

    [Theory]
    [InlineData("5", 5)]
    [InlineData("60", 60)]
    [InlineData(" 10 ", 10)]
    [InlineData("010", 10)]
    public void AcceptsBounds(string text, int minutes)
    {
        var option = Parse(["--worker-timeout-minutes", text]);
        var environment = Parse([], text);

        Assert.True(option.IsValid, option.Error);
        Assert.Equal(minutes, option.Minutes);
        Assert.True(environment.IsValid, environment.Error);
        Assert.Equal(minutes, environment.Minutes);
    }

    [Theory]
    [InlineData("4")]
    [InlineData("61")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("+10")]
    [InlineData("10.5")]
    [InlineData("1e1")]
    [InlineData("abc")]
    [InlineData("2147483648")]
    public void RejectsOutOfRangeOrMalformed(string text)
    {
        var result = Parse(["--worker-timeout-minutes", text]);

        Assert.False(result.IsValid);
        Assert.Equal($"Invalid --worker-timeout-minutes value '{text}'. Expected a whole number of minutes from 5 to 60.", result.Error);
    }

    [Fact]
    public void RejectsMalformedEnvironment()
    {
        var result = Parse([], "abc");

        Assert.False(result.IsValid);
        Assert.Equal("Invalid TIA_MCP_WORKER_TIMEOUT_MINUTES value 'abc'. Expected a whole number of minutes from 5 to 60.", result.Error);
    }

    [Theory]
    [InlineData("--worker-timeout-minutes")]
    [InlineData("--worker-timeout-minutes", "--read-only")]
    [InlineData("--worker-timeout-minutes=")]
    [InlineData("--worker-timeout-minutes", "   ")]
    public void RejectsMissingValue(params string[] args)
    {
        var result = Parse(args);

        Assert.False(result.IsValid);
        Assert.Equal("--worker-timeout-minutes requires a value.", result.Error);
    }

    [Fact]
    public void RejectsRepeatedFlag()
    {
        var result = Parse(["--worker-timeout-minutes", "10", "--WORKER-TIMEOUT-MINUTES=30"]);

        Assert.False(result.IsValid);
        Assert.Equal("--worker-timeout-minutes may be specified only once.", result.Error);
    }

    [Fact]
    public void IgnoresOtherOptions()
    {
        var result = Parse(["--max-item-chars", "100000", "--access-mode", "full"]);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(5, result.Minutes);
        Assert.Equal(WorkerTimeoutSource.Default, result.Source);
    }

    [Theory]
    [InlineData(WorkerTimeoutSource.Default, "default")]
    [InlineData(WorkerTimeoutSource.Option, "option")]
    [InlineData(WorkerTimeoutSource.Environment, "environment")]
    public void SourceNameUsesContractSpelling(WorkerTimeoutSource source, string expected)
        => Assert.Equal(expected, WorkerTimeoutParseResult.SourceName(source));
}
