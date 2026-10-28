using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Network;
using TiaMcpServer.OpennessWorker.Openness.Network;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class NetworkMutationVerificationTests
{
    [Theory]
    [InlineData(0, true, "worker_operation_failed")]
    [InlineData(2, true, "worker_operation_failed")]
    [InlineData(1, false, "worker_operation_failed")]
    [InlineData(1, true, null)]
    public void PreflightDependency_UsesPremutationFailureCategory(int count, bool complete, string? expected)
    {
        Assert.Equal(expected, NetworkPostconditionChecks.ClassifyDependencySelection(count, complete));
    }

    [Theory]
    [InlineData(1, false, "worker_operation_failed")]
    [InlineData(0, false, "worker_operation_failed")]
    [InlineData(1, true, null)]
    [InlineData(0, true, "postcondition_failed")]
    [InlineData(2, true, "postcondition_failed")]
    public void SelectionCertainty_UnknownCandidateDeniesEvenWithOneVisibleMatch(int count, bool complete, string? expected)
    {
        Assert.Equal(expected, NetworkPostconditionChecks.ClassifySelection(count, complete));
    }

    [Theory]
    [InlineData("device")]
    [InlineData("node")]
    public void WorkerTopLevelIdentityConflict_IsRejected(string identity)
    {
        var request = new WorkerRequest { DeviceName = "Station", NodeId = "E1", NetworkObjectTarget = new()
        { Kind = "node", DeviceName = identity == "device" ? "Other" : "Station", NodeId = identity == "node" ? "e1" : "E1" } };
        var error = Assert.Throws<TiaMcpServer.OpennessWorker.Worker.WorkerOperationException>(() =>
            TiaMcpServer.OpennessWorker.Openness.Network.NetworkConfigurationTargetBinding.Resolve(request));
        Assert.Equal(WorkerFailureCategories.ValidationError, error.FailureCategory);
    }

    [Fact]
    public void WorkerTargetBinding_ClonesConstraintsAndAcceptsDeviceCasing()
    {
        var request = new WorkerRequest { DeviceName = "station", NodeId = "E1", NetworkObjectTarget = new()
        { Kind = "node", DeviceName = "Station", NodeId = "E1", NodeIndex = 0, InterfaceName = "X1",
          InterfacePath = new() { new() { Name = "X1", PositionNumber = 32768, TypeIdentifier = "type" } } } };
        var target = TiaMcpServer.OpennessWorker.Openness.Network.NetworkConfigurationTargetBinding.Resolve(request);
        request.NetworkObjectTarget.InterfacePath![0].PositionNumber = 33024;
        Assert.Equal(32768, target.InterfacePath![0].PositionNumber);
        Assert.Equal("X1", target.InterfaceName);
        Assert.Equal(0, target.NodeIndex);
    }
    private static NetworkOperationRequest Configure() => new()
    {
        OperationId = "configure", Operation = "configure_network_device",
        Target = new() { DeviceName = "PLC_1", NodeId = "node-1" },
        Changes = new() { IpAddress = "192.168.0.10", PnDeviceName = "new-name" }
    };

    [Fact]
    public void RejectedConfigure_ProjectsOutcomeAsResultAndTypedFailure()
    {
        var outcome = new ConfigureNetworkDeviceResultInfo { DeviceName = "PLC_1",
            AppliedSettings = new() { ["Address"] = "192.168.0.10" } };
        var item = NetworkPayloadContract.Project(Configure(), WorkerCallResult.Fail("engineering_rejected", "scripted")
            with { ConfigureNetworkDeviceOutcome = outcome });
        Assert.Equal("failed", item.Status);
        Assert.Equal("engineering_rejected", item.Failure!.Category);
        Assert.Equal("192.168.0.10", item.Result!.Value.GetProperty("appliedSettings").GetProperty("Address").GetString());
        Assert.False(item.Result.Value.TryGetProperty("verification", out _));
    }

    [Fact]
    public void RejectedConfigureOutcome_ContradictingRequest_IsProtocolError()
    {
        var item = NetworkPayloadContract.Project(Configure(), WorkerCallResult.Fail("engineering_rejected", "scripted")
            with { ConfigureNetworkDeviceOutcome = new() { DeviceName = "PLC_1",
                AppliedSettings = new() { ["SubnetMask"] = "255.0.0.0" } } });
        Assert.Equal("protocol_error", item.Failure!.Category);
        Assert.Null(item.Result);
        Assert.DoesNotContain("255.0.0.0", item.Failure.Message);
        Assert.Equal("The worker outcome for 'configure_network_device' did not match its declared result contract and was rejected.", item.Failure.Message);
    }

    [Fact]
    public void RejectedConfigure_WithoutOutcome_HasNullResult()
    {
        var item = NetworkPayloadContract.Project(Configure(), WorkerCallResult.Fail("engineering_rejected", "scripted"));
        Assert.Equal("engineering_rejected", item.Failure!.Category);
        Assert.Null(item.Result);
    }

    [Theory]
    [InlineData("PLC_1_1", "succeeded")]
    [InlineData("", "failed")]
    public void AddedDevice_AcceptsReportedNonblankIdentity(string name, string status)
    {
        var request = new NetworkOperationRequest { OperationId = "add", Operation = "add_network_device", DeviceName = "PLC_1", TypeIdentifier = "type" };
        var item = NetworkPayloadContract.Project(request, WorkerCallResult.Ok(WorkerJson.SerializePayload(
            new AddDeviceResultInfo { DeviceName = name, RootItemName = "CPU", TypeIdentifier = "type" })));
        Assert.Equal(status, item.Status);
        if (status == "succeeded") Assert.Equal(name, item.Result!.Value.GetProperty("deviceName").GetString());
        else Assert.Equal("protocol_error", item.Failure!.Category);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConfigureSuccess_WithPartialOrEmptyAppliedSubset_IsSucceeded(bool applied)
    {
        var item = NetworkPayloadContract.Project(Configure(), WorkerCallResult.Ok(WorkerJson.SerializePayload(
            new ConfigureNetworkDeviceResultInfo { DeviceName = "PLC_1", AppliedSettings = applied ? new() { ["Address"] = "192.168.0.10" } : new() })));
        Assert.Equal("succeeded", item.Status);
    }

    [Theory]
    [InlineData("subnet-1", "succeeded")]
    [InlineData("subnet-2", "failed")]
    public void SubnetResult_RequiresMatchingResolvedIdentity(string requestedId, string status)
    {
        var operation = new NetworkOperationRequest { OperationId = "update", Operation = "update_subnet",
            Target = new() { SubnetId = requestedId } };
        var item = NetworkPayloadContract.Project(operation, WorkerCallResult.Ok(WorkerJson.SerializePayload(
            new SubnetLifecycleResultInfo { SubnetId = "subnet-1", Name = "PN/IE" })));
        Assert.Equal(status, item.Status);
        if (status == "failed") Assert.Equal("protocol_error", item.Failure!.Category);
    }
    [Fact]
    public void QualifiedKey_IgnoresOptionalEvidenceButPreservesOrdinalOwnerAndNodeIdentity()
    {
        var target = new NetworkWriteTargetEvidence("configure", "configure_network_device", "PLC_1", null,
            [], "X1", null, "E1", null, null, null, null,
            new[] { new NetworkInterfacePathSegmentInfo { Name = "CPU/Owner", PositionNumber = 32768 } });
        NetworkWriteEffect Effect(NetworkWriteTargetEvidence value) => new("configure_network_device", value,
            new Dictionary<string, string>(), new Dictionary<string, string?>(), [], true);
        var effects = new Dictionary<string, NetworkWriteEffect> { ["configure"] = Effect(target) };
        var policy = new NetworkVerificationPolicy(effects);
        var key = Assert.Single(policy.ObjectKeys(Configure(), false));
        effects["configure"] = Effect(target with { DeviceName = "plc_1", NetworkInterfaceName = "Other evidence",
            InterfacePath = new[] { new NetworkInterfacePathSegmentInfo { Name = "CPU/Owner", PositionNumber = 32768, TypeIdentifier = "Optional" } } });
        Assert.Equal(key, Assert.Single(policy.ObjectKeys(Configure(), true)));
        effects["configure"] = Effect(target with { NodeId = "e1" });
        Assert.NotEqual(key, Assert.Single(policy.ObjectKeys(Configure(), true)));
        effects["configure"] = Effect(target with { InterfacePath = new[] { new NetworkInterfacePathSegmentInfo { Name = "cpu/Owner", PositionNumber = 32768 } } });
        Assert.NotEqual(key, Assert.Single(policy.ObjectKeys(Configure(), true)));
        effects["configure"] = Effect(target with { InterfacePath = null });
        Assert.Throws<InvalidOperationException>(() => policy.ObjectKeys(Configure(), false));
    }
}
