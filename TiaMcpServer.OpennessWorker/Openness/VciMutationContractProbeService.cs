using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
                "M-EXPORT" => () => RunDeferredCase(result),
                "M-DISCONNECT" => () => RunDeferredCase(result),
                "M-CONNECT" => () => RunDeferredCase(result),
                "M-P2W" => () => RunDeferredCase(result),
                "M-W2P" => () => RunDeferredCase(result),
                "M-DELETE-MAPPING" => () => RunDeleteMapping(currentPortal, project, request, result),
                "M-DELETE-WORKSPACE" => () => RunDeleteWorkspace(currentPortal, project, request, result),
                "M-DELETE-GROUP" => () => RunDeleteGroups(currentPortal, project, request, result),
                "M-TX-GROUP" => () => RunDeferredCase(result),
                "M-TX-WORKSPACE" => () => RunDeferredCase(result),
                "M-TX-EXPORT" => () => RunDeferredCase(result),
                "M-TX-CONNECT" => () => RunDeferredCase(result),
                "M-TX-P2W" => () => RunDeferredCase(result),
                "M-TX-W2P" => () => RunDeferredCase(result),
                "M-TX-DISCONNECT" => () => RunDeferredCase(result),
                "M-TX-DELETE-WORKSPACE" => () => RunDeferredCase(result),
                "M-TX-DELETE-GROUP" => () => RunDeferredCase(result),
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
                "N-OBJECT-NULL" => () => RunDeferredCase(result),
                "N-OBJECT-UNSUPPORTED" => () => RunDeferredCase(result),
                "N-OBJECT-FOREIGN" => () => RunDeferredCase(result),
                "N-OBJECT-DISPOSED" => () => RunDeferredCase(result),
                "N-OBJECT-ALREADY-MAPPED" => () => RunDeferredCase(result),
                "N-OBJECT-DELETED" => () => RunDeferredCase(result),
                "N-FORMAT-NULL" => () => RunDeferredCase(result),
                "N-FORMAT-EMPTY" => () => RunDeferredCase(result),
                "N-FORMAT-UNSUPPORTED" => () => RunDeferredCase(result),
                "N-FORMAT-WRONG-CASE" => () => RunDeferredCase(result),
                "N-FORMAT-MISMATCH" => () => RunDeferredCase(result),
                "N-FILENAME-INVALID" => () => RunDeferredCase(result),
                "N-FILENAME-ABSOLUTE" => () => RunDeferredCase(result),
                "N-FILENAME-TRAVERSAL" => () => RunDeferredCase(result),
                "N-FILENAME-COLLISION" => () => RunDeferredCase(result),
                "N-CONNECT-MISSING" => () => RunDeferredCase(result),
                "N-CONNECT-MALFORMED" => () => RunDeferredCase(result),
                "N-CONNECT-WRONG-OBJECT" => () => RunDeferredCase(result),
                "N-CONNECT-PARTIAL-FILE-SET" => () => RunDeferredCase(result),
                "N-SYNC-MISSING" => () => RunDeferredCase(result),
                "N-SYNC-MALFORMED" => () => RunDeferredCase(result),
                "N-SYNC-UNCHANGED" => () => RunDeferredCase(result),
                "N-SYNC-PROJECT-ONLY" => () => RunDeferredCase(result),
                "N-SYNC-WORKSPACE-ONLY" => () => RunDeferredCase(result),
                "N-SYNC-BOTH-SIDES" => () => RunDeferredCase(result),
                "N-SYNC-INVALID-ENUM" => () => RunDeferredCase(result),
                "N-DELETE-NONEMPTY" => () => RunDeleteNonemptyGroup(currentPortal, project, request, result),
                "N-DELETE-TWICE" => () => RunDeleteTwice(currentPortal, project, request, result),
                "N-STALE-MAPPING-PROXY" => () => RunStaleMappingProxy(currentPortal, project, request, result),
                _ => throw new ArgumentException(
                    "The VCI mutation-probe case ID is not in the locked vocabulary.",
                    nameof(request)),
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

        result.Before = CaptureSnapshot(project, request, result);
        if (!TryAcquireRoot(project, result, out _, out var root)
            || request.Workspace is null)
        {
            SetNotObservableUnlessTerminal(result, "selected_workspace_not_found");
            return;
        }

        var workspace = ResolveWorkspace(root!, request.Workspace);
        if (workspace is null)
        {
            SetNotObservable(result, "selected_workspace_not_found");
            return;
        }

        var readRequest = ToReadRequest(request);
        var objectResolution = VciProbeEngineeringObjectResolver.Resolve(
            project,
            readRequest,
            request.EngineeringObject!);
        if (objectResolution.Candidate?.EngineeringObject is not IEngineeringObject engineeringObject)
        {
            SetNotObservable(result, "selected_engineering_object_not_found");
            return;
        }

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
            var formats = workspace.GetSupportedFileFormats(engineeringObject).ToList();
            var supportsSimaticMl = formats.Contains("SimaticML", StringComparer.Ordinal);
            AddCheck(result.Preconditions, "exact_SimaticML_supported", supportsSimaticMl, null);
            if (!supportsSimaticMl)
            {
                SetNotObservable(result, "selected_format_not_supported");
                return;
            }

            result.Return = ToReturnInfo(formats, request.MaxCollectionItems, result.Omissions);
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

        result.After = CaptureSnapshot(project, request, result);
        var rootAbsentAfter = !Directory.Exists(request.WorkspaceRoot) && !File.Exists(request.WorkspaceRoot);
        AddCheck(result.SafetyInvariants, "workspace_root_absent_after_inventory", rootAbsentAfter, null);
        if (!rootAbsentAfter)
        {
            result.UncertainOutcome = true;
            result.StopScenarioFamily = true;
        }
    }

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
        var workspaceName = withLanguage ? names.LanguageWorkspace : names.RootWorkspace;
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
                : new FileInfo(request.WorkspaceRoot + "\0invalid-library.al21");
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
                result.Return = ToReturnInfo(returnValue, request.MaxCollectionItems, result.Omissions);

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
            workspace = group?.Workspaces.Find(ScenarioNames(request).LanguageWorkspace)
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
        if (request.Mapping is null
            || !TryAcquireRoot(project, result, out _, out var root))
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
        var compact = new string(request.RunId.Where(char.IsLetterOrDigit).Take(12).ToArray());
        if (compact.Length == 0)
        {
            compact = "Run";
        }

        var prefix = "CodexVci_" + compact;
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
