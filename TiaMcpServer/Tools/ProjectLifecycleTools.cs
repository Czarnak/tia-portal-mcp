using System.ComponentModel;
using ModelContextProtocol.Server;
using TiaMcpServer.Safety;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tools
{
    [McpServerToolType]
    public static class ProjectLifecycleTools
    {
        [McpServerTool(Name = "get_project_status")]
        [Description("Get status and metadata for the active TIA Portal project.")]
        public static async Task<string> GetProjectStatus(
            OpennessWorkerClient workerClient,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        {
            return await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false);
        }

        [McpServerTool(Name = "preview_open_project")]
        [Description("Preview opening a TIA Portal project and return a short-lived safetyToken. Pass the token to open_project after reviewing the preview.")]
        public static Task<string> PreviewOpenProject(
            [Description("Path to the .ap21 project file to open.")] string projectPath,
            [Description("Set true to allow rebinding this MCP session from a previously bound project.")] bool forceRebind = false)
        {
            var target = new { projectPath };
            var requestedInput = new { projectPath, forceRebind };
            return Task.FromResult(WriteSafetyTooling.CreatePreview(
                "open_project",
                projectPath,
                target,
                $"Open and bind TIA Portal project '{projectPath}'.",
                requestedInput,
                WriteSafetyTooling.DescribePathState(projectPath)));
        }

        [McpServerTool(Name = "open_project")]
        [Description("Open a TIA Portal project and bind this MCP session to it. Requires confirm=true and a safetyToken from preview_open_project.")]
        public static async Task<string> OpenProject(
            OpennessWorkerClient workerClient,
            [Description("Path to the .ap21 project file to open.")] string projectPath,
            [Description("Set to true to confirm the write operation. Required safety flag; operation is rejected when false.")] bool confirm = false,
            [Description("Safety token returned by preview_open_project for this exact write request.")] string? safetyToken = null,
            [Description("Set true to allow rebinding this MCP session from a previously bound project.")] bool forceRebind = false)
        {
            if (!confirm)
            {
                return "Operation not confirmed. Set confirm=true to proceed with opening a project.";
            }

            var target = new { projectPath };
            var requestedInput = new { projectPath, forceRebind };
            var safety = await WriteSafetyTooling.ValidateForApplyAsync(
                safetyToken,
                "preview_open_project",
                "open_project",
                projectPath,
                target,
                requestedInput,
                () => Task.FromResult(WriteSafetyTooling.DescribePathState(projectPath))).ConfigureAwait(false);
            if (!safety.IsValid)
            {
                return safety.Error!;
            }

            var result = await workerClient.OpenProjectAsync(projectPath, forceRebind).ConfigureAwait(false);
            var status = result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                ? null
                : await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false);

            WriteSafetyService.Shared.AppendAudit("open_project", projectPath, target, requestedInput, safety.CurrentState, result);
            return WriteSafetyTooling.BuildApplyResult("open_project", result, "get_project_status", status);
        }

        [McpServerTool(Name = "preview_create_project")]
        [Description("Preview creating a new TIA Portal project and return a short-lived safetyToken. Pass the token to create_project after reviewing the preview.")]
        public static Task<string> PreviewCreateProject(
            [Description("Directory where the project folder should be created.")] string projectDirectory,
            [Description("Name of the new TIA Portal project.")] string projectName,
            [Description("Optional project author metadata.")] string? author = null,
            [Description("Optional project comment metadata.")] string? comment = null)
        {
            var target = new { projectDirectory, projectName };
            var requestedInput = new { projectDirectory, projectName, author, comment };
            return Task.FromResult(WriteSafetyTooling.CreatePreview(
                "create_project",
                null,
                target,
                $"Create TIA Portal project '{projectName}' in '{projectDirectory}'.",
                requestedInput,
                WriteSafetyTooling.DescribeProjectCreationState(projectDirectory, projectName)));
        }

        [McpServerTool(Name = "create_project")]
        [Description("Create a new TIA Portal project and bind this MCP session to it. Requires confirm=true and a safetyToken from preview_create_project.")]
        public static async Task<string> CreateProject(
            OpennessWorkerClient workerClient,
            [Description("Directory where the project folder should be created.")] string projectDirectory,
            [Description("Name of the new TIA Portal project.")] string projectName,
            [Description("Optional project author metadata.")] string? author = null,
            [Description("Optional project comment metadata.")] string? comment = null,
            [Description("Set to true to confirm the write operation. Required safety flag; operation is rejected when false.")] bool confirm = false,
            [Description("Safety token returned by preview_create_project for this exact write request.")] string? safetyToken = null)
        {
            if (!confirm)
            {
                return "Operation not confirmed. Set confirm=true to proceed with creating a project.";
            }

            var target = new { projectDirectory, projectName };
            var requestedInput = new { projectDirectory, projectName, author, comment };
            var safety = await WriteSafetyTooling.ValidateForApplyAsync(
                safetyToken,
                "preview_create_project",
                "create_project",
                null,
                target,
                requestedInput,
                () => Task.FromResult(WriteSafetyTooling.DescribeProjectCreationState(projectDirectory, projectName))).ConfigureAwait(false);
            if (!safety.IsValid)
            {
                return safety.Error!;
            }

            var result = await workerClient.CreateProjectAsync(projectDirectory, projectName, author, comment)
                .ConfigureAwait(false);
            var status = result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                ? null
                : await workerClient.GetProjectStatusAsync(null).ConfigureAwait(false);

            WriteSafetyService.Shared.AppendAudit("create_project", null, target, requestedInput, safety.CurrentState, result);
            return WriteSafetyTooling.BuildApplyResult("create_project", result, "get_project_status", status);
        }

        [McpServerTool(Name = "preview_save_project")]
        [Description("Preview saving the active TIA Portal project and return a short-lived safetyToken. Pass the token to save_project after reviewing the preview.")]
        public static async Task<string> PreviewSaveProject(
            OpennessWorkerClient workerClient,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        {
            var currentState = await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false);
            var target = new { projectPath };
            var requestedInput = new { projectPath };
            return WriteSafetyTooling.CreatePreview(
                "save_project",
                projectPath,
                target,
                "Save the active TIA Portal project.",
                requestedInput,
                currentState);
        }

        [McpServerTool(Name = "save_project")]
        [Description("Save the active TIA Portal project. Requires confirm=true and a safetyToken from preview_save_project.")]
        public static async Task<string> SaveProject(
            OpennessWorkerClient workerClient,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null,
            [Description("Set to true to confirm the write operation. Required safety flag; operation is rejected when false.")] bool confirm = false,
            [Description("Safety token returned by preview_save_project for this exact write request.")] string? safetyToken = null)
        {
            if (!confirm)
            {
                return "Operation not confirmed. Set confirm=true to proceed with saving a project.";
            }

            var target = new { projectPath };
            var requestedInput = new { projectPath };
            var safety = await WriteSafetyTooling.ValidateForApplyAsync(
                safetyToken,
                "preview_save_project",
                "save_project",
                projectPath,
                target,
                requestedInput,
                () => workerClient.GetProjectStatusAsync(projectPath)).ConfigureAwait(false);
            if (!safety.IsValid)
            {
                return safety.Error!;
            }

            var result = await workerClient.SaveProjectAsync(projectPath).ConfigureAwait(false);
            var status = result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                ? null
                : await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false);

            WriteSafetyService.Shared.AppendAudit("save_project", projectPath, target, requestedInput, safety.CurrentState, result);
            return WriteSafetyTooling.BuildApplyResult("save_project", result, "get_project_status", status);
        }

        [McpServerTool(Name = "preview_save_project_as")]
        [Description("Preview saving the active TIA Portal project to a copy and return a short-lived safetyToken. Pass the token to save_project_as after reviewing the preview.")]
        public static async Task<string> PreviewSaveProjectAs(
            OpennessWorkerClient workerClient,
            [Description("Parent directory for the copied project.")] string targetDirectory,
            [Description("Name of the copied project directory.")] string targetName,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null,
            [Description("Set true to bind this MCP session to the copied project path after save-as.")] bool rebind = true)
        {
            var currentState = await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false);
            var target = new { projectPath, targetDirectory, targetName };
            var requestedInput = new { projectPath, targetDirectory, targetName, rebind };
            return WriteSafetyTooling.CreatePreview(
                "save_project_as",
                projectPath,
                target,
                $"Save active project as '{targetName}' in '{targetDirectory}'.",
                requestedInput,
                currentState);
        }

        [McpServerTool(Name = "save_project_as")]
        [Description("Save the active TIA Portal project to a copy directory. Requires confirm=true and a safetyToken from preview_save_project_as.")]
        public static async Task<string> SaveProjectAs(
            OpennessWorkerClient workerClient,
            [Description("Parent directory for the copied project.")] string targetDirectory,
            [Description("Name of the copied project directory.")] string targetName,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null,
            [Description("Set true to bind this MCP session to the copied project path after save-as.")] bool rebind = true,
            [Description("Set to true to confirm the write operation. Required safety flag; operation is rejected when false.")] bool confirm = false,
            [Description("Safety token returned by preview_save_project_as for this exact write request.")] string? safetyToken = null)
        {
            if (!confirm)
            {
                return "Operation not confirmed. Set confirm=true to proceed with saving a project copy.";
            }

            var target = new { projectPath, targetDirectory, targetName };
            var requestedInput = new { projectPath, targetDirectory, targetName, rebind };
            var safety = await WriteSafetyTooling.ValidateForApplyAsync(
                safetyToken,
                "preview_save_project_as",
                "save_project_as",
                projectPath,
                target,
                requestedInput,
                () => workerClient.GetProjectStatusAsync(projectPath)).ConfigureAwait(false);
            if (!safety.IsValid)
            {
                return safety.Error!;
            }

            var result = await workerClient.SaveProjectAsAsync(projectPath, targetDirectory, targetName, rebind)
                .ConfigureAwait(false);
            var status = result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                ? null
                : await workerClient.GetProjectStatusAsync(rebind ? null : projectPath).ConfigureAwait(false);

            WriteSafetyService.Shared.AppendAudit("save_project_as", projectPath, target, requestedInput, safety.CurrentState, result);
            return WriteSafetyTooling.BuildApplyResult("save_project_as", result, "get_project_status", status);
        }

        [McpServerTool(Name = "preview_archive_project")]
        [Description("Preview archiving the active TIA Portal project and return a short-lived safetyToken. Pass the token to archive_project after reviewing the preview.")]
        public static async Task<string> PreviewArchiveProject(
            OpennessWorkerClient workerClient,
            [Description("Directory where the archive should be written.")] string archiveDirectory,
            [Description("Archive file name, with or without extension.")] string archiveName,
            [Description("Archive mode: None, DiscardRestorableData, Compressed, or DiscardRestorableDataAndCompressed.")] string? mode = null,
            [Description("Save the project before archiving.")] bool saveBeforeArchive = true,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        {
            var currentState = await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false);
            var target = new { projectPath, archiveDirectory, archiveName };
            var requestedInput = new { projectPath, archiveDirectory, archiveName, mode, saveBeforeArchive };
            return WriteSafetyTooling.CreatePreview(
                "archive_project",
                projectPath,
                target,
                $"Archive active project to '{archiveDirectory}\\{archiveName}'.",
                requestedInput,
                currentState);
        }

        [McpServerTool(Name = "archive_project")]
        [Description("Archive the active TIA Portal project. Requires confirm=true and a safetyToken from preview_archive_project.")]
        public static async Task<string> ArchiveProject(
            OpennessWorkerClient workerClient,
            [Description("Directory where the archive should be written.")] string archiveDirectory,
            [Description("Archive file name, with or without extension.")] string archiveName,
            [Description("Archive mode: None, DiscardRestorableData, Compressed, or DiscardRestorableDataAndCompressed.")] string? mode = null,
            [Description("Save the project before archiving.")] bool saveBeforeArchive = true,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null,
            [Description("Set to true to confirm the write operation. Required safety flag; operation is rejected when false.")] bool confirm = false,
            [Description("Safety token returned by preview_archive_project for this exact write request.")] string? safetyToken = null)
        {
            if (!confirm)
            {
                return "Operation not confirmed. Set confirm=true to proceed with archiving a project.";
            }

            var target = new { projectPath, archiveDirectory, archiveName };
            var requestedInput = new { projectPath, archiveDirectory, archiveName, mode, saveBeforeArchive };
            var safety = await WriteSafetyTooling.ValidateForApplyAsync(
                safetyToken,
                "preview_archive_project",
                "archive_project",
                projectPath,
                target,
                requestedInput,
                () => workerClient.GetProjectStatusAsync(projectPath)).ConfigureAwait(false);
            if (!safety.IsValid)
            {
                return safety.Error!;
            }

            var result = await workerClient.ArchiveProjectAsync(
                projectPath,
                archiveDirectory,
                archiveName,
                mode,
                saveBeforeArchive).ConfigureAwait(false);
            var status = result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                ? null
                : await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false);

            WriteSafetyService.Shared.AppendAudit("archive_project", projectPath, target, requestedInput, safety.CurrentState, result);
            return WriteSafetyTooling.BuildApplyResult("archive_project", result, "get_project_status", status);
        }

        [McpServerTool(Name = "preview_close_project")]
        [Description("Preview closing the active TIA Portal project and return a short-lived safetyToken. Pass the token to close_project after reviewing the preview.")]
        public static async Task<string> PreviewCloseProject(
            OpennessWorkerClient workerClient,
            [Description("Optional path to a .ap21 project file. If omitted, closes the currently bound/open project.")] string? projectPath = null,
            [Description("Save the project before closing it.")] bool saveBeforeClose = true)
        {
            var currentState = await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false);
            var target = new { projectPath };
            var requestedInput = new { projectPath, saveBeforeClose };
            return WriteSafetyTooling.CreatePreview(
                "close_project",
                projectPath,
                target,
                "Close the active TIA Portal project.",
                requestedInput,
                currentState);
        }

        [McpServerTool(Name = "close_project")]
        [Description("Close the active TIA Portal project and clear this MCP session binding. Requires confirm=true and a safetyToken from preview_close_project.")]
        public static async Task<string> CloseProject(
            OpennessWorkerClient workerClient,
            [Description("Optional path to a .ap21 project file. If omitted, closes the currently bound/open project.")] string? projectPath = null,
            [Description("Save the project before closing it.")] bool saveBeforeClose = true,
            [Description("Set to true to confirm the write operation. Required safety flag; operation is rejected when false.")] bool confirm = false,
            [Description("Safety token returned by preview_close_project for this exact write request.")] string? safetyToken = null)
        {
            if (!confirm)
            {
                return "Operation not confirmed. Set confirm=true to proceed with closing a project.";
            }

            var target = new { projectPath };
            var requestedInput = new { projectPath, saveBeforeClose };
            var safety = await WriteSafetyTooling.ValidateForApplyAsync(
                safetyToken,
                "preview_close_project",
                "close_project",
                projectPath,
                target,
                requestedInput,
                () => workerClient.GetProjectStatusAsync(projectPath)).ConfigureAwait(false);
            if (!safety.IsValid)
            {
                return safety.Error!;
            }

            var result = await workerClient.CloseProjectAsync(projectPath, saveBeforeClose).ConfigureAwait(false);

            WriteSafetyService.Shared.AppendAudit("close_project", projectPath, target, requestedInput, safety.CurrentState, result);
            return WriteSafetyTooling.BuildApplyResult("close_project", result, "get_project_status", null);
        }
    }
}
