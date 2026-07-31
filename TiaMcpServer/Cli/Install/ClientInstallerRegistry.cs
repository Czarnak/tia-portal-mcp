namespace TiaMcpServer.Cli.Install;

internal static class ClientInstallerRegistry
{
    private static readonly Dictionary<ClientKind, IMcpClientInstaller> Installers = new()
    {
        { ClientKind.ClaudeCode, new ClaudeCodeInstaller() },
        { ClientKind.Codex, new CodexInstaller() },
        { ClientKind.OpenCode, new OpenCodeInstaller() },
        { ClientKind.MiMoCode, new MiMoCodeInstaller() }
    };

    public static IMcpClientInstaller GetInstaller(ClientKind client)
    {
        if (Installers.TryGetValue(client, out var installer))
        {
            return installer;
        }

        throw new ArgumentException($"No installer registered for client: {client}", nameof(client));
    }
}
