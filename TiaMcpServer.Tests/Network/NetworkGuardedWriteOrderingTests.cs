using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using TiaMcpServer.OperationBatches;
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
    public async Task DifferentDeviceCasing_RepeatedSettingsSupersedeWithoutChangingImmediateIdentity()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
        var second = NetworkGuardedWriteFixture.Configure("second", "10.0.0.2");
        second.Target!.DeviceName = "plc_grouped";
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Configure("first", "10.0.0.1"), second);
        Assert.All(response.Verification!.Operations, operation => Assert.Equal("passed", operation.Status));
        Assert.Equal("PLC_Grouped", response.Verification.Operations[0].Evidence!.Identity.DeviceName);
        Assert.Equal("plc_grouped", response.Verification.Operations[1].Evidence!.Identity.DeviceName);
        Assert.True(response.Success);
        var address = Assert.Single(response.Verification.FinalChecks, check => check.Field == "Address");
        Assert.Equal("10.0.0.2", address.Expected);
        Assert.Equal("passed", address.Status);
        Assert.Single(response.Verification.FinalChecks, check => check.Subject?.NodeId == "node-2" && check.Field == "exists");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DifferentDeviceCasing_DeleteSupersedesConnectedRelationships(bool includeIo)
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-io-move");
        var connect = NetworkGuardedWriteFixture.Configure("connect", connect: true);
        connect.Target!.DeviceName = "plc_grouped";
        connect.Changes = new()
        {
            Subnet = new() { SubnetId = "subnet-1" },
            IoSystem = includeIo ? new() { SubnetId = "subnet-1", Number = 1 } : null
        };
        var response = await fixture.RunAsync(false, connect, NetworkGuardedWriteFixture.Delete());
        Assert.All(response.Verification!.Operations, operation => Assert.Equal("passed", operation.Status));
        Assert.Equal("plc_grouped", response.Verification.Operations[0].Evidence!.Identity.DeviceName);
        Assert.Contains(response.Effects[1].Effect!.AffectedNodes, node => node.DeviceName == "PLC_Grouped");
        Assert.True(response.Success);
        Assert.DoesNotContain(response.Verification.FinalChecks, check => check.Field is "Subnet" or "IoSystemSubnet" or "IoSystemNumber");
        Assert.Single(response.Verification.FinalChecks, check => check.Subject?.NodeId == "node-2" && check.Field == "exists");
        Assert.Contains(response.Verification.FinalChecks, check => check.Subject?.NodeId == "node-3" && check.Field == "exists" && check.Status == "passed");
    }

    [Fact]
    public async Task RepeatedSettings_PreserveImmediateChecks()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Configure("first", "10.0.0.1"), NetworkGuardedWriteFixture.Configure("second", "10.0.0.2"));
        Assert.True(response.Success);
        Assert.Equal("passed", response.Verification!.Operations[0].Evidence!.Checks[0].Status);
        Assert.Equal("10.0.0.1", response.Verification.Operations[0].Evidence!.Checks[0].Expected);
        Assert.True(response.Verification.Success);
        Assert.Contains(response.Verification.FinalChecks, c => c.Expected == "10.0.0.2" && c.Status == "passed");
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
        Assert.Contains(response.Verification!.FinalChecks, c => c.Subject?.NodeId == "node-2" && c.Status == "passed");
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
        Assert.Contains(response.Verification.FinalChecks, c => c.Expected == "10.0.0.1" && c.Status == "passed");
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
        Assert.Contains(response.Verification.FinalChecks, c => c.Expected == "10.0.0.1" && c.Status == "passed");
        Assert.Single(requests.Methods(), method => method == "configure_network_device");
        Assert.DoesNotContain("delete_subnet", requests.Methods());
        using var record = System.Text.Json.JsonDocument.Parse(Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path)));
        Assert.Equal(mode == McpAccessMode.Full ? "policy" : "none", record.RootElement.GetProperty("confirmation").GetProperty("by").GetString());
        Assert.All(record.RootElement.GetProperty("guards").EnumerateArray(), guard =>
            Assert.Equal(System.Text.Json.JsonValueKind.Null, guard.GetProperty("satisfiedBy").ValueKind));
    }

    [Fact]
    public async Task PartialConfiguration_VerifiesAppliedSubsetPreservesSkippedAndStops()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-partial");
        var partial = NetworkGuardedWriteFixture.Configure("partial", "10.0.0.5");
        partial.Changes = new() { IpAddress = "10.0.0.5", IoSystem = new() { SubnetId = "subnet-1", Number = 1 } };
        var response = await fixture.RunAsync(false, partial, NetworkGuardedWriteFixture.Delete());
        Assert.False(response.Success);
        Assert.Equal("worker_operation_failed", response.Batch!.Operations[0].Failure!.Category);
        Assert.NotNull(response.Batch.Operations[0].Result);
        Assert.Equal("earlierOperationFailed", response.Batch.Operations[1].SkipReason);
        Assert.True(response.Verification!.Success);
        Assert.Single(response.Verification.Operations);
        // The skipped IoSystem keeps its pre-write relationship, never the requested one.
        var preserved = response.Verification.FinalChecks.Where(c => c.Field.StartsWith("IoSystem", StringComparison.Ordinal)).ToArray();
        Assert.Equal(new[] { "IoSystemNumber", "IoSystemSubnet" }, preserved.Select(c => c.Field).Order(StringComparer.Ordinal));
        Assert.All(preserved, c => { Assert.Equal("passed", c.Status); Assert.NotEqual("subnet-1", c.Expected); Assert.NotEqual("1", c.Expected); });
    }
    [Fact]
    public async Task DeletedSubnet_AggregatePassCannotHideLostUngroupedNode()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-lost-node");
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Delete());
        Assert.False(response.Success);
        Assert.Equal("passed", response.Verification!.Operations[0].Status);
        Assert.Contains(response.Verification.FinalChecks, c => c.Subject?.DeviceName == "PLC_Ungrouped" && c.Status == "failed");
    }
    [Fact]
    public async Task PostReadFailure_IsUnverifiedAndNeverReplayed()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-postread-failure");
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Configure("first", "10.0.0.1"));
        Assert.False(response.Success);
        Assert.Contains(response.Verification!.FinalChecks, c => c.Status == "unverified");
        Assert.Contains(response.Warnings, w => w.Contains("do not replay"));
        Assert.Single(requests.Methods(), m => m == "configure_network_device");
    }
    [Fact]
    public async Task ReplanningOccursBetweenDispatches()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
        await fixture.RunAsync(false, NetworkGuardedWriteFixture.Configure("first", "10.0.0.1"), NetworkGuardedWriteFixture.Configure("second", "10.0.0.2"));
        var relevant = requests.Methods().Where(m => m is "read_hardware_config" or "configure_network_device").ToArray();
        Assert.Equal(new[] { "read_hardware_config", "read_hardware_config", "configure_network_device", "read_hardware_config", "configure_network_device", "read_hardware_config" }, relevant);
    }
    [Fact]
    public async Task ReturnedPlanCannotMutateRetainedInventory()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
        var domain = new NetworkWriteDomain(fixture.Client);
        var operation = NetworkGuardedWriteFixture.Delete();
        var plan = await domain.PlanAsync("network-guarded", new[] { operation });
        plan.Items[0].Effect!.AffectedNodes[0].NodeId = "caller-corruption";
        var projected = domain.Project(operation, await domain.MutateAsync("network-guarded", operation));
        var verification = await domain.VerifyAsync("network-guarded", StructuredOperationBatch.FromItems(new[] { projected }));
        Assert.True(verification!.Success);
        Assert.DoesNotContain(verification.FinalChecks, c => c.Subject?.NodeId == "caller-corruption");
        Assert.Contains(verification.FinalChecks, c => c.Subject?.NodeId == "node-2");
    }
    [Fact]
    public async Task AddedDevice_VerifiesTopLevelItemDespiteSameNamedChild()
    {
        // The fake device has the ET200SP shape: the top-level head module has a child with the same name.
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
        var response = await fixture.RunAsync(false, new NetworkOperationRequest { OperationId = "add", Operation = "add_network_device", DeviceName = "new", DeviceItemName = "CPU", TypeIdentifier = "OrderNumber:TEST" });
        Assert.True(response.Success);
        Assert.Equal("succeeded", response.Batch!.Operations[0].Status);
        Assert.Contains(response.Verification!.FinalChecks, c => c.Kind == "device" && c.Field == "deviceItemName" && c.Status == "passed");
        Assert.Contains(response.Verification.FinalChecks, c => c.Kind == "device" && c.Field == "typeIdentifier" && c.Status == "passed" && c.Observed == "OrderNumber:TEST");
    }
    [Fact]
    public async Task UnknownAttempt_RetainsUnverifiedRecordAndSkipsFollowingItems()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-unknown-result");
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Configure("unknown", "10.0.0.8"), NetworkGuardedWriteFixture.Delete());
        Assert.False(response.Success);
        Assert.Equal("unverified", Assert.Single(response.Verification!.Operations).Status);
        Assert.Null(response.Verification.Operations[0].Evidence);
        Assert.Equal("earlierOperationFailed", response.Batch!.Operations[1].SkipReason);
        Assert.Single(requests.Methods(), m => m == "configure_network_device");
        Assert.DoesNotContain("delete_subnet", requests.Methods());
    }
    [Fact]
    public async Task SubnetUpdateThenDelete_SupersedesExistenceAndName()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
        var rename = new NetworkOperationRequest { OperationId = "rename", Operation = "update_subnet", Target = new() { Kind = "subnet", SubnetId = "subnet-1" }, SubnetChanges = new() { Name = "Renamed" } };
        var response = await fixture.RunAsync(false, rename, NetworkGuardedWriteFixture.Delete());
        Assert.True(response.Success);
        Assert.Equal(2, response.Verification!.Operations.Count);
        Assert.DoesNotContain(response.Verification.FinalChecks, c => c.Field == "Name" || c.Kind == "subnet" && c.Subject!.SubnetId == "subnet-1" && c.Field == "exists");
        Assert.Contains(response.Verification.FinalChecks, c => c.Kind == "subnet" && c.Field == "absent" && c.Status == "passed");
    }
    [Fact]
    public async Task ProfibusAttributes_UseTypedInspectionAndEnumSymbol()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
        var create = new NetworkOperationRequest { OperationId = "create", Operation = "create_subnet", Subnet = new() { Name = "PB", NetworkType = "Profibus", HighestAddress = 31, TransmissionSpeed = "Baud187500" } };
        var created = await fixture.RunAsync(false, create);
        Assert.True(created.Success);
        var id = created.Batch!.Operations[0].Result!.Value.GetProperty("subnetId").GetString();
        var update = new NetworkOperationRequest { OperationId = "update", Operation = "update_subnet", Target = new() { Kind = "subnet", SubnetId = id }, SubnetChanges = new() { HighestAddress = 63, TransmissionSpeed = "Baud1500000" } };
        var updated = await fixture.RunAsync(false, update);
        Assert.True(updated.Success);
        Assert.Equal("available", updated.Effects[0].Effect!.CurrentSettings["HighestAddress"].Availability);
        Assert.Contains(updated.Verification!.FinalChecks, c => c.Expected == "63" && c.Status == "passed");
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
        Assert.Contains(response.Verification!.Operations[0].Evidence!.Checks, c => c.Name == "PnDeviceNameAutoGeneration" && c.Observed == "false");
        Assert.Contains(response.Verification.FinalChecks, c => c.Field == "PnDeviceNameAutoGeneration" && c.Expected == "false" && c.Status == "passed");
        Assert.Contains(response.Verification.FinalChecks, c => c.Field == "PnDeviceName" && c.Expected == "plc-renamed" && c.Status == "passed");
    }
    [Fact]
    public async Task SubnetOnlyMove_DoesNotInferSupersessionOfEarlierExplicitIoTuple()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-io-move");
        var attach = NetworkGuardedWriteFixture.Configure("attach");
        attach.Changes = new() { IoSystem = new() { SubnetId = "subnet-1", Number = 1 } };
        var move = NetworkGuardedWriteFixture.Configure("move");
        move.Changes = new() { Subnet = new() { SubnetId = "subnet-2" } };
        var response = await fixture.RunAsync(false, attach, move);
        Assert.False(response.Success);
        Assert.All(response.Verification!.Operations, operation => Assert.Equal("passed", operation.Status));
        var io = Assert.Single(response.Verification.FinalChecks, c => c.Field == "IoSystemSubnet");
        Assert.Equal("failed", io.Status);
        Assert.Equal("subnet-1", io.Expected);
        Assert.Null(io.Observed);
        Assert.Contains("side effects", io.Message);
        var number = Assert.Single(response.Verification.FinalChecks, c => c.Field == "IoSystemNumber");
        Assert.Equal("1", number.Expected);
    }
    [Fact]
    public async Task IoSystemAttach_ReportsScalarSubnetAndNumberWithoutNestedJsonStrings()
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
        var checks = response.Verification!.Operations[0].Evidence!.Checks;
        Assert.Contains(checks, c => c.Name == "IoSystemSubnet" && c.Expected == "subnet-1" && c.Observed == "subnet-1");
        Assert.Contains(checks, c => c.Name == "IoSystemNumber" && c.Expected == "1" && c.Observed == "1");
        Assert.Contains(response.Verification.FinalChecks, c => c.Field == "IoSystemSubnet" && c.Status == "passed");
        Assert.Contains(response.Verification.FinalChecks, c => c.Field == "IoSystemNumber" && c.Status == "passed");
        using var document = System.Text.Json.JsonDocument.Parse(TiaMcpServer.Json.CanonicalJson.Serialize(response));
        AssertNoNestedJson(document.RootElement);
    }
    private static void AssertNoNestedJson(System.Text.Json.JsonElement element)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object: foreach (var p in element.EnumerateObject()) AssertNoNestedJson(p.Value); break;
            case System.Text.Json.JsonValueKind.Array: foreach (var v in element.EnumerateArray()) AssertNoNestedJson(v); break;
            case System.Text.Json.JsonValueKind.String:
                var text = element.GetString()!.TrimStart();
                Assert.False(text.StartsWith('[') || text.StartsWith('{'), $"Nested JSON string: {text}");
                break;
        }
    }
    [Fact]
    public async Task ReplannedRootCount_DoesNotEraseEarlierPreservationExpectation()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-root-drift");
        var rename = new NetworkOperationRequest { OperationId = "rename", Operation = "update_subnet", Target = new() { Kind = "subnet", SubnetId = "subnet-1" }, SubnetChanges = new() { Name = "Renamed" } };
        var response = await fixture.RunAsync(false, rename, NetworkGuardedWriteFixture.Delete());
        Assert.False(response.Success);
        Assert.Equal(1, response.Effects[1].Effect!.RootDeviceCount);
        Assert.Contains(response.Verification!.FinalChecks, c => c.Kind == "write" && c.Field == "networkDeviceCountUnchanged" && c.Expected == "2" && c.Observed == "1" && c.Status == "failed");
    }
    [Fact]
    public async Task UnknownDelete_StillInspectsExactPlannedNodes()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-unknown-result");
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Delete());
        Assert.False(response.Success);
        Assert.Equal("unverified", Assert.Single(response.Verification!.Operations).Status);
        Assert.Contains(response.Verification.FinalChecks, c => c.Subject?.DeviceName == "PLC_Grouped" && c.Subject.NodeId == "node-2" && c.Field == "exists" && c.Status == "passed");
        Assert.Contains(response.Verification.FinalChecks, c => c.Subject?.DeviceName == "PLC_Ungrouped" && c.Subject.NodeId == "node-3" && c.Field == "exists" && c.Status == "passed");
    }
}
