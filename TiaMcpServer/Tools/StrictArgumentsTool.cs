using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Plc;

namespace TiaMcpServer.Tools;

internal static class PlcWriteToolRegistration
{
    private static readonly IReadOnlySet<string> AllowedKeys = new HashSet<string>(StringComparer.Ordinal) { "operations", "dryRun" };
    private static readonly IReadOnlySet<string> BooleanKeys = new HashSet<string>(StringComparer.Ordinal) { "dryRun" };
    private static readonly IReadOnlyDictionary<string, string> RetiredItemMembers = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["yamlContent"] = "use 'content' with 'expectedContentHash'",
        ["sourceContent"] = "use 'content' with 'expectedContentHash'",
    };

    internal static IMcpServerBuilder WithPlcWriteTools(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<McpServerTool>(services => new StrictArgumentsTool(
            McpServerTool.Create(typeof(PlcWriteTools).GetMethod(nameof(PlcWriteTools.PlcWrite))!,
                target: null, options: new McpServerToolCreateOptions
                {
                    Services = services,
                    SchemaCreateOptions = new AIJsonSchemaCreateOptions
                    {
                        TransformOptions = new AIJsonSchemaTransformOptions { DisallowAdditionalProperties = true }
                    }
                }),
            "plc_write", AllowedKeys, BooleanKeys, RetiredItemMembers));
        return builder;
    }
}

/// <summary>
/// Rejects unknown or malformed root arguments, and retired members inside array items, before SDK
/// binding: the call never enters the write runner, so nothing is audited and no worker is called.
/// </summary>
internal sealed class StrictArgumentsTool(
    McpServerTool inner,
    string toolName,
    IReadOnlySet<string> allowedKeys,
    IReadOnlySet<string> booleanKeys,
    IReadOnlyDictionary<string, string> retiredItemMembers) : DelegatingMcpServerTool(inner)
{
    public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        var arguments = request.Params.Arguments;
        if (arguments is null) return base.InvokeAsync(request, cancellationToken);
        if (arguments.Any(pair => !allowedKeys.Contains(pair.Key)
                || booleanKeys.Contains(pair.Key) && pair.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)))
        {
            var booleans = booleanKeys.Count == 0 ? "" : $" {string.Join(", ", booleanKeys.Order())} must be a boolean when present.";
            return Reject($"{toolName} accepts only these arguments: {string.Join(", ", allowedKeys.Order())}.{booleans}");
        }

        var retired = arguments.Values.Where(value => value.ValueKind == JsonValueKind.Array)
            .SelectMany(value => value.EnumerateArray())
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .SelectMany(item => item.EnumerateObject())
            .Select(member => member.Name)
            .FirstOrDefault(retiredItemMembers.ContainsKey);
        return retired is null
            ? base.InvokeAsync(request, cancellationToken)
            : Reject($"{toolName} no longer accepts the operation member '{retired}': {retiredItemMembers[retired]}.");
    }

    private static ValueTask<CallToolResult> Reject(string text) => ValueTask.FromResult(new CallToolResult
    {
        IsError = true,
        Content = [new TextContentBlock { Text = text }],
    });
}
