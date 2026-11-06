using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Library;

[Collection("Mcp protocol serial")]
public sealed class LibraryWriteMcpTests
{
    [Theory]
    [InlineData("create_global")]
    [InlineData("open_global")]
    [InlineData("retrieve_global")]
    [InlineData("save_global")]
    [InlineData("close_global")]
    [InlineData("archive_global")]
    public async Task ReadWrite_LifecycleCall_PromptsOnce_AuditUser(string operation)
    {
        var prompts = new List<string>();
        await using var fixture = await Fixture.StartAsync(McpAccessMode.ReadWrite, Options(prompts));

        var reply = await CallAsync(fixture, Lifecycle(operation));

        AssertApplied(reply, operation);
        Assert.Contains(operation, Assert.Single(prompts));
        Assert.Equal(new[] { "library_global_" + operation }, fixture.Mutations());
        Assert.Equal(2, fixture.Requests.Methods().Count(method => method == "library_global_plan"));
        AssertAudit(fixture, reply, "user", "confirmed", "succeeded");
    }

    [Fact]
    public async Task ReadWrite_MixedLifecycleAndContent_PromptsOnce()
    {
        var prompts = new List<string>();
        await using var fixture = await Fixture.StartAsync(McpAccessMode.ReadWrite, Options(prompts));

        var reply = await CallAsync(fixture, Lifecycle("save_global"), Folder());

        AssertApplied(reply, "save_global", "create_folder");
        Assert.Single(prompts);
        Assert.Equal(new[] { "library_global_save_global", "library_global_create_folder" }, fixture.Mutations());
        AssertAudit(fixture, reply, "user", "confirmed", "succeeded", "succeeded");
    }

    [Fact]
    public async Task ReadWrite_ContentOnly_NoPrompt_AuditNone()
    {
        var prompts = new List<string>();
        await using var fixture = await Fixture.StartAsync(McpAccessMode.ReadWrite, Options(prompts));

        var reply = await CallAsync(fixture, Folder());

        AssertApplied(reply, "create_folder");
        Assert.Empty(prompts);
        Assert.Equal(new[] { "library_global_create_folder" }, fixture.Mutations());
        AssertAudit(fixture, reply, "none", "not_requested", "succeeded");
    }

    [Fact]
    public async Task LocalSession_GlobalFolderPreview_PreservesExistingFixture()
    {
        var prompts = new List<string>();
        await using var fixture = await Fixture.StartAsync(McpAccessMode.ReadWrite, Options(prompts),
            scenario: "library-write-roundtrip.amc21");
        var before = fixture.Harness.WorkerClient.BindingSnapshot;
        var reply = await fixture.Harness.Client.CallToolAsync("library_write", new Dictionary<string, object?>
        {
            ["operations"] = new[] { Folder() }, ["dryRun"] = true
        });

        var root = Document(reply);
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("preview", root.GetProperty("phase").GetString());
        Assert.Empty(prompts);
        Assert.Empty(fixture.Mutations());
        Assert.Equal(new[] { "library_global_plan" }, fixture.Requests.Methods().Where(method => method.StartsWith("library_", StringComparison.Ordinal)));
        Assert.True(before.SameBinding(fixture.Harness.WorkerClient.BindingSnapshot));
        AssertAudit(fixture, reply, "none", "not_requested", "skipped");
    }

    [Fact]
    public async Task ReadWrite_Declined_NoMutation_AccessDenied()
    {
        var prompts = new List<string>();
        await using var fixture = await Fixture.StartAsync(McpAccessMode.ReadWrite, Options(prompts, "decline"));
        var before = fixture.Harness.WorkerClient.BindingSnapshot;

        var reply = await CallAsync(fixture, Lifecycle("close_global"));

        AssertRejected(reply, "access_denied", "blocked");
        Assert.Single(prompts);
        Assert.Empty(fixture.Mutations());
        Assert.True(before.SameBinding(fixture.Harness.WorkerClient.BindingSnapshot));
        AssertAudit(fixture, reply, "user", "declined", "skipped");
    }

