using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Safety;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Tests.TestUtilities;
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

    private sealed class Fixture : IDisposable
    {
        internal readonly TempAuditDirectory Audit = new();
        internal readonly string Root = Directory.CreateTempSubdirectory("lifecycle-caller-").FullName;
        internal string Source { get; private set; } = string.Empty;
        internal readonly ProjectSessionBinding Binding;
        internal readonly OpennessWorkerClient Client;
        internal readonly WriteExecution Execution;
        private FakeWorkerUiOpenProject? _uiOpen;

        internal Fixture(string? configured = null, string? worker = null)
        {
            if (configured is not null)
                _uiOpen = new FakeWorkerUiOpenProject(configured);
            Binding = new ProjectSessionBinding(configured);
            Client = new OpennessWorkerClient(Binding, workerExecutablePath: worker ?? FakeWorkerLocator.Locate(),
                accessPolicy: new OperationAccessPolicy(McpAccessMode.Full));
            Execution = LifecycleTestCalls.Execution(Client, Audit);
        }

        internal async Task Bind(string source = SourcePath)
        {
            Source = Physical(source);
            _uiOpen?.Dispose();
            _uiOpen = new FakeWorkerUiOpenProject(Source);
            await FakeWorkerBinding.BindVerifiedAsync(Client, Binding, Source);
        }

        internal string Physical(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            var relative = Path.IsPathFullyQualified(path) ? path.Substring(Path.GetPathRoot(path)!.Length)
                : Path.Combine("FakeWorker", path + ".ap21");
            var result = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(result)!);
            if (!File.Exists(result)) File.WriteAllText(result, "scripted fixture");
            return result;
        }

        internal Task<CallToolResult> Open(string destination = DestinationPath,
            bool forceRebind = true, bool dryRun = true)
            => ProjectWriteTools.OpenProject(Client, Execution, Physical(destination),
                forceRebind: forceRebind, dryRun: dryRun);

        public void Dispose()
        {
            Client.Dispose();
            _uiOpen?.Dispose();
            Audit.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    [Fact]
    public async Task LifecycleDryRuns_OmittedPath_ReportResolvedSourceAndSaveChoices()
    {
        using var fixture = new Fixture();
        await fixture.Bind("lifecycle-probe-only");
        var source = fixture.Binding.BoundProjectPath;
        var directory = Directory.CreateTempSubdirectory("tia-archive-test-").FullName;
        try
        {
            var calls = new[]
            {
                await ProjectWriteTools.SaveProject(fixture.Client, fixture.Execution, dryRun: true),
                await ProjectWriteTools.SaveProjectAs(fixture.Client, fixture.Execution,
                    fixture.Root, "Copy", dryRun: true),
                await ProjectWriteTools.ArchiveProject(fixture.Client, fixture.Execution,
                    directory, "Backup", dryRun: true),
                await ProjectWriteTools.CloseProject(fixture.Client, fixture.Execution, dryRun: true)
            };
            foreach (var call in calls)
            {
                var document = LifecycleTestCalls.Document(call);
                LifecycleTestCalls.Phase(document, "preview");
                Assert.Equal(source, document.GetProperty("effects").GetProperty("sourceProjectPath").GetString());
            }
            Assert.True(LifecycleTestCalls.Document(calls[2]).GetProperty("effects").GetProperty("savesSource").GetBoolean());
            Assert.True(LifecycleTestCalls.Document(calls[3]).GetProperty("effects").GetProperty("savesSource").GetBoolean());
            Assert.Equal(4, LifecycleTestCalls.AuditCount(fixture.Audit));

            var applied = await ProjectWriteTools.SaveProject(fixture.Client, fixture.Execution);
            LifecycleTestCalls.Succeeded(LifecycleTestCalls.Document(applied));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(DestinationPath, true)]
    [InlineData(@"C:\Lifecycle\B-ui-owned.ap21", false)]
    public async Task OpenProject_ForceRebindDryRun_ReportsSourceDestinationAndOwnership(
        string destination, bool willCloseSource)
    {
        using var fixture = new Fixture();
        await fixture.Bind();

        var result = await fixture.Open(destination);
        var document = LifecycleTestCalls.Document(result);
        var effects = document.GetProperty("effects");
        LifecycleTestCalls.Phase(document, "preview");
        Assert.Equal(fixture.Source, effects.GetProperty("sourceProjectPath").GetString());
        Assert.Equal(fixture.Physical(destination), effects.GetProperty("destinationProjectPath").GetString());
        Assert.Equal(willCloseSource, effects.GetProperty("willCloseSource").GetBoolean());
        Assert.Equal(willCloseSource, effects.GetProperty("sourceOpenedByWorker").GetBoolean());
        Assert.Equal(willCloseSource, document.GetProperty("guards").EnumerateArray()
            .Any(guard => guard.GetProperty("id").GetString() == "closes_source_project"));
        await AssertNoOpenProjectCallsAsync(fixture.Client, fixture.Source);
        Assert.Equal(1, LifecycleTestCalls.AuditCount(fixture.Audit));
    }

    [Fact]
    public async Task LifecycleDryRuns_ExplicitPathAndUnsavedChoices_ReportFreshConsequences()
    {
        using var fixture = new Fixture();
        const string source = "lifecycle-probe-only";
        await fixture.Bind(source);
        var directory = Directory.CreateTempSubdirectory("tia-archive-test-").FullName;
        try
        {
            var archive = await ProjectWriteTools.ArchiveProject(fixture.Client, fixture.Execution,
                directory, "Backup", saveBeforeArchive: false, projectPath: fixture.Source, dryRun: true);
            var close = await ProjectWriteTools.CloseProject(fixture.Client, fixture.Execution,
                projectPath: fixture.Source, saveBeforeClose: false, dryRun: true);
            foreach (var call in new[] { archive, close })
            {
                var document = LifecycleTestCalls.Document(call);
                LifecycleTestCalls.Phase(document, "preview");
                Assert.Equal(fixture.Binding.BoundProjectPath,
                    document.GetProperty("effects").GetProperty("sourceProjectPath").GetString());
                Assert.False(document.GetProperty("effects").GetProperty("savesSource").GetBoolean());
                Assert.False(document.TryGetProperty("requestedInputHash", out _));
            }
            Assert.Equal(JsonValueKind.Object,
                LifecycleTestCalls.Document(archive).GetProperty("effects").GetProperty("sourceStatus").ValueKind);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task OpenProject_DifferentBoundPathWithoutForce_RejectsWithoutChangingSource()
    {
        using var fixture = new Fixture();
        await fixture.Bind();
        var before = fixture.Binding.CaptureSnapshot();

        LifecycleTestCalls.Rejected(await fixture.Open(forceRebind: false), WorkerFailureCategories.BindingConflict);

        Assert.True(before.SameBinding(fixture.Binding.CaptureSnapshot()));
        await AssertNoOpenProjectCallsAsync(fixture.Client, fixture.Source);
        Assert.Equal(1, LifecycleTestCalls.AuditCount(fixture.Audit));
    }

    [Fact]
    public async Task ArchiveProject_DirectoryRemovedAfterDryRun_FreshCallRejectsBeforeMutation()
    {
        using var fixture = new Fixture();
        const string source = "lifecycle-probe-only";
        await fixture.Bind(source);
        var directory = Directory.CreateTempSubdirectory("tia-archive-test-").FullName;
        try
        {
            var preview = await ProjectWriteTools.ArchiveProject(fixture.Client, fixture.Execution,
                directory, "Backup", projectPath: fixture.Source, dryRun: true);
            LifecycleTestCalls.Phase(LifecycleTestCalls.Document(preview), "preview");
            Directory.Delete(directory);
            var result = await ProjectWriteTools.ArchiveProject(fixture.Client, fixture.Execution,
                directory, "Backup", projectPath: fixture.Source);
            LifecycleTestCalls.Rejected(result, WorkerFailureCategories.ValidationError);
            Assert.False(Directory.Exists(directory));
            Assert.Equal(2, LifecycleTestCalls.AuditCount(fixture.Audit));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ArchiveProject_MissingDirectory_RejectsWithoutCreatingIt()
    {
        using var fixture = new Fixture();
        await fixture.Bind("lifecycle-probe-only");
        var directory = Path.Combine(Path.GetTempPath(), "tia-archive-missing-" + Guid.NewGuid().ToString("N"));

        var result = await ProjectWriteTools.ArchiveProject(fixture.Client, fixture.Execution,
            directory, "Backup", dryRun: true);
        LifecycleTestCalls.Rejected(result, WorkerFailureCategories.ValidationError);
        Assert.False(Directory.Exists(directory));
    }

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
            workerExecutablePath: FakeWorkerLocator.Locate(), accessPolicy: new OperationAccessPolicy(McpAccessMode.Full));
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
            workerExecutablePath: FakeWorkerLocator.Locate(), accessPolicy: new OperationAccessPolicy(McpAccessMode.Full));
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
            workerExecutablePath: FakeWorkerLocator.Locate(), accessPolicy: new OperationAccessPolicy(McpAccessMode.Full));
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, sourcePath);

        var result = await client.ProbeOpenProjectRebindAsync(sourcePath, DestinationPath);

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, result.FailureCategory);
        Assert.Equal(ProjectBindingSnapshot.InvalidatedState, binding.CaptureSnapshot().State);
    }

    [Fact]
    public async Task RebindProbe_IsAbsentFromSixRegisteredWriteTools()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectWriteTools>(accessMode: McpAccessMode.Full);

        var toolNames = (await harness.Client.ListToolsAsync())
            .Select(tool => tool.Name)
            .ToArray();

        Assert.Equal(6, toolNames.Length);
        Assert.DoesNotContain("probe_open_project_rebind", toolNames);
    }


    [Fact]
    public async Task OpenProject_ModifiedWorkerOwnedSource_PolicyCannotBypassHardBlock()
    {
        using var fixture = new Fixture();
        await fixture.Bind();

        var result = await ProjectWriteTools.OpenProject(fixture.Client, fixture.Execution,
            fixture.Physical(@"C:\Lifecycle\B-modified.ap21"), forceRebind: true);

        LifecycleTestCalls.Rejected(result, WorkerFailureCategories.GuardBlocked);
        LifecycleTestCalls.Phase(LifecycleTestCalls.Document(result), "blocked");
        await AssertNoOpenProjectCallsAsync(fixture.Client, fixture.Source);
    }

    [Fact]
    public async Task OpenProject_SourceBecomesModifiedAfterDryRun_FreshApplyBlocksAndDoesNotOpen()
    {
        using var fixture = new Fixture();
        await fixture.Bind();
        const string destination = @"C:\open\Line.ap21";

        var preview = await fixture.Open(destination);
        LifecycleTestCalls.Phase(LifecycleTestCalls.Document(preview), "preview");
        var result = await fixture.Open(destination, dryRun: false);

        LifecycleTestCalls.Rejected(result, "guard_blocked");
        Assert.Contains(LifecycleTestCalls.Document(result).GetProperty("guards").EnumerateArray(),
            guard => guard.GetProperty("id").GetString() == "discards_unsaved_source_changes");
        Assert.Equal(fixture.Source, fixture.Binding.BoundProjectPath);
        await AssertNoOpenProjectCallsAsync(fixture.Client, fixture.Source);
    }

    [Fact]
    public async Task OpenProject_ModifiedUiOwnedSource_RemainsOpenWhenDestinationIsBound()
    {
        using var fixture = new Fixture();
        await fixture.Bind();
        const string destination = @"C:\Lifecycle\B-ui-owned.ap21";

        var result = await fixture.Open(destination, dryRun: false);
        var document = LifecycleTestCalls.Document(result);
        LifecycleTestCalls.Succeeded(document);
        Assert.False(document.GetProperty("effects").GetProperty("willCloseSource").GetBoolean());
        Assert.Equal(fixture.Physical(destination), fixture.Binding.BoundProjectPath);
    }

    [Fact]
    public async Task OpenProject_SamePathModifiedSource_IsIdempotent()
    {
        const string source = @"C:\FakeWorker\guarded-lifecycle-modified.ap21";
        using var fixture = new Fixture();
        await fixture.Bind(source);

        var result = await fixture.Open(source, forceRebind: false, dryRun: false);
        var document = LifecycleTestCalls.Document(result);
        LifecycleTestCalls.Succeeded(document);
        Assert.True(document.GetProperty("effects").GetProperty("sourceStatus").GetProperty("isModified").GetBoolean());
        Assert.False(document.GetProperty("effects").GetProperty("willCloseSource").GetBoolean());
        Assert.Equal(fixture.Source, fixture.Binding.BoundProjectPath);
    }

    [Fact]
    public async Task OpenProject_InconsistentProbe_RejectsWithoutDispatch()
    {
        const string source = @"C:\FakeWorker\lifecycle-rebind-probe-wrong-disposition.ap21";
        using var fixture = new Fixture();
        await fixture.Bind(source);

        LifecycleTestCalls.Rejected(await fixture.Open(), WorkerFailureCategories.ProtocolError);

        await AssertNoOpenProjectCallsAsync(fixture.Client, fixture.Source);
    }

    [Fact]
    public async Task OpenProject_ConfiguredSourceCannotBeVerified_PreservesCategoryAndBinding()
    {
        using var fixture = new Fixture("worker-error-with-category");

        LifecycleTestCalls.Rejected(await fixture.Open(), WorkerFailureCategories.ValidationError);

        Assert.Equal(ProjectBindingSnapshot.ConfiguredUnverifiedState, fixture.Binding.CaptureSnapshot().State);
    }

    [Fact]
    public async Task OpenProject_UnboundDryRun_DoesNotNeedWorkerOrRebindProbe()
    {
        using var fixture = new Fixture(worker: "worker-must-not-start.exe");
        var before = fixture.Binding.CaptureSnapshot();
        var result = await fixture.Open(@"C:\open\Line.ap21", forceRebind: false);
        var effects = LifecycleTestCalls.Document(result).GetProperty("effects");

        Assert.Equal(JsonValueKind.Null, effects.GetProperty("sourceProjectPath").ValueKind);
        Assert.Equal(fixture.Physical(@"C:\open\Line.ap21"), effects.GetProperty("destinationProjectPath").GetString());
        Assert.True(before.SameBinding(fixture.Binding.CaptureSnapshot()));
    }

    [Fact]
    public async Task OpenProject_InvalidatedForceRebind_RegroundsRetainedSourceBeforeDryRun()
    {
        using var fixture = new Fixture();
        await fixture.Bind();
        fixture.Binding.Invalidate("Simulated stale binding");
        var invalidated = fixture.Binding.CaptureSnapshot();

        var result = await fixture.Open();

        LifecycleTestCalls.Phase(LifecycleTestCalls.Document(result), "preview");
        var promoted = fixture.Binding.CaptureSnapshot();
        Assert.True(promoted.IsVerified);
        Assert.Equal(fixture.Source, promoted.ProjectPath);
        Assert.True(promoted.Revision >= invalidated.Revision + 2);
        Assert.NotEqual(invalidated.BindingId, promoted.BindingId);
        Assert.NotNull(promoted.ToWorkerIdentity());
        await AssertNoOpenProjectCallsAsync(fixture.Client, fixture.Source);
    }

    [Fact]
    public async Task OpenProject_InvalidatedModifiedSource_RecoveryStillBlocksMutation()
    {
        using var fixture = new Fixture();
        await fixture.Bind();
        fixture.Binding.Invalidate("Simulated stale binding");

        LifecycleTestCalls.Rejected(await fixture.Open(@"C:\Lifecycle\B-modified.ap21", dryRun: false),
            "guard_blocked");

        Assert.True(fixture.Binding.IsVerified);
        Assert.Equal(fixture.Source, fixture.Binding.BoundProjectPath);
        await AssertNoOpenProjectCallsAsync(fixture.Client, fixture.Source);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OpenProject_BlankPath_RejectsBeforeGrounding(bool invalidated, bool forceRebind)
    {
        using var fixture = new Fixture("worker-error-with-category", "worker-must-not-start.exe");
        if (invalidated) fixture.Binding.Invalidate("Simulated stale binding");
        var before = fixture.Binding.CaptureSnapshot();

        var result = await fixture.Open("   ", forceRebind);
        LifecycleTestCalls.Rejected(result, WorkerFailureCategories.ValidationError);
        Assert.Contains("path",
            LifecycleTestCalls.Document(result).GetProperty("error").GetProperty("message").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.True(before.SameBinding(fixture.Binding.CaptureSnapshot()));
    }

    [Fact]
    public async Task OpenProject_ConfiguredDifferentPathWithoutForce_RejectsBeforeStatusPromotion()
    {
        using var fixture = new Fixture("worker-error-with-category", "worker-must-not-start.exe");
        var before = fixture.Binding.CaptureSnapshot();

        LifecycleTestCalls.Rejected(await fixture.Open(forceRebind: false), WorkerFailureCategories.BindingConflict);

        Assert.True(before.SameBinding(fixture.Binding.CaptureSnapshot()));
    }

    [Fact]
    public async Task OpenProject_InvalidatedStatusFailure_PreservesFailureAndRetainedSource()
    {
        using var fixture = new Fixture("worker-error-with-category");
        fixture.Binding.Invalidate("Simulated stale binding");
        var before = fixture.Binding.CaptureSnapshot();

        var result = await fixture.Open();
        LifecycleTestCalls.Rejected(result, WorkerFailureCategories.ValidationError);

        Assert.Equal("invalid value",
            LifecycleTestCalls.Document(result).GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(ProjectBindingSnapshot.InvalidatedState, fixture.Binding.CaptureSnapshot().State);
        Assert.Equal(before.ProjectPath, fixture.Binding.BoundProjectPath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenProject_InvalidatedWithoutRecoveryAuthority_RejectsWithoutGrounding(bool retainSource)
    {
        using var fixture = new Fixture(retainSource ? SourcePath : null, "worker-must-not-start.exe");
        fixture.Binding.Invalidate("Simulated stale binding");
        var before = fixture.Binding.CaptureSnapshot();

        LifecycleTestCalls.Rejected(await fixture.Open(forceRebind: !retainSource), WorkerFailureCategories.BindingConflict);

        Assert.True(before.SameBinding(fixture.Binding.CaptureSnapshot()));
    }

    [Fact]
    public async Task OpenProject_PreRecoverySnapshot_CannotDispatchAfterRegroundRevision()
    {
        using var fixture = new Fixture();
        await fixture.Bind();
        var oldBinding = fixture.Binding.CaptureSnapshot();
        fixture.Binding.Invalidate("Simulated stale binding");

        var preview = await fixture.Open();
        LifecycleTestCalls.Phase(LifecycleTestCalls.Document(preview), "preview");
        Assert.NotEqual(oldBinding.BindingId, fixture.Binding.CaptureSnapshot().BindingId);
        Assert.True(fixture.Binding.CaptureSnapshot().Revision > oldBinding.Revision);
        var dispatched = false;
        var stale = await fixture.Client.ExecuteWithPinnedBindingAsync(oldBinding, async () =>
        {
            dispatched = true;
            return await fixture.Client.OpenProjectAsync(fixture.Physical(DestinationPath), forceRebind: true);
        });

        Assert.False(dispatched);
        Assert.Equal(WorkerFailureCategories.BindingConflict, stale.Failure!.FailureCategory);
        await AssertNoOpenProjectCallsAsync(fixture.Client, fixture.Source);
    }

    private static async Task AssertNoOpenProjectCallsAsync(OpennessWorkerClient client, string sourcePath)
    {
        var status = await client.GetProjectStatusAsync(sourcePath);
        Assert.True(status.Success, status.Error);
        using var document = JsonDocument.Parse(status.Payload);
        Assert.Equal(0, document.RootElement.GetProperty("openProjectCalls").GetInt32());
    }
}
