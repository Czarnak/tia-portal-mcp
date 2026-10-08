using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Tools;

[Collection("Mcp protocol serial")]
public sealed class BindProjectToolProtocolTests
{
    private const string A = "C:/Projects/A.ap21";
    private const string B = "C:/Projects/B.ap21";

    private sealed class Fixture : IDisposable
    {
        private readonly TempAuditDirectory directory = new();
        private readonly FakeWorkerPortals portals;
        private readonly FakeWorkerUiOpenProject ui;

        public Fixture(params FakeWorkerPortals.Entry[] entries)
        {
            Directory.CreateDirectory(directory.Path);
            portals = new FakeWorkerPortals(entries);
            ui = new FakeWorkerUiOpenProject(entries.FirstOrDefault()?.ProjectPath);
            Log = new FakeWorkerRequestLog(directory.Path);
        }

        public string AuditPath => directory.Path;
        public FakeWorkerRequestLog Log { get; }
        public string[] Methods() => Log.Methods().Where(method => method != "hello").ToArray();
        public void Dispose() { Log.Dispose(); ui.Dispose(); portals.Dispose(); directory.Dispose(); }
    }

    private static ValueTask<CallToolResult> Bind(McpProtocolTestHarness harness, string? path = null, bool? force = false)
        => harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?>
        {
            ["projectPath"] = path, ["forceRebind"] = force
        });

    private static JsonElement Document(CallToolResult response)
    {
        Assert.Empty(StructuredContractInspector.FindViolations(response));
        return response.StructuredContent!.Value;
    }

    private static JsonElement Value(CallToolResult response, string transition)
    {
        var document = Document(response);
        Assert.False(response.IsError == true);
        Assert.True(document.GetProperty("success").GetBoolean());
        var result = document.GetProperty("result");
        Assert.Equal("succeeded", result.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("failure").ValueKind);
        var value = result.GetProperty("value");
        Assert.Equal(transition, value.GetProperty("transition").GetString());
        Assert.Equal("verified", value.GetProperty("binding").GetProperty("state").GetString());
        Assert.Null(value.GetProperty("project").GetProperty("metadata").GetString());
        return value;
    }

    private static void Rejected(CallToolResult response, string category)
    {
        var document = Document(response);
        Assert.True(response.IsError);
        Assert.False(document.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("result").ValueKind);
        Assert.Equal(category, document.GetProperty("error").GetProperty("category").GetString());
    }

    private static JsonElement Failed(CallToolResult response, string category, string state)
    {
        var document = Document(response);
        Assert.False(response.IsError == true);
        Assert.False(document.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("error").ValueKind);
        var result = document.GetProperty("result");
        Assert.Equal("failed", result.GetProperty("status").GetString());
        Assert.Equal(category, result.GetProperty("failure").GetProperty("category").GetString());
        var value = result.GetProperty("value");
        Assert.Equal("none", value.GetProperty("transition").GetString());
        Assert.Equal(state, value.GetProperty("binding").GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Object, value.GetProperty("previousBinding").ValueKind);
        return value;
    }

    private static void NoTransport(McpProtocolTestHarness harness)
        => Assert.Null(typeof(OpennessWorkerClient).GetField("_transport", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(harness.WorkerClient));

    [Theory]
    [InlineData(null, null)]
    [InlineData("discovery-project-closed", null)]
    [InlineData("discovery-portal-lost", null)]
    [InlineData("discovery-missing-identity", null)]
    [InlineData("discovery-project-closed", "target_not_found")]
    [InlineData("discovery-portal-lost", "target_ambiguous")]
    public async Task Discovery_ReconcilesLiveIdentityInCanonicalBindingAndPortals(string? observation, string? selectorFailure)
    {
        using var fixture = new Fixture(new FakeWorkerPortals.Entry(42, A),
            new FakeWorkerPortals.Entry(43, observation is null ? null : $"C:/Projects/{observation}.ap21"));
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(accessMode: McpAccessMode.ReadOnly);
        Value(await Bind(harness, A), "bound");
        var before = fixture.Methods().Length;
        var arguments = new Dictionary<string, object?> { ["action"] = selectorFailure is null ? "list_portals" : "list_server_groups" };
        if (selectorFailure is not null) arguments["serverAlias"] = selectorFailure;
        var response = await harness.Client.CallToolAsync("bind_project", arguments);
        var document = Document(response);
        Assert.Equal(document.GetRawText(), Assert.IsType<TextContentBlock>(Assert.Single(response.Content)).Text);
        var value = observation is null ? document.GetProperty("result").GetProperty("value")
            : Failed(response, observation == "discovery-missing-identity" ? "postcondition_failed" : "binding_conflict", "invalidated");
        Assert.Equal("verified", value.GetProperty("previousBinding").GetProperty("state").GetString());
        Assert.Equal(observation is null, document.GetProperty("success").GetBoolean());
        Assert.Equal("none", value.GetProperty("transition").GetString());
        Assert.Equal(JsonValueKind.Null, value.GetProperty("project").ValueKind);
        Assert.NotEmpty(value.GetProperty("portals").EnumerateArray());
        Assert.All(value.GetProperty("portals").EnumerateArray(), portal =>
            Assert.Equal(observation is null && portal.GetProperty("processId").GetInt32() == 42, portal.GetProperty("isBound").GetBoolean()));
        if (observation == "discovery-project-closed")
            Assert.Equal(JsonValueKind.Null, value.GetProperty("portals")[0].GetProperty("projectPath").ValueKind);
        Assert.Equal(6, value.GetProperty("inspection").EnumerateObject().Count(p => p.Value.ValueKind == JsonValueKind.Null));
        Assert.Equal(selectorFailure is null ? new[] { "list_tia_portal_processes" }
            : new[] { "list_server_groups", "list_tia_portal_processes" }, fixture.Methods().Skip(before));
        Assert.Empty(Directory.GetFiles(fixture.AuditPath, "*.jsonl", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(McpAccessMode.ReadOnly)]
    [InlineData(McpAccessMode.ReadWrite)]
    [InlineData(McpAccessMode.Full)]
    public async Task BindProject_RegisteredInEveryMode(McpAccessMode mode)
    {
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(mode);
        var tool = Assert.Single(await harness.Client.ListToolsAsync(), tool => tool.Name == "bind_project");
        Assert.False(tool.ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.False(tool.ProtocolTool.Annotations.DestructiveHint);
        Assert.True(tool.ProtocolTool.Annotations.IdempotentHint);
        Assert.True(tool.ProtocolTool.Annotations.OpenWorldHint);
        var schema = tool.ProtocolTool.InputSchema;
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(new[] { "action", "forceRebind", "group", "portalProcessId", "projectPath", "serverAlias", "serverProjectName" }, schema.GetProperty("properties").EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(new[] { "boolean", "null" }, schema.GetProperty("properties").GetProperty("forceRebind")
            .GetProperty("type").EnumerateArray().Select(type => type.GetString()));
        Assert.NotNull(tool.ProtocolTool.OutputSchema);
        Assert.Equal("boolean", tool.ProtocolTool.OutputSchema!.Value.GetProperty("properties")
            .GetProperty("success").GetProperty("type").GetString());
        NoTransport(harness);
    }

    [Theory]
    [InlineData("{\"unexpected\":true}")]
    [InlineData("{\"projectPath\":42}")]
    [InlineData("{\"projectPath\":{}}")]
    [InlineData("{\"forceRebind\":\"true\"}")]
    [InlineData("{\"forceRebind\":1}")]
    [InlineData("{\"forceRebind\":[]}")]
    public async Task BindProject_RejectsUnknownOrMistypedArguments(string json)
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(accessMode: McpAccessMode.ReadOnly);
        var arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;
        Rejected(await harness.Client.CallToolAsync("bind_project", arguments), "validation_error");
        NoTransport(harness);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative.ap21")]
    [InlineData("C:A.ap21")]
    [InlineData("C:/Projects/A.txt")]
    [InlineData("C:/Projects/A.als21")]
    [InlineData("C:/Projects/A.ap21/child")]
    public async Task BindProject_RejectsRelativeOrNonAp21Path(string path)
    {
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadOnly);
        Rejected(await Bind(harness, path), "validation_error");
        NoTransport(harness);
    }

    [Fact]
    public async Task BindProject_NoPathSingleProject_Bound()
    {
        using var fixture = new Fixture(new FakeWorkerPortals.Entry(42, A));
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(accessMode: McpAccessMode.ReadOnly);
        var value = Value(await Bind(harness), "bound");
        Assert.Equal("unbound", value.GetProperty("previousBinding").GetProperty("state").GetString());
        Assert.Single(value.GetProperty("portals").EnumerateArray());
        Assert.Equal(new[] { "list_tia_portal_processes", "select_portal_project", "get_project_status" }, fixture.Methods());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"action\":null}")]
    [InlineData("{\"action\":\"bind\"}")]
    public async Task BindAction_PreservesDefaultShape(string json)
    {
        using var fixture = new Fixture(new FakeWorkerPortals.Entry(42, A));
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(accessMode: McpAccessMode.ReadOnly);
        var value = Value(await harness.Client.CallToolAsync("bind_project", JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!), "bound");
        Assert.False(value.TryGetProperty("inspection", out _));
    }

    [Fact]
    public async Task ListPortals_DoesNotSelectSoleProject()
    {
        using var fixture = new Fixture(new FakeWorkerPortals.Entry(42, A));
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(accessMode: McpAccessMode.ReadOnly);
        var response = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?> { ["action"] = "list_portals" });
        var document = Document(response);
        Assert.True(document.GetProperty("success").GetBoolean());
        var value = document.GetProperty("result").GetProperty("value");
        Assert.Equal("none", value.GetProperty("transition").GetString());
        Assert.Equal("unbound", value.GetProperty("binding").GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, value.GetProperty("project").ValueKind);
        Assert.Equal("list_portals", value.GetProperty("inspection").GetProperty("action").GetString());
        Assert.Single(value.GetProperty("portals").EnumerateArray());
        Assert.Equal(new[] { "list_tia_portal_processes" }, fixture.Methods());
    }

    [Theory]
    [InlineData("list_server_connections", "serverConnections")]
    [InlineData("list_server_groups", "serverGroups")]
    [InlineData("list_server_projects", "serverProjects")]
    [InlineData("list_local_sessions", "localSessions")]
    [InlineData("get_lock_state", "lockState")]
    public async Task Inspection_ProjectsTypedSlotThroughRegisteredSchema(string action, string slot)
    {
        using var fixture = new Fixture(new FakeWorkerPortals.Entry(42, A));
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(accessMode: McpAccessMode.ReadOnly);
        var arguments = new Dictionary<string, object?> { ["action"] = action, ["portalProcessId"] = 42 };
        if (action != "list_server_connections") arguments["serverAlias"] = "Fixture";
        if (action is "list_server_projects" or "list_local_sessions" or "get_lock_state") arguments["group"] = JsonSerializer.Deserialize<JsonElement>("""{"isRoot":true,"name":null}""");
        if (action is "list_local_sessions" or "get_lock_state") arguments["serverProjectName"] = "Project A";
        var response = await harness.Client.CallToolAsync("bind_project", arguments);
        var document = Document(response);
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        var value = document.GetProperty("result").GetProperty("value");
        Assert.Equal("none", value.GetProperty("transition").GetString());
        Assert.Equal(value.GetProperty("previousBinding").GetRawText(), value.GetProperty("binding").GetRawText());
        Assert.Equal(JsonValueKind.Null, value.GetProperty("project").ValueKind);
        var inspection = value.GetProperty("inspection");
        Assert.Equal(action, inspection.GetProperty("action").GetString());
        Assert.Equal(42, inspection.GetProperty("portalProcessId").GetInt32());
        Assert.Equal(JsonValueKind.Object, inspection.GetProperty(slot).ValueKind);
        Assert.Equal(new[] { action }, fixture.Methods());
        Assert.Empty(Directory.GetFiles(fixture.AuditPath, "*.jsonl", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("server-failure", "worker_operation_failed")]
    [InlineData("malformed", "protocol_error")]
    [InlineData("missing-pid", "protocol_error")]
    [InlineData("wrong-pid", "protocol_error")]
    public async Task Inspection_ExecutedFailuresKeepCanonicalOutcome(string alias, string category)
    {
        using var fixture = new Fixture(new FakeWorkerPortals.Entry(42, A));
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(accessMode: McpAccessMode.ReadOnly);
        var response = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?>
            { ["action"] = "list_server_groups", ["portalProcessId"] = 42, ["serverAlias"] = alias });
        var value = Failed(response, category, "unbound");
        var inspection = value.GetProperty("inspection");
        Assert.Equal("list_server_groups", inspection.GetProperty("action").GetString());
        Assert.Equal(6, inspection.EnumerateObject().Count(p => p.Value.ValueKind == JsonValueKind.Null));
        Assert.DoesNotContain("secret-sentinel", Document(response).GetRawText());
    }

    [Theory]
    [InlineData("{\"action\":\"list_portals\",\"projectPath\":null}")]
    [InlineData("{\"action\":\"list_portals\",\"forceRebind\":false}")]
    [InlineData("{\"action\":\"list_portals\",\"portalProcessId\":42}")]
    [InlineData("{\"action\":\"list_server_connections\",\"portalProcessId\":null}")]
    [InlineData("{\"action\":\"list_server_connections\",\"portalProcessId\":0}")]
    [InlineData("{\"action\":\"list_server_connections\",\"portalProcessId\":1.5}")]
    [InlineData("{\"action\":\"list_server_connections\",\"portalProcessId\":\"42\"}")]
    [InlineData("{\"action\":\"list_server_connections\",\"portalProcessId\":true}")]
    [InlineData("{\"action\":\"list_server_connections\",\"serverAlias\":null}")]
    [InlineData("{\"action\":\"list_server_groups\"}")]
    [InlineData("{\"action\":\"list_server_groups\",\"serverAlias\":\" \"}")]
    [InlineData("{\"action\":\"list_server_projects\",\"serverAlias\":\"Fixture\",\"group\":{\"isRoot\":true}}")]
    [InlineData("{\"action\":\"list_server_projects\",\"serverAlias\":\"Fixture\",\"group\":{\"name\":null}}")]
    [InlineData("{\"action\":\"list_server_projects\",\"serverAlias\":\"Fixture\",\"group\":{\"isRoot\":true,\"name\":\"Root\"}}")]
    [InlineData("{\"action\":\"list_server_projects\",\"serverAlias\":\"Fixture\",\"group\":{\"isRoot\":false,\"name\":null}}")]
    [InlineData("{\"action\":\"list_server_projects\",\"serverAlias\":\"Fixture\",\"group\":{\"isRoot\":true,\"name\":null,\"extra\":false}}")]
    [InlineData("{\"action\":\"list_local_sessions\",\"serverAlias\":\"Fixture\",\"group\":{\"isRoot\":true,\"name\":null}}")]
    [InlineData("{\"action\":\"get_lock_state\",\"serverAlias\":\"Fixture\",\"group\":{\"isRoot\":true,\"name\":null},\"serverProjectName\":\"\"}")]
    [InlineData("{\"action\":\"bind\",\"portalProcessId\":0}")]
    [InlineData("{\"action\":\"BIND\"}")]
    [InlineData("{\"action\":true}")]
    [InlineData("{\"action\":\"get_session_state\"}")]
    [InlineData("{\"action\":\"list_portals\",\"operations\":[]}")]
    public async Task Inspection_InvalidArgumentsRejectedBeforeDispatch(string json)
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(accessMode: McpAccessMode.ReadOnly);
        Rejected(await harness.Client.CallToolAsync("bind_project", JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!), "validation_error");
        NoTransport(harness);
    }

    [Fact]
    public async Task BindProject_NullForceRebind_UsesDefaultFalseThroughSdk()
    {
        using var fixture = new Fixture(new FakeWorkerPortals.Entry(42, A), new FakeWorkerPortals.Entry(43, B));
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(accessMode: McpAccessMode.ReadOnly);
        Value(await Bind(harness, A, null), "bound");
        var before = fixture.Methods();
        Rejected(await Bind(harness, B, null), "binding_conflict");
        Assert.Equal(before, fixture.Methods());
        Value(await Bind(harness, null, null), "unchanged");
    }

    [Fact]
    public async Task BindProject_SamePath_Unchanged()
    {
        using var fixture = new Fixture(new FakeWorkerPortals.Entry(42, A));
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(accessMode: McpAccessMode.ReadOnly);
        Value(await Bind(harness, A), "bound");
        var snapshot = harness.WorkerClient.BindingSnapshot;
        Value(await Bind(harness, A.ToUpperInvariant()), "unchanged");
        Assert.True(snapshot.SameBinding(harness.WorkerClient.BindingSnapshot));
    }

    [Fact]
    public async Task BindProject_OtherPathForce_Switched()
    {
        using var fixture = new Fixture(new FakeWorkerPortals.Entry(42, A), new FakeWorkerPortals.Entry(43, B));
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(accessMode: McpAccessMode.ReadOnly);
        Value(await Bind(harness, A), "bound");
        var value = Value(await Bind(harness, B, true), "switched");
        Assert.Equal(42, value.GetProperty("previousBinding").GetProperty("portalProcessId").GetInt32());
        Assert.Equal(43, value.GetProperty("binding").GetProperty("portalProcessId").GetInt32());
        Assert.DoesNotContain(fixture.Methods(), method => method is "open_project" or "create_project" or "close_project" or "save_project");
    }

    [Fact]
    public async Task BindProject_OtherPathNoForce_RejectedBindingConflictNothingSent()
    {
        using var fixture = new Fixture(new FakeWorkerPortals.Entry(42, A), new FakeWorkerPortals.Entry(43, B));
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(accessMode: McpAccessMode.ReadOnly);
        Value(await Bind(harness, A), "bound");
        var before = fixture.Methods();
        Rejected(await Bind(harness, B), "binding_conflict");
        Assert.Equal(before, fixture.Methods());
    }

    [Fact]
    public async Task BindProject_NoProjects_FailedTargetNotFoundIsErrorFalse()
    {
        using var fixture = new Fixture();
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(accessMode: McpAccessMode.ReadOnly);
        var value = Failed(await Bind(harness), "target_not_found", "unbound");
        Assert.Empty(value.GetProperty("portals").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, value.GetProperty("binding").GetProperty("projectPath").ValueKind);
        Assert.Equal(JsonValueKind.Null, value.GetProperty("binding").GetProperty("portalProcessId").ValueKind);
    }

    [Fact]
    public async Task BindProject_Ambiguous_FailedOutcomeWithPortals_IsErrorFalse()
    {
        using var fixture = new Fixture(new FakeWorkerPortals.Entry(42, A), new FakeWorkerPortals.Entry(43, B));
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(accessMode: McpAccessMode.ReadOnly);
        var value = Failed(await Bind(harness), "target_ambiguous", "unbound");
        Assert.Equal(2, value.GetProperty("portals").GetArrayLength());
        Assert.Equal(new[] { "list_tia_portal_processes" }, fixture.Methods());
    }

    [Fact]
    public async Task BindProject_AdvertisedPathNeedsNoLocalFile_AndMissingTargetIsOperationalFailure()
    {
        using var fixture = new Fixture(new FakeWorkerPortals.Entry(42, A));
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(accessMode: McpAccessMode.ReadOnly);
        Value(await Bind(harness, A), "bound");
        var value = Failed(await Bind(harness, "C:/Projects/Missing.ap21", true), "target_not_found", "verified");
        Assert.Single(value.GetProperty("portals").EnumerateArray());
        Assert.True(value.GetProperty("portals")[0].GetProperty("isBound").GetBoolean());
    }

    [Fact]
    public async Task BindProject_FailedSwitch_ReportsInvalidatedBinding()
    {
        const string target = "C:/Projects/portal-switch-fails-after-detach.ap21";
        using var fixture = new Fixture(new FakeWorkerPortals.Entry(42, A), new FakeWorkerPortals.Entry(43, target));
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(accessMode: McpAccessMode.ReadOnly);
        Value(await Bind(harness, A), "bound");
        var value = Failed(await Bind(harness, target, true), "worker_operation_failed", "invalidated");
        Assert.Equal("verified", value.GetProperty("previousBinding").GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, value.GetProperty("project").ValueKind);
    }

    [Fact]
    public async Task BindProject_HeadlessRefusal_IsRejectedAfterSelection()
    {
        using var fixture = new Fixture(new FakeWorkerPortals.Entry(42, A, HasUserInterface: false, Modified: true), new FakeWorkerPortals.Entry(43, B));
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(accessMode: McpAccessMode.ReadOnly);
        Value(await Bind(harness, A), "bound");
        var before = fixture.Methods().Length;
        Rejected(await Bind(harness, B, true), "binding_conflict");
        Assert.Equal(new[] { "select_portal_project" }, fixture.Methods().Skip(before));
        Assert.True(harness.WorkerClient.BindingSnapshot.IsVerified);
    }

    [Fact]
    public async Task BindProject_ReadOnly_Binds()
    {
        using var fixture = new Fixture(new FakeWorkerPortals.Entry(42, A));
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadOnly);
        Value(await Bind(harness, A), "bound");
        Assert.All(fixture.Methods(), method => Assert.True(OperationPolicyCatalog.GetCapability(method) is OperationCapability.Observe or OperationCapability.SessionSelection));
    }

    [Fact]
    public async Task PortalProcessInfo_IsBoundFollowsVerifiedBindingNotAttachment()
    {
        using var fixture = new Fixture(new FakeWorkerPortals.Entry(42, A), new FakeWorkerPortals.Entry(43, null));
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(accessMode: McpAccessMode.ReadOnly);
        var portals = Value(await Bind(harness), "bound").GetProperty("portals");
        Assert.True(portals[0].GetProperty("isBound").GetBoolean());
        Assert.False(portals[1].GetProperty("isBound").GetBoolean());
    }

    [Fact]
    public async Task BindProject_ProtocolFailure_ReturnsFailedValueAndUnboundPortals()
    {
        using var fixture = new Fixture(new FakeWorkerPortals.Entry(42, "C:/Projects/portal-selection-malformed.ap21"));
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(accessMode: McpAccessMode.ReadOnly);
        var value = Failed(await Bind(harness), "protocol_error", "unbound");
        Assert.Single(value.GetProperty("portals").EnumerateArray());
        Assert.False(value.GetProperty("portals")[0].GetProperty("isBound").GetBoolean());
        Assert.DoesNotContain("untrustedMarker", value.GetRawText());
    }

    [Fact]
    public async Task BindProject_TextEqualsStructuredContent()
    {
        using var fixture = new Fixture(new FakeWorkerPortals.Entry(42, A));
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(accessMode: McpAccessMode.ReadOnly);
        var response = await Bind(harness);
        Assert.Equal(Document(response).GetRawText(), Assert.IsType<TextContentBlock>(Assert.Single(response.Content)).Text);
    }

    [Fact]
    public async Task BindProject_DescriptionStatesNeverOpensAndOpennessDialog()
    {
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadOnly);
        var description = Assert.Single(await harness.Client.ListToolsAsync(), tool => tool.Name == "bind_project").Description!;
        Assert.Contains("never opens, creates, closes or saves", description);
        Assert.Contains("only open project", description);
        Assert.Contains("Openness access dialog", description);
        Assert.Contains("human", description);
    }

    [Fact]
    public async Task BindProject_ThenRead_DoesNotChangeBinding()
    {
        using var fixture = new Fixture(new FakeWorkerPortals.Entry(42, A));
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadOnly);
        Value(await Bind(harness, A), "bound");
        var snapshot = harness.WorkerClient.BindingSnapshot;
        Document(await harness.Client.CallToolAsync("get_project_status", new Dictionary<string, object?>()));
        Assert.True(snapshot.SameBinding(harness.WorkerClient.BindingSnapshot));
        Assert.Equal(new[] { "select_portal_project", "get_project_status", "get_project_status" }, fixture.Methods());
    }

    [Fact]
    public async Task ReadWriteNoProject_BindThenNetworkWritePreview_PassesBindingGate()
    {
        const string source = "C:/Projects/network-roundtrip.ap21";
        using var fixture = new Fixture(new FakeWorkerPortals.Entry(42, source));
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, fixture.AuditPath);
        var arguments = new Dictionary<string, object?> { ["dryRun"] = true, ["operations"] = new[] { new
        {
            operationId = "add", operation = "add_network_device", projectPath = source,
            typeIdentifier = "OrderNumber:6ES7 510-1DJ01-0AB0/V2.0", deviceName = "PLC_1"
        } } };
        var blocked = await harness.Client.CallToolAsync("network_write", arguments);
        Assert.True(blocked.IsError);
        Assert.Equal("binding_conflict", Document(blocked).GetProperty("error").GetProperty("category").GetString());
        NoTransport(harness);
        Value(await Bind(harness, source), "bound");
        var preview = Document(await harness.Client.CallToolAsync("network_write", arguments));
        Assert.True(preview.GetProperty("success").GetBoolean(), preview.GetRawText());
        Assert.Equal("preview", preview.GetProperty("phase").GetString());
        Assert.NotEmpty(preview.GetProperty("effects").EnumerateArray());
    }
}
