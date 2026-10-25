using System.Text.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Cursors;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// End-to-end evidence for the public hardware-page sequence.  The real host coordinator and
/// executor run here; only the net48 worker boundary is replaced with the scripted FakeWorker.
/// </summary>
[Collection(RealWorkerProcessCollection.Name)]
public sealed class HardwarePaginationFakeWorkerTests
{
    private const string Scenario = "hardware-pagination";

    // The trimming scenario below deliberately sits close to HardwarePageProjector's 60,000-char
    // page budget to prove the "only complete trailing candidates" trimming behavior. A bare
    // scenario key (e.g. "hardware-pagination-trimming") gets absolutized by
    // ProjectPathNormalization.Canonicalize (Path.GetFullPath) against the FakeWorker process's
    // current working directory, and that resolved path is itself embedded in the page's
    // nextCursor. Its length therefore varies with wherever the repository happens to be checked
    // out, which silently shifts the trimming boundary — this is what made the assertion below
    // pass on some machines/checkouts and fail on others (observed: CI). Using an already-rooted
    // literal makes Canonicalize a no-op, so the resolved path — and the boundary — stop depending
    // on the checkout location. The cursor of a bound read also carries the host binding's path,
    // so the literal is kept short to stay inside the fixture's calibrated headroom.
    private const string TrimmingScenarioProjectPath = @"C:\hardware-pagination-trimming";

