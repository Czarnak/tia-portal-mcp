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
    private const string AccessModeFlag = "--access-mode";
    private const string AccessModePrefix = "--access-mode=";
    private const string ReadOnlyFlag = "--read-only";
    private const string ReadWriteFlag = "--read-write";

    public static DoctorCliOptions Parse(string[] args)
    {
        bool json = args.Any(arg => string.Equals(arg, JsonFlag, StringComparison.OrdinalIgnoreCase));
        bool verbose = false;
        bool help = false;
        string? projectPath = null;

        var hasCliAccessMode = args.Any(IsAccessModeArgument);
        var accessModeResult = hasCliAccessMode
            ? AccessModeParser.Parse(args)
            : AccessModeParseResult.Ok(ResolveAccessMode());
        if (!accessModeResult.IsValid)
        {
            return new DoctorCliOptions(
                false,
                json,
                verbose,
                help,
                null,
                default,
                accessModeResult.Error);
        }

        var accessMode = accessModeResult.Mode;

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

            if (string.Equals(arg, ReadOnlyFlag, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, ReadWriteFlag, StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith(AccessModePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(arg, AccessModeFlag, StringComparison.OrdinalIgnoreCase))
            {
                // AccessModeParser already validated the value above.
                i++;
                continue;
            }

            if (string.Equals(arg, ProjectFlag, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length ||
                    string.IsNullOrWhiteSpace(args[i + 1]) ||
                    args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return new DoctorCliOptions(false, json, verbose, help, null, accessMode, "--project requires a value.");
                }

                if (projectPath is not null)
                {
                    return new DoctorCliOptions(false, json, verbose, help, projectPath, accessMode, "--project may be specified only once.");
                }

                projectPath = args[++i];
                continue;
            }

            if (arg.StartsWith(ProjectPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var candidate = arg.Substring(ProjectPrefix.Length);
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    return new DoctorCliOptions(false, json, verbose, help, null, accessMode, "--project requires a value.");
                }


                if (projectPath is not null)
                {
                    return new DoctorCliOptions(false, json, verbose, help, projectPath, accessMode, "--project may be specified only once.");
                }

                projectPath = candidate;

                continue;
            }

            return new DoctorCliOptions(false, json, verbose, help, projectPath, accessMode, $"Unknown doctor argument: '{arg}'.");
        }

        return new DoctorCliOptions(true, json, verbose, help, projectPath, accessMode, null);
    }

    private static bool IsAccessModeArgument(string arg)
        => string.Equals(arg, ReadOnlyFlag, StringComparison.OrdinalIgnoreCase)
           || string.Equals(arg, ReadWriteFlag, StringComparison.OrdinalIgnoreCase)
           || string.Equals(arg, AccessModeFlag, StringComparison.OrdinalIgnoreCase)
           || arg.StartsWith(AccessModePrefix, StringComparison.OrdinalIgnoreCase);

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
