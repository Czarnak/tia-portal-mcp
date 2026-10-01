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
    [Theory]
    [InlineData("accept", true, true)]
    [InlineData("accept", false, true)]
    [InlineData("decline", true, true)]
    [InlineData("cancel", true, true)]
    public async Task Full_ModifiedClose_NeverPrompts_AndAuditsPolicy(
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
            McpAccessMode.Full, audit.Path, fixture.SourcePath, clientOptions: options);

        var result = await harness.Client.CallToolAsync("close_project", new Dictionary<string, object?>
        {
            ["saveBeforeClose"] = false
        });

        Assert.Empty(prompts);
        var document = Document(result);
        Assert.Equal(applied, document.GetProperty("success").GetBoolean());
        Assert.Equal(!applied, result.IsError == true);
        Assert.Equal(applied ? "applied" : "blocked", document.GetProperty("phase").GetString());
        if (!applied) Assert.Equal("access_denied", document.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(applied ? ProjectBindingSnapshot.UnboundState : ProjectBindingSnapshot.VerifiedState,
            harness.WorkerClient.BindingSnapshot.State);
        Assert.Empty(StructuredContractInspector.FindViolations(result));
        AssertAudit(audit.Path, result, "policy");
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
    public async Task Full_ModifiedClose_UsesModePolicy()
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

    private static void AssertAudit(string directory, CallToolResult result, string? satisfaction, bool checkGuard = true)
    {
        var lines = Directory.GetFiles(directory, "writes-*.jsonl").SelectMany(File.ReadLines).ToArray();
        var line = Assert.Single(lines);
        using var record = JsonDocument.Parse(line);
        var text = Assert.Single(result.Content.OfType<TextContentBlock>()).Text;
        Assert.Equal(text, record.RootElement.GetProperty("responseText").GetString());
        Assert.Equal("sha256:" + ContentHashes.Sha256Hex(text), record.RootElement.GetProperty("responseHash").GetString());
        Assert.Equal(2, record.RootElement.GetProperty("recordVersion").GetInt32());
        var confirmation = record.RootElement.GetProperty("confirmation");
        Assert.Equal(Document(result).GetProperty("phase").GetString() == "preview" ? "none" : "policy", confirmation.GetProperty("by").GetString());
        Assert.Equal("not_requested", confirmation.GetProperty("outcome").GetString());
        if (checkGuard)
        {
            var guard = Assert.Single(record.RootElement.GetProperty("guards").EnumerateArray());
            Assert.Equal(satisfaction, guard.GetProperty("satisfiedBy").GetString());
        }
    }
}
