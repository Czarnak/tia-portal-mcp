using System;
using System.IO;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public sealed class ProjectTreeLiveHarnessContractTests
{
    [Fact]
    public void LiveHarness_IsReadOnlyAndCoversTheApprovedEvidenceMatrix()
    {
        var source = File.ReadAllText(RepositoryFile("scripts", "live-test-project-tree-v3.ps1"));
        Assert.Contains("--read-only", source, StringComparison.Ordinal);
        Assert.Contains("1..3", source, StringComparison.Ordinal);
        Assert.Contains("Measure-InitialBrowse", source, StringComparison.Ordinal);
        Assert.Contains("Read-AllSnapshotPages", source, StringComparison.Ordinal);
        Assert.Contains("Reconstruct-TypedSelector", source, StringComparison.Ordinal);
        Assert.Contains("target_not_found", source, StringComparison.Ordinal);
        Assert.Contains("target_ambiguous", source, StringComparison.Ordinal);
        Assert.Contains("invalid_selector", source, StringComparison.Ordinal);
        Assert.Contains("Assert-CanonicalRepresentationsEqual", source, StringComparison.Ordinal);
        Assert.Contains("ConvertTo-Json", source, StringComparison.Ordinal);
        Assert.Contains("project-tree-v3-evidence.json", source, StringComparison.Ordinal);

        foreach (var writeToolName in new[]
        {
            "apply_write_batch",
            "archive_project",
            "close_project",
            "compile_check",
            "create_project",
            "network_write",
            "open_project",
            "preview_write_batch",
            "save_project",
            "save_project_as",
        })
        {
            Assert.DoesNotContain(writeToolName, source, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("confirm=true", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepositoryFile(params string[] pathSegments)
        => Path.GetFullPath(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }.Concat(pathSegments).ToArray()));
}
