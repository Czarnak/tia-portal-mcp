using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Plc;
using TiaMcpServer.Safety;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tests.Plc;

/// <summary>
/// A verified binding to one stateful plc-write FakeWorker scenario and the production guard
/// catalog. The scenario name is the project path. <see cref="AfterGate"/> runs once the gate has
/// verified the binding, to model a binding change between the gate and the lease.
/// </summary>
internal sealed class PlcGuardedWriteFixture : IDisposable
{
    private readonly string _path;

    private PlcGuardedWriteFixture(OpennessWorkerClient client, ProjectSessionBinding binding, string auditDirectory, string path)
    {
        Client = client;
        Binding = binding;
        _path = path;
        Runner = new WriteExecution(new ShiftingGate(new OpennessWriteBindingGate(client), () => AfterGate?.Invoke()),
            new JsonlWriteAuditSink(auditDirectory), WriteGuardRegistration.ProductionCatalog(), TimeProvider.System);
    }

    public OpennessWorkerClient Client { get; }

    public ProjectSessionBinding Binding { get; }

    public WriteExecution Runner { get; }

    public Action? AfterGate { get; set; }

    public static async Task<PlcGuardedWriteFixture> CreateAsync(TempAuditDirectory audit, string scenario,
        McpAccessMode mode = McpAccessMode.ReadWrite)
    {
        Directory.CreateDirectory(audit.Path);
        var binding = new ProjectSessionBinding(null);
        var client = new OpennessWorkerClient(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(mode));
        try
        {
            await NetworkVerifiedWriteFixture.VerifyAsync(client, binding, scenario);
            return new PlcGuardedWriteFixture(client, binding, audit.Path, scenario);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task<PlcGuardedWriteResponse> RunAsync(bool dryRun, params PlcOperationRequest[] operations)
        => Document(await RunRawAsync(dryRun, operations));

    public Task<CallToolResult> RunRawAsync(bool dryRun, params PlcOperationRequest[] operations)
        => Runner.RunAsync(new PlcWriteDomain(Client),
            new WriteCall<PlcOperationRequest>(operations.FirstOrDefault(o => o.ProjectPath is not null)?.ProjectPath ?? _path, operations, dryRun));

    public static PlcGuardedWriteResponse Document(CallToolResult result)
        => CanonicalJson.Deserialize<PlcGuardedWriteResponse>(((JsonElement)result.StructuredContent!).GetRawText());

    /// <summary>The contentHash a plc_read of <paramref name="blockPath"/> in <paramref name="format"/> returns.</summary>
    public async Task<string> ReadHashAsync(string blockPath, string format)
    {
        var read = new PlcOperationRequest { OperationId = "read", Operation = "get_block_content", BlockPath = blockPath, Format = format };
        var item = PlcPayloadContract.Project(read, await PlcWorkerInvoker.InvokeReadAsync(Client, read));
        return item.Result!.Value.GetProperty("contentHash").GetString()!;
    }

    public async Task<string> InventoryAsync() => (await Client.ListTagTablesAsync(null, _path)).Payload;

    public void Dispose() => Client.Dispose();

    public static PlcOperationRequest CreateTable(string id, string table = "Valves", string plc = "PLC_2")
        => new() { OperationId = id, Operation = "create_tag_table", PlcName = plc, TableName = table };

    public static PlcOperationRequest CreateTag(string id, string name, string table = "Default tag table", string plc = "PLC_2")
        => new() { OperationId = id, Operation = "create_tag", PlcName = plc, TableName = table, Name = name, DataType = "Bool" };

    public static PlcOperationRequest UpdateBlock(string id, string blockPath, string hash, string format = "xml")
        => new() { OperationId = id, Operation = "update_block_logic", BlockPath = blockPath, Format = format,
            Content = $"<Block written=\"{id}\"/>", ExpectedContentHash = hash };

    public static PlcOperationRequest CreateBlock(string id, string blockPath)
        => new() { OperationId = id, Operation = "create_block", BlockPath = blockPath, BlockType = "FC" };

    private sealed class ShiftingGate(IWriteBindingGate inner, Action afterGate) : IWriteBindingGate
    {
        public McpAccessMode AccessMode => inner.AccessMode;

        public ProjectBindingSnapshot CurrentBinding => inner.CurrentBinding;

        public async Task<WorkerCallResult> RequireVerifiedWriteBindingAsync(string? projectPath)
        {
            var result = await inner.RequireVerifiedWriteBindingAsync(projectPath);
            afterGate();
            return result;
        }

        public Task<WriteLeaseResult<T>> RunUnderLeaseAsync<T>(ProjectBindingSnapshot binding, Func<Task<T>> operation)
            where T : class
            => inner.RunUnderLeaseAsync(binding, operation);

        public Task<WriteLeaseResult<T>> RunUnderLeaseAsync<T>(ProjectBindingSnapshot binding, Func<Task<T>> operation,
            CancellationToken cancellationToken)
            where T : class
            => inner.RunUnderLeaseAsync(binding, operation, cancellationToken);
    }
}
