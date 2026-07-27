using System.Text.Json;
using System.Text.Json.Serialization;
using Siemens.Engineering;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.Openness;
using WorkerTiaPortalSession = TiaMcpServer.OpennessWorker.Openness.TiaPortalSession;

namespace TiaMcpServer.OpennessWorker;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // TIA Portal 권한 요청 다이얼로그는 Attach() 호출마다 뜬다.
    // 세션을 프로세스 수명 동안 재사용해 Attach()를 최초 1회만 호출한다.
    private static readonly WorkerTiaPortalSession _sharedSession = new(allowTiaConfirmations: true);

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

        Console.Error.WriteLine($"TIA Openness worker access mode: {_accessMode.ToString().ToUpperInvariant()}");

        string? line;
        while ((line = Console.In.ReadLine()) is not null)
        {
            var response = HandleLineWithCapturedStderr(line);
            Console.Out.WriteLine(JsonSerializer.Serialize(response, JsonOptions));
            Console.Out.Flush();
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
            var request = JsonSerializer.Deserialize<WorkerRequest>(line, JsonOptions);
            if (request is null)
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "Worker request was empty.");
            }

            // Defense in depth: authorize BEFORE the dispatch switch, before any handler
            // is called, and before any Siemens API call is made.
            var denial = WorkerOperationAuthorization.Authorize(_accessMode, request.Method);
            if (denial is not null)
            {
                return denial;
            }

            return request.Method switch
            {
                "browse_project_tree" => BrowseProjectTree(request),
                "read_hardware_config" => ReadHardwareConfig(request),
                "search_equipment_catalog" => SearchEquipmentCatalog(request),
                "add_network_device" => AddNetworkDevice(request),
                "configure_network_device" => ConfigureNetworkDevice(request),
                "read_cross_references" => ReadCrossReferences(request),
                "get_block_content"   => GetBlockContent(request),
                "update_block_logic"  => UpdateBlockLogic(request),
                "get_type_content"    => GetTypeContent(request),
                "update_type_content" => UpdateTypeContent(request),
                "list_tag_tables"     => ListTagTables(request),
                "compile_check"       => CompileCheck(request),
                "create_tag_table"    => CreateTagTable(request),
                "delete_tag_table"    => DeleteTagTable(request),
                "create_tag"          => CreateTag(request),
                "update_tag"          => UpdateTag(request),
                "delete_tag"          => DeleteTag(request),
                "create_user_constant" => CreateUserConstant(request),
                "update_user_constant" => UpdateUserConstant(request),
                "delete_user_constant" => DeleteUserConstant(request),
                "get_project_status"  => GetProjectStatus(request),
                "probe_project_status_for_lifecycle" => ProbeProjectStatusForLifecycle(request),
                "create_block"        => CreateBlock(request),
                "delete_block"        => DeleteBlock(request),
                "create_block_group"  => CreateBlockGroup(request),
                "delete_block_group"  => DeleteBlockGroup(request),
                "start_plc"           => StartPlc(request),
                "stop_plc"            => StopPlc(request),
                "open_project"        => OpenProject(request),
                "create_project"      => CreateProject(request),
                "save_project"        => SaveProject(request),
                "save_project_as"     => SaveProjectAs(request),
                "archive_project"     => ArchiveProject(request),
                "close_project"       => CloseProject(request),
                _ => throw new WorkerOperationException(
                    WorkerFailureCategories.ValidationError,
                    $"Unsupported worker method '{request.Method}'.")
            };
        }
        catch (JsonException ex)
        {
            return Failure(WorkerFailureCategories.ValidationError, $"Worker request was invalid JSON: {ex.Message}");
        }
        catch (WorkerOperationException ex)
        {
            return Failure(ex.FailureCategory, ex.Message, ex.Warnings);
        }
        catch (Exception ex)
        {
            // Type + message only — never the full exception (which includes a stack trace) —
            // since this is captured into the response's Warnings by HandleLineWithCapturedStderr.
            Console.Error.WriteLine($"Unhandled worker exception: {ex.GetType().Name}: {ex.Message}");
            return Failure(WorkerFailureCategories.WorkerOperationFailed, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static WorkerResponse BrowseProjectTree(WorkerRequest request)
    {
        return WithProject(request, project =>
        {
            var tree = new ProjectTreeWalker().Walk(project);
            return Success(ProjectTreeFilter.Apply(tree, request.StartPath, request.Depth));
        });
    }

    private static WorkerResponse ReadHardwareConfig(WorkerRequest request)
    {
        return WithProject(request, project => Success(HardwareConfigReader.Read(project)));
    }

    private static WorkerResponse SearchEquipmentCatalog(WorkerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "Query is required.");
        }

        return WithSession(request, session =>
        {
            session.EnsureConnected();

            var failure = EnsureRequestedProjectOpen(session, request.ProjectPath);
            if (failure is not null)
            {
                return failure;
            }

            if (session.TiaPortal is null)
            {
                return Failure(
                    WorkerFailureCategories.WorkerOperationFailed,
                    "No TIA Portal session is connected. Please start TIA Portal and try again.");
            }

            return Success(EquipmentCatalogSearcher.Search(session.TiaPortal, request.Query!, request.MaxResults));
        });
    }

    private static WorkerResponse AddNetworkDevice(WorkerRequest request)
    {
        if (!CatalogTypeIdentifier.IsCreatable(request.TypeIdentifier))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                CatalogTypeIdentifier.BuildValidationMessage(request.TypeIdentifier));
        }

        if (string.IsNullOrWhiteSpace(request.DeviceName))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "DeviceName is required.");
        }

        if (!request.Confirm)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "Operation not confirmed. Set confirm=true to proceed with adding a network device.");
        }

        return WithProject(request, project => Success(NetworkDeviceCreator.Create(
            project,
            request.TypeIdentifier!,
            request.DeviceName!,
            string.IsNullOrWhiteSpace(request.DeviceItemName) ? request.DeviceName! : request.DeviceItemName!)));
    }

    private static WorkerResponse ConfigureNetworkDevice(WorkerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceName))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "DeviceName is required.");
        }

        if (!request.Confirm)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "Operation not confirmed. Set confirm=true to proceed with configuring a network device.");
        }

        return WithProject(request, project => Success(NetworkDeviceConfigurator.Configure(
            project,
            request.DeviceName!,
            request.IpAddress,
            request.SubnetMask,
            request.PnDeviceName,
            request.SubnetName,
            request.IoSystemName)));
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

        return WithProject(request, project => Success(
            CrossReferenceReader.Read(project, request.PlcName, filter, request.MaxResults)));
    }

    private static WorkerResponse GetBlockContent(WorkerRequest request)
    {
        if (string.IsNullOrEmpty(request.BlockPath))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "BlockPath is required.");
        }

        var format = NormalizeBlockFormat(request.Format);

        return WithProject(request, project => RawPayload(
            BlockExporter.Export(project, request.BlockPath!, format)));
    }

    private static WorkerResponse UpdateBlockLogic(WorkerRequest request)
    {
        if (string.IsNullOrEmpty(request.BlockPath))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "BlockPath is required.");
        }

        if (string.IsNullOrEmpty(request.YamlContent))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "YamlContent is required.");
        }

        var format = NormalizeBlockFormat(request.Format);

        return WithProject(request, project =>
        {
            var result = BlockImporter.Import(project, request.BlockPath!, request.YamlContent!, format);
            return RawPayload(result.Payload, result.Warnings);
        });
    }

    private static WorkerResponse GetTypeContent(WorkerRequest request)
    {
        if (string.IsNullOrEmpty(request.TypePath))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "TypePath is required.");
        }

        var format = NormalizeTypeFormat(request.Format);

        return WithProject(request, project => RawPayload(
            PlcTypeExporter.Export(project, request.TypePath!, format)));
    }

    private static WorkerResponse UpdateTypeContent(WorkerRequest request)
    {
        if (string.IsNullOrEmpty(request.TypePath))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "TypePath is required.");
        }

        if (string.IsNullOrEmpty(request.SourceContent))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "SourceContent is required.");
        }

        var format = NormalizeTypeFormat(request.Format);

        return WithProject(request, project => Success(
            PlcTypeImporter.Import(project, request.TypePath!, request.SourceContent!, format)));
    }

    /// <summary>
    /// The host normalizes <see cref="WorkerRequest.Format"/> before sending, but the worker is
    /// also driven directly by the live harness — so the type operations re-apply their own
    /// default (source, i.e. .udt) rather than trusting the field to be set.
    /// </summary>
    private static string NormalizeTypeFormat(string? format)
    {
        if (!SourceFormatNames.TryNormalize(format, SourceFormatNames.Source, out var normalized, out var error))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                error ?? "Invalid format.");
        }

        return normalized;
    }

    /// <summary>
    /// Same contract as <see cref="NormalizeTypeFormat"/> with the block default: xml, because
    /// get_block_content and update_block_logic have callers whose payloads must not change when
    /// format is omitted.
    /// </summary>
    private static string NormalizeBlockFormat(string? format)
    {
        if (!SourceFormatNames.TryNormalize(format, SourceFormatNames.Xml, out var normalized, out var error))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                error ?? "Invalid format.");
        }

        return normalized;
    }

    private static WorkerResponse ListTagTables(WorkerRequest request)
    {
        return WithProject(request, project => Success(TagTableReader.ReadAll(project, request.PlcName)));
    }

    private static WorkerResponse CompileCheck(WorkerRequest request)
    {
        return WithProject(request, project => Success(CompileChecker.Compile(project, request.PlcName, request.BlockPath)));
    }

    private static WorkerResponse CreateTagTable(WorkerRequest request)
    {
        return TagMutation(request, project =>
            TagMutationService.CreateTagTable(project, request.PlcName, request.TableName!, request.FolderPath));
    }

    private static WorkerResponse DeleteTagTable(WorkerRequest request)
    {
        return TagMutation(request, project =>
            TagMutationService.DeleteTagTable(project, request.PlcName, request.TableName!, request.FolderPath));
    }

    private static WorkerResponse CreateTag(WorkerRequest request)
    {
        return TagMutation(request, project =>
            TagMutationService.CreateTag(
                project,
                request.PlcName,
                request.TableName!,
                request.FolderPath,
                request.Name!,
                request.DataType!,
                request.LogicalAddress));
    }

    private static WorkerResponse UpdateTag(WorkerRequest request)
    {
        return TagMutation(request, project =>
            TagMutationService.UpdateTag(
                project,
                request.PlcName,
                request.TableName!,
                request.FolderPath,
                request.Name!,
                request.NewName,
                request.DataType,
                request.LogicalAddress,
                request.ExternalAccessible,
                request.ExternalVisible,
                request.ExternalWritable,
                request.IsSafety));
    }

    private static WorkerResponse DeleteTag(WorkerRequest request)
    {
        return TagMutation(request, project =>
            TagMutationService.DeleteTag(
                project,
                request.PlcName,
                request.TableName!,
                request.FolderPath,
                request.Name!));
    }

    private static WorkerResponse CreateUserConstant(WorkerRequest request)
    {
        return TagMutation(request, project =>
            TagMutationService.CreateUserConstant(
                project,
                request.PlcName,
                request.TableName!,
                request.FolderPath,
                request.Name!,
                request.DataType!,
                request.Value!));
    }

    private static WorkerResponse UpdateUserConstant(WorkerRequest request)
    {
        return TagMutation(request, project =>
            TagMutationService.UpdateUserConstant(
                project,
                request.PlcName,
                request.TableName!,
                request.FolderPath,
                request.Name!,
                request.DataType,
                request.Value));
    }

    private static WorkerResponse DeleteUserConstant(WorkerRequest request)
    {
        return TagMutation(request, project =>
            TagMutationService.DeleteUserConstant(
                project,
                request.PlcName,
                request.TableName!,
                request.FolderPath,
                request.Name!));
    }

    private static WorkerResponse CreateBlock(WorkerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BlockPath))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "BlockPath is required.");
        }

        if (string.IsNullOrWhiteSpace(request.BlockType))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "BlockType is required. Valid values: FB, FC, OB, GlobalDB.");
        }

        if (!request.Confirm)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "Operation not confirmed. Set confirm=true to proceed with creating a block.");
        }

        return WithProject(request, project => Success(
            BlockMutationService.CreateBlock(
                project,
                request.BlockPath!,
                request.BlockType!,
                request.Language,
                request.OBEventClass)));
    }

    private static WorkerResponse DeleteBlock(WorkerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BlockPath))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "BlockPath is required.");
        }

        if (!request.Confirm)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "Operation not confirmed. Set confirm=true to proceed with deleting a block.");
        }

        return WithProject(request, project => Success(
            BlockMutationService.DeleteBlock(project, request.BlockPath!)));
    }

    private static WorkerResponse CreateBlockGroup(WorkerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BlockPath))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "BlockPath is required.");
        }

        if (!request.Confirm)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "Operation not confirmed. Set confirm=true to proceed with creating a block group.");
        }

        return WithProject(request, project => Success(
            BlockMutationService.CreateBlockGroup(project, request.BlockPath!)));
    }

    private static WorkerResponse DeleteBlockGroup(WorkerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BlockPath))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "BlockPath is required.");
        }

        if (!request.Confirm)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "Operation not confirmed. Set confirm=true to proceed with deleting a block group.");
        }

        return WithProject(request, project => Success(
            BlockMutationService.DeleteBlockGroup(project, request.BlockPath!)));
    }

    private static WorkerResponse StartPlc(WorkerRequest request)
    {
        if (!request.Confirm)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "Operation not confirmed. Set confirm=true to proceed with starting the PLC.");
        }

        return WithProject(request, project => Success(
            PlcOnlineService.Start(project, request.PlcName)));
    }

    private static WorkerResponse StopPlc(WorkerRequest request)
    {
        if (!request.Confirm)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "Operation not confirmed. Set confirm=true to proceed with stopping the PLC.");
        }

        return WithProject(request, project => Success(
            PlcOnlineService.Stop(project, request.PlcName)));
    }

    private static WorkerResponse GetProjectStatus(WorkerRequest request)
    {
        return ProjectLifecycle(request, session =>
        {
            var status = ProjectLifecycleService.GetStatusReadOnly(session, request.ProjectPath);
            return new ProjectLifecycleResultInfo
            {
                Operation = "get_project_status",
                ProjectPath = status.Path,
                Project = status
            };
        }, requiresConfirm: false);
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
        }, requiresConfirm: false);
    }

    private static WorkerResponse OpenProject(WorkerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectPath))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "ProjectPath is required.");
        }

        return ProjectLifecycle(
            request,
            session => ProjectLifecycleService.OpenProject(session, request.ProjectPath!),
            requiresConfirm: true);
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
                request.Comment),
            requiresConfirm: true);
    }

    private static WorkerResponse SaveProject(WorkerRequest request)
    {
        return ProjectLifecycle(
            request,
            session => ProjectLifecycleService.SaveProject(session, request.ProjectPath),
            requiresConfirm: true);
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
                request.Rebind),
            requiresConfirm: true);
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
                request.SaveBeforeArchive),
            requiresConfirm: true);
    }

    private static WorkerResponse CloseProject(WorkerRequest request)
    {
        return ProjectLifecycle(
            request,
            session => ProjectLifecycleService.CloseProject(session, request.ProjectPath, request.SaveBeforeClose),
            requiresConfirm: true);
    }

    private static WorkerResponse TagMutation(WorkerRequest request, Func<Project, TagMutationResultInfo> mutate)
    {
        if (!request.Confirm)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "Operation not confirmed. Set confirm=true to proceed with the tag operation.");
        }

        return WithProject(request, project => Success(mutate(project)));
    }

    private static WorkerResponse ProjectLifecycle(
        WorkerRequest request,
        Func<WorkerTiaPortalSession, ProjectLifecycleResultInfo> operation,
        bool requiresConfirm)
    {
        if (requiresConfirm && !request.Confirm)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "Operation not confirmed. Set confirm=true to proceed with the project operation.");
        }

        return WithSession(request, session => Success(operation(session)));
    }

    /// <summary>Opens an Openness session, ensures a project is available, then runs <paramref name="body"/>.</summary>
    private static WorkerResponse WithProject(WorkerRequest request, Func<Project, WorkerResponse> body)
    {
        return WithSession(request, session =>
        {
            session.EnsureConnected();

            var failure = EnsureRequestedProjectOpen(session, request.ProjectPath);
            if (failure is not null)
            {
                return failure;
            }

            if (session.Project is null)
            {
                return Failure(
                    WorkerFailureCategories.WorkerOperationFailed,
                    "No project is open. Provide a projectPath argument or open a project in TIA Portal.");
            }

            return body(session.Project);
        });
    }

    /// <summary>
    /// Applies <see cref="ProjectOpenPolicy"/> before any non-lifecycle operation may open a
    /// project. Returns null to continue, or the failure response to return to the host.
    /// In read-only mode, opening a project is never permitted — only the currently open
    /// project may be used.
    /// </summary>
    private static WorkerResponse? EnsureRequestedProjectOpen(WorkerTiaPortalSession session, string? requestedProjectPath)
    {
        var currentPath = session.CurrentProjectPath;

        if (_accessMode == McpAccessMode.ReadOnly)
        {
            // Read-only mode: never open a project. Only use what is already open.
            if (currentPath is null)
            {
                var requested = ProjectPathNormalization.Canonicalize(requestedProjectPath);
                if (requested is not null)
                {
                    // A path was supplied but nothing is open — do NOT open it; treat it as an assertion failure.
                    return Failure(
                        WorkerFailureCategories.AccessDenied,
                        "Read-only mode operates only on the project already open in TIA Portal. "
                        + "Open the intended project manually and retry.");
                }

                return Failure(
                    WorkerFailureCategories.WorkerOperationFailed,
                    "No project is open in TIA Portal. Open the intended project manually and retry.");
            }

            if (requestedProjectPath is not null)
            {
                var requested = ProjectPathNormalization.Canonicalize(requestedProjectPath);
                if (requested is not null && !string.Equals(currentPath, requested, StringComparison.OrdinalIgnoreCase))
                {
                    return Failure(
                        WorkerFailureCategories.BindingConflict,
                        $"Read-only mode is bound to '{currentPath}' but the request targets '{requested}'. "
                        + "Read-only mode does not switch projects. Omit projectPath to use the open project.");
                }
            }

            return null;
        }

        // Read-write mode: existing behavior.
        switch (ProjectOpenPolicy.Decide(currentPath, requestedProjectPath))
        {
            case ProjectOpenDecision.OpenRequested:
                session.OpenProject(requestedProjectPath!);
                return null;
            case ProjectOpenDecision.Refuse:
                return Failure(
                    WorkerFailureCategories.BindingConflict,
                    ProjectOpenPolicy.RefusalMessage(currentPath!, requestedProjectPath!));
            default:
                return null;
        }
    }

    /// <summary>Runs <paramref name="body"/> with the shared long-lived session.</summary>
    private static WorkerResponse WithSession(WorkerRequest request, Func<WorkerTiaPortalSession, WorkerResponse> body)
    {
        return Execute(() => body(_sharedSession));
    }

    /// <summary>Single place that maps Openness exceptions to a <see cref="WorkerResponse"/> failure and stamps completed operations with their resolved project path.</summary>
    private static WorkerResponse Execute(Func<WorkerResponse> body)
    {
        try
        {
            return Stamp(body());
        }
        catch (EngineeringException ex)
        {
            return Failure(WorkerFailureCategories.WorkerOperationFailed, $"TIA Portal operation failed: {ex.Message}");
        }
        catch (NonRecoverableException ex)
        {
            return Failure(
                WorkerFailureCategories.WorkerOperationFailed,
                $"TIA Portal was closed unexpectedly: {ex.Message}. Please restart TIA Portal and try again.");
        }
        catch (InvalidOperationException ex)
        {
            return Failure(WorkerFailureCategories.WorkerOperationFailed, ex.Message);
        }
        catch (System.IO.IOException ex)
        {
            return Failure(WorkerFailureCategories.WorkerOperationFailed, ex.Message);
        }
    }

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
            response.ResolvedProjectPath = _sharedSession.CurrentProjectPath;
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
            Payload = JsonSerializer.Serialize(payload, JsonOptions)
        };
    }

    private static WorkerResponse RawPayload(string payload, IReadOnlyList<string>? warnings = null)
    {
        return new WorkerResponse
        {
            Success = true,
            Payload = payload,
            Warnings = warnings is { Count: > 0 } ? new List<string>(warnings) : null
        };
    }

    private static WorkerResponse Failure(string failureCategory, string error, IReadOnlyList<string>? warnings = null)
    {
        return new WorkerResponse
        {
            Success = false,
            Error = error,
            FailureCategory = failureCategory,
            Warnings = warnings is { Count: > 0 } ? new List<string>(warnings) : null
        };
    }
}
