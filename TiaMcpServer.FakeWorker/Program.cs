using System.Text.Json;

// Scripted stand-in for TiaMcpServer.OpennessWorker used by IPC integration tests.
// Mirrors the real worker's request loop: one JSON line in, one JSON line out, until
// stdin closes. The test encodes the scenario in the request's projectPath field.
var seq = 0;
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
            // without the payload failing its declared contract.
            Respond($$"""{"success":true,"payload":"{\"devices\":[],\"subnets\":[],\"messages\":[\"seq:{{seq}}\"]}"}""");
            break;
        case "network-write-item-failure":
            // Stable hardware state (so preview/apply token binding holds) followed by a failing
            // first write: the batch RAN, so the MCP call itself is not an error.
            Respond(ReadMethod(line) switch
            {
                "read_hardware_config" => """{"success":true,"payload":"{\"devices\":[],\"subnets\":[],\"messages\":[]}"}""",
                _ => """{"success":false,"error":"device could not be added"}"""
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
string HardwareConfigPayload() => """
    {
      "devices": [
        {
          "name": "PLC_1",
          "typeIdentifier": "OrderNumber:TEST",
          "items": [
            {
              "name": "PROFINET interface_1",
              "typeIdentifier": "OrderNumber:TEST",
              "positionNumber": 1,
              "address": null,
              "networkInterfaces": [
                {
                  "name": "PROFINET interface_1",
                  "nodes": [
                    {
                      "name": "X1",
                      "nodeId": "0",
                      "nodeType": "Ethernet",
                      "ipAddress": "192.168.0.10",
                      "subnetMask": "255.255.255.0",
                      "pnDeviceName": "plc-1",
                      "subnetName": "PN/IE_1",
                      "ioSystemName": "IO system_1"
                    }
                  ]
                }
              ],
              "items": []
            }
          ]
        },
        {
          "name": "PC_System_1",
          "typeIdentifier": "OrderNumber:PC-System",
          "items": [
            {
              "name": "IE general_1",
              "typeIdentifier": "OrderNumber:IE-General",
              "positionNumber": 1,
              "address": null,
              "networkInterfaces": [
                {
                  "name": "PROFINET interface_1",
                  "nodes": [
                    {
                      "name": "E1",
                      "nodeId": "0",
                      "nodeType": "Ethernet",
                      "ipAddress": "192.168.0.20",
                      "subnetMask": "255.255.255.0",
                      "pnDeviceName": null,
                      "subnetName": "PN/IE_1",
                      "ioSystemName": null
                    }
                  ]
                }
              ],
              "items": []
            },
            {
              "name": "IE general_2",
              "typeIdentifier": "OrderNumber:IE-General",
              "positionNumber": 2,
              "address": null,
              "networkInterfaces": [
                {
                  "name": "PROFINET interface_2",
                  "nodes": [
                    {
                      "name": "E2",
                      "nodeId": "1",
                      "nodeType": "Ethernet",
                      "ipAddress": "10.0.0.20",
                      "subnetMask": "255.255.255.0",
                      "pnDeviceName": null,
                      "subnetName": "PN/IE_2",
                      "ioSystemName": null
                    }
                  ]
                }
              ],
              "items": []
            }
          ]
        }
      ],
      "subnets": [
        {
          "name": "PN/IE_1",
          "subnetId": "subnet-1",
          "networkType": "Ethernet",
          "typeIdentifier": "Ethernet",
          "ioSystems": [
            {
              "name": "IO system_1",
              "number": 100,
              "ioControllerName": "PLC_1",
              "connectedDeviceNames": []
            }
          ],
          "connectedNodeNames": ["PLC_1.X1", "PC_System_1.E1"]
        },
        {
          "name": "PN/IE_2",
          "subnetId": "subnet-2",
          "networkType": "Ethernet",
          "typeIdentifier": "Ethernet",
          "ioSystems": [],
          "connectedNodeNames": ["PC_System_1.E2"]
        }
      ],
      "messages": []
    }
    """;

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
