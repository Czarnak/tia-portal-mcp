namespace TiaMcpServer.Cli.Install;

public static class ClientAliasResolver
{
    public static ClientKind? MapToClientKind(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();

        if (string.Equals(normalized, "claude-code", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "claude", StringComparison.OrdinalIgnoreCase))
        {
            return ClientKind.ClaudeCode;
        }

        if (string.Equals(normalized, "codex", StringComparison.OrdinalIgnoreCase))
        {
            return ClientKind.Codex;
        }

        if (string.Equals(normalized, "opencode", StringComparison.OrdinalIgnoreCase))
        {
            return ClientKind.OpenCode;
        }

        if (string.Equals(normalized, "mimocode", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "mimo", StringComparison.OrdinalIgnoreCase))
        {
            return ClientKind.MiMoCode;
        }

        return null;
    }
}
