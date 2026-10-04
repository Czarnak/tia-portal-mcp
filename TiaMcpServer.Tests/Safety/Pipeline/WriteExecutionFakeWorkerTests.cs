using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Safety.Pipeline;

/// <summary>
/// <see cref="WriteExecution"/> end to end through the real <see cref="OpennessWorkerClient"/>,
/// the real binding gate and lease, and the JSONL audit sink, against the stateful
/// <c>network-subnet-lifecycle</c> FakeWorker scenario (two subnets, each connected to a node).
/// Each test owns its client and therefore a fresh FakeWorker process and state.
/// </summary>
[Collection(RealWorkerProcessCollection.Name)]
public sealed class WriteExecutionFakeWorkerTests
{
    private const string Scenario = "network-subnet-lifecycle";
    private const string ConnectedSubnet = "subnet-eth-1";

    [Fact]
    public async Task DryRun_ReportsTheConnectedSubnetGuardAndLeavesTheSubnet()
    {
        using var audit = new TempAuditDirectory();
        using var client = await CreateBoundClientAsync();

        var result = await RunAsync(client, audit, ConnectedSubnet, dryRun: true);

        var document = Structured(result);
        Assert.False(result.IsError);
        Assert.Equal(WritePhases.Preview, document.GetProperty("phase").GetString());
        Assert.True(document.GetProperty("success").GetBoolean());
        var guard = Assert.Single(document.GetProperty("guards").EnumerateArray());
        Assert.Equal(SubnetProbeDomain.ConnectedSubnetGuard, guard.GetProperty("id").GetString());
        Assert.Equal(WriteGuardSeverities.Acknowledge, guard.GetProperty("severity").GetString());
        Assert.False(guard.GetProperty("acknowledged").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("batch").ValueKind);
        Assert.Contains(ConnectedSubnet, await SubnetIdsAsync(client));
    }

    [Fact]
    public async Task ReadWrite_WithoutConfirmation_IsBlockedAndLeavesTheSubnet()
    {
        using var audit = new TempAuditDirectory();
        using var client = await CreateBoundClientAsync();

        var result = await RunAsync(client, audit, ConnectedSubnet, dryRun: false);

        var document = Structured(result);
        Assert.True(result.IsError);
        Assert.Equal(WritePhases.Blocked, document.GetProperty("phase").GetString());
        Assert.Equal(
            WorkerFailureCategories.AccessDenied,
            document.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("batch").ValueKind);
        Assert.Contains(ConnectedSubnet, await SubnetIdsAsync(client));
        Assert.Equal(WritePhases.Blocked, Assert.Single(ReadAudit(audit)).GetProperty("phase").GetString());
    }

    [Fact]
    public async Task Full_PolicyDeletesTheSubnetVerifiesTheCountAndAuditsTheTarget()
    {
        using var audit = new TempAuditDirectory();
        using var client = await CreateBoundClientAsync(McpAccessMode.Full);

        var result = await RunAsync(
            client, audit, ConnectedSubnet, dryRun: false);

        var document = Structured(result);
        Assert.False(result.IsError, Text(result));
        Assert.Equal(WritePhases.Applied, document.GetProperty("phase").GetString());
        Assert.True(document.GetProperty("success").GetBoolean());
        Assert.True(Assert.Single(document.GetProperty("guards").EnumerateArray()).GetProperty("acknowledged").GetBoolean());
        var operation = Assert.Single(document.GetProperty("batch").GetProperty("operations").EnumerateArray());
        Assert.Equal("succeeded", operation.GetProperty("status").GetString());
        Assert.Equal(ConnectedSubnet, operation.GetProperty("result").GetProperty("subnetId").GetString());
        var verification = document.GetProperty("verification");
        Assert.Equal(2, verification.GetProperty("subnetCountBefore").GetInt32());
        Assert.Equal(1, verification.GetProperty("subnetCountAfter").GetInt32());
        Assert.DoesNotContain(ConnectedSubnet, await SubnetIdsAsync(client));

        var record = Assert.Single(ReadAudit(audit));
        Assert.Equal(WritePhases.Applied, record.GetProperty("phase").GetString());
        var item = Assert.Single(record.GetProperty("items").EnumerateArray());
        Assert.Equal("succeeded", item.GetProperty("status").GetString());
        Assert.Contains(ConnectedSubnet, item.GetProperty("target").GetString());
        Assert.Equal(
            GuardSatisfactions.Policy,
            Assert.Single(record.GetProperty("guards").EnumerateArray()).GetProperty("satisfiedBy").GetString());
    }

    [Fact]
    public async Task UnknownSubnetId_FailsWithTargetNotFoundAndLeavesBothSubnets()
    {
        using var audit = new TempAuditDirectory();
        using var client = await CreateBoundClientAsync();

        var result = await RunAsync(client, audit, "subnet-eth-9", dryRun: false);

        var document = Structured(result);
        Assert.True(result.IsError);
        Assert.Equal(WritePhases.Error, document.GetProperty("phase").GetString());
        Assert.Equal(
            WorkerFailureCategories.TargetNotFound,
            document.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(2, (await SubnetIdsAsync(client)).Count);
    }

    private static async Task<OpennessWorkerClient> CreateBoundClientAsync(McpAccessMode mode = McpAccessMode.ReadWrite)
    {
        var binding = new ProjectSessionBinding(null);
        var client = new OpennessWorkerClient(
            binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate(), accessPolicy: new(mode));
        try
        {
            await NetworkVerifiedWriteFixture.VerifyAsync(client, binding, Scenario);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static Task<CallToolResult> RunAsync(
        OpennessWorkerClient client,
        TempAuditDirectory audit,
        string subnetId,
        bool dryRun)
    {
        var execution = new WriteExecution(
            new OpennessWriteBindingGate(client),
            new JsonlWriteAuditSink(audit.Path),
            SubnetProbeDomain.Catalog,
            TimeProvider.System);
        var call = new WriteCall<NetworkOperationRequest>(
            Scenario, new[] { SubnetProbeDomain.DeleteSubnet("delete", subnetId, Scenario) }, dryRun);
        return execution.RunAsync(new SubnetProbeDomain(client), call);
    }

    private static async Task<IReadOnlyList<string>> SubnetIdsAsync(OpennessWorkerClient client)
    {
        var state = await NetworkWritePlanner.ReadCurrentStateAsync(client, Scenario);
        Assert.True(state.Success, state.Error);
        return state.State!.Subnets.Select(subnet => subnet.SubnetId).ToArray();
    }

    private static IReadOnlyList<JsonElement> ReadAudit(TempAuditDirectory audit)
        => Directory.GetFiles(audit.Path, "writes-*.jsonl")
            .SelectMany(File.ReadAllLines)
            .Where(line => line.Length > 0)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToArray();

    private static JsonElement Structured(CallToolResult result)
        => Assert.IsType<JsonElement>(result.StructuredContent);

    private static string Text(CallToolResult result)
        => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
}
