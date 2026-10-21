namespace TiaMcpServer.OpennessWorker.Openness.Infrastructure;

internal static class TemporaryDirectoryCleanup
{
    internal static void TryDeleteTempDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception)
        {
            // Cleanup must not replace exported content or the original export failure.
            Console.Error.WriteLine("Temporary export directory cleanup failed: " + exception.Message);
        }
    }
}
