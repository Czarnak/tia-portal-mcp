using TiaMcpServer.Contracts.Project;

namespace TiaMcpServer.Tests.TestSupport;

/// <summary>
/// Declares the project already open in the simulated TIA Portal UI before the child worker
/// starts. Use only from test collections that disable parallelization; the environment is
/// process-wide and is restored when the fixture is disposed.
/// </summary>
internal sealed class FakeWorkerUiOpenProject : IDisposable
{
    private const string Variable = "TIA_MCP_FAKE_WORKER_UI_OPEN_PROJECT";
    private const string StatusGroundingVariable = "TIA_MCP_FAKE_WORKER_STATUS_GROUNDING";
    private readonly string? _previous = Environment.GetEnvironmentVariable(Variable);
    private readonly string? _previousStatusGrounding = Environment.GetEnvironmentVariable(StatusGroundingVariable);

    /// <param name="projectPath">The project already open in the simulated UI.</param>
    /// <param name="groundsStatus">
    /// When true, the launched worker answers its first identity-less <c>get_project_status</c> for
    /// that project before any scenario script runs, so a binding fixture can ground every scenario.
    /// Later identity-less status requests reach the scenario script again.
    /// </param>
    public FakeWorkerUiOpenProject(string? projectPath, bool groundsStatus = false)
    {
        Environment.SetEnvironmentVariable(Variable, ProjectPathNormalization.Canonicalize(projectPath));
        if (groundsStatus) Environment.SetEnvironmentVariable(StatusGroundingVariable, "1");
    }

    /// <summary>
    /// Unbound transport and MCP protocol tests send scenario keywords relative to the
    /// FakeWorker executable's working directory. Resolve the UI-open fixture from that same
    /// directory so the source and requested path represent the same project.
    /// </summary>
    public static FakeWorkerUiOpenProject ForWorkerRelativePath(string requestedPath)
        => new(Path.IsPathFullyQualified(requestedPath)
            ? requestedPath
            : Path.Combine(Path.GetDirectoryName(FakeWorkerLocator.Locate())!, requestedPath));

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Variable, _previous);
        Environment.SetEnvironmentVariable(StatusGroundingVariable, _previousStatusGrounding);
    }
}
