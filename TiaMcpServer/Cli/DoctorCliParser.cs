namespace TiaMcpServer.Cli;

public sealed record DoctorCliOptions(
    bool Valid,
    bool Json,
    bool Verbose,
    string? ProjectPath,
    string? ParseError);

public static class DoctorCliParser
{
    private const string JsonFlag = "--json";
    private const string VerboseFlag = "--verbose";
    private const string ProjectFlag = "--project";
    private const string ProjectPrefix = "--project=";

    public static DoctorCliOptions Parse(string[] args)
    {
        bool json = false;
        bool verbose = false;
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

            if (string.Equals(arg, ProjectFlag, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    return new DoctorCliOptions(false, json, verbose, null, $"--project requires a value.");
                }

                projectPath = args[++i];
                continue;
            }

            if (arg.StartsWith(ProjectPrefix, StringComparison.OrdinalIgnoreCase))
            {
                projectPath = arg.Substring(ProjectPrefix.Length);
                continue;
            }

            return new DoctorCliOptions(false, json, verbose, projectPath, $"Unknown doctor argument: '{arg}'.");
        }

        return new DoctorCliOptions(true, json, verbose, projectPath, null);
    }
}
