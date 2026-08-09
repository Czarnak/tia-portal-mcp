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
        var canary = SliceMethod(source, "private static void RunCanaryCase", "private static");

        Assert.Contains("GetSupportedFileFormats", inventory, StringComparison.Ordinal);
        Assert.Contains("StringComparer.Ordinal", inventory, StringComparison.Ordinal);
        Assert.Contains("\"SimaticML\"", inventory, StringComparison.Ordinal);
        Assert.Contains("VciProbeEngineeringObjectResolver.Resolve", inventory, StringComparison.Ordinal);
        Assert.DoesNotContain("ExclusiveAccess", inventory, StringComparison.Ordinal);
        Assert.DoesNotContain("Transaction", inventory, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.Create", inventory, StringComparison.Ordinal);

        Assert.Contains("CaptureSnapshot", canary, StringComparison.Ordinal);
        Assert.DoesNotContain("ExclusiveAccess", canary, StringComparison.Ordinal);
        Assert.DoesNotContain("Transaction", canary, StringComparison.Ordinal);
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
