using Siemens.Engineering;
using Siemens.Engineering.VersionControl;
using TiaMcpServer.Contracts.Vci;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Vci;

using Project = Siemens.Engineering.Project;

public static class VciWriter
{
    public static VciWriteOutcomeInfo Write(TiaPortal portal, Project project, string method, VciWriteRequestInfo write)
    {
        var outcome = new VciWriteOutcomeInfo();
        VciLastAttemptedInfo? lastAttempted = null;
        var importCandidatesHandled = false;
        if (write.Operation == "delete_workspace" || write.Operation == "delete_mapping") outcome.Notes.Add(VciNotes.FilesRemain);
        if (write.Operation == "sync_to_project") outcome.Notes.Add(VciNotes.ImportedNotValidated);
        try
        {
            if (!VciMethods.Write.Contains(method) || method != VciMethods.ForOperation(write.Operation))
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "Unsupported VCI write method or operation.");
            var service = project.GetService<VersionControlInterface>()
                ?? throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                    "The project does not expose the Version Control Interface.");
            if (write.Operation == "create_workspace")
            {
                outcome.Leaves = 1;
                var plan = VciPlanner.Plan(project, write);
                var root = VciPathPolicy.ValidateRoot(write.RootPath);
                if (plan.WorkspaceConflict is not null || plan.Unverifiable is not null || !root.IsValid)
                {
                    outcome.Failed = 1;
                    throw new WorkerOperationException(WorkerFailureCategories.StateChanged,
                        plan.WorkspaceConflict ?? plan.Unverifiable ?? root.Reason!, vciWriteOutcome: outcome);
                }
                Directory.CreateDirectory(root.CanonicalPath!);
                service.WorkspaceGroup.Workspaces.Create(write.WorkspaceName!, new DirectoryInfo(root.CanonicalPath!));
                outcome.Succeeded = 1;
                outcome.ProjectModified = true;
            }
            else
            {
                var workspace = VciReader.FindWorkspace(service, write.WorkspaceName);
                if (write.Operation == "delete_workspace")
                {
                    outcome.Leaves = 1;
                    workspace.Delete();
                    outcome.Succeeded = 1;
                    outcome.ProjectModified = true;
                }
                else
                {
                    var leaves = VciScopeResolver.Expand(project, write.Scope!);
                    var candidates = new List<(VciFileKey Key, VciLastAttemptedInfo Attempt)>();
                    if (write.Operation == "export") outcome.Leaves = leaves.Count;
                    foreach (var leaf in leaves)
                    {
                        Attempt(new VciLastAttemptedInfo { Path = leaf.Path, Operation = write.Operation,
                            Format = write.Operation == "export" ? write.Format ?? "SimaticML" : null });
                        try
                        {
                            var mapping = workspace.MappedObjects.Find(leaf.Object);
                            if (write.Operation == "export")
                            {
                                if (mapping is not null) { outcome.Skipped.AlreadyMapped++; continue; }
                                var format = VciFormats.Resolve(write.Format,
                                    workspace.GetSupportedFileFormats(leaf.Object).ToList(), leaf.IsPlc);
                                if (format.Format is null) throw new InvalidOperationException("No safe supported export format.");
                                Attempt(new VciLastAttemptedInfo { Path = leaf.Path, Operation = write.Operation, Format = format.Format });
                                if (format.IsFallback) outcome.FormatFallbacks++;
                                var key = VciMirroredPath.Build(write.RelativeDirectory, leaf.Path);
                                var problem = VciMirroredPath.FirstProblem(workspace.RootPath.FullName, key);
                                if (problem is not null) throw new InvalidOperationException(problem);
                                var directory = ExportDirectory(workspace, key);
                                Directory.CreateDirectory(directory);
                                // Recheck the full mirrored path and current keys after directory creation, immediately before export.
                                ExportDirectory(workspace, key);
                                workspace.ExportObject(leaf.Object, new DirectoryInfo(key.Directory), key.FileName, format.Format);
                                outcome.Succeeded++;
                                outcome.ProjectModified = true;
                                continue;
                            }
                            if (mapping is null) continue;
                            outcome.Leaves++;
                            Attempt(new VciLastAttemptedInfo { Path = leaf.Path, Operation = write.Operation, Format = mapping.FileFormat });
                            if (write.Operation == "delete_mapping")
                            {
                                mapping.Delete();
                                outcome.Succeeded++;
                                outcome.ProjectModified = true;
                                continue;
                            }
                            var status = mapping.GetStatus();
                            var direction = write.Operation == "sync_to_project" ? VciSyncDirection.ToProject : VciSyncDirection.ToWorkspace;
                            switch (VciStatusFilter.Decide(direction, status.CompareState.ToString(), status.IndividualObjectCompareDetails.ToString()))
                            {
                                case VciSyncDecision.SkipEqual: outcome.Skipped.Equal++; break;
                                case VciSyncDecision.SkipConflict: outcome.Skipped.Conflict++; break;
                                case VciSyncDecision.SkipTargetChanged: outcome.Skipped.TargetChanged++; break;
                                case VciSyncDecision.FailMissing: outcome.Failed++; break;
                                case VciSyncDecision.Sync:
                                    candidates.Add((VciFileKey.FromMapping(workspace.RootPath.FullName,
                                        mapping.DirectoryPath.FullName, mapping.FileNameWithoutExtension), lastAttempted!));
                                    break;
                            }
                        }
                        catch (NonRecoverableException) { throw; }
                        catch (EngineeringException) { outcome.Failed++; }
                        catch (Exception) { outcome.Failed++; }
                    }

                    if (candidates.Count > 0 && write.Operation == "sync_to_project")
                    {
                        importCandidatesHandled = true;
                        var committed = false;
                        try
                        {
                            using (var access = portal.ExclusiveAccess("Import VCI workspace scope"))
                            {
                                var commitRequested = false;
                                using (var transaction = access.Transaction(project, "Import VCI workspace scope"))
                                {
                                    var allSucceeded = true;
                                    foreach (var candidate in candidates)
                                    {
                                        Attempt(candidate.Attempt);
                                        try { FindMapping(workspace, candidate.Key).Synchronize(SynchronizationMode.WorkspaceToProject); }
                                        catch (NonRecoverableException) { throw; }
                                        catch (EngineeringException) { allSucceeded = false; }
                                        catch (Exception) { allSucceeded = false; }
                                    }
                                    if (allSucceeded && transaction.CanCommit)
                                    {
                                        transaction.CommitOnDispose();
                                        commitRequested = true;
                                    }
                                }
                                // Commit is known only after transaction disposal, independently of access release.
                                committed = commitRequested;
                            }
                        }
                        finally
                        {
                            if (committed) { outcome.Succeeded = candidates.Count; outcome.ProjectModified = true; }
                            else { outcome.Failed += candidates.Count; outcome.Succeeded = 0; outcome.ProjectModified = false; }
                        }
                    }
                    else if (write.Operation == "sync_to_workspace")
                    {
                        foreach (var candidate in candidates)
                        {
                            Attempt(candidate.Attempt);
                            try
                            {
                                FindMapping(workspace, candidate.Key).Synchronize(SynchronizationMode.ProjectToWorkspace);
                                outcome.Succeeded++;
                            }
                            catch (NonRecoverableException) { throw; }
                            catch (EngineeringException) { outcome.Failed++; }
                            catch (Exception) { outcome.Failed++; }
                        }
                    }
                }
            }
            if (outcome.Failed > 0)
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                    "One or more VCI leaves failed; inspect the counts before retrying.", vciWriteOutcome: outcome);
            return outcome;
        }
        catch (NonRecoverableException)
        {
            if (!importCandidatesHandled) outcome.Failed++;
            outcome.LastAttempted = lastAttempted;
            throw new WorkerOperationException(WorkerFailureCategories.PortalLost,
                "VCI lost the Portal; restart TIA Portal and reopen the project. Exported files remain; unsaved workspaces, mappings and project edits are lost; do not retry the same leaf unattended.",
                vciWriteOutcome: outcome);
        }
        catch (WorkerOperationException) { throw; }
        catch (Exception ex)
        {
            if (!importCandidatesHandled) outcome.Failed++;
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, ex.Message, vciWriteOutcome: outcome);
        }
        finally
        {
            if (outcome.ProjectModified) outcome.Notes.Add(VciNotes.SaveProject);
            if (write.BreadcrumbPath is not null) VciBreadcrumbFile.TryDelete(write.BreadcrumbPath);
        }

        void Attempt(VciLastAttemptedInfo attempt)
        {
            lastAttempted = attempt;
            if (write.BreadcrumbPath is not null)
                VciBreadcrumbFile.TryWrite(write.BreadcrumbPath, new VciBreadcrumb(write.RequestId ?? string.Empty,
                    write.OperationId, attempt.Operation, attempt.Path, attempt.Format));
        }
    }

    private static string ExportDirectory(Workspace workspace, VciFileKey key)
    {
        var root = workspace.RootPath.FullName;
        var directory = VciPathPolicy.ResolveRelativeDirectory(root, key.Directory.TrimStart('\\'));
        if (!directory.IsValid) throw new InvalidOperationException(directory.Reason);
        if (workspace.MappedObjects.Any(mapping => key.Equals(VciFileKey.FromMapping(root,
                mapping.DirectoryPath.FullName, mapping.FileNameWithoutExtension)))
            || (Directory.Exists(directory.CanonicalPath) && Directory.EnumerateFiles(directory.CanonicalPath!)
                .Any(file => Path.GetFileName(file).StartsWith(key.FileName + ".", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("The export file key is now occupied.");
        return directory.CanonicalPath!;
    }

    private static MappedObject FindMapping(Workspace workspace, VciFileKey key)
    {
        var matches = workspace.MappedObjects.Where(mapping => key.Equals(VciFileKey.FromMapping(
            workspace.RootPath.FullName, mapping.DirectoryPath.FullName, mapping.FileNameWithoutExtension))).ToList();
        return matches.Count == 1 ? matches[0] : throw new InvalidOperationException("The VCI mapping changed or is ambiguous.");
    }
}
