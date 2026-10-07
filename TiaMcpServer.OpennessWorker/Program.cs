using System.Text.Json;
using Siemens.Engineering;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.Openness;
using WorkerTiaPortalSession = TiaMcpServer.OpennessWorker.Openness.TiaPortalSession;

namespace TiaMcpServer.OpennessWorker;

internal static class Program
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
                "read_hardware_config" => ReadHardwareConfig(request),
                "read_hardware_page_candidates" => ReadHardwarePageCandidates(request),
                "list_network_objects" => ListNetworkObjects(request),
                "inspect_network_object" => InspectNetworkObject(request),
                "probe_network_object_attributes" => ProbeNetworkObjectAttributes(request),
                "probe_io_system_qualification" => ProbeIoSystemQualification(request),
                "probe_subnet_lifecycle_mutations" => ProbeSubnetLifecycleMutations(request),
                "search_equipment_catalog" => SearchEquipmentCatalog(request),
                "add_network_device" => AddNetworkDevice(request),
                "configure_network_device" => ConfigureNetworkDevice(request),
                "create_subnet" => CreateSubnet(request),
                "update_subnet" => UpdateSubnet(request),
                "delete_subnet" => DeleteSubnet(request),
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
                "probe_open_project_rebind" => ProbeOpenProjectRebind(request),
                "get_basic_project_status" => GetBasicProjectStatus(request),
                "create_block"        => CreateBlock(request),
                "delete_block"        => DeleteBlock(request),
                "create_block_group"  => CreateBlockGroup(request),
                "delete_block_group"  => DeleteBlockGroup(request),
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
        catch (NetworkCursorException ex)
        {
            return Failure(ex.Category, ex.Message);
        }
        catch (WorkerOperationException ex)
        {
            return Failure(ex.FailureCategory, ex.Message, ex.Warnings, ex.BlockImportOutcome);
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
            if (string.IsNullOrWhiteSpace(request.ProjectPath))
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                    "select_portal_project requires projectPath.");
            }

            ValidateExpectedSessionIdentityForRequest(_sharedSession, request,
                allowMissingExpectedIdentity: true, useCachedIdentity: true);
            return Success(_sharedSession.SelectPortalProject(request.ProjectPath!));
        });
    }

    private static WorkerResponse BrowseProjectTreeV3Snapshot(WorkerRequest request)
    {
        try
        {
            ProjectTreeNodeTypes.Validate(request.StartSelector);
            return WithProject(request, project =>
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

    private static WorkerResponse ReadHardwareConfig(WorkerRequest request)
    {
        return WithProject(request, project => Success(HardwareConfigReader.Read(
            project,
            request.DeviceName,
            request.PlcName,
            request.IncludeIoDetails,
            request.IncludeTagMatches)));
    }

    private static WorkerResponse ReadHardwarePageCandidates(WorkerRequest request)
    {
        return WithProject(request, project =>
        {
            ValidateHardwarePageContinuationIdentity(request);
            var source = HardwarePageCandidateSourceFactory.Create(
                project,
                request.DeviceName,
                request.PlcName,
                request.IncludeIoDetails,
                request.IncludeTagMatches);
            return Success(HardwarePageCandidateReader.Read(
                source,
                request.DeviceName,
                request.PlcName,
                request.IncludeIoDetails,
                request.IncludeTagMatches,
                request.HardwarePageSize,
                request.HardwarePageContinuation));
        });
    }

    private static WorkerResponse ListNetworkObjects(WorkerRequest request)
    {
        ValidateListNetworkObjectsRequest(request);

        int pageSize;
        try
        {
            pageSize = NetworkObjectPageBuilder.ResolvePageSize(request.NetworkObjectPageSize);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "NetworkObjectPageSize must be between 1 and 200.");
        }

        return WithProject(request, project =>
        {
            var orderedItems = NetworkObjectIndexReader.Read(project, request.NetworkObjectKinds!, request.NetworkObjectDeviceName);
            var queryHash = NetworkObjectCursorCodec.CreateQueryHash(request.NetworkObjectKinds!, request.NetworkObjectDeviceName);
            var snapshotHash = NetworkObjectCursorCodec.CreateSnapshotHash(orderedItems);
            var offset = request.NetworkObjectCursor is null
                ? 0
                : NetworkObjectCursorCodec.Decode(
                    request.NetworkObjectCursor,
                    queryHash,
                    snapshotHash,
                    orderedItems.Count).Offset;
            return Success(NetworkObjectPageBuilder.Build(orderedItems, pageSize, offset, queryHash, snapshotHash));
        });
    }

    private static void ValidateListNetworkObjectsRequest(WorkerRequest request)
    {
        if (request.NetworkObjectKinds is null || request.NetworkObjectKinds.Count == 0)
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "NetworkObjectKinds is required.");
        }

        if (request.NetworkObjectKinds.Any(kind => !NetworkObjectKinds.All.Contains(kind))
            || request.NetworkObjectKinds.Distinct(StringComparer.Ordinal).Count() != request.NetworkObjectKinds.Count)
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "NetworkObjectKinds is invalid.");
        }

        if (request.NetworkObjectDeviceName is not null && string.IsNullOrWhiteSpace(request.NetworkObjectDeviceName))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "NetworkObjectDeviceName must not be blank.");
        }

        if (request.NetworkObjectDeviceName is not null
            && (request.NetworkObjectKinds.Contains(NetworkObjectKinds.Subnet)
                || request.NetworkObjectKinds.Contains(NetworkObjectKinds.IoSystem)))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "NetworkObjectDeviceName is not applicable to subnet or ioSystem discovery.");
        }
    }

    private static WorkerResponse InspectNetworkObject(WorkerRequest request)
    {
        if (request.NetworkObjectTarget is null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "NetworkObjectTarget is required.");
        }

        return WithProject(request, project =>
        {
            var resolution = NetworkObjectSelectorResolver.Resolve(project, request.NetworkObjectTarget);
            if (!resolution.Success)
            {
                return Failure(
                    resolution.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                    resolution.Error ?? "The network object target could not be resolved.");
            }

            return Success(NetworkObjectInspector.Inspect(
                resolution.Resolved!,
                request.NetworkAttributeNames));
        });
    }

    private static WorkerResponse ProbeIoSystemQualification(WorkerRequest request)
    {
        var validation = IoSystemQualificationProbeValidator.Validate(request);
        if (validation is not null)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, validation);
        try
        {
            var response = WithSession(request, session =>
            {
                var failure = EnsureRequestedProjectOpen(session, request.ProjectPath);
                if (failure is not null)
                    return Failure(failure.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                        "The requested qualification project is unavailable.");
                ValidateExpectedAfterProjectResolution(session, request);
                if (session.ActiveContext is null || session.TiaPortal is null)
                    return Failure(WorkerFailureCategories.WorkerOperationFailed, "No project or Portal session is available.");
                var probe = request.IoSystemQualification!;
                var result = probe.Mode switch
                {
                    "inspectOwner" => IoSystemQualificationProbeService.InspectOwner(session.TiaPortal, session.RequireStandaloneOwner().Project, probe),
                    "compileBaseline" => IoSystemQualificationProbeService.CompileBaseline(session.TiaPortal, session.RequireStandaloneOwner().Project, probe),
                    "setAndCompile" => IoSystemQualificationProbeService.SetAndCompile(session.TiaPortal, session.RequireStandaloneOwner().Project, probe),
                    _ => throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "Unsupported qualification mode.")
                };
                return new WorkerResponse { Success = true, Payload = IoSystemQualificationEvidence.SerializeBounded(result) };
            });
            return IoSystemQualificationEvidence.NormalizeSessionResponse(response, request.IoSystemQualification!.Mode);
        }
        catch (WorkerOperationException exception)
        {
            return Failure(exception.FailureCategory, "IO-system qualification was rejected. Refresh session, target, owner, and metadata evidence before proceeding.");
        }
        catch (Exception)
        {
            return Failure(WorkerFailureCategories.WorkerOperationFailed,
                "IO-system qualification could not complete. A requested mutation may have committed; inspect current state before restoration or retry.");
        }
    }
    private static WorkerResponse ProbeNetworkObjectAttributes(WorkerRequest request)
    {
        if (request.NetworkObjectTarget is null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "NetworkObjectTarget is required.");
        }

        if (request.NetworkAttributeNames is { Count: 0 }
            || request.NetworkAttributeNames is { Count: > 200 }
            || (request.NetworkAttributeNames is not null
                && (request.NetworkAttributeNames.Any(string.IsNullOrWhiteSpace)
                    || request.NetworkAttributeNames.Distinct(StringComparer.Ordinal).Count()
                        != request.NetworkAttributeNames.Count)))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "NetworkAttributeNames must contain between 1 and 200 unique, nonblank names when supplied.");
        }

        return WithProject(request, project =>
        {
            var resolution = NetworkObjectSelectorResolver.Resolve(project, request.NetworkObjectTarget);
            if (!resolution.Success)
            {
                return Failure(
                    resolution.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                    resolution.Error ?? "The network object target could not be resolved.");
            }

            var selectedNames = request.NetworkAttributeNames is null
                ? null
                : new HashSet<string>(request.NetworkAttributeNames, StringComparer.Ordinal);
            var attributes = resolution.Resolved!.EngineeringObject.GetAttributeInfos()
                .Where(info => selectedNames is null || selectedNames.Contains(info.Name))
                .OrderBy(info => info.Name, StringComparer.Ordinal)
                .Select(info => ProbeNetworkAttribute(resolution.Resolved.EngineeringObject, info))
                .ToList();

            return Success(new NetworkAttributeProbeInfo
            {
                Target = resolution.Resolved.Target,
                Attributes = attributes,
                Messages = resolution.Resolved.Messages.ToList(),
            });
        });
    }

    private static NetworkAttributeProbeEntryInfo ProbeNetworkAttribute(
        IEngineeringObject engineeringObject,
        EngineeringAttributeInfo info)
    {
        string? observedClrValueType = null;
        string? exceptionCategory = null;
        if (info.AccessMode is EngineeringAttributeAccessMode.Read or EngineeringAttributeAccessMode.ReadWrite)
        {
            try
            {
                var value = engineeringObject.GetAttribute(info.Name);
                observedClrValueType = value?.GetType().FullName;
            }
            catch (Exception exception)
            {
                exceptionCategory = ProbeExceptionCategory(exception);
            }
        }

        return new NetworkAttributeProbeEntryInfo
        {
            Name = info.Name,
            AccessMode = Enum.GetName(typeof(EngineeringAttributeAccessMode), info.AccessMode) ?? "Unknown",
            SupportedClrTypeNames = info.SupportedTypes
                .Select(type => type.FullName ?? type.Name)
                .OrderBy(typeName => typeName, StringComparer.Ordinal)
                .ToList(),
            ObservedClrValueType = observedClrValueType,
            ExceptionCategory = exceptionCategory,
        };
    }

    private static string ProbeExceptionCategory(Exception exception)
        => exception switch
        {
            EngineeringSecurityException => "accessDenied",
            EngineeringNotSupportedException => "notSupported",
            EngineeringObjectDisposedException => "objectDisposed",
            EngineeringException => "engineeringError",
            NonRecoverableException => "nonRecoverable",
            _ => "unexpectedError",
        };

    private static WorkerResponse ProbeSubnetLifecycleMutations(WorkerRequest request)
    {
        if (!request.Confirm)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "Operation not confirmed. Set confirm=true to run the subnet lifecycle mutation probe.");
        }
        if (string.IsNullOrWhiteSpace(request.ProbeRunId))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "ProbeRunId must contain 6 to 12 ASCII letters or digits.");
        }
        var probeRunId = request.ProbeRunId!;
        if (probeRunId.Length is < 6 or > 12
            || probeRunId.Any(character => !char.IsLetterOrDigit(character)))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "ProbeRunId must contain 6 to 12 ASCII letters or digits.");
        }
        if (string.IsNullOrWhiteSpace(request.ProbeConnectedEthernetSubnetId)
            || string.IsNullOrWhiteSpace(request.ProbeConnectedProfibusSubnetId))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "Both connected Ethernet and PROFIBUS subnet IDs are required.");
        }
        if (request.ProbeProfibusHighestAddress is null
            || string.IsNullOrWhiteSpace(request.ProbeProfibusTransmissionSpeed))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "The PROFIBUS edit probe requires HighestAddress and TransmissionSpeed values.");
        }

        return WithSession(request, session =>
        {
            var failure = EnsureRequestedProjectOpen(session, request.ProjectPath);
            if (failure is not null)
            {
                return failure;
            }
            ValidateExpectedAfterProjectResolution(session, request);
            if (session.ActiveContext is null || session.TiaPortal is null)
            {
                return Failure(
                    WorkerFailureCategories.WorkerOperationFailed,
                    "No project or TIA Portal session is available for the subnet mutation probe.");
            }

            return Success(SubnetLifecycleMutationProbeService.Run(
                session.TiaPortal,
                session.RequireStandaloneOwner().Project,
                probeRunId,
                request.ProbeConnectedEthernetSubnetId!,
                request.ProbeConnectedProfibusSubnetId!,
                request.ProbeProfibusHighestAddress.Value,
                request.ProbeProfibusTransmissionSpeed!));
        });
    }

    private static WorkerResponse SearchEquipmentCatalog(WorkerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "Query is required.");
        }

        return WithSession(request, session =>
        {
            var failure = EnsureRequestedProjectOpen(session, request.ProjectPath);
            if (failure is not null)
            {
                return failure;
            }
            ValidateExpectedAfterProjectResolution(session, request);

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

        return WithProject(request, project =>
        {
            var result = NetworkDeviceCreator.Create(project, request.TypeIdentifier!, request.DeviceName!,
                string.IsNullOrWhiteSpace(request.DeviceItemName) ? request.DeviceName! : request.DeviceItemName!);
            result.Verification = NetworkMutationVerifier.VerifyAddedDevice(project, request, result);
            return Success(result);
        });
    }

    private static WorkerResponse ConfigureNetworkDevice(WorkerRequest request)
    {
        var target = NetworkConfigurationTargetBinding.Resolve(request);

        if (!request.Confirm)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "Operation not confirmed. Set confirm=true to proceed with configuring a network device.");
        }

        return WithProject(request, project =>
        {
            // Capture the normalized owner while retaining supplied constraints for the final
            // pre-setter resolver. Immediate checks must not re-search a bare device namespace.
            var prepared = NetworkObjectSelectorResolver.ResolveNode(project, target);
            if (!prepared.Success) throw new WorkerOperationException(prepared.FailureCategory!, prepared.Error!);
            var dispatchTarget = target.InterfacePath is not null || target.ItemPath is not null ? target : prepared.Resolved!.Target;
            dispatchTarget.DeviceName = target.DeviceName;
            dispatchTarget.InterfaceName = target.InterfaceName;
            dispatchTarget.NodeIndex = target.NodeIndex;
            var result = NetworkDeviceConfigurator.Configure(
                project,
                dispatchTarget,
                request.IpAddress,
                request.SubnetMask,
                request.PnDeviceName,
                request.PnDeviceNameAutoGeneration,
                request.SubnetId,
                request.IoSystemSubnetId,
                request.IoSystemNumber);
            request.NetworkObjectTarget = dispatchTarget;
            result.Verification = NetworkMutationVerifier.VerifyConfiguration(project, request, result);
            return Success(result);
        });
    }

    private static WorkerResponse CreateSubnet(WorkerRequest request)
    {
        if (!request.Confirm)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "Operation not confirmed. Set confirm=true to proceed with creating a subnet.");
        }

        if (string.IsNullOrWhiteSpace(request.SubnetName))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "SubnetName is required.");
        }

        if (!SubnetLifecycleContract.IsSupportedNetworkType(request.SubnetNetworkType))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "SubnetNetworkType is required. Valid values: "
                + $"{SubnetLifecycleContract.Ethernet}, {SubnetLifecycleContract.Profibus}.");
        }

        if (string.Equals(request.SubnetNetworkType, SubnetLifecycleContract.Ethernet, StringComparison.Ordinal)
            && (request.SubnetHighestAddress is not null || request.SubnetTransmissionSpeed is not null))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                $"SubnetHighestAddress and SubnetTransmissionSpeed are not applicable for network type "
                + $"'{SubnetLifecycleContract.Ethernet}'.");
        }

        ValidateSubnetHighestAddressRange(request.SubnetHighestAddress);
        ValidateSubnetTransmissionSpeedValue(request.SubnetTransmissionSpeed);

        return WithSubnetLifecycleProject(request, session => Success(SubnetLifecycleService.Create(
            session.TiaPortal!,
            session.RequireStandaloneOwner().Project,
            request.SubnetName!,
            request.SubnetNetworkType!,
            request.SubnetHighestAddress,
            request.SubnetTransmissionSpeed)));
    }

    private static WorkerResponse UpdateSubnet(WorkerRequest request)
    {
        if (!request.Confirm)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "Operation not confirmed. Set confirm=true to proceed with updating a subnet.");
        }

        if (string.IsNullOrWhiteSpace(request.SubnetId))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "SubnetId is required.");
        }

        if (request.SubnetName is not null && string.IsNullOrWhiteSpace(request.SubnetName))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "SubnetName must not be blank when supplied.");
        }

        if (request.SubnetName is null && request.SubnetHighestAddress is null && request.SubnetTransmissionSpeed is null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "update_subnet requires at least one requested change (name, highestAddress, or transmissionSpeed).");
        }

        ValidateSubnetHighestAddressRange(request.SubnetHighestAddress);
        ValidateSubnetTransmissionSpeedValue(request.SubnetTransmissionSpeed);

        return WithSubnetLifecycleProject(request, session => Success(SubnetLifecycleService.Update(
            session.TiaPortal!,
            session.RequireStandaloneOwner().Project,
            request.SubnetId!,
            request.SubnetName,
            request.SubnetHighestAddress,
            request.SubnetTransmissionSpeed)));
    }

    private static WorkerResponse DeleteSubnet(WorkerRequest request)
    {
        if (!request.Confirm)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "Operation not confirmed. Set confirm=true to proceed with deleting a subnet.");
        }

        if (string.IsNullOrWhiteSpace(request.SubnetId))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "SubnetId is required.");
        }

        return WithSubnetLifecycleProject(request, session => Success(SubnetLifecycleService.Delete(
            session.TiaPortal!,
            session.RequireStandaloneOwner().Project,
            request.SubnetId!)));
    }

    private static void ValidateSubnetHighestAddressRange(int? value)
    {
        if (value is { } address
            && (address < SubnetLifecycleContract.MinimumHighestAddress || address > SubnetLifecycleContract.MaximumHighestAddress))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                $"SubnetHighestAddress must be between {SubnetLifecycleContract.MinimumHighestAddress} and "
                + $"{SubnetLifecycleContract.MaximumHighestAddress} (received {address}).");
        }
    }

    private static void ValidateSubnetTransmissionSpeedValue(string? value)
    {
        if (value is not null && !SubnetLifecycleContract.IsSupportedTransmissionSpeed(value))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                $"SubnetTransmissionSpeed value '{value}' is not supported. Valid values: "
                + $"{string.Join(", ", SubnetLifecycleContract.TransmissionSpeeds)}.");
        }
    }

    /// <summary>
    /// Shared session/project plumbing for the three subnet lifecycle operations: connects, selects
    /// only an already-open standalone project, and requires both <see cref="WorkerTiaPortalSession.TiaPortal"/>
    /// and <see cref="WorkerTiaPortalSession.Project"/> — the lifecycle service needs the portal handle
    /// for <c>ExclusiveAccess</c>/<c>Transaction</c>, not just the project.
    /// </summary>
    private static WorkerResponse WithSubnetLifecycleProject(WorkerRequest request, Func<WorkerTiaPortalSession, WorkerResponse> body)
    {
        return WithSession(request, session =>
        {
            var failure = EnsureRequestedProjectOpen(session, request.ProjectPath);
            if (failure is not null)
            {
                return failure;
            }
            ValidateExpectedAfterProjectResolution(session, request);

            if (session.ActiveContext is null || session.TiaPortal is null)
            {
                return Failure(
                    WorkerFailureCategories.WorkerOperationFailed,
                    "No project or TIA Portal session is available for the subnet lifecycle operation.");
            }

            session.RequireStandaloneOwner();
            return body(session);
        });
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

        return WithProject(request, project => Success(
            CrossReferenceReader.Read(project, request.CrossReferenceSelector, filter, request.MaxResults)));
    }

    private static WorkerResponse GetBlockContent(WorkerRequest request)
    {
        if (string.IsNullOrEmpty(request.BlockPath))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "BlockPath is required.");
        }

        var format = NormalizeBlockFormat(request.Format);
        var withDependencies = request.WithDependencies == true;

        return WithProject(request, project =>
        {
            var content = BlockExporter.Export(project, request.BlockPath!, format, withDependencies);
            return RawPayload(content, SourceReadWarnings.ForExport(withDependencies, format, content));
        });
    }

    private static WorkerResponse UpdateBlockLogic(WorkerRequest request)
    {
        var normalizedFormat = BlockUpdateOutcomeDecorator.NormalizeFormatOrNull(request.Format);
        var importerEntered = false;
        try
        {
            if (string.IsNullOrEmpty(request.BlockPath))
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "BlockPath is required.");
            }

            if (string.IsNullOrEmpty(request.Content))
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "Content is required.");
            }

            normalizedFormat = NormalizeBlockFormat(request.Format);

            var response = WithProject(request, project =>
            {
                importerEntered = true;
                var result = BlockImporter.Import(project, request.BlockPath!, request.Content!, normalizedFormat, request.ExpectedContentHash);
                return RawPayload(result.Payload, result.Warnings, result.Outcome);
            });
            return response.Success
                ? response
                : BlockUpdateOutcomeDecorator.Decorate(response, normalizedFormat, importerEntered);
        }
        catch (WorkerOperationException exception)
        {
            return BlockUpdateOutcomeDecorator.Decorate(exception, normalizedFormat, importerEntered);
        }
        catch (Exception exception)
        {
            return BlockUpdateOutcomeDecorator.Decorate(exception, normalizedFormat, importerEntered);
        }
    }

    private static WorkerResponse GetTypeContent(WorkerRequest request)
    {
        if (string.IsNullOrEmpty(request.TypePath))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "TypePath is required.");
        }

        var format = NormalizeTypeFormat(request.Format);
        var withDependencies = request.WithDependencies == true;

        return WithProject(request, project =>
        {
            var content = PlcTypeExporter.Export(project, request.TypePath!, format, withDependencies);
            return RawPayload(content, SourceReadWarnings.ForExport(withDependencies, format, content));
        });
    }

    private static WorkerResponse UpdateTypeContent(WorkerRequest request)
    {
        if (string.IsNullOrEmpty(request.TypePath))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "TypePath is required.");
        }

        if (string.IsNullOrEmpty(request.Content))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "Content is required.");
        }

        var format = NormalizeTypeFormat(request.Format);

        return WithProject(request, project => Success(
            PlcTypeImporter.Import(project, request.TypePath!, request.Content!, format, request.ExpectedContentHash)));
    }

    /// <summary>
    /// The host normalizes <see cref="WorkerRequest.Format"/> before sending, but internal
    /// diagnostics and tests can invoke the worker directly. Type operations therefore re-apply
    /// their own default (source, i.e. .udt) rather than trusting the field to be set.
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
        return WithProject(request, project => Success(TagTableReader.ReadInventory(
            project, request.PlcName, request.TableName, request.FolderPath)));
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

    private static WorkerResponse GetProjectStatus(WorkerRequest request)
    {
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
            session => ProjectLifecycleService.SaveProject(
                session,
                request.ProjectPath,
                request.ExpectedSessionIdentity),
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
                request.Rebind,
                request.ExpectedSessionIdentity),
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
                request.SaveBeforeArchive,
                request.ExpectedSessionIdentity),
            requiresConfirm: true);
    }

    private static WorkerResponse CloseProject(WorkerRequest request)
    {
        return ProjectLifecycle(
            request,
            session => ProjectLifecycleService.CloseProject(
                session,
                request.ProjectPath,
                request.SaveBeforeClose,
                request.ExpectedSessionIdentity),
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
            var failure = EnsureRequestedProjectOpen(session, request.ProjectPath);
            if (failure is not null)
            {
                return failure;
            }
            ValidateExpectedAfterProjectResolution(session, request);

            if (session.ActiveContext is null)
            {
                return Failure(
                    WorkerFailureCategories.WorkerOperationFailed,
                    ProjectOpenPolicy.NoProjectOpenMessage(_accessMode));
            }

            return body(session.RequireStandaloneOwner().Project);
        });
    }

    /// <summary>
    /// Applies <see cref="ProjectOpenPolicy"/> before a non-lifecycle operation uses the
    /// already-open project. Returns null to continue, or a failure response to the host.
    /// </summary>
    private static WorkerResponse? EnsureRequestedProjectOpen(WorkerTiaPortalSession session, string? requestedProjectPath)
    {
        var currentPath = session.CurrentProjectPath;

        switch (ProjectOpenPolicy.Decide(currentPath, requestedProjectPath))
        {
            case ProjectOpenDecision.RequestedNotOpen:
                return Failure(
                    WorkerFailureCategories.AccessDenied,
                    ProjectOpenPolicy.NotOpenMessage(requestedProjectPath!, _accessMode));
            case ProjectOpenDecision.Refuse:
                return Failure(
                    WorkerFailureCategories.BindingConflict,
                    ProjectOpenPolicy.RefusalMessage(currentPath!, requestedProjectPath!, _accessMode));
            default:
                if (currentPath is not null)
                    return null;
                return Failure(
                    WorkerFailureCategories.WorkerOperationFailed,
                    ProjectOpenPolicy.NoProjectOpenMessage(_accessMode));
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

            return body(_sharedSession);
        });
    }

    private static bool AllowsMissingExpectedSessionIdentity(string method)
        => !OperationPolicyCatalog.RequiresExpectedSessionIdentity(method);

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
        BlockImportOutcomeInfo? blockImportOutcome = null)
    {
        return new WorkerResponse
        {
            Success = true,
            Payload = payload,
            Warnings = warnings is { Count: > 0 } ? new List<string>(warnings) : null,
            BlockImportOutcome = blockImportOutcome
        };
    }

    private static WorkerResponse Failure(
        string failureCategory,
        string error,
        IReadOnlyList<string>? warnings = null,
        BlockImportOutcomeInfo? blockImportOutcome = null)
    {
        return new WorkerResponse
        {
            Success = false,
            Error = error,
            FailureCategory = failureCategory,
            Warnings = warnings is { Count: > 0 } ? new List<string>(warnings) : null,
            BlockImportOutcome = blockImportOutcome
        };
    }
}
