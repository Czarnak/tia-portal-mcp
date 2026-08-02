namespace TiaMcpServer.Cli.Install;

internal sealed class ClaudeCodeInstaller : IMcpClientInstaller
{
    public ClientKind Client => ClientKind.ClaudeCode;

    public Task<ClientDetectionResult> DetectAsync(
        Func<string, ExecutableResolutionResult> resolveClientExe,
        CancellationToken cancellationToken)
    {
        var result = resolveClientExe("claude");
        return Task.FromResult(new ClientDetectionResult(
            result.Found,
            result.ResolvedPath,
            result.Kind,
            result.Found ? null : "Claude Code was not found.\n\nExpected command:\n  claude\n\nVerify the installation with:\n  claude --version\n\nAfter installing Claude Code, run:\n  tia-mcp install claude-code"));
    }

    public NativeCommand BuildInstallCommand(
        InstallOptions options,
        McpLaunchSpec spec,
        Func<string, ExecutableResolutionResult> resolveClientExe)
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
