using System.Text.Json;
using System.Text.Json.Nodes;
using TiaMcpServer.Contracts;

// Scripted stand-in for TiaMcpServer.OpennessWorker used by IPC integration tests.
// Mirrors the real worker's request loop: one JSON line in, one JSON line out, until
// stdin closes. The test encodes the scenario in the request's projectPath field.
var seq = 0;
var workerSessionId = Guid.NewGuid().ToString("N");
const int FakePortalProcessId = 4242;
var fakeSessionGeneration = 1L;
string? fakeProjectPath = null;
string? currentProjectPath = null;
string? currentMethod = null;
var requestJsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
};

// Process-local, mutable hardware state for the "multi-homed-network" scenario (see below): a
// single PC station exposing two ports on separate interfaces. Declared once per FakeWorker
// process so a configure_network_device call mutates it and a later read_hardware_config call in
// the SAME process observes the mutation - proving a real read -> select -> preview -> apply ->
// read round trip, not just a static fixture.
var multiHomedPlcNode = new MultiHomedNode { Name = "PLC port", NodeId = "node-plc", IpAddress = "192.168.0.20" };
var multiHomedDbNode = new MultiHomedNode { Name = "Database port", NodeId = "node-db", IpAddress = "10.20.30.40" };

// Process-local, mutable subnet state shared by every "network-subnet-lifecycle*" scenario key
// (Task 6, Phase 4): two devices that never change, and two subnets - one Ethernet, one PROFIBUS -
// each already connected to a node. Sharing this exact list across the main scenario and its
// switch variants (malformed/postcondition-failed/second-item-failure/alt-path) means a resolved
// subnet's identity is byte-for-byte identical no matter which of those keys reads it, so a
// project-path tampering test can bind a token against one key and get rejected against another
// for exactly that reason - never a coincidentally different target. Connected subnets are never
// treated as undeletable here: delete_subnet removes them unconditionally, matching production's
// "connected deletion is allowed, no dependency inventory" rule.
var subnetLifecycleState = new List<SubnetLifecycleSubnetState>
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
var subnetLifecycleNextId = 1;
var subnetLifecycleSecondFailureWriteCount = 0;
var subnetLifecycleStateDriftReadCount = 0;
var updateBlockPostconditionAttempt = 0;
var createBlockPostconditionAttempt = 0;

// Two devices that never change across any subnet lifecycle operation, modelling the stable
// "root device count" the production SubnetLifecycleService verifies after every commit.
const int SubnetLifecycleDeviceCount = 2;

