using Xunit;

namespace TiaMcpServer.Tests.TestSupport;

public sealed class RepositoryFilesTests
{
    [Fact]
    public void FindRoot_WalksNestedOutputsAndUsesNearestSolutionMarker()
    {
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        File.WriteAllText(Path.Combine(directory.Path, "TiaMcpServer.slnx"), "");
        var checkout = Directory.CreateDirectory(Path.Combine(directory.Path, "checkout"));
        File.WriteAllText(Path.Combine(checkout.FullName, "TiaMcpServer.slnx"), "");
        var output = Directory.CreateDirectory(Path.Combine(checkout.FullName, "custom", "nested", "output"));

        Assert.Equal(checkout.FullName, RepositoryFiles.FindRoot(output.FullName));
    }

    [Fact]
    public void FindRoot_WithoutSolutionMarker_ReportsStartingDirectory()
    {
        var startDirectory = Path.Combine(Path.GetPathRoot(RepositoryFiles.Root)!, Guid.NewGuid().ToString("N"));

        var error = Assert.Throws<DirectoryNotFoundException>(() => RepositoryFiles.FindRoot(startDirectory));

        Assert.Contains(startDirectory, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Path_CombinesPartsFromTheCurrentCheckout()
    {
        var path = RepositoryFiles.Path("TiaMcpServer.Tests", "TiaMcpServer.Tests.csproj");

        Assert.Equal(Path.Combine(RepositoryFiles.Root, "TiaMcpServer.Tests", "TiaMcpServer.Tests.csproj"), path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void ReadSource_NormalizesWindowsNewlines()
    {
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        File.WriteAllText(Path.Combine(directory.Path, "source.cs"), "first\r\nsecond\n");

        Assert.Equal("first\nsecond\n", RepositoryFiles.ReadSource(directory.Path, "source.cs"));
    }

    [Theory]
    [InlineData("", "match", 0)]
    [InlineData("missing", "match", 0)]
    [InlineData("Match match", "match", 1)]
    [InlineData("aaaaa", "aa", 2)]
    public void CountOccurrences_CountsOrdinalNonOverlappingMatches(string source, string value, int expected)
        => Assert.Equal(expected, RepositoryFiles.CountOccurrences(source, value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CountOccurrences_RejectsAnEmptySearchValue(string? value)
        => Assert.ThrowsAny<ArgumentException>(() => RepositoryFiles.CountOccurrences("source", value!));
}
