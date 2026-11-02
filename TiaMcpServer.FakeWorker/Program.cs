using System.Text.Json;
using static TiaMcpServer.FakeWorker.ScenarioSupport;
using static TiaMcpServer.FakeWorker.Block.BlockScenarios;
using static TiaMcpServer.FakeWorker.Network.NetworkScenarios;
using static TiaMcpServer.FakeWorker.Lifecycle.LifecycleScenarios;
using System.Text.Json.Nodes;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.CrossReferences;
using TiaMcpServer.Contracts.Diagnostics;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Multiuser;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.FakeWorker.Plc;
using TiaMcpServer.OpennessWorker.Worker;

// Scripted stand-in for TiaMcpServer.OpennessWorker used by IPC integration tests.
// Mirrors the real worker's request loop: one JSON line in, one JSON line out, until
// stdin closes. Scenarios use projectPath file names; UI-open and Portal-inventory environment
// fixtures declare source state independently, so ordinary reads cannot select or open a project.
var launchLog = Environment.GetEnvironmentVariable("TIA_MCP_FAKE_WORKER_LAUNCH_LOG");
if (!string.IsNullOrWhiteSpace(launchLog))
    AppendLogLine(launchLog, JsonSerializer.Serialize(new { processId = Environment.ProcessId, args }));

var seq = 0;
var requestLog = Environment.GetEnvironmentVariable("TIA_MCP_FAKE_WORKER_REQUEST_LOG");
var modeIndex = Array.FindIndex(args, arg => string.Equals(arg, "--access-mode", StringComparison.OrdinalIgnoreCase));
var accessMode = modeIndex < 0 ? McpAccessMode.ReadWrite
    : modeIndex + 1 < args.Length && McpAccessModeNames.TryParse(args[modeIndex + 1], out var parsedMode)
        ? parsedMode : McpAccessMode.ReadOnly;
var workerSessionId = Guid.NewGuid().ToString("N");
var fakeSessionGeneration = 1L;
var portalDisposalRevision = 0L;
// Tests may model a project that was already opened in the TIA Portal UI before the worker
// starts. Ordinary requests never establish this mutable session state.
string? fakeProjectPath = ProjectPathNormalization.Canonicalize(
    Environment.GetEnvironmentVariable("TIA_MCP_FAKE_WORKER_UI_OPEN_PROJECT"));
// Binding fixtures opt in so the first identity-less get_project_status grounds the UI-open
// project before any scenario script runs. Launches without the flag keep every scripted response.
var statusGroundingDeclared =
    Environment.GetEnvironmentVariable("TIA_MCP_FAKE_WORKER_STATUS_GROUNDING") == "1";
string? fakeSessionContainerPath = null;
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
// Process metadata can omit the engineering path of a typed local-session owner. A sole
// declared Portal still hosts the UI-open fixture; selection must inspect its typed owner.
if (fakePortalProcessId is null && fakePortals.Count == 1
    && string.Equals(Path.GetExtension(fakeProjectPath), ".amc21", StringComparison.OrdinalIgnoreCase))
    fakePortalProcessId = fakePortals[0].ProcessId;
if (fakePortalProcessId is null) fakeProjectPath = null;
string? currentProjectPath = null;
string? currentMethod = null;
string? currentRequestLine = null;
var requestJsonOptions = WorkerJson.Envelope;

var compileIdentityDrift = false;
var projectTreeV3ScenarioCalls = new Dictionary<string, int>(StringComparer.Ordinal);
var localOwnerStatusReads = 0;
var localConfirmationDriftApplied = false;
string? recoveryClosedSource = null;
var networkScenarios = new TiaMcpServer.FakeWorker.Network.NetworkScenarios();
var lifecycleScenarios = new TiaMcpServer.FakeWorker.Lifecycle.LifecycleScenarios();
var blockScenarios = new TiaMcpServer.FakeWorker.Block.BlockScenarios();
// Stateful PLC write fixture: reads in the same process observe earlier writes.
var vciWriteRoundtrip = new TiaMcpServer.FakeWorker.Vci.VciWriteRoundtripScenario();
var hmiWriteRoundtrip = new TiaMcpServer.FakeWorker.Hmi.HmiWriteRoundtripScenario();
var plcWriteRoundtrip = new PlcWriteRoundtripScenario(format => CompletedBlockOutcome(format, "succeeded"));
var plcWriteIncomplete = new PlcWriteRoundtripScenario(format => CompletedBlockOutcome(format, "succeeded"), inventoryIncomplete: true);
var plcWriteLateBlock = new PlcWriteRoundtripScenario(format => CompletedBlockOutcome(format, "succeeded"), incompleteAfterWrite: true);
var plcWriteExportLost = new PlcWriteRoundtripScenario(format => CompletedBlockOutcome(format, "succeeded"), exportLostAfterWrite: true);
var plcWriteContentDrift = new PlcWriteRoundtripScenario(format => CompletedBlockOutcome(format, "succeeded"), driftAtImport: true);
var plcWriteUnverifiable = new PlcWriteRoundtripScenario(format => CompletedBlockOutcome(format, "succeeded"), skippedBlock: true);

