using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TiaMcpServer.Contracts;
using TiaMcpServer.Diagnostics;

namespace TiaMcpServer.Worker;

public class OpennessWorkerClient : IDisposable
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromMinutes(5);

    private readonly ProjectSessionBinding _projectSessionBinding;
    private readonly ILogger<OpennessWorkerClient>? _logger;
    private readonly string? _workerExecutablePathOverride;
    private readonly TimeSpan _requestTimeout;
    private readonly object _transportLock = new();
    private PersistentWorkerTransport? _transport;

    public OpennessWorkerClient(
        ProjectSessionBinding projectSessionBinding,
        ILogger<OpennessWorkerClient>? logger = null,
        string? workerExecutablePath = null,
        TimeSpan? requestTimeout = null)
    {
        _projectSessionBinding = projectSessionBinding;
        _logger = logger;
        _workerExecutablePathOverride = workerExecutablePath;
        _requestTimeout = requestTimeout ?? DefaultRequestTimeout;
    }

    /// <summary>
    /// How a completed (successful) worker call changes this session's project binding. Declared
    /// explicitly per call site so the binding transition is a deliberate, readable property of
    /// each operation rather than an implicit side effect of "some call succeeded".
    /// </summary>
    private enum BindingTransition
    {
        /// <summary>No binding change. Direct status, the internal lifecycle probe, save, archive,
        /// and every unrelated data read/write use this; an unbound session stays unbound, and a
        /// bound session only gets a divergence warning if the worker reports a different project.</summary>
        None,

        /// <summary>Bind the session to the worker's reported <see cref="WorkerCallResult.ResolvedProjectPath"/>.
        /// Open, create, and rebinding save-as use this; a missing resolved path is a broken
        /// postcondition, never a fallback to caller input.</summary>
        BindResolvedPath,

        /// <summary>Clear the session binding. Close uses this.</summary>
        Clear
    }

    public Task<WorkerCallResult> BrowseProjectTreeAsync(string? projectPath, int? depth = null, string? startPath = null)
    {
        return SendBoundProjectRequestAsync(
            "browse_project_tree",
            projectPath,
            request =>
            {
                request.Depth = depth;
                request.StartPath = startPath;
            },
            "[]");
    }

    public Task<WorkerCallResult> ReadHardwareConfigAsync(string? projectPath)
    {
        return SendBoundProjectRequestAsync("read_hardware_config", projectPath, _ => { }, "{}");
    }

    public Task<WorkerCallResult> SearchEquipmentCatalogAsync(string query, string? projectPath, int? maxResults = null)
    {
        return SendBoundProjectRequestAsync(
            "search_equipment_catalog",
            projectPath,
            request =>
            {
                request.Query = query;
                request.MaxResults = maxResults;
            },
            "[]");
    }

    public Task<WorkerCallResult> AddNetworkDeviceAsync(
        string typeIdentifier,
        string deviceName,
        string deviceItemName,
        string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "add_network_device",
            projectPath,
            request =>
            {
                request.TypeIdentifier = typeIdentifier;
                request.DeviceName = deviceName;
                request.DeviceItemName = deviceItemName;
                request.Confirm = true;
                request.AllowTiaConfirmations = true;
            },
            "{}");
    }

    public Task<WorkerCallResult> ConfigureNetworkDeviceAsync(
        string deviceName,
        string? ipAddress,
        string? subnetMask,
        string? pnDeviceName,
        string? subnetName,
        string? ioSystemName,
        string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "configure_network_device",
            projectPath,
            request =>
            {
                request.DeviceName = deviceName;
                request.IpAddress = ipAddress;
                request.SubnetMask = subnetMask;
                request.PnDeviceName = pnDeviceName;
                request.SubnetName = subnetName;
                request.IoSystemName = ioSystemName;
                request.Confirm = true;
                request.AllowTiaConfirmations = true;
            },
            "{}");
    }

    public Task<WorkerCallResult> ReadCrossReferencesAsync(string? projectPath, string? plcName, string? filter, int? maxResults = null)
    {
        // Validate the filter before TryResolve so an invalid filter fails fast without a worker round-trip.
        if (!CrossReferenceFilterNames.TryNormalize(filter, out var normalizedFilter, out var filterError))
        {
            return Task.FromResult(WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, filterError!));
        }

        return SendBoundProjectRequestAsync(
            "read_cross_references",
            projectPath,
            request =>
            {
                request.PlcName = plcName;
                request.CrossReferenceFilter = normalizedFilter;
                request.MaxResults = maxResults;
            },
            "{}");
    }

    public Task<WorkerCallResult> GetBlockContentAsync(string blockPath, string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "get_block_content",
            projectPath,
            request => request.BlockPath = blockPath,
            string.Empty);
    }

    public Task<WorkerCallResult> UpdateBlockLogicAsync(string blockPath, string yamlContent, string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "update_block_logic",
            projectPath,
            request =>
            {
                request.BlockPath = blockPath;
                request.YamlContent = yamlContent;
                request.AllowTiaConfirmations = true;
            },
            string.Empty);
    }

    public Task<WorkerCallResult> ListTagTablesAsync(string? plcName, string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "list_tag_tables",
            projectPath,
            request => request.PlcName = plcName,
            "[]");
    }

    public Task<WorkerCallResult> CompileCheckAsync(string? blockPath, string? plcName, string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "compile_check",
            projectPath,
            request =>
            {
                request.BlockPath = blockPath;
                request.PlcName = plcName;
            },
            "{}");
    }

    public Task<WorkerCallResult> CreateTagTableAsync(
        string? plcName,
        string tableName,
        string? folderPath,
        string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "create_tag_table",
            projectPath,
            request =>
            {
                request.PlcName = plcName;
                request.TableName = tableName;
                request.FolderPath = folderPath;
                request.Confirm = true;
                request.AllowTiaConfirmations = true;
            },
            "{}");
    }

    public Task<WorkerCallResult> DeleteTagTableAsync(
        string? plcName,
        string tableName,
        string? folderPath,
        string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "delete_tag_table",
            projectPath,
            request =>
            {
                request.PlcName = plcName;
                request.TableName = tableName;
                request.FolderPath = folderPath;
                request.Confirm = true;
                request.AllowTiaConfirmations = true;
            },
            "{}");
    }

    public Task<WorkerCallResult> CreateTagAsync(
        string? plcName,
        string tableName,
        string? folderPath,
        string name,
        string dataType,
        string? logicalAddress,
        string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "create_tag",
            projectPath,
            request =>
            {
                request.PlcName = plcName;
                request.TableName = tableName;
                request.FolderPath = folderPath;
                request.Name = name;
                request.DataType = dataType;
                request.LogicalAddress = logicalAddress;
                request.Confirm = true;
                request.AllowTiaConfirmations = true;
            },
            "{}");
    }

    public Task<WorkerCallResult> UpdateTagAsync(
        string? plcName,
        string tableName,
        string? folderPath,
        string name,
        string? newName,
        string? dataType,
        string? logicalAddress,
        bool? externalAccessible,
        bool? externalVisible,
        bool? externalWritable,
        bool? isSafety,
        string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "update_tag",
            projectPath,
            request =>
            {
                request.PlcName = plcName;
                request.TableName = tableName;
                request.FolderPath = folderPath;
                request.Name = name;
                request.NewName = newName;
                request.DataType = dataType;
                request.LogicalAddress = logicalAddress;
                request.ExternalAccessible = externalAccessible;
                request.ExternalVisible = externalVisible;
                request.ExternalWritable = externalWritable;
                request.IsSafety = isSafety;
                request.Confirm = true;
                request.AllowTiaConfirmations = true;
            },
            "{}");
    }

    public Task<WorkerCallResult> DeleteTagAsync(
        string? plcName,
        string tableName,
        string? folderPath,
        string name,
        string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "delete_tag",
            projectPath,
            request =>
            {
                request.PlcName = plcName;
                request.TableName = tableName;
                request.FolderPath = folderPath;
                request.Name = name;
                request.Confirm = true;
                request.AllowTiaConfirmations = true;
            },
            "{}");
    }

    public Task<WorkerCallResult> CreateUserConstantAsync(
        string? plcName,
        string tableName,
        string? folderPath,
        string name,
        string dataType,
        string value,
        string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "create_user_constant",
            projectPath,
            request =>
            {
                request.PlcName = plcName;
                request.TableName = tableName;
                request.FolderPath = folderPath;
                request.Name = name;
                request.DataType = dataType;
                request.Value = value;
                request.Confirm = true;
                request.AllowTiaConfirmations = true;
            },
            "{}");
    }

    public Task<WorkerCallResult> UpdateUserConstantAsync(
        string? plcName,
        string tableName,
        string? folderPath,
        string name,
        string? dataType,
        string? value,
        string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "update_user_constant",
            projectPath,
            request =>
            {
                request.PlcName = plcName;
                request.TableName = tableName;
                request.FolderPath = folderPath;
                request.Name = name;
                request.DataType = dataType;
                request.Value = value;
                request.Confirm = true;
                request.AllowTiaConfirmations = true;
            },
            "{}");
    }

    public Task<WorkerCallResult> DeleteUserConstantAsync(
        string? plcName,
        string tableName,
        string? folderPath,
        string name,
        string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "delete_user_constant",
            projectPath,
            request =>
            {
                request.PlcName = plcName;
                request.TableName = tableName;
                request.FolderPath = folderPath;
                request.Name = name;
                request.Confirm = true;
                request.AllowTiaConfirmations = true;
            },
            "{}");
    }

    public Task<WorkerCallResult> CreateBlockAsync(
        string blockPath,
        string blockType,
        string? language,
        string? obEventClass,
        string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "create_block",
            projectPath,
            request =>
            {
                request.BlockPath = blockPath;
                request.BlockType = blockType;
                request.Language = language;
                request.OBEventClass = obEventClass;
                request.Confirm = true;
                request.AllowTiaConfirmations = true;
            },
            "{}");
    }

    public Task<WorkerCallResult> DeleteBlockAsync(string blockPath, string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "delete_block",
            projectPath,
            request =>
            {
                request.BlockPath = blockPath;
                request.Confirm = true;
                request.AllowTiaConfirmations = true;
            },
            "{}");
    }

    public Task<WorkerCallResult> CreateBlockGroupAsync(string blockPath, string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "create_block_group",
            projectPath,
            request =>
            {
                request.BlockPath = blockPath;
                request.Confirm = true;
                request.AllowTiaConfirmations = true;
            },
            "{}");
    }

    public Task<WorkerCallResult> DeleteBlockGroupAsync(string blockPath, string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "delete_block_group",
            projectPath,
            request =>
            {
                request.BlockPath = blockPath;
                request.Confirm = true;
                request.AllowTiaConfirmations = true;
            },
            "{}");
    }

    public Task<WorkerCallResult> StartPlcAsync(string? plcName, string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "start_plc",
            projectPath,
            request =>
            {
                request.PlcName = plcName;
                request.Confirm = true;
                request.AllowTiaConfirmations = true;
            },
            "{}");
    }

    public Task<WorkerCallResult> StopPlcAsync(string? plcName, string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "stop_plc",
            projectPath,
            request =>
            {
                request.PlcName = plcName;
                request.Confirm = true;
                request.AllowTiaConfirmations = true;
            },
            "{}");
    }

    public Task<WorkerCallResult> GetProjectStatusAsync(string? projectPath)
    {
        // BindingTransition.None (the default): a direct status read never binds an unbound
        // session and never adopts a diverging worker path - it only warns on divergence.
        return SendBoundProjectRequestAsync(
            "get_project_status",
            projectPath,
            _ => { },
            "{}");
    }

    /// <summary>
    /// Internal state read used only by save/save-as/archive/close preview and apply-time
    /// current-state checks. The worker method backing this call may open a project when a
    /// path is supplied and none is open yet (required so those lifecycle writes can inspect
    /// state before acting) - but exactly like <see cref="GetProjectStatusAsync"/>, this
    /// host-side call is <see cref="BindingTransition.None"/>: an unbound session stays
    /// unbound even on success. Never exposed as an MCP tool; callable only from
    /// <c>ProjectLifecycleTools</c>'s own lifecycle-write implementations.
    /// </summary>
    internal Task<WorkerCallResult> ProbeProjectStatusForLifecycleAsync(string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "probe_project_status_for_lifecycle",
            projectPath,
            _ => { },
            "{}");
    }

    public async Task<WorkerCallResult> OpenProjectAsync(string projectPath, bool forceRebind)
    {
        // A blank/whitespace path is caller input error, not a binding conflict — check it
        // separately so CanBind's single out-string ("Project path is required." vs. an
        // already-bound conflict) isn't collapsed into one category by inferring from its text.
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, "Project path is required.");
        }

        // Upfront gate: the generic helper's TryResolve has no forceRebind concept, so open keeps
        // its own binding-policy check against the CALLER's requested path before doing any work.
        if (!_projectSessionBinding.CanBind(projectPath, forceRebind, out var bindingError))
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.BindingConflict, bindingError!);
        }

        var boundProjectPathBeforeCall = _projectSessionBinding.BoundProjectPath;
        var result = await InvokeWorkerAsync(
            new WorkerRequest
            {
                Method = "open_project",
                ProjectPath = projectPath,
                Confirm = true,
                ForceRebind = forceRebind,
                AllowTiaConfirmations = true
            }).ConfigureAwait(false);

        if (!result.Success)
        {
            return result;
        }

        // Bind to the project the worker actually opened, never the caller's projectPath argument.
        result = ApplyBindingTransition(
            BindingTransition.BindResolvedPath,
            result,
            requestedProjectPath: projectPath,
            boundProjectPathBeforeCall,
            bindForceRebind: forceRebind);

        if (!result.Success)
        {
            return result;
        }

        return string.IsNullOrEmpty(result.Payload) ? result with { Payload = "{}" } : result;
    }

    public async Task<WorkerCallResult> CreateProjectAsync(
        string projectDirectory,
        string projectName,
        string? author,
        string? comment)
    {
        var boundProjectPathBeforeCall = _projectSessionBinding.BoundProjectPath;
        var result = await InvokeWorkerAsync(
            new WorkerRequest
            {
                Method = "create_project",
                ProjectDirectory = projectDirectory,
                ProjectName = projectName,
                Author = author,
                Comment = comment,
                Confirm = true,
                AllowTiaConfirmations = true
            }).ConfigureAwait(false);

        if (!result.Success)
        {
            return result;
        }

        // Bind to the project the worker actually created (its ResolvedProjectPath), never a path
        // parsed from payload text or reconstructed from the caller's directory/name. A newly
        // created project is a fresh binding target, so force-rebind past any prior binding.
        result = ApplyBindingTransition(
            BindingTransition.BindResolvedPath,
            result,
            requestedProjectPath: null,
            boundProjectPathBeforeCall,
            bindForceRebind: true);

        if (!result.Success)
        {
            return result;
        }

        return string.IsNullOrEmpty(result.Payload) ? result with { Payload = "{}" } : result;
    }

    public Task<WorkerCallResult> SaveProjectAsync(string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "save_project",
            projectPath,
            request =>
            {
                request.Confirm = true;
                request.AllowTiaConfirmations = true;
            },
            "{}");
    }

    /// <summary>
    /// Shared rejection message for the unsupported <c>save_project_as(rebind:false)</c> mode.
    /// Referenced by both this client's guard and <c>ProjectLifecycleTools.SaveProjectAs</c> so
    /// the two host-side defenses speak with one voice.
    /// </summary>
    internal const string RebindFalseUnsupportedMessage =
        "save_project_as requires rebind=true. The rebind=false mode is not supported: Siemens "
        + "SaveAs switches the active project to the copy, so a non-rebinding save would leave the "
        + "TIA Openness worker and this MCP session bound to different projects.";

    public Task<WorkerCallResult> SaveProjectAsAsync(
        string? projectPath,
        string targetDirectory,
        string targetName,
        bool rebind)
    {
        // Defense in depth (mirrors the tool-layer guard): rebind=false is rejected before any
        // worker invocation, so it can never reach the transport or mutate the session binding.
        if (!rebind)
        {
            return Task.FromResult(
                WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, RebindFalseUnsupportedMessage));
        }

        // Past the guard rebind is always true: the worker opens the copy before this call
        // returns, so the session adopts the worker's ResolvedProjectPath (never payload text) and
        // gets no divergence warning. Task 4 tightens the worker-side copied-path guarantees behind
        // that ResolvedProjectPath.
        return SendBoundProjectRequestAsync(
            "save_project_as",
            projectPath,
            request =>
            {
                request.TargetDirectory = targetDirectory;
                request.TargetName = targetName;
                request.Rebind = true;
                request.Confirm = true;
                request.AllowTiaConfirmations = true;
            },
            "{}",
            BindingTransition.BindResolvedPath);
    }

    public Task<WorkerCallResult> ArchiveProjectAsync(
        string? projectPath,
        string archiveDirectory,
        string archiveName,
        string? mode,
        bool saveBeforeArchive)
    {
        if (!ArchiveModeNames.TryNormalize(mode, out var normalizedMode, out var modeError))
        {
            return Task.FromResult(WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, modeError!));
        }

        return SendBoundProjectRequestAsync(
            "archive_project",
            projectPath,
            request =>
            {
                request.ArchiveDirectory = archiveDirectory;
                request.ArchiveName = archiveName;
                request.ArchiveMode = normalizedMode;
                request.SaveBeforeArchive = saveBeforeArchive;
                request.Confirm = true;
                request.AllowTiaConfirmations = true;
            },
            "{}");
    }

    public Task<WorkerCallResult> CloseProjectAsync(string? projectPath, bool saveBeforeClose)
    {
        return SendBoundProjectRequestAsync(
            "close_project",
            projectPath,
            request =>
            {
                request.SaveBeforeClose = saveBeforeClose;
                request.Confirm = true;
                request.AllowTiaConfirmations = true;
            },
            "{}",
            BindingTransition.Clear);
    }

    private async Task<WorkerCallResult> SendBoundProjectRequestAsync(
        string method,
        string? projectPath,
        Action<WorkerRequest> configure,
        string emptyPayload,
        BindingTransition transition = BindingTransition.None)
    {
        var boundProjectPathBeforeCall = _projectSessionBinding.BoundProjectPath;
        if (!_projectSessionBinding.TryResolve(projectPath, out var effectiveProjectPath, out var bindingError))
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.BindingConflict, bindingError!);
        }

        var request = new WorkerRequest
        {
            Method = method,
            ProjectPath = effectiveProjectPath
        };
        configure(request);

        var result = await InvokeWorkerAsync(request).ConfigureAwait(false);
        if (result.Success)
        {
            // The only helper caller that binds is save_project_as(rebind:true), which force-rebinds
            // onto the copy by design; None/Clear ignore bindForceRebind.
            result = ApplyBindingTransition(
                transition,
                result,
                requestedProjectPath: projectPath,
                boundProjectPathBeforeCall,
                bindForceRebind: true);
        }

        return result.Success && string.IsNullOrEmpty(result.Payload)
            ? result with { Payload = emptyPayload }
            : result;
    }

    /// <summary>
    /// Applies a call's declared <see cref="BindingTransition"/> to a SUCCESSFUL worker result.
    /// This is the single place a session binding changes as the result of a completed call, so
    /// the rule "bind only to worker ground truth, only on success" lives in exactly one method.
    /// Returns the original result (possibly with a divergence warning appended), or a
    /// <c>postcondition_failed</c>/<c>binding_conflict</c> failure if a required bind could not
    /// be honored.
    /// </summary>
    private WorkerCallResult ApplyBindingTransition(
        BindingTransition transition,
        WorkerCallResult result,
        string? requestedProjectPath,
        string? boundProjectPathBeforeCall,
        bool bindForceRebind)
    {
        switch (transition)
        {
            case BindingTransition.BindResolvedPath:
                return BindToResolvedProjectPath(result, bindForceRebind);
            case BindingTransition.Clear:
                // Close leaves the session with nothing bound. Clear(requestedProjectPath) is the
                // guarded path; if it refuses (a different project was actually open) fall back to
                // the unconditional Clear(null) so no stale binding can survive a close.
                if (!_projectSessionBinding.Clear(requestedProjectPath, out _))
                {
                    _projectSessionBinding.Clear(null, out _);
                }

                return result;
            case BindingTransition.None:
            default:
                return WarnOnBindingDivergence(result, boundProjectPathBeforeCall);
        }
    }

    /// <summary>
    /// Binds the session to the worker's reported <see cref="WorkerCallResult.ResolvedProjectPath"/>.
    /// A success with no resolved path is a broken postcondition - it must NEVER fall back to the
    /// caller's requested path, a target directory/name, or anything parsed from payload text.
    /// </summary>
    private WorkerCallResult BindToResolvedProjectPath(WorkerCallResult result, bool forceRebind)
    {
        if (string.IsNullOrWhiteSpace(result.ResolvedProjectPath))
        {
            return WorkerCallResult.Fail(
                WorkerFailureCategories.PostconditionFailed,
                "The TIA Openness worker reported success but did not return the project path it "
                + "opened, so this MCP session cannot be bound to it. Inspect the current project "
                + "state in TIA Portal before retrying.",
                result.Warnings);
        }

        if (!_projectSessionBinding.Bind(result.ResolvedProjectPath!, forceRebind, out var bindError))
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.BindingConflict, bindError!, result.Warnings);
        }

        return result;
    }

    /// <summary>
    /// For a <see cref="BindingTransition.None"/> call on an already-bound session, surfaces a
    /// single divergence warning (without adopting the worker's path) when the worker reports it
    /// operated on a different project than the one this session is bound to. Equivalent path
    /// spellings produce no warning; the binding is left untouched.
    /// </summary>
    private WorkerCallResult WarnOnBindingDivergence(WorkerCallResult result, string? boundProjectPathBeforeCall)
    {
        if (boundProjectPathBeforeCall is null
            || result.ResolvedProjectPath is null
            || _projectSessionBinding.IsBoundTo(result.ResolvedProjectPath))
        {
            // Unbound sessions never bind here (that is the intentional behavior change: only
            // open/create/save-as bind), and an equivalent path is not a divergence.
            return result;
        }

        // Containment only (see docs/superpowers/specs Round 4 design): the root cause - a
        // zero-confirmation read tool able to attach a different project than the one this session
        // is bound to - is deferred. Surface the divergence so the caller (and a human) can see it,
        // rather than silently trusting the bound path.
        //
        // IsBoundTo lexically canonicalizes both sides with Path.GetFullPath (see
        // ProjectPathNormalization) instead of comparing the caller's raw spelling. It does not
        // resolve filesystem identity aliases such as 8.3 short names, junctions/symlinks, or UNC
        // paths versus mapped drives.
        _logger?.LogWarning(
            "TIA Openness worker: session is bound to '{BoundProjectPath}' but the worker reports it "
            + "operated on '{ResolvedProjectPath}'.",
            boundProjectPathBeforeCall,
            result.ResolvedProjectPath);

        return result with
        {
            Warnings = AppendWarning(
                result.Warnings,
                $"This MCP session is bound to project '{boundProjectPathBeforeCall}', but the TIA Openness "
                + $"worker reports it actually operated on '{result.ResolvedProjectPath}' for this call. "
                + $"Treat this call's results as describing '{result.ResolvedProjectPath}', not the bound "
                + "project. If this is unexpected, verify what is currently open in the TIA Portal UI "
                + "before issuing any write, or start a new MCP session bound to the intended project.")
        };
    }

    private static IReadOnlyList<string> AppendWarning(IReadOnlyList<string> warnings, string warning)
    {
        var combined = new List<string>(warnings.Count + 1);
        combined.AddRange(warnings);
        combined.Add(warning);
        return combined;
    }

    /// <summary>
    /// Message used for every transport-level failure (timeout, crash, broken pipe, null
    /// response, malformed protocol data): the write may or may not have reached TIA Portal, so
    /// the caller must inspect current state rather than assume either outcome and retry.
    /// </summary>
    private const string InspectStateBeforeRetryGuidance =
        "The write outcome is unknown. Inspect current project state before retrying.";

    private async Task<WorkerCallResult> InvokeWorkerAsync(WorkerRequest request)
    {
        try
        {
            // Exactly one transport request per call: SendAsync neither loops nor retries: on any
            // failure below the transport already killed/will-recreate its process on the NEXT
            // call, and this method never re-invokes SendAsync for the request that just failed.
            var response = await GetOrCreateTransport().SendAsync(request).ConfigureAwait(false);
            var warnings = CapWarnings(response.Warnings);
            foreach (var warning in warnings)
            {
                _logger?.LogWarning("TIA Openness worker warning: {Line}", warning);
            }

            if (response.Success)
            {
                return WorkerCallResult.Ok(response.Payload ?? string.Empty, warnings) with
                {
                    ResolvedProjectPath = response.ResolvedProjectPath
                };
            }

            var failureCategory = WorkerFailureCategories.IsKnown(response.FailureCategory)
                ? response.FailureCategory!
                : WorkerFailureCategories.WorkerOperationFailed;
            return WorkerCallResult.Fail(
                failureCategory,
                response.Error ?? "The TIA Openness worker failed without an error message.",
                warnings);
        }
        catch (Win32Exception ex)
        {
            // The worker process never started (e.g. missing/invalid executable): a distinct,
            // more actionable failure than a mid-request crash, so it keeps its own message and
            // the generic worker-operation-failed category rather than worker_crashed.
            return WorkerCallResult.Fail(
                WorkerFailureCategories.WorkerOperationFailed,
                $"Failed to launch the TIA Openness worker process ({ex.Message}). "
                + "Verify that .NET Framework 4.8 is installed and that the 'openness-worker' folder "
                + "beside the MCP server executable is complete; rebuild or reinstall if files are missing.");
        }
        catch (TimeoutException)
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.WorkerTimeout, InspectStateBeforeRetryGuidance);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or JsonException)
        {
            // Broken pipe (IOException), a crashed/null response or a failed process launch
            // (InvalidOperationException), or malformed/protocol-desynced JSON (JsonException) —
            // all mean the worker cannot be trusted to have completed the request as sent.
            return WorkerCallResult.Fail(WorkerFailureCategories.WorkerCrashed, InspectStateBeforeRetryGuidance);
        }
    }

    private PersistentWorkerTransport GetOrCreateTransport()
    {
        lock (_transportLock)
        {
            _transport ??= new PersistentWorkerTransport(
                _workerExecutablePathOverride ?? LocateWorkerExecutable(),
                _requestTimeout,
                _logger);
            return _transport;
        }
    }

    public void Dispose()
    {
        lock (_transportLock)
        {
            _transport?.Dispose();
            _transport = null;
        }
    }

    // A degraded read of a large project can emit hundreds of "Skipping X" lines; cap what
    // reaches the agent so warnings cannot flood a small model's context.
    private const int MaxWarningLines = 20;
    private const int MaxWarningLineChars = 1_000;
    private const string WarningTruncationTrailer = " [TRUNCATED]";

    private static IReadOnlyList<string> CapWarnings(IReadOnlyList<string>? warnings)
    {
        if (warnings is null || warnings.Count == 0)
        {
            return Array.Empty<string>();
        }

        var lines = warnings
            .Select(line => CapWarningLine(line.Trim()))
            .Where(line => line.Length > 0)
            .ToList();

        if (lines.Count > MaxWarningLines)
        {
            var dropped = lines.Count - MaxWarningLines;
            lines = lines.Take(MaxWarningLines).ToList();
            lines.Add($"(+{dropped} more worker warnings truncated)");
        }

        return lines;
    }

    private static string CapWarningLine(string line)
    {
        if (line.Length <= MaxWarningLineChars)
        {
            return line;
        }

        return line.Substring(0, MaxWarningLineChars - WarningTruncationTrailer.Length)
            + WarningTruncationTrailer;
    }

    private static string LocateWorkerExecutable()
        => OpennessWorkerLocator.LocateOrThrow(AppContext.BaseDirectory, FileSystemService.Instance);

}
