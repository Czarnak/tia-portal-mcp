using System.Text.Json;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Cursors;
using TiaMcpServer.Hmi;
using TiaMcpServer.Network;
using TiaMcpServer.Plc;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.TestSupport;
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

    private static OpennessWorkerClient CreateClient()
        => new(
            new ProjectSessionBinding(null),
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(McpAccessMode.ReadOnly));

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
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath(PlcScenario);
        using var client = CreateClient();
        var operations = new[]
        {
            new PlcOperationRequest
            {
                OperationId = "b", Operation = "get_block_content", BlockPath = "PLC_1/Main", ProjectPath = PlcScenario,
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
        using var client = CreateClient();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NetworkReadTools.NetworkRead(
                client,
                new[] { new NetworkOperationRequest { OperationId = "n", Operation = "search_equipment_catalog", Query = "CPU" } },
                Cancelled()));

        Assert.Empty(log.Methods());
    }

    [Fact]
    public async Task HmiRead_CancelledToken_ThrowsWithoutWorkerRequest()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        using var client = CreateClient();
        var codec = new HmiPageCursorCodec(AuthenticatedCursorProtector.CreateProcessScoped());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            HmiReadTools.HmiRead(
                client,
                codec,
                new[] { new HmiOperationRequest { OperationId = "h", Operation = "list_hmi_devices" } },
                Cancelled()));

        Assert.Empty(log.Methods());
    }

    [Fact]
    public async Task VciRead_CancelledToken_ThrowsWithoutWorkerRequest()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        using var client = CreateClient();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            VciReadTools.VciRead(
                client,
                new[] { new VciOperationRequest { OperationId = "v", Operation = "list_workspaces" } },
                Cancelled()));

        Assert.Empty(log.Methods());
    }
}