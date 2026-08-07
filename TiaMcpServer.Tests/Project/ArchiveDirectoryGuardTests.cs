using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public class ArchiveDirectoryGuardTests
{
    private const string ProjectFilePath = @"C:\Projects\SimpleProject\SimpleProject.ap21";

    [Fact]
    public void FlagsTheProjectsOwnContainingFolder()
    {
        var result = ArchiveDirectoryGuard.IsWithinProjectFolder(@"C:\Projects\SimpleProject", ProjectFilePath);

        Assert.True(result);
    }

    [Fact]
    public void FlagsTheProjectsOwnFolderRegardlessOfTrailingSeparator()
    {
        var result = ArchiveDirectoryGuard.IsWithinProjectFolder(@"C:\Projects\SimpleProject\", ProjectFilePath);

        Assert.True(result);
    }

    [Fact]
    public void FlagsTheProjectsOwnFolderRegardlessOfCasing()
    {
        var result = ArchiveDirectoryGuard.IsWithinProjectFolder(@"c:\projects\simpleproject", ProjectFilePath);

        Assert.True(result);
    }

    [Fact]
    public void FlagsADirectSubdirectoryOfTheProjectFolder()
    {
        // TIA Portal reportedly auto-deletes such subdirectories in some archiving scenarios;
        // block categorically rather than only the exact reproduced failure case.
        var result = ArchiveDirectoryGuard.IsWithinProjectFolder(@"C:\Projects\SimpleProject\Archives", ProjectFilePath);

        Assert.True(result);
    }

    [Fact]
    public void FlagsADeeplyNestedSubdirectoryOfTheProjectFolder()
    {
        var result = ArchiveDirectoryGuard.IsWithinProjectFolder(@"C:\Projects\SimpleProject\A\B\C", ProjectFilePath);

        Assert.True(result);
    }

    [Fact]
    public void FlagsASubdirectoryRegardlessOfTrailingSeparator()
    {
        var result = ArchiveDirectoryGuard.IsWithinProjectFolder(@"C:\Projects\SimpleProject\Archives\", ProjectFilePath);

        Assert.True(result);
    }

    [Fact]
    public void AllowsTheParentDirectory()
    {
        var result = ArchiveDirectoryGuard.IsWithinProjectFolder(@"C:\Projects", ProjectFilePath);

        Assert.False(result);
    }

    [Fact]
    public void AllowsASiblingDirectory()
    {
        var result = ArchiveDirectoryGuard.IsWithinProjectFolder(@"C:\Projects\Archives", ProjectFilePath);

        Assert.False(result);
    }

    [Fact]
    public void AllowsASiblingDirectoryThatSharesANamePrefix()
    {
        // Regression guard: a naive StartsWith(projectDirectory) without a separator boundary
        // would wrongly flag "SimpleProjectBackup" as nested inside "SimpleProject".
        var result = ArchiveDirectoryGuard.IsWithinProjectFolder(@"C:\Projects\SimpleProjectBackup", ProjectFilePath);

        Assert.False(result);
    }

    [Fact]
    public void ReturnsFalseWhenProjectFilePathIsBlank()
    {
        var result = ArchiveDirectoryGuard.IsWithinProjectFolder(@"C:\Projects\SimpleProject", string.Empty);

        Assert.False(result);
    }

    [Fact]
    public void ReturnsFalseWhenArchiveDirectoryIsBlank()
    {
        var result = ArchiveDirectoryGuard.IsWithinProjectFolder(string.Empty, ProjectFilePath);

        Assert.False(result);
    }
}
