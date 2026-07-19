namespace TiaMcpServer.Cli;

public sealed record DoctorCliOptions(
    bool Valid,
    bool Json,
    bool Verbose,
    bool Help,
    string? ProjectPath,
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
                    return new DoctorCliOptions(false, json, verbose, help, null, $"--project requires a value.");
                }

                projectPath = args[++i];
                continue;
            }

            if (arg.StartsWith(ProjectPrefix, StringComparison.OrdinalIgnoreCase))
            {
                projectPath = arg.Substring(ProjectPrefix.Length);
                if (string.IsNullOrWhiteSpace(projectPath))
                {
                    return new DoctorCliOptions(false, json, verbose, help, null, $"--project requires a value.");
                }

                continue;
            }

            return new DoctorCliOptions(false, json, verbose, help, projectPath, $"Unknown doctor argument: '{arg}'.");
        }

        return new DoctorCliOptions(true, json, verbose, help, projectPath, null);
    }
}
