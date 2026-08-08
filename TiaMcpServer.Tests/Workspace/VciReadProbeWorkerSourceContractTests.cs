using System.Reflection;
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
    public void VciReadContractProbeService_ExistsAsACompileOnlyShell()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "VciReadContractProbeService.cs"));

        Assert.Contains("static class VciReadContractProbeService", source, StringComparison.Ordinal);
        Assert.Contains("VciProbeCaseResultInfo Execute(", source, StringComparison.Ordinal);
        Assert.Contains("throw new NotImplementedException(", source, StringComparison.Ordinal);
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
