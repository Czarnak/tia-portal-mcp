using System.ComponentModel;
using ModelContextProtocol.Server;
using TiaMcpServer.Safety;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tools
{
    /// <summary>
    /// Project lifecycle tools. Every write tool is self-previewing: call it without a
    /// safetyToken to get a preview plus a single-use token, then call it again with the
    /// same arguments plus confirm=true and the token to apply. target/requestedInput are
    /// built exactly once per method so preview and apply can never drift apart.
    /// </summary>
    [McpServerToolType]
    public static class ProjectLifecycleTools
    {
        private const string SafetyFlowDescription =
            "Two-step safety flow in one tool: call WITHOUT safetyToken to get a preview and a single-use token "
            + "(expires after 10 minutes), review it, then call again with the same arguments plus confirm=true and the safetyToken.";

        [McpServerTool(Name = "get_project_status")]
        [Description("Get status and metadata for the active TIA Portal project.")]
        public static async Task<string> GetProjectStatus(OpennessWorkerClient workerClient, [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
            => (await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false)).ToEnvelopeText();

        [McpServerTool(Name = "open_project")]
        [Description("Open a TIA Portal project and bind this MCP session to it. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
        public static async Task<string> OpenProject(OpennessWorkerClient workerClient, WriteSafetyService safety, [Description("Path to the .ap21 project file to open.")] string projectPath, [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false, [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null, [Description("Set true to allow rebinding this MCP session from a previously bound project.")] bool forceRebind = false)
        {
            var target = new { projectPath };
            var requestedInput = new { projectPath, forceRebind };
            if (string.IsNullOrWhiteSpace(safetyToken)) return WriteSafetyTooling.CreatePreview(safety, "open_project", projectPath, target, $"Open and bind TIA Portal project '{projectPath}'.", requestedInput, WorkerCallResult.Ok(WriteSafetyTooling.DescribePathState(projectPath)), diff: null, instructions: ApplyInstructions("open_project"));
            if (!confirm) return ConfirmRequired("open_project");
            var safetyContext = await WriteSafetyTooling.ValidateForApplyAsync(safety, safetyToken, PreviewHint("open_project"), "open_project", projectPath, target, requestedInput, () => Task.FromResult(WorkerCallResult.Ok(WriteSafetyTooling.DescribePathState(projectPath)))).ConfigureAwait(false);
            if (!safetyContext.IsValid) return safetyContext.Error!;
            var result = await workerClient.OpenProjectAsync(projectPath, forceRebind).ConfigureAwait(false);
            var status = result.Success ? (await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false)).ToText() : null;
            safety.AppendAudit("open_project", projectPath, target, requestedInput, safetyContext.CurrentState, result.ToText());
            return WriteSafetyTooling.BuildApplyResult("open_project", result, "get_project_status", status);
        }

        [McpServerTool(Name = "create_project")]
        [Description("Create a new TIA Portal project and bind this MCP session to it. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
        public static async Task<string> CreateProject(OpennessWorkerClient workerClient, WriteSafetyService safety, [Description("Directory where the project folder should be created.")] string projectDirectory, [Description("Name of the new TIA Portal project.")] string projectName, [Description("Optional project author metadata.")] string? author = null, [Description("Optional project comment metadata.")] string? comment = null, [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false, [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null)
        {
            var target = new { projectDirectory, projectName };
            var requestedInput = new { projectDirectory, projectName, author, comment };
            if (string.IsNullOrWhiteSpace(safetyToken)) return WriteSafetyTooling.CreatePreview(safety, "create_project", null, target, $"Create TIA Portal project '{projectName}' in '{projectDirectory}'.", requestedInput, WorkerCallResult.Ok(WriteSafetyTooling.DescribeProjectCreationState(projectDirectory, projectName)), diff: null, instructions: ApplyInstructions("create_project"));
            if (!confirm) return ConfirmRequired("create_project");
            var safetyContext = await WriteSafetyTooling.ValidateForApplyAsync(safety, safetyToken, PreviewHint("create_project"), "create_project", null, target, requestedInput, () => Task.FromResult(WorkerCallResult.Ok(WriteSafetyTooling.DescribeProjectCreationState(projectDirectory, projectName)))).ConfigureAwait(false);
            if (!safetyContext.IsValid) return safetyContext.Error!;
            var result = await workerClient.CreateProjectAsync(projectDirectory, projectName, author, comment).ConfigureAwait(false);
            var status = result.Success ? (await workerClient.GetProjectStatusAsync(null).ConfigureAwait(false)).ToText() : null;
            safety.AppendAudit("create_project", null, target, requestedInput, safetyContext.CurrentState, result.ToText());
            return WriteSafetyTooling.BuildApplyResult("create_project", result, "get_project_status", status);
        }

        [McpServerTool(Name = "save_project")]
        [Description("Save the active TIA Portal project. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
        public static async Task<string> SaveProject(OpennessWorkerClient workerClient, WriteSafetyService safety, [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null, [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false, [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null)
        {
            var target = new { projectPath };
            var requestedInput = new { projectPath };
            if (string.IsNullOrWhiteSpace(safetyToken)) return WriteSafetyTooling.CreatePreview(safety, "save_project", projectPath, target, "Save the active TIA Portal project.", requestedInput, await workerClient.ProbeProjectStatusForLifecycleAsync(projectPath).ConfigureAwait(false), diff: null, instructions: ApplyInstructions("save_project"));
            if (!confirm) return ConfirmRequired("save_project");
            var safetyContext = await WriteSafetyTooling.ValidateForApplyAsync(safety, safetyToken, PreviewHint("save_project"), "save_project", projectPath, target, requestedInput, () => workerClient.ProbeProjectStatusForLifecycleAsync(projectPath)).ConfigureAwait(false);
            if (!safetyContext.IsValid) return safetyContext.Error!;
            var result = await workerClient.SaveProjectAsync(projectPath).ConfigureAwait(false);
            var status = result.Success ? (await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false)).ToText() : null;
            safety.AppendAudit("save_project", projectPath, target, requestedInput, safetyContext.CurrentState, result.ToText());
            return WriteSafetyTooling.BuildApplyResult("save_project", result, "get_project_status", status);
        }

        [McpServerTool(Name = "save_project_as")]
        [Description("Save the active TIA Portal project to a copy directory. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
        public static async Task<string> SaveProjectAs(OpennessWorkerClient workerClient, WriteSafetyService safety, [Description("Parent directory for the copied project.")] string targetDirectory, [Description("Name of the copied project directory.")] string targetName, [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null, [Description("Set true to bind this MCP session to the copied project path after save-as.")] bool rebind = true, [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false, [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null)
        {
            var target = new { projectPath, targetDirectory, targetName };
            var requestedInput = new { projectPath, targetDirectory, targetName, rebind };
            if (string.IsNullOrWhiteSpace(safetyToken)) return WriteSafetyTooling.CreatePreview(safety, "save_project_as", projectPath, target, $"Save active project as '{targetName}' in '{targetDirectory}'.", requestedInput, await workerClient.ProbeProjectStatusForLifecycleAsync(projectPath).ConfigureAwait(false), diff: null, instructions: ApplyInstructions("save_project_as"));
            if (!confirm) return ConfirmRequired("save_project_as");
            var safetyContext = await WriteSafetyTooling.ValidateForApplyAsync(safety, safetyToken, PreviewHint("save_project_as"), "save_project_as", projectPath, target, requestedInput, () => workerClient.ProbeProjectStatusForLifecycleAsync(projectPath)).ConfigureAwait(false);
            if (!safetyContext.IsValid) return safetyContext.Error!;
            var result = await workerClient.SaveProjectAsAsync(projectPath, targetDirectory, targetName, rebind).ConfigureAwait(false);
            var status = result.Success ? (await workerClient.GetProjectStatusAsync(rebind ? null : projectPath).ConfigureAwait(false)).ToText() : null;
            safety.AppendAudit("save_project_as", projectPath, target, requestedInput, safetyContext.CurrentState, result.ToText());
            return WriteSafetyTooling.BuildApplyResult("save_project_as", result, "get_project_status", status);
        }

        [McpServerTool(Name = "archive_project")]
        [Description("Archive the active TIA Portal project. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
        public static async Task<string> ArchiveProject(OpennessWorkerClient workerClient, WriteSafetyService safety, [Description("Directory where the archive should be written.")] string archiveDirectory, [Description("Archive file name, with or without extension.")] string archiveName, [Description("Archive mode: None, DiscardRestorableData, Compressed, or DiscardRestorableDataAndCompressed.")] string? mode = null, [Description("Save the project before archiving.")] bool saveBeforeArchive = true, [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null, [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false, [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null)
        {
            var target = new { projectPath, archiveDirectory, archiveName };
            var requestedInput = new { projectPath, archiveDirectory, archiveName, mode, saveBeforeArchive };
            if (string.IsNullOrWhiteSpace(safetyToken)) return WriteSafetyTooling.CreatePreview(safety, "archive_project", projectPath, target, $"Archive active project to '{archiveDirectory}\\{archiveName}'.", requestedInput, await workerClient.ProbeProjectStatusForLifecycleAsync(projectPath).ConfigureAwait(false), diff: null, instructions: ApplyInstructions("archive_project"));
            if (!confirm) return ConfirmRequired("archive_project");
            var safetyContext = await WriteSafetyTooling.ValidateForApplyAsync(safety, safetyToken, PreviewHint("archive_project"), "archive_project", projectPath, target, requestedInput, () => workerClient.ProbeProjectStatusForLifecycleAsync(projectPath)).ConfigureAwait(false);
            if (!safetyContext.IsValid) return safetyContext.Error!;
            var result = await workerClient.ArchiveProjectAsync(projectPath, archiveDirectory, archiveName, mode, saveBeforeArchive).ConfigureAwait(false);
            var status = result.Success ? (await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false)).ToText() : null;
            safety.AppendAudit("archive_project", projectPath, target, requestedInput, safetyContext.CurrentState, result.ToText());
            return WriteSafetyTooling.BuildApplyResult("archive_project", result, "get_project_status", status);
        }

        [McpServerTool(Name = "close_project")]
        [Description("Close the active TIA Portal project and clear this MCP session binding. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
        public static async Task<string> CloseProject(OpennessWorkerClient workerClient, WriteSafetyService safety, [Description("Optional path to a .ap21 project file. If omitted, closes the currently bound/open project.")] string? projectPath = null, [Description("Save the project before closing it.")] bool saveBeforeClose = true, [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false, [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null)
        {
            var target = new { projectPath };
            var requestedInput = new { projectPath, saveBeforeClose };
            if (string.IsNullOrWhiteSpace(safetyToken)) return WriteSafetyTooling.CreatePreview(safety, "close_project", projectPath, target, "Close the active TIA Portal project.", requestedInput, await workerClient.ProbeProjectStatusForLifecycleAsync(projectPath).ConfigureAwait(false), diff: null, instructions: ApplyInstructions("close_project"));
            if (!confirm) return ConfirmRequired("close_project");
            var safetyContext = await WriteSafetyTooling.ValidateForApplyAsync(safety, safetyToken, PreviewHint("close_project"), "close_project", projectPath, target, requestedInput, () => workerClient.ProbeProjectStatusForLifecycleAsync(projectPath)).ConfigureAwait(false);
            if (!safetyContext.IsValid) return safetyContext.Error!;
            var result = await workerClient.CloseProjectAsync(projectPath, saveBeforeClose).ConfigureAwait(false);
            safety.AppendAudit("close_project", projectPath, target, requestedInput, safetyContext.CurrentState, result.ToText());
            return WriteSafetyTooling.BuildApplyResult("close_project", result, "get_project_status", null);
        }

        private static string ApplyInstructions(string toolName) => $"Preview only — nothing was changed. To apply, call {toolName} again with the same arguments plus confirm=true and this safetyToken.";
        private static string ConfirmRequired(string toolName) => $"Safety token provided but confirm=false. Set confirm=true and resend the safetyToken to apply, or call {toolName} without safetyToken for a fresh preview.";
        private static string PreviewHint(string toolName) => $"{toolName} (without safetyToken)";
    }
}
