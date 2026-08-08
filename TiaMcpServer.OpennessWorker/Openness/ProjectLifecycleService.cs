using Siemens.Engineering;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;

namespace TiaMcpServer.OpennessWorker.Openness;

public static class ProjectLifecycleService
{
    /// <summary>
    /// Pure read: never opens, closes, or switches a project. Reuses
    /// <see cref="ProjectOpenPolicy"/> - the same decision engine every other read-only worker
    /// operation already uses via <c>Program.EnsureRequestedProjectOpen</c> - so "a read must
    /// never silently attach a different project" is enforced identically everywhere instead of
    /// this method re-deriving its own rule.
    /// </summary>
    public static ProjectStatusInfo GetStatusReadOnly(TiaPortalSession session, string? requestedProjectPath)
    {
        session.EnsureConnected();

        var currentPath = session.CurrentProjectPath;
        if (ProjectOpenPolicy.Decide(currentPath, requestedProjectPath) == ProjectOpenDecision.Refuse)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.BindingConflict,
                ProjectOpenPolicy.RefusalMessage(currentPath!, requestedProjectPath!));
        }

        // ProjectOpenDecision.OpenRequested (nothing attached, a path was requested) is
        // deliberately NOT acted on here, unlike EnsureRequestedProjectOpen: a read must report
        // IsOpen=false rather than open the requested project as a side effect.
        return session.Project is null
            ? new ProjectStatusInfo { IsOpen = false }
            : ReadStatusWithMetadata(session.Project);
    }

    /// <summary>
    /// The read-only status read (above) carries the extended metadata surface; the write-side
    /// probes (<see cref="ProbeStatusForLifecycle"/> and lifecycle result/close payloads) stay on
    /// plain <see cref="ReadStatus"/> so their payloads and safety-token binding remain unchanged.
    /// </summary>
    private static ProjectStatusInfo ReadStatusWithMetadata(Project project)
    {
        var status = ReadStatus(project);
        status.Metadata = ProjectMetadataReader.Read(project);
        return status;
    }

    /// <summary>
    /// Internal state probe for save/save-as/archive/close preview and apply-time
    /// current-state checks only - never exposed as an MCP tool. Retains the original
    /// <c>GetStatus</c> behavior: it may open <paramref name="projectPath"/> when nothing is
    /// open yet, because those lifecycle writes must be able to inspect a project's state
    /// before acting on it.
    /// </summary>
    public static ProjectStatusInfo ProbeStatusForLifecycle(TiaPortalSession session, string? projectPath)
    {
        EnsureProject(session, projectPath);

        return session.Project is null
            ? new ProjectStatusInfo { IsOpen = false }
            : ReadStatus(session.Project);
    }

    public static ProjectLifecycleResultInfo OpenProject(TiaPortalSession session, string projectPath)
    {
        RequireAbsoluteFile(projectPath, "ProjectPath");

        session.EnsureConnected();
        session.OpenProject(projectPath);

        return Result("open_project", session.Project);
    }

    public static ProjectLifecycleResultInfo CreateProject(
        TiaPortalSession session,
        string projectDirectory,
        string projectName,
        string? author,
        string? comment)
    {
        RequireAbsoluteDirectory(projectDirectory, "ProjectDirectory", mustExist: true);
        RequireName(projectName, "ProjectName");

        session.EnsureConnected();
        if (session.TiaPortal is null)
        {
            throw new InvalidOperationException("No TIA Portal session is connected. Please start TIA Portal and try again.");
        }

        Project project;
        if (string.IsNullOrWhiteSpace(author) && string.IsNullOrWhiteSpace(comment))
        {
            project = session.TiaPortal.Projects.Create(new DirectoryInfo(projectDirectory), projectName);
        }
        else
        {
            var createParams = new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>("TargetDirectory", new DirectoryInfo(projectDirectory)),
                new KeyValuePair<string, object>("Name", projectName)
            };

            if (!string.IsNullOrWhiteSpace(author))
            {
                createParams.Add(new KeyValuePair<string, object>("Author", author!));
            }

            if (!string.IsNullOrWhiteSpace(comment))
            {
                createParams.Add(new KeyValuePair<string, object>("Comment", comment!));
            }

            project = ((IEngineeringComposition)session.TiaPortal.Projects)
                .Create(typeof(Project), createParams) as Project ??
                throw new InvalidOperationException($"TIA Portal did not return a project after creating '{projectName}'.");
        }

        session.Project = project;
        return Result("create_project", project);
    }

    public static ProjectLifecycleResultInfo SaveProject(TiaPortalSession session, string? projectPath)
    {
        var project = EnsureProject(session, projectPath);
        project.Save();

        return Result("save_project", project);
    }

    /// <summary>Shared rejection message for the unsupported <c>save_project_as(rebind:false)</c> mode.</summary>
    internal const string RebindFalseUnsupportedMessage =
        "save_project_as requires rebind=true. The rebind=false mode is not supported: Siemens "
        + "SaveAs switches the active project to the copy, so a non-rebinding save would leave the "
        + "worker and the MCP session bound to different projects.";

    public static ProjectLifecycleResultInfo SaveProjectAs(
        TiaPortalSession session,
        string? projectPath,
        string targetDirectory,
        string targetName,
        bool rebind)
    {
        // Defense in depth (host layers reject first): rebind=false is unsupported. Reject before
        // any Siemens-touching call so a rejected mode can never mutate project state.
        if (!rebind)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                RebindFalseUnsupportedMessage);
        }

        var project = EnsureProject(session, projectPath);
        RequireAbsoluteDirectory(targetDirectory, "TargetDirectory", mustExist: true);
        RequireName(targetName, "TargetName");

        var copyDirectory = Path.Combine(targetDirectory, targetName);
        project.SaveAs(new DirectoryInfo(copyDirectory));

        // Siemens SaveAs switches the active project to the copy in place. Do NOT close/reopen: a
        // second lifecycle mutation is exactly what previously stranded host and worker on different
        // projects. Instead, discover the copied .ap?? file and require the live active project to
        // BE that file. Program.Stamp then reports it as ResolvedProjectPath (from
        // session.CurrentProjectPath), and the host binds only from that verified field.
        var copiedProjectPath = Directory.Exists(copyDirectory)
            ? Directory.GetFiles(copyDirectory, "*.ap??", SearchOption.AllDirectories).FirstOrDefault()
            : null;
        var activeProjectPath = session.CurrentProjectPath;

        if (string.IsNullOrWhiteSpace(copiedProjectPath)
            || string.IsNullOrWhiteSpace(activeProjectPath)
            || !ProjectPathsEqual(copiedProjectPath!, activeProjectPath!))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.PostconditionFailed,
                $"save_project_as saved under '{copyDirectory}' but could not confirm the active project "
                + $"matches the copied project file (discovered copy: '{copiedProjectPath ?? "(none)"}', "
                + $"active project: '{activeProjectPath ?? "(none)"}').",
                warnings: new[] { "Project state may have changed; inspect the open project before retrying." });
        }

        // session.Project is already the copy (Siemens switched it), so the payload is built from it
        // without any explicit close/reopen.
        return Result("save_project_as", session.Project);
    }

    private static bool ProjectPathsEqual(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Rejects before any Siemens-touching call (mirrors the save_project_as rebind=false defense
    /// above): archiving with the open project's own containing folder as the target always fails
    /// against a live TIA Portal V21 instance ("A project directory that already exists cannot be
    /// saved"), and archiving into a subdirectory of that folder was reported to sometimes succeed
    /// but have TIA Portal silently auto-delete the subdirectory. "Permitted in some cases" is not a
    /// reason to allow it here - block the whole folder categorically rather than distinguish safe
    /// from unsafe subdirectories.
    /// </summary>
    private static void RequireArchiveDirectoryOutsideProjectFolder(Project project, string archiveDirectory)
    {
        var projectFilePath = project.Path?.FullName;
        if (string.IsNullOrWhiteSpace(projectFilePath)
            || !ArchiveDirectoryGuard.IsWithinProjectFolder(archiveDirectory, projectFilePath!))
        {
            return;
        }

        throw new WorkerOperationException(
            WorkerFailureCategories.ValidationError,
            ArchiveDirectoryGuard.BuildRejectionMessage(archiveDirectory));
    }

    public static ProjectLifecycleResultInfo ArchiveProject(
        TiaPortalSession session,
        string? projectPath,
        string archiveDirectory,
        string archiveName,
        string archiveMode,
        bool saveBeforeArchive)
    {
        var project = EnsureProject(session, projectPath);
        // Existence is checked further down, AFTER the project-folder guard: a caller pointed at a
        // non-existent subdirectory of the project's own folder must see the guard's explanation,
        // not a bare "directory not found" that gives no hint the target is categorically rejected.
        RequireAbsoluteDirectory(archiveDirectory, "ArchiveDirectory", mustExist: false);
        RequireName(archiveName, "ArchiveName");
        RequireArchiveDirectoryOutsideProjectFolder(project, archiveDirectory);
        RequireDirectoryExists(archiveDirectory, "ArchiveDirectory");

        if (!Enum.TryParse<ProjectArchivationMode>(archiveMode, ignoreCase: true, out var mode))
        {
            throw new InvalidOperationException($"Invalid archive mode '{archiveMode}'.");
        }

        var resolvedArchiveName = ArchiveModeNames.EnsureArchiveExtension(archiveName, archiveMode);

        if (saveBeforeArchive)
        {
            project.Save();
        }

        project.Archive(new DirectoryInfo(archiveDirectory), resolvedArchiveName, mode);

        return Result("archive_project", project);
    }

    public static ProjectLifecycleResultInfo CloseProject(
        TiaPortalSession session,
        string? projectPath,
        bool saveBeforeClose)
    {
        var project = EnsureProject(session, projectPath);
        var status = ReadStatus(project);

        if (saveBeforeClose)
        {
            project.Save();
        }

        project.Close();
        session.MarkProjectClosed();

        return new ProjectLifecycleResultInfo
        {
            Operation = "close_project",
            ProjectPath = status.Path,
            Project = new ProjectStatusInfo
            {
                IsOpen = false,
                Name = status.Name,
                Path = status.Path,
                Version = status.Version,
                Author = status.Author,
                CreationTime = status.CreationTime,
                LastModified = status.LastModified,
                LastModifiedBy = status.LastModifiedBy,
                Size = status.Size
            }
        };
    }

    private static Project EnsureProject(TiaPortalSession session, string? projectPath)
    {
        session.EnsureConnected();

        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            session.OpenProject(projectPath!);
        }

        return session.Project ??
            throw new InvalidOperationException("No project is open. Provide a projectPath argument or open a project in TIA Portal.");
    }

    private static ProjectLifecycleResultInfo Result(string operation, Project? project)
    {
        var status = project is null
            ? new ProjectStatusInfo { IsOpen = false }
            : ReadStatus(project);

        return new ProjectLifecycleResultInfo
        {
            Operation = operation,
            ProjectPath = status.Path,
            Project = status
        };
    }

    private static ProjectStatusInfo ReadStatus(Project project)
    {
        return new ProjectStatusInfo
        {
            IsOpen = true,
            Name = Read(() => project.Name),
            Path = project.Path?.FullName,
            Version = Read(() => project.Version),
            Author = Read(() => project.Author),
            IsModified = ReadNullable(() => project.IsModified),
            CreationTime = ReadNullable(() => project.CreationTime),
            LastModified = ReadNullable(() => project.LastModified),
            LastModifiedBy = Read(() => project.LastModifiedBy),
            Size = ReadNullable(() => project.Size)
        };
    }

    private static string? Read(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine($"Could not read project metadata: {ex.Message}");
            return null;
        }
    }

    private static T? ReadNullable<T>(Func<T> read)
        where T : struct
    {
        try
        {
            return read();
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine($"Could not read project metadata: {ex.Message}");
            return null;
        }
    }

    private static void RequireAbsoluteFile(string path, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"{fieldName} is required.");
        }

        if (!Path.IsPathRooted(path))
        {
            throw new InvalidOperationException($"{fieldName} must be an absolute path.");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("TIA Portal project file was not found.", path);
        }
    }

    private static void RequireAbsoluteDirectory(string path, string fieldName, bool mustExist)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"{fieldName} is required.");
        }

        if (!Path.IsPathRooted(path))
        {
            throw new InvalidOperationException($"{fieldName} must be an absolute path.");
        }

        if (mustExist && !Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"{fieldName} '{path}' was not found.");
        }
    }

    private static void RequireName(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{fieldName} is required.");
        }
    }

    private static void RequireDirectoryExists(string path, string fieldName)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"{fieldName} '{path}' was not found.");
        }
    }
}
