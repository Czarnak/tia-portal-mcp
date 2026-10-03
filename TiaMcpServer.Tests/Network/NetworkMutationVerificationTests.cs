using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using TiaMcpServer.OpennessWorker.Openness;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class NetworkMutationVerificationTests
{
    private static NetworkOperationRequest Configure(bool io = false) => new()
    {
        OperationId = "configure", Operation = "configure_network_device",
        Target = new() { DeviceName = "PLC_1", NodeId = "node-1" },
        Changes = io ? new() { IoSystem = new() { SubnetId = "subnet-1", Number = 100 } }
            : new() { IpAddress = "192.168.0.10" },
    };

    private static NetworkMutationVerificationInfo Evidence(params NetworkVerificationCheckInfo[] checks) => new()
    {
        Identity = new() { ["deviceName"] = "PLC_1", ["nodeId"] = "node-1" },
        Checks = checks.ToList(),
        Status = checks.Any(x => x.Status == "failed") ? "failed"
            : checks.Any(x => x.Status == "unverified") ? "unverified"
            : checks.Length == 0 ? "not_required" : "passed",
    };

    private static ConfigureNetworkDeviceResultInfo ConfigResult(NetworkMutationVerificationInfo? evidence) => new()
    {
        DeviceName = "PLC_1", AppliedSettings = new() { ["Address"] = "192.168.0.10" },
        Verification = evidence,
    };

    private static StructuredOperationItem Project(NetworkOperationRequest op, object result, bool required = true)
        => NetworkPayloadContract.Project(op, WorkerCallResult.Ok(WorkerJson.SerializePayload(result)), required);

    [Fact]
    public void AppliedSubsetOnly_IsVerified()
    {
        var result = ConfigResult(Evidence(NetworkPostconditionChecks.Compare("Address", "192.168.0.10", "192.168.0.10", true)));
        var op = Configure();
        op.Changes = new() { IpAddress = "192.168.0.10", SubnetMask = "255.255.255.0" };
        result.SkippedSettings["SubnetMask"] = "read only";
        var item = Project(op, result);
        Assert.Equal(new[] { "Address" }, result.Verification!.Checks.Select(check => check.Name));
        Assert.Equal("worker_operation_failed", item.Failure!.Category);
        Assert.NotNull(item.Result);
    }

    [Fact]
    public void UnreadableAppliedValue_IsUnverified()
    {
        var unreadable = NetworkPostconditionChecks.Compare("Address", "192.168.0.10", null, false);
        Assert.Equal("unverified", unreadable.Status);
        var item = Project(Configure(), ConfigResult(Evidence(unreadable)));
        Assert.Equal("postcondition_failed", item.Failure!.Category);
        Assert.NotNull(item.Result);
    }

    [Fact]
    public void WrongIoSystemOnOtherSubnet_Fails()
    {
        var wrongIoTuple = NetworkPostconditionChecks.Compare("IoSystem", "[\"subnet-1\",100]", "[\"subnet-2\",100]", true);
        Assert.Equal("failed", wrongIoTuple.Status);
        var result = ConfigResult(Evidence(wrongIoTuple));
        result.AppliedSettings = new() { ["IoSystem"] = "100" };
        Assert.Equal("postcondition_failed", Project(Configure(true), result).Failure!.Category);
    }

    [Fact]
    public void MissingOrContradictoryVerification_IsProtocolError()
    {
        var missingEvidence = Project(Configure(), ConfigResult(null));
        Assert.Equal("protocol_error", missingEvidence.Failure!.Category);
        Assert.Null(missingEvidence.Result);
        var evidence = Evidence(NetworkPostconditionChecks.Compare("Address", "192.168.0.10", "other", true));
        evidence.Status = "passed";
        AssertProtocolError(Project(Configure(), ConfigResult(evidence)));
        evidence.Status = "failed";
        evidence.Identity["nodeId"] = "wrong-node";
        AssertProtocolError(Project(Configure(), ConfigResult(evidence)));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("unaccounted")]
    [InlineData("overlap")]
    [InlineData("unrequested")]
    [InlineData("wrong-value")]
    public void UnknownOrUnaccountedSetting_IsProtocolError(string corruption)
    {
        var result = ConfigResult(Evidence(NetworkPostconditionChecks.Compare("Address", "192.168.0.10", "192.168.0.10", true)));
        if (corruption == "unknown") result.AppliedSettings["Unknown"] = "secret-canary";
        if (corruption == "unaccounted") result.AppliedSettings.Clear();
        if (corruption == "overlap") result.SkippedSettings["Address"] = "secret-canary";
        if (corruption == "unrequested") result.AppliedSettings["SubnetMask"] = "255.0.0.0";
        if (corruption == "wrong-value") result.AppliedSettings["Address"] = "other";
        AssertProtocolError(Project(Configure(), result));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PostconditionFailure_StopsLaterItems(bool required)
    {
        var result = ConfigResult(Evidence(NetworkPostconditionChecks.Compare("Address", "192.168.0.10", "wrong", true)));
        var calls = 0;
        var batch = await StructuredOperationBatchExecutionEngine.ApplyWritesAsync(
            new[] { Configure(), Configure() },
            _ => { calls++; return Task.FromResult(WorkerCallResult.Ok(WorkerJson.SerializePayload(result))); },
            (op, worker) => required ? NetworkPayloadContract.Project(op, worker, true) : NetworkPayloadContract.Project(op, worker));
        var wrongState = batch.Operations[0];
        var later = batch.Operations[1];
        Assert.Equal("postcondition_failed", wrongState.Failure!.Category);
        Assert.NotNull(wrongState.Result);
        Assert.Equal("earlierOperationFailed", later.SkipReason);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PresentEvidence_IsStrictOnBothOverloads(bool required)
    {
        var evidence = Evidence(NetworkPostconditionChecks.Compare("Address", "192.168.0.10", "wrong", true));
        evidence.Checks[0].Status = "passed";
        evidence.Status = "passed";
        AssertProtocolError(Project(Configure(), ConfigResult(evidence), required));
    }

    [Fact]
    public void NotRequired_IsOnlyValidForNoAppliedConfiguration()
    {
        var result = ConfigResult(Evidence());
        AssertProtocolError(Project(Configure(), result));
        result.AppliedSettings.Clear();
        result.SkippedSettings["Address"] = "read only";
        var item = Project(Configure(), result);
        Assert.Equal("worker_operation_failed", item.Failure!.Category);
        Assert.NotNull(item.Result);
    }

    private static (NetworkOperationRequest Op, SubnetLifecycleResultInfo Result) Deletion()
    {
        var evidence = new NetworkMutationVerificationInfo
        {
            Status = "passed", Identity = new() { ["subnetId"] = "subnet-1" },
            Checks = new()
            {
                NetworkPostconditionChecks.Compare("subnetAbsent", "true", "true", true),
                NetworkPostconditionChecks.Compare("networkDeviceCountUnchanged", "3", "3", true),
                NetworkPostconditionChecks.Compare("affectedNodesPreserved", "true", "true", true),
                NetworkPostconditionChecks.Compare("affectedConnectionsRemoved", "true", "true", true),
            },
        };
        return (new() { OperationId = "delete", Operation = "delete_subnet", Target = new() { Kind = "subnet", SubnetId = "subnet-1" } },
            new() { SubnetId = "subnet-1", Name = "LAN", NetworkDeviceCount = 3, NetworkDeviceCountUnchanged = true, Verification = evidence });
    }

    [Fact]
    public void Deletion_PreservesAffectedNodesAcrossScopes()
    {
        var (op, result) = Deletion();
        Assert.Equal("succeeded", Project(op, result).Status);
        result.Verification!.Checks[2] = NetworkPostconditionChecks.Compare("affectedNodesPreserved", "true", "false", true);
        result.Verification.Status = "failed";
        Assert.Equal("postcondition_failed", Project(op, result).Failure!.Category);
    }

    [Fact]
    public void UninspectableSubnetId_IsNotAbsence()
    {
        var (op, result) = Deletion();
        result.Verification!.Checks[0] = NetworkPostconditionChecks.Compare("subnetAbsent", "true", null, false);
        result.Verification.Status = "unverified";
        Assert.Equal("postcondition_failed", Project(op, result).Failure!.Category);
    }

    private static void AssertProtocolError(StructuredOperationItem item)
    {
        Assert.Equal("protocol_error", item.Failure!.Category);
        Assert.Null(item.Result);
        Assert.DoesNotContain("secret-canary", item.Failure.Message);
    }
}
