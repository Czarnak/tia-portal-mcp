using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tools;
using Xunit;

namespace TiaMcpServer.Tests.Tools;

[Collection("Mcp protocol serial")]
public sealed class LifecycleMcpProtocolTests
{
    public static IEnumerable<object[]> LifecycleTools => new[]
    {
        "open_project", "create_project", "save_project", "save_project_as", "archive_project", "close_project"
    }.Select(tool => new object[] { tool });

    [Theory]
    [MemberData(nameof(LifecycleTools))]
    public Task ReadWrite_LifecycleAccept_Mutates(string tool)
        => LifecycleConfirmationAsync(tool, McpAccessMode.ReadWrite, "accept", true);

    [Theory]
    [MemberData(nameof(LifecycleTools))]
    public Task ReadWrite_LifecycleDecline_NoMutation(string tool)
        => LifecycleConfirmationAsync(tool, McpAccessMode.ReadWrite, "decline", false);

    [Theory]
    [MemberData(nameof(LifecycleTools))]
    public Task ReadWrite_LifecycleCancel_NoMutation(string tool)
        => LifecycleConfirmationAsync(tool, McpAccessMode.ReadWrite, "cancel", false);

    [Theory]
    [MemberData(nameof(LifecycleTools))]
    public Task ReadWrite_NoElicitationCapability_AccessDenied(string tool)
        => LifecycleConfirmationAsync(tool, McpAccessMode.ReadWrite, null, false);

    [Theory]
    [MemberData(nameof(LifecycleTools))]
    public Task Full_Lifecycle_SendsNoElicitation(string tool)
        => LifecycleConfirmationAsync(tool, McpAccessMode.Full, "decline", true);

