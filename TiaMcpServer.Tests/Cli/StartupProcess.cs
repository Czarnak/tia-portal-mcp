using System.Diagnostics;

namespace TiaMcpServer.Tests.Cli;

internal static class StartupProcess
{
    internal static async Task<StartupResult> RunAsync(IReadOnlyDictionary<string, string>? environment, params string[] args)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // Requires a current solution build in this configuration (see docs/development/building.md).
        // Building only this linked-source test project does not build the host launched below.
        foreach (var arg in new[] { "run", "--no-build", "--no-restore", "--project", "TiaMcpServer", "--configuration", configuration, "--" }.Concat(args))
        {
            startInfo.ArgumentList.Add(arg);
        }

        startInfo.Environment.Remove("TIA_MCP_ACCESS_MODE");
        startInfo.Environment.Remove("TIA_PROJECT_PATH");
        startInfo.Environment.Remove("TIA_MCP_WORKER_TIMEOUT_MINUTES");
        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                startInfo.Environment[name] = value;
            }
        }
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the MCP host.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return new StartupResult(process.ExitCode, await standardOutput.WaitAsync(timeout.Token), await standardError.WaitAsync(timeout.Token));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    internal sealed record StartupResult(int ExitCode, string StandardOutput, string StandardError);
}