    [Fact]
    public async Task Full_LifecycleCall_NoPrompt_AuditPolicy()
    {
        var prompts = new List<string>();
        await using var fixture = await Fixture.StartAsync(McpAccessMode.Full, Options(prompts, "decline"));

        var reply = await CallAsync(fixture, Lifecycle("close_global"));

        AssertApplied(reply, "close_global");
        Assert.Empty(prompts);
        Assert.Equal(new[] { "library_global_close_global" }, fixture.Mutations());
        AssertAudit(fixture, reply, "policy", "not_requested", "succeeded");
    }

    [Fact]
    public async Task ReadOnly_ToolAbsent()
    {
        await using var fixture = await Fixture.StartAsync(McpAccessMode.ReadOnly);

        Assert.DoesNotContain(await fixture.Harness.Client.ListToolsAsync(), tool => tool.Name == "library_write");
        Assert.Empty(fixture.Mutations());
        Assert.Empty(fixture.AuditLines());
    }

    [Fact]
    public async Task Unbound_BindingConflict()
    {
        using var uiOpen = new FakeWorkerUiOpenProject(null);
        await using var fixture = await Fixture.StartAsync(McpAccessMode.Full, bound: false);
        var before = fixture.Requests.Methods();

        var reply = await CallAsync(fixture, Folder());

        AssertRejected(reply, "binding_conflict", "error");
        Assert.Equal(before, fixture.Requests.Methods());
        AssertAudit(fixture, reply, "none", "not_requested", "skipped");
    }

    [Fact]
    public async Task UnknownMember_CanonicalValidationError_NoAudit()
    {
        await using var fixture = await Fixture.StartAsync(McpAccessMode.ReadWrite);
        var before = fixture.Requests.Methods();
        var reply = await fixture.Harness.Client.CallToolAsync("library_write", new Dictionary<string, object?>
        {
            ["operations"] = new[] { Folder() }, ["unknown"] = true
        });

        ArgumentRejectionAssert.IsCanonicalValidationError(reply, "library_write");
        Assert.Equal(before, fixture.Requests.Methods());
        Assert.Empty(fixture.AuditLines());
    }

    [Fact]
    public async Task MasterCopyExists_Blocked()
    {
        await using var fixture = await Fixture.StartAsync(McpAccessMode.Full);

        var reply = await CallAsync(fixture, Copy("Taken"));

        AssertRejected(reply, "guard_blocked", "blocked");
        var guard = Assert.Single(CanonicalToolDocument.Read(reply).GetProperty("guards").EnumerateArray());
        Assert.Equal("library_master_copy_exists", guard.GetProperty("id").GetString());
        Assert.Empty(fixture.Mutations());
        AssertAudit(fixture, reply, "none", "not_requested", "skipped");
    }

    [Fact]
    public async Task MissingLibrary_TargetNotFound_NoMutation()
    {
        await using var fixture = await Fixture.StartAsync(McpAccessMode.Full);
        var item = Folder();
        item["libraryName"] = "Missing";

        var reply = await CallAsync(fixture, item);

        AssertRejected(reply, "target_not_found", "error");
        Assert.Empty(fixture.Mutations());
        AssertAudit(fixture, reply, "none", "not_requested", "skipped");
    }

    [Fact]
    public async Task Renamed_PostconditionFailed_ReportsActualName()
    {
        await using var fixture = await Fixture.StartAsync(McpAccessMode.Full);

        var reply = await CallAsync(fixture, Copy("Renamed"), Folder());

        var item = AssertAttemptedFailure(reply, "postcondition_failed");
        Assert.Equal("Renamed_1", item.GetProperty("result").GetProperty("actualName").GetString());
        Assert.Equal(new[] { "library_global_copy_master_copy" }, fixture.Mutations());
        Assert.Equal("skipped", CanonicalToolDocument.Read(reply).GetProperty("batch").GetProperty("operations")[1].GetProperty("status").GetString());
        AssertAudit(fixture, reply, "policy", "not_requested", "failed", "skipped");
    }

