using System.Text.Json;
using Siemens.Engineering;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.CrossReferences;
using TiaMcpServer.Contracts.Diagnostics;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Vci;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Openness.CrossReferences;
using TiaMcpServer.OpennessWorker.Openness.Hmi;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.Multiuser;
using TiaMcpServer.OpennessWorker.Openness.Network;
using TiaMcpServer.OpennessWorker.Openness.Project;
using TiaMcpServer.OpennessWorker.Openness.Vci;
using TiaMcpServer.OpennessWorker.Worker;
using WorkerTiaPortalSession = TiaMcpServer.OpennessWorker.Openness.Project.TiaPortalSession;

namespace TiaMcpServer.OpennessWorker;

internal static partial class Program
{
    // TIA Portal 권한 요청 다이얼로그는 Attach() 호출마다 뜬다.
    // 세션을 프로세스 수명 동안 재사용해 Attach()를 최초 1회만 호출한다.
    private static readonly WorkerTiaPortalSession _sharedSession = new(allowTiaConfirmations: true);
    private static readonly MultiuserInventoryService _sharedInventory = new(_sharedSession);

    private static readonly McpAccessMode _accessMode = WorkerOperationAuthorization.ParseAccessMode(
        Environment.GetCommandLineArgs());

    static Program()
    {
        AssemblyResolver.Register();
    }

    private static void Main()
    {
        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        Console.Error.WriteLine($"TIA Openness worker access mode: {McpAccessModeNames.ToName(_accessMode).ToUpperInvariant()}");

        try
        {
            string? line;
            while ((line = Console.In.ReadLine()) is not null)
            {
                var response = HandleLineWithCapturedStderr(line);
                Console.Out.WriteLine(JsonSerializer.Serialize(response, WorkerJson.Envelope));
                Console.Out.Flush();
            }
        }
        finally
        {
            _sharedSession.Dispose();
        }
    }

    /// <summary>
    /// Redirects Console.Error to a per-request buffer so degradation lines ("Skipping X…")
    /// become structured response warnings instead of racy stderr in the persistent worker.
    /// Async TIA events that fire BETWEEN requests still hit the real stderr stream.
    /// </summary>
    private static WorkerResponse HandleLineWithCapturedStderr(string line)
    {
        var originalError = Console.Error;
        var buffer = new System.IO.StringWriter();
        // TIA events can write from other threads while a request runs; synchronize the buffer.
        Console.SetError(System.IO.TextWriter.Synchronized(buffer));

        WorkerResponse response;
        try
        {
            response = HandleLine(line);
        }
        finally
        {
            Console.SetError(originalError);
        }

        var captured = SplitWarningLines(buffer.ToString());
        response.Warnings = WorkerWarningMerger.Merge(response.Warnings, captured);

        return response;
    }

    private static List<string> SplitWarningLines(string captured)
    {
        var lines = new List<string>();
        foreach (var raw in captured.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = raw.Trim();
            if (trimmed.Length > 0)
            {
                lines.Add(trimmed);
            }
        }

        return lines;
    }

