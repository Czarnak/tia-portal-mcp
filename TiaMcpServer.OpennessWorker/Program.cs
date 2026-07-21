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

    static Program()
    {
        AssemblyResolver.Register();
    }

    private static void Main()
    {
        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.OutputEncoding = System.Text.Encoding.UTF8;

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
        if (captured.Count > 0)
        {
            response.Warnings = captured;
        }

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
                return Failure("Worker request was empty.");
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
                _                     => Failure($"Unsupported worker method '{request.Method}'.")
            };
        }
        catch (JsonException ex)
        {
            return Failure($"Worker request was invalid JSON: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return Failure($"{ex.GetType().Name}: {ex.Message}");
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
            return Failure("Query is required.");
        }

        return WithSession(request, session =>
        {
            session.EnsureConnected();

            if (!string.IsNullOrEmpty(request.ProjectPath))
            {
                session.OpenProject(request.ProjectPath!);
            }

            if (session.TiaPortal is null)
            {
                return Failure("No TIA Portal session is connected. Please start TIA Portal and try again.");
            }

            return Success(EquipmentCatalogSearcher.Search(session.TiaPortal, request.Query!, request.MaxResults));
        });
    }

    private static WorkerResponse AddNetworkDevice(WorkerRequest request)
    {
        if (!CatalogTypeIdentifier.IsCreatable(request.TypeIdentifier))
        {
            return Failure(CatalogTypeIdentifier.BuildValidationMessage(request.TypeIdentifier));
        }

        if (string.IsNullOrWhiteSpace(request.DeviceName))
        {
            return Failure("DeviceName is required.");
        }

        if (!request.Confirm)
        {
            return Failure("Operation not confirmed. Set confirm=true to proceed with adding a network device.");
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
            return Failure("DeviceName is required.");
        }

        if (!request.Confirm)
        {
            return Failure("Operation not confirmed. Set confirm=true to proceed with configuring a network device.");
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
            return Failure(filterError ?? "Invalid cross-reference filter.");
        }

        return WithProject(request, project => Success(
            CrossReferenceReader.Read(project, request.PlcName, filter, request.MaxResults)));
    }

    private static WorkerResponse GetBlockContent(WorkerRequest request)
    {
        if (string.IsNullOrEmpty(request.BlockPath))
        {
            return Failure("BlockPath is required.");
        }

        return WithProject(request, project => RawPayload(BlockExporter.Export(project, request.BlockPath!)));
    }

    private static WorkerResponse UpdateBlockLogic(WorkerRequest request)
    {
        if (string.IsNullOrEmpty(request.BlockPath))
        {
            return Failure("BlockPath is required.");
        }

        if (string.IsNullOrEmpty(request.YamlContent))
        {
            return Failure("YamlContent is required.");
        }

        return WithProject(request, project => RawPayload(BlockImporter.Import(project, request.BlockPath!, request.YamlContent!)));
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
            return Failure("BlockPath is required.");
        }

        if (string.IsNullOrWhiteSpace(request.BlockType))
        {
            return Failure("BlockType is required. Valid values: FB, FC, OB, GlobalDB.");
        }

        if (!request.Confirm)
        {
            return Failure("Operation not confirmed. Set confirm=true to proceed with creating a block.");
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
            return Failure("BlockPath is required.");
        }

        if (!request.Confirm)
        {
            return Failure("Operation not confirmed. Set confirm=true to proceed with deleting a block.");
        }

        return WithProject(request, project => Success(
            BlockMutationService.DeleteBlock(project, request.BlockPath!)));
    }

    private static WorkerResponse CreateBlockGroup(WorkerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BlockPath))
        {
            return Failure("BlockPath is required.");
        }

        if (!request.Confirm)
        {
            return Failure("Operation not confirmed. Set confirm=true to proceed with creating a block group.");
        }

        return WithProject(request, project => Success(
            BlockMutationService.CreateBlockGroup(project, request.BlockPath!)));
    }

    private static WorkerResponse DeleteBlockGroup(WorkerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BlockPath))
        {
            return Failure("BlockPath is required.");
        }

        if (!request.Confirm)
        {
            return Failure("Operation not confirmed. Set confirm=true to proceed with deleting a block group.");
        }

        return WithProject(request, project => Success(
            BlockMutationService.DeleteBlockGroup(project, request.BlockPath!)));
    }

    private static WorkerResponse StartPlc(WorkerRequest request)
    {
        if (!request.Confirm)
        {
            return Failure("Operation not confirmed. Set confirm=true to proceed with starting the PLC.");
        }

        return WithProject(request, project => Success(
            PlcOnlineService.Start(project, request.PlcName)));
    }

    private static WorkerResponse StopPlc(WorkerRequest request)
    {
        if (!request.Confirm)
        {
            return Failure("Operation not confirmed. Set confirm=true to proceed with stopping the PLC.");
        }

        return WithProject(request, project => Success(
            PlcOnlineService.Stop(project, request.PlcName)));
    }

    private static WorkerResponse GetProjectStatus(WorkerRequest request)
    {
        return ProjectLifecycle(request, session =>
        {
            var status = ProjectLifecycleService.GetStatus(session, request.ProjectPath);
            return new ProjectLifecycleResultInfo
            {
                Operation = "get_project_status",
                ProjectPath = status.Path,
                Project = status
            };
        }, requiresConfirm: false);
    }

    private static WorkerResponse OpenProject(WorkerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectPath))
        {
            return Failure("ProjectPath is required.");
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
            return Failure("Operation not confirmed. Set confirm=true to proceed with the tag operation.");
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
            return Failure("Operation not confirmed. Set confirm=true to proceed with the project operation.");
        }

        return WithSession(request, session => Success(operation(session)));
    }

    /// <summary>Opens an Openness session, ensures a project is available, then runs <paramref name="body"/>.</summary>
    private static WorkerResponse WithProject(WorkerRequest request, Func<Project, WorkerResponse> body)
    {
        return WithSession(request, session =>
        {
            session.EnsureConnected();

            if (!string.IsNullOrEmpty(request.ProjectPath))
            {
                session.OpenProject(request.ProjectPath!);
            }

            if (session.Project is null)
            {
                return Failure("No project is open. Provide a projectPath argument or open a project in TIA Portal.");
            }

            return body(session.Project);
        });
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
            return Failure($"TIA Portal operation failed: {ex.Message}");
        }
        catch (NonRecoverableException ex)
        {
            return Failure($"TIA Portal was closed unexpectedly: {ex.Message}. Please restart TIA Portal and try again.");
        }
        catch (InvalidOperationException ex)
        {
            return Failure(ex.Message);
        }
        catch (System.IO.IOException ex)
        {
            return Failure(ex.Message);
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

    private static WorkerResponse RawPayload(string payload)
    {
        return new WorkerResponse
        {
            Success = true,
            Payload = payload
        };
    }

    private static WorkerResponse Failure(string error)
    {
        return new WorkerResponse
        {
            Success = false,
            Error = error
        };
    }
}
