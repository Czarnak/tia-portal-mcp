using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Remoting;
using System.Security;
using System.Text.Json;
using Siemens.Engineering;
using Siemens.Engineering.VersionControl;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Executes one locked, read-only VCI contract-probe case. Siemens exceptions raised by the
/// deliberate observation stay inside the typed case result; only request/project infrastructure
/// failures escape to the worker boundary.
/// </summary>
internal static class VciReadContractProbeService
{
    private const string SignatureDoesNotPermitArgument = "signature_does_not_permit_argument";
    private const string NoVciService = "vci_service_not_available";
    private const string NoRootGroup = "workspace_root_group_not_available";
    private const string NoWorkspace = "no_workspace_available";
    private const string WorkspaceSearchIncomplete = "workspace_search_incomplete_budget_exhausted";
    private const string SecondaryPathNotSupplied = "secondary_project_path_not_supplied";
    private const string SecondaryCandidateNotUnique = "secondary_project_candidate_not_unique";
    private const string SecondaryAttachDenied = "secondary_project_attach_denied";
    private const string ForeignObjectNotAvailable = "foreign_object_not_available";
    private const string NoMissingMapping = "no_naturally_missing_mapping_file";
    private const string NoInaccessibleMapping = "no_naturally_inaccessible_mapping_file";
    private const string MappedFileSearchIncomplete = "mapped_file_search_incomplete_budget_exhausted";

