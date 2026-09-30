using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
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
            using var client = new OpennessWorkerClient(new ProjectSessionBinding(path), logger: null,
                workerExecutablePath: FakeWorkerLocator.Locate(), accessPolicy: new(McpAccessMode.Full));
            var audit = new Audit();
            var execution = new WriteExecution(new OpennessWriteBindingGate(client), audit,
                LifecycleWriteDomain.Catalog, TimeProvider.System);
            var result = await ProjectWriteTools.ExecuteAsync(client, execution,
                new("save_project") { ProjectPath = path }, new(new UserConfirmationOptions(false)));
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
