using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.Safety;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// End-to-end evidence that the Phase 4 subnet lifecycle operations (<c>create_subnet</c>,
/// <c>update_subnet</c>, <c>delete_subnet</c>) run through the exact same public
/// guarded dry-run/actual/audit protocol as every other dedicated network write — proven against a
/// stateful FakeWorker scenario rather than real TIA Portal.
///
/// <para>
/// The <c>network-subnet-lifecycle</c> scenario begins with one Ethernet and one PROFIBUS subnet,
/// both already connected to a node, and a stable two-device root count. Every test in this file
/// creates, renames, or deletes subnets against that same in-process mutable state and re-reads it
/// to prove the mutation (or lack of one) actually happened — never against a static fixture.
/// </para>
/// </summary>
[Collection(RealWorkerProcessCollection.Name)]
public class NetworkSubnetLifecycleFakeWorkerTests
{
    private const string Scenario = "network-subnet-lifecycle";
    private const string AltPathScenario = "network-subnet-lifecycle-alt-path";
    private const string MalformedSuccessScenario = "network-subnet-lifecycle-malformed-success";
    private const string PostconditionFailedScenario = "network-subnet-lifecycle-postcondition-failed";
    private const string SecondItemFailureScenario = "network-subnet-lifecycle-second-item-failure";
    private const string StateDriftScenario = "network-subnet-lifecycle-state-drift";

    private static OpennessWorkerClient CreateClient(ProjectSessionBinding? binding = null)
        => new(
            binding ?? new ProjectSessionBinding(null),
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());

    private static OpennessWorkerClient CreateWriteClient(
        TempAuditDirectory audit,
        out WriteExecution safety,
        string projectPath = Scenario)
    {
        var binding = new ProjectSessionBinding(null);
        var client = CreateClient(binding);
        NetworkVerifiedWriteFixture.VerifyAsync(client, binding, projectPath).GetAwaiter().GetResult();
        safety = NetworkGuardedWriteFixture.CreateRunner(client, audit.Path);
        return client;
    }

    private static NetworkOperationRequest CreateSubnetOp(
        string operationId,
        string name,
        string networkType,
        int? highestAddress = null,
        string? transmissionSpeed = null,
        string projectPath = Scenario) => new()
    {
        OperationId = operationId,
        Operation = "create_subnet",
        ProjectPath = projectPath,
        Subnet = new NetworkSubnetDefinition
        {
            Name = name,
            NetworkType = networkType,
            HighestAddress = highestAddress,
            TransmissionSpeed = transmissionSpeed,
        },
    };

    private static NetworkOperationRequest UpdateSubnetOp(
        string operationId,
        string subnetId,
        NetworkSubnetChanges changes,
        string projectPath = Scenario) => new()
    {
        OperationId = operationId,
        Operation = "update_subnet",
        ProjectPath = projectPath,
        Target = new NetworkObjectTarget { Kind = NetworkObjectKinds.Subnet, SubnetId = subnetId },
        SubnetChanges = changes,
    };

    private static NetworkOperationRequest DeleteSubnetOp(
        string operationId,
        string subnetId,
        string projectPath = Scenario) => new()
    {
        OperationId = operationId,
        Operation = "delete_subnet",
        ProjectPath = projectPath,
        Target = new NetworkObjectTarget { Kind = NetworkObjectKinds.Subnet, SubnetId = subnetId },
    };

    private static NetworkOperationRequest ReadHardware(string operationId, string projectPath = Scenario) => new()
    {
        OperationId = operationId,
        Operation = "read_hardware_config",
        ProjectPath = projectPath,
    };

    private static async Task<CallToolResult> NetworkWrite(
        OpennessWorkerClient client,
        WriteExecution safety,
        NetworkOperationRequest[] operations,
        bool dryRun = true)
        => await NetworkWriteTools.NetworkWrite(client, safety, operations, dryRun, CancellationToken.None);

    private static async Task<CallToolResult> NetworkRead(
        OpennessWorkerClient client,
        NetworkOperationRequest[] operations)
        => await NetworkReadTools.NetworkRead(client, operations);

