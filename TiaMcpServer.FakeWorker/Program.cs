using System.Text.Json;
using System.Text.Json.Nodes;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;

// Scripted stand-in for TiaMcpServer.OpennessWorker used by IPC integration tests.
// Mirrors the real worker's request loop: one JSON line in, one JSON line out, until
// stdin closes. Scenarios use projectPath file names; UI-open and Portal-inventory environment
// fixtures declare source state independently, so ordinary reads cannot select or open a project.
var launchLog = Environment.GetEnvironmentVariable("TIA_MCP_FAKE_WORKER_LAUNCH_LOG");
if (!string.IsNullOrWhiteSpace(launchLog))
    File.AppendAllText(launchLog, JsonSerializer.Serialize(new { processId = Environment.ProcessId, args }) + Environment.NewLine);

var seq = 0;
var requestLog = Environment.GetEnvironmentVariable("TIA_MCP_FAKE_WORKER_REQUEST_LOG");
var modeIndex = Array.FindIndex(args, arg => string.Equals(arg, "--access-mode", StringComparison.OrdinalIgnoreCase));
var accessMode = modeIndex < 0 ? McpAccessMode.ReadWrite
    : modeIndex + 1 < args.Length && McpAccessModeNames.TryParse(args[modeIndex + 1], out var parsedMode)
        ? parsedMode : McpAccessMode.ReadOnly;
var workerSessionId = Guid.NewGuid().ToString("N");
var fakeSessionGeneration = 1L;
// Tests may model a project that was already opened in the TIA Portal UI before the worker
// starts. Ordinary requests never establish this mutable session state.
string? fakeProjectPath = ProjectPathNormalization.Canonicalize(
    Environment.GetEnvironmentVariable("TIA_MCP_FAKE_WORKER_UI_OPEN_PROJECT"));
var portalInventoryDeclaration = Environment.GetEnvironmentVariable("TIA_MCP_FAKE_WORKER_PORTALS");
var portalInventoryDeclared = portalInventoryDeclaration is not null;
var fakePortals = portalInventoryDeclared
    ? portalInventoryDeclaration!.Split(';', StringSplitOptions.RemoveEmptyEntries)
        .Select(FakePortalState.Parse).ToList()
    : new List<FakePortalState> { new() { ProcessId = 4242, ProjectPath = fakeProjectPath, HasUserInterface = true } };
int? fakePortalProcessId = portalInventoryDeclared
    ? fakePortals.FirstOrDefault(portal => fakeProjectPath is not null &&
        string.Equals(portal.ProjectPath, fakeProjectPath, StringComparison.OrdinalIgnoreCase))?.ProcessId
    : 4242;
if (fakePortalProcessId is null) fakeProjectPath = null;
string? currentProjectPath = null;
string? currentMethod = null;
string? currentRequestLine = null;
var requestJsonOptions = WorkerJson.Envelope;

// Process-local, mutable hardware state for the "multi-homed-network" scenario (see below): a
// single PC station exposing two ports on separate interfaces. Declared once per FakeWorker
// process so a configure_network_device call mutates it and a later read_hardware_config call in
// the SAME process observes the mutation - proving a real read -> select -> preview -> apply ->
// read round trip, not just a static fixture.
var multiHomedPlcNode = new MultiHomedNode { Name = "PLC port", NodeId = "node-plc", IpAddress = "192.168.0.20" };
var multiHomedDbNode = new MultiHomedNode { Name = "Database port", NodeId = "node-db", IpAddress = "10.20.30.40" };
HardwareConfigInfo? qualifiedNetworkState = null;
var qualifiedHardwareReadCount = 0;
HardwareConfigInfo? guardedNetworkState = null;
HardwareConfigInfo? roundtripNetworkState = null;
var guardedNetworkWrites = 0;
var guardedSubnetAttributes = new Dictionary<(string SubnetId, string Name), string>();

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
var blockOutcomeUpdateAttempt = 0;
var createBlockPostconditionAttempt = 0;
var orderedTypeWriteCount = 0;
var tagUpdateFlagDriftSnapshotReadCount = 0;
var tagUpdateTargetDriftReadCount = 0;
var tagUpdateTargetDriftMutationCount = 0;
var tagUpdateStrictSnapshotFailureBroadReadCount = 0;
var tagUpdateInvalidSnapshotBroadReadCount = 0;
var tagSafetyDedupReadCount = 0;
var tagSafetySnapshotReadCount = 0;
var tagSafetyMutationCount = 0;
var tagSafetySnapshotReadsAtMutation = 0;
var tagSafetyBroadReadCount = 0;
var lifecycleRebindProbeReadCount = 0;
var lifecycleRebindOpenProjectCalls = 0;
string? guardedLifecycleScenario = null;
string? lifecycleProbeOnlyCopiedPath = null;
var guardedLifecycleModified = false;
var tagSafetyTargetExists = true;
var tagSafetyTargetTagName = "Start";
var tagSafetySiblingTag = new TagSafetyIdentityInfo("PLC_1", "/", "Outputs", "Before",
    "PLC_1/Tag tables/Outputs/Before", "Bool", "%Q0.0", true, true, false);
var hardwarePaginationScenarioCalls = new Dictionary<string, int>(StringComparer.Ordinal);
var hardwarePaginationIdentityDrift = false;
var projectTreeV3ScenarioCalls = new Dictionary<string, int>(StringComparer.Ordinal);
var projectTreeSafetyScenarioCalls = new Dictionary<string, int>(StringComparer.Ordinal);
var projectTreeDedupCounters = new Dictionary<string, int>(StringComparer.Ordinal);
var projectTreeDedupPhase = "preview";

// Two devices that never change across any subnet lifecycle operation, modelling the stable
// "root device count" the production SubnetLifecycleService verifies after every commit.
const int SubnetLifecycleDeviceCount = 2;

