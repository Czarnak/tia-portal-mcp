using System.Globalization;
using TiaMcpServer.Contracts.Project;

namespace TiaMcpServer.Tests.TestSupport;

/// <summary>
/// Declares the simulated Portal inventory before a child worker starts. Use only in
/// RealWorkerProcessCollection or Mcp protocol serial; the environment is process-wide.
/// Consumers must select their worker access mode explicitly.
/// </summary>
internal sealed class FakeWorkerPortals : IDisposable
{
    private const string Variable = "TIA_MCP_FAKE_WORKER_PORTALS";
    private readonly string? _previous = Environment.GetEnvironmentVariable(Variable);

    internal sealed record Entry(int ProcessId, string? ProjectPath, bool HasUserInterface = true,
        int OtherClients = 0, bool? Modified = false);

    public FakeWorkerPortals(params Entry[] entries)
        => Environment.SetEnvironmentVariable(Variable, string.Join(";", entries.Select(entry =>
            string.Join("|", entry.ProcessId.ToString(CultureInfo.InvariantCulture),
                ProjectPathNormalization.Canonicalize(entry.ProjectPath) ?? "",
                entry.HasUserInterface ? "ui" : "headless",
                entry.OtherClients.ToString(CultureInfo.InvariantCulture), entry.Modified switch { true => "true", false => "false", null => "unknown" }))));

    public void Dispose() => Environment.SetEnvironmentVariable(Variable, _previous);
}
