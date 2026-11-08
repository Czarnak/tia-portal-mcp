using TiaMcpServer.Contracts.Safety;

namespace TiaMcpServer.Cli;

public enum AccessModeSource
{
    Default,
    Option,
    Environment
}

/// <summary>
/// Resolves the <see cref="McpAccessMode"/> from CLI arguments and environment variables.
/// Precedence: CLI argument > environment variable > default (ReadOnly).
/// </summary>
public static class AccessModeParser
{
    private const string EnvVarName = "TIA_MCP_ACCESS_MODE";

    /// <summary>
    /// Resolves the access mode from the given CLI arguments and environment.
    /// Returns a result indicating success with the resolved mode, or failure with a message.
    /// </summary>
    public static AccessModeParseResult Parse(string[] args, Func<string, string?>? getEnvironmentVariable = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        McpAccessMode? cliMode = null;

        AccessModeParseResult? RecordCliMode(McpAccessMode candidate)
        {
            if (cliMode is not null && cliMode.Value != candidate)
            {
                return AccessModeParseResult.Fail(
                    "Conflicting access mode arguments were supplied. Specify only one of 'read-only', 'read-write', or 'full'.");
            }

            cliMode = candidate;
            return null;
        }

        // 1. Check all CLI arguments. Repeating the same mode is harmless; contradictory
        // explicit modes are rejected rather than depending on argument order.
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--read-only", StringComparison.OrdinalIgnoreCase))
            {
                var conflict = RecordCliMode(McpAccessMode.ReadOnly);
                if (conflict is not null)
                {
                    return conflict;
                }

                continue;
            }

            if (string.Equals(args[i], "--read-write", StringComparison.OrdinalIgnoreCase))
            {
                var conflict = RecordCliMode(McpAccessMode.ReadWrite);
                if (conflict is not null)
                {
                    return conflict;
                }

                continue;
            }

            if (string.Equals(args[i], "--access-mode", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length ||
                    string.IsNullOrWhiteSpace(args[i + 1]) ||
                    args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return AccessModeParseResult.Fail(
                        "--access-mode requires a value. Valid values: 'read-only', 'read-write', 'full'.");
                }

                var parsed = ParseValue(args[++i]);
                if (!parsed.IsValid)
                {
                    return parsed;
                }

                var conflict = RecordCliMode(parsed.Mode);
                if (conflict is not null)
                {
                    return conflict;
                }

                continue;
            }

            const string accessModePrefix = "--access-mode=";
            if (args[i].StartsWith(accessModePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var parsed = ParseValue(args[i].Substring(accessModePrefix.Length));
                if (!parsed.IsValid)
                {
                    return parsed;
                }

                var conflict = RecordCliMode(parsed.Mode);
                if (conflict is not null)
                {
                    return conflict;
                }
            }
        }

        if (cliMode is not null)
        {
            return AccessModeParseResult.Ok(cliMode.Value, AccessModeSource.Option);
        }

        // 2. Check environment variable
        var envValue = getEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            var parsed = ParseValue(envValue);
            return parsed.IsValid
                ? AccessModeParseResult.Ok(parsed.Mode, AccessModeSource.Environment)
                : AccessModeParseResult.Fail(
                    $"Invalid {EnvVarName} value '{envValue}'. Valid values: 'read-only', 'read-write', 'full'.");
        }

        // 3. Default
        return AccessModeParseResult.Ok(McpAccessMode.ReadOnly, AccessModeSource.Default);
    }

    /// <summary>
    /// Parses a canonical access mode name into an <see cref="McpAccessMode"/>.
    /// </summary>
    public static AccessModeParseResult ParseValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return AccessModeParseResult.Fail(
                "Access mode value is empty. Valid values: 'read-only', 'read-write', 'full'.");
        }

        if (McpAccessModeNames.TryParse(value, out var mode))
        {
            return AccessModeParseResult.Ok(mode);
        }

        return AccessModeParseResult.Fail(
            $"Invalid access mode '{value}'. Valid values: 'read-only', 'read-write', 'full'.");
    }
}

public sealed record AccessModeParseResult(bool IsValid, McpAccessMode Mode, AccessModeSource Source, string? Error)
{
    public static AccessModeParseResult Ok(McpAccessMode mode, AccessModeSource source = AccessModeSource.Option)
        => new(true, mode, source, null);

    public static AccessModeParseResult Fail(string error) => new(false, default, default, error);

    public static string SourceName(AccessModeSource source) => source switch
    {
        AccessModeSource.Default => "default",
        AccessModeSource.Option => "option",
        AccessModeSource.Environment => "environment",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
    };
}
