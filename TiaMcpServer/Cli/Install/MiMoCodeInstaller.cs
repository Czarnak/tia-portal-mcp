namespace TiaMcpServer.Cli.Install;

internal sealed class MiMoCodeInstaller : IMcpClientInstaller
{
    public ClientKind Client => ClientKind.MiMoCode;

    public Task<ClientDetectionResult> DetectAsync(
        Func<string, ExecutableResolutionResult> resolveClientExe,
        CancellationToken cancellationToken)
    {
        var result = resolveClientExe("mimo");
        return Task.FromResult(new ClientDetectionResult(
            result.Found,
            result.ResolvedPath,
            result.Kind,
            result.Found ? null : "MiMoCode CLI was not found.\n\nExpected command:\n  mimo\n\nVerify the installation with:\n  mimo --version\n\nAfter installing MiMoCode, run:\n  tia-mcp install mimocode"));
    }

    public NativeCommand BuildInstallCommand(
        InstallOptions options,
        McpLaunchSpec spec,
        Func<string, ExecutableResolutionResult> resolveClientExe)
    {
        // MiMoCode uses interactive mode; the user enters values manually.
        // The install command prints a guide before launching.
        var args = new List<string> { "mcp", "add" };
        return new NativeCommand("mimo", args, true);
    }

    public NativeCommand? BuildVerificationCommand(InstallOptions options, McpLaunchSpec spec)
    {
        return new NativeCommand("mimo", new[] { "mcp", "list" }, false);
    }
}
