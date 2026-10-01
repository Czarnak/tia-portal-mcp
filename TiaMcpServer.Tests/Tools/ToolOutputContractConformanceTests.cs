using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Tools;

/// <summary>
/// Phase 0 guard of docs/roadmap/json-contract.md, observed through the real MCP protocol on the
/// production read-write surface.
///
/// <para>
/// Every registered tool is either on the structured JSON contract - it advertises an output
/// schema - or is listed in <see cref="LegacyTextContractTools"/> with the reason it has not
/// migrated. The register only shrinks: migrating a tool without removing it here fails, and so does
/// registering a tool that is neither structured nor listed. Every structured tool also carries a
/// success probe and a rejection probe in <see cref="StructuredToolProbes"/>, and each probe must
/// return one canonical document, identical in both representations, with no JSON inside a string.
/// </para>
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class ToolOutputContractConformanceTests
{
    private const string BatchRedesign =
        "Excluded from the JSON contract roadmap: the batch tools are redesigned separately.";

    /// <summary>Tools still on a legacy text contract, each with the reason it has not migrated.</summary>
    private static readonly IReadOnlyDictionary<string, string> LegacyTextContractTools =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["execute_read_batch"] = BatchRedesign,
            ["preview_write_batch"] = BatchRedesign,
            ["apply_write_batch"] = BatchRedesign,
        };

    private static readonly IReadOnlyDictionary<string, ToolProbe> StructuredToolProbes = new[]
    {
        new ToolProbe("open_project", "applied", false, null, new()),
        new ToolProbe("open_project", "rejected", true, null, new()),
        new ToolProbe("create_project", "applied", false, null, new()),
        new ToolProbe("create_project", "rejected", true, null, new()),
        new ToolProbe("save_project", "applied", false, "guarded-lifecycle", new()),
        new ToolProbe("save_project", "rejected", true, null, new()),
        new ToolProbe("save_project_as", "applied", false, "guarded-lifecycle", new()),
        new ToolProbe("save_project_as", "rejected", true, null, new()),
        new ToolProbe("archive_project", "applied", false, "guarded-lifecycle", new()),
        new ToolProbe("archive_project", "rejected", true, null, new()),
        new ToolProbe("close_project", "applied", false, "guarded-lifecycle", new()),
        new ToolProbe("close_project", "rejected", true, null, new()),
        new ToolProbe("compile_check", "succeeded", false, "compile-passed",
            new Dictionary<string, object?>()),
        new ToolProbe("compile_check", "rejected", true, null,
            new Dictionary<string, object?>()),
        new ToolProbe("compile_check", "compilerErrors", false, "compile-errors",
            new Dictionary<string, object?>()),
        new ToolProbe("compile_check", "malformed", false, "compile-malformed",
            new Dictionary<string, object?>()),
        new ToolProbe("compile_check", "omitted", false, "compile-oversized",
            new Dictionary<string, object?>()),
        new ToolProbe("get_project_status", "succeeded", false, null,
            new Dictionary<string, object?> { ["projectPath"] = "status-no-project" }),
        new ToolProbe("get_project_status", "rejected", true, "network-roundtrip",
            new Dictionary<string, object?> { ["projectPath"] = "different-project" }),
        new ToolProbe("get_project_status", "malformed", false, null,
            new Dictionary<string, object?> { ["projectPath"] = "status-malformed" }),
        new ToolProbe("get_project_status", "omitted", false, null,
            new Dictionary<string, object?> { ["projectPath"] = "status-oversized" }),
        new ToolProbe(
            "network_read",
            "rejected",
            ExpectIsError: true,
            StartupProjectPath: null,
            new Dictionary<string, object?> { ["operations"] = Array.Empty<object>() }),
        new ToolProbe(
            "network_read",
            "succeeded",
            ExpectIsError: false,
            StartupProjectPath: null,
            new Dictionary<string, object?>
            {
                ["operations"] = new[]
                {
                    new
                    {
                        operationId = "hardware",
                        operation = "read_hardware_config",
                        projectPath = "network-roundtrip",
                    },
                },
            }),
        new ToolProbe(
            "network_write",
            "rejected",
            ExpectIsError: true,
            StartupProjectPath: null,
            new Dictionary<string, object?> { ["operations"] = Array.Empty<object>() }),
        new ToolProbe(
            "network_write",
            "previewed",
            ExpectIsError: false,
            StartupProjectPath: "network-roundtrip",
            new Dictionary<string, object?>
            {
                ["operations"] = new object[]
                {
                    new
                    {
                        operationId = "add",
                        operation = "add_network_device",
                        projectPath = "network-roundtrip",
                        typeIdentifier = "OrderNumber:TEST",
                        deviceName = "PLC_1",
                    },
                    new
                    {
                        operationId = "configure",
                        operation = "configure_network_device",
                        projectPath = "network-roundtrip",
                        target = new { deviceName = "PLC_1", nodeId = "node-1" },
                        changes = new { ipAddress = "192.168.0.10" },
                    },
                },
            }),
        new ToolProbe(
            "browse_project_tree",
            "rejected",
            ExpectIsError: true,
            StartupProjectPath: null,
            new Dictionary<string, object?> { ["pageSize"] = 0 }),
        new ToolProbe(
            "browse_project_tree",
            "succeeded",
            ExpectIsError: false,
            StartupProjectPath: null,
            new Dictionary<string, object?>
            {
                ["projectPath"] = "project-tree-v3-small",
                ["pageSize"] = 4,
            }),
    }.ToDictionary(probe => probe.Name, StringComparer.Ordinal);

    public static TheoryData<string> ProbeNames
    {
        get
        {
            var names = new TheoryData<string>();
            foreach (var name in StructuredToolProbes.Keys.Order(StringComparer.Ordinal))
            {
                names.Add(name);
            }

            return names;
        }
    }

    [Fact]
    public async Task EveryRegisteredToolIsStructuredOrListedAsLegacy()
    {
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(
            McpAccessMode.Full);
        var tools = await harness.Client.ListToolsAsync();
        var registered = tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
        var structured = tools
            .Where(tool => tool.ProtocolTool.OutputSchema is not null)
            .Select(tool => tool.Name)
            .ToHashSet(StringComparer.Ordinal);

        var unlisted = registered
            .Where(name => !structured.Contains(name) && !LegacyTextContractTools.ContainsKey(name))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(
            unlisted.Length == 0,
            "These tools advertise no output schema and are not listed in LegacyTextContractTools: "
                + string.Join(", ", unlisted));

        var migrated = LegacyTextContractTools.Keys
            .Where(structured.Contains)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(
            migrated.Length == 0,
            "These tools now advertise an output schema; remove them from LegacyTextContractTools "
                + "and give them probes: " + string.Join(", ", migrated));

        var stale = LegacyTextContractTools.Keys
            .Where(name => !registered.Contains(name))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(
            stale.Length == 0,
            "LegacyTextContractTools lists tools that are not registered: " + string.Join(", ", stale));

        var withoutBothProbes = structured
            .Where(name => !HasProbe(name, expectIsError: true) || !HasProbe(name, expectIsError: false))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(
            withoutBothProbes.Length == 0,
            "These structured tools need both a success and a rejection probe in StructuredToolProbes: "
                + string.Join(", ", withoutBothProbes));

        var probedButNotStructured = StructuredToolProbes.Values
            .Select(probe => probe.Tool)
            .Where(name => !structured.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(
            probedButNotStructured.Length == 0,
            "StructuredToolProbes probes tools that advertise no output schema: "
                + string.Join(", ", probedButNotStructured));
    }

    [Theory]
    [MemberData(nameof(ProbeNames))]
    public async Task StructuredToolReturnsOneCanonicalDocumentWithoutNestedJson(string probeName)
    {
        var probe = StructuredToolProbes[probeName];
        using var audit = new TempAuditDirectory();
        using var fixture = new LifecycleProtocolFixture();
        var lifecycle = probe.Tool is "open_project" or "create_project" or "save_project"
            or "save_project_as" or "archive_project" or "close_project";
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(
            McpAccessMode.Full,
            audit.Path,
            probe.StartupProjectPath == "guarded-lifecycle" ? fixture.SourcePath : probe.StartupProjectPath);

        var arguments = lifecycle ? fixture.Arguments(probe.Tool, probe.ExpectIsError) : probe.Arguments;
        var result = await harness.Client.CallToolAsync(probe.Tool, arguments);

        var violations = StructuredContractInspector.FindViolations(result);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
        Assert.True(
            probe.ExpectIsError == (result.IsError == true),
            $"Expected isError={probe.ExpectIsError}. Response: {TextOf(result)}");
        if (lifecycle && !probe.ExpectIsError)
        {
            using var document = System.Text.Json.JsonDocument.Parse(TextOf(result));
            Assert.Equal("applied", document.RootElement.GetProperty("phase").GetString());
            Assert.True(document.RootElement.GetProperty("success").GetBoolean(), TextOf(result));
            Assert.Equal("succeeded", document.RootElement.GetProperty("result").GetProperty("status").GetString());
            Assert.Equal("succeeded", document.RootElement.GetProperty("verification").GetProperty("status").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Null, document.RootElement.GetProperty("error").ValueKind);
        }
    }

    private static bool HasProbe(string tool, bool expectIsError)
        => StructuredToolProbes.Values.Any(probe =>
            string.Equals(probe.Tool, tool, StringComparison.Ordinal) && probe.ExpectIsError == expectIsError);

    private static string TextOf(CallToolResult result)
        => string.Join(
            Environment.NewLine,
            result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private sealed record ToolProbe(
        string Tool,
        string Case,
        bool ExpectIsError,
        string? StartupProjectPath,
        Dictionary<string, object?> Arguments)
    {
        public string Name => $"{Tool}/{Case}";
    }
}
