using System;
using System.IO;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public sealed class ProjectTreeSafetySourceContractTests
{
    [Fact]
    public void ProjectTreeSafetySnapshotReader_UsesBlockTargetResolverForDeterministicOwnership()
    {
        var source = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "TiaMcpServer.OpennessWorker", "Openness", "ProjectTreeSafetySnapshotReader.cs"));

        Assert.Contains("BlockTargetResolver.ResolveOwnerForDeterministicPath", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SoftwareUnitName = null", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectTreeSafetySnapshotReader_ReusesAuthoritativeBlockExporterAndOrdersCollections()
    {
        var source = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "TiaMcpServer.OpennessWorker", "Openness", "ProjectTreeSafetySnapshotReader.cs"));

        Assert.Contains("BlockExporter.Export", source, StringComparison.Ordinal);
        Assert.Contains("OrderBy", source, StringComparison.Ordinal);
        Assert.Contains("StringComparer.Ordinal", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectTreeSafetySnapshotReader_CanonicalizesAncestorsFromResolvedGroupNames()
    {
        var source = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "TiaMcpServer.OpennessWorker", "Openness", "ProjectTreeSafetySnapshotReader.cs"));

        Assert.Contains("BlockTargetResolver.ResolveBlockGroupPath", source, StringComparison.Ordinal);
        Assert.Contains("resolvedParent.ResolvedFolderNames", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadAncestors(owner.RootBlocksPath, address.FolderPath)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BlockMutationService_UsesBlockTargetResolverForDeterministicOwnership()
    {
        var source = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "TiaMcpServer.OpennessWorker", "Openness", "BlockMutationService.cs"));

        Assert.Contains("BlockTargetResolver.ResolveOwnerForDeterministicPath", source, StringComparison.Ordinal);
        Assert.DoesNotContain("plcSoftware.BlockGroup, address.FolderPath", source, StringComparison.Ordinal);
    }

    private static string GetRepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
