using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Tests.TestSupport;
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
    /// <summary>Tools still on a legacy text contract, each with the reason it has not migrated.</summary>
    private static readonly IReadOnlyDictionary<string, string> LegacyTextContractTools =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static readonly object XrefTarget = new
    {
        path = new[]
        {
            new { nodeType = "Device", name = "PLC_1_Device" },
            new { nodeType = "PlcSoftware", name = "PLC_1" },
        },
    };

    private static readonly IReadOnlyDictionary<string, ToolProbe> StructuredToolProbes = new[]
    {
        new ToolProbe("bind_project", "succeeded", false, null, new()),
        new ToolProbe("bind_project", "rejected", true, null,
            new Dictionary<string, object?> { ["projectPath"] = "relative.ap21" }),
        new ToolProbe("bind_project", "ambiguous", false, null, new()),
        new ToolProbe("bind_project", "list_portals", false, null, new() { ["action"] = "list_portals" }),
        new ToolProbe("bind_project", "list_server_connections", false, null, new() { ["action"] = "list_server_connections" }),
        new ToolProbe("bind_project", "list_server_groups", false, null, new() { ["action"] = "list_server_groups", ["serverAlias"] = "Fixture" }),
        new ToolProbe("bind_project", "list_server_projects", false, null, new() { ["action"] = "list_server_projects", ["serverAlias"] = "Fixture",
            ["group"] = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"isRoot":true,"name":null}""") }),
        new ToolProbe("bind_project", "list_local_sessions", false, null, new() { ["action"] = "list_local_sessions", ["serverAlias"] = "Fixture",
            ["group"] = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"isRoot":true,"name":null}"""), ["serverProjectName"] = "Project A" }),
        new ToolProbe("bind_project", "get_lock_state", false, null, new() { ["action"] = "get_lock_state", ["serverAlias"] = "Fixture",
            ["group"] = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"isRoot":true,"name":null}"""), ["serverProjectName"] = "Project A" }),
        new ToolProbe("bind_project", "inspection_rejected", true, null, new() { ["action"] = "list_portals", ["forceRebind"] = false }),
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
        new ToolProbe("get_project_status", "succeeded", false, null, new()),
        new ToolProbe("get_project_status", "rejected", true, null,
            new Dictionary<string, object?> { ["unexpectedArgument"] = true }),
        new ToolProbe("get_project_status", "malformed", false, null, new()),
        new ToolProbe("get_project_status", "omitted", false, null, new()),
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
            StartupProjectPath: "network-roundtrip",
            new Dictionary<string, object?>
            {
                ["operations"] = new[]
                {
                    new
                    {
                        operationId = "hardware",
                        operation = "read_hardware_config",
                    },
                },
            }),
        new ToolProbe("vci_read", "rejected", true, null,
            new Dictionary<string, object?> { ["operations"] = Array.Empty<object>() }),
        new ToolProbe("vci_read", "succeeded", false, "vci-read-roundtrip",
            new Dictionary<string, object?> { ["operations"] = new[] { new { operationId = "w", operation = "list_workspaces" } } }),
        new ToolProbe(
            "plc_read",
            "rejected",
            ExpectIsError: true,
            StartupProjectPath: null,
            new Dictionary<string, object?> { ["operations"] = Array.Empty<object>() }),
        new ToolProbe(
            "plc_read",
            "succeeded",
            ExpectIsError: false,
            StartupProjectPath: "plc-read-roundtrip",
            new Dictionary<string, object?>
            {
                ["operations"] = new[]
                {
                    new
                    {
                        operationId = "block",
                        operation = "get_block_content",
                        blockPath = "PLC_1/Main",
                    },
                },
            }),
        new ToolProbe(
            "hmi_read",
            "rejected",
            ExpectIsError: true,
            StartupProjectPath: null,
            new Dictionary<string, object?> { ["operations"] = Array.Empty<object>() }),
        new ToolProbe(
            "hmi_read",
            "succeeded",
            ExpectIsError: false,
            StartupProjectPath: "hmi-read-roundtrip",
            new Dictionary<string, object?>
            {
                ["operations"] = new[]
                {
                    new
                    {
                        operationId = "devices",
                        operation = "list_hmi_devices",
                    },
                },
            }),
        new ToolProbe(
            "read_cross_references",
            "rejected",
            ExpectIsError: true,
            StartupProjectPath: null,
            new Dictionary<string, object?> { ["target"] = new { path = Array.Empty<object>() } }),
        new ToolProbe(
            "read_cross_references",
            "succeeded",
            ExpectIsError: false,
            StartupProjectPath: "xref-roundtrip",
            new Dictionary<string, object?>
            {
                ["target"] = XrefTarget,
            }),
        new ToolProbe(
            "read_cross_references",
            "omittedSources",
            ExpectIsError: false,
            StartupProjectPath: "xref-oversized",
            new Dictionary<string, object?>
            {
                ["target"] = XrefTarget,
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
                ["dryRun"] = true,
                ["operations"] = new object[]
                {
                    new
                    {
                        operationId = "add",
                        operation = "add_network_device",
                        typeIdentifier = "OrderNumber:TEST",
                        deviceName = "PLC_1",
                    },
                    new
                    {
                        operationId = "configure",
                        operation = "configure_network_device",
                        target = new { deviceName = "PLC_1", nodeId = "node-1" },
                        changes = new { ipAddress = "192.168.0.10" },
                    },
                },
            }),
        new ToolProbe("vci_write", "rejected", true, null,
            new Dictionary<string, object?> { ["operations"] = Array.Empty<object>() }),
        new ToolProbe("vci_write", "previewed", false, "vci-write-roundtrip",
            new Dictionary<string, object?> { ["dryRun"] = true, ["operations"] = new object[]
            { new { operationId = "v", operation = "export", workspaceName = "Fixture", scope = new[] { new { nodeType = "Device", name = "Fixture" } } } } }),
        new ToolProbe("hmi_write", "rejected", true, null,
            new Dictionary<string, object?> { ["operations"] = Array.Empty<object>() }),
        new ToolProbe("hmi_write", "previewed", false, "hmi-write-roundtrip",
            new Dictionary<string, object?> { ["dryRun"] = true, ["operations"] = new object[]
            {
                new { operationId = "table", operation = "create_tag_table", tableName = "T" },
                new { operationId = "tag", operation = "create_tag", tableName = "T", tagName = "Tag", dataType = "Bool" },
            } }),
        new ToolProbe(
            "plc_write",
            "rejected",
            ExpectIsError: true,
            StartupProjectPath: null,
            new Dictionary<string, object?> { ["operations"] = Array.Empty<object>() }),
        new ToolProbe(
            "plc_write",
            "previewed",
            ExpectIsError: false,
            StartupProjectPath: "plc-write-roundtrip",
            new Dictionary<string, object?>
            {
                ["dryRun"] = true,
                ["operations"] = new object[]
                {
                    new
                    {
                        operationId = "table",
                        operation = "create_tag_table",
                        plcName = "PLC_2",
                        tableName = "Valves",
                    },
                    new
                    {
                        operationId = "tag",
                        operation = "create_tag",
                        plcName = "PLC_2",
                        tableName = "Valves",
                        name = "Valve1",
                        dataType = "Bool",
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
            StartupProjectPath: "project-tree-v3-small",
            new Dictionary<string, object?>
            {
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
        var sourcePath = probe.Name switch
        {
            "bind_project/succeeded" => "C:/Projects/Binding.ap21",
            "bind_project/rejected" => null,
            "bind_project/ambiguous" => "C:/Projects/Binding.ap21",
            "get_project_status/succeeded" => "status-no-project",
            "get_project_status/malformed" => "status-malformed",
            "get_project_status/omitted" => "status-oversized",
            "network_read/succeeded" => "network-roundtrip",
            "plc_read/succeeded" => "plc-read-roundtrip",
            "vci_read/succeeded" => "vci-read-roundtrip",
            "hmi_read/succeeded" => "hmi-read-roundtrip",
            "plc_write/previewed" => "plc-write-roundtrip",
            "read_cross_references/succeeded" => "xref-roundtrip",
            "read_cross_references/omittedSources" => "xref-oversized",
            "browse_project_tree/succeeded" => "project-tree-v3-small",
            _ => probe.StartupProjectPath == "guarded-lifecycle"
                ? fixture.SourcePath : probe.StartupProjectPath
        };
        using var portals = probe.Name switch
        {
            "bind_project/succeeded" => new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, sourcePath)),
            "bind_project/rejected" => new FakeWorkerPortals(),
            "bind_project/ambiguous" => new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, sourcePath), new FakeWorkerPortals.Entry(43, "C:/Projects/Other.ap21")),
            _ => null
        };
        using var uiOpen = probe.Name is "get_project_status/succeeded" or "get_project_status/malformed" or
            "get_project_status/omitted" or "network_read/succeeded" or "plc_read/succeeded" or "vci_read/succeeded" or "hmi_read/succeeded" or
            "read_cross_references/succeeded" or "read_cross_references/omittedSources" or
            "browse_project_tree/succeeded"
            ? FakeWorkerUiOpenProject.ForWorkerRelativePath(sourcePath!)
            : new FakeWorkerUiOpenProject(sourcePath);
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
        if (probe.Tool == "bind_project")
        {
            var document = result.StructuredContent!.Value;
            if (probe.ExpectIsError)
            {
                Assert.Equal(System.Text.Json.JsonValueKind.Null, document.GetProperty("result").ValueKind);
                Assert.Equal("validation_error", document.GetProperty("error").GetProperty("category").GetString());
            }
            else
            {
                Assert.Equal(probe.Case != "ambiguous", document.GetProperty("success").GetBoolean());
                Assert.Equal(probe.Case != "ambiguous" ? "succeeded" : "failed", document.GetProperty("result").GetProperty("status").GetString());
                if (probe.Case == "ambiguous")
                    Assert.Equal(2, document.GetProperty("result").GetProperty("value").GetProperty("portals").GetArrayLength());
            }
        }
        if (probe.Tool == "network_write")
            Assert.Equal("2.0", result.StructuredContent!.Value.GetProperty("contractVersion").GetString());
        if (probe.Tool == "network_read")
        {
            var document = result.StructuredContent!.Value;
            Assert.Equal("1.0", document.GetProperty("contractVersion").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Array, document.GetProperty("warnings").ValueKind);
            if (probe.ExpectIsError)
            {
                Assert.Equal(System.Text.Json.JsonValueKind.Null, document.GetProperty("batch").ValueKind);
            }
        }
        if (probe.Tool is "plc_read" or "vci_read")
        {
            var document = result.StructuredContent!.Value;
            Assert.Equal("1.0", document.GetProperty("contractVersion").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Array, document.GetProperty("warnings").ValueKind);
            Assert.Equal(
                probe.ExpectIsError ? System.Text.Json.JsonValueKind.Null : System.Text.Json.JsonValueKind.Object,
                document.GetProperty("batch").ValueKind);
        }
        if (probe.Tool == "hmi_read")
        {
            var document = result.StructuredContent!.Value;
            Assert.Equal("hmi_read", document.GetProperty("tool").GetString());
            Assert.Equal("1.1", document.GetProperty("contractVersion").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Array, document.GetProperty("warnings").ValueKind);
            Assert.Equal(
                probe.ExpectIsError ? System.Text.Json.JsonValueKind.Null : System.Text.Json.JsonValueKind.Object,
                document.GetProperty("batch").ValueKind);
            if (!probe.ExpectIsError)
            {
                var item = document.GetProperty("batch").GetProperty("operations")[0];
                Assert.Equal("succeeded", item.GetProperty("status").GetString());
                Assert.Equal("HMI_1_RT", item.GetProperty("result").GetProperty("devices")[0].GetProperty("softwareName").GetString());
            }
        }
        if (probe.Tool == "plc_write")
        {
            var document = result.StructuredContent!.Value;
            Assert.Equal("plc_write", document.GetProperty("tool").GetString());
            Assert.Equal("2.0", document.GetProperty("contractVersion").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Array, document.GetProperty("warnings").ValueKind);
            Assert.Equal(System.Text.Json.JsonValueKind.Null, document.GetProperty("batch").ValueKind);
            Assert.Equal(probe.ExpectIsError ? "error" : "preview", document.GetProperty("phase").GetString());
            if (!probe.ExpectIsError)
            {
                Assert.True(document.GetProperty("success").GetBoolean());
                Assert.Equal("table", document.GetProperty("effects")[1].GetProperty("effect").GetProperty("dependsOn").GetString());
            }
        }
        if (probe.Tool == "read_cross_references")
        {
            var document = result.StructuredContent!.Value;
            Assert.Equal("1.0", document.GetProperty("contractVersion").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Array, document.GetProperty("warnings").ValueKind);
            if (probe.ExpectIsError)
            {
                Assert.Equal(System.Text.Json.JsonValueKind.Null, document.GetProperty("result").ValueKind);
            }
            else
            {
                var value = document.GetProperty("result").GetProperty("value");
                var omitted = value.GetProperty("omittedSourceCount").GetInt32();
                if (probe.Case == "omittedSources")
                {
                    Assert.True(omitted > 0);
                    Assert.False(value.GetProperty("isComplete").GetBoolean());
                    Assert.Equal(40, value.GetProperty("totalSourceCount").GetInt32());
                    Assert.Equal(40, omitted + value.GetProperty("sources").GetArrayLength());
                    Assert.True(value.GetRawText().Length <= 60_000);
                }
                else
                {
                    Assert.Equal(0, omitted);
                }
            }
        }
        if (probe.Name == "network_read/succeeded")
        {
            var document = result.StructuredContent!.Value;
            var evidence = document.GetProperty("batch").GetProperty("operations")[0]
                .GetProperty("result").GetProperty("discoveryEvidence");
            Assert.Equal("project", evidence.GetProperty("scope").GetString());
            Assert.True(evidence.GetProperty("complete").GetBoolean());
            Assert.Equal(System.Text.Json.JsonValueKind.Array, evidence.GetProperty("failures").ValueKind);
            Assert.Empty(evidence.GetProperty("failures").EnumerateArray());
            Assert.Equal(1, document.GetProperty("batch").GetProperty("counts")
                .GetProperty("succeeded").GetInt32());
        }
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

    [Fact]
    public async Task EveryRegisteredToolRejectsArgumentShapeErrorsCanonically()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path);
        var before = requests.Methods();

        foreach (var tool in await harness.Client.ListToolsAsync())
        {
            var properties = tool.ProtocolTool.InputSchema.GetProperty("properties");
            var cases = new List<Dictionary<string, object?>> { new() { ["unexpectedArgument"] = true } };
            cases.AddRange(properties.EnumerateObject()
                .Where(property => property.Value.TryGetProperty("type", out var type)
                    && type.ValueKind == System.Text.Json.JsonValueKind.String && type.GetString() == "boolean")
                .Select(property => new Dictionary<string, object?> { [property.Name] = null }));
            if (properties.TryGetProperty("operations", out _))
                cases.Add(new() { ["operations"] = new[] { new Dictionary<string, object?> { ["unexpectedMember"] = 1 } } });

            foreach (var arguments in cases)
                ArgumentRejectionAssert.IsCanonicalValidationError(await harness.Client.CallToolAsync(tool.Name, arguments), tool.Name);
        }

        Assert.Equal(before, requests.Methods());
        Assert.Empty(Network.NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
    }

    [Theory]
    [InlineData("plc_read", "operations")]
    [InlineData("plc_write", "operations")]
    [InlineData("network_read", "operations")]
    [InlineData("network_write", "operations")]
    [InlineData("hmi_read", "operations")]
    [InlineData("hmi_write", "operations")]
    [InlineData("vci_read", "operations")]
    [InlineData("vci_write", "operations")]
    [InlineData("open_project", "projectPath")]
    [InlineData("create_project", "projectDirectory")]
    [InlineData("create_project", "projectName")]
    [InlineData("save_project_as", "targetDirectory")]
    [InlineData("save_project_as", "targetName")]
    [InlineData("archive_project", "archiveDirectory")]
    [InlineData("archive_project", "archiveName")]
    public async Task NonNullableRootNull_IsCanonicalAndUnaudited(string tool, string parameter)
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path);
        var before = requests.Methods();
        var arguments = tool switch
        {
            "open_project" => new Dictionary<string, object?> { ["projectPath"] = Path.Combine(audit.Path, "Project.ap21") },
            "create_project" => new Dictionary<string, object?> { ["projectDirectory"] = audit.Path, ["projectName"] = "Project" },
            "save_project_as" => new Dictionary<string, object?> { ["targetDirectory"] = audit.Path, ["targetName"] = "Copy" },
            "archive_project" => new Dictionary<string, object?> { ["archiveDirectory"] = audit.Path, ["archiveName"] = "Archive" },
            _ => new Dictionary<string, object?> { ["operations"] = Array.Empty<object>() },
        };
        arguments[parameter] = null;

        var result = await harness.Client.CallToolAsync(tool, arguments);

        ArgumentRejectionAssert.IsCanonicalValidationError(result, tool);
        var message = result.StructuredContent!.Value.GetProperty("error").GetProperty("message").GetString()!;
        Assert.Contains(parameter, message);
        Assert.Contains("null", message);
        Assert.Equal(before, requests.Methods());
        Assert.Empty(Network.NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
    }

    [Fact]
    public async Task UnboundContentReadsFailClosedCanonically()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path);
        var calls = new (string Tool, Dictionary<string, object?> Arguments)[]
        {
            ("plc_read", Operations(new { operationId = "block", operation = "get_block_content", blockPath = "PLC_1/Main" })),
            ("network_read", Operations(new { operationId = "hardware", operation = "read_hardware_config" })),
            ("hmi_read", Operations(new { operationId = "devices", operation = "list_hmi_devices" })),
            ("vci_read", Operations(new { operationId = "w", operation = "list_workspaces" })),
            ("browse_project_tree", new Dictionary<string, object?>()),
            ("read_cross_references", new Dictionary<string, object?> { ["target"] = XrefTarget }),
        };

        foreach (var (tool, arguments) in calls)
        {
            var result = await harness.Client.CallToolAsync(tool, arguments);

            var violations = StructuredContractInspector.FindViolations(result);
            Assert.True(violations.Count == 0, tool + ": " + string.Join(Environment.NewLine, violations));
            Assert.True(result.IsError == true, $"{tool}: {TextOf(result)}");
            var document = result.StructuredContent!.Value;
            var error = document.GetProperty(tool == "browse_project_tree" ? "failure" : "error");
            Assert.Equal("binding_conflict", error.GetProperty("category").GetString());
            Assert.Equal(TiaMcpServer.Contracts.Project.ProjectSessionBinding.BindFirstMessage, error.GetProperty("message").GetString());
            if (tool is "plc_read" or "network_read" or "hmi_read" or "vci_read")
                Assert.Equal(System.Text.Json.JsonValueKind.Null, document.GetProperty("batch").ValueKind);
        }

        Assert.DoesNotContain(requests.Methods(), TiaMcpServer.Contracts.Project.ProjectCapabilityCatalog.RequiresBoundProject);
    }

    [Fact]
    public async Task UnboundRead_InvalidItem_IsValidationError()
    {
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full);

        var result = await harness.Client.CallToolAsync(
            "plc_read",
            Operations(new { operationId = "bad", operation = "not_a_plc_operation" }));

        Assert.True(result.IsError == true, TextOf(result));
        Assert.Equal(
            "validation_error",
            result.StructuredContent!.Value.GetProperty("error").GetProperty("category").GetString());
    }

    public static TheoryData<string> RetiredItemPathTools => new()
    {
        "plc_read", "plc_write", "network_read", "network_write",
        "hmi_read", "hmi_write", "vci_read", "vci_write",
    };

    [Theory]
    [MemberData(nameof(RetiredItemPathTools))]
    public async Task RetiredProjectPathIsRejectedCanonically(string tool)
    {
        var (bound, item) = tool switch
        {
            "plc_read" => ("plc-read-roundtrip", (object)new { operationId = "block", operation = "get_block_content", blockPath = "PLC_1/Main", projectPath = "plc-read-roundtrip" }),
            "plc_write" => ("plc-write-roundtrip", new { operationId = "table", operation = "create_tag_table", plcName = "PLC_2", tableName = "Valves", projectPath = "plc-write-roundtrip" }),
            "network_read" => ("network-roundtrip", new { operationId = "hardware", operation = "read_hardware_config", deviceName = "PLC_1", projectPath = "network-roundtrip" }),
            "network_write" => ("network-roundtrip", new { operationId = "add", operation = "add_network_device", typeIdentifier = "OrderNumber:TEST", deviceName = "PLC_1", projectPath = "network-roundtrip" }),
            "hmi_read" => ("hmi-read-roundtrip", new { operationId = "devices", operation = "list_hmi_devices", projectPath = "hmi-read-roundtrip" }),
            "hmi_write" => ("hmi-write-roundtrip", new { operationId = "table", operation = "create_tag_table", tableName = "T", projectPath = "hmi-write-roundtrip" }),
            "vci_read" => ("vci-read-roundtrip", new { operationId = "w", operation = "list_workspaces", projectPath = "vci-read-roundtrip" }),
            "vci_write" => ("vci-write-roundtrip", new { operationId = "v", operation = "export", workspaceName = "Fixture", scope = new[] { new { nodeType = "Device", name = "Fixture" } }, projectPath = "vci-write-roundtrip" }),
            _ => throw new ArgumentOutOfRangeException(nameof(tool)),
        };
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var uiOpen = tool.EndsWith("_read", StringComparison.Ordinal)
            ? FakeWorkerUiOpenProject.ForWorkerRelativePath(bound)
            : new FakeWorkerUiOpenProject(bound);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(
            McpAccessMode.Full, audit.Path, bound);
        var before = requests.Methods().ToList();

        var result = await harness.Client.CallToolAsync(tool, Operations(item));

        ArgumentRejectionAssert.IsCanonicalValidationError(result, tool);
        Assert.Contains("projectPath", TextOf(result), StringComparison.Ordinal);
        Assert.DoesNotContain(
            requests.Methods().Skip(before.Count),
            TiaMcpServer.Contracts.Project.ProjectCapabilityCatalog.RequiresBoundProject);
        Assert.Empty(Network.NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
    }

    public static TheoryData<string> RetiredRootPathTools => new()
    {
        "get_project_status", "browse_project_tree", "read_cross_references", "compile_check",
        "save_project", "save_project_as", "archive_project", "close_project",
    };

    [Theory]
    [MemberData(nameof(RetiredRootPathTools))]
    public async Task RetiredRootProjectPathIsRejectedCanonically(string tool)
    {
        var isRead = tool is "get_project_status" or "browse_project_tree" or "read_cross_references";
        var bound = tool switch
        {
            "get_project_status" => "direct-status-only",
            "browse_project_tree" => "project-tree-v3-snapshot",
            "read_cross_references" => "xref-roundtrip",
            "compile_check" => "compile-passed",
            _ => "C:/Projects/guarded-lifecycle-root.ap21",
        };
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        var valid = tool switch
        {
            "read_cross_references" => new Dictionary<string, object?> { ["target"] = XrefTarget },
            "browse_project_tree" => new() { ["startSelector"] = new[] { new { nodeType = "Device", name = "PLC_1" } }, ["depth"] = 1 },
            "save_project_as" => new() { ["targetDirectory"] = audit.Path, ["targetName"] = "Copy" },
            "archive_project" => new() { ["archiveDirectory"] = audit.Path, ["archiveName"] = "Archive" },
            _ => new Dictionary<string, object?>(),
        };
        var retired = new Dictionary<string, object?>(valid) { ["projectPath"] = bound };
        using var uiOpen = isRead
            ? FakeWorkerUiOpenProject.ForWorkerRelativePath(bound)
            : new FakeWorkerUiOpenProject(bound);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(
            McpAccessMode.ReadWrite, audit.Path, bound);
        var before = requests.Methods().ToList();

        var result = await harness.Client.CallToolAsync(tool, retired);

        ArgumentRejectionAssert.IsCanonicalValidationError(result, tool);
        Assert.Contains("unknown argument", TextOf(result), StringComparison.Ordinal);
        Assert.Contains("projectPath", TextOf(result), StringComparison.Ordinal);
        Assert.Empty(requests.Methods().Skip(before.Count));
        Assert.Empty(Network.NetworkGuardedWriteMcpTests.AuditLines(audit.Path));

        // Control: the same call without projectPath is not an argument-shape failure.
        var control = await harness.Client.CallToolAsync(tool, valid);
        Assert.DoesNotContain("unknown argument", TextOf(control), StringComparison.Ordinal);
    }

    private static Dictionary<string, object?> Operations(object item)
        => new() { ["operations"] = new[] { item } };

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
