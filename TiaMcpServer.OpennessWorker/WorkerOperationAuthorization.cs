using System;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker;

/// <summary>
/// Worker-side authorization. Enforces the immutable access mode before any handler is called.
/// This is the final defense layer: even if a raw worker request bypasses the host, the worker
/// independently rejects prohibited operations.
/// </summary>
internal static class WorkerOperationAuthorization
{
    /// <summary>
    /// Parses the access mode from the worker process command-line arguments.
    /// Missing configuration preserves the historical read-write default. Explicit but malformed
    /// or contradictory configuration fails closed to read-only so a launch or argument-
    /// propagation defect cannot silently enable mutation operations.
    /// </summary>
    public static McpAccessMode ParseAccessMode(string[] args)
    {
        McpAccessMode? explicitMode = null;

        bool RecordMode(McpAccessMode candidate)
        {
            if (explicitMode is not null && explicitMode.Value != candidate)
            {
                return false;
            }

            explicitMode = candidate;
            return true;
        }

        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--access-mode", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length ||
                    string.IsNullOrWhiteSpace(args[i + 1]) ||
                    args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return FailClosed("--access-mode requires a value");
                }

                var parsed = ParseValue(args[++i]);
                if (parsed is null)
                {
                    return FailClosed($"invalid access mode '{args[i]}'");
                }

                if (!RecordMode(parsed.Value))
                {
                    return FailClosed("conflicting access mode arguments were supplied");
                }

                continue;
            }

            const string prefix = "--access-mode=";
            if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var value = args[i].Substring(prefix.Length);
                var parsed = ParseValue(value);
                if (parsed is null)
                {
                    return FailClosed($"invalid access mode '{value}'");
                }

                if (!RecordMode(parsed.Value))
                {
                    return FailClosed("conflicting access mode arguments were supplied");
                }
            }
        }

        return explicitMode ?? McpAccessMode.ReadWrite;
    }

    /// <summary>
    /// Whether the worker may automatically accept TIA Portal confirmation dialogs.
    /// Read-only mode must never auto-confirm an action that could change state.
    /// </summary>
    public static bool AllowsTiaConfirmations(McpAccessMode mode)
        => mode == McpAccessMode.ReadWrite;

    /// <summary>
    /// Returns null if the operation is allowed, or a failure response if denied.
    /// </summary>
    public static WorkerResponse? Authorize(McpAccessMode mode, string operation)
    {
        if (OperationPolicyCatalog.IsAllowed(mode, operation))
        {
            return null;
        }

        return new WorkerResponse
        {
            Success = false,
            Error = $"Operation '{operation}' is disabled because the worker is running in {ModeLabel(mode)} mode.",
            FailureCategory = WorkerFailureCategories.AccessDenied
        };
    }

    private static McpAccessMode? ParseValue(string value)
    {
        var normalizedValue = value.Trim();
        if (string.Equals(normalizedValue, "read-only", StringComparison.OrdinalIgnoreCase))
        {
            return McpAccessMode.ReadOnly;
        }

        if (string.Equals(normalizedValue, "read-write", StringComparison.OrdinalIgnoreCase))
        {
            return McpAccessMode.ReadWrite;
        }

        return null;
    }

    private static McpAccessMode FailClosed(string reason)
    {
        Console.Error.WriteLine(
            $"Error: Worker access-mode configuration is invalid ({reason}). Falling back to read-only.");
        return McpAccessMode.ReadOnly;
    }

    private static string ModeLabel(McpAccessMode mode)
        => mode == McpAccessMode.ReadOnly ? "read-only" : "read-write";
}
