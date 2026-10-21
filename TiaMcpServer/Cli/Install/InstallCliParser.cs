using TiaMcpServer.Contracts.Safety;

namespace TiaMcpServer.Cli.Install;

public static class InstallCliParser
{
    private const string NameFlag = "--name";
    private const string NamePrefix = "--name=";
    private const string AccessModeFlag = "--access-mode";
    private const string AccessModePrefix = "--access-mode=";
    private const string TiaProjectFlag = "--tia-project";
    private const string TiaProjectPrefix = "--tia-project=";
    private const string ServerPathFlag = "--server-path";
    private const string ServerPathPrefix = "--server-path=";
    private const string DryRunFlag = "--dry-run";
    private const string JsonFlag = "--json";
    private const string HelpFlag = "--help";

    public static InstallOptions Parse(string[] args)
    {
        // --help and --json are detected up front so they hold regardless of argument order,
        // and a help request ignores every other argument.
        var json = args.Any(arg => IsFlag(arg, JsonFlag));
        if (args.Any(arg => IsFlag(arg, HelpFlag)))
        {
            return new InstallOptions(true, null, "tia-portal", "read-only", null, null, false, json, true, null);
        }

        bool dryRun = false;
        string? serverName = null;
        string? accessMode = null;
        string? tiaProject = null;
        string? serverPath = null;
        ClientKind? client = null;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (IsFlag(arg, DryRunFlag))
            {
                dryRun = true;
                continue;
            }

            if (IsFlag(arg, JsonFlag))
            {
                continue;
            }

            if (string.Equals(arg, NameFlag, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length ||
                    string.IsNullOrWhiteSpace(args[i + 1]) ||
                    args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return Invalid("--name requires a value.", json);
                }

                serverName = args[++i];
                continue;
            }

            if (arg.StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                serverName = arg.Substring(NamePrefix.Length);
                if (string.IsNullOrWhiteSpace(serverName))
                {
                    return Invalid("--name requires a value.", json);
                }

                continue;
            }

            if (string.Equals(arg, AccessModeFlag, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length ||
                    string.IsNullOrWhiteSpace(args[i + 1]) ||
                    args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return Invalid("--access-mode requires a value. Valid values: 'read-only', 'read-write', 'full'.", json);
                }

                accessMode = args[++i];
                continue;
            }

            if (arg.StartsWith(AccessModePrefix, StringComparison.OrdinalIgnoreCase))
            {
                accessMode = arg.Substring(AccessModePrefix.Length);
                if (string.IsNullOrWhiteSpace(accessMode))
                {
                    return Invalid("--access-mode requires a value. Valid values: 'read-only', 'read-write', 'full'.", json);
                }

                continue;
            }

            if (string.Equals(arg, TiaProjectFlag, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length ||
                    string.IsNullOrWhiteSpace(args[i + 1]) ||
                    args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return Invalid("--tia-project requires a value.", json);
                }

                tiaProject = args[++i];
                continue;
            }

            if (arg.StartsWith(TiaProjectPrefix, StringComparison.OrdinalIgnoreCase))
            {
                tiaProject = arg.Substring(TiaProjectPrefix.Length);
                if (string.IsNullOrWhiteSpace(tiaProject))
                {
                    return Invalid("--tia-project requires a value.", json);
                }

                continue;
            }

            if (string.Equals(arg, ServerPathFlag, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length ||
                    string.IsNullOrWhiteSpace(args[i + 1]) ||
                    args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return Invalid("--server-path requires a value.", json);
                }

                serverPath = args[++i];
                continue;
            }

            if (arg.StartsWith(ServerPathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                serverPath = arg.Substring(ServerPathPrefix.Length);
                if (string.IsNullOrWhiteSpace(serverPath))
                {
                    return Invalid("--server-path requires a value.", json);
                }

                continue;
            }

            // Positional argument: client name
            if (!arg.StartsWith("--", StringComparison.Ordinal) && client is null)
            {
                var resolved = ClientAliasResolver.MapToClientKind(arg);
                if (resolved is null)
                {
                    return Invalid($"Unsupported MCP client: '{arg}'.", json);
                }

                client = resolved;
                continue;
            }

            return Invalid($"Unknown install argument: '{arg}'.", json);
        }

        if (client is null)
        {
            return Invalid("No MCP client specified. Usage: tia-mcp install <client>", json);
        }

        if (!McpAccessModeNames.TryParse(accessMode ?? "read-only", out var parsedMode))
        {
            return Invalid($"Invalid access mode '{accessMode}'. Valid values: 'read-only', 'read-write', 'full'.", json);
        }

        accessMode = McpAccessModeNames.ToName(parsedMode);

        // The value becomes the server's --project, which refuses to start on anything else.
        if (tiaProject is not null && ConfiguredProjectPath.Validate(tiaProject.Trim()) is { } invalidProject)
        {
            return Invalid($"--tia-project {invalidProject} Got '{tiaProject}'.", json);
        }

        // Validate MiMoCode + --json is not supported
        if (client == ClientKind.MiMoCode && json)
        {
            return new InstallOptions(
                false,
                client,
                serverName ?? "tia-portal",
                accessMode ?? "read-only",
                tiaProject,
                serverPath,
                dryRun,
                json,
                false,
                "MiMoCode installation uses interactive mode and does not support --json output.");
        }

        return new InstallOptions(
            true,
            client,
            serverName ?? "tia-portal",
            accessMode ?? "read-only",
            tiaProject,
            serverPath,
            dryRun,
            json,
            false,
            null);
    }

    private static bool IsFlag(string arg, string flag)
        => string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase);

    private static InstallOptions Invalid(string error, bool json)
        => new(false, null, "tia-portal", "read-only", null, null, false, json, false, error);
}
