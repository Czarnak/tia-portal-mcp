using Siemens.Engineering;
using Siemens.Engineering.VersionControl;
using TiaMcpServer.Contracts.Vci;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Vci;

using Project = Siemens.Engineering.Project;

public static class VciPlanner
{
    public static VciPlanInfo Plan(Project project, VciWriteRequestInfo write)
    {
        var plan = new VciPlanInfo();
        if (!VciMethods.Write.Contains(VciMethods.ForOperation(write.Operation)))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                $"Unsupported VCI operation '{write.Operation}'.");
        var service = project.GetService<VersionControlInterface>()
            ?? throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                "The project does not expose the Version Control Interface.");
        if (write.Operation == "create_workspace")
        {
            if (string.IsNullOrWhiteSpace(write.WorkspaceName) || string.IsNullOrWhiteSpace(write.RootPath))
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                    "workspaceName and rootPath are required.");
            try
            {
                if (VciPathPolicy.HasInvalidRootCharacters(write.RootPath!)) throw new ArgumentException();
                plan.WorkspaceRootPath = Path.GetFullPath(write.RootPath!).TrimEnd('\\', '/');
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "workspace_root_invalid");
            }
            foreach (var candidate in service.WorkspaceGroup.Workspaces)
            {
                if (candidate.RootPath is not null && string.Equals(
                        Path.GetFullPath(candidate.RootPath.FullName).TrimEnd('\\', '/'),
                        plan.WorkspaceRootPath, StringComparison.OrdinalIgnoreCase))
                {
                    plan.WorkspaceConflict = "An existing workspace uses this root: " + candidate.Name;
                    break;
                }
            }
            return plan;
        }

        var workspace = VciReader.FindWorkspace(service, write.WorkspaceName);
        plan.WorkspaceRootPath = workspace.RootPath?.FullName;
        if ((write.Operation == "export" || write.Operation == "sync_to_workspace" || write.Operation == "sync_to_project")
            && string.IsNullOrWhiteSpace(plan.WorkspaceRootPath))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                "The selected workspace has no root directory.");
        if (write.Operation == "delete_workspace") return plan;

        if (write.Operation == "export")
        {
            var syntax = VciPathPolicy.ValidateRelativeSyntax(write.RelativeDirectory);
            if (!syntax.IsValid)
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, syntax.Reason!);
        }
        var leaves = VciScopeResolver.Expand(project, write.Scope!);
        plan.Leaves = leaves.Count;
        var unmapped = new List<VciLeaf>();
        foreach (var leaf in leaves)
        {
            if (workspace.MappedObjects.Find(leaf.Object) is not null) plan.MappedLeaves++;
            else unmapped.Add(leaf);
        }
        if (write.Operation != "export") return plan;

        foreach (var leaf in unmapped)
        {
            var name = leaf.Path[leaf.Path.Count - 1].Name;
            List<string> supported;
            try { supported = workspace.GetSupportedFileFormats(leaf.Object).ToList(); }
            catch (Exception ex) when (ex is not NonRecoverableException)
            {
                plan.Unverifiable = $"Supported formats could not be read for leaf {name}: {ex.Message}";
                return plan;
            }
            var format = VciFormats.Resolve(write.Format, supported, leaf.IsPlc);
            if (format.Format is null)
            {
                plan.Unverifiable = "No safe supported export format for leaf: " + name;
                return plan;
            }
            if (format.IsFallback) plan.FormatFallbacks++;
            var key = VciMirroredPath.Build(write.RelativeDirectory, leaf.Path, leaf.IsPcStation);
            var problem = VciMirroredPath.FirstProblem(plan.WorkspaceRootPath!, key, format.Format);
            if (problem is not null)
            {
                AddOffender(plan.Unrepresentable, name + ": " + problem);
                continue;
            }
            var directory = VciPathPolicy.ResolveRelativeDirectory(plan.WorkspaceRootPath!, key.Directory.TrimStart('\\'));
            if (!directory.IsValid)
            {
                plan.Unverifiable = name + ": " + directory.Reason;
                return plan;
            }
        }
        return plan;
    }

    private static void AddOffender(List<string> offenders, string offender)
    {
        if (offenders.Count < 10) offenders.Add(offender);
    }
}
