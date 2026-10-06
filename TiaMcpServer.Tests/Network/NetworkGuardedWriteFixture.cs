using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.Tools;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tests.Network;

internal sealed class NetworkGuardedWriteFixture : IDisposable
{
    private readonly string _path;
    public OpennessWorkerClient Client { get; }
    public ProjectSessionBinding Binding { get; }
    public WriteExecution Runner { get; }
    public JsonlWriteAuditSink Audit { get; }
    internal static WriteExecution CreateRunner(OpennessWorkerClient client, string auditDirectory)
        => new(new OpennessWriteBindingGate(client), new JsonlWriteAuditSink(auditDirectory),
            WriteGuardRegistration.ProductionCatalog(), TimeProvider.System);
    private NetworkGuardedWriteFixture(OpennessWorkerClient client, ProjectSessionBinding binding, TempAuditDirectory audit, string path)
    {
        Client = client; Binding = binding; _path = path;
        Audit = new JsonlWriteAuditSink(audit.Path);
        Runner = new WriteExecution(new OpennessWriteBindingGate(client), Audit,
            new WriteGuardCatalog(NetworkGuardDefinitions.Definitions), TimeProvider.System);
    }
    public static async Task<NetworkGuardedWriteFixture> CreateAsync(TempAuditDirectory audit, string projectPath, McpAccessMode mode = McpAccessMode.ReadWrite)
    {
        Directory.CreateDirectory(audit.Path);
        var binding = new ProjectSessionBinding(null);
        var client = new OpennessWorkerClient(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate(), accessPolicy: new(mode));
        try
        {
            await NetworkVerifiedWriteFixture.VerifyAsync(client, binding, projectPath);
            return new(client, binding, audit, projectPath);
        }
        catch { client.Dispose(); throw; }
    }
    public async Task<NetworkGuardedWriteResponse> RunAsync(bool dryRun, params NetworkOperationRequest[] operations)
    {
        var result = await Runner.RunAsync(new NetworkWriteDomain(Client), new WriteCall<NetworkOperationRequest>(_path, operations, dryRun));
        return CanonicalJson.Deserialize<NetworkGuardedWriteResponse>(((JsonElement)result.StructuredContent!).GetRawText());
    }
    public void Dispose() => Client.Dispose();
    public static NetworkOperationRequest Delete(string id = "delete") => new() { OperationId = id, Operation = "delete_subnet", Target = new() { Kind = "subnet", SubnetId = "subnet-1" } };
    public static NetworkOperationRequest Configure(string id, string? address = null, bool connect = false) => new()
    {
        OperationId = id, Operation = "configure_network_device", Target = new() { DeviceName = "PLC_Grouped", NodeId = "node-2" },
        Changes = new() { IpAddress = address, Subnet = connect ? new() { SubnetId = "subnet-1" } : null }
    };
}