string? line;
while ((line = Console.In.ReadLine()) is not null)
{
    if (!string.IsNullOrWhiteSpace(requestLog))
        AppendLogLine(requestLog, ReadMethod(line) ?? "unknown");
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
        // Script external loss observed while stamping a successful discovery response.
        var observation = fakePortals.Select(portal => Path.GetFileNameWithoutExtension(portal.ProjectPath))
            .FirstOrDefault(key => key is "discovery-project-closed" or "discovery-portal-lost" or "discovery-missing-identity" or "discovery-portal-restarted");
        if (observation is "discovery-project-closed" or "discovery-portal-lost" or "discovery-portal-restarted")
        {
            recoveryClosedSource = fakeProjectPath;
            var portal = AttachedPortal();
            if (portal is not null) portal.ProjectPath = null;
            fakeProjectPath = null;
            fakeSessionGeneration++;
            if (observation == "discovery-portal-restarted")
            {
                if (portal is not null) fakePortals.Remove(portal);
                fakePortals.Add(new FakePortalState { ProcessId = 44, HasUserInterface = true });
                fakePortalProcessId = 44;
            }
            if (observation == "discovery-portal-lost")
            {
                if (portal is not null) fakePortals.Remove(portal);
                fakePortalProcessId = null;
            }
        }
        Respond(JsonSerializer.Serialize(new WorkerResponse { Success = true, PortalProcessId = fakePortalProcessId,
            Payload = WorkerJson.SerializePayload(new TiaPortalProcessListInfo
        {
            AttachedProcessId = fakePortalProcessId,
            Processes = fakePortals.OrderBy(portal => portal.ProcessId).Select(portal => new TiaPortalProcessInfo
            {
                ProcessId = portal.ProcessId, ProjectPath = portal.ProjectPath,
                HasUserInterface = portal.HasUserInterface,
                AttachedByThisWorker = portal.ProcessId == fakePortalProcessId
            }).ToList()
        }) }, WorkerJson.Envelope), includeSessionIdentity: observation != "discovery-missing-identity");
        continue;
    }

    if (currentMethod == "select_portal_project")
    {
        SelectPortalProject(JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Envelope)!);
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
            lifecycleScenarios.FailClosedVerification)
        {
            // The scripted close verification fails decoding after the source has actually
            // closed. Keep the closed state while exercising the verifier's failure path.
            Respond(Success("{\"untrustedMarker\":true}"));
            continue;
        }

        if (fakeProjectPath is null && statusRead)
        {
            var recoveryFault = recoveryClosedSource is not null && File.Exists(recoveryClosedSource + ".recovery")
                ? File.ReadAllText(recoveryClosedSource + ".recovery") : null;
            if (currentMethod == "get_project_status" && recoveryClosedSource is not null)
            {
                if (recoveryFault == "pause-status" && currentExpectedSessionIdentity is null
                    || recoveryFault == "pause-proof" && currentExpectedSessionIdentity is not null)
                {
                    File.WriteAllText(recoveryClosedSource + ".observed", "status in flight");
                    if (!SpinWait.SpinUntil(() => File.Exists(recoveryClosedSource + ".release"), TimeSpan.FromSeconds(10)))
                        throw new InvalidOperationException("Recovery test did not release the status response.");
                }
                if (recoveryFault == "failed-status" || recoveryFault == "failed-proof" && currentExpectedSessionIdentity is not null)
                {
                    Respond(JsonSerializer.Serialize(new WorkerResponse
                    {
                        Success = false, FailureCategory = WorkerFailureCategories.WorkerOperationFailed,
                        Error = "Scripted owner inventory unavailable."
                    }, WorkerJson.Envelope));
                    continue;
                }
                var identity = new WorkerSessionIdentity
                {
                    WorkerSessionId = recoveryFault == "worker" ? "changed-worker" : workerSessionId,
                    PortalProcessId = recoveryFault == "portal" ? 999 : fakePortalProcessId,
                    SessionGeneration = recoveryFault == "negative-generation" ? -1 : fakeSessionGeneration + (recoveryFault == "generation" ? 1 : 0),
                    Context = recoveryFault == "context" ? LocalContext(recoveryClosedSource, false) : null
                };
                Respond(JsonSerializer.Serialize(new WorkerResponse
                {
                    Success = true, SessionIdentity = recoveryFault == "missing" ? null : identity,
                    ResolvedProjectPath = recoveryFault == "resolved-owner" ? "C:/Projects/Other.ap21" : null,
                    PortalProcessId = recoveryFault == "missing-pid" ? null : fakePortalProcessId,
                    Payload = recoveryFault == "malformed" ? "{}" : WorkerJson.SerializePayload(new ProjectStatusResultInfo
                    {
                        Operation = recoveryFault == "wrong-operation" ? "open_project" : "get_project_status",
                        Success = recoveryFault != "payload-failed",
                        ProjectPath = recoveryFault == "owner" ? "C:/Projects/Other.amc21" : null,
                        Project = new ProjectStatusInfo
                        {
                            IsOpen = recoveryFault == "owner",
                            Path = recoveryFault == "owner" ? "C:/Projects/Other.amc21" : null
                        }
                    })
                }, WorkerJson.Envelope), includeSessionIdentity: false);
                continue;
            }
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
        if (Path.GetFileNameWithoutExtension(fakeProjectPath) == "local-owner-replaced"
            && ++localOwnerStatusReads == 2)
            fakeSessionGeneration++;
        var attached = AttachedPortal();
        var statusPayload = DirectStatusPayload(new ProjectStatusInfo
        {
            IsOpen = fakeProjectPath is not null, Path = fakeProjectPath,
            Name = fakeProjectPath is null ? null : Path.GetFileNameWithoutExtension(fakeProjectPath),
            IsModified = fakeProjectPath is null ? null : attached?.Modified,
            Version = fakeProjectPath is null || IsLocalPath(fakeProjectPath) ? null : "V21",
            Context = LocalContext(fakeProjectPath, attached?.WorkerOpened == true)
        });
        if (Path.GetFileNameWithoutExtension(fakeProjectPath) == "local-status-secret")
        {
            var corrupt = JsonNode.Parse(statusPayload)!.AsObject();
            corrupt["project"]!["context"]!["PRIVATE_STATUS_CONTEXT_MEMBER"] = "PRIVATE_STATUS_CONTEXT_VALUE";
            statusPayload = corrupt.ToJsonString();
        }
        Respond(Success(statusPayload));
        continue;
    }

    if (portalInventoryDeclared && IsLocalPath(fakeProjectPath)
        && currentMethod == "probe_open_project_rebind")
    {
        var driftFile = fakeSessionContainerPath is null ? null : fakeSessionContainerPath + ".drift";
        if (!localConfirmationDriftApplied && driftFile is not null && File.Exists(driftFile))
        {
            switch (File.ReadAllText(driftFile))
            {
                case "owner": fakeSessionGeneration++; break;
                case "consequence": AttachedPortal()!.Modified = true; break;
            }
            localConfirmationDriftApplied = true;
        }
        var destination = ReadField(line, "rebindDestinationProjectPath")!;
        Respond(Success(WorkerJson.SerializePayload(ProjectRebindStateInfo.Create(
            fakeProjectPath, destination, AttachedPortal()?.Modified ?? false,
            AttachedPortal()?.WorkerOpened == true,
            LocalContext(fakeProjectPath, AttachedPortal()?.WorkerOpened == true)))));
        continue;
    }
    if (portalInventoryDeclared && IsLocalPath(fakeProjectPath)
        && currentMethod is "get_basic_project_status" or "probe_project_status_for_lifecycle")
    {
        var status = new ProjectStatusInfo
        {
            IsOpen = true, Path = fakeProjectPath, IsModified = AttachedPortal()?.Modified,
            Context = LocalContext(fakeProjectPath, AttachedPortal()?.WorkerOpened == true,
                Path.GetFileNameWithoutExtension(fakeSessionContainerPath) == "local-session-open-verification-mismatch"
                    ? "C:/Private/PRIVATE_VERIFICATION_OWNER.als21" : null)
        };
        Respond(SuccessWithResolvedPath(WorkerJson.SerializePayload(new ProjectLifecycleResultInfo
        {
            Operation = currentMethod == "get_basic_project_status"
                ? "get_project_status" : currentMethod!,
            ProjectPath = fakeProjectPath, Project = status
        }), fakeProjectPath!));
        continue;
    }
    if (portalInventoryDeclared && IsLocalPath(fakeProjectPath) && currentMethod == "save_project")
    {
        // Mirrors LocalSession.Save(): the session is persisted locally and stays the same owner.
        AttachedPortal()!.Modified = false;
        Respond(SuccessWithResolvedPath(WorkerJson.SerializePayload(new ProjectLifecycleResultInfo
        {
            Operation = "save_project", ProjectPath = fakeProjectPath,
            Project = new ProjectStatusInfo
            {
                IsOpen = true, Path = fakeProjectPath, IsModified = false,
                Context = LocalContext(fakeProjectPath, AttachedPortal()?.WorkerOpened == true)
            }
        }), fakeProjectPath!));
        continue;
    }

    if (portalInventoryDeclared && currentMethod == "open_project"
        && Path.GetFileNameWithoutExtension(currentProjectPath)?.StartsWith("local-session-open-failed-", StringComparison.Ordinal) == true)
    {
        fakeProjectPath = null;
        fakeSessionGeneration++;
        Respond(JsonSerializer.Serialize(new WorkerResponse
        {
            Success = false,
            FailureCategory = Path.GetFileNameWithoutExtension(currentProjectPath)!["local-session-open-failed-".Length..],
            Error = "The ALS replacement failed after source close; inspect before retrying."
        }, WorkerJson.Envelope), includeSessionIdentity: false);
        continue;
    }

    if (portalInventoryDeclared && currentMethod == "open_project"
        && (Path.GetFileNameWithoutExtension(currentProjectPath) is "local-session-open" or "local-session-open-bad-result"
            or "local-session-open-context-mismatch" or "local-session-open-verification-mismatch"
            or "local-session-open-passive-observation" or "local-session-open-malformed-identity"
            or "local-session-open-eof" or "local-session-open-timeout")
        && string.Equals(Path.GetExtension(currentProjectPath), ".als21", StringComparison.OrdinalIgnoreCase))
    {
        if (Path.GetFileNameWithoutExtension(currentProjectPath) is "local-session-open-eof" or "local-session-open-timeout")
        {
            File.WriteAllText(currentProjectPath + ".attempted", "opener reached");
            if (Path.GetFileNameWithoutExtension(currentProjectPath) == "local-session-open-eof") Environment.Exit(17);
            Thread.Sleep(TimeSpan.FromSeconds(30));
        }
        var openedPath = ProjectPathNormalization.Canonicalize("C:/Projects/Local.amc21")!;
        var context = LocalContext(openedPath, true, currentProjectPath);
        var status = new ProjectStatusInfo
        {
            IsOpen = true, Path = openedPath, IsModified = false, Context = context
        };
        var payload = WorkerJson.SerializePayload(new ProjectLifecycleResultInfo
        {
            Operation = "open_project", ProjectPath = openedPath, Project = status
        });
        if (Path.GetFileNameWithoutExtension(currentProjectPath) == "local-session-open-bad-result")
        {
            var badResult = JsonNode.Parse(payload)!.AsObject();
            badResult["project"]!["context"]!["PRIVATE_MUTATION_RESULT_MEMBER"] = "PRIVATE_MUTATION_RESULT_VALUE";
            payload = badResult.ToJsonString();
        }
        Respond(SuccessWithResolvedPath(payload, openedPath));
        continue;
    }

    if (lifecycleScenarios.TryHandleGuarded(line, currentProjectPath, fakeProjectPath, out var lifecycleResponse))
    {
        Respond(lifecycleResponse);
        continue;
    }

    // The open-policy gate above already proved the request targets the UI-open project.
    // network-binding-mismatch keeps modelling a worker that resolves a different project.
    if (statusGroundingDeclared && currentMethod == "get_project_status" && currentExpectedSessionIdentity is null)
    {
        var grounded = DirectStatusPayload(new ProjectStatusInfo { IsOpen = true, Path = fakeProjectPath });
        Respond(scenario == "network-binding-mismatch"
            ? SuccessWithResolvedPath(grounded, @"C:\FakeWorker\Different.ap21")
            : Success(grounded));
        // One-shot: only the bootstrap answer changes; later identity-less status calls
        // (recovery probes, configured-project grounding) reach their scenario scripts again.
        statusGroundingDeclared = false;
        continue;
    }

    switch (scenario)
    {
        case "portal-disposed":
            Respond(currentMethod == "get_project_status"
                ? Success(DirectStatusPayload(new ProjectStatusInfo { IsOpen = true, Path = fakeProjectPath }))
                : JsonSerializer.Serialize(DisposedEngineeringFailure(), WorkerJson.Envelope));
            break;
        case "portal-lost":
            Respond(currentMethod == "get_project_status"
                ? Success(DirectStatusPayload(new ProjectStatusInfo { IsOpen = true, Path = fakeProjectPath }))
                : JsonSerializer.Serialize(new WorkerResponse
                {
                    Success = false,
                    FailureCategory = WorkerFailureCategories.PortalLost,
                    Error = "The Portal connection was lost."
                }, WorkerJson.Envelope));
            break;
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
                lifecycleScenarios.RecordOpenProject();
            }
            Respond(SuccessWithResolvedPath("{\"isOpen\":true}", currentProjectPath!));
            break;
        case @"C:\Lifecycle\B-ui-owned.ap21":
            if (ReadMethod(line) == "open_project")
            {
                lifecycleScenarios.RecordOpenProject();
            }
            Respond(SuccessWithResolvedPath("{\"isOpen\":true}", currentProjectPath!));
            break;
        case "C:\\bound\\Session.ap21":
            // Used by the "already bound but worker reports a different project" test: the
            // session is pre-bound to this literal path (see IsSameProject/TryResolveWithSnapshot), so a
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
            // (ProjectSessionBinding path matching) must not.
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
        case "block-outcome-success":
        case "block-outcome-postcondition":
        case "block-outcome-partial":
        case "block-outcome-missing-success":
        case "block-outcome-missing-failure":
        case "block-outcome-invalid":
        case "block-outcome-oversized":
        case "block-outcome-preimport-validation":
        case "block-outcome-preimport-access":
        case "block-outcome-preimport-no-project":
        case "block-outcome-preimport-project-open":
        case "block-outcome-preimport-identity":
        case "create-block-postcondition-failed":
            blockScenarios.Handle(line, scenario!, currentExpectedSessionIdentity, Respond);
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
            // the host request-to-WorkerRequest mapping.
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
        case "network-read-warnings":
        case "network-config-partial":
        case "network-config-all-skipped":
        case "network-roundtrip":
            networkScenarios.Handle(line, scenario!, seq, currentProjectPath, ScenarioKey(currentProjectPath), Respond, () => fakeSessionGeneration++);
            break;
        case "vci-write-roundtrip":
        case "vci-portal-lost":
        case "vci-timeout":
        case "vci-timeout-silent":
        case "vci-crash":
            Respond(vciWriteRoundtrip.Handle(line, scenario));
            break;
        case "vci-read-roundtrip":
            Respond(TiaMcpServer.FakeWorker.Vci.VciReadRoundtripScenario.Handle(line));
            break;
        case "hmi-read-roundtrip":
            Respond(ReadMethod(line) switch
            {
                "hmi_list_hmi_devices" => Success(WorkerJson.SerializePayload(new HmiDeviceListInfo
                {
                    Devices =
                    {
                        new HmiDeviceInfo { DeviceName = "HMI_1", SoftwareName = "HMI_1_RT", Kind = "unified", TypeIdentifier = null },
                    },
                })),
                // Echoes the forwarded query so the host test can see the prefixed method and parameters arrive.
                "hmi_list_tags" => Success(WorkerJson.SerializePayload(new HmiTagListInfo
                {
                    Page = new HmiPage(ReadHmiQueryInt(line, "offset") ?? 0, ReadHmiQueryInt(line, "limit") ?? 0, 1, null),
                    Tags =
                    {
                        new HmiTagRowInfo
                        {
                            Name = $"{ReadHmiQueryField(line, "hmiName")}|{ReadHmiQueryField(line, "tableName")}|{ReadHmiQueryInt(line, "offset")}|{ReadHmiQueryInt(line, "limit")}",
                        },
                    },
                })),
                "hmi_get_tag" when ReadHmiQueryField(line, "tagName") == "Missing"
                    => """{"success":false,"failureCategory":"target_not_found","error":"tag not found"}""",
                "hmi_get_tag" => Success(WorkerJson.SerializePayload(new HmiTagDetailInfo { Tag = new HmiTagRowInfo { Name = ReadHmiQueryField(line, "tagName") ?? string.Empty } })),
                // Deliberately does not decode as HmiSystemTagListInfo: must surface as protocol_error.
                "hmi_list_system_tags" => Success("""{"secret":"SENTINEL-PAYLOAD"}"""),
                _ => $$"""{"success":false,"error":"unexpected hmi read method '{{ReadMethod(line)}}'"}"""
            });
            break;
        case "hmi-read-paged":
            // Five rows "1".."5"; snapshot "s1" is the only continuation the worker still holds.
            if (ReadMethod(line) != "hmi_list_tags")
            {
                Respond("""{"success":false,"error":"unexpected hmi paged method"}""");
            }
            else if (ReadHmiQueryField(line, "snapshotId") is { } heldSnapshot && heldSnapshot != "s1")
            {
                Respond("""{"success":false,"failureCategory":"snapshot_unavailable","error":"snapshot not held"}""");
            }
            else
            {
                var pageOffset = ReadHmiQueryInt(line, "offset") ?? 0;
                var pageLimit = ReadHmiQueryInt(line, "limit") ?? 2;
                var pageEnd = Math.Min(pageOffset + pageLimit, 5);
                var hasMore = pageEnd < 5;
                var pagedList = new HmiTagListInfo
                {
                    Page = new HmiPage(pageOffset, pageLimit, 5, hasMore ? pageEnd : null)
                    {
                        SnapshotId = hasMore ? "s1" : null,
                    },
                };
                for (var n = pageOffset + 1; n <= pageEnd; n++)
                {
                    pagedList.Tags.Add(new HmiTagRowInfo { Name = n.ToString() });
                }

                Respond(Success(WorkerJson.SerializePayload(pagedList)));
            }

            break;
        case "plc-read-roundtrip":
            Respond(ReadMethod(line) switch
            {
                "get_block_content" when ReadField(line, "blockPath") == "PLC_1/Missing"
                    => """{"success":false,"error":"block not found"}""",
                "get_block_content" => Success($"<Block path=\"{ReadField(line, "blockPath")}\" format=\"{ReadField(line, "format")}\"/>"),
                "get_type_content" => Success($"TYPE \"{ReadField(line, "typePath")}\" format={ReadField(line, "format")}"),
                "list_tag_tables" => Success(WorkerJson.SerializePayload(new PlcTagInventoryInfo
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
        case "hmi-write-roundtrip":
            Respond(hmiWriteRoundtrip.Handle(line));
            break;
        case "plc-write-roundtrip":
            Respond(plcWriteRoundtrip.Handle(line, scenario));
            break;
        case "plc-write-incomplete":
            Respond(plcWriteIncomplete.Handle(line, scenario));
            break;
        case "plc-write-late-block":
            Respond(plcWriteLateBlock.Handle(line, scenario));
            break;
        case "plc-write-export-lost":
            Respond(plcWriteExportLost.Handle(line, scenario));
            break;
        case "plc-write-content-drift":
            Respond(plcWriteContentDrift.Handle(line, scenario));
            break;
        case "plc-write-unverifiable":
            Respond(plcWriteUnverifiable.Handle(line, scenario));
            break;
        case "xref-roundtrip":
            Respond(ReadMethod(line) != "read_cross_references"
                ? $$"""{"success":false,"error":"unexpected xref method '{{ReadMethod(line)}}'"}"""
                : LastXrefSegmentName(line) == "Unsupported"
                    ? """{"success":false,"failureCategory":"target_kind_unsupported","error":"The selected target does not provide cross-references."}"""
                    : Success(WorkerJson.SerializePayload(XrefReport(1, 10, ReadField(line, "crossReferenceFilter") != "UnusedObjects"))));
            break;
        case "xref-oversized":
            // 40 sources of ~3,000 characters exceed the 60,000-character value budget.
            Respond(Success(WorkerJson.SerializePayload(XrefReport(40, 3_000, true))));
            break;
        case "network-mixed-results":
        case "network-binding-mismatch":
        case "network-connection-evidence":
        case "network-connection-evidence-degraded":
        case "network-state-seq":
        case "network-io-map":
        case "network-io-map-malformed":
        case "project-enumeration-completeness":
            networkScenarios.Handle(line, scenario!, seq, currentProjectPath, ScenarioKey(currentProjectPath), Respond, () => fakeSessionGeneration++);
            break;
        case "project-tree-v3-snapshot":
            Respond(ReadMethod(line) == "browse_project_tree_v3_snapshot"
                && HasNonNullField(line, "startSelector")
                ? SuccessWithResolvedPath(
                    WorkerJson.SerializePayload(ProjectTreeV3Snapshot()),
                    ReadField(line, "projectPath") ?? scenario)
                : $$"""{"success":false,"error":"expected typed browse_project_tree_v3_snapshot"}""");
            break;
        case "project-tree-v3-small":
        case "project-tree-v3-disposed":
        case "project-tree-v3-one-shot":
            Respond(scenario == "project-tree-v3-disposed" && currentMethod == "list_tag_tables"
                ? JsonSerializer.Serialize(DisposedEngineeringFailure(), WorkerJson.Envelope)
                : ProjectTreeV3ScenarioResponse(line, scenario));
            break;
        case "project-tree-v3-malformed":
            Respond(ReadMethod(line) == "browse_project_tree_v3_snapshot"
                ? Success("""{"startSelector":null,"depth":null,"roots":{"PROJECT_TREE_SECRET_MARKER":true}}""")
                : $$"""{"success":false,"error":"expected browse_project_tree_v3_snapshot, got '{{ReadMethod(line)}}'"}""");
            break;
        case "hardware-pagination":
        case "hardware-pagination-missing-identity":
        case "hardware-pagination-payload-only-identity":
        case "hardware-pagination-missing-continuation-identity":
        case "hardware-pagination-binding-conflict":
        case "hardware-pagination-malformed-offset":
        case "hardware-pagination-incoherent-counts":
        case "hardware-pagination-wrong-payload":
        case "hardware-pagination-snapshot-drift":
        case "hardware-pagination-out-of-range":
        case "hardware-pagination-identity-drift":
        case "hardware-pagination-trimming":
        case "hardware-pagination-telemetry":
        case "network-unresolvable-target":
        case "network-write-item-failure":
        case "multi-homed-network":
        case "network-ambiguous-node":
        case "invalid-network-success-payload":
            networkScenarios.Handle(line, scenario!, seq, currentProjectPath, ScenarioKey(currentProjectPath), Respond, () => fakeSessionGeneration++);
            break;
        case "direct-status-only":
            lifecycleScenarios.Handle(line, scenario!, currentProjectPath, currentExpectedSessionIdentity, Respond);
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
            compileIdentityDrift = scenario == "compile-identity-drift" && ReadMethod(line) == "compile_check";
            Respond(ReadMethod(line) switch
            {
                "get_project_status" => Success(DirectStatusPayload(new ProjectStatusInfo { IsOpen = true, Path = currentProjectPath })),
                "read_hardware_config" => Success(WorkerJson.SerializePayload(new HardwareConfigInfo())),
                "compile_check" => StandaloneCompileResponse(scenario, line),
                _ => $$"""{"success":false,"error":"unexpected standalone compile method '{{ReadMethod(line)}}'"}"""
            });
            compileIdentityDrift = false;
            break;
        case "status-no-project":
        case "status-with-metadata":
        case "status-malformed":
        case "status-oversized":
        case "lifecycle-probe-only":
        case @"C:\FakeWorker\lifecycle-rebind-probe.ap21":
        case @"C:\FakeWorker\lifecycle-rebind-probe-missing-source.ap21":
        case @"C:\FakeWorker\lifecycle-rebind-probe-null-modified.ap21":
        case @"C:\FakeWorker\lifecycle-rebind-probe-wrong-destination.ap21":
        case @"C:\FakeWorker\lifecycle-rebind-probe-wrong-disposition.ap21":
        case @"C:\FakeWorker\lifecycle-rebind-probe-binding-conflict.ap21":
        case "save-as-uncertain-state":
        case "C:\\bound\\FailingSave.ap21":
        case "C:\\Projects\\SimpleProject\\SimpleProject.ap21":
            lifecycleScenarios.Handle(line, scenario!, currentProjectPath, currentExpectedSessionIdentity, Respond);
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
        case "list-network-objects-success":
        case "inspect-network-object-success":
        case "list-network-objects-malformed":
        case "inspect-network-object-malformed":
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
        case "network-subnet-lifecycle":
        case "network-subnet-lifecycle-alt-path":
        case "network-subnet-lifecycle-malformed-success":
        case "network-subnet-lifecycle-postcondition-failed":
        case "network-subnet-lifecycle-second-item-failure":
        case "list-network-objects-large":
            networkScenarios.Handle(line, scenario!, seq, currentProjectPath, ScenarioKey(currentProjectPath), Respond, () => fakeSessionGeneration++);
            break;
        default:
            Respond($$"""{"success":false,"error":"unknown scenario '{{scenario}}'"}""");
            break;
    }
}

FakePortalState? AttachedPortal()
    => fakePortals.FirstOrDefault(portal => portal.ProcessId == fakePortalProcessId);

void SelectPortalProject(WorkerRequest request)
{
    var targetPath = ProjectPathNormalization.Canonicalize(request.ProjectPath);
    var advertisers = fakePortals.Where(portal => request.PortalProcessId is { } pid
        ? portal.ProcessId == pid
        : targetPath is not null && string.Equals(portal.ProjectPath, targetPath, StringComparison.OrdinalIgnoreCase)).ToList();
    if (advertisers.Count == 0 && request.PortalProcessId is null && IsLocalPath(targetPath)
        && fakePortals.Count == 1)
        advertisers.Add(fakePortals[0]);
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
    var selectedOwnerPath = target.ProcessId == fakePortalProcessId
        ? fakeProjectPath : target.ProjectPath;
    if (selectedOwnerPath is null || targetPath is not null
        && !string.Equals(selectedOwnerPath, targetPath, StringComparison.OrdinalIgnoreCase))
    {
        Respond(JsonSerializer.Serialize(new WorkerResponse
        {
            Success = false, FailureCategory = WorkerFailureCategories.TargetNotFound,
            Error = "The selected Portal does not contain the exact requested open owner."
        }, WorkerJson.Envelope));
        return;
    }
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
    switch (ScenarioKey(targetPath ?? selectedOwnerPath))
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
    var sameOwner = !reattached && string.Equals(fakeProjectPath, selectedOwnerPath, StringComparison.OrdinalIgnoreCase);
    fakeProjectPath = selectedOwnerPath;
    // Cold adoption never claims ownership; re-verifying the same owner preserves it.
    if (!sameOwner) target.WorkerOpened = false;
    if (!sameOwner) fakeSessionGeneration++;
    Respond(Success(ScenarioKey(targetPath ?? selectedOwnerPath) == "portal-selection-malformed"
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
    if (alias is "identity-loss" or "portal-lost" or "server-failure" or "target_not_found" or "target_ambiguous")
    {
        Respond(JsonSerializer.Serialize(new WorkerResponse { Success = false,
            FailureCategory = alias == "identity-loss" ? WorkerFailureCategories.BindingConflict
                : alias == "portal-lost" ? WorkerFailureCategories.PortalLost
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
            { RemoteIdentity = identity, ConnectionObservation = observation, Projects = [new() { Name = alias == "oversized" ? new string('x', 60010) : "Project A" }] }),
        "list_local_sessions" => WorkerJson.SerializePayload(new MultiuserLocalSessionsInfo
            { RemoteIdentity = identity, ConnectionObservation = observation, Sessions = [new() { SessionId = 0, ProjectPath = "C:\\Sessions\\A.als21" }] }),
        "get_lock_state" => WorkerJson.SerializePayload(new MultiuserLockStateInfo
            { RemoteIdentity = identity, ConnectionObservation = observation, IsLocked = false, ObservedAt = observation.ObservedAt }),
        _ => throw new InvalidOperationException()
    };
    Respond(JsonSerializer.Serialize(new WorkerResponse { Success = true,
        Payload = alias == "malformed" ? "{\"secret-sentinel\":true}" : payload,
        PortalProcessId = alias == "missing-pid" ? null : alias == "wrong-pid" ? 43 : fakePortalProcessId }, WorkerJson.Envelope));
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
            lifecycleScenarios.UpgradeLegacyFixture(response, currentMethod, currentProjectPath, fakeProjectPath, currentRequestLine, ScenarioKey);
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
                fakeSessionContainerPath = null;
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
                    fakeSessionContainerPath = currentMethod == "open_project"
                        && string.Equals(Path.GetExtension(currentProjectPath), ".als21", StringComparison.OrdinalIgnoreCase)
                        && IsLocalPath(projectPath)
                        ? ProjectPathNormalization.Canonicalize(currentProjectPath) : null;
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
                WorkerSessionId = compileIdentityDrift || networkScenarios.IdentityDrift ? "drifted-worker-session" : workerSessionId,
                SessionGeneration = fakeSessionGeneration,
                PortalProcessId = fakePortalProcessId,
                ProjectPath = successful && currentMethod == "select_portal_project"
                    && ScenarioKey(currentProjectPath) == "portal-selection-other-path"
                    ? ProjectPathNormalization.Canonicalize("C:/Projects/Wrong.ap21") : projectPath,
                Context = LocalContext(projectPath, AttachedPortal()?.WorkerOpened == true)
            }, WorkerJson.Envelope);
            if (currentMethod == "open_project" && recoveryClosedSource is not null
                && File.Exists(recoveryClosedSource + ".recovery"))
            {
                switch (File.ReadAllText(recoveryClosedSource + ".recovery"))
                {
                    case "open-worker": response["sessionIdentity"]!["workerSessionId"] = "changed-worker"; break;
                    case "open-portal": response["sessionIdentity"]!["portalProcessId"] = 999; break;
                    case "open-generation": response["sessionIdentity"]!["sessionGeneration"] = fakeSessionGeneration - 1; break;
                    case "open-missing": response["sessionIdentity"] = null; break;
                }
            }
            if (response["sessionIdentity"]?["context"] is JsonObject context)
            {
                if (currentMethod == "open_project"
                    && Path.GetFileNameWithoutExtension(currentProjectPath) == "local-session-open-malformed-identity")
                {
                    context.Remove("remoteIdentity");
                    response["payload"] = "PRIVATE_MALFORMED_IDENTITY_PAYLOAD";
                }
                if (currentMethod == "open_project"
                    && Path.GetFileNameWithoutExtension(currentProjectPath) == "local-session-open-context-mismatch")
                    context["sessionContainerPath"] = "C:/Private/PRIVATE_ENVELOPE_OWNER.als21";
                if (currentMethod == "open_project"
                    && Path.GetFileNameWithoutExtension(currentProjectPath) == "local-session-open-passive-observation")
                    context["connectionObservation"]!["observedAt"] = "2026-01-01T00:00:00+00:00";
                if (Path.GetFileNameWithoutExtension(projectPath) == "local-envelope-secret")
                    context["PRIVATE_IDENTITY_CONTEXT_MEMBER"] = "PRIVATE_IDENTITY_CONTEXT_VALUE";
                if (Path.GetFileNameWithoutExtension(projectPath) == "local-envelope-missing-null")
                    context.Remove("sessionContainerPath");
                if (Path.GetFileNameWithoutExtension(projectPath) == "local-envelope-invalid-mode")
                    context["sessionMode"] = "invalid_session_mode";
                if (Path.GetFileNameWithoutExtension(projectPath) == "local-envelope-mismatched-path")
                    context["engineeringProjectPath"] = "C:/Projects/PRIVATE_OTHER_ENGINEERING_PATH.amc21";
                if (Path.GetFileNameWithoutExtension(projectPath) == "local-envelope-forged-remote")
                    context["remoteIdentity"] = new JsonObject { ["serverAlias"] = "PRIVATE_FORGED_SERVER_ALIAS" };
            }
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

WorkerResponse DisposedEngineeringFailure()
    => WorkerExecution.Execute(() =>
    {
        portalDisposalRevision++;
        throw new Siemens.Engineering.EngineeringException("disposed composition");
    }, () => portalDisposalRevision);

string? ScenarioKey(string? path)
{
    if (path is not null && lifecycleScenarios.ProbeOnlyCopiedPath is not null
        && string.Equals(path, lifecycleScenarios.ProbeOnlyCopiedPath, StringComparison.OrdinalIgnoreCase))
        return "lifecycle-probe-only";
    if (path is not null && path.EndsWith(".ap21", StringComparison.OrdinalIgnoreCase))
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name is "project-tree-v3-small" or "project-tree-v3-disposed") return name;
        if (name is "portal-switch-fails-after-detach" or "portal-switch-hang" or "portal-switch-crash"
            or "portal-selection-malformed" or "portal-selection-other-path") return name;
        if (name.StartsWith("lifecycle-rebind-probe", StringComparison.Ordinal)) return @"C:\FakeWorker\" + name + ".ap21";
        if (name is "lifecycle-probe-only" or "worker-error-with-category" or "save-as-uncertain-state") return name;
        if (name is "network-roundtrip" or "portal-lost" or "portal-disposed") return name;
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

// Tests read these logs while the worker runs. A reader holding the file makes Windows refuse the
// append (sharing or lock violation), so wait briefly instead of crashing the worker.
static void AppendLogLine(string path, string text)
{
    for (var attempt = 0; ; attempt++)
    {
        try
        {
            File.AppendAllText(path, text + Environment.NewLine);
            return;
        }
        catch (IOException ex) when (attempt < 100 && (ex.HResult & 0xFFFF) is 32 or 33)
        {
            Thread.Sleep(50);
        }
    }
}

string? ReadHmiQueryField(string requestLine, string name)
{
    using var doc = JsonDocument.Parse(requestLine);
    return doc.RootElement.TryGetProperty("hmiQuery", out var q) && q.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

int? ReadHmiQueryInt(string requestLine, string name)
{
    using var doc = JsonDocument.Parse(requestLine);
    return doc.RootElement.TryGetProperty("hmiQuery", out var q) && q.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
}

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

bool IsLocalPath(string? path)
    => string.Equals(Path.GetExtension(path), ".amc21", StringComparison.OrdinalIgnoreCase);

ProjectContextInfo? LocalContext(string? path, bool openedByWorker, string? sessionContainerPath = null)
    => !IsLocalPath(path) ? null : new ProjectContextInfo
    {
        ContainerKind = ProjectContainerKinds.LocalSession,
        SessionMode = MultiuserSessionModes.Unknown,
        Capabilities = ProjectCapabilityCatalog.Describe(ProjectContainerKinds.LocalSession).ToList(),
        RemoteIdentity = Path.GetFileNameWithoutExtension(path) == "local-context-oversized"
            ? new MultiuserRemoteIdentity
            {
                ServerAlias = new string('x', 61_000), Host = "server", Port = 1234,
                Protocol = "https", Group = new ProjectServerGroupIdentity { IsRoot = false, Name = "Group A" },
                ServerProjectName = "Project A", LocalSessionId = 0,
                LocalSessionPath = "C:/Sessions/A.als21"
            }
            : null,
        ConnectionObservation = new ProjectServerConnectionObservation
        {
            State = ProjectServerConnectionStates.Unknown,
            ObservedAt = DateTimeOffset.UtcNow,
            ObservationSource = openedByWorker
                ? ProjectServerConnectionObservationSources.SessionOpen
                : ProjectServerConnectionObservationSources.SessionBind
        },
        EngineeringProjectPath = path,
        SessionContainerPath = openedByWorker ? sessionContainerPath ?? fakeSessionContainerPath : null,
        OpenedByWorker = openedByWorker
    };

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
    return Success(WorkerJson.SerializePayload(report));
}

// Hardware fixtures are serialized from the shared Contracts DTOs and carry the same deterministic
// selectors the real worker now emits. Keeping this construction in one place means a future
// selector-contract change fails the FakeWorker build instead of silently invalidating every
// preview/apply scenario with stale hand-written JSON.
int NextProjectTreeV3ScenarioCall(string scenario)
{
    projectTreeV3ScenarioCalls.TryGetValue(scenario, out var calls);
    calls++;
    projectTreeV3ScenarioCalls[scenario] = calls;
    return calls;
}

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
    },
    Skipped = new List<ProjectTreeSkippedNodeInfo>()
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
        WorkerJson.SerializePayload(ProjectTreeV3Fixture()),
        ReadField(requestLine, "projectPath") ?? scenario);
}

ProjectTreeBrowseResultInfo ProjectTreeV3Fixture() => new()
{
    StartSelector = null,
    Depth = null,
    Skipped = new List<ProjectTreeSkippedNodeInfo>(),
    Roots = new List<ProjectTreeNode>
    {
        new()
        {
            Name = "PLC_1",
            NodeType = ProjectTreeNodeTypes.Device,
            Details = null,
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
