using System.Reflection;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using TiaMcpServer.Tools;
using Xunit;

namespace TiaMcpServer.Tests.Workspace;

/// <summary>
/// Source-contract tests for the internal <c>probe_vci_read_contract</c> worker dispatch seam.
///
/// <para>
/// <c>Program.cs</c> and <c>VciReadContractProbeService.cs</c> live in the net48
/// <c>TiaMcpServer.OpennessWorker</c> project and call into <c>Siemens.Engineering.*</c> types, so
/// (unlike the vendor-free <c>VciProbeJsonBoundary</c>) they cannot be linked into this net8 test
/// project or exercised behaviorally here. These tests instead read the worker source files as
/// text and assert the exact structural facts Task 2 requires — the same pattern used by
/// <c>NetworkIntrospectionWorkerDispatchTests</c> for the analogous network dispatch seam.
/// </para>
/// </summary>
public class VciReadProbeWorkerSourceContractTests
{
    [Fact]
    public void WorkerProgram_DispatchesProbeVciReadContractToTheNarrowHandler()
    {
        var source = File.ReadAllText(FindRepositoryFile("TiaMcpServer.OpennessWorker", "Program.cs"));

        Assert.Contains("\"probe_vci_read_contract\" => ProbeVciReadContract(request)", source, StringComparison.Ordinal);
        Assert.Contains("private static WorkerResponse ProbeVciReadContract(WorkerRequest request)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProbeVciReadContractHandler_ValidatesTheProbeRequestBeforeWithProject()
    {
        // Normalized to LF so the "\n    }\n" method-end delimiter matches regardless of whether
        // the file is checked out with CRLF (Windows) or LF line endings.
        var source = File.ReadAllText(FindRepositoryFile("TiaMcpServer.OpennessWorker", "Program.cs"))
            .Replace("\r\n", "\n");

        var handlerStart = source.IndexOf(
            "private static WorkerResponse ProbeVciReadContract(WorkerRequest request)", StringComparison.Ordinal);
        Assert.True(handlerStart >= 0, "ProbeVciReadContract handler not found.");

        var handlerEnd = source.IndexOf("\n    }\n", handlerStart, StringComparison.Ordinal);
        Assert.True(handlerEnd > handlerStart, "Could not find the end of the ProbeVciReadContract handler.");

        var handlerBody = source[handlerStart..handlerEnd];

        var validateIndex = handlerBody.IndexOf("VciReadProbeContract.Validate(", StringComparison.Ordinal);
        var withProjectIndex = handlerBody.IndexOf("WithProject(", StringComparison.Ordinal);

        Assert.True(validateIndex >= 0, "Handler does not call VciReadProbeContract.Validate.");
        Assert.True(withProjectIndex >= 0, "Handler does not call WithProject.");
        Assert.True(
            validateIndex < withProjectIndex,
            "VciReadProbeContract.Validate must run before WithProject.");

        Assert.Contains("VciReadContractProbeService.Execute", handlerBody, StringComparison.Ordinal);
        Assert.Contains("Success(", handlerBody, StringComparison.Ordinal);
        Assert.Contains("WorkerFailureCategories.ValidationError", handlerBody, StringComparison.Ordinal);
    }

    [Fact]
    public void HandleLine_RunsTheVciJsonBoundaryBeforeNormalDeserialization()
    {
        var source = File.ReadAllText(FindRepositoryFile("TiaMcpServer.OpennessWorker", "Program.cs"));

        var handleLineStart = source.IndexOf("private static WorkerResponse HandleLine(string line)", StringComparison.Ordinal);
        Assert.True(handleLineStart >= 0, "HandleLine method not found.");

        var boundaryIndex = source.IndexOf("VciProbeJsonBoundary.Validate(", handleLineStart, StringComparison.Ordinal);
        var deserializeIndex = source.IndexOf(
            "JsonSerializer.Deserialize<WorkerRequest>", handleLineStart, StringComparison.Ordinal);

        Assert.True(boundaryIndex >= 0, "HandleLine does not call VciProbeJsonBoundary.Validate.");
        Assert.True(deserializeIndex >= 0, "HandleLine does not deserialize the WorkerRequest.");
        Assert.True(
            boundaryIndex < deserializeIndex,
            "VciProbeJsonBoundary.Validate must run before normal WorkerRequest deserialization.");
    }

    [Fact]
    public void VciReadContractProbeService_DispatchesEveryLockedCaseAndRejectsUnknownCases()
    {
        var source = ReadProbeServiceSource();
        var execute = ReadMethod(source, "public static VciProbeCaseResultInfo Execute");
        var switchStart = execute.IndexOf("Action dispatch = request.CaseId switch", StringComparison.Ordinal);
        Assert.True(switchStart >= 0, "Execute does not dispatch on request.CaseId.");
        var switchEnd = execute.IndexOf("};", switchStart, StringComparison.Ordinal);
        Assert.True(switchEnd > switchStart, "Could not determine the end of the case dispatch switch.");
        var dispatch = execute[switchStart..(switchEnd + 2)];

        Assert.Contains("VciReadProbeContract.IsKnownCase(request.CaseId)", execute, StringComparison.Ordinal);
        Assert.True(
            execute.IndexOf("VciReadProbeContract.IsKnownCase(request.CaseId)", StringComparison.Ordinal) < switchStart,
            "Known-case validation must fail closed before dispatch.");

        var caseArms = Regex.Matches(dispatch, "\"(?<caseId>[^\"]+)\"\\s*=>", RegexOptions.CultureInvariant)
            .Select(match => match.Groups["caseId"].Value)
            .ToArray();
        Assert.Equal(VciReadProbeContract.CaseIds.Count, caseArms.Length);
        foreach (var caseId in VciReadProbeContract.CaseIds)
        {
            Assert.Equal(1, caseArms.Count(actual => string.Equals(actual, caseId, StringComparison.Ordinal)));
        }

        Assert.Matches(
            new Regex(@"_\s*=>\s*throw\s+new\s+ArgumentException\(", RegexOptions.CultureInvariant),
            dispatch);
    }

    [Fact]
    public void VciReadContractProbeService_SamplesProjectStateInFinallyForEveryOutcome()
    {
        var source = ReadProbeServiceSource();

        AssertMethodMatches(source,
            @"var\s+isModifiedBefore\s*=\s*project\.IsModified\s*;[\s\S]*try\s*\{[\s\S]*return\s+result\s*;[\s\S]*finally\s*\{[\s\S]*IsModifiedBefore\s*=\s*isModifiedBefore[\s\S]*IsModifiedAfter\s*=\s*project\.IsModified");
        Assert.Contains("not_observable", source, StringComparison.Ordinal);
        Assert.Contains("Exception = ToExceptionInfo(outcome.Exception)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProbeVciReadContractHandler_ReturnsDeliberateCaseExceptionsAsSuccessfulPayloads()
    {
        var program = File.ReadAllText(FindRepositoryFile("TiaMcpServer.OpennessWorker", "Program.cs"));
        var service = ReadProbeServiceSource();

        AssertMethodMatches(program,
            @"Success\(VciReadContractProbeService\.Execute\(tiaPortal,\s*project,\s*request\.VciProbe!?\)\)");
        Assert.Contains("Outcome = outcome.Outcome", service, StringComparison.Ordinal);
        Assert.Contains("Exception = ToExceptionInfo(outcome.Exception)", service, StringComparison.Ordinal);
    }

    [Fact]
    public void VciReadContractProbeService_FoldsNormalizedValueAndExceptionEvidenceIntoWireDtos()
    {
        var source = ReadProbeServiceSource();

        Assert.NotNull(typeof(VciProbeExceptionInfo).GetProperty("InnerException"));
        Assert.NotNull(typeof(VciProbeSnapshotInfo).GetProperty("Members"));
        Assert.Contains("InnerException = ToExceptionInfo(exception.InnerException)", source, StringComparison.Ordinal);
        Assert.Contains("normalized.Items", source, StringComparison.Ordinal);
        Assert.Contains("normalized.EnumName", source, StringComparison.Ordinal);
        Assert.Contains("normalized.EnumIntegralValue", source, StringComparison.Ordinal);
        Assert.Contains("normalized.OriginalPath", source, StringComparison.Ordinal);
        Assert.Contains("normalized.CanonicalPath", source, StringComparison.Ordinal);
        Assert.Contains("normalized.PathCanonicalizationException", source, StringComparison.Ordinal);
        Assert.Contains("normalized.Omission", source, StringComparison.Ordinal);

        var snapshot = ReadMethod(source, "private static void RunSnapshot");
        Assert.Contains("read.Snapshot.Members.AddRange(read.Members)", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("result.Return", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void VciReadProbeProductionSources_ContainNoProhibitedWriteCallSites()
    {
        var opennessDirectory = Path.GetDirectoryName(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "VciReadContractProbeService.cs"))!;
        var prohibitedCall = new Regex(
            @"\.\s*(?:Create|Delete|ConnectObject|ExportObject|Synchronize|SetAttribute|SetAttributes|Save|Compile|Download)\s*\(",
            RegexOptions.CultureInvariant);

        // Scan every Siemens-facing VCI probe source. Pure JSON/fingerprint helpers are excluded:
        // SHA256.Create() is not an Openness mutation and must not false-positive as VCI Create().
        foreach (var fileName in new[]
                 {
                     "VciReadContractProbeService.cs",
                     "VciProbeSnapshotReader.cs",
                     "VciProbeEngineeringObjectCatalog.cs",
                     "VciProbeEngineeringObjectResolver.cs",
                     "VciProbeObservationRunner.cs",
                     "VciProbeValueNormalizer.cs",
                 })
        {
            var file = Path.Combine(opennessDirectory, fileName);
            var source = File.ReadAllText(file);
            Assert.False(prohibitedCall.IsMatch(source), $"Prohibited write call found in {Path.GetFileName(file)}.");
        }
    }

    [Fact]
    public void VciReadContractProbeService_UsesOnlyTheLockedNegativeInvocations()
    {
        var source = ReadProbeServiceSource();
        var groupFind = ReadMethod(source, "private static void RunGroupFind");
        var workspaceFind = ReadMethod(source, "private static void RunWorkspaceFind");
        var nullFormat = ReadMethod(source, "private static void RunNullFormat");
        var unsupportedFormat = ReadMethod(source, "private static void RunUnsupportedFormat");
        var foreignFormat = ReadMethod(source, "private static void RunForeignFormat");

        Assert.Equal(2, CountOccurrences(groupFind, "groups.Find(missingName)"));
        Assert.Equal(1, CountOccurrences(groupFind, "groups.Find(string.Empty)"));
        Assert.Equal(1, CountOccurrences(groupFind, "groups.Find(\"   \")"));
        Assert.Equal(1, CountOccurrences(groupFind, "groups.Find(null!)"));
        Assert.Equal(5, CountOccurrences(groupFind, "groups.Find("));

        Assert.Equal(2, CountOccurrences(workspaceFind, "workspaces.Find(missingName)"));
        Assert.Equal(1, CountOccurrences(workspaceFind, "workspaces.Find(string.Empty)"));
        Assert.Equal(1, CountOccurrences(workspaceFind, "workspaces.Find(\"   \")"));
        Assert.Equal(1, CountOccurrences(workspaceFind, "workspaces.Find(null!)"));
        Assert.Equal(5, CountOccurrences(workspaceFind, "workspaces.Find("));

        AssertMethodMatches(nullFormat, @"workspace!?\.GetSupportedFileFormats\(null!\)");
        Assert.Equal(1, CountOccurrences(nullFormat, ".GetSupportedFileFormats("));
        AssertMethodMatches(unsupportedFormat, @"workspace!?\.GetSupportedFileFormats\(\(IEngineeringObject\)service!\)");
        Assert.Equal(1, CountOccurrences(unsupportedFormat, ".GetSupportedFileFormats("));
        AssertMethodMatches(foreignFormat, @"workspace!?\.GetSupportedFileFormats\(foreignObject\)");
        Assert.Equal(1, CountOccurrences(foreignFormat, ".GetSupportedFileFormats("));

        foreach (var method in new[] { groupFind, workspaceFind, nullFormat, unsupportedFormat, foreignFormat })
        {
            Assert.DoesNotContain("MethodInfo", method, StringComparison.Ordinal);
            Assert.DoesNotContain(".Invoke(", method, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void VciReadContractProbeService_RepeatabilityRetainsTwoOrderedObservationsAndCanaryReacquires()
    {
        var source = ReadProbeServiceSource();

        var repeatability = ReadMethod(source, "private static void RunRepeatability");
        Assert.Contains("var first =", repeatability, StringComparison.Ordinal);
        Assert.Contains("var second =", repeatability, StringComparison.Ordinal);
        Assert.Contains("Observations = new List<VciProbeReturnInfo> { first, second }", repeatability, StringComparison.Ordinal);
        Assert.Contains("IsIdentical =", repeatability, StringComparison.Ordinal);
        var repeatabilityObservation = ReadMethod(source, "private static VciProbeReturnInfo ReadRepeatabilityObservation");
        Assert.Contains("GetSupportedFileFormats", repeatabilityObservation, StringComparison.Ordinal);

        var canary = ReadMethod(source, "private static void RunCanary");
        Assert.Contains("project.GetService<VersionControlInterface>()", canary, StringComparison.Ordinal);
        Assert.Contains("service.WorkspaceGroup", canary, StringComparison.Ordinal);
        Assert.Contains("root.Groups.Count", canary, StringComparison.Ordinal);
        Assert.Contains("root.Workspaces.Count", canary, StringComparison.Ordinal);
    }

    [Fact]
    public void VciReadContractProbeService_ForeignAndMappedFileCasesAreReadOnlyAndFailClosed()
    {
        var source = ReadProbeServiceSource();

        Assert.Contains("SecondaryProjectPath", source, StringComparison.Ordinal);
        Assert.Contains("TiaPortal.GetProcesses()", source, StringComparison.Ordinal);
        AssertMethodMatches(source, @"matchingProcesses\[0\]\.Attach\(\)");
        Assert.Contains("!candidateProject.IsPrimary", source, StringComparison.Ordinal);
        Assert.Contains("secondary_project_path_not_supplied", source, StringComparison.Ordinal);
        Assert.Contains("secondary_project_candidate_not_unique", source, StringComparison.Ordinal);
        Assert.Contains("secondary_project_attach_denied", source, StringComparison.Ordinal);
        Assert.Contains("foreign_object_not_available", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Projects.Open(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Close(", source, StringComparison.Ordinal);

        Assert.Contains("File.Exists(", source, StringComparison.Ordinal);
        Assert.Contains("File.Open(", source, StringComparison.Ordinal);
        Assert.Contains("no_naturally_missing_mapping_file", source, StringComparison.Ordinal);
        Assert.Contains("no_naturally_inaccessible_mapping_file", source, StringComparison.Ordinal);
    }

    [Fact]
    public void VciReadContractProbeService_ForeignAcquisitionCatchesOnlyUnderstoodFailures()
    {
        var foreign = ReadMethod(ReadProbeServiceSource(), "private static void RunForeignFormat");

        Assert.Contains("catch (EngineeringSecurityException)", foreign, StringComparison.Ordinal);
        Assert.Contains("catch (RemotingException)", foreign, StringComparison.Ordinal);
        Assert.DoesNotContain("catch (EngineeringException)", foreign, StringComparison.Ordinal);
        Assert.DoesNotContain("NonRecoverableException", foreign, StringComparison.Ordinal);
    }

    [Fact]
    public void VciReadContractProbeService_ReportsBudgetExhaustionInsteadOfClaimingAbsence()
    {
        var source = ReadProbeServiceSource();
        var acquireWorkspace = ReadMethod(source, "private static bool TryAcquireWorkspace");
        var runMappedFileStatus = ReadMethod(source, "private static void RunMappedFileStatus");
        var findWorkspace = ReadMethod(source, "private static Workspace? FindFirstWorkspace");
        var findWorkspaceCore = ReadMethod(source, "private static Workspace? FindFirstWorkspaceCore");
        var findMapping = ReadMethod(source, "private static MappedFileCandidate? FindMappedFileCandidate");
        var findMappingCore = ReadMethod(source, "private static MappedFileCandidate? FindMappedFileCandidateCore");

        Assert.Contains("result.Omissions", acquireWorkspace, StringComparison.Ordinal);
        Assert.Contains("WorkspaceSearchIncomplete", acquireWorkspace, StringComparison.Ordinal);
        Assert.Contains("workspace_search_incomplete_budget_exhausted", source, StringComparison.Ordinal);
        Assert.Contains("searchIncomplete", acquireWorkspace, StringComparison.Ordinal);
        Assert.Contains("result.Omissions", runMappedFileStatus, StringComparison.Ordinal);
        Assert.Contains("MappedFileSearchIncomplete", runMappedFileStatus, StringComparison.Ordinal);
        Assert.Contains("mapped_file_search_incomplete_budget_exhausted", source, StringComparison.Ordinal);
        Assert.Contains("searchIncomplete", runMappedFileStatus, StringComparison.Ordinal);

        Assert.Contains("List<VciProbeOmissionInfo> omissions", findWorkspace, StringComparison.Ordinal);
        Assert.Contains("List<VciProbeOmissionInfo> omissions", findMapping, StringComparison.Ordinal);
        foreach (var budget in new[] { "MaxGroupDepth", "MaxGroups", "MaxWorkspaces" })
        {
            Assert.Contains($"nameof(request.{budget})", findWorkspaceCore, StringComparison.Ordinal);
        }
        foreach (var budget in new[] { "MaxGroupDepth", "MaxGroups", "MaxWorkspaces", "MaxMappings" })
        {
            Assert.Contains($"nameof(request.{budget})", findMappingCore, StringComparison.Ordinal);
        }
        Assert.Contains("TraversalPath = traversalPath", source, StringComparison.Ordinal);
    }

    [Fact]
    public void VciReadContractProbeService_WorkspaceBudgetOmissionUsesLocalPathIndexAndGlobalObservedCount()
    {
        var core = ReadMethod(
            ReadProbeServiceSource(),
            "private static Workspace? FindFirstWorkspaceCore");
        var workspaceLoopStart = core.IndexOf(
            "foreach (Workspace workspace in (IEnumerable)((dynamic)group).Workspaces)",
            StringComparison.Ordinal);
        Assert.True(workspaceLoopStart >= 0, "Workspace traversal loop was not found.");

        var childLoopStart = core.IndexOf(
            "foreach (var child in (IEnumerable)((dynamic)group).Groups)",
            workspaceLoopStart,
            StringComparison.Ordinal);
        Assert.True(childLoopStart > workspaceLoopStart, "Could not isolate the workspace traversal loop.");
        var workspaceLoop = core[workspaceLoopStart..childLoopStart];

        var localIndexDeclaration = core.IndexOf("var workspaceIndex = 0;", StringComparison.Ordinal);
        Assert.InRange(localIndexDeclaration, 0, workspaceLoopStart - 1);

        var budgetBranchStart = workspaceLoop.IndexOf(
            "if (workspacesObserved >= request.MaxWorkspaces)",
            StringComparison.Ordinal);
        var budgetBranchEnd = workspaceLoop.IndexOf(
            "searchIncomplete = true;",
            budgetBranchStart,
            StringComparison.Ordinal);
        Assert.True(budgetBranchStart >= 0 && budgetBranchEnd > budgetBranchStart, "Could not isolate the workspace-budget branch.");
        var budgetBranch = workspaceLoop[budgetBranchStart..budgetBranchEnd];

        AssertMethodMatches(
            budgetBranch,
            @"OmitSearch\(\s*omissions,\s*""[^""]+"",\s*nameof\(request\.MaxWorkspaces\),\s*request\.MaxWorkspaces,\s*workspacesObserved,\s*AppendTraversalPath\(traversalPath,\s*""workspaces"",\s*workspaceIndex\)\s*\)\s*;");
    }

    [Fact]
    public void VciReadContractProbeService_DisposesEveryDiscoveredProcessProxyAcrossForeignEarlyReturns()
    {
        var foreign = ReadMethod(ReadProbeServiceSource(), "private static void RunForeignFormat");
        var acquireIndex = foreign.IndexOf("var processes = TiaPortal.GetProcesses();", StringComparison.Ordinal);
        Assert.True(acquireIndex >= 0, "Foreign case does not retain every discovered process proxy.");

        var tryIndex = foreign.IndexOf("try", acquireIndex, StringComparison.Ordinal);
        Assert.True(tryIndex > acquireIndex, "Process selection must begin inside the disposal try/finally.");

        var filterIndex = foreign.IndexOf("processes.Where(", tryIndex, StringComparison.Ordinal);
        Assert.True(filterIndex > tryIndex, "Process-path reads must occur inside the disposal try/finally.");

        var zeroOrMultipleIndex = foreign.IndexOf("matchingProcesses.Count != 1", filterIndex, StringComparison.Ordinal);
        Assert.True(zeroOrMultipleIndex > filterIndex, "Zero/multiple selection must occur after bounded filtering.");

        var attachIndex = foreign.IndexOf("matchingProcesses[0].Attach()", zeroOrMultipleIndex, StringComparison.Ordinal);
        Assert.True(attachIndex > zeroOrMultipleIndex, "Attach must use the uniquely selected process proxy.");

        var sharedPortalGuardIndex = foreign.IndexOf("ReferenceEquals(attached, currentPortal)", attachIndex, StringComparison.Ordinal);
        Assert.True(sharedPortalGuardIndex > attachIndex, "The shared portal instance must be rejected after attach.");
        var attachedOwnershipIndex = foreign.IndexOf("using (attached)", attachIndex, StringComparison.Ordinal);
        Assert.True(
            attachedOwnershipIndex > sharedPortalGuardIndex,
            "The worker must not take disposal ownership until it proves the attached portal is not the shared instance.");

        var finallyIndex = foreign.LastIndexOf("finally", StringComparison.Ordinal);
        Assert.True(finallyIndex > attachedOwnershipIndex, "The foreign acquisition must have a final disposal path.");

        var disposeIndex = foreign.IndexOf("process.Dispose()", finallyIndex, StringComparison.Ordinal);

        Assert.DoesNotContain("return;", foreign[acquireIndex..tryIndex], StringComparison.Ordinal);
        Assert.True(disposeIndex > finallyIndex, "Every discovered process proxy must be disposed in finally.");
        Assert.Equal(1, CountOccurrences(foreign, "process.Dispose()"));
    }

    [Fact]
    public void NoPublicToolOrHostRegistrationSourceMentionsTheInternalVciProbeOperation()
    {
        var repositoryRoot = FindRepositoryRoot();
        var toolsDirectory = Path.Combine(repositoryRoot, "TiaMcpServer", "Tools");
        Assert.True(Directory.Exists(toolsDirectory), $"Expected tools directory at '{toolsDirectory}'.");

        foreach (var file in Directory.EnumerateFiles(toolsDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain("probe_vci_read_contract", content, StringComparison.Ordinal);
        }

        var programSource = File.ReadAllText(Path.Combine(repositoryRoot, "TiaMcpServer", "Program.cs"));
        Assert.DoesNotContain("probe_vci_read_contract", programSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Same whole-assembly enumeration <c>McpToolSchemaTests</c> uses, re-asserted here to prove
    /// this task's change did not alter the public tool surface: still exactly 14 read-write tools
    /// and 4 read-only tools, and the internal VCI probe is absent from both.
    /// </summary>
    [Fact]
    public void PublicToolSurface_RemainsFourteenReadWriteAndFourReadOnlyWithoutTheInternalVciProbe()
    {
        var toolTypes = typeof(ProjectLifecycleTools).Assembly
            .GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null);

        var readWriteToolNames = toolTypes
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>())
            .Where(a => a is not null)
            .Select(a => a!.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(14, readWriteToolNames.Length);
        Assert.DoesNotContain("probe_vci_read_contract", readWriteToolNames);

        var networkReadToolType = typeof(NetworkOperationRequest).Assembly.GetType("TiaMcpServer.Network.NetworkReadTools");
        Assert.NotNull(networkReadToolType);

        var readOnlyToolNames = new[]
            {
                typeof(ProjectReadTools),
                typeof(ReadBatchTools),
                networkReadToolType!,
            }
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
            .Where(attribute => attribute is not null)
            .Select(attribute => attribute!.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(4, readOnlyToolNames.Length);
        Assert.DoesNotContain("probe_vci_read_contract", readOnlyToolNames);
    }

    /// <summary>
    /// Source-contract tests for the Task 4 bounded engineering-object catalog and resolver.
    ///
    /// <para>
    /// <c>VciProbeEngineeringObjectCatalog.cs</c> and <c>VciProbeEngineeringObjectResolver.cs</c>
    /// call into <c>Siemens.Engineering.*</c> types, so — exactly like
    /// <c>VciReadContractProbeService.cs</c> above — they cannot be linked into this net8 test
    /// project or exercised behaviorally here. These tests read the worker source files as text and
    /// assert the exact structural facts Task 4 requires.
    /// </para>
    /// </summary>
    [Fact]
    public void CatalogAndResolver_UseObjectIdentifierProviderGetIdentifierAndFind()
    {
        var catalogSource = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "VciProbeEngineeringObjectCatalog.cs"));
        var resolverSource = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "VciProbeEngineeringObjectResolver.cs"));

        Assert.Contains("GetService<ObjectIdentifierProvider>()", catalogSource, StringComparison.Ordinal);
        Assert.Contains(".GetIdentifier(", catalogSource, StringComparison.Ordinal);

        Assert.Contains("GetService<ObjectIdentifierProvider>()", resolverSource, StringComparison.Ordinal);
        Assert.Contains(".Find(", resolverSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_DiscoversExactlyTheSixBoundedCandidateFamilies()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "VciProbeEngineeringObjectCatalog.cs"));

        Assert.Contains("\"project\"", source, StringComparison.Ordinal);
        Assert.Contains("\"device\"", source, StringComparison.Ordinal);
        Assert.Contains("\"device_item\"", source, StringComparison.Ordinal);
        Assert.Contains("\"plc_block\"", source, StringComparison.Ordinal);
        Assert.Contains("\"plc_tag_table\"", source, StringComparison.Ordinal);
        Assert.Contains("\"plc_type\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_ReusesPlcSoftwareLocatorForPlcSoftwareDiscovery()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "VciProbeEngineeringObjectCatalog.cs"));

        Assert.Contains("PlcSoftwareLocator.FindInDevice(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingTraversalReaders_ArePublicDtoUnchangedByTaskFour()
    {
        // ProjectTreeWalker and NetworkObjectIndexReader are the "established recursive traversal
        // patterns" Task 4 must reuse, not modify. Their public node/summary DTOs are asserted here
        // by name so a later edit that renames or removes a member breaks this test rather than
        // silently drifting the public shape those readers already ship.
        var projectTreeWalkerSource = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "ProjectTreeWalker.cs"));
        var networkObjectIndexReaderSource = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "NetworkObjectIndexReader.cs"));

        Assert.Contains("public class ProjectTreeWalker", projectTreeWalkerSource, StringComparison.Ordinal);
        Assert.Contains("public List<ProjectTreeNode> Walk(Project project)", projectTreeWalkerSource, StringComparison.Ordinal);

        Assert.Contains("public static class NetworkObjectIndexReader", networkObjectIndexReaderSource, StringComparison.Ordinal);
        Assert.Contains(
            "public static IReadOnlyList<NetworkObjectSummaryInfo> Read(",
            networkObjectIndexReaderSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_SelectsOneRepresentativePerDistinctRuntimeTypeBeforeFillingRemainingBudget()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "VciProbeEngineeringObjectCatalog.cs"));

        var seenTypesIndex = source.IndexOf("seenTypes", StringComparison.Ordinal);
        Assert.True(seenTypesIndex >= 0, "Catalog does not track distinct runtime types seen during selection.");

        Assert.Contains("RuntimeTypeName", source, StringComparison.Ordinal);
        Assert.Contains(nameof(VciProbeRequestInfo.MaxEngineeringObjects), source, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolver_VerifiesRuntimeTypeAndFingerprintBeforeReturningAResolvedCandidate()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "VciProbeEngineeringObjectResolver.cs"));

        // The resolver re-runs catalog discovery (which itself calls VciProbeSelectorFingerprint
        // .Compute for every candidate) and compares the fresh, re-resolved candidate's fingerprint
        // against the selector's stored one, rather than recomputing the hash a second time here.
        Assert.Contains("VciProbeEngineeringObjectCatalog.Enumerate(", source, StringComparison.Ordinal);
        Assert.Contains("candidate.Fingerprint", source, StringComparison.Ordinal);
        Assert.Contains("selector.Fingerprint", source, StringComparison.Ordinal);
        Assert.Contains("selector_stale_or_ambiguous", source, StringComparison.Ordinal);

        // Resolve-by-identifier must be attempted before the structural-path fallback.
        var stableIdIndex = source.IndexOf("StableIdentifier", StringComparison.Ordinal);
        var structuralMatchIndex = source.IndexOf("FindStructuralMatch", StringComparison.Ordinal);
        Assert.True(stableIdIndex >= 0, "Resolver does not reference StableIdentifier.");
        Assert.True(structuralMatchIndex >= 0, "Resolver does not fall back to a structural-path match.");
        Assert.True(
            stableIdIndex < structuralMatchIndex,
            "Resolver must attempt the stable-identifier path before the structural-path fallback.");
    }

    [Fact]
    public void Resolver_RejectsSelectorsWithoutANonblankFingerprintBeforeResolving()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "VciProbeEngineeringObjectResolver.cs"));

        var missingFingerprintCheck = source.IndexOf(
            "string.IsNullOrWhiteSpace(selector.Fingerprint)",
            StringComparison.Ordinal);
        var resolvedReturn = source.IndexOf(
            "VciProbeEngineeringObjectResolution.Resolved(candidate)",
            StringComparison.Ordinal);

        Assert.True(
            missingFingerprintCheck >= 0,
            "Resolver must reject a missing or whitespace-only fingerprint rather than structurally resolving it.");
        Assert.Contains("SelectorStaleOrAmbiguous", source, StringComparison.Ordinal);
        Assert.True(
            missingFingerprintCheck < resolvedReturn,
            "Resolver must reject a missing fingerprint before it can return a resolved candidate.");
    }

    [Fact]
    public void CatalogAndResolver_RecordOmissionsInsteadOfUnboundedTraversalOrSilentDrops()
    {
        var catalogSource = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "VciProbeEngineeringObjectCatalog.cs"));

        Assert.Contains("VciProbeOmissionInfo", catalogSource, StringComparison.Ordinal);
        Assert.Contains(nameof(VciProbeRequestInfo.MaxCollectionItems), catalogSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_DoesNotCacheSiemensObjectProxiesAcrossInvocations()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "VciProbeEngineeringObjectCatalog.cs"));

        // No static mutable field may retain a discovered candidate/engineering object between
        // Enumerate(...) calls (worker requests never share Siemens object proxies). Matches a
        // field declaration specifically (name followed by '=' or ';') so it does not false-positive
        // on a method whose return type happens to be the same generic list (name followed by '(').
        var staticFieldPattern = new System.Text.RegularExpressions.Regex(
            @"static\s+(readonly\s+)?List<VciProbeEngineeringObjectCandidate>\s*\??\s*\w+\s*[=;]");
        Assert.False(
            staticFieldPattern.IsMatch(source),
            "Catalog must not declare a static field caching discovered candidates across worker requests.");
    }

    [Fact]
    public void SnapshotReader_UsesTheExactReadOnlyVciPositiveMatrix()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "VciProbeSnapshotReader.cs"));

        // This protects the Task 5 read boundary. Removing one of these members would silently
        // reduce the observed VCI surface rather than producing evidence for the live gate.
        foreach (var member in new[]
                 {
                     "GetService<VersionControlInterface>",
                     "WorkspaceGroup",
                     "Groups",
                     "Workspaces",
                     "MappedObjects",
                     "GetSupportedFileFormats",
                     "Status",
                     "GetStatus",
                     "GetChildStatus",
                 })
        {
            Assert.Contains(member, source, StringComparison.Ordinal);
        }

        foreach (var workspaceProperty in new[]
                 {
                     "Name",
                     "RootPath",
                     "Comment",
                     "WorkspaceLanguage",
                     "GlobalLibraryPath",
                     "DeleteUnusedTypeVersionFromLibrary",
                 })
        {
            Assert.Contains(workspaceProperty, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SnapshotReader_PreservesEvidenceAndBoundsEveryTraversal()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "VciProbeSnapshotReader.cs"));

        Assert.Contains("MaxGroupDepth", source, StringComparison.Ordinal);
        Assert.Contains("MaxGroups", source, StringComparison.Ordinal);
        Assert.Contains("MaxWorkspaces", source, StringComparison.Ordinal);
        Assert.Contains("MaxMappings", source, StringComparison.Ordinal);
        Assert.Contains("SameNameOrdinal", source, StringComparison.Ordinal);
        Assert.Contains("EnumerationIndex", source, StringComparison.Ordinal);
        Assert.Contains("ParentCanonicalKey", source, StringComparison.Ordinal);
        Assert.Contains("VciProbeOmissionInfo", source, StringComparison.Ordinal);
        Assert.Contains("VciProbeMemberObservationInfo", source, StringComparison.Ordinal);
        Assert.Contains("VciProbeObservationRunner.Run(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".OrderBy(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".OrderByDescending(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotReader_InvokesSupportedFormatsOnceAndDoesNotContainVciWrites()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "VciProbeSnapshotReader.cs"));

        Assert.Equal(1, CountOccurrences(source, ".GetSupportedFileFormats("));
        Assert.Contains("no_workspace_candidate_pair", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Create(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Delete(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".ExportObject(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".ConnectObject(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Synchronize(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotReader_PreservesCompleteMappingSelectorsAndSeparateStatusEvidence()
    {
        var mappingBody = ReadSnapshotReaderMethod("private static void WalkMappings");

        AssertMethodMatches(mappingBody,
            @"var\s+objectOutcome\s*=\s*Observe\(result,\s*project,\s*""EngineeringObject"",\s*\(\)\s*=>\s*mapping\.EngineeringObject\)");
        AssertMethodMatches(mappingBody,
            @"EngineeringObject\s*=\s*FindEngineeringObjectSelector\(project,\s*request,\s*objectOutcome\.ReturnValue\)");
        AssertMethodMatches(mappingBody,
            @"Workspace\s*=\s*new\s+VciWorkspaceSelectorInfo\s*\{\s*GroupPath\s*=\s*new\s+List<VciGroupPathSegmentInfo>\(groupPath\),\s*WorkspaceName\s*=\s*workspaceSnapshot\.Name,\s*CanonicalRootPath\s*=\s*workspaceSnapshot\.RootPath\s*\}");
        AssertMethodMatches(mappingBody,
            @"var\s+status\s*=\s*Observe\(result,\s*project,\s*""Status"",\s*\(\)\s*=>\s*mapping\.Status\)");
        AssertMethodMatches(mappingBody,
            @"var\s+getStatus\s*=\s*Observe\(result,\s*project,\s*""GetStatus"",\s*\(\)\s*=>\s*mapping\.GetStatus\(\)\)");
        AssertMethodMatches(mappingBody,
            @"var\s+childStatus\s*=\s*Observe\(result,\s*project,\s*""GetChildStatus"",\s*\(\)\s*=>\s*mapping\.GetChildStatus\(\)\)");
        AssertMethodMatches(mappingBody, @"StatusProperty\s*=\s*Render\(status\.ReturnValue,");
        AssertMethodMatches(mappingBody, @"GetStatus\s*=\s*Render\(getStatus\.ReturnValue,");
        AssertMethodMatches(mappingBody, @"ChildStatus\s*=\s*Render\(childStatus\.ReturnValue,");
    }

    [Fact]
    public void SnapshotReader_PreservesTypedFormatItemsAndCompleteWorkspaceIdentity()
    {
        var formatsBody = ReadSnapshotReaderMethod("internal static VciProbeSnapshotReadResult ReadSupportedFormats");
        var resolveWorkspaceBody = ReadSnapshotReaderMethod("private static object? ResolveWorkspace");

        AssertMethodMatches(formatsBody,
            @"CandidateCollectionRuntimeType\s*=\s*formats\.GetType\(\)\.FullName\s*\?\?\s*formats\.GetType\(\)\.Name");
        AssertMethodMatches(formatsBody, @"foreach\s*\(var\s+format\s+in\s+formats\)");
        AssertMethodMatches(formatsBody, @"var\s+normalized\s*=\s*VciProbeValueNormalizer\.Normalize\(format,");
        AssertMethodMatches(formatsBody, @"Description\s*=\s*Render\(normalized\)");
        AssertMethodMatches(formatsBody, @"RuntimeTypeName\s*=\s*normalized\.RuntimeType");
        AssertMethodMatches(formatsBody, @"IsNull\s*=\s*normalized\.Kind\s*==\s*""null""");

        AssertMethodMatches(resolveWorkspaceBody,
            @"index\s*==\s*segment\.Index\s*&&\s*string\.Equals\(name,\s*segment\.Name,\s*StringComparison\.Ordinal\)\s*&&\s*sameNameOrdinal\s*==\s*segment\.SameNameOrdinal");
        AssertMethodMatches(resolveWorkspaceBody,
            @"string\.Equals\(candidate\.Name\s+as\s+string,\s*selector\.WorkspaceName,\s*StringComparison\.Ordinal\)\s*&&\s*string\.Equals\(canonicalRootPath,\s*selector\.CanonicalRootPath,\s*StringComparison\.Ordinal\)");
        AssertMethodMatches(resolveWorkspaceBody, @"return\s+workspaceMatchCount\s*==\s*1\s*\?\s*workspaceMatch\s*:\s*null");
    }

    [Fact]
    public void SnapshotReader_PreservesTypedCanonicalizationFailuresAndPathQualifiedOmissions()
    {
        var observeBody = ReadSnapshotReaderMethod("private static VciProbeObservationOutcomeInfo Observe");
        var formatsBody = ReadSnapshotReaderMethod("internal static VciProbeSnapshotReadResult ReadSupportedFormats");
        var groupBody = ReadSnapshotReaderMethod("private static void WalkGroup");
        var workspaceBody = ReadSnapshotReaderMethod("private static void WalkWorkspaces");
        var mappingBody = ReadSnapshotReaderMethod("private static void WalkMappings");

        AssertMethodMatches(observeBody,
            @"var\s+failure\s*=\s*outcome\.Exception\s*\?\?\s*normalized\.PathCanonicalizationException");
        AssertMethodMatches(observeBody, @"Exception\s*=\s*ToExceptionInfo\(failure\)");
        AssertMethodMatches(formatsBody, @"request\.MaxCollectionItems,\s*index,\s*""formats""\)");
        Assert.Equal(2, CountRegexMatches(groupBody, @"request\.Max(?:GroupDepth|Groups),\s*[^\n]+,\s*FormatGroupPath\(parentPath\)\)"));
        AssertMethodMatches(workspaceBody, @"request\.MaxWorkspaces,\s*[^\n]+,\s*FormatGroupPath\(groupPath\)\)");
        AssertMethodMatches(mappingBody, @"request\.MaxMappings,\s*[^\n]+,\s*FormatGroupPath\(groupPath\)\)");
    }

    private static string ReadProbeServiceSource()
        => File.ReadAllText(FindRepositoryFile(
                "TiaMcpServer.OpennessWorker", "Openness", "VciReadContractProbeService.cs"))
            .Replace("\r\n", "\n");

    private static string ReadMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method '{signature}' was not found.");

        var nextMethod = source.IndexOf("\n    private static ", start + signature.Length, StringComparison.Ordinal);
        if (nextMethod < 0)
        {
            nextMethod = source.LastIndexOf("\n}", StringComparison.Ordinal);
        }

        Assert.True(nextMethod > start, $"Could not determine the end of method '{signature}'.");
        return source[start..nextMethod];
    }

    private static string ReadSnapshotReaderMethod(string signature)
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "VciProbeSnapshotReader.cs"))
            .Replace("\r\n", "\n");
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Snapshot reader method '{signature}' was not found.");

        var nextMethod = source.IndexOf("\n    private static ", start + signature.Length, StringComparison.Ordinal);
        if (nextMethod < 0)
        {
            nextMethod = source.IndexOf("\n}\n\ninternal sealed class", start + signature.Length, StringComparison.Ordinal);
        }

        Assert.True(nextMethod > start, $"Could not determine the end of snapshot reader method '{signature}'.");
        return source[start..nextMethod];
    }

    private static void AssertMethodMatches(string methodBody, string pattern)
        => Assert.Matches(new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.Singleline), methodBody);

    private static int CountRegexMatches(string value, string pattern)
        => System.Text.RegularExpressions.Regex.Matches(value, pattern, System.Text.RegularExpressions.RegexOptions.Singleline).Count;

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string FindRepositoryFile(params string[] segments)
        => Path.Combine(new[] { FindRepositoryRoot() }.Concat(segments).ToArray());

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TiaMcpServer.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
