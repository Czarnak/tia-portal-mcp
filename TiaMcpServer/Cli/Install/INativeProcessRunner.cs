namespace TiaMcpServer.Cli.Install;

internal interface INativeProcessRunner
{
    Task<NativeCommandResult> RunAsync(NativeCommand command, CancellationToken cancellationToken);
}
