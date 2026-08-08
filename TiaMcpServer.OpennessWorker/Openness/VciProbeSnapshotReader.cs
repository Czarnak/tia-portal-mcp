using System;
using System.Collections.Generic;
using System.IO;
using Siemens.Engineering;
using Siemens.Engineering.VersionControl;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Bounded, read-only VCI evidence reader.  It deliberately keeps Siemens proxies local to one
/// call; selectors are re-resolved on every invocation and are evidence locators, never caches.
/// </summary>
internal static class VciProbeSnapshotReader
{
    internal static VciProbeSnapshotReadResult Read(Project project, VciProbeRequestInfo request)
    {
        var result = new VciProbeSnapshotReadResult();
        var serviceOutcome = Observe(result, project, "GetService<VersionControlInterface>",
            () => project.GetService<VersionControlInterface>());

        result.Snapshot.Service = new VciProbeServiceSnapshotInfo
        {
            ServiceAvailable = serviceOutcome.ReturnValue is not null,
        };
        if (serviceOutcome.ReturnValue is not VersionControlInterface service)
        {
            return result;
        }

        var rootOutcome = Observe(result, project, "WorkspaceGroup", () => service.WorkspaceGroup);
        result.Snapshot.Service.RootGroupAvailable = rootOutcome.ReturnValue is not null;
        if (rootOutcome.ReturnValue is null)
        {
            return result;
        }

        dynamic root = rootOutcome.ReturnValue;
        WalkGroup(result, project, request, root, parentCanonicalKey: null, parentPath: new List<VciGroupPathSegmentInfo>(), depth: 0, parentSnapshot: null);
        return result;
    }

    internal static VciProbeSnapshotReadResult ReadSupportedFormats(Project project, VciProbeRequestInfo request)
    {
        var result = new VciProbeSnapshotReadResult();
        if (request.Workspace is null || request.EngineeringObject is null)
        {
            result.NotObservableReason = "no_workspace_candidate_pair";
            return result;
        }

        var serviceOutcome = Observe(result, project, "GetService<VersionControlInterface>",
            () => project.GetService<VersionControlInterface>());
        if (serviceOutcome.ReturnValue is not VersionControlInterface service)
        {
            result.NotObservableReason = "no_workspace_candidate_pair";
            return result;
        }

        var rootOutcome = Observe(result, project, "WorkspaceGroup", () => service.WorkspaceGroup);
        if (rootOutcome.ReturnValue is null)
        {
            result.NotObservableReason = "no_workspace_candidate_pair";
            return result;
        }

        var workspace = ResolveWorkspace(rootOutcome.ReturnValue, request.Workspace);
        var engineeringObject = VciProbeEngineeringObjectResolver.Resolve(project, request, request.EngineeringObject);
        if (workspace is null || engineeringObject.Candidate is null)
        {
            result.NotObservableReason = "no_workspace_candidate_pair";
            return result;
        }

        // Exactly one invocation: the returned composition is subsequently enumerated in its raw
        // source order, including null/duplicate/empty values, under MaxCollectionItems.
        var formatsOutcome = Observe(result, project, "GetSupportedFileFormats",
            () => ((dynamic)workspace).GetSupportedFileFormats((IEngineeringObject)engineeringObject.Candidate.EngineeringObject));
        if (formatsOutcome.ReturnValue is not System.Collections.IEnumerable formats)
        {
            return result;
        }

        result.Snapshot.CandidateCollectionRuntimeType = formats.GetType().FullName ?? formats.GetType().Name;

        var index = 0;
        foreach (var format in formats)
        {
            if (index >= request.MaxCollectionItems)
            {
                Omit(result, "Supported-format enumeration stopped at the configured collection budget.", nameof(request.MaxCollectionItems), request.MaxCollectionItems, index, "formats");
                break;
            }

            var normalized = VciProbeValueNormalizer.Normalize(format, request.MaxCollectionItems);
            result.Snapshot.Candidates.Add(new VciProbeCandidateSnapshotInfo
            {
                EnumerationIndex = index,
                CanonicalKey = "format:" + index,
                Description = Render(normalized),
                RuntimeTypeName = normalized.RuntimeType,
                IsNull = normalized.Kind == "null",
            });
            index++;
        }

        return result;
    }