string? line;
while ((line = Console.In.ReadLine()) is not null)
{
    if (!string.IsNullOrWhiteSpace(requestLog))
        File.AppendAllText(requestLog, (ReadMethod(line) ?? "unknown") + Environment.NewLine);
    currentRequestLine = line;
    seq++;
    string? scenario = null;
    currentProjectPath = null;
    currentMethod = null;
    WorkerSessionIdentity? currentExpectedSessionIdentity = null;
    try
    {
        using var doc = JsonDocument.Parse(line);
        currentMethod = doc.RootElement.TryGetProperty("method", out var method) && method.ValueKind == JsonValueKind.String
            ? method.GetString()
            : null;
        if (doc.RootElement.TryGetProperty("projectPath", out var p) && p.ValueKind == JsonValueKind.String)
        {
            currentProjectPath = p.GetString();
            scenario = ScenarioKey(currentProjectPath);
        }
        if (doc.RootElement.TryGetProperty("projectDirectory", out var d) && d.ValueKind == JsonValueKind.String)
        {
            // create_project's target directory is not an existing source project. Other tests
            // use this field as a script selector for raw requests; it must never become the
            // active or requested source path for an ordinary read.
            if (currentMethod == "create_project") currentProjectPath = d.GetString();
            scenario = ScenarioKey(d.GetString());
        }

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

    // An omitted projectPath targets the project already open in the simulated UI.
    if (scenario is null && currentProjectPath is null && fakeProjectPath is not null)
        scenario = ScenarioKey(fakeProjectPath);

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
        }, WorkerJson.Envelope));
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

    if (currentMethod == "list_tia_portal_processes")
    {
        Respond(Success(WorkerJson.SerializePayload(new TiaPortalProcessListInfo
        {
            AttachedProcessId = fakePortalProcessId,
            Processes = fakePortals.OrderBy(portal => portal.ProcessId).Select(portal => new TiaPortalProcessInfo
            {
                ProcessId = portal.ProcessId, ProjectPath = portal.ProjectPath,
                HasUserInterface = portal.HasUserInterface,
                AttachedByThisWorker = portal.ProcessId == fakePortalProcessId
            }).ToList()
        })));
        continue;
    }

    if (currentMethod == "select_portal_project")
    {
        SelectPortalProject(currentProjectPath);
        continue;
    }

    if (currentMethod is "list_server_connections" or "list_server_groups" or "list_server_projects"
        or "list_local_sessions" or "get_lock_state")
    {
        InspectMultiuser(JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Envelope)!);
        continue;
    }

    var establishesProject = currentMethod is "open_project" or "create_project";
    var statusRead = currentMethod is "get_project_status" or "get_basic_project_status"
        or "probe_project_status_for_lifecycle";
    var rebindProbe = currentMethod == "probe_open_project_rebind";
    if (!establishesProject && !rebindProbe)
    {
        var decision = ProjectOpenPolicy.Decide(fakeProjectPath, currentProjectPath);
        if (decision == ProjectOpenDecision.Refuse)
        {
            Respond(JsonSerializer.Serialize(new WorkerResponse
            {
                Success = false,
                FailureCategory = WorkerFailureCategories.BindingConflict,
                Error = ProjectOpenPolicy.RefusalMessage(fakeProjectPath!, currentProjectPath!, accessMode)
            }, WorkerJson.Envelope));
            continue;
        }

        if (fakeProjectPath is null && currentMethod == "get_basic_project_status" &&
            guardedLifecycleScenario?.Contains("-verification-failure", StringComparison.OrdinalIgnoreCase) == true)
        {
            // The scripted close verification fails decoding after the source has actually
            // closed. Keep the closed state while exercising the verifier's failure path.
            Respond(Success("{\"untrustedMarker\":true}"));
            continue;
        }

        if (fakeProjectPath is null && statusRead)
        {
            var closed = new ProjectStatusInfo { IsOpen = false };
            Respond(Success(currentMethod == "get_project_status"
                ? WorkerJson.SerializePayload(new ProjectStatusResultInfo
                {
                    Operation = "get_project_status", Project = closed
                })
                : WorkerJson.SerializePayload(new ProjectLifecycleResultInfo
                {
                    Operation = currentMethod == "get_basic_project_status"
                        ? "get_project_status" : currentMethod!, Project = closed
                })));
            continue;
        }

        if (decision == ProjectOpenDecision.RequestedNotOpen || fakeProjectPath is null)
        {
            Respond(JsonSerializer.Serialize(new WorkerResponse
            {
                Success = false,
                FailureCategory = decision == ProjectOpenDecision.RequestedNotOpen
                    ? WorkerFailureCategories.AccessDenied : WorkerFailureCategories.WorkerOperationFailed,
                Error = decision == ProjectOpenDecision.RequestedNotOpen
                    ? ProjectOpenPolicy.NotOpenMessage(currentProjectPath!, accessMode)
                    : ProjectOpenPolicy.NoProjectOpenMessage(accessMode)
            }, WorkerJson.Envelope));
            continue;
        }
    }

    if (portalInventoryDeclared && currentMethod == "get_project_status")
    {
        var attached = AttachedPortal();
        Respond(Success(DirectStatusPayload(new ProjectStatusInfo
        {
            IsOpen = fakeProjectPath is not null, Path = fakeProjectPath,
            Name = fakeProjectPath is null ? null : Path.GetFileNameWithoutExtension(fakeProjectPath),
            IsModified = fakeProjectPath is null ? null : attached?.Modified,
            Version = fakeProjectPath is null ? null : "V21"
        })));
        continue;
    }

    if (currentProjectPath?.Contains("guarded-lifecycle", StringComparison.OrdinalIgnoreCase) == true)
    {
        if (!string.Equals(guardedLifecycleScenario, currentProjectPath, StringComparison.OrdinalIgnoreCase))
            guardedLifecycleModified = currentProjectPath.Contains("-modified", StringComparison.OrdinalIgnoreCase);
        guardedLifecycleScenario = currentProjectPath;
    }
    if (guardedLifecycleScenario is not null && (currentProjectPath?.Contains("guarded-lifecycle", StringComparison.OrdinalIgnoreCase) == true
        || currentProjectPath is null && currentMethod == "get_basic_project_status"))
    {
        Respond(GuardedLifecycleResponse(line, guardedLifecycleScenario));
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
            if (ReadMethod(line) == "open_project")
            {
                lifecycleRebindOpenProjectCalls++;
            }
            Respond(SuccessWithResolvedPath("{\"isOpen\":true}", currentProjectPath!));
            break;
        case @"C:\Lifecycle\B-ui-owned.ap21":
            if (ReadMethod(line) == "open_project")
            {
                lifecycleRebindOpenProjectCalls++;
            }
            Respond(SuccessWithResolvedPath("{\"isOpen\":true}", currentProjectPath!));
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
                Respond(BlockOutcomeResponse(
                    success: false,
                    category: WorkerFailureCategories.PostconditionFailed,
                    message: $"block update verification failed on attempt {updateBlockPostconditionAttempt}",
                    outcome: CompletedBlockOutcome(SourceFormatNames.Xml, compileStage: "unavailable")));
            }
            break;
        case "block-outcome-success":
        case "block-outcome-postcondition":
        case "block-outcome-partial":
        case "block-outcome-missing-success":
        case "block-outcome-missing-failure":
        case "block-outcome-invalid":
        case "block-outcome-oversized":
        case "block-outcome-audit-failure":
            if (ReadMethod(line) == "get_project_status" && currentExpectedSessionIdentity is null)
            {
                Respond("""{"success":true,"payload":"{\"isOpen\":true}"}""");
            }
            else if (ReadMethod(line) == "get_block_content")
            {
                Respond("""{"success":true,"payload":"DATA_BLOCK \"Before\"\r\nBEGIN\r\nEND_DATA_BLOCK\r\n"}""");
            }
            else if (ReadMethod(line) == "update_block_logic")
            {
                blockOutcomeUpdateAttempt++;
                Respond(scenario switch
                {
                    "block-outcome-success" => BlockOutcomeResponse(
                        true, null, $"update attempt {blockOutcomeUpdateAttempt}",
                        CompletedBlockOutcome(SourceFormatNames.Source, "succeeded")),
                    "block-outcome-postcondition" => BlockOutcomeResponse(
                        false, WorkerFailureCategories.PostconditionFailed,
                        $"postcondition attempt {blockOutcomeUpdateAttempt}",
                        CompletedBlockOutcome(SourceFormatNames.Source, "failed")),
                    "block-outcome-partial" => BlockOutcomeResponse(
                        true, null, $"partial attempt {blockOutcomeUpdateAttempt}",
                        CompletedBlockOutcome(SourceFormatNames.Source, "unavailable", partialReport: true)),
                    "block-outcome-missing-success" =>
                        $$"""{"success":true,"payload":"untrusted marker attempt {{blockOutcomeUpdateAttempt}}"}""",
                    "block-outcome-missing-failure" =>
                        $$"""{"success":false,"failureCategory":"postcondition_failed","error":"untrusted marker attempt {{blockOutcomeUpdateAttempt}}"}""",
                    "block-outcome-invalid" => BlockOutcomeResponse(
                        false, WorkerFailureCategories.PostconditionFailed,
                        $"untrusted marker attempt {blockOutcomeUpdateAttempt}",
                        CompletedBlockOutcome(SourceFormatNames.Source, "failed") with
                        {
                            TargetMutationCommitted = false
                        }),
                    "block-outcome-oversized" => BlockOutcomeResponse(
                        false, WorkerFailureCategories.PostconditionFailed,
                        $"untrusted marker attempt {blockOutcomeUpdateAttempt}",
                        OversizedBlockOutcome()),
                    _ => BlockOutcomeResponse(
                        false, WorkerFailureCategories.PostconditionFailed,
                        $"audit failure attempt {blockOutcomeUpdateAttempt}",
                        AuditFailureBlockOutcome())
                });
            }
            else
            {
                Respond($$"""{"success":false,"failureCategory":"validation_error","error":"unexpected method '{{ReadMethod(line)}}'"}""");
            }
            break;
        case "block-outcome-preimport-validation":
        case "block-outcome-preimport-access":
        case "block-outcome-preimport-no-project":
        case "block-outcome-preimport-project-open":
        case "block-outcome-preimport-identity":
            if (ReadMethod(line) == "get_project_status" && currentExpectedSessionIdentity is null)
            {
                Respond("""{"success":true,"payload":"{\"isOpen\":true}"}""");
            }
            else
            {
                var category = scenario switch
                {
                    "block-outcome-preimport-validation" => WorkerFailureCategories.ValidationError,
                    "block-outcome-preimport-access" => WorkerFailureCategories.AccessDenied,
                    "block-outcome-preimport-identity" => WorkerFailureCategories.BindingConflict,
                    _ => WorkerFailureCategories.WorkerOperationFailed
                };
                Respond(BlockOutcomeResponse(
                    false,
                    category,
                    $"pre-import {scenario}",
                    NotStartedBlockOutcome(SourceFormatNames.Source)));
            }
            break;
        case "block-outcome-transport-hang":
        case "block-outcome-transport-crash":
        case "block-outcome-transport-malformed":
        case "block-outcome-transport-null-response":
            if (ReadMethod(line) == "get_project_status" && currentExpectedSessionIdentity is null)
            {
                Respond("""{"success":true,"payload":"{\"isOpen\":true}"}""");
            }
            else if (scenario.EndsWith("hang", StringComparison.Ordinal))
            {
                Thread.Sleep(Timeout.Infinite);
            }
            else if (scenario.EndsWith("crash", StringComparison.Ordinal))
            {
                Environment.Exit(23);
            }
            else if (scenario.EndsWith("malformed", StringComparison.Ordinal))
            {
                Respond("this is not json");
            }
            else
            {
                Console.Out.WriteLine();
                Console.Out.Flush();
            }
            break;
        case "block-outcome-status-failure":
            Respond("""{"success":false,"failureCategory":"worker_operation_failed","error":"configured project verification failed"}""");
            break;
        case "block-outcome-status-failure-with-stale-outcome":
            Respond(ReadMethod(line) == "get_project_status"
                ? BlockOutcomeResponse(
                    false,
                    WorkerFailureCategories.WorkerOperationFailed,
                    $"configured project verification failed on request {seq}",
                    CompletedBlockOutcome(SourceFormatNames.Source, "succeeded"))
                : BlockOutcomeResponse(
                    false,
                    WorkerFailureCategories.ProtocolError,
                    $"update_block_logic must not be sent; observed request {seq}",
                    CompletedBlockOutcome(SourceFormatNames.Source, "succeeded")));
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
        case "tree-safety-dedup":
            Respond(ProjectTreeDedupResponse(line));
            break;
        case "tree-safety-create-block-content-drift":
        case "tree-safety-unit-unrelated-sibling-drift":
        case "tree-safety-route-create-block":
        case "tree-safety-route-create-block-group":
        case "tree-safety-route-delete-block-group":
        case "tree-safety-create-group-collision-drift":
        case "tree-safety-delete-group-descendant-drift":
        case "tree-safety-malformed-payload":
        case "tree-safety-authoritative-export-failure":
        case "tree-safety-duplicate-group-occupancy":
        case "tree-safety-conflicting-descendants":
        case "tree-safety-unit-root":
        case "tree-safety-unit-nested":
            Respond(ProjectTreeSafetyResponse(line, scenario));
            break;
        case "tree-safety-request-echo":
            Respond(Success(line));
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
        case "batch-preview-tag-safety":
            Respond(ReadMethod(line) == "read_create_tag_table_safety_snapshot"
                ? Success(ToCamelCaseJson(new CreateTagTableSafetySnapshotInfo(
                    "PLC_1", "", "Inputs", Array.Empty<TagCollisionProbeInfo>())))
                : Success(line));
            break;
        case "echo":
            // Returns the received request verbatim so tests can assert which fields survived
            // the BatchOperationRequest -> WorkerRequest hop.
            Respond(ReadMethod(line) == "update_block_logic"
                ? BlockOutcomeResponse(
                    true,
                    null,
                    line,
                    CompletedBlockOutcome(
                        string.Equals(
                            ReadField(line, "format"),
                            SourceFormatNames.Source,
                            StringComparison.Ordinal)
                                ? SourceFormatNames.Source
                                : SourceFormatNames.Xml,
                        "unavailable"))
                : JsonSerializer.Serialize(new { success = true, payload = line }));
            break;
        case "tag-safety-all-routes":
        case "tag-safety-invalid-routes":
        case "tag-safety-private-collision-kind":
            Respond(ReadMethod(line) == "get_project_status"
                ? """{"success":true,"payload":"{\"isOpen\":true}"}"""
                : TagSafetyRouteResponse(line, scenario == "tag-safety-invalid-routes",
                    invalidCollision: scenario == "tag-safety-private-collision-kind"));
            break;
        case "tag-safety-same-object-drift":
        case @"C:\FakeWorker\tag-safety-same-object-drift.ap21":
        case "tag-safety-collision-drift":
        case @"C:\FakeWorker\tag-safety-collision-drift.ap21":
        case "tag-safety-unrelated-sibling":
        case @"C:\FakeWorker\tag-safety-unrelated-sibling.ap21":
        case "tag-safety-delete-table-export-drift":
        case @"C:\FakeWorker\tag-safety-delete-table-export-drift.ap21":
        case "tag-safety-reread":
        case @"C:\FakeWorker\tag-safety-reread.ap21":
        case "tag-safety-authorized-apply":
        case @"C:\FakeWorker\tag-safety-authorized-apply.ap21":
            Respond(TagSafetyBehaviorResponse(line, Path.GetFileNameWithoutExtension(scenario)));
            break;
        case "tag-safety-route-proof":
        case @"C:\FakeWorker\tag-safety-route-proof.ap21":
        case "tag-safety-dedup-proof":
        case @"C:\FakeWorker\tag-safety-dedup-proof.ap21":
            Respond(ReadMethod(line) switch
            {
                "get_project_status" => """{"success":true,"payload":"{\"isOpen\":true}"}""",
                "list_tag_tables" => """{"success":false,"error":"wrong route: list_tag_tables"}""",
                "read_delete_tag_safety_snapshot" when scenario.Contains("tag-safety-dedup-proof", StringComparison.Ordinal) && ++tagSafetyDedupReadCount > 1
                    => """{"success":false,"error":"dedup missing: repeated read_delete_tag_safety_snapshot"}""",
                "read_delete_tag_safety_snapshot" => Success(ToCamelCaseJson(new DeleteTagSafetySnapshotInfo(
                    new("PLC_1", "", "Inputs", "PLC_1/Inputs"),
                    new("PLC_1", "", "Inputs", "Start", "PLC_1/Inputs/Start", "Bool", "%I0.0", true, true, false)))),
                _ => """{"success":false,"error":"unexpected tag safety route-proof method"}"""
            });
            break;
        case "tag-update-snapshot-unavailable-visible":
            Respond(ReadMethod(line) switch
            {
                "get_project_status" => """{"success":true,"payload":"{\"isOpen\":true}"}""",
                "list_tag_tables" => """{"success":true,"payload":"{\"tables\":[]}"}""",
                "update_tag" => """{"success":true,"payload":"{}"}""",
                "read_update_tag_safety_snapshot" => Success(ToCamelCaseJson(TagUpdateSnapshot(externalVisible: null))),
                _ => $$"""{"success":false,"error":"unexpected update-tag safety fixture method '{{ReadMethod(line)}}'"}"""
            });
            break;
        case "tag-update-snapshot-unavailable-all":
            Respond(ReadMethod(line) switch
            {
                "get_project_status" => """{"success":true,"payload":"{\"isOpen\":true}"}""",
                "list_tag_tables" => """{"success":true,"payload":"{\"tables\":[]}"}""",
                "update_tag" => """{"success":true,"payload":"{}"}""",
                "read_update_tag_safety_snapshot" => Success(ToCamelCaseJson(TagUpdateSnapshot(null, null, null))),
                _ => $$"""{"success":false,"error":"unexpected all-unavailable update-tag fixture method '{{ReadMethod(line)}}'"}"""
            });
            break;
        case "tag-update-flag-drift":
            Respond(ReadMethod(line) switch
            {
                "get_project_status" => """{"success":true,"payload":"{\"isOpen\":true}"}""",
                "list_tag_tables" => """{"success":true,"payload":"{\"tables\":[]}"}""",
                "update_tag" => """{"success":true,"payload":"{}"}""",
                "read_update_tag_safety_snapshot" => TagUpdateDriftSnapshotResponse(++tagUpdateFlagDriftSnapshotReadCount),
                _ => $$"""{"success":false,"error":"unexpected update-tag safety fixture method '{{ReadMethod(line)}}'"}"""
            });
            break;
        case "tag-update-target-drift":
            switch (ReadMethod(line))
            {
                case "get_project_status":
                    Respond($$"""{"success":true,"payload":"{\"isOpen\":true,\"targetDriftMutationCount\":{{tagUpdateTargetDriftMutationCount}}}"}""");
                    break;
                case "read_update_tag_safety_snapshot":
                    Respond(Success(ToCamelCaseJson(TagUpdateSnapshot(dataType: ++tagUpdateTargetDriftReadCount == 1 ? "Bool" : "DInt"))));
                    break;
                case "list_tag_tables":
                    Respond("""{"success":false,"error":"wrong route: list_tag_tables"}""");
                    break;
                case "update_tag":
                    tagUpdateTargetDriftMutationCount++;
                    Respond("""{"success":true,"payload":"{}"}""");
                    break;
                default:
                    Respond("""{"success":false,"error":"unexpected target-drift update-tag fixture method"}""");
                    break;
            }
            break;
        case "tag-update-snapshot-read-fails":
            switch (ReadMethod(line))
            {
                case "read_update_tag_safety_snapshot":
                    Respond("""{"success":false,"failureCategory":"worker_operation_failed","error":"strict update-tag snapshot read failed"}""");
                    break;
                case "list_tag_tables":
                    tagUpdateStrictSnapshotFailureBroadReadCount++;
                    Respond("""{"success":true,"payload":"{\"tables\":[]}"}""");
                    break;
                case "get_project_status":
                    Respond($$"""{"success":true,"payload":"{\"isOpen\":true,\"strictSnapshotFailureBroadReadCount\":{{tagUpdateStrictSnapshotFailureBroadReadCount}}}"}""");
                    break;
                default:
                    Respond("""{"success":false,"error":"unexpected update-tag snapshot-failure fixture method"}""");
                    break;
            }
            break;
        case "tag-update-snapshot-invalid-payload":
            switch (ReadMethod(line))
            {
                case "read_update_tag_safety_snapshot":
                    Respond(InvalidTagUpdateSnapshotResponse(line));
                    break;
                case "list_tag_tables":
                    tagUpdateInvalidSnapshotBroadReadCount++;
                    Respond("""{"success":true,"payload":"{\"tables\":[]}"}""");
                    break;
                case "get_project_status":
                    Respond($$"""{"success":true,"payload":"{\"isOpen\":true,\"invalidSnapshotBroadReadCount\":{{tagUpdateInvalidSnapshotBroadReadCount}}}"}""");
                    break;
                default:
                    Respond("""{"success":false,"error":"unexpected update-tag invalid-snapshot fixture method"}""");
                    break;
            }
            break;
        case "tag-update-broad-best-effort-omission":
            Respond(ReadMethod(line) switch
            {
                "get_project_status" => """{"success":true,"payload":"{\"isOpen\":true}"}""",
                "read_update_tag_safety_snapshot" => Success(ToCamelCaseJson(TagUpdateSnapshot())),
                "list_tag_tables" => """{"success":true,"payload":"{\"tables\":[]}","warnings":["Skipping unrelated tag table: access denied."]}""",
                "update_tag" => """{"success":true,"payload":"{}"}""",
                _ => """{"success":false,"error":"unexpected update-tag broad-omission fixture method"}"""
            });
            break;
        case "tag-update-exact-malformed-payload":
            Respond(ReadMethod(line) switch
            {
                "get_project_status" => """{"success":true,"payload":"{\"isOpen\":true}"}""",
                "read_update_tag_safety_snapshot" => Success("{not valid json"),
                "list_tag_tables" => """{"success":false,"error":"wrong route: list_tag_tables"}""",
                _ => """{"success":false,"error":"unexpected update-tag malformed-exact fixture method"}"""
            });
            break;
        case "network-read-warnings":
            // A contract-valid hardware payload carried alongside worker warnings, so the network
            // read path can be proven to copy warnings onto the item it decoded successfully.
            Respond("""{"success":true,"payload":"{\"devices\":[],\"subnets\":[],\"messages\":[]}","warnings":["Skipping device 'X' while reading hardware configuration: access denied.","Skipping subnet 'Y' while reading hardware configuration: not supported."]}""");
            break;
        case "network-config-partial":
        case "network-config-all-skipped":
            roundtripNetworkState ??= RoundtripHardwareConfig();
            Respond(ReadMethod(line) switch
            {
                "read_hardware_config" => Success(ToCamelCaseJson(roundtripNetworkState)),
                "configure_network_device" => Success(ToCamelCaseJson(new ConfigureNetworkDeviceResultInfo
                {
                    DeviceName = "PLC_1",
                    AppliedSettings = scenario == "network-config-partial"
                        ? new Dictionary<string, string> { ["Address"] = "192.168.0.10" }
                        : new Dictionary<string, string>(),
                    SkippedSettings = scenario == "network-config-partial"
                        ? new Dictionary<string, string> { ["IoSystem"] = "No IO connector." }
                        : new Dictionary<string, string>
                        {
                            ["Address"] = "Read only.",
                            ["IoSystem"] = "No IO connector.",
                        },
                    Messages = new List<string> { $"seq:{seq}" },
                    Verification = FakeConfigurationVerification(line, "PLC_1", scenario == "network-config-partial"
                        ? new Dictionary<string, string> { ["Address"] = "192.168.0.10" } : new()),
                })),
                _ => """{"success":false,"error":"unexpected sparse configuration method"}""",
            });
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
                "read_hardware_config" => Success(ToCamelCaseJson(roundtripNetworkState)),
                "search_equipment_catalog" => """{"success":true,"payload":"[{\"typeName\":\"TEST\",\"articleNumber\":null,\"version\":null,\"typeIdentifier\":\"OrderNumber:TEST\",\"typeIdentifierNormalized\":null,\"catalogPath\":null,\"description\":null}]"}""",
                "list_network_objects" => Success(ToCamelCaseJson(ListNetworkObjectsFixture())),
                "inspect_network_object" => Success(ToCamelCaseJson(InspectNetworkObjectFixture())),
                "add_network_device" or "configure_network_device" => HandleGuardedNetwork(line, roundtripNetworkState, scenario),
                _ => $$"""{"success":false,"error":"unexpected network method '{{ReadMethod(line)}}'"}"""
            });
            break;
        case "plc-read-roundtrip":
            Respond(ReadMethod(line) switch
            {
                "get_block_content" when ReadField(line, "blockPath") == "PLC_1/Missing"
                    => """{"success":false,"error":"block not found"}""",
                "get_block_content" => Success($"<Block path=\"{ReadField(line, "blockPath")}\" format=\"{ReadField(line, "format")}\"/>"),
                "get_type_content" => Success($"TYPE \"{ReadField(line, "typePath")}\" format={ReadField(line, "format")}"),
                "list_tag_tables" => Success(ToCamelCaseJson(new PlcTagInventoryInfo
                {
                    Plcs =
                    {
                        new PlcTagInventoryPlcInfo
                        {
                            PlcName = "PLC_1",
                            DeviceName = "PLC_1_Device",
                            // Echoes the forwarded narrowing so the host test can see it reached the worker.
                            Tables = ReadField(line, "tableName") is { } tableName
                                ? new List<TagTableInfo> { new() { Name = tableName, FolderPath = ReadField(line, "folderPath") ?? "/" } }
                                : new List<TagTableInfo>(),
                        },
                    },
                })),
                _ => $$"""{"success":false,"error":"unexpected plc read method '{{ReadMethod(line)}}'"}"""
            });
            break;
        case "xref-roundtrip":
            Respond(ReadMethod(line) != "read_cross_references"
                ? $$"""{"success":false,"error":"unexpected xref method '{{ReadMethod(line)}}'"}"""
                : LastXrefSegmentName(line) == "Unsupported"
                    ? """{"success":false,"failureCategory":"target_kind_unsupported","error":"The selected target does not provide cross-references."}"""
                    : Success(ToCamelCaseJson(XrefReport(1, 10, ReadField(line, "crossReferenceFilter") != "UnusedObjects"))));
            break;
        case "xref-oversized":
            // 40 sources of ~3,000 characters exceed the 60,000-character value budget.
            Respond(Success(ToCamelCaseJson(XrefReport(40, 3_000, true))));
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
                ? Success(ToCamelCaseJson(ConnectionEvidenceHardwareConfig(
                    currentProjectPath?.Contains("degraded", StringComparison.Ordinal) == true)))
                : """{"success":false,"error":"expected ordinary hardware read"}""");
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

        case "project-enumeration-completeness":
            Respond(ReadMethod(line) == "read_hardware_config"
                ? Success(ToCamelCaseJson(ProjectCompletenessHardware()))
                : $$"""{"success":false,"error":"unexpected project completeness method '{{ReadMethod(line)}}'"}""");
            break;
        case "project-tree-v3-snapshot":
            Respond(ReadMethod(line) == "browse_project_tree_v3_snapshot"
                && HasNonNullField(line, "startSelector")
                ? SuccessWithResolvedPath(
                    ToCamelCaseJson(ProjectTreeV3Snapshot()),
                    ReadField(line, "projectPath") ?? scenario)
                : $$"""{"success":false,"error":"expected typed browse_project_tree_v3_snapshot"}""");
            break;
        case "project-tree-v3-small":
        case "project-tree-v3-counted":
        case "project-tree-v3-one-shot":
            Respond(ProjectTreeV3ScenarioResponse(line, scenario));
            break;
        case "project-tree-v3-malformed":
            Respond(ReadMethod(line) == "browse_project_tree_v3_snapshot"
                ? Success("""{"startSelector":null,"depth":null,"roots":{"PROJECT_TREE_SECRET_MARKER":true}}""")
                : $$"""{"success":false,"error":"expected browse_project_tree_v3_snapshot, got '{{ReadMethod(line)}}'"}""");
            break;
        case "hardware-pagination":
            // The host owns cursor authentication, binding, and public projection. This scenario
            // deliberately mirrors only the internal typed candidate seam: duplicate device names,
            // stable nested-group diagnostics, and device-first/subnet-second offsets make every
            // public reconstruction assertion independent of the production worker implementation.
            Respond(ReadMethod(line) == "read_hardware_page_candidates"
                ? HardwarePaginationResponse(line)
                : ReadMethod(line) == "read_hardware_config"
                    ? Success(ToCamelCaseJson(HardwarePaginationUnpaged()))
                    : $$"""{"success":false,"error":"expected read_hardware_page_candidates or read_hardware_config, got '{{ReadMethod(line)}}'"}""");
            break;
        case "hardware-pagination-missing-identity":
            Respond(HardwarePaginationResponse(line), includeSessionIdentity: false);
            break;
        case "hardware-pagination-payload-only-identity":
            Respond(
                """{"success":true,"payload":"{\"sessionIdentity\":{\"workerSessionId\":\"payload-only\",\"sessionGeneration\":99,\"portalProcessId\":999,\"projectPath\":\"payload-only\"}}"}""",
                includeSessionIdentity: false);
            break;
        case "hardware-pagination-missing-continuation-identity":
            Respond(
                HardwarePaginationResponse(line),
                includeSessionIdentity:
                    NextHardwarePaginationScenarioCall("hardware-pagination-missing-continuation-identity") == 1);
            break;
        case "hardware-pagination-binding-conflict":
            if (NextHardwarePaginationScenarioCall("hardware-pagination-binding-conflict") == 1)
            {
                Respond(HardwarePaginationResponse(line));
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
                ? HardwarePaginationResponse(line)
                : $$"""{"success":false,"error":"expected read_hardware_page_candidates, got '{{ReadMethod(line)}}'"}""");
            break;
        case "hardware-pagination-wrong-payload":
            Respond(ReadMethod(line) == "read_hardware_page_candidates"
                ? """{"success":true,"payload":"{\"privateLocator\":\"not-public\"}"}"""
                : $$"""{"success":false,"error":"expected read_hardware_page_candidates, got '{{ReadMethod(line)}}'"}""");
            break;
        case "hardware-pagination-snapshot-drift":
        case "hardware-pagination-out-of-range":
            Respond(HardwarePaginationDerivedContinuationResponse(line));
            break;
        case "hardware-pagination-identity-drift":
            hardwarePaginationIdentityDrift = NextHardwarePaginationScenarioCall("hardware-pagination-identity-drift") > 1;
            Respond(HardwarePaginationResponse(line));
            hardwarePaginationIdentityDrift = false;
            break;
        case "hardware-pagination-trimming":
        case "hardware-pagination-telemetry":
            Respond(ReadMethod(line) == "read_hardware_page_candidates"
                ? HardwarePaginationResponse(line)
                : $$"""{"success":false,"error":"expected read_hardware_page_candidates, got '{{ReadMethod(line)}}'"}""");
            break;
        case "network-unresolvable-target":
            // A contract-valid, empty HardwareConfigInfo: no device can ever match a
            // configure_network_device target here, so a preview against this scenario proves
            // NetworkIdentityResolver's fail-closed path issues no safety token.
            Respond(Success(ToCamelCaseJson(new HardwareConfigInfo
                { DiscoveryEvidence = new() { Scope = "project", Complete = true } })));
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
                "read_hardware_config" => Success(ToCamelCaseJson(new HardwareConfigInfo
                    { DiscoveryEvidence = new() { Scope = "project", Complete = true } })),
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
        case "type-content-ordered-protocol-failure":
            Respond(ReadMethod(line) switch
            {
                "get_project_status" => """{"success":true,"payload":"{\"isOpen\":true}"}""",
                "get_type_content" => """{"success":true,"payload":"TYPE AnalogInputSettings STRUCT Value : Real; END_STRUCT END_TYPE"}""",
                "update_type_content" => ++orderedTypeWriteCount switch
                {
                    1 => """{"success":true,"payload":"{}"}""",
                    2 => "this is not json",
                    _ => """{"success":false,"error":"third write should have been skipped"}"""
                },
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
                ("update_block_logic", "source") => BlockOutcomeResponse(
                    true,
                    null,
                    "{}",
                    CompletedBlockOutcome(SourceFormatNames.Source, "unavailable")),
                var other => $$"""{"success":false,"error":"expected format 'source' for both methods, got method '{{other.Item1}}' with format '{{other.Item2}}'"}"""
            });
            break;
        case "direct-status-only":
            // Used to prove the direct get_project_status MCP tool routes through the
            // GetProjectStatusAsync operation only, never the internal lifecycle probe.
            Respond(ReadMethod(line) == "get_project_status"
                ? Success(DirectStatusPayload(new ProjectStatusInfo { IsOpen = true, Path = currentProjectPath }))
                : $$"""{"success":false,"error":"expected get_project_status, got '{{ReadMethod(line)}}'"}""");
            break;
        case "compile-passed":
        case "compile-errors":
        case "compile-warning":
        case "compile-unknown":
        case "compile-unavailable":
        case "compile-attempt-failure":
        case "compile-malformed":
        case "compile-inconsistent":
        case "compile-oversized":
        case "compile-arguments":
        case "compile-identity-drift":
            hardwarePaginationIdentityDrift = scenario == "compile-identity-drift" && ReadMethod(line) == "compile_check";
            Respond(ReadMethod(line) switch
            {
                "get_project_status" => Success(DirectStatusPayload(new ProjectStatusInfo { IsOpen = true, Path = currentProjectPath })),
                "read_hardware_config" => Success(ToCamelCaseJson(new HardwareConfigInfo())),
                "compile_check" => StandaloneCompileResponse(scenario, line),
                _ => $$"""{"success":false,"error":"unexpected standalone compile method '{{ReadMethod(line)}}'"}"""
            });
            hardwarePaginationIdentityDrift = false;
            break;
        case "status-no-project":
            // Simulates the real worker's GetStatusReadOnly when nothing is open and no path
            // was requested: isOpen:false, no resolvedProjectPath - nothing was opened.
            Respond(Success(ToCamelCaseJson(new ProjectStatusResultInfo
            {
                Operation = "get_project_status",
                Project = new ProjectStatusInfo { IsOpen = false }
            })));
            break;
        case "status-with-metadata":
            // Simulates the real worker's GetStatusReadOnly WITH the extended metadata surface,
            // exercising every collection/section of ProjectMetadataInfo so host-side contract
            // tests can assert the full additive metadata schema over the real IPC pipe. Built
            // from the shared Contracts DTO so a contract change here is a compile error, never
            // a silently stale hand-written literal.
            Respond(ReadMethod(line) == "get_project_status"
                ? Success(DirectStatusPayload(StatusWithMetadataFixture()))
                : $$"""{"success":false,"error":"expected get_project_status, got '{{ReadMethod(line)}}'"}""");
            break;
        case "status-malformed":
            Respond(Success("{\"PRIVATE_STATUS_MARKER\":\"" + new string('x', 70000) + "\"}"));
            break;
        case "status-oversized":
        {
            var status = StatusWithMetadataFixture();
            status.Metadata!.Comment!.Translations![0].Text = new string('x', 70_000);
            var oversizedPayload = DirectStatusPayload(status);
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
        case @"C:\FakeWorker\lifecycle-rebind-probe.ap21":
        case @"C:\FakeWorker\lifecycle-rebind-probe-missing-source.ap21":
        case @"C:\FakeWorker\lifecycle-rebind-probe-null-modified.ap21":
        case @"C:\FakeWorker\lifecycle-rebind-probe-wrong-destination.ap21":
        case @"C:\FakeWorker\lifecycle-rebind-probe-wrong-disposition.ap21":
        case @"C:\FakeWorker\lifecycle-rebind-probe-binding-conflict.ap21":
            // One persistent process first verifies source A, then handles the internal probe.
            // The destination travels only in rebindDestinationProjectPath, never projectPath.
            if (ReadMethod(line) == "get_project_status")
            {
                Respond(Success(ToCamelCaseJson(new { isOpen = true, openProjectCalls = lifecycleRebindOpenProjectCalls })));
            }
            else if (ReadMethod(line) == "get_basic_project_status")
            {
                Respond(Success("{\"isOpen\":true}"));
            }
            else if (ReadMethod(line) == "probe_project_status_for_lifecycle")
            {
                Respond(Success("{\"isOpen\":true}"));
            }
            else if (ReadMethod(line) == "open_project")
            {
                lifecycleRebindOpenProjectCalls++;
                Respond(SuccessWithResolvedPath("{\"isOpen\":true}", currentProjectPath!));
            }
            else
            {
                Respond(RebindProbeResponse(line, currentProjectPath!));
            }
            break;
        case "save-as-uncertain-state":
            // Simulates the real worker's postcondition_failed when save_project_as saved a copy
            // but could not confirm the active project is that copy: a failure carrying the
            // uncertain-state warning. The unbound status bootstrap and the protected lifecycle
            // probe succeed so a registered save-as preview can issue its safety token; only the
            // protected save-as apply returns the failure and retains its verified source binding.
            Respond(ReadMethod(line) is "probe_project_status_for_lifecycle" ||
                    (ReadMethod(line) == "get_project_status" && currentExpectedSessionIdentity is null)
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
                var subnet = SelectableSubnet("PN/IE", "subnet-1", "Ethernet", "System:Subnet.Ethernet", Array.Empty<IoSystemInfo>(), Array.Empty<string>());
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
                    if (scenario == "network-qualified-final-binding-drift") fakeSessionGeneration++;
                    if (scenario == "network-qualified-final-repeat-interface-drift" && guardedNetworkWrites > 1) qualifiedDevice.Items[0].Items[0].NetworkInterfaces[0].Name = "Changed service";
                    if (scenario == "network-qualified-final-interface-drift") qualifiedDevice.Items[0].Items[0].NetworkInterfaces[0].Name = "Changed service";
                    NetworkNodeReadSelectorBuilder.ApplyInventory(qualifiedHardware);
                }
                Respond(Success(ToCamelCaseJson(qualifiedHardware)));
            }
            else if (ReadMethod(line) == "list_network_objects")
                Respond(Success(ToCamelCaseJson(new NetworkObjectListInfo { TotalCount = 2, ReturnedCount = 2,
                    Items = qualifiedNodes.Select(node => new NetworkObjectSummaryInfo { Kind = NetworkObjectKinds.Node,
                        Selectable = node.Selectable, Selector = node.Selector,
                        Evidence = new() { NodeName = node.Name, Name = node.Name } }).ToList() })));
            else if (ReadMethod(line) == "inspect_network_object")
            {
                var selectedTarget = JsonSerializer.Deserialize<WorkerRequest>(line, requestJsonOptions)!.NetworkObjectTarget!;
                var selectedOwner = NetworkInterfacePathMatcher.Match(qualifiedDevice.Items, selectedTarget.InterfacePath!,
                    item => item.Items, item => item.Name, item => item.PositionNumber, item => item.TypeIdentifier);
                var selectedInterface = selectedOwner.Success ? selectedOwner.Item!.NetworkInterfaces.Single() : null;
                var selectedNode = selectedInterface is null ? null : NetworkNodeReadSelectorBuilder.MatchNode(
                    selectedInterface.Nodes, selectedTarget.NodeId, selectedTarget.NodeIndex, node => node.NodeId);
                if (!string.Equals(selectedTarget.DeviceName, qualifiedDevice.Name, StringComparison.OrdinalIgnoreCase)
                    || selectedInterface is null || selectedNode?.Success != true
                    || (selectedTarget.InterfaceName is not null && !string.Equals(selectedTarget.InterfaceName, selectedInterface.Name, StringComparison.Ordinal)))
                    Respond(ToCamelCaseJson(new WorkerResponse { Success = false, FailureCategory = WorkerFailureCategories.TargetEvidenceMismatch,
                        Error = "Qualified fixture selector did not match." }));
                else Respond(Success(ToCamelCaseJson(new NetworkObjectInspectionInfo { Target = selectedNode.Item!.Selector!,
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
                Respond(Success(ToCamelCaseJson(new SubnetLifecycleResultInfo { SubnetId = "subnet-1", Name = "PN/IE", NetworkDeviceCount = 1, NetworkDeviceCountUnchanged = true,
                    Verification = FakePassedVerification(new() { ["subnetId"] = "subnet-1" }, new() { ["subnetAbsent"] = "true", ["affectedNodesPreserved"] = "true", ["affectedConnectionsRemoved"] = "true", ["networkDeviceCountUnchanged"] = "1" }) })));
            }
            else Respond("""{"success":false,"error":"unsupported qualified-read fixture operation"}""");
            break;

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
            guardedNetworkState ??= ConnectionEvidenceHardwareConfig(scenario.StartsWith("network-guarded-incomplete", StringComparison.Ordinal));
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
            if ((scenario is "network-guarded-partial" or "network-guarded-io-move" || scenario.StartsWith("network-guarded-late-io-", StringComparison.Ordinal)) && guardedNetworkWrites == 0 && guardedNetworkState.Subnets[0].IoSystems.Count == 0)
                guardedNetworkState.Subnets[0].IoSystems.Add(SelectableIoSystem("subnet-1", "IO", 1, "PLC_Grouped"));
            if (scenario == "network-guarded-io-move" && guardedNetworkState.Subnets.Count == 1)
            {
                var other = SelectableSubnet("Other", "subnet-2", "Ethernet", "System:Subnet.Ethernet", Array.Empty<IoSystemInfo>(), Array.Empty<string>());
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
            // text/structuredContent equality, minimal-result shape, audit, and every token
            // tampering path bind against this key.
            Respond(ReadMethod(line) switch
            {
                "read_hardware_config" => Success(ToCamelCaseJson(SubnetLifecycleHardwareConfig(subnetLifecycleState))),
                "inspect_network_object" => InspectSubnetLifecycle(line, subnetLifecycleState),
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
                "read_hardware_config" => Success(ToCamelCaseJson(SubnetLifecycleHardwareConfig(subnetLifecycleState))),
                "inspect_network_object" => InspectSubnetLifecycle(line, subnetLifecycleState),
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
                "read_hardware_config" => Success(ToCamelCaseJson(SubnetLifecycleHardwareConfig(subnetLifecycleState))),
                "inspect_network_object" => InspectSubnetLifecycle(line, subnetLifecycleState),
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

FakePortalState? AttachedPortal()
    => fakePortals.FirstOrDefault(portal => portal.ProcessId == fakePortalProcessId);

void SelectPortalProject(string? requestedPath)
{
    var targetPath = ProjectPathNormalization.Canonicalize(requestedPath);
    var advertisers = fakePortals.Where(portal => targetPath is not null &&
        string.Equals(portal.ProjectPath, targetPath, StringComparison.OrdinalIgnoreCase)).ToList();
    // These three refusals precede every session mutation, matching the real worker's E2 boundary.
    if (advertisers.Count != 1)
    {
        Respond(JsonSerializer.Serialize(new WorkerResponse
        {
            Success = false,
            FailureCategory = advertisers.Count == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
            Error = advertisers.Count == 0 ? "No Portal advertises the requested project." : "Multiple Portals advertise the requested project."
        }, WorkerJson.Envelope));
        return;
    }
    var target = advertisers[0];
    var previous = AttachedPortal();
    var reattached = fakePortalProcessId != target.ProcessId;
    if (reattached && previous is { HasUserInterface: false, OtherClients: 0, Modified: not false })
    {
        Respond(JsonSerializer.Serialize(new WorkerResponse
        {
            Success = false, FailureCategory = WorkerFailureCategories.GuardBlocked,
            Error = "Cannot detach the sole client of a headless Portal with a modified or unknown-state project."
        }, WorkerJson.Envelope));
        return;
    }
    var result = new PortalProjectSelectionInfo
    {
        PreviousProcessId = fakePortalProcessId, PreviousProjectPath = fakeProjectPath,
        PreviousProjectIsModified = fakeProjectPath is null ? null : previous?.Modified,
        PreviousProjectWasWorkerOpened = previous?.WorkerOpened == true,
        Reattached = reattached
    };
    if (reattached)
    {
        if (previous is not null) previous.WorkerOpened = false;
        fakePortalProcessId = null;
        fakeProjectPath = null;
        fakeSessionGeneration++;
    }
    switch (ScenarioKey(targetPath))
    {
        case "portal-switch-fails-after-detach":
            Respond(JsonSerializer.Serialize(new WorkerResponse
            {
                Success = false, FailureCategory = WorkerFailureCategories.WorkerOperationFailed,
                Error = "Scripted Portal attach failure after detach."
            }, WorkerJson.Envelope));
            return;
        case "portal-switch-hang":
            Thread.Sleep(Timeout.Infinite);
            return;
        case "portal-switch-crash":
            Environment.Exit(17);
            return;
    }
    fakePortalProcessId = target.ProcessId;
    fakeProjectPath = target.ProjectPath;
    // Adoption, including a same-Portal selection, does not inherit worker ownership.
    target.WorkerOpened = false;
    fakeSessionGeneration++;
    Respond(Success(ScenarioKey(targetPath) == "portal-selection-malformed"
        ? "{\"untrustedMarker\":true}"
        : WorkerJson.SerializePayload(result)));
}

void InspectMultiuser(WorkerRequest request)
{
    var alias = request.MultiuserServerAlias;
    if (request.ProjectPath is not null
        || alias == "expect-bound" && (request.ExpectedSessionIdentity is null || request.PortalProcessId != fakePortalProcessId)
        || alias == "expect-unbound" && request.ExpectedSessionIdentity is not null)
    {
        Respond(JsonSerializer.Serialize(BindingConflict("Unexpected inspection request identity or projectPath."), WorkerJson.Envelope));
        return;
    }
    if (fakePortalProcessId is { } attached && request.PortalProcessId is { } requested && attached != requested)
    {
        Respond(JsonSerializer.Serialize(BindingConflict("Another Portal is already attached."), WorkerJson.Envelope));
        return;
    }
    if (fakePortalProcessId is null)
    {
        var candidates = fakePortals.Where(p => request.PortalProcessId is null || p.ProcessId == request.PortalProcessId).ToArray();
        if (candidates.Length != 1)
        {
            Respond(JsonSerializer.Serialize(new WorkerResponse { Success = false,
                FailureCategory = candidates.Length == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
                Error = "Select one Portal." }, WorkerJson.Envelope));
            return;
        }
        fakePortalProcessId = candidates[0].ProcessId;
    }
    if (alias == "hang") { Thread.Sleep(Timeout.Infinite); return; }
    if (alias == "crash") { Environment.Exit(17); return; }
    if (alias is "identity-loss" or "server-failure" or "target_not_found" or "target_ambiguous")
    {
        Respond(JsonSerializer.Serialize(new WorkerResponse { Success = false,
            FailureCategory = alias == "identity-loss" ? WorkerFailureCategories.BindingConflict
                : alias == "server-failure" ? WorkerFailureCategories.WorkerOperationFailed : alias,
            Error = "Scripted inventory failure." }, WorkerJson.Envelope));
        return;
    }
    var identity = new MultiuserRemoteIdentity
    {
        ServerAlias = alias ?? "Fixture", Host = "server", Port = 1234,
        Group = request.MultiuserGroupIsRoot is { } root ? new() { IsRoot = root, Name = request.MultiuserGroupName } : null,
        ServerProjectName = request.MultiuserServerProjectName
    };
    var observation = new ProjectServerConnectionObservation
    { State = "connected", ObservationSource = "explicitRead", ObservedAt = DateTimeOffset.Parse("2026-10-05T12:00:00Z") };
    var payload = request.Method switch
    {
        "list_server_connections" => WorkerJson.SerializePayload(new MultiuserServerConnectionsInfo
            { Connections = [new() { ServerAlias = "Fixture", Host = "server", Port = 1234 }] }),
        "list_server_groups" => WorkerJson.SerializePayload(new MultiuserServerGroupsInfo
            { RemoteIdentity = identity, ConnectionObservation = observation, Groups = [new() { IsRoot = false, Name = "Group A" }] }),
        "list_server_projects" => WorkerJson.SerializePayload(new MultiuserServerProjectsInfo
            { RemoteIdentity = identity, ConnectionObservation = observation, Projects = [new() { Name = "Project A" }] }),
        "list_local_sessions" => WorkerJson.SerializePayload(new MultiuserLocalSessionsInfo
            { RemoteIdentity = identity, ConnectionObservation = observation, Sessions = [new() { SessionId = 0, ProjectPath = "C:\\Sessions\\A.als21" }] }),
        "get_lock_state" => WorkerJson.SerializePayload(new MultiuserLockStateInfo
            { RemoteIdentity = identity, ConnectionObservation = observation, IsLocked = false, ObservedAt = observation.ObservedAt }),
        _ => throw new InvalidOperationException()
    };
    Respond(JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = payload, PortalProcessId = fakePortalProcessId }, WorkerJson.Envelope));
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
            UpgradeLegacyLifecycleFixture(response);
            var resolvedPath = response["resolvedProjectPath"]?.GetValue<string>();
            var successful = response["success"]?.GetValue<bool>() == true;
            var isAuthorizedPathTransition = successful && currentMethod is
                "open_project" or "create_project" or "save_project_as";
            var projectPath = successful && string.Equals(currentMethod, "close_project", StringComparison.Ordinal)
                ? null
                : isAuthorizedPathTransition
                    ? ProjectPathNormalization.Canonicalize(resolvedPath ?? currentProjectPath ?? fakeProjectPath)
                    : fakeProjectPath;

            if (successful && string.Equals(currentMethod, "close_project", StringComparison.Ordinal))
            {
                if (fakeProjectPath is not null)
                {
                    fakeSessionGeneration++;
                }

                fakeProjectPath = null;
                var closedPortal = AttachedPortal();
                if (closedPortal is not null)
                {
                    closedPortal.ProjectPath = null;
                    closedPortal.WorkerOpened = false;
                    closedPortal.Modified = false;
                }
                response["resolvedProjectPath"] = null;
            }
            else if (successful && projectPath is not null)
            {
                if (isAuthorizedPathTransition &&
                    !string.Equals(fakeProjectPath, projectPath, StringComparison.OrdinalIgnoreCase))
                {
                    fakeSessionGeneration++;
                }

                if (isAuthorizedPathTransition)
                {
                    if (fakePortalProcessId is null)
                    {
                        fakePortalProcessId = fakePortals.FirstOrDefault()?.ProcessId ?? 4242;
                        if (fakePortals.Count == 0)
                            fakePortals.Add(new FakePortalState { ProcessId = fakePortalProcessId.Value, HasUserInterface = true });
                        fakeSessionGeneration++;
                    }
                    var portal = AttachedPortal()!;
                    if (currentMethod is "open_project" or "create_project" &&
                        !string.Equals(fakeProjectPath, projectPath, StringComparison.OrdinalIgnoreCase))
                    {
                        portal.WorkerOpened = true;
                        if (currentMethod == "open_project")
                            portal.Modified = JsonSerializer.Deserialize<ProjectLifecycleResultInfo>(
                                response["payload"]?.GetValue<string>() ?? "{}", WorkerJson.Envelope)
                                ?.Project?.IsModified == true;
                    }
                    portal.ProjectPath = projectPath;
                    if (currentMethod is "create_project" or "save_project_as") portal.Modified = false;
                    fakeProjectPath = projectPath;
                }
            }

            if (successful && currentMethod == "save_project" && AttachedPortal() is { } savedPortal)
                savedPortal.Modified = false;

            response["sessionIdentity"] = JsonSerializer.SerializeToNode(new WorkerSessionIdentity
            {
                WorkerSessionId = hardwarePaginationIdentityDrift ? "drifted-worker-session" : workerSessionId,
                SessionGeneration = fakeSessionGeneration,
                PortalProcessId = fakePortalProcessId,
                ProjectPath = successful && currentMethod == "select_portal_project"
                    && ScenarioKey(currentProjectPath) == "portal-selection-other-path"
                    ? ProjectPathNormalization.Canonicalize("C:/Projects/Wrong.ap21") : projectPath
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

void UpgradeLegacyLifecycleFixture(JsonObject response)
{
    if (response["success"]?.GetValue<bool>() != true
        || currentMethod is not ("open_project" or "create_project" or "save_project" or "save_project_as"
            or "archive_project" or "close_project" or "probe_project_status_for_lifecycle" or "get_basic_project_status")) return;
    var payload = response["payload"]?.GetValue<string>();
    if (payload is null || JsonNode.Parse(payload) is not JsonObject root
        || root.Any(property => property.Key != "isOpen")) return;
    var path = ProjectPathNormalization.Canonicalize(response["resolvedProjectPath"]?.GetValue<string>() ?? currentProjectPath ?? fakeProjectPath);
    if (currentMethod == "save_project_as" && ScenarioKey(currentProjectPath) == "lifecycle-probe-only")
    {
        var directory = Path.Combine(ReadField(currentRequestLine!, "targetDirectory")!, ReadField(currentRequestLine!, "targetName")!);
        path = Path.Combine(directory, "Copy.ap21");
        response["resolvedProjectPath"] = path;
        lifecycleProbeOnlyCopiedPath = path;
    }
    var status = new ProjectStatusInfo
    {
        IsOpen = currentMethod != "close_project", Path = path,
        IsModified = currentMethod == "close_project" ? null : false
    };
    response["payload"] = WorkerJson.SerializePayload(new ProjectLifecycleResultInfo
    {
        Operation = currentMethod == "get_basic_project_status" ? "get_project_status" : currentMethod!,
        ProjectPath = path, Project = status
    });
    response["resolvedProjectPath"] = currentMethod == "close_project" ? null : path;
}

string GuardedLifecycleResponse(string requestLine, string fixture)
{
    var method = ReadMethod(requestLine);
    var mutation = method is "open_project" or "create_project" or "save_project" or "save_project_as" or "archive_project" or "close_project";
    if (mutation && fixture.Contains("-worker-failure", StringComparison.OrdinalIgnoreCase))
        return JsonSerializer.Serialize(new WorkerResponse { Success = false, FailureCategory = WorkerFailureCategories.WorkerOperationFailed, Error = "Scripted lifecycle mutation failure." }, WorkerJson.Envelope);
    var path = ProjectPathNormalization.Canonicalize(currentProjectPath ?? fakeProjectPath);
    if (method == "probe_open_project_rebind")
        return Success(WorkerJson.SerializePayload(ProjectRebindStateInfo.Create(fakeProjectPath,
            ReadField(requestLine, "rebindDestinationProjectPath")!,
            fakeProjectPath is null ? null : guardedLifecycleModified,
            fakeProjectPath is not null && fixture.Contains("-ui-owned", StringComparison.OrdinalIgnoreCase) != true)));
    if (method == "create_project")
    {
        var directory = Path.Combine(ReadField(requestLine, "projectDirectory")!, ReadField(requestLine, "projectName")!);
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, ReadField(requestLine, "projectName")! + ".ap21");
        File.WriteAllText(path, "FakeWorker lifecycle artifact");
    }
    if (method == "save_project_as")
    {
        var directory = Path.Combine(ReadField(requestLine, "targetDirectory")!, ReadField(requestLine, "targetName")!);
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, Path.GetFileName(fakeProjectPath ?? "Project.ap21"));
        File.WriteAllText(path, "FakeWorker copied lifecycle artifact");
    }
    if (method is "save_project" or "save_project_as" or "create_project") guardedLifecycleModified = false;
    if (method == "archive_project")
    {
        ArchiveModeNames.TryNormalize(ReadField(requestLine, "archiveMode"), out var archiveMode, out _);
        var name = ArchiveModeNames.EnsureArchiveExtension(ReadField(requestLine, "archiveName")!, archiveMode);
        var target = Path.Combine(ReadField(requestLine, "archiveDirectory")!, name);
        if (archiveMode is ArchiveModeNames.Compressed or ArchiveModeNames.DiscardRestorableDataAndCompressed)
            File.WriteAllText(target, "FakeWorker archived lifecycle artifact");
        else Directory.CreateDirectory(target);
        using var request = JsonDocument.Parse(requestLine);
        if (request.RootElement.TryGetProperty("saveBeforeArchive", out var save) && save.ValueKind == JsonValueKind.True)
            guardedLifecycleModified = false;
    }
    var noProject = method == "get_basic_project_status" && fakeProjectPath is null;
    var status = new ProjectStatusInfo
    {
        IsOpen = method != "close_project" && !noProject,
        Path = noProject ? null : path,
        Name = noProject ? null : Path.GetFileNameWithoutExtension(path),
        IsModified = noProject || method == "close_project" ? null : guardedLifecycleModified,
        Version = noProject ? null : "V21",
        Author = fixture.Contains("-oversized", StringComparison.OrdinalIgnoreCase) ? new string('x', 70_000) : null
    };
    if (method == "get_project_status") return Success(DirectStatusPayload(status));
    var payload = WorkerJson.SerializePayload(new ProjectLifecycleResultInfo
    {
        Operation = method == "get_basic_project_status" ? "get_project_status" : method!,
        ProjectPath = status.Path, Project = status
    });
    if (fixture.Contains("-document-limit", StringComparison.OrdinalIgnoreCase))
    {
        // Each outcome fits individually; three copies plus the response envelope exceed its
        // document budget. This exercises omission of a complete value after composition.
        status.Author = new string('x', Math.Max(0, 59_999 - payload.Length));
        payload = WorkerJson.SerializePayload(new ProjectLifecycleResultInfo
        {
            Operation = method == "get_basic_project_status" ? "get_project_status" : method!,
            ProjectPath = status.Path, Project = status
        });
    }
    if (mutation && fixture.Contains("-malformed", StringComparison.OrdinalIgnoreCase)
        || method == "get_basic_project_status" && fixture.Contains("-verification-failure", StringComparison.OrdinalIgnoreCase))
        payload = "{\"untrustedMarker\":true}";
    return JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = payload,
        ResolvedProjectPath = method == "close_project" || noProject ? null : path,
        Warnings = mutation && fixture.Contains("-oversized", StringComparison.OrdinalIgnoreCase)
            ? new List<string> { new string('w', 190_000), "retained mutation warning" } : new List<string>() }, WorkerJson.Envelope);
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
    var activePath = ProjectPathNormalization.Canonicalize(fakeProjectPath);

    if (string.IsNullOrWhiteSpace(expected.WorkerSessionId) ||
        expected.SessionGeneration < 0 ||
        expected.PortalProcessId is null ||
        expected.PortalProcessId <= 0 ||
        !string.Equals(expectedPath, activePath, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(
            expected.WorkerSessionId,
            workerSessionId,
            StringComparison.Ordinal) ||
        expected.SessionGeneration != fakeSessionGeneration ||
        expected.PortalProcessId != fakePortalProcessId)
    {
        return BindingConflict(
            "The expected worker/Portal/project session identity does not match the FakeWorker session.");
    }

    var establishesProject = method is "open_project" or "create_project" or "select_portal_project"
        or "list_tia_portal_processes";
    var permitsNoSource = method is "get_project_status" or "get_basic_project_status"
        or "probe_project_status_for_lifecycle" or "probe_open_project_rebind";
    var requestedPath =
        ProjectPathNormalization.Canonicalize(requestedProjectPath);

    if (!establishesProject && !(permitsNoSource && activePath is null) &&
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
    if (path is not null && lifecycleProbeOnlyCopiedPath is not null
        && string.Equals(path, lifecycleProbeOnlyCopiedPath, StringComparison.OrdinalIgnoreCase))
        return "lifecycle-probe-only";
    if (path is not null && path.EndsWith(".ap21", StringComparison.OrdinalIgnoreCase))
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name is "portal-switch-fails-after-detach" or "portal-switch-hang" or "portal-switch-crash"
            or "portal-selection-malformed" or "portal-selection-other-path") return name;
        if (name.StartsWith("lifecycle-rebind-probe", StringComparison.Ordinal)) return @"C:\FakeWorker\" + name + ".ap21";
        if (name is "lifecycle-probe-only" or "worker-error-with-category" or "save-as-uncertain-state") return name;
        if (name == "network-roundtrip") return name;
        if (path.EndsWith(@"\open\Line.ap21", StringComparison.OrdinalIgnoreCase)) return @"C:\open\Line.ap21";
        if (name == "B-ui-owned") return @"C:\Lifecycle\B-ui-owned.ap21";
    }
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

// Exact-route fixtures reject wrong selectors before returning each operation's typed payload.
string TagSafetyBehaviorResponse(string requestLine, string scenario)
{
    var method = ReadMethod(requestLine);
    if (method == "get_project_status")
    {
        // Read-only observation: bootstrap and assertions never advance the snapshot phase.
        return Success(ToCamelCaseJson(new
        {
            isOpen = true,
            snapshotReadCount = tagSafetySnapshotReadCount,
            mutationCount = tagSafetyMutationCount,
            snapshotReadsAtMutation = tagSafetySnapshotReadsAtMutation,
            broadReadCount = tagSafetyBroadReadCount,
            targetExists = tagSafetyTargetExists,
            targetTagName = tagSafetyTargetTagName,
            siblingTableName = tagSafetySiblingTag.TableName,
            siblingTagName = tagSafetySiblingTag.TagName
        }));
    }
    if (method == "list_tag_tables")
    {
        tagSafetyBroadReadCount++;
        return """{"success":false,"error":"wrong route: list_tag_tables"}""";
    }

    var expectedWrite = scenario switch
    {
        "tag-safety-same-object-drift" or "tag-safety-authorized-apply" => "update_tag",
        "tag-safety-collision-drift" => "create_tag",
        "tag-safety-unrelated-sibling" => "delete_tag",
        "tag-safety-delete-table-export-drift" => "delete_tag_table",
        "tag-safety-reread" => "create_user_constant",
        _ => throw new InvalidOperationException("Unknown tag safety behavior scenario.")
    };
    var requestedName = ReadField(requestLine, "name");
    if (ReadField(requestLine, "plcName") != "PLC_1" ||
        ReadField(requestLine, "tableName") != "Inputs" ||
        ReadField(requestLine, "folderPath") is not (null or "" or "/") ||
        (expectedWrite is "update_tag" or "delete_tag" && requestedName != "Start") ||
        (expectedWrite == "update_tag" && ReadField(requestLine, "newName") != "Start_1") ||
        (expectedWrite == "create_tag" && (requestedName is not ("Start_1" or "AddressOnly") ||
            ReadField(requestLine, "dataType") != "Bool" || ReadField(requestLine, "logicalAddress") != "%I0.1")) ||
        (expectedWrite == "create_user_constant" && requestedName != "DebounceMs"))
    {
        return """{"success":false,"error":"wrong tag safety behavior selector"}""";
    }
    if (method == expectedWrite)
    {
        // Count every mutation attempt, including one the host should have rejected.
        tagSafetyMutationCount++;
        tagSafetySnapshotReadsAtMutation = tagSafetySnapshotReadCount;
        if (expectedWrite == "update_tag")
            tagSafetyTargetTagName = ReadField(requestLine, "newName")!;
        if (expectedWrite is "delete_tag" or "delete_tag_table")
            tagSafetyTargetExists = false;
        return Success("{}");
    }
    if (method != $"read_{expectedWrite}_safety_snapshot")
        return """{"success":false,"error":"unexpected tag safety behavior method"}""";

    // Only an exact selector read advances state. The second read is apply, before mutation.
    var applyPhase = ++tagSafetySnapshotReadCount > 1;
    var table = new TagTableSafetyIdentityInfo("PLC_1", "/", "Inputs", "PLC_1/Tag tables/Inputs");
    var tag = new TagSafetyIdentityInfo("PLC_1", "/", "Inputs", tagSafetyTargetTagName,
        table.CanonicalPath + "/" + tagSafetyTargetTagName, "Bool", "%I0.0", true, true, false);
    var empty = Array.Empty<TagCollisionProbeInfo>();
    object payload;
    switch (scenario)
    {
        case "tag-safety-same-object-drift":
        case "tag-safety-authorized-apply":
            if (applyPhase && scenario == "tag-safety-same-object-drift")
                tag = tag with { ExternalVisible = false };
            payload = new UpdateTagSafetySnapshotInfo(table, tag, "Start_1", "%I0.0", empty,
                new[] { new TagCollisionProbeInfo("logical-address", tag.TagName, tag.CanonicalPath, "%I0.0", true) });
            break;
        case "tag-safety-collision-drift":
            // Independent variants: same name at another address, or another name at the same address.
            var nameCollisions = applyPhase && requestedName == "Start_1"
                ? new[] { new TagCollisionProbeInfo("tag-name", "Start_1", table.CanonicalPath + "/Start_1", "%I0.2", false) }
                : empty;
            var addressCollisions = applyPhase && requestedName == "AddressOnly"
                ? new[] { new TagCollisionProbeInfo("logical-address", "Other", table.CanonicalPath + "/Other", "%I0.1", false) }
                : empty;
            payload = new CreateTagSafetySnapshotInfo(table, requestedName!, "%I0.1", nameCollisions, addressCollisions);
            break;
        case "tag-safety-unrelated-sibling":
            if (applyPhase)
            {
                // This Outputs-table tag would appear in the old broad inventory. It is outside
                // the Inputs/Start selector, so the exact target snapshot remains byte-identical.
                tagSafetySiblingTag = tagSafetySiblingTag with
                {
                    TagName = "After", CanonicalPath = "PLC_1/Tag tables/Outputs/After"
                };
            }
            payload = new DeleteTagSafetySnapshotInfo(table, tag);
            break;
        case "tag-safety-delete-table-export-drift":
            var xml = $"<Document><SW.Tags.PlcTagTable><Name>Inputs</Name><Comment>{(applyPhase ? "B" : "A")}</Comment></SW.Tags.PlcTagTable></Document>";
            var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(xml))).ToLowerInvariant();
            payload = new DeleteTagTableSafetySnapshotInfo(table, xml, digest, xml.Length);
            break;
        case "tag-safety-reread":
            var constantCollisions = applyPhase
                ? new[] { new TagCollisionProbeInfo("user-constant-name", "DebounceMs", table.CanonicalPath + "/DebounceMs", null, false) }
                : empty;
            payload = new CreateUserConstantSafetySnapshotInfo(table, "DebounceMs", constantCollisions);
            break;
        default:
            throw new InvalidOperationException("Unknown tag safety snapshot scenario.");
    }
    return Success(ToCamelCaseJson(payload));
}

string TagSafetyRouteResponse(string requestLine, bool malformed, bool invalidCollision = false)
{
    var method = ReadMethod(requestLine);
    if (ReadField(requestLine, "plcName") != "PLC_1" ||
        ReadField(requestLine, "tableName") != "Inputs" ||
        ReadField(requestLine, "folderPath") != "Area")
    {
        return """{"success":false,"error":"wrong tag safety selector"}""";
    }
    var table = new TagTableSafetyIdentityInfo("PLC_1", "Area", "Inputs", "PLC_1/Area/Inputs");
    var tag = new TagSafetyIdentityInfo("PLC_1", "Area", "Inputs", "Start", "PLC_1/Area/Inputs/Start", "Bool", "%I0.0", true, true, false);
    var constant = new UserConstantSafetyIdentityInfo("PLC_1", "Area", "Inputs", "Start", "PLC_1/Area/Inputs/Start", "Bool", "true");
    var collisions = Array.Empty<TagCollisionProbeInfo>();
    object? payload = method switch
    {
        "read_create_tag_table_safety_snapshot" => new CreateTagTableSafetySnapshotInfo("PLC_1", "Area", "Inputs", collisions),
        "read_delete_tag_table_safety_snapshot" => new DeleteTagTableSafetySnapshotInfo(table, "<Document />", new string('a', 64), 12),
        "read_create_tag_safety_snapshot" when ReadField(requestLine, "logicalAddress") == "%I1.0" && ReadField(requestLine, "dataType") == "Bool"
            => new CreateTagSafetySnapshotInfo(table, "Start", "%I1.0", collisions, collisions),
        "read_update_tag_safety_snapshot" when ReadField(requestLine, "newName") == "Run" && ReadField(requestLine, "logicalAddress") == "%I1.0"
            => new UpdateTagSafetySnapshotInfo(table, tag, "Run", "%I1.0", collisions, collisions),
        "read_delete_tag_safety_snapshot" => new DeleteTagSafetySnapshotInfo(table, tag),
        "read_create_user_constant_safety_snapshot" => new CreateUserConstantSafetySnapshotInfo(table, "Start", collisions),
        "read_update_user_constant_safety_snapshot" => new UpdateUserConstantSafetySnapshotInfo(table, constant, "Start", collisions),
        "read_delete_user_constant_safety_snapshot" => new DeleteUserConstantSafetySnapshotInfo(table, constant),
        _ => null
    };
    if (payload is null || (!method!.Contains("tag_table", StringComparison.Ordinal) && ReadField(requestLine, "name") != "Start"))
    {
        return """{"success":false,"error":"wrong route or effective tag safety selector"}""";
    }
    if (invalidCollision && payload is UpdateTagSafetySnapshotInfo update)
    {
        payload = update with
        {
            NameCollisions = new[]
            {
                new TagCollisionProbeInfo("PRIVATE_SNAPSHOT_SENTINEL", "Run", "PLC_1/Area/Inputs/Run", null, false)
            }
        };
    }
    return Success(malformed ? "{}" : ToCamelCaseJson(payload));
}

// Wraps a payload document as a successful worker response. Serializing beats hand-escaping once a
// payload is more than a few members: the escaping is what a hand-written literal gets wrong, and a
// mis-escaped payload would fail the strict Network contract for the wrong reason.
string Success(string payload) => JsonSerializer.Serialize(new { success = true, payload });

string BlockOutcomeResponse(
    bool success,
    string? category,
    string message,
    BlockImportOutcomeInfo outcome)
    => JsonSerializer.Serialize(new WorkerResponse
    {
        Success = success,
        Payload = success ? message : null,
        FailureCategory = category,
        Error = success ? null : message,
        Warnings = success
            ? null
            : new List<string>
            {
                "Project state may have changed; inspect the project before retrying."
            },
        BlockImportOutcome = outcome
    }, WorkerJson.Envelope);

BlockImportOutcomeInfo CompletedBlockOutcome(
    string format,
    string compileStage,
    bool partialReport = false)
{
    CompileCheckReport? report = null;
    if (compileStage is "succeeded" or "failed" || partialReport)
    {
        var failed = compileStage == "failed";
        report = new CompileCheckReport
        {
            Scope = "block",
            BlockPath = "PLC_1/Blocks/Main",
            TotalErrorCount = failed ? 1 : 0,
            TotalWarningCount = partialReport ? 1 : 0,
            OverallState = failed ? "Error" : partialReport ? "Warning" : "Success",
            Plcs = new List<PlcCompileInfo>
            {
                new()
                {
                    PlcName = "PLC_1",
                    DeviceName = "Device_1",
                    State = failed ? "Error" : partialReport ? "Warning" : "Success",
                    ErrorCount = failed ? 1 : 0,
                    WarningCount = partialReport ? 1 : 0,
                    Messages = failed
                        ? new List<CompileMessageInfo>
                        {
                            new() { Description = "Compile failed.", Path = "PLC_1/Blocks/Main", Severity = "Error" }
                        }
                        : new List<CompileMessageInfo>(),
                    DiagnosticNotes = new List<string>()
                }
            }
        };
    }

    return new BlockImportOutcomeInfo
    {
        ImportStage = "completed",
        ImportResultState = compileStage == "failed" ? "non_success" : "success",
        TargetMutationCommitted = true,
        CompileStage = compileStage,
        CompileReport = report,
        CompileDetailsOmitted = partialReport,
        FinalReadStage = "succeeded",
        TargetPresent = true,
        ContentRelation = "unknown",
        TemporarySourceState = format == SourceFormatNames.Xml ? "not_applicable" : "removed"
    };
}

BlockImportOutcomeInfo NotStartedBlockOutcome(string format)
    => new()
    {
        ImportStage = "not_started",
        ImportResultState = "unavailable",
        TargetMutationCommitted = false,
        CompileStage = "not_started",
        FinalReadStage = "not_started",
        ContentRelation = "unknown",
        TemporarySourceState = format == SourceFormatNames.Xml ? "not_applicable" : "not_created"
    };

BlockImportOutcomeInfo AuditFailureBlockOutcome()
{
    var outcome = CompletedBlockOutcome(SourceFormatNames.Source, "unavailable", partialReport: true);
    var messages = Enumerable.Range(1, 20)
        .Select(index => new CompileMessageInfo
        {
            Description = $"Sanitized diagnostic {index:D2}",
            Path = $"PLC_1/Blocks/Item{index:D2}",
            Severity = "Warning"
        })
        .ToList();
    outcome.CompileReport!.Plcs[0].Messages = messages;
    outcome.CompileReport.Plcs[0].WarningCount = messages.Count;
    outcome.CompileReport.TotalWarningCount = messages.Count;
    return outcome with
    {
        ImportResultState = "non_success",
        CompileDetailsOmitted = true
    };
}

BlockImportOutcomeInfo OversizedBlockOutcome()
{
    var escaped = new string('\\', 256);
    return CompletedBlockOutcome(SourceFormatNames.Source, "unavailable", partialReport: true) with
    {
        CompileReport = new CompileCheckReport
        {
            Scope = "block",
            OverallState = "Warning",
            TotalWarningCount = 1,
            Plcs = Enumerable.Range(0, 8).Select(_ => new PlcCompileInfo
            {
                PlcName = escaped,
                DeviceName = escaped,
                State = escaped,
                Messages = new List<CompileMessageInfo>(),
                DiagnosticNotes = new List<string>()
            }).ToList()
        }
    };
}


string RebindProbeResponse(string requestLine, string sourcePath)
{
    const string destinationPath = @"C:\Lifecycle\B.ap21";
    const string modifiedDestinationPath = @"C:\Lifecycle\B-modified.ap21";
    const string uiOwnedDestinationPath = @"C:\Lifecycle\B-ui-owned.ap21";
    const string driftDestinationPath = @"C:\open\Line.ap21";
    var requestedDestination = ReadField(requestLine, "rebindDestinationProjectPath");
    var destinationName = Path.GetFileName(requestedDestination);
    var knownDestination = destinationName is "B.ap21" or "B-modified.ap21" or "B-ui-owned.ap21" or "Line.ap21" ||
        string.Equals(requestedDestination, destinationPath, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(requestedDestination, modifiedDestinationPath, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(requestedDestination, uiOwnedDestinationPath, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(requestedDestination, driftDestinationPath, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(requestedDestination, sourcePath, StringComparison.OrdinalIgnoreCase);
    if (ReadMethod(requestLine) != "probe_open_project_rebind" ||
        !string.Equals(ReadField(requestLine, "projectPath"), sourcePath, StringComparison.OrdinalIgnoreCase) ||
        !knownDestination)
    {
        return JsonSerializer.Serialize(new
        {
            success = false,
            failureCategory = WorkerFailureCategories.ValidationError,
            error = "The rebind probe request did not carry the exact source and destination."
        });
    }

    if (sourcePath.EndsWith("-binding-conflict.ap21", StringComparison.Ordinal))
    {
        return JsonSerializer.Serialize(new
        {
            success = false,
            failureCategory = WorkerFailureCategories.BindingConflict,
            error = "The source session changed before the rebind-state read."
        });
    }

    var isDrift = requestedDestination?.EndsWith(@"\open\Line.ap21", StringComparison.OrdinalIgnoreCase) == true;
    var isModified = destinationName is "B-modified.ap21" or "B-ui-owned.ap21" ||
        string.Equals(requestedDestination, sourcePath, StringComparison.OrdinalIgnoreCase) ||
        (isDrift && ++lifecycleRebindProbeReadCount > 1);
    var workerOwned = destinationName != "B-ui-owned.ap21";
    var state = ProjectRebindStateInfo.Create(sourcePath, requestedDestination!, isModified, workerOwned);
    if (sourcePath.EndsWith("-missing-source.ap21", StringComparison.Ordinal))
    {
        state.SourceProjectPath = null;
    }
    else if (sourcePath.EndsWith("-null-modified.ap21", StringComparison.Ordinal))
    {
        state.SourceIsModified = null;
    }
    else if (sourcePath.EndsWith("-wrong-destination.ap21", StringComparison.Ordinal))
    {
        state.DestinationProjectPath = @"C:\Lifecycle\Other.ap21";
    }
    else if (sourcePath.EndsWith("-wrong-disposition.ap21", StringComparison.Ordinal))
    {
        state.WillCloseSource = false;
    }

    return Success(ToCamelCaseJson(state));
}

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

string? LastXrefSegmentName(string requestLine)
{
    using var doc = JsonDocument.Parse(requestLine);
    return doc.RootElement.TryGetProperty("crossReferenceSelector", out var selector)
        && selector.TryGetProperty("path", out var path) && path.GetArrayLength() > 0
            ? path[path.GetArrayLength() - 1].GetProperty("name").GetString()
            : null;
}

CrossReferenceReport XrefReport(int sources, int nameChars, bool isComplete)
{
    var report = new CrossReferenceReport
    {
        Target = new CrossReferenceSelectorInfo
        {
            Path =
            {
                new ProjectTreeSelectorSegment { NodeType = "Device", Name = "PLC_1_Device" },
                new ProjectTreeSelectorSegment { NodeType = "PlcSoftware", Name = "PLC_1" },
            },
        },
        IsComplete = isComplete,
        OwnerQueryCount = 1,
        SuccessfulOwnerQueryCount = 1,
        Messages = isComplete ? new List<string>() : new List<string> { "One owner could not be read." },
        TotalSourceCount = sources,
        TotalReferenceCount = sources,
        TotalLocationCount = sources,
    };
    for (var i = 0; i < sources; i++)
    {
        report.Sources.Add(new CrossReferenceSourceInfo
        {
            Name = sources == 1 ? "FB_Main" : $"FB_{i}_" + new string('x', nameChars),
            TypeName = "FB",
            References =
            {
                new CrossReferenceTargetInfo
                {
                    Name = "Tag1",
                    Locations = { new CrossReferenceLocationInfo { Access = "Read", ReferenceType = "Uses" } },
                },
            },
        });
    }
    return report;
}

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

bool HasNonNullField(string requestLine, string propertyName)
{
    try
    {
        using var document = JsonDocument.Parse(requestLine);
        return document.RootElement.TryGetProperty(propertyName, out var value)
            && value.ValueKind != JsonValueKind.Null;
    }
    catch (JsonException)
    {
        return false;
    }
}

// Renders a payload exactly as the real worker does (WorkerJson.SerializePayload), from a real
// Contracts DTO: the CLR type decides which members exist, and the shared policy decides whether
// null members are written, so a fixture can never show the host a wire shape production does not.
string ToCamelCaseJson<T>(T value) => WorkerJson.SerializePayload(value);

string DirectStatusPayload(ProjectStatusInfo status) => ToCamelCaseJson(new ProjectStatusResultInfo
{
    Operation = "get_project_status", ProjectPath = status.Path, Project = status
});

string StandaloneCompileResponse(string scenarioName, string requestLine)
{
    if (scenarioName == "compile-attempt-failure")
        return JsonSerializer.Serialize(new WorkerResponse { Success = false, FailureCategory = WorkerFailureCategories.WorkerOperationFailed, Error = "Compiler invocation failed." }, WorkerJson.Envelope);
    if (scenarioName == "compile-malformed") return Success("{\"PRIVATE_COMPILER_MARKER\":true}");
    var errors = scenarioName == "compile-errors" ? 1 : 0;
    var warnings = scenarioName == "compile-warning" ? 1 : 0;
    var state = scenarioName switch
    {
        "compile-errors" or "compile-unavailable" => "Error",
        "compile-warning" => "Warning", "compile-unknown" => "Cancelled", _ => "Success"
    };
    var blockPath = ReadField(requestLine, "blockPath");
    var report = new CompileCheckReport
    {
        Scope = blockPath is null ? "plc" : "block", BlockPath = blockPath,
        TotalErrorCount = errors, TotalWarningCount = warnings,
        OverallState = state == "Cancelled" && blockPath is null ? "Success" : state,
        Plcs = [new PlcCompileInfo
        {
            PlcName = ReadField(requestLine, "plcName") ?? "PLC_1", State = state,
            ErrorCount = errors, WarningCount = warnings,
            Messages = errors + warnings == 0 ? [] : [new CompileMessageInfo { Description = "Compile diagnostic", Path = "Main", Severity = state }],
            DiagnosticNotes = scenarioName == "compile-oversized" ? [new string('x', 70000)]
                : scenarioName == "compile-unavailable" ? ["Compilation failed; compiler details are unavailable."] : []
        }]
    };
    if (scenarioName == "compile-inconsistent") report.TotalErrorCount = 7;
    return Success(ToCamelCaseJson(report));
}

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

HardwareConfigInfo ProjectCompletenessHardware() => new()
{
    Devices = new List<DeviceInfo>
    {
        new() { Name = "Direct PLC", TypeIdentifier = "OrderNumber:CPU" },
        new() { Name = "Grouped ET200", TypeIdentifier = "OrderNumber:ET200" },
    },
};

HardwareConfigInfo HardwarePaginationUnpaged() => new()
{
    Devices = HardwarePaginationDevices().Select(candidate => candidate.Device).ToList(),
    Subnets = HardwarePaginationSubnets().Select(candidate => candidate.Subnet).ToList(),
    Messages = new List<string> { "Fixture page diagnostic." },
};

string HardwarePaginationResponse(string requestLine)
{
    var payload = HardwarePaginationCandidates(requestLine);
    return JsonSerializer.Serialize(new
    {
        success = true,
        payload = ToCamelCaseJson(payload),
    });
}

string HardwarePaginationDerivedContinuationResponse(string requestLine)
{
    var request = JsonSerializer.Deserialize<WorkerRequest>(requestLine, requestJsonOptions)
        ?? throw new JsonException("Could not deserialize the hardware-page request.");
    var payload = HardwarePaginationCandidates(requestLine);
    var continuation = request.HardwarePageContinuation;
    if (continuation is not null && !string.Equals(continuation.SnapshotHash, payload.SnapshotHash, StringComparison.Ordinal))
    {
        return JsonSerializer.Serialize(new { success = false, failureCategory = WorkerFailureCategories.CursorSnapshotMismatch, error = "Descriptor evidence changed after the preceding page." });
    }

    var total = payload.TotalDevices + payload.TotalSubnets;
    if (continuation is not null && continuation.Offset > total)
    {
        return JsonSerializer.Serialize(new { success = false, failureCategory = WorkerFailureCategories.CursorOutOfRange, error = "Continuation offset is outside the current descriptor range." });
    }

    return HardwarePaginationResponse(requestLine);
}

int NextHardwarePaginationScenarioCall(string scenario)
{
    hardwarePaginationScenarioCalls.TryGetValue(scenario, out var calls);
    calls++;
    hardwarePaginationScenarioCalls[scenario] = calls;
    return calls;
}

int NextProjectTreeV3ScenarioCall(string scenario)
{
    projectTreeV3ScenarioCalls.TryGetValue(scenario, out var calls);
    calls++;
    projectTreeV3ScenarioCalls[scenario] = calls;
    return calls;
}

HardwarePageCandidateResultInfo HardwarePaginationCandidates(string requestLine)
{
    var request = JsonSerializer.Deserialize<WorkerRequest>(requestLine, requestJsonOptions)
        ?? throw new JsonException("Could not deserialize the hardware-page request.");
    var pageSize = request.HardwarePageSize
        ?? throw new JsonException("HardwarePageSize is required.");
    var startOffset = request.HardwarePageContinuation?.Offset ?? 0;

    var devices = HardwarePaginationDevices()
        .Where(candidate => request.DeviceName is null
            || string.Equals(candidate.Device.Name, request.DeviceName, StringComparison.OrdinalIgnoreCase))
        .ToList();
    var subnets = HardwarePaginationSubnets();
    var all = devices.Cast<HardwarePageFixtureCandidate>()
        .Concat(subnets)
        .ToArray();
    var returned = all.Skip(startOffset).Take(pageSize).ToArray();

    var returnedDevices = returned
        .OfType<HardwarePageFixtureDevice>()
        .Select((candidate, index) => new HardwareDevicePageCandidateInfo(
            startOffset + index,
            candidate.Device,
            candidate.Messages))
        .ToArray();
    var returnedSubnets = returned
        .OfType<HardwarePageFixtureSubnet>()
        .Select((candidate, index) => new HardwareSubnetPageCandidateInfo(
            startOffset + returnedDevices.Length + index,
            candidate.Subnet,
            candidate.Messages))
        .ToArray();

    var payload = new HardwarePageCandidateResultInfo(
        OrderingVersion: 1,
        QueryHash: HardwarePageEvidence.CreateQueryHash(
            request.DeviceName,
            request.PlcName,
            request.IncludeIoDetails,
            request.IncludeTagMatches),
        SnapshotHash: "b3f6dbb2e1f86d7b9063c9d02b8b3f4fd4e565f4bf0b58830d5c34fc3c0ca1ee",
        StartOffset: startOffset,
        TotalDevices: devices.Count,
        TotalSubnets: subnets.Count,
        Messages: HardwarePaginationMessages(),
        DeviceCandidates: returnedDevices,
        SubnetCandidates: returnedSubnets);
    return ScenarioKey(currentProjectPath) switch
    {
        "hardware-pagination-malformed-offset" => payload with { DeviceCandidates = payload.DeviceCandidates.Select((candidate, index) => index == 0 ? candidate with { Offset = candidate.Offset + 1 } : candidate).ToArray() },
        "hardware-pagination-incoherent-counts" => payload with { TotalDevices = 0 },
        "hardware-pagination-snapshot-drift" when request.HardwarePageContinuation is not null => payload with { SnapshotHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
        "hardware-pagination-out-of-range" when request.HardwarePageContinuation is not null => payload with { TotalDevices = 0, TotalSubnets = 0, DeviceCandidates = Array.Empty<HardwareDevicePageCandidateInfo>(), SubnetCandidates = Array.Empty<HardwareSubnetPageCandidateInfo>() },
        _ => payload,
    };
}

IReadOnlyList<string> HardwarePaginationMessages()
    => string.Equals(ScenarioKey(currentProjectPath), "hardware-pagination-telemetry", StringComparison.Ordinal)
        ? new[] { "Fixture page diagnostic.", $"Worker candidate requests: {NextHardwarePaginationScenarioCall("hardware-pagination-telemetry")}." }
        : new[] { "Fixture page diagnostic." };

List<HardwarePageFixtureDevice> HardwarePaginationDevices()
{
    var devices = new List<HardwarePageFixtureDevice>
    {
        new(
            new DeviceInfo { Name = "PLC_DUP", TypeIdentifier = "OrderNumber:CPU-1515", Items = new List<DeviceItemInfo>() },
            new[] { "Nested locator fixture: Plant A/Cell 1/PLC_DUP." }),
        new(
            new DeviceInfo { Name = "PLC_DUP", TypeIdentifier = "OrderNumber:CPU-1516", Items = new List<DeviceItemInfo>() },
            new[] { "Nested locator fixture: Plant A/Cell 2/PLC_DUP." }),
        new(
            new DeviceInfo { Name = "ET200_GROUPED", TypeIdentifier = "OrderNumber:ET200", Items = new List<DeviceItemInfo>() },
            new[] { "Nested locator fixture: Plant A/Remote IO/ET200_GROUPED." }),
    };

    if (string.Equals(ScenarioKey(currentProjectPath), "hardware-pagination-trimming", StringComparison.Ordinal))
    {
        // Sized so a single LARGE_DEVICE candidate lands just under HardwarePageProjector's
        // 60,000-char page budget (~60 chars of headroom) while LARGE_DEVICE plus the next
        // candidate lands just over it (~60 chars over): exactly one device is kept per page.
        // Calibrated against the authenticated hardware cursor and
        // HardwarePaginationFakeWorkerTests.TrimmingScenarioProjectPath, which is a fixed,
        // already-rooted literal — see the comment there for why the path must be fixed for this
        // size to be portable across machines/checkouts.
        devices.Insert(0, new HardwarePageFixtureDevice(
            new DeviceInfo
            {
                Name = "LARGE_DEVICE",
                TypeIdentifier = new string('L', 58_602),
                Items = new List<DeviceItemInfo>(),
            },
            new[] { "Candidate diagnostic: large device." }));
    }

    return devices;
}

List<HardwarePageFixtureSubnet> HardwarePaginationSubnets() => new()
{
    new(
        SelectableSubnet(
            "PN/IE_MAIN", "subnet-main", "Ethernet", "Ethernet",
            Array.Empty<IoSystemInfo>(), new[] { "PLC_DUP.X1" }),
        new[] { "Candidate diagnostic: main subnet." }),
    new(
        SelectableSubnet(
            "PN/IE_REMOTE", "subnet-remote", "Ethernet", "Ethernet",
            Array.Empty<IoSystemInfo>(), new[] { "ET200_GROUPED.X1" }),
        new[] { "Candidate diagnostic: remote subnet." }),
};

ProjectTreeBrowseResultInfo ProjectTreeV3Snapshot() => new()
{
    StartSelector = new List<ProjectTreeSelectorSegment>
    {
        new() { NodeType = ProjectTreeNodeTypes.Device, Name = "PLC_1" }
    },
    Depth = 1,
    Roots = new List<ProjectTreeNode>
    {
        new() { Name = "PLC_1", NodeType = ProjectTreeNodeTypes.Device, Details = null, Children = new List<ProjectTreeNode>() }
    }
};

string ProjectTreeV3ScenarioResponse(string requestLine, string scenario)
{
    if (ReadMethod(requestLine) != "browse_project_tree_v3_snapshot")
    {
        return $$"""{"success":false,"error":"expected browse_project_tree_v3_snapshot, got '{{ReadMethod(requestLine)}}'"}""";
    }

    var call = NextProjectTreeV3ScenarioCall(scenario);
    if (scenario == "project-tree-v3-one-shot" && call > 1)
    {
        return """{"success":false,"error":"project-tree observation attempted more than once"}""";
    }

    return SuccessWithResolvedPath(
        ToCamelCaseJson(ProjectTreeV3Fixture(
            scenario == "project-tree-v3-counted" ? call : null)),
        ReadField(requestLine, "projectPath") ?? scenario);
}

ProjectTreeBrowseResultInfo ProjectTreeV3Fixture(int? observation = null) => new()
{
    StartSelector = null,
    Depth = null,
    Roots = new List<ProjectTreeNode>
    {
        new()
        {
            Name = "PLC_1",
            NodeType = ProjectTreeNodeTypes.Device,
            Details = observation is null
                ? null
                : new Dictionary<string, string> { ["Observation"] = observation.Value.ToString() },
            Children = new List<ProjectTreeNode>
            {
                new()
                {
                    Name = "PLC_1",
                    NodeType = ProjectTreeNodeTypes.PlcSoftware,
                    Details = null,
                    Children = new List<ProjectTreeNode>
                    {
                        new()
                        {
                            Name = "Blocks",
                            NodeType = ProjectTreeNodeTypes.BlockFolder,
                            Details = null,
                            Children = new List<ProjectTreeNode>
                            {
                                new()
                                {
                                    Name = "Main",
                                    NodeType = ProjectTreeNodeTypes.Fb,
                                    Details = new Dictionary<string, string>
                                    {
                                        ["Number"] = "42",
                                        ["ProgrammingLanguage"] = "SCL",
                                        ["HeaderAuthor"] = "Fake Vendor",
                                        ["HeaderVersion"] = "2.3",
                                        ["HeaderFamily"] = "Motion",
                                        ["HeaderName"] = "Reusable main cycle",
                                    },
                                    Children = new List<ProjectTreeNode>(),
                                },
                            },
                        },
                    },
                },
            },
        },
    },
};

string ProjectTreeDedupResponse(string requestLine)
{
    var request = JsonSerializer.Deserialize<WorkerRequest>(requestLine, requestJsonOptions)!;
    if (request.Method == "get_project_status") return Success("{\"isOpen\":true}");
    // The test explicitly marks phase boundaries via this fixture-only counter probe.
    // No production request or protocol field is added, and reads never infer a phase by count.
    if (request.Method == "list_tag_tables" && request.PlcName is "preview" or "apply")
    {
        projectTreeDedupPhase = request.PlcName;
        return Success(JsonSerializer.Serialize(projectTreeDedupCounters));
    }

    var key = request.Method + "." + projectTreeDedupPhase;
    projectTreeDedupCounters[key] = projectTreeDedupCounters.GetValueOrDefault(key) + 1;
    var scenario = request.Method switch
    {
        "read_create_block_safety_snapshot" => "tree-safety-route-create-block",
        "read_create_block_group_safety_snapshot" => "tree-safety-route-create-block-group",
        "read_delete_block_group_safety_snapshot" => "tree-safety-route-delete-block-group",
        _ => null
    };
    if (scenario is not null) return ProjectTreeSafetyResponse(requestLine, scenario);
    if (request.Method is "create_block" or "create_block_group" or "delete_block_group") return Success("{}");
    if (request.Method == "get_block_content") return Success("<FB>unchanged</FB>");
    if (request.Method == "delete_block") return Success("{}");
    return JsonSerializer.Serialize(new { success = false, error = $"unexpected dedup method '{request.Method}'" });
}

string ProjectTreeSafetyResponse(string requestLine, string scenario)
{
    var request = JsonSerializer.Deserialize<WorkerRequest>(requestLine, requestJsonOptions)!;
    if (request.Method == "get_project_status"
        && (!scenario.StartsWith("tree-safety-route-", StringComparison.Ordinal)
            || NextProjectTreeSafetyScenarioCall(scenario + "-binding-probe") == 1))
        return Success("{\"isOpen\":true}");

    var expectedMethod = scenario switch
    {
        "tree-safety-route-delete-block-group" or "tree-safety-delete-group-descendant-drift" or "tree-safety-unit-nested" or "tree-safety-conflicting-descendants"
            => "read_delete_block_group_safety_snapshot",
        "tree-safety-route-create-block-group" or "tree-safety-create-group-collision-drift" or "tree-safety-unit-unrelated-sibling-drift" or "tree-safety-duplicate-group-occupancy"
            => "read_create_block_group_safety_snapshot",
        _ => "read_create_block_safety_snapshot"
    };
    if (request.Method != expectedMethod)
    {
        // Retain the original broad-tree fixtures so a routing regression reproduces the
        // original false acceptance/rejection, while route-only scenarios reject all fallback.
        if (scenario == "tree-safety-create-block-content-drift" && request.Method == "browse_project_tree")
            return Success(ToCamelCaseJson(CurrentBroadProjectTreePassesPreviewButNotContentDrift()));
        if (scenario == "tree-safety-unit-unrelated-sibling-drift" && request.Method == "browse_project_tree")
            return Success(ToCamelCaseJson(CurrentProjectTreeFalseInvalidatesAcrossUnitSiblings()));
        if (!scenario.StartsWith("tree-safety-route-", StringComparison.Ordinal)
            && request.Method == expectedMethod.Substring(5, expectedMethod.Length - 5 - "_safety_snapshot".Length))
            return Success("{}");
        return JsonSerializer.Serialize(new { success = false, error = $"unexpected method '{request.Method}' for {scenario}" });
    }

    var call = NextProjectTreeSafetyScenarioCall(scenario);
    if (scenario == "tree-safety-malformed-payload")
        return Success("{\"occupiedBlock\":{\"content\":\"PRIVATE_TREE_SNAPSHOT_CONTENT\"}}");
    if (scenario == "tree-safety-authoritative-export-failure")
        return JsonSerializer.Serialize(new
        {
            success = false,
            failureCategory = WorkerFailureCategories.WorkerOperationFailed,
            error = "Authoritative Simatic ML XML could not be exported for a project-tree safety snapshot."
        });

    var unitScoped = scenario is "tree-safety-unit-root" or "tree-safety-unit-nested" or "tree-safety-unit-unrelated-sibling-drift";
    var rootPath = unitScoped ? "PLC_1/Units/Line1/Blocks" : "PLC_1/Blocks";
    var owner = new ProjectTreeOwnerScopeInfo(unitScoped ? "SoftwareUnit" : "Plc", "PLC_1", unitScoped ? "Line1" : null, rootPath);
    var parentPath = scenario == "tree-safety-unit-root" ? rootPath : rootPath + (unitScoped ? "/Motion" : "/Main");
    var ancestors = parentPath == rootPath ? Array.Empty<ProjectTreeAncestorInfo>()
        : new[] { new ProjectTreeAncestorInfo(unitScoped ? "Motion" : "Main", parentPath, "UserBlockGroup") };

    if (scenario == "tree-safety-duplicate-group-occupancy")
    {
        var group = new ProjectTreeOccupancyInfo("UserBlockGroup", "AreaA", parentPath + "/AreaA");
        return Success(ToCamelCaseJson(new CreateBlockGroupSafetySnapshotInfo(owner, parentPath, ancestors, new[] { group, group })));
    }
    if (scenario == "tree-safety-conflicting-descendants")
    {
        var path = parentPath + "/AreaA/Mixer";
        return Success(ToCamelCaseJson(new DeleteBlockGroupSafetySnapshotInfo(owner, parentPath, parentPath + "/AreaA", ancestors,
            new[]
            {
                new ProjectTreeGroupDescendantInfo("FB", "Mixer", path, "hash-before", "PRIVATE_TREE_SNAPSHOT_CONTENT_BEFORE", Array.Empty<ProjectTreeGroupDescendantInfo>()),
                new ProjectTreeGroupDescendantInfo("FB", "Mixer", path, "hash-after", "PRIVATE_TREE_SNAPSHOT_CONTENT_AFTER", Array.Empty<ProjectTreeGroupDescendantInfo>())
            })));
    }

    if (expectedMethod == "read_create_block_safety_snapshot")
    {
        var occupied = scenario == "tree-safety-create-block-content-drift";
        var content = call == 1 ? "<FB>before</FB>" : "<FB>after</FB>";
        var snapshot = new CreateBlockSafetySnapshotInfo(owner, parentPath, ancestors,
            occupied ? new[] { new ProjectTreeOccupancyInfo("FB", "Mixer", parentPath + "/Mixer") } : Array.Empty<ProjectTreeOccupancyInfo>(),
            occupied ? new ProjectTreeBlockExportInfo("Mixer", parentPath + "/Mixer", "FB", "xml", TreeContentHash(content), content) : null);
        return Success(ToCamelCaseJson(snapshot));
    }
    if (expectedMethod == "read_create_block_group_safety_snapshot")
    {
        // The Line2 sibling changes on each read in the retained broad fixture; the exact
        // Line1 owner/parent/name occupancy remains identical across preview and apply.
        if (scenario == "tree-safety-unit-unrelated-sibling-drift")
            _ = CurrentProjectTreeFalseInvalidatesAcrossUnitSiblings();
        var occupancies = scenario == "tree-safety-create-group-collision-drift" && call > 1
            ? new[] { new ProjectTreeOccupancyInfo("UserBlockGroup", "AreaA", parentPath + "/AreaA") }
            : Array.Empty<ProjectTreeOccupancyInfo>();
        return Success(ToCamelCaseJson(new CreateBlockGroupSafetySnapshotInfo(owner, parentPath, ancestors, occupancies)));
    }

    var groupPath = parentPath + "/AreaA";
    var descendantContent = scenario == "tree-safety-delete-group-descendant-drift" && call > 1 ? "<FB>after</FB>" : "<FB>before</FB>";
    return Success(ToCamelCaseJson(new DeleteBlockGroupSafetySnapshotInfo(owner, parentPath, groupPath, ancestors,
        new[] { new ProjectTreeGroupDescendantInfo("FB", "Mixer", groupPath + "/Mixer", TreeContentHash(descendantContent), descendantContent, Array.Empty<ProjectTreeGroupDescendantInfo>()) })));
}

static string TreeContentHash(string content)
    => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

List<ProjectTreeNode> CurrentBroadProjectTreePassesPreviewButNotContentDrift()
{
    // The current broad project-tree contract exposes the occupied target's identity, but not
    // its block content. The fixture therefore returns the same broad tree after the hidden
    // target-content drift that the first RED must detect.
    return new()
    {
        new ProjectTreeNode
        {
            Name = "PLC_1",
            NodeType = "Device",
            Details = new Dictionary<string, string> { ["Path"] = "PLC_1" },
            Children = new List<ProjectTreeNode>
            {
                new()
                {
                    Name = "Blocks",
                    NodeType = "BlockFolder",
                    Details = new Dictionary<string, string> { ["Path"] = "PLC_1/Blocks" },
                    Children = new List<ProjectTreeNode>
                    {
                        new()
                        {
                            Name = "Mixer",
                            NodeType = "FB",
                            Details = new Dictionary<string, string> { ["Path"] = "PLC_1/Blocks/Main/Mixer" },
                            Children = new List<ProjectTreeNode>(),
                        },
                    },
                },
            },
        },
    };
}

List<ProjectTreeNode> CurrentProjectTreeFalseInvalidatesAcrossUnitSiblings()
{
    var call = NextProjectTreeSafetyScenarioCall("tree-safety-unit-unrelated-sibling-drift");
    var unrelatedSiblingName = call == 1 ? "AreaB" : "AreaB-Changed";
    return new()
    {
        new ProjectTreeNode
        {
            Name = "PLC_1",
            NodeType = "Device",
            Details = new Dictionary<string, string> { ["Path"] = "PLC_1" },
            Children = new List<ProjectTreeNode>
            {
                new()
                {
                    Name = "Units",
                    NodeType = "UnitFolder",
                    Details = new Dictionary<string, string> { ["Path"] = "PLC_1/Units" },
                    Children = new List<ProjectTreeNode>
                    {
                        new()
                        {
                            Name = "Line1",
                            NodeType = "Unit",
                            Details = new Dictionary<string, string> { ["Path"] = "PLC_1/Units/Line1" },
                            Children = new List<ProjectTreeNode>
                            {
                                new()
                                {
                                    Name = "AreaA",
                                    NodeType = "BlockGroup",
                                    Details = new Dictionary<string, string> { ["Path"] = "PLC_1/Units/Line1/Blocks/Motion/AreaA" },
                                    Children = new List<ProjectTreeNode>(),
                                },
                            },
                        },
                        new()
                        {
                            Name = "Line2",
                            NodeType = "Unit",
                            Details = new Dictionary<string, string> { ["Path"] = "PLC_1/Units/Line2" },
                            Children = new List<ProjectTreeNode>
                            {
                                new()
                                {
                                    Name = unrelatedSiblingName,
                                    NodeType = "BlockGroup",
                                    Details = new Dictionary<string, string> { ["Path"] = $"PLC_1/Units/Line2/Blocks/Motion/{unrelatedSiblingName}" },
                                    Children = new List<ProjectTreeNode>(),
                                },
                            },
                        },
                    },
                },
            },
        },
    };
}

int NextProjectTreeSafetyScenarioCall(string scenario)
{
    projectTreeSafetyScenarioCalls.TryGetValue(scenario, out var calls);
    calls++;
    projectTreeSafetyScenarioCalls[scenario] = calls;
    return calls;
}

HardwareConfigInfo SingleNodeHardwareConfig(
    string deviceName,
    string itemName,
    string interfaceName,
    string nodeName,
    string nodeId,
    IEnumerable<string>? messages = null) => new()
{
    DiscoveryEvidence = new() { Scope = "project", Complete = true },
    RootDeviceCount = 1,
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
    DiscoveryEvidence = new() { Scope = "project", Complete = true },
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

string ConfigureQualifiedFixture(string line, HardwareConfigInfo state, string scenario)
{
    var request = JsonSerializer.Deserialize<WorkerRequest>(line, requestJsonOptions)!;
    NetworkObjectSelectorInfo target;
    try { target = NetworkConfigurationTargetBinding.Resolve(request); }
    catch (WorkerOperationException ex) { return ToCamelCaseJson(new WorkerResponse { Success = false, FailureCategory = ex.FailureCategory, Error = ex.Message }); }
    var device = state.Devices.Single();
    if (!string.Equals(device.Name, target.DeviceName, StringComparison.OrdinalIgnoreCase))
        return ToCamelCaseJson(new WorkerResponse { Success = false, FailureCategory = WorkerFailureCategories.TargetNotFound, Error = "Device not found." });
    if (target.InterfacePath is null)
        return ToCamelCaseJson(new WorkerResponse { Success = false, FailureCategory = WorkerFailureCategories.TargetAmbiguous, Error = "Use qualified interfacePath." });
    var owner = NetworkInterfacePathMatcher.Match(device.Items, target.InterfacePath, x => x.Items, x => x.Name, x => x.PositionNumber, x => x.TypeIdentifier);
    if (!owner.Success) return ToCamelCaseJson(new WorkerResponse { Success = false, FailureCategory = owner.FailureCategory, Error = owner.Error });
    var networkInterface = owner.Item!.NetworkInterfaces.Single();
    var node = NetworkNodeReadSelectorBuilder.MatchNode(networkInterface.Nodes, target.NodeId, target.NodeIndex, x => x.NodeId);
    if (!node.Success || target.InterfaceName is not null && target.InterfaceName != networkInterface.Name)
        return ToCamelCaseJson(new WorkerResponse { Success = false, FailureCategory = node.FailureCategory ?? WorkerFailureCategories.TargetEvidenceMismatch, Error = node.Error ?? "Interface constraint mismatch." });
    // Preflight all dependency selectors before any scalar mutation.
    if (request.SubnetId is not null || request.IoSystemNumber is not null)
        return ToCamelCaseJson(new WorkerResponse { Success = false, FailureCategory = WorkerFailureCategories.WorkerOperationFailed, Error = "Requested dependency was not found; no mutation." });
    if (scenario == "network-qualified-budget-known-observations")
    {
        // Attempted assignments can leave known old values in failed postconditions.
        guardedNetworkWrites++;
        var attempted = new Dictionary<string, string>
        { ["Address"] = request.IpAddress!, ["SubnetMask"] = request.SubnetMask!, ["PnDeviceName"] = request.PnDeviceName! };
        var failedEvidence = FakeConfigurationVerification(line, request.DeviceName!, attempted);
        failedEvidence.Status = "failed";
        foreach (var check in failedEvidence.Checks)
        {
            check.Observed = check.Name switch { "Address" => node.Item!.IpAddress,
                "SubnetMask" => node.Item!.SubnetMask, _ => node.Item!.PnDeviceName };
            check.Status = "failed"; check.Message = "The attempted setting retained its known old value.";
        }
        return Success(ToCamelCaseJson(new ConfigureNetworkDeviceResultInfo
        { DeviceName = request.DeviceName!, AppliedSettings = attempted, Verification = failedEvidence }));
    }
    var applied = new Dictionary<string, string>();
    var skipped = new Dictionary<string, string>();
    guardedNetworkWrites++;
    if (request.IpAddress is not null) { node.Item!.IpAddress = request.IpAddress; applied["Address"] = request.IpAddress; }
    if (request.SubnetMask is not null)
    {
        if (scenario == "network-qualified-partial") skipped["SubnetMask"] = "Read only";
        else { node.Item!.SubnetMask = request.SubnetMask; applied["SubnetMask"] = request.SubnetMask; }
    }
    if (request.PnDeviceName is not null) { node.Item!.PnDeviceName = request.PnDeviceName; applied["PnDeviceName"] = request.PnDeviceName; }
    return Success(ToCamelCaseJson(new ConfigureNetworkDeviceResultInfo { DeviceName = request.DeviceName!, AppliedSettings = applied,
        SkippedSettings = skipped, Verification = FakeConfigurationVerification(line, request.DeviceName!, applied) }));
}

HardwareConfigInfo QualifiedHardwareFixture() => new()
{
    RootDeviceCount = 1,
    DiscoveryEvidence = new() { Scope = "project", Complete = true },
    Devices = new() { new() { Name = "S7-1500/ET200MP station_1", Items = new() { new()
    {
        Name = "PLC_DP", PositionNumber = 1,
        SelectorDiagnostics = new() { "Generic item type evidence is unavailable." },
        Items = new()
        {
            new() { Name = "PROFINET interface_1", PositionNumber = 32768,
                SelectorDiagnostics = new() { "Generic item type evidence is unavailable." }, NetworkInterfaces = new()
                { new() { Name = "PROFINET interface_1", SelectorDiagnostics = new() { "Generic owner type evidence is unavailable." },
                    Nodes = new() { new() { NodeId = "E1", Name = "X1", IpAddress = "192.168.12.2" } } } } },
            new() { Name = "PROFINET interface_2", PositionNumber = 33024,
                SelectorDiagnostics = new() { "Generic item type evidence is unavailable." }, NetworkInterfaces = new()
                { new() { Name = "PROFINET interface_2", SelectorDiagnostics = new() { "Generic owner type evidence is unavailable." },
                    Nodes = new() { new() { NodeId = "E1", Name = "X2", IpAddress = "192.168.13.20" } } } } },
        },
    } } } },
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

    foreach (var node in nodes)
        node.Selector = NetworkSelectorFactory.QualifiedNode(deviceName, node.NodeId,
            path.Select(segment => new NetworkInterfacePathSegmentInfo { Name = segment.Name,
                PositionNumber = segment.PositionNumber, TypeIdentifier = segment.TypeIdentifier }).ToList(), interfaceName);

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
    DiscoveryEvidence = new() { Scope = "project", Complete = true },
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
        applied["Address"] = ipAddress;
    }

    var subnetMask = ReadField(requestLine, "subnetMask");
    if (subnetMask is not null)
    {
        target.SubnetMask = subnetMask;
        applied["SubnetMask"] = subnetMask;
    }

    var pnDeviceName = ReadField(requestLine, "pnDeviceName");
    if (pnDeviceName is not null)
    {
        target.PnDeviceName = pnDeviceName;
        applied["PnDeviceName"] = pnDeviceName;
    }

    var result = new ConfigureNetworkDeviceResultInfo
    {
        DeviceName = "PC_1",
        AppliedSettings = applied,
        SkippedSettings = new Dictionary<string, string>(),
        Messages = new List<string> { $"configured nodeId '{nodeId}'" },
        Verification = FakeConfigurationVerification(requestLine, "PC_1", applied),
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

// Complete fixture inventory: both PLC ports survive subnet deletion and expose current membership.
List<DeviceInfo> SubnetLifecycleDevices() => new()
{
    SingleNodeHardwareConfig("PLC_1", "Interface", "Interface", "X1", "eth-node").Devices[0],
    new() { Name = "HMI_1", TypeIdentifier = "OrderNumber:HMI", Items = new() },
};

HardwareConfigInfo SubnetLifecycleHardwareConfig(List<SubnetLifecycleSubnetState> subnets)
{
    var devices = SubnetLifecycleDevices();
    var nodes = devices[0].Items[0].NetworkInterfaces[0].Nodes;
    nodes.Add(SelectableNode("PLC_1", "MPI", "pb-node", "Profibus"));
    foreach (var node in nodes)
    {
        var connected = subnets.SingleOrDefault(s => s.ConnectedNodeNames.Contains("PLC_1." + node.Name));
        node.SubnetName = connected?.Name;
        node.ConnectionEvidence = new() { Complete = true, SubnetId = connected?.SubnetId };
    }
    return new()
    {
        DiscoveryEvidence = new() { Scope = "project", Complete = true },
        RootDeviceCount = SubnetLifecycleDeviceCount,
        Devices = devices,
        Subnets = subnets.Select(subnet =>
        {
            var info = SelectableSubnet(subnet.Name, subnet.SubnetId, subnet.NetworkType,
                "System:Subnet." + subnet.NetworkType, Array.Empty<IoSystemInfo>(), subnet.ConnectedNodeNames);
            info.ConnectionEvidence = new()
            {
                Complete = true,
                Nodes = nodes.Where(n => n.ConnectionEvidence!.SubnetId == subnet.SubnetId)
                    .Select(n => new NetworkNodeIdentityInfo { DeviceName = "PLC_1", NodeId = n.NodeId! }).ToList()
            };
            return info;
        }).ToList()
    };
}

string InspectSubnetLifecycle(string request, List<SubnetLifecycleSubnetState> subnets)
{
    var decoded = JsonSerializer.Deserialize<WorkerRequest>(request, requestJsonOptions)!;
    var subnet = subnets.Single(s => s.SubnetId == decoded.NetworkObjectTarget!.SubnetId);
    return Success(ToCamelCaseJson(new NetworkObjectInspectionInfo
    {
        Target = decoded.NetworkObjectTarget!,
        Attributes = decoded.NetworkAttributeNames!.Select(name => new NetworkAttributeInfo
        {
            Name = name, Source = "dynamic", Access = "readWrite", Availability = "available",
            Value = name == "HighestAddress" ? new() { Kind = "integer", Value = subnet.HighestAddress }
                : new() { Kind = "enum", Value = new NetworkEnumValueInfo { TypeName = "Fixture.Speed", Symbol = subnet.TransmissionSpeed!, NumericValue = 1 } }
        }).ToList()
    }));
}

HardwareConfigInfo RoundtripHardwareConfig()
{
    var state = JsonSerializer.Deserialize<HardwareConfigInfo>(HardwareConfigPayload(), requestJsonOptions)!;
    state.DiscoveryEvidence = new() { Scope = "project", Complete = true };
    state.RootDeviceCount = state.Devices.Count;
    foreach (var node in GuardedNodes(state))
        node.ConnectionEvidence = new() { Complete = true,
            SubnetId = state.Subnets.SingleOrDefault(s => s.Name == node.SubnetName)?.SubnetId };
    foreach (var subnet in state.Subnets)
        subnet.ConnectionEvidence = new() { Complete = true,
            Nodes = state.Devices.SelectMany(d => d.Items.SelectMany(i => i.NetworkInterfaces).SelectMany(i => i.Nodes)
                .Where(n => n.ConnectionEvidence!.SubnetId == subnet.SubnetId)
                .Select(n => new NetworkNodeIdentityInfo { DeviceName = d.Name!, NodeId = n.NodeId! })).ToList() };
    return state;
}

// An ordinary-read fixture with explicit all-scope identities and a distinct root count.
// Older/page scenarios intentionally keep their conditional omissions.
HardwareConfigInfo ConnectionEvidenceHardwareConfig(bool degraded)
{
    var result = SingleNodeHardwareConfig("PLC_Grouped", "Interface", "Interface", "Same display name", "node-2");
    // Explicit synthetic ordinary project traversal; relationship loss is modeled independently.
    result.DiscoveryEvidence = new() { Scope = "project", Complete = true };
    result.RootDeviceCount = 2;
    var ungrouped = SingleNodeHardwareConfig("PLC_Ungrouped", "Interface", "Interface", "Same display name", "node-3");
    result.Devices.Add(ungrouped.Devices[0]);
    var node = result.Devices[0].Items[0].NetworkInterfaces[0].Nodes[0];
    node.SubnetName = "Network";
    node.ConnectionEvidence = new NetworkNodeConnectionInfo { Complete = true, SubnetId = "subnet-1" };
    var ungroupedNode = result.Devices[1].Items[0].NetworkInterfaces[0].Nodes[0];
    ungroupedNode.SubnetName = "Network";
    ungroupedNode.ConnectionEvidence = new NetworkNodeConnectionInfo { Complete = true, SubnetId = "subnet-1" };
    var disconnected = SelectableNode("PLC_Grouped", "Disconnected port", "node-disconnected", "Ethernet");
    disconnected.ConnectionEvidence = new NetworkNodeConnectionInfo { Complete = true };
    result.Devices[0].Items[0].NetworkInterfaces[0].Nodes.Add(disconnected);
    var subnet = SelectableSubnet("Network", "subnet-1", "Ethernet", "Ethernet",
        Array.Empty<IoSystemInfo>(), new[] { "Same display name", "Same display name" });
    subnet.ConnectionEvidence = new NetworkSubnetConnectionsInfo
    {
        Complete = !degraded,
        Nodes = new() { new() { DeviceName = "PLC_Grouped", NodeId = "node-2" },
            new() { DeviceName = "PLC_Ungrouped", NodeId = "node-3" } },
        Messages = degraded ? new() { "Could not complete connected-node enumeration: unavailable" } : new()
    };
    result.Subnets.Add(subnet);
    return result;
}

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
/// Scripted verification for deterministic FakeWorker outcomes, not live Siemens read-back.
/// </summary>
IEnumerable<NodeInfo> GuardedNodes(HardwareConfigInfo state) => state.Devices.SelectMany(d => d.Items)
    .SelectMany(i => i.NetworkInterfaces).SelectMany(i => i.Nodes);

DeviceItemInfo GuardedItem(string device, int index, string name, string type, params DeviceItemPathSegmentInfo[] parents) => new()
{
    Name = name, TypeIdentifier = type, PositionNumber = index, Selectable = true,
    Selector = new() { Kind = "deviceItem", DeviceName = device, ItemPath = parents.Concat(new[]
        { new DeviceItemPathSegmentInfo { Index = index, Name = name, PositionNumber = index, TypeIdentifier = type } }).ToList() }
};

string HandleGuardedNetwork(string request, HardwareConfigInfo state, string scenario)
{
    var method = ReadMethod(request);
    if (method == "inspect_network_object")
    {
        var decoded = JsonSerializer.Deserialize<WorkerRequest>(request, requestJsonOptions)!;
        var id = decoded.NetworkObjectTarget!.SubnetId!;
        return Success(ToCamelCaseJson(new NetworkObjectInspectionInfo
        {
            Target = decoded.NetworkObjectTarget,
            Attributes = decoded.NetworkAttributeNames!.Select(name => new NetworkAttributeInfo
            {
                Name = name, Source = "dynamic", Access = "readWrite", Availability = "available",
                Value = name == "TransmissionSpeed"
                    ? new() { Kind = "enum", Value = new NetworkEnumValueInfo { TypeName = "Fixture.Speed", Symbol = guardedSubnetAttributes[(id, name)], NumericValue = 1 } }
                    : new() { Kind = "integer", Value = int.Parse(guardedSubnetAttributes[(id, name)], System.Globalization.CultureInfo.InvariantCulture) }
            }).ToList()
        }));
    }
    if (method == "read_hardware_config")
    {
        if (scenario == "network-guarded-late-traversal" && guardedNetworkWrites > 0)
            state.DiscoveryEvidence = new() { Scope = "project", Complete = false,
                Failures = new() { new() { Stage = "deviceEnumeration", Message = "Synthetic late ungrouped-device traversal failure." } } };
        if (scenario == "network-guarded-late-unreadable-subnet" && guardedNetworkWrites > 0 && state.Subnets.Count == 0)
            state.Subnets.Add(new() { SubnetId = "", SelectorDiagnostics = new() { "Unreadable subnet ID" } });
        if (scenario == "network-guarded-late-unreadable-attribute" && guardedNetworkWrites > 0)
            state.Subnets.Add(new() { SubnetId = "", SelectorDiagnostics = new() { "Unreadable subnet ID" } });
        if (scenario == "network-guarded-late-io-number" && guardedNetworkWrites > 0)
            state.Subnets[0].IoSystems.Add(new() { Number = null, SelectorDiagnostics = new() { "Unreadable IO number" } });
        if (scenario == "network-guarded-late-io-subnet" && guardedNetworkWrites > 0)
            state.Subnets.Add(new() { SubnetId = "", SelectorDiagnostics = new() { "Unreadable subnet ID" } });
        if (scenario == "network-guarded-root-drift" && guardedNetworkWrites > 0) state.RootDeviceCount = 1;
        if (scenario == "network-guarded-postread-failure" && guardedNetworkWrites > 0)
            return "{\"success\":false,\"error\":\"postread unavailable\"}";
        if (scenario == "network-guarded-late-block" && guardedNetworkWrites > 0 && state.Subnets.Count > 0)
            state.Subnets[0].ConnectionEvidence = new() { Complete = false, Messages = new() { "late inventory failure" } };
        if (scenario == "network-guarded-late-node-block" && guardedNetworkWrites > 0)
            GuardedNodes(state).First().ConnectionEvidence = new() { Complete = false,
                Messages = new() { "Could not read connected subnet identity: unavailable", "Could not read node 'Same display name' IO system: unavailable" } };
        return Success(ToCamelCaseJson(state));
    }
    guardedNetworkWrites++;
    if (method is "create_subnet" or "update_subnet")
    {
        var id = method == "create_subnet" ? "subnet-created-" + guardedNetworkWrites : ReadField(request, "subnetId")!;
        var subnet = method == "create_subnet"
            ? SelectableSubnet(ReadField(request, "subnetName")!, id, ReadField(request, "subnetNetworkType")!, "System:Subnet." + ReadField(request, "subnetNetworkType"), Array.Empty<IoSystemInfo>(), Array.Empty<string>())
            : state.Subnets.Single(s => s.SubnetId == id);
        if (method == "create_subnet")
        {
            subnet.ConnectionEvidence = new() { Complete = true };
            state.Subnets.Add(subnet);
        }
        if (ReadField(request, "subnetName") is { } name) subnet.Name = name;
        if (ReadIntField(request, "subnetHighestAddress") is { } address) guardedSubnetAttributes[(id, "HighestAddress")] = address.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (ReadField(request, "subnetTransmissionSpeed") is { } speed) guardedSubnetAttributes[(id, "TransmissionSpeed")] = speed;
        var verification = FakeSubnetVerification(request, id);
        var countCheck = verification.Checks.Single(c => c.Name == "networkDeviceCountUnchanged");
        countCheck.Expected = countCheck.Observed = state.RootDeviceCount!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Success(ToCamelCaseJson(new SubnetLifecycleResultInfo
        { SubnetId = id, Name = subnet.Name, NetworkDeviceCount = state.RootDeviceCount.Value, NetworkDeviceCountUnchanged = true, Verification = verification }));
    }
    if (method == "configure_network_device")
    {
        var node = GuardedNodes(state).Single(n => n.NodeId == ReadField(request, "nodeId"));
        var applied = new Dictionary<string, string>();
        var skipped = new Dictionary<string, string>();
        if (ReadField(request, "ipAddress") is { } address) { node.IpAddress = address; applied["Address"] = address; }
        if (ReadField(request, "subnetMask") is { } mask) { node.SubnetMask = mask; applied["SubnetMask"] = mask; }
        if (ReadField(request, "pnDeviceName") is { } pn) { node.PnDeviceName = pn; applied["PnDeviceName"] = pn; }
        if (ReadField(request, "subnetId") is { } id)
        {
            node.ConnectionEvidence = new() { Complete = true, SubnetId = id };
            applied["Subnet"] = id;
            foreach (var previous in state.Subnets) previous.ConnectionEvidence?.Nodes.RemoveAll(n => n.DeviceName == "PLC_Grouped" && n.NodeId == node.NodeId);
            var subnet = state.Subnets.Single(s => s.SubnetId == id);
            subnet.ConnectionEvidence!.Nodes.Add(new() { DeviceName = "PLC_Grouped", NodeId = node.NodeId });
        }
        if (ReadIntField(request, "ioSystemNumber") is { } ioNumber)
        {
            if (scenario == "network-guarded-io-move" || scenario.StartsWith("network-guarded-late-io-", StringComparison.Ordinal))
            {
                node.ConnectionEvidence!.IoSystemSubnetId = ReadField(request, "ioSystemSubnetId");
                node.ConnectionEvidence.IoSystemNumber = ioNumber;
                applied["IoSystem"] = ioNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else skipped["IoSystem"] = "No IO connector available";
        }
        if (scenario == "network-guarded-unknown-result") return "{\"success\":false,\"error\":\"outcome unavailable after mutation\"}";
        return Success(ToCamelCaseJson(new ConfigureNetworkDeviceResultInfo
        {
            DeviceName = ReadField(request, "deviceName")!, AppliedSettings = applied, SkippedSettings = skipped,
            Verification = FakeConfigurationVerification(request, ReadField(request, "deviceName")!, applied)
        }));
    }
    if (method == "add_network_device")
    {
        var name = ReadField(request, "deviceName")!;
        var itemName = ReadField(request, "deviceItemName") ?? name;
        var type = ReadField(request, "typeIdentifier")!;
        state.RootDeviceCount++;
        var rack = GuardedItem(name, 0, "Rack", "Rack:TEST");
        rack.Items.Add(GuardedItem(name, 0, itemName, type, rack.Selector!.ItemPath!.ToArray()));
        state.Devices.Add(new() { Name = name, TypeIdentifier = "Device:Station", Items = new()
        {
            rack, GuardedItem(name, 1, "PowerSupply", "Supply:TEST")
        } });
        return Success(ToCamelCaseJson(new AddDeviceResultInfo
        {
            DeviceName = name, RootItemName = itemName, TypeIdentifier = type,
            Verification = FakePassedVerification(new() { ["deviceName"] = name, ["deviceItemName"] = itemName }, new()
            { ["deviceName"] = name, ["deviceItemName"] = itemName, ["typeIdentifier"] = type })
        }));
    }
    if (method == "delete_subnet")
    {
        var id = ReadField(request, "subnetId")!;
        var subnet = state.Subnets.Single(s => s.SubnetId == id);
        state.Subnets.Remove(subnet);
        foreach (var node in GuardedNodes(state))
            if (node.ConnectionEvidence?.SubnetId == id) node.ConnectionEvidence = new() { Complete = true };
        if (scenario == "network-guarded-lost-node") state.Devices.RemoveAt(1);
        if (scenario == "network-guarded-unknown-result") return "{\"success\":false,\"error\":\"delete outcome unavailable\"}";
        return Success(ToCamelCaseJson(new SubnetLifecycleResultInfo
        {
            SubnetId = id, Name = subnet.Name, NetworkDeviceCount = state.RootDeviceCount!.Value, NetworkDeviceCountUnchanged = true,
            Verification = FakePassedVerification(new() { ["subnetId"] = id }, new()
            {
                ["subnetAbsent"] = "true", ["affectedNodesPreserved"] = "true", ["affectedConnectionsRemoved"] = "true",
                ["networkDeviceCountUnchanged"] = state.RootDeviceCount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })
        }));
    }
    return "{\"success\":false,\"error\":\"unsupported guarded fixture operation\"}";
}

NetworkMutationVerificationInfo FakePassedVerification(Dictionary<string, string> identity, Dictionary<string, string> values) => new()
{
    Identity = identity,
    Status = values.Count == 0 ? "not_required" : "passed",
    Checks = values.Select(pair => new NetworkVerificationCheckInfo
    {
        Name = pair.Key, Status = "passed", Expected = pair.Value, Observed = pair.Value,
    }).ToList(),
};

NetworkMutationVerificationInfo FakeConfigurationVerification(string requestLine, string deviceName, Dictionary<string, string> applied)
{
    var values = new Dictionary<string, string>(applied);
    if (values.ContainsKey("IoSystem")) values["IoSystem"] = JsonSerializer.Serialize(new object?[]
        { ReadField(requestLine, "ioSystemSubnetId") ?? ReadField(requestLine, "subnetId"), ReadIntField(requestLine, "ioSystemNumber") });
    var identity = new Dictionary<string, string> { ["deviceName"] = deviceName, ["nodeId"] = ReadField(requestLine, "nodeId")! };
    var target = JsonSerializer.Deserialize<WorkerRequest>(requestLine, requestJsonOptions)!.NetworkObjectTarget;
    if (target?.InterfacePath is not null) identity["interfacePath"] = NetworkInterfacePathEncoding.Encode(target.InterfacePath);
    else if (target?.ItemPath is not null) identity["interfacePath"] = NetworkInterfacePathEncoding.Encode(target.ItemPath.Select(x =>
        new NetworkInterfacePathSegmentInfo { Name = x.Name, PositionNumber = x.PositionNumber, TypeIdentifier = x.TypeIdentifier }).ToArray());
    if (target?.InterfaceName is not null) identity["interfaceName"] = target.InterfaceName;
    return FakePassedVerification(identity, values);
}

NetworkMutationVerificationInfo FakeSubnetVerification(string requestLine, string subnetId)
{
    var method = ReadMethod(requestLine);
    var values = new Dictionary<string, string>
    {
        ["networkDeviceCountUnchanged"] = SubnetLifecycleDeviceCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
    if (method == "delete_subnet")
    {
        values.Add("subnetAbsent", "true");
        values.Add("affectedNodesPreserved", "true");
        values.Add("affectedConnectionsRemoved", "true");
    }
    else
    {
        values.Add("subnetIdentity", subnetId);
        if (ReadField(requestLine, "subnetName") is { } name) values.Add("Name", name);
        if (method == "create_subnet") values.Add("TypeIdentifier", "System:Subnet." + ReadField(requestLine, "subnetNetworkType"));
        if (ReadIntField(requestLine, "subnetHighestAddress") is { } address) values.Add("HighestAddress", address.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (ReadField(requestLine, "subnetTransmissionSpeed") is { } speed) values.Add("TransmissionSpeed", speed);
    }
    return FakePassedVerification(new() { ["subnetId"] = subnetId }, values);
}

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
        Verification = FakeSubnetVerification(requestLine, subnetId),
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
        Verification = FakeSubnetVerification(requestLine, target.SubnetId),
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
        Verification = FakeSubnetVerification(requestLine, target.SubnetId),
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

UpdateTagSafetySnapshotInfo TagUpdateSnapshot(
    bool? externalAccessible = false, bool? externalVisible = true, bool? externalWritable = false, string dataType = "Bool")
{
    return new(
        new("ResolvedPLC", "/", "Default tag table", "ResolvedPLC/Default tag table"),
        new("ResolvedPLC", "/", "Default tag table", "MotorReady", "ResolvedPLC/Default tag table/MotorReady",
            dataType, "%I0.0", externalAccessible, externalVisible, externalWritable),
        "MotorReady", "%I0.0", Array.Empty<TagCollisionProbeInfo>(), Array.Empty<TagCollisionProbeInfo>());
}

string TagUpdateDriftSnapshotResponse(int readCount)
    => Success(ToCamelCaseJson(TagUpdateSnapshot(externalAccessible: readCount != 1)));

string InvalidTagUpdateSnapshotResponse(string requestLine)
{
    var variant = ReadField(requestLine, "name") ?? "empty";
    if (variant == "malformed")
    {
        return Success("{not valid json");
    }
    if (variant == "root-array")
    {
        return Success("[]");
    }

    var snapshot = JsonNode.Parse(ToCamelCaseJson(TagUpdateSnapshot()))!.AsObject();
    snapshot["targetTag"]!["dataType"] = "PRIVATE_SNAPSHOT_SENTINEL";

    if (variant == "empty")
    {
        snapshot.Clear();
    }
    else if (variant.StartsWith("missing-", StringComparison.Ordinal) ||
             variant.StartsWith("null-", StringComparison.Ordinal) ||
             variant.StartsWith("wrong-", StringComparison.Ordinal))
    {
        var parts = variant[(variant.IndexOf('-') + 1)..].Split('.');
        var owner = parts.Length == 1 ? snapshot : snapshot[parts[0]]!.AsObject();
        if (variant.StartsWith("missing-", StringComparison.Ordinal)) owner.Remove(parts[^1]);
        else owner[parts[^1]] = variant.StartsWith("null-", StringComparison.Ordinal)
            ? null : JsonValue.Create(123);
    }

    if (variant == "unknown-member")
    {
        snapshot["unexpected"] = true;
    }

    var payload = JsonSerializer.Serialize(snapshot);
    if (variant == "duplicate-member")
    {
        payload = payload.Insert(1, "\"effectiveName\":\"Duplicate\",");
    }
    return Success(payload);
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

abstract record HardwarePageFixtureCandidate(IReadOnlyList<string> Messages);

sealed record HardwarePageFixtureDevice(
    DeviceInfo Device,
    IReadOnlyList<string> Messages) : HardwarePageFixtureCandidate(Messages);

sealed record HardwarePageFixtureSubnet(
    SubnetInfo Subnet,
    IReadOnlyList<string> Messages) : HardwarePageFixtureCandidate(Messages);

sealed class FakePortalState
{
    public int ProcessId { get; init; }
    public string? ProjectPath { get; set; }
    public bool HasUserInterface { get; init; }
    public int OtherClients { get; init; }
    public bool? Modified { get; set; }
    public bool WorkerOpened { get; set; }

    public static FakePortalState Parse(string declaration)
    {
        var fields = declaration.Split('|');
        if (fields.Length != 5 || fields[2] is not ("ui" or "headless"))
            throw new FormatException("Portal inventory entries require pid|path|ui-or-headless|otherClients|modified.");
        return new FakePortalState
        {
            ProcessId = int.Parse(fields[0], System.Globalization.CultureInfo.InvariantCulture),
            ProjectPath = ProjectPathNormalization.Canonicalize(fields[1]),
            HasUserInterface = fields[2] == "ui",
            OtherClients = int.Parse(fields[3], System.Globalization.CultureInfo.InvariantCulture),
            Modified = fields[4] == "unknown" ? null : bool.Parse(fields[4])
        };
    }
}
