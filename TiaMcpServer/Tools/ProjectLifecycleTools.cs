using System.ComponentModel;
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
        public static Task<string> GetProjectStatus(OpennessWorkerClient workerClient, [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
            => ProjectReadTools.GetProjectStatus(workerClient, projectPath);

        [Description("Open a TIA Portal project and bind this MCP session to it. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
        public static Task<string> OpenProject(OpennessWorkerClient workerClient, WriteSafetyService safety, [Description("Path to the .ap21 project file to open.")] string projectPath, [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false, [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null, [Description("Set true to allow rebinding this MCP session from a previously bound project.")] bool forceRebind = false)
            => ProjectWriteTools.OpenProject(workerClient, safety, projectPath, confirm, safetyToken, forceRebind);

        [Description("Create a new TIA Portal project and bind this MCP session to it. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
        public static Task<string> CreateProject(OpennessWorkerClient workerClient, WriteSafetyService safety, [Description("Directory where the project folder should be created.")] string projectDirectory, [Description("Name of the new TIA Portal project.")] string projectName, [Description("Optional project author metadata.")] string? author = null, [Description("Optional project comment metadata.")] string? comment = null, [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false, [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null)
            => ProjectWriteTools.CreateProject(workerClient, safety, projectDirectory, projectName, author, comment, confirm, safetyToken);

        [Description("Save the active TIA Portal project. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
        public static Task<string> SaveProject(OpennessWorkerClient workerClient, WriteSafetyService safety, [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null, [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false, [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null)
            => ProjectWriteTools.SaveProject(workerClient, safety, projectPath, confirm, safetyToken);

        [Description("Save the active TIA Portal project to a copy directory. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
        public static Task<string> SaveProjectAs(OpennessWorkerClient workerClient, WriteSafetyService safety, [Description("Parent directory for the copied project.")] string targetDirectory, [Description("Name of the copied project directory.")] string targetName, [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null, [Description("Bind this MCP session to the copied project after save-as. Must be true; rebind=false is not supported.")] bool rebind = true, [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false, [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null)
            => ProjectWriteTools.SaveProjectAs(workerClient, safety, targetDirectory, targetName, projectPath, rebind, confirm, safetyToken);

        [Description("Archive the active TIA Portal project. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
        public static Task<string> ArchiveProject(OpennessWorkerClient workerClient, WriteSafetyService safety, [Description("Directory where the archive should be written. TIA Portal rejects a combined directory+file path longer than roughly 140 characters.")] string archiveDirectory, [Description("Archive file name. For Compressed and DiscardRestorableDataAndCompressed modes, .zap21 is appended automatically if not already present.")] string archiveName, [Description("Archive mode: None, DiscardRestorableData, Compressed, or DiscardRestorableDataAndCompressed.")] string? mode = null, [Description("Save the project before archiving.")] bool saveBeforeArchive = true, [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null, [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false, [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null)
            => ProjectWriteTools.ArchiveProject(workerClient, safety, archiveDirectory, archiveName, mode, saveBeforeArchive, projectPath, confirm, safetyToken);

        [Description("Close the active TIA Portal project and clear this MCP session binding. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
        public static Task<string> CloseProject(OpennessWorkerClient workerClient, WriteSafetyService safety, [Description("Optional path to a .ap21 project file. If omitted, closes the currently bound/open project.")] string? projectPath = null, [Description("Save the project before closing it.")] bool saveBeforeClose = true, [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false, [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null)
            => ProjectWriteTools.CloseProject(workerClient, safety, projectPath, saveBeforeClose, confirm, safetyToken);
    }
}