    private static void WalkGroup(
        VciProbeSnapshotReadResult result,
        Project project,
        VciProbeRequestInfo request,
        dynamic group,
        string? parentCanonicalKey,
        List<VciGroupPathSegmentInfo> parentPath,
        int depth,
        VciProbeGroupSnapshotInfo? parentSnapshot)
    {
        if (depth > request.MaxGroupDepth)
        {
            Omit(result, "Workspace-group traversal stopped at the configured depth.", nameof(request.MaxGroupDepth), request.MaxGroupDepth, depth - 1, FormatGroupPath(parentPath));
            return;
        }

        var groupsOutcome = Observe(result, project, "Groups", () => group.Groups);
        var workspacesOutcome = Observe(result, project, "Workspaces", () => group.Workspaces);
        if (groupsOutcome.ReturnValue is null && workspacesOutcome.ReturnValue is null)
        {
            return;
        }

        var siblingNames = new Dictionary<string, int>(StringComparer.Ordinal);
        var groupIndex = 0;
        if (groupsOutcome.ReturnValue is System.Collections.IEnumerable groups)
        {
            foreach (var childObject in groups)
            {
                if (result.Snapshot.Groups.Count >= request.MaxGroups)
                {
                    Omit(result, "Workspace-group traversal stopped at the configured group budget.", nameof(request.MaxGroups), request.MaxGroups, result.Snapshot.Groups.Count, FormatGroupPath(parentPath));
                    break;
                }

                dynamic child = childObject;
                var nameOutcome = Observe(result, project, "Name", () => child.Name);
                var name = Render(nameOutcome.ReturnValue, request.MaxCollectionItems);
                siblingNames.TryGetValue(name, out var SameNameOrdinal);
                siblingNames[name] = SameNameOrdinal + 1;
                var canonicalKey = (parentCanonicalKey ?? "root") + "/" + groupIndex + ":" + SameNameOrdinal + ":" + name;
                var childPath = new List<VciGroupPathSegmentInfo>(parentPath)
                {
                    new VciGroupPathSegmentInfo { Index = groupIndex, Name = name, SameNameOrdinal = SameNameOrdinal },
                };
                var snapshot = new VciProbeGroupSnapshotInfo
                {
                    EnumerationIndex = result.Snapshot.Groups.Count,
                    CanonicalKey = canonicalKey,
                    Name = name,
                    Depth = depth + 1,
                    ParentCanonicalKey = parentCanonicalKey,
                };
                result.Snapshot.Groups.Add(snapshot);
                if (parentSnapshot is not null)
                {
                    parentSnapshot.ChildGroupCount++;
                }
                else
                {
                    result.Snapshot.Service!.RootGroupCount++;
                }
                WalkGroup(result, project, request, child, canonicalKey, childPath, depth + 1, snapshot);
                groupIndex++;
            }
        }

        // The root's own workspaces are also evidence and must be observed without conflating
        // them with service acquisition or child-group enumeration.
        WalkWorkspaces(result, project, request, workspacesOutcome.ReturnValue, parentCanonicalKey ?? "root", parentPath, parentSnapshot);
    }

    private static void WalkWorkspaces(VciProbeSnapshotReadResult result, Project project, VciProbeRequestInfo request, object? workspacesValue, string groupKey, List<VciGroupPathSegmentInfo> groupPath, VciProbeGroupSnapshotInfo? groupSnapshot)
    {
        if (workspacesValue is not System.Collections.IEnumerable workspaces)
        {
            return;
        }

        var index = 0;
        foreach (var workspaceObject in workspaces)
        {
            if (result.Snapshot.Workspaces.Count >= request.MaxWorkspaces)
            {
                Omit(result, "Workspace enumeration stopped at the configured workspace budget.", nameof(request.MaxWorkspaces), request.MaxWorkspaces, result.Snapshot.Workspaces.Count, FormatGroupPath(groupPath));
                break;
            }

            dynamic workspace = workspaceObject;
            var name = Render(Observe(result, project, "Name", () => workspace.Name).ReturnValue, request.MaxCollectionItems);
            var root = Observe(result, project, "RootPath", () => workspace.RootPath);
            var comment = Observe(result, project, "Comment", () => workspace.Comment);
            var language = Observe(result, project, "WorkspaceLanguage", () => workspace.WorkspaceLanguage);
            var library = Observe(result, project, "GlobalLibraryPath", () => workspace.GlobalLibraryPath);
            var deleteUnused = Observe(result, project, "DeleteUnusedTypeVersionFromLibrary", () => workspace.DeleteUnusedTypeVersionFromLibrary);
            var workspaceSnapshot = new VciProbeWorkspaceSnapshotInfo
            {
                EnumerationIndex = result.Snapshot.Workspaces.Count,
                CanonicalKey = groupKey + "/workspace:" + index + ":" + name,
                Name = name,
                RootPath = RenderCanonicalPath(root.ReturnValue),
                Comment = Render(comment.ReturnValue, request.MaxCollectionItems),
                WorkspaceLanguage = Render(language.ReturnValue, request.MaxCollectionItems),
                GlobalLibraryPath = Render(library.ReturnValue, request.MaxCollectionItems),
                DeleteUnusedTypeVersionFromLibrary = deleteUnused.ReturnValue is bool value && value,
            };
            result.Snapshot.Workspaces.Add(workspaceSnapshot);
            if (groupSnapshot is not null)
            {
                groupSnapshot.WorkspaceCount++;
            }
            WalkMappings(result, project, request, workspace, groupPath, workspaceSnapshot);
            index++;
        }
    }

