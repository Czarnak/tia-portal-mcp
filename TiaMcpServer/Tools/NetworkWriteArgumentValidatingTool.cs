using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Network;

namespace TiaMcpServer.Tools;

internal static class NetworkWriteToolRegistration
{
    internal static IMcpServerBuilder WithNetworkWriteTools(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<McpServerTool>(services => new NetworkWriteArgumentValidatingTool(
            McpServerTool.Create(typeof(NetworkWriteTools).GetMethod(nameof(NetworkWriteTools.NetworkWrite))!,
                target: null, options: new McpServerToolCreateOptions
                {
                    Services = services,
                    SchemaCreateOptions = new AIJsonSchemaCreateOptions
                    {
                        TransformOptions = new AIJsonSchemaTransformOptions { DisallowAdditionalProperties = true }
                    }
                })));
        return builder;
    }
}

/// <summary>Rejects obsolete or malformed root arguments before the write call enters the runner.</summary>
internal sealed class NetworkWriteArgumentValidatingTool(McpServerTool innerTool) : DelegatingMcpServerTool(innerTool)
{
    public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        var arguments = request.Params.Arguments;
        if (arguments is not null && (arguments.Keys.Any(key => key is not ("operations" or "dryRun"))
            || arguments.TryGetValue("dryRun", out var dryRun) && dryRun.ValueKind is not (JsonValueKind.True or JsonValueKind.False)))
        {
            return ValueTask.FromResult(new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = "network_write accepts only operations and an optional boolean dryRun. Use dryRun:true to preview; omitted or false executes the write." }]
            });
        }
        return base.InvokeAsync(request, cancellationToken);
    }
}
