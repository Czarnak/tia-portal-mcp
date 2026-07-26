using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Safety;

/// <summary>
/// Immutable singleton that enforces the access mode policy. Every worker invocation
/// must pass through <see cref="Authorize"/> before any request is sent to the worker
/// process. This is the primary host-side defense: it rejects prohibited operations
/// before the worker transport is started, before TIA Portal is connected, and before
/// any state snapshots are collected.
/// </summary>
public sealed class OperationAccessPolicy
{
    public OperationAccessPolicy(McpAccessMode mode)
    {
        Mode = mode;
    }

    public McpAccessMode Mode { get; }

    /// <summary>
    /// Checks whether <paramref name="operation"/> is permitted under the current mode.
    /// Returns null when allowed, or a <see cref="WorkerCallResult"/> failure when denied.
    /// </summary>
    public WorkerCallResult? Authorize(string operation)
    {
        if (OperationPolicyCatalog.IsAllowed(Mode, operation))
        {
            return null;
        }

        return WorkerCallResult.Fail(
            WorkerFailureCategories.AccessDenied,
            $"Operation '{operation}' is disabled because this MCP server is running in {ModeLabel()} mode.");
    }

    private string ModeLabel() => Mode == McpAccessMode.ReadOnly ? "read-only" : "read-write";
}
