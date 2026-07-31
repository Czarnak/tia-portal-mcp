using System.Text.Json;

namespace TiaMcpServer.Cli.Install;

public static class InstallCommand
{
    private const string UsageText = """
        Usage: tia-mcp install <client> [options]

        Register the TIA Portal MCP server with a supported MCP client.

        Supported clients:
          claude-code, claude    Claude Code (Anthropic)
          codex                  Codex (OpenAI)
          opencode               OpenCode
          mimocode, mimo         MiMoCode (Xiaomi)

        Options:
          --name <name>          Server registration name (default: tia-portal).
          --access-mode <mode>   Access mode: read-only, read-write (default: read-only).
          --tia-project <path>   Bind to a specific TIA Portal project.
          --server-path <path>   Explicit path to the tia-mcp executable.
          --dry-run              Print the install command without executing.
          --json                 Emit JSON output (not supported with mimocode).
          --help                 Show this help message and exit.

        Exit codes:
          0  Success.
          1  General failure.
          2  Invalid arguments.
          3  Unsupported client.
          4  Client executable not found.
          5  tia-mcp executable not found.
          6  Native command failed.
          7  Verification failed.
          8  Unsupported option combination.
        """;

    public static Task<int> RunAsync(string[] args)
        => RunAsync(args, new NativeProcessRunner(), Console.Out, Console.Error);

    internal static Task<int> RunAsync(string[] args, INativeProcessRunner runner, TextWriter output, TextWriter error)
        => RunAsync(args, runner, output, error, ExecutableResolver.ResolveServerExecutable, ExecutableResolver.FindClientExecutable);

