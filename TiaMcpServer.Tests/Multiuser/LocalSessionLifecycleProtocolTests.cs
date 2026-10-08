using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Multiuser;

[Collection("Mcp protocol serial")]
public sealed class LocalSessionLifecycleProtocolTests
{
    private const string Amc = "C:/Projects/Local.amc21";

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
}
