using TiaMcpServer.Contracts;

namespace TiaMcpServer.Tests;

/// <summary>
/// Declares the project already open in the simulated TIA Portal UI before the child worker
/// starts. Use only from test collections that disable parallelization; the environment is
/// process-wide and is restored when the fixture is disposed.
/// </summary>
internal sealed class FakeWorkerUiOpenProject : IDisposable
{
    private const string Variable = "TIA_MCP_FAKE_WORKER_UI_OPEN_PROJECT";
    private readonly string? _previous = Environment.GetEnvironmentVariable(Variable);

    public FakeWorkerUiOpenProject(string? projectPath)
        => Environment.SetEnvironmentVariable(Variable, ProjectPathNormalization.Canonicalize(projectPath));

    /// <summary>
    /// Unbound transport and MCP protocol tests send scenario keywords relative to the
    /// FakeWorker executable's working directory. Resolve the UI-open fixture from that same
    /// directory so the source and requested path represent the same project.
    /// </summary>
    public static FakeWorkerUiOpenProject ForWorkerRelativePath(string requestedPath)
        => new(Path.IsPathFullyQualified(requestedPath)
            ? requestedPath
            : Path.Combine(Path.GetDirectoryName(FakeWorkerLocator.Locate())!, requestedPath));

    public void Dispose() => Environment.SetEnvironmentVariable(Variable, _previous);
}
