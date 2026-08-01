namespace TiaMcpServer.Cli.Install;

internal interface IMcpClientInstaller
{
    ClientKind Client { get; }

    Task<ClientDetectionResult> DetectAsync(
        Func<string, ExecutableResolutionResult> resolveClientExe,
        CancellationToken cancellationToken);

    NativeCommand BuildInstallCommand(
        InstallOptions options,
        McpLaunchSpec spec,
        Func<string, ExecutableResolutionResult> resolveClientExe);

    NativeCommand? BuildVerificationCommand(InstallOptions options, McpLaunchSpec spec);
}
