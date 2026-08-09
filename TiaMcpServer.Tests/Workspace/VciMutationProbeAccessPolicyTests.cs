using System.Reflection;
using ModelContextProtocol.Server;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.Safety;
using TiaMcpServer.Tools;
using Xunit;

namespace TiaMcpServer.Tests.Workspace;

public class VciMutationProbeAccessPolicyTests
{
    [Fact]
    public void MutationProbe_IsDeniedInReadOnlyModeAndAllowedInReadWriteMode()
    {
        Assert.NotNull(new OperationAccessPolicy(McpAccessMode.ReadOnly)
            .Authorize(VciMutationProbeContract.OperationName));
        Assert.Null(new OperationAccessPolicy(McpAccessMode.ReadWrite)
            .Authorize(VciMutationProbeContract.OperationName));
        Assert.Equal(
            OperationCapability.ProjectMutation,
            OperationPolicyCatalog.GetCapability(VciMutationProbeContract.OperationName));
        Assert.NotNull(WorkerOperationAuthorization.Authorize(
            McpAccessMode.ReadOnly,
            VciMutationProbeContract.OperationName));
        Assert.Null(WorkerOperationAuthorization.Authorize(
            McpAccessMode.ReadWrite,
            VciMutationProbeContract.OperationName));
    }

    [Fact]
    public void MutationProbe_IsKnownButRemainsAbsentFromThePublicToolSurface()
    {
        Assert.Contains(VciMutationProbeContract.OperationName, OperationPolicyCatalog.AllOperationNames);

        var toolTypes = typeof(ProjectLifecycleTools).Assembly
            .GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null);
        var readWriteToolNames = ToolNames(toolTypes);
        Assert.Equal(14, readWriteToolNames.Length);
        Assert.DoesNotContain(VciMutationProbeContract.OperationName, readWriteToolNames);

        var networkReadToolType = typeof(NetworkOperationRequest).Assembly
            .GetType("TiaMcpServer.Network.NetworkReadTools");
        Assert.NotNull(networkReadToolType);

        var readOnlyToolNames = ToolNames(new[]
        {
            typeof(ProjectReadTools),
            typeof(ReadBatchTools),
            networkReadToolType!,
        });
        Assert.Equal(4, readOnlyToolNames.Length);
        Assert.DoesNotContain(VciMutationProbeContract.OperationName, readOnlyToolNames);
    }

    private static string?[] ToolNames(IEnumerable<Type> types)
        => types
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
            .Where(attribute => attribute is not null)
            .Select(attribute => attribute!.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
}
