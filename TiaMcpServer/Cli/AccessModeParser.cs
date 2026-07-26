using System;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.Cli;

/// <summary>
/// Resolves the <see cref="McpAccessMode"/> from CLI arguments and environment variables.
/// Precedence: CLI argument > environment variable > default (ReadWrite).
/// </summary>
public static class AccessModeParser
{
    private const string EnvVarName = "TIA_MCP_ACCESS_MODE";

    /// <summary>
    /// Resolves the access mode from the given CLI arguments and environment.
    /// Returns a result indicating success with the resolved mode, or failure with a message.
    /// </summary>
    public static AccessModeParseResult Parse(string[] args)
    {
        // 1. Check CLI arguments
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--read-only", StringComparison.OrdinalIgnoreCase))
            {
                return AccessModeParseResult.Ok(McpAccessMode.ReadOnly);
            }

            if (string.Equals(args[i], "--read-write", StringComparison.OrdinalIgnoreCase))
            {
                return AccessModeParseResult.Ok(McpAccessMode.ReadWrite);
            }

            if (string.Equals(args[i], "--access-mode", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length ||
                    string.IsNullOrWhiteSpace(args[i + 1]) ||
                    args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return AccessModeParseResult.Fail(
                        "--access-mode requires a value. Valid values: 'read-only', 'read-write'.");
                }

                return ParseValue(args[i + 1]);
            }

            const string accessModePrefix = "--access-mode=";
            if (args[i].StartsWith(accessModePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return ParseValue(args[i].Substring(accessModePrefix.Length));
            }
        }

        // 2. Check environment variable
        var envValue = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            return ParseValue(envValue);
        }

        // 3. Default
        return AccessModeParseResult.Ok(McpAccessMode.ReadWrite);
    }

    /// <summary>
    /// Parses a string value ("read-only" or "read-write") into an <see cref="McpAccessMode"/>.
    /// </summary>
    public static AccessModeParseResult ParseValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return AccessModeParseResult.Fail(
                "Access mode value is empty. Valid values: 'read-only', 'read-write'.");
        }

        var normalizedValue = value.Trim();
        if (string.Equals(normalizedValue, "read-only", StringComparison.OrdinalIgnoreCase))
        {
            return AccessModeParseResult.Ok(McpAccessMode.ReadOnly);
        }

        if (string.Equals(normalizedValue, "read-write", StringComparison.OrdinalIgnoreCase))
        {
            return AccessModeParseResult.Ok(McpAccessMode.ReadWrite);
        }

        return AccessModeParseResult.Fail(
            $"Invalid access mode '{value}'. Valid values: 'read-only', 'read-write'.");
    }
}

public sealed record AccessModeParseResult(bool IsValid, McAccessMode Mode, string? Error)
{
    public static AccessModeParseResult Ok(McpAccessMode mode) => new(true, mode, null);

    public static AccessModeParseResult Fail(string error) => new(false, default, error);
}
