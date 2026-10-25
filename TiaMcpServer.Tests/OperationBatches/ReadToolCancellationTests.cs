using System.Text.Json;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Cursors;
using TiaMcpServer.Hmi;
using TiaMcpServer.Network;
using TiaMcpServer.Plc;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Vci;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.OperationBatches;

/// <summary>
/// A client cancel stops each batch read tool before its next item: the call throws and sends the
/// worker nothing. The log sets a process-wide variable, so these stay in the real-worker collection
/// and create the log before the client's first request.
/// </summary>
[Collection(RealWorkerProcessCollection.Name)]
public class ReadToolCancellationTests
{
    private const string PlcScenario = "plc-read-roundtrip";

    // Bound first, so the bind-first precheck passes and the cancellation check is what stops the call.
    private static async Task<OpennessWorkerClient> CreateClientAsync()
    {
        var binding = new ProjectSessionBinding(null);
        var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(McpAccessMode.ReadOnly));
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, PlcScenario);
        return client;
    }

    private static CancellationToken Cancelled()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        return cts.Token;
    }

    [Fact]
    public async Task PlcRead_CancelledToken_ThrowsWithoutWorkerRequest()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        using var client = await CreateClientAsync();
        var operations = new[]
        {
            new PlcOperationRequest
            {
                OperationId = "b", Operation = "get_block_content", BlockPath = "PLC_1/Main",
            },
        };

        var sanity = await PlcReadTools.PlcRead(client, operations);
        var status = Assert.IsType<JsonElement>(sanity.StructuredContent)
            .GetProperty("batch").GetProperty("operations")[0].GetProperty("status").GetString();
        Assert.Equal("succeeded", status);
        var before = log.Methods().Length;
        Assert.True(before > 0);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PlcReadTools.PlcRead(client, operations, Cancelled()));

        Assert.Equal(before, log.Methods().Length);
    }

    [Fact]
    public async Task NetworkRead_CancelledToken_ThrowsWithoutWorkerRequest()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        using var client = await CreateClientAsync();
        using var protector = AuthenticatedCursorProtector.CreateProcessScoped();
        var executor = NetworkReadTestFactory.Create(client, protector);
        var before = log.Methods().Length;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NetworkReadTools.NetworkRead(
                client,
                executor,
                new[] { new NetworkOperationRequest { OperationId = "n", Operation = "search_equipment_catalog", Query = "CPU" } },
                Cancelled()));

        Assert.Equal(before, log.Methods().Length);
    }

    [Fact]
    public async Task HmiRead_CancelledToken_ThrowsWithoutWorkerRequest()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        using var client = await CreateClientAsync();
        var before = log.Methods().Length;
        var codec = new HmiPageCursorCodec(AuthenticatedCursorProtector.CreateProcessScoped());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            HmiReadTools.HmiRead(
                client,
                codec,
                new[] { new HmiOperationRequest { OperationId = "h", Operation = "list_hmi_devices" } },
                Cancelled()));

        Assert.Equal(before, log.Methods().Length);
    }

    [Fact]
    public async Task VciRead_CancelledToken_ThrowsWithoutWorkerRequest()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        using var client = await CreateClientAsync();
        var before = log.Methods().Length;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            VciReadTools.VciRead(
                client,
                new[] { new VciOperationRequest { OperationId = "v", Operation = "list_workspaces" } },
                Cancelled()));

        Assert.Equal(before, log.Methods().Length);
    }
}
