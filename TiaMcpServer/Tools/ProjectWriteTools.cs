using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tools;

/// <summary>
/// Write-only project lifecycle tools. Only exposed in read-write mode.
/// Every write tool is self-previewing: call without safetyToken for a preview + token,
/// then call again with confirm=true and the token to apply.
/// </summary>
[McpServerToolType]
public class ProjectWriteTools
{
    private const string SafetyFlowDescription =
        "Two-step safety flow in one tool: call WITHOUT safetyToken to get a preview and a single-use token "
        + "(expires after 10 minutes), review it, then call again with the same arguments plus confirm=true and the safetyToken.";

    [McpServerTool(
        Name = "open_project",
        ReadOnly = false,
        Destructive = true,
        OpenWorld = false)]
    [Description("Open a TIA Portal project and bind this MCP session to it. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
    public static async Task<string> OpenProject(OpennessWorkerClient workerClient, WriteSafetyService safety, [Description("Path to the .ap21 project file to open.")] string projectPath, [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false, [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null, [Description("Set true to allow rebinding this MCP session from a previously bound project.")] bool forceRebind = false)
    {
        var requestedInput = new { projectPath, forceRebind };
        if (string.IsNullOrWhiteSpace(safetyToken))
        {
            // Reject impossible requests before recovery or configured-source verification can
            // contact the worker or change the binding. The pinned check below still protects
            // against a binding change between this precheck and token creation.
            if (workerClient is not null)
            {
                var initialBindingCheck = workerClient.CheckOpenProjectBinding(projectPath, forceRebind);
                if (!initialBindingCheck.Success)
                {
                    return WriteSafetyTooling.BuildApplyResult("open_project", initialBindingCheck);
                }
            }

            ProjectBindingSnapshot? recoveredBinding = null;
            if (workerClient is not null &&
                workerClient.BindingSnapshot.State == ProjectBindingSnapshot.InvalidatedState)
            {
                var recovery = await workerClient.RegroundInvalidatedSourceForOpenAsync(forceRebind)
                    .ConfigureAwait(false);
                if (!recovery.Success)
                {
                    return WriteSafetyTooling.BuildApplyResult("open_project", recovery.Failure!);
                }

                // Pin the exact promoted revision returned by recovery, not a later recapture.
                recoveredBinding = recovery.Value!;
            }

            // A configured path is only a caller assertion. Ground it before pinning the
            // preview lease; otherwise the status promotion would change the pinned binding.
            else if (workerClient is not null &&
                workerClient.BindingSnapshot.State == ProjectBindingSnapshot.ConfiguredUnverifiedState)
            {
                var configuredPath = workerClient.BindingSnapshot.ProjectPath;
                var verification = await workerClient.GetProjectStatusAsync(configuredPath).ConfigureAwait(false);
                if (!verification.Success)
                {
                    return WriteSafetyTooling.BuildApplyResult("open_project", verification);
                }

                if (!workerClient.BindingSnapshot.IsVerified)
                {
                    return WriteSafetyTooling.BuildApplyResult("open_project", WorkerCallResult.Fail(
                        WorkerFailureCategories.BindingConflict,
                        "The configured source project could not be verified for rebind preview."));
                }
            }

            return await CreatePinnedPreviewAsync(workerClient!, "open_project", async () =>
            {
                if (workerClient is not null)
                {
                    var bindingCheck = workerClient.CheckOpenProjectBinding(projectPath, forceRebind);
                    if (!bindingCheck.Success)
                    {
                        return WriteSafetyTooling.BuildApplyResult("open_project", bindingCheck);
                    }
                }

                var currentState = await ReadOpenProjectCurrentStateAsync(
                    workerClient!, projectPath, forceRebind).ConfigureAwait(false);
                if (currentState.Success && WouldCloseModifiedSource(currentState.Payload))
                {
                    return WriteSafetyTooling.BuildApplyResult("open_project", WorkerCallResult.Fail(
                        WorkerFailureCategories.ValidationError,
                        "The worker-owned source project has unsaved changes and would be closed by this rebind. Save or close it explicitly before previewing again."));
                }

                var sourceProjectPath = workerClient is null
                    ? null
                    : ProjectPathNormalization.Canonicalize(workerClient.BindingSnapshot.ProjectPath);
                var destinationProjectPath = ProjectPathNormalization.Canonicalize(projectPath) ?? projectPath;
                var previewTarget = new { projectPath, sourceProjectPath, destinationProjectPath };
                return WriteSafetyTooling.CreatePreview(
                    safety, "open_project", projectPath, previewTarget,
                    DescribeOpenProjectPreview(sourceProjectPath, projectPath, currentState), requestedInput,
                    currentState, diff: null, instructions: ApplyInstructions("open_project"));
            }, recoveredBinding).ConfigureAwait(false);
        }
        if (!confirm) return ConfirmRequired("open_project");
        var applySourceProjectPath = ProjectPathNormalization.Canonicalize(workerClient?.BindingSnapshot.ProjectPath);
        var applyDestinationProjectPath = ProjectPathNormalization.Canonicalize(projectPath) ?? projectPath;
        var target = new { projectPath, sourceProjectPath = applySourceProjectPath, destinationProjectPath = applyDestinationProjectPath };
        var apply = await WriteSafetyTooling.ValidateAndExecuteForApplyAsync(workerClient!, safety, safetyToken, PreviewHint("open_project"), "open_project", projectPath, target, requestedInput, () => ReadOpenProjectCurrentStateAsync(workerClient!, projectPath, forceRebind), () => workerClient!.OpenProjectAsync(projectPath, forceRebind), async (context, operationResult) =>
        {
            var verification = operationResult.Success ? (await workerClient!.GetBasicProjectStatusAsync(projectPath).ConfigureAwait(false)).ToText() : null;
            safety.AppendAudit("open_project", projectPath, target, requestedInput, context.CurrentState, operationResult.ToText());
            return verification;
        }).ConfigureAwait(false);
        var safetyContext = apply.SafetyContext;
        if (!safetyContext.IsValid) return SafetyFailure("open_project", safetyContext);
        var result = apply.OperationResult!;
        return WriteSafetyTooling.BuildApplyResult("open_project", result, "get_project_status", apply.VerificationResult);
    }

    private static string DescribeOpenProjectPreview(
        string? sourceProjectPath,
        string destinationProjectPath,
        WorkerCallResult currentState)
    {
        if (sourceProjectPath is null)
        {
            return $"No source project is bound. Open and bind destination project '{destinationProjectPath}'.";
        }

        if (string.Equals(sourceProjectPath, destinationProjectPath, StringComparison.OrdinalIgnoreCase))
        {
            return $"Project '{sourceProjectPath}' is already bound; it will remain open.";
        }

        if (!currentState.Success)
        {
            return $"Open destination project '{destinationProjectPath}' from source '{sourceProjectPath}'.";
        }

        using var document = JsonDocument.Parse(currentState.Payload);
        var state = document.RootElement;
        var source = state.GetProperty("sourceProjectPath").GetString();
        var destination = state.GetProperty("destinationProjectPath").GetString();
        var willCloseSource = state.GetProperty("willCloseSource").GetBoolean();
        return willCloseSource
            ? $"Open destination project '{destination}' and close worker-owned source project '{source}'."
            : $"Open destination project '{destination}'; source project '{source}' will remain open.";
    }

    private static async Task<WorkerCallResult> ReadOpenProjectCurrentStateAsync(
        OpennessWorkerClient workerClient,
        string projectPath,
        bool forceRebind)
    {
        var destinationState = WriteSafetyTooling.DescribePathState(projectPath);
        if (workerClient is null)
        {
            // Pure filesystem preview tests have no worker and therefore no source binding.
            return WorkerCallResult.Ok(destinationState);
        }

        var binding = workerClient.BindingSnapshot;
        if (binding.State == ProjectBindingSnapshot.ConfiguredUnverifiedState ||
            binding.State == ProjectBindingSnapshot.InvalidatedState)
        {
            return WorkerCallResult.Fail(
                WorkerFailureCategories.BindingConflict,
                "The source project binding is not verified for rebind preview.");
        }

        var source = ProjectPathNormalization.Canonicalize(binding.ProjectPath);
        var destination = ProjectPathNormalization.Canonicalize(projectPath);
        if (destination is null)
        {
            return WorkerCallResult.Fail(
                WorkerFailureCategories.ValidationError,
                "A destination project path is required for open_project preview.");
        }

        if (!binding.IsVerified)
        {
            return WorkerCallResult.Ok(destinationState);
        }

        if (source is null)
        {
            return WorkerCallResult.Fail(
                WorkerFailureCategories.BindingConflict,
                "The verified source project path is unavailable for rebind preview.");
        }

        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
        {
            return WorkerCallResult.Ok(destinationState);
        }

        var probe = await workerClient.ProbeOpenProjectRebindAsync(source, destination).ConfigureAwait(false);
        if (!probe.Success)
        {
            return probe;
        }

        ProjectRebindStateInfo state;
        try
        {
            state = ProjectRebindStatePayloadContract.Decode(probe.Payload, source, destination);
        }
        catch (JsonException)
        {
            return WorkerCallResult.Fail(
                WorkerFailureCategories.ProtocolError,
                "The rebind-state probe did not match the pinned source and destination.");
        }

        using var destinationDocument = JsonDocument.Parse(destinationState);
        var combinedState = JsonSerializer.Serialize(new
        {
            destination = destinationDocument.RootElement,
            sourceProjectPath = state.SourceProjectPath,
            destinationProjectPath = state.DestinationProjectPath,
            sourceIsModified = state.SourceIsModified,
            sourceOpenedByWorker = state.SourceOpenedByWorker,
            willCloseSource = state.WillCloseSource
        });
        return WorkerCallResult.Ok(combinedState, probe.Warnings);
    }

    private static bool WouldCloseModifiedSource(string currentState)
    {
        using var document = JsonDocument.Parse(currentState);
        var root = document.RootElement;
        return root.TryGetProperty("willCloseSource", out var willClose) &&
            willClose.ValueKind == JsonValueKind.True &&
            root.TryGetProperty("sourceIsModified", out var modified) &&
            modified.ValueKind == JsonValueKind.True;
    }

    [McpServerTool(
        Name = "create_project",
        ReadOnly = false,
        Destructive = true,
        OpenWorld = false)]
    [Description("Create a new TIA Portal project and bind this MCP session to it. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
    public static async Task<string> CreateProject(OpennessWorkerClient workerClient, WriteSafetyService safety, [Description("Directory where the project folder should be created.")] string projectDirectory, [Description("Name of the new TIA Portal project.")] string projectName, [Description("Optional project author metadata.")] string? author = null, [Description("Optional project comment metadata.")] string? comment = null, [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false, [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null)
    {
        var target = new { projectDirectory, projectName };
        var requestedInput = new { projectDirectory, projectName, author, comment };
        if (string.IsNullOrWhiteSpace(safetyToken)) return await CreatePinnedPreviewAsync(workerClient, "create_project", () => Task.FromResult(WriteSafetyTooling.CreatePreview(safety, "create_project", null, target, $"Create TIA Portal project '{projectName}' in '{projectDirectory}'.", requestedInput, WorkerCallResult.Ok(WriteSafetyTooling.DescribeProjectCreationState(projectDirectory, projectName)), diff: null, instructions: ApplyInstructions("create_project")))).ConfigureAwait(false);
        if (!confirm) return ConfirmRequired("create_project");
        var apply = await WriteSafetyTooling.ValidateAndExecuteForApplyAsync(workerClient, safety, safetyToken, PreviewHint("create_project"), "create_project", null, target, requestedInput, () => Task.FromResult(WorkerCallResult.Ok(WriteSafetyTooling.DescribeProjectCreationState(projectDirectory, projectName))), () => workerClient.CreateProjectAsync(projectDirectory, projectName, author, comment), async (context, operationResult) =>
        {
            var verification = operationResult.Success ? (await workerClient.GetBasicProjectStatusAsync(null).ConfigureAwait(false)).ToText() : null;
            safety.AppendAudit("create_project", null, target, requestedInput, context.CurrentState, operationResult.ToText());
            return verification;
        }).ConfigureAwait(false);
        var safetyContext = apply.SafetyContext;
        if (!safetyContext.IsValid) return SafetyFailure("create_project", safetyContext);
        var result = apply.OperationResult!;
        return WriteSafetyTooling.BuildApplyResult("create_project", result, "get_project_status", apply.VerificationResult);
    }

    [McpServerTool(
        Name = "save_project",
        ReadOnly = false,
        Destructive = true,
        OpenWorld = false)]
    [Description("Save the active TIA Portal project. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
    public static async Task<string> SaveProject(OpennessWorkerClient workerClient, WriteSafetyService safety, [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null, [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false, [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null)
    {
        var requestedInput = new { projectPath };
        if (!string.IsNullOrWhiteSpace(safetyToken) && !confirm) return ConfirmRequired("save_project");
        var bindingGate = await workerClient.RequireVerifiedWriteBindingAsync(projectPath).ConfigureAwait(false);
        if (!bindingGate.Success) return WriteSafetyTooling.BuildApplyResult("save_project", bindingGate);
        if (string.IsNullOrWhiteSpace(safetyToken)) return await CreatePinnedPreviewAsync(workerClient, "save_project", async () =>
        {
            var displayedPath = ResolveDisplayedProjectPath(workerClient, projectPath);
            var previewTarget = new { projectPath = displayedPath };
            return WriteSafetyTooling.CreatePreview(safety, "save_project", projectPath, previewTarget,
                $"Save project '{displayedPath}'.", requestedInput,
                await workerClient.ProbeProjectStatusForLifecycleAsync(projectPath).ConfigureAwait(false),
                diff: null, instructions: ApplyInstructions("save_project"));
        }).ConfigureAwait(false);
        var target = new { projectPath = ResolveDisplayedProjectPath(workerClient, projectPath) };
        var apply = await WriteSafetyTooling.ValidateAndExecuteForApplyAsync(workerClient, safety, safetyToken, PreviewHint("save_project"), "save_project", projectPath, target, requestedInput, () => workerClient.ProbeProjectStatusForLifecycleAsync(projectPath), () => workerClient.SaveProjectAsync(projectPath), async (context, operationResult) =>
        {
            var verification = operationResult.Success ? (await workerClient.GetBasicProjectStatusAsync(projectPath).ConfigureAwait(false)).ToText() : null;
            safety.AppendAudit("save_project", projectPath, target, requestedInput, context.CurrentState, operationResult.ToText());
            return verification;
        }).ConfigureAwait(false);
        var safetyContext = apply.SafetyContext;
        if (!safetyContext.IsValid) return SafetyFailure("save_project", safetyContext);
        var result = apply.OperationResult!;
        return WriteSafetyTooling.BuildApplyResult("save_project", result, "get_project_status", apply.VerificationResult);
    }

    [McpServerTool(
        Name = "save_project_as",
        ReadOnly = false,
        Destructive = true,
        OpenWorld = false)]
    [Description("Save the active TIA Portal project to a copy directory. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
    public static async Task<string> SaveProjectAs(OpennessWorkerClient workerClient, WriteSafetyService safety, [Description("Parent directory for the copied project.")] string targetDirectory, [Description("Name of the copied project directory.")] string targetName, [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null, [Description("Bind this MCP session to the copied project after save-as. Must be true; rebind=false is not supported.")] bool rebind = true, [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false, [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null)
    {
        if (!rebind)
        {
            return WriteSafetyTooling.BuildApplyResult(
                "save_project_as",
                WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, OpennessWorkerClient.RebindFalseUnsupportedMessage));
        }

        var requestedInput = new { projectPath, targetDirectory, targetName, rebind };
        if (!string.IsNullOrWhiteSpace(safetyToken) && !confirm) return ConfirmRequired("save_project_as");
        var bindingGate = await workerClient.RequireVerifiedWriteBindingAsync(projectPath).ConfigureAwait(false);
        if (!bindingGate.Success) return WriteSafetyTooling.BuildApplyResult("save_project_as", bindingGate);
        if (string.IsNullOrWhiteSpace(safetyToken)) return await CreatePinnedPreviewAsync(workerClient, "save_project_as", async () =>
        {
            var displayedPath = ResolveDisplayedProjectPath(workerClient, projectPath);
            var previewTarget = new { projectPath = displayedPath, targetDirectory, targetName };
            return WriteSafetyTooling.CreatePreview(safety, "save_project_as", projectPath, previewTarget,
                $"Save source project '{displayedPath}' as '{targetName}' in '{targetDirectory}' and rebind to the copy.",
                requestedInput, await workerClient.ProbeProjectStatusForLifecycleAsync(projectPath).ConfigureAwait(false),
                diff: null, instructions: ApplyInstructions("save_project_as"));
        }).ConfigureAwait(false);
        var target = new { projectPath = ResolveDisplayedProjectPath(workerClient, projectPath), targetDirectory, targetName };
        var apply = await WriteSafetyTooling.ValidateAndExecuteForApplyAsync(workerClient, safety, safetyToken, PreviewHint("save_project_as"), "save_project_as", projectPath, target, requestedInput, () => workerClient.ProbeProjectStatusForLifecycleAsync(projectPath), () => workerClient.SaveProjectAsAsync(projectPath, targetDirectory, targetName, rebind), async (context, operationResult) =>
        {
            var verification = operationResult.Success ? (await workerClient.GetBasicProjectStatusAsync(null).ConfigureAwait(false)).ToText() : null;
            safety.AppendAudit("save_project_as", projectPath, target, requestedInput, context.CurrentState, operationResult.ToText());
            return verification;
        }).ConfigureAwait(false);
        var safetyContext = apply.SafetyContext;
        if (!safetyContext.IsValid) return SafetyFailure("save_project_as", safetyContext);
        var result = apply.OperationResult!;
        return WriteSafetyTooling.BuildApplyResult("save_project_as", result, "get_project_status", apply.VerificationResult);
    }

    [McpServerTool(
        Name = "archive_project",
        ReadOnly = false,
        Destructive = true,
        OpenWorld = false)]
    [Description("Archive the active TIA Portal project. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
    public static async Task<string> ArchiveProject(OpennessWorkerClient workerClient, WriteSafetyService safety, [Description("Directory where the archive should be written. TIA Portal rejects a combined directory+file path longer than roughly 140 characters.")] string archiveDirectory, [Description("Archive file name. For Compressed and DiscardRestorableDataAndCompressed modes, .zap21 is appended automatically if not already present.")] string archiveName, [Description("Archive mode: None, DiscardRestorableData, Compressed, or DiscardRestorableDataAndCompressed.")] string? mode = null, [Description("Save the project before archiving.")] bool saveBeforeArchive = true, [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null, [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false, [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null)
    {
        var resolvedArchiveName = ArchiveModeNames.TryNormalize(mode, out var normalizedMode, out _)
            ? ArchiveModeNames.EnsureArchiveExtension(archiveName, normalizedMode)
            : archiveName;
        var requestedInput = new { projectPath, archiveDirectory, archiveName, mode, saveBeforeArchive };
        if (!string.IsNullOrWhiteSpace(safetyToken) && !confirm) return ConfirmRequired("archive_project");
        var bindingGate = await workerClient.RequireVerifiedWriteBindingAsync(projectPath).ConfigureAwait(false);
        if (!bindingGate.Success) return WriteSafetyTooling.BuildApplyResult("archive_project", bindingGate);
        if (string.IsNullOrWhiteSpace(safetyToken)) return await CreatePinnedPreviewAsync(workerClient, "archive_project", async () =>
        {
            var displayedPath = ResolveDisplayedProjectPath(workerClient, projectPath);
            var previewTarget = new { projectPath = displayedPath, archiveDirectory, archiveName = resolvedArchiveName, saveBeforeArchive };
            var summary = saveBeforeArchive
                ? $"Save project '{displayedPath}', then archive it to '{archiveDirectory}\\{resolvedArchiveName}'."
                : $"Archive project '{displayedPath}' to '{archiveDirectory}\\{resolvedArchiveName}' without saving first.";
            return WriteSafetyTooling.CreatePreview(safety, "archive_project", projectPath, previewTarget, summary,
                requestedInput, RejectIfArchiveDirectoryWithinProjectFolder(
                    await workerClient.ProbeProjectStatusForLifecycleAsync(projectPath).ConfigureAwait(false), archiveDirectory),
                diff: null, instructions: ApplyInstructions("archive_project"));
        }).ConfigureAwait(false);
        var target = new { projectPath = ResolveDisplayedProjectPath(workerClient, projectPath), archiveDirectory, archiveName = resolvedArchiveName, saveBeforeArchive };
        var apply = await WriteSafetyTooling.ValidateAndExecuteForApplyAsync(workerClient, safety, safetyToken, PreviewHint("archive_project"), "archive_project", projectPath, target, requestedInput, async () => RejectIfArchiveDirectoryWithinProjectFolder(await workerClient.ProbeProjectStatusForLifecycleAsync(projectPath).ConfigureAwait(false), archiveDirectory), () => workerClient.ArchiveProjectAsync(projectPath, archiveDirectory, archiveName, mode, saveBeforeArchive), async (context, operationResult) =>
        {
            var verification = operationResult.Success ? (await workerClient.GetBasicProjectStatusAsync(projectPath).ConfigureAwait(false)).ToText() : null;
            safety.AppendAudit("archive_project", projectPath, target, requestedInput, context.CurrentState, operationResult.ToText());
            return verification;
        }).ConfigureAwait(false);
        var safetyContext = apply.SafetyContext;
        if (!safetyContext.IsValid) return SafetyFailure("archive_project", safetyContext);
        var result = apply.OperationResult!;
        return WriteSafetyTooling.BuildApplyResult("archive_project", result, "get_project_status", apply.VerificationResult);
    }

    [McpServerTool(
        Name = "close_project",
        ReadOnly = false,
        Destructive = true,
        OpenWorld = false)]
    [Description("Close the active TIA Portal project and clear this MCP session binding. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
    public static async Task<string> CloseProject(OpennessWorkerClient workerClient, WriteSafetyService safety, [Description("Optional path to a .ap21 project file. If omitted, closes the currently bound/open project.")] string? projectPath = null, [Description("Save the project before closing it.")] bool saveBeforeClose = true, [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false, [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null)
    {
        var requestedInput = new { projectPath, saveBeforeClose };
        if (!string.IsNullOrWhiteSpace(safetyToken) && !confirm) return ConfirmRequired("close_project");
        var bindingGate = await workerClient.RequireVerifiedWriteBindingAsync(projectPath).ConfigureAwait(false);
        if (!bindingGate.Success) return WriteSafetyTooling.BuildApplyResult("close_project", bindingGate);
        if (string.IsNullOrWhiteSpace(safetyToken)) return await CreatePinnedPreviewAsync(workerClient, "close_project", async () =>
        {
            var displayedPath = ResolveDisplayedProjectPath(workerClient, projectPath);
            var previewTarget = new { projectPath = displayedPath, saveBeforeClose };
            var summary = saveBeforeClose
                ? $"Save project '{displayedPath}', then close it."
                : $"Close project '{displayedPath}' without saving.";
            return WriteSafetyTooling.CreatePreview(safety, "close_project", projectPath, previewTarget, summary,
                requestedInput, await workerClient.ProbeProjectStatusForLifecycleAsync(projectPath).ConfigureAwait(false),
                diff: null, instructions: ApplyInstructions("close_project"));
        }).ConfigureAwait(false);
        var target = new { projectPath = ResolveDisplayedProjectPath(workerClient, projectPath), saveBeforeClose };
        var apply = await WriteSafetyTooling.ValidateAndExecuteForApplyAsync(workerClient, safety, safetyToken, PreviewHint("close_project"), "close_project", projectPath, target, requestedInput, () => workerClient.ProbeProjectStatusForLifecycleAsync(projectPath), () => workerClient.CloseProjectAsync(projectPath, saveBeforeClose), (context, operationResult) =>
        {
            safety.AppendAudit("close_project", projectPath, target, requestedInput, context.CurrentState, operationResult.ToText());
            return Task.FromResult<string?>(null);
        }).ConfigureAwait(false);
        var safetyContext = apply.SafetyContext;
        if (!safetyContext.IsValid) return SafetyFailure("close_project", safetyContext);
        var result = apply.OperationResult!;
        return WriteSafetyTooling.BuildApplyResult("close_project", result, "get_project_status", null);
    }

    private static string? ResolveDisplayedProjectPath(OpennessWorkerClient workerClient, string? requestedProjectPath)
        => requestedProjectPath ?? workerClient.BindingSnapshot.ProjectPath;

    private static WorkerCallResult RejectIfArchiveDirectoryWithinProjectFolder(WorkerCallResult probe, string archiveDirectory)
    {
        if (!probe.Success)
        {
            return probe;
        }

        if (ArchiveDirectoryGuard.IsWithinProjectFolder(archiveDirectory, probe.ResolvedProjectPath ?? string.Empty))
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, ArchiveDirectoryGuard.BuildRejectionMessage(archiveDirectory));
        }

        if (!Directory.Exists(archiveDirectory))
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, "The archive directory must already exist.");
        }

        return probe;
    }

    private static string ApplyInstructions(string toolName) => $"Preview only — nothing was changed. To apply, call {toolName} again with the same arguments plus confirm=true and this safetyToken.";

    private static string ConfirmRequired(string toolName) => WriteSafetyTooling.BuildApplyResult(
        toolName,
        WorkerCallResult.Fail(
            WorkerFailureCategories.ValidationError,
            $"Safety token provided but confirm=false. Set confirm=true and resend the safetyToken to apply, or call {toolName} without safetyToken for a fresh preview."));

    private static string SafetyFailure(string toolName, WriteSafetyApplyContext safetyContext) => WriteSafetyTooling.BuildApplyResult(
        toolName,
        WorkerCallResult.Fail(
            safetyContext.FailureCategory ?? WorkerFailureCategories.ValidationError,
            safetyContext.Error ?? "Safety validation failed."));

    private static async Task<string> CreatePinnedPreviewAsync(
        OpennessWorkerClient workerClient,
        string toolName,
        Func<Task<string>> createPreview,
        ProjectBindingSnapshot? expectedBinding = null)
    {
        // Pure open/create preview tests intentionally pass no worker client: those previews only
        // describe filesystem state and cannot touch Siemens. Runtime DI always supplies a client;
        // every worker-backed preview reaches the pinned branch below.
        if (workerClient is null)
        {
            return await createPreview().ConfigureAwait(false);
        }

        var execution = await workerClient.ExecuteWithPinnedBindingAsync(
            expectedBinding ?? workerClient.BindingSnapshot,
            createPreview).ConfigureAwait(false);
        return execution.Success
            ? execution.Value!
            : WriteSafetyTooling.BuildApplyResult(toolName, execution.Failure!);
    }

    private static string PreviewHint(string toolName) => $"{toolName} (without safetyToken)";
}
