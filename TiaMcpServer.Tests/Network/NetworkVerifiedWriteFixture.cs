using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
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
        var requestedPath = ProjectPathNormalization.Canonicalize(projectPath);
        if (requestedPath is null)
        {
            throw new InvalidOperationException(
                "The FakeWorker target-project read requires a canonical project path.");
        }

        var probe = await client
            .ReadHardwareConfigAsync(requestedPath)
            .ConfigureAwait(false);
        if (!probe.Success || probe.SessionIdentity is null)
        {
            throw new InvalidOperationException(
                $"The FakeWorker target-project read failed: " +
                $"{probe.Error ?? "missing session identity"}");
        }

        var reportedPath = ProjectPathNormalization.Canonicalize(
            probe.SessionIdentity.ProjectPath);
        if (reportedPath is null ||
            !string.Equals(
                requestedPath,
                reportedPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The FakeWorker reported project '{reportedPath ?? "<missing>"}' " +
                $"for requested project '{requestedPath ?? "<missing>"}'.");
        }

        if (!binding.BindVerified(
                probe.SessionIdentity,
                forceRebind: false,
                out var error))
        {
            throw new InvalidOperationException(
                $"Could not establish the FakeWorker project binding: {error}");
        }
    }

    public void Dispose() => Client.Dispose();
}
