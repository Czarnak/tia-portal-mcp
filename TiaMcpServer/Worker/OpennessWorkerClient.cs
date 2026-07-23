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
        return SendBoundProjectRequestAsync(
            "get_project_status",
            projectPath,
            _ => { },
            "{}");
    }

    public async Task<WorkerCallResult> OpenProjectAsync(string projectPath, bool forceRebind)
    {
        if (!_projectSessionBinding.CanBind(projectPath, forceRebind, out var bindingError))
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.BindingConflict, bindingError!);
        }

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

        if (!_projectSessionBinding.Bind(projectPath, forceRebind, out var bindError))
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.BindingConflict, bindError!, result.Warnings);
        }

        return string.IsNullOrEmpty(result.Payload) ? result with { Payload = "{}" } : result;
    }

    public async Task<WorkerCallResult> CreateProjectAsync(
        string projectDirectory,
        string projectName,
        string? author,
        string? comment)
    {
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

        var projectPath = TryReadProjectPath(result.Payload);
        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            _projectSessionBinding.Bind(projectPath!, forceRebind: true, out _);
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

    public async Task<WorkerCallResult> SaveProjectAsAsync(
        string? projectPath,
        string targetDirectory,
        string targetName,
        bool rebind)
    {
        var result = await SendBoundProjectRequestAsync(
            "save_project_as",
            projectPath,
            request =>
            {
                request.TargetDirectory = targetDirectory;
                request.TargetName = targetName;
                request.Rebind = rebind;
                request.Confirm = true;
                request.AllowTiaConfirmations = true;
            },
            "{}",
            // rebind=true deliberately changes which project is attached (worker closes the
            // original and opens the copy before this call returns) - the divergence warning
            // exists to catch unintended drift, not this tool's documented purpose.
            attachmentChangeExpected: rebind).ConfigureAwait(false);

        if (rebind && result.Success)
        {
            var copiedProjectPath = TryReadProjectPath(result.Payload);
            if (!string.IsNullOrWhiteSpace(copiedProjectPath))
            {
                _projectSessionBinding.Bind(copiedProjectPath!, forceRebind: true, out _);
            }
        }

        return result;
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

    public async Task<WorkerCallResult> CloseProjectAsync(string? projectPath, bool saveBeforeClose)
    {
        var result = await SendBoundProjectRequestAsync(
            "close_project",
            projectPath,
            request =>
            {
                request.SaveBeforeClose = saveBeforeClose;
                request.Confirm = true;
                request.AllowTiaConfirmations = true;
            },
            "{}").ConfigureAwait(false);

        if (result.Success && _projectSessionBinding.Clear(projectPath, out _) is false)
        {
            _projectSessionBinding.Clear(null, out _);
        }

        return result;
    }

    private async Task<WorkerCallResult> SendBoundProjectRequestAsync(
        string method,
        string? projectPath,
        Action<WorkerRequest> configure,
        string emptyPayload,
        bool attachmentChangeExpected = false)
    {
        var boundProjectPathBeforeCall = _projectSessionBinding.BoundProjectPath;
        var sessionWasUnbound = boundProjectPathBeforeCall is null;
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
        if (result.Success && result.ResolvedProjectPath is not null)
        {
            if (sessionWasUnbound)
            {
                // Bind to what the worker actually operated on, never to what the caller asked for.
                // forceRebind is false: if a concurrent OpenProjectAsync/CreateProjectAsync bound the
                // session while this call was in flight, decline rather than clobber that binding.
                if (!_projectSessionBinding.Bind(result.ResolvedProjectPath, forceRebind: false, out var bindError))
                {
                    _logger?.LogWarning(
                        "TIA Openness worker: could not bind session to resolved project path '{ResolvedProjectPath}': {Error}",
                        result.ResolvedProjectPath,
                        bindError);
                    result = result with
                    {
                        Warnings = AppendWarning(
                            result.Warnings,
                            $"The TIA Openness worker operated on project '{result.ResolvedProjectPath}', but this MCP "
                            + $"session could not be bound to it ({bindError}). This call's results describe "
                            + $"'{result.ResolvedProjectPath}', which may differ from whatever project a later call "
                            + "without an explicit projectPath resolves to. Pass projectPath explicitly on subsequent "
                            + "calls until the session's binding is resolved.")
                    };
                }
            }
            else if (!attachmentChangeExpected && !_projectSessionBinding.IsBoundTo(result.ResolvedProjectPath))
            {
                // Containment only (see docs/superpowers/specs Round 4 design): the root cause -
                // a zero-confirmation read tool able to attach a different project than the one
                // this session is bound to - is deferred. Surface the divergence so the caller
                // (and a human) can see it, rather than silently trusting the bound path.
                //
                // attachmentChangeExpected opts out an operation that deliberately changes what
                // is attached (save_project_as with rebind=true is the only caller today) - for
                // that operation a "resolved path differs from the bound path" is the documented
                // outcome, not a signal of drift.
                //
                // IsBoundTo lexically canonicalizes both sides with Path.GetFullPath (see
                // ProjectPathNormalization) instead of comparing the caller's raw spelling.
                // It does not resolve filesystem identity aliases such as 8.3 short names,
                // junctions/symlinks, or UNC paths versus mapped drives.
                _logger?.LogWarning(
                    "TIA Openness worker: session is bound to '{BoundProjectPath}' but the worker reports it "
                    + "operated on '{ResolvedProjectPath}'.",
                    boundProjectPathBeforeCall,
                    result.ResolvedProjectPath);
                result = result with
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
        }

        return result.Success && string.IsNullOrEmpty(result.Payload)
            ? result with { Payload = emptyPayload }
            : result;
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

    private static string? TryReadProjectPath(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("projectPath", out var projectPath) &&
                projectPath.ValueKind == JsonValueKind.String)
            {
                return projectPath.GetString();
            }

            if (document.RootElement.TryGetProperty("project", out var project) &&
                project.ValueKind == JsonValueKind.Object &&
                project.TryGetProperty("path", out var statusPath) &&
                statusPath.ValueKind == JsonValueKind.String)
            {
                return statusPath.GetString();
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
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
