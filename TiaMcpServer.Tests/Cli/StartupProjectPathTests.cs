using TiaMcpServer.Cli;
using Xunit;

namespace TiaMcpServer.Tests.Cli;

public class StartupProjectPathTests
{
    [Theory]
    [InlineData(@"C:\Projects\Line.ap21")]
    [InlineData(@"C:\Projects\Line.AP21")]
    [InlineData(@"C:\Projects\Local.amc21")]
    [InlineData(@"\\server\share\Line.ap21")]
    public void Validate_AcceptsAbsoluteAp21OrAmc21(string path)
    {
        Assert.Null(ConfiguredProjectPath.Validate(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("Line.ap21")]
    [InlineData(@"C:Line.ap21")]
    [InlineData(@"\Projects\Line.ap21")]
    [InlineData(@"C:\Projects\Line.als21")]
    [InlineData(@"C:\Projects\Line.ap20")]
    [InlineData(@"C:\Projects\Line")]
    public void Validate_RejectsRelativeOrUnsupportedPath(string? path)
    {
        Assert.Equal("must be an absolute .ap21 or .amc21 engineering path.", ConfiguredProjectPath.Validate(path));
    }

    [Theory]
    [InlineData("--project", "Line.ap21")]
    [InlineData("--project", @"C:\Projects\Line.txt")]
    [InlineData(@"--project=C:\Projects\Line.als21")]
    public async Task StartupRejectsInvalidProjectOption(params string[] args)
    {
        var result = await StartupProcess.RunAsync(null, args);

        Assert.Equal(1, result.ExitCode);
        Assert.StartsWith("Error: --project must be an absolute .ap21 or .amc21 engineering path.",
            result.StandardError, StringComparison.Ordinal);
        Assert.Empty(result.StandardOutput);
    }

    [Theory]
    [InlineData("Line.ap21")]
    [InlineData(@"C:\Projects\Line.zap21")]
    public async Task StartupRejectsInvalidProjectEnvironment(string value)
    {
        var result = await StartupProcess.RunAsync(new Dictionary<string, string>
        {
            [ConfiguredProjectPath.EnvVar] = value
        });

        Assert.Equal(1, result.ExitCode);
        Assert.StartsWith($"Error: {ConfiguredProjectPath.EnvVar} must be an absolute .ap21 or .amc21 engineering path.",
            result.StandardError, StringComparison.Ordinal);
        Assert.Empty(result.StandardOutput);
    }

    [Theory]
    [InlineData("--project", @"C:\Projects\Line.ap21")]
    [InlineData(@"--project=C:\Projects\Local.amc21")]
    public async Task StartupAcceptsAbsoluteProjectOption(params string[] args)
    {
        var result = await StartupProcess.RunAsync(null, args);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Error:", result.StandardError, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\Projects\Line.ap21")]
    [InlineData("   ")]
    public async Task StartupAcceptsAbsoluteOrBlankProjectEnvironment(string value)
    {
        var result = await StartupProcess.RunAsync(new Dictionary<string, string>
        {
            [ConfiguredProjectPath.EnvVar] = value
        });

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Error:", result.StandardError, StringComparison.Ordinal);
    }
}
