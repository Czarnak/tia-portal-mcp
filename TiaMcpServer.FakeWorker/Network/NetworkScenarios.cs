using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Network;
using TiaMcpServer.OpennessWorker.Worker;
using static TiaMcpServer.FakeWorker.ScenarioSupport;

namespace TiaMcpServer.FakeWorker.Network;

internal sealed partial class NetworkScenarios
{
    // Captured in 2026-10-29-network-write-native-rejections-live-rerun.md, L11c/L13mini (English UI).
    private const string LiveConfigureRejectionMessage = "Error when calling method 'ConnectToSubnet' of type 'Siemens.Engineering.HW.Node'."
        + "\r\n\r\nAssignment to subnet failed.";
    private const string LiveAddressRejectionMessage = "Error when calling method 'set_Address' of type 'Siemens.Engineering.HW.Node'."
        + "\r\n\r\nnot-an-ip is not a valid IP address";
    // Process-local, mutable hardware state for the "multi-homed-network" scenario (see below): a
    // single PC station exposing two ports on separate interfaces. Declared once per FakeWorker
    // process so a configure_network_device call mutates it and a later read_hardware_config call in
    // the SAME process observes the mutation - proving a real read -> select -> preview -> apply ->
    // read round trip, not just a static fixture.
    private readonly MultiHomedNode multiHomedPlcNode = new MultiHomedNode { Name = "PLC port", NodeId = "node-plc", IpAddress = "192.168.0.20" };
    private readonly MultiHomedNode multiHomedDbNode = new MultiHomedNode { Name = "Database port", NodeId = "node-db", IpAddress = "10.20.30.40" };
    private HardwareConfigInfo? qualifiedNetworkState = null;
    private int qualifiedHardwareReadCount = 0;
    private HardwareConfigInfo? guardedNetworkState = null;
    private HardwareConfigInfo? roundtripNetworkState = null;
    private int guardedNetworkWrites = 0;
    private readonly Dictionary<(string SubnetId, string Name), string> guardedSubnetAttributes = new Dictionary<(string SubnetId, string Name), string>();
    private readonly Dictionary<string, bool> guardedPnAutoGeneration = new Dictionary<string, bool>(StringComparer.Ordinal);

    // Process-local, mutable subnet state shared by every "network-subnet-lifecycle*" scenario key
    // (Task 6, Phase 4): two devices that never change, and two subnets - one Ethernet, one PROFIBUS -
    // each already connected to a node. Sharing this exact list across the main scenario and its
    // switch variants (malformed/postcondition-failed/second-item-failure/alt-path) means a resolved
    // subnet's identity is byte-for-byte identical no matter which of those keys reads it, so a
    // project-path binding test can bind against one key and get rejected against another for
    // exactly that reason - never a coincidentally different target. Connected subnets are never
    // treated as undeletable here: delete_subnet removes them unconditionally, matching production's
    // "connected deletion is allowed, no dependency inventory" rule.
    private readonly List<SubnetLifecycleSubnetState> subnetLifecycleState = new List<SubnetLifecycleSubnetState>
    {
        new()
        {
            SubnetId = "subnet-eth-1",
            Name = "PN/IE_1",
            NetworkType = SubnetLifecycleContract.Ethernet,
            ConnectedNodeNames = new List<string> { "PLC_1.X1" },
        },
        new()
        {
            SubnetId = "subnet-pb-1",
            Name = "MPI/DP_1",
            NetworkType = SubnetLifecycleContract.Profibus,
            HighestAddress = 31,
            TransmissionSpeed = "Baud187500",
            ConnectedNodeNames = new List<string> { "PLC_1.MPI" },
        },
    };
    private int subnetLifecycleNextId = 1;
    private int subnetLifecycleSecondFailureWriteCount = 0;
    private readonly Dictionary<string, int> hardwarePaginationScenarioCalls = new Dictionary<string, int>(StringComparer.Ordinal);
    public bool IdentityDrift { get; private set; }
    // Two devices that never change across any subnet lifecycle operation, modelling the stable
    // "root device count" the production SubnetLifecycleService verifies after every commit.
    private const int SubnetLifecycleDeviceCount = 2;


