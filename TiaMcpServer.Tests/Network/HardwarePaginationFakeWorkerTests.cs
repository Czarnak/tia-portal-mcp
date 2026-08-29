using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// End-to-end evidence for the public hardware-page sequence.  The real host coordinator and
/// executor run here; only the net48 worker boundary is replaced with the scripted FakeWorker.
/// </summary>
public sealed class HardwarePaginationFakeWorkerTests
{
    private const string Scenario = "hardware-pagination";

    [Fact]
    public async Task NetworkRead_PagedHardwareReconstructsTheStableDeviceThenSubnetSequence()
    {
        using var client = CreateClient();

        var first = await ReadPage(client, pageSize: 2, projectPath: Scenario);
        var second = await ReadPage(
            client,
            pageSize: 1,
            cursor: first.GetProperty("pagination").GetProperty("nextCursor").GetString());
        var third = await ReadPage(
            client,
            pageSize: 2,
            cursor: second.GetProperty("pagination").GetProperty("nextCursor").GetString());

        Assert.Equal(
            new[] { "PLC_DUP", "PLC_DUP", "ET200_GROUPED" },
            first.GetProperty("devices").EnumerateArray()
                .Concat(second.GetProperty("devices").EnumerateArray())
                .Concat(third.GetProperty("devices").EnumerateArray())
                .Select(device => device.GetProperty("name").GetString())
                .ToArray());
        Assert.Equal(
            new[] { "PN/IE_MAIN", "PN/IE_REMOTE" },
            first.GetProperty("subnets").EnumerateArray()
                .Concat(second.GetProperty("subnets").EnumerateArray())
                .Concat(third.GetProperty("subnets").EnumerateArray())
                .Select(subnet => subnet.GetProperty("name").GetString())
                .ToArray());

        Assert.Equal(3, first.GetProperty("pagination").GetProperty("totalDevices").GetInt32());
        Assert.Equal(2, first.GetProperty("pagination").GetProperty("totalSubnets").GetInt32());
        Assert.False(third.GetProperty("pagination").TryGetProperty("nextCursor", out _));
    }

    [Theory]
    [InlineData("hardware-pagination-missing-identity")]
    [InlineData("hardware-pagination-malformed-offset")]
    [InlineData("hardware-pagination-incoherent-counts")]
    [InlineData("hardware-pagination-wrong-payload")]
    public async Task NetworkRead_InvalidCandidateResponsesFailClosedAsProtocolErrors(string scenario)
    {
        using var client = CreateClient();

        var operation = await ReadOperation(client, pageSize: 1, projectPath: scenario);

        Assert.Equal("failed", operation.GetProperty("status").GetString());
        Assert.Equal(
            WorkerFailureCategories.ProtocolError,
            operation.GetProperty("failure").GetProperty("category").GetString());
        Assert.DoesNotContain("Nested locator fixture", operation.GetRawText(), StringComparison.Ordinal);
    }

    private static OpennessWorkerClient CreateClient()
        => new(new ProjectSessionBinding(null), logger: null, workerExecutablePath: FakeWorkerLocator.Locate());

    private static async Task<JsonElement> ReadPage(
        OpennessWorkerClient client,
        int pageSize,
        string? projectPath = null,
        string? cursor = null)
    {
        var operation = await ReadOperation(client, pageSize, projectPath, cursor);
        Assert.True(
            string.Equals("succeeded", operation.GetProperty("status").GetString(), StringComparison.Ordinal),
            operation.GetRawText());
        return operation.GetProperty("result").Clone();
    }

    private static async Task<JsonElement> ReadOperation(
        OpennessWorkerClient client,
        int pageSize,
        string? projectPath = null,
        string? cursor = null)
    {
        var result = await NetworkReadTools.NetworkRead(
            client,
            new[]
            {
                new NetworkOperationRequest
                {
                    OperationId = "hardware",
                    Operation = "read_hardware_config",
                    ProjectPath = projectPath,
                    PageSize = pageSize,
                    Cursor = cursor,
                },
            });

        Assert.False(result.IsError, Text(result));
        return Assert.IsType<JsonElement>(result.StructuredContent)
            .GetProperty("batch")
            .GetProperty("operations")[0]
            .Clone();
    }

    private static string Text(ModelContextProtocol.Protocol.CallToolResult result)
        => string.Join("\n", result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(block => block.Text));
}
