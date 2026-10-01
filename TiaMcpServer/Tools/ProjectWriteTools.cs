using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.ProjectLifecycle;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tools;

/// <summary>Full-mode single-call lifecycle writes through the guarded pipeline.</summary>
[McpServerToolType]
public class ProjectWriteTools
{
    private const string Flow = " Set dryRun=true to inspect effects and guards without changing the project. "
        + "Read-write asks for form confirmation once per lifecycle call; full proceeds by policy without prompting. "
        + "Block guards stop the call in every mode.";

    [McpServerTool(Name = "open_project", ReadOnly = false, Destructive = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(LifecycleWriteResponse))]
    [Description("Open a TIA Portal project and bind this MCP session to it." + Flow)]
    public static Task<CallToolResult> OpenProject(
        OpennessWorkerClient workerClient, WriteExecution execution,
        [Description("Absolute path to the existing .ap21 project file.")] string projectPath,
        [Description("Allow rebinding from the currently bound project.")] bool forceRebind = false,
        [Description("Inspect effects and guards without mutation or confirmation.")] bool dryRun = false,
        McpServer? server = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(workerClient, execution, new("open_project") { ProjectPath = projectPath, ForceRebind = forceRebind },
            Confirmation(server), dryRun, cancellationToken);

    [McpServerTool(Name = "create_project", ReadOnly = false, Destructive = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(LifecycleWriteResponse))]
    [Description("Create a new TIA Portal project and bind this MCP session to it." + Flow)]
    public static Task<CallToolResult> CreateProject(
        OpennessWorkerClient workerClient, WriteExecution execution,
        [Description("Existing absolute parent directory for the new project.")] string projectDirectory,
        [Description("Name of the new project directory.")] string projectName,
        [Description("Optional author metadata.")] string? author = null,
        [Description("Optional comment metadata.")] string? comment = null,
        [Description("Inspect effects and guards without mutation or confirmation.")] bool dryRun = false,
        McpServer? server = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(workerClient, execution, new("create_project")
            { ProjectDirectory = projectDirectory, ProjectName = projectName, Author = author, Comment = comment },
            Confirmation(server), dryRun, cancellationToken);

    [McpServerTool(Name = "save_project", ReadOnly = false, Destructive = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(LifecycleWriteResponse))]
    [Description("Save the active verified TIA Portal project." + Flow)]
    public static Task<CallToolResult> SaveProject(
        OpennessWorkerClient workerClient, WriteExecution execution,
        [Description("Optional absolute path of the active bound project.")] string? projectPath = null,
        [Description("Inspect effects and guards without mutation or confirmation.")] bool dryRun = false,
        McpServer? server = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(workerClient, execution, new("save_project") { ProjectPath = projectPath },
            Confirmation(server), dryRun, cancellationToken);

    [McpServerTool(Name = "save_project_as", ReadOnly = false, Destructive = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(LifecycleWriteResponse))]
    [Description("Save the active TIA Portal project to a copy directory and bind this session to the copy." + Flow)]
    public static Task<CallToolResult> SaveProjectAs(
        OpennessWorkerClient workerClient, WriteExecution execution,
        [Description("Existing absolute parent directory for the copied project.")] string targetDirectory,
        [Description("Name of the copied project directory.")] string targetName,
        [Description("Optional absolute path of the active bound project.")] string? projectPath = null,
        [Description("Must be true; rebind=false is unsupported.")] bool rebind = true,
        [Description("Inspect effects and guards without mutation or confirmation.")] bool dryRun = false,
        McpServer? server = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(workerClient, execution, new("save_project_as")
            { ProjectPath = projectPath, TargetDirectory = targetDirectory, TargetName = targetName, Rebind = rebind },
            Confirmation(server), dryRun, cancellationToken);

    [McpServerTool(Name = "archive_project", ReadOnly = false, Destructive = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(LifecycleWriteResponse))]
    [Description("Archive the active verified TIA Portal project." + Flow)]
    public static Task<CallToolResult> ArchiveProject(
        OpennessWorkerClient workerClient, WriteExecution execution,
        [Description("Existing absolute archive directory outside the project folder.")] string archiveDirectory,
        [Description("Archive name; compressed modes append .zap21 when absent.")] string archiveName,
        [Description("None, DiscardRestorableData, Compressed, or DiscardRestorableDataAndCompressed.")] string? mode = null,
        [Description("Save before archiving.")] bool saveBeforeArchive = true,
        [Description("Optional absolute path of the active bound project.")] string? projectPath = null,
        [Description("Inspect effects and guards without mutation or confirmation.")] bool dryRun = false,
        McpServer? server = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(workerClient, execution, new("archive_project")
            { ProjectPath = projectPath, ArchiveDirectory = archiveDirectory, ArchiveName = archiveName, Mode = mode,
                SaveBeforeArchive = saveBeforeArchive }, Confirmation(server), dryRun, cancellationToken);

    [McpServerTool(Name = "close_project", ReadOnly = false, Destructive = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(LifecycleWriteResponse))]
    [Description("Close the active verified TIA Portal project and clear this session binding." + Flow)]
    public static Task<CallToolResult> CloseProject(
        OpennessWorkerClient workerClient, WriteExecution execution,
        [Description("Optional absolute path of the active bound project.")] string? projectPath = null,
        [Description("Save before closing. Unsaved discarding fires an acknowledge guard.")] bool saveBeforeClose = true,
        [Description("Inspect effects and guards without mutation or confirmation.")] bool dryRun = false,
        McpServer? server = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(workerClient, execution, new("close_project") { ProjectPath = projectPath, SaveBeforeClose = saveBeforeClose },
            Confirmation(server), dryRun, cancellationToken);

    internal static Task<CallToolResult> ExecuteAsync(
        OpennessWorkerClient workerClient, WriteExecution execution, LifecycleWriteItem item,
        WriteConfirmationContext confirmation, bool dryRun = false,
        CancellationToken cancellationToken = default)
        => execution.RunAsync(new LifecycleWriteDomain(workerClient, item),
            new WriteCall<LifecycleWriteItem>(item.Operation is "open_project" or "create_project" ? null : item.ProjectPath,
                new[] { item }, dryRun), confirmation, new LifecycleBindingStrategy(workerClient), cancellationToken);

    private static WriteConfirmationContext Confirmation(McpServer? server)
        => new(server is null ? null : UserConfirmation.For(server, TimeSpan.FromMinutes(2)));
}
