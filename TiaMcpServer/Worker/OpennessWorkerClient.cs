using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.Worker;

public class OpennessWorkerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly TimeSpan WorkerTimeout = TimeSpan.FromMinutes(5);

    private readonly ProjectSessionBinding _projectSessionBinding;

    public OpennessWorkerClient(ProjectSessionBinding projectSessionBinding)
    {
        _projectSessionBinding = projectSessionBinding;
    }

    public Task<string> BrowseProjectTreeAsync(string? projectPath)
    {
        return SendBoundProjectRequestAsync("browse_project_tree", projectPath, _ => { }, "[]");
    }

    public Task<string> ReadHardwareConfigAsync(string? projectPath)
    {
        return SendBoundProjectRequestAsync("read_hardware_config", projectPath, _ => { }, "{}");
    }

    public Task<string> SearchEquipmentCatalogAsync(string query, string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "search_equipment_catalog",
            projectPath,
            request => request.Query = query,
            "[]");
    }

    public Task<string> AddNetworkDeviceAsync(
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

    public Task<string> ConfigureNetworkDeviceAsync(
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

    public Task<string> ReadCrossReferencesAsync(string? projectPath, string? plcName, string? filter)
    {
        // Validate the filter before TryResolve so an invalid filter does not bind the session.
        if (!CrossReferenceFilterNames.TryNormalize(filter, out var normalizedFilter, out var filterError))
        {
            return Task.FromResult($"Error: {filterError}");
        }

        return SendBoundProjectRequestAsync(
            "read_cross_references",
            projectPath,
            request =>
            {
                request.PlcName = plcName;
                request.CrossReferenceFilter = normalizedFilter;
            },
            "{}");
    }

    public Task<string> GetBlockContentAsync(string blockPath, string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "get_block_content",
            projectPath,
            request => request.BlockPath = blockPath,
            string.Empty);
    }

    public Task<string> UpdateBlockLogicAsync(string blockPath, string yamlContent, string? projectPath)
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

    public Task<string> ListTagTablesAsync(string? plcName, string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "list_tag_tables",
            projectPath,
            request => request.PlcName = plcName,
            "[]");
    }

    public Task<string> CompileCheckAsync(string? blockPath, string? plcName, string? projectPath)
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

    public Task<string> CreateTagTableAsync(
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

    public Task<string> DeleteTagTableAsync(
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

    public Task<string> CreateTagAsync(
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

    public Task<string> UpdateTagAsync(
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

    public Task<string> DeleteTagAsync(
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

    public Task<string> CreateUserConstantAsync(
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

    public Task<string> UpdateUserConstantAsync(
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

    public Task<string> DeleteUserConstantAsync(
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

    public Task<string> CreateBlockAsync(
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

    public Task<string> DeleteBlockAsync(string blockPath, string? projectPath)
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

    public Task<string> CreateBlockGroupAsync(string blockPath, string? projectPath)
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

    public Task<string> DeleteBlockGroupAsync(string blockPath, string? projectPath)
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

    public Task<string> StartPlcAsync(string? plcName, string? projectPath)
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

    public Task<string> StopPlcAsync(string? plcName, string? projectPath)
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

    public Task<string> GetProjectStatusAsync(string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "get_project_status",
            projectPath,
            _ => { },
            "{}");
    }

    public async Task<string> OpenProjectAsync(string projectPath, bool forceRebind)
    {
        if (!CanBind(projectPath, forceRebind, out var bindingError))
        {
            return $"Error: {bindingError}";
        }

        try
        {
            var response = await SendAsync(
                new WorkerRequest
                {
                    Method = "open_project",
                    ProjectPath = projectPath,
                    Confirm = true,
                    ForceRebind = forceRebind,
                    AllowTiaConfirmations = true
                }).ConfigureAwait(false);

            if (!response.Success)
            {
                return FormatWorkerError(response);
            }

            if (!_projectSessionBinding.Bind(projectPath, forceRebind, out var bindError))
            {
                return $"Error: {bindError}";
            }

            return response.Payload ?? "{}";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or JsonException)
        {
            return $"Error: {ex.Message}";
        }
    }

    public async Task<string> CreateProjectAsync(
        string projectDirectory,
        string projectName,
        string? author,
        string? comment)
    {
        try
        {
            var response = await SendAsync(
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

            if (!response.Success)
            {
                return FormatWorkerError(response);
            }

            var projectPath = TryReadProjectPath(response.Payload);
            if (!string.IsNullOrWhiteSpace(projectPath))
            {
                _projectSessionBinding.Bind(projectPath!, forceRebind: true, out _);
            }

            return response.Payload ?? "{}";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or JsonException)
        {
            return $"Error: {ex.Message}";
        }
    }

    public Task<string> SaveProjectAsync(string? projectPath)
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

    public async Task<string> SaveProjectAsAsync(
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
            "{}").ConfigureAwait(false);

        if (rebind && !result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
        {
            var copiedProjectPath = TryReadProjectPath(result);
            if (!string.IsNullOrWhiteSpace(copiedProjectPath))
            {
                _projectSessionBinding.Bind(copiedProjectPath!, forceRebind: true, out _);
            }
        }

        return result;
    }

    public Task<string> ArchiveProjectAsync(
        string? projectPath,
        string archiveDirectory,
        string archiveName,
        string? mode,
        bool saveBeforeArchive)
    {
        if (!ArchiveModeNames.TryNormalize(mode, out var normalizedMode, out var modeError))
        {
            return Task.FromResult($"Error: {modeError}");
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

    public async Task<string> CloseProjectAsync(string? projectPath, bool saveBeforeClose)
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

        if (!result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase) &&
            _projectSessionBinding.Clear(projectPath, out _) is false)
        {
            _projectSessionBinding.Clear(null, out _);
        }

        return result;
    }

    private async Task<string> SendBoundProjectRequestAsync(
        string method,
        string? projectPath,
        Action<WorkerRequest> configure,
        string emptyPayload)
    {
        try
        {
            if (!_projectSessionBinding.TryResolve(projectPath, out var effectiveProjectPath, out var bindingError))
            {
                return $"Error: {bindingError}";
            }

            var request = new WorkerRequest
            {
                Method = method,
                ProjectPath = effectiveProjectPath
            };
            configure(request);

            var response = await SendAsync(request).ConfigureAwait(false);

            return response.Success
                ? response.Payload ?? emptyPayload
                : FormatWorkerError(response);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or JsonException)
        {
            return $"Error: {ex.Message}";
        }
    }

    private bool CanBind(string projectPath, bool forceRebind, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            error = "Project path is required.";
            return false;
        }

        var boundProjectPath = _projectSessionBinding.BoundProjectPath;
        if (boundProjectPath is null ||
            forceRebind ||
            string.Equals(boundProjectPath, projectPath.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        error = $"This MCP session is already bound to project '{boundProjectPath}' and cannot use '{projectPath}'. Start a new MCP session for a different TIA project or set forceRebind=true.";
        return false;
    }

    private static string FormatWorkerError(WorkerResponse response)
    {
        return $"Error: {response.Error ?? "The TIA Openness worker failed without an error message."}";
    }

    private static string? TryReadProjectPath(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

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

    // Siemens Openness is not safe for concurrent multi-process access to one TIA Portal
    // instance; serialize every worker invocation until the persistent-worker rework lands.
    private static readonly SemaphoreSlim WorkerGate = new(1, 1);

    private static async Task<WorkerResponse> SendAsync(WorkerRequest request)
    {
        await WorkerGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await SendUnguardedAsync(request).ConfigureAwait(false);
        }
        finally
        {
            WorkerGate.Release();
        }
    }

    private static async Task<WorkerResponse> SendUnguardedAsync(WorkerRequest request)
    {
        var workerPath = LocateWorkerExecutable();
        var startInfo = new ProcessStartInfo
        {
            FileName = workerPath,
            WorkingDirectory = Path.GetDirectoryName(workerPath) ?? AppContext.BaseDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Failed to start the TIA Openness worker process.");

        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions)).ConfigureAwait(false);
        process.StandardInput.Close();

        using var timeout = new CancellationTokenSource(WorkerTimeout);
        var responseLineTask = process.StandardOutput.ReadLineAsync();
        var completed = await Task.WhenAny(responseLineTask, Task.Delay(Timeout.InfiniteTimeSpan, timeout.Token))
            .ConfigureAwait(false);

        if (completed != responseLineTask)
        {
            TryKill(process);
            throw new TimeoutException($"TIA Openness worker did not respond within {WorkerTimeout.TotalMinutes:N0} minutes.");
        }

        var responseLine = await responseLineTask.ConfigureAwait(false);
        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(responseLine))
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? "No response was written." : stderr.Trim();
            throw new InvalidOperationException($"TIA Openness worker exited without a response. {detail}");
        }

        var response = JsonSerializer.Deserialize<WorkerResponse>(responseLine, JsonOptions);
        return response ?? throw new InvalidOperationException("TIA Openness worker returned an empty response.");
    }

    private static string LocateWorkerExecutable()
    {
        var packagedPath = Path.Combine(AppContext.BaseDirectory, "openness-worker", "TiaMcpServer.OpennessWorker.exe");
        if (File.Exists(packagedPath))
        {
            return packagedPath;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            foreach (var configuration in new[] { "Debug", "Release" })
            {
                var candidatePath = Path.Combine(
                    directory.FullName,
                    "TiaMcpServer.OpennessWorker",
                    "bin",
                    configuration,
                    "net48",
                    "TiaMcpServer.OpennessWorker.exe");

                if (File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "TIA Openness worker executable was not found. Build the solution and ensure the openness-worker folder is beside the MCP server executable.",
            packagedPath);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
