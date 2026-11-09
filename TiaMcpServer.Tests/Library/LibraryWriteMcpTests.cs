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
    [InlineData(McpAccessMode.ReadWrite, false)]
    [InlineData(McpAccessMode.Full, false)]
    [InlineData(McpAccessMode.ReadWrite, true)]
    public async Task Mcp_MetadataOnly_NoPromptOrPlanning_AndLifecyclePromptsOnce(McpAccessMode mode, bool lifecycle)
    {
        var prompts = new List<string>();
        await using var fixture = await Fixture.StartAsync(mode, Options(prompts));
        var items = lifecycle ? new[] { Lifecycle("save_global"), Metadata("set_languages"), Metadata("set_comments") }
            : new[] { Metadata("set_languages"), Metadata("set_comments") };
        var reply = await CallAsync(fixture, items);
        AssertApplied(reply, lifecycle ? ["save_global", "set_languages", "set_comments"] : ["set_languages", "set_comments"]);
        Assert.Equal(lifecycle ? 1 : 0, prompts.Count);
        if (!lifecycle) Assert.DoesNotContain("library_global_plan", fixture.Requests.Methods());
        var comment = Document(reply).GetProperty("batch").GetProperty("operations")[lifecycle ? 2 : 1]
            .GetProperty("result").GetProperty("metadataChange").GetProperty("comments")[0];
        Assert.Equal("  exact\ntext  ", comment.GetProperty("after").GetString());
        Assert.Equal("pl-PL", comment.GetProperty("culture").GetString());
        AssertAudit(fixture, reply, lifecycle ? "user" : mode == McpAccessMode.Full ? "policy" : "none",
            lifecycle ? "confirmed" : "not_requested", lifecycle ? ["succeeded", "succeeded", "succeeded"] : ["succeeded", "succeeded"]);
    }

    [Fact]
    public async Task Mcp_MetadataDryRun_DependencyAndLocalSessionCapability()
    {
        await using var fixture = await Fixture.StartAsync(McpAccessMode.ReadWrite, scenario: "library-write-roundtrip.amc21");
        var reply = await fixture.Harness.Client.CallToolAsync("library_write", new Dictionary<string, object?>
            { ["operations"] = new[] { Metadata("set_languages"), Metadata("set_comments") }, ["dryRun"] = true });
        var root = Document(reply);
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal(1, root.GetProperty("effects")[1].GetProperty("effect").GetProperty("createdByItem").GetInt32());
        Assert.Empty(fixture.Mutations());
        Assert.True(fixture.Harness.WorkerClient.BindingSnapshot.IsVerified);
        AssertApplied(await CallAsync(fixture, Metadata("set_languages"), Metadata("set_comments")), "set_languages", "set_comments");
        Assert.Equal(new[] { "library_global_set_languages", "library_global_set_comments" }, fixture.Mutations());
    }

    [Theory]
    [InlineData("CommentsRefused", "set_comments", "worker_operation_failed", "EngineeringNotSupportedException")]
    [InlineData("LanguagesRefused", "set_languages", "engineering_rejected", "EngineeringTargetInvocationException")]
    [InlineData("PortalLost", "set_comments", "portal_lost", null)]
    [InlineData("MalformedMetadata", "set_comments", "protocol_error", null)]
    public async Task Mcp_MetadataFailure_StopsAndRetainsNativeEvidence(string library, string operation, string category, string? nativeType)
    {
        var prompts = new List<string>();
        await using var fixture = await Fixture.StartAsync(McpAccessMode.ReadWrite, Options(prompts));
        var item = Metadata(operation); item["libraryName"] = library;
        var reply = await CallAsync(fixture, item, Folder());
        var failure = AssertAttemptedFailure(reply, category);
        Assert.Equal("skipped", Document(reply).GetProperty("batch").GetProperty("operations")[1].GetProperty("status").GetString());
        Assert.Single(fixture.Mutations());
        Assert.Empty(prompts);
        Assert.DoesNotContain("library_global_plan", fixture.Requests.Methods());
        if (nativeType is not null) Assert.Equal(nativeType, failure.GetProperty("failure").GetProperty("engineeringError").GetProperty("type").GetString());
        if (category == "portal_lost") Assert.False(fixture.Harness.WorkerClient.BindingSnapshot.IsVerified);
        if (category == "protocol_error") Assert.DoesNotContain("PRIVATE_METADATA", Document(reply).GetRawText());
        AssertAudit(fixture, reply, "none", "not_requested", "failed", "skipped");
    }

    [Fact]
    public async Task Mcp_MetadataUnbound_RefusesBeforeWorker()
    {
        await using var fixture = await Fixture.StartAsync(McpAccessMode.ReadWrite, bound: false);
        AssertRejected(await CallAsync(fixture, Metadata("set_comments")), "binding_conflict", "error");
        Assert.Empty(fixture.Requests.Methods());
    }

    private static Dictionary<string, object?> Metadata(string operation) => operation == "set_comments"
        ? new() { ["operationId"] = operation, ["operation"] = operation, ["scope"] = "global", ["libraryName"] = "Fixture",
            ["comments"] = new[] { new { culture = "PL-pl", text = "  exact\ntext  " } } }
        : new() { ["operationId"] = operation, ["operation"] = operation, ["scope"] = "global", ["libraryName"] = "Fixture",
            ["activateLanguages"] = new[] { "PL-pl" }, ["editingLanguage"] = "pl-PL", ["referenceLanguage"] = "pl-PL" };

    [Theory]
    [MemberData(nameof(LibraryWriteMethodsTests.Authors), MemberType = typeof(LibraryWriteMethodsTests))]
    public async Task Mcp_AuthorAdmission_ValidatesBeforeWorker(string operation, string? author, bool valid)
    {
        await using var fixture = await Fixture.StartAsync(McpAccessMode.Full);
        var before = fixture.Requests.Methods();
        var item = LibraryWriteMethodsTests.AuthorRequest(operation, author);
        item.OperationId = "author";
        var reply = await CallAsync(fixture, item);
        if (valid)
        {
            AssertApplied(reply, operation);
            Assert.Equal(new[] { LibraryWriteMethods.ForItem(item) }, fixture.Mutations());
            return;
        }
        AssertRejected(reply, "validation_error", "error");
        Assert.Equal(before, fixture.Requests.Methods());
        AssertAudit(fixture, reply, "none", "not_requested", "skipped");
        if (author?.Length > 255)
        {
            var text = Document(reply).GetProperty("error").GetRawText();
            Assert.Contains("author", text);
            Assert.Contains("255", text);
            Assert.Contains(author.Length.ToString(), text);
            Assert.DoesNotContain(author, text);
        }
    }

    [Theory]
    [InlineData(McpAccessMode.ReadWrite, null, false)]
    [InlineData(McpAccessMode.ReadWrite, "  First\nSecond\r\nThird 😀  ", false)]
    [InlineData(McpAccessMode.Full, null, false)]
    [InlineData(McpAccessMode.Full, "  First\nSecond\r\nThird 😀  ", false)]
    [InlineData(McpAccessMode.ReadWrite, "Author", true)]
    [InlineData(McpAccessMode.Full, "Author", true)]
    public async Task Mcp_CreateGlobal_AuthorMetadataConfirmationAndPreview(McpAccessMode mode, string? author, bool dryRun)
    {
        var prompts = new List<string>();
        await using var fixture = await Fixture.StartAsync(mode, Options(prompts));
        var item = LibraryWriteMethodsTests.AuthorRequest("create_global", author);
        item.OperationId = "create";
        var reply = await fixture.Harness.Client.CallToolAsync("library_write", new Dictionary<string, object?>
        {
            ["operations"] = new[] { item }, ["dryRun"] = dryRun
        });
        if (dryRun)
        {
            Assert.True(Document(reply).GetProperty("success").GetBoolean());
            Assert.Equal("preview", Document(reply).GetProperty("phase").GetString());
            Assert.Empty(fixture.Mutations());
            Assert.DoesNotContain("metadata", Document(reply).GetRawText());
        }
        else
        {
            AssertApplied(reply, "create_global");
            var metadata = Document(reply).GetProperty("batch").GetProperty("operations")[0].GetProperty("result").GetProperty("metadata");
            Assert.Equal(author ?? "Native default", metadata.GetProperty("author").GetString());
            Assert.True(metadata.GetProperty("complete").GetBoolean());
        }
        if (mode == McpAccessMode.ReadWrite && !dryRun)
        {
            var prompt = Assert.Single(prompts);
            if (author is null) Assert.DoesNotContain("author", prompt);
            else Assert.Contains("author '" + author + "'", prompt);
        }
        else Assert.Empty(prompts);
    }

    [Fact]
    public async Task Mcp_CreateTypeThenRelease_OneCall_TwoItemsApplied()
    {
        await using var fixture = await Fixture.StartAsync(McpAccessMode.Full);
        var release = TypeOperation("release_type_version");
        release["typeName"] = "CreatedType";

        var reply = await CallAsync(fixture, TypeOperation("create_type"), release);

        AssertApplied(reply, "create_type", "release_type_version");
        Assert.Equal(new[] { "library_project_create_type", "library_project_release_type_version" }, fixture.Mutations());
        Assert.DoesNotContain("library_project_plan", fixture.Requests.Methods());
        var items = Document(reply).GetProperty("batch").GetProperty("operations");
        Assert.Equal("CreatedType", items[0].GetProperty("result").GetProperty("type").GetProperty("typeName").GetString());
        var released = items[1].GetProperty("result").GetProperty("type");
        Assert.Equal("CreatedType", released.GetProperty("typeName").GetString());
        Assert.Equal("1.0.0", released.GetProperty("versionNumber").GetString());
        Assert.Equal("Committed", released.GetProperty("versionState").GetString());
        AssertAudit(fixture, reply, "policy", "not_requested", "succeeded", "succeeded");
    }

    [Theory]
    [InlineData("create_type")]
    [InlineData("create_type_version")]
    [InlineData("release_type_version")]
    [InlineData("set_default_type_version")]
    [InlineData("synchronize_type")]
    [InlineData("instantiate_type_version")]
    [InlineData("delete_type")]
    [InlineData("delete_type_version")]
    public async Task Mcp_TypeOperations_NeverPrompt_ReadWrite(string operation)
    {
        var prompts = new List<string>();
        await using var fixture = await Fixture.StartAsync(McpAccessMode.ReadWrite, Options(prompts, "decline"));

        var reply = await CallAsync(fixture, TypeOperation(operation));

        AssertApplied(reply, operation);
        Assert.Empty(prompts);
        Assert.Equal(new[] { "library_project_" + operation }, fixture.Mutations());
        Assert.DoesNotContain("library_project_plan", fixture.Requests.Methods());
        Assert.True(Document(reply).GetProperty("batch").GetProperty("operations")[0].GetProperty("result").TryGetProperty("type", out _));
        AssertAudit(fixture, reply, "none", "not_requested", "succeeded");
    }

    [Fact]
    public async Task Mcp_DryRun_ShowsCreatedByItem()
    {
        var prompts = new List<string>();
        await using var fixture = await Fixture.StartAsync(McpAccessMode.ReadWrite, Options(prompts));
        var release = TypeOperation("release_type_version");
        release["typeName"] = "CreatedType";

        var reply = await fixture.Harness.Client.CallToolAsync("library_write", new Dictionary<string, object?>
        {
            ["operations"] = new[] { TypeOperation("create_type"), release }, ["dryRun"] = true
        });

        var root = Document(reply);
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal("preview", root.GetProperty("phase").GetString());
        var effects = root.GetProperty("effects");
        Assert.Equal(2, effects.GetArrayLength());
        Assert.False(effects[0].GetProperty("effect").TryGetProperty("createdByItem", out _));
        Assert.Equal(1, effects[1].GetProperty("effect").GetProperty("createdByItem").GetInt32());
        Assert.Empty(prompts);
        Assert.Empty(fixture.Mutations());
        Assert.Equal(new[] { "library_project_plan", "library_project_plan" },
            fixture.Requests.Methods().Where(method => method.StartsWith("library_", StringComparison.Ordinal)));
        AssertAudit(fixture, reply, "none", "not_requested", "skipped", "skipped");

        AssertAttemptedFailure(await CallAsync(fixture, release), "target_not_found");
    }

    [Fact]
    public async Task Mcp_CreateTypeVersion_StaleHash_StateChanged()
    {
        await using var fixture = await Fixture.StartAsync(McpAccessMode.ReadWrite);
        var item = TypeOperation("create_type_version");
        item["expectedContentHash"] = ContentHashRules.Compute("xml", "<Stale />");

        var reply = await CallAsync(fixture, item, Folder());

        var failure = AssertAttemptedFailure(reply, "state_changed");
        Assert.Equal(JsonValueKind.Null, failure.GetProperty("result").ValueKind);
        Assert.Equal(new[] { "library_project_create_type_version" }, fixture.Mutations());
        Assert.Equal("skipped", Document(reply).GetProperty("batch").GetProperty("operations")[1].GetProperty("status").GetString());
        AssertAudit(fixture, reply, "none", "not_requested", "failed", "skipped");
    }

    [Fact]
    public async Task Mcp_ApplyResponse_HasNoEffects()
    {
        await using var fixture = await Fixture.StartAsync(McpAccessMode.Full);

        var reply = await CallAsync(fixture, TypeOperation("create_type"));

        AssertApplied(reply, "create_type");
        Assert.Empty(Document(reply).GetProperty("effects").EnumerateArray());
        Assert.DoesNotContain("library_project_plan", fixture.Requests.Methods());
    }

    [Fact]
    public async Task Mcp_ToolDescription_ListsTwentyTwoOperations()
    {
        await using var fixture = await Fixture.StartAsync(McpAccessMode.ReadWrite);
        var tool = Assert.Single(await fixture.Harness.Client.ListToolsAsync(), tool => tool.Name == "library_write");
        var description = tool.Description!;
        var operations = description[(description.IndexOf("writes: ", StringComparison.Ordinal) + "writes: ".Length)..]
            .Split(". Every", StringSplitOptions.None)[0].Split(", ", StringSplitOptions.None);

        Assert.Equal(22, operations.Length);
        Assert.Equal(LibraryWriteMethods.Operations, operations);
        Assert.DoesNotContain("edit_type_version", description);
    }

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
    public async Task Apply_MasterCopyCollisionAfterEarlierItems_StopsWithGuardBlocked()
    {
        await using var fixture = await Fixture.StartAsync(McpAccessMode.Full);

        var reply = await CallAsync(fixture, Folder(), Copy("Taken"), Lifecycle("save_global"));

        var root = Document(reply);
        Assert.False(reply.IsError);
        Assert.Equal("guard_blocked", root.GetProperty("batch").GetProperty("operations")[1].GetProperty("failure").GetProperty("category").GetString());
        Assert.Empty(root.GetProperty("effects").EnumerateArray());
        Assert.Equal(new[] { "library_global_create_folder", "library_global_copy_master_copy" }, fixture.Mutations());
        AssertAudit(fixture, reply, "policy", "not_requested", "succeeded", "failed", "skipped");
    }

    [Fact]
    public async Task MissingLibrary_TargetNotFound_NoMutation()
    {
        await using var fixture = await Fixture.StartAsync(McpAccessMode.Full);
        var item = Folder();
        item["libraryName"] = "Missing";

        var reply = await CallAsync(fixture, item);

        AssertAttemptedFailure(reply, "target_not_found");
        Assert.Equal(new[] { "library_global_create_folder" }, fixture.Mutations());
        AssertAudit(fixture, reply, "policy", "not_requested", "failed");
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

    private static Dictionary<string, object?> TypeOperation(string operation)
    {
        var item = new Dictionary<string, object?> { ["operationId"] = operation, ["operation"] = operation };
        if (operation is "create_type" or "create_type_version")
        {
            item["plcName"] = "PLC";
            item["plcKind"] = "block";
            item["plcObjectName"] = operation == "create_type" ? "CreatedType" : "FixtureType";
            if (operation == "create_type_version")
            {
                item["content"] = "<Document />";
                item["expectedContentHash"] = ContentHashRules.Compute("xml", "<Document />");
            }
            return item;
        }
        item["typeName"] = "FixtureType";
        if (operation is "synchronize_type" or "instantiate_type_version") item["sourceScope"] = "project";
        if (operation != "instantiate_type_version") item["scope"] = "project";
        if (operation == "instantiate_type_version") item["plcName"] = "PLC";
        if (operation is "release_type_version" or "set_default_type_version" or "delete_type_version") item["versionNumber"] = "1.0.0";
        return item;
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
