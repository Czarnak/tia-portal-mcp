using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Plc;
using TiaMcpServer.Contracts.Block;

namespace TiaMcpServer.Tools;

internal static class PlcWriteToolRegistration
{
    internal static readonly IReadOnlyDictionary<string, string> RetiredItemMembers = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["yamlContent"] = "use 'content' with 'expectedContentHash'",
        ["sourceContent"] = "use 'content' with 'expectedContentHash'",
    };

    internal static JsonNode AddObEventClassEnum(AIJsonSchemaCreateContext context, JsonNode schema)
    {
        if (context.PropertyInfo is { Name: "obEventClass" } property
            && property.DeclaringType == typeof(PlcOperationRequest)
            && schema is JsonObject node)
        {
            var values = new JsonArray();
            foreach (var cls in ObEventClasses.All) values.Add(cls.Name);
            values.Add(null);
            node["enum"] = values;
        }

        return schema;
    }
}

internal static class ClosedToolRegistration
{
    /// <summary>
    /// Registers every public static <see cref="McpServerToolAttribute"/> method of <paramref name="toolType"/>, in
    /// declaration order, as a <see cref="StrictArgumentsTool"/>. <paramref name="decorate"/> wraps the strict tool,
    /// so a bespoke validator runs before the strict check.
    /// </summary>
    internal static IMcpServerBuilder WithClosedTools(this IMcpServerBuilder builder, Type toolType,
        Func<IServiceProvider, string, string, CallToolResult> reject, bool closeNestedSchemas,
        Func<AIJsonSchemaCreateContext, JsonNode, JsonNode>? transformSchemaNode = null,
        IReadOnlyDictionary<string, string>? retiredItemMembers = null,
        Func<string, McpServerTool, McpServerTool>? decorate = null)
    {
        var methods = toolType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.IsDefined(typeof(McpServerToolAttribute)))
            .OrderBy(method => method.MetadataToken);
        foreach (var method in methods)
        {
            builder.Services.AddSingleton(services =>
            {
                var name = "";
                var strict = StrictArgumentsTool.CreateClosedTool(method, services, message => reject(services, name, message),
                    closeNestedSchemas, transformSchemaNode, retiredItemMembers);
                name = strict.ProtocolTool.Name;
                return decorate is null ? strict : decorate(name, strict);
            });
        }

        return builder;
    }
}

/// <summary>
/// Rejects argument-shape errors before SDK binding, so the call never enters the tool: unknown root keys (derived
/// from the input schema), retired item members, missing required keys, forbidden nulls, and arguments that do not
/// deserialize into their bound parameter type with the SDK's own serializer options. Rejections never echo values.
/// </summary>
internal sealed class StrictArgumentsTool : DelegatingMcpServerTool
{
    private const int MaxPathLength = 256;
    private static readonly IReadOnlyDictionary<string, string> NoRetiredMembers = new Dictionary<string, string>();

    private readonly Func<string, CallToolResult> _reject;
    private readonly IReadOnlyDictionary<string, string> _retiredItemMembers;
    private readonly IReadOnlySet<string> _rootKeys;
    private readonly IReadOnlyList<string> _required;
    private readonly IReadOnlySet<string> _nonNullableParameters;
    private readonly IReadOnlyDictionary<string, JsonTypeInfo> _parameterTypes;
    private readonly Tool _protocolTool;

