using System.Diagnostics;

namespace TiaMcpServer.Cli.Install;

internal sealed class NativeProcessRunner : INativeProcessRunner
{
    public async Task<NativeCommandResult> RunAsync(NativeCommand command, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command.Executable,
            UseShellExecute = false,
            CreateNoWindow = !command.Interactive,
            RedirectStandardOutput = !command.Interactive,
            RedirectStandardError = !command.Interactive,
            RedirectStandardInput = !command.Interactive
        };

        foreach (var arg in command.Arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return new NativeCommandResult(-1, string.Empty, "Failed to start process.");
            }

            if (command.Interactive)
            {
                // Interactive mode: wait for exit without capturing streams
                await process.WaitForExitAsync(cancellationToken);
                return new NativeCommandResult(process.ExitCode, string.Empty, string.Empty);
            }

            // Non-interactive: capture output
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            return new NativeCommandResult(process.ExitCode, stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            return new NativeCommandResult(-1, string.Empty, "Operation was cancelled.");
        }
        catch (Exception ex)
        {
            return new NativeCommandResult(-1, string.Empty, ex.Message);
        }
    }
}
