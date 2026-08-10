using System.Text.RegularExpressions;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Workspace;

public class VciMutationProbeWorkerSourceContractTests
{
    [Fact]
    public void WorkerProgram_ValidatesTheRawEnvelopeBeforeDeserializationAndDispatchesOnePrivateHandler()
    {
        var source = File.ReadAllText(FindRepositoryFile("TiaMcpServer.OpennessWorker", "Program.cs"));

        var boundaryIndex = source.IndexOf("VciMutationProbeJsonBoundary.Validate(line)", StringComparison.Ordinal);
        var deserializeIndex = source.IndexOf("JsonSerializer.Deserialize<WorkerRequest>", StringComparison.Ordinal);
        Assert.True(boundaryIndex >= 0, "The raw mutation envelope must be validated.");
        Assert.True(boundaryIndex < deserializeIndex, "Raw validation must run before permissive deserialization.");
        Assert.Single(Regex.Matches(source, "\"probe_vci_mutation_contract\" =>").Cast<Match>());
        Assert.Contains("private static WorkerResponse ProbeVciMutationContract(WorkerRequest request)", source, StringComparison.Ordinal);

        var handler = SliceMethod(source, "private static WorkerResponse ProbeVciMutationContract", "private static");
        Assert.True(handler.IndexOf("VciMutationProbeContract.Validate", StringComparison.Ordinal)
            < handler.IndexOf("WithProject", StringComparison.Ordinal));
        Assert.Contains("VciMutationContractProbeService.Execute", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerProgram_MutationProbeRebindsOnlyWorkerOwnedProjectsAndClosesThemWithoutSaving()
    {
        var program = File.ReadAllText(FindRepositoryFile("TiaMcpServer.OpennessWorker", "Program.cs"));
        var mutationHandler = SliceMethod(program, "private static WorkerResponse ProbeVciMutationContract", "private static");
        var readHandler = SliceMethod(program, "private static WorkerResponse ProbeVciReadContract", "private static");
        var rebindGate = SliceMethod(program, "private static WorkerResponse? EnsureRequestedProjectOpen", "private static");
        var session = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker",
            "Openness",
            "TiaPortalSession.cs"));

        Assert.Contains("allowWorkerOwnedProjectRebind: true", mutationHandler, StringComparison.Ordinal);
        Assert.Contains("RequestWorkerOpenedProjectCloseOnDispose", mutationHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("allowWorkerOwnedProjectRebind: true", readHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestWorkerOpenedProjectCloseOnDispose", readHandler, StringComparison.Ordinal);
        Assert.Contains("session.ProjectOpenedByWorker", rebindGate, StringComparison.Ordinal);
        Assert.Contains("session.OpenProject(requestedProjectPath!)", rebindGate, StringComparison.Ordinal);
        Assert.Contains("!session.ProjectOpenedByWorker", rebindGate, StringComparison.Ordinal);
        Assert.Contains("requires TIA Portal to have no user-opened project", rebindGate, StringComparison.Ordinal);
        Assert.Contains("internal bool ProjectOpenedByWorker", session, StringComparison.Ordinal);
        AssertOrdered(
            session,
            "if (disposing && _closeWorkerOpenedProjectOnDispose && _projectOpenedByWorker",
            "Project.Close()",
            "_tiaPortal?.Dispose()");
        Assert.DoesNotContain("Project.Save", session, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerProgram_MutationProbeWaitsSixtySecondsAfterEachWorkerOwnedProjectOpen()
    {
        var program = File.ReadAllText(FindRepositoryFile("TiaMcpServer.OpennessWorker", "Program.cs"));
        var mutationHandler = SliceMethod(program, "private static WorkerResponse ProbeVciMutationContract", "private static");

        Assert.Contains("TimeSpan.FromSeconds(60)", program, StringComparison.Ordinal);
        AssertOrdered(
            mutationHandler,
            "WaitForWorkerOpenedProjectSettlement(ProjectOpenSettlementDelay)",
            "VciMutationContractProbeService.Execute");
    }

    [Fact]
    public void Service_DispatchesEveryLockedCaseAndRejectsUnknownCases()
    {
        var source = ServiceSource();

        foreach (var caseId in VciMutationProbeContract.CaseIds)
        {
            Assert.Single(Regex.Matches(source, Regex.Escape("\"" + caseId + "\" =>")).Cast<Match>());
        }

        Assert.Contains("throw new ArgumentException", source, StringComparison.Ordinal);
        Assert.Contains("not in the locked vocabulary", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_HasNoArbitraryInvocationOrProjectPersistenceEscapeHatch()
    {
        var source = ServiceSource();

        Assert.DoesNotContain("dynamic", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IEngineeringObject.Invoke", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Project.Save", source, StringComparison.Ordinal);
        Assert.DoesNotContain("project.Save", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAs", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Compile", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_InventoryAndCanaryAreReadOnlyAndInventoryRequiresExactSimaticMl()
    {
        var source = ServiceSource();
        var inventory = SliceMethod(source, "private static void RunInventory", "private static");
        var inventoryDiscovery = SliceMethod(
            source,
            "private static InventoryWorkspaceSelection? FindInventoryWorkspace",
            "private static VciProbeReturnInfo InventoryReturn");
        var canary = SliceMethod(source, "private static void RunCanaryCase", "private static");

        Assert.Contains("GetSupportedFileFormats", inventoryDiscovery, StringComparison.Ordinal);
        Assert.Contains("StringComparer.Ordinal", inventoryDiscovery, StringComparison.Ordinal);
        Assert.Contains("\"SimaticML\"", inventoryDiscovery, StringComparison.Ordinal);
        Assert.Contains("VciProbeEngineeringObjectCatalog.Enumerate", inventory, StringComparison.Ordinal);
        Assert.Contains("FindInventoryWorkspace", inventory, StringComparison.Ordinal);
        Assert.DoesNotContain("ExclusiveAccess", inventory, StringComparison.Ordinal);
        Assert.DoesNotContain("Transaction", inventory, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.Create", inventory, StringComparison.Ordinal);

        Assert.Contains("CaptureSnapshot", canary, StringComparison.Ordinal);
        Assert.DoesNotContain("ExclusiveAccess", canary, StringComparison.Ordinal);
        Assert.DoesNotContain("Transaction", canary, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_InventoryReportsProgressAroundEveryPotentiallyBlockingOpennessBoundary()
    {
        var source = ServiceSource();
        var inventory = SliceMethod(source, "private static void RunInventory", "private static");

        AssertOrdered(
            inventory,
            "TraceProgress(request, \"before_snapshot:start\")",
            "result.Before = CaptureSnapshot",
            "TraceProgress(request, \"before_snapshot:complete\")",
            "TraceProgress(request, \"workspace_root:start\")",
            "TryAcquireRoot",
            "TraceProgress(request, \"workspace_root:complete\")",
            "TraceProgress(request, \"engineering_object_catalog:start\")",
            "VciProbeEngineeringObjectCatalog.Enumerate",
            "TraceProgress(request, \"engineering_object_catalog:complete\")",
            "TraceProgress(request, \"supported_format_discovery:start\")",
            "FindInventoryWorkspace",
            "TraceProgress(request, \"supported_format_discovery:complete\")",
            "TraceProgress(request, \"after_snapshot:start\")",
            "result.After = CaptureSnapshot",
            "TraceProgress(request, \"after_snapshot:complete\")");
        Assert.Contains("Console.Error.WriteLine", source, StringComparison.Ordinal);
        Assert.Contains("caseInstanceId", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_CompletesSnapshotsAndCanaryForPreconditionDrivenApplyObservations()
    {
        var source = ServiceSource();
        var execute = SliceMethod(source, "public static VciMutationProbeCaseResultInfo Execute", "private static");
        var completion = SliceMethod(source, "private static void CompleteApplyObservation", "private static");

        AssertOrdered(execute, "dispatch();", "CompleteApplyObservation", "return result;");
        Assert.Contains("result.Before ??= CaptureSnapshot", completion, StringComparison.Ordinal);
        Assert.Contains("result.After ??= CaptureSnapshot", completion, StringComparison.Ordinal);
        Assert.Contains("result.Canary = RunCanary", completion, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_Task4MutationsUseTheSharedExclusiveTransactionGate()
    {
        var source = ServiceSource();
        var mutationGate = SliceMethod(source, "private static void RunCommittedMutation", "private static");

        Assert.Contains("tiaPortal.ExclusiveAccess", mutationGate, StringComparison.Ordinal);
        Assert.Contains("exclusiveAccess.Transaction(project", mutationGate, StringComparison.Ordinal);
        Assert.Contains("transaction.CanCommit", mutationGate, StringComparison.Ordinal);
        Assert.Contains("transaction.CommitOnDispose()", mutationGate, StringComparison.Ordinal);

        var snapshotIndex = mutationGate.IndexOf("CaptureSnapshot", StringComparison.Ordinal);
        var canaryIndex = mutationGate.IndexOf("RunCanary", StringComparison.Ordinal);
        var commitIndex = mutationGate.IndexOf("transaction.CommitOnDispose()", StringComparison.Ordinal);
        Assert.True(snapshotIndex >= 0 && snapshotIndex < commitIndex);
        Assert.True(canaryIndex >= 0 && canaryIndex < commitIndex);
    }

    [Fact]
    public void Service_Task4LifecycleAndNegativeCasesUseTypedVciMembers()
    {
        var source = ServiceSource();

        Assert.Contains("root.Groups.Create", source, StringComparison.Ordinal);
        Assert.Contains("group.Groups.Create", source, StringComparison.Ordinal);
        Assert.Contains("group.Workspaces.Create", source, StringComparison.Ordinal);
        Assert.Contains("CultureInfo.GetCultureInfo(\"en-US\")", source, StringComparison.Ordinal);
        Assert.Contains("mapping.Delete()", source, StringComparison.Ordinal);
        Assert.Contains("workspace.Delete()", source, StringComparison.Ordinal);
        Assert.Contains("group.Delete()", source, StringComparison.Ordinal);
        Assert.Contains("signature_does_not_permit_argument", source, StringComparison.Ordinal);
        Assert.Contains("harness_confinement_rejected_before_worker", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_Task5ExportUsesGuardedTypedOrderBeforeTheSharedCanary()
    {
        var source = ServiceSource();
        var export = SliceMethod(source, "private static void RunExport", "private static");
        AssertOrdered(
            export,
            "GetSupportedFileFormats",
            "Contains(\"SimaticML\", StringComparer.Ordinal)",
            "ResolveExportTarget",
            "workspace.ExportObject",
            "workspace.MappedObjects.Find",
            "BuildMappingReturn");

        var mutationGate = SliceMethod(source, "private static void RunCommittedMutation", "private static");
        AssertOrdered(mutationGate, "mutation()", "RunCanary");
    }

    [Fact]
    public void Service_Task5ConfinesPathsBeforeConstructingSiemensPathArguments()
    {
        var source = ServiceSource();
        var resolver = SliceMethod(source, "private static ExportTarget? ResolveExportTarget", "private static");

        AssertOrdered(
            resolver,
            "VciMutationPathPolicy.ResolveRelativeDirectory",
            "VciMutationPathPolicy.ResolveFile",
            "new DirectoryInfo");
        Assert.DoesNotContain("new DirectoryInfo(request.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new FileInfo(request.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_Task5DisconnectConnectAndEvidenceUseTypedVciMembers()
    {
        var source = ServiceSource();
        var disconnect = SliceMethod(source, "private static void RunDisconnect", "private static");
        var connect = SliceMethod(source, "private static void RunConnect", "private static");
        var evidence = SliceMethod(source, "private static VciProbeReturnInfo BuildMappingReturn", "private static");

        AssertOrdered(disconnect, "CaptureBoundedFileSet", "mapping.Delete()", "CaptureBoundedFileSet");
        AssertOrdered(connect, "workspace.ConnectObject", "workspace.MappedObjects.Find", "BuildMappingReturn");
        Assert.Contains("mapping.Status", evidence, StringComparison.Ordinal);
        Assert.Contains("mapping.GetStatus()", evidence, StringComparison.Ordinal);
        Assert.Contains("mapping.GetChildStatus()", evidence, StringComparison.Ordinal);
        Assert.Contains("CaptureBoundedFileSet", evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_DoesNotLabelAValidSeedAsMalformedOrPartialConnectEvidence()
    {
        var source = ServiceSource();
        var connectNegative = SliceMethod(source, "private static void RunConnectNegative", "private static");

        Assert.Contains(
            "input is ConnectInput.Malformed or ConnectInput.PartialFileSet",
            connectNegative,
            StringComparison.Ordinal);
        AssertOrdered(
            connectNegative,
            "input is ConnectInput.Malformed or ConnectInput.PartialFileSet",
            "SetNotObservable",
            "workspace!.ConnectObject");
    }

    [Fact]
    public void Service_Task5CasesNoLongerUseTheDeferredHandler()
    {
        var source = ServiceSource();
        var task5Cases = new[]
        {
            "M-EXPORT", "M-DISCONNECT", "M-CONNECT",
            "N-OBJECT-NULL", "N-OBJECT-UNSUPPORTED", "N-OBJECT-FOREIGN",
            "N-OBJECT-DISPOSED", "N-OBJECT-ALREADY-MAPPED", "N-OBJECT-DELETED",
            "N-FORMAT-NULL", "N-FORMAT-EMPTY", "N-FORMAT-UNSUPPORTED",
            "N-FORMAT-WRONG-CASE", "N-FORMAT-MISMATCH",
            "N-FILENAME-INVALID", "N-FILENAME-ABSOLUTE", "N-FILENAME-TRAVERSAL",
            "N-FILENAME-COLLISION", "N-CONNECT-MISSING", "N-CONNECT-MALFORMED",
            "N-CONNECT-WRONG-OBJECT", "N-CONNECT-PARTIAL-FILE-SET",
        };

        foreach (var caseId in task5Cases)
        {
            var arm = Regex.Match(
                source,
                Regex.Escape("\"" + caseId + "\" =>") + @"[^\r\n]+",
                RegexOptions.CultureInvariant);
            Assert.True(arm.Success, $"Missing switch arm for {caseId}.");
            Assert.DoesNotContain("RunDeferredCase", arm.Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Service_Task6SynchronizationTreatsSynchronizeAsVoidAndReadsStateSeparately()
    {
        var source = ServiceSource();
        var synchronize = SliceMethod(source, "private static void RunSynchronization", "private static");

        AssertOrdered(
            synchronize,
            "mapping.GetStatus()",
            "CaptureBoundedFileSet",
            "mapping.Synchronize(mode);",
            "mapping.GetStatus()",
            "CaptureBoundedFileSet");
        Assert.DoesNotContain("= mapping.Synchronize", source, StringComparison.Ordinal);
        Assert.Contains("SynchronizationMode.ProjectToWorkspace", source, StringComparison.Ordinal);
        Assert.Contains("SynchronizationMode.WorkspaceToProject", source, StringComparison.Ordinal);
        Assert.Contains("IndividualObjectCompareDetails.ProjectObjectChanged", source, StringComparison.Ordinal);
        Assert.Contains("IndividualObjectCompareDetails.WorkspaceFileChanged", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_Task6InvalidSynchronizationEnumUsesOneExplicitCastOnly()
    {
        var source = ServiceSource();
        var invalid = SliceMethod(source, "private static void RunInvalidSynchronizationMode", "private static");

        Assert.Contains("mapping.Synchronize((SynchronizationMode)int.MaxValue);", invalid, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(source, @"\(SynchronizationMode\)int\.MaxValue").Cast<Match>());
    }

    [Fact]
    public void Service_Task6WorkspaceToProjectUsesIndependentVerificationExportAndHashes()
    {
        var source = ServiceSource();
        var synchronize = SliceMethod(source, "private static void RunSynchronization", "private static");

        Assert.Contains("ResolveComparisonTarget", synchronize, StringComparison.Ordinal);
        Assert.Contains("baseline_and_changed_exports_identical", synchronize, StringComparison.Ordinal);
        AssertOrdered(synchronize, "controlledExportsDiffer", "expectedDifferenceEstablished");
        Assert.Contains("ResolveVerificationTarget", synchronize, StringComparison.Ordinal);
        Assert.Contains("verificationWorkspace!.ExportObject", synchronize, StringComparison.Ordinal);
        Assert.Contains("HashSetsEqual", synchronize, StringComparison.Ordinal);
        Assert.Contains("ComputeSha256", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Compile", synchronize, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_ScenarioStateIsScopedByRunAndScenarioAndDoesNotRequireAnInventoryMapping()
    {
        var source = ServiceSource();
        var names = SliceMethod(source, "private static ScenarioIdentity ScenarioNames", "private static");
        var workspaceResolver = SliceMethod(
            source,
            "private static Workspace? ResolveSelectedOrScenarioWorkspace",
            "private static");
        var mappingResolver = SliceMethod(source, "private static MappedObject? ResolveMapping", "private static");

        Assert.Contains("request.RunId", names, StringComparison.Ordinal);
        Assert.Contains("request.ScenarioId", names, StringComparison.Ordinal);
        Assert.Contains("request.WorkspaceName", workspaceResolver, StringComparison.Ordinal);
        Assert.Contains("ResolveSelectedOrScenarioWorkspace", mappingResolver, StringComparison.Ordinal);
        Assert.Contains("TryResolveEngineeringObject", mappingResolver, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_TransactionGroupDeletionTargetsTheEmptyNestedScenarioGroup()
    {
        var source = ServiceSource();
        var transactionCase = SliceMethod(source, "private static void RunTransactionCase", "private static");

        Assert.Contains("names.NestedGroup", transactionCase, StringComparison.Ordinal);
        Assert.Contains("nestedGroup.Delete()", transactionCase, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_Task6RollbackGateOmitsCommitAndSeparatesProjectFromFileEvidence()
    {
        var source = ServiceSource();
        var rollback = SliceMethod(source, "private static void RunRollbackOnlyMutation", "private static");

        Assert.Contains("tiaPortal.ExclusiveAccess", rollback, StringComparison.Ordinal);
        Assert.Contains("exclusiveAccess.Transaction(project", rollback, StringComparison.Ordinal);
        Assert.Contains("mutation()", rollback, StringComparison.Ordinal);
        Assert.Contains("CaptureSnapshot", rollback, StringComparison.Ordinal);
        Assert.Contains("CaptureBoundedFileSet", rollback, StringComparison.Ordinal);
        Assert.Contains("RunCanary", rollback, StringComparison.Ordinal);
        Assert.DoesNotContain("CommitOnDispose", rollback, StringComparison.Ordinal);
        Assert.Contains("project_state_rolled_back", rollback, StringComparison.Ordinal);
        Assert.Contains("external_files_rolled_back", rollback, StringComparison.Ordinal);
        Assert.Contains("if (!projectStateRolledBack)", rollback, StringComparison.Ordinal);
        Assert.Contains("result.UncertainOutcome = true", rollback, StringComparison.Ordinal);
        Assert.Contains("result.StopScenarioFamily = true", rollback, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_Task6CasesNoLongerUseTheDeferredHandler()
    {
        var source = ServiceSource();
        var task6Cases = new[]
        {
            "M-P2W", "M-W2P", "M-TX-GROUP", "M-TX-WORKSPACE", "M-TX-EXPORT",
            "M-TX-CONNECT", "M-TX-P2W", "M-TX-W2P", "M-TX-DISCONNECT",
            "M-TX-DELETE-WORKSPACE", "M-TX-DELETE-GROUP", "N-SYNC-MISSING",
            "N-SYNC-MALFORMED", "N-SYNC-UNCHANGED", "N-SYNC-PROJECT-ONLY",
            "N-SYNC-WORKSPACE-ONLY", "N-SYNC-BOTH-SIDES", "N-SYNC-INVALID-ENUM",
        };

        foreach (var caseId in task6Cases)
        {
            var arm = Regex.Match(
                source,
                Regex.Escape("\"" + caseId + "\" =>") + @"[^\r\n]+",
                RegexOptions.CultureInvariant);
            Assert.True(arm.Success, $"Missing switch arm for {caseId}.");
            Assert.DoesNotContain("RunDeferredCase", arm.Value, StringComparison.Ordinal);
        }
    }

    private static string ServiceSource()
        => File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker",
            "Openness",
            "VciMutationContractProbeService.cs"));

    private static string SliceMethod(string source, string startMarker, string nextMethodMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing method marker '{startMarker}'.");
        var next = source.IndexOf(nextMethodMarker, start + startMarker.Length, StringComparison.Ordinal);
        return next < 0 ? source.Substring(start) : source.Substring(start, next - start);
    }

    private static void AssertOrdered(string source, params string[] markers)
    {
        var previous = -1;
        foreach (var marker in markers)
        {
            var current = source.IndexOf(marker, previous + 1, StringComparison.Ordinal);
            Assert.True(current >= 0, $"Missing ordered marker '{marker}'.");
            Assert.True(current > previous, $"Marker '{marker}' was out of order.");
            previous = current;
        }
    }

    private static string FindRepositoryFile(params string[] parts)
        => Path.Combine(new[] { FindRepositoryRoot() }.Concat(parts).ToArray());

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TiaMcpServer.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
