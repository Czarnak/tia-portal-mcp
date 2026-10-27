using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Network;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Network;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class NetworkGuardedWriteOrderingTests
{
    [Theory]
    [InlineData("bare")]
    [InlineData("position")]
    [InlineData("type")]
    [InlineData("interface")]
    [InlineData("index")]
    public async Task WrongQualifiedOrBareConstraints_RefuseBeforeMutationDispatch(string constraint)
    {
        using var audit = new TempAuditDirectory();
        var log = Path.Combine(audit.Path, "requests.log"); Directory.CreateDirectory(audit.Path);
        var prior = Environment.GetEnvironmentVariable("TIA_MCP_FAKE_WORKER_REQUEST_LOG");
        Environment.SetEnvironmentVariable("TIA_MCP_FAKE_WORKER_REQUEST_LOG", log);
        try
        {
            using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-qualified-read");
            var operation = new NetworkOperationRequest { OperationId = "write", Operation = "configure_network_device", Target = new()
            { DeviceName = "S7-1500/ET200MP station_1", NodeId = "E1", InterfacePath = new[]
                { new NetworkInterfacePathSegment { Name = "PLC_DP", PositionNumber = 1 },
                  new NetworkInterfacePathSegment { Name = "PROFINET interface_1", PositionNumber = 32768 } } }, Changes = new() { IpAddress = "10.0.0.1" } };
            if (constraint == "bare") operation.Target.InterfacePath = null;
            if (constraint == "position") operation.Target.InterfacePath![1].PositionNumber = 33024;
            if (constraint == "type") operation.Target.InterfacePath![1].TypeIdentifier = "Missing type";
            if (constraint == "interface") operation.Target.InterfaceName = "PROFINET interface_2";
            if (constraint == "index") operation.Target.NodeIndex = 1;
            // Repeat the same refusal; neither call may dispatch a mutation or consume an owner guess.
            Assert.False((await fixture.RunAsync(false, operation)).Success);
            Assert.False((await fixture.RunAsync(false, operation)).Success);
            Assert.DoesNotContain("configure_network_device", File.ReadAllLines(log));
        }
        finally { Environment.SetEnvironmentVariable("TIA_MCP_FAKE_WORKER_REQUEST_LOG", prior); }
    }

    [Fact]
    public async Task QualifiedLateOwnerChange_RefusesBeforeMutationDispatch()
    {
        using var audit = new TempAuditDirectory();
        var log = Path.Combine(audit.Path, "requests.log"); Directory.CreateDirectory(audit.Path);
        var prior = Environment.GetEnvironmentVariable("TIA_MCP_FAKE_WORKER_REQUEST_LOG");
        Environment.SetEnvironmentVariable("TIA_MCP_FAKE_WORKER_REQUEST_LOG", log);
        try
        {
            using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-qualified-owner-drift");
            // The caller's earlier read observes the original owner; every later read sees it changed.
            Assert.True((await fixture.Client.ReadHardwareConfigAsync()).Success);
            var operation = new NetworkOperationRequest { OperationId = "write", Operation = "configure_network_device", Target = new()
            { DeviceName = "S7-1500/ET200MP station_1", NodeId = "E1", InterfacePath = new[]
                { new NetworkInterfacePathSegment { Name = "PLC_DP", PositionNumber = 1 },
                  new NetworkInterfacePathSegment { Name = "PROFINET interface_1", PositionNumber = 32768 } } }, Changes = new() { IpAddress = "10.0.0.1" } };
            var response = await fixture.RunAsync(false, operation);
            Assert.False(response.Success);
            Assert.DoesNotContain("configure_network_device", File.ReadAllLines(log));
        }
        finally { Environment.SetEnvironmentVariable("TIA_MCP_FAKE_WORKER_REQUEST_LOG", prior); }
    }

    [Fact]
    public async Task SameNodeDifferentCasing_FirstIsSupersededSecondTrusted()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
        var second = NetworkGuardedWriteFixture.Configure("second", "10.0.0.2");
        second.Target!.DeviceName = "plc_grouped";
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Configure("first", "10.0.0.1"), second);
        Assert.True(response.Success);
        Assert.Equal(new[] { "superseded", "trusted" }, response.Verification!.Operations.Select(o => o.Check));
    }

    [Fact]
    public async Task ConnectThenDelete_ReplansLateInfo()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-disconnected");
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Configure("connect", connect: true), NetworkGuardedWriteFixture.Delete());
        Assert.True(response.Success);
        Assert.Single(response.Guards, g => g.Id == "network_delete_connected_subnet");
        Assert.Single(response.Effects[1].Effect!.AffectedNodes);
    }
    [Fact]
    public async Task EarlierAdd_RefreshesRootCount()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
        var add = new NetworkOperationRequest { OperationId = "add", Operation = "add_network_device", DeviceName = "new", TypeIdentifier = "OrderNumber:TEST" };
        var response = await fixture.RunAsync(false, add, NetworkGuardedWriteFixture.Delete());
        Assert.True(response.Success);
        Assert.Equal(3, response.Effects[1].Effect!.RootDeviceCount);
    }
    [Fact]
    public async Task LateBlock_PreservesEarlierChanges()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-late-block");
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Configure("first", "10.0.0.1"), NetworkGuardedWriteFixture.Delete(), NetworkGuardedWriteFixture.Configure("last", "10.0.0.3"));
        Assert.Equal("applied", response.Phase);
        Assert.False(response.Success);
        Assert.Null(response.Error);
        Assert.Equal("succeeded", response.Batch!.Operations[0].Status);
        Assert.Equal("earlierOperationFailed", response.Batch.Operations[2].SkipReason);
        Assert.Contains(response.Guards, g => g.Id == "network_state_unverifiable");
        Assert.Single(response.Verification!.Operations);
        Assert.Equal("first", response.Verification.Operations[0].OperationId);
    }

    [Theory]
    [InlineData(McpAccessMode.ReadWrite, false)]
    [InlineData(McpAccessMode.Full, false)]
    [InlineData(McpAccessMode.ReadWrite, true)]
    [InlineData(McpAccessMode.Full, true)]
    public async Task ProducerScopedLateBlock_RetainsEarlierMutationAndOneAudit(McpAccessMode mode, bool node)
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit,
            node ? "network-guarded-late-node-block" : "network-guarded-late-block", mode);
        var blocked = node ? NetworkGuardedWriteFixture.Configure("connect", connect: true) : NetworkGuardedWriteFixture.Delete();
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Configure("first", "10.0.0.1"), blocked,
            NetworkGuardedWriteFixture.Configure("last", "10.0.0.3"));
        Assert.Equal("applied", response.Phase);
        Assert.False(response.Success);
        Assert.Null(response.Error);
        Assert.Equal("succeeded", response.Batch!.Operations[0].Status);
        Assert.Equal("failed", response.Batch.Operations[1].Status);
        Assert.Equal("earlierOperationFailed", response.Batch.Operations[2].SkipReason);
        Assert.Contains(response.Guards, g => g.Id == "network_state_unverifiable" && g.Severity == "block");
        Assert.Equal("first", Assert.Single(response.Verification!.Operations).OperationId);
        Assert.Single(requests.Methods(), method => method == "configure_network_device");
        Assert.DoesNotContain("delete_subnet", requests.Methods());
        using var record = System.Text.Json.JsonDocument.Parse(Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path)));
        Assert.Equal(mode == McpAccessMode.Full ? "policy" : "none", record.RootElement.GetProperty("confirmation").GetProperty("by").GetString());
        Assert.All(record.RootElement.GetProperty("guards").EnumerateArray(), guard =>
            Assert.Equal(System.Text.Json.JsonValueKind.Null, guard.GetProperty("satisfiedBy").ValueKind));
    }

    [Fact]
    public async Task RejectedConfiguration_StopsCallWithTypedFailureAndAppliedSubset()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-rejected-setting");
        var partial = NetworkGuardedWriteFixture.Configure("partial", "10.0.0.5");
        partial.Changes = new() { IpAddress = "10.0.0.5", PnDeviceName = "rejected-name" };
        var response = await fixture.RunAsync(false, partial, NetworkGuardedWriteFixture.Delete());
        Assert.False(response.Success);
        Assert.Null(response.Error);
        Assert.Equal("applied", response.Phase);
        var failed = response.Batch!.Operations[0];
        Assert.Equal("failed", failed.Status);
        Assert.Equal("engineering_rejected", failed.Failure!.Category);
        Assert.Equal("EngineeringTargetInvocationException", failed.Failure.EngineeringError!.Type);
        Assert.Equal("10.0.0.5", failed.Result!.Value.GetProperty("appliedSettings").GetProperty("Address").GetString());
        Assert.Equal("earlierOperationFailed", response.Batch.Operations[1].SkipReason);
        Assert.True(response.Verification!.Success);
        Assert.Empty(response.Verification.Operations);
        var state = await NetworkWritePlanner.ReadCurrentStateAsync(fixture.Client);
        Assert.Equal("10.0.0.5", state.State!.Devices[0].Items[0].NetworkInterfaces[0].Nodes[0].IpAddress);
        Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
    }

    [Fact]
    public async Task ReplanningOccursBetweenDispatches()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
        await fixture.RunAsync(false, NetworkGuardedWriteFixture.Configure("first", "10.0.0.1"), NetworkGuardedWriteFixture.Configure("second", "10.0.0.2"));
        var relevant = requests.Methods().Where(m => m is "read_hardware_config" or "configure_network_device").ToArray();
        Assert.Equal(new[] { "read_hardware_config", "configure_network_device", "read_hardware_config", "configure_network_device" }, relevant);
    }
    [Fact]
    public async Task ReturnedPlanCannotMutateRetainedInventory()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
        var domain = new NetworkWriteDomain(fixture.Client);
        var operation = NetworkGuardedWriteFixture.Delete();
        var plan = await domain.PlanAsync(new[] { operation });
        plan.Items[0].Effect!.AffectedNodes[0].NodeId = "caller-corruption";
        var projected = domain.Project(operation, await domain.MutateAsync(operation));
        var verification = await domain.VerifyAsync(StructuredOperationBatch.FromItems(new[] { projected }));
        Assert.True(verification!.Success);
        Assert.Equal("trusted", Assert.Single(verification.Operations).Check);
    }

    [Fact]
    public async Task UnknownAttempt_FailsItemSkipsRestAndHasNoVerificationEntry()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-unknown-result");
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Configure("unknown", "10.0.0.8"), NetworkGuardedWriteFixture.Delete());
        Assert.False(response.Success);
        Assert.True(response.Verification!.Success);
        Assert.Empty(response.Verification.Operations);
        Assert.Equal("earlierOperationFailed", response.Batch!.Operations[1].SkipReason);
        Assert.Single(requests.Methods(), m => m == "configure_network_device");
        Assert.DoesNotContain("delete_subnet", requests.Methods());
    }
    [Fact]
    public async Task SubnetUpdateThenDelete_UpdateIsSuperseded()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
        var rename = new NetworkOperationRequest { OperationId = "rename", Operation = "update_subnet", Target = new() { Kind = "subnet", SubnetId = "subnet-1" }, SubnetChanges = new() { Name = "Renamed" } };
        var response = await fixture.RunAsync(false, rename, NetworkGuardedWriteFixture.Delete());
        Assert.True(response.Success);
        Assert.Equal(new[] { "superseded", "trusted" }, response.Verification!.Operations.Select(o => o.Check));
    }

    [Fact]
    public async Task ConfigurePreview_ReportsInspectedNodeAccess()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
        var response = await fixture.RunAsync(true, NetworkGuardedWriteFixture.Configure("address", "10.0.0.9"));
        var address = response.Effects[0].Effect!.CurrentSettings["Address"];
        Assert.Equal(("dynamic", "readWrite", "available"), (address.Source, address.Access, address.Availability));
        Assert.Equal(new[] { "System.String" }, address.SupportedTypes);
    }
    [Fact]
    public async Task ConfigurePreview_ReportsUnknownAccessWhenNodeInspectIsUnavailable()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-node-inspect-unavailable");
        var response = await fixture.RunAsync(true, NetworkGuardedWriteFixture.Configure("address", "10.0.0.9"));
        var address = response.Effects[0].Effect!.CurrentSettings["Address"];
        Assert.Equal(("modeled", "unknown"), (address.Source, address.Access));
    }
    [Fact]
    public async Task SubnetRenamePreview_ReportsInspectedAccess()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
        var rename = new NetworkOperationRequest { OperationId = "rename", Operation = "update_subnet", Target = new() { Kind = "subnet", SubnetId = "subnet-1" }, SubnetChanges = new() { Name = "Renamed" } };
        var name = (await fixture.RunAsync(true, rename)).Effects[0].Effect!.CurrentSettings["Name"];
        Assert.Equal(("dynamic", "readWrite"), (name.Source, name.Access));
    }
    [Theory]
    [InlineData("network-guarded-pn-autogeneration", true)]
    [InlineData("network-guarded-pn-autogeneration", false)]
    [InlineData("network-guarded-pn-readonly", false)]
    public async Task PnDeviceNameWithoutWritableName_FailsAtPlanTimeWithoutMutation(string scenario, bool dryRun)
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, scenario);
        var operation = NetworkGuardedWriteFixture.Configure("rename");
        operation.Changes = new() { IpAddress = "10.0.0.9", PnDeviceName = "plc-renamed" };
        var response = await fixture.RunAsync(dryRun, operation);
        Assert.False(response.Success);
        Assert.Equal("validation_error", response.Error!.Category);
        Assert.Contains("pnDeviceNameAutoGeneration:false", response.Error.Message);
        Assert.DoesNotContain("configure_network_device", requests.Methods());
    }
    [Fact]
    public async Task PnDeviceNameAutoGenerationOptIn_IsAVisibleRequestedSetting()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-pn-autogeneration");
        var operation = NetworkGuardedWriteFixture.Configure("rename");
        operation.Changes = new() { PnDeviceName = "plc-renamed", PnDeviceNameAutoGeneration = false };
        var response = await fixture.RunAsync(false, operation);
        Assert.True(response.Success);
        var effect = response.Effects[0].Effect!;
        Assert.Equal("false", effect.RequestedSettings["PnDeviceNameAutoGeneration"]);
        Assert.Equal("boolean", effect.CurrentSettings["PnDeviceNameAutoGeneration"].Value!.Kind);
        var applied = response.Batch!.Operations[0].Result!.Value.GetProperty("appliedSettings");
        Assert.Equal("false", applied.GetProperty("PnDeviceNameAutoGeneration").GetString());
    }

    [Fact]
    public async Task IoSystemAttach_RequestedSettingsAreScalarSubnetAndNumber()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-io-move");
        var attach = NetworkGuardedWriteFixture.Configure("attach");
        attach.Changes = new() { IoSystem = new() { SubnetId = "subnet-1", Number = 1 } };
        var response = await fixture.RunAsync(false, attach);
        Assert.True(response.Success);
        var effect = response.Effects[0].Effect!;
        Assert.Equal(new[] { "IoSystemNumber", "IoSystemSubnet" }, effect.RequestedSettings.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("subnet-1", effect.RequestedSettings["IoSystemSubnet"]);
        Assert.Equal("1", effect.RequestedSettings["IoSystemNumber"]);
        Assert.Equal(new[] { "IoSystemNumber", "IoSystemSubnet" }, effect.CurrentSettings.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ReplannedRootCount_DoesNotEraseEarlierPreservationExpectation()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-root-drift");
        var rename = new NetworkOperationRequest { OperationId = "rename", Operation = "update_subnet", Target = new() { Kind = "subnet", SubnetId = "subnet-1" }, SubnetChanges = new() { Name = "Renamed" } };
        var response = await fixture.RunAsync(false, rename, NetworkGuardedWriteFixture.Delete());
        Assert.True(response.Success);
        Assert.Equal(1, response.Effects[1].Effect!.RootDeviceCount);
    }

    [Fact]
    public async Task MissingIoConnector_FailsTargetNotFoundWithoutMutation()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-no-io-connector");
        var operation = NetworkGuardedWriteFixture.Configure("io");
        operation.Changes = new() { IpAddress = "10.0.0.99", IoSystem = new() { SubnetId = "subnet-1", Number = 1 } };
        var before = await NetworkWritePlanner.ReadCurrentStateAsync(fixture.Client);
        var response = await fixture.RunAsync(false, operation, NetworkGuardedWriteFixture.Delete());
        Assert.False(response.Success);
        Assert.Equal("target_not_found", response.Batch!.Operations[0].Failure!.Category);
        Assert.Null(response.Batch.Operations[0].Result);
        Assert.Equal("skipped", response.Batch.Operations[1].Status);
        var after = await NetworkWritePlanner.ReadCurrentStateAsync(fixture.Client);
        Assert.Equal(TiaMcpServer.Json.CanonicalJson.Serialize(before.State), TiaMcpServer.Json.CanonicalJson.Serialize(after.State));
        Assert.Single(requests.Methods(), m => m == "configure_network_device");
        Assert.DoesNotContain("delete_subnet", requests.Methods());
    }

    [Fact]
    public async Task SameRawNodeIdOnDistinctInterfaces_BothAreTrusted()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-qualified-read");
        NetworkOperationRequest Configure(string id, string owner, int position) => new()
        {
            OperationId = id, Operation = "configure_network_device", Changes = new() { IpAddress = "10.0.0.1" },
            Target = new() { DeviceName = "S7-1500/ET200MP station_1", NodeId = "E1", InterfacePath = new[]
            { new NetworkInterfacePathSegment { Name = "PLC_DP", PositionNumber = 1 },
              new NetworkInterfacePathSegment { Name = owner, PositionNumber = position } } }
        };
        var response = await fixture.RunAsync(false, Configure("first", "PROFINET interface_1", 32768), Configure("second", "PROFINET interface_2", 33024));
        Assert.True(response.Success);
        Assert.Equal(new[] { "trusted", "trusted" }, response.Verification!.Operations.Select(o => o.Check));
    }
    [Fact]
    public async Task ContradictoryRejectedOutcome_IsProtocolErrorWithoutApplyingUnrequestedMask()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-rejected-contradiction");
        var before = await NetworkWritePlanner.ReadCurrentStateAsync(fixture.Client);
        var operation = NetworkGuardedWriteFixture.Configure("reject");
        operation.Changes = new() { IpAddress = "10.0.0.5", PnDeviceName = "rejected" };
        var response = await fixture.RunAsync(false, operation, NetworkGuardedWriteFixture.Delete());
        Assert.False(response.Success);
        var failed = response.Batch!.Operations[0];
        Assert.Equal("protocol_error", failed.Failure!.Category);
        Assert.Null(failed.Result);
        Assert.DoesNotContain("255.0.0.0", failed.Failure.Message);
        Assert.Equal("skipped", response.Batch.Operations[1].Status);
        var after = await NetworkWritePlanner.ReadCurrentStateAsync(fixture.Client);
        Assert.Equal(before.State!.Devices[0].Items[0].NetworkInterfaces[0].Nodes[0].SubnetMask,
            after.State!.Devices[0].Items[0].NetworkInterfaces[0].Nodes[0].SubnetMask);
        Assert.Equal("10.0.0.5", after.State.Devices[0].Items[0].NetworkInterfaces[0].Nodes[0].IpAddress);
    }
}