    private static async Task LifecycleConfirmationAsync(string tool, McpAccessMode mode, string? action, bool applies)
    {
        using var audit = new TempAuditDirectory();
        using var fixture = new LifecycleProtocolFixture();
        using var requests = new LifecycleRequestLog(fixture.Root);
        var prompts = 0;
        var options = action is null ? null : ClientOptions((request, _) =>
        {
            prompts++;
            Assert.Contains(tool, request.Message);
            Assert.Contains("confirm", request.RequestedSchema!.Required!);
            return ValueTask.FromResult(new ElicitResult
            {
                Action = action,
                Content = new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.SerializeToElement(true) }
            });
        });
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(
            mode, audit.Path, tool is "open_project" or "create_project" ? null : fixture.SourcePath, clientOptions: options);
        var before = harness.WorkerClient.BindingSnapshot;
        var result = await harness.Client.CallToolAsync(tool, fixture.Arguments(tool));
        var document = Document(result);
        Assert.Equal(mode == McpAccessMode.ReadWrite && action is not null ? 1 : 0, prompts);
        Assert.Equal(applies, document.GetProperty("success").GetBoolean());
        Assert.Equal(!applies, result.IsError == true);
        Assert.Equal(applies ? "applied" : "blocked", document.GetProperty("phase").GetString());
        Assert.Empty(StructuredContractInspector.FindViolations(result));
        await harness.WorkerClient.GetBasicProjectStatusAsync(null);
        var methods = requests.Methods();
        Assert.Contains("hello", methods); // Prove recording is active before trusting absence.
        Assert.Contains("get_basic_project_status", methods);
        Assert.Equal(applies ? new[] { tool } : Array.Empty<string>(), methods.Where(IsLifecycleMutation));
        if (applies)
        {
            Assert.Equal("succeeded", document.GetProperty("result").GetProperty("status").GetString());
            Assert.Equal("succeeded", document.GetProperty("verification").GetProperty("status").GetString());
        }
        else
        {
            Assert.Equal("access_denied", document.GetProperty("error").GetProperty("category").GetString());
            Assert.Equal(JsonValueKind.Null, document.GetProperty("result").ValueKind);
            Assert.True(before.SameBinding(harness.WorkerClient.BindingSnapshot));
            Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Created")));
            Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Copy")));
            Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.ArchiveDirectory));
        }
    }

    [Fact]
    public async Task ReadWrite_OpenProject_BindsAndUnblocksWrites()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = new LifecycleProtocolFixture();
        var prompts = 0;
        var options = ClientOptions((_, _) =>
        {
            prompts++;
            return ValueTask.FromResult(new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.SerializeToElement(true) }
            });
        });
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(
            McpAccessMode.ReadWrite, audit.Path, clientOptions: options);
        var before = await harness.Client.CallToolAsync("compile_check", new Dictionary<string, object?>());
        Assert.Equal("binding_conflict", before.StructuredContent!.Value.GetProperty("error").GetProperty("category").GetString());
        var opened = await harness.Client.CallToolAsync("open_project", fixture.Arguments("open_project"));
        Assert.True(Document(opened).GetProperty("success").GetBoolean());
        Assert.True(harness.WorkerClient.BindingSnapshot.IsVerified);
        Assert.Equal(fixture.DestinationPath, harness.WorkerClient.BindingSnapshot.ProjectPath);
        var saved = await harness.Client.CallToolAsync("save_project", fixture.Arguments("save_project"));
        Assert.True(Document(saved).GetProperty("success").GetBoolean());
        Assert.Equal(2, prompts);
    }

    private static bool IsLifecycleMutation(string method)
        => method is "open_project" or "create_project" or "save_project" or "save_project_as" or "archive_project" or "close_project";

    // This class runs in the exclusive MCP protocol collection, so the child-process
    // environment cannot overlap other tests. Only method names are recorded.
    private sealed class LifecycleRequestLog : IDisposable
    {
        private const string Variable = "TIA_MCP_FAKE_WORKER_REQUEST_LOG";
        private readonly string? _previous = Environment.GetEnvironmentVariable(Variable);
        private readonly string _path;

        public LifecycleRequestLog(string directory)
        {
            _path = Path.Combine(directory, "requests.log");
            Environment.SetEnvironmentVariable(Variable, _path);
        }

        public string[] Methods() => File.Exists(_path) ? File.ReadAllLines(_path) : Array.Empty<string>();
        public void Dispose() => Environment.SetEnvironmentVariable(Variable, _previous);
    }

    [Theory]
    [InlineData("accept", true, true)]
    [InlineData("accept", false, false)]
    [InlineData("decline", true, false)]
    [InlineData("cancel", true, false)]
    public async Task ReadWrite_ModifiedClose_ConfirmsAndAuditsOutcome(
        string action, bool confirm, bool applied)
    {
        using var audit = new TempAuditDirectory();
        using var fixture = new LifecycleProtocolFixture("-modified");
        var prompts = new List<string>();
        var options = ClientOptions((request, _) =>
        {
            prompts.Add(request.Message);
            Assert.Contains(fixture.SourcePath, request.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("confirm", request.RequestedSchema!.Required!);
            return ValueTask.FromResult(new ElicitResult
            {
                Action = action,
                Content = new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.SerializeToElement(confirm) }
            });
        });
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(
            McpAccessMode.ReadWrite, audit.Path, fixture.SourcePath, clientOptions: options);

        var result = await harness.Client.CallToolAsync("close_project", new Dictionary<string, object?>
        {
            ["saveBeforeClose"] = false
        });

        Assert.Single(prompts);
        var document = Document(result);
        Assert.Equal(applied, document.GetProperty("success").GetBoolean());
        Assert.Equal(!applied, result.IsError == true);
        Assert.Equal(applied ? "applied" : "blocked", document.GetProperty("phase").GetString());
        if (!applied) Assert.Equal("access_denied", document.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(applied ? ProjectBindingSnapshot.UnboundState : ProjectBindingSnapshot.VerifiedState,
            harness.WorkerClient.BindingSnapshot.State);
        Assert.Empty(StructuredContractInspector.FindViolations(result));
        AssertAudit(audit.Path, result, applied ? "user" : null, confirmationBy: "user",
            confirmationOutcome: applied ? "confirmed" : action == "cancel" ? "cancelled" : "declined");
    }

    [Fact]
    public async Task Full_ModifiedClose_UnsupportedClient_UsesPolicy()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = new LifecycleProtocolFixture("-modified");
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(
            McpAccessMode.Full, audit.Path, fixture.SourcePath);
        var result = await harness.Client.CallToolAsync("close_project", new Dictionary<string, object?>
        {
            ["saveBeforeClose"] = false
        });

        Assert.False(result.IsError == true);
        Assert.True(Document(result).GetProperty("success").GetBoolean());
        Assert.Equal(ProjectBindingSnapshot.UnboundState, harness.WorkerClient.BindingSnapshot.State);
        AssertAudit(audit.Path, result, "policy");
    }

    [Fact]
    public async Task Full_ModifiedClose_UrlOnlyCapability_UsesPolicyWithoutSendingAForm()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = new LifecycleProtocolFixture("-modified");
        var prompts = 0;
        var options = new McpClientOptions
        {
            Capabilities = new ClientCapabilities
            {
                Elicitation = new ElicitationCapability { Url = new UrlElicitationCapability() }
            },
            Handlers = new McpClientHandlers
            {
                ElicitationHandler = (_, _) =>
                {
                    prompts++;
                    return ValueTask.FromResult(new ElicitResult
                    {
                        Action = "accept",
                        Content = new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.SerializeToElement(true) }
                    });
                }
            }
        };
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(
            McpAccessMode.Full, audit.Path, fixture.SourcePath, clientOptions: options);
        var result = await harness.Client.CallToolAsync("close_project", new Dictionary<string, object?>
        {
            ["saveBeforeClose"] = false
        });
        Assert.Equal(0, prompts);
        Assert.False(result.IsError == true);
        Assert.True(Document(result).GetProperty("success").GetBoolean());
        Assert.Equal(ProjectBindingSnapshot.UnboundState, harness.WorkerClient.BindingSnapshot.State);
        AssertAudit(audit.Path, result, "policy");
    }

    [Fact]
    public async Task Full_DryRun_ReportsPolicyGuard_WithoutPromptOrClose()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = new LifecycleProtocolFixture("-modified");
        var prompts = 0;
        var options = ClientOptions((_, _) =>
        {
            prompts++;
            return ValueTask.FromResult(new ElicitResult { Action = "accept" });
        });
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(
            McpAccessMode.Full, audit.Path, fixture.SourcePath, clientOptions: options);
        var result = await harness.Client.CallToolAsync("close_project", new Dictionary<string, object?>
        {
            ["saveBeforeClose"] = false, ["dryRun"] = true
        });

        var document = Document(result);
        Assert.Equal(0, prompts);
        Assert.Equal("preview", document.GetProperty("phase").GetString());
        Assert.True(document.GetProperty("success").GetBoolean());
        var guard = Assert.Single(document.GetProperty("guards").EnumerateArray());
        Assert.True(guard.GetProperty("acknowledged").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("result").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.GetProperty("verification").ValueKind);
        Assert.True(harness.WorkerClient.BindingSnapshot.IsVerified);
        AssertAudit(audit.Path, result, null);
    }

    [Fact]
    public async Task Full_CloseWithoutSaveModified_ExecutesWithPolicyGuard()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = new LifecycleProtocolFixture("-modified");
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(
            McpAccessMode.Full, audit.Path, fixture.SourcePath);
        var result = await harness.Client.CallToolAsync("close_project", new Dictionary<string, object?>
        {
            ["saveBeforeClose"] = false
        });
        Assert.False(result.IsError == true);
        Assert.True(Document(result).GetProperty("success").GetBoolean());
        AssertAudit(audit.Path, result, "policy");
    }

    [Theory]
    [InlineData("-worker-failure", "failed")]
    [InlineData("-malformed", "failed")]
    [InlineData("-verification-failure", "succeeded")]
    public async Task AttemptedFailures_PreserveTypedOutcome_WithoutTopLevelRejection(
        string scenario, string mutationStatus)
    {
        using var audit = new TempAuditDirectory();
        using var fixture = new LifecycleProtocolFixture(scenario);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(
            McpAccessMode.Full, audit.Path, fixture.SourcePath);
        var result = await harness.Client.CallToolAsync("save_project", fixture.Arguments("save_project"));
        var document = Document(result);
        Assert.False(result.IsError == true);
        Assert.False(document.GetProperty("success").GetBoolean());
        Assert.Equal("applied", document.GetProperty("phase").GetString());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("error").ValueKind);
        Assert.Equal(mutationStatus, document.GetProperty("result").GetProperty("status").GetString());
        if (scenario == "-verification-failure")
        {
            Assert.Equal("failed", document.GetProperty("verification").GetProperty("status").GetString());
            Assert.NotEmpty(document.GetProperty("warnings").EnumerateArray());
        }
        Assert.Empty(StructuredContractInspector.FindViolations(result));
        AssertAudit(audit.Path, result, null, checkGuard: false);
    }

    [Theory]
    [InlineData("open_project")]
    [InlineData("create_project")]
    [InlineData("save_project")]
    [InlineData("save_project_as")]
    [InlineData("archive_project")]
    [InlineData("close_project")]
    public async Task EveryLifecycleDryRun_PreservesSourceAndFilesystem(string tool)
    {
        using var audit = new TempAuditDirectory();
        using var fixture = new LifecycleProtocolFixture();
        // Exercise create from an unbound session and the other tools from a verified source.
        var source = tool == "create_project" ? null : fixture.SourcePath;
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(
            McpAccessMode.Full, audit.Path, source);
        var before = harness.WorkerClient.BindingSnapshot;
        var arguments = fixture.Arguments(tool);
        arguments["dryRun"] = true;
        if (tool == "open_project") arguments["forceRebind"] = true;
        var result = await harness.Client.CallToolAsync(tool, arguments);
        var document = Document(result);
        Assert.False(result.IsError == true);
        Assert.Equal("preview", document.GetProperty("phase").GetString());
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.True(before.SameBinding(harness.WorkerClient.BindingSnapshot));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Created")));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Copy")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.ArchiveDirectory));
        Assert.Empty(StructuredContractInspector.FindViolations(result));
        AssertAudit(audit.Path, result, null, checkGuard: false);
    }

    private static McpClientOptions ClientOptions(
        Func<ElicitRequestParams, CancellationToken, ValueTask<ElicitResult>> handler) => new()
    {
        Capabilities = new ClientCapabilities
        {
            Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() }
        },
        Handlers = new McpClientHandlers
        {
            ElicitationHandler = (request, cancellationToken) => handler(
                request ?? throw new InvalidOperationException("The confirmation request is required."), cancellationToken)
        }
    };

    private static JsonElement Document(CallToolResult result)
    {
        using var document = JsonDocument.Parse(Assert.Single(result.Content.OfType<TextContentBlock>()).Text);
        return document.RootElement.Clone();
    }

    private static void AssertAudit(string directory, CallToolResult result, string? satisfaction, bool checkGuard = true,
        string? confirmationBy = null, string? confirmationOutcome = null)
    {
        var lines = Directory.GetFiles(directory, "writes-*.jsonl").SelectMany(File.ReadLines).ToArray();
        var line = Assert.Single(lines);
        using var record = JsonDocument.Parse(line);
        var text = Assert.Single(result.Content.OfType<TextContentBlock>()).Text;
        Assert.Equal(text, record.RootElement.GetProperty("responseText").GetString());
        Assert.Equal("sha256:" + ContentHashes.Sha256Hex(text), record.RootElement.GetProperty("responseHash").GetString());
        Assert.Equal(2, record.RootElement.GetProperty("recordVersion").GetInt32());
        var confirmation = record.RootElement.GetProperty("confirmation");
        Assert.Equal(confirmationBy ?? (Document(result).GetProperty("phase").GetString() == "preview" ? "none" : "policy"), confirmation.GetProperty("by").GetString());
        Assert.Equal(confirmationOutcome ?? "not_requested", confirmation.GetProperty("outcome").GetString());
        if (checkGuard)
        {
            var guard = Assert.Single(record.RootElement.GetProperty("guards").EnumerateArray());
            Assert.Equal(satisfaction, guard.GetProperty("satisfiedBy").GetString());
        }
    }
}
