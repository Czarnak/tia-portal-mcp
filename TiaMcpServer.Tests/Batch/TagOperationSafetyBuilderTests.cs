using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class TagOperationSafetyBuilderTests
{
    [Theory]
    [InlineData(null, "/", "PLC_1/Tag tables/Inputs")]
    [InlineData(" /Area//Signals/ ", "/Area/Signals", "PLC_1/Tag tables/Area/Signals/Inputs")]
    public void TableIdentity_NormalizesFolderSeparators(string? folder, string expectedFolder, string expectedPath)
    {
        var table = TagOperationSafetySnapshotBuilder.BuildTableIdentity("PLC_1", folder, "Inputs");
        Assert.Equal(expectedFolder, table.FolderPath);
        Assert.Equal(expectedPath, table.CanonicalPath);
    }

    [Fact]
    public void DeleteSnapshot_PreservesCompleteExportAndHashesExactContent()
    {
        var table = new TagTableSafetyIdentityInfo("PLC_1", "/", "Inputs", "PLC_1/Tag tables/Inputs");
        var snapshot = TagOperationSafetySnapshotBuilder.BuildDeleteTagTableSnapshot(table, "<Document />");
        Assert.Equal("<Document />", snapshot.ExportedSimaticMl);
        Assert.Equal(12, snapshot.CharacterCount);
        Assert.Same(table, snapshot.TargetTable);
        Assert.Equal("f0a79fec323a922b8967d8bfee43ade7c6e12520a50c3fc6dd46fd58135a0a3e", snapshot.ExportSha256);
        const string detailed = "<Document GeneratedOn=\"preserve\"><Unknown ID=\"7\"> \r\n<!--keep-->x</Unknown></Document>";
        Assert.Equal(detailed, TagOperationSafetySnapshotBuilder.BuildDeleteTagTableSnapshot(table, detailed).ExportedSimaticMl);
    }

    [Fact]
    public void CollisionSelection_BindsOnlyRequestedValuesAcrossTablesAndMarksTheTarget()
    {
        var target = new TagCollisionProbeInfo("tag-name", "Start", "PLC_1/Tag tables/A/Start", "%I0.0", false);
        var sibling = new TagCollisionProbeInfo("tag-name", "Start", "PLC_1/Tag tables/B/Start", "%I0.1", false);
        var unrelated = new TagCollisionProbeInfo("tag-name", "Other", "PLC_1/Tag tables/C/Other", "%I0.2", false);
        var candidates = new[] { unrelated, sibling, target };
        var names = TagOperationSafetySnapshotBuilder.SelectCollisions("tag-name", candidates, "Start", target.CanonicalPath);
        Assert.Equal(new[] { target with { IsTarget = true }, sibling }, names);
        var addresses = TagOperationSafetySnapshotBuilder.SelectCollisions("logical-address", candidates, "%I0.1", target.CanonicalPath);
        Assert.Equal(new[] { sibling with { Kind = "logical-address" } }, addresses);
        Assert.Empty(TagOperationSafetySnapshotBuilder.SelectCollisions("logical-address", candidates, null, null));
    }

    [Fact]
    public void CollisionOrdering_IsOrdinalAndIndependentOfEnumerationOrder()
    {
        var a = new TagCollisionProbeInfo("tag-name", "Start", "PLC_1/Tag tables/A/Start", "%I0.0", false);
        var b = new TagCollisionProbeInfo("tag-name", "Start", "PLC_1/Tag tables/B/Start", "%I0.0", true);
        Assert.Equal(new[] { a, b }, TagOperationSafetySnapshotBuilder.OrderCollisions(new[] { b, a }));
        Assert.Equal(new[] { a, b }, TagOperationSafetySnapshotBuilder.OrderCollisions(new[] { a, b }));
    }

    [Fact]
    public void TagIdentity_PreservesExternalFlagsIncludingUnavailableValues()
    {
        var table = TagOperationSafetySnapshotBuilder.BuildTableIdentity("PLC_1", "/", "Inputs");
        var tag = TagOperationSafetySnapshotBuilder.BuildTagIdentity(table, "Start", "Bool", "%I0.0", false, null, true);
        Assert.Equal("PLC_1/Tag tables/Inputs/Start", tag.CanonicalPath);
        Assert.False(tag.ExternalAccessible);
        Assert.Null(tag.ExternalVisible);
        Assert.True(tag.ExternalWritable);
        var constant = TagOperationSafetySnapshotBuilder.BuildConstantIdentity(table, "DebounceMs", "Int", "25");
        Assert.Equal("PLC_1/Tag tables/Inputs/DebounceMs", constant.CanonicalPath);
        Assert.Equal("25", constant.Value);
    }

    [Fact]
    public void DeleteTagTableSnapshot_UsesTheVerifiedTimestampFreeExportCallFirst()
    {
        var path = Source("TiaMcpServer.OpennessWorker/Openness/TagOperationSafetySnapshotReader.cs");
        Assert.True(File.Exists(path), "The deterministic tag-table safety reader must exist.");
        var text = File.ReadAllText(path);
        Assert.Contains("ExportOptions.None", text, StringComparison.Ordinal);
        Assert.Contains(".Export(new FileInfo(", text, StringComparison.Ordinal);
        Assert.Contains("DocumentInfoOptions.None", text, StringComparison.Ordinal);
    }

    private static string Source(string relative)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relative));
}
