namespace TiaMcpServer.Cli.Install;

internal sealed class OpenCodeInstaller : IMcpClientInstaller
{
    public ClientKind Client => ClientKind.OpenCode;

    public async Task<ClientDetectionResult> DetectAsync(INativeProcessRunner runner, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(
            new NativeCommand("where.exe", new[] { "opencode" }, false),
            cancellationToken);

        if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Stdout))
        {
            var path = result.Stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
            return new ClientDetectionResult(true, path, null);
        }

        return new ClientDetectionResult(false, null, "OpenCode CLI not found. Install it from https://github.com/opencode-ai/opencode");
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

        return new NativeCommand("opencode", args, false);
    }

    public NativeCommand? BuildVerificationCommand(InstallOptions options, McpLaunchSpec spec)
    {
        return new NativeCommand("opencode", new[] { "mcp", "list" }, false);
    }
}
