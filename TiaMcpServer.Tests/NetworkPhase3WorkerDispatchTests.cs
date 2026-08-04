namespace TiaMcpServer.Tests;

using Xunit;

public class NetworkPhase3WorkerDispatchTests
{
    [Fact]
    public void WorkerProgram_DispatchesListNetworkObjectsToTheNarrowHandler()
    {
        var source = File.ReadAllText(FindRepositoryFile("TiaMcpServer.OpennessWorker", "Program.cs"));

        Assert.Contains("\"list_network_objects\" => ListNetworkObjects(request)", source);
        Assert.Contains("private static WorkerResponse ListNetworkObjects(WorkerRequest request)", source);
        Assert.Contains("NetworkObjectIndexReader", source);
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TiaMcpServer.sln")))
            {
                return Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            }
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
