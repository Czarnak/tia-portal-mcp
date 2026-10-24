namespace TiaMcpServer.Tests.TestSupport;

/// <summary>
/// Records only child-worker method names. The environment variable is process-wide, so use only
/// in a serial collection such as RealWorkerProcessCollection or Mcp protocol serial
/// (ProcessEnvironmentIsolationTests enforces this), with the consumer's worker access mode stated
/// explicitly.
/// </summary>
internal sealed class FakeWorkerRequestLog : IDisposable
{
    private const string Variable = "TIA_MCP_FAKE_WORKER_REQUEST_LOG";
    private readonly string? _previous = Environment.GetEnvironmentVariable(Variable);
    public FakeWorkerRequestLog(string directory)
    {
        Path = System.IO.Path.Combine(directory, "requests-" + Guid.NewGuid().ToString("N") + ".log");
        Environment.SetEnvironmentVariable(Variable, Path);
    }

    public string Path { get; }

    public string[] Methods() => File.Exists(Path) ? File.ReadAllLines(Path) : Array.Empty<string>();
    public void Dispose() => Environment.SetEnvironmentVariable(Variable, _previous);
}
