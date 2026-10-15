using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Vci;
using Xunit;

namespace TiaMcpServer.Tests.Vci;

public sealed class VciBreadcrumbTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tia-mcp-vci-breadcrumb", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_root, "nested", "breadcrumb.json");

    [Fact]
    public void MatchingId_ReturnsValueAndDeletesFile()
    {
        var breadcrumb = new VciBreadcrumb("request-a", "item-a", "export",
            new List<ProjectTreeSelectorSegment> { new() { NodeType = "FB", Name = "LeafA" } }, null);
        Assert.True(VciBreadcrumbFile.TryWrite(FilePath, breadcrumb));
        var result = VciBreadcrumbFile.Take(FilePath, "request-a");
        Assert.NotNull(result);
        Assert.Equal("export", result.Operation);
        Assert.Equal("LeafA", Assert.Single(result.Path).Name);
        Assert.Null(result.Format);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void ForeignId_IsIgnoredAndDeleted()
    {
        Assert.True(VciBreadcrumbFile.TryWrite(FilePath, new VciBreadcrumb("foreign", "item", "export", new(), "SCL")));
        Assert.Null(VciBreadcrumbFile.Take(FilePath, "request-a"));
        Assert.False(File.Exists(FilePath));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"requestId\":\"request-a\"}")]
    public void MalformedFile_IsIgnoredAndDeleted(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, content);
        Assert.Null(VciBreadcrumbFile.Take(FilePath, "request-a"));
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void MissingFile_ReturnsNull()
        => Assert.Null(VciBreadcrumbFile.Take(FilePath, "request-a"));

    [Fact]
    public void TryWrite_UnwritableDirectory_ReturnsFalse()
    {
        Directory.CreateDirectory(_root);
        var blocker = Path.Combine(_root, "file");
        File.WriteAllText(blocker, "keep");
        Assert.False(VciBreadcrumbFile.TryWrite(Path.Combine(blocker, "breadcrumb.json"), new VciBreadcrumb("id", "item", "export", new(), null)));
        Assert.Equal("keep", File.ReadAllText(blocker));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