    internal static async Task<int> RunAsync(
        string[] args,
        INativeProcessRunner runner,
        TextWriter output,
        TextWriter error,
        Func<string?, string?> resolveServerExe,
        Func<string, string?> findClientExe)
    {
        var options = InstallCliParser.Parse(args);

        // Handle --help
        if (options.Help)
        {
            output.WriteLine(options.Json
                ? JsonSerializer.Serialize(new { usage = UsageText })
                : UsageText);
            return 0;
        }

        // Handle parse errors
        if (!options.Valid)
        {
            if (options.Json)
            {
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    success = false,
                    error = options.ParseError
                }));
            }
            else
            {
                error.WriteLine($"error: {options.ParseError}");
                error.WriteLine(UsageText);
            }

            // Exit code 8 for unsupported combo, 3 for unsupported client, 2 for other parse errors
            if (options.ParseError?.Contains("does not support --json") == true ||
                options.ParseError?.Contains("does not support") == true)
            {
                return 8;
            }

            if (options.ParseError?.Contains("Unsupported MCP client") == true)
            {
                return 3;
            }

            return 2;
        }

        var client = options.Client!.Value;
        var installer = ClientInstallerRegistry.GetInstaller(client);

        // Resolve tia-mcp executable
        var serverExePath = resolveServerExe(options.ServerPath);
        if (serverExePath is null)
        {
            var msg = "tia-mcp executable not found. Install it with: dotnet tool install -g TiaMcpServer";
            if (options.Json)
            {
                output.WriteLine(JsonSerializer.Serialize(new { success = false, client = client.ToString(), error = msg }));
            }
            else
            {
                error.WriteLine($"error: {msg}");
            }

            return 5;
        }

        // Build MCP launch arguments
        var launchArgs = new List<string> { "--access-mode", options.AccessMode };
        if (!string.IsNullOrWhiteSpace(options.TiaProject))
        {
            launchArgs.Add("--project");
            launchArgs.Add(options.TiaProject);
        }

        var spec = new McpLaunchSpec(options.ServerName, serverExePath, launchArgs);

        // Detect client executable
        var detection = await installer.DetectAsync(runner, CancellationToken.None);
        if (!detection.Found)
        {
            if (options.Json)
            {
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    success = false,
                    client = client.ToString(),
                    error = detection.Error
                }));
            }
            else
            {
                error.WriteLine($"error: {detection.Error}");
            }

            return 4;
        }

        // For MiMoCode with --json, reject
        if (client == ClientKind.MiMoCode && options.Json)
        {
            var msg = "MiMoCode installation uses interactive mode and does not support --json output.";
            output.WriteLine(JsonSerializer.Serialize(new { success = false, client = client.ToString(), error = msg }));
            return 8;
        }

        // Build install command
        var installCommand = installer.BuildInstallCommand(options, spec);

        // Dry-run: print command and exit
        if (options.DryRun)
        {
            if (options.Json)
            {
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    dryRun = true,
                    client = client.ToString(),
                    executable = installCommand.Executable,
                    arguments = installCommand.Arguments,
                    interactive = installCommand.Interactive
                }));
            }
            else
            {
                output.WriteLine($"[dry-run] Would execute: {installCommand.Executable} {string.Join(" ", installCommand.Arguments)}");
            }

            return 0;
        }

        // Execute install command
        var installResult = await runner.RunAsync(installCommand, CancellationToken.None);

        if (installResult.ExitCode != 0)
        {
            if (options.Json)
            {
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    success = false,
                    client = client.ToString(),
                    serverName = spec.ServerName,
                    accessMode = options.AccessMode,
                    serverPath = spec.ExecutablePath,
                    interactive = installCommand.Interactive,
                    installCommand = FormatCommand(installCommand),
                    installExitCode = installResult.ExitCode,
                    error = string.IsNullOrWhiteSpace(installResult.Stderr) ? installResult.Stdout : installResult.Stderr
                }));
            }
            else
            {
                error.WriteLine($"error: Install command failed (exit code {installResult.ExitCode})");
                if (!string.IsNullOrWhiteSpace(installResult.Stderr))
                {
                    error.WriteLine(installResult.Stderr);
                }

                if (!string.IsNullOrWhiteSpace(installResult.Stdout))
                {
                    error.WriteLine(installResult.Stdout);
                }
            }

            return 6;
        }

        // Run verification
        int? verificationExitCode = null;
        string? verificationStdout = null;
        string? verificationStderr = null;

        var verifyCommand = installer.BuildVerificationCommand(options, spec);
        if (verifyCommand is not null)
        {
            var verifyResult = await runner.RunAsync(verifyCommand, CancellationToken.None);
            verificationExitCode = verifyResult.ExitCode;
            verificationStdout = verifyResult.Stdout;
            verificationStderr = verifyResult.Stderr;
        }

        // Output success
        if (options.Json)
        {
            output.WriteLine(JsonSerializer.Serialize(new
            {
                success = true,
                client = client.ToString(),
                serverName = spec.ServerName,
                accessMode = options.AccessMode,
                serverPath = spec.ExecutablePath,
                interactive = installCommand.Interactive,
                installCommand = FormatCommand(installCommand),
                installExitCode = installResult.ExitCode,
                verificationExitCode = verificationExitCode
            }));
        }
        else
        {
            output.WriteLine($"Successfully registered '{spec.ServerName}' with {FormatClientName(client)}.");
            output.WriteLine($"  Server path: {spec.ExecutablePath}");
            output.WriteLine($"  Access mode: {options.AccessMode}");
            if (!string.IsNullOrWhiteSpace(options.TiaProject))
            {
                output.WriteLine($"  TIA project: {options.TiaProject}");
            }

            if (verificationExitCode is not null)
            {
                output.WriteLine(verificationExitCode == 0
                    ? "  Verification: passed"
                    : $"  Verification: failed (exit code {verificationExitCode})");
            }
        }

        return verificationExitCode is > 0 ? 7 : 0;
    }

    private static string[] FormatCommand(NativeCommand command)
    {
        var result = new List<string> { command.Executable };
        result.AddRange(command.Arguments);
        return result.ToArray();
    }

    private static string FormatClientName(ClientKind client) => client switch
    {
        ClientKind.ClaudeCode => "Claude Code",
        ClientKind.Codex => "Codex",
        ClientKind.OpenCode => "OpenCode",
        ClientKind.MiMoCode => "MiMoCode",
        _ => client.ToString()
    };
}