    private static WorkerResponse HandleLine(string line)
    {
        try
        {
            var request = JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Envelope);
            if (request is null)
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "Worker request was empty.");
            }

            // Protocol compatibility is established before authorization, Attach(), or any
            // Siemens API call. This prevents a stale same-major worker from performing a
            // lifecycle mutation and only afterwards revealing that it cannot enforce identity.
            if (string.Equals(request.Method, "hello", StringComparison.Ordinal))
            {
                if (!string.Equals(request.ProtocolVersion, WorkerProtocol.Version, StringComparison.Ordinal))
                {
                    return Failure(
                        WorkerFailureCategories.ValidationError,
                        $"Worker protocol mismatch. Host requested '{request.ProtocolVersion ?? "(missing)"}', "
                        + $"worker requires '{WorkerProtocol.Version}'.");
                }

                return new WorkerResponse
                {
                    Success = true,
                    Payload = "{}",
                    ProtocolVersion = WorkerProtocol.Version,
                    Capabilities = WorkerProtocol.RequiredCapabilities.ToList()
                };
            }

            if (!string.Equals(request.ProtocolVersion, WorkerProtocol.Version, StringComparison.Ordinal))
            {
                return Failure(
                    WorkerFailureCategories.ValidationError,
                    $"Worker protocol mismatch. Request carried '{request.ProtocolVersion ?? "(missing)"}', "
                    + $"worker requires '{WorkerProtocol.Version}'. No Siemens operation was performed.");
            }

            // Defense in depth: authorize BEFORE the dispatch switch, before any handler
            // is called, and before any Siemens API call is made.
            var denial = WorkerOperationAuthorization.Authorize(_accessMode, request.Method);
            if (denial is not null)
            {
                return request.Method == "update_block_logic"
                    ? BlockUpdateOutcomeDecorator.Decorate(
                        denial,
                        BlockUpdateOutcomeDecorator.NormalizeFormatOrNull(request.Format),
                        importerEntered: false)
                    : denial;
            }

            return request.Method switch
            {
                "list_tia_portal_processes" => ListPortalProcesses(request),
                "list_server_connections" or "list_server_groups" or "list_server_projects" or "list_local_sessions" or "get_lock_state"
                    => WithPortalRead(request, _ => MultiuserPortalReadDispatch.Invoke(_sharedInventory, request)),
                "select_portal_project" => SelectPortalProject(request),
                "browse_project_tree_v3_snapshot" => BrowseProjectTreeV3Snapshot(request),
                "read_hardware_config" => NetworkWorkerDispatch.ReadHardwareConfig(request),
                "read_hardware_page_candidates" => NetworkWorkerDispatch.ReadHardwarePageCandidates(request),
                "list_network_objects" => NetworkWorkerDispatch.ListNetworkObjects(request),
                "inspect_network_object" => NetworkWorkerDispatch.InspectNetworkObject(request),
                "search_equipment_catalog" => NetworkWorkerDispatch.SearchEquipmentCatalog(request),
                "add_network_device" => NetworkWorkerDispatch.AddNetworkDevice(request),
                "configure_network_device" => NetworkWorkerDispatch.ConfigureNetworkDevice(request),
                "create_subnet" => NetworkWorkerDispatch.CreateSubnet(request),
                "update_subnet" => NetworkWorkerDispatch.UpdateSubnet(request),
                "delete_subnet" => NetworkWorkerDispatch.DeleteSubnet(request),
                "read_cross_references" => ReadCrossReferences(request),
                "get_block_content"   => PlcWorkerDispatch.GetBlockContent(request),
                "update_block_logic"  => PlcWorkerDispatch.UpdateBlockLogic(request),
                "get_type_content"    => PlcWorkerDispatch.GetTypeContent(request),
                "update_type_content" => PlcWorkerDispatch.UpdateTypeContent(request),
                "list_tag_tables"     => PlcWorkerDispatch.ListTagTables(request),
                "compile_check"       => PlcWorkerDispatch.CompileCheck(request),
                "create_tag_table"    => PlcWorkerDispatch.CreateTagTable(request),
                "delete_tag_table"    => PlcWorkerDispatch.DeleteTagTable(request),
                "create_tag"          => PlcWorkerDispatch.CreateTag(request),
                "update_tag"          => PlcWorkerDispatch.UpdateTag(request),
                "delete_tag"          => PlcWorkerDispatch.DeleteTag(request),
                "create_user_constant" => PlcWorkerDispatch.CreateUserConstant(request),
                "update_user_constant" => PlcWorkerDispatch.UpdateUserConstant(request),
                "delete_user_constant" => PlcWorkerDispatch.DeleteUserConstant(request),
                "get_project_status"  => GetProjectStatus(request),
                "probe_project_status_for_lifecycle" => ProbeProjectStatusForLifecycle(request),
                "probe_open_project_rebind" => ProbeOpenProjectRebind(request),
                "get_basic_project_status" => GetBasicProjectStatus(request),
                "create_block"        => PlcWorkerDispatch.CreateBlock(request),
                "delete_block"        => PlcWorkerDispatch.DeleteBlock(request),
                "create_block_group"  => PlcWorkerDispatch.CreateBlockGroup(request),
                "delete_block_group"  => PlcWorkerDispatch.DeleteBlockGroup(request),
                "open_project"        => OpenProject(request),
                "create_project"      => CreateProject(request),
                "save_project"        => SaveProject(request),
                "save_project_as"     => SaveProjectAs(request),
                "archive_project"     => ArchiveProject(request),
                "close_project"       => CloseProject(request),
                var m when HmiWriteMethods.All.Contains(m, StringComparer.Ordinal) => WriteHmi(request),
                var m when HmiReadMethods.All.Contains(m, StringComparer.Ordinal) => ReadHmi(request),
                var m when VciMethods.Read.Contains(m, StringComparer.Ordinal) => ReadVci(request),
                VciMethods.Plan => PlanVci(request),
                var m when VciMethods.Write.Contains(m, StringComparer.Ordinal) => WriteVci(request),
                _ => throw new WorkerOperationException(
                    WorkerFailureCategories.ValidationError,
                    $"Unsupported worker method '{request.Method}'.")
            };
        }
        catch (JsonException ex)
        {
            return Failure(WorkerFailureCategories.ValidationError, $"Worker request was invalid JSON: {ex.Message}");
        }
        catch (NetworkCursorException ex)
        {
            return Failure(ex.Category, ex.Message);
        }
        catch (WorkerOperationException ex)
        {
            return Failure(ex.FailureCategory, ex.Message, ex.Warnings, ex.BlockImportOutcome, ex.HmiWriteOutcome, ex.VciWriteOutcome, ex.EngineeringError, ex.ConfigureNetworkDeviceOutcome);
        }
        catch (Exception ex)
        {
            // Type + message only — never the full exception (which includes a stack trace) —
            // since this is captured into the response's Warnings by HandleLineWithCapturedStderr.
            Console.Error.WriteLine($"Unhandled worker exception: {ex.GetType().Name}: {ex.Message}");
            return Failure(WorkerFailureCategories.WorkerOperationFailed, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static WorkerResponse ListPortalProcesses(WorkerRequest request)
    {
        return Execute(() =>
        {
            var inventory = TiaPortalProcessInventory.Read();
            return Success(new TiaPortalProcessListInfo
            {
                AttachedProcessId = _sharedSession.CurrentProcessId,
                Processes = TiaPortalTargetSelector.ToProcessInfos(
                    inventory.Select(entry => entry.Candidate).ToList(), _sharedSession.CurrentProcessId)
            });
        });
    }

    private static WorkerResponse SelectPortalProject(WorkerRequest request)
    {
        return Execute(() =>
        {
            if (request.ProjectPath is not null && string.IsNullOrWhiteSpace(request.ProjectPath))
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                    "select_portal_project requires an exact projectPath when supplied.");
            }

            ValidateExpectedSessionIdentityForRequest(_sharedSession, request,
                allowMissingExpectedIdentity: true, useCachedIdentity: true);
            return Success(_sharedSession.SelectPortalProject(request.ProjectPath, request.PortalProcessId));
        });
    }

    private static WorkerResponse BrowseProjectTreeV3Snapshot(WorkerRequest request)
    {
        try
        {
            ProjectTreeNodeTypes.Validate(request.StartSelector);
            return WithEngineeringRoot(request, project =>
            {
                var selected = new ProjectTreeSnapshotWalker().WalkSnapshot(project, request.StartSelector, request.Depth);
                return Success(new ProjectTreeBrowseResultInfo
                {
                    StartSelector = selected.CanonicalStartSelector?.ToList(),
                    Depth = request.Depth,
                    Roots = selected.Roots.ToList(),
                    Skipped = selected.Skipped.ToList()
                });
            });
        }
        catch (ProjectTreeSelectionException exception)
        {
            throw new WorkerOperationException(exception.Category, exception.Message);
        }
    }

    private static WorkerResponse ReadCrossReferences(WorkerRequest request)
    {
        if (!CrossReferenceFilterNames.TryNormalize(
                request.CrossReferenceFilter,
                out var filter,
                out var filterError))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                filterError ?? "Invalid cross-reference filter.");
        }

        if (request.CrossReferenceSelector is null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.InvalidSelector,
                "read_cross_references requires a target selector.");
        }

        return WithEngineeringRoot(request, project => Success(
            CrossReferenceReader.Read(project, request.CrossReferenceSelector, filter, request.MaxResults)));
    }

    private static WorkerResponse WriteHmi(WorkerRequest request)
        => WithEngineeringRoot(request, project => Success(HmiWriteDispatch.Write(project, request.Method, request.HmiWrite ?? new HmiWriteRequestInfo())));

    private static WorkerResponse ReadHmi(WorkerRequest request)
    {
        return WithEngineeringRoot(request, project => Success(
            HmiReadDispatch.Read(project, request.Method, request.HmiQuery ?? new HmiQueryInfo())));
    }

    private static WorkerResponse ReadVci(WorkerRequest request)
        => WithEngineeringRoot(request, project => Success(
            VciDispatch.Read(project, request.Method, request.VciQuery ?? new VciQueryInfo())));

    private static WorkerResponse PlanVci(WorkerRequest request)
        => WithEngineeringRoot(request, project => Success(
            VciDispatch.Plan(project, request.VciWrite ?? new VciWriteRequestInfo())));

    private static WorkerResponse WriteVci(WorkerRequest request)
        => WithActiveContext(request, session => Success(VciDispatch.Write(session.TiaPortal!,
            session.EngineeringRoot!, request.Method, request.VciWrite ?? new VciWriteRequestInfo())));

    private static WorkerResponse GetProjectStatus(WorkerRequest request)
    {
        if (request.ProjectPath is null && request.ExpectedSessionIdentity is { ProjectPath: null } empty)
            return Execute(() =>
            {
                // Recovery must prove zero owners without EnsureConnected adopting a retained path.
                _sharedSession.ValidateEmptyPortal(empty);
                return Success(new ProjectStatusResultInfo
                {
                    Operation = "get_project_status", Project = new ProjectStatusInfo { IsOpen = false }
                });
            });
        return WithSession(request, session =>
        {
            var status = ProjectLifecycleService.GetStatusReadOnly(session, request.ProjectPath, _accessMode);
            return Success(new ProjectStatusResultInfo
            {
                Operation = "get_project_status",
                ProjectPath = status.Path,
                Project = status
            });
        });
    }

    /// <summary>
    /// Basic-status variant of <see cref="GetProjectStatus"/> for lifecycle post-write
    /// verification: returns the plain status (no extended metadata) with the same
    /// <c>get_project_status</c> result shape, so lifecycle write payloads stay unchanged while
    /// the metadata read (history, settings providers) is never performed after a write. Never
    /// registered as an MCP tool; callable only via the host's
    /// <c>OpennessWorkerClient.GetBasicProjectStatusAsync</c>.
    /// </summary>
    private static WorkerResponse GetBasicProjectStatus(WorkerRequest request)
    {
        return ProjectLifecycle(request, session =>
        {
            var status = ProjectLifecycleService.GetBasicStatusReadOnly(session, request.ProjectPath, _accessMode);
            return new ProjectLifecycleResultInfo
            {
                Operation = "get_project_status",
                ProjectPath = status.Path,
                Project = status
            };
        });
    }

    /// <summary>
    /// Internal state probe for save/save-as/archive/close preview and apply-time
    /// current-state checks. Never registered as an MCP tool; callable only via this worker
    /// operation name from the host's <c>OpennessWorkerClient.ProbeProjectStatusForLifecycleAsync</c>.
    /// </summary>
    private static WorkerResponse ProbeProjectStatusForLifecycle(WorkerRequest request)
    {
        return ProjectLifecycle(request, session =>
        {
            var status = ProjectLifecycleService.ProbeStatusForLifecycle(session, request.ProjectPath);
            return new ProjectLifecycleResultInfo
            {
                Operation = "probe_project_status_for_lifecycle",
                ProjectPath = status.Path,
                Project = status
            };
        });
    }

    /// <summary>Internal, non-mutating read of the live source before a proposed rebind.</summary>
    private static WorkerResponse ProbeOpenProjectRebind(WorkerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RebindDestinationProjectPath))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "RebindDestinationProjectPath is required for the rebind-state probe.");
        }

        return WithSession(request, session =>
            Success(session.ReadProjectRebindState(request.RebindDestinationProjectPath!)));
    }

    private static WorkerResponse OpenProject(WorkerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectPath))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "ProjectPath is required.");
        }

        return ProjectLifecycle(
            request,
            session => ProjectLifecycleService.OpenProject(session, request.ProjectPath!));
    }

    private static WorkerResponse CreateProject(WorkerRequest request)
    {
        return ProjectLifecycle(
            request,
            session => ProjectLifecycleService.CreateProject(
                session,
                request.ProjectDirectory!,
                request.ProjectName!,
                request.Author,
                request.Comment));
    }

    private static WorkerResponse SaveProject(WorkerRequest request)
    {
        return ProjectLifecycle(
            request,
            session => ProjectLifecycleService.SaveProject(
                session,
                request.ProjectPath,
                request.ExpectedSessionIdentity));
    }

    private static WorkerResponse SaveProjectAs(WorkerRequest request)
    {
        return ProjectLifecycle(
            request,
            session => ProjectLifecycleService.SaveProjectAs(
                session,
                request.ProjectPath,
                request.TargetDirectory!,
                request.TargetName!,
                request.Rebind,
                request.ExpectedSessionIdentity));
    }

    private static WorkerResponse ArchiveProject(WorkerRequest request)
    {
        return ProjectLifecycle(
            request,
            session => ProjectLifecycleService.ArchiveProject(
                session,
                request.ProjectPath,
                request.ArchiveDirectory!,
                request.ArchiveName!,
                request.ArchiveMode ?? ArchiveModeNames.Compressed,
                request.SaveBeforeArchive,
                request.ExpectedSessionIdentity));
    }

    private static WorkerResponse CloseProject(WorkerRequest request)
    {
        return ProjectLifecycle(
            request,
            session => ProjectLifecycleService.CloseProject(
                session,
                request.ProjectPath,
                request.SaveBeforeClose,
                request.ExpectedSessionIdentity));
    }

    private static WorkerResponse ProjectLifecycle(
        WorkerRequest request,
        Func<WorkerTiaPortalSession, ProjectLifecycleResultInfo> operation)
    {
        return WithSession(request, session => Success(operation(session)));
    }

    /// <summary>
    /// Runs a content read, write or compile against the common <see cref="ProjectBase"/> root of any
    /// open container. Which container kinds reach a handler is decided by the capability gate.
    /// </summary>
    private static WorkerResponse WithEngineeringRoot(WorkerRequest request, Func<ProjectBase, WorkerResponse> body)
        => WithActiveContext(request, session => body(session.EngineeringRoot!));

    /// <summary>Checks the active context and Portal after fresh identity validation, then runs <paramref name="body"/>.</summary>
    private static WorkerResponse WithActiveContext(WorkerRequest request, Func<WorkerTiaPortalSession, WorkerResponse> body)
    {
        return WithSession(request, session =>
        {
            ProjectOpenGate.Require(session.CurrentProjectPath, request.ProjectPath, _accessMode,
                allowRequestedNotOpen: false, allowNoProject: false);
            ValidateExpectedAfterProjectResolution(session, request);

            if (session.ActiveContext is null || session.TiaPortal is null)
            {
                return Failure(WorkerFailureCategories.WorkerOperationFailed,
                    ProjectOpenPolicy.NoProjectOpenMessage(_accessMode));
            }

            return body(session);
        });
    }

    /// <summary>
    /// Applies <see cref="ProjectOpenGate"/> before a non-lifecycle operation uses the
    /// already-open project. Returns null to continue, or a failure response to the host.
    /// </summary>
    private static WorkerResponse? EnsureRequestedProjectOpen(WorkerTiaPortalSession session, string? requestedProjectPath)
    {
        try
        {
            ProjectOpenGate.Require(session.CurrentProjectPath, requestedProjectPath, _accessMode,
                allowRequestedNotOpen: false, allowNoProject: false);
            return null;
        }
        catch (WorkerOperationException exception)
        {
            return Failure(exception.FailureCategory, exception.Message);
        }
    }

    /// <summary>
    /// Re-check the caller's identity after resolving the already-open project so no operation
    /// body can run on a generation that changed after the original precondition check.
    /// </summary>
    private static void ValidateExpectedAfterProjectResolution(
        WorkerTiaPortalSession session,
        WorkerRequest request)
        => ValidateExpectedSessionIdentityForRequest(
            session,
            request,
            AllowsMissingExpectedSessionIdentity(request.Method));

    private static void ValidateHardwarePageContinuationIdentity(WorkerRequest request)
    {
        if (request.HardwarePageContinuation is null)
        {
            return;
        }

        ValidateExpectedSessionIdentityForRequest(
            _sharedSession,
            request,
            allowMissingExpectedIdentity: false);
    }

    private static void ValidateExpectedSessionIdentityForRequest(
        WorkerTiaPortalSession session,
        WorkerRequest request,
        bool allowMissingExpectedIdentity,
        bool useCachedIdentity = false)
    {
        try
        {
            session.ValidateExpectedSessionIdentity(
                request.ExpectedSessionIdentity,
                allowMissingExpectedIdentity,
                useCachedIdentity);
        }
        catch (WorkerOperationException exception) when (
            request.HardwarePageContinuation is not null
            && string.Equals(request.Method, "read_hardware_page_candidates", StringComparison.Ordinal)
            && string.Equals(
                exception.FailureCategory,
                WorkerFailureCategories.BindingConflict,
                StringComparison.Ordinal))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.CursorBindingMismatch,
                "The hardware-page continuation no longer matches the live worker session.");
        }
    }

    private static WorkerResponse WithPortalRead(WorkerRequest request, Func<WorkerTiaPortalSession, WorkerResponse> body)
        => Execute(() => MultiuserPortalReadDispatch.Run(_sharedSession, request, body));

    /// <summary>Runs <paramref name="body"/> with the shared long-lived session.</summary>
    private static WorkerResponse WithSession(WorkerRequest request, Func<WorkerTiaPortalSession, WorkerResponse> body)
    {
        return Execute(() =>
        {
            var allowMissingExpectedIdentity = AllowsMissingExpectedSessionIdentity(request.Method);

            // First compare against the cached identity. A request carrying an identity from a
            // restarted worker is rejected before Attach() or any other Siemens call. Then refresh
            // the live Portal/project handles and compare again so a UI-side close, reopen, or
            // SaveAs cannot race into the requested engineering operation.
            ValidateExpectedSessionIdentityForRequest(
                _sharedSession,
                request,
                allowMissingExpectedIdentity);
            _sharedSession.EnsureConnected(request.ProjectPath);
            ValidateExpectedSessionIdentityForRequest(
                _sharedSession,
                request,
                allowMissingExpectedIdentity);

            var capabilityOperation = ProjectCapabilityCatalog.OperationFor(request.Method);
            if (capabilityOperation is not null && _sharedSession.ActiveContext is { } activeContext
                && !ProjectCapabilityCatalog.Supports(activeContext.ContainerKind, capabilityOperation))
                return Failure(WorkerFailureCategories.UnsupportedCapability,
                    $"{capabilityOperation} is unavailable for this project container.");

            return body(_sharedSession);
        }, MutatesProject(request.Method));
    }

    /// <summary>Only project-mutation methods may report a Portal rejection; the shared policy catalog is the single classification.</summary>
    private static bool MutatesProject(string method)
        => OperationPolicyCatalog.GetCapability(method) == OperationCapability.ProjectMutation;

    private static bool AllowsMissingExpectedSessionIdentity(string method)
        => !OperationPolicyCatalog.RequiresExpectedSessionIdentity(method);

    /// <summary>Single place that maps Openness exceptions to a <see cref="WorkerResponse"/> failure and stamps completed operations with their resolved project path.</summary>
    private static WorkerResponse Execute(Func<WorkerResponse> body, bool mutates = false)
        => WorkerExecution.Execute(() => Stamp(body()), () => _sharedSession.PortalDisposalRevision, mutates);

    /// <summary>
    /// Records which project the worker actually operated on. Stamped in one place so all
    /// operations report it without each remembering to.
    /// </summary>
    private static WorkerResponse Stamp(WorkerResponse response)
    {
        if (!response.Success)
        {
            return response;
        }

        try
        {
            response.PortalProcessId = _sharedSession.CurrentProcessId;
            response.ResolvedProjectPath = _sharedSession.CurrentProjectPath;
            response.SessionIdentity = _sharedSession.GetSessionIdentity();
        }
        catch (Exception ex)
        {
            // A diagnostic stamp must never demote a completed operation to a failure:
            // the operation already succeeded, so a failed path read leaves the path null
            // rather than propagating out of Execute and being reported as an error.
            // However, a systematically failing path read must not vanish: stderr is captured
            // into the response's Warnings, and stdout is the wire protocol so it cannot be used.
            Console.Error.WriteLine($"Could not resolve project path for response stamp: {ex.GetType().Name}: {ex.Message}");
        }

        return response;
    }

    private static WorkerResponse Success<T>(T payload)
    {
        return new WorkerResponse
        {
            Success = true,
            Payload = WorkerJson.SerializePayload(payload)
        };
    }

    private static WorkerResponse RawPayload(
        string payload,
        IReadOnlyList<string>? warnings = null,
        BlockImportOutcomeInfo? blockImportOutcome = null,
        HmiWriteOutcomeInfo? hmiWriteOutcome = null,
        TiaMcpServer.Contracts.Vci.VciWriteOutcomeInfo? vciWriteOutcome = null)
    {
        return new WorkerResponse
        {
            Success = true,
            Payload = payload,
            Warnings = warnings is { Count: > 0 } ? new List<string>(warnings) : null,
            HmiWriteOutcome = hmiWriteOutcome,
            VciWriteOutcome = vciWriteOutcome,
            BlockImportOutcome = blockImportOutcome
        };
    }

    private static WorkerResponse Failure(
        string failureCategory,
        string error,
        IReadOnlyList<string>? warnings = null,
        BlockImportOutcomeInfo? blockImportOutcome = null,
        HmiWriteOutcomeInfo? hmiWriteOutcome = null,
        TiaMcpServer.Contracts.Vci.VciWriteOutcomeInfo? vciWriteOutcome = null,
        EngineeringErrorInfo? engineeringError = null,
        ConfigureNetworkDeviceResultInfo? configureNetworkDeviceOutcome = null)
    {
        return new WorkerResponse
        {
            Success = false,
            Error = error,
            FailureCategory = failureCategory,
            Warnings = warnings is { Count: > 0 } ? new List<string>(warnings) : null,
            EngineeringError = engineeringError,
            ConfigureNetworkDeviceOutcome = configureNetworkDeviceOutcome,
            HmiWriteOutcome = hmiWriteOutcome,
            VciWriteOutcome = vciWriteOutcome,
            BlockImportOutcome = blockImportOutcome
        };
    }
}