    [Fact]
    public async Task NetworkRead_PagedHardwareReconstructsTheStableDeviceThenSubnetSequence()
    {
        using var client = await CreateClientAsync(Scenario);
        using var protector = AuthenticatedCursorProtector.CreateProcessScoped();
        var executor = NetworkReadTestFactory.Create(client, protector);

        var first = await ReadPage(client, executor, pageSize: 2, projectPath: Scenario);
        var second = await ReadPage(
            client,
            executor,
            pageSize: 1,
            cursor: first.GetProperty("pagination").GetProperty("nextCursor").GetString());
        var third = await ReadPage(
            client,
            executor,
            pageSize: 2,
            cursor: second.GetProperty("pagination").GetProperty("nextCursor").GetString());

        AssertPageCounters(first, 3, 2);
        AssertPageCounters(second, 3, 2);
        AssertPageCounters(third, 3, 2);

        Assert.Equal(
            new[] { "PLC_DUP:OrderNumber:CPU-1515", "PLC_DUP:OrderNumber:CPU-1516", "ET200_GROUPED:OrderNumber:ET200" },
            first.GetProperty("devices").EnumerateArray()
                .Concat(second.GetProperty("devices").EnumerateArray())
                .Concat(third.GetProperty("devices").EnumerateArray())
                .Select(device => $"{device.GetProperty("name").GetString()}:{device.GetProperty("typeIdentifier").GetString()}")
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
        Assert.Equal("Fixture page diagnostic.", first.GetProperty("messages")[0].GetString());
        Assert.DoesNotContain("Candidate diagnostic: main subnet.", first.GetProperty("messages").EnumerateArray().Select(message => message.GetString()));
        Assert.Contains("Candidate diagnostic: main subnet.", third.GetProperty("messages").EnumerateArray().Select(message => message.GetString()));
        Assert.False(third.GetProperty("pagination").TryGetProperty("nextCursor", out _));
    }

    [Theory]
    [InlineData("hardware-pagination-missing-identity")]
    [InlineData("hardware-pagination-malformed-offset")]
    [InlineData("hardware-pagination-incoherent-counts")]
    [InlineData("hardware-pagination-wrong-payload")]
    public async Task NetworkRead_InvalidCandidateResponsesFailClosedAsProtocolErrors(string scenario)
    {
        using var client = await CreateClientAsync(scenario);
        using var protector = AuthenticatedCursorProtector.CreateProcessScoped();
        var executor = NetworkReadTestFactory.Create(client, protector);

        var operation = await ReadOperation(client, executor, pageSize: 1, projectPath: scenario);

        Assert.Equal("failed", operation.GetProperty("status").GetString());
        Assert.Equal(
            WorkerFailureCategories.ProtocolError,
            operation.GetProperty("failure").GetProperty("category").GetString());
        // The wrong-shape fixture's private payload sentinel must never be echoed by the public
        // protocol_error document. This fails if the host starts forwarding rejected worker JSON.
        Assert.DoesNotContain("not-public", operation.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NetworkRead_ChangedCursorBoundFilterFailsBeforeTheWorker()
    {
        using var client = await CreateClientAsync("hardware-pagination-telemetry");
        using var protector = AuthenticatedCursorProtector.CreateProcessScoped();
        var executor = NetworkReadTestFactory.Create(client, protector);
        var first = await ReadPage(client, executor, 1, "hardware-pagination-telemetry");
        var operation = await ReadOperation(client, executor, 1, cursor: Cursor(first), deviceName: "PLC_DUP");
        var resumed = await ReadPage(client, executor, 1, cursor: Cursor(first));

        Assert.Equal(WorkerFailureCategories.CursorFilterMismatch, FailureCategory(operation));
        Assert.Contains("Worker candidate requests: 2.", resumed.GetProperty("messages").EnumerateArray().Select(message => message.GetString()));
    }

    [Fact]
    public async Task NetworkRead_ChangedCursorBoundDetailFlagFailsBeforeTheWorker()
    {
        using var client = await CreateClientAsync("hardware-pagination-telemetry");
        using var protector = AuthenticatedCursorProtector.CreateProcessScoped();
        var executor = NetworkReadTestFactory.Create(client, protector);
        var first = await ReadPage(client, executor, 1, "hardware-pagination-telemetry");
        var operation = await ReadOperation(client, executor, 1, cursor: Cursor(first), includeIoDetails: true);
        var resumed = await ReadPage(client, executor, 1, cursor: Cursor(first));

        Assert.Equal(WorkerFailureCategories.CursorFilterMismatch, FailureCategory(operation));
        Assert.Contains("Worker candidate requests: 2.", resumed.GetProperty("messages").EnumerateArray().Select(message => message.GetString()));
    }

    [Theory]
    [InlineData("hardware-pagination-snapshot-drift", WorkerFailureCategories.CursorSnapshotMismatch)]
    [InlineData("hardware-pagination-out-of-range", WorkerFailureCategories.CursorOutOfRange)]
    [InlineData("hardware-pagination-identity-drift", WorkerFailureCategories.CursorBindingMismatch)]
    public async Task NetworkRead_ContinuationWorkerDriftUsesTheApprovedFailureCategory(string scenario, string category)
    {
        using var client = await CreateClientAsync(scenario);
        using var protector = AuthenticatedCursorProtector.CreateProcessScoped();
        var executor = NetworkReadTestFactory.Create(client, protector);
        var first = await ReadPage(client, executor, 1, scenario);
        var operation = await ReadOperation(client, executor, 1, cursor: Cursor(first));

        Assert.Equal(category, FailureCategory(operation));
    }

    [Fact]
    public async Task NetworkRead_HostBindingChangeAfterTheFirstPageFailsBeforeTheWorker()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
        using var protector = AuthenticatedCursorProtector.CreateProcessScoped();
        var executor = NetworkReadTestFactory.Create(client, protector);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, Scenario);
        var first = await ReadPage(client, executor, 1, Scenario);
        // Same worker identity, new binding epoch: the read precheck passes, the cursor does not.
        var identity = binding.CaptureSnapshot().ToWorkerIdentity()!;
        Assert.True(binding.Clear(null, out var error), error);
        Assert.True(binding.BindVerified(identity, forceRebind: false, out error), error);

        var operation = await ReadOperation(client, executor, 1, cursor: Cursor(first));

        Assert.Equal(WorkerFailureCategories.CursorBindingMismatch, FailureCategory(operation));
    }

    [Fact]
    public async Task NetworkRead_CursorSignedByAnotherProcessScopedCodecIsInvalid()
    {
        string cursor;
        using (var firstClient = await CreateClientAsync(Scenario))
        using (var protector = AuthenticatedCursorProtector.CreateProcessScoped())
        {
            var executor = NetworkReadTestFactory.Create(firstClient, protector);
            cursor = Cursor(await ReadPage(firstClient, executor, 1, Scenario));
        }

        using var otherProcess = new AuthenticatedCursorProtector(new byte[32], "other-process");
        var exception = Assert.Throws<CursorException>(
            () => new HardwarePageCursorCodec(otherProcess).Decode(cursor));

        Assert.Equal(WorkerFailureCategories.InvalidCursor, exception.Category);
    }

    [Fact]
    public async Task NetworkRead_UnpagedFixtureIsCanonicalAndPagedContinuationStaysOnTheBinding()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
        using var protector = AuthenticatedCursorProtector.CreateProcessScoped();
        var executor = NetworkReadTestFactory.Create(client, protector);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, Scenario);
        var bound = binding.CaptureSnapshot();
        var unpaged = await ReadPage(client, executor, 50, Scenario, paged: false);
        var first = await ReadPage(client, executor, 1, Scenario);
        var second = await ReadPage(client, executor, 4, cursor: Cursor(first));

