using TiaMcpServer.Cli.Install;
using Xunit;

namespace TiaMcpServer.Tests.Cli;

public class ExecutableResolverTests
{
    [Fact]
    public void ResolveServerExecutable_ExplicitPath_ReturnsPath()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var result = ExecutableResolver.ResolveServerExecutable(tempFile);

            Assert.NotNull(result);
            Assert.Equal(Path.GetFullPath(tempFile), result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ResolveServerExecutable_MissingExplicitPath_ReturnsNull()
    {
        var result = ExecutableResolver.ResolveServerExecutable(@"C:\nonexistent\path\tia-mcp.exe");

        Assert.Null(result);
    }

    [Fact]
    public void ResolveServerExecutable_NullOrEmpty_FallsThroughResolution()
    {
        // This test verifies that null input doesn't throw and falls through to other resolution methods.
        // The actual result depends on whether tia-mcp is installed on the test machine.
        var result = ExecutableResolver.ResolveServerExecutable(null);

        // We can't assert a specific value since it depends on the environment,
        // but it should not throw.
    }

    [Fact]
    public void FindClientExecutable_NonexistentClient_ReturnsNull()
    {
        var result = ExecutableResolver.FindClientExecutable("nonexistent_client_xyz");

        Assert.Null(result);
    }

    [Fact]
    public void ResolveServerExecutable_ExplicitPathWithSpaces_IsResolved()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "test dir with spaces");
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "tia-mcp.exe");
        File.WriteAllText(tempFile, "");
        try
        {
            var result = ExecutableResolver.ResolveServerExecutable(tempFile);

            Assert.NotNull(result);
            Assert.Equal(Path.GetFullPath(tempFile), result);
        }
        finally
        {
            File.Delete(tempFile);
            Directory.Delete(tempDir);
        }
    }
}