    [Fact]
    public async Task MalformedOutcome_ProtocolError_NotEchoed()
    {
        await using var fixture = await Fixture.StartAsync(McpAccessMode.Full);

        var reply = await CallAsync(fixture, Copy("MalformedOutcome"));

        var item = AssertAttemptedFailure(reply, "protocol_error");
        Assert.Equal(JsonValueKind.Null, item.GetProperty("result").ValueKind);
        Assert.DoesNotContain("PRIVATE_LIBRARY_OUTCOME", CanonicalToolDocument.Read(reply).GetRawText());
        Assert.DoesNotContain("PRIVATE_LIBRARY_OUTCOME", Assert.Single(fixture.AuditLines()));
        Assert.Equal(new[] { "library_global_copy_master_copy" }, fixture.Mutations());
        AssertAudit(fixture, reply, "policy", "not_requested", "failed");
    }

    [Fact]
    public async Task PortalLost_InvalidatesBinding()
    {
        await using var fixture = await Fixture.StartAsync(McpAccessMode.Full, scenario: "library-write-portal-lost");
        var before = fixture.Harness.WorkerClient.BindingSnapshot;
        Assert.True(before.IsVerified);

        var reply = await CallAsync(fixture, Folder());

        AssertAttemptedFailure(reply, "portal_lost");
        Assert.False(fixture.Harness.WorkerClient.BindingSnapshot.IsVerified);
        Assert.Equal(ProjectBindingSnapshot.InvalidatedState, fixture.Harness.WorkerClient.BindingSnapshot.State);
        Assert.False(before.SameBinding(fixture.Harness.WorkerClient.BindingSnapshot));
        Assert.Equal(new[] { "library_global_create_folder" }, fixture.Mutations());
        AssertAudit(fixture, reply, "policy", "not_requested", "failed");
        var methods = fixture.Requests.Methods();
        var next = await CallAsync(fixture, Folder());
        AssertRejected(next, "binding_conflict", "error");
        Assert.Equal(methods, fixture.Requests.Methods());
    }

    private static Dictionary<string, object?> Folder() => new()
    {
        ["operationId"] = "folder", ["operation"] = "create_folder", ["scope"] = "global",
        ["libraryName"] = "Fixture", ["folderKind"] = "masterCopies", ["folderPath"] = Array.Empty<string>(), ["name"] = "Folder"
    };

    private static object Copy(string name) => new
    {
        operationId = "copy", operation = "copy_master_copy", scope = "global", libraryName = "Fixture",
        sourceScope = "global", sourceLibraryName = "Source", folderPath = Array.Empty<string>(),
        sourceFolderPath = Array.Empty<string>(), name
    };

    private static object Lifecycle(string operation) => operation switch
    {
        "create_global" => new { operationId = "life", operation, directory = "C:/Fixture", name = "Created" },
        "open_global" => new { operationId = "life", operation, filePath = "C:/Fixture/Library.al21", openMode = "readWrite" },
        "retrieve_global" => new { operationId = "life", operation, filePath = "C:/Fixture/Library.zal21", directory = "C:/Fixture", openMode = "readWrite" },
        "save_global" => new { operationId = "life", operation, scope = "global", libraryName = "Fixture" },
        "close_global" => new { operationId = "life", operation, scope = "global", libraryName = "Fixture", closeAction = "discard" },
        "archive_global" => new { operationId = "life", operation, scope = "global", libraryName = "Fixture", directory = "C:/Fixture", name = "Archive" },
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private static ValueTask<CallToolResult> CallAsync(Fixture fixture, params object[] items)
        => fixture.Harness.Client.CallToolAsync("library_write", new Dictionary<string, object?> { ["operations"] = items });