        Assert.True(bound.SameBinding(client.BindingSnapshot));
        Assert.Equal(ProjectBindingSnapshot.VerifiedState, client.BindingSnapshot.State);
        Assert.Equal(
            CanonicalJson.Serialize(CanonicalJson.Deserialize<HardwareConfigInfo>(unpaged.GetRawText())),
            CanonicalJson.Serialize(new HardwareConfigInfo
            {
                Devices = first.GetProperty("devices").EnumerateArray().Concat(second.GetProperty("devices").EnumerateArray())
                    .Select(element => CanonicalJson.Deserialize<DeviceInfo>(element.GetRawText())).ToList(),
                Subnets = first.GetProperty("subnets").EnumerateArray().Concat(second.GetProperty("subnets").EnumerateArray())
                    .Select(element => CanonicalJson.Deserialize<SubnetInfo>(element.GetRawText())).ToList(),
                Messages = new List<string> { "Fixture page diagnostic." },
            }));
    }

    [Fact]
    public async Task NetworkRead_CanonicalItemTrimmingResumesWithTheUnemittedCandidatesAndDiagnostics()
    {
        using var client = await CreateClientAsync(TrimmingScenarioProjectPath);
        using var protector = AuthenticatedCursorProtector.CreateProcessScoped();
        var executor = NetworkReadTestFactory.Create(client, protector);
        var first = await ReadPage(client, executor, 6, TrimmingScenarioProjectPath);
        var second = await ReadPage(client, executor, 6, cursor: Cursor(first));

        var firstReturned = first.GetProperty("pagination").GetProperty("returnedDevices").GetInt32()
            + first.GetProperty("pagination").GetProperty("returnedSubnets").GetInt32();
        Assert.InRange(firstReturned, 1, 5);
        Assert.Equal(4, first.GetProperty("pagination").GetProperty("totalDevices").GetInt32());
        Assert.Contains(
            second.GetProperty("messages").EnumerateArray().Select(message => message.GetString()),
            message => string.Equals(message, "Nested locator fixture: Plant A/Cell 1/PLC_DUP.", StringComparison.Ordinal));
        Assert.Equal(
            6,
            first.GetProperty("devices").GetArrayLength() + first.GetProperty("subnets").GetArrayLength()
                + second.GetProperty("devices").GetArrayLength() + second.GetProperty("subnets").GetArrayLength());
    }

    private static async Task<OpennessWorkerClient> CreateClientAsync(string projectPath)
    {
        var binding = new ProjectSessionBinding(null);
        var client = new OpennessWorkerClient(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, projectPath);
        return client;
    }

    private static async Task<JsonElement> ReadPage(
        OpennessWorkerClient client,
        NetworkReadOperationExecutor executor,
        int pageSize,
        string? projectPath = null,
        string? cursor = null,
        string? deviceName = null,
        bool? includeIoDetails = null,
        bool paged = true)
    {
        var operation = await ReadOperation(client, executor, pageSize, projectPath, cursor, deviceName, includeIoDetails, paged);
        Assert.True(
            string.Equals("succeeded", operation.GetProperty("status").GetString(), StringComparison.Ordinal),
            operation.GetRawText());
        return operation.GetProperty("result").Clone();
    }

    private static async Task<JsonElement> ReadOperation(
        OpennessWorkerClient client,
        NetworkReadOperationExecutor executor,
        int pageSize,
        string? projectPath = null,
        string? cursor = null,
        string? deviceName = null,
        bool? includeIoDetails = null,
        bool paged = true)
    {
        using var uiOpen = projectPath is null
            ? null : FakeWorkerUiOpenProject.ForWorkerRelativePath(projectPath);
        var result = await NetworkReadTools.NetworkRead(
            client,
            executor,
            new[]
            {
                new NetworkOperationRequest
                {
                    OperationId = "hardware",
                    Operation = "read_hardware_config",
                    PageSize = paged ? pageSize : null,
                    Cursor = cursor,
                    DeviceName = deviceName,
                    IncludeIoDetails = includeIoDetails,
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

    private static string Cursor(JsonElement page)
        => page.GetProperty("pagination").GetProperty("nextCursor").GetString()!;

    private static string FailureCategory(JsonElement operation)
        => operation.GetProperty("failure").GetProperty("category").GetString()!;

    private static void AssertPageCounters(JsonElement page, int totalDevices, int totalSubnets)
    {
        var pagination = page.GetProperty("pagination");
        Assert.Equal(totalDevices, pagination.GetProperty("totalDevices").GetInt32());
        Assert.Equal(totalSubnets, pagination.GetProperty("totalSubnets").GetInt32());
        Assert.Equal(page.GetProperty("devices").GetArrayLength(), pagination.GetProperty("returnedDevices").GetInt32());
        Assert.Equal(page.GetProperty("subnets").GetArrayLength(), pagination.GetProperty("returnedSubnets").GetInt32());
    }
}
