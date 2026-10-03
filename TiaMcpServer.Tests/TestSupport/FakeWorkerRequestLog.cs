namespace TiaMcpServer.Tests;

/// <summary>
/// Records only child-worker method names. Use only in RealWorkerProcessCollection or
/// Mcp protocol serial, with the consumer's worker access mode stated explicitly.
/// </summary>
internal sealed class FakeWorkerRequestLog : IDisposable
{
    private const string Variable = "TIA_MCP_FAKE_WORKER_REQUEST_LOG";
    private readonly string? _previous = Environment.GetEnvironmentVariable(Variable);
    private readonly string _path;

    public FakeWorkerRequestLog(string directory)
    {
        _path = Path.Combine(directory, "requests-" + Guid.NewGuid().ToString("N") + ".log");
        Environment.SetEnvironmentVariable(Variable, _path);
    }

    public string[] Methods() => File.Exists(_path) ? File.ReadAllLines(_path) : Array.Empty<string>();
    public void Dispose() => Environment.SetEnvironmentVariable(Variable, _previous);
}
