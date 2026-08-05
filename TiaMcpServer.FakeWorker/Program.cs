using System.Text.Json;
using TiaMcpServer.Contracts;

// Scripted stand-in for TiaMcpServer.OpennessWorker used by IPC integration tests.
// Mirrors the real worker's request loop: one JSON line in, one JSON line out, until
// stdin closes. The test encodes the scenario in the request's projectPath field.
var seq = 0;

// Process-local, mutable hardware state for the "multi-homed-network" scenario (see below): a
// single PC station exposing two ports on separate interfaces. Declared once per FakeWorker
// process so a configure_network_device call mutates it and a later read_hardware_config call in
// the SAME process observes the mutation - proving a real read -> select -> preview -> apply ->
// read round trip, not just a static fixture.
var multiHomedPlcNode = new MultiHomedNode { Name = "PLC port", NodeId = "node-plc", IpAddress = "192.168.0.20" };
var multiHomedDbNode = new MultiHomedNode { Name = "Database port", NodeId = "node-db", IpAddress = "10.20.30.40" };

string? line;
while ((line = Console.In.ReadLine()) is not null)
{
    seq++;
    string? scenario = null;
    try
    {
        using var doc = JsonDocument.Parse(line);
        if (doc.RootElement.TryGetProperty("projectPath", out var p) && p.ValueKind == JsonValueKind.String)
        {
            scenario = p.GetString();
        }
        else if (doc.RootElement.TryGetProperty("projectDirectory", out var d) && d.ValueKind == JsonValueKind.String)
        {
            // create_project carries no projectPath; its scenario key is the target directory so
            // create-specific IPC tests can drive the fake worker just like path-keyed scenarios.
            scenario = d.GetString();
        }
    }
    catch (JsonException)
    {
        scenario = "malformed-request";
    }

    switch (scenario)
    {
        case "ok":
            // seq proves whether two requests hit the same process (2.1 reuse/restart tests).
            Respond($$"""{"success":true,"payload":"{\"seq\":{{seq}}}"}""");
            break;
        case "ok-with-resolved-path":
            Respond("""{"success":true,"payload":"{}","resolvedProjectPath":"C:\\resolved\\Ground.ap21"}""");
            break;
        case "open-resolved-differs":
            // Worker reports a resolved project path that differs from the caller-supplied path,
            // proving open binds the worker's ground-truth path, never the caller's argument.
            Respond("""{"success":true,"payload":"{}","resolvedProjectPath":"C:\\worker\\Ground.ap21"}""");
            break;
        case "create-resolved-differs":
            // Keyed by projectDirectory (create sends no projectPath). Reports a resolved path
            // matching neither the target directory nor the project name, proving create binds
            // the worker's ground-truth path, never the caller's create arguments.
            Respond("""{"success":true,"payload":"{}","resolvedProjectPath":"C:\\worker\\Created.ap21"}""");
            break;
        case "C:\\open\\Line.ap21":
            // A normal open: the worker reports the SAME path it was asked to open. Both the
            // open_project call and the follow-up get_project_status call resolve here, so a
            // full open preview/apply round trip succeeds and the session binds to this path.
            Respond("""{"success":true,"payload":"{\"isOpen\":true}","resolvedProjectPath":"C:\\open\\Line.ap21"}""");
            break;
        case "C:\\bound\\Session.ap21":
            // Used by the "already bound but worker reports a different project" test: the
            // session is pre-bound to this literal path (see IsSameProject/TryResolve), so a
            // request without an explicit projectPath forwards this exact string as the
            // scenario key. Reports a DIFFERENT resolvedProjectPath to simulate divergence.
            Respond("""{"success":true,"payload":"{}","resolvedProjectPath":"C:\\actual\\Other.ap21"}""");
            break;
        case "C:\\stable\\Project.ap21":
            // Used by the "already bound, worker reports the SAME project" test: reports its
            // own scenario key back as resolvedProjectPath - no divergence, no warning expected.
            Respond("""{"success":true,"payload":"{}","resolvedProjectPath":"C:\\stable\\Project.ap21"}""");
            break;
        case "C:\\equivalent\\Project.ap21":
            // Used by the "equivalent but differently-spelled path" divergence test (Finding 2):
            // reports the identical project via forward slashes instead of back slashes. A raw
            // string.Equals would misclassify this as divergence; the canonicalized comparison
            // (ProjectSessionBinding.IsBoundTo) must not.
            Respond("""{"success":true,"payload":"{}","resolvedProjectPath":"C:/equivalent/Project.ap21"}""");
            break;
        case "ok-with-warnings":
            Respond("""{"success":true,"payload":"{\"hello\":true}","warnings":["Skipping device 'X' while reading hardware configuration: access denied.","Skipping subnet 'Y' while reading hardware configuration: not supported."]}""");
            break;
        case "ok-with-stderr":
            // Stderr between/during requests is host-log-only now; it must NOT surface as warnings.
            Console.Error.WriteLine("orphan stderr line: attach diagnostics");
            Console.Error.Flush();
            Respond("""{"success":true,"payload":"{\"hello\":true}"}""");
            break;
        case "error-prefix-payload":
            Respond("""{"success":true,"payload":"Error: literal payload text, not a failure"}""");
            break;
        case "worker-error":
            Respond("""{"success":false,"error":"boom"}""");
            break;
        case "worker-error-with-category":
            // Proves OpennessWorkerClient.InvokeWorkerAsync preserves an approved
            // worker-reported category instead of overwriting it with worker_operation_failed.
            Respond("""{"success":false,"error":"invalid value","failureCategory":"validation_error"}""");
            break;
        case "worker-error-with-target-not-found-category":
            // Isolates the target_not_found approved-category contract without changing
            // the long-standing validation_error behavior of the shared scenario above.
            Respond("""{"success":false,"error":"target not found","failureCategory":"target_not_found"}""");
            break;
        case "update-block-postcondition-failed":
            Respond($$"""{"success":false,"failureCategory":"postcondition_failed","error":"block update verification failed on attempt {{seq}}","warnings":["Project state may have changed; inspect the project before retrying."]}""");
            break;
        case "create-block-postcondition-failed":
            Respond($$"""{"success":false,"failureCategory":"postcondition_failed","error":"block creation verification failed on attempt {{seq}}","warnings":["Project state may have changed; inspect the project before retrying."]}""");
            break;
        case "malformed":
            Console.Out.WriteLine("this is not json");
            Console.Out.Flush();
            break;
        case "null-response":
            Console.Out.WriteLine("null");
            Console.Out.Flush();
            break;
        case "crash":
            Console.Error.WriteLine("worker crashed during attach");
            Console.Error.Flush();
            return;
        case "hang":
            Thread.Sleep(Timeout.Infinite);
            break;
        case "echo":
            // Returns the received request verbatim so tests can assert which fields survived
            // the BatchOperationRequest -> WorkerRequest hop.
            Respond(JsonSerializer.Serialize(new { success = true, payload = line }));
            break;
        case "network-read-warnings":
            // A contract-valid hardware payload carried alongside worker warnings, so the network
            // read path can be proven to copy warnings onto the item it decoded successfully.
            Respond("""{"success":true,"payload":"{\"devices\":[],\"subnets\":[],\"messages\":[]}","warnings":["Skipping device 'X' while reading hardware configuration: access denied.","Skipping subnet 'Y' while reading hardware configuration: not supported."]}""");
            break;
        case "network-roundtrip":
            Respond(ReadMethod(line) switch
            {
                // The request still advances seq, but its safety-bound state must remain stable
                // between preview and apply; write responses below expose the request ordering.
                // Both read payloads must satisfy their declared Phase 2 result contracts
                // (HardwareConfigInfo / CatalogEntryInfo[]); an unmapped member here would be
                // rejected as protocol_error instead of decoding. The hardware payload models a
                // PLC plus a multi-homed PC station so node, subnet and IO-system identities are
                // observable end to end.
                "read_hardware_config" => Success(HardwareConfigPayload()),
                "search_equipment_catalog" => """{"success":true,"payload":"[{\"typeName\":\"TEST\",\"typeIdentifier\":\"OrderNumber:TEST\"}]"}""",
                // The write payloads must satisfy AddDeviceResultInfo / ConfigureNetworkDeviceResultInfo
                // too. Their free-text members carry seq so request ordering stays observable
                // without smuggling an unmapped member past the declared contract.
                "add_network_device" => $$"""{"success":true,"payload":"{\"deviceName\":\"PLC_1\",\"rootItemName\":\"PLC_1\",\"typeIdentifier\":\"OrderNumber:TEST\",\"warnings\":[\"seq:{{seq}}\"]}"}""",
                "configure_network_device" => $$"""{"success":true,"payload":"{\"deviceName\":\"PLC_1\",\"appliedSettings\":{\"ipAddress\":\"192.168.0.10\"},\"skippedSettings\":{},\"messages\":[\"seq:{{seq}}\"]}"}""",
                _ => $$"""{"success":false,"error":"unexpected network method '{{ReadMethod(line)}}'"}"""
            });
            break;
        case "network-state-seq":
            // A contract-valid HardwareConfigInfo that reports the request sequence in its own
            // messages array, so a test can count how many worker requests a preview issued
            // without the payload failing its declared contract. Also resolvable: it models a
            // "PLC_2" device with a "node-1" node, so a configure_network_device target in the
            // same batch can be resolved by NetworkIdentityResolver against this same read.
            Respond(Success(ToCamelCaseJson(SingleNodeHardwareConfig(
                "PLC_2", "if_1", "if_1", "n1", "node-1", messages: new[] { $"seq:{seq}" }))));
            break;
        case "network-unresolvable-target":
            // A contract-valid, empty HardwareConfigInfo: no device can ever match a
            // configure_network_device target here, so a preview against this scenario proves
            // NetworkIdentityResolver's fail-closed path issues no safety token.
            Respond("""{"success":true,"payload":"{\"devices\":[],\"subnets\":[],\"messages\":[]}"}""");
            break;
        case "network-write-item-failure":
            // Stable hardware state (so preview/apply token binding holds) followed by a failing
            // first write: the batch RAN, so the MCP call itself is not an error. The read models a
            // resolvable "PLC_1"/"node-1" target so the configure_network_device operation in the
            // batch can resolve against it at both preview and apply, before its own write call
            // fails structurally like every other method in this scenario.
            Respond(ReadMethod(line) switch
            {
                "read_hardware_config" => Success(ToCamelCaseJson(SingleNodeHardwareConfig(
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
                "read_hardware_config" => Success(ToCamelCaseJson(
                    MultiHomedHardwareConfig(multiHomedPlcNode, multiHomedDbNode))),
                "configure_network_device" => ConfigureMultiHomedNode(line, multiHomedPlcNode, multiHomedDbNode),
                _ => $$"""{"success":false,"error":"unexpected network method '{{ReadMethod(line)}}' for multi-homed-network"}"""
            });
            break;
        case "network-ambiguous-node":
            // A contract-valid HardwareConfigInfo where ONE device exposes TWO nodes reporting the
            // SAME nodeId across its two interfaces: proves NetworkIdentityResolver's ambiguous-match
            // fail-closed path (postcondition_failed, no token issued) through the actual worker/tool
            // wiring, not only the pure resolver unit tests.
            Respond(Success(ToCamelCaseJson(AmbiguousNodeHardwareConfig())));
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
                "read_hardware_config" => Success(ToCamelCaseJson(new HardwareConfigInfo())),
                "search_equipment_catalog" => """{"success":true,"payload":"{\"unexpectedShape\":true}"}""",
                "add_network_device" => """{"success":true,"payload":"{\"unexpectedShape\":true}"}""",
                _ => $$"""{"success":false,"error":"unexpected network method '{{ReadMethod(line)}}' for invalid-network-success-payload"}"""
            });
            break;
        case "type-content-roundtrip":
            // Used by TypeOperationFakeWorkerTests to drive a full get_type_content /
            // update_type_content round trip. A single scenario key must serve both methods:
            // update_type_content's preview AND apply each also issue a get_type_content
            // current-state read against the SAME projectPath, so the method (not the
            // scenario key) is what has to pick the response.
            Respond(ReadMethod(line) switch
            {
                "get_type_content" => """{"success":true,"payload":"TYPE AnalogInputSettings STRUCT Value : Real; END_STRUCT END_TYPE"}""",
                "update_type_content" => """{"success":true,"payload":"{}"}""",
                _ => $$"""{"success":false,"error":"expected get_type_content or update_type_content, got '{{ReadMethod(line)}}'"}"""
            });
            break;
        case "block-source-roundtrip":
            // Used by BlockCurrentStateReadTests to drive a full format=source preview/apply round
            // trip for update_block_logic. Dispatches on method AND format: the current-state read
            // the safety token binds to must carry the write item's own format, so a read that
            // fell back to xml is answered with a failure naming what it sent rather than a
            // payload, and the round trip fails loudly instead of binding the wrong artifact.
            Respond((ReadMethod(line), ReadField(line, "format")) switch
            {
                ("get_block_content", "source") => """{"success":true,"payload":"DATA_BLOCK \"Recipe\"\r\nSTRUCT\r\nEND_STRUCT;\r\nBEGIN\r\nEND_DATA_BLOCK\r\n"}""",
                ("update_block_logic", "source") => """{"success":true,"payload":"{}"}""",
                var other => $$"""{"success":false,"error":"expected format 'source' for both methods, got method '{{other.Item1}}' with format '{{other.Item2}}'"}"""
            });
            break;
        case "direct-status-only":
            // Used to prove the direct get_project_status MCP tool routes through the
            // GetProjectStatusAsync operation only, never the internal lifecycle probe.
            Respond(ReadMethod(line) == "get_project_status"
                ? """{"success":true,"payload":"{\"isOpen\":true}"}"""
                : $$"""{"success":false,"error":"expected get_project_status, got '{{ReadMethod(line)}}'"}""");
            break;
        case "status-no-project":
            // Simulates the real worker's GetStatusReadOnly when nothing is open and no path
            // was requested: isOpen:false, no resolvedProjectPath - nothing was opened.
            Respond("""{"success":true,"payload":"{\"isOpen\":false}"}""");
            break;
        case "lifecycle-probe-only":
            // Guards against a regression where a save/save-as/archive/close current-state
            // read reverts to the direct status operation: fails ONLY when the request used
            // get_project_status; every other operation (the probe itself, or the tool's own
            // write call that follows) succeeds normally so the full preview/apply round trip
            // can complete. save_project_as additionally needs a resolvedProjectPath so the
            // rebind bind succeeds after the write.
            Respond(ReadMethod(line) switch
            {
                "get_project_status" => """{"success":false,"error":"current-state read must use probe_project_status_for_lifecycle, not get_project_status"}""",
                "save_project_as" => """{"success":true,"payload":"{\"isOpen\":true}","resolvedProjectPath":"C:\\lifecycle\\Copy.ap21"}""",
                _ => """{"success":true,"payload":"{\"isOpen\":true}"}"""
            });
            break;
        case "save-as-uncertain-state":
            // Simulates the real worker's postcondition_failed when save_project_as saved a copy
            // but could not confirm the active project is that copy: a failure carrying the
            // uncertain-state warning. The host must surface it and never bind the session.
            Respond("""{"success":false,"failureCategory":"postcondition_failed","error":"could not confirm the copied project path","warnings":["Project state may have changed; inspect the open project before retrying."]}""");
            break;
        case "C:\\bound\\FailingSave.ap21":
            // A bound-path scenario whose save_project_as call fails, proving a failed rebinding
            // save-as leaves the pre-existing session binding untouched (no partial rebind).
            Respond("""{"success":false,"failureCategory":"worker_operation_failed","error":"save failed"}""");
            break;
        case "C:\\Projects\\SimpleProject\\SimpleProject.ap21":
            // Used by the archive-directory-guard preview test: every request (including the
            // probe_project_status_for_lifecycle current-state read) reports itself as the
            // resolvedProjectPath, so the host-side ArchiveDirectoryGuard check has a concrete
            // project path to classify the caller's archiveDirectory against.
            Respond("""{"success":true,"payload":"{\"isOpen\":true}","resolvedProjectPath":"C:\\Projects\\SimpleProject\\SimpleProject.ap21"}""");
            break;

        // ---------------------------------------------------------------------------
        // Phase 3: list_network_objects and inspect_network_object fixtures
        // ---------------------------------------------------------------------------

        case "list-network-objects-success":
            // One object of every kind (6 total), including one unselectable summary (no selector).
            // Dispatches on method so both methods can share this project path if needed in future.
            Respond(ReadMethod(line) == "list_network_objects"
                ? Success(ToCamelCaseJson(ListNetworkObjectsFixture()))
                : $$"""{"success":false,"error":"expected list_network_objects, got '{{ReadMethod(line)}}'"}""");
            break;

        case "inspect-network-object-success":
            // Full set of attribute value kinds plus three special-case attribute names.
            Respond(ReadMethod(line) == "inspect_network_object"
                ? Success(ToCamelCaseJson(InspectNetworkObjectFixture()))
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

        case "list-network-objects-large":
            // Deterministic large-list scenario for budget tests: 20 items (all node kind), a
            // scripted nextCursor, and a totalCount that matches. No real pagination logic — the
            // same fixture is returned on every call; the cursor is for binding tests only.
            Respond(ReadMethod(line) == "list_network_objects"
                ? Success(ToCamelCaseJson(LargeListNetworkObjectsFixture()))
                : $$"""{"success":false,"error":"expected list_network_objects, got '{{ReadMethod(line)}}'"}""");
            break;

        default:
            Respond($$"""{"success":false,"error":"unknown scenario '{{scenario}}'"}""");
            break;
    }
}

void Respond(string json)
{
    Console.Out.WriteLine(json);
    Console.Out.Flush();
}

// Wraps a payload document as a successful worker response. Serializing beats hand-escaping once a
// payload is more than a few members: the escaping is what a hand-written literal gets wrong, and a
// mis-escaped payload would fail the strict Network contract for the wrong reason.
string Success(string payload) => JsonSerializer.Serialize(new { success = true, payload });

// A complete HardwareConfigInfo: every collection is present, and members that are genuinely
// unset are explicit nulls rather than omitted, so the payload exercises the strict registry the
// way a real worker read does.
string HardwareConfigPayload() => ToCamelCaseJson(RoundTripHardwareConfig());

string? ReadMethod(string requestLine) => ReadField(requestLine, "method");

string? ReadField(string requestLine, string propertyName)
{
    try
    {
        using var doc = JsonDocument.Parse(requestLine);
        // ValueKind-guarded so a missing field, an explicit JSON null and a non-string all read as
        // null rather than throwing: a scenario that dispatches on an ABSENT field (format) must be
        // able to see its absence.
        return doc.RootElement.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
    catch (JsonException)
    {
        return null;
    }
}

// Renders a real Contracts DTO as camelCase JSON. Used by scenarios that must serialize through
// complete contract-shaped objects (HardwareConfigInfo, ConfigureNetworkDeviceResultInfo) rather
// than hand-maintained escaped JSON string fragments: the CLR type is the source of truth for
// which members exist, so a contract change here is a compile error, not a silently stale literal.
string ToCamelCaseJson<T>(T value)
    => JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

// Hardware fixtures are serialized from the shared Contracts DTOs and carry the same deterministic
// selectors the real worker now emits. Keeping this construction in one place means a future
// selector-contract change fails the FakeWorker build instead of silently invalidating every
// preview/apply scenario with stale hand-written JSON.
HardwareConfigInfo RoundTripHardwareConfig() => new()
{
    Devices = new List<DeviceInfo>
    {
        new()
        {
            Name = "PLC_1",
            TypeIdentifier = "OrderNumber:TEST",
            Items = new List<DeviceItemInfo>
            {
                SelectableDeviceItem(
                    "PLC_1", 0, "PROFINET interface_1", "OrderNumber:TEST", 1,
                    "PROFINET interface_1",
                    SelectableNode(
                        "PLC_1", "X1", "node-1", "Ethernet", "192.168.0.10",
                        "255.255.255.0", "plc-1", "PN/IE_1", "IO system_1")),
            },
        },
        new()
        {
            Name = "PC_System_1",
            TypeIdentifier = "OrderNumber:PC-System",
            Items = new List<DeviceItemInfo>
            {
                SelectableDeviceItem(
                    "PC_System_1", 0, "IE general_1", "OrderNumber:IE-General", 1,
                    "PROFINET interface_1",
                    SelectableNode(
                        "PC_System_1", "E1", "0", "Ethernet", "192.168.0.20",
                        "255.255.255.0", null, "PN/IE_1", null)),
                SelectableDeviceItem(
                    "PC_System_1", 1, "IE general_2", "OrderNumber:IE-General", 2,
                    "PROFINET interface_2",
                    SelectableNode(
                        "PC_System_1", "E2", "1", "Ethernet", "10.0.0.20",
                        "255.255.255.0", null, "PN/IE_2", null)),
            },
        },
    },
    Subnets = new List<SubnetInfo>
    {
        SelectableSubnet(
            "PN/IE_1", "subnet-1", "Ethernet", "Ethernet",
            new[] { SelectableIoSystem("subnet-1", "IO system_1", 100, "PLC_1") },
            new[] { "PLC_1.X1", "PC_System_1.E1" }),
        SelectableSubnet(
            "PN/IE_2", "subnet-2", "Ethernet", "Ethernet",
            Array.Empty<IoSystemInfo>(),
            new[] { "PC_System_1.E2" }),
    },
};

HardwareConfigInfo SingleNodeHardwareConfig(
    string deviceName,
    string itemName,
    string interfaceName,
    string nodeName,
    string nodeId,
    IEnumerable<string>? messages = null) => new()
{
    Devices = new List<DeviceInfo>
    {
        new()
        {
            Name = deviceName,
            TypeIdentifier = "OrderNumber:TEST",
            Items = new List<DeviceItemInfo>
            {
                SelectableDeviceItem(
                    deviceName, 0, itemName, "OrderNumber:TEST", 1, interfaceName,
                    SelectableNode(deviceName, nodeName, nodeId, "Ethernet")),
            },
        },
    },
    Messages = messages?.ToList() ?? new List<string>(),
};

HardwareConfigInfo AmbiguousNodeHardwareConfig() => new()
{
    Devices = new List<DeviceInfo>
    {
        new()
        {
            Name = "PC_1",
            TypeIdentifier = "OrderNumber:PC-System",
            Items = new List<DeviceItemInfo>
            {
                SelectableDeviceItem(
                    "PC_1", 0, "IE general_1", "OrderNumber:IE-General", 1, "if_1",
                    SelectableNode("PC_1", "Port A", "dup-node", "Ethernet", "192.168.0.20")),
                SelectableDeviceItem(
                    "PC_1", 1, "IE general_2", "OrderNumber:IE-General", 2, "if_2",
                    SelectableNode("PC_1", "Port B", "dup-node", "Ethernet", "10.20.30.40")),
            },
        },
    },
};

DeviceItemInfo SelectableDeviceItem(
    string deviceName,
    int index,
    string itemName,
    string typeIdentifier,
    int positionNumber,
    string interfaceName,
    params NodeInfo[] nodes)
{
    var path = new List<DeviceItemPathSegmentInfo>
    {
        new()
        {
            Index = index,
            Name = itemName,
            PositionNumber = positionNumber,
            TypeIdentifier = typeIdentifier,
        },
    };

    return new DeviceItemInfo
    {
        Name = itemName,
        TypeIdentifier = typeIdentifier,
        PositionNumber = positionNumber,
        Selectable = true,
        Selector = new NetworkObjectSelectorInfo
        {
            Kind = NetworkObjectKinds.DeviceItem,
            DeviceName = deviceName,
            ItemPath = path,
        },
        NetworkInterfaces = new List<NetworkInterfaceInfo>
        {
            new()
            {
                Name = interfaceName,
                Selectable = true,
                Selector = new NetworkObjectSelectorInfo
                {
                    Kind = NetworkObjectKinds.NetworkInterface,
                    DeviceName = deviceName,
                    ItemPath = path,
                    InterfaceName = interfaceName,
                },
                Nodes = nodes.ToList(),
            },
        },
    };
}

NodeInfo SelectableNode(
    string deviceName,
    string name,
    string nodeId,
    string? nodeType = null,
    string? ipAddress = null,
    string? subnetMask = null,
    string? pnDeviceName = null,
    string? subnetName = null,
    string? ioSystemName = null) => new()
{
    Name = name,
    NodeId = nodeId,
    NodeType = nodeType,
    IpAddress = ipAddress,
    SubnetMask = subnetMask,
    PnDeviceName = pnDeviceName,
    SubnetName = subnetName,
    IoSystemName = ioSystemName,
    Selectable = true,
    Selector = new NetworkObjectSelectorInfo
    {
        Kind = NetworkObjectKinds.Node,
        DeviceName = deviceName,
        NodeId = nodeId,
    },
};

SubnetInfo SelectableSubnet(
    string name,
    string subnetId,
    string? networkType,
    string? typeIdentifier,
    IEnumerable<IoSystemInfo> ioSystems,
    IEnumerable<string> connectedNodeNames) => new()
{
    Name = name,
    SubnetId = subnetId,
    NetworkType = networkType,
    TypeIdentifier = typeIdentifier,
    Selectable = true,
    Selector = new NetworkObjectSelectorInfo { Kind = NetworkObjectKinds.Subnet, SubnetId = subnetId },
    IoSystems = ioSystems.ToList(),
    ConnectedNodeNames = connectedNodeNames.ToList(),
};

IoSystemInfo SelectableIoSystem(string subnetId, string name, int number, string? controllerName) => new()
{
    Name = name,
    Number = number,
    IoControllerName = controllerName,
    Selectable = true,
    Selector = new NetworkObjectSelectorInfo
    {
        Kind = NetworkObjectKinds.IoSystem,
        SubnetId = subnetId,
        Number = number,
    },
};

// Builds the current HardwareConfigInfo for the "multi-homed-network" scenario from the live
// mutable node state, so a read after a configure_network_device call observes the mutation.
HardwareConfigInfo MultiHomedHardwareConfig(MultiHomedNode plc, MultiHomedNode db) => new()
{
    Devices = new List<DeviceInfo>
    {
        new()
        {
            Name = "PC_1",
            TypeIdentifier = "OrderNumber:PC-System",
            Items = new List<DeviceItemInfo>
            {
                SelectableDeviceItem(
                    "PC_1", 0, "IE general_1", "OrderNumber:IE-General", 1,
                    "PROFINET interface_1",
                    SelectableNode(
                        "PC_1", plc.Name, plc.NodeId, "Ethernet", plc.IpAddress, plc.SubnetMask,
                        plc.PnDeviceName, "PN/IE_1")),
                SelectableDeviceItem(
                    "PC_1", 1, "IE general_2", "OrderNumber:IE-General", 2,
                    "PROFINET interface_2",
                    SelectableNode(
                        "PC_1", db.Name, db.NodeId, "Ethernet", db.IpAddress, db.SubnetMask,
                        db.PnDeviceName, "PN/IE_2")),
            },
        },
    },
    Subnets = new List<SubnetInfo>
    {
        SelectableSubnet(
            "PN/IE_1", "subnet-plc", "Ethernet", null,
            Array.Empty<IoSystemInfo>(), new[] { $"PC_1.{plc.Name}" }),
        SelectableSubnet(
            "PN/IE_2", "subnet-db", "Ethernet", null,
            Array.Empty<IoSystemInfo>(), new[] { $"PC_1.{db.Name}" }),
    },
    Messages = new List<string>(),
};

// Parses the forwarded nodeId and mutates ONLY the matching node's live state, so the OTHER node
// stays byte-for-byte identical on a later read. Real Openness would resolve this same nodeId to a
// specific Node object before applying any of these settings; this fixture mirrors that by keying
// off the same exact identifier the host already resolved before ever sending this request.
string ConfigureMultiHomedNode(string requestLine, MultiHomedNode plc, MultiHomedNode db)
{
    var nodeId = ReadField(requestLine, "nodeId");
    var target = nodeId switch
    {
        "node-plc" => plc,
        "node-db" => db,
        _ => null,
    };

    if (target is null)
    {
        return $$"""{"success":false,"error":"multi-homed-network has no node with nodeId '{{nodeId}}'"}""";
    }

    var applied = new Dictionary<string, string>();

    var ipAddress = ReadField(requestLine, "ipAddress");
    if (ipAddress is not null)
    {
        target.IpAddress = ipAddress;
        applied["ipAddress"] = ipAddress;
    }

    var subnetMask = ReadField(requestLine, "subnetMask");
    if (subnetMask is not null)
    {
        target.SubnetMask = subnetMask;
        applied["subnetMask"] = subnetMask;
    }

    var pnDeviceName = ReadField(requestLine, "pnDeviceName");
    if (pnDeviceName is not null)
    {
        target.PnDeviceName = pnDeviceName;
        applied["pnDeviceName"] = pnDeviceName;
    }

    var result = new ConfigureNetworkDeviceResultInfo
    {
        DeviceName = "PC_1",
        AppliedSettings = applied,
        SkippedSettings = new Dictionary<string, string>(),
        Messages = new List<string> { $"configured nodeId '{nodeId}'" },
    };

    return Success(ToCamelCaseJson(result));
}

// Builds the Phase 3 list_network_objects fixture: one object of every kind (6 total), with the
// communicationConnection entry unselectable (selector is null) because its connection index cannot
// always be determined at list time. TotalCount matches Items.Count (no hidden items on this page).
NetworkObjectListInfo ListNetworkObjectsFixture() => new()
{
    Items = new List<NetworkObjectSummaryInfo>
    {
        new()
        {
            Kind = NetworkObjectKinds.DeviceItem,
            Selectable = true,
            Selector = new NetworkObjectSelectorInfo
            {
                Kind = NetworkObjectKinds.DeviceItem,
                DeviceName = "PLC_1",
                ItemPath = new List<DeviceItemPathSegmentInfo>
                {
                    new()
                    {
                        Index = 0,
                        Name = "PROFINET interface_1",
                        PositionNumber = 1,
                        TypeIdentifier = "OrderNumber:TEST",
                    },
                },
            },
            Evidence = new NetworkObjectEvidenceInfo
            {
                Name = "PROFINET interface_1",
                TypeIdentifier = "OrderNumber:TEST",
                PositionNumber = 1,
                DeviceItemPath = new List<string> { "PROFINET interface_1" },
            },
        },
        new()
        {
            Kind = NetworkObjectKinds.NetworkInterface,
            Selectable = true,
            Selector = new NetworkObjectSelectorInfo
            {
                Kind = NetworkObjectKinds.NetworkInterface,
                DeviceName = "PLC_1",
                ItemPath = new List<DeviceItemPathSegmentInfo>
                {
                    new()
                    {
                        Index = 0,
                        Name = "PROFINET interface_1",
                        PositionNumber = 1,
                        TypeIdentifier = "OrderNumber:TEST",
                    },
                },
                InterfaceName = "PROFINET interface_1",
            },
            Evidence = new NetworkObjectEvidenceInfo
            {
                Name = "PROFINET interface_1",
                DeviceItemPath = new List<string> { "PROFINET interface_1" },
                InterfaceName = "PROFINET interface_1",
            },
        },
        new()
        {
            Kind = NetworkObjectKinds.Node,
            Selectable = true,
            Selector = new NetworkObjectSelectorInfo
            {
                Kind = NetworkObjectKinds.Node,
                DeviceName = "PLC_1",
                NodeId = "node-1",
            },
            Evidence = new NetworkObjectEvidenceInfo { Name = "X1", NodeName = "X1" },
        },
        new()
        {
            Kind = NetworkObjectKinds.Subnet,
            Selectable = true,
            Selector = new NetworkObjectSelectorInfo
            {
                Kind = NetworkObjectKinds.Subnet,
                SubnetId = "subnet-1",
            },
            Evidence = new NetworkObjectEvidenceInfo { Name = "PN/IE_1", SubnetName = "PN/IE_1" },
        },
        new()
        {
            Kind = NetworkObjectKinds.IoSystem,
            Selectable = true,
            Selector = new NetworkObjectSelectorInfo
            {
                Kind = NetworkObjectKinds.IoSystem,
                SubnetId = "subnet-1",
                Number = 100,
            },
            Evidence = new NetworkObjectEvidenceInfo
            {
                Name = "IO system_1",
                SubnetName = "PN/IE_1",
                IoSystemName = "IO system_1",
            },
        },
        new()
        {
            Kind = NetworkObjectKinds.CommunicationConnection,
            Selectable = false,
            Selector = null, // connection index not always determinable at list time
            Evidence = new NetworkObjectEvidenceInfo
            {
                Name = "S7 connection_1",
                TypeIdentifier = "S7",
                ConnectionIsValid = false,
            },
            Diagnostics = new List<string>
            {
                "Connection identity could not be read; selector not available.",
            },
        },
    },
    TotalCount = 6,
    ReturnedCount = 6,
    NextCursor = null,
};

// Builds the Phase 3 inspect_network_object fixture: attributes covering the full typed value
// vocabulary. Each attribute carries source provenance, access classification, supportedTypes, and
// availability. The special names unknownAttribute, readFailed, and unrepresentable exercise the
// three non-available availability states so round-trip tests can assert all lifecycle paths.
NetworkObjectInspectionInfo InspectNetworkObjectFixture() => new()
{
    Target = new NetworkObjectSelectorInfo
    {
        Kind = NetworkObjectKinds.Node,
        DeviceName = "PLC_1",
        NodeId = "node-1",
    },
    Evidence = new NetworkObjectEvidenceInfo
    {
        Name = "X1",
        TypeIdentifier = "OrderNumber:TEST",
        PositionNumber = 1,
        Address = "192.168.0.10",
        DeviceItemPath = new List<string> { "PLC_1", "X1" },
        InterfaceName = "PROFINET interface_1",
        InterfaceType = "PROFINET",
        InterfaceOperatingMode = "IoController",
        NodeName = "X1",
        NodeType = "Ethernet",
        SubnetName = "PN/IE_1",
        NetworkType = "Ethernet",
        IoSystemName = "IO system_1",
        IoControllerName = "PLC_1",
        ConnectionIsValid = true,
        LocalEndpointName = "PLC_1.X1",
        PartnerEndpointName = "ET200SP_1.X1",
        LocalSubnetName = "PN/IE_1",
        PartnerSubnetName = "PN/IE_1",
    },
    Attributes = new List<NetworkAttributeInfo>
    {
        new()
        {
            Name = "nullAttribute",
            Source = "modeled",
            Access = "readOnly",
            SupportedTypes = new List<string>(),
            Availability = "available",
            Value = new NetworkAttributeValueInfo { Kind = "null", Value = null },
        },
        new()
        {
            Name = "stringAttribute",
            Source = "modeled",
            Access = "readOnly",
            SupportedTypes = new List<string> { "string" },
            Availability = "available",
            Value = new NetworkAttributeValueInfo { Kind = "string", Value = "192.168.0.10" },
        },
        new()
        {
            Name = "booleanAttribute",
            Source = "modeled",
            Access = "readOnly",
            SupportedTypes = new List<string> { "boolean" },
            Availability = "available",
            Value = new NetworkAttributeValueInfo { Kind = "boolean", Value = true },
        },
        new()
        {
            Name = "integerAttribute",
            Source = "modeled",
            Access = "readOnly",
            SupportedTypes = new List<string> { "integer" },
            Availability = "available",
            Value = new NetworkAttributeValueInfo { Kind = "integer", Value = 1500L },
        },
        new()
        {
            Name = "numberAttribute",
            Source = "modeled",
            Access = "readOnly",
            SupportedTypes = new List<string> { "number" },
            Availability = "available",
            Value = new NetworkAttributeValueInfo { Kind = "number", Value = 3.14 },
        },
        new()
        {
            Name = "enumAttribute",
            Source = "modeled",
            Access = "readOnly",
            SupportedTypes = new List<string> { "enum" },
            Availability = "available",
            Value = new NetworkAttributeValueInfo
            {
                Kind = "enum",
                Value = new NetworkEnumValueInfo { TypeName = "MediaType", Symbol = "Ethernet", NumericValue = 1 },
            },
        },
        new()
        {
            Name = "unknownAttribute",
            Source = null,
            Access = "unknown",
            SupportedTypes = new List<string>(),
            Availability = "unknownAttribute",
            Diagnostic = new NetworkAttributeDiagnosticInfo
            {
                Category = "unknown_attribute",
                Message = "Attribute was not recognized.",
            },
        },
        new()
        {
            Name = "readFailed",
            Source = "modeled",
            Access = "readOnly",
            SupportedTypes = new List<string>(),
            Availability = "readFailed",
            Diagnostic = new NetworkAttributeDiagnosticInfo { Category = "read_error", Message = "read failed" },
        },
        new()
        {
            Name = "unrepresentable",
            Source = "modeled",
            Access = "readOnly",
            SupportedTypes = new List<string>(),
            Availability = "unrepresentable",
            Diagnostic = new NetworkAttributeDiagnosticInfo { Category = "type_error", Message = "cannot represent value" },
        },
    },
    Messages = new List<string>(),
};

// Deterministic large-list scenario: 20 node entries with distinct nodeIds, a scripted next-page
// cursor, and a totalCount larger than Items.Count to indicate more pages exist. No real pagination
// is implemented; the cursor value is stable so budget tests can verify it is forwarded correctly.
NetworkObjectListInfo LargeListNetworkObjectsFixture()
{
    var items = Enumerable.Range(1, 20)
        .Select(i => new NetworkObjectSummaryInfo
        {
            Kind = NetworkObjectKinds.Node,
            Selectable = true,
            Selector = new NetworkObjectSelectorInfo
            {
                Kind = NetworkObjectKinds.Node,
                DeviceName = "LargeSwitch",
                NodeId = $"node-{i:D3}",
            },
            Evidence = new NetworkObjectEvidenceInfo
            {
                Name = $"Port_{i:D2}",
                NodeName = $"Port_{i:D2}",
            },
        })
        .ToList();

    return new NetworkObjectListInfo
    {
        Items = items,
        TotalCount = 100,
        ReturnedCount = items.Count,
        NextCursor = "large-list-page-2",
    };
}

/// <summary>Mutable process-local state for one node of the "multi-homed-network" scenario.</summary>
sealed class MultiHomedNode
{
    public required string Name { get; init; }

    public required string NodeId { get; init; }

    public string IpAddress { get; set; } = string.Empty;

    public string? SubnetMask { get; set; }

    public string? PnDeviceName { get; set; }
}
