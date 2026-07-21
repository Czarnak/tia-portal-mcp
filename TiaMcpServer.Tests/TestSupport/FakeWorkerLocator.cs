namespace TiaMcpServer.Tests;

/// <summary>
/// Locates the built TiaMcpServer.FakeWorker.exe by walking up from the test assembly's output
/// directory. Shared by every test that drives OpennessWorkerClient against the fake worker.
/// </summary>
internal static class FakeWorkerLocator
{
    public static string Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            foreach (var configuration in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "TiaMcpServer.FakeWorker", "bin", configuration, "net8.0",
                    "TiaMcpServer.FakeWorker.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            directory = directory.Parent!;
        }

        throw new FileNotFoundException("TiaMcpServer.FakeWorker.exe not found; build the solution first.");
    }
}
