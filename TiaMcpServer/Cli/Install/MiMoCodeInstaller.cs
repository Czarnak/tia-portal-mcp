namespace TiaMcpServer.Cli.Install;

internal sealed class MiMoCodeInstaller : IMcpClientInstaller
{
    public ClientKind Client => ClientKind.MiMoCode;

    public async Task<ClientDetectionResult> DetectAsync(INativeProcessRunner runner, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(
            new NativeCommand("where.exe", new[] { "mimo" }, false),
            cancellationToken);

        if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Stdout))
        {
            var path = result.Stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
            return new ClientDetectionResult(true, path, null);
        }

        return new ClientDetectionResult(false, null, "MiMoCode CLI not found. Install it from https://github.com/Xiaomi/mimocode");
    }

    public NativeCommand BuildInstallCommand(InstallOptions options, McpLaunchSpec spec)
    {
        // MiMoCode uses interactive mode; the user enters values manually
        var args = new List<string> { "mcp", "add" };
        return new NativeCommand("mimo", args, true);
    }

    public NativeCommand? BuildVerificationCommand(InstallOptions options, McpLaunchSpec spec)
    {
        return new NativeCommand("mimo", new[] { "mcp", "list" }, false);
    }
}
