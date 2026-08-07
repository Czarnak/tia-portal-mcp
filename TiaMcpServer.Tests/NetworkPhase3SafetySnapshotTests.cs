using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.Safety;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// Proves that canonical serialization of an enriched hardware-config result is stable:
/// two calls with the same object graph must produce byte-identical output.
///
/// This guards the safety-token binding contract: a network write token is bound to the
/// canonical JSON of the current hardware state, and a second serialisation of the same graph
/// must always reproduce the same binding key.
/// </summary>
public class NetworkPhase3SafetySnapshotTests
{
    /// <summary>
    /// Serializes a fully-populated, selector-enriched <see cref="HardwareConfigInfo"/> graph twice
    /// and verifies that both serializations hash to exactly the same value. Any non-determinism
    /// in the serialized form (property ordering, floating-point rendering, list ordering) would
    /// break the safety-token assumption.
    /// </summary>
    [Fact]
    public void EnrichedHardwareConfig_SerializesIdentically_WhenCalledTwice()
    {
        var config = BuildEnrichedConfig();

        var json1 = CanonicalJson.Serialize(config);
        var json2 = CanonicalJson.Serialize(config);

        Assert.Equal(json1, json2);

        var hash1 = Sha256Hex(json1);
        var hash2 = Sha256Hex(json2);

        Assert.Equal(hash1, hash2);
    }

    /// <summary>
    /// A selector-unselectable object (Selectable = false, Selector = null, non-empty Diagnostics)
    /// must also serialize deterministically so degraded results remain stable.
    /// </summary>
    [Fact]
    public void UnselectableHardwareItems_SerializeIdentically_WhenCalledTwice()
    {
        var config = new HardwareConfigInfo
        {
            Devices =
            {
                new DeviceInfo
                {
                    Name = "BadDevice",
                    Items =
                    {
                        new DeviceItemInfo
                        {
                            Name = null,
                            Selectable = false,
                            Selector = null,
                            SelectorDiagnostics = { "Device name could not be read; selector not available." },
                        }
                    }
                }
            }
        };

        var json1 = CanonicalJson.Serialize(config);
        var json2 = CanonicalJson.Serialize(config);

        Assert.Equal(json1, json2);
    }

