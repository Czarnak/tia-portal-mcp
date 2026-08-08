using System.Reflection;
using ModelContextProtocol.Server;
using TiaMcpServer.Batch;
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
