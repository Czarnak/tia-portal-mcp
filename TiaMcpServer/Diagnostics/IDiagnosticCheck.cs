namespace TiaMcpServer.Diagnostics;

public interface IDiagnosticCheck
{
    string Id { get; }

    string Name { get; }

    DiagnosticCheckResult Run();
}
