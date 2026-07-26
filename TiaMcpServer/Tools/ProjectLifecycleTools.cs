using System.ComponentModel;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tools
{
    /// <summary>
    /// Project lifecycle tools. Every write tool is self-previewing: call it without a
    /// safetyToken to get a preview plus a single-use token, then call it again with the
    /// same arguments plus confirm=true and the token to apply. target/requestedInput are
    /// built exactly once per method so preview and apply can never drift apart.
    ///
    /// NOTE: This class is no longer registered as an MCP tool type. Tools have been split
    /// into ProjectReadTools and ProjectWriteTools. This class is kept for test backward
    /// compatibility (tests reference its methods directly).
    /// </summary>
    public static class ProjectLifecycleTools
    {
        private const string SafetyFlowDescription =
            "Two-step safety flow in one tool: call WITHOUT safetyToken to get a preview and a single-use token "
            + "(expires after 10 minutes), review it, then call again with the same arguments plus confirm=true and the safetyToken.";

        [Description("Get status and metadata for the active TIA Portal project.")]
        public static async Task<string> GetProjectStatus(OpennessWorkerClient workerClient, [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
            => (await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false)).ToEnvelopeText();

        [Description("Open a TIA Portal project and bind this MCP session to it. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
        public static async Task<string> OpenProject(OpennessWorkerClient workerClient, WriteSafetyService safety, [Description("Path to the .ap21 project file to open.")] string projectPath, [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false, [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null, [Description("Set true to allow rebinding this MCP session from a previously bound project.")] bool forceRebind = false)
        {
            var target = new { projectPath };
            var requestedInput = new { projectPath, forceRebind };
            if (string.IsNullOrWhiteSpace(safetyToken)) return WriteSafetyTooling.CreatePreview(safety, "open_project", projectPath, target, $"Open and bind TIA Portal project '{projectPath}'.", requestedInput, WorkerCallResult.Ok(WriteSafetyTooling.DescribePathState(projectPath)), diff: null, instructions: ApplyInstructions("open_project"));
            if (!confirm) return ConfirmRequired("open_project");
            var safetyContext = await WriteSafetyTooling.ValidateForApplyAsync(safety, safetyToken, PreviewHint("open_project"), "open_project", projectPath, target, requestedInput, () => Task.FromResult(WorkerCallResult.Ok(WriteSafetyTooling.DescribePathState(projectPath)))).ConfigureAwait(false);
            if (!safetyContext.IsValid) return SafetyFailure("open_project", safetyContext);
            var result = await workerClient.OpenProjectAsync(projectPath, forceRebind).ConfigureAwait(false);
            var status = result.Success ? (await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false)).ToText() : null;
            safety.AppendAudit("open_project", projectPath, target, requestedInput, safetyContext.CurrentState, result.ToText());
            return WriteSafetyTooling.BuildApplyResult("open_project", result, "get_project_status", status);
        }

        [Description("Create a new TIA Portal project and bind this MCP session to it. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
        public static async Task<string> CreateProject(OpennessWorkerClient workerClient, WriteSafetyService safety, [Description("Directory where the project folder should be created.")] string projectDirectory, [Description("Name of the new TIA Portal project.")] string projectName, [Description("Optional project author metadata.")] string? author = null, [Description("Optional project comment metadata.")] string? comment = null, [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false, [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null)
        {
            var target = new { projectDirectory, projectName };
            var requestedInput = new { projectDirectory, projectName, author, comment };
            if (string.IsNullOrWhiteSpace(safetyToken)) return WriteSafetyTooling.CreatePreview(safety, "create_project", null, target, $"Create TIA Portal project '{projectName}' in '{projectDirectory}'.", requestedInput, WorkerCallResult.Ok(WriteSafetyTooling.DescribeProjectCreationState(projectDirectory, projectName)), diff: null, instructions: ApplyInstructions("create_project"));
            if (!confirm) return ConfirmRequired("create_project");
            var safetyContext = await WriteSafetyTooling.ValidateForApplyAsync(safety, safetyToken, PreviewHint("create_project"), "create_project", null, target, requestedInput, () => Task.FromResult(WorkerCallResult.Ok(WriteSafetyTooling.DescribeProjectCreationState(projectDirectory, projectName)))).ConfigureAwait(false);
            if (!safetyContext.IsValid) return SafetyFailure("create_project", safetyContext);
            var result = await workerClient.CreateProjectAsync(projectDirectory, projectName, author, comment).ConfigureAwait(false);
            var status = result.Success ? (await workerClient.GetProjectStatusAsync(null).ConfigureAwait(false)).ToText() : null;
            safety.AppendAudit("create_project", null, target, requestedInput, safetyContext.CurrentState, result.ToText());
            return WriteSafetyTooling.BuildApplyResult("create_project", result, "get_project_status", status);
        }

        [Description("Save the active TIA Portal project. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
        public static async Task<string> SaveProject(OpennessWorkerClient workerClient, WriteSafetyService safety, [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null, [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false, [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null)
        {
            var target = new { projectPath };
            var requestedInput = new { projectPath };
            if (string.IsNullOrWhiteSpace(safetyToken)) return WriteSafetyTooling.CreatePreview(safety, "save_project", projectPath, target, "Save the active TIA Portal project.", requestedInput, await workerClient.ProbeProjectStatusForLifecycleAsync(projectPath).ConfigureAwait(false), diff: null, instructions: ApplyInstructions("save_project"));
            if (!confirm) return ConfirmRequired("save_project");
            var safetyContext = await WriteSafetyTooling.ValidateForApplyAsync(safety, safetyToken, PreviewHint("save_project"), "save_project", projectPath, target, requestedInput, () => workerClient.ProbeProjectStatusForLifecycleAsync(projectPath)).ConfigureAwait(false);
            if (!safetyContext.IsValid) return SafetyFailure("save_project", safetyContext);
            var result = await workerClient.SaveProjectAsync(projectPath).ConfigureAwait(false);
            var status = result.Success ? (await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false)).ToText() : null;
            safety.AppendAudit("save_project", projectPath, target, requestedInput, safetyContext.CurrentState, result.ToText());
            return WriteSafetyTooling.BuildApplyResult("save_project", result, "get_project_status", status);
        }

        [Description("Save the active TIA Portal project to a copy directory. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
        public static async Task<string> SaveProjectAs(OpennessWorkerClient workerClient, WriteSafetyService safety, [Description("Parent directory for the copied project.")] string targetDirectory, [Description("Name of the copied project directory.")] string targetName, [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null, [Description("Bind this MCP session to the copied project after save-as. Must be true; rebind=false is not supported.")] bool rebind = true, [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false, [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null)
        {
            // rebind=false is an unsupported mode: Siemens SaveAs switches the active project to the
            // copy, so a non-rebinding save would strand this MCP session and the worker on different
            // projects. Reject at the first boundary - before any current-state probe, preview,
            // token issuance, worker invocation, or audit append.
            if (!rebind)
            {
                return WriteSafetyTooling.BuildApplyResult(
                    "save_project_as",
                    WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, OpennessWorkerClient.RebindFalseUnsupportedMessage));
            }

            var target = new { projectPath, targetDirectory, targetName };
            var requestedInput = new { projectPath, targetDirectory, targetName, rebind };
            if (string.IsNullOrWhiteSpace(safetyToken)) return WriteSafetyTooling.CreatePreview(safety, "save_project_as", projectPath, target, $"Save active project as '{targetName}' in '{targetDirectory}'.", requestedInput, await workerClient.ProbeProjectStatusForLifecycleAsync(projectPath).ConfigureAwait(false), diff: null, instructions: ApplyInstructions("save_project_as"));
            if (!confirm) return ConfirmRequired("save_project_as");
            var safetyContext = await WriteSafetyTooling.ValidateForApplyAsync(safety, safetyToken, PreviewHint("save_project_as"), "save_project_as", projectPath, target, requestedInput, () => workerClient.ProbeProjectStatusForLifecycleAsync(projectPath)).ConfigureAwait(false);
            if (!safetyContext.IsValid) return SafetyFailure("save_project_as", safetyContext);
            var result = await workerClient.SaveProjectAsAsync(projectPath, targetDirectory, targetName, rebind).ConfigureAwait(false);
            var status = result.Success ? (await workerClient.GetProjectStatusAsync(rebind ? null : projectPath).ConfigureAwait(false)).ToText() : null;
            safety.AppendAudit("save_project_as", projectPath, target, requestedInput, safetyContext.CurrentState, result.ToText());
            return WriteSafetyTooling.BuildApplyResult("save_project_as", result, "get_project_status", status);
        }

        [Description("Archive the active TIA Portal project. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
        public static async Task<string> ArchiveProject(OpennessWorkerClient workerClient, WriteSafetyService safety, [Description("Directory where the archive should be written. TIA Portal rejects a combined directory+file path longer than roughly 140 characters.")] string archiveDirectory, [Description("Archive file name. For Compressed and DiscardRestorableDataAndCompressed modes, .zap21 is appended automatically if not already present.")] string archiveName, [Description("Archive mode: None, DiscardRestorableData, Compressed, or DiscardRestorableDataAndCompressed.")] string? mode = null, [Description("Save the project before archiving.")] bool saveBeforeArchive = true, [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null, [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false, [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null)
        {
            // The worker appends the mode-appropriate extension (e.g. .zap21 for Compressed) before
            // writing the file, since Siemens' Archive() does not add it. Reflect that resolved name
            // here so the preview/audit trail names the file that actually lands on disk.
            var resolvedArchiveName = ArchiveModeNames.TryNormalize(mode, out var normalizedMode, out _)
                ? ArchiveModeNames.EnsureArchiveExtension(archiveName, normalizedMode)
                : archiveName;
            var target = new { projectPath, archiveDirectory, archiveName = resolvedArchiveName };
            var requestedInput = new { projectPath, archiveDirectory, archiveName, mode, saveBeforeArchive };
            if (string.IsNullOrWhiteSpace(safetyToken)) return WriteSafetyTooling.CreatePreview(safety, "archive_project", projectPath, target, $"Archive active project to '{archiveDirectory}\\{resolvedArchiveName}'.", requestedInput, RejectIfArchiveDirectoryWithinProjectFolder(await workerClient.ProbeProjectStatusForLifecycleAsync(projectPath).ConfigureAwait(false), archiveDirectory), diff: null, instructions: ApplyInstructions("archive_project"));
            if (!confirm) return ConfirmRequired("archive_project");
            var safetyContext = await WriteSafetyTooling.ValidateForApplyAsync(safety, safetyToken, PreviewHint("archive_project"), "archive_project", projectPath, target, requestedInput, async () => RejectIfArchiveDirectoryWithinProjectFolder(await workerClient.ProbeProjectStatusForLifecycleAsync(projectPath).ConfigureAwait(false), archiveDirectory)).ConfigureAwait(false);
            if (!safetyContext.IsValid) return SafetyFailure("archive_project", safetyContext);
            var result = await workerClient.ArchiveProjectAsync(projectPath, archiveDirectory, archiveName, mode, saveBeforeArchive).ConfigureAwait(false);
            var status = result.Success ? (await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false)).ToText() : null;
            safety.AppendAudit("archive_project", projectPath, target, requestedInput, safetyContext.CurrentState, result.ToText());
            return WriteSafetyTooling.BuildApplyResult("archive_project", result, "get_project_status", status);
        }

        [Description("Close the active TIA Portal project and clear this MCP session binding. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
        public static async Task<string> CloseProject(OpennessWorkerClient workerClient, WriteSafetyService safety, [Description("Optional path to a .ap21 project file. If omitted, closes the currently bound/open project.")] string? projectPath = null, [Description("Save the project before closing it.")] bool saveBeforeClose = true, [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false, [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null)
        {
            var target = new { projectPath };
            var requestedInput = new { projectPath, saveBeforeClose };
            if (string.IsNullOrWhiteSpace(safetyToken)) return WriteSafetyTooling.CreatePreview(safety, "close_project", projectPath, target, "Close the active TIA Portal project.", requestedInput, await workerClient.ProbeProjectStatusForLifecycleAsync(projectPath).ConfigureAwait(false), diff: null, instructions: ApplyInstructions("close_project"));
            if (!confirm) return ConfirmRequired("close_project");
            var safetyContext = await WriteSafetyTooling.ValidateForApplyAsync(safety, safetyToken, PreviewHint("close_project"), "close_project", projectPath, target, requestedInput, () => workerClient.ProbeProjectStatusForLifecycleAsync(projectPath)).ConfigureAwait(false);
            if (!safetyContext.IsValid) return SafetyFailure("close_project", safetyContext);
            var result = await workerClient.CloseProjectAsync(projectPath, saveBeforeClose).ConfigureAwait(false);
            safety.AppendAudit("close_project", projectPath, target, requestedInput, safetyContext.CurrentState, result.ToText());
            return WriteSafetyTooling.BuildApplyResult("close_project", result, "get_project_status", null);
        }

        /// <summary>
        /// Host-side mirror of the worker's <c>RequireArchiveDirectoryOutsideProjectFolder</c> guard,
        /// applied to the SAME current-state probe the preview/apply flow already reads (no extra
        /// worker round trip). Substitutes a validation_error in place of the probe's real payload
        /// when <paramref name="archiveDirectory"/> is nested in the open project's folder, so
        /// <see cref="WriteSafetyTooling.CreatePreview"/> and <c>ValidateForApplyAsync</c> - both of
        /// which already treat a failed current-state read as "render/report the failure, don't
        /// issue or honor a token" - reject the call before a safety token is ever created. A
        /// rejected preview costs the caller nothing but a fresh call with a corrected path; without
        /// this, the only rejection point was apply-time, after a token round trip had already been
        /// spent on a target that was always going to fail.
        /// </summary>
        private static WorkerCallResult RejectIfArchiveDirectoryWithinProjectFolder(WorkerCallResult probe, string archiveDirectory)
        {
            if (!probe.Success || !ArchiveDirectoryGuard.IsWithinProjectFolder(archiveDirectory, probe.ResolvedProjectPath ?? string.Empty))
            {
                return probe;
            }

            return WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, ArchiveDirectoryGuard.BuildRejectionMessage(archiveDirectory));
        }

        private static string ApplyInstructions(string toolName) => $"Preview only — nothing was changed. To apply, call {toolName} again with the same arguments plus confirm=true and this safetyToken.";

        // confirm=false is caller input error: render it as a categorized validation_error envelope
        // through the same BuildApplyResult path as every other guarded-write failure, never as a
        // raw uncapped string.
        private static string ConfirmRequired(string toolName) => WriteSafetyTooling.BuildApplyResult(
            toolName,
            WorkerCallResult.Fail(
                WorkerFailureCategories.ValidationError,
                $"Safety token provided but confirm=false. Set confirm=true and resend the safetyToken to apply, or call {toolName} without safetyToken for a fresh preview."));

        // A safety-token rejection already carries a real category (validation_error / binding_conflict
        // / state_changed, or an uncertain read outcome's own category); surface it as the same
        // categorized envelope shape, not a bare error string.
        private static string SafetyFailure(string toolName, WriteSafetyApplyContext safetyContext) => WriteSafetyTooling.BuildApplyResult(
            toolName,
            WorkerCallResult.Fail(
                safetyContext.FailureCategory ?? WorkerFailureCategories.ValidationError,
                safetyContext.Error ?? "Safety validation failed."));

        private static string PreviewHint(string toolName) => $"{toolName} (without safetyToken)";
    }
}
