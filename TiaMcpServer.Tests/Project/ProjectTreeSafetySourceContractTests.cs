using System;
using System.IO;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public sealed class ProjectTreeSafetySourceContractTests
{
    [Theory]
    [InlineData("read_create_block_safety_snapshot")]
    [InlineData("read_create_block_group_safety_snapshot")]
    [InlineData("read_delete_block_group_safety_snapshot")]
    public void InternalTreeSafetyRead_IsIdentityRequiredSafetyRead(string method)
    {
        Assert.Equal(OperationCapability.SafetyRead, OperationPolicyCatalog.GetCapability(method));
        Assert.True(OperationPolicyCatalog.RequiresExpectedSessionIdentity(method));
    }

    [Fact]
    public void WorkerGuard_RejectsMissingExpectedSessionIdentity_ForSafetyReads()
    {
        var program = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "TiaMcpServer.OpennessWorker", "Program.cs"));
        Assert.Contains("AllowsMissingExpectedSessionIdentity(request.Method)", program, StringComparison.Ordinal);
        Assert.Contains("!OperationPolicyCatalog.RequiresExpectedSessionIdentity(method)", program, StringComparison.Ordinal);
        foreach (var method in new[] { "read_create_block_safety_snapshot", "read_create_block_group_safety_snapshot", "read_delete_block_group_safety_snapshot" })
            Assert.True(OperationPolicyCatalog.RequiresExpectedSessionIdentity(method));
    }

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
    public void ProjectTreeSafetySnapshotReader_RequiresAuthoritativeXmlRatherThanCompanionOnlyExport()
    {
        var source = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "TiaMcpServer.OpennessWorker", "Openness", "ProjectTreeSafetySnapshotReader.cs"));

        Assert.Contains("BlockExporter.ExportForSafety(project, blockPath)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BlockExporter.Export(project, blockPath, SourceFormatNames.Xml)", source, StringComparison.Ordinal);
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
