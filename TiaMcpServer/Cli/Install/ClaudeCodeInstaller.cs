namespace TiaMcpServer.Cli.Install;

internal sealed class ClaudeCodeInstaller : IMcpClientInstaller
{
    public ClientKind Client => ClientKind.ClaudeCode;

    public async Task<ClientDetectionResult> DetectAsync(INativeProcessRunner runner, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(
            new NativeCommand("where.exe", new[] { "claude" }, false),
            cancellationToken);

        if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Stdout))
        {
            var path = result.Stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
            return new ClientDetectionResult(true, path, null);
        }

        return new ClientDetectionResult(false, null, "Claude Code CLI not found. Install it from https://docs.anthropic.com/en/docs/claude-code");
    }

    public NativeCommand BuildInstallCommand(InstallOptions options, McpLaunchSpec spec)
    {
        var args = new List<string> { "mcp", "add", "--scope", "user", spec.ServerName, "--" };
        args.Add(spec.ExecutablePath);
        args.Add("--access-mode");
        args.Add(options.AccessMode);

        if (!string.IsNullOrWhiteSpace(options.TiaProject))
        {
            args.Add("--project");
            args.Add(options.TiaProject);
        }

        return new NativeCommand("claude", args, false);
    }

    public NativeCommand? BuildVerificationCommand(InstallOptions options, McpLaunchSpec spec)
    {
        return new NativeCommand("claude", new[] { "mcp", "get", spec.ServerName }, false);
    }
}
