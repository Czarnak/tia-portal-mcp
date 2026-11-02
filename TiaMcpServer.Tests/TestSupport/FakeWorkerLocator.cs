namespace TiaMcpServer.Tests.TestSupport;

/// <summary>
/// Locates the built FakeWorker in the running test assembly's configuration.
/// </summary>
internal static class FakeWorkerLocator
{
    public static string Locate()
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var candidate = RepositoryFiles.Path(
            "TiaMcpServer.FakeWorker", "bin", configuration, "net10.0",
            "TiaMcpServer.FakeWorker.exe");
        if (File.Exists(candidate))
            return candidate;

        throw new FileNotFoundException("TiaMcpServer.FakeWorker.exe not found; build the solution first.");
    }
}
