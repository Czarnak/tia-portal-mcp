using System.Text.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Plc;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.OperationBatches;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConfiguredPayloadBudgetCollection
{
    public const string Name = "Configured payload budget";
}

/// <summary>Configures the process-wide budget, so it runs alone and restores the defaults.</summary>
[Collection(ConfiguredPayloadBudgetCollection.Name)]
public class ConfiguredPayloadBudgetTests
{
    [Fact]
    public void RaisedItemLimitReturnsLargeBlockWithContentHash()
    {
        var item = PlcPayloadContract.Project(
            new PlcOperationRequest { OperationId = "big", Operation = "get_block_content", BlockPath = "PLC_1/big" },
            WorkerCallResult.Ok(new string('x', 70_000)));
        try
        {
            StructuredOperationBatchPayloadBudget.Configure(100_000, 300_000);

            var bounded = PlcReadTools.ApplyBudget(StructuredOperationBatch.FromItems(new[] { item }));

            Assert.Equal(100_000, StructuredOperationBatchPayloadBudget.MaxItemChars);
            Assert.Equal(OperationBatchStatus.Succeeded, bounded.Operations[0].Status);
            var result = Assert.IsType<JsonElement>(bounded.Operations[0].Result);
            Assert.StartsWith("xml:sha256:", result.GetProperty("contentHash").GetString());
        }
        finally
        {
            StructuredOperationBatchPayloadBudget.Configure(
                StructuredOperationBatchPayloadBudget.DefaultItemChars,
                StructuredOperationBatchPayloadBudget.DefaultDocumentChars);
        }
    }
}
