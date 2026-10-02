using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.ProjectLifecycle;
using TiaMcpServer.Safety;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using TiaMcpServer.Tests.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Project;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class LifecycleResponseBudgetTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LongArchiveEvidence_PreservesVerdictAndGuardsWithinDocumentBudget(bool dryRun)
    {
        // Each Windows path stays below its long-path limit, with valid directory segments.
        var directory = @"C:\" + string.Join("\\", Enumerable.Repeat(new string('a', 200), 149));
        var source = Path.Combine(directory, "Source.ap21");
        var archive = Path.Combine(directory, "Backup.zap21");
        var item = new LifecycleWriteItem("archive_project")
        {
            ArchiveDirectory = directory, ArchiveName = "Backup", SaveBeforeArchive = false,
            Mode = ArchiveModeNames.DiscardRestorableData
        };
        using var client = new OpennessWorkerClient(new ProjectSessionBinding(null), accessPolicy: new(McpAccessMode.Full));
        var domain = new LifecycleWriteDomain(client, item);
        var effect = new LifecycleEffects(source, null, null,
            new ProjectStatusInfo { IsOpen = true, Path = source, IsModified = true },
            null, false, false, false, false, item.Mode, archive);
        Assert.True(CanonicalJson.Serialize(effect.SourceStatus).Length < StructuredOperationBatchPayloadBudget.MaxItemChars);
        var guards = GuardDecisions.Decide(domain.EvaluateGuards(new[] { item },
            new[] { ItemPlan<LifecycleEffects>.Resolved(effect) }), ConfirmationMode.Policy, LifecycleWriteDomain.Catalog, dryRun).Guards;
        Assert.Equal(3, guards.Count);
        var error = dryRun ? null : new WriteToolError(WorkerFailureCategories.GuardBlocked, "The archive destination is blocked.");
        var report = new WriteReport<LifecycleEffects, StandaloneToolOutcome<ProjectStatusInfo>>(
            dryRun ? WritePhases.Preview : WritePhases.Blocked, dryRun, error, Array.Empty<string>(),
            guards, new[] { new WriteEffect<LifecycleEffects>(item.OperationId, effect) }, null, null);
        var response = domain.Compose(report);
        Assert.True(CanonicalJson.Serialize(response).Length <= StructuredOperationBatchPayloadBudget.MaxDocumentChars);
        Assert.Equal(report.Phase, response.Phase);
        Assert.Equal(report.Success, response.Success);
        Assert.Equal(report.Error, response.Error);
        Assert.Equal(guards.Select(guard => (guard.Id, guard.Severity, guard.OperationId, guard.Acknowledged)),
            response.Guards.Select(guard => (guard.Id, guard.Severity, guard.OperationId, guard.Acknowledged)));
        Assert.Contains(response.Warnings, warning => warning.Contains("omitted", StringComparison.Ordinal));
        Assert.True(response.Effects is null || response.Effects.SourceStatus is null);
        if (response.Effects is not null)
        {
            Assert.Equal(source, response.Effects.SourceProjectPath);
            Assert.Equal(archive, response.Effects.ArchivePath);
        }
        Assert.Null(response.Result);
        Assert.Null(response.Verification);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OversizedGuardDetails_OmitsWholeMessageAndPreservesAcknowledgement(bool acknowledged)
    {
        using var client = new OpennessWorkerClient(new ProjectSessionBinding(null), accessPolicy: new(McpAccessMode.Full));
        var domain = new LifecycleWriteDomain(client, new LifecycleWriteItem("close_project"));
        var guard = new WriteGuardReport("discards_unsaved_changes", WriteGuardSeverities.Acknowledge,
            "lifecycle", "Unbounded consequence details: " + new string('x', 190_000), acknowledged);
        var error = acknowledged ? null : new WriteToolError(WorkerFailureCategories.GuardBlocked, "Acknowledgement is required.");
        var report = new WriteReport<LifecycleEffects, StandaloneToolOutcome<ProjectStatusInfo>>(
            acknowledged ? WritePhases.Preview : WritePhases.Blocked, acknowledged, error,
            Array.Empty<string>(), new[] { guard }, Array.Empty<WriteEffect<LifecycleEffects>>(), null, null);
        var response = domain.Compose(report);
        Assert.True(CanonicalJson.Serialize(response).Length <= StructuredOperationBatchPayloadBudget.MaxDocumentChars);
        var retained = Assert.Single(response.Guards);
        Assert.Equal((guard.Id, guard.Severity, guard.OperationId, guard.Acknowledged),
            (retained.Id, retained.Severity, retained.OperationId, retained.Acknowledged));
        Assert.Contains("omitted", retained.Message, StringComparison.Ordinal);
        Assert.Contains(LifecycleWriteDomain.Catalog.Get(guard.Id).Description, retained.Message, StringComparison.Ordinal);
        Assert.Contains(response.Warnings, warning => warning.Contains("guard", StringComparison.OrdinalIgnoreCase)
            && warning.Contains("omitted", StringComparison.Ordinal));
        Assert.Equal(report.Phase, response.Phase);
        Assert.Equal(report.Success, response.Success);
        Assert.Equal(report.Error, response.Error);
    }

    [Theory]
    [InlineData("oversized")]
    [InlineData("document-limit")]
    public async Task OversizedResponse_PreservesActualSuccessAndDisclosesWholeValueOmission(string scenario)
    {
        var directory = Path.Combine(Path.GetTempPath(), "guarded-lifecycle-" + scenario + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Source.ap21");
        File.WriteAllText(path, "fixture");
        try
        {
            using var uiOpen = new FakeWorkerUiOpenProject(path);
            using var client = new OpennessWorkerClient(new ProjectSessionBinding(path), logger: null,
                workerExecutablePath: FakeWorkerLocator.Locate(), accessPolicy: new(McpAccessMode.Full));
            var audit = new Audit();
            var execution = new WriteExecution(new OpennessWriteBindingGate(client), audit,
                LifecycleWriteDomain.Catalog, TimeProvider.System);
            var result = await ProjectWriteTools.ExecuteAsync(client, execution,
                new("save_project") { ProjectPath = path }, new(null));
            var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            Assert.True(root.GetProperty("success").GetBoolean(), text);
            Assert.True(text.Length <= StructuredOperationBatchPayloadBudget.MaxDocumentChars);
            Assert.Equal(text, Assert.Single(audit.Records).ResponseText);
            var outcome = root.GetProperty("result");
            Assert.Equal(OperationBatchStatus.Omitted, outcome.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, outcome.GetProperty("value").ValueKind);
            Assert.Equal(scenario == "oversized" ? StructuredOperationBatchPayloadBudget.ItemLimitReason
                : StructuredOperationBatchPayloadBudget.DocumentLimitReason,
                outcome.GetProperty("omission").GetProperty("reason").GetString());
            if (scenario == "oversized")
            {
                Assert.Equal(OperationBatchStatus.Omitted, root.GetProperty("verification").GetProperty("status").GetString());
                Assert.Contains(root.GetProperty("warnings").EnumerateArray(), warning => warning.GetString() == "retained mutation warning");
                Assert.Contains(root.GetProperty("warnings").EnumerateArray(), warning => warning.GetString()!.Contains("omitted", StringComparison.Ordinal));
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class Audit : IWriteAuditSink
    {
        public List<WriteAuditRecord> Records { get; } = new();
        public void Append(WriteAuditRecord record) => Records.Add(record);
    }
}