    /// <summary>
    /// Verifies that the new selector fields survive a JSON round-trip (serialize → deserialize →
    /// serialize again) without drift, so the safety token computed from the first render matches
    /// one computed after a second round-trip.
    /// </summary>
    [Fact]
    public void EnrichedHardwareConfig_RoundTripProducesStableHash()
    {
        var config = BuildEnrichedConfig();

        var json1 = CanonicalJson.Serialize(config);

        // Round-trip: strict deserialize then re-serialize.
        var deserialized = CanonicalJson.Deserialize<HardwareConfigInfo>(json1);
        var json2 = CanonicalJson.Serialize(deserialized);

        Assert.Equal(json1, json2);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static HardwareConfigInfo BuildEnrichedConfig()
    {
        var itemPath = new List<DeviceItemPathSegmentInfo>
        {
            new() { Index = 0, Name = "PROFINET interface_1", PositionNumber = 0, TypeIdentifier = "OrderNumber:IF" },
        };

        return new HardwareConfigInfo
        {
            Devices =
            {
                new DeviceInfo
                {
                    Name = "PLC_1",
                    TypeIdentifier = "OrderNumber:CPU",
                    Items =
                    {
                        new DeviceItemInfo
                        {
                            Name = "PROFINET interface_1",
                            TypeIdentifier = "OrderNumber:IF",
                            PositionNumber = 0,
                            Selectable = true,
                            Selector = NetworkSelectorFactory.DeviceItem("PLC_1", itemPath),
                            NetworkInterfaces =
                            {
                                new NetworkInterfaceInfo
                                {
                                    Name = "PROFINET interface_1",
                                    Selectable = true,
                                    Selector = NetworkSelectorFactory.NetworkInterface(
                                        "PLC_1", itemPath, "PROFINET interface_1", "PROFINET", null),
                                    Nodes =
                                    {
                                        new NodeInfo
                                        {
                                            Name = "X1",
                                            NodeId = "node-1",
                                            NodeType = "Ethernet",
                                            IpAddress = "192.168.0.10",
                                            Selectable = true,
                                            Selector = NetworkSelectorFactory.Node("PLC_1", "node-1"),
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            },
            Subnets =
            {
                new SubnetInfo
                {
                    Name = "PN/IE_1",
                    SubnetId = "subnet-abc",
                    NetworkType = "Ethernet",
                    Selectable = true,
                    Selector = NetworkSelectorFactory.Subnet("subnet-abc"),
                    IoSystems =
                    {
                        new IoSystemInfo
                        {
                            Name = "IO system_1",
                            Number = 100,
                            Selectable = true,
                            Selector = NetworkSelectorFactory.IoSystem("subnet-abc", 100),
                        }
                    },
                    ConnectedNodeNames = { "PLC_1.X1" }
                }
            }
        };
    }

    private static string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    // ===========================================================================================
    // Phase 4 (Task 2): safety-token semantics for the subnet lifecycle operations, proved through
    // the SAME canonical token machinery every other network write uses — no new token
    // implementation and no subnet-specific payload. These exercise the real
    // NetworkSafetySnapshot.BuildTargets + WriteSafetyService pipeline end to end, not mocks.
    // ===========================================================================================

    private const string ToolName = "network_write";

    private static NetworkOperationRequest CreateSubnetOp(
        string operationId, string name, string networkType, string? projectPath = null) => new()
    {
        OperationId = operationId,
        Operation = "create_subnet",
        ProjectPath = projectPath,
        Subnet = new NetworkSubnetDefinition { Name = name, NetworkType = networkType },
    };

    private static NetworkOperationRequest UpdateSubnetOp(
        string operationId, string subnetId, NetworkSubnetChanges changes, string? projectPath = null) => new()
    {
        OperationId = operationId,
        Operation = "update_subnet",
        ProjectPath = projectPath,
        Target = new NetworkObjectTarget { Kind = NetworkObjectKinds.Subnet, SubnetId = subnetId },
        SubnetChanges = changes,
    };

    private static NetworkOperationRequest DeleteSubnetOp(
        string operationId, string subnetId, string? projectPath = null) => new()
    {
        OperationId = operationId,
        Operation = "delete_subnet",
        ProjectPath = projectPath,
        Target = new NetworkObjectTarget { Kind = NetworkObjectKinds.Subnet, SubnetId = subnetId },
    };

    private static SubnetInfo SubnetFixture(
        string name,
        string subnetId,
        string? networkType = "Ethernet",
        List<string>? connectedNodeNames = null,
        List<IoSystemInfo>? ioSystems = null) => new()
    {
        Name = name,
        SubnetId = subnetId,
        NetworkType = networkType,
        ConnectedNodeNames = connectedNodeNames ?? new List<string>(),
        IoSystems = ioSystems ?? new List<IoSystemInfo>(),
    };

    private static HardwareConfigInfo StateFixture(params SubnetInfo[] subnets) => new() { Subnets = subnets.ToList() };

    private static IReadOnlyList<NetworkWriteTargetEvidence> Targets(
        IReadOnlyList<NetworkOperationRequest> operations, HardwareConfigInfo? state)
    {
        var resolution = NetworkSafetySnapshot.BuildTargets(operations, state);
        Assert.True(resolution.Success, resolution.Error);
        return resolution.Targets!;
    }

    // ---- create_subnet: token bound to requested name/network type ---------------------------

    [Fact]
    public void CreateSubnetToken_RejectsWhenRequestedNameChangesBetweenPreviewAndApply()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        var state = new HardwareConfigInfo();
        var previewOps = new[] { CreateSubnetOp("op1", "LINE_1", SubnetLifecycleContract.Ethernet) };

        var preview = safety.CreateCanonicalPreview(
            ToolName, null, Targets(previewOps, null), "summary", previewOps, state, "instructions");

        var applyOps = new[] { CreateSubnetOp("op1", "LINE_2", SubnetLifecycleContract.Ethernet) };
        var result = safety.ValidateAndConsumeCanonical(
            preview.SafetyToken, ToolName, null, Targets(applyOps, null), applyOps, state);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreateSubnetToken_RejectsWhenRequestedNetworkTypeChangesBetweenPreviewAndApply()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        var state = new HardwareConfigInfo();
        var previewOps = new[] { CreateSubnetOp("op1", "LINE_1", SubnetLifecycleContract.Ethernet) };

        var preview = safety.CreateCanonicalPreview(
            ToolName, null, Targets(previewOps, null), "summary", previewOps, state, "instructions");

        // Target evidence (SubnetName only) is unchanged, but the full requested-input document —
        // which the token also binds — now names a different network type. The token must still be
        // rejected: a caller cannot dodge the input binding just because it falls outside evidence.
        var applyOps = new[] { CreateSubnetOp("op1", "LINE_1", SubnetLifecycleContract.Profibus) };
        var result = safety.ValidateAndConsumeCanonical(
            preview.SafetyToken, ToolName, null, Targets(applyOps, null), applyOps, state);

        Assert.False(result.IsValid);
    }

    // ---- update_subnet / delete_subnet: token bound to the resolved subnet -------------------

    [Fact]
    public void UpdateSubnetToken_RejectsWhenResolvedSubnetNameChangesBetweenPreviewAndApply()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        var ops = new[] { UpdateSubnetOp("op1", "S-1", new NetworkSubnetChanges { Name = "RENAMED" }) };
        var stateAtPreview = StateFixture(SubnetFixture("LINE_1", "S-1"));

        var preview = safety.CreateCanonicalPreview(
            ToolName, null, Targets(ops, stateAtPreview), "summary", ops, stateAtPreview, "instructions");

        // Someone else renamed the same subnet (same subnetId) between preview and apply.
        var stateAtApply = StateFixture(SubnetFixture("LINE_1_EXTERNALLY_RENAMED", "S-1"));
        var result = safety.ValidateAndConsumeCanonical(
            preview.SafetyToken, ToolName, null, Targets(ops, stateAtApply), ops, stateAtApply);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void DeleteSubnetToken_RejectsWhenResolvedSubnetNameChangesBetweenPreviewAndApply()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        var ops = new[] { DeleteSubnetOp("op1", "S-1") };
        var stateAtPreview = StateFixture(SubnetFixture("LINE_1", "S-1"));

        var preview = safety.CreateCanonicalPreview(
            ToolName, null, Targets(ops, stateAtPreview), "summary", ops, stateAtPreview, "instructions");

        var stateAtApply = StateFixture(SubnetFixture("LINE_1_EXTERNALLY_RENAMED", "S-1"));
        var result = safety.ValidateAndConsumeCanonical(
            preview.SafetyToken, ToolName, null, Targets(ops, stateAtApply), ops, stateAtApply);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void UpdateSubnetApply_FailsClosedWhenSubnetNoLongerResolvesAtApplyTime()
    {
        // Mirrors NetworkWriteTools.ApplyAsync: targets are re-resolved against a FRESH state read
        // right before the token is even checked. If the subnet was deleted/renamed away in the
        // meantime, resolution itself fails closed — the token is never reached.
        var ops = new[] { UpdateSubnetOp("op1", "S-1", new NetworkSubnetChanges { Name = "RENAMED" }) };
        var stateAtApply = StateFixture(SubnetFixture("LINE_2", "S-2")); // S-1 no longer exists

        var resolution = NetworkSafetySnapshot.BuildTargets(ops, stateAtApply);

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, resolution.FailureCategory);
    }

    [Fact]
    public void DeleteSubnetApply_FailsClosedWhenSubnetNoLongerResolvesAtApplyTime()
    {
        var ops = new[] { DeleteSubnetOp("op1", "S-1") };
        var stateAtApply = StateFixture(SubnetFixture("LINE_2", "S-2"));

        var resolution = NetworkSafetySnapshot.BuildTargets(ops, stateAtApply);

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, resolution.FailureCategory);
    }

    // ---- ordering and project path stay token-bound, exactly like every other network write ---

    [Fact]
    public void RequestOrderRemainsTokenBoundForSubnetBatches()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        var forward = new[]
        {
            CreateSubnetOp("op1", "LINE_1", SubnetLifecycleContract.Ethernet),
            CreateSubnetOp("op2", "LINE_2", SubnetLifecycleContract.Ethernet),
        };
        var state = new HardwareConfigInfo();

        var preview = safety.CreateCanonicalPreview(
            ToolName, null, Targets(forward, null), "summary", forward, state, "instructions");

        var reordered = new[] { forward[1], forward[0] };
        var result = safety.ValidateAndConsumeCanonical(
            preview.SafetyToken, ToolName, null, Targets(reordered, null), reordered, state);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ProjectPathRemainsTokenBoundForSubnetWrites()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        var ops = new[] { DeleteSubnetOp("op1", "S-1", projectPath: @"C:\Projects\LineA.ap21") };
        var state = StateFixture(SubnetFixture("LINE_1", "S-1"));
        var targets = Targets(ops, state);

        var preview = safety.CreateCanonicalPreview(
            ToolName, @"C:\Projects\LineA.ap21", targets, "summary", ops, state, "instructions");

        var result = safety.ValidateAndConsumeCanonical(
            preview.SafetyToken, ToolName, @"C:\Projects\LineB.ap21", targets, ops, state);

        Assert.False(result.IsValid);
        Assert.Equal(WorkerFailureCategories.BindingConflict, result.FailureCategory);
    }

    // ---- connected relationship data never becomes a target dependency or a deletion blocker --

    [Fact]
    public void ConnectedRelationshipOnlyChange_LeavesTargetUnaffected_ButStillInvalidatesViaFullStateHash()
    {
        // A change to ConnectedNodeNames/IoSystems must not be visible in the resolved target
        // evidence at all (no dependency inventory lives in NetworkWriteTargetEvidence), but it IS
        // still part of the whole-project current-state hash the existing token mechanism already
        // binds — so a stale token is still correctly rejected, via the pre-existing state-hash
        // path rather than any new subnet-specific dependency check.
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        var ops = new[] { DeleteSubnetOp("op1", "S-1") };
        var stateAtPreview = StateFixture(SubnetFixture(
            "LINE_1", "S-1",
            connectedNodeNames: new List<string> { "PLC_1.X1" },
            ioSystems: new List<IoSystemInfo> { new() { Name = "IOSYS_1", Number = 100 } }));
        var stateAtApply = StateFixture(SubnetFixture(
            "LINE_1", "S-1",
            connectedNodeNames: new List<string> { "PLC_1.X1", "PLC_2.X1" }, // dependency changed
            ioSystems: new List<IoSystemInfo> { new() { Name = "IOSYS_1", Number = 100 } }));

        var targetsAtPreview = Targets(ops, stateAtPreview);
        var targetsAtApply = Targets(ops, stateAtApply);

        // The resolved target itself is untouched by the connected-relationship change.
        Assert.Equal(CanonicalJson.Serialize(targetsAtPreview), CanonicalJson.Serialize(targetsAtApply));

        var preview = safety.CreateCanonicalPreview(
            ToolName, null, targetsAtPreview, "summary", ops, stateAtPreview, "instructions");
        var result = safety.ValidateAndConsumeCanonical(
            preview.SafetyToken, ToolName, null, targetsAtApply, ops, stateAtApply);

        // Rejected — but via the whole-state hash mismatch, not a "different target" or any
        // dependency-derived blocker.
        Assert.False(result.IsValid);
        Assert.Equal(WorkerFailureCategories.StateChanged, result.FailureCategory);
    }

    // ---- missing/duplicate subnet identity never reaches token issuance ----------------------

    [Fact]
    public void MissingSubnetIdentity_PreventsTokenIssuance()
    {
        // Both network_write's PreviewAsync and ApplyAsync call BuildTargets and bail out on
        // failure BEFORE ever calling CreateCanonicalPreview/ValidateAndConsumeCanonical. Proving
        // resolution fails closed here is what "prevents token issuance" means for this pipeline —
        // there is no separate subnet-specific gate to add.
        var ops = new[] { DeleteSubnetOp("op1", "S-404") };
        var state = StateFixture(SubnetFixture("LINE_1", "S-1"));

        var resolution = NetworkSafetySnapshot.BuildTargets(ops, state);

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, resolution.FailureCategory);
    }

    [Fact]
    public void DuplicateSubnetIdentity_PreventsTokenIssuance()
    {
        var ops = new[] { UpdateSubnetOp("op1", "S-DUP", new NetworkSubnetChanges { Name = "X" }) };
        var state = StateFixture(SubnetFixture("LINE_1", "S-DUP"), SubnetFixture("LINE_2", "S-DUP"));

        var resolution = NetworkSafetySnapshot.BuildTargets(ops, state);

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, resolution.FailureCategory);
    }
}