    public static VciProbeCaseResultInfo Execute(
        TiaPortal currentPortal,
        Project project,
        VciProbeRequestInfo request)
    {
        if (currentPortal is null)
        {
            throw new ArgumentNullException(nameof(currentPortal));
        }

        if (project is null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (!VciReadProbeContract.IsKnownCase(request.CaseId))
        {
            throw new ArgumentException("The VCI read-probe case ID is not in the locked vocabulary.", nameof(request));
        }

        var result = NewResult(request);
        var isModifiedBefore = project.IsModified;

        try
        {
            Action dispatch = request.CaseId switch
            {
                "N-FMT-FOREIGN" => () => RunForeignFormat(currentPortal, project, request, result),
                "N-FMT-NULL" => () => RunNullFormat(project, request, result),
                "N-FMT-UNSUPPORTED" => () => RunUnsupportedFormat(project, request, result),
                "N-GRP-FIND-EMPTY" => () => RunGroupFind(project, request, result, GroupFindInput.Empty),
                "N-GRP-FIND-MISSING" => () => RunGroupFind(project, request, result, GroupFindInput.Missing),
                "N-GRP-FIND-NULL" => () => RunGroupFind(project, request, result, GroupFindInput.Null),
                "N-GRP-FIND-WHITESPACE" => () => RunGroupFind(project, request, result, GroupFindInput.Whitespace),
                "N-MAP-INACCESSIBLE-FILE" => () => RunMappedFileStatus(project, request, result, inaccessible: true),
                "N-MAP-MISSING-FILE" => () => RunMappedFileStatus(project, request, result, inaccessible: false),
                "N-WS-FIND-EMPTY" => () => RunWorkspaceFind(project, request, result, WorkspaceFindInput.Empty),
                "N-WS-FIND-MISSING" => () => RunWorkspaceFind(project, request, result, WorkspaceFindInput.Missing),
                "N-WS-FIND-NULL" => () => RunWorkspaceFind(project, request, result, WorkspaceFindInput.Null),
                "N-WS-FIND-WHITESPACE" => () => RunWorkspaceFind(project, request, result, WorkspaceFindInput.Whitespace),
                "R-CANARY" => () => RunCanary(project, request, result),
                "R-FMT" => () => RunSnapshot(project, request, result, supportedFormatsOnly: true),
                "R-GRP" => () => RunSnapshot(project, request, result, supportedFormatsOnly: false),
                "R-MAP" => () => RunSnapshot(project, request, result, supportedFormatsOnly: false),
                "R-REP" => () => RunRepeatability(project, request, result),
                "R-SVC" => () => RunSnapshot(project, request, result, supportedFormatsOnly: false),
                "R-WS" => () => RunSnapshot(project, request, result, supportedFormatsOnly: false),
                _ => throw new ArgumentException("The VCI read-probe case ID is not in the locked vocabulary.", nameof(request)),
            };

            dispatch();
            return result;
        }
        finally
        {
            result.ProjectState = new VciProbeProjectStateInfo
            {
                IsModifiedBefore = isModifiedBefore,
                IsModifiedAfter = project.IsModified,
            };
        }
    }

    private static VciProbeCaseResultInfo NewResult(VciProbeRequestInfo request)
        => new()
        {
            SchemaVersion = VciReadProbeContract.SchemaVersion,
            RunId = request.RunId,
            SessionId = request.SessionId,
            CaseId = request.CaseId,
            CaseInstanceId = request.CaseInstanceId,
        };

    private static void RunSnapshot(
        Project project,
        VciProbeRequestInfo request,
        VciProbeCaseResultInfo result,
        bool supportedFormatsOnly)
    {
        var read = supportedFormatsOnly
            ? VciProbeSnapshotReader.ReadSupportedFormats(project, request)
            : VciProbeSnapshotReader.Read(project, request);

        result.Omissions.AddRange(read.Omissions);
        if (read.NotObservableReason is not null)
        {
            SetNotObservable(result, read.NotObservableReason);
            return;
        }

        result.Outcome = "returned";
        read.Snapshot.Members.AddRange(read.Members);
        result.Snapshot = read.Snapshot;
    }

    private static void RunGroupFind(
        Project project,
        VciProbeRequestInfo request,
        VciProbeCaseResultInfo result,
        GroupFindInput input)
    {
        if (!TryAcquireRoot(project, result, out _, out var root))
        {
            return;
        }

        var parent = ResolveParentGroup(root!, request.Workspace);
        if (parent is null)
        {
            SetNotObservable(result, "workspace_parent_group_not_available");
            return;
        }

        WorkspaceUserGroupComposition groups = ((dynamic)parent).Groups;
        VciProbeObservationOutcomeInfo outcome;
        switch (input)
        {
            case GroupFindInput.Missing:
                var missingName = "__tia_mcp_probe_missing_group_" + Guid.NewGuid().ToString("N");
                var proof = VciProbeObservationRunner.Run(() => project.IsModified, () => groups.Find(missingName));
                if (!string.Equals(proof.Outcome, "returned_null", StringComparison.Ordinal))
                {
                    SetNotObservable(result, "guaranteed_missing_group_name_not_proven");
                    return;
                }
                outcome = VciProbeObservationRunner.Run(() => project.IsModified, () => groups.Find(missingName));
                break;
            case GroupFindInput.Empty:
                outcome = VciProbeObservationRunner.Run(() => project.IsModified, () => groups.Find(string.Empty));
                break;
            case GroupFindInput.Whitespace:
                outcome = VciProbeObservationRunner.Run(() => project.IsModified, () => groups.Find("   "));
                break;
            case GroupFindInput.Null:
                outcome = VciProbeObservationRunner.Run(() => project.IsModified, () => groups.Find(null!));
                break;
            default:
                SetNotObservable(result, SignatureDoesNotPermitArgument);
                return;
        }

        ApplyOutcome(result, outcome, request);
    }

    private static void RunWorkspaceFind(
        Project project,
        VciProbeRequestInfo request,
        VciProbeCaseResultInfo result,
        WorkspaceFindInput input)
    {
        if (!TryAcquireRoot(project, result, out _, out var root))
        {
            return;
        }

        var parent = ResolveParentGroup(root!, request.Workspace);
        if (parent is null)
        {
            SetNotObservable(result, "workspace_parent_group_not_available");
            return;
        }

        WorkspaceComposition workspaces = ((dynamic)parent).Workspaces;
        VciProbeObservationOutcomeInfo outcome;
        switch (input)
        {
            case WorkspaceFindInput.Missing:
                var missingName = "__tia_mcp_probe_missing_workspace_" + Guid.NewGuid().ToString("N");
                var proof = VciProbeObservationRunner.Run(() => project.IsModified, () => workspaces.Find(missingName));
                if (!string.Equals(proof.Outcome, "returned_null", StringComparison.Ordinal))
                {
                    SetNotObservable(result, "guaranteed_missing_workspace_name_not_proven");
                    return;
                }
                outcome = VciProbeObservationRunner.Run(() => project.IsModified, () => workspaces.Find(missingName));
                break;
            case WorkspaceFindInput.Empty:
                outcome = VciProbeObservationRunner.Run(() => project.IsModified, () => workspaces.Find(string.Empty));
                break;
            case WorkspaceFindInput.Whitespace:
                outcome = VciProbeObservationRunner.Run(() => project.IsModified, () => workspaces.Find("   "));
                break;
            case WorkspaceFindInput.Null:
                outcome = VciProbeObservationRunner.Run(() => project.IsModified, () => workspaces.Find(null!));
                break;
            default:
                SetNotObservable(result, SignatureDoesNotPermitArgument);
                return;
        }

        ApplyOutcome(result, outcome, request);
    }

    private static void RunNullFormat(Project project, VciProbeRequestInfo request, VciProbeCaseResultInfo result)
    {
        if (!TryAcquireWorkspace(project, request, result, out _, out var workspace))
        {
            return;
        }

        var outcome = VciProbeObservationRunner.Run(
            () => project.IsModified,
            () => workspace!.GetSupportedFileFormats(null!));
        ApplyOutcome(result, outcome, request);
    }

    private static void RunUnsupportedFormat(Project project, VciProbeRequestInfo request, VciProbeCaseResultInfo result)
    {
        if (!TryAcquireWorkspace(project, request, result, out var service, out var workspace))
        {
            return;
        }

        var outcome = VciProbeObservationRunner.Run(
            () => project.IsModified,
            () => workspace!.GetSupportedFileFormats((IEngineeringObject)service!));
        ApplyOutcome(result, outcome, request);
    }

    private static void RunForeignFormat(
        TiaPortal currentPortal,
        Project project,
        VciProbeRequestInfo request,
        VciProbeCaseResultInfo result)
    {
        if (string.IsNullOrWhiteSpace(request.SecondaryProjectPath))
        {
            SetNotObservable(result, SecondaryPathNotSupplied);
            return;
        }

        if (!TryAcquireWorkspace(project, request, result, out _, out var workspace))
        {
            return;
        }

        var secondaryPath = CanonicalPath(request.SecondaryProjectPath);
        if (secondaryPath is null
            || string.Equals(secondaryPath, CanonicalPath(project.Path), StringComparison.OrdinalIgnoreCase))
        {
            SetNotObservable(result, SecondaryCandidateNotUnique);
            return;
        }

        var processes = TiaPortal.GetProcesses();
        try
        {
            var matchingProcesses = processes.Where(process => string.Equals(
                    CanonicalPath(process.ProjectPath), secondaryPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matchingProcesses.Count != 1)
            {
                SetNotObservable(result, SecondaryCandidateNotUnique);
                return;
            }

            TiaPortal attached;
            try
            {
                attached = matchingProcesses[0].Attach();
            }
            catch (EngineeringSecurityException)
            {
                SetNotObservable(result, SecondaryAttachDenied);
                return;
            }
            catch (RemotingException)
            {
                SetNotObservable(result, ForeignObjectNotAvailable);
                return;
            }

            if (ReferenceEquals(attached, currentPortal))
            {
                SetNotObservable(result, SecondaryCandidateNotUnique);
                return;
            }

            using (attached)
            {
                if (Equals(attached, currentPortal))
                {
                    SetNotObservable(result, SecondaryCandidateNotUnique);
                    return;
                }

                try
                {
                    var projects = attached.Projects
                        .Where(candidateProject => !candidateProject.IsPrimary
                            && string.Equals(
                                CanonicalPath(candidateProject.Path), secondaryPath, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (projects.Count != 1)
                    {
                        SetNotObservable(result, ForeignObjectNotAvailable);
                        return;
                    }

                    var catalog = VciProbeEngineeringObjectCatalog.Enumerate(projects[0], request);
                    result.Omissions.AddRange(catalog.Omissions);
                    var foreignObject = catalog.Candidates.FirstOrDefault()?.EngineeringObject as IEngineeringObject;
                    if (foreignObject is null)
                    {
                        SetNotObservable(result, ForeignObjectNotAvailable);
                        return;
                    }

                    var outcome = VciProbeObservationRunner.Run(
                        () => project.IsModified,
                        () => workspace!.GetSupportedFileFormats(foreignObject));
                    ApplyOutcome(result, outcome, request);
                }
                catch (RemotingException)
                {
                    SetNotObservable(result, ForeignObjectNotAvailable);
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static void RunMappedFileStatus(
        Project project,
        VciProbeRequestInfo request,
        VciProbeCaseResultInfo result,
        bool inaccessible)
    {
        if (!TryAcquireRoot(project, result, out _, out var root))
        {
            return;
        }

        var candidate = FindMappedFileCandidate(
            root!, request, inaccessible, result.Omissions, out var searchIncomplete);
        if (candidate is null)
        {
            SetNotObservable(
                result,
                searchIncomplete
                    ? MappedFileSearchIncomplete
                    : inaccessible ? NoInaccessibleMapping : NoMissingMapping);
            return;
        }

        var outcome = VciProbeObservationRunner.Run(
            () => project.IsModified,
            () => candidate.Mapping.GetStatus());
        ApplyOutcome(result, outcome, request);
    }

    private static void RunRepeatability(Project project, VciProbeRequestInfo request, VciProbeCaseResultInfo result)
    {
        var first = ReadRepeatabilityObservation(project, request, result.Omissions);
        var second = ReadRepeatabilityObservation(project, request, result.Omissions);

        result.Outcome = "returned";
        result.Repeatability = new VciProbeRepeatabilityInfo
        {
            Observations = new List<VciProbeReturnInfo> { first, second },
            IsIdentical = string.Equals(
                JsonSerializer.Serialize(first),
                JsonSerializer.Serialize(second),
                StringComparison.Ordinal),
        };
    }

    private static VciProbeReturnInfo ReadRepeatabilityObservation(
        Project project,
        VciProbeRequestInfo request,
        List<VciProbeOmissionInfo> omissions)
    {
        var observation = new VciProbeReturnInfo
        {
            ClrTypeName = "vci_repeatability_observation",
        };

        var serviceOutcome = VciProbeObservationRunner.Run(
            () => project.IsModified,
            () => project.GetService<VersionControlInterface>());
        AddOutcomeMember(observation.Members, "service", serviceOutcome, request, omissions);
        if (serviceOutcome.ReturnValue is not VersionControlInterface service)
        {
            return observation;
        }

        var rootOutcome = VciProbeObservationRunner.Run(
            () => project.IsModified,
            () => service.WorkspaceGroup);
        AddOutcomeMember(observation.Members, "root", rootOutcome, request, omissions);
        if (rootOutcome.ReturnValue is not WorkspaceSystemGroup root)
        {
            return observation;
        }

        AddOutcomeMember(observation.Members, "groups.count", VciProbeObservationRunner.Run(
            () => project.IsModified, () => root.Groups.Count), request, omissions);
        AddOutcomeMember(observation.Members, "workspaces.count", VciProbeObservationRunner.Run(
            () => project.IsModified, () => root.Workspaces.Count), request, omissions);

        if (request.Workspace is not null && request.EngineeringObject is not null)
        {
            var workspace = ResolveWorkspace(root, request.Workspace);
            var objectResolution = VciProbeEngineeringObjectResolver.Resolve(project, request, request.EngineeringObject);
            if (workspace is not null && objectResolution.Candidate is not null)
            {
                AddOutcomeMember(observation.Members, "formats", VciProbeObservationRunner.Run(
                    () => project.IsModified,
                    () => workspace.GetSupportedFileFormats(
                        (IEngineeringObject)objectResolution.Candidate.EngineeringObject)), request, omissions);
            }
        }

        return observation;
    }

    private static void RunCanary(Project project, VciProbeRequestInfo request, VciProbeCaseResultInfo result)
    {
        var serviceOutcome = VciProbeObservationRunner.Run(
            () => project.IsModified,
            () => project.GetService<VersionControlInterface>());
        if (serviceOutcome.ReturnValue is not VersionControlInterface service)
        {
            ApplyOutcome(result, serviceOutcome, request);
            return;
        }

        var rootOutcome = VciProbeObservationRunner.Run(
            () => project.IsModified,
            () => service.WorkspaceGroup);
        if (rootOutcome.ReturnValue is not WorkspaceSystemGroup root)
        {
            ApplyOutcome(result, rootOutcome, request);
            return;
        }

        var groupsOutcome = VciProbeObservationRunner.Run(
            () => project.IsModified,
            () => root.Groups.Count);
        if (groupsOutcome.Exception is not null)
        {
            ApplyOutcome(result, groupsOutcome, request);
            return;
        }

        var workspacesOutcome = VciProbeObservationRunner.Run(
            () => project.IsModified,
            () => root.Workspaces.Count);
        if (workspacesOutcome.Exception is not null)
        {
            ApplyOutcome(result, workspacesOutcome, request);
            return;
        }

        result.Outcome = "returned";
        result.Snapshot = new VciProbeSnapshotInfo
        {
            Members = new List<VciProbeMemberObservationInfo>
            {
                ToMember("service", serviceOutcome, request, result.Omissions),
                ToMember("root", rootOutcome, request, result.Omissions),
                ToMember("groups.count", groupsOutcome, request, result.Omissions),
                ToMember("workspaces.count", workspacesOutcome, request, result.Omissions),
            },
            Service = new VciProbeServiceSnapshotInfo
            {
                ServiceAvailable = true,
                RootGroupAvailable = true,
                RootGroupCount = (int)groupsOutcome.ReturnValue!,
            },
            Groups = new List<VciProbeGroupSnapshotInfo>
            {
                new()
                {
                    EnumerationIndex = 0,
                    CanonicalKey = "root",
                    Name = "root",
                    Depth = 0,
                    ChildGroupCount = (int)groupsOutcome.ReturnValue!,
                    WorkspaceCount = (int)workspacesOutcome.ReturnValue!,
                },
            },
        };
    }

    private static bool TryAcquireWorkspace(
        Project project,
        VciProbeRequestInfo request,
        VciProbeCaseResultInfo result,
        out VersionControlInterface? service,
        out Workspace? workspace)
    {
        workspace = null;
        if (!TryAcquireRoot(project, result, out service, out var root))
        {
            return false;
        }

        var searchIncomplete = false;
        workspace = request.Workspace is null
            ? FindFirstWorkspace(root!, request, result.Omissions, out searchIncomplete)
            : ResolveWorkspace(root!, request.Workspace);
        if (workspace is not null)
        {
            return true;
        }

        SetNotObservable(result, searchIncomplete ? WorkspaceSearchIncomplete : NoWorkspace);
        return false;
    }

    private static bool TryAcquireRoot(
        Project project,
        VciProbeCaseResultInfo result,
        out VersionControlInterface? service,
        out WorkspaceSystemGroup? root)
    {
        service = null;
        root = null;
        var serviceOutcome = VciProbeObservationRunner.Run(
            () => project.IsModified,
            () => project.GetService<VersionControlInterface>());
        if (serviceOutcome.Exception is not null)
        {
            ApplyOutcome(result, serviceOutcome, new VciProbeRequestInfo());
            return false;
        }

        var acquiredService = serviceOutcome.ReturnValue as VersionControlInterface;
        service = acquiredService;
        if (acquiredService is null)
        {
            SetNotObservable(result, NoVciService);
            return false;
        }

        var rootOutcome = VciProbeObservationRunner.Run(
            () => project.IsModified,
            () => acquiredService.WorkspaceGroup);
        if (rootOutcome.Exception is not null)
        {
            ApplyOutcome(result, rootOutcome, new VciProbeRequestInfo());
            return false;
        }

        root = rootOutcome.ReturnValue as WorkspaceSystemGroup;
        if (root is not null)
        {
            return true;
        }

        SetNotObservable(result, NoRootGroup);
        return false;
    }

    private static object? ResolveParentGroup(WorkspaceSystemGroup root, VciWorkspaceSelectorInfo? selector)
    {
        object current = root;
        if (selector is null)
        {
            return current;
        }

        foreach (var segment in selector.GroupPath)
        {
            var index = 0;
            var sameNameOrdinal = 0;
            object? match = null;
            foreach (var child in (IEnumerable)((dynamic)current).Groups)
            {
                var name = ((WorkspaceUserGroup)child).Name ?? string.Empty;
                if (index == segment.Index
                    && string.Equals(name, segment.Name, StringComparison.Ordinal)
                    && sameNameOrdinal == segment.SameNameOrdinal)
                {
                    match = child;
                }

                if (string.Equals(name, segment.Name, StringComparison.Ordinal))
                {
                    sameNameOrdinal++;
                }
                index++;
            }

            if (match is null)
            {
                return null;
            }
            current = match;
        }

        return current;
    }

    private static Workspace? ResolveWorkspace(WorkspaceSystemGroup root, VciWorkspaceSelectorInfo selector)
    {
        var parent = ResolveParentGroup(root, selector);
        if (parent is null)
        {
            return null;
        }

        Workspace? match = null;
        var count = 0;
        foreach (Workspace candidate in (IEnumerable)((dynamic)parent).Workspaces)
        {
            if (string.Equals(candidate.Name, selector.WorkspaceName, StringComparison.Ordinal)
                && string.Equals(
                    CanonicalPath(candidate.RootPath),
                    selector.CanonicalRootPath,
                    StringComparison.Ordinal))
            {
                match = candidate;
                count++;
            }
        }
        return count == 1 ? match : null;
    }

    private static Workspace? FindFirstWorkspace(
        WorkspaceSystemGroup root,
        VciProbeRequestInfo request,
        List<VciProbeOmissionInfo> omissions,
        out bool searchIncomplete)
    {
        var groupsObserved = 0;
        var workspacesObserved = 0;
        searchIncomplete = false;
        return FindFirstWorkspaceCore(
            root, 0, "root", request, omissions,
            ref groupsObserved, ref workspacesObserved, ref searchIncomplete);
    }

    private static Workspace? FindFirstWorkspaceCore(
        object group,
        int depth,
        string traversalPath,
        VciProbeRequestInfo request,
        List<VciProbeOmissionInfo> omissions,
        ref int groupsObserved,
        ref int workspacesObserved,
        ref bool searchIncomplete)
    {
        if (depth > request.MaxGroupDepth)
        {
            OmitSearch(
                omissions,
                "Workspace search stopped at the configured group-depth budget.",
                nameof(request.MaxGroupDepth),
                request.MaxGroupDepth,
                depth - 1,
                traversalPath);
            searchIncomplete = true;
            return null;
        }

        foreach (Workspace workspace in (IEnumerable)((dynamic)group).Workspaces)
        {
            if (workspacesObserved >= request.MaxWorkspaces)
            {
                OmitSearch(
                    omissions,
                    "Workspace search stopped at the configured workspace budget.",
                    nameof(request.MaxWorkspaces),
                    request.MaxWorkspaces,
                    workspacesObserved,
                    AppendTraversalPath(traversalPath, "workspaces", workspacesObserved));
                searchIncomplete = true;
                return null;
            }
            workspacesObserved++;
            return workspace;
        }

        var childIndex = 0;
        foreach (var child in (IEnumerable)((dynamic)group).Groups)
        {
            if (groupsObserved >= request.MaxGroups)
            {
                OmitSearch(
                    omissions,
                    "Workspace search stopped at the configured group budget.",
                    nameof(request.MaxGroups),
                    request.MaxGroups,
                    groupsObserved,
                    AppendTraversalPath(traversalPath, "groups", childIndex));
                searchIncomplete = true;
                return null;
            }
            groupsObserved++;
            var found = FindFirstWorkspaceCore(
                child,
                depth + 1,
                AppendTraversalPath(traversalPath, "groups", childIndex),
                request,
                omissions,
                ref groupsObserved,
                ref workspacesObserved,
                ref searchIncomplete);
            if (found is not null || searchIncomplete)
            {
                return found;
            }
            childIndex++;
        }

        return null;
    }

    private static MappedFileCandidate? FindMappedFileCandidate(
        WorkspaceSystemGroup root,
        VciProbeRequestInfo request,
        bool inaccessible,
        List<VciProbeOmissionInfo> omissions,
        out bool searchIncomplete)
    {
        var groupsObserved = 0;
        var workspacesObserved = 0;
        var mappingsObserved = 0;
        searchIncomplete = false;
        return FindMappedFileCandidateCore(
            root, 0, "root", request, inaccessible, omissions,
            ref groupsObserved, ref workspacesObserved, ref mappingsObserved, ref searchIncomplete);
    }

    private static MappedFileCandidate? FindMappedFileCandidateCore(
        object group,
        int depth,
        string traversalPath,
        VciProbeRequestInfo request,
        bool inaccessible,
        List<VciProbeOmissionInfo> omissions,
        ref int groupsObserved,
        ref int workspacesObserved,
        ref int mappingsObserved,
        ref bool searchIncomplete)
    {
        if (depth > request.MaxGroupDepth)
        {
            OmitSearch(
                omissions,
                "Mapped-file search stopped at the configured group-depth budget.",
                nameof(request.MaxGroupDepth),
                request.MaxGroupDepth,
                depth - 1,
                traversalPath);
            searchIncomplete = true;
            return null;
        }

        var workspaceIndex = 0;
        foreach (Workspace workspace in (IEnumerable)((dynamic)group).Workspaces)
        {
            var workspacePath = AppendTraversalPath(traversalPath, "workspaces", workspaceIndex);
            if (workspacesObserved >= request.MaxWorkspaces)
            {
                OmitSearch(
                    omissions,
                    "Mapped-file search stopped at the configured workspace budget.",
                    nameof(request.MaxWorkspaces),
                    request.MaxWorkspaces,
                    workspacesObserved,
                    workspacePath);
                searchIncomplete = true;
                return null;
            }
            workspacesObserved++;

            var mappingIndex = 0;
            foreach (MappedObject mapping in workspace.MappedObjects)
            {
                if (mappingsObserved >= request.MaxMappings)
                {
                    OmitSearch(
                        omissions,
                        "Mapped-file search stopped at the configured mapping budget.",
                        nameof(request.MaxMappings),
                        request.MaxMappings,
                        mappingsObserved,
                        AppendTraversalPath(workspacePath, "mappedObjects", mappingIndex));
                    searchIncomplete = true;
                    return null;
                }
                mappingsObserved++;

                var path = ResolveMappedFilePath(workspace, mapping);
                if (path is null)
                {
                    mappingIndex++;
                    continue;
                }

                if (!inaccessible && !File.Exists(path))
                {
                    return new MappedFileCandidate(mapping, path);
                }

                if (inaccessible && IsNaturallyInaccessible(path))
                {
                    return new MappedFileCandidate(mapping, path);
                }
                mappingIndex++;
            }
            workspaceIndex++;
        }

        var childIndex = 0;
        foreach (var child in (IEnumerable)((dynamic)group).Groups)
        {
            if (groupsObserved >= request.MaxGroups)
            {
                OmitSearch(
                    omissions,
                    "Mapped-file search stopped at the configured group budget.",
                    nameof(request.MaxGroups),
                    request.MaxGroups,
                    groupsObserved,
                    AppendTraversalPath(traversalPath, "groups", childIndex));
                searchIncomplete = true;
                return null;
            }
            groupsObserved++;
            var found = FindMappedFileCandidateCore(
                child,
                depth + 1,
                AppendTraversalPath(traversalPath, "groups", childIndex),
                request,
                inaccessible,
                omissions,
                ref groupsObserved,
                ref workspacesObserved,
                ref mappingsObserved,
                ref searchIncomplete);
            if (found is not null || searchIncomplete)
            {
                return found;
            }
            childIndex++;
        }

        return null;
    }

    private static void OmitSearch(
        List<VciProbeOmissionInfo> omissions,
        string reason,
        string budgetName,
        int budgetValue,
        int observedCount,
        string traversalPath)
        => omissions.Add(new VciProbeOmissionInfo
        {
            Reason = reason,
            BudgetName = budgetName,
            BudgetValue = budgetValue,
            ObservedCount = observedCount,
            TraversalPath = traversalPath,
        });

    private static string AppendTraversalPath(string traversalPath, string collectionName, int index)
        => traversalPath + "/" + collectionName + "[" + index + "]";

    private static string? ResolveMappedFilePath(Workspace workspace, MappedObject mapping)
    {
        var rootPath = CanonicalPath(workspace.RootPath);
        var directoryPath = RawPath(mapping.DirectoryPath);
        if (rootPath is null || directoryPath is null
            || string.IsNullOrWhiteSpace(mapping.FileNameWithoutExtension)
            || string.IsNullOrWhiteSpace(mapping.FileFormat))
        {
            return null;
        }

        var directory = Path.IsPathRooted(directoryPath)
            ? directoryPath
            : Path.Combine(rootPath, directoryPath);
        var extension = mapping.FileFormat.StartsWith(".", StringComparison.Ordinal)
            ? mapping.FileFormat
            : "." + mapping.FileFormat;
        var candidate = CanonicalPath(Path.Combine(directory, mapping.FileNameWithoutExtension + extension));
        if (candidate is null || !IsWithinRoot(rootPath, candidate))
        {
            return null;
        }
        return candidate;
    }

    private static bool IsNaturallyInaccessible(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            _ = stream.Length;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private static void ApplyOutcome(
        VciProbeCaseResultInfo result,
        VciProbeObservationOutcomeInfo outcome,
        VciProbeRequestInfo request)
    {
        result.Outcome = outcome.Outcome;
        result.NotObservableReason = outcome.NotObservableReason;
        result.Exception = ToExceptionInfo(outcome.Exception);
        if (string.Equals(outcome.Outcome, "returned", StringComparison.Ordinal)
            || string.Equals(outcome.Outcome, "returned_null", StringComparison.Ordinal))
        {
            result.Return = ToReturnInfo(outcome.ReturnValue, request.MaxCollectionItems, result.Omissions);
        }
    }

    private static VciProbeReturnInfo ToReturnInfo(
        object? value,
        int maxCollectionItems,
        List<VciProbeOmissionInfo> omissions)
    {
        var normalized = VciProbeValueNormalizer.Normalize(value, maxCollectionItems);
        AddOmissions(normalized, omissions);
        var result = new VciProbeReturnInfo
        {
            ClrTypeName = normalized.RuntimeType,
            IsNull = string.Equals(normalized.Kind, "null", StringComparison.Ordinal),
            StringValue = RenderScalar(normalized),
        };
        FlattenMembers(result.Members, "$", normalized);
        return result;
    }

    private static void AddOutcomeMember(
        List<VciProbeMemberObservationInfo> members,
        string name,
        VciProbeObservationOutcomeInfo outcome,
        VciProbeRequestInfo request,
        List<VciProbeOmissionInfo> omissions)
        => members.Add(ToMember(name, outcome, request, omissions));

    private static VciProbeMemberObservationInfo ToMember(
        string name,
        VciProbeObservationOutcomeInfo outcome,
        VciProbeRequestInfo request,
        List<VciProbeOmissionInfo> omissions)
    {
        var normalized = VciProbeValueNormalizer.Normalize(outcome.ReturnValue, request.MaxCollectionItems);
        AddOmissions(normalized, omissions);
        return new VciProbeMemberObservationInfo
        {
            Name = name,
            ClrTypeName = normalized.RuntimeType,
            IsNull = string.Equals(normalized.Kind, "null", StringComparison.Ordinal),
            StringValue = RenderScalar(normalized),
            Exception = ToExceptionInfo(outcome.Exception ?? normalized.PathCanonicalizationException),
        };
    }

    private static void FlattenMembers(
        List<VciProbeMemberObservationInfo> members,
        string name,
        VciProbeNormalizedValueInfo normalized)
    {
        if (normalized.Items.Count > 0)
        {
            for (var index = 0; index < normalized.Items.Count; index++)
            {
                var item = normalized.Items[index];
                var itemName = name + "[" + index + "]";
                members.Add(ToMember(itemName, item));
                FlattenMembers(members, itemName, item);
            }
        }

        if (string.Equals(normalized.Kind, "enum", StringComparison.Ordinal))
        {
            members.Add(ScalarMember(name + ".enumName", normalized.EnumName));
            members.Add(ScalarMember(name + ".enumIntegralValue", normalized.EnumIntegralValue));
        }
        else if (string.Equals(normalized.Kind, "path", StringComparison.Ordinal))
        {
            members.Add(ScalarMember(name + ".pathKind", normalized.PathKind));
            members.Add(ScalarMember(name + ".originalPath", normalized.OriginalPath));
            members.Add(new VciProbeMemberObservationInfo
            {
                Name = name + ".canonicalPath",
                ClrTypeName = typeof(string).FullName ?? nameof(String),
                IsNull = normalized.CanonicalPath is null,
                StringValue = normalized.CanonicalPath,
                Exception = ToExceptionInfo(normalized.PathCanonicalizationException),
            });
        }
    }

    private static VciProbeMemberObservationInfo ToMember(string name, VciProbeNormalizedValueInfo normalized)
        => new()
        {
            Name = name,
            ClrTypeName = normalized.RuntimeType,
            IsNull = string.Equals(normalized.Kind, "null", StringComparison.Ordinal),
            StringValue = RenderScalar(normalized),
            Exception = ToExceptionInfo(normalized.PathCanonicalizationException),
        };

    private static VciProbeMemberObservationInfo ScalarMember(string name, string? value)
        => new()
        {
            Name = name,
            ClrTypeName = typeof(string).FullName ?? nameof(String),
            IsNull = value is null,
            StringValue = value,
        };

    private static string? RenderScalar(VciProbeNormalizedValueInfo normalized)
    {
        if (normalized.StringValue is not null)
        {
            return normalized.StringValue;
        }
        if (string.Equals(normalized.Kind, "enum", StringComparison.Ordinal))
        {
            return (normalized.EnumName ?? "<unnamed>") + " (" + normalized.EnumIntegralValue + ")";
        }
        if (string.Equals(normalized.Kind, "path", StringComparison.Ordinal))
        {
            return normalized.CanonicalPath ?? normalized.OriginalPath;
        }
        return normalized.Kind is "collection" or "null" ? null : normalized.Kind;
    }

    private static void AddOmissions(
        VciProbeNormalizedValueInfo normalized,
        List<VciProbeOmissionInfo> omissions)
    {
        if (normalized.Omission is not null)
        {
            omissions.Add(normalized.Omission);
        }
        foreach (var item in normalized.Items)
        {
            AddOmissions(item, omissions);
        }
    }

    private static VciProbeExceptionInfo? ToExceptionInfo(VciProbeNormalizedExceptionInfo? exception)
        => exception is null
            ? null
            : new VciProbeExceptionInfo
            {
                ExceptionTypeName = exception.ExceptionTypeName,
                Message = exception.Message,
                HResult = exception.HResult,
                InnerException = ToExceptionInfo(exception.InnerException),
            };

    private static void SetNotObservable(VciProbeCaseResultInfo result, string reason)
    {
        result.Outcome = "not_observable";
        result.NotObservableReason = reason;
        result.Return = null;
        result.Snapshot = null;
        result.Exception = null;
        result.Repeatability = null;
    }

    private static string? RawPath(object? value)
        => value switch
        {
            FileInfo file => file.FullName,
            DirectoryInfo directory => directory.FullName,
            string text => text,
            _ => null,
        };

    private static string? CanonicalPath(object? value)
    {
        var raw = RawPath(value);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        try
        {
            return Path.GetFullPath(raw).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (PathTooLongException)
        {
            return null;
        }
        catch (SecurityException)
        {
            return null;
        }
    }

    private static bool IsWithinRoot(string rootPath, string candidatePath)
    {
        var prefix = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return candidatePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class MappedFileCandidate
    {
        public MappedFileCandidate(MappedObject mapping, string path)
        {
            Mapping = mapping;
            Path = path;
        }

        public MappedObject Mapping { get; }
        public string Path { get; }
    }

    private enum GroupFindInput
    {
        Missing,
        Empty,
        Whitespace,
        Null,
    }

    private enum WorkspaceFindInput
    {
        Missing,
        Empty,
        Whitespace,
        Null,
    }
}
