namespace TiaMcpServer.Cli;

/// <summary>Rejects removed startup options with actionable migration guidance.</summary>
public static class RemovedCliOptions
{
    public static bool TryGetError(string[] args, out string error)
    {
        foreach (var arg in args)
        {
            if (string.Equals(arg, "--confirm-with-user", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("--confirm-with-user=", StringComparison.OrdinalIgnoreCase))
            {
                error = "--confirm-with-user was removed. Confirmation follows the access mode: read-write asks for every lifecycle call; use --access-mode full to run lifecycle tools without prompts.";
                return true;
            }
        }

        error = string.Empty;
        return false;
    }
}