    private static void WalkMappings(VciProbeSnapshotReadResult result, Project project, VciProbeRequestInfo request, dynamic workspace, List<VciGroupPathSegmentInfo> groupPath, VciProbeWorkspaceSnapshotInfo workspaceSnapshot)
    {
        var mappingsOutcome = Observe(result, project, "MappedObjects", () => workspace.MappedObjects);
        if (mappingsOutcome.ReturnValue is not System.Collections.IEnumerable mappings)
        {
            return;
        }

        var index = 0;
        foreach (var mappedObject in mappings)
        {
            if (result.Snapshot.Mappings.Count >= request.MaxMappings)
            {
                Omit(result, "Mapping enumeration stopped at the configured mapping budget.", nameof(request.MaxMappings), request.MaxMappings, result.Snapshot.Mappings.Count, FormatGroupPath(groupPath));
                break;
            }

            dynamic mapping = mappedObject;
            var directory = Observe(result, project, "DirectoryPath", () => mapping.DirectoryPath);
            var file = Observe(result, project, "FileNameWithoutExtension", () => mapping.FileNameWithoutExtension);
            var format = Observe(result, project, "FileFormat", () => mapping.FileFormat);
            var objectOutcome = Observe(result, project, "EngineeringObject", () => mapping.EngineeringObject);
            var status = Observe(result, project, "Status", () => mapping.Status);
            var getStatus = Observe(result, project, "GetStatus", () => mapping.GetStatus());
            var childStatus = Observe(result, project, "GetChildStatus", () => mapping.GetChildStatus());
            var mappingSelector = new VciMappingSelectorInfo
            {
                Workspace = new VciWorkspaceSelectorInfo { GroupPath = new List<VciGroupPathSegmentInfo>(groupPath), WorkspaceName = workspaceSnapshot.Name, CanonicalRootPath = workspaceSnapshot.RootPath },
                EngineeringObject = FindEngineeringObjectSelector(project, request, objectOutcome.ReturnValue),
                RelativeDirectory = Render(directory.ReturnValue, request.MaxCollectionItems),
                FileName = Render(file.ReturnValue, request.MaxCollectionItems),
                Format = Render(format.ReturnValue, request.MaxCollectionItems),
            };
            result.Snapshot.Mappings.Add(new VciProbeMappingSnapshotInfo
            {
                EnumerationIndex = result.Snapshot.Mappings.Count,
                CanonicalKey = workspaceSnapshot.CanonicalKey + "/mapping:" + index,
                Selector = mappingSelector,
                Status = Render(getStatus.ReturnValue, request.MaxCollectionItems),
                StatusProperty = Render(status.ReturnValue, request.MaxCollectionItems),
                GetStatus = Render(getStatus.ReturnValue, request.MaxCollectionItems),
                ChildStatus = Render(childStatus.ReturnValue, request.MaxCollectionItems),
            });
            workspaceSnapshot.MappedObjectCount++;
            index++;
        }
    }

    private static object? ResolveWorkspace(object root, VciWorkspaceSelectorInfo selector)
    {
        dynamic current = root;
        foreach (var segment in selector.GroupPath)
        {
            var index = 0;
            object? matched = null;
            var matchedCount = 0;
            var sameNameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var child in (System.Collections.IEnumerable)current.Groups)
            {
                var name = ((dynamic)child).Name as string ?? string.Empty;
                sameNameCounts.TryGetValue(name, out var sameNameOrdinal);
                sameNameCounts[name] = sameNameOrdinal + 1;
                if (index == segment.Index
                    && string.Equals(name, segment.Name, StringComparison.Ordinal)
                    && sameNameOrdinal == segment.SameNameOrdinal)
                {
                    matched = child;
                    matchedCount++;
                }
                index++;
            }
            if (matched is null || matchedCount != 1)
            {
                return null;
            }
            current = matched;
        }