    private static JsonElement Structured(CallToolResult result)
        => Assert.IsType<JsonElement>(result.StructuredContent);

    private static string Text(CallToolResult result)
        => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;


    /// <summary>Asserts the text block and structuredContent are byte-for-byte the same document.</summary>
    private static JsonElement AssertOneCanonicalDocument(CallToolResult result)
    {
        var structured = Structured(result);
        var text = Text(result);
        Assert.Equal(CanonicalJson.Serialize(structured), text);
        using var textDocument = JsonDocument.Parse(text);
        Assert.True(JsonElement.DeepEquals(structured, textDocument.RootElement));
        return structured;
    }

    private static JsonElement FirstOperationResult(CallToolResult applied)
        => Structured(applied).GetProperty("batch").GetProperty("operations")[0].GetProperty("result");

    /// <summary>Asserts the new subnet mutation result includes typed immediate verification.</summary>
    private static void AssertVerifiedSubnetResult(JsonElement result, int expectedDeviceCount = 2)
    {
        var members = result.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Assert.Equal(
            new[] { "name", "networkDeviceCount", "networkDeviceCountUnchanged", "subnetId", "verification" },
            members);
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("subnetId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("name").GetString()));
        Assert.Equal(expectedDeviceCount, result.GetProperty("networkDeviceCount").GetInt32());
        Assert.True(result.GetProperty("networkDeviceCountUnchanged").GetBoolean());
        var verification = result.GetProperty("verification");
        Assert.Equal("passed", verification.GetProperty("status").GetString());
        Assert.Equal(result.GetProperty("subnetId").GetString(), verification.GetProperty("identity").GetProperty("subnetId").GetString());
        Assert.All(verification.GetProperty("checks").EnumerateArray(), check => Assert.Equal("passed", check.GetProperty("status").GetString()));
    }

    private static async Task PreviewAndApply(
        OpennessWorkerClient client,
        WriteExecution safety,
        NetworkOperationRequest[] operations,
        Action<CallToolResult> assertApplied)
    {
        var preview = await NetworkWrite(client, safety, operations);
        Assert.False(preview.IsError, Text(preview));

        var applied = await NetworkWrite(client, safety, operations, dryRun: false);
        assertApplied(applied);
    }

    // --- Create ------------------------------------------------------------------------------

    [Fact]
    public async Task CreateSubnet_Ethernet_AppliesAndPostReadConfirmsIdentityWithUnchangedDeviceCount()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateWriteClient(audit, out var safety);
        var operations = new[] { CreateSubnetOp("create", "NewEthernetSubnet", SubnetLifecycleContract.Ethernet) };

        string? createdSubnetId = null;
        await PreviewAndApply(client, safety, operations, applied =>
        {
            Assert.False(applied.IsError, Text(applied));
            var root = Structured(applied);
            Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
            var result = FirstOperationResult(applied);
            AssertVerifiedSubnetResult(result);
            Assert.Equal("NewEthernetSubnet", result.GetProperty("name").GetString());
            createdSubnetId = result.GetProperty("subnetId").GetString();
        });

        Assert.False(string.IsNullOrWhiteSpace(createdSubnetId));

        // Post-read identity: the SAME subnetId now shows up in a fresh hardware read, with the
        // requested name, and the root device count is still 2.
        var read = await NetworkRead(client, new[] { ReadHardware("post-read") });
        var hardware = Structured(read).GetProperty("batch").GetProperty("operations")[0].GetProperty("result");
        Assert.Equal(2, hardware.GetProperty("devices").GetArrayLength());
        var createdSubnet = hardware.GetProperty("subnets").EnumerateArray()
            .First(subnet => subnet.GetProperty("subnetId").GetString() == createdSubnetId);
        Assert.Equal("NewEthernetSubnet", createdSubnet.GetProperty("name").GetString());
        Assert.Equal("Ethernet", createdSubnet.GetProperty("networkType").GetString());
    }

    [Fact]
    public async Task CreateSubnet_Profibus_WithHighestAddressAndTransmissionSpeed_Applies()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateWriteClient(audit, out var safety);
        var operations = new[]
        {
            CreateSubnetOp(
                "create",
                "NewProfibusSubnet",
                SubnetLifecycleContract.Profibus,
                highestAddress: 20,
                transmissionSpeed: "Baud500000"),
        };

