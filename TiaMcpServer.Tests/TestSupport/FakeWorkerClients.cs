using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tests.TestSupport;

internal static class FakeWorkerClients
{
    internal static OpennessWorkerClient Create(
        McpAccessMode mode = McpAccessMode.ReadWrite,
        ProjectSessionBinding? binding = null,
        string? workerPath = null,
        TimeSpan? requestTimeout = null)
        => new(binding ?? new ProjectSessionBinding(null),
            workerExecutablePath: workerPath ?? FakeWorkerLocator.Locate(),
            requestTimeout: requestTimeout,
            accessPolicy: new OperationAccessPolicy(mode));

    internal static async Task<OpennessWorkerClient> CreateBoundAsync(
        string scenario,
        McpAccessMode mode = McpAccessMode.ReadWrite)
    {
        var binding = new ProjectSessionBinding(null);
        var client = Create(mode, binding);
        try
        {
            await FakeWorkerBinding.BindVerifiedAsync(client, binding, scenario);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}
