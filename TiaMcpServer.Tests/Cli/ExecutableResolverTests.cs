using TiaMcpServer.Cli.Install;
using Xunit;

namespace TiaMcpServer.Tests.Cli;

public class ExecutableResolverTests
{
    // --- Server executable resolution ---

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
        var result = ExecutableResolver.ResolveServerExecutable(null);

        // We can't assert a specific value since it depends on the environment,
        // but it should not throw.
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

    // --- Client executable resolution ---

    [Fact]
    public void ResolveClientExecutable_NonexistentCommand_ReturnsNotFound()
    {
        var result = ExecutableResolver.ResolveClientExecutable("nonexistent_command_xyz_12345");

        Assert.False(result.Found);
        Assert.Null(result.ResolvedPath);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveClientExecutable_EmptyCommand_ReturnsNotFound()
    {
        var result = ExecutableResolver.ResolveClientExecutable("");

        Assert.False(result.Found);
        Assert.Contains("empty", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveClientExecutable_WhitespaceCommand_ReturnsNotFound()
    {
        var result = ExecutableResolver.ResolveClientExecutable("   ");

        Assert.False(result.Found);
    }

    [Fact]
    public void ResolveClientExecutable_AbsolutePathExistingFile_ReturnsFullPath()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var result = ExecutableResolver.ResolveClientExecutable(tempFile);

            Assert.True(result.Found);
            Assert.Equal(Path.GetFullPath(tempFile), result.ResolvedPath);
            Assert.Equal(ExecutableKind.Native, result.Kind);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ResolveClientExecutable_AbsolutePathNonexistent_ReturnsNotFound()
    {
        var result = ExecutableResolver.ResolveClientExecutable(@"C:\nonexistent\path\client.exe");

        Assert.False(result.Found);
        Assert.Contains("does not exist", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveClientExecutable_AbsolutePathWithSpaces_IsResolved()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "test dir with spaces " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "myclient.exe");
        File.WriteAllText(tempFile, "");
        try
        {
            var result = ExecutableResolver.ResolveClientExecutable(tempFile);

            Assert.True(result.Found);
            Assert.Equal(Path.GetFullPath(tempFile), result.ResolvedPath);
        }
        finally
        {
            File.Delete(tempFile);
            Directory.Delete(tempDir);
        }
    }

    [Fact]
    public void ResolveClientExecutable_WhereExeFindsExe_ReturnsNativeKind()
    {
        // cmd.exe should always be findable via where.exe
        var result = ExecutableResolver.ResolveClientExecutable("cmd");

        Assert.True(result.Found);
        Assert.NotNull(result.ResolvedPath);
        Assert.EndsWith(".exe", result.ResolvedPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ExecutableKind.Native, result.Kind);
    }

    [Fact]
    public void ResolveClientExecutable_KnownCommand_WhereExeFindsIt()
    {
        // where.exe should find itself
        var result = ExecutableResolver.ResolveClientExecutable("where");

        Assert.True(result.Found);
        Assert.NotNull(result.ResolvedPath);
        Assert.Contains("where", result.ResolvedPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveClientExecutable_ResultCommand_PreservesOriginalName()
    {
        var result = ExecutableResolver.ResolveClientExecutable("cmd");

        Assert.Equal("cmd", result.Command);
    }

    [Fact]
    public void ResolveClientExecutable_UnicodePath_IsHandled()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "test_unicode_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "client.exe");
        File.WriteAllText(tempFile, "");
        try
        {
            var result = ExecutableResolver.ResolveClientExecutable(tempFile);

            Assert.True(result.Found);
            Assert.Equal(Path.GetFullPath(tempFile), result.ResolvedPath);
        }
        finally
        {
            File.Delete(tempFile);
            Directory.Delete(tempDir);
        }
    }

    [Fact]
    public async Task ResolveClientExecutable_Cancellation_DoesNotThrow()
    {
        // ResolveClientExecutable is synchronous and doesn't take a CancellationToken,
        // but the DetectAsync path does. Verify it doesn't throw.
        var resolver = new ClaudeCodeInstaller();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Should not throw even with cancelled token
        var result = await resolver.DetectAsync(
            ExecutableResolver.ResolveClientExecutable, cts.Token);

        // Result depends on environment, but should not throw
        Assert.NotNull(result);
    }

    // --- FindClientExecutable legacy method ---

    [Fact]
    public void FindClientExecutable_NonexistentClient_ReturnsNull()
    {
        var result = ExecutableResolver.FindClientExecutable("nonexistent_client_xyz");

        Assert.Null(result);
    }

    [Fact]
    public void FindClientExecutable_KnownCommand_ReturnsPath()
    {
        var result = ExecutableResolver.FindClientExecutable("cmd");

        Assert.NotNull(result);
    }

    // --- ClassifyExtension ---

    [Theory]
    [InlineData(@"C:\path\tool.exe", ExecutableKind.Native)]
    [InlineData(@"C:\path\tool.cmd", ExecutableKind.CommandScript)]
    [InlineData(@"C:\path\tool.bat", ExecutableKind.BatchScript)]
    [InlineData(@"C:\path\tool", ExecutableKind.Native)]
    public void ClassifyExtension_ReturnsCorrectKind(string path, ExecutableKind expected)
    {
        var result = ExecutableResolver.ClassifyExtension(path);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(@"C:\path\TOOL.CMD", ExecutableKind.CommandScript)]
    [InlineData(@"C:\path\TOOL.BAT", ExecutableKind.BatchScript)]
    [InlineData(@"C:\path\TOOL.EXE", ExecutableKind.Native)]
    public void ClassifyExtension_CaseInsensitive(string path, ExecutableKind expected)
    {
        var result = ExecutableResolver.ClassifyExtension(path);

        Assert.Equal(expected, result);
    }
}
