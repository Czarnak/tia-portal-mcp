using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using TiaMcpServer.OpennessWorker.Openness;
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
    [InlineData(true, false, false, "Requested subnet was not connected, so IO system attachment was skipped.")]
    [InlineData(true, false, true, "Requested subnet was not connected, so IO system attachment was skipped.")]
    [InlineData(false, false, false, "The network interface does not expose an IO connector.")]
    [InlineData(true, true, false, "The network interface does not expose an IO connector.")]
    [InlineData(false, false, true, null)]
    [InlineData(true, true, true, null)]
    public void Preflight_ExistingIoSkipPrecedence(bool requested, bool connected, bool connector, string? expected)
    {
        Assert.Equal(expected, NetworkPostconditionChecks.IoSystemSkipReason(requested, connected, connector));
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

    [Theory]
    [InlineData("device")]
    [InlineData("node")]
    public void WorkerTopLevelIdentityConflict_IsRejected(string identity)
    {
        var request = new WorkerRequest { DeviceName = "Station", NodeId = "E1", NetworkObjectTarget = new()
        { Kind = "node", DeviceName = identity == "device" ? "Other" : "Station", NodeId = identity == "node" ? "e1" : "E1" } };
        var error = Assert.Throws<TiaMcpServer.OpennessWorker.WorkerOperationException>(() =>
            TiaMcpServer.OpennessWorker.NetworkConfigurationTargetBinding.Resolve(request));
        Assert.Equal(WorkerFailureCategories.ValidationError, error.FailureCategory);
    }

    [Fact]
    public void WorkerTargetBinding_ClonesConstraintsAndAcceptsDeviceCasing()
    {
        var request = new WorkerRequest { DeviceName = "station", NodeId = "E1", NetworkObjectTarget = new()
        { Kind = "node", DeviceName = "Station", NodeId = "E1", NodeIndex = 0, InterfaceName = "X1",
          InterfacePath = new() { new() { Name = "X1", PositionNumber = 32768, TypeIdentifier = "type" } } } };
        var target = TiaMcpServer.OpennessWorker.NetworkConfigurationTargetBinding.Resolve(request);
        request.NetworkObjectTarget.InterfacePath![0].PositionNumber = 33024;
        Assert.Equal(32768, target.InterfacePath![0].PositionNumber);
        Assert.Equal("X1", target.InterfaceName);
        Assert.Equal(0, target.NodeIndex);
    }

    [Theory]
    [InlineData("[{}]")]
    [InlineData("[{\"name\":\"X1\"}]")]
    [InlineData("[{\"name\":\"X1\",\"positionNumber\":32768,\"extra\":true}]")]
    [InlineData("[{\"name\":\"X1\",\"name\":\"X2\",\"positionNumber\":32768}]")]
    [InlineData("[{\"name\":\"X1\",\"positionNumber\":\"32768\"}]")]
    [InlineData("[]")]
    public void InterfacePathEncoding_RejectsIncompleteOrUntypedEvidence(string encoded)
        => Assert.Throws<System.Text.Json.JsonException>(() => NetworkInterfacePathEncoding.Decode(encoded));

    [Fact]
    public void InterfacePathEncoding_RoundTripsEscapedOrdinalIdentity()
    {
        var path = new[] { new NetworkInterfacePathSegmentInfo { Name = "X1\"/雪", PositionNumber = 32768, TypeIdentifier = "Optional" } };
        var encoded = NetworkInterfacePathEncoding.Encode(path);
        Assert.Equal(encoded, NetworkInterfacePathEncoding.Encode(NetworkInterfacePathEncoding.Decode(encoded)));
        Assert.Equal(path[0].Name, NetworkInterfacePathEncoding.Decode(encoded)[0].Name);
    }

    [Fact]
    public void QualifiedImmediateIdentity_IsAcceptedAndWrongOwnerRejected()
    {
        var op = Configure();
        op.Target!.InterfacePath = new[] { new NetworkInterfacePathSegment { Name = "X1", PositionNumber = 32768 } };
        var result = ConfigResult(Evidence(NetworkPostconditionChecks.Compare("Address", "192.168.0.10", "192.168.0.10", true)));
        result.Verification!.Identity["interfacePath"] = "[{\"name\":\"X1\",\"positionNumber\":32768}]";
        Assert.Equal("succeeded", Project(op, result).Status);
        result.Verification.Identity["interfacePath"] = "[{\"name\":\"X2\",\"positionNumber\":33024}]";
        AssertProtocolError(Project(op, result));
    }

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

    [Theory]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("unexpected")]
    [InlineData("status")]
    [InlineData("identity")]
    [InlineData("expected")]
    [InlineData("failed-equal")]
    [InlineData("unverified-observed")]
    [InlineData("unverified-diagnostic")]
    public void MalformedEvidence_IsRejectedWithoutEcho(string corruption)
    {
        var evidence = Evidence(NetworkPostconditionChecks.Compare("Address", "192.168.0.10", "192.168.0.10", true));
        var check = evidence.Checks[0];
        switch (corruption)
        {
            case "duplicate": evidence.Checks.Add(check); break;
            case "missing": evidence.Checks.Clear(); break;
            case "unexpected": check.Name = "secret-canary"; break;
            case "status": check.Status = "secret-canary"; break;
            case "identity": evidence.Identity["extra"] = "secret-canary"; break;
            case "expected": check.Expected = "secret-canary"; break;
            case "failed-equal": check.Status = "failed"; check.Message = "mismatch"; break;
            case "unverified-observed": check.Status = "unverified"; check.Message = "unreadable"; break;
            case "unverified-diagnostic": check.Status = "unverified"; check.Observed = null; break;
        }
        AssertProtocolError(Project(Configure(), ConfigResult(evidence)));
    }

    [Fact]
    public void AddedDevice_RequiresExactReturnedIdentitiesAndType()
    {
        var op = new NetworkOperationRequest { OperationId = "add", Operation = "add_network_device", DeviceName = "PLC", DeviceItemName = "CPU", TypeIdentifier = "OrderNumber:CPU" };
        var result = new AddDeviceResultInfo
        {
            DeviceName = "PLC", RootItemName = "CPU", TypeIdentifier = "OrderNumber:CPU",
            Verification = new()
            {
                Identity = new() { ["deviceName"] = "PLC", ["deviceItemName"] = "CPU" }, Status = "passed",
                Checks = new()
                {
                    NetworkPostconditionChecks.Compare("deviceName", "PLC", "PLC", true),
                    NetworkPostconditionChecks.Compare("deviceItemName", "CPU", "CPU", true),
                    NetworkPostconditionChecks.Compare("typeIdentifier", "OrderNumber:CPU", "OrderNumber:CPU", true),
                },
            },
        };
        Assert.Equal("succeeded", Project(op, result).Status);
        result.Verification.Checks[2] = NetworkPostconditionChecks.Compare("typeIdentifier", "OrderNumber:CPU", "wrong", true);
        result.Verification.Status = "failed";
        Assert.Equal("postcondition_failed", Project(op, result).Failure!.Category);
        result.DeviceName = "other";
        AssertProtocolError(Project(op, result));
    }

    [Theory]
    [InlineData("create_subnet")]
    [InlineData("update_subnet")]
    public void Subnet_RequiresRequestedAttributesAndImmediateCount(string operation)
    {
        var op = new NetworkOperationRequest
        {
            OperationId = "subnet", Operation = operation, Target = new() { SubnetId = "subnet-1", Kind = "subnet" },
            Subnet = new() { Name = "Bus", NetworkType = "Profibus", HighestAddress = 126, TransmissionSpeed = "Baud1500000" },
            SubnetChanges = new() { Name = "Bus", HighestAddress = 126, TransmissionSpeed = "Baud1500000" },
        };
        var result = new SubnetLifecycleResultInfo
        {
            SubnetId = "subnet-1", Name = "Bus", NetworkDeviceCount = 2, NetworkDeviceCountUnchanged = true,
            Verification = new()
            {
                Identity = new() { ["subnetId"] = "subnet-1" }, Status = "passed",
                Checks = new()
                {
                    NetworkPostconditionChecks.Compare("subnetIdentity", "subnet-1", "subnet-1", true),
                    NetworkPostconditionChecks.Compare("Name", "Bus", "Bus", true),
                    NetworkPostconditionChecks.Compare("HighestAddress", "126", "126", true),
                    NetworkPostconditionChecks.Compare("TransmissionSpeed", "Baud1500000", "Baud1500000", true),
                    NetworkPostconditionChecks.Compare("networkDeviceCountUnchanged", "2", "2", true),
                },
            },
        };
        if (operation == "create_subnet") result.Verification.Checks.Add(NetworkPostconditionChecks.Compare("TypeIdentifier", "System:Subnet.Profibus", "System:Subnet.Profibus", true));
        Assert.Equal("succeeded", Project(op, result).Status);
        result.Verification.Checks[4] = NetworkPostconditionChecks.Compare("networkDeviceCountUnchanged", "2", "3", true);
        result.NetworkDeviceCount = 3;
        result.NetworkDeviceCountUnchanged = false;
        result.Verification.Status = "failed";
        var failed = Project(op, result);
        Assert.Equal("postcondition_failed", failed.Failure!.Category);
        Assert.NotNull(failed.Result);
        result.Verification.Checks.Add(result.Verification.Checks[4]);
        AssertProtocolError(Project(op, result));
    }
}
