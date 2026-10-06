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
    [InlineData("TagMutationService.cs")]
    [InlineData("TagTargetResolver.cs")]
    public void WriteServicesNeverCallFirstMatchFind(string file)
    {
        var source = ReadOpenness(file);
        Assert.DoesNotContain("PlcSoftwareLocator.Find(", source, StringComparison.Ordinal);
        Assert.Contains("PlcSoftwareLocator.FindUnique(", source, StringComparison.Ordinal);
    }
}
