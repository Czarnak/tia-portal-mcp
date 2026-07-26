using TiaMcpServer.Contracts;

namespace TiaMcpServer.Cli;

public sealed record DoctorCliOptions(
    bool Valid,
    bool Json,
    bool Verbose,
    bool Help,
    string? ProjectPath,
    McpAccessMode AccessMode,
    string? ParseError);

public static class DoctorCliParser
{
    private const string JsonFlag = "--json";
    private const string VerboseFlag = "--verbose";
    private const string HelpFlag = "--help";
    private const string ProjectFlag = "--project";
    private const string ProjectPrefix = "--project=";

    public static DoctorCliOptions Parse(string[] args)
    {
        bool json = args.Any(arg => string.Equals(arg, JsonFlag, StringComparison.OrdinalIgnoreCase));
        bool verbose = false;
        bool help = false;
        string? projectPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (string.Equals(arg, JsonFlag, StringComparison.OrdinalIgnoreCase))
            {
                json = true;
                continue;
            }

            if (string.Equals(arg, VerboseFlag, StringComparison.OrdinalIgnoreCase))
            {
                verbose = true;
                continue;
            }

            if (string.Equals(arg, HelpFlag, StringComparison.OrdinalIgnoreCase))
            {
                help = true;
                continue;
            }

            if (string.Equals(arg, ProjectFlag, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length ||
                    string.IsNullOrWhiteSpace(args[i + 1]) ||
                    args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return new DoctorCliOptions(false, json, verbose, help, null, ResolveAccessMode(), $"--project requires a value.");
                }

                projectPath = args[++i];
                continue;
            }

            if (arg.StartsWith(ProjectPrefix, StringComparison.OrdinalIgnoreCase))
            {
                projectPath = arg.Substring(ProjectPrefix.Length);
                if (string.IsNullOrWhiteSpace(projectPath))
                {
                    return new DoctorCliOptions(false, json, verbose, help, null, ResolveAccessMode(), $"--project requires a value.");
                }

                continue;
            }

            return new DoctorCliOptions(false, json, verbose, help, projectPath, ResolveAccessMode(), $"Unknown doctor argument: '{arg}'.");
        }

        return new DoctorCliOptions(true, json, verbose, help, projectPath, ResolveAccessMode(), null);
    }

    private static McpAccessMode ResolveAccessMode()
    {
        var envValue = Environment.GetEnvironmentVariable("TIA_MCP_ACCESS_MODE");
        if (!string.IsNullOrWhiteSpace(envValue) &&
            AccessModeParser.ParseValue(envValue) is { IsValid: true } result)
        {
            return result.Mode;
        }

        return McpAccessMode.ReadWrite;
    }
}
