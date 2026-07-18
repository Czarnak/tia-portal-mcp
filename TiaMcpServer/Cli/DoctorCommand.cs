using System.Text.Json;
using TiaMcpServer.Diagnostics;
using TiaMcpServer.Diagnostics.Checks;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Cli;

public static class DoctorCommand
{
    private const string UsageText = """
        Usage: tia-mcp doctor [--json] [--verbose] [--project <path>]

        Options:
          --json       Emit a single JSON document to stdout.
          --verbose    Include additional evidence.
          --project    Informational project binding (does not start the MCP host).

        Exit codes:
          0  No blocking failures.
          1  One or more diagnostic checks failed.
          2  Invalid arguments or an unexpected error.
        """;

    public static Task<int> RunAsync(string[] args)
    {
        var options = DoctorCliParser.Parse(args);
        if (!options.Valid)
        {
            Console.Error.WriteLine($"error: {options.ParseError}");
            Console.Error.WriteLine(UsageText);
            return Task.FromResult(2);
        }

        try
        {
            var runner = BuildRunner(options);
            var report = runner.Run();

            if (options.Json)
            {
                Console.Out.WriteLine(DoctorJsonRenderer.Render(report, options.Verbose));
            }
            else
            {
                DoctorTextRenderer.Render(report, options.Verbose, Console.Out);
            }

            return Task.FromResult(report.Status == DiagnosticStatus.Failed ? 1 : 0);
        }
        catch (Exception ex)
        {
            if (options.Json)
            {
                Console.Error.WriteLine(JsonSerializer.Serialize(new { error = ex.Message }));
            }
            else
            {
                Console.Error.WriteLine($"Fatal: {ex.Message}");
            }

            return Task.FromResult(2);
        }
    }

    private static DoctorRunner BuildRunner(DoctorCliOptions options)
    {
        var appInfo = ApplicationInfoService.Instance;
        var env = EnvironmentVariableService.Instance;
        var registry = RegistryService.Instance;
        var fileSystem = FileSystemService.Instance;
        var processes = ProcessEnumerationService.Instance;
        var identity = WindowsIdentityService.Instance;

        var checks = new List<IDiagnosticCheck>
        {
            new OperatingSystemCheck(appInfo),
            new DotNetRuntimeCheck(appInfo),
            new DotNetFrameworkCheck(registry, appInfo),
            new TiaPortalInstallationCheck(env, registry, fileSystem),
            new OpennessAssembliesCheck(env, registry, fileSystem),
            new OpennessGroupCheck(identity),
            new OpennessWorkerCheck(appInfo, fileSystem),
            new HostWorkerVersionCheck(appInfo, fileSystem),
            new TiaPortalProcessCheck(processes, appInfo),
            new ProjectBindingCheck(env, options.ProjectPath)
        };

        return new DoctorRunner(appInfo, checks);
    }
}
