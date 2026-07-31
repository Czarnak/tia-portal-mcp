using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// Verifies the MCP input schema the SDK actually generates for the write tools never exposes
/// the DI-injected <c>safety</c>/<c>workerClient</c> parameters as model-supplied arguments.
///
/// Those tools declare <c>WriteSafetyService safety</c> and <c>OpennessWorkerClient
/// workerClient</c> as plain typed parameters with no [FromServices]-style attribute, relying
/// entirely on the MCP SDK inferring "these come from DI, not the model" from
/// McpServerToolCreateOptions.Services (mirroring exactly how the real host wires tools in
/// Program.cs via WithToolsFromAssembly). If the SDK ever stopped recognizing that - a version
/// bump, a change in how Services is threaded through - every write tool's schema would gain a
/// required object argument no model can ever supply, taking down the entire write surface,
/// while BatchToolMetadataTests and WriteToolSafetyTokenTests (which only reflect over
/// [McpServerTool]/[Description] attributes) would stay green.
///
/// This is why it has to inspect McpServerTool.Create(...).ProtocolTool.InputSchema - the actual
/// generated JSON schema - rather than attributes.
/// </summary>
public class McpToolSchemaTests
{
    private static readonly IServiceProvider Services = BuildServices();

    private static IServiceProvider BuildServices()
    {
        var binding = new ProjectSessionBinding(null);
        var workerClient = new OpennessWorkerClient(binding);
        var safety = new WriteSafetyService();
        return new FakeServiceProvider(binding, workerClient, safety);
    }

    private static string[] SchemaPropertyNames(Type toolType, string methodName)
    {
        var method = toolType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);

        var tool = McpServerTool.Create(
            method!,
            target: null,
            options: new McpServerToolCreateOptions { Services = Services });

        var inputSchema = tool.ProtocolTool.InputSchema;
        var names = inputSchema.TryGetProperty("properties", out var properties)
            ? properties.EnumerateObject().Select(p => p.Name).ToArray()
            : Array.Empty<string>();

        // A DoesNotContain-only assertion downstream would pass vacuously if schema generation
        // silently broke for a tool (empty/missing "properties" node) - this makes that a hard
        // failure instead of a false green.
        Assert.NotEmpty(names);
        return names;
    }

    [Theory]
    [InlineData(nameof(ProjectLifecycleTools.GetProjectStatus))]
    [InlineData(nameof(ProjectLifecycleTools.OpenProject))]
    [InlineData(nameof(ProjectLifecycleTools.CreateProject))]
    [InlineData(nameof(ProjectLifecycleTools.SaveProject))]
    [InlineData(nameof(ProjectLifecycleTools.SaveProjectAs))]
    [InlineData(nameof(ProjectLifecycleTools.ArchiveProject))]
    [InlineData(nameof(ProjectLifecycleTools.CloseProject))]
    public void ProjectLifecycleTools_SchemaNeverExposesInjectedServiceParameters(string methodName)
    {
        var properties = SchemaPropertyNames(typeof(ProjectLifecycleTools), methodName);

        Assert.DoesNotContain("workerClient", properties);
        Assert.DoesNotContain("safety", properties);
    }

    /// <summary>
    /// Whole-assembly enumeration (not a hardcoded/spot-checked list of tool classes): finds
    /// every type carrying <see cref="McpServerToolTypeAttribute"/> in the same assembly
    /// ProjectLifecycleTools lives in - which, for TiaMcpServer.Tests, is the test assembly
    /// itself, since the host's tool source files are compiled directly into it (see
    /// TiaMcpServer.Tests.csproj's Compile Include entries). Counts every method on those types
    /// carrying [McpServerTool] and asserts the exact approved surface: 10 tools total, and the
    /// internal lifecycle probe (probe_project_status_for_lifecycle, never [McpServerTool]-decorated)
    /// absent.
    /// </summary>
    [Fact]
    public void McpToolSurface_ExposesExactlyTenApprovedTools()
    {
        var toolTypes = typeof(ProjectLifecycleTools).Assembly
            .GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null);

        var toolNames = toolTypes
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>())
            .Where(a => a is not null)
            .Select(a => a!.Name)
            .ToArray();

        Assert.Equal(10, toolNames.Length);
        Assert.Equal(10, toolNames.Distinct().Count());
        Assert.DoesNotContain("probe_project_status_for_lifecycle", toolNames);
    }

    [Fact]
    public void OpenProject_SchemaStillExposesProjectPathAsAModelArgument()
    {
        // Negative assertions alone would also pass if the SDK generated an empty schema for
        // every tool (e.g. a broken reflection path) - this proves it did pick up the real,
        // model-facing parameters, not just excluded the DI ones.
        var properties = SchemaPropertyNames(typeof(ProjectLifecycleTools), nameof(ProjectLifecycleTools.OpenProject));

        Assert.Contains("projectPath", properties);
        Assert.Contains("confirm", properties);
        Assert.Contains("safetyToken", properties);
    }

    [Theory]
    [InlineData(nameof(BatchTools.PreviewWriteBatch))]
    [InlineData(nameof(BatchTools.ApplyWriteBatch))]
    public void BatchTools_SchemaNeverExposesInjectedServiceParameters(string methodName)
    {
        var properties = SchemaPropertyNames(typeof(BatchTools), methodName);

        Assert.DoesNotContain("workerClient", properties);
        Assert.DoesNotContain("safety", properties);
        Assert.Contains("operations", properties);
    }

    /// <summary>
    /// Minimal hand-rolled IServiceProvider (rather than pulling in Microsoft.Extensions.DependencyInjection
    /// for a ServiceCollection) - the schema generator only needs GetService(type) to resolve for
    /// the exact runtime types the tools declare, mirroring Program.cs's real DI registrations.
    /// </summary>
    private sealed class FakeServiceProvider : IServiceProvider, IServiceProviderIsService
    {
        private readonly Dictionary<Type, object> _services;

        public FakeServiceProvider(params object[] services)
            => _services = services.ToDictionary(s => s.GetType(), s => s);

        public object? GetService(Type serviceType)
        {
            // The SDK's schema builder does not cast Services to IServiceProviderIsService - it
            // asks the container to RESOLVE one: options.Services.GetService<IServiceProviderIsService>().
            // The real Microsoft.Extensions.DependencyInjection ServiceProvider registers itself
            // as that service internally; a hand-rolled IServiceProvider has to do the same, or
            // every DI-injected parameter silently falls back to a required schema property
            // (confirmed empirically before this line was added: "workerClient" showed up as a
            // required {"type":"object"} property even though this class already implemented
            // IServiceProviderIsService.IsService below - implementing the interface was not
            // enough, resolving it through GetService is what the SDK actually calls).
            if (serviceType == typeof(IServiceProviderIsService))
            {
                return this;
            }

            return _services.TryGetValue(serviceType, out var service) ? service : null;
        }

        public bool IsService(Type serviceType) => _services.ContainsKey(serviceType);
    }

    [Fact]
    public void BrowseProjectTree_SchemaExposesOnlyModelInputs()
    {
        var properties = SchemaPropertyNames(
            typeof(ProjectReadTools),
            nameof(ProjectReadTools.BrowseProjectTree));

        Assert.Equal(
            new[] { "depth", "projectPath", "startPath" },
            properties.OrderBy(name => name).ToArray());
        Assert.DoesNotContain("workerClient", properties);
    }
}
