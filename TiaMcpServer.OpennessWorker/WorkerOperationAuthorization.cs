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
    /// </summary>
    public static McpAccessMode ParseAccessMode(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--access-mode", StringComparison.OrdinalIgnoreCase) &&
                i + 1 < args.Length)
            {
                var value = args[i + 1];
                if (string.Equals(value, "read-only", StringComparison.OrdinalIgnoreCase))
                {
                    return McpAccessMode.ReadOnly;
                }

                if (string.Equals(value, "read-write", StringComparison.OrdinalIgnoreCase))
                {
                    return McpAccessMode.ReadWrite;
                }

                Console.Error.WriteLine($"Warning: Invalid worker access mode '{value}'. Defaulting to read-write.");
                return McpAccessMode.ReadWrite;
            }

            const string prefix = "--access-mode=";
            if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var value = args[i].Substring(prefix.Length);
                if (string.Equals(value, "read-only", StringComparison.OrdinalIgnoreCase))
                {
                    return McpAccessMode.ReadOnly;
                }

                if (string.Equals(value, "read-write", StringComparison.OrdinalIgnoreCase))
                {
                    return McpAccessMode.ReadWrite;
                }

                Console.Error.WriteLine($"Warning: Invalid worker access mode '{value}'. Defaulting to read-write.");
                return McpAccessMode.ReadWrite;
            }
        }

        return McpAccessMode.ReadWrite;
    }

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

    private static string ModeLabel(McpAccessMode mode)
        => mode == McpAccessMode.ReadOnly ? "read-only" : "read-write";
}