string? line;
while ((line = Console.In.ReadLine()) is not null)
{
    seq++;
    string? scenario = null;
    currentProjectPath = null;
    currentMethod = null;
    WorkerSessionIdentity? currentExpectedSessionIdentity = null;
    try
    {
        using var doc = JsonDocument.Parse(line);
        if (doc.RootElement.TryGetProperty("projectPath", out var p) && p.ValueKind == JsonValueKind.String)
        {
            currentProjectPath = p.GetString();
            scenario = ScenarioKey(currentProjectPath);
        }
        else if (doc.RootElement.TryGetProperty("projectDirectory", out var d) && d.ValueKind == JsonValueKind.String)
        {
            // create_project carries no projectPath; its scenario key is the target directory so
            // create-specific IPC tests can drive the fake worker just like path-keyed scenarios.
            currentProjectPath = d.GetString();
            scenario = ScenarioKey(currentProjectPath);
        }

        currentMethod = doc.RootElement.TryGetProperty("method", out var method) && method.ValueKind == JsonValueKind.String
            ? method.GetString()
            : null;

        if (doc.RootElement.TryGetProperty(
                "expectedSessionIdentity",
                out var expectedIdentity) &&
            expectedIdentity.ValueKind == JsonValueKind.Object)
        {
            currentExpectedSessionIdentity =
                expectedIdentity.Deserialize<WorkerSessionIdentity>(requestJsonOptions);
        }
    }
    catch (JsonException)
    {
        scenario = "malformed-request";
    }

    if (string.Equals(currentMethod, "hello", StringComparison.Ordinal))
    {
        // Handshake traffic is transport setup, not an engineering request; preserve the
        // historical sequence values used by reuse/restart tests.
        seq--;
        Console.Out.WriteLine(JsonSerializer.Serialize(new WorkerResponse
        {
            Success = true,
            Payload = "{}",
            ProtocolVersion = WorkerProtocol.Version,
            Capabilities = WorkerProtocol.RequiredCapabilities.ToList()
        }));
        Console.Out.Flush();
        continue;
    }

    var identityFailure = ValidateExpectedSessionIdentity(
        currentMethod,
        currentProjectPath,
        currentExpectedSessionIdentity);
    if (identityFailure is not null)
    {
        Respond(JsonSerializer.Serialize(identityFailure), includeSessionIdentity: false);
        continue;
    }

    switch (scenario)
    {
        case "ok":
            // seq proves whether two requests hit the same process (2.1 reuse/restart tests).
            Respond($$"""{"success":true,"payload":"{\"seq\":{{seq}}}"}""");
            break;
        case "missing-session-identity":
            // A structurally valid success that violates the new binding postcondition. Used to
            // prove open/create never fall back to caller input or resolvedProjectPath when the
            // worker omits its complete worker/Portal/project identity.
            Respond("""{"success":true,"payload":"{}"}""", includeSessionIdentity: false);
            break;
        case "ok-with-resolved-path":
            // A canonical unbound status read is fixture bootstrap. The later protected save-as
            // retains this scenario's original copied-path response.
            Respond(ReadMethod(line) == "get_project_status" &&
                    currentExpectedSessionIdentity is null &&
                    currentProjectPath is not null &&
                    Path.IsPathFullyQualified(currentProjectPath)
                ? """{"success":true,"payload":"{\"isOpen\":true}"}"""
                : """{"success":true,"payload":"{}","resolvedProjectPath":"C:\\resolved\\Ground.ap21"}""");
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
            // The write fixture canonicalizes its unbound bootstrap path before sending it, while
            // the ordinary worker-error read test retains this scenario's relative path and must
            // still observe the scripted failure.
            Respond(ReadMethod(line) == "read_hardware_config" &&
                    currentExpectedSessionIdentity is null &&
                    currentProjectPath is not null &&
                    Path.IsPathFullyQualified(currentProjectPath)
                ? Success(HardwareConfigPayload())
                : """{"success":false,"error":"boom"}""");
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
            // Fixture bootstrap must not consume the protected write's attempt sequence.
            if (ReadMethod(line) == "get_project_status" && currentExpectedSessionIdentity is null)
            {
                Respond("""{"success":true,"payload":"{\"isOpen\":true}"}""");
            }
            else
            {
                updateBlockPostconditionAttempt++;
                Respond($$"""{"success":false,"failureCategory":"postcondition_failed","error":"block update verification failed on attempt {{updateBlockPostconditionAttempt}}","warnings":["Project state may have changed; inspect the project before retrying."]}""");
            }
            break;
        case "create-block-postcondition-failed":
            // Fixture bootstrap must not consume the protected write's attempt sequence.
            if (ReadMethod(line) == "get_project_status" && currentExpectedSessionIdentity is null)
            {
                Respond("""{"success":true,"payload":"{\"isOpen\":true}"}""");
            }
            else
            {
                createBlockPostconditionAttempt++;
                Respond($$"""{"success":false,"failureCategory":"postcondition_failed","error":"block creation verification failed on attempt {{createBlockPostconditionAttempt}}","warnings":["Project state may have changed; inspect the project before retrying."]}""");
            }
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
        case "network-binding-mismatch":
            Respond(ReadMethod(line) == "read_hardware_config"
                ? SuccessWithResolvedPath(
                    HardwareConfigPayload(),
                    @"C:\FakeWorker\Different.ap21")
                : $$"""{"success":false,"error":"expected read_hardware_config, got '{{ReadMethod(line)}}'"}""");
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

        case "network-io-map":
            // Structured I/O-map scenario: read_hardware_config returns ioDetails (addresses,
            // channels, tag matches) ONLY when the request opted in with includeIoDetails=true.
            // When not requested, IoDetails is null and the JsonIgnore attribute omits it, so the
            // default read stays byte-identical to the legacy hardware shape. Built from the
            // shared Contracts DTOs so a contract change here is a compile error, never a silently
            // stale hand-written literal.
            Respond(ReadMethod(line) == "read_hardware_config"
                ? Success(ToCamelCaseJson(IoMapHardwareConfig(
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
                ? Success(ToCamelCaseJson(IoMapMalformedHardwareConfig()))
                : $$"""{"success":false,"error":"expected read_hardware_config, got '{{ReadMethod(line)}}'"}""");
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
                "get_project_status" => """{"success":true,"payload":"{\"isOpen\":true}"}""",
                "get_type_content" => """{"success":true,"payload":"TYPE AnalogInputSettings STRUCT Value : Real; END_STRUCT END_TYPE"}""",
                "update_type_content" => """{"success":true,"payload":"{}"}""",
                _ => $$"""{"success":false,"error":"expected get_project_status, get_type_content, or update_type_content, got '{{ReadMethod(line)}}'"}"""
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
                ("get_project_status", _) => """{"success":true,"payload":"{\"isOpen\":true}"}""",
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
        case "status-with-metadata":
            // Simulates the real worker's GetStatusReadOnly WITH the extended metadata surface,
            // exercising every collection/section of ProjectMetadataInfo so host-side contract
            // tests can assert the full additive metadata schema over the real IPC pipe. Built
            // from the shared Contracts DTO so a contract change here is a compile error, never
            // a silently stale hand-written literal.
            Respond(ReadMethod(line) == "get_project_status"
                ? Success(ToCamelCaseJson(StatusWithMetadataFixture()))
                : $$"""{"success":false,"error":"expected get_project_status, got '{{ReadMethod(line)}}'"}""");
            break;
        case "status-oversized":
        {
            // A get_project_status payload well over the standalone response budget (60000 chars),
            // proving ProjectStandaloneToolTests that the direct status tool is capped by the
            // shared StandaloneToolResultFormatter like every other standalone read.
            var oversizedPayload = "{\"isOpen\":true,\"metadata\":{\"comment\":{\"text\":\""
                + new string('x', 70_000) + "\"}}}";
            Respond(ReadMethod(line) == "get_project_status"
                ? Success(oversizedPayload)
                : $$"""{"success":false,"error":"expected get_project_status, got '{{ReadMethod(line)}}'"}""");
            break;
        }
        case "lifecycle-probe-only":
            // Guards against a regression where a save/save-as/archive/close current-state
            // read reverts to the direct status operation. Only the unbound direct-status fixture
            // bootstrap succeeds; a bound direct status still fails. Every other operation (the
            // probe itself, or the tool's own write call that follows) succeeds normally so the
            // full preview/apply round trip can complete. save_project_as additionally needs a
            // resolvedProjectPath so the rebind bind succeeds after the write.
            Respond(ReadMethod(line) switch
            {
                "get_project_status" when currentExpectedSessionIdentity is null =>
                    """{"success":true,"payload":"{\"isOpen\":true}"}""",
                "get_project_status" =>
                    """{"success":false,"error":"current-state read must use probe_project_status_for_lifecycle, not get_project_status"}""",
                "save_project_as" => """{"success":true,"payload":"{\"isOpen\":true}","resolvedProjectPath":"C:\\lifecycle\\Copy.ap21"}""",
                _ => """{"success":true,"payload":"{\"isOpen\":true}"}"""
            });
            break;
        case "save-as-uncertain-state":
            // Simulates the real worker's postcondition_failed when save_project_as saved a copy
            // but could not confirm the active project is that copy: a failure carrying the
            // uncertain-state warning. The unbound status bootstrap succeeds, but the protected
            // save-as must surface the failure and retain its verified source binding.
            Respond(ReadMethod(line) == "get_project_status" &&
                    currentExpectedSessionIdentity is null
                ? """{"success":true,"payload":"{\"isOpen\":true}"}"""
                : """{"success":false,"failureCategory":"postcondition_failed","error":"could not confirm the copied project path","warnings":["Project state may have changed; inspect the open project before retrying."]}""");
            break;
        case "C:\\bound\\FailingSave.ap21":
            // A bound-path scenario whose save_project_as call fails, proving a failed rebinding
            // save-as leaves the pre-existing session binding untouched (no partial rebind). Its
            // unbound status call exists only to establish that exact verified fixture binding.
            Respond(ReadMethod(line) == "get_project_status" &&
                    currentExpectedSessionIdentity is null
                ? """{"success":true,"payload":"{\"isOpen\":true}"}"""
                : """{"success":false,"failureCategory":"worker_operation_failed","error":"save failed"}""");
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

        // ---------------------------------------------------------------------------
        // Phase 4: subnet lifecycle fixtures (Task 6)
        // ---------------------------------------------------------------------------

        case "network-subnet-lifecycle":
            // The main stateful scenario: normal create/update/delete round trips, canonical
            // text/structuredContent equality, minimal-result shape, audit, and every token
            // tampering path bind against this key.
            Respond(ReadMethod(line) switch
            {
                "read_hardware_config" => Success(ToCamelCaseJson(SubnetLifecycleHardwareConfig(subnetLifecycleState))),
                "create_subnet" or "update_subnet" or "delete_subnet" =>
                    DispatchSubnetLifecycleWrite(line, subnetLifecycleState),
                _ => $$"""{"success":false,"error":"unexpected method '{{ReadMethod(line)}}' for network-subnet-lifecycle"}"""
            });
            break;

        case "network-subnet-lifecycle-alt-path":
            // Same shared mutable state as "network-subnet-lifecycle", reached through a
            // DIFFERENT scenario key: a token issued against one key is rejected against this one
            // purely because the project path differs, never because the resolved target differs
            // (both keys read the exact same underlying list).
            Respond(ReadMethod(line) switch
            {
                "read_hardware_config" => Success(ToCamelCaseJson(SubnetLifecycleHardwareConfig(subnetLifecycleState))),
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
                "read_hardware_config" => Success(ToCamelCaseJson(SubnetLifecycleHardwareConfig(subnetLifecycleState))),
                "create_subnet" or "update_subnet" or "delete_subnet" =>
                    $$"""{"success":true,"payload":"{\"subnetId\":\"subnet-malformed-1\",\"name\":\"Malformed\",\"networkDeviceCount\":{{SubnetLifecycleDeviceCount}},\"networkDeviceCountUnchanged\":true,\"relationshipSummary\":\"connected to 2 devices\"}"}""",
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
                "read_hardware_config" => Success(ToCamelCaseJson(SubnetLifecycleHardwareConfig(subnetLifecycleState))),
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
                "read_hardware_config" => Success(ToCamelCaseJson(SubnetLifecycleHardwareConfig(subnetLifecycleState))),
                "create_subnet" or "update_subnet" or "delete_subnet" =>
                    HandleSecondItemFailureWrite(line, subnetLifecycleState),
                _ => $$"""{"success":false,"error":"unexpected method '{{ReadMethod(line)}}' for network-subnet-lifecycle-second-item-failure"}"""
            });
            break;

        case "network-subnet-lifecycle-state-drift":
            // read_hardware_config reports the SAME subnet identity (name/subnetId) on every call,
            // but its connectedNodeNames - deliberately never part of the resolved target evidence
            // - differs after the first read. A token issued against the first read must be
            // rejected at apply against the SECOND, drifted read via the whole-project
            // current-state hash (state_changed), never via a "different target" mismatch, mirroring
            // the pure-safety-layer proof in NetworkIntrospectionSafetySnapshotTests at the full FakeWorker
            // level.
            // Fixture bootstrap establishes a binding from the baseline without consuming the
            // preview/apply drift sequence; only bound reads advance that sequence.
            Respond(ReadMethod(line) == "read_hardware_config"
                ? Success(ToCamelCaseJson(SubnetLifecycleStateDriftHardwareConfig(
                    currentExpectedSessionIdentity is null
                        ? 1
                        : ++subnetLifecycleStateDriftReadCount)))
                : $$"""{"success":false,"error":"unexpected method '{{ReadMethod(line)}}' for network-subnet-lifecycle-state-drift"}""");
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

void Respond(string json, bool includeSessionIdentity = true)
{
    // Add the same structural identity contract as the real worker. Centralizing it here keeps
    // every existing scripted scenario useful while making worker restarts observable: each fake
    // process receives a fresh workerSessionId.
    try
    {
        if (includeSessionIdentity && JsonNode.Parse(json) is JsonObject response)
        {
            var resolvedPath = response["resolvedProjectPath"]?.GetValue<string>();
            var projectPath = string.Equals(currentMethod, "close_project", StringComparison.Ordinal)
                ? null
                : ProjectPathNormalization.Canonicalize(resolvedPath ?? currentProjectPath);

            if (string.Equals(currentMethod, "close_project", StringComparison.Ordinal))
            {
                if (fakeProjectPath is not null)
                {
                    fakeSessionGeneration++;
                }

                fakeProjectPath = null;
                response["resolvedProjectPath"] = null;
            }
            else if (projectPath is not null)
            {
                var isAuthorizedPathTransition =
                    string.Equals(currentMethod, "open_project", StringComparison.Ordinal) ||
                    string.Equals(currentMethod, "create_project", StringComparison.Ordinal) ||
                    string.Equals(currentMethod, "save_project_as", StringComparison.Ordinal);
                if (isAuthorizedPathTransition &&
                    fakeProjectPath is not null &&
                    !string.Equals(fakeProjectPath, projectPath, StringComparison.OrdinalIgnoreCase))
                {
                    fakeSessionGeneration++;
                }

                fakeProjectPath = projectPath;
            }

            response["sessionIdentity"] = JsonSerializer.SerializeToNode(new WorkerSessionIdentity
            {
                WorkerSessionId = workerSessionId,
                SessionGeneration = fakeSessionGeneration,
                PortalProcessId = FakePortalProcessId,
                ProjectPath = projectPath
            });
            json = response.ToJsonString();
        }
    }
    catch (JsonException)
    {
        // Deliberately malformed protocol scenarios must remain malformed.
    }

    Console.Out.WriteLine(json);
    Console.Out.Flush();
}

WorkerResponse? ValidateExpectedSessionIdentity(
    string? method,
    string? requestedProjectPath,
    WorkerSessionIdentity? expected)
{
    var requiresIdentity =
        OperationPolicyCatalog.RequiresExpectedSessionIdentity(method ?? string.Empty);

    if (expected is null)
    {
        return requiresIdentity
            ? BindingConflict(
                "This operation requires expected worker/Portal/project session identity.")
            : null;
    }

    var expectedPath =
        ProjectPathNormalization.Canonicalize(expected.ProjectPath);
    var activePath =
        ProjectPathNormalization.Canonicalize(fakeProjectPath);

    if (string.IsNullOrWhiteSpace(expected.WorkerSessionId) ||
        expected.SessionGeneration < 0 ||
        expected.PortalProcessId is null ||
        expected.PortalProcessId <= 0 ||
        expectedPath is null ||
        activePath is null ||
        !string.Equals(
            expected.WorkerSessionId,
            workerSessionId,
            StringComparison.Ordinal) ||
        expected.SessionGeneration != fakeSessionGeneration ||
        expected.PortalProcessId != FakePortalProcessId ||
        !string.Equals(expectedPath, activePath, StringComparison.OrdinalIgnoreCase))
    {
        return BindingConflict(
            "The expected worker/Portal/project session identity does not match the FakeWorker session.");
    }

    var establishesProject =
        string.Equals(method, "open_project", StringComparison.Ordinal) ||
        string.Equals(method, "create_project", StringComparison.Ordinal);
    var requestedPath =
        ProjectPathNormalization.Canonicalize(requestedProjectPath);

    if (!establishesProject &&
        requestedPath is not null &&
        !string.Equals(
            expectedPath,
            requestedPath,
            StringComparison.OrdinalIgnoreCase))
    {
        return BindingConflict(
            "The request project path does not match the expected project session identity.");
    }

    return null;
}

WorkerResponse BindingConflict(string error)
    => new()
    {
        Success = false,
        FailureCategory = WorkerFailureCategories.BindingConflict,
        Error = error
    };

string? ScenarioKey(string? path)
{
    if (string.IsNullOrWhiteSpace(path) || path.EndsWith(".ap21", StringComparison.OrdinalIgnoreCase))
    {
        return path;
    }

    // ProjectPathNormalization turns test scenario keywords into absolute paths once a startup
    // binding is configured. The final segment remains the scripted key.
    return Path.GetFileName(path);
}

// A complete ProjectStatusInfo carrying every extended metadata section, modelling what the real
// GetStatusReadOnly produces on V21. History has fewer entries than the reader's cap so
// historyTruncated=0, and the compilation settings read as real booleans (not null), so tests
// can assert the full non-degraded schema.
ProjectStatusInfo StatusWithMetadataFixture() => new()
{
    IsOpen = true,
    Name = "Ground",
    Path = @"C:\Projects\Ground\Ground.ap21",
    Version = "V21",
    Author = "TiaBot",
    IsModified = false,
    CreationTime = new DateTime(2026, 1, 10, 8, 30, 0),
    LastModified = new DateTime(2026, 2, 14, 17, 5, 0),
    LastModifiedBy = "TiaBot",
    Size = 2048,
    Metadata = new ProjectMetadataInfo
    {
        Copyright = "© ACME Controls",
        Family = "Lines",
        Comment = new ProjectCommentInfo
        {
            Translations = new List<ProjectCommentTranslationInfo>
            {
                new() { Culture = "en-US", Text = "Ground line" },
                new() { Culture = "pt-BR", Text = "Linha de piso" },
            },
        },
        LanguageSettings = new ProjectLanguageSettingsInfo
        {
            Languages = new List<string> { "en-US", "de-DE", "pt-BR" },
            ActiveLanguages = new List<string> { "en-US", "pt-BR" },
            EditingLanguage = "en-US",
            ReferenceLanguage = "de-DE",
        },
        HistoryEntries = new List<ProjectHistoryEntryInfo>
        {
            new() { Text = "Project created", DateTime = new DateTime(2026, 1, 10, 8, 0, 0) },
            new() { Text = "Line imported", DateTime = new DateTime(2026, 1, 12, 9, 30, 0) },
        },
        HistoryTruncated = false,
        UsedProducts = new List<ProjectUsedProductInfo>
        {
            new() { Name = "S7-1500", Version = "V4.5" },
            new() { Name = "WinCC", Version = "V7.4" },
        },
        CompilationSettings = new ProjectCompilationSettingsInfo
        {
            IsSimulationDuringBlockCompilationEnabled = true,
            IsVirtualPlcDuringBlockCompilationEnabled = false,
        },
    },
};

// Wraps a payload document as a successful worker response. Serializing beats hand-escaping once a
// payload is more than a few members: the escaping is what a hand-written literal gets wrong, and a
// mis-escaped payload would fail the strict Network contract for the wrong reason.
string Success(string payload) => JsonSerializer.Serialize(new { success = true, payload });

string SuccessWithResolvedPath(string payload, string resolvedProjectPath)
    => JsonSerializer.Serialize(new
    {
        success = true,
        payload,
        resolvedProjectPath
    });

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

// ---------------------------------------------------------------------------
// Phase 4: subnet lifecycle fixtures (Task 6)
// ---------------------------------------------------------------------------

List<DeviceInfo> SubnetLifecycleDevices() => new()
{
    new() { Name = "PLC_1", TypeIdentifier = "OrderNumber:TEST", Items = new List<DeviceItemInfo>() },
    new() { Name = "HMI_1", TypeIdentifier = "OrderNumber:HMI", Items = new List<DeviceItemInfo>() },
};

/// <summary>
/// Renders the CURRENT mutable subnet list as a contract-valid <see cref="HardwareConfigInfo"/>.
/// Devices are always the same two entries; only <paramref name="subnets"/> reflects whatever
/// create_subnet/update_subnet/delete_subnet has done to the shared state so far.
/// </summary>
HardwareConfigInfo SubnetLifecycleHardwareConfig(List<SubnetLifecycleSubnetState> subnets) => new()
{
    Devices = SubnetLifecycleDevices(),
    Subnets = subnets
        .Select(subnet => SelectableSubnet(
            subnet.Name,
            subnet.SubnetId,
            subnet.NetworkType,
            subnet.NetworkType,
            Array.Empty<IoSystemInfo>(),
            subnet.ConnectedNodeNames))
        .ToList(),
    Messages = new List<string>(),
};

/// <summary>
/// Dedicated fixture for the "network-subnet-lifecycle-state-drift" scenario: reports the SAME one
/// Ethernet subnet identity on every read, but its connectedNodeNames differs after the first call
/// - a relationship-only change that never appears in resolved target evidence but still
/// invalidates a token via the whole-project current-state hash.
/// </summary>
HardwareConfigInfo SubnetLifecycleStateDriftHardwareConfig(int readCount) => new()
{
    Devices = SubnetLifecycleDevices(),
    Subnets = new List<SubnetInfo>
    {
        SelectableSubnet(
            "PN/IE_1",
            "subnet-eth-1",
            SubnetLifecycleContract.Ethernet,
            SubnetLifecycleContract.Ethernet,
            Array.Empty<IoSystemInfo>(),
            readCount <= 1
                ? new[] { "PLC_1.X1" }
                : new[] { "PLC_1.X1", "PLC_2.X1" }),
    },
    Messages = new List<string>(),
};

/// <summary>
/// Dispatches one subnet lifecycle write against the shared mutable state and returns the exact
/// four-member <see cref="SubnetLifecycleResultInfo"/> JSON. Shared by every scenario key that
/// performs a REAL (non-switched) mutation, so create/update/delete behave identically no matter
/// which key reached them.
/// </summary>
string DispatchSubnetLifecycleWrite(string requestLine, List<SubnetLifecycleSubnetState> subnets)
    => ReadMethod(requestLine) switch
    {
        "create_subnet" => HandleCreateSubnet(requestLine, subnets),
        "update_subnet" => HandleUpdateSubnet(requestLine, subnets),
        "delete_subnet" => HandleDeleteSubnet(requestLine, subnets),
        _ => $$"""{"success":false,"error":"unexpected subnet lifecycle method '{{ReadMethod(requestLine)}}'"}""",
    };

/// <summary>
/// Assigns a deterministic, nonblank, never-reused subnet id and applies PROFIBUS-only attributes
/// only when the requested network type is PROFIBUS - mirroring
/// <c>SubnetLifecycleService.ApplyProfibusAttributes</c>'s Ethernet/PROFIBUS split. The new subnet
/// starts with an EMPTY connectedNodeNames list: nothing connects it, so it is immediately
/// deletable as an "empty subnet" without needing a third preset fixture.
/// </summary>
string HandleCreateSubnet(string requestLine, List<SubnetLifecycleSubnetState> subnets)
{
    var name = ReadField(requestLine, "subnetName") ?? string.Empty;
    var networkType = ReadField(requestLine, "subnetNetworkType") ?? string.Empty;
    var isProfibus = string.Equals(networkType, SubnetLifecycleContract.Profibus, StringComparison.Ordinal);

    var subnetId = $"subnet-created-{subnetLifecycleNextId}";
    subnetLifecycleNextId++;

    subnets.Add(new SubnetLifecycleSubnetState
    {
        SubnetId = subnetId,
        Name = name,
        NetworkType = networkType,
        HighestAddress = isProfibus ? ReadIntField(requestLine, "subnetHighestAddress") : null,
        TransmissionSpeed = isProfibus ? ReadField(requestLine, "subnetTransmissionSpeed") : null,
        ConnectedNodeNames = new List<string>(),
    });

    return Success(ToCamelCaseJson(new SubnetLifecycleResultInfo
    {
        SubnetId = subnetId,
        Name = name,
        NetworkDeviceCount = SubnetLifecycleDeviceCount,
        NetworkDeviceCountUnchanged = true,
    }));
}

/// <summary>
/// Applies only the fields present on the request to the EXACT matching SubnetId - every other
/// subnet in the shared list, and every field the request omitted, is left untouched.
/// </summary>
string HandleUpdateSubnet(string requestLine, List<SubnetLifecycleSubnetState> subnets)
{
    var subnetId = ReadField(requestLine, "subnetId");
    var target = subnets.FirstOrDefault(subnet => subnet.SubnetId == subnetId);
    if (target is null)
    {
        return $$"""{"success":false,"error":"network-subnet-lifecycle has no subnet with subnetId '{{subnetId}}'"}""";
    }

    var name = ReadField(requestLine, "subnetName");
    if (name is not null)
    {
        target.Name = name;
    }

    var highestAddress = ReadIntField(requestLine, "subnetHighestAddress");
    if (highestAddress is not null)
    {
        target.HighestAddress = highestAddress;
    }

    var transmissionSpeed = ReadField(requestLine, "subnetTransmissionSpeed");
    if (transmissionSpeed is not null)
    {
        target.TransmissionSpeed = transmissionSpeed;
    }

    return Success(ToCamelCaseJson(new SubnetLifecycleResultInfo
    {
        SubnetId = target.SubnetId,
        Name = target.Name,
        NetworkDeviceCount = SubnetLifecycleDeviceCount,
        NetworkDeviceCountUnchanged = true,
    }));
}

/// <summary>
/// Removes the exact matching subnet from the shared list UNCONDITIONALLY - a non-empty
/// connectedNodeNames never blocks this, matching production's "connected deletion is allowed, no
/// dependency inventory" rule. The device collection is never touched.
/// </summary>
string HandleDeleteSubnet(string requestLine, List<SubnetLifecycleSubnetState> subnets)
{
    var subnetId = ReadField(requestLine, "subnetId");
    var target = subnets.FirstOrDefault(subnet => subnet.SubnetId == subnetId);
    if (target is null)
    {
        return $$"""{"success":false,"error":"network-subnet-lifecycle has no subnet with subnetId '{{subnetId}}'"}""";
    }

    subnets.Remove(target);

    return Success(ToCamelCaseJson(new SubnetLifecycleResultInfo
    {
        SubnetId = target.SubnetId,
        Name = target.Name,
        NetworkDeviceCount = SubnetLifecycleDeviceCount,
        NetworkDeviceCountUnchanged = true,
    }));
}

/// <summary>
/// The FIRST subnet write against "network-subnet-lifecycle-second-item-failure" performs a REAL
/// mutation (proving the earlier item stays applied); every later one fails structurally without
/// touching state, proving the batch stops and later items are skipped.
/// </summary>
string HandleSecondItemFailureWrite(string requestLine, List<SubnetLifecycleSubnetState> subnets)
{
    subnetLifecycleSecondFailureWriteCount++;
    return subnetLifecycleSecondFailureWriteCount == 1
        ? DispatchSubnetLifecycleWrite(requestLine, subnets)
        : $$"""{"success":false,"error":"deliberate second-item failure for network-subnet-lifecycle-second-item-failure"}""";
}

int? ReadIntField(string requestLine, string propertyName)
{
    try
    {
        using var doc = JsonDocument.Parse(requestLine);
        return doc.RootElement.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
    }
    catch (JsonException)
    {
        return null;
    }
}

bool? ReadBoolField(string requestLine, string propertyName)
{
    try
    {
        using var doc = JsonDocument.Parse(requestLine);
        return doc.RootElement.TryGetProperty(propertyName, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : null;
    }
    catch (JsonException)
    {
        return null;
    }
}

// Builds the "network-io-map" hardware fixture. ioDetails is attached only when the read opted
// in; otherwise the DeviceItemInfo.IoDetails JsonIgnore attribute omits the member entirely, so
// a default read is byte-identical to the pre-I/O-map shape.
HardwareConfigInfo IoMapHardwareConfig(
    bool includeIoDetails,
    bool includeTagMatches,
    string? deviceName,
    string? plcName)
{
    var selectedDevice = deviceName is null
        || string.Equals("PLC_1", deviceName, StringComparison.OrdinalIgnoreCase);
    var selectedPlc = !includeTagMatches
        || plcName is null
        || string.Equals("PLC_1", plcName, StringComparison.Ordinal);

    return new HardwareConfigInfo
    {
        Devices = selectedDevice
            ? new List<DeviceInfo>
            {
                new()
                {
                    Name = "PLC_1",
                    TypeIdentifier = "OrderNumber:TEST",
                    Items = new List<DeviceItemInfo>
                    {
                        IoMapDeviceItem("DI_16", includeIoDetails, includeTagMatches && selectedPlc),
                    },
                },
            }
            : new List<DeviceInfo>(),
        Subnets = new List<SubnetInfo>(),
        Messages = includeTagMatches && !selectedPlc
            ? new List<string> { $"No PLC named '{plcName}' was found; no tag matches are reported." }
            : new List<string>(),
    };
}

DeviceItemInfo IoMapDeviceItem(string itemName, bool includeIoDetails, bool includeTagMatches)
{
    var item = SelectableDeviceItem(
        "PLC_1", 0, itemName, "OrderNumber:TEST", 1, "PROFINET interface_1");
    if (!includeIoDetails)
    {
        return item;
    }

    item.IoDetails = new DeviceItemIoDetailsInfo
    {
        Addresses = new List<IoAddressInfo>
        {
            // Diagnosis-type addresses on PROFINET interfaces report StartAddress = -1 (and
            // Length = -1) on V21; the worker normalizes those to null, so the fixture models the
            // normalized shape. Ordinal IoType order ("Diagnosis" < "Input" < "Output") mirrors
            // the real worker's deterministic sort.
            new()
            {
                IoType = "Diagnosis",
                StartAddress = null,
                Length = null,
                Context = null,
                ControllerNames = new List<string>(),
            },
            new()
            {
                IoType = "Input",
                StartAddress = 4,
                Length = 2,
                Context = "Device",
                ControllerNames = new List<string> { "PLC_1" },
            },
            new()
            {
                IoType = "Output",
                StartAddress = 4,
                Length = 2,
                Context = "Device",
                ControllerNames = new List<string> { "PLC_1" },
            },
        },
        Channels = new List<IoChannelInfo>
        {
            new()
            {
                Number = 0,
                IoType = "Input",
                Type = "Digital",
                ChannelAddressBits = 32,
                ChannelWidthBits = 1,
                LogicalAddress = "%I4.0",
                TagMatches = includeTagMatches
                    ? new List<IoTagMatchInfo>
                    {
                        // Ordinal order (table, folder, name): mirrors the real worker's sort so the
                        // fixture and production agree on deterministic output.
                        new() { Name = "RunPermit", DataType = "Bool", LogicalAddress = "%I4.0", TableName = "Tag table_1", FolderPath = "/" },
                        new() { Name = "StartButton", DataType = "Bool", LogicalAddress = "%I4.0", TableName = "Tag table_1", FolderPath = "/" },
                    }
                    : new List<IoTagMatchInfo>(),
            },
            new()
            {
                Number = 1,
                IoType = "Input",
                Type = "Analog",
                ChannelAddressBits = 512,
                ChannelWidthBits = 16,
                LogicalAddress = "%IW64",
                TagMatches = includeTagMatches
                    ? new List<IoTagMatchInfo>
                    {
                        new() { Name = "AnalogIn", DataType = "Int", LogicalAddress = "%IW64", TableName = "Tag table_1", FolderPath = "/" },
                    }
                    : new List<IoTagMatchInfo>(),
            },
        },
    };
    return item;
}

// Builds the "network-io-map-malformed" fixture: a structurally valid device item whose
// ioDetails carries an EXPLICIT null addresses collection — the exact shape that must be
// rejected as protocol_error by NetworkPayloadContract.
HardwareConfigInfo IoMapMalformedHardwareConfig() => new()
{
    Devices = new List<DeviceInfo>
    {
        new()
        {
            Name = "PLC_1",
            TypeIdentifier = "OrderNumber:TEST",
            Items = new List<DeviceItemInfo>
            {
                new()
                {
                    Name = "DI_16",
                    TypeIdentifier = "OrderNumber:TEST",
                    PositionNumber = 1,
                    Selectable = false,
                    SelectorDiagnostics = new List<string> { "No selector fixture for the malformed I/O-map item." },
                    NetworkInterfaces = new List<NetworkInterfaceInfo>(),
                    CommunicationConnections = new List<CommunicationConnectionInfo>(),
                    Items = new List<DeviceItemInfo>(),
                    IoDetails = new DeviceItemIoDetailsInfo
                    {
                        Addresses = null!, // explicit null collection -> protocol_error
                        Channels = new List<IoChannelInfo>(),
                    },
                },
            },
        },
    },
    Subnets = new List<SubnetInfo>(),
    Messages = new List<string>(),
};

/// <summary>Mutable process-local state for one subnet in the Phase 4 lifecycle scenarios.</summary>
sealed class SubnetLifecycleSubnetState
{
    public required string SubnetId { get; init; }

    public string Name { get; set; } = string.Empty;

    public string NetworkType { get; set; } = string.Empty;

    public int? HighestAddress { get; set; }

    public string? TransmissionSpeed { get; set; }

    public List<string> ConnectedNodeNames { get; set; } = new();
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
