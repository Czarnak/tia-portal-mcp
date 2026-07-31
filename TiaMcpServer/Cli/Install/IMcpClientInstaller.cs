namespace TiaMcpServer.Cli.Install;

internal interface IMcpClientInstaller
{
    ClientKind Client { get; }

    Task<ClientDetectionResult> DetectAsync(INativeProcessRunner runner, CancellationToken cancellationToken);

    NativeCommand BuildInstallCommand(InstallOptions options, McpLaunchSpec spec);

    NativeCommand? BuildVerificationCommand(InstallOptions options, McpLaunchSpec spec);
}
