namespace TiaMcpServer.Cli.Install;

internal sealed class OpenCodeInstaller : IMcpClientInstaller
{
    public ClientKind Client => ClientKind.OpenCode;

    public Task<ClientDetectionResult> DetectAsync(
        Func<string, ExecutableResolutionResult> resolveClientExe,
        CancellationToken cancellationToken)
    {
        var result = resolveClientExe("opencode");
        return Task.FromResult(new ClientDetectionResult(
            result.Found,
            result.ResolvedPath,
            result.Kind,
            result.Found ? null : "OpenCode CLI was not found.\n\nExpected command:\n  opencode\n\nVerify the installation with:\n  opencode --version\n\nAfter installing OpenCode, run:\n  tia-mcp install opencode"));
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

        return new NativeCommand("opencode", args, false);
    }

    public NativeCommand? BuildVerificationCommand(InstallOptions options, McpLaunchSpec spec)
    {
        return new NativeCommand("opencode", new[] { "mcp", "list" }, false);
    }
}
