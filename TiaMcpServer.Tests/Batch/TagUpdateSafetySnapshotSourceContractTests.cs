using TiaMcpServer.Batch;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class TagUpdateSafetySnapshotSourceContractTests
{
    [Fact]
    public void WorkerProgram_DispatchesSnapshotReadThroughWithProjectAndSharedPolicy()
    {
        var source = ReadRepositorySource("TiaMcpServer.OpennessWorker", "Program.cs");
        var handler = ReadWorkerHandler(source, "ReadUpdateTagSafetySnapshot");

        Assert.Contains("\"read_update_tag_safety_snapshot\" => ReadUpdateTagSafetySnapshot(request)", source, StringComparison.Ordinal);
        Assert.Contains("return WithProject(request, project => Success(", handler, StringComparison.Ordinal);
        Assert.Contains("=> !OperationPolicyCatalog.RequiresExpectedSessionIdentity(method)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Reader_UsesResolvedPlcNameFromLocator()
    {
        var source = ReadRepositorySource("TiaMcpServer.OpennessWorker", "Openness", "TagUpdateSafetySnapshotReader.cs");

        Assert.Contains("resolved.PlcName", source, StringComparison.Ordinal);
        Assert.DoesNotContain("plcName ?? string.Empty", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Reader_SerializesEveryExactTargetFieldAndPreservesUnavailableFlags()
    {
        var source = ReadRepositorySource("TiaMcpServer.OpennessWorker", "Openness", "TagUpdateSafetySnapshotReader.cs");

        Assert.Contains("resolved.PlcName", source, StringComparison.Ordinal);
        Assert.Contains("resolved.FolderPath", source, StringComparison.Ordinal);
        Assert.Contains("resolved.Table.Name", source, StringComparison.Ordinal);
        Assert.Contains("resolved.Tag.Name", source, StringComparison.Ordinal);
        Assert.Contains("resolved.Tag.DataTypeName", source, StringComparison.Ordinal);
        Assert.Contains("resolved.Tag.LogicalAddress", source, StringComparison.Ordinal);
        Assert.Contains("ReadOptionalFlag(() => resolved.Tag.ExternalAccessible)", source, StringComparison.Ordinal);
        Assert.Contains("ReadOptionalFlag(() => resolved.Tag.ExternalVisible)", source, StringComparison.Ordinal);
        Assert.Contains("ReadOptionalFlag(() => resolved.Tag.ExternalWritable)", source, StringComparison.Ordinal);
        Assert.Contains("catch (NotSupportedException)", source, StringComparison.Ordinal);
        Assert.Contains("return null;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InternalSnapshotRead_IsAbsentFromPublicBatchAndNetworkCatalogs()
    {
        const string method = "read_update_tag_safety_snapshot";
        Assert.DoesNotContain(method, ReadRepositorySource("TiaMcpServer", "Batch", "BatchOperationCatalog.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain(method, ReadRepositorySource("TiaMcpServer", "Network", "NetworkOperationCatalog.cs"), StringComparison.Ordinal);
    }

    private static string ReadRepositorySource(params string[] pathSegments)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "TiaMcpServer.sln")))
        {
            root = root.Parent;
        }

        if (root is null)
        {
            throw new DirectoryNotFoundException("Could not locate the repository root.");
        }

        return File.ReadAllText(Path.Combine(new[] { root.FullName }.Concat(pathSegments).ToArray()));
    }

    private static string ReadWorkerHandler(string source, string methodName)
    {
        var marker = $"    private static WorkerResponse {methodName}(WorkerRequest request)";
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find worker handler '{methodName}'.");

        var next = source.IndexOf("\n    private static WorkerResponse ", start + marker.Length, StringComparison.Ordinal);
        Assert.True(next >= 0, $"Could not find the end of worker handler '{methodName}'.");
        return source[start..next];
    }
}
