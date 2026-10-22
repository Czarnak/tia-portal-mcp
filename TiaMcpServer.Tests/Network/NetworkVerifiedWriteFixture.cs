using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// Creates the worker-verified project binding required by production write gates while retaining
/// the existing path-keyed FakeWorker scenarios. The fixture reads the requested scenario path and
/// keeps that exact worker-reported identity. Every later response must still match the captured
/// worker id, Portal PID, generation, and canonical project path.
/// </summary>
internal sealed class NetworkVerifiedWriteFixture : IDisposable
{
    private NetworkVerifiedWriteFixture(
        OpennessWorkerClient client,
        ProjectSessionBinding binding)
    {
        Client = client;
        Binding = binding;
    }

    public OpennessWorkerClient Client { get; }

    public ProjectSessionBinding Binding { get; }

    public static async Task<NetworkVerifiedWriteFixture> CreateAsync(
        TempAuditDirectory audit,
        string projectPath,
        McpAccessMode mode = McpAccessMode.ReadWrite)
    {
        var binding = new ProjectSessionBinding(null);
        var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(mode));

        try
        {
            await VerifyAsync(client, binding, projectPath).ConfigureAwait(false);
            return new NetworkVerifiedWriteFixture(client, binding);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    internal static async Task VerifyAsync(
        OpennessWorkerClient client,
        ProjectSessionBinding binding,
        string projectPath)
    {
        var requestedPath = ProjectPathNormalization.Canonicalize(projectPath);
        if (requestedPath is null)
        {
            throw new InvalidOperationException(
                "The FakeWorker target-project read requires a canonical project path.");
        }

        // Write fixtures require an already-open target; the helper declares it before the first
        // worker launch and binds from the identity-bearing project status.
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, requestedPath).ConfigureAwait(false);
    }

    public void Dispose() => Client.Dispose();
}
