namespace TiaMcpServer.Cli.Install;

internal sealed class CodexInstaller : IMcpClientInstaller
{
    public ClientKind Client => ClientKind.Codex;

    public Task<ClientDetectionResult> DetectAsync(
        Func<string, ExecutableResolutionResult> resolveClientExe,
        CancellationToken cancellationToken)
    {
        var result = resolveClientExe("codex");
        return Task.FromResult(new ClientDetectionResult(
            result.Found,
            result.ResolvedPath,
            result.Kind,
            result.Found ? null : "Codex CLI was not found.\n\nExpected command:\n  codex\n\nVerify the installation with:\n  codex --version\n\nAfter installing Codex, run:\n  tia-mcp install codex"));
    }

    public NativeCommand BuildInstallCommand(
        InstallOptions options,
        McpLaunchSpec spec,
        Func<string, ExecutableResolutionResult> resolveClientExe)
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
