using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class TagOperationPrerequisiteBaselineTests
{
    [Fact]
    public void Program_RegistersWriteBatchTools()
    {
        var text = File.ReadAllText(Source("TiaMcpServer/Program.cs"));

        Assert.Contains(".WithTools<WriteBatchTools>()", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateTag_BaselineStillCarriesPr3MutableFlagsThroughTheRegisteredWritePath()
    {
        var capability = File.ReadAllText(Source("TiaMcpServer.Contracts/OperationCapability.cs"));
        var catalog = File.ReadAllText(Source("TiaMcpServer/Batch/BatchOperationCatalog.cs"));
        var invoker = File.ReadAllText(Source("TiaMcpServer/Batch/BatchWorkerInvoker.cs"));

        Assert.Contains("SafetyRead", capability, StringComparison.Ordinal);
        Assert.Contains("\"externalAccessible\"", catalog, StringComparison.Ordinal);
        Assert.Contains("\"externalVisible\"", catalog, StringComparison.Ordinal);
        Assert.Contains("\"externalWritable\"", catalog, StringComparison.Ordinal);
        Assert.Contains(
            "client.UpdateTagAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.NewName, op.DataType, op.LogicalAddress, op.ExternalAccessible, op.ExternalVisible, op.ExternalWritable, op.IsSafety, op.ProjectPath)",
            invoker,
            StringComparison.Ordinal);
    }

    private static string Source(string relative)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relative));
}
