using System.Text.Json;
using TiaMcpServer.Diagnostics;
using TiaMcpServer.Diagnostics.Checks;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Cli;

public static class DoctorCommand
{
    private const string UsageText = """
        Usage: tia-mcp doctor [--json] [--verbose] [--project <path>] [--access-mode <mode>] [--help]

        Options:
          --json          Emit a single JSON document to stdout.
          --verbose       Include additional evidence.
          --project       Validate an exact .ap21 binding without opening or attaching to TIA Portal.
          --access-mode   Report diagnostics for read-only or read-write mode.
          --read-only     Alias for --access-mode read-only.
          --read-write    Alias for --access-mode read-write.
          --help          Show command usage and exit.

        Exit codes:
          0  No blocking failures.
          1  One or more diagnostic checks failed.
          2  Invalid arguments or an unexpected error.
        """;

    public static Task<int> RunAsync(string[] args)
        => RunAsync(args, options => BuildRunner(options).Run(), Console.Out, Console.Error);

    public static Task<int> RunAsync(
        string[] args,
        Func<DoctorCliOptions, DoctorReport> runDoctor,
        TextWriter output,
        TextWriter error)
    {
        var options = DoctorCliParser.Parse(args);
        if (!options.Valid)
        {
            if (options.Json)
            {
                output.WriteLine(JsonSerializer.Serialize(new { error = options.ParseError }));
            }
            else
            {
                error.WriteLine($"error: {options.ParseError}");
                error.WriteLine(UsageText);
            }

            return Task.FromResult(2);
        }

        if (options.Help)
        {
            output.WriteLine(options.Json
                ? JsonSerializer.Serialize(new { usage = UsageText })
                : UsageText);
            return Task.FromResult(0);
        }

        try
        {
            var report = runDoctor(options);

            if (options.Json)
            {
                output.WriteLine(DoctorJsonRenderer.Render(report, options.Verbose, options.AccessMode));
            }
            else
            {
                DoctorTextRenderer.Render(report, options.Verbose, output, options.AccessMode);
            }

            return Task.FromResult(report.HasUnexpectedCheckFailure ? 2 : report.Status == DiagnosticStatus.Failed ? 1 : 0);
        }
        catch (Exception ex)
        {
            if (options.Json)
            {
                output.WriteLine(JsonSerializer.Serialize(new { error = ex.Message }));
            }
            else
            {
                error.WriteLine($"Fatal: {ex.Message}");
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

        var hasConfiguredProjectBinding = ProjectBindingCheck.HasConfiguredBinding(env, options.ProjectPath);

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
            new TiaPortalProcessCheck(
                processes,
                appInfo,
                options.AccessMode,
                hasConfiguredProjectBinding),
            new ProjectBindingCheck(env, fileSystem, options.ProjectPath, options.AccessMode)
        };

        return new DoctorRunner(appInfo, checks);
    }
}