        object? workspaceMatch = null;
        var workspaceMatchCount = 0;
        foreach (var workspace in (System.Collections.IEnumerable)current.Workspaces)
        {
            dynamic candidate = workspace;
            var canonicalRootPath = RenderCanonicalPath(candidate.RootPath);
            if (string.Equals(candidate.Name as string, selector.WorkspaceName, StringComparison.Ordinal)
                && string.Equals(canonicalRootPath, selector.CanonicalRootPath, StringComparison.Ordinal))
            {
                workspaceMatch = workspace;
                workspaceMatchCount++;
            }
        }
        return workspaceMatchCount == 1 ? workspaceMatch : null;
    }

    private static VciProbeObservationOutcomeInfo Observe(VciProbeSnapshotReadResult result, Project project, string name, Func<object?> read)
    {
        var outcome = VciProbeObservationRunner.Run(() => project.IsModified, read);
        var normalized = VciProbeValueNormalizer.Normalize(outcome.ReturnValue, 1);
        var failure = outcome.Exception ?? normalized.PathCanonicalizationException;
        result.Members.Add(new VciProbeMemberObservationInfo
        {
            Name = name,
            ClrTypeName = normalized.RuntimeType,
            IsNull = outcome.ReturnValue is null,
            StringValue = failure is null ? Render(normalized) : failure.ExceptionTypeName + ": " + failure.Message,
            Exception = ToExceptionInfo(failure),
        });
        return outcome;
    }

    private static void Omit(VciProbeSnapshotReadResult result, string reason, string budgetName, int budgetValue, int observedCount, string traversalPath)
        => result.Omissions.Add(new VciProbeOmissionInfo { Reason = reason, BudgetName = budgetName, BudgetValue = budgetValue, ObservedCount = observedCount, TraversalPath = traversalPath });

    private static VciEngineeringObjectSelectorInfo FindEngineeringObjectSelector(Project project, VciProbeRequestInfo request, object? engineeringObject)
    {
        if (engineeringObject is null)
        {
            return new VciEngineeringObjectSelectorInfo();
        }

        foreach (var candidate in VciProbeEngineeringObjectCatalog.Enumerate(project, request).Candidates)
        {
            if (Equals(candidate.EngineeringObject, engineeringObject))
            {
                return candidate.Selector;
            }
        }

        return new VciEngineeringObjectSelectorInfo();
    }

    private static VciProbeExceptionInfo? ToExceptionInfo(VciProbeNormalizedExceptionInfo? exception)
        => exception is null
            ? null
            : new VciProbeExceptionInfo
            {
                ExceptionTypeName = exception.ExceptionTypeName,
                Message = exception.Message,
                HResult = exception.HResult,
            };

    private static string FormatGroupPath(IReadOnlyList<VciGroupPathSegmentInfo> path)
    {
        var result = "root";
        foreach (var segment in path)
        {
            result += "/" + segment.Index + ":" + segment.SameNameOrdinal + ":" + segment.Name;
        }
        return result;
    }

    private static string Render(object? value, int maxCollectionItems) => Render(VciProbeValueNormalizer.Normalize(value, maxCollectionItems));

    private static string? RenderCanonicalPath(object? value)
    {
        var normalized = VciProbeValueNormalizer.Normalize(value, 1);
        return normalized.Kind == "path" ? normalized.CanonicalPath : Render(normalized);
    }

    private static string Render(VciProbeNormalizedValueInfo normalized)
        => normalized.StringValue ?? normalized.CanonicalPath ?? normalized.OriginalPath ?? normalized.EnumName ?? normalized.EnumIntegralValue ?? string.Empty;
}

internal sealed class VciProbeSnapshotReadResult
{
    public VciProbeSnapshotInfo Snapshot { get; } = new VciProbeSnapshotInfo();
    public List<VciProbeMemberObservationInfo> Members { get; } = new List<VciProbeMemberObservationInfo>();
    public List<VciProbeOmissionInfo> Omissions { get; } = new List<VciProbeOmissionInfo>();
    public string? NotObservableReason { get; set; }
}
