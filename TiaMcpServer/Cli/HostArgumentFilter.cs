namespace TiaMcpServer.Cli;

/// <summary>
/// Removes arguments consumed by the application before the remaining arguments are passed to
/// <c>Host.CreateApplicationBuilder</c>. The generic host command-line configuration provider
/// requires key/value pairs and does not support value-less switches such as <c>--read-only</c>.
/// </summary>
public static class HostArgumentFilter
{
    public static string[] RemoveAccessModeArguments(string[] args)
    {
        var remaining = new List<string>(args.Length);

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--read-only", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--read-write", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("--access-mode=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(arg, "--access-mode", StringComparison.OrdinalIgnoreCase))
            {
                // AccessModeParser validates the value before this filter runs.
                if (i + 1 < args.Length)
                {
                    i++;
                }

                continue;
            }

            remaining.Add(arg);
        }

        return remaining.ToArray();
    }
}
