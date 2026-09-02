using TiaMcpServer.Batch;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class TagUpdateSafetySnapshotSourceContractTests
{
    [Fact]
    public void WorkerProgram_DispatchesSnapshotReadThroughWithProjectAndSharedPolicy()
    {
        var source = ReadRepositorySource("TiaMcpServer.OpennessWorker", "Program.cs");

        Assert.Contains("\"read_update_tag_safety_snapshot\" => ReadUpdateTagSafetySnapshot(request)", source, StringComparison.Ordinal);
        Assert.Contains("return WithProject(request, project => Success(", source, StringComparison.Ordinal);
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
}