    public void Handle(string line, string scenario, int seq, string? currentProjectPath, string? projectScenario,
        Action<string, bool> respond, Action advanceSessionGeneration)
    {
        void Respond(string json, bool includeSessionIdentity = true) => respond(json, includeSessionIdentity);
        switch (scenario)
        {
            case "network-read-warnings":
                // A contract-valid hardware payload carried alongside worker warnings, so the network
                // read path can be proven to copy warnings onto the item it decoded successfully.
                Respond("""{"success":true,"payload":"{\"devices\":[],\"subnets\":[],\"messages\":[]}","warnings":["Skipping device 'X' while reading hardware configuration: access denied.","Skipping subnet 'Y' while reading hardware configuration: not supported."]}""");
                break;
            case "network-config-partial":
            case "network-config-all-skipped":
                roundtripNetworkState ??= RoundtripHardwareConfig();
                if (ReadMethod(line) == "configure_network_device")
                {
                    var applied = new Dictionary<string, string>();
                    if (scenario == "network-config-partial" && ReadField(line, "ipAddress") is { } address)
                    {
                        GuardedNodes(roundtripNetworkState).Single(n => n.NodeId == "node-1").IpAddress = address;
                        applied[NetworkSettingKeys.Address] = address;
                    }
                    Respond(WorkerJson.SerializePayload(new { success = false, failureCategory = "engineering_rejected",
                        error = "TIA Portal rejected the operation: scripted",
                        engineeringError = new { type = "EngineeringTargetInvocationException", message = "scripted", inner = Array.Empty<object>() },
                        configureNetworkDeviceOutcome = new ConfigureNetworkDeviceResultInfo
                        { DeviceName = "PLC_1", AppliedSettings = applied, Messages = new() { $"seq:{seq}" } } }));
                }
                else Respond(ReadMethod(line) == "read_hardware_config"
                    ? Success(WorkerJson.SerializePayload(roundtripNetworkState))
                    : """{"success":false,"error":"unexpected sparse configuration method"}""");
                break;
            case "network-roundtrip":
                roundtripNetworkState ??= RoundtripHardwareConfig();
                Respond(ReadMethod(line) switch
                {
                    // The request still advances seq, but its safety-bound state must remain stable
                    // between preview and apply; write responses below expose the request ordering.
                    // Both read payloads must satisfy their declared Phase 2 result contracts
                    // (HardwareConfigInfo / CatalogEntryInfo[]); an unmapped member here would be
                    // rejected as protocol_error instead of decoding. The hardware payload models a
                    // PLC plus a multi-homed PC station so node, subnet and IO-system identities are
                    // observable end to end.
                    "read_hardware_config" => Success(WorkerJson.SerializePayload(roundtripNetworkState)),
                    "search_equipment_catalog" => """{"success":true,"payload":"[{\"typeName\":\"TEST\",\"articleNumber\":null,\"version\":null,\"typeIdentifier\":\"OrderNumber:TEST\",\"typeIdentifierNormalized\":null,\"catalogPath\":null,\"description\":null}]"}""",
                    "list_network_objects" => Success(WorkerJson.SerializePayload(ListNetworkObjectsFixture())),
                    "inspect_network_object" => Success(WorkerJson.SerializePayload(InspectNetworkObjectFixture())),
                    "add_network_device" or "configure_network_device" => HandleGuardedNetwork(line, roundtripNetworkState, scenario),
                    _ => $$"""{"success":false,"error":"unexpected network method '{{ReadMethod(line)}}'"}"""
                });
                break;
            case "network-mixed-results":
                // One explicitly open project can return distinct outcomes for a batch without
                // pretending that each item switched the Portal to a different project.
                Respond(ReadField(line, "deviceName") switch
                {
                    "worker-failure" => """{"success":false,"error":"boom"}""",
                    "contract-failure" => $$"""{"success":true,"payload":"{\"seq\":{{seq}}}"}""",
                    "good" => Success(HardwareConfigPayload()),
                    _ => """{"success":false,"error":"unexpected mixed-result device"}"""
                });
                break;
            case "network-binding-mismatch":
                Respond(ReadMethod(line) == "read_hardware_config"
                    ? SuccessWithResolvedPath(
                        HardwareConfigPayload(),
                        @"C:\FakeWorker\Different.ap21")
                    : $$"""{"success":false,"error":"expected read_hardware_config, got '{{ReadMethod(line)}}'"}""");
                break;
            case "network-connection-evidence":
            case "network-connection-evidence-degraded":
                Respond(ReadMethod(line) == "read_hardware_config"
                    ? Success(WorkerJson.SerializePayload(ConnectionEvidenceHardwareConfig(
                        currentProjectPath?.Contains("degraded", StringComparison.Ordinal) == true)))
                    : """{"success":false,"error":"expected ordinary hardware read"}""");
                break;

            case "network-state-seq":
                // A contract-valid HardwareConfigInfo that reports the request sequence in its own
                // messages array, so a test can count how many worker requests a preview issued
                // without the payload failing its declared contract. Also resolvable: it models a
                // "PLC_2" device with a "node-1" node, so a configure_network_device target in the
                // same batch can be resolved by NetworkIdentityResolver against this same read.
                Respond(Success(WorkerJson.SerializePayload(SingleNodeHardwareConfig(
                    "PLC_2", "if_1", "if_1", "n1", "node-1", messages: new[] { $"seq:{seq}" }))));
                break;

            case "network-io-map":
                // Structured I/O-map scenario: read_hardware_config returns ioDetails (addresses,
                // channels, tag matches) ONLY when the request opted in with includeIoDetails=true.
                // When not requested, IoDetails is null and the JsonIgnore attribute omits it, so the
                // default read stays byte-identical to the legacy hardware shape. Built from the
                // shared Contracts DTOs so a contract change here is a compile error, never a silently
                // stale hand-written literal.
                Respond(ReadMethod(line) == "read_hardware_config"
                    ? Success(WorkerJson.SerializePayload(IoMapHardwareConfig(
                        ReadBoolField(line, "includeIoDetails") == true,
                        ReadBoolField(line, "includeTagMatches") == true,
                        ReadField(line, "deviceName"),
                        ReadField(line, "plcName"))))
                    : $$"""{"success":false,"error":"expected read_hardware_config, got '{{ReadMethod(line)}}'"}""");
                break;

            case "network-io-map-malformed":
                // The worker reports SUCCESS but the ioDetails payload carries an EXPLICIT null
                // addresses collection, which CLR initialization can never produce. The declared
                // contract must reject it as protocol_error rather than forwarding it.
                Respond(ReadMethod(line) == "read_hardware_config"
                    ? Success(WorkerJson.SerializePayload(IoMapMalformedHardwareConfig()))
                    : $$"""{"success":false,"error":"expected read_hardware_config, got '{{ReadMethod(line)}}'"}""");
                break;

            case "project-enumeration-completeness":
                Respond(ReadMethod(line) == "read_hardware_config"
                    ? Success(WorkerJson.SerializePayload(ProjectCompletenessHardware()))
                    : $$"""{"success":false,"error":"unexpected project completeness method '{{ReadMethod(line)}}'"}""");
                break;
            case "hardware-pagination":
                // The host owns cursor authentication, binding, and public projection. This scenario
                // deliberately mirrors only the internal typed candidate seam: duplicate device names,
                // stable nested-group diagnostics, and device-first/subnet-second offsets make every
                // public reconstruction assertion independent of the production worker implementation.
                Respond(ReadMethod(line) == "read_hardware_page_candidates"
                    ? HardwarePaginationResponse(line, projectScenario)
                    : ReadMethod(line) == "read_hardware_config"
                        ? Success(WorkerJson.SerializePayload(HardwarePaginationUnpaged(projectScenario)))
                        : $$"""{"success":false,"error":"expected read_hardware_page_candidates or read_hardware_config, got '{{ReadMethod(line)}}'"}""");
                break;
            case "hardware-pagination-missing-identity":
                Respond(HardwarePaginationResponse(line, projectScenario), includeSessionIdentity: false);
                break;
            case "hardware-pagination-payload-only-identity":
                Respond(
                    """{"success":true,"payload":"{\"sessionIdentity\":{\"workerSessionId\":\"payload-only\",\"sessionGeneration\":99,\"portalProcessId\":999,\"projectPath\":\"payload-only\"}}"}""",
                    includeSessionIdentity: false);
                break;
            case "hardware-pagination-missing-continuation-identity":
                Respond(
                    HardwarePaginationResponse(line, projectScenario),
                    includeSessionIdentity:
                        NextHardwarePaginationScenarioCall("hardware-pagination-missing-continuation-identity") == 1);
                break;
            case "hardware-pagination-binding-conflict":
                if (NextHardwarePaginationScenarioCall("hardware-pagination-binding-conflict") == 1)
                {
                    Respond(HardwarePaginationResponse(line, projectScenario));
                }
                else
                {
                    Respond(
                        JsonSerializer.Serialize(BindingConflict("continuation identity rejected")),
                        includeSessionIdentity: false);
                }
                break;
            case "hardware-pagination-malformed-offset":
            case "hardware-pagination-incoherent-counts":
                Respond(ReadMethod(line) == "read_hardware_page_candidates"
                    ? HardwarePaginationResponse(line, projectScenario)
                    : $$"""{"success":false,"error":"expected read_hardware_page_candidates, got '{{ReadMethod(line)}}'"}""");
                break;
            case "hardware-pagination-wrong-payload":
                Respond(ReadMethod(line) == "read_hardware_page_candidates"
                    ? """{"success":true,"payload":"{\"privateLocator\":\"not-public\"}"}"""
                    : $$"""{"success":false,"error":"expected read_hardware_page_candidates, got '{{ReadMethod(line)}}'"}""");
                break;
            case "hardware-pagination-snapshot-drift":
            case "hardware-pagination-out-of-range":
                Respond(HardwarePaginationDerivedContinuationResponse(line, projectScenario));
                break;
            case "hardware-pagination-identity-drift":
                IdentityDrift = NextHardwarePaginationScenarioCall("hardware-pagination-identity-drift") > 1;
                Respond(HardwarePaginationResponse(line, projectScenario));
                IdentityDrift = false;
                break;
            case "hardware-pagination-trimming":
            case "hardware-pagination-telemetry":
                Respond(ReadMethod(line) == "read_hardware_page_candidates"
                    ? HardwarePaginationResponse(line, projectScenario)
                    : $$"""{"success":false,"error":"expected read_hardware_page_candidates, got '{{ReadMethod(line)}}'"}""");
                break;
            case "network-unresolvable-target":
                // A contract-valid, empty HardwareConfigInfo: no device can ever match a
                // configure_network_device target here, so a preview against this scenario proves
                // NetworkIdentityResolver's fail-closed path issues no guarded write.
                Respond(Success(WorkerJson.SerializePayload(new HardwareConfigInfo
                    { DiscoveryEvidence = new() { Scope = "project", Complete = true } })));
                break;
            case "network-write-item-failure":
                // Stable hardware state followed by a failing first write: the batch RAN, so the MCP
                // call itself is not an error. The read models a resolvable "PLC_1"/"node-1" target so
                // the configure_network_device operation in the batch resolves against it before its
                // own write call
                // fails structurally like every other method in this scenario.
                Respond(ReadMethod(line) switch
                {
                    "read_hardware_config" => Success(WorkerJson.SerializePayload(SingleNodeHardwareConfig(
                        "PLC_1", "if_1", "if_1", "n1", "node-1"))),
                    _ => """{"success":false,"error":"device could not be added"}"""
                });
                break;
            case "multi-homed-network":
                // Stateful proof fixture (Task 7): one PC station ("PC_1") with two ports on separate
                // interfaces, node-plc and node-db. read_hardware_config always reports the CURRENT
                // mutable state; configure_network_device parses the forwarded nodeId and mutates only
                // the matching node object, so a later read in the same process observes the change on
                // exactly that port and byte-for-byte identical data on the other one.
                Respond(ReadMethod(line) switch
                {
                    "read_hardware_config" => Success(WorkerJson.SerializePayload(
                        MultiHomedHardwareConfig(multiHomedPlcNode, multiHomedDbNode))),
                    "configure_network_device" => ConfigureMultiHomedNode(line, multiHomedPlcNode, multiHomedDbNode),
                    _ => $$"""{"success":false,"error":"unexpected network method '{{ReadMethod(line)}}' for multi-homed-network"}"""
                });
                break;
            case "network-ambiguous-node":
                // A contract-valid HardwareConfigInfo where ONE device exposes TWO nodes reporting the
                // SAME nodeId across its two interfaces: proves NetworkIdentityResolver's ambiguous-match
                // fail-closed path (target_ambiguous, nothing written) through the actual worker/tool
                // wiring, not only the pure resolver unit tests.
                Respond(Success(WorkerJson.SerializePayload(AmbiguousNodeHardwareConfig())));
                break;
            case "invalid-network-success-payload":
                // The worker reports SUCCESS for every method, but search_equipment_catalog and
                // add_network_device both return a payload that cannot decode as their declared result
                // contract (CatalogEntryInfo[] / AddDeviceResultInfo). read_hardware_config always
                // returns a valid, contract-shaped (if empty) HardwareConfigInfo: a write batch must be
                // able to complete its mandatory current-state read even though this scenario's whole
                // point is a DIFFERENT operation's payload being rejected as protocol_error.
                Respond(ReadMethod(line) switch
                {
                    "read_hardware_config" => Success(WorkerJson.SerializePayload(new HardwareConfigInfo
                        { DiscoveryEvidence = new() { Scope = "project", Complete = true } })),
                    "search_equipment_catalog" => """{"success":true,"payload":"{\"unexpectedShape\":true}"}""",
                    "add_network_device" => """{"success":true,"payload":"{\"unexpectedShape\":true}"}""",
                    _ => $$"""{"success":false,"error":"unexpected network method '{{ReadMethod(line)}}' for invalid-network-success-payload"}"""
                });
                break;
            case "network-qualified-budget-known-observations":
            case "network-qualified-budget-long":
            case "network-qualified-budget-item":
            case "network-qualified-budget-escaped":
            case "network-qualified-budget-late-growth":
            case "network-qualified-owner-drift":
            case "network-qualified-partial":
            case "network-qualified-read":
            case "network-qualified-delete":
            case "network-qualified-legacy":
            case "network-qualified-late-subnet":
            case "network-qualified-late-node":
            case "network-qualified-late-device":
            case "network-qualified-late-owner":
            case "network-qualified-late-root":
            case "network-qualified-final-missing-discovery":
            case "network-qualified-final-binding-drift":
            case "network-qualified-final-interface-drift":
            case "network-qualified-final-repeat-interface-drift":
                var qualifiedHardware = qualifiedNetworkState ??= QualifiedHardwareFixture();
                var qualifiedDevice = qualifiedHardware.Devices[0];
                if (scenario == "network-qualified-budget-known-observations")
                {
                    qualifiedDevice.Items[0].Name = new string('o', 9800);
                    if (guardedNetworkWrites == 0)
                    {
                        var oldNode = qualifiedDevice.Items[0].Items[0].NetworkInterfaces[0].Nodes[0];
                        oldNode.IpAddress = new string('a', 7000);
                        oldNode.SubnetMask = new string('m', 7000);
                        oldNode.PnDeviceName = new string('p', 7000);
                    }
                }
                if (scenario == "network-qualified-budget-long")
                    qualifiedDevice.Items[0].Name = new string('\u4e00', 800);
                if (scenario == "network-qualified-budget-item")
                    qualifiedDevice.Items[0].Name = new string('\u4e00', 12000);
                if (scenario == "network-qualified-budget-escaped")
                    qualifiedDevice.Items[0].Name = string.Concat(Enumerable.Repeat("rack/\\\"\u4e00", 80));
                if (scenario != "network-qualified-read" && scenario != "network-qualified-partial" && scenario != "network-qualified-owner-drift" && qualifiedHardware.Subnets.Count == 0 && guardedNetworkWrites == 0)
                {
                    NetworkNodeReadSelectorBuilder.Apply(qualifiedDevice, true);
                    var connected = qualifiedDevice.Items[0].Items.SelectMany(i => i.NetworkInterfaces).SelectMany(i => i.Nodes).ToList();
                    foreach (var n in connected) n.ConnectionEvidence = new() { Complete = true, SubnetId = "subnet-1", IoSystemSubnetId = "subnet-1", IoSystemNumber = 100 };
                    var subnet = SelectableSubnet("PN/IE", "subnet-1", SubnetLifecycleContract.Ethernet, SubnetLifecycleContract.EthernetTypeIdentifier, Array.Empty<IoSystemInfo>(), Array.Empty<string>());
                    subnet.ConnectionEvidence = new() { Complete = true, Nodes = connected.Select(n => new NetworkNodeIdentityInfo {
                        DeviceName = qualifiedDevice.Name!, NodeId = n.NodeId, InterfacePath = scenario == "network-qualified-legacy" ? null : n.Selector!.InterfacePath }).ToList() };
                    qualifiedHardware.Subnets.Add(subnet);
                }
                NetworkNodeReadSelectorBuilder.Apply(qualifiedDevice, true);
                var qualifiedNodes = qualifiedDevice.Items[0].Items.SelectMany(item => item.NetworkInterfaces)
                    .SelectMany(networkInterface => networkInterface.Nodes).ToList();
                if (ReadMethod(line) == "read_hardware_config")
                {
                    if (scenario == "network-qualified-owner-drift" && ++qualifiedHardwareReadCount > 1)
                        qualifiedDevice.Items[0].Items[0].Name = "Changed owner";
                    if (guardedNetworkWrites > 0)
                    {
                        if (scenario == "network-qualified-budget-late-growth" && qualifiedDevice.Items[0].Items.Count == 2)
                        {
                            for (var budgetIndex = 0; budgetIndex < 30; budgetIndex++)
                                qualifiedDevice.Items[0].Items.Add(new()
                                {
                                    Name = new string('\u4e00', 900) + budgetIndex, PositionNumber = 40000 + budgetIndex,
                                    SelectorDiagnostics = new() { "Generic item type evidence is unavailable." },
                                    NetworkInterfaces = new() { new() { Name = "late", SelectorDiagnostics = new() { "Generic owner type evidence is unavailable." }, Nodes = new() { new()
                                    { NodeId = "E1", Name = "late", ConnectionEvidence = new() { Complete = true, SubnetId = "subnet-1" } } } } }
                                });
                            NetworkNodeReadSelectorBuilder.ApplyInventory(qualifiedHardware);
                            qualifiedHardware.Subnets[0].ConnectionEvidence!.Nodes = qualifiedDevice.Items[0].Items
                                .SelectMany(i => i.NetworkInterfaces).SelectMany(i => i.Nodes).Select(n => new NetworkNodeIdentityInfo
                                { DeviceName = qualifiedDevice.Name!, NodeId = n.NodeId, InterfacePath = n.Selector!.InterfacePath }).ToList();
                        }
                        if (scenario == "network-qualified-late-subnet") qualifiedHardware.Subnets.Add(new() { SubnetId = "", SelectorDiagnostics = new() { "Unreadable subnet identity" } });
                        if (scenario == "network-qualified-late-node") qualifiedNodes[0].NodeId = "";
                        if (scenario == "network-qualified-late-device") qualifiedHardware.Devices.Add(new());
                        if (scenario == "network-qualified-late-owner") qualifiedDevice.Items[0].Items.Add(new() { PositionNumber = null });
                        if (scenario == "network-qualified-late-root") qualifiedHardware.RootDeviceCount = null;
                        if (scenario == "network-qualified-final-missing-discovery") qualifiedHardware.DiscoveryEvidence = null;
                        if (scenario == "network-qualified-final-binding-drift") advanceSessionGeneration();
                        if (scenario == "network-qualified-final-repeat-interface-drift" && guardedNetworkWrites > 1) qualifiedDevice.Items[0].Items[0].NetworkInterfaces[0].Name = "Changed service";
                        if (scenario == "network-qualified-final-interface-drift") qualifiedDevice.Items[0].Items[0].NetworkInterfaces[0].Name = "Changed service";
                        NetworkNodeReadSelectorBuilder.ApplyInventory(qualifiedHardware);
                    }
                    Respond(Success(WorkerJson.SerializePayload(qualifiedHardware)));
                }
                else if (ReadMethod(line) == "list_network_objects")
                    Respond(Success(WorkerJson.SerializePayload(new NetworkObjectListInfo { TotalCount = 2, ReturnedCount = 2,
                        Items = qualifiedNodes.Select(node => new NetworkObjectSummaryInfo { Kind = NetworkObjectKinds.Node,
                            Selectable = node.Selectable, Selector = node.Selector,
                            Evidence = new() { NodeName = node.Name, Name = node.Name } }).ToList() })));
                else if (ReadMethod(line) == "inspect_network_object")
                {
                    var selectedTarget = JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Envelope)!.NetworkObjectTarget!;
                    var selectedOwner = NetworkInterfacePathMatcher.Match(qualifiedDevice.Items, selectedTarget.InterfacePath!,
                        item => item.Items, item => item.Name, item => item.PositionNumber, item => item.TypeIdentifier);
                    var selectedInterface = selectedOwner.Success ? selectedOwner.Item!.NetworkInterfaces.Single() : null;
                    var selectedNode = selectedInterface is null ? null : NetworkNodeReadSelectorBuilder.MatchNode(
                        selectedInterface.Nodes, selectedTarget.NodeId, selectedTarget.NodeIndex, node => node.NodeId);
                    if (!string.Equals(selectedTarget.DeviceName, qualifiedDevice.Name, StringComparison.OrdinalIgnoreCase)
                        || selectedInterface is null || selectedNode?.Success != true
                        || (selectedTarget.InterfaceName is not null && !string.Equals(selectedTarget.InterfaceName, selectedInterface.Name, StringComparison.Ordinal)))
                        Respond(WorkerJson.SerializePayload(new WorkerResponse { Success = false, FailureCategory = WorkerFailureCategories.TargetEvidenceMismatch,
                            Error = "Qualified fixture selector did not match." }));
                    else Respond(Success(WorkerJson.SerializePayload(new NetworkObjectInspectionInfo { Target = selectedNode.Item!.Selector!,
                        Evidence = new() { NodeName = selectedNode.Item.Name, Address = selectedNode.Item.IpAddress,
                            InterfaceName = selectedInterface.Name } })));
                }
                else if (ReadMethod(line) == "configure_network_device")
                    Respond(ConfigureQualifiedFixture(line, qualifiedHardware, scenario));
                else if (ReadMethod(line) == "delete_subnet")
                {
                    guardedNetworkWrites++;
                    qualifiedHardware.Subnets.Clear();
                    foreach (var n in qualifiedNodes) n.ConnectionEvidence = new() { Complete = true };
                    Respond(Success(WorkerJson.SerializePayload(new SubnetLifecycleResultInfo { SubnetId = "subnet-1", Name = "PN/IE" })));
                }
                else Respond("""{"success":false,"error":"unsupported qualified-read fixture operation"}""");
                break;

            case "list-network-objects-success":
                // One object of every kind (6 total), including one unselectable summary (no selector).
                // Dispatches on method so both methods can share this project path if needed in future.
                Respond(ReadMethod(line) == "list_network_objects"
                    ? Success(WorkerJson.SerializePayload(ListNetworkObjectsFixture()))
                    : $$"""{"success":false,"error":"expected list_network_objects, got '{{ReadMethod(line)}}'"}""");
                break;

            case "inspect-network-object-success":
                // Full set of attribute value kinds plus three special-case attribute names.
                Respond(ReadMethod(line) == "inspect_network_object"
                    ? Success(WorkerJson.SerializePayload(InspectNetworkObjectFixture()))
                    : $$"""{"success":false,"error":"expected inspect_network_object, got '{{ReadMethod(line)}}'"}""");
                break;

            case "list-network-objects-malformed":
                // Worker reports SUCCESS but the payload fails the declared result contract.
                Respond("""{"success":true,"payload":"{\"unexpectedShape\":true}"}""");
                break;

            case "inspect-network-object-malformed":
                // Worker reports SUCCESS but the payload fails the declared result contract.
                Respond("""{"success":true,"payload":"{\"unexpectedShape\":true}"}""");
                break;

            // ---------------------------------------------------------------------------
            // Phase 4: subnet lifecycle fixtures (Task 6)
            // ---------------------------------------------------------------------------

            case "network-guarded-rejected-setting":
            case "network-guarded-rejected-contradiction":
            case "network-guarded-no-io-connector":
            case "network-guarded":
            case string traversalScenario when traversalScenario.StartsWith("network-guarded-traversal-", StringComparison.Ordinal):
            case string identityScenario when identityScenario.StartsWith("network-guarded-identity-", StringComparison.Ordinal):
            case "network-guarded-late-traversal":
            case "network-guarded-late-unreadable-subnet":
            case "network-guarded-late-unreadable-attribute":
            case string lateIoScenario when lateIoScenario.StartsWith("network-guarded-late-io-", StringComparison.Ordinal):
            case "network-guarded-optional-metadata":
            case "network-guarded-missing-discovery":
            case "network-guarded-incomplete":
            case "network-guarded-incomplete-node":
            case "network-guarded-incomplete-root":
            case "network-guarded-incomplete-selector":
            case "network-guarded-late-node-block":
            case "network-guarded-disconnected":
            case "network-guarded-late-block":
            case "network-guarded-partial":
            case "network-guarded-lost-node":
            case "network-guarded-postread-failure":
            case "network-guarded-unknown-result":
            case "network-guarded-io-move":
            case "network-guarded-root-drift":
            case "network-guarded-node-inspect-unavailable":
            case "network-guarded-pn-autogeneration":
            case "network-guarded-pn-readonly":
                guardedNetworkState ??= ConnectionEvidenceHardwareConfig(scenario is "network-guarded-incomplete" or "network-guarded-incomplete-selector");
                if (scenario is "network-guarded-rejected-setting" or "network-guarded-rejected-contradiction"
                    && guardedNetworkState.Subnets.All(subnet => subnet.SubnetId != "590-3"))
                {
                    var profibus = SelectableSubnet("PROFIBUS_1", "590-3", SubnetLifecycleContract.Profibus,
                        SubnetLifecycleContract.ProfibusTypeIdentifier, Array.Empty<IoSystemInfo>(), Array.Empty<string>());
                    profibus.ConnectionEvidence = new() { Complete = true };
                    guardedNetworkState.Subnets.Add(profibus);
                }
                if (scenario == "network-guarded-identity-device" && guardedNetworkState.Devices.Count == 2)
                    guardedNetworkState.Devices.Add(new() { Name = null });
                if (scenario == "network-guarded-identity-node" && GuardedNodes(guardedNetworkState).All(node => node.NodeId.Length > 0))
                    guardedNetworkState.Devices[0].Items[0].NetworkInterfaces[0].Nodes.Add(new()
                        { NodeId = "", SelectorDiagnostics = new() { "Node identity is unreadable." } });
                if (scenario == "network-guarded-identity-subnet" && guardedNetworkState.Subnets.Count == 1)
                    guardedNetworkState.Subnets.Add(new() { Name = "Other", SubnetId = "", NetworkType = "Ethernet",
                        SelectorDiagnostics = new() { "Subnet identity is unreadable." } });
                if (scenario == "network-guarded-identity-subnet-name") guardedNetworkState.Subnets[0].Name = "";
                if (scenario == "network-guarded-identity-io" && guardedNetworkState.Subnets[0].IoSystems.Count == 0)
                {
                    guardedNetworkState.Subnets[0].IoSystems.Add(SelectableIoSystem("subnet-1", "IO", 1, "PLC_Grouped"));
                    guardedNetworkState.Subnets[0].IoSystems.Add(new() { Number = null,
                        SelectorDiagnostics = new() { "IO system number is unreadable." } });
                }
                if (scenario.StartsWith("network-guarded-traversal-", StringComparison.Ordinal))
                {
                    var stage = scenario["network-guarded-traversal-".Length..];
                    guardedNetworkState.DiscoveryEvidence = new() { Scope = stage == "deviceSelection" ? "device" : "project", Complete = false,
                        Failures = new() { new() { Stage = stage, Message = "Synthetic traversal failure: " + stage } } };
                }
                if (scenario == "network-guarded-missing-discovery") guardedNetworkState.DiscoveryEvidence = null;
                if (scenario == "network-guarded-optional-metadata" && guardedNetworkState.Messages.Count == 0)
                {
                    guardedNetworkState.Messages.Add("Optional TypeIdentifier metadata is unavailable.");
                    guardedNetworkState.Devices[0].Items[0].TypeIdentifier = null;
                    guardedNetworkState.Devices[0].Items[0].Selectable = false;
                    guardedNetworkState.Devices[0].Items[0].Selector = null;
                    guardedNetworkState.Devices[0].Items[0].SelectorDiagnostics.Add("Optional TypeIdentifier metadata is unavailable.");
                    var optionalInterface = guardedNetworkState.Devices[0].Items[0].NetworkInterfaces[0];
                    optionalInterface.Selectable = false;
                    optionalInterface.Selector = null;
                    optionalInterface.SelectorDiagnostics.Add("Optional owner TypeIdentifier metadata is unavailable.");
                }
                if (scenario == "network-guarded-incomplete-node")
                    GuardedNodes(guardedNetworkState).First().ConnectionEvidence = new() { Complete = false,
                        Messages = new() { "Could not read connected subnet identity: unavailable", "Could not read node 'Same display name' IO system: unavailable" } };
                if (scenario == "network-guarded-incomplete-root" && guardedNetworkState.Messages.Count == 0)
                {
                    guardedNetworkState.RootDeviceCount = null;
                    guardedNetworkState.Messages.Add("Could not read root device count: unavailable.");
                }
                if (scenario == "network-guarded-incomplete-selector" && guardedNetworkState.Subnets[0].SelectorDiagnostics.Count == 0)
                {
                    guardedNetworkState.Subnets[0].Selectable = false;
                    guardedNetworkState.Subnets[0].Selector = null;
                    guardedNetworkState.Subnets[0].SubnetId = string.Empty;
                    guardedNetworkState.Subnets[0].SelectorDiagnostics.Add("Subnet selector identity was ambiguous.");
                }
                if ((scenario is "network-guarded-partial" or "network-guarded-io-move" or "network-guarded-no-io-connector" || scenario.StartsWith("network-guarded-late-io-", StringComparison.Ordinal)) && guardedNetworkWrites == 0 && guardedNetworkState.Subnets[0].IoSystems.Count == 0)
                    guardedNetworkState.Subnets[0].IoSystems.Add(SelectableIoSystem("subnet-1", "IO", 1, "PLC_Grouped"));
                if (scenario == "network-guarded-io-move" && guardedNetworkState.Subnets.Count == 1)
                {
                    var other = SelectableSubnet("Other", "subnet-2", SubnetLifecycleContract.Ethernet, SubnetLifecycleContract.EthernetTypeIdentifier, Array.Empty<IoSystemInfo>(), Array.Empty<string>());
                    other.ConnectionEvidence = new() { Complete = true };
                    guardedNetworkState.Subnets.Add(other);
                }
                if (scenario == "network-guarded-disconnected" && guardedNetworkWrites == 0)
                {
                    foreach (var node in GuardedNodes(guardedNetworkState)) node.ConnectionEvidence = new() { Complete = true };
                    guardedNetworkState.Subnets[0].ConnectionEvidence!.Nodes.Clear();
                }
                Respond(HandleGuardedNetwork(line, guardedNetworkState, scenario));
                break;

            case "network-subnet-lifecycle":
                // The main stateful scenario: normal create/update/delete round trips, canonical
                // text/structuredContent equality, minimal-result shape, and audit bind against this key.
                Respond(ReadMethod(line) switch
                {
                    "read_hardware_config" => Success(WorkerJson.SerializePayload(SubnetLifecycleHardwareConfig(subnetLifecycleState))),
                    "inspect_network_object" => InspectSubnetLifecycle(line, subnetLifecycleState),
                    "create_subnet" or "update_subnet" or "delete_subnet" =>
                        DispatchSubnetLifecycleWrite(line, subnetLifecycleState),
                    _ => $$"""{"success":false,"error":"unexpected method '{{ReadMethod(line)}}' for network-subnet-lifecycle"}"""
                });
                break;

            case "network-subnet-lifecycle-alt-path":
                // Same shared mutable state as "network-subnet-lifecycle", reached through a
                // DIFFERENT scenario key: a write naming this key while bound to the main one is
                // rejected purely because the project path differs, never because the resolved target
                // differs (both keys read the exact same underlying list).
                Respond(ReadMethod(line) switch
                {
                    "read_hardware_config" => Success(WorkerJson.SerializePayload(SubnetLifecycleHardwareConfig(subnetLifecycleState))),
                    "inspect_network_object" => InspectSubnetLifecycle(line, subnetLifecycleState),
                    "create_subnet" or "update_subnet" or "delete_subnet" =>
                        DispatchSubnetLifecycleWrite(line, subnetLifecycleState),
                    _ => $$"""{"success":false,"error":"unexpected method '{{ReadMethod(line)}}' for network-subnet-lifecycle-alt-path"}"""
                });
                break;

            case "network-subnet-lifecycle-malformed-success":
                // The worker reports SUCCESS for every subnet write, but the payload carries an extra
                // unmapped member (device-detail/relationship-style free text) alongside the four
                // declared SubnetLifecycleResultInfo fields. The strict decode contract
                // (JsonUnmappedMemberHandling.Disallow) must reject this as protocol_error rather than
                // publish the extra text - proving the protocol never grows relationship/device-detail
                // wording even if a worker ever sent it.
                Respond(ReadMethod(line) switch
                {
                    "read_hardware_config" => Success(WorkerJson.SerializePayload(SubnetLifecycleHardwareConfig(subnetLifecycleState))),
                    "inspect_network_object" => InspectSubnetLifecycle(line, subnetLifecycleState),
                    "create_subnet" or "update_subnet" or "delete_subnet" =>
                        $$"""{"success":true,"payload":"{\"subnetId\":\"subnet-malformed-1\",\"name\":\"Malformed\",\"relationshipSummary\":\"connected to 2 devices\"}"}""",
                    _ => $$"""{"success":false,"error":"unexpected method '{{ReadMethod(line)}}' for network-subnet-lifecycle-malformed-success"}"""
                });
                break;

            case "network-subnet-lifecycle-postcondition-failed":
                // Every subnet write reports the worker's own postcondition_failed outcome (the
                // transaction committed but post-read verification did not match) rather than any
                // success wording - modelled exactly like the existing block create/update
                // postcondition_failed scenarios above.
                Respond(ReadMethod(line) switch
                {
                    "read_hardware_config" => Success(WorkerJson.SerializePayload(SubnetLifecycleHardwareConfig(subnetLifecycleState))),
                    "inspect_network_object" => InspectSubnetLifecycle(line, subnetLifecycleState),
                    "create_subnet" or "update_subnet" or "delete_subnet" =>
                        $$"""{"success":false,"failureCategory":"postcondition_failed","error":"subnet lifecycle verification failed on attempt {{seq}}","warnings":["Project state may have changed; inspect the project before retrying."]}""",
                    _ => $$"""{"success":false,"error":"unexpected method '{{ReadMethod(line)}}' for network-subnet-lifecycle-postcondition-failed"}"""
                });
                break;

            case "network-subnet-lifecycle-second-item-failure":
                // The FIRST subnet write in a batch against this key succeeds and mutates the shared
                // state exactly like the main scenario; the SECOND and every later one fails
                // structurally. Proves a later failure stops the batch while the earlier success stays
                // applied (no batch-wide rollback) and later items are skipped.
                Respond(ReadMethod(line) switch
                {
                    "read_hardware_config" => Success(WorkerJson.SerializePayload(SubnetLifecycleHardwareConfig(subnetLifecycleState))),
                    "inspect_network_object" => InspectSubnetLifecycle(line, subnetLifecycleState),
                    "create_subnet" or "update_subnet" or "delete_subnet" =>
                        HandleSecondItemFailureWrite(line, subnetLifecycleState),
                    _ => $$"""{"success":false,"error":"unexpected method '{{ReadMethod(line)}}' for network-subnet-lifecycle-second-item-failure"}"""
                });
                break;

            case "list-network-objects-large":
                using (var request = JsonDocument.Parse(line))
                {
                    var cursor = request.RootElement.TryGetProperty("networkObjectCursor", out var token) ? token.GetString() : null;
                    Respond(ReadMethod(line) != "list_network_objects"
                        ? $$"""{"success":false,"error":"expected list_network_objects, got '{{ReadMethod(line)}}'"}"""
                        : cursor is null or "large-list-page-2"
                            ? Success(WorkerJson.SerializePayload(LargeListNetworkObjectsFixture(cursor is not null)))
                            : """{"success":false,"failureCategory":"invalid_cursor","error":"expected original worker cursor"}""");
                }
                break;

        }
    }
}
