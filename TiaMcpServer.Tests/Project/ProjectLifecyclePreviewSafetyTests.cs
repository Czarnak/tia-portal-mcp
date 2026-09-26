using System.Text.Json;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Project;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class ProjectLifecyclePreviewSafetyTests
{
    private const string SourcePath = @"C:\FakeWorker\lifecycle-rebind-probe.ap21";
    private const string DestinationPath = @"C:\Lifecycle\B.ap21";

    [Fact]
    public void RebindProbeRequest_CarriesDestinationSeparatelyFromBoundSource()
    {
        var expectedIdentity = new WorkerSessionIdentity
        {
            WorkerSessionId = "worker-A",
            SessionGeneration = 7,
            PortalProcessId = 4242,
            ProjectPath = SourcePath
        };
        var request = new WorkerRequest
        {
            Method = "probe_open_project_rebind",
            ProjectPath = SourcePath,
            RebindDestinationProjectPath = DestinationPath,
            ExpectedSessionIdentity = expectedIdentity
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            request,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var root = json.RootElement;
        var identity = root.GetProperty("expectedSessionIdentity");

        Assert.Equal("probe_open_project_rebind", root.GetProperty("method").GetString());
        Assert.Equal(SourcePath, root.GetProperty("projectPath").GetString());
        Assert.Equal(DestinationPath, root.GetProperty("rebindDestinationProjectPath").GetString());
        Assert.Equal("worker-A", identity.GetProperty("workerSessionId").GetString());
        Assert.Equal(7, identity.GetProperty("sessionGeneration").GetInt64());
        Assert.Equal(4242, identity.GetProperty("portalProcessId").GetInt32());
        Assert.Equal(SourcePath, identity.GetProperty("projectPath").GetString());
    }

    [Fact]
    public async Task RebindProbe_UsesVerifiedSourceIdentityAndKeepsBindingOnSource()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(
            binding,
            workerExecutablePath: FakeWorkerLocator.Locate());
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, SourcePath);
        var bindingBefore = binding.CaptureSnapshot();
        var expectedIdentity = Assert.IsType<WorkerSessionIdentity>(bindingBefore.ToWorkerIdentity());

        // The FakeWorker's lifecycle-rebind-probe scenario checks the method, source path,
        // destination field, and expected identity before returning this typed snapshot.
        var result = await client.ProbeOpenProjectRebindAsync(SourcePath, DestinationPath);

        Assert.True(result.Success, result.Error);
        var state = JsonSerializer.Deserialize<ProjectRebindStateInfo>(
            result.Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(state);
        Assert.Equal(SourcePath, state.SourceProjectPath);
        Assert.Equal(DestinationPath, state.DestinationProjectPath);
        Assert.Equal(false, state.SourceIsModified);
        Assert.True(state.SourceOpenedByWorker);
        Assert.True(state.WillCloseSource);
        Assert.True(bindingBefore.SameBinding(binding.CaptureSnapshot()));
        Assert.NotNull(result.SessionIdentity);
        Assert.Equal(expectedIdentity.WorkerSessionId, result.SessionIdentity.WorkerSessionId);
        Assert.Equal(expectedIdentity.SessionGeneration, result.SessionIdentity.SessionGeneration);
        Assert.Equal(expectedIdentity.PortalProcessId, result.SessionIdentity.PortalProcessId);
        Assert.Equal(expectedIdentity.ProjectPath, result.SessionIdentity.ProjectPath);
    }

    [Theory]
    [InlineData("lifecycle-rebind-probe-missing-source")]
    [InlineData("lifecycle-rebind-probe-null-modified")]
    [InlineData("lifecycle-rebind-probe-wrong-destination")]
    [InlineData("lifecycle-rebind-probe-wrong-disposition")]
    public async Task RebindProbe_InvalidSnapshot_IsProtocolErrorWithoutEchoingPayload(string scenario)
    {
        var sourcePath = $@"C:\FakeWorker\{scenario}.ap21";
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(
            binding,
            workerExecutablePath: FakeWorkerLocator.Locate());
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, sourcePath);

        var result = await client.ProbeOpenProjectRebindAsync(sourcePath, DestinationPath);

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.ProtocolError, result.FailureCategory);
        Assert.Equal(string.Empty, result.Payload);
    }

    [Fact]
    public async Task RebindProbe_WorkerBindingConflict_PreservesCategoryAndInvalidatesBinding()
    {
        const string sourcePath = @"C:\FakeWorker\lifecycle-rebind-probe-binding-conflict.ap21";
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(
            binding,
            workerExecutablePath: FakeWorkerLocator.Locate());
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, sourcePath);

        var result = await client.ProbeOpenProjectRebindAsync(sourcePath, DestinationPath);

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, result.FailureCategory);
        Assert.Equal(ProjectBindingSnapshot.InvalidatedState, binding.CaptureSnapshot().State);
    }

    [Fact]
    public async Task RebindProbe_IsAbsentFromEightRegisteredWriteTools()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectWriteTools, WriteBatchTools>();

        var toolNames = (await harness.Client.ListToolsAsync())
            .Select(tool => tool.Name)
            .ToArray();

        Assert.Equal(8, toolNames.Length);
        Assert.DoesNotContain("probe_open_project_rebind", toolNames);
    }

    [Fact]
    public async Task OpenProject_ModifiedWorkerOwnedSource_PreviewFailsWithoutToken()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        var safety = audit.CreateSafety(projectSessionBinding: binding);
        using var client = new OpennessWorkerClient(binding, workerExecutablePath: FakeWorkerLocator.Locate());
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, SourcePath);

        var preview = await ProjectWriteTools.OpenProject(
            client, safety, @"C:\Lifecycle\B-modified.ap21", forceRebind: true);
        using var document = JsonDocument.Parse(preview);
        var root = document.RootElement;

        Assert.False(root.TryGetProperty("safetyToken", out _));
        Assert.Equal(WorkerFailureCategories.ValidationError, root.GetProperty("failureCategory").GetString());
        await AssertNoOpenProjectCallsAsync(client, SourcePath);
        AssertNoAudit(audit);
    }

    [Fact]
    public async Task OpenProject_SourceBecomesModified_ApplyIsStateChangedAndDoesNotOpen()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        var safety = audit.CreateSafety(projectSessionBinding: binding);
        using var client = new OpennessWorkerClient(binding, workerExecutablePath: FakeWorkerLocator.Locate());
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, SourcePath);
        // Existing FakeWorker open fixture succeeds, so the RED test proves the old B-only
        // state hash reaches worker Open instead of merely hitting an unknown-scenario error.
        const string destination = @"C:\open\Line.ap21";

        var preview = await ProjectWriteTools.OpenProject(client, safety, destination, forceRebind: true);
        using var previewDocument = JsonDocument.Parse(preview);
        var token = previewDocument.RootElement.GetProperty("safetyToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        var apply = await ProjectWriteTools.OpenProject(
            client, safety, destination, confirm: true, safetyToken: token, forceRebind: true);
        using var applyDocument = JsonDocument.Parse(apply);
        Assert.False(applyDocument.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            WorkerFailureCategories.StateChanged,
            applyDocument.RootElement.GetProperty("failureCategory").GetString());
        Assert.Equal(SourcePath, binding.CaptureSnapshot().ProjectPath);
        await AssertNoOpenProjectCallsAsync(client, SourcePath);
        AssertNoAudit(audit);

        // A state-changed apply consumes the token. Retrying it must not reach worker Open.
        var retry = await ProjectWriteTools.OpenProject(
            client, safety, destination, confirm: true, safetyToken: token, forceRebind: true);
        using var retryDocument = JsonDocument.Parse(retry);
        Assert.Equal(
            WorkerFailureCategories.ValidationError,
            retryDocument.RootElement.GetProperty("failureCategory").GetString());
        await AssertNoOpenProjectCallsAsync(client, SourcePath);
        AssertNoAudit(audit);
    }

    [Fact]
    public async Task OpenProject_ModifiedUiOwnedSource_RemainsOpen()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        var safety = audit.CreateSafety(projectSessionBinding: binding);
        using var client = new OpennessWorkerClient(binding, workerExecutablePath: FakeWorkerLocator.Locate());
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, SourcePath);
        const string destination = @"C:\Lifecycle\B-ui-owned.ap21";

        var preview = await ProjectWriteTools.OpenProject(client, safety, destination, forceRebind: true);
        using var previewDocument = JsonDocument.Parse(preview);
        var token = previewDocument.RootElement.GetProperty("safetyToken").GetString();

        var apply = await ProjectWriteTools.OpenProject(
            client, safety, destination, confirm: true, safetyToken: token, forceRebind: true);
        using var applyDocument = JsonDocument.Parse(apply);
        Assert.True(applyDocument.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(destination, binding.CaptureSnapshot().ProjectPath);
    }

    [Fact]
    public async Task OpenProject_SamePathModifiedSource_IsIdempotent()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        var safety = audit.CreateSafety(projectSessionBinding: binding);
        using var client = new OpennessWorkerClient(binding, workerExecutablePath: FakeWorkerLocator.Locate());
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, SourcePath);

        var preview = await ProjectWriteTools.OpenProject(client, safety, SourcePath);
        using var previewDocument = JsonDocument.Parse(preview);
        var token = previewDocument.RootElement.GetProperty("safetyToken").GetString();

        var apply = await ProjectWriteTools.OpenProject(
            client, safety, SourcePath, confirm: true, safetyToken: token);
        using var applyDocument = JsonDocument.Parse(apply);
        Assert.True(applyDocument.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(SourcePath, binding.CaptureSnapshot().ProjectPath);
    }

    [Fact]
    public async Task OpenProject_InconsistentSnapshot_RejectsWithoutToken()
    {
        const string source = @"C:\FakeWorker\lifecycle-rebind-probe-wrong-disposition.ap21";
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        var safety = audit.CreateSafety(projectSessionBinding: binding);
        using var client = new OpennessWorkerClient(binding, workerExecutablePath: FakeWorkerLocator.Locate());
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, source);

        var preview = await ProjectWriteTools.OpenProject(client, safety, DestinationPath, forceRebind: true);
        using var document = JsonDocument.Parse(preview);
        var root = document.RootElement;

        Assert.False(root.TryGetProperty("safetyToken", out _));
        Assert.Equal(WorkerFailureCategories.ProtocolError, root.GetProperty("failureCategory").GetString());
        await AssertNoOpenProjectCallsAsync(client, source);
        AssertNoAudit(audit);
    }

    [Fact]
    public async Task OpenProject_ConfiguredSourceCannotBeVerified_RejectsWithoutToken()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding("worker-error-with-category");
        var safety = audit.CreateSafety(projectSessionBinding: binding);
        using var client = new OpennessWorkerClient(binding, workerExecutablePath: FakeWorkerLocator.Locate());

        var preview = await ProjectWriteTools.OpenProject(client, safety, DestinationPath, forceRebind: true);
        using var document = JsonDocument.Parse(preview);
        var root = document.RootElement;

        Assert.False(root.TryGetProperty("safetyToken", out _));
        Assert.Equal(WorkerFailureCategories.ValidationError, root.GetProperty("failureCategory").GetString());
        Assert.Equal(ProjectBindingSnapshot.ConfiguredUnverifiedState, binding.CaptureSnapshot().State);
        AssertNoAudit(audit);
    }

    [Fact]
    public async Task OpenProject_UnboundSource_DoesNotNeedRebindProbe()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        var safety = audit.CreateSafety(projectSessionBinding: binding);
        using var client = new OpennessWorkerClient(binding, workerExecutablePath: FakeWorkerLocator.Locate());
        const string destination = @"C:\open\Line.ap21";

        var preview = await ProjectWriteTools.OpenProject(client, safety, destination);
        using var previewDocument = JsonDocument.Parse(preview);
        var token = previewDocument.RootElement.GetProperty("safetyToken").GetString();

        var apply = await ProjectWriteTools.OpenProject(
            client, safety, destination, confirm: true, safetyToken: token);
        using var applyDocument = JsonDocument.Parse(apply);
        Assert.True(applyDocument.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task OpenProject_UnboundWorkerFailure_PreservesPriorCategory()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        var safety = audit.CreateSafety(projectSessionBinding: binding);
        using var client = new OpennessWorkerClient(binding, workerExecutablePath: FakeWorkerLocator.Locate());

        var preview = await ProjectWriteTools.OpenProject(client, safety, "worker-error-with-category");
        using var previewDocument = JsonDocument.Parse(preview);
        var token = previewDocument.RootElement.GetProperty("safetyToken").GetString();

        var apply = await ProjectWriteTools.OpenProject(
            client, safety, "worker-error-with-category", confirm: true, safetyToken: token);
        using var applyDocument = JsonDocument.Parse(apply);
        var root = applyDocument.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(WorkerFailureCategories.ValidationError, root.GetProperty("failureCategory").GetString());
        Assert.Equal("invalid value", root.GetProperty("error").GetString());
        Assert.False(root.TryGetProperty("operationResult", out _));
    }

    private static async Task AssertNoOpenProjectCallsAsync(OpennessWorkerClient client, string sourcePath)
    {
        // The persistent FakeWorker returns this process-local counter on a bound status read.
        var status = await client.GetProjectStatusAsync(sourcePath);
        Assert.True(status.Success, status.Error);
        using var document = JsonDocument.Parse(status.Payload);
        Assert.Equal(0, document.RootElement.GetProperty("openProjectCalls").GetInt32());
    }

    private static void AssertNoAudit(TempAuditDirectory audit)
    {
        var lineCount = Directory.Exists(audit.Path)
            ? Directory.GetFiles(audit.Path).Sum(file => File.ReadAllLines(file).Length)
            : 0;
        Assert.Equal(0, lineCount);
    }
}
