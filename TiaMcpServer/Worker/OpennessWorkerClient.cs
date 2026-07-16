using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<OpennessWorkerClient>? _logger;
    private readonly string? _workerExecutablePathOverride;

    public OpennessWorkerClient(
        ProjectSessionBinding projectSessionBinding,
        ILogger<OpennessWorkerClient>? logger = null,
        string? workerExecutablePath = null)
    {
        _projectSessionBinding = projectSessionBinding;
        _logger = logger;
        _workerExecutablePathOverride = workerExecutablePath;
    }

    public Task<WorkerCallResult> BrowseProjectTreeAsync(string? projectPath)
    {
        return SendBoundProjectRequestAsync("browse_project_tree", projectPath, _ => { }, "[]");
    }

    public Task<WorkerCallResult> ReadHardwareConfigAsync(string? projectPath)
    {
        return SendBoundProjectRequestAsync("read_hardware_config", projectPath, _ => { }, "{}");
    }

    public Task<WorkerCallResult> SearchEquipmentCatalogAsync(string query, string? projectPath)
    {
        return SendBoundProjectRequestAsync(
            "search_equipment_catalog",
            projectPath,
            request => request.Query = query,
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

    public Task<WorkerCallResult> ReadCrossReferencesAsync(string? projectPath, string? plcName, string? filter)
    {
        // Validate the filter before TryResolve so an invalid filter does not bind the session.
        if (!CrossReferenceFilterNames.TryNormalize(filter, out var normalizedFilter, out var filterError))
        {
            return Task.FromResult(WorkerCallResult.Fail(filterError!));
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
        if (!CanBind(projectPath, forceRebind, out var bindingError))
        {
            return WorkerCallResult.Fail(bindingError!);
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
            return WorkerCallResult.Fail(bindError!, result.Warnings);
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
            "{}").ConfigureAwait(false);

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
            return Task.FromResult(WorkerCallResult.Fail(modeError!));
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
        string emptyPayload)
    {
        if (!_projectSessionBinding.TryResolve(projectPath, out var effectiveProjectPath, out var bindingError))
        {
            return WorkerCallResult.Fail(bindingError!);
        }

        var request = new WorkerRequest
        {
            Method = method,
            ProjectPath = effectiveProjectPath
        };
        configure(request);

        var result = await InvokeWorkerAsync(request).ConfigureAwait(false);
        return result.Success && string.IsNullOrEmpty(result.Payload)
            ? result with { Payload = emptyPayload }
            : result;
    }

    private async Task<WorkerCallResult> InvokeWorkerAsync(WorkerRequest request)
    {
        try
        {
            var (response, stderrWarnings) = await SendAsync(request).ConfigureAwait(false);
            foreach (var warning in stderrWarnings)
            {
                _logger?.LogWarning("TIA Openness worker stderr: {Line}", warning);
            }

            return response.Success
                ? WorkerCallResult.Ok(response.Payload ?? string.Empty, stderrWarnings)
                : WorkerCallResult.Fail(
                    response.Error ?? "The TIA Openness worker failed without an error message.",
                    stderrWarnings);
        }
        catch (Win32Exception ex)
        {
            return WorkerCallResult.Fail(
                $"Failed to launch the TIA Openness worker process ({ex.Message}). "
                + "Verify that .NET Framework 4.8 is installed and that the 'openness-worker' folder "
                + "beside the MCP server executable is complete; rebuild or reinstall if files are missing.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or JsonException)
        {
            return WorkerCallResult.Fail(ex.Message);
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

    // Siemens Openness is not safe for concurrent multi-process access to one TIA Portal
    // instance; serialize every worker invocation until the persistent-worker rework lands.
    private static readonly SemaphoreSlim WorkerGate = new(1, 1);

    private async Task<(WorkerResponse Response, IReadOnlyList<string> StderrLines)> SendAsync(WorkerRequest request)
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

    private async Task<(WorkerResponse Response, IReadOnlyList<string> StderrLines)> SendUnguardedAsync(WorkerRequest request)
    {
        var workerPath = _workerExecutablePathOverride ?? LocateWorkerExecutable();
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
        var stderrLines = SplitStderrLines(stderr);

        if (string.IsNullOrWhiteSpace(responseLine))
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? "No response was written." : stderr.Trim();
            throw new InvalidOperationException($"TIA Openness worker exited without a response. {detail}");
        }

        var response = JsonSerializer.Deserialize<WorkerResponse>(responseLine, JsonOptions);
        return (response ?? throw new InvalidOperationException("TIA Openness worker returned an empty response."), stderrLines);
    }

    // A degraded read of a large project can emit hundreds of "Skipping X" lines; cap what
    // reaches the agent so warnings cannot flood a small model's context.
    private const int MaxStderrWarningLines = 20;

    private static IReadOnlyList<string> SplitStderrLines(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return Array.Empty<string>();
        }

        var lines = stderr.Replace("\r\n", "\n").Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        if (lines.Count > MaxStderrWarningLines)
        {
            var dropped = lines.Count - MaxStderrWarningLines;
            lines = lines.Take(MaxStderrWarningLines).ToList();
            lines.Add($"(+{dropped} more worker warnings truncated)");
        }

        return lines;
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
