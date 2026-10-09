using System.Text.Json.Nodes;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;
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
    [InlineData(0, 0, new[] { 0 }, new int[0])]
    [InlineData(1, 0, new[] { 1 }, new int[0])]
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

    // ---------------------------------------------------------------------------------------
    // Worker-payload reader rejections below the nine required root members (already pinned above
    // by Decode_RejectsEveryMissingRequiredRootMember): a null or wrongly typed candidate, device,
    // subnet or message element, and a candidate missing a required member.
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    public void Decode_RejectsRootMessagesContainingNullOrANonString(string extraElement)
    {
        var operation = Operation();
        var payload = ValidPayload(operation).Replace("\"page-message\"", $"\"page-message\",{extraElement}");

        var decoded = HardwarePagePayloadContract.Decode(
            operation,
            WorkerCallResult.Ok(payload),
            continuation: null);

        AssertProtocolFailure(decoded, null);
    }

    [Fact]
    public void Decode_RejectsADeviceCandidateThatIsNull()
    {
        var operation = Operation();
        var root = JsonNode.Parse(ValidPayload(operation))!.AsObject();
        root["deviceCandidates"]!.AsArray()[0] = null;

        var decoded = HardwarePagePayloadContract.Decode(
            operation,
            WorkerCallResult.Ok(root.ToJsonString()),
            continuation: null);

        AssertProtocolFailure(decoded, null);
    }

    [Fact]
    public void Decode_RejectsADeviceCandidateThatIsNotAnObject()
    {
        var operation = Operation();
        var root = JsonNode.Parse(ValidPayload(operation))!.AsObject();
        root["deviceCandidates"]!.AsArray()[0] = "not-an-object";

        var decoded = HardwarePagePayloadContract.Decode(
            operation,
            WorkerCallResult.Ok(root.ToJsonString()),
            continuation: null);

        AssertProtocolFailure(decoded, null);
    }

    // The reader treats deviceCandidates[] and subnetCandidates[] the same way, so "a candidate"
    // is exercised through deviceCandidates[] for the null/non-object/missing-member cases and
    // through subnetCandidates[] for the members that differ between the two (subnet, and this
    // file's only payload with a subnet candidate).

    [Theory]
    [InlineData("offset")]
    [InlineData("device")]
    [InlineData("messages")]
    public void Decode_RejectsADeviceCandidateMissingARequiredMember(string member)
    {
        var operation = Operation();
        var root = JsonNode.Parse(ValidPayload(operation))!.AsObject();
        root["deviceCandidates"]!.AsArray()[0]!.AsObject().Remove(member);

        var decoded = HardwarePagePayloadContract.Decode(
            operation,
            WorkerCallResult.Ok(root.ToJsonString()),
            continuation: null);

        AssertProtocolFailure(decoded, member);
    }

    [Theory]
    [InlineData("offset")]
    [InlineData("subnet")]
    [InlineData("messages")]
    public void Decode_RejectsASubnetCandidateMissingARequiredMember(string member)
    {
        var operation = Operation();
        var baseRoot = ExplainedSubnetRoot(operation);
        AssertAccepted(operation, baseRoot);
        var root = JsonNode.Parse(baseRoot.ToJsonString())!.AsObject();
        root["subnetCandidates"]!.AsArray()[0]!.AsObject().Remove(member);

        var decoded = HardwarePagePayloadContract.Decode(
            operation,
            WorkerCallResult.Ok(root.ToJsonString()),
            continuation: null);

        AssertProtocolFailure(decoded, member);
    }

    [Fact]
    public void Decode_RejectsADeviceThatIsNotAnObject()
    {
        var operation = Operation();
        var root = JsonNode.Parse(ValidPayload(operation))!.AsObject();
        root["deviceCandidates"]!.AsArray()[0]!.AsObject()["device"] = "not-an-object";

        var decoded = HardwarePagePayloadContract.Decode(
            operation,
            WorkerCallResult.Ok(root.ToJsonString()),
            continuation: null);

        AssertProtocolFailure(decoded, null);
    }

    [Fact]
    public void Decode_RejectsASubnetThatIsNotAnObject()
    {
        var operation = Operation();
        var baseRoot = ExplainedSubnetRoot(operation);
        AssertAccepted(operation, baseRoot);
        var root = JsonNode.Parse(baseRoot.ToJsonString())!.AsObject();
        root["subnetCandidates"]!.AsArray()[0]!.AsObject()["subnet"] = "not-an-object";

        var decoded = HardwarePagePayloadContract.Decode(
            operation,
            WorkerCallResult.Ok(root.ToJsonString()),
            continuation: null);

        AssertProtocolFailure(decoded, null);
    }

    [Fact]
    public void Decode_RejectsADeviceCandidateMessagesContainingNull()
    {
        var operation = Operation();
        var root = JsonNode.Parse(ValidPayload(operation))!.AsObject();
        root["deviceCandidates"]!.AsArray()[0]!.AsObject()["messages"]!.AsArray().Add(null);

        var decoded = HardwarePagePayloadContract.Decode(
            operation,
            WorkerCallResult.Ok(root.ToJsonString()),
            continuation: null);

        AssertProtocolFailure(decoded, null);
    }

    // ---------------------------------------------------------------------------------------
    // Typed rules. Each test starts from a complete base payload that Decode accepts (asserted),
    // changes exactly one thing, proves the worker-payload reader still accepts the result, and
    // then proves Decode rejects it, so the rejection comes from HardwarePagePayloadContract.Validate
    // and not from the reader. Subnet-based bases use ExplainedSubnetRoot, because the shared
    // Subnet() fixture is unselectable without a diagnostic and fails the public projection.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Decode_AcceptsAPayloadWithAValidSubnetCandidate()
    {
        var operation = Operation();

        var decoded = HardwarePagePayloadContract.Decode(
            operation,
            WorkerCallResult.Ok(ExplainedSubnetRoot(operation).ToJsonString()),
            continuation: null);

        Assert.True(decoded.IsSuccess);
        Assert.Single(decoded.Payload!.SubnetCandidates);
    }

    [Fact]
    public void Decode_RejectsANullRootMessageElementThePayloadReaderAccepts()
    {
        var operation = Operation();

        AssertRejectedByTypedRules(
            operation,
            DeviceRoot(operation),
            root => root["messages"]!.AsArray().Add(null));
    }

    [Fact]
    public void Decode_RejectsANullDeviceCandidateElementThePayloadReaderAccepts()
    {
        var operation = Operation();

        AssertRejectedByTypedRules(
            operation,
            DeviceRoot(operation),
            root => root["deviceCandidates"]!.AsArray()[0] = null);
    }

    [Fact]
    public void Decode_RejectsANullSubnetCandidateElementThePayloadReaderAccepts()
    {
        var operation = Operation();

        AssertRejectedByTypedRules(
            operation,
            ExplainedSubnetRoot(operation),
            root => root["subnetCandidates"]!.AsArray()[0] = null);
    }

    [Fact]
    public void Decode_RejectsANullDeviceCandidateMessageElementThePayloadReaderAccepts()
    {
        var operation = Operation();

        AssertRejectedByTypedRules(
            operation,
            DeviceRoot(operation),
            root => root["deviceCandidates"]!.AsArray()[0]!["messages"]!.AsArray().Add(null));
    }

    [Fact]
    public void Decode_RejectsANullSubnetCandidateMessageElementThePayloadReaderAccepts()
    {
        var operation = Operation();

        AssertRejectedByTypedRules(
            operation,
            ExplainedSubnetRoot(operation),
            root => root["subnetCandidates"]!.AsArray()[0]!["messages"]!.AsArray().Add(null));
    }

    [Fact]
    public void Decode_RejectsANonPositiveOrderingVersionThePayloadReaderAccepts()
    {
        var operation = Operation();

        AssertRejectedByTypedRules(operation, DeviceRoot(operation), root => root["orderingVersion"] = 0);
    }

    [Fact]
    public void Decode_RejectsAQueryHashThatDoesNotMatchTheRequestThePayloadReaderAccepts()
    {
        var operation = Operation();

        AssertRejectedByTypedRules(
            operation,
            DeviceRoot(operation),
            root => root["queryHash"] = new string('d', 64));
    }

    [Fact]
    public void Decode_RejectsASnapshotHashThatIsNotLowercaseSha256ThePayloadReaderAccepts()
    {
        var operation = Operation();

        AssertRejectedByTypedRules(
            operation,
            DeviceRoot(operation),
            root => root["snapshotHash"] = new string('B', 64));
    }

    [Theory]
    [InlineData("ordering")]
    [InlineData("query")]
    [InlineData("snapshot")]
    public void Decode_RejectsEachContinuationEvidenceMismatchThePayloadReaderAccepts(string mismatched)
    {
        var operation = Operation();
        var queryHash = HardwarePageEvidence.CreateQueryHash(null, null, false, false);
        var matching = new HardwarePageContinuationInfo(1, queryHash, SnapshotHash, 0);
        var mismatching = new HardwarePageContinuationInfo(
            mismatched == "ordering" ? 2 : 1,
            mismatched == "query" ? new string('d', 64) : queryHash,
            mismatched == "snapshot" ? new string('c', 64) : SnapshotHash,
            0);

        AssertRejectedByTypedRules(
            operation,
            DeviceRoot(operation),
            mutate: null,
            baseContinuation: matching,
            mutatedContinuation: mismatching);
    }

    [Fact]
    public void Decode_RejectsASubnetOffsetThatIsNotTheNextContiguousOffsetThePayloadReaderAccepts()
    {
        var operation = Operation();
        var baseRoot = ExplainedSubnets(CandidatePayload(
            operation,
            startOffset: 0,
            totalDevices: 1,
            totalSubnets: 1,
            deviceOffsets: new[] { 0 },
            subnetOffsets: new[] { 1 }));

        AssertRejectedByTypedRules(
            operation,
            baseRoot,
            root => root["subnetCandidates"]!.AsArray()[0]!["offset"] = 2);
    }

    [Fact]
    public void Decode_RejectsASubnetOffsetInsideTheDeviceRangeThePayloadReaderAccepts()
    {
        // Base: one device (offset 0) and two subnets, page size 2, so the page returns the device
        // and the first subnet (offset 1). Raising totalDevices to 2 keeps every count rule
        // satisfied (total 4, returned 2 = min(2, 4)) and only moves the subnet offset inside the
        // device range, which is the offset rule this case exists for.
        var operation = Operation();
        var baseRoot = ExplainedSubnets(CandidatePayload(
            operation,
            startOffset: 0,
            totalDevices: 1,
            totalSubnets: 2,
            deviceOffsets: new[] { 0 },
            subnetOffsets: new[] { 1 }));

        AssertRejectedByTypedRules(operation, baseRoot, root => root["totalDevices"] = 2);
    }

    [Fact]
    public void Decode_RejectsANegativeSubnetTotalThePayloadReaderAccepts()
    {
        var operation = Operation();

        AssertRejectedByTypedRules(
            operation,
            ExplainedSubnetRoot(operation),
            root => root["totalSubnets"] = -1);
    }

    [Fact]
    public void Decode_RejectsAReturnedCountThatDoesNotFillThePageThePayloadReaderAccepts()
    {
        // Rule covered: returned count must equal min(page size, total - start). Raising totalDevices
        // to 1 makes the total 2, so a page of size 2 must return 2 candidates but returns only the
        // one subnet. (The subnet-offset-inside-the-device-range rule is covered separately above.)
        var operation = Operation();

        AssertRejectedByTypedRules(
            operation,
            ExplainedSubnetRoot(operation),
            root => root["totalDevices"] = 1);
    }

    [Fact]
    public void Decode_RejectsACandidateThatFailsThePublicHardwareContractThePayloadReaderAccepts()
    {
        var operation = Operation();

        AssertRejectedByTypedRules(
            operation,
            ExplainedSubnetRoot(operation),
            root => root["subnetCandidates"]!.AsArray()[0]!["subnet"]!["selectable"] = true);
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

    private static JsonObject DeviceRoot(NetworkOperationRequest operation)
        => JsonNode.Parse(ValidPayload(operation))!.AsObject();

    private static JsonObject ExplainedSubnetRoot(NetworkOperationRequest operation)
        => ExplainedSubnets(CandidatePayload(
            operation,
            startOffset: 0,
            totalDevices: 0,
            totalSubnets: 1,
            deviceOffsets: Array.Empty<int>(),
            subnetOffsets: new[] { 0 }));

    // Subnet() is unselectable with no diagnostic, which the public hardware contract rejects.
    // Give each subnet the one diagnostic that makes it a valid unselectable subnet.
    private static JsonObject ExplainedSubnets(string payload)
    {
        var root = JsonNode.Parse(payload)!.AsObject();
        foreach (var candidate in root["subnetCandidates"]!.AsArray())
        {
            candidate!["subnet"]!["selectorDiagnostics"] = new JsonArray("unselectable in this fixture");
        }

        return root;
    }

    private static void AssertAccepted(NetworkOperationRequest operation, JsonObject root)
    {
        var baseline = HardwarePagePayloadContract.Decode(
            operation,
            WorkerCallResult.Ok(root.ToJsonString()),
            continuation: null);
        Assert.True(baseline.IsSuccess, "The base payload must be accepted before it is mutated.");
    }

    /// <summary>
    /// The unmutated base must Decode successfully; the mutated payload must still be accepted by the
    /// reader and rejected by Decode, so the typed rules did the rejecting.
    /// </summary>
    private static void AssertRejectedByTypedRules(
        NetworkOperationRequest operation,
        JsonObject baseRoot,
        Action<JsonObject>? mutate,
        HardwarePageContinuationInfo? baseContinuation = null,
        HardwarePageContinuationInfo? mutatedContinuation = null)
    {
        var basePayload = baseRoot.ToJsonString();
        var baseline = HardwarePagePayloadContract.Decode(
            operation,
            WorkerCallResult.Ok(basePayload),
            baseContinuation);
        Assert.True(baseline.IsSuccess, "The base payload must be accepted before it is mutated.");

        var mutated = JsonNode.Parse(basePayload)!.AsObject();
        mutate?.Invoke(mutated);
        var payload = mutated.ToJsonString();
        Assert.NotNull(CanonicalJson.DeserializeWorkerPayload<HardwarePageCandidateResultInfo>(payload));

        var decoded = HardwarePagePayloadContract.Decode(
            operation,
            WorkerCallResult.Ok(payload),
            mutatedContinuation ?? baseContinuation);

        AssertProtocolFailure(decoded, payload);
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
