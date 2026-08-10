using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Siemens.Engineering;
using Siemens.Engineering.VersionControl;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Internal-only, closed-catalogue VCI mutation evidence probe. Every persistent mutation uses
/// one exclusive-access transaction, verifies bounded post-call state and a read-only canary,
/// and requests commit only after those checks pass.
/// </summary>
internal static class VciMutationContractProbeService
{
    private const string RequiredFixtureState = "required_fixture_state_not_available";
    private const string SignatureDoesNotPermitArgument = "signature_does_not_permit_argument";
    private const string HarnessConfinementRejected = "harness_confinement_rejected_before_worker";

    public static VciMutationProbeCaseResultInfo Execute(
        TiaPortal currentPortal,
        Project project,
        VciMutationProbeRequestInfo request)
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

        var validationError = VciMutationProbeContract.Validate(request);
        if (validationError is not null)
        {
            throw new ArgumentException(validationError, nameof(request));
        }

        var result = NewResult(request);
        var isModifiedBefore = project.IsModified;
        try
        {
            Action dispatch = request.CaseId switch
            {
                "P-INVENTORY" => () => RunInventory(project, request, result),
                "M-CANARY" => () => RunCanaryCase(project, request, result),
                "M-GROUP" => () => RunCreateGroups(currentPortal, project, request, result),
                "M-WORKSPACE-ROOT" => () => RunCreateWorkspace(currentPortal, project, request, result, withLanguage: false),
                "M-WORKSPACE-LANGUAGE" => () => RunCreateWorkspace(currentPortal, project, request, result, withLanguage: true),
                "M-EXPORT" => () => RunExport(currentPortal, project, request, result),
                "M-DISCONNECT" => () => RunDisconnect(currentPortal, project, request, result),
                "M-CONNECT" => () => RunConnect(currentPortal, project, request, result),
                "M-P2W" => () => RunSynchronization(currentPortal, project, request, result, SynchronizationMode.ProjectToWorkspace),
                "M-W2P" => () => RunSynchronization(currentPortal, project, request, result, SynchronizationMode.WorkspaceToProject),
                "M-DELETE-MAPPING" => () => RunDeleteMapping(currentPortal, project, request, result),
                "M-DELETE-WORKSPACE" => () => RunDeleteWorkspace(currentPortal, project, request, result),
                "M-DELETE-GROUP" => () => RunDeleteGroups(currentPortal, project, request, result),
                "M-TX-GROUP" => () => RunTransactionCase(currentPortal, project, request, result, TransactionMutation.Group),
                "M-TX-WORKSPACE" => () => RunTransactionCase(currentPortal, project, request, result, TransactionMutation.Workspace),
                "M-TX-EXPORT" => () => RunTransactionCase(currentPortal, project, request, result, TransactionMutation.Export),
                "M-TX-CONNECT" => () => RunTransactionCase(currentPortal, project, request, result, TransactionMutation.Connect),
                "M-TX-P2W" => () => RunTransactionCase(currentPortal, project, request, result, TransactionMutation.ProjectToWorkspace),
                "M-TX-W2P" => () => RunTransactionCase(currentPortal, project, request, result, TransactionMutation.WorkspaceToProject),
                "M-TX-DISCONNECT" => () => RunTransactionCase(currentPortal, project, request, result, TransactionMutation.Disconnect),
                "M-TX-DELETE-WORKSPACE" => () => RunTransactionCase(currentPortal, project, request, result, TransactionMutation.DeleteWorkspace),
                "M-TX-DELETE-GROUP" => () => RunTransactionCase(currentPortal, project, request, result, TransactionMutation.DeleteGroup),
                "N-GROUP-NULL" => () => RunGroupNameNegative(currentPortal, project, request, result, GroupNameInput.Null),
                "N-GROUP-EMPTY" => () => RunGroupNameNegative(currentPortal, project, request, result, GroupNameInput.Empty),
                "N-GROUP-WHITESPACE" => () => RunGroupNameNegative(currentPortal, project, request, result, GroupNameInput.Whitespace),
                "N-GROUP-DUPLICATE" => () => RunGroupNameNegative(currentPortal, project, request, result, GroupNameInput.Duplicate),
                "N-GROUP-INVALID" => () => RunGroupNameNegative(currentPortal, project, request, result, GroupNameInput.Invalid),
                "N-WORKSPACE-NULL" => () => RunWorkspaceNameNegative(currentPortal, project, request, result, WorkspaceNameInput.Null),
                "N-WORKSPACE-EMPTY" => () => RunWorkspaceNameNegative(currentPortal, project, request, result, WorkspaceNameInput.Empty),
                "N-WORKSPACE-WHITESPACE" => () => RunWorkspaceNameNegative(currentPortal, project, request, result, WorkspaceNameInput.Whitespace),
                "N-WORKSPACE-DUPLICATE" => () => RunWorkspaceNameNegative(currentPortal, project, request, result, WorkspaceNameInput.Duplicate),
                "N-WORKSPACE-INVALID" => () => RunWorkspaceNameNegative(currentPortal, project, request, result, WorkspaceNameInput.Invalid),
                "N-WORKSPACE-PATH-RELATIVE" => () => RunHarnessConfinementOnly(result),
                "N-WORKSPACE-PATH-MISSING-PARENT" => () => RunHarnessConfinementOnly(result),
                "N-WORKSPACE-PATH-CONFLICT" => () => RunHarnessConfinementOnly(result),
                "N-WORKSPACE-PATH-FILE" => () => RunHarnessConfinementOnly(result),
                "N-WORKSPACE-LANGUAGE-NULL" => () => RunNullWorkspaceLanguage(currentPortal, project, request, result),
                "N-WORKSPACE-LANGUAGE-INVALID" => () => SetNotObservable(result, SignatureDoesNotPermitArgument),
                "N-WORKSPACE-GLOBAL-LIBRARY-NULL" => () => RunGlobalLibraryNegative(currentPortal, project, request, result, useNull: true),
                "N-WORKSPACE-GLOBAL-LIBRARY-INVALID" => () => RunGlobalLibraryNegative(currentPortal, project, request, result, useNull: false),
                "N-OBJECT-NULL" => () => RunObjectNegative(currentPortal, project, request, result, ObjectInput.Null),
                "N-OBJECT-UNSUPPORTED" => () => RunObjectNegative(currentPortal, project, request, result, ObjectInput.Unsupported),
                "N-OBJECT-FOREIGN" => () => RunUnavailableTask5Case(result),
                "N-OBJECT-DISPOSED" => () => RunUnavailableTask5Case(result),
                "N-OBJECT-ALREADY-MAPPED" => () => RunObjectNegative(currentPortal, project, request, result, ObjectInput.AlreadyMapped),
                "N-OBJECT-DELETED" => () => RunUnavailableTask5Case(result),
                "N-FORMAT-NULL" => () => RunFormatNegative(currentPortal, project, request, result, FormatInput.Null),
                "N-FORMAT-EMPTY" => () => RunFormatNegative(currentPortal, project, request, result, FormatInput.Empty),
                "N-FORMAT-UNSUPPORTED" => () => RunFormatNegative(currentPortal, project, request, result, FormatInput.Unsupported),
                "N-FORMAT-WRONG-CASE" => () => RunFormatNegative(currentPortal, project, request, result, FormatInput.WrongCase),
                "N-FORMAT-MISMATCH" => () => RunFormatNegative(currentPortal, project, request, result, FormatInput.Mismatch),
                "N-FILENAME-INVALID" => () => RunInvalidFilename(currentPortal, project, request, result),
                "N-FILENAME-ABSOLUTE" => () => RunHarnessConfinementOnly(result),
                "N-FILENAME-TRAVERSAL" => () => RunHarnessConfinementOnly(result),
                "N-FILENAME-COLLISION" => () => RunFilenameCollision(currentPortal, project, request, result),
                "N-CONNECT-MISSING" => () => RunConnectNegative(currentPortal, project, request, result, ConnectInput.Missing),
                "N-CONNECT-MALFORMED" => () => RunConnectNegative(currentPortal, project, request, result, ConnectInput.Malformed),
                "N-CONNECT-WRONG-OBJECT" => () => RunUnavailableTask5Case(result),
                "N-CONNECT-PARTIAL-FILE-SET" => () => RunConnectNegative(currentPortal, project, request, result, ConnectInput.PartialFileSet),
                "N-SYNC-MISSING" => () => RunSynchronizationNegative(currentPortal, project, request, result, SynchronizationInput.Missing),
                "N-SYNC-MALFORMED" => () => RunSynchronizationNegative(currentPortal, project, request, result, SynchronizationInput.Malformed),
                "N-SYNC-UNCHANGED" => () => RunSynchronizationNegative(currentPortal, project, request, result, SynchronizationInput.Unchanged),
                "N-SYNC-PROJECT-ONLY" => () => RunSynchronizationNegative(currentPortal, project, request, result, SynchronizationInput.ProjectOnly),
                "N-SYNC-WORKSPACE-ONLY" => () => RunSynchronizationNegative(currentPortal, project, request, result, SynchronizationInput.WorkspaceOnly),
                "N-SYNC-BOTH-SIDES" => () => RunSynchronizationNegative(currentPortal, project, request, result, SynchronizationInput.BothSides),
                "N-SYNC-INVALID-ENUM" => () => RunInvalidSynchronizationMode(currentPortal, project, request, result),
                "N-DELETE-NONEMPTY" => () => RunDeleteNonemptyGroup(currentPortal, project, request, result),
                "N-DELETE-TWICE" => () => RunDeleteTwice(currentPortal, project, request, result),
                "N-STALE-MAPPING-PROXY" => () => RunStaleMappingProxy(currentPortal, project, request, result),
                _ => throw new ArgumentException(
                    "The VCI mutation-probe case ID is not in the locked vocabulary.",
                    nameof(request)),
            };

            dispatch();
            CompleteApplyObservation(project, request, result);
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

    private static void CompleteApplyObservation(
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result)
    {
        if (!string.Equals(request.Mode, "Apply", StringComparison.Ordinal))
        {
            return;
        }

        result.Before ??= CaptureSnapshot(project, request, result);
        result.After ??= CaptureSnapshot(project, request, result);
        if (!result.Canary.Attempted)
        {
            result.Canary = RunCanary(project, request, result);
        }
    }

    private static VciMutationProbeCaseResultInfo NewResult(VciMutationProbeRequestInfo request)
        => new()
        {
            SchemaVersion = VciMutationProbeContract.SchemaVersion,
            RunId = request.RunId,
            SessionId = request.SessionId,
            ScenarioId = request.ScenarioId,
            CaseId = request.CaseId,
            CaseInstanceId = request.CaseInstanceId,
            InvocationLayer = "worker_typed_openness",
            InputCategory = ClassifyInput(request.CaseId),
            SanitizedArguments = new List<VciMutationArgumentInfo>
            {
                new() { Name = "caseId", Category = "locked_vocabulary", Value = request.CaseId },
                new() { Name = "workspaceRoot", Category = "canonical_absolute_path", Value = CanonicalPath(request.WorkspaceRoot) },
            },
        };

    private static string ClassifyInput(string caseId)
    {
        if (string.Equals(caseId, "P-INVENTORY", StringComparison.Ordinal))
        {
            return "inventory";
        }

        if (string.Equals(caseId, "M-CANARY", StringComparison.Ordinal))
        {
            return "canary";
        }

        if (caseId.StartsWith("M-TX-", StringComparison.Ordinal))
        {
            return "transaction_characterization";
        }

        return caseId.StartsWith("N-", StringComparison.Ordinal) ? "negative" : "positive";
    }

    private static void RunInventory(
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result)
    {
        var rootAbsentBefore = !Directory.Exists(request.WorkspaceRoot) && !File.Exists(request.WorkspaceRoot);
        AddCheck(result.Preconditions, "workspace_root_absent_before_inventory", rootAbsentBefore, null);
        if (!rootAbsentBefore)
        {
            SetNotObservable(result, RequiredFixtureState);
            return;
        }

        TraceProgress(request, "before_snapshot:start");
        result.Before = CaptureSnapshot(project, request, result);
        TraceProgress(request, "before_snapshot:complete");
        TraceProgress(request, "workspace_root:start");
        if (!TryAcquireRoot(project, result, out _, out var root))
        {
            SetNotObservableUnlessTerminal(result, "selected_workspace_not_found");
            return;
        }
        TraceProgress(request, "workspace_root:complete");

        var readRequest = ToReadRequest(request);
        TraceProgress(request, "engineering_object_catalog:start");
        var catalog = VciProbeEngineeringObjectCatalog.Enumerate(project, readRequest);
        TraceProgress(request, "engineering_object_catalog:complete");
        result.Omissions.AddRange(catalog.Omissions);
        var objectMatches = catalog.Candidates
            .Where(candidate => StructuralPathsEqual(
                candidate.Selector.StructuralPath,
                request.EngineeringObject!.StructuralPath))
            .Take(2)
            .ToList();
        if (objectMatches.Count != 1
            || objectMatches[0].EngineeringObject is not IEngineeringObject engineeringObject)
        {
            SetNotObservable(result, "selected_engineering_object_not_found");
            return;
        }
        var objectCandidate = objectMatches[0];

        var selectedSimulationDb = request.EngineeringObject!.StructuralPath.Count > 0
            && string.Equals(
                request.EngineeringObject.StructuralPath[
                    request.EngineeringObject.StructuralPath.Count - 1].Name,
                "Simulation_DB",
                StringComparison.Ordinal);
        AddCheck(result.Preconditions, "selected_engineering_object_is_Simulation_DB", selectedSimulationDb, null);
        if (!selectedSimulationDb)
        {
            SetNotObservable(result, RequiredFixtureState);
            return;
        }

        try
        {
            TraceProgress(request, "supported_format_discovery:start");
            var workspaceSelection = FindInventoryWorkspace(
                root!,
                engineeringObject,
                request,
                result.Omissions);
            TraceProgress(request, "supported_format_discovery:complete");
            if (workspaceSelection is null)
            {
                SetNotObservable(result, "selected_workspace_not_found");
                return;
            }

            AddCheck(result.Preconditions, "exact_SimaticML_supported", true, null);
            result.Return = InventoryReturn(objectCandidate, workspaceSelection);
            result.Outcome = "returned";
        }
        catch (NonRecoverableException)
        {
            throw;
        }
        catch (Exception exception) when (IsEvidenceException(exception))
        {
            RecordException(result, exception);
        }

        TraceProgress(request, "after_snapshot:start");
        result.After = CaptureSnapshot(project, request, result);
        TraceProgress(request, "after_snapshot:complete");
        var rootAbsentAfter = !Directory.Exists(request.WorkspaceRoot) && !File.Exists(request.WorkspaceRoot);
        AddCheck(result.SafetyInvariants, "workspace_root_absent_after_inventory", rootAbsentAfter, null);
        if (!rootAbsentAfter)
        {
            result.UncertainOutcome = true;
            result.StopScenarioFamily = true;
        }
    }

    private static bool StructuralPathsEqual(
        IReadOnlyList<VciEngineeringObjectPathSegmentInfo> left,
        IReadOnlyList<VciEngineeringObjectPathSegmentInfo> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index].Index != right[index].Index
                || !string.Equals(left[index].Name, right[index].Name, StringComparison.Ordinal)
                || !string.Equals(left[index].ObjectType, right[index].ObjectType, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static InventoryWorkspaceSelection? FindInventoryWorkspace(
        WorkspaceSystemGroup root,
        IEngineeringObject engineeringObject,
        VciMutationProbeRequestInfo request,
        List<VciProbeOmissionInfo> omissions)
    {
        var groupsVisited = 0;
        var workspacesVisited = 0;
        return FindInventoryWorkspaceInGroup(
            root,
            engineeringObject,
            request,
            omissions,
            new List<VciGroupPathSegmentInfo>(),
            depth: 0,
            ref groupsVisited,
            ref workspacesVisited);
    }

    private static InventoryWorkspaceSelection? FindInventoryWorkspaceInGroup(
        WorkspaceGroup group,
        IEngineeringObject engineeringObject,
        VciMutationProbeRequestInfo request,
        List<VciProbeOmissionInfo> omissions,
        List<VciGroupPathSegmentInfo> groupPath,
        int depth,
        ref int groupsVisited,
        ref int workspacesVisited)
    {
        foreach (var workspace in group.Workspaces)
        {
            if (workspacesVisited >= request.MaxWorkspaces)
            {
                omissions.Add(new VciProbeOmissionInfo
                {
                    Reason = "Inventory workspace discovery stopped at the configured workspace budget.",
                    BudgetName = nameof(VciMutationProbeRequestInfo.MaxWorkspaces),
                    BudgetValue = request.MaxWorkspaces,
                    ObservedCount = workspacesVisited,
                });
                return null;
            }

            workspacesVisited++;
            var formats = new List<string>();
            foreach (var format in workspace.GetSupportedFileFormats(engineeringObject))
            {
                if (formats.Count >= request.MaxCollectionItems)
                {
                    omissions.Add(new VciProbeOmissionInfo
                    {
                        Reason = "Supported-format enumeration stopped at the configured collection budget.",
                        BudgetName = nameof(VciMutationProbeRequestInfo.MaxCollectionItems),
                        BudgetValue = request.MaxCollectionItems,
                        ObservedCount = formats.Count,
                    });
                    break;
                }

                formats.Add(format);
            }

            if (formats.Contains("SimaticML", StringComparer.Ordinal))
            {
                return new InventoryWorkspaceSelection(
                    workspace,
                    new VciWorkspaceSelectorInfo
                    {
                        GroupPath = new List<VciGroupPathSegmentInfo>(groupPath),
                        WorkspaceName = workspace.Name,
                        CanonicalRootPath = CanonicalPath(workspace.RootPath),
                    },
                    formats);
            }
        }

        if (depth >= request.MaxGroupDepth)
        {
            omissions.Add(new VciProbeOmissionInfo
            {
                Reason = "Inventory workspace discovery stopped at the configured group depth.",
                BudgetName = nameof(VciMutationProbeRequestInfo.MaxGroupDepth),
                BudgetValue = request.MaxGroupDepth,
                ObservedCount = depth,
            });
            return null;
        }

        var siblingNameOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
        var groupIndex = 0;
        foreach (var child in group.Groups)
        {
            if (groupsVisited >= request.MaxGroups)
            {
                omissions.Add(new VciProbeOmissionInfo
                {
                    Reason = "Inventory workspace discovery stopped at the configured group budget.",
                    BudgetName = nameof(VciMutationProbeRequestInfo.MaxGroups),
                    BudgetValue = request.MaxGroups,
                    ObservedCount = groupsVisited,
                });
                return null;
            }

            siblingNameOrdinals.TryGetValue(child.Name, out var sameNameOrdinal);
            siblingNameOrdinals[child.Name] = sameNameOrdinal + 1;
            groupsVisited++;
            var childPath = new List<VciGroupPathSegmentInfo>(groupPath)
            {
                new()
                {
                    Index = groupIndex,
                    Name = child.Name,
                    SameNameOrdinal = sameNameOrdinal,
                },
            };
            var match = FindInventoryWorkspaceInGroup(
                child,
                engineeringObject,
                request,
                omissions,
                childPath,
                depth + 1,
                ref groupsVisited,
                ref workspacesVisited);
            if (match is not null)
            {
                return match;
            }

            groupIndex++;
        }

        return null;
    }

    private static VciProbeReturnInfo InventoryReturn(
        VciProbeEngineeringObjectCandidate objectCandidate,
        InventoryWorkspaceSelection workspaceSelection)
    {
        var result = new VciProbeReturnInfo
        {
            ClrTypeName = typeof(InventoryWorkspaceSelection).FullName ?? nameof(InventoryWorkspaceSelection),
        };
        result.Members.Add(Member("workspace.name", workspaceSelection.Selector.WorkspaceName));
        result.Members.Add(Member("workspace.canonicalRootPath", workspaceSelection.Selector.CanonicalRootPath));
        result.Members.Add(Member("workspace.groupPath", RenderGroupPath(workspaceSelection.Selector.GroupPath)));
        result.Members.Add(Member("engineeringObject.runtimeType", objectCandidate.RuntimeTypeName));
        result.Members.Add(Member("engineeringObject.stableIdentifier", objectCandidate.Selector.StableIdentifier));
        result.Members.Add(Member("engineeringObject.fingerprint", objectCandidate.Fingerprint));
        result.Members.Add(Member("engineeringObject.structuralPath", RenderStructuralPath(objectCandidate.Selector.StructuralPath)));
        for (var index = 0; index < workspaceSelection.Formats.Count; index++)
        {
            result.Members.Add(Member("fileFormat[" + index + "]", workspaceSelection.Formats[index]));
        }

        return result;
    }

    private static string RenderGroupPath(IReadOnlyList<VciGroupPathSegmentInfo> path)
        => string.Join(
            "/",
            path.Select(segment => segment.Index.ToString(CultureInfo.InvariantCulture)
                + ":" + segment.SameNameOrdinal.ToString(CultureInfo.InvariantCulture)
                + ":" + segment.Name));

    private static string RenderStructuralPath(IReadOnlyList<VciEngineeringObjectPathSegmentInfo> path)
        => string.Join(
            "/",
            path.Select(segment => segment.Index.ToString(CultureInfo.InvariantCulture)
                + ":" + segment.ObjectType
                + ":" + segment.Name));

    private static void RunCanaryCase(
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result)
    {
        result.Before = CaptureSnapshot(project, request, result);
        result.Canary = RunCanary(project, request, result);
        result.After = CaptureSnapshot(project, request, result);
        result.Outcome = result.Canary.Usable ? "returned" : "not_observable";
        if (!result.Canary.Usable)
        {
            result.NotObservableReason = RequiredFixtureState;
        }
    }

    private static void RunCreateGroups(
        TiaPortal tiaPortal,
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result)
    {
        if (!TryAcquireRoot(project, result, out _, out var root))
        {
            return;
        }

        var names = ScenarioNames(request);
        if (root!.Groups.Find(names.Group) is not null)
        {
            SetNotObservable(result, RequiredFixtureState);
            return;
        }

        RunCommittedMutation(tiaPortal, project, request, result, expectThrow: false, commitOnSuccess: true, () =>
        {
            var group = root.Groups.Create(names.Group);
            var nested = group.Groups.Create(names.NestedGroup);
            return nested.Name;
        });
    }

    private static void RunCreateWorkspace(
        TiaPortal tiaPortal,
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result,
        bool withLanguage)
    {
        if (!TryAcquireScenarioGroup(project, request, result, out var group))
        {
            return;
        }

        var names = ScenarioNames(request);
        var workspaceName = string.IsNullOrWhiteSpace(request.WorkspaceName)
            ? withLanguage ? names.LanguageWorkspace : names.RootWorkspace
            : request.WorkspaceName!;
        if (!workspaceName.StartsWith(names.Prefix, StringComparison.Ordinal))
        {
            SetNotObservable(result, RequiredFixtureState);
            return;
        }
        if (group!.Workspaces.Find(workspaceName) is not null)
        {
            SetNotObservable(result, RequiredFixtureState);
            return;
        }

        var path = ScenarioWorkspacePath(request, workspaceName);
        RunCommittedMutation(tiaPortal, project, request, result, expectThrow: false, commitOnSuccess: true, () =>
        {
            var workspace = withLanguage
                ? group.Workspaces.Create(
                    workspaceName,
                    new DirectoryInfo(path),
                    CultureInfo.GetCultureInfo("en-US"))
                : group.Workspaces.Create(workspaceName, new DirectoryInfo(path));
            return workspace.Name;
        });
    }

    private static void RunExport(
        TiaPortal tiaPortal,
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result)
    {
        if (!TryResolveWorkspaceAndEngineeringObject(
                project, request, result, out var workspace, out var engineeringObject))
        {
            return;
        }

        var formats = workspace!.GetSupportedFileFormats(engineeringObject!).ToList();
        if (!formats.Contains("SimaticML", StringComparer.Ordinal))
        {
            SetNotObservable(result, "selected_format_not_supported");
            return;
        }

        var target = ResolveExportTarget(workspace, request, result);
        if (target is null)
        {
            return;
        }

        if (workspace.MappedObjects.Find(engineeringObject!) is not null)
        {
            SetNotObservable(result, RequiredFixtureState);
            return;
        }

        RunCommittedMutation(tiaPortal, project, request, result, expectThrow: false, commitOnSuccess: true, () =>
        {
            _ = workspace.ExportObject(
                engineeringObject!,
                target.Directory,
                target.FileNameWithoutExtension,
                "SimaticML");
            var mapping = workspace.MappedObjects.Find(engineeringObject!);
            if (mapping is null)
            {
                throw new InvalidOperationException("ExportObject returned without a rediscoverable mapping.");
            }
            return BuildMappingReturn(
                workspace,
                mapping,
                target.Directory,
                target.FileNameWithoutExtension,
                request.MaxCollectionItems,
                result.Omissions);
        });
    }

    private static void RunDisconnect(
        TiaPortal tiaPortal,
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result)
    {
        var mapping = ResolveMapping(project, request, result);
        if (mapping is null)
        {
            return;
        }

        var directory = mapping.DirectoryPath;
        var fileNameWithoutExtension = mapping.FileNameWithoutExtension;
        RunCommittedMutation(tiaPortal, project, request, result, expectThrow: false, commitOnSuccess: true, () =>
        {
            var filesBefore = CaptureBoundedFileSet(
                directory,
                fileNameWithoutExtension,
                request.MaxCollectionItems,
                result.Omissions);
            mapping.Delete();
            var filesAfter = CaptureBoundedFileSet(
                directory,
                fileNameWithoutExtension,
                request.MaxCollectionItems,
                result.Omissions);
            return BuildFileTransitionReturn("MappedObject.Delete", filesBefore, filesAfter);
        });
    }

    private static void RunConnect(
        TiaPortal tiaPortal,
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result)
    {
        if (!TryResolveWorkspaceAndEngineeringObject(
                project, request, result, out var workspace, out var engineeringObject))
        {
            return;
        }

        var formats = workspace!.GetSupportedFileFormats(engineeringObject!).ToList();
        if (!formats.Contains("SimaticML", StringComparer.Ordinal))
        {
            SetNotObservable(result, "selected_format_not_supported");
            return;
        }

        var target = ResolveExportTarget(workspace, request, result);
        if (target is null)
        {
            return;
        }

        var retainedFiles = CaptureBoundedFileSet(
            target.Directory,
            target.FileNameWithoutExtension,
            request.MaxCollectionItems,
            result.Omissions);
        if (retainedFiles.Count == 0 || workspace.MappedObjects.Find(engineeringObject!) is not null)
        {
            SetNotObservable(result, RequiredFixtureState);
            return;
        }

        RunCommittedMutation(tiaPortal, project, request, result, expectThrow: false, commitOnSuccess: true, () =>
        {
            _ = workspace.ConnectObject(
                engineeringObject!,
                target.Directory,
                target.FileNameWithoutExtension,
                "SimaticML");
            var mapping = workspace.MappedObjects.Find(engineeringObject!);
            if (mapping is null)
            {
                throw new InvalidOperationException("ConnectObject returned without a rediscoverable mapping.");
            }
            return BuildMappingReturn(
                workspace,
                mapping,
                target.Directory,
                target.FileNameWithoutExtension,
                request.MaxCollectionItems,
                result.Omissions);
        });
    }

    private static void RunObjectNegative(
        TiaPortal tiaPortal,
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result,
        ObjectInput input)
    {
        var workspace = ResolveSelectedOrScenarioWorkspace(project, request, result);
        if (workspace is null)
        {
            return;
        }

        var target = ResolveExportTarget(workspace, request, result);
        if (target is null)
        {
            return;
        }

        IEngineeringObject? argument;
        switch (input)
        {
            case ObjectInput.Null:
                argument = null;
                break;
            case ObjectInput.Unsupported:
                if (!TryAcquireRoot(project, result, out var service, out _))
                {
                    return;
                }
                argument = (IEngineeringObject)service!;
                break;
            case ObjectInput.AlreadyMapped:
                if (!TryResolveEngineeringObject(project, request, result, out argument)
                    || workspace.MappedObjects.Find(argument!) is null)
                {
                    SetNotObservableUnlessTerminal(result, RequiredFixtureState);
                    return;
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(input));
        }

        RunCommittedMutation(tiaPortal, project, request, result, expectThrow: true, commitOnSuccess: false, () =>
            workspace.ExportObject(
                argument!,
                target.Directory,
                target.FileNameWithoutExtension,
                "SimaticML"));
    }

    private static void RunFormatNegative(
        TiaPortal tiaPortal,
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result,
        FormatInput input)
    {
        if (!TryResolveWorkspaceAndEngineeringObject(
                project, request, result, out var workspace, out var engineeringObject))
        {
            return;
        }

        var target = ResolveExportTarget(workspace!, request, result);
        if (target is null)
        {
            return;
        }

        string? format;
        switch (input)
        {
            case FormatInput.Null:
                format = null;
                break;
            case FormatInput.Empty:
                format = string.Empty;
                break;
            case FormatInput.Unsupported:
                format = "__unsupported__";
                break;
            case FormatInput.WrongCase:
                format = "simaticml";
                break;
            case FormatInput.Mismatch:
                format = workspace!.GetSupportedFileFormats(engineeringObject!)
                    .FirstOrDefault(candidate => !string.Equals(candidate, "SimaticML", StringComparison.Ordinal));
                if (format is null)
                {
                    SetNotObservable(result, "selected_format_not_supported");
                    return;
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(input));
        }

        RunCommittedMutation(tiaPortal, project, request, result, expectThrow: true, commitOnSuccess: false, () =>
            workspace!.ExportObject(
                engineeringObject!,
                target.Directory,
                target.FileNameWithoutExtension,
                format!));
    }

    private static void RunInvalidFilename(
        TiaPortal tiaPortal,
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result)
    {
        if (!TryResolveWorkspaceAndEngineeringObject(
                project, request, result, out var workspace, out var engineeringObject))
        {
            return;
        }

        var directoryDecision = VciMutationPathPolicy.ResolveRelativeDirectory(
            workspace!.RootPath.FullName,
            ExportRelativeDirectory());
        if (!directoryDecision.IsValid)
        {
            SetNotObservable(result, RequiredFixtureState);
            return;
        }

        var directory = new DirectoryInfo(directoryDecision.CanonicalPath!);
        RunCommittedMutation(tiaPortal, project, request, result, expectThrow: true, commitOnSuccess: false, () =>
            workspace.ExportObject(engineeringObject!, directory, "bad<name>", "SimaticML"));
    }

    private static void RunFilenameCollision(
        TiaPortal tiaPortal,
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result)
    {
        if (!TryResolveWorkspaceAndEngineeringObject(
                project, request, result, out var workspace, out var engineeringObject))
        {
            return;
        }

        var target = ResolveExportTarget(workspace!, request, result);
        if (target is null
            || CaptureBoundedFileSet(
                    target.Directory,
                    target.FileNameWithoutExtension,
                    request.MaxCollectionItems,
                    result.Omissions).Count == 0)
        {
            SetNotObservableUnlessTerminal(result, RequiredFixtureState);
            return;
        }

        RunCommittedMutation(tiaPortal, project, request, result, expectThrow: true, commitOnSuccess: false, () =>
            workspace!.ExportObject(
                engineeringObject!,
                target.Directory,
                target.FileNameWithoutExtension,
                "SimaticML"));
    }

    private static void RunConnectNegative(
        TiaPortal tiaPortal,
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result,
        ConnectInput input)
    {
        if (!TryResolveWorkspaceAndEngineeringObject(
                project, request, result, out var workspace, out var engineeringObject))
        {
            return;
        }

        if (input is ConnectInput.Malformed or ConnectInput.PartialFileSet)
        {
            if (input == ConnectInput.PartialFileSet)
            {
                var exported = ResolveExportTarget(workspace!, request, result);
                var exportedFiles = exported is null
                    ? new List<string>()
                    : CaptureBoundedFileSet(
                        exported.Directory,
                        exported.FileNameWithoutExtension,
                        request.MaxCollectionItems,
                        result.Omissions);
                if (exportedFiles.Count <= 1)
                {
                    SetNotObservable(result, "selected_format_is_single_file");
                    return;
                }
            }

            SetNotObservable(result, RequiredFixtureState);
            return;
        }

        var target = ResolveTarget(
            workspace!,
            ExportRelativeDirectory(),
            "__missing__",
            result);
        if (target is null)
        {
            return;
        }
        if (CaptureBoundedFileSet(
                target.Directory,
                target.FileNameWithoutExtension,
                request.MaxCollectionItems,
                result.Omissions).Count != 0)
        {
            SetNotObservable(result, RequiredFixtureState);
            return;
        }

        RunCommittedMutation(tiaPortal, project, request, result, expectThrow: true, commitOnSuccess: false, () =>
            workspace!.ConnectObject(
                engineeringObject!,
                target.Directory,
                target.FileNameWithoutExtension,
                "SimaticML"));
    }

    private static bool TryResolveWorkspaceAndEngineeringObject(
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result,
        out Workspace? workspace,
        out IEngineeringObject? engineeringObject)
    {
        workspace = ResolveSelectedOrScenarioWorkspace(project, request, result);
        engineeringObject = null;
        return workspace is not null
            && TryResolveEngineeringObject(project, request, result, out engineeringObject);
    }

    private static bool TryResolveEngineeringObject(
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result,
        out IEngineeringObject? engineeringObject)
    {
        engineeringObject = null;
        if (request.EngineeringObject is null)
        {
            SetNotObservable(result, "selected_engineering_object_not_found");
            return false;
        }

        var resolution = VciProbeEngineeringObjectResolver.Resolve(
            project,
            ToReadRequest(request),
            request.EngineeringObject);
        engineeringObject = resolution.Candidate?.EngineeringObject as IEngineeringObject;
        if (engineeringObject is not null)
        {
            return true;
        }

        SetNotObservable(result, "selected_engineering_object_not_found");
        return false;
    }

    private static ExportTarget? ResolveExportTarget(
        Workspace workspace,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result)
    {
        var relativeDirectory = string.IsNullOrWhiteSpace(request.RelativeDirectory)
            ? ExportRelativeDirectory()
            : request.RelativeDirectory!;
        var fileNameWithoutExtension = string.IsNullOrWhiteSpace(request.FileName)
            ? "Simulation_DB"
            : request.FileName!;
        var directoryDecision = VciMutationPathPolicy.ResolveRelativeDirectory(
            workspace.RootPath.FullName,
            relativeDirectory);
        if (!directoryDecision.IsValid)
        {
            AddCheck(result.Preconditions, "export_directory_confined", false, directoryDecision.RejectionCategory);
            SetNotObservable(result, RequiredFixtureState);
            return null;
        }

        var fileDecision = VciMutationPathPolicy.ResolveFile(
            workspace.RootPath.FullName,
            relativeDirectory,
            fileNameWithoutExtension);
        if (!fileDecision.IsValid)
        {
            AddCheck(result.Preconditions, "export_file_confined", false, fileDecision.RejectionCategory);
            SetNotObservable(result, RequiredFixtureState);
            return null;
        }

        AddCheck(result.Preconditions, "export_directory_confined", true, directoryDecision.CanonicalPath);
        AddCheck(result.Preconditions, "export_file_confined", true, fileDecision.CanonicalPath);
        return new ExportTarget(
            new DirectoryInfo(directoryDecision.CanonicalPath!),
            fileNameWithoutExtension,
            fileDecision.CanonicalPath!);
    }

    private static ExportTarget? ResolveTarget(
        Workspace workspace,
        string relativeDirectory,
        string fileNameWithoutExtension,
        VciMutationProbeCaseResultInfo result)
    {
        var directoryDecision = VciMutationPathPolicy.ResolveRelativeDirectory(
            workspace.RootPath.FullName,
            relativeDirectory);
        var fileDecision = VciMutationPathPolicy.ResolveFile(
            workspace.RootPath.FullName,
            relativeDirectory,
            fileNameWithoutExtension);
        if (!directoryDecision.IsValid || !fileDecision.IsValid)
        {
            SetNotObservable(result, RequiredFixtureState);
            return null;
        }

        return new ExportTarget(
            new DirectoryInfo(directoryDecision.CanonicalPath!),
            fileNameWithoutExtension,
            fileDecision.CanonicalPath!);
    }

    private static ExportTarget? ResolveSeedTarget(
        Workspace workspace,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result)
    {
        if (string.IsNullOrWhiteSpace(request.SeedRelativePath))
        {
            SetNotObservable(result, RequiredFixtureState);
            return null;
        }

        var relativeDirectory = Path.GetDirectoryName(request.SeedRelativePath!) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(request.SeedRelativePath!);
        return ResolveTarget(workspace, relativeDirectory, fileName, result);
    }

    private static string ExportRelativeDirectory()
        => Path.Combine("mapping", "export");

    private static VciProbeReturnInfo BuildMappingReturn(
        Workspace workspace,
        MappedObject mapping,
        DirectoryInfo directory,
        string fileNameWithoutExtension,
        int maxCollectionItems,
        List<VciProbeOmissionInfo> omissions)
    {
        var result = new VciProbeReturnInfo
        {
            ClrTypeName = mapping.GetType().FullName ?? mapping.GetType().Name,
            StringValue = "mapping_observation",
        };
        result.Members.Add(Member("workspace", workspace.Name));
        result.Members.Add(Member("directoryPath", mapping.DirectoryPath.FullName));
        result.Members.Add(Member("fileNameWithoutExtension", mapping.FileNameWithoutExtension));
        result.Members.Add(Member("fileFormat", mapping.FileFormat));
        result.Members.Add(Member("status", mapping.Status));
        result.Members.Add(Member("getStatus", mapping.GetStatus()));
        result.Members.Add(Member("getChildStatus", mapping.GetChildStatus()));

        var files = CaptureBoundedFileSet(
            directory,
            fileNameWithoutExtension,
            maxCollectionItems,
            omissions);
        for (var index = 0; index < files.Count; index++)
        {
            result.Members.Add(Member("file[" + index + "]", files[index]));
        }
        return result;
    }

    private static VciProbeReturnInfo BuildFileTransitionReturn(
        string operation,
        IReadOnlyList<string> filesBefore,
        IReadOnlyList<string> filesAfter)
    {
        var result = new VciProbeReturnInfo
        {
            ClrTypeName = "vci_file_transition",
            StringValue = operation,
        };
        for (var index = 0; index < filesBefore.Count; index++)
        {
            result.Members.Add(Member("before[" + index + "]", filesBefore[index]));
        }
        for (var index = 0; index < filesAfter.Count; index++)
        {
            result.Members.Add(Member("after[" + index + "]", filesAfter[index]));
        }
        result.Members.Add(Member("filesRemain", filesAfter.Count > 0));
        return result;
    }

    private static List<string> CaptureBoundedFileSet(
        DirectoryInfo directory,
        string fileNameWithoutExtension,
        int maxCollectionItems,
        List<VciProbeOmissionInfo> omissions)
    {
        if (!directory.Exists)
        {
            return new List<string>();
        }

        var files = Directory.EnumerateFiles(
                directory.FullName,
                fileNameWithoutExtension + "*",
                SearchOption.TopDirectoryOnly)
            .Take(maxCollectionItems + 1)
            .Select(file => (Path.GetFileName(file) ?? string.Empty)
                + "|sha256:" + ComputeSha256(file))
            .ToList();
        if (files.Count > maxCollectionItems)
        {
            files.RemoveAt(files.Count - 1);
            omissions.Add(new VciProbeOmissionInfo
            {
                Reason = "Generated-file enumeration stopped at the configured collection budget.",
                BudgetName = nameof(VciMutationProbeRequestInfo.MaxCollectionItems),
                BudgetValue = maxCollectionItems,
                ObservedCount = maxCollectionItems,
                TraversalPath = directory.FullName,
            });
        }
        return files;
    }

    private static string ComputeSha256(string path)
    {
        try
        {
            using var stream = File.Open(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var sha256 = SHA256.Create();
            return BitConverter.ToString(sha256.ComputeHash(stream))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "unavailable:" + exception.GetType().Name;
        }
    }

    private static VciProbeMemberObservationInfo Member(string name, object? value)
        => new()
        {
            Name = name,
            ClrTypeName = value?.GetType().FullName ?? "null",
            IsNull = value is null,
            StringValue = value?.ToString(),
        };

    private static void RunUnavailableTask5Case(VciMutationProbeCaseResultInfo result)
        => SetNotObservable(result, RequiredFixtureState);

    private static void RunSynchronization(
        TiaPortal tiaPortal,
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result,
        SynchronizationMode mode)
    {
        var mapping = ResolveMapping(project, request, result);
        if (mapping is null)
        {
            return;
        }

        var statusBefore = mapping.GetStatus();
        var directory = mapping.DirectoryPath;
        var fileNameWithoutExtension = mapping.FileNameWithoutExtension;
        var filesBefore = CaptureBoundedFileSet(
            directory,
            fileNameWithoutExtension,
            request.MaxCollectionItems,
            result.Omissions);
        if (filesBefore.Count == 0)
        {
            SetNotObservable(result, RequiredFixtureState);
            result.StopScenarioFamily = true;
            return;
        }

        if (!ResolveComparisonTarget(
                project,
                request,
                result,
                out var comparisonTarget))
        {
            result.StopScenarioFamily = true;
            return;
        }

        var comparisonFiles = CaptureBoundedFileSet(
            comparisonTarget!.Directory,
            comparisonTarget.FileNameWithoutExtension,
            request.MaxCollectionItems,
            result.Omissions);
        if (comparisonFiles.Count == 0)
        {
            SetNotObservable(result, RequiredFixtureState);
            result.StopScenarioFamily = true;
            return;
        }

        var controlledExportsDiffer = !HashSetsEqual(filesBefore, comparisonFiles);
        AddCheck(
            result.Preconditions,
            "baseline_and_changed_exports_differ",
            controlledExportsDiffer,
            null);
        if (!controlledExportsDiffer)
        {
            SetNotObservable(result, "baseline_and_changed_exports_identical");
            result.StopScenarioFamily = true;
            return;
        }

        var requiredDifference = mode == SynchronizationMode.ProjectToWorkspace
            ? IndividualObjectCompareDetails.ProjectObjectChanged
            : IndividualObjectCompareDetails.WorkspaceFileChanged;
        var expectedDifferenceEstablished = statusBefore.CompareState == CompareState.Unequal
            && statusBefore.IndividualObjectCompareDetails == requiredDifference;
        AddCheck(
            result.Preconditions,
            mode == SynchronizationMode.ProjectToWorkspace
                ? "project_only_difference_established"
                : "workspace_only_difference_established",
            expectedDifferenceEstablished,
            statusBefore.IndividualObjectCompareDetails.ToString());
        if (!expectedDifferenceEstablished)
        {
            SetNotObservable(
                result,
                mode == SynchronizationMode.ProjectToWorkspace
                    ? "expected_project_only_state_not_established"
                    : RequiredFixtureState);
            result.StopScenarioFamily = true;
            return;
        }

        Workspace? verificationWorkspace = null;
        ExportTarget? verificationTarget = null;
        if (mode == SynchronizationMode.WorkspaceToProject
            && !ResolveVerificationTarget(
                project,
                request,
                result,
                mapping.EngineeringObject,
                out verificationWorkspace,
                out verificationTarget))
        {
            result.StopScenarioFamily = true;
            return;
        }

        RunCommittedMutation(tiaPortal, project, request, result, expectThrow: false, commitOnSuccess: true, () =>
        {
            var callStatusBefore = mapping.GetStatus();
            var callFilesBefore = CaptureBoundedFileSet(
                directory,
                fileNameWithoutExtension,
                request.MaxCollectionItems,
                result.Omissions);
            mapping.Synchronize(mode);
            var callStatusAfter = mapping.GetStatus();
            var callFilesAfter = CaptureBoundedFileSet(
                directory,
                fileNameWithoutExtension,
                request.MaxCollectionItems,
                result.Omissions);
            if (mode == SynchronizationMode.ProjectToWorkspace
                && !HashSetsEqual(callFilesAfter, comparisonFiles))
            {
                throw new InvalidOperationException(
                    "The ProjectToWorkspace result did not hash-match the changed VCI-produced file set.");
            }
            var verificationFiles = new List<string>();
            if (mode == SynchronizationMode.WorkspaceToProject)
            {
                _ = verificationWorkspace!.ExportObject(
                    mapping.EngineeringObject,
                    verificationTarget!.Directory,
                    verificationTarget.FileNameWithoutExtension,
                    "SimaticML");
                verificationFiles = CaptureBoundedFileSet(
                    verificationTarget.Directory,
                    verificationTarget.FileNameWithoutExtension,
                    request.MaxCollectionItems,
                    result.Omissions);
                if (!HashSetsEqual(callFilesBefore, verificationFiles))
                {
                    throw new InvalidOperationException(
                        "The WorkspaceToProject verification export did not hash-match the changed VCI-produced file set.");
                }
            }
            return BuildSynchronizationReturn(
                mode,
                callStatusBefore,
                callStatusAfter,
                callFilesBefore,
                callFilesAfter,
                verificationFiles,
                comparisonFiles);
        });
    }

    private static bool ResolveComparisonTarget(
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result,
        out ExportTarget? comparisonTarget)
    {
        comparisonTarget = null;
        if (string.IsNullOrWhiteSpace(request.RelativeDirectory)
            || string.IsNullOrWhiteSpace(request.FileName)
            || !TryAcquireScenarioGroup(project, request, result, out var group))
        {
            SetNotObservableUnlessTerminal(result, RequiredFixtureState);
            return false;
        }

        var comparisonWorkspace = group!.Workspaces.Find(ScenarioNames(request).LanguageWorkspace);
        if (comparisonWorkspace is null)
        {
            SetNotObservable(result, RequiredFixtureState);
            return false;
        }

        comparisonTarget = ResolveTarget(
            comparisonWorkspace,
            request.RelativeDirectory!,
            request.FileName!,
            result);
        return comparisonTarget is not null;
    }

    private static bool ResolveVerificationTarget(
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result,
        IEngineeringObject engineeringObject,
        out Workspace? verificationWorkspace,
        out ExportTarget? verificationTarget)
    {
        verificationWorkspace = null;
        verificationTarget = null;
        if (!TryAcquireScenarioGroup(project, request, result, out var group))
        {
            return false;
        }

        var names = ScenarioNames(request);
        verificationWorkspace = group!.Workspaces.Find(names.LanguageWorkspace);
        if (verificationWorkspace is null
            || verificationWorkspace.MappedObjects.Find(engineeringObject) is not null)
        {
            SetNotObservable(result, RequiredFixtureState);
            return false;
        }

        verificationTarget = ResolveTarget(
            verificationWorkspace,
            Path.Combine("mapping", "verify"),
            "Simulation_DB_Verify",
            result);
        return verificationTarget is not null;
    }

    private static void RunSynchronizationNegative(
        TiaPortal tiaPortal,
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result,
        SynchronizationInput input)
    {
        var mapping = ResolveMapping(project, request, result);
        if (mapping is null)
        {
            return;
        }

        if (input == SynchronizationInput.BothSides)
        {
            SetNotObservable(result, RequiredFixtureState);
            return;
        }

        var status = mapping.GetStatus();
        var stateEstablished = input switch
        {
            SynchronizationInput.Missing => status.CompareState == CompareState.WorkspaceFileMissing,
            SynchronizationInput.Malformed => status.CompareState == CompareState.Unknown,
            SynchronizationInput.Unchanged => status.CompareState == CompareState.Equal,
            SynchronizationInput.ProjectOnly => status.CompareState == CompareState.Unequal
                && status.IndividualObjectCompareDetails == IndividualObjectCompareDetails.ProjectObjectChanged,
            SynchronizationInput.WorkspaceOnly => status.CompareState == CompareState.Unequal
                && status.IndividualObjectCompareDetails == IndividualObjectCompareDetails.WorkspaceFileChanged,
            _ => false,
        };
        AddCheck(result.Preconditions, "requested_synchronization_state_established", stateEstablished, status.CompareState.ToString());
        if (!stateEstablished)
        {
            SetNotObservable(result, RequiredFixtureState);
            return;
        }

        var mode = input == SynchronizationInput.ProjectOnly
            ? SynchronizationMode.ProjectToWorkspace
            : SynchronizationMode.WorkspaceToProject;
        var directory = mapping.DirectoryPath;
        var fileNameWithoutExtension = mapping.FileNameWithoutExtension;
        RunCommittedMutation(tiaPortal, project, request, result, expectThrow: true, commitOnSuccess: false, () =>
        {
            var beforeStatus = mapping.GetStatus();
            var beforeFiles = CaptureBoundedFileSet(
                directory,
                fileNameWithoutExtension,
                request.MaxCollectionItems,
                result.Omissions);
            mapping.Synchronize(mode);
            var afterStatus = mapping.GetStatus();
            var afterFiles = CaptureBoundedFileSet(
                directory,
                fileNameWithoutExtension,
                request.MaxCollectionItems,
                result.Omissions);
            return BuildSynchronizationReturn(
                mode,
                beforeStatus,
                afterStatus,
                beforeFiles,
                afterFiles);
        });
    }

    private static void RunInvalidSynchronizationMode(
        TiaPortal tiaPortal,
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result)
    {
        var mapping = ResolveMapping(project, request, result);
        if (mapping is null)
        {
            return;
        }

        RunCommittedMutation(tiaPortal, project, request, result, expectThrow: true, commitOnSuccess: false, () =>
        {
            mapping.Synchronize((SynchronizationMode)int.MaxValue);
            return mapping.GetStatus();
        });
    }

    private static VciProbeReturnInfo BuildSynchronizationReturn(
        SynchronizationMode mode,
        IndividualObjectCompareResult statusBefore,
        IndividualObjectCompareResult statusAfter,
        IReadOnlyList<string> filesBefore,
        IReadOnlyList<string> filesAfter,
        IReadOnlyList<string>? verificationFiles = null,
        IReadOnlyList<string>? comparisonFiles = null)
    {
        var result = new VciProbeReturnInfo
        {
            ClrTypeName = "vci_synchronization_observation",
            StringValue = mode.ToString(),
        };
        result.Members.Add(Member("before.compareState", statusBefore.CompareState));
        result.Members.Add(Member("before.details", statusBefore.IndividualObjectCompareDetails));
        result.Members.Add(Member("after.compareState", statusAfter.CompareState));
        result.Members.Add(Member("after.details", statusAfter.IndividualObjectCompareDetails));
        for (var index = 0; index < filesBefore.Count; index++)
        {
            result.Members.Add(Member("before.file[" + index + "]", filesBefore[index]));
        }
        for (var index = 0; index < filesAfter.Count; index++)
        {
            result.Members.Add(Member("after.file[" + index + "]", filesAfter[index]));
        }
        if (verificationFiles is not null)
        {
            for (var index = 0; index < verificationFiles.Count; index++)
            {
                result.Members.Add(Member("verification.file[" + index + "]", verificationFiles[index]));
            }
        }
        if (comparisonFiles is not null)
        {
            for (var index = 0; index < comparisonFiles.Count; index++)
            {
                result.Members.Add(Member("comparison.file[" + index + "]", comparisonFiles[index]));
            }
        }
        return result;
    }

    private static bool HashSetsEqual(
        IReadOnlyList<string> expected,
        IReadOnlyList<string> actual)
    {
        var expectedHashes = expected.Select(ExtractSha256).ToList();
        var actualHashes = actual.Select(ExtractSha256).ToList();
        if (expectedHashes.Any(hash => hash is null)
            || actualHashes.Any(hash => hash is null))
        {
            return false;
        }

        return expectedHashes.Cast<string>().OrderBy(hash => hash, StringComparer.Ordinal)
            .SequenceEqual(
                actualHashes.Cast<string>().OrderBy(hash => hash, StringComparer.Ordinal),
                StringComparer.Ordinal);
    }

    private static string? ExtractSha256(string fileEvidence)
    {
        const string marker = "|sha256:";
        var markerIndex = fileEvidence.LastIndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return null;
        }

        var hash = fileEvidence.Substring(markerIndex + marker.Length);
        return hash.Length == 64 && hash.All(Uri.IsHexDigit) ? hash : null;
    }

    private static void RunTransactionCase(
        TiaPortal tiaPortal,
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result,
        TransactionMutation mutationKind)
    {
        Func<object?>? mutation = null;
        ExportTarget? fileTarget = null;
        var names = ScenarioNames(request);

        switch (mutationKind)
        {
            case TransactionMutation.Group:
                if (TryAcquireRoot(project, result, out _, out var root)
                    && root!.Groups.Find(names.Prefix + "_TxGroup") is null)
                {
                    mutation = () => root.Groups.Create(names.Prefix + "_TxGroup").Name;
                }
                break;
            case TransactionMutation.Workspace:
                if (TryAcquireScenarioGroup(project, request, result, out var group)
                    && group!.Workspaces.Find(names.Prefix + "_TxWorkspace") is null)
                {
                    var path = ScenarioWorkspacePath(request, names.Prefix + "_TxWorkspace");
                    mutation = () => group.Workspaces.Create(
                        names.Prefix + "_TxWorkspace",
                        new DirectoryInfo(path)).Name;
                }
                break;
            case TransactionMutation.Export:
                if (TryResolveWorkspaceAndEngineeringObject(
                        project, request, result, out var exportWorkspace, out var exportObject))
                {
                    fileTarget = ResolveExportTarget(exportWorkspace!, request, result);
                    if (fileTarget is not null
                        && exportWorkspace!.MappedObjects.Find(exportObject!) is null)
                    {
                        mutation = () => exportWorkspace.ExportObject(
                            exportObject!,
                            fileTarget.Directory,
                            fileTarget.FileNameWithoutExtension,
                            "SimaticML");
                    }
                }
                break;
            case TransactionMutation.Connect:
                if (TryResolveWorkspaceAndEngineeringObject(
                        project, request, result, out var connectWorkspace, out var connectObject))
                {
                    fileTarget = ResolveExportTarget(connectWorkspace!, request, result);
                    if (fileTarget is not null
                        && connectWorkspace!.MappedObjects.Find(connectObject!) is null
                        && CaptureBoundedFileSet(
                            fileTarget.Directory,
                            fileTarget.FileNameWithoutExtension,
                            request.MaxCollectionItems,
                            result.Omissions).Count > 0)
                    {
                        mutation = () => connectWorkspace.ConnectObject(
                            connectObject!,
                            fileTarget.Directory,
                            fileTarget.FileNameWithoutExtension,
                            "SimaticML");
                    }
                }
                break;
            case TransactionMutation.ProjectToWorkspace:
            case TransactionMutation.WorkspaceToProject:
                var syncMapping = ResolveMapping(project, request, result);
                if (syncMapping is not null)
                {
                    fileTarget = new ExportTarget(
                        syncMapping.DirectoryPath,
                        syncMapping.FileNameWithoutExtension,
                        Path.Combine(syncMapping.DirectoryPath.FullName, syncMapping.FileNameWithoutExtension));
                    var mode = mutationKind == TransactionMutation.ProjectToWorkspace
                        ? SynchronizationMode.ProjectToWorkspace
                        : SynchronizationMode.WorkspaceToProject;
                    mutation = () =>
                    {
                        syncMapping.Synchronize(mode);
                        return syncMapping.GetStatus();
                    };
                }
                break;
            case TransactionMutation.Disconnect:
                var disconnectMapping = ResolveMapping(project, request, result);
                if (disconnectMapping is not null)
                {
                    fileTarget = new ExportTarget(
                        disconnectMapping.DirectoryPath,
                        disconnectMapping.FileNameWithoutExtension,
                        Path.Combine(disconnectMapping.DirectoryPath.FullName, disconnectMapping.FileNameWithoutExtension));
                    mutation = () =>
                    {
                        disconnectMapping.Delete();
                        return "mapping_disconnected";
                    };
                }
                break;
            case TransactionMutation.DeleteWorkspace:
                var workspace = ResolveSelectedOrScenarioWorkspace(project, request, result);
                if (workspace is not null && workspace.MappedObjects.Count == 0)
                {
                    mutation = () =>
                    {
                        workspace.Delete();
                        return "workspace_deleted";
                    };
                }
                break;
            case TransactionMutation.DeleteGroup:
                if (TryAcquireScenarioGroup(project, request, result, out var deleteGroup))
                {
                    var nestedGroup = deleteGroup!.Groups.Find(names.NestedGroup);
                    if (nestedGroup is not null
                        && nestedGroup.Groups.Count == 0
                        && nestedGroup.Workspaces.Count == 0)
                    {
                        mutation = () =>
                        {
                            nestedGroup.Delete();
                            return "nested_group_deleted";
                        };
                    }
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutationKind));
        }

        if (mutation is null)
        {
            SetNotObservableUnlessTerminal(result, RequiredFixtureState);
            return;
        }

        RunRollbackOnlyMutation(
            tiaPortal,
            project,
            request,
            result,
            mutation,
            fileTarget);
    }

    private static void RunRollbackOnlyMutation(
        TiaPortal tiaPortal,
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result,
        Func<object?> mutation,
        ExportTarget? fileTarget)
    {
        result.Transaction.Requested = true;
        result.Before = CaptureSnapshot(project, request, result);
        var snapshotBefore = SnapshotSignature(result.Before);
        var isModifiedBefore = project.IsModified;
        var filesBefore = fileTarget is null
            ? new List<string>()
            : CaptureBoundedFileSet(
                fileTarget.Directory,
                fileTarget.FileNameWithoutExtension,
                request.MaxCollectionItems,
                result.Omissions);
        try
        {
            using (var exclusiveAccess = tiaPortal.ExclusiveAccess(
                "VCI Workspace Phase 1 rollback probe: " + request.CaseId))
            using (var transaction = exclusiveAccess.Transaction(project, request.CaseId))
            {
                result.Transaction.Started = true;
                var returnValue = mutation();
                result.Outcome = returnValue is null ? "returned_null" : "returned";
                result.Return = returnValue as VciProbeReturnInfo
                    ?? ToReturnInfo(returnValue, request.MaxCollectionItems, result.Omissions);
                result.After = CaptureSnapshot(project, request, result);
                result.Canary = RunCanary(project, request, result);
                result.Transaction.CanCommitBeforeDispose = transaction.CanCommit;
            }
        }
        catch (NonRecoverableException)
        {
            result.UncertainOutcome = true;
            result.StopScenarioFamily = true;
            throw;
        }
        catch (Exception exception) when (IsEvidenceException(exception))
        {
            RecordException(result, exception);
        }
        finally
        {
            result.Transaction.Disposed = result.Transaction.Started;
            result.After = CaptureSnapshot(project, request, result);
            result.Canary = RunCanary(project, request, result);
            var filesAfter = fileTarget is null
                ? new List<string>()
                : CaptureBoundedFileSet(
                    fileTarget.Directory,
                    fileTarget.FileNameWithoutExtension,
                    request.MaxCollectionItems,
                    result.Omissions);
            var projectStateRolledBack = string.Equals(
                snapshotBefore,
                SnapshotSignature(result.After),
                StringComparison.Ordinal)
                && project.IsModified == isModifiedBefore;
            AddCheck(
                result.SafetyInvariants,
                "project_state_rolled_back",
                projectStateRolledBack,
                null);
            if (!projectStateRolledBack)
            {
                result.UncertainOutcome = true;
                result.StopScenarioFamily = true;
            }
            AddCheck(
                result.SafetyInvariants,
                "external_files_rolled_back",
                filesBefore.SequenceEqual(filesAfter, StringComparer.Ordinal),
                "Filesystem rollback is independent evidence, not inferred from project rollback.");
        }
    }

    private static string SnapshotSignature(VciProbeSnapshotInfo? snapshot)
    {
        if (snapshot is null)
        {
            return "null";
        }

        return string.Join("|", new[]
        {
            snapshot.Service?.ServiceAvailable.ToString() ?? "null",
            snapshot.Service?.RootGroupAvailable.ToString() ?? "null",
            string.Join(",", snapshot.Groups.Select(group => group.CanonicalKey + ":" + group.Name)),
            string.Join(",", snapshot.Workspaces.Select(workspace => workspace.CanonicalKey + ":" + workspace.Name + ":" + workspace.RootPath)),
            string.Join(",", snapshot.Mappings.Select(mapping => mapping.CanonicalKey + ":" + mapping.Status)),
        });
    }

    private static void RunDeleteMapping(
        TiaPortal tiaPortal,
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result)
    {
        var mapping = ResolveMapping(project, request, result);
        if (mapping is null)
        {
            return;
        }

        RunCommittedMutation(tiaPortal, project, request, result, expectThrow: false, commitOnSuccess: true, () =>
        {
            mapping.Delete();
            return "mapping_deleted";
        });
    }

    private static void RunDeleteWorkspace(
        TiaPortal tiaPortal,
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result)
    {
        var workspace = ResolveSelectedOrScenarioWorkspace(project, request, result);
        if (workspace is null)
        {
            return;
        }

        if (workspace.MappedObjects.Count != 0)
        {
            SetNotObservable(result, RequiredFixtureState);
            return;
        }

        RunCommittedMutation(tiaPortal, project, request, result, expectThrow: false, commitOnSuccess: true, () =>
        {
            workspace.Delete();
            return "workspace_deleted";
        });
    }

    private static void RunDeleteGroups(
        TiaPortal tiaPortal,
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result)
    {
        if (!TryAcquireRoot(project, result, out _, out var root))
        {
            return;
        }

        var names = ScenarioNames(request);
        var group = root!.Groups.Find(names.Group);
        var nested = group?.Groups.Find(names.NestedGroup);
        if (group is null
            || nested is null
            || nested.Groups.Count != 0
            || nested.Workspaces.Count != 0
            || group.Workspaces.Count != 0)
        {
            SetNotObservable(result, RequiredFixtureState);
            return;
        }

        RunCommittedMutation(tiaPortal, project, request, result, expectThrow: false, commitOnSuccess: true, () =>
        {
            nested.Delete();
            group.Delete();
            return "groups_deleted_child_first";
        });
    }

    private static void RunGroupNameNegative(
        TiaPortal tiaPortal,
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result,
        GroupNameInput input)
    {
        if (!TryAcquireRoot(project, result, out _, out var root))
        {
            return;
        }

        var names = ScenarioNames(request);
        if (input == GroupNameInput.Duplicate && root!.Groups.Find(names.Group) is null)
        {
            SetNotObservable(result, RequiredFixtureState);
            return;
        }

        RunCommittedMutation(tiaPortal, project, request, result, expectThrow: true, commitOnSuccess: false, () =>
        {
            var created = input switch
            {
                GroupNameInput.Null => root!.Groups.Create(null!),
                GroupNameInput.Empty => root!.Groups.Create(string.Empty),
                GroupNameInput.Whitespace => root!.Groups.Create("   "),
                GroupNameInput.Duplicate => root!.Groups.Create(names.Group),
                GroupNameInput.Invalid => root!.Groups.Create("CodexVci_Invalid:*?"),
                _ => throw new ArgumentOutOfRangeException(nameof(input)),
            };
            return created?.Name;
        });
    }

    private static void RunWorkspaceNameNegative(
        TiaPortal tiaPortal,
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result,
        WorkspaceNameInput input)
    {
        if (!TryAcquireScenarioGroup(project, request, result, out var group))
        {
            return;
        }

        var names = ScenarioNames(request);
        if (input == WorkspaceNameInput.Duplicate
            && group!.Workspaces.Find(names.RootWorkspace) is null)
        {
            SetNotObservable(result, RequiredFixtureState);
            return;
        }

        var path = ScenarioWorkspacePath(request, "negative-name");
        RunCommittedMutation(tiaPortal, project, request, result, expectThrow: true, commitOnSuccess: false, () =>
        {
            var created = input switch
            {
                WorkspaceNameInput.Null => group!.Workspaces.Create(null!, new DirectoryInfo(path)),
                WorkspaceNameInput.Empty => group!.Workspaces.Create(string.Empty, new DirectoryInfo(path)),
                WorkspaceNameInput.Whitespace => group!.Workspaces.Create("   ", new DirectoryInfo(path)),
                WorkspaceNameInput.Duplicate => group!.Workspaces.Create(
                    names.RootWorkspace,
                    new DirectoryInfo(ScenarioWorkspacePath(request, names.RootWorkspace + "_duplicate"))),
                WorkspaceNameInput.Invalid => group!.Workspaces.Create(
                    "CodexVci_Invalid:*?",
                    new DirectoryInfo(path)),
                _ => throw new ArgumentOutOfRangeException(nameof(input)),
            };
            return created?.Name;
        });
    }

    private static void RunNullWorkspaceLanguage(
        TiaPortal tiaPortal,
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result)
    {
        if (!TryAcquireScenarioGroup(project, request, result, out var group))
        {
            return;
        }

        var name = ScenarioNames(request).Prefix + "_NullLanguage";
        var path = ScenarioWorkspacePath(request, name);
        RunCommittedMutation(tiaPortal, project, request, result, expectThrow: true, commitOnSuccess: false, () =>
            group!.Workspaces.Create(name, new DirectoryInfo(path), null!));
    }

    private static void RunGlobalLibraryNegative(
        TiaPortal tiaPortal,
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result,
        bool useNull)
    {
        var workspace = ResolveSelectedOrScenarioWorkspace(project, request, result);
        if (workspace is null)
        {
            return;
        }

        RunCommittedMutation(tiaPortal, project, request, result, expectThrow: true, commitOnSuccess: false, () =>
        {
            workspace.GlobalLibraryPath = useNull
                ? null!
                : new FileInfo("\0invalid-library.al21");
            return workspace.GlobalLibraryPath;
        });
    }

    private static void RunDeleteNonemptyGroup(
        TiaPortal tiaPortal,
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result)
    {
        if (!TryAcquireScenarioGroup(project, request, result, out var group)
            || (group!.Groups.Count == 0 && group.Workspaces.Count == 0))
        {
            SetNotObservableUnlessTerminal(result, RequiredFixtureState);
            return;
        }

        RunCommittedMutation(tiaPortal, project, request, result, expectThrow: true, commitOnSuccess: false, () =>
        {
            group.Delete();
            return "nonempty_group_deleted";
        });
    }

    private static void RunDeleteTwice(
        TiaPortal tiaPortal,
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result)
    {
        var workspace = ResolveSelectedOrScenarioWorkspace(project, request, result);
        if (workspace is null)
        {
            return;
        }

        RunCommittedMutation(tiaPortal, project, request, result, expectThrow: true, commitOnSuccess: false, () =>
        {
            workspace.Delete();
            workspace.Delete();
            return "workspace_deleted_twice";
        });
    }

    private static void RunStaleMappingProxy(
        TiaPortal tiaPortal,
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result)
    {
        var mapping = ResolveMapping(project, request, result);
        if (mapping is null)
        {
            return;
        }

        RunCommittedMutation(tiaPortal, project, request, result, expectThrow: true, commitOnSuccess: false, () =>
        {
            mapping.Delete();
            return mapping.Status;
        });
    }

    private static void RunHarnessConfinementOnly(VciMutationProbeCaseResultInfo result)
        => SetNotObservable(result, HarnessConfinementRejected);

    private static void RunDeferredCase(VciMutationProbeCaseResultInfo result)
        => SetNotObservable(result, RequiredFixtureState);

    private static void RunCommittedMutation(
        TiaPortal tiaPortal,
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result,
        bool expectThrow,
        bool commitOnSuccess,
        Func<object?> mutation)
    {
        result.Transaction.Requested = true;
        result.Before = CaptureSnapshot(project, request, result);
        try
        {
            using (var exclusiveAccess = tiaPortal.ExclusiveAccess(
                "VCI Workspace Phase 1 mutation probe: " + request.CaseId))
            using (var transaction = exclusiveAccess.Transaction(project, request.CaseId))
            {
                result.Transaction.Started = true;
                var returnValue = mutation();
                result.Outcome = returnValue is null ? "returned_null" : "returned";
                result.Return = returnValue as VciProbeReturnInfo
                    ?? ToReturnInfo(returnValue, request.MaxCollectionItems, result.Omissions);

                var postCallSnapshot = CaptureSnapshot(project, request, result);
                result.After = postCallSnapshot;
                result.Canary = RunCanary(project, request, result);
                var rootsConfined = ScenarioWorkspaceRootsAreConfined(postCallSnapshot, request);
                AddCheck(result.SafetyInvariants, "post_call_snapshot_captured", true, null);
                AddCheck(result.SafetyInvariants, "read_only_canary_usable", result.Canary.Usable, result.Canary.Outcome);
                AddCheck(result.SafetyInvariants, "scenario_workspace_roots_confined", rootsConfined, null);

                result.Transaction.CanCommitBeforeDispose = transaction.CanCommit;
                if (commitOnSuccess
                    && result.Canary.Usable
                    && rootsConfined
                    && transaction.CanCommit)
                {
                    transaction.CommitOnDispose();
                    result.Transaction.CommitRequested = transaction.CommitRequested;
                }
                else if (commitOnSuccess)
                {
                    SetNotObservable(result, transaction.CanCommit ? RequiredFixtureState : "transaction_not_supported");
                    result.UncertainOutcome = true;
                    result.StopScenarioFamily = true;
                }
            }
        }
        catch (NonRecoverableException)
        {
            result.UncertainOutcome = true;
            result.StopScenarioFamily = true;
            throw;
        }
        catch (Exception exception) when (IsEvidenceException(exception))
        {
            RecordException(result, exception);
            if (!expectThrow)
            {
                result.StopScenarioFamily = true;
            }
        }
        finally
        {
            result.Transaction.Disposed = result.Transaction.Started;
            result.After = CaptureSnapshot(project, request, result);
            if (!result.Canary.Attempted)
            {
                result.Canary = RunCanary(project, request, result);
            }
        }
    }

    private static VciMutationCanaryInfo RunCanary(
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result)
    {
        var snapshot = CaptureSnapshot(project, request, result);
        var usable = snapshot.Service?.ServiceAvailable == true
            && snapshot.Service.RootGroupAvailable;
        return new VciMutationCanaryInfo
        {
            Attempted = true,
            Usable = usable,
            Outcome = usable ? "returned" : "not_observable",
        };
    }

    private static VciProbeSnapshotInfo CaptureSnapshot(
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result)
    {
        var read = VciProbeSnapshotReader.Read(project, ToReadRequest(request));
        read.Snapshot.Members.AddRange(read.Members);
        result.Omissions.AddRange(read.Omissions);
        return read.Snapshot;
    }

    private static void TraceProgress(VciMutationProbeRequestInfo request, string stage)
    {
        Console.Error.WriteLine(
            "[vci-mutation-progress] utc={0:O} caseId={1} caseInstanceId={2} stage={3}",
            DateTimeOffset.UtcNow,
            request.CaseId,
            request.CaseInstanceId,
            stage);
        Console.Error.Flush();
    }

    private static VciProbeRequestInfo ToReadRequest(VciMutationProbeRequestInfo request)
        => new()
        {
            RunId = request.RunId,
            SessionId = request.SessionId,
            CaseId = "R-CANARY",
            CaseInstanceId = request.CaseInstanceId,
            Workspace = request.Workspace,
            EngineeringObject = request.EngineeringObject,
            MaxGroupDepth = request.MaxGroupDepth,
            MaxGroups = request.MaxGroups,
            MaxWorkspaces = request.MaxWorkspaces,
            MaxMappings = request.MaxMappings,
            MaxEngineeringObjects = request.MaxEngineeringObjects,
            MaxCollectionItems = request.MaxCollectionItems,
        };

    private static bool TryAcquireRoot(
        Project project,
        VciMutationProbeCaseResultInfo result,
        out VersionControlInterface? service,
        out WorkspaceSystemGroup? root)
    {
        service = null;
        root = null;
        try
        {
            service = project.GetService<VersionControlInterface>();
            root = service?.WorkspaceGroup;
            if (service is null || root is null)
            {
                SetNotObservable(result, RequiredFixtureState);
                return false;
            }

            return true;
        }
        catch (NonRecoverableException)
        {
            throw;
        }
        catch (Exception exception) when (IsEvidenceException(exception))
        {
            RecordException(result, exception);
            return false;
        }
    }

    private static bool TryAcquireScenarioGroup(
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result,
        out WorkspaceUserGroup? group)
    {
        group = null;
        if (!TryAcquireRoot(project, result, out _, out var root))
        {
            return false;
        }

        group = root!.Groups.Find(ScenarioNames(request).Group);
        if (group is not null)
        {
            return true;
        }

        SetNotObservable(result, RequiredFixtureState);
        return false;
    }

    private static WorkspaceGroup? ResolveParentGroup(
        WorkspaceSystemGroup root,
        VciWorkspaceSelectorInfo selector)
    {
        WorkspaceGroup current = root;
        foreach (var segment in selector.GroupPath)
        {
            WorkspaceUserGroup? match = null;
            var index = 0;
            var sameNameOrdinal = 0;
            foreach (var child in current.Groups)
            {
                if (index == segment.Index
                    && string.Equals(child.Name, segment.Name, StringComparison.Ordinal)
                    && sameNameOrdinal == segment.SameNameOrdinal)
                {
                    match = child;
                }

                if (string.Equals(child.Name, segment.Name, StringComparison.Ordinal))
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

    private static Workspace? ResolveWorkspace(
        WorkspaceSystemGroup root,
        VciWorkspaceSelectorInfo selector)
    {
        var group = ResolveParentGroup(root, selector);
        if (group is null)
        {
            return null;
        }

        Workspace? match = null;
        var matchCount = 0;
        foreach (var workspace in group.Workspaces)
        {
            if (string.Equals(workspace.Name, selector.WorkspaceName, StringComparison.Ordinal)
                && string.Equals(
                    CanonicalPath(workspace.RootPath),
                    selector.CanonicalRootPath,
                    StringComparison.Ordinal))
            {
                match = workspace;
                matchCount++;
            }
        }

        return matchCount == 1 ? match : null;
    }

    private static Workspace? ResolveSelectedOrScenarioWorkspace(
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result)
    {
        if (!TryAcquireRoot(project, result, out _, out var root))
        {
            return null;
        }

        Workspace? workspace;
        if (request.Workspace is not null)
        {
            workspace = ResolveWorkspace(root!, request.Workspace);
        }
        else
        {
            var group = root!.Groups.Find(ScenarioNames(request).Group);
            workspace = !string.IsNullOrWhiteSpace(request.WorkspaceName)
                ? group?.Workspaces.Find(request.WorkspaceName)
                : group?.Workspaces.Find(ScenarioNames(request).LanguageWorkspace)
                    ?? group?.Workspaces.Find(ScenarioNames(request).RootWorkspace);
        }

        if (workspace is null)
        {
            SetNotObservable(result, "selected_workspace_not_found");
        }
        return workspace;
    }

    private static MappedObject? ResolveMapping(
        Project project,
        VciMutationProbeRequestInfo request,
        VciMutationProbeCaseResultInfo result)
    {
        if (request.Mapping is null)
        {
            var scenarioWorkspace = ResolveSelectedOrScenarioWorkspace(project, request, result);
            if (scenarioWorkspace is null
                || !TryResolveEngineeringObject(project, request, result, out var scenarioObject))
            {
                SetNotObservableUnlessTerminal(result, "selected_mapping_not_found");
                return null;
            }

            var scenarioMapping = scenarioWorkspace.MappedObjects.Find(scenarioObject!);
            if (scenarioMapping is null)
            {
                SetNotObservable(result, "selected_mapping_not_found");
            }
            return scenarioMapping;
        }

        if (!TryAcquireRoot(project, result, out _, out var root))
        {
            SetNotObservableUnlessTerminal(result, "selected_mapping_not_found");
            return null;
        }

        var workspace = ResolveWorkspace(root!, request.Mapping.Workspace);
        var objectResolution = VciProbeEngineeringObjectResolver.Resolve(
            project,
            ToReadRequest(request),
            request.Mapping.EngineeringObject);
        if (workspace is null
            || objectResolution.Candidate?.EngineeringObject is not IEngineeringObject engineeringObject)
        {
            SetNotObservable(result, "selected_mapping_not_found");
            return null;
        }

        var mapping = workspace.MappedObjects.Find(engineeringObject);
        if (mapping is null
            || !MappingFileIdentityMatches(mapping, request.Mapping))
        {
            SetNotObservable(result, "selected_mapping_not_found");
            return null;
        }

        return mapping;
    }

    private static bool MappingFileIdentityMatches(MappedObject mapping, VciMappingSelectorInfo selector)
        => (selector.RelativeDirectory is null
                || string.Equals(
                    CanonicalPath(mapping.DirectoryPath),
                    CanonicalPath(selector.RelativeDirectory),
                    StringComparison.Ordinal))
            && (selector.FileName is null
                || string.Equals(mapping.FileNameWithoutExtension, selector.FileName, StringComparison.Ordinal))
            && (selector.Format is null
                || string.Equals(mapping.FileFormat, selector.Format, StringComparison.Ordinal));

    private static bool ScenarioWorkspaceRootsAreConfined(
        VciProbeSnapshotInfo snapshot,
        VciMutationProbeRequestInfo request)
    {
        var prefix = ScenarioNames(request).Prefix;
        var root = CanonicalPath(request.WorkspaceRoot);
        if (root is null)
        {
            return false;
        }

        foreach (var workspace in snapshot.Workspaces)
        {
            if (workspace.Name.StartsWith(prefix, StringComparison.Ordinal)
                && (workspace.RootPath is null || !IsWithinRoot(root, workspace.RootPath)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsWithinRoot(string root, string candidate)
    {
        var canonicalCandidate = CanonicalPath(candidate);
        if (canonicalCandidate is null)
        {
            return false;
        }

        var separator = Path.DirectorySeparatorChar.ToString();
        var rootWithSeparator = root.EndsWith(separator, StringComparison.Ordinal)
            ? root
            : root + separator;
        return string.Equals(canonicalCandidate, root, StringComparison.OrdinalIgnoreCase)
            || canonicalCandidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static ScenarioIdentity ScenarioNames(VciMutationProbeRequestInfo request)
    {
        var compactRun = new string(request.RunId.Where(char.IsLetterOrDigit).Take(12).ToArray());
        if (compactRun.Length == 0)
        {
            compactRun = "Run";
        }

        var compactScenario = new string(request.ScenarioId.Where(char.IsLetterOrDigit).Take(12).ToArray());
        if (compactScenario.Length == 0)
        {
            compactScenario = "Scenario";
        }

        var prefix = "CodexVci_" + compactRun + "_" + compactScenario;
        return new ScenarioIdentity(
            prefix,
            prefix,
            prefix + "_Nested",
            prefix + "_Root",
            prefix + "_Language");
    }

    private static string ScenarioWorkspacePath(VciMutationProbeRequestInfo request, string workspaceName)
    {
        var root = CanonicalPath(request.WorkspaceRoot)
            ?? throw new ArgumentException("The workspace root is not canonicalizable.", nameof(request));
        var path = CanonicalPath(Path.Combine(root, workspaceName))
            ?? throw new ArgumentException("The workspace path is not canonicalizable.", nameof(request));
        if (!IsWithinRoot(root, path))
        {
            throw new ArgumentException("The workspace path escapes the approved root.", nameof(request));
        }
        return path;
    }

    private static string? CanonicalPath(object? value)
    {
        try
        {
            return value switch
            {
                DirectoryInfo directory => Path.GetFullPath(directory.FullName).TrimEnd(Path.DirectorySeparatorChar),
                FileInfo file => Path.GetFullPath(file.FullName),
                string path when !string.IsNullOrWhiteSpace(path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar),
                _ => null,
            };
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return null;
        }
    }

    private static VciProbeReturnInfo ToReturnInfo(
        object? value,
        int maxCollectionItems,
        List<VciProbeOmissionInfo> omissions)
    {
        var normalized = VciProbeValueNormalizer.Normalize(value, maxCollectionItems);
        if (normalized.Omission is not null)
        {
            omissions.Add(normalized.Omission);
        }

        return new VciProbeReturnInfo
        {
            ClrTypeName = normalized.RuntimeType,
            IsNull = string.Equals(normalized.Kind, "null", StringComparison.Ordinal),
            StringValue = normalized.StringValue
                ?? normalized.CanonicalPath
                ?? normalized.OriginalPath
                ?? normalized.EnumName
                ?? normalized.EnumIntegralValue,
        };
    }

    private static void RecordException(VciMutationProbeCaseResultInfo result, Exception exception)
    {
        var normalized = VciProbeExceptionNormalizer.Normalize(exception);
        result.Outcome = "threw";
        result.Exception = ToExceptionInfo(normalized);
    }

    private static VciProbeExceptionInfo ToExceptionInfo(VciProbeNormalizedExceptionInfo exception)
        => new()
        {
            ExceptionTypeName = exception.ExceptionTypeName,
            Message = exception.Message,
            HResult = exception.HResult,
            InnerException = exception.InnerException is null
                ? null
                : ToExceptionInfo(exception.InnerException),
        };

    private static bool IsEvidenceException(Exception exception)
        => exception is EngineeringException
            or InvalidOperationException
            or ArgumentException
            or IOException;

    private static void AddCheck(
        List<VciMutationCheckInfo> checks,
        string name,
        bool satisfied,
        string? detail)
        => checks.Add(new VciMutationCheckInfo
        {
            Name = name,
            Satisfied = satisfied,
            Detail = detail,
        });

    private static void SetNotObservable(VciMutationProbeCaseResultInfo result, string reason)
    {
        result.Outcome = "not_observable";
        result.NotObservableReason = reason;
        result.Return = null;
        result.Exception = null;
    }

    private static void SetNotObservableUnlessTerminal(VciMutationProbeCaseResultInfo result, string reason)
    {
        if (string.IsNullOrEmpty(result.Outcome))
        {
            SetNotObservable(result, reason);
        }
    }

    private sealed class InventoryWorkspaceSelection
    {
        public InventoryWorkspaceSelection(
            Workspace workspace,
            VciWorkspaceSelectorInfo selector,
            List<string> formats)
        {
            Workspace = workspace;
            Selector = selector;
            Formats = formats;
        }

        public Workspace Workspace { get; }
        public VciWorkspaceSelectorInfo Selector { get; }
        public List<string> Formats { get; }
    }

    private sealed class ScenarioIdentity
    {
        public ScenarioIdentity(
            string prefix,
            string group,
            string nestedGroup,
            string rootWorkspace,
            string languageWorkspace)
        {
            Prefix = prefix;
            Group = group;
            NestedGroup = nestedGroup;
            RootWorkspace = rootWorkspace;
            LanguageWorkspace = languageWorkspace;
        }

        public string Prefix { get; }
        public string Group { get; }
        public string NestedGroup { get; }
        public string RootWorkspace { get; }
        public string LanguageWorkspace { get; }
    }

    private sealed class ExportTarget
    {
        public ExportTarget(
            DirectoryInfo directory,
            string fileNameWithoutExtension,
            string canonicalFilePath)
        {
            Directory = directory;
            FileNameWithoutExtension = fileNameWithoutExtension;
            CanonicalFilePath = canonicalFilePath;
        }

        public DirectoryInfo Directory { get; }
        public string FileNameWithoutExtension { get; }
        public string CanonicalFilePath { get; }
    }

    private enum ObjectInput
    {
        Null,
        Unsupported,
        AlreadyMapped,
    }

    private enum FormatInput
    {
        Null,
        Empty,
        Unsupported,
        WrongCase,
        Mismatch,
    }

    private enum ConnectInput
    {
        Missing,
        Malformed,
        PartialFileSet,
    }

    private enum SynchronizationInput
    {
        Missing,
        Malformed,
        Unchanged,
        ProjectOnly,
        WorkspaceOnly,
        BothSides,
    }

    private enum TransactionMutation
    {
        Group,
        Workspace,
        Export,
        Connect,
        ProjectToWorkspace,
        WorkspaceToProject,
        Disconnect,
        DeleteWorkspace,
        DeleteGroup,
    }

    private enum GroupNameInput
    {
        Null,
        Empty,
        Whitespace,
        Duplicate,
        Invalid,
    }

    private enum WorkspaceNameInput
    {
        Null,
        Empty,
        Whitespace,
        Duplicate,
        Invalid,
    }
}
