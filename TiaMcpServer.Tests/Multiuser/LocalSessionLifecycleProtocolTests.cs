using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Multiuser;

[Collection("Mcp protocol serial")]
public sealed class LocalSessionLifecycleProtocolTests
{
    private const string Amc = "C:/Projects/Local.amc21";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternallyClosedLocalSource_ForcedOpenRecoversOnlyAttachedEmptyPortal(bool dryRun)
    {
        const string source = "C:/Projects/discovery-project-closed.amc21";
        using var portals = new FakeWorkerPortals(
            new FakeWorkerPortals.Entry(42, source), new FakeWorkerPortals.Entry(43, "C:/Projects/Other.amc21"));
        using var ui = new FakeWorkerUiOpenProject(source);
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        var input = Path.Combine(audit.Path, "local-session-open.als21");
        File.WriteAllText(input, "offline fixture");
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path);
        var adopted = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?> { ["projectPath"] = source });
        Assert.True(adopted.StructuredContent!.Value.GetProperty("success").GetBoolean());
        var verified = harness.WorkerClient.BindingSnapshot;
        await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?> { ["action"] = "list_portals" });
        Assert.Equal(ProjectBindingSnapshot.InvalidatedState, harness.WorkerClient.BindingSnapshot.State);

        var opened = await harness.Client.CallToolAsync("open_project", new Dictionary<string, object?>
        {
            ["projectPath"] = input, ["forceRebind"] = true, ["dryRun"] = dryRun
        });

        var document = opened.StructuredContent!.Value;
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal(dryRun ? "preview" : "applied", document.GetProperty("phase").GetString());
        Assert.False(verified.SameBinding(harness.WorkerClient.BindingSnapshot));
        Assert.Equal(dryRun ? 0 : 1, log.Methods().Count(method => method == "open_project"));
        Assert.Equal(1, log.Methods().Count(method => method == "select_portal_project"));
        Assert.DoesNotContain(log.Methods(), method => method is "close_project" or "save_project");
        if (dryRun) Assert.False(harness.WorkerClient.BindingSnapshot.IsVerified);
        else Assert.Equal(42, harness.WorkerClient.BindingSnapshot.PortalProcessId);
        Assert.Single(File.ReadAllLines(Assert.Single(Directory.GetFiles(audit.Path, "*.jsonl", SearchOption.AllDirectories))));
    }

    [Theory]
    [InlineData("owner", false)]
    [InlineData("missing", false)]
    [InlineData("worker", false)]
    [InlineData("portal", false)]
    [InlineData("generation", false)]
    [InlineData("malformed", false)]
    [InlineData("context", false)]
    [InlineData("owner", true)]
    [InlineData("generation", true)]
    public async Task EmptyPortalRecovery_UnprovedOrChangedStatusCannotOpen(string fault, bool afterPreview)
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        var source = Path.Combine(audit.Path, "discovery-project-closed.amc21");
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, source),
            new FakeWorkerPortals.Entry(43, "C:/Projects/Other.amc21"));
        using var ui = new FakeWorkerUiOpenProject(source);
        using var log = new FakeWorkerRequestLog(audit.Path);
        var input = Path.Combine(audit.Path, "local-session-open.als21");
        File.WriteAllText(input, "offline fixture");
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path);
        var adopted = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?> { ["projectPath"] = source });
        Assert.True(adopted.StructuredContent!.Value.GetProperty("success").GetBoolean());
        await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?> { ["action"] = "list_portals" });
        var arguments = new Dictionary<string, object?> { ["projectPath"] = input, ["forceRebind"] = true, ["dryRun"] = true };
        if (afterPreview)
        {
            var preview = await harness.Client.CallToolAsync("open_project", arguments);
            Assert.True(preview.StructuredContent!.Value.GetProperty("success").GetBoolean(), preview.StructuredContent.Value.GetRawText());
        }
        File.WriteAllText(source + ".recovery", fault);
        arguments["dryRun"] = !afterPreview;

        var result = await harness.Client.CallToolAsync("open_project", arguments);

        Assert.False(result.StructuredContent!.Value.GetProperty("success").GetBoolean());
        Assert.False(harness.WorkerClient.BindingSnapshot.IsVerified);
        Assert.DoesNotContain("open_project", log.Methods());
        Assert.Equal(1, log.Methods().Count(method => method == "select_portal_project"));
    }

    [Fact]
    public async Task ExternallyClosedLocalSource_NoForcePreservesInvalidatedBindingWithoutStatusOrOpener()
    {
        const string source = "C:/Projects/discovery-project-closed.amc21";
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, source));
        using var ui = new FakeWorkerUiOpenProject(source);
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        var input = Path.Combine(audit.Path, "local-session-open.als21");
        File.WriteAllText(input, "offline fixture");
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path);
        await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?> { ["projectPath"] = source });
        await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?> { ["action"] = "list_portals" });
        var before = harness.WorkerClient.BindingSnapshot;
        var requests = log.Methods().Length;
        var result = await harness.Client.CallToolAsync("open_project", new Dictionary<string, object?> { ["projectPath"] = input, ["dryRun"] = true });
        Assert.False(result.StructuredContent!.Value.GetProperty("success").GetBoolean());
        Assert.True(before.SameBinding(harness.WorkerClient.BindingSnapshot));
        Assert.Equal(requests, log.Methods().Length);
    }

    [Theory]
    [InlineData("open-worker")]
    [InlineData("open-portal")]
    [InlineData("open-generation")]
    [InlineData("open-missing")]
    public async Task EmptyPortalRecovery_CompletedOpenerMustPreserveProvedAttachment(string fault)
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        var source = Path.Combine(audit.Path, "discovery-project-closed.amc21");
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, source));
        using var ui = new FakeWorkerUiOpenProject(source);
        using var log = new FakeWorkerRequestLog(audit.Path);
        var input = Path.Combine(audit.Path, "local-session-open.als21");
        File.WriteAllText(input, "offline fixture");
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path);
        await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?> { ["projectPath"] = source });
        await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?> { ["action"] = "list_portals" });
        File.WriteAllText(source + ".recovery", fault);

        var result = await harness.Client.CallToolAsync("open_project", new Dictionary<string, object?>
        {
            ["projectPath"] = input, ["forceRebind"] = true
        });

        var document = result.StructuredContent!.Value;
        Assert.False(document.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("error").ValueKind);
        Assert.Equal(JsonValueKind.Object, document.GetProperty("result").ValueKind);
        Assert.False(result.IsError == true);
        Assert.Equal(ProjectBindingSnapshot.InvalidatedState, harness.WorkerClient.BindingSnapshot.State);
        Assert.Equal(1, log.Methods().Count(method => method == "open_project"));
        Assert.Equal(1, log.Methods().Count(method => method == "select_portal_project"));
        Assert.DoesNotContain(log.Methods(), method => method is "close_project" or "save_project");
        Assert.Single(File.ReadAllLines(Assert.Single(Directory.GetFiles(audit.Path, "*.jsonl", SearchOption.AllDirectories))));
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("directory")]
    [InlineData("unknown-extension")]
    [InlineData("missing-file")]
    [InlineData("amc-owner")]
    public async Task AlsOpen_RejectsNonFileOrNonOpenerInputsBeforeDispatch(string kind)
    {
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, null));
        using var ui = new FakeWorkerUiOpenProject(null);
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        var input = kind switch
        {
            "relative" => "relative.als21",
            "directory" => audit.Path,
            "unknown-extension" => Path.Combine(audit.Path, "Unknown.txt"),
            "missing-file" => Path.Combine(audit.Path, "Missing.als21"),
            _ => Path.Combine(audit.Path, "Owner.amc21")
        };
        if (kind is "unknown-extension" or "amc-owner") File.WriteAllText(input, "offline fixture");
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path);

        var result = await harness.Client.CallToolAsync("open_project", new Dictionary<string, object?>
        {
            ["projectPath"] = input
        });
        var document = result.StructuredContent!.Value;
        Assert.False(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal(WorkerFailureCategories.ValidationError,
            document.GetProperty("error").GetProperty("category").GetString());
        Assert.DoesNotContain("open_project", log.Methods());
        Assert.False(harness.WorkerClient.BindingSnapshot.IsVerified);
    }

    [Theory]
    [InlineData(".ap21")]
    [InlineData(".als21")]
    public async Task BorrowedLocalSource_DifferentDestinationBlocksBeforeOpen(string extension)
    {
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, Amc));
        using var ui = new FakeWorkerUiOpenProject(Amc);
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        var destination = Path.Combine(audit.Path, "Destination" + extension);
        File.WriteAllText(destination, "offline fixture");
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path);
        var bound = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?> { ["projectPath"] = Amc });
        Assert.True(bound.StructuredContent!.Value.GetProperty("success").GetBoolean(), bound.StructuredContent!.Value.GetRawText());

        var arguments = new Dictionary<string, object?>
        {
            ["projectPath"] = destination, ["forceRebind"] = true, ["dryRun"] = true
        };
        var preview = await harness.Client.CallToolAsync("open_project", arguments);
        var previewDocument = preview.StructuredContent!.Value;
        Assert.True(previewDocument.GetProperty("success").GetBoolean(), previewDocument.GetRawText());
        Assert.Equal("preview", previewDocument.GetProperty("phase").GetString());
        Assert.Contains(previewDocument.GetProperty("guards").EnumerateArray(), guard =>
            guard.GetProperty("id").GetString() == "local_session_source_preservation_unproved");
        Assert.Equal(JsonValueKind.Object, previewDocument.GetProperty("effects").GetProperty("sourceContext").ValueKind);
        Assert.DoesNotContain("open_project", log.Methods());

        arguments["dryRun"] = false;
        var actual = await harness.Client.CallToolAsync("open_project", arguments);
        var document = actual.StructuredContent!.Value;
        Assert.False(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal("blocked", document.GetProperty("phase").GetString());
        Assert.Equal(WorkerFailureCategories.GuardBlocked, document.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains(document.GetProperty("guards").EnumerateArray(), guard =>
            guard.GetProperty("id").GetString() == "local_session_source_preservation_unproved");
        Assert.DoesNotContain("open_project", log.Methods());
        var auditRecords = File.ReadAllLines(Assert.Single(Directory.GetFiles(audit.Path, "*.jsonl", SearchOption.AllDirectories)));
        Assert.Equal(2, auditRecords.Length);
    }

    [Fact]
    public async Task FullAlsOpen_ReturnsTypedAmcIdentityAndExactAlsProvenance()
    {
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, null));
        using var ui = new FakeWorkerUiOpenProject(null);
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        var input = Path.Combine(audit.Path, "local-session-open.ALS21");
        File.WriteAllText(input, "offline fixture");
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path);

        var result = await harness.Client.CallToolAsync("open_project", new Dictionary<string, object?>
        {
            ["projectPath"] = input
        });
        var document = result.StructuredContent!.Value;
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal("applied", document.GetProperty("phase").GetString());
        var project = document.GetProperty("result").GetProperty("value").GetProperty("project");
        Assert.Equal(ProjectPathNormalization.Canonicalize(Amc), project.GetProperty("path").GetString());
        Assert.Equal(ProjectPathNormalization.Canonicalize(input),
            project.GetProperty("context").GetProperty("sessionContainerPath").GetString());
        Assert.Equal(ProjectPathNormalization.Canonicalize(Amc), harness.WorkerClient.BindingSnapshot.ProjectPath);
        Assert.Equal(1, log.Methods().Count(method => method == "open_project"));
        Assert.Single(Directory.GetFiles(audit.Path, "*.jsonl", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task AlsDryRun_NeverOpensOrElicitsAndAuditsOnce()
    {
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, null));
        using var ui = new FakeWorkerUiOpenProject(null);
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        var input = Path.Combine(audit.Path, "local-session-open.ALS21");
        File.WriteAllText(input, "offline fixture");
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path);

        var result = await harness.Client.CallToolAsync("open_project", new Dictionary<string, object?>
        {
            ["projectPath"] = input, ["dryRun"] = true
        });
        var document = result.StructuredContent!.Value;
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal("preview", document.GetProperty("phase").GetString());
        Assert.Equal(ProjectPathNormalization.Canonicalize(input),
            document.GetProperty("effects").GetProperty("destinationProjectPath").GetString());
        Assert.DoesNotContain("open_project", log.Methods());
        Assert.Single(File.ReadAllLines(Assert.Single(Directory.GetFiles(audit.Path, "*.jsonl", SearchOption.AllDirectories))));
    }

    [Theory]
    [InlineData("accept", true, true)]
    [InlineData("decline", true, false)]
    [InlineData("cancel", true, false)]
    [InlineData("accept", false, false)]
    public async Task ReadWriteAlsOpen_RequiresOneAcceptedBooleanConfirmation(string action, bool confirm, bool applies)
    {
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, null));
        using var ui = new FakeWorkerUiOpenProject(null);
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        var input = Path.Combine(audit.Path, "local-session-open.ALS21");
        File.WriteAllText(input, "offline fixture");
        var prompts = 0;
        var options = new McpClientOptions
        {
            Capabilities = new ClientCapabilities { Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() } },
            Handlers = new McpClientHandlers
            {
                ElicitationHandler = (_, _) =>
                {
                    prompts++;
                    return ValueTask.FromResult(new ElicitResult
                    {
                        Action = action,
                        Content = new Dictionary<string, JsonElement>
                        {
                            ["confirm"] = JsonSerializer.SerializeToElement(confirm)
                        }
                    });
                }
            }
        };
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(
            McpAccessMode.ReadWrite, audit.Path, clientOptions: options);

        var result = await harness.Client.CallToolAsync("open_project", new Dictionary<string, object?>
        {
            ["projectPath"] = input
        });
        var document = result.StructuredContent!.Value;
        Assert.Equal(1, prompts);
        Assert.Equal(applies, document.GetProperty("success").GetBoolean());
        Assert.Equal(applies ? "applied" : "blocked", document.GetProperty("phase").GetString());
        Assert.Equal(applies ? 1 : 0, log.Methods().Count(method => method == "open_project"));
        Assert.Single(File.ReadAllLines(Assert.Single(Directory.GetFiles(audit.Path, "*.jsonl", SearchOption.AllDirectories))));
    }

    [Theory]
    [InlineData(".ap21")]
    [InlineData(".als21")]
    public async Task WorkerOpenedLocalSource_DifferentDestinationRequiresTerminalOperation(string extension)
    {
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, null));
        using var ui = new FakeWorkerUiOpenProject(null);
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        var input = Path.Combine(audit.Path, "local-session-open.als21");
        var destination = Path.Combine(audit.Path, "Different" + extension);
        File.WriteAllText(input, "offline fixture");
        File.WriteAllText(destination, "offline fixture");
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path);
        var opened = await harness.Client.CallToolAsync("open_project", new Dictionary<string, object?> { ["projectPath"] = input });
        Assert.True(opened.StructuredContent!.Value.GetProperty("success").GetBoolean(), opened.StructuredContent.Value.GetRawText());

        var blocked = await harness.Client.CallToolAsync("open_project", new Dictionary<string, object?>
        {
            ["projectPath"] = destination, ["forceRebind"] = true
        });
        var document = blocked.StructuredContent!.Value;
        Assert.False(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal("blocked", document.GetProperty("phase").GetString());
        Assert.Contains(document.GetProperty("guards").EnumerateArray(), guard =>
            guard.GetProperty("id").GetString() == "local_session_requires_terminal_operation");
        Assert.Equal(1, log.Methods().Count(method => method == "open_project"));
        Assert.Equal(ProjectPathNormalization.Canonicalize(Amc), harness.WorkerClient.BindingSnapshot.ProjectPath);
    }

    [Fact]
    public async Task VerifiedSameAlsReuse_PreservesOwnerBindingAndProvenance()
    {
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, null));
        using var ui = new FakeWorkerUiOpenProject(null);
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        var input = Path.Combine(audit.Path, "local-session-open.als21");
        File.WriteAllText(input, "offline fixture");
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path);
        var first = await harness.Client.CallToolAsync("open_project", new Dictionary<string, object?> { ["projectPath"] = input });
        Assert.True(first.StructuredContent!.Value.GetProperty("success").GetBoolean(), first.StructuredContent.Value.GetRawText());
        var before = harness.WorkerClient.BindingSnapshot;

        var second = await harness.Client.CallToolAsync("open_project", new Dictionary<string, object?> { ["projectPath"] = input });
        Assert.True(second.StructuredContent!.Value.GetProperty("success").GetBoolean(), second.StructuredContent.Value.GetRawText());
        var after = harness.WorkerClient.BindingSnapshot;
        Assert.Equal(before.BindingId, after.BindingId);
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(before.SessionGeneration, after.SessionGeneration);
        Assert.True(after.Context!.OpenedByWorker);
        Assert.Equal(ProjectPathNormalization.Canonicalize(input), after.Context.SessionContainerPath);
    }

    [Fact]
    public async Task ReadWriteKnownSameAlsReuse_PromptsAndAuditsEachActualCallWithoutOwnerChurn()
    {
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, null));
        using var ui = new FakeWorkerUiOpenProject(null);
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        var input = Path.Combine(audit.Path, "local-session-open.als21");
        File.WriteAllText(input, "offline fixture");
        var prompts = 0;
        var options = new McpClientOptions
        {
            Capabilities = new ClientCapabilities { Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() } },
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
            McpAccessMode.ReadWrite, audit.Path, clientOptions: options);
        var arguments = new Dictionary<string, object?> { ["projectPath"] = input };
        var first = await harness.Client.CallToolAsync("open_project", arguments);
        Assert.True(first.StructuredContent!.Value.GetProperty("success").GetBoolean(), first.StructuredContent.Value.GetRawText());
        var before = harness.WorkerClient.BindingSnapshot;

        var second = await harness.Client.CallToolAsync("open_project", arguments);
        Assert.True(second.StructuredContent!.Value.GetProperty("success").GetBoolean(), second.StructuredContent.Value.GetRawText());
        var after = harness.WorkerClient.BindingSnapshot;
        Assert.Equal(2, prompts);
        Assert.Equal(2, log.Methods().Count(method => method == "open_project"));
        Assert.Equal(2, File.ReadAllLines(Assert.Single(Directory.GetFiles(audit.Path, "*.jsonl", SearchOption.AllDirectories))).Length);
        Assert.Equal(before.BindingId, after.BindingId);
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(before.SessionGeneration, after.SessionGeneration);
        Assert.True(after.Context!.OpenedByWorker);
        Assert.Equal(ProjectPathNormalization.Canonicalize(input), after.Context.SessionContainerPath);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("consequence")]
    public async Task ReadWriteAlsReuse_ChangedLocalStateDuringConfirmationBlocksBeforeSecondOpen(string drift)
    {
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, null));
        using var ui = new FakeWorkerUiOpenProject(null);
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        var input = Path.Combine(audit.Path, "local-session-open.als21");
        File.WriteAllText(input, "offline fixture");
        var prompts = 0;
        var options = new McpClientOptions
        {
            Capabilities = new ClientCapabilities { Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() } },
            Handlers = new McpClientHandlers
            {
                ElicitationHandler = (_, _) =>
                {
                    if (++prompts == 2) File.WriteAllText(input + ".drift", drift);
                    return ValueTask.FromResult(new ElicitResult
                    {
                        Action = "accept",
                        Content = new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.SerializeToElement(true) }
                    });
                }
            }
        };
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(
            McpAccessMode.ReadWrite, audit.Path, clientOptions: options);
        var arguments = new Dictionary<string, object?> { ["projectPath"] = input };
        var first = await harness.Client.CallToolAsync("open_project", arguments);
        Assert.True(first.StructuredContent!.Value.GetProperty("success").GetBoolean(), first.StructuredContent.Value.GetRawText());

        var second = await harness.Client.CallToolAsync("open_project", arguments);
        var document = second.StructuredContent!.Value;
        Assert.False(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal("blocked", document.GetProperty("phase").GetString());
        Assert.Equal(WorkerFailureCategories.BindingConflict,
            document.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(2, prompts);
        Assert.Equal(1, log.Methods().Count(method => method == "open_project"));
        Assert.Equal(2, File.ReadAllLines(Assert.Single(Directory.GetFiles(audit.Path, "*.jsonl", SearchOption.AllDirectories))).Length);
    }

    [Fact]
    public async Task MalformedPostOpenResult_ReportsPossibleMutationWithoutReplay()
    {
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, null));
        using var ui = new FakeWorkerUiOpenProject(null);
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        var input = Path.Combine(audit.Path, "local-session-open-bad-result.als21");
        File.WriteAllText(input, "offline fixture");
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path);

        var result = await harness.Client.CallToolAsync("open_project", new Dictionary<string, object?> { ["projectPath"] = input });
        var document = result.StructuredContent!.Value;
        Assert.False(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal("applied", document.GetProperty("phase").GetString());
        Assert.Equal(WorkerFailureCategories.ProtocolError,
            document.GetProperty("result").GetProperty("failure").GetProperty("category").GetString());
        Assert.DoesNotContain("PRIVATE_MUTATION_RESULT_MEMBER", document.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE_MUTATION_RESULT_VALUE", document.GetRawText(), StringComparison.Ordinal);
        Assert.Contains(document.GetProperty("warnings").EnumerateArray(), warning =>
            warning.GetString()!.Contains("Inspect", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, log.Methods().Count(method => method == "open_project"));
        Assert.Single(File.ReadAllLines(Assert.Single(Directory.GetFiles(audit.Path, "*.jsonl", SearchOption.AllDirectories))));
    }

    [Theory]
    [InlineData(WorkerFailureCategories.WorkerOperationFailed)]
    [InlineData(WorkerFailureCategories.PostconditionFailed)]
    [InlineData(WorkerFailureCategories.GuardBlocked)]
    public async Task SentAlsOpener_CompletedFailureInvalidatesVerifiedSource(string category)
    {
        const string source = "C:/Projects/guarded-lifecycle-ui-owned.ap21";
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, source));
        using var ui = new FakeWorkerUiOpenProject(source);
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        var input = Path.Combine(audit.Path, "local-session-open-failed-" + category + ".als21");
        File.WriteAllText(input, "offline fixture");
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path);
        var bound = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?> { ["projectPath"] = source });
        Assert.True(bound.StructuredContent!.Value.GetProperty("success").GetBoolean());
        var before = harness.WorkerClient.BindingSnapshot;
        Assert.True(before.IsVerified);

        var call = await harness.Client.CallToolAsync("open_project", new Dictionary<string, object?>
        {
            ["projectPath"] = input, ["forceRebind"] = true
        });
        var document = call.StructuredContent!.Value;
        Assert.False(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal("applied", document.GetProperty("phase").GetString());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("error").ValueKind);
        Assert.Equal(category, document.GetProperty("result").GetProperty("failure").GetProperty("category").GetString());
        Assert.Contains(document.GetProperty("warnings").EnumerateArray(), warning =>
            warning.GetString()!.Contains("Inspect", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ProjectBindingSnapshot.InvalidatedState, harness.WorkerClient.BindingSnapshot.State);
        Assert.False(before.SameBinding(harness.WorkerClient.BindingSnapshot));
        Assert.True(harness.WorkerClient.BindingSnapshot.Revision > before.Revision);
        Assert.Equal(1, log.Methods().Count(method => method == "open_project"));
        Assert.DoesNotContain(log.Methods(), method => method is "save_project" or "close_project");
        Assert.Single(File.ReadAllLines(Assert.Single(Directory.GetFiles(audit.Path, "*.jsonl", SearchOption.AllDirectories))));
    }

    [Fact]
    public async Task SentAlsOpener_MalformedEnvelopeIdentityInvalidatesVerifiedSource()
    {
        const string source = "C:/Projects/guarded-lifecycle-ui-owned.ap21";
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, source));
        using var ui = new FakeWorkerUiOpenProject(source);
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        var input = Path.Combine(audit.Path, "local-session-open-malformed-identity.als21");
        File.WriteAllText(input, "offline fixture");
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path);
        var bound = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?> { ["projectPath"] = source });
        Assert.True(bound.StructuredContent!.Value.GetProperty("success").GetBoolean());
        Assert.True(harness.WorkerClient.BindingSnapshot.IsVerified);

        var call = await harness.Client.CallToolAsync("open_project", new Dictionary<string, object?>
        {
            ["projectPath"] = input, ["forceRebind"] = true
        });
        var document = call.StructuredContent!.Value;
        Assert.False(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal("applied", document.GetProperty("phase").GetString());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("error").ValueKind);
        var failure = document.GetProperty("result").GetProperty("failure");
        Assert.Equal(WorkerFailureCategories.ProtocolError, failure.GetProperty("category").GetString());
        Assert.Contains("local-session identity", failure.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE_MALFORMED_IDENTITY_PAYLOAD", document.GetRawText(), StringComparison.Ordinal);
        Assert.Contains(document.GetProperty("warnings").EnumerateArray(), warning =>
            warning.GetString()!.Contains("Inspect", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ProjectBindingSnapshot.InvalidatedState, harness.WorkerClient.BindingSnapshot.State);
        Assert.Equal(1, log.Methods().Count(method => method == "open_project"));
        var auditRecord = Assert.Single(File.ReadAllLines(
            Assert.Single(Directory.GetFiles(audit.Path, "*.jsonl", SearchOption.AllDirectories))));
        Assert.DoesNotContain("PRIVATE_MALFORMED_IDENTITY_PAYLOAD", auditRecord, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("context-mismatch", "result", "PRIVATE_ENVELOPE_OWNER")]
    [InlineData("verification-mismatch", "verification", "PRIVATE_VERIFICATION_OWNER")]
    public async Task ContradictoryAlsContext_ReportsProtocolFailureWithoutTrustedBinding(
        string scenario, string outcome, string privateOwner)
    {
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, null));
        using var ui = new FakeWorkerUiOpenProject(null);
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        var input = Path.Combine(audit.Path, "local-session-open-" + scenario + ".als21");
        File.WriteAllText(input, "offline fixture");
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path);

        var call = await harness.Client.CallToolAsync("open_project", new Dictionary<string, object?> { ["projectPath"] = input });
        var document = call.StructuredContent!.Value;
        Assert.False(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal("applied", document.GetProperty("phase").GetString());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("error").ValueKind);
        var failure = document.GetProperty(outcome).GetProperty("failure");
        Assert.Equal(WorkerFailureCategories.ProtocolError, failure.GetProperty("category").GetString());
        Assert.DoesNotContain(privateOwner, document.GetRawText(), StringComparison.Ordinal);
        Assert.Contains(document.GetProperty("warnings").EnumerateArray(), warning =>
            warning.GetString()!.Contains("Inspect", StringComparison.OrdinalIgnoreCase));
        Assert.False(harness.WorkerClient.BindingSnapshot.IsVerified);
        Assert.Equal(1, log.Methods().Count(method => method == "open_project"));
        var auditRecord = Assert.Single(File.ReadAllLines(
            Assert.Single(Directory.GetFiles(audit.Path, "*.jsonl", SearchOption.AllDirectories))));
        Assert.DoesNotContain(privateOwner, auditRecord, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AlsOpen_DifferentPassiveObservationPresentationKeepsSameOwner()
    {
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, null));
        using var ui = new FakeWorkerUiOpenProject(null);
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        var input = Path.Combine(audit.Path, "local-session-open-passive-observation.als21");
        File.WriteAllText(input, "offline fixture");
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path);

        var call = await harness.Client.CallToolAsync("open_project", new Dictionary<string, object?> { ["projectPath"] = input });
        var document = call.StructuredContent!.Value;
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.True(harness.WorkerClient.BindingSnapshot.IsVerified);
        Assert.Equal(ProjectPathNormalization.Canonicalize(input),
            harness.WorkerClient.BindingSnapshot.Context!.SessionContainerPath);
    }

    [Theory]
    [InlineData("eof", WorkerFailureCategories.WorkerCrashed)]
    [InlineData("timeout", WorkerFailureCategories.WorkerTimeout)]
    public async Task SentAlsOpener_TransportLossReportsPossibleMutationWithoutReplay(string loss, string category)
    {
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, null));
        using var ui = new FakeWorkerUiOpenProject(null);
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        var input = Path.Combine(audit.Path, "local-session-open-" + loss + ".als21");
        File.WriteAllText(input, "offline fixture");
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(
            McpAccessMode.Full, audit.Path, requestTimeout: TimeSpan.FromSeconds(5));

        var result = await harness.Client.CallToolAsync("open_project", new Dictionary<string, object?> { ["projectPath"] = input });
        var document = result.StructuredContent!.Value;
        Assert.False(document.GetProperty("success").GetBoolean());
        Assert.Equal("applied", document.GetProperty("phase").GetString());
        Assert.Equal(category, document.GetProperty("result").GetProperty("failure").GetProperty("category").GetString());
        Assert.Contains(document.GetProperty("warnings").EnumerateArray(), warning =>
            warning.GetString()!.Contains("Inspect", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(input + ".attempted"));
        Assert.Equal(1, log.Methods().Count(method => method == "open_project"));
        Assert.False(harness.WorkerClient.BindingSnapshot.IsVerified);
        Assert.Single(File.ReadAllLines(Assert.Single(Directory.GetFiles(audit.Path, "*.jsonl", SearchOption.AllDirectories))));
    }
}