        await PreviewAndApply(client, safety, operations, applied =>
        {
            Assert.False(applied.IsError, Text(applied));
            Assert.True(Structured(applied).GetProperty("success").GetBoolean(), Text(applied));
            var result = FirstOperationResult(applied);
            AssertVerifiedSubnetResult(result);
            Assert.Equal("NewProfibusSubnet", result.GetProperty("name").GetString());
        });
    }

    // --- Rename / update -----------------------------------------------------------------------

    [Fact]
    public async Task UpdateSubnet_Ethernet_RenamesTheExactTargetAndLeavesTheOtherSubnetUntouched()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateWriteClient(audit, out var safety);
        var operations = new[]
        {
            UpdateSubnetOp("rename", "subnet-eth-1", new NetworkSubnetChanges { Name = "RenamedEthernet" }),
        };

        await PreviewAndApply(client, safety, operations, applied =>
        {
            Assert.False(applied.IsError, Text(applied));
            var result = FirstOperationResult(applied);
            AssertVerifiedSubnetResult(result);
            Assert.Equal("subnet-eth-1", result.GetProperty("subnetId").GetString());
            Assert.Equal("RenamedEthernet", result.GetProperty("name").GetString());
        });

        var read = await NetworkRead(client, new[] { ReadHardware("post-read") });
        var subnets = Structured(read).GetProperty("batch").GetProperty("operations")[0]
            .GetProperty("result").GetProperty("subnets");
        var renamed = subnets.EnumerateArray().First(s => s.GetProperty("subnetId").GetString() == "subnet-eth-1");
        Assert.Equal("RenamedEthernet", renamed.GetProperty("name").GetString());

        // update_subnet applied ONLY to the exact targeted id: the PROFIBUS subnet is untouched.
        var untouched = subnets.EnumerateArray().First(s => s.GetProperty("subnetId").GetString() == "subnet-pb-1");
        Assert.Equal("MPI/DP_1", untouched.GetProperty("name").GetString());
    }

    [Fact]
    public async Task UpdateSubnet_Profibus_RenamesHighestAddressAndBaudRateAndLeavesEthernetSubnetUntouched()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateWriteClient(audit, out var safety);
        var operations = new[]
        {
            UpdateSubnetOp(
                "rename",
                "subnet-pb-1",
                new NetworkSubnetChanges
                {
                    Name = "RenamedProfibus",
                    HighestAddress = 99,
                    TransmissionSpeed = "Baud1500000",
                }),
        };

        await PreviewAndApply(client, safety, operations, applied =>
        {
            Assert.False(applied.IsError, Text(applied));
            var result = FirstOperationResult(applied);
            AssertVerifiedSubnetResult(result);
            Assert.Equal("subnet-pb-1", result.GetProperty("subnetId").GetString());
            Assert.Equal("RenamedProfibus", result.GetProperty("name").GetString());
        });

        var read = await NetworkRead(client, new[] { ReadHardware("post-read") });
        var subnets = Structured(read).GetProperty("batch").GetProperty("operations")[0]
            .GetProperty("result").GetProperty("subnets");
        var untouched = subnets.EnumerateArray().First(s => s.GetProperty("subnetId").GetString() == "subnet-eth-1");
        Assert.Equal("PN/IE_1", untouched.GetProperty("name").GetString());
    }

    // --- Delete --------------------------------------------------------------------------------

    [Fact]
    public async Task DeleteSubnet_EmptyNewlyCreatedSubnet_RemovesItAndLeavesDevicesUnchanged()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateWriteClient(audit, out var safety);

        string createdSubnetId = null!;
        await PreviewAndApply(
            client,
            safety,
            new[] { CreateSubnetOp("create", "EmptySubnet", SubnetLifecycleContract.Ethernet) },
            applied => createdSubnetId = FirstOperationResult(applied).GetProperty("subnetId").GetString()!);

        await PreviewAndApply(
            client,
            safety,
            new[] { DeleteSubnetOp("delete", createdSubnetId) },
            applied =>
            {
                Assert.False(applied.IsError, Text(applied));
                var result = FirstOperationResult(applied);
                AssertVerifiedSubnetResult(result);
                Assert.Equal(createdSubnetId, result.GetProperty("subnetId").GetString());
            });

        var read = await NetworkRead(client, new[] { ReadHardware("post-read") });
        var hardware = Structured(read).GetProperty("batch").GetProperty("operations")[0].GetProperty("result");
        Assert.Equal(2, hardware.GetProperty("devices").GetArrayLength());
        Assert.DoesNotContain(
            hardware.GetProperty("subnets").EnumerateArray(),
            subnet => subnet.GetProperty("subnetId").GetString() == createdSubnetId);
    }

    [Fact]
    public async Task DeleteSubnet_ConnectedEthernetSubnet_SucceedsWithoutAnyDependencyBlocking()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateWriteClient(audit, out var safety);

        await PreviewAndApply(
            client,
            safety,
            new[] { DeleteSubnetOp("delete", "subnet-eth-1") },
            applied =>
            {
                Assert.False(applied.IsError, Text(applied));
                var result = FirstOperationResult(applied);
                AssertVerifiedSubnetResult(result);
                Assert.Equal("subnet-eth-1", result.GetProperty("subnetId").GetString());
            });

        var read = await NetworkRead(client, new[] { ReadHardware("post-read") });
        var hardware = Structured(read).GetProperty("batch").GetProperty("operations")[0].GetProperty("result");
        Assert.Equal(2, hardware.GetProperty("devices").GetArrayLength());
        Assert.DoesNotContain(
            hardware.GetProperty("subnets").EnumerateArray(),
            subnet => subnet.GetProperty("subnetId").GetString() == "subnet-eth-1");
    }

    [Fact]
    public async Task DeleteSubnet_ConnectedProfibusSubnet_SucceedsWithoutAnyDependencyBlocking()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateWriteClient(audit, out var safety);

        await PreviewAndApply(
            client,
            safety,
            new[] { DeleteSubnetOp("delete", "subnet-pb-1") },
            applied =>
            {
                Assert.False(applied.IsError, Text(applied));
                var result = FirstOperationResult(applied);
                AssertVerifiedSubnetResult(result);
                Assert.Equal("subnet-pb-1", result.GetProperty("subnetId").GetString());
            });

        var read = await NetworkRead(client, new[] { ReadHardware("post-read") });
        var hardware = Structured(read).GetProperty("batch").GetProperty("operations")[0].GetProperty("result");
        Assert.Equal(2, hardware.GetProperty("devices").GetArrayLength());
        Assert.DoesNotContain(
            hardware.GetProperty("subnets").EnumerateArray(),
            subnet => subnet.GetProperty("subnetId").GetString() == "subnet-pb-1");
    }

    // --- Protocol shape: canonical equality, minimal result, audit -----------------------------

    [Fact]
    public async Task NetworkWrite_SubnetLifecyclePreviewAndApply_TextBlockAndStructuredContentAreIdentical()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateWriteClient(audit, out var safety);
        var operations = new[] { CreateSubnetOp("create", "CanonicalSubnet", SubnetLifecycleContract.Ethernet) };

        var preview = await NetworkWrite(client, safety, operations);
        AssertOneCanonicalDocument(preview);

        var applied = await NetworkWrite(client, safety, operations, dryRun: false);
        AssertOneCanonicalDocument(applied);
    }

    [Fact]
    public async Task NetworkWrite_SubnetLifecycleApply_AppendsOneAuditRecordMatchingTheExactResponse()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateWriteClient(audit, out var safety);
        var operations = new[] { DeleteSubnetOp("delete", "subnet-eth-1") };

        var applied = await NetworkWrite(client, safety, operations, dryRun: false);
        var root = Structured(applied);
        Assert.False(applied.IsError, Text(applied));

        var auditFile = Assert.Single(Directory.GetFiles(audit.Path, "*.jsonl"));
        var record = JsonDocument.Parse(Assert.Single(File.ReadLines(auditFile)));
        Assert.Equal(CanonicalJson.Serialize(root), record.RootElement.GetProperty("responseText").GetString());
    }

    // --- Explicit project binding --------------------------------------------------------------
    [Fact]
    public async Task NetworkWrite_SubnetLifecycleApply_DifferentProjectPathIsRejectedAsBindingConflict()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateWriteClient(audit, out var safety);
        // The explicit project constraint differs from the already verified binding.
        var operations = new[] { DeleteSubnetOp("delete", "subnet-eth-1", AltPathScenario) };
        var result = await NetworkWrite(client, safety, operations, dryRun: false);

        Assert.True(result.IsError);
        Assert.Equal(
            WorkerFailureCategories.BindingConflict,
            Structured(result).GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("already bound", Text(result));
        Assert.Contains(AltPathScenario, Text(result));
    }


    // --- Failure propagation ---------------------------------------------------------------------

    [Fact]
    public async Task NetworkWrite_SubnetLifecyclePostconditionFailed_PropagatesWithoutAnySuccessWording()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateWriteClient(audit, out var safety, PostconditionFailedScenario);
        var operations = new[]
        {
            CreateSubnetOp("create", "WillFailVerification", SubnetLifecycleContract.Ethernet, projectPath: PostconditionFailedScenario),
        };

        var applied = await NetworkWrite(client, safety, operations, dryRun: false);

        Assert.False(applied.IsError, Text(applied));
        var root = Structured(applied);
        Assert.False(root.GetProperty("success").GetBoolean());
        var item = root.GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("failed", item.GetProperty("status").GetString());
        Assert.Equal(
            WorkerFailureCategories.PostconditionFailed,
            item.GetProperty("failure").GetProperty("category").GetString());
        Assert.DoesNotContain("succeeded", item.GetProperty("failure").GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("result").ValueKind);
    }

    [Fact]
    public async Task NetworkWrite_SubnetLifecycleMalformedVerboseSuccessPayload_BecomesProtocolErrorWithNoRawEcho()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateWriteClient(audit, out var safety, MalformedSuccessScenario);
        var operations = new[]
        {
            CreateSubnetOp("create", "WillBeMalformed", SubnetLifecycleContract.Ethernet, projectPath: MalformedSuccessScenario),
        };

        var applied = await NetworkWrite(client, safety, operations, dryRun: false);

        Assert.False(applied.IsError, Text(applied));
        var item = Structured(applied).GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("failed", item.GetProperty("status").GetString());
        Assert.Equal(
            WorkerFailureCategories.ProtocolError,
            item.GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("result").ValueKind);

        // The rejected payload — including the extra unmapped "relationship" text — is never
        // echoed back to the caller.
        var failureText = Text(applied);
        Assert.DoesNotContain("relationshipSummary", failureText);
        Assert.DoesNotContain("connected to 2 devices", failureText);
    }

    // --- Batch stop-on-first-failure, no batch-wide rollback -------------------------------------

    [Fact]
    public async Task NetworkWrite_SubnetLifecycleBatch_LaterFailureStopsButEarlierSuccessStaysAppliedAndDeviceCountUnchanged()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateWriteClient(audit, out var safety, SecondItemFailureScenario);
        var operations = new[]
        {
            CreateSubnetOp("first", "SurvivesTheFailure", SubnetLifecycleContract.Ethernet, projectPath: SecondItemFailureScenario),
            CreateSubnetOp("second", "NeverCreated", SubnetLifecycleContract.Ethernet, projectPath: SecondItemFailureScenario),
        };

        var applied = await NetworkWrite(client, safety, operations, dryRun: false);

        Assert.False(applied.IsError, Text(applied));
        var root = Structured(applied);
        Assert.False(root.GetProperty("success").GetBoolean());
        var items = root.GetProperty("batch").GetProperty("operations");
        Assert.Equal(2, items.GetArrayLength());

        Assert.Equal("succeeded", items[0].GetProperty("status").GetString());
        var firstResult = items[0].GetProperty("result");
        AssertVerifiedSubnetResult(firstResult);
        var createdSubnetId = firstResult.GetProperty("subnetId").GetString();

        Assert.Equal("failed", items[1].GetProperty("status").GetString());

        // The partial-write warning is attached to the operation that stopped the batch (the
        // failed one), not the earlier successful item.
        var warning = items[1].GetProperty("warnings")[0].GetString();
        Assert.Contains("may already have changed", warning);
        Assert.Contains("no rollback", warning, StringComparison.OrdinalIgnoreCase);

        // Proves the earlier success was NOT rolled back: a fresh read against the same scenario
        // (same process, shared mutable state) still reports the created subnet.
        var read = await NetworkRead(client, new[] { ReadHardware("post-read", SecondItemFailureScenario) });
        var hardware = Structured(read).GetProperty("batch").GetProperty("operations")[0].GetProperty("result");
        Assert.Contains(
            hardware.GetProperty("subnets").EnumerateArray(),
            subnet => subnet.GetProperty("subnetId").GetString() == createdSubnetId);
        Assert.Equal(2, hardware.GetProperty("devices").GetArrayLength());
    }

    // --- Full lifecycle in one batch: unchanged root device count on every successful item -------

    [Fact]
    public async Task NetworkWrite_FullLifecycleBatch_CreateUpdateDelete_AllSucceedWithUnchangedRootDeviceCountEverywhere()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateWriteClient(audit, out var safety);

        // One batch with all three lifecycle operation kinds together: create_subnet resolves from
        // the request alone (no existing-target dependency), so it can share a batch with an
        // update/delete against the two PRE-EXISTING fixture subnets without any ordering conflict.
        var operations = new[]
        {
            CreateSubnetOp("create", "LifecycleSubnet", SubnetLifecycleContract.Ethernet),
            UpdateSubnetOp("update", "subnet-pb-1", new NetworkSubnetChanges { Name = "LifecycleSubnetRenamed" }),
            DeleteSubnetOp("delete", "subnet-eth-1"),
        };
        var applied = await NetworkWrite(client, safety, operations, dryRun: false);

        Assert.False(applied.IsError, Text(applied));
        var root = Structured(applied);
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        var items = root.GetProperty("batch").GetProperty("operations");

        Assert.All(
            items.EnumerateArray(),
            item =>
            {
                Assert.Equal("succeeded", item.GetProperty("status").GetString());
                AssertVerifiedSubnetResult(item.GetProperty("result"));
            });

        // The canonical result data alone is sufficient to render the user-facing summary: one
        // create, one update, one delete, every one reporting the device count as unchanged — with
        // no relationship or device-detail text added anywhere in the protocol.
        var summary = RenderCreateUpdateDeleteSummary(items);
        Assert.Equal(
            "Created subnets: 1. Number of network devices remains unchanged.\n"
                + "Modified subnets: 1. Number of network devices remains unchanged.\n"
                + "Deleted subnets: 1. Number of network devices remains unchanged.",
            summary);
    }

    /// <summary>
    /// Derives the user-facing summary template purely from canonical result data (operation names,
    /// statuses, and each result's own <c>networkDeviceCountUnchanged</c>) — proving the minimal
    /// four-member result is sufficient to render it without any additional protocol field.
    /// </summary>
    private static string RenderCreateUpdateDeleteSummary(JsonElement previewOperationsAndUpdate)
    {
        int CountSucceeded(string operationName) => previewOperationsAndUpdate.EnumerateArray()
            .Count(item => item.GetProperty("operation").GetString() == operationName
                && item.GetProperty("status").GetString() == "succeeded");

        var succeededItems = previewOperationsAndUpdate.EnumerateArray()
            .Where(item => item.GetProperty("status").GetString() == "succeeded")
            .ToArray();
        Assert.NotEmpty(succeededItems);
        Assert.All(
            succeededItems,
            item => Assert.True(item.GetProperty("result").GetProperty("networkDeviceCountUnchanged").GetBoolean()));

        const string Unchanged = "Number of network devices remains unchanged.";
        return $"Created subnets: {CountSucceeded("create_subnet")}. {Unchanged}\n"
            + $"Modified subnets: {CountSucceeded("update_subnet")}. {Unchanged}\n"
            + $"Deleted subnets: {CountSucceeded("delete_subnet")}. {Unchanged}";
    }
}
