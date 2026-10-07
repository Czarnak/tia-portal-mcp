using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.ProjectLifecycle;
using TiaMcpServer.Safety;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using TiaMcpServer.Tests.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Project;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class LifecycleGuardedBehaviorTests
{
    [Fact]
    public void LifecycleDomain_ConfirmsEveryCall()
        => Assert.True(new LifecycleWriteDomain(null!, new("save_project")).ConfirmsEveryCall);

    [Theory]
    [InlineData("open_project", "Destination", true)]
    [InlineData("create_project", "Created", true)]
    [InlineData("save_project", "Source", true)]
    [InlineData("save_project_as", "Copy", true)]
    [InlineData("archive_project", "Archive.zap21", true)]
    [InlineData("close_project", "Source", true)]
    [InlineData("close_project", "Source", false)]
    public void DescribeForConfirmation_NamesOperationAndTarget(string operation, string target, bool save)
    {
        var item = new LifecycleWriteItem(operation) { SaveBeforeClose = save };
        var effect = new LifecycleEffects("Source", "Destination", operation == "create_project" ? "Created" : "Copy",
            null, null, operation == "open_project", save, false, false, null, "Archive.zap21");
        var message = new LifecycleWriteDomain(null!, item).DescribeForConfirmation([item], [ItemPlan<LifecycleEffects>.Resolved(effect)]);
        Assert.Contains(operation, message);
        Assert.Contains(target, message);
        if (operation == "open_project") Assert.Contains("Close source project 'Source'", message);
        if (operation == "close_project") Assert.Contains(save ? "save" : "discard", message);
    }

    public static IEnumerable<object[]> BehaviorMatrix()
        => from operation in new[] { "open_project", "create_project", "save_project", "save_project_as", "archive_project", "close_project" }
           from behavior in new[] { "rejected", "dryRun", "success", "worker-failure", "malformed", "verification-failure" }
           select new object[] { operation, behavior };

    [Theory]
    [MemberData(nameof(BehaviorMatrix))]
    public async Task EveryOperation_UsesGuardedTypedOutcomes(string operation, string behavior)
    {
        using var fixture = new Fixture(behavior);
        var item = fixture.Item(operation);
        if (behavior == "rejected") item = item with { ProjectPath = "relative-path" };
        var dryRun = behavior == "dryRun";
        var result = await ProjectWriteTools.ExecuteAsync(fixture.Client, fixture.Execution, item,
            new(null), dryRun);
        using var document = JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        var root = document.RootElement;
        Assert.Equal(operation, root.GetProperty("tool").GetString());
        Assert.Equal(behavior == "rejected" ? "error" : dryRun ? "preview" : "applied", root.GetProperty("phase").GetString());
        Assert.Equal(behavior == "rejected", result.IsError == true);
        Assert.Equal(behavior is "dryRun" or "success", root.GetProperty("success").GetBoolean());
        Assert.Equal(behavior == "rejected", root.GetProperty("error").ValueKind == JsonValueKind.Object);
        Assert.DoesNotContain("untrustedMarker", root.GetRawText());
        var audit = Assert.Single(fixture.Audit.Records);
        Assert.Equal(root.GetRawText(), audit.ResponseText);
        if (behavior is "rejected" or "dryRun")
        {
            Assert.Equal(JsonValueKind.Null, root.GetProperty("result").ValueKind);
            Assert.Equal(JsonValueKind.Null, root.GetProperty("verification").ValueKind);
            if (operation == "create_project") Assert.False(Directory.Exists(item.ProjectDirectory + Path.DirectorySeparatorChar + item.ProjectName));
            if (operation == "save_project_as") Assert.False(Directory.Exists(item.TargetDirectory + Path.DirectorySeparatorChar + item.TargetName));
            return;
        }
        var outcome = root.GetProperty("result");
        Assert.Equal(behavior is "worker-failure" or "malformed" ? "failed" : "succeeded", outcome.GetProperty("status").GetString());
        if (behavior is "worker-failure" or "malformed")
        {
            Assert.Equal(JsonValueKind.Null, root.GetProperty("verification").ValueKind);
            Assert.Equal(behavior == "malformed" ? WorkerFailureCategories.ProtocolError : WorkerFailureCategories.WorkerOperationFailed,
                outcome.GetProperty("failure").GetProperty("category").GetString());
        }
        else
        {
            Assert.Equal(JsonValueKind.Object, outcome.GetProperty("value").ValueKind);
            var verification = root.GetProperty("verification");
            Assert.Equal(behavior == "verification-failure" ? "failed" : "succeeded", verification.GetProperty("status").GetString());
            if (operation == "close_project" && behavior == "success")
            {
                Assert.False(verification.GetProperty("value").GetProperty("isOpen").GetBoolean());
                Assert.Equal(JsonValueKind.Null, verification.GetProperty("value").GetProperty("path").ValueKind);
                Assert.Equal(ProjectBindingSnapshot.UnboundState, fixture.Client.BindingSnapshot.State);
            }
        }
    }

    [Theory]
    [InlineData("closes_source_project", "open_project", false, WriteGuardSeverities.Info)]
    [InlineData("discards_unsaved_source_changes", "open_project", true, WriteGuardSeverities.Block)]
    [InlineData("discards_unsaved_changes", "close_project", true, WriteGuardSeverities.Acknowledge)]
    [InlineData("archive_without_save", "archive_project", true, WriteGuardSeverities.Info)]
    [InlineData("archive_discards_restorable_data", "archive_project", false, WriteGuardSeverities.Info)]
    [InlineData("archive_inside_project_folder", "archive_project", false, WriteGuardSeverities.Block)]
    [InlineData("target_exists", "create_project", false, WriteGuardSeverities.Block)]
    [InlineData("target_exists", "save_project_as", false, WriteGuardSeverities.Block)]
    public void AcceptedGuardTable_AllSevenRulesAreRepresented(string id, string operation, bool modified, string severity)
    {
        var source = Path.Combine(Path.GetTempPath(), "guarded-source", "Source.ap21");
        var item = new LifecycleWriteItem(operation)
        {
            SaveBeforeArchive = false, SaveBeforeClose = false,
            ArchiveDirectory = id == "archive_inside_project_folder" ? Path.GetDirectoryName(source) : Path.GetTempPath()
        };
        var effects = new LifecycleEffects(source, source + ".other", Path.GetTempPath(),
            new ProjectStatusInfo { IsOpen = true, Path = source, IsModified = modified }, true,
            operation == "open_project", false, true, id == "target_exists",
            id == "archive_discards_restorable_data" ? ArchiveModeNames.DiscardRestorableDataAndCompressed : ArchiveModeNames.Compressed,
            Path.Combine(Path.GetTempPath(), "Archive.zap21"));
        var domain = new LifecycleWriteDomain(null!, item);
        var fired = domain.EvaluateGuards(new[] { item }, new[] { ItemPlan<LifecycleEffects>.Resolved(effects) });
        var guard = Assert.Single(fired, guard => guard.Id == id);
        Assert.Equal(severity, LifecycleWriteDomain.Catalog.Get(guard.Id).Severity);
        Assert.Equal(item.OperationId, guard.OperationId);
    }

    [Fact]
    public async Task UnboundDryRuns_DoNotStartAWorker()
    {
        using var fixture = new Fixture("success", configured: false, workerPath: Path.Combine(Path.GetTempPath(), "missing-lifecycle-worker.exe"));
        foreach (var operation in new[] { "open_project", "create_project" })
        {
            var result = await ProjectWriteTools.ExecuteAsync(fixture.Client, fixture.Execution, fixture.Item(operation),
                new(null), dryRun: true);
            Assert.False(result.IsError == true);
            Assert.Equal(ProjectBindingSnapshot.UnboundState, fixture.Client.BindingSnapshot.State);
        }
    }

    private sealed class Audit : IWriteAuditSink
    {
        public List<WriteAuditRecord> Records { get; } = new();
        public void Append(WriteAuditRecord record) => Records.Add(record);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root;
        private readonly string _source;
        private readonly string _destination;
        private readonly FakeWorkerUiOpenProject _uiOpen;
        public OpennessWorkerClient Client { get; }
        public WriteExecution Execution { get; }
        public Audit Audit { get; } = new();

        public Fixture(string behavior, bool configured = true, string? workerPath = null)
        {
            _root = Path.Combine(Path.GetTempPath(), "guarded-lifecycle-" + behavior + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "Source"));
            Directory.CreateDirectory(Path.Combine(_root, "Archives"));
            _source = Path.Combine(_root, "Source", "Source.ap21");
            _destination = Path.Combine(_root, "Destination.ap21");
            File.WriteAllText(_source, "fixture");
            File.WriteAllText(_destination, "fixture");
            _uiOpen = new FakeWorkerUiOpenProject(configured ? _source : null);
            Client = new(new ProjectSessionBinding(configured ? _source : null), logger: null,
                workerExecutablePath: workerPath ?? FakeWorkerLocator.Locate(), accessPolicy: new(McpAccessMode.Full));
            Execution = new(new OpennessWriteBindingGate(Client), Audit, LifecycleWriteDomain.Catalog, TimeProvider.System);
        }

        public LifecycleWriteItem Item(string operation) => new(operation)
        {
            ProjectPath = operation == "open_project" ? _destination : operation == "create_project" ? null : _source,
            ForceRebind = true, ProjectDirectory = _root, ProjectName = "Created",
            TargetDirectory = _root, TargetName = "Copy", ArchiveDirectory = Path.Combine(_root, "Archives"), ArchiveName = "Archive"
        };

        public void Dispose()
        {
            Client.Dispose();
            _uiOpen.Dispose();
            Directory.Delete(_root, recursive: true);
        }
    }
}
