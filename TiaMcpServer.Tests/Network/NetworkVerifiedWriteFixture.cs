using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// Creates the worker-verified project binding required by production write gates while retaining
/// the existing path-keyed FakeWorker scenarios. The neutral <c>ok</c> status request starts the
/// FakeWorker and returns its real process-local identity; only the project path is then changed to
/// the scenario under test. Every later response must still match the captured worker id, Portal
/// PID, generation, and canonical project path.
/// </summary>
internal sealed class NetworkVerifiedWriteFixture : IDisposable
{
    private NetworkVerifiedWriteFixture(
        OpennessWorkerClient client,
        WriteSafetyService safety,
        ProjectSessionBinding binding)
    {
        Client = client;
        Safety = safety;
        Binding = binding;
    }

    public OpennessWorkerClient Client { get; }

    public WriteSafetyService Safety { get; }

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
            var safety = audit.CreateSafety(projectSessionBinding: binding);
            return new NetworkVerifiedWriteFixture(client, safety, binding);
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
        var probe = await client.GetProjectStatusAsync("ok").ConfigureAwait(false);
        if (!probe.Success || probe.SessionIdentity is null)
        {
            throw new InvalidOperationException(
                $"The FakeWorker identity probe failed: {probe.Error ?? "missing session identity"}");
        }

        var identity = new WorkerSessionIdentity
        {
            WorkerSessionId = probe.SessionIdentity.WorkerSessionId,
            SessionGeneration = probe.SessionIdentity.SessionGeneration,
            PortalProcessId = probe.SessionIdentity.PortalProcessId,
            ProjectPath = ProjectPathNormalization.Canonicalize(projectPath)
        };

        if (!binding.BindVerified(identity, forceRebind: true, out var error))
        {
            throw new InvalidOperationException($"Could not establish the FakeWorker project binding: {error}");
        }
    }

    public void Dispose() => Client.Dispose();
}
