using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Vci;
using TiaMcpServer.Vci;
using Xunit;

namespace TiaMcpServer.Tests.Vci;

public sealed class VciBreadcrumbTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tia-mcp-vci-breadcrumb", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_root, "nested", "breadcrumb.json");

    [Fact]
    public void DefaultLocations_AreDistinctWithinThePerUserDirectory()
    {
        var first = new VciBreadcrumbLocation();
        var second = new VciBreadcrumbLocation();

        Assert.NotEqual(first.Path, second.Path);
        Assert.Equal(Path.GetDirectoryName(VciBreadcrumbFile.DefaultPath), Path.GetDirectoryName(first.Path));
        Assert.Equal(Path.GetDirectoryName(first.Path), Path.GetDirectoryName(second.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentLocations_InterleavedCompletionPreservesOtherHostsEvidence(bool takeCompleted)
    {
        var firstPath = Path.Combine(_root, Path.GetFileName(new VciBreadcrumbLocation().Path));
        var secondPath = Path.Combine(_root, Path.GetFileName(new VciBreadcrumbLocation().Path));
        var first = new VciBreadcrumbLocation(firstPath);
        var second = new VciBreadcrumbLocation(secondPath);
        Assert.Equal(firstPath, first.Path);
        Assert.Equal(secondPath, second.Path);
        Assert.True(VciBreadcrumbFile.TryWrite(first.Path, new VciBreadcrumb("request-a", "item-a", "export",
            new() { new() { NodeType = "FB", Name = "LeafA" } }, "SimaticML")));
        Assert.True(VciBreadcrumbFile.TryWrite(second.Path, new VciBreadcrumb("request-b", "item-b", "sync_to_workspace",
            new() { new() { NodeType = "FC", Name = "LeafB" } }, "SCL")));

        if (takeCompleted)
        {
            var completed = VciBreadcrumbFile.Take(second.Path, "request-b");
            Assert.NotNull(completed);
            Assert.Equal("LeafB", Assert.Single(completed.Path).Name);
        }
        else VciBreadcrumbFile.TryDelete(second.Path);

        Assert.False(File.Exists(second.Path));
        Assert.True(File.Exists(first.Path));
        var pending = VciBreadcrumbFile.Take(first.Path, "request-a");
        Assert.NotNull(pending);
        Assert.Equal("LeafA", Assert.Single(pending.Path).Name);
        Assert.Equal("export", pending.Operation);
        Assert.Equal("SimaticML", pending.Format);
        Assert.False(File.Exists(first.Path));
    }

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