    public StrictArgumentsTool(McpServerTool inner, MethodInfo method, Func<string, CallToolResult> reject,
        IReadOnlyDictionary<string, string>? retiredItemMembers = null) : base(inner)
    {
        _reject = reject;
        _retiredItemMembers = retiredItemMembers ?? NoRetiredMembers;
        _rootKeys = RootKeys(inner);
        var schema = inner.ProtocolTool.InputSchema;
        _required = schema.TryGetProperty("required", out var required)
            ? required.EnumerateArray().Select(key => key.GetString()!).ToArray()
            : [];
        _parameterTypes = method.GetParameters()
            .Where(parameter => parameter.Name is not null && _rootKeys.Contains(parameter.Name))
            .ToDictionary(parameter => parameter.Name!,
                parameter => McpJsonUtilities.DefaultOptions.GetTypeInfo(parameter.ParameterType), StringComparer.Ordinal);
        var nullability = new NullabilityInfoContext();
        _nonNullableParameters = method.GetParameters()
            .Where(parameter => parameter.Name is not null && _rootKeys.Contains(parameter.Name)
                && nullability.Create(parameter).WriteState == NullabilityState.NotNull)
            .Select(parameter => parameter.Name!)
            .ToHashSet(StringComparer.Ordinal);
        var tool = JsonSerializer.SerializeToNode(inner.ProtocolTool, McpJsonUtilities.DefaultOptions)!;
        tool["inputSchema"]!["additionalProperties"] = false;
        _protocolTool = tool.Deserialize<Tool>(McpJsonUtilities.DefaultOptions)!;
    }

    public override Tool ProtocolTool => _protocolTool;

    internal static McpServerTool CreateClosedTool(MethodInfo method, IServiceProvider services,
        Func<string, CallToolResult> reject, bool closeNestedSchemas,
        Func<AIJsonSchemaCreateContext, JsonNode, JsonNode>? transformSchemaNode = null,
        IReadOnlyDictionary<string, string>? retiredItemMembers = null)
        => new StrictArgumentsTool(McpServerTool.Create(method, target: null, options: new McpServerToolCreateOptions
        {
            Services = services,
            SerializerOptions = McpJsonUtilities.DefaultOptions,
            SchemaCreateOptions = new AIJsonSchemaCreateOptions
            {
                TransformSchemaNode = transformSchemaNode,
                TransformOptions = new AIJsonSchemaTransformOptions { DisallowAdditionalProperties = closeNestedSchemas },
            },
        }), method, reject, retiredItemMembers);

    internal static IReadOnlySet<string> RootKeys(McpServerTool tool)
        => tool.ProtocolTool.InputSchema.TryGetProperty("properties", out var properties)
            ? properties.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

    internal static string Message(string toolName, string detail, IReadOnlySet<string> acceptedKeys)
    {
        var accepted = acceptedKeys.Count == 0 ? "none" : string.Join(", ", acceptedKeys.Order(StringComparer.Ordinal));
        return $"{toolName} arguments do not match its input schema: {detail}. Accepted arguments: {accepted}.";
    }

    public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        var detail = Check(request.Params.Arguments ?? new Dictionary<string, JsonElement>());
        return detail is null
            ? base.InvokeAsync(request, cancellationToken)
            : ValueTask.FromResult(_reject(Message(_protocolTool.Name, detail, _rootKeys)));
    }

    private string? Check(IDictionary<string, JsonElement> arguments)
    {
        var unknown = arguments.Keys.FirstOrDefault(key => !_rootKeys.Contains(key));
        if (unknown is not null) return $"unknown argument '{unknown}'";

        var retired = arguments.Values.Where(value => value.ValueKind == JsonValueKind.Array)
            .SelectMany(value => value.EnumerateArray())
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .SelectMany(item => item.EnumerateObject())
            .Select(member => member.Name)
            .FirstOrDefault(_retiredItemMembers.ContainsKey);
        if (retired is not null) return $"operation member '{retired}' is retired: {_retiredItemMembers[retired]}";

        var missing = _required.FirstOrDefault(key => !arguments.ContainsKey(key));
        if (missing is not null) return $"missing required argument '{missing}'";

        foreach (var (name, value) in arguments)
        {
            if (!_parameterTypes.TryGetValue(name, out var type)) continue;
            if (value.ValueKind == JsonValueKind.Null && _nonNullableParameters.Contains(name))
                return $"argument '{name}' must not be null";
            try
            {
                JsonSerializer.Deserialize(value, type);
            }
            catch (JsonException exception)
            {
                // Only the path is reported: JsonException messages and caller values can carry caller data.
                return exception.Path is { Length: <= MaxPathLength } path
                    ? $"argument '{name}' is malformed at {path}"
                    : $"argument '{name}' is malformed";
            }
        }

        return null;
    }
}
