using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Tools;

/// <summary>
/// Temporary probes the maintainer uses to test how a client handles elicitation and the forced-approval
/// marker. Registered only while <see cref="EnvironmentVariable"/> is <c>1</c>; never touches the worker.
/// </summary>
public static class ApprovalSpikeTools
{
    public const string EnvironmentVariable = "TIA_MCP_APPROVAL_SPIKE";

    /// <summary>The fixed reply of the marker probe.</summary>
    public const string MarkedProbeResult = "spike_marked_probe executed";

    private static readonly TimeSpan ElicitationTimeout = TimeSpan.FromMinutes(2);

    /// <summary>True only when the variable is exactly <c>1</c>.</summary>
    public static bool IsEnabled(Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        return getEnvironmentVariable(EnvironmentVariable) == "1";
    }

    /// <summary>Builds the two probe tools; only the marker probe carries the approval marker.</summary>
    public static IReadOnlyList<McpServerTool> Create()
    {
        var marked = McpServerTool.Create(Method(nameof(MarkedProbe)));
        var elicitation = McpServerTool.Create(Method(nameof(ElicitationProbeAsync)));
        return new[] { ToolApprovalMarker.Apply(marked), elicitation };
    }

    private static MethodInfo Method(string name) =>
        typeof(ApprovalSpikeTools).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;

    [McpServerTool(Name = "spike_marked_probe", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Spike probe: carries the forced-approval marker and returns a fixed string. Touches nothing.")]
    public static string MarkedProbe() => MarkedProbeResult;

    [McpServerTool(Name = "spike_elicitation_probe", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Spike probe: asks the client for a confirmation and reports the elicitation capability, outcome and detail. Touches nothing.")]
    public static async Task<string> ElicitationProbeAsync(McpServer server, CancellationToken cancellationToken)
    {
        var supported = server.ClientCapabilities?.Elicitation is not null;
        var result = await UserConfirmation.For(server, ElicitationTimeout)
            .AskAsync("Spike probe: tick confirm to test elicitation.", cancellationToken);
        return $"elicitationCapability={supported}; outcome={result.Outcome}; detail={result.Detail ?? "none"}";
    }
}
