using Xunit;

namespace TiaMcpServer.Tests.Worker;

public sealed class PlcWriteServicesSourceContractTests
{
    private static string ReadOpenness(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TiaMcpServer.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, "TiaMcpServer.OpennessWorker", "Openness", file));
    }

    [Theory]
    [InlineData("Plc/TagMutationService.cs")]
    [InlineData("Plc/TagTargetResolver.cs")]
    [InlineData("Block/BlockMutationService.cs")]
    [InlineData("Block/BlockTargetResolver.cs")]
    [InlineData("Plc/PlcTypeTargetResolver.cs")]
    public void WriteServicesNeverCallFirstMatchFind(string file)
    {
        var source = ReadOpenness(file);
        Assert.DoesNotContain("PlcSoftwareLocator.Find(", source, StringComparison.Ordinal);
        Assert.Contains("PlcSoftwareLocator.FindUnique(", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Block/BlockTargetResolver.cs")]
    [InlineData("Plc/PlcTypeTargetResolver.cs")]
    [InlineData("Block/BlockMutationService.cs")]
    public void TargetResolutionNeverThrowsUncategorizedMisses(string file)
    {
        Assert.DoesNotContain("InvalidOperationException", ReadOpenness(file), StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateBlockLogic_ChecksHashBeforeImport()
    {
        var source = ReadOpenness("Block/BlockImporter.cs");
        var xmlCheck = source.IndexOf("RequireContentHash", StringComparison.Ordinal);
        var sourceCheck = source.IndexOf("RequireContentHash", xmlCheck + 1, StringComparison.Ordinal);
        var firstImport = source.IndexOf("boundary.BeforeSiemensCall()", StringComparison.Ordinal);
        Assert.True(xmlCheck >= 0 && sourceCheck > xmlCheck, "both formats check the hash");
        Assert.True(xmlCheck < firstImport, "the XML hash check precedes the first Siemens call");
        Assert.True(sourceCheck < source.IndexOf("ExecuteSource(", StringComparison.Ordinal),
            "the source hash check precedes the generation call");
    }

    [Fact]
    public void UpdateTypeContent_ChecksHashBeforeImport()
    {
        var source = ReadOpenness("Plc/PlcTypeImporter.cs");
        var check = source.IndexOf("RequireContentHash", StringComparison.Ordinal);
        Assert.True(check >= 0);
        Assert.True(check < source.IndexOf("var outcome =", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateBlock_NeverImportsWithOverride()
    {
        var source = ReadOpenness("Block/BlockMutationService.cs");
        Assert.DoesNotContain("ImportOptions.Override", source, StringComparison.Ordinal);
        Assert.Contains("ImportOptions.None", source, StringComparison.Ordinal);
    }
}