    private static McpClientOptions Options(List<string> prompts, string action = "accept") => new()
    {
        Capabilities = new ClientCapabilities { Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() } },
        Handlers = new McpClientHandlers
        {
            ElicitationHandler = (request, _) =>
            {
                Assert.NotNull(request);
                prompts.Add(request.Message);
                Assert.Contains("confirm", request.RequestedSchema!.Required!);
                return ValueTask.FromResult(new ElicitResult
                {
                    Action = action, Content = new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.SerializeToElement(true) }
                });
            }
        }
    };

    private static JsonElement Document(CallToolResult reply)
    {
        var root = CanonicalToolDocument.Read(reply);
        Assert.Equal("2.0", root.GetProperty("contractVersion").GetString());
        Assert.Empty(StructuredContractInspector.FindViolations(reply));
        return root;
    }

    private static void AssertApplied(CallToolResult reply, params string[] operations)
    {
        var root = Document(reply);
        Assert.False(reply.IsError == true);
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal("applied", root.GetProperty("phase").GetString());
        var items = root.GetProperty("batch").GetProperty("operations").EnumerateArray().ToArray();
        Assert.Equal(operations, items.Select(item => item.GetProperty("operation").GetString()));
        Assert.All(items, item => Assert.Equal("succeeded", item.GetProperty("status").GetString()));
        Assert.True(root.GetProperty("verification").GetProperty("success").GetBoolean());
    }

    private static void AssertRejected(CallToolResult reply, string category, string phase)
    {
        var root = Document(reply);
        Assert.True(reply.IsError == true);
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(phase, root.GetProperty("phase").GetString());
        Assert.Equal(category, root.GetProperty("error").GetProperty("category").GetString());
    }

    private static JsonElement AssertAttemptedFailure(CallToolResult reply, string category)
    {
        var root = Document(reply);
        Assert.False(reply.IsError == true);
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("applied", root.GetProperty("phase").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        var item = root.GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("failed", item.GetProperty("status").GetString());
        Assert.Equal(category, item.GetProperty("failure").GetProperty("category").GetString());
        return item;
    }

    private static void AssertAudit(Fixture fixture, CallToolResult reply, string by, string outcome, params string[] statuses)
    {
        using var record = JsonDocument.Parse(Assert.Single(fixture.AuditLines()));
        var root = record.RootElement;
        var text = Assert.Single(reply.Content.OfType<TextContentBlock>()).Text;
        Assert.Equal(text, root.GetProperty("responseText").GetString());
        Assert.Equal("sha256:" + ContentHashRules.Sha256Hex(text), root.GetProperty("responseHash").GetString());
        Assert.Equal(2, root.GetProperty("recordVersion").GetInt32());
        Assert.Equal("library_write", root.GetProperty("tool").GetString());
        Assert.Equal(by, root.GetProperty("confirmation").GetProperty("by").GetString());
        Assert.Equal(outcome, root.GetProperty("confirmation").GetProperty("outcome").GetString());
        Assert.Equal(statuses, root.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("status").GetString()));
        Assert.All(root.GetProperty("guards").EnumerateArray(), guard => Assert.Equal(JsonValueKind.Null, guard.GetProperty("satisfiedBy").ValueKind));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly TempAuditDirectory _audit = new();
        public FakeWorkerRequestLog Requests { get; private set; } = null!;
        public McpProtocolTestHarness Harness { get; private set; } = null!;

        public static async Task<Fixture> StartAsync(McpAccessMode mode, McpClientOptions? options = null,
            string scenario = "library-write-roundtrip", bool bound = true)
        {
            var fixture = new Fixture();
            Directory.CreateDirectory(fixture._audit.Path);
            fixture.Requests = new FakeWorkerRequestLog(fixture._audit.Path);
            try
            {
                fixture.Harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(mode, fixture._audit.Path,
                    bound ? scenario : null, clientOptions: options);
                if (bound)
                {
                    Assert.True(fixture.Harness.WorkerClient.BindingSnapshot.IsVerified);
                    Assert.Contains("hello", fixture.Requests.Methods());
                    Assert.Contains("get_project_status", fixture.Requests.Methods());
                }
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public string[] Mutations() => Requests.Methods().Where(method => LibraryWriteMethods.All.Contains(method, StringComparer.Ordinal)
            && method is not (LibraryWriteMethods.GlobalPlan or LibraryWriteMethods.ProjectPlan)).ToArray();
        public string[] AuditLines() => Directory.GetFiles(_audit.Path, "writes-*.jsonl").SelectMany(File.ReadLines).ToArray();

        public async ValueTask DisposeAsync()
        {
            if (Harness is not null) await Harness.DisposeAsync();
            Requests.Dispose();
            _audit.Dispose();
        }
    }
}
