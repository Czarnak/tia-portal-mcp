using System.Text.Json.Nodes;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class HardwarePagePayloadContractTests
{
    private const string SnapshotHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Theory]
    [InlineData("private-invalid-json-payload", "private-invalid-json-payload")]
    [InlineData("[]", null)]
    [InlineData("null", null)]
    public void Decode_RejectsInvalidJsonAndWrongRootTypesWithoutEchoingPayload(
        string payload,
        string? uniqueRejectedText)
    {
        var operation = Operation();

        var decoded = HardwarePagePayloadContract.Decode(
            operation,
            WorkerCallResult.Ok(payload),
            continuation: null);

        AssertProtocolFailure(decoded, uniqueRejectedText);
    }

    [Theory]
    [InlineData("orderingVersion")]
    [InlineData("queryHash")]
    [InlineData("snapshotHash")]
    [InlineData("startOffset")]
    [InlineData("totalDevices")]
    [InlineData("totalSubnets")]
    [InlineData("messages")]
    [InlineData("deviceCandidates")]
    [InlineData("subnetCandidates")]
    public void Decode_RejectsEveryMissingRequiredRootMember(string member)
    {
        var operation = Operation();
        var root = JsonNode.Parse(ValidPayload(operation))!.AsObject();
        root.Remove(member);

        var decoded = HardwarePagePayloadContract.Decode(
            operation,
            WorkerCallResult.Ok(root.ToJsonString()),
            continuation: null);

        AssertProtocolFailure(decoded, member);
    }

    [Theory]
    [InlineData(new[] { 0, 0 })]
    [InlineData(new[] { 1, 0 })]
    [InlineData(new[] { 0, 2 })]
    public void Decode_RejectsDuplicateOutOfOrderAndNoncontiguousOffsets(int[] offsets)
    {
        var operation = Operation();
        var payload = CandidatePayload(
            operation,
            startOffset: 0,
            totalDevices: 3,
            totalSubnets: 0,
            deviceOffsets: offsets,
            subnetOffsets: Array.Empty<int>());

        var decoded = HardwarePagePayloadContract.Decode(
            operation,
            WorkerCallResult.Ok(payload),
            continuation: null);

        AssertProtocolFailure(decoded, payload);
    }

    [Fact]
    public void Decode_RejectsAStartOffsetDifferentFromTheRequestedContinuation()
    {
        var operation = Operation();
        var continuation = new HardwarePageContinuationInfo(
            1,
            HardwarePageEvidence.CreateQueryHash(null, null, false, false),
            SnapshotHash,
            1);
        var payload = CandidatePayload(
            operation,
            startOffset: 0,
            totalDevices: 2,
            totalSubnets: 0,
            deviceOffsets: new[] { 0 },
            subnetOffsets: Array.Empty<int>());

        var decoded = HardwarePagePayloadContract.Decode(
            operation,
            WorkerCallResult.Ok(payload),
            continuation);

        AssertProtocolFailure(decoded, payload);
    }

    [Theory]
    [InlineData(-1, 0, new[] { 0 }, new int[0])]
    [InlineData(0, -1, new int[0], new[] { 0 })]
    [InlineData(0, 0, new[] { 0 }, new int[0])]
    [InlineData(1, 0, new[] { 1 }, new int[0])]
    [InlineData(1, 1, new int[0], new[] { 0 })]
    [InlineData(int.MaxValue, 1, new[] { 0, 1 }, new int[0])]
    public void Decode_RejectsNegativeCountsReturnedCountsAboveTotalsAndKindInconsistentOffsets(
        int totalDevices,
        int totalSubnets,
        int[] deviceOffsets,
        int[] subnetOffsets)
    {
        var operation = Operation();
        var payload = CandidatePayload(
            operation,
            startOffset: 0,
            totalDevices,
            totalSubnets,
            deviceOffsets,
            subnetOffsets);

        var decoded = HardwarePagePayloadContract.Decode(
            operation,
            WorkerCallResult.Ok(payload),
            continuation: null);

        AssertProtocolFailure(decoded, payload);
    }

    [Fact]
    public void Decode_RejectsWrongQueryOrderingAndSnapshotEvidenceForAContinuation()
    {
        var operation = Operation();
        var requested = new HardwarePageContinuationInfo(
            2,
            HardwarePageEvidence.CreateQueryHash(null, null, false, false),
            new string('c', 64),
            0);

        var decoded = HardwarePagePayloadContract.Decode(
            operation,
            WorkerCallResult.Ok(ValidPayload(operation)),
            requested);

        AssertProtocolFailure(decoded, SnapshotHash);
    }

    [Fact]
    public void Decode_RejectsADeviceCandidateContainingASubnetPayload()
    {
        var operation = Operation();
        var root = JsonNode.Parse(ValidPayload(operation))!.AsObject();
        var deviceCandidate = root["deviceCandidates"]!.AsArray()[0]!.AsObject();
        deviceCandidate["device"] = JsonNode.Parse(CanonicalJson.Serialize(Subnet("wrong-kind")));
        var payload = root.ToJsonString();

        var decoded = HardwarePagePayloadContract.Decode(
            operation,
            WorkerCallResult.Ok(payload),
            continuation: null);

        AssertProtocolFailure(decoded, "wrong-kind");
    }

    [Fact]
    public void Decode_RejectsPayloadSessionIdentityWithoutExposingIt()
    {
        const string SecretSession = "secret-worker-session";
        var operation = Operation();
        var root = JsonNode.Parse(ValidPayload(operation))!.AsObject();
        root["sessionIdentity"] = new JsonObject { ["workerSessionId"] = SecretSession };

        var decoded = HardwarePagePayloadContract.Decode(
            operation,
            WorkerCallResult.Ok(root.ToJsonString()),
            continuation: null);

        AssertProtocolFailure(decoded, SecretSession);
    }

    [Fact]
    public void Decode_AcceptsTheExactTypedCandidateContract()
    {
        var operation = Operation();

        var decoded = HardwarePagePayloadContract.Decode(
            operation,
            WorkerCallResult.Ok(ValidPayload(operation)),
            continuation: null);

        Assert.True(decoded.IsSuccess);
        Assert.NotNull(decoded.Payload);
        Assert.Null(decoded.Item);
        Assert.Equal(0, decoded.Payload!.StartOffset);
        Assert.Single(decoded.Payload.DeviceCandidates);
    }

    private static void AssertProtocolFailure(HardwarePagePayloadContractResult decoded, string? rejectedText)
    {
        Assert.False(decoded.IsSuccess);
        Assert.Null(decoded.Payload);
        var item = Assert.IsType<StructuredOperationItem>(decoded.Item);
        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
        if (rejectedText is not null)
        {
            Assert.DoesNotContain(rejectedText, CanonicalJson.Serialize(item), StringComparison.Ordinal);
        }
    }

    private static NetworkOperationRequest Operation() => new()
    {
        OperationId = "hardware",
        Operation = "read_hardware_config",
        PageSize = 2,
    };

    private static string ValidPayload(NetworkOperationRequest operation)
        => CandidatePayload(
            operation,
            startOffset: 0,
            totalDevices: 1,
            totalSubnets: 0,
            deviceOffsets: new[] { 0 },
            subnetOffsets: Array.Empty<int>());

    private static string CandidatePayload(
        NetworkOperationRequest operation,
        int startOffset,
        int totalDevices,
        int totalSubnets,
        IReadOnlyList<int> deviceOffsets,
        IReadOnlyList<int> subnetOffsets)
        => CanonicalJson.Serialize(new HardwarePageCandidateResultInfo(
            OrderingVersion: 1,
            QueryHash: HardwarePageEvidence.CreateQueryHash(
                operation.DeviceName,
                operation.PlcName,
                operation.IncludeIoDetails,
                operation.IncludeTagMatches),
            SnapshotHash,
            StartOffset: startOffset,
            TotalDevices: totalDevices,
            TotalSubnets: totalSubnets,
            Messages: new[] { "page-message" },
            DeviceCandidates: deviceOffsets
                .Select(offset => new HardwareDevicePageCandidateInfo(
                    offset,
                    Device($"device-{offset}"),
                    new[] { $"device-message-{offset}" }))
                .ToArray(),
            SubnetCandidates: subnetOffsets
                .Select(offset => new HardwareSubnetPageCandidateInfo(
                    offset,
                    Subnet($"subnet-{offset}"),
                    new[] { $"subnet-message-{offset}" }))
                .ToArray()));

    internal static DeviceInfo Device(string name) => new()
    {
        Name = name,
        TypeIdentifier = "OrderNumber:device",
        Items = new List<DeviceItemInfo>(),
    };

    internal static SubnetInfo Subnet(string name) => new()
    {
        Name = name,
        SubnetId = $"id-{name}",
        NetworkType = "Ethernet",
        TypeIdentifier = "Subnet",
        Selectable = false,
        Selector = null,
        SelectorDiagnostics = new List<string>(),
        IoSystems = new List<IoSystemInfo>(),
        ConnectedNodeNames = new List<string>(),
    };
}
