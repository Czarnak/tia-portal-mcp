namespace TiaMcpServer.Cli;

internal static class StartupOptionReader
{
    public static (string? Value, string? Error) ReadSingle(string[] args, string flag)
    {
        string? cliValue = null;
        var prefix = flag + "=";
        for (var i = 0; i < args.Length; i++)
        {
            string? candidate;
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    return (null, $"{flag} requires a value.");
                candidate = args[++i];
            }
            else if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                candidate = args[i].Substring(prefix.Length);
            }
            else
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(candidate))
                return (null, $"{flag} requires a value.");
            if (cliValue is not null)
                return (null, $"{flag} may be specified only once.");
            cliValue = candidate;
        }

        return (cliValue, null);
    }
}
