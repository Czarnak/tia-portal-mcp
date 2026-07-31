namespace TiaMcpServer.Cli.Install;

internal sealed class CodexInstaller : IMcpClientInstaller
{
    public ClientKind Client => ClientKind.Codex;

    public async Task<ClientDetectionResult> DetectAsync(INativeProcessRunner runner, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(
            new NativeCommand("where.exe", new[] { "codex" }, false),
            cancellationToken);

        if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Stdout))
        {
            var path = result.Stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
            return new ClientDetectionResult(true, path, null);
        }

        return new ClientDetectionResult(false, null, "Codex CLI not found. Install it from https://github.com/openai/codex");
    }

    public NativeCommand BuildInstallCommand(InstallOptions options, McpLaunchSpec spec)
    {
        var args = new List<string> { "mcp", "add", spec.ServerName, "--" };
        args.Add(spec.ExecutablePath);
        args.Add("--access-mode");
        args.Add(options.AccessMode);

        if (!string.IsNullOrWhiteSpace(options.TiaProject))
        {
            args.Add("--project");
            args.Add(options.TiaProject);
        }

        return new NativeCommand("codex", args, false);
    }

    public NativeCommand? BuildVerificationCommand(InstallOptions options, McpLaunchSpec spec)
    {
        return new NativeCommand("codex", new[] { "mcp", "get", spec.ServerName, "--json" }, false);
    }
}
